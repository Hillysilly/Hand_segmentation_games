// People: pose tracks matched across frames, with their segmentation silhouettes.
import { PLAYER_COLORS, MAX_HEALTH, STAGE_HEALTH } from "./config.js";
import { H, frame, persons, unseenFor } from "./state.js";
import { dist, clamp, videoRect, toScreen, rayToEdge, greedyMatch } from "./util.js";

export class Person {
  constructor(box, num) {
    Object.assign(this, box);      // head box: x, y, w, h
    this.num = num;
    this.color = PLAYER_COLORS[(num - 1) % PLAYER_COLORS.length];
    this.lastSeen = 0;
    this.lm = null;                // pose landmarks in screen space
    this.world = null;             // pose world landmarks: metres around the hips, raw (unmirrored) camera axes
    this.mask = null;              // silhouette 0..255, in raw (unmirrored) frame space
    this.mw = 0; this.mh = 0;
    this.edge = document.createElement("canvas");   // outline, drawn in player color
    this.fill = document.createElement("canvas");   // solid red silhouette for hit flashes
    this.health = MAX_HEALTH;
    this.shown = MAX_HEALTH;   // trailing "damage taken" segment of the bar
    this.hitFlash = 0;
    this.ko = 0;               // seconds left knocked out
    this.beamAcc = 0;          // beam damage not yet shown as a floating number
    this.edgeList = [];        // mask indices on the silhouette edge (lightning spawn points)
    this.auraLevel = 0;        // 0..1, flares up while charging or firing
    this.facing = "front";     // spin tracking: which way the body faces the camera
    this.facingCand = "front"; this.facingSince = 0;
    this.turnedAt = -Infinity; // when they last turned away
    this.profile = 0; this.profileAt = -Infinity;   // last side profile seen (-1 / 1, by nose direction)
    this.outSide = 0;          // profile passed on the way out (turning away)
    this.swRef = 0;            // usual (front-on) shoulder width, in torso lengths
    this.swNow = 1;            // current shoulder width relative to that
    this.narrow = false;       // shoulders currently collapsed (side view)
    this.narrowTimes = [];     // when recent side views started
    this.swingHist = [];       // recent wrist sides for the arm-swing trigger
    this.spinUntil = 0;        // tornado runs until this time
    this.spin = 0;             // 0..1 tornado strength
    this.spinOn = false;
    this.tw = null;            // smoothed tornado geometry
    this.orbiters = [];        // solid chunks riding the tornado
    this.streaks = [];         // solid wind streaks wrapping around it
    this.flexing = false;      // holding the flex pose this frame
    this.flexCharge = 0;       // 0..1 while holding a flex to power up
    this.stage = 0;            // aura stage: 0 off, 1 yellow, 2 blue, 3 white
    this.flexReady = true;     // flex was released since the last transformation
    this.env = null;           // aura envelope: body reach per direction from the torso
    this.envC = null;          // ...and that torso center
  }
  get cx() { return this.x + this.w / 2; }
  get cy() { return this.y + this.h / 2; }
}

export const maxHealth = p => MAX_HEALTH + STAGE_HEALTH * p.stage;

function freeNumber() {
  const used = new Set(persons.map(p => p.num));
  let n = 1;
  while (used.has(n)) n++;
  return n;
}

// Head box from pose landmarks (0 nose, 2/5 eyes, 7/8 ears, 11/12 shoulders).
function headBox(lm) {
  const w = Math.max(dist(lm[7], lm[8]) * 1.3, dist(lm[11], lm[12]) * 0.45, 40);
  const cx = (lm[0].x + lm[7].x + lm[8].x) / 3, cy = (lm[0].y + lm[2].y + lm[5].y) / 3;
  return { x: cx - w / 2, y: cy - w * 0.6, w, h: w * 1.25 };
}

