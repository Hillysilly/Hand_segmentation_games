// Simulated input source: plays a scripted scenario (see scenarios.js) as if it were the webcam,
// feeding the same ingest functions with MediaPipe-shaped results, a segmentation mask per person,
// a drawn camera frame, and a scripted mic level.
import { POSE_WIDTH } from "../config.js";
import { frame } from "../state.js";
import { mic } from "../audio.js";
import { ingestPoses } from "../people.js";
import { ingestHands } from "../hands.js";
import { ingestFaces } from "../face.js";
import { resetWorld } from "../engine.js";
import { V, buildBody, project, poseLandmarks, handLandmarks } from "./body.js";

const SKIN = "#c8906e", SHIRT = "#3a4352", PANTS = "#262b36";

function canvas(w, h) {
  const c = document.createElement("canvas");
  c.width = w; c.height = h;
  return c;
}

export class SimSource {
  constructor(scenario, { width = 1280, height = 720 } = {}) {
    this.sc = scenario;
    this.image = canvas(width, height);
    this.ictx = this.image.getContext("2d");
    this.maskCanvas = canvas(POSE_WIDTH, Math.round(POSE_WIDTH * height / width));
    this.mctx = this.maskCanvas.getContext("2d", { willReadFrequently: true });
    this.micDb = -60;
    this.drawFrames = true;   // tests switch this off except on frames they render
  }

  // Become the app's camera and microphone.
  attach() {
    frame.img = this.image; frame.w = this.image.width; frame.h = this.image.height;
    mic.read = () => this.micDb;
  }

  // Feed the detections for scenario time t (seconds). Without t, play in real time on a loop,
  // starting over from an empty scene each time the script ends.
  poll(now, t) {
    if (t === undefined) {
      if (this.t0 === undefined) this.t0 = now;
      t = (now - this.t0) / 1000;
      if (t >= this.sc.duration) { this.t0 = now; t = 0; resetWorld(); this.attach(); }
    }
    const r = this.frameAt(t);
    ingestPoses(r.pose, now);
    ingestHands(r.hands, now);
    if (r.faces.length) ingestFaces(r.faces, now);
  }

  frameAt(t) {
    this.micDb = this.sc.mic ? this.sc.mic(t) : -62;
    const people = this.sc.people.map(f => f(t)).filter(Boolean)
      .map(pose => ({ pose, body: buildBody(pose) }));
    const pose = { landmarks: [], worldLandmarks: [], segmentationMasks: [] };
    const hands = { landmarks: [], worldLandmarks: [], handedness: [] };
    const faces = [];
    for (const { pose: ps, body } of people) {
      const { lm, world } = poseLandmarks(body, ps);
      if (ps.noise) shake(lm, world, ps.noise);
      if (ps.face) faces.push(faceLandmarks(body, ps));
      pose.landmarks.push(lm);
      pose.worldLandmarks.push(world);
      pose.segmentationMasks.push(this.mask(body, ps));
      if (ps.hideHands) continue;
      for (const hand of body.hands) {
        const h = handLandmarks(hand, ps);
        if (ps.noise) shake(h.lm, h.world, { xy: ps.noise.xy, z: ps.noise.z / 3 });
        const w = h.lm[0];
        if (w.x < 0 || w.x > 1 || w.y < 0 || w.y > 1) continue;   // wrist out of frame: no detection
        hands.landmarks.push(h.lm);
        hands.worldLandmarks.push(h.world);
        hands.handedness.push([{ categoryName: h.label, score: 0.98 }]);
      }
    }
    if (this.drawFrames) this.paintFrame(people);
    return { pose, hands, faces };
  }

  // Segmentation mask in MediaPipe's shape: { width, height, getAsFloat32Array() }, raw frame space.
  mask(body, ps) {
    const c = this.mctx, { width: w, height: h } = this.maskCanvas;
    c.fillStyle = "#000"; c.fillRect(0, 0, w, h);
    if (!ps.maskGlitch) for (const s of shapes(body, ps, w, h)) s.draw(c, "#fff");
    const px = c.getImageData(0, 0, w, h).data, data = new Float32Array(w * h);
    for (let i = 0; i < data.length; i++) data[i] = px[i * 4] / 255;
    return { width: w, height: h, getAsFloat32Array: () => data };
  }

  // A stand-in camera picture: a dim room with the people drawn in, back to front.
  paintFrame(people) {
    const c = this.ictx, { width: w, height: h } = this.image;
    const g = c.createLinearGradient(0, 0, 0, h);
    g.addColorStop(0, "#2b3542"); g.addColorStop(0.62, "#3d4654"); g.addColorStop(0.63, "#2a2c31"); g.addColorStop(1, "#1d1e22");
    c.fillStyle = g; c.fillRect(0, 0, w, h);
    c.fillStyle = "#4b5a6b"; c.fillRect(w * 0.08, h * 0.12, w * 0.2, h * 0.3);     // window
    c.fillStyle = "#33302c"; c.fillRect(w * 0.72, h * 0.3, w * 0.18, h * 0.33);    // shelf
    const all = people.flatMap(({ pose, body }) => shapes(body, pose, w, h));
    all.sort((a, b) => a.z - b.z);
    for (const s of all) s.draw(c, s.color);
  }
}

