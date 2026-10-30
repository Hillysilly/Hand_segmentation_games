// Hands: landmark tracks matched across frames, gesture classification, palm direction, ownership.
import { FINGERS } from "./config.js";
import { clock, persons, hands } from "./state.js";
import { dist, videoRect, toScreen, greedyMatch } from "./util.js";
import { hitsSelf } from "./people.js";

function classify(p) {
  const w = p[0];
  // A finger counts as extended when its tip is clearly farther from the wrist than its PIP joint.
  const ext = FINGERS.map(f => dist(w, p[f.tip]) > dist(w, p[f.pip]) * 1.12);
  const n = ext.filter(Boolean).length;
  if (n === 4) return "open";
  if (n === 0) return "fist";
  if (ext[0] && !ext[1] && !ext[2] && ext[3]) return "web";   // index + pinky up, middle + ring down
  return "none";
}

export class Hand {
  constructor(pts) {
    this.pts = pts;
    this.world = null;   // hand world landmarks: metres, raw (unmirrored) camera axes
    this.label = "HAND";
    this.lastSeen = 0;
    this.gesture = "none";
    this.candidate = "";
    this.candidateN = 0;
    this.charge = 0;
    this.openGlow = 0;   // 0..1 palm glow intensity
    this.openTime = 0;   // seconds the palm has been held open
    this.beam = 0;       // 0..1 beam intensity
    this.owner = null;
    this.n = { x: 0, y: 0, z: -1 };   // palm normal in mirrored screen space; -z faces the camera
    this.curlKnown = false;           // finger curl has settled which side is the palm
    this.hist = [];                   // recent palm positions, for detecting throws
    this.lastThrow = -Infinity;
    this.lastWeb = -Infinity;
    this.cx = pts[9].x; this.cy = pts[9].y; this.ps = 100;
  }
}

export function ingestHands(result, now) {
  const r = videoRect();
  const dets = result.landmarks.map(lms => lms.map(l => toScreen(l, r)));
  const match = greedyMatch(dets, hands, (pts, h) => {
    const d = dist(pts[9], h.pts[9]);
    return d < Math.max(120, h.ps * 1.5) ? d : null;
  });
  dets.forEach((pts, i) => {
    let h = match.get(i);
    if (!h) { h = new Hand(pts); hands.push(h); }
    else for (let j = 0; j < pts.length; j++) {
      h.pts[j].x += (pts[j].x - h.pts[j].x) * 0.6;
      h.pts[j].y += (pts[j].y - h.pts[j].y) * 0.6;
    }
    h.lastSeen = now;
    const raw = result.handedness?.[i]?.[0]?.categoryName;
    h.label = raw === "Left" ? "LEFT" : raw === "Right" ? "RIGHT" : "HAND";

    const wl = h.world = result.worldLandmarks?.[i] ?? null;
    if (wl) {
      // Palm axis = (index knuckle - wrist) x (pinky knuckle - wrist), in raw (unmirrored) space.
      const a = { x: wl[5].x - wl[0].x, y: wl[5].y - wl[0].y, z: wl[5].z - wl[0].z };
      const b = { x: wl[17].x - wl[0].x, y: wl[17].y - wl[0].y, z: wl[17].z - wl[0].z };
      let cx = a.y * b.z - a.z * b.y, cy = a.z * b.x - a.x * b.z, cz = a.x * b.y - a.y * b.x;
      const len = Math.hypot(cx, cy, cz) || 1;
      cx /= len; cy /= len; cz /= len;

      // Which side is the palm? Fingers only bend toward it, so measure how far the finger joints
      // sit off the palm plane along the axis. If the model mirrors its depth guess, the axis and
      // the curl mirror together, so the on-screen direction still comes out right.
      let curl = 0;
      for (const [mcp, dip, tip] of [[5, 7, 8], [9, 11, 12], [13, 15, 16], [17, 19, 20]]) {
        for (const j of [dip, tip]) {
          curl += (wl[j].x - wl[mcp].x) * cx + (wl[j].y - wl[mcp].y) * cy + (wl[j].z - wl[mcp].z) * cz;
        }
      }
      curl /= 8 * (Math.hypot(a.x, a.y, a.z) || 1);

      // Clear curl decides. A flat hand keeps whichever side curl last picked (continuity);
      // a hand that has never shown curl falls back to the handedness label.
      let sign, byCurl = false;
      if (Math.abs(curl) > 0.12) {
        sign = Math.sign(curl); byCurl = true; h.curlKnown = true;
      } else if (h.curlKnown) {
        sign = -cx * h.n.x + cy * h.n.y + cz * h.n.z < 0 ? -1 : 1;
      } else {
        sign = h.label === "RIGHT" ? 1 : -1;
      }
      let nx = -sign * cx, ny = sign * cy, nz = sign * cz;   // negated x: into mirrored screen space

      // Never fire into yourself: unless curl just settled it, if this direction runs back into
      // the owner's head or body and the opposite one doesn't, flip it.
      const p = h.owner, side = Math.hypot(nx, ny);
      if (p && side > 0.2 && !byCurl) {
        const dx = nx / side, dy = ny / side;
        if (hitsSelf(p, h.cx, h.cy, dx, dy) && !hitsSelf(p, h.cx, h.cy, -dx, -dy)) {
          nx = -nx; ny = -ny; nz = -nz;
        }
      }

      // snap instead of smoothing through zero when the direction reverses
      if (nx * h.n.x + ny * h.n.y + nz * h.n.z < 0) {
        h.n = { x: nx, y: ny, z: nz };
      } else {
        h.n.x += (nx - h.n.x) * 0.5;
        h.n.y += (ny - h.n.y) * 0.5;
        h.n.z += (nz - h.n.z) * 0.5;
        const nl = Math.hypot(h.n.x, h.n.y, h.n.z) || 1;
        h.n.x /= nl; h.n.y /= nl; h.n.z /= nl;
      }
    }

    // Debounce: a new gesture must hold for 2 detection frames before it takes over.
    const g = classify(h.pts);
    if (g === h.candidate) h.candidateN++; else { h.candidate = g; h.candidateN = 1; }
    if (h.candidateN >= 2) h.gesture = g;
  });
}

// A hand belongs to the body whose pose wrist it sits on; failing that, the closest head
// (weighting vertical distance less, since hands sit below heads).
export function ownerOf(h) {
  let best = null, bestD = Infinity;
  for (const p of persons) {
    if (!p.lm || clock - p.lastSeen > 300) continue;
    const d = Math.min(dist(h.pts[0], p.lm[15]), dist(h.pts[0], p.lm[16]));
    if (d < bestD) { bestD = d; best = p; }
  }
  if (best && bestD < h.ps * 2) return best;
  best = null; bestD = Infinity;
  for (const p of persons) {
    const d = Math.hypot(h.cx - p.cx, (h.cy - p.cy) * 0.5);
    if (d < bestD) { bestD = d; best = p; }
  }
  return best;
}