export function ingestPoses(result, now) {
  frame.seen = now;
  const r = videoRect();
  const dets = result.landmarks.map(lms => lms.map(l => ({ ...toScreen(l, r), v: l.visibility ?? 1 })));
  const boxes = dets.map(headBox);
  const match = greedyMatch(boxes, persons, (b, p) => {
    const d = Math.hypot(b.x + b.w / 2 - p.cx, b.y + b.h / 2 - p.cy);
    return d < Math.max(b.w, p.w) * 1.5 ? d : null;
  });
  boxes.forEach((b, i) => {
    let p = match.get(i);
    if (!p) { p = new Person(b, freeNumber()); persons.push(p); }
    else for (const k of ["x", "y", "w", "h"]) p[k] += (b[k] - p[k]) * 0.5;
    p.lastSeen = now;
    p.lm = dets[i];
    p.world = result.worldLandmarks?.[i] ?? null;
    const m = result.segmentationMasks?.[i];
    if (m) setMask(p, m.getAsFloat32Array(), m.width, m.height);
  });
}

// Store the silhouette and prebuild its outline / hit-flash images.
function setMask(p, data, mw, mh) {
  if (p.mw !== mw || p.mh !== mh) {
    p.mw = mw; p.mh = mh;
    p.mask = new Uint8Array(mw * mh);
    p.edge.width = p.fill.width = mw;
    p.edge.height = p.fill.height = mh;
    p.edgeImg = p.edge.getContext("2d").createImageData(mw, mh);
    p.fillImg = p.fill.getContext("2d").createImageData(mw, mh);
  }
  const m = p.mask;
  for (let i = 0; i < m.length; i++) m[i] = clamp(data[i], 0, 1) * 255;
  const [r, g, b] = p.ko > 0 ? [255, 70, 50] : p.color;
  const e = p.edgeImg.data, f = p.fillImg.data;
  p.edgeList.length = 0;
  for (let y = 0; y < mh; y++) {
    for (let x = 0; x < mw; x++) {
      const i = y * mw + x, o = i * 4;
      const edge = m[i] > 127 && x > 0 && y > 0 && x < mw - 1 && y < mh - 1 &&
        (m[i - 1] <= 127 || m[i + 1] <= 127 || m[i - mw] <= 127 || m[i + mw] <= 127);
      if (edge) p.edgeList.push(i);
      e[o] = r; e[o + 1] = g; e[o + 2] = b; e[o + 3] = edge ? 255 : 0;
      f[o] = 255; f[o + 1] = 50; f[o + 2] = 30; f[o + 3] = m[i];
    }
  }
  p.edge.getContext("2d").putImageData(p.edgeImg, 0, 0);
  p.fill.getContext("2d").putImageData(p.fillImg, 0, 0);
}

export const isFresh = p => p.mask && unseenFor(p.lastSeen) < 300;

// Silhouette value (0..255) under a screen point.
export function maskAt(p, sx, sy, r) {
  const u = 1 - (sx - r.x) / r.w, v = (sy - r.y) / r.h;
  if (u < 0 || u >= 1 || v < 0 || v >= 1) return 0;
  return p.mask[(v * p.mh | 0) * p.mw + (u * p.mw | 0)];
}

export const inHead = (p, x, y) => x > p.x && x < p.x + p.w && y > p.y && y < p.y + p.h;
export const bodyAt = (p, x, y, r) => inHead(p, x, y) || (isFresh(p) && maskAt(p, x, y, r) > 127);

// Would a beam from (x,y) along (dx,dy) run back into p's own head or body? The hand itself
// is usually inside the silhouette, so for the body this means leaving it and then re-entering.
export function hitsSelf(p, x, y, dx, dy) {
  const r = videoRect(), L = rayToEdge(x, y, dx, dy), startInHead = inHead(p, x, y);
  const useMask = isFresh(p);
  let outside = 0, left = false;
  for (let t = 0; t < L; t += 8) {
    const px = x + dx * t, py = y + dy * t;
    if (!startInHead && inHead(p, px, py)) return true;
    if (!useMask) continue;
    if (maskAt(p, px, py, r) > 127) {
      if (left) return true;
      outside = 0;
    } else if (++outside >= 3) {
      left = true;
    }
  }
  return false;
}

export const torsoCenter = L => ({ x: (L[11].x + L[12].x + L[23].x + L[24].x) / 4,
  y: Math.min(H - 10, (L[11].y + L[12].y + L[23].y + L[24].y) / 4) });

export const torsoLen = L => Math.max(30, dist({ x: (L[11].x + L[12].x) / 2, y: (L[11].y + L[12].y) / 2 },
  { x: (L[23].x + L[24].x) / 2, y: (L[23].y + L[24].y) / 2 }));