// The body as painted shapes (capsules and polygons) in pixel space, each with a depth for sorting.
function shapes(body, ps, w, h) {
  const P = q => { const p = project(q, ps); return { x: p.x * w, y: p.y * h }; };
  const px = h / ps.height;   // pixels per metre at the person
  const J = body.joints, out = [];
  const limb = (a, b, width, color) => out.push({ z: (a.z + b.z) / 2, color,
    draw: (c, col) => capsule(c, P(a), P(b), width * px, col) });
  out.push({ z: 0, color: SHIRT, draw: (c, col) => polygon(c, [J[11], J[12], J[24], J[23]].map(P), 0.1 * px, col) });
  for (const [a, b] of [[23, 25], [25, 27], [24, 26], [26, 28]]) limb(J[a], J[b], 0.14, PANTS);
  for (const [a, b] of [[11, 13], [12, 14]]) limb(J[a], J[b], 0.1, SHIRT);
  for (const [a, b] of [[13, 15], [14, 16]]) limb(J[a], J[b], 0.085, SHIRT);
  const neck = { x: (J[11].x + J[12].x) / 2, y: 0.55, z: (J[11].z + J[12].z) / 2 };
  limb(neck, body.head, 0.09, SKIN);
  out.push({ z: body.head.z, color: SKIN, draw: (c, col) => {
    const p = P(body.head);
    c.fillStyle = col; c.beginPath(); c.ellipse(p.x, p.y, 0.09 * px, 0.115 * px, 0, 0, Math.PI * 2); c.fill();
  } });
  if (!ps.hideHands) {
    for (const hand of body.hands) {
      const H = hand.pts;
      out.push({ z: H[0].z + 0.01, color: SKIN, draw: (c, col) => {
        polygon(c, [0, 1, 5, 9, 13, 17].map(i => P(H[i])), 0.02 * px, col);
        for (const f of [[1, 2, 3, 4], [5, 6, 7, 8], [9, 10, 11, 12], [13, 14, 15, 16], [17, 18, 19, 20]]) {
          for (let k = 0; k < 3; k++) capsule(c, P(H[f[k]]), P(H[f[k + 1]]), 0.018 * px, col);
        }
      } });
    }
  }
  return out;
}

function capsule(c, a, b, width, color) {
  c.strokeStyle = color; c.lineWidth = Math.max(1, width); c.lineCap = "round";
  c.beginPath(); c.moveTo(a.x, a.y); c.lineTo(b.x, b.y); c.stroke();
}

function polygon(c, pts, round, color) {
  c.fillStyle = c.strokeStyle = color; c.lineWidth = Math.max(1, round); c.lineJoin = "round";
  c.beginPath();
  pts.forEach((p, i) => i ? c.lineTo(p.x, p.y) : c.moveTo(p.x, p.y));
  c.closePath(); c.fill(); c.stroke();
}

// Tracking noise like MediaPipe's: every landmark jiggles a little each frame (xy in normalised image
// units, z in metres; z is much the noisier, as in the real thing).
function shake(lm, world, { xy, z }) {
  const gauss = () => Math.sqrt(-2 * Math.log(1 - Math.random())) * Math.cos(2 * Math.PI * Math.random());
  lm.forEach((l, j) => {
    const dx = gauss() * xy, dy = gauss() * xy, dz = gauss() * z;
    l.x += dx; l.y += dy;
    world[j].z += dz;
  });
}

// What the face tracker would report for a pose with an expression, face: { open: [their right eye,
// left eye] (1 = usual, 0 = shut), frown, raise } (see face.js for the shape).
function faceLandmarks(body, ps) {
  const f = ps.face, J = body.joints;
  const pt = q => { const p = project(q, ps); return { x: p.x, y: p.y }; };
  const eye = (c, open, [a, b, up, down]) => ({
    [a]: pt(V(c.x - 0.016, c.y, c.z)), [b]: pt(V(c.x + 0.016, c.y, c.z)),
    [up]: pt(V(c.x, c.y + 0.006 * open, c.z)), [down]: pt(V(c.x, c.y - 0.006 * open, c.z)),
  });
  return {
    pts: { 1: pt(J[0]), ...eye(J[5], f.open?.[0] ?? 1, [33, 133, 159, 145]), ...eye(J[2], f.open?.[1] ?? 1, [263, 362, 386, 374]) },
    shapes: { browDownLeft: f.frown || 0, browDownRight: f.frown || 0, browInnerUp: f.raise || 0 },
  };
}
