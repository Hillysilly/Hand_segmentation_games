// Synthetic people for the simulator: a parametric skeleton posed in metres, then projected through
// a virtual webcam into the same shapes MediaPipe returns (pose and hand landmarks, world landmarks).
//
// Body frame: metres, origin between the hips, x = the person's left, y = up, z = the way they face
// (toward the camera at yaw 0). The camera image is unmirrored, like a webcam's raw frame, so the
// person's left shows up on the image's right.
//
// A pose is { x, y, height, yaw, headYaw, headPitch, left, right, hideHands }: (x, y) is where the
// hip centre sits in the normalised image, height is how many metres the frame spans top to bottom
// at the person's distance, yaw turns the whole body (positive = toward their left), and left /
// right are arms (see limbDir and handPoints). noise: { xy, z } adds tracking jitter (see sim.js);
// maskGlitch blanks their segmentation mask, as the real segmenter sometimes does for a frame or two.

export const V = (x, y, z) => ({ x, y, z });
const add = (a, b) => V(a.x + b.x, a.y + b.y, a.z + b.z);
const sub = (a, b) => V(a.x - b.x, a.y - b.y, a.z - b.z);
const mul = (a, s) => V(a.x * s, a.y * s, a.z * s);
const dot = (a, b) => a.x * b.x + a.y * b.y + a.z * b.z;
const cross = (a, b) => V(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
const len = a => Math.hypot(a.x, a.y, a.z);
const norm = a => mul(a, 1 / (len(a) || 1));
const lerpV = (a, b, t) => add(a, mul(sub(b, a), t));
const rotY = (p, a) => V(p.x * Math.cos(a) + p.z * Math.sin(a), p.y, -p.x * Math.sin(a) + p.z * Math.cos(a));
const rotX = (p, a) => V(p.x, p.y * Math.cos(a) - p.z * Math.sin(a), p.y * Math.sin(a) + p.z * Math.cos(a));

// Finger curl per finger [thumb, index, middle, ring, pinky]: 0 straight, 1 fully bent.
export const CURL = {
  open: [0, 0, 0, 0, 0],
  fist: [1, 1, 1, 1, 1],
  web: [0, 0, 1, 1, 0],          // index + pinky out, middle + ring folded
  relaxed: [0.6, 0.1, 0.9, 0.9, 0.9],
};

// Limb direction from two angles: `out` swings it from straight down out to the side (π/2 is
// horizontal, π straight up), then `fwd` tips it toward the way the body faces.
function limbDir(side, [out, fwd = 0]) {
  const flat = V(side * Math.sin(out), -Math.cos(out), 0);
  return norm(add(mul(flat, Math.cos(fwd)), V(0, 0, Math.sin(fwd))));
}

const PALM = {
  front: () => V(0, 0, 1), back: () => V(0, 0, -1), up: () => V(0, 1, 0), down: () => V(0, -1, 0),
  out: side => V(side, 0, 0), in: side => V(-side, 0, 0),
};

// [distance along the hand, offset toward the thumb, segment lengths, splay] per finger.
const FINGER_BASE = [
  [0.095, 0.025, [0.042, 0.025, 0.021], 0.12],   // index
  [0.097, 0.003, [0.046, 0.028, 0.022], 0],      // middle
  [0.09, -0.018, [0.043, 0.027, 0.021], -0.1],   // ring
  [0.08, -0.036, [0.034, 0.02, 0.019], -0.22],   // pinky
];
const BEND = [1.4, 1.75, 1.15];   // radians per joint at full curl: knuckle, middle, tip

// The 21 hand landmarks. hand = { dir: [out, fwd] (default: along the forearm), palm: which way the
// palm faces (a PALM key), curl: CURL entry or array }. Fingers bend toward the palm.
function handPoints(wrist, side, hand, foreDir) {
  const f = hand.dir ? limbDir(side, hand.dir) : foreDir;
  const want = PALM[hand.palm || "in"](side);
  let n = sub(want, mul(f, dot(want, f)));
  if (len(n) < 1e-3) n = sub(V(0, 0, 1), mul(f, f.z));
  n = norm(n);
  const t = side < 0 ? cross(n, f) : cross(f, n);   // toward the thumb: right hand n×f, left hand f×n
  const curl = hand.curl || CURL.relaxed;
  const pts = new Array(21);
  pts[0] = wrist;
  FINGER_BASE.forEach(([along, across, segs, splay], i) => {
    const c = curl[i + 1], sp = splay * (1 - c);
    const fi = norm(add(mul(f, Math.cos(sp)), mul(t, Math.sin(sp))));
    let p = add(wrist, add(mul(f, along), mul(t, across)));
    const base = 5 + i * 4;
    pts[base] = p;
    let ang = 0;
    segs.forEach((L, k) => {
      ang += c * BEND[k];
      p = add(p, mul(add(mul(fi, Math.cos(ang)), mul(n, Math.sin(ang))), L));
      pts[base + 1 + k] = p;
    });
  });
  // thumb: out to the side when straight, tucked across the palm when curled
  const ext = norm(add(mul(f, 0.55), mul(t, 0.85))), tuck = norm(add(add(mul(f, 0.65), mul(t, -0.45)), mul(n, 0.6)));
  let p = add(wrist, add(mul(f, 0.025), mul(t, 0.022)));
  pts[1] = p;
  [0.04, 0.032, 0.027].forEach((L, k) => {
    p = add(p, mul(norm(lerpV(ext, tuck, Math.min(1, curl[0] * (0.5 + 0.5 * k)))), L));
    pts[2 + k] = p;
  });
  return pts;
}

const ARM_DOWN = { upper: [0.15, 0], fore: [0.12, 0.3], hand: { palm: "in", curl: CURL.relaxed } };

// Face points around the head centre, before any head turn.
const FACE = {
  0: [0, -0.01, 0.1],
  1: [0.015, 0.03, 0.088], 2: [0.033, 0.03, 0.083], 3: [0.05, 0.03, 0.072],
  4: [-0.015, 0.03, 0.088], 5: [-0.033, 0.03, 0.083], 6: [-0.05, 0.03, 0.072],
  7: [0.075, 0.005, -0.005], 8: [-0.075, 0.005, -0.005],
  9: [0.025, -0.045, 0.087], 10: [-0.025, -0.045, 0.087],
};
const HEAD = V(0, 0.73, 0.01);

// All joints in the body frame (yawed), plus the head centre and both hands' landmarks.
export function buildBody(pose) {
  const j = new Array(33);
  const both = (i, x, y, z) => { j[i] = V(x, y, z); j[i + 1] = V(-x, y, z); };
  both(11, 0.18, 0.5, 0);           // shoulders
  both(23, 0.11, 0, 0);             // hips
  both(25, 0.11, -0.45, 0.03);      // knees
  both(27, 0.11, -0.88, 0);         // ankles
  both(29, 0.11, -0.93, -0.05);     // heels
  both(31, 0.11, -0.95, 0.13);      // toes
  for (const k in FACE) j[k] = add(HEAD, rotY(rotX(V(...FACE[k]), pose.headPitch || 0), pose.headYaw || 0));

  const handList = [];
  for (const [side, arm, s, e, w, pinky, index, thumb] of [
    [1, pose.left, 11, 13, 15, 17, 19, 21], [-1, pose.right, 12, 14, 16, 18, 20, 22]]) {
    const a = arm || ARM_DOWN;
    const fore = limbDir(side, a.fore);
    j[e] = add(j[s], mul(limbDir(side, a.upper), 0.29));
    j[w] = add(j[e], mul(fore, 0.26));
    const hp = handPoints(j[w], side, a.hand || {}, fore);
    j[pinky] = hp[18]; j[index] = hp[6]; j[thumb] = hp[2];
    handList.push({ side, pts: hp });
  }

  const yaw = pose.yaw || 0;
  const turn = q => rotY(q, yaw);
  return {
    joints: j.map(turn),
    head: turn(HEAD),
    hands: handList.map(h => ({ side: h.side, pts: h.pts.map(turn) })),
    facing: Math.cos(yaw + (pose.headYaw || 0)),   // 1 facing the camera, -1 facing away
  };
}

// Virtual webcam: hips at (pose.x, pose.y), pose.height metres tall frame, mild perspective.
const ASPECT = 16 / 9, DEPTH = 2.5;
export function project(q, pose) {
  const s = DEPTH / (DEPTH - q.z), viewH = pose.height, viewW = viewH * ASPECT;
  return { x: pose.x + q.x * s / viewW, y: pose.y - q.y * s / viewH, z: -q.z / viewW };
}

// MediaPipe-shaped pose landmarks (normalised image) and world landmarks (metres, y down, z away).
export function poseLandmarks(body, pose) {
  const faceVis = Math.min(0.99, Math.max(0.02, 0.5 + 1.5 * body.facing));
  const lm = body.joints.map((q, i) => {
    const p = project(q, pose);
    let v = i <= 10 ? faceVis : 0.99;
    if (p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1) v = Math.min(v, 0.1);   // out of frame
    return { x: p.x, y: p.y, z: p.z, visibility: v };
  });
  const world = body.joints.map((q, i) => ({ x: q.x, y: -q.y, z: -q.z, visibility: lm[i].visibility }));
  return { lm, world };
}

// Hand landmarks (normalised image) and hand world landmarks (metres around the middle knuckle).
// MediaPipe labels handedness as if the image were mirrored, so a real right hand reads "Left".
export function handLandmarks(hand, pose) {
  const lm = hand.pts.map(q => project(q, pose));
  const c = hand.pts[9];
  const world = hand.pts.map(q => ({ x: q.x - c.x, y: -(q.y - c.y), z: -(q.z - c.z) }));
  const label = hand.side > 0 ? "Right" : "Left";
  return { lm, world, label };
}
