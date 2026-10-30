// The Iron Man suit, rendered with three.js: a rigged suit model posed from the tracked body (see
// suitmodel.js), its gauntlets' fingers posed from the hand landmarks, and an arc reactor on the
// chest. A tracked hand with no suit body to wear it gets simple hand armour instead (a back plate,
// finger segments and the repulsor) pinned to its landmarks. Everything is drawn into the 2D canvas so it layers
// with the other effects.
//
// three.js space here is screen pixels: x right, y up (so y = -screenY), z toward the viewer, seen
// through an orthographic camera. World landmarks are raw camera space (x right in the unmirrored
// image, y down, z away from the camera); the screen is mirrored, so a world vector (x, y, z)
// becomes (-x, -y, -z) here.
import { settings } from "../config.js";
import { ctx, W, H, DPR, clock, persons, hands } from "../state.js";
import { dist, clamp } from "../util.js";
import { glow } from "../gfx.js";
import { isFresh } from "../people.js";
import { loadSuitModel, modelReady, modelStatus, makeBodyRig, poseBody, hideBody, boneScreen } from "./suitmodel.js";

const REACTOR_OUT = 0.21;    // metres the reactor sits in front of the torso's midline
const FINGER_BONES = [[1, 2], [2, 3], [3, 4], [5, 6], [6, 7], [7, 8], [9, 10], [10, 11], [11, 12],
  [13, 14], [14, 15], [15, 16], [17, 18], [18, 19], [19, 20]];

let T = null;                // the three.js module, once loaded
let renderer = null, scene = null, camera = null, geo = null, mat = null;
let drawn = { w: 0, h: 0, dpr: 0 };
const rigs = new Map();      // Person or Hand -> its armour rig
let glows = [];              // glowing bits, drawn in 2D over the suit: { x, y, r, rgb, a }
export const armorStatus = { loaded: false, webgl: false, model: "", error: "" };

export async function initArmor() {
  if (T) return armorStatus;
  try {
    T = await import("three");
  } catch (e) {
    armorStatus.error = `three.js failed to load: ${e.message}`;
    console.warn(armorStatus.error);
    return armorStatus;
  }
  scene = new T.Scene();
  camera = new T.OrthographicCamera(0, 1, 0, -1, 1, 6000);
  camera.position.z = 3000;
  scene.add(new T.HemisphereLight(0xe8f4ff, 0x302020, 1.2));
  const key = new T.DirectionalLight(0xffffff, 2.5);
  key.position.set(-0.4, 1, 0.9);
  scene.add(key);
  buildParts();
  if (await loadSuitModel(T)) Object.assign(armorStatus, { model: modelStatus.url, skipped: modelStatus.skipped });
  else armorStatus.error = `no suit model, only the reactor and hands: ${modelStatus.error}`;
  armorStatus.loaded = true;
  try {
    renderer = new T.WebGLRenderer({ alpha: true, antialias: true, preserveDrawingBuffer: true });
    renderer.setClearColor(0x000000, 0);
    renderer.toneMapping = T.ACESFilmicToneMapping;
    scene.environment = new T.PMREMGenerator(renderer).fromScene(studio(), 0.04).texture;
    armorStatus.webgl = true;
  } catch (e) {
    renderer = null;
    armorStatus.error = `no WebGL, so the suit is tracked but not drawn: ${e.message}`;
    console.warn(armorStatus.error);
  }
  return armorStatus;
}

// A simple studio for the metal to reflect: dim walls, a bright softbox overhead, warm and cool strips.
function studio() {
  const s = new T.Scene();
  s.add(new T.Mesh(new T.BoxGeometry(10, 10, 10), new T.MeshBasicMaterial({ color: 0x1b2027, side: T.BackSide })));
  const panel = (w, h, x, y, z, color, power) => {
    const m = new T.Mesh(new T.PlaneGeometry(w, h),
      new T.MeshBasicMaterial({ color: new T.Color(color).multiplyScalar(power), side: T.DoubleSide }));
    m.position.set(x, y, z); m.lookAt(0, 0, 0);
    s.add(m);
  };
  panel(6, 3, 0, 4.5, 1, 0xffffff, 6);
  panel(1, 6, -4.5, 0, 1.5, 0xbfd8ff, 3);
  panel(1, 6, 4.5, 0, 1.5, 0xffe2c0, 3);
  panel(5, 1, 0, -1.5, 4.5, 0x8fb0d0, 1.5);
  return s;
}

function buildParts() {
  mat = {
    red: new T.MeshStandardMaterial({ color: 0x9e1118, metalness: 0.8, roughness: 0.3 }),
    gold: new T.MeshStandardMaterial({ color: 0xd4a33a, metalness: 1, roughness: 0.38 }),
    steel: new T.MeshStandardMaterial({ color: 0x8a9099, metalness: 1, roughness: 0.35 }),
    copper: new T.MeshStandardMaterial({ color: 0xb8733a, metalness: 1, roughness: 0.3 }),
    dark: new T.MeshStandardMaterial({ color: 0x15171c, metalness: 0.5, roughness: 0.6 }),
    light: new T.MeshBasicMaterial({ color: 0xd8f6ff, side: T.DoubleSide }),   // reactor core, repulsors
  };
  geo = {
    box: new T.BoxGeometry(1, 1, 1),
    torus: new T.TorusGeometry(0.5, 0.11, 16, 48),
    thin: new T.TorusGeometry(0.3, 0.035, 10, 40),
    disc: new T.CircleGeometry(0.5, 40),
    bone: new T.CylinderGeometry(1, 1, 1, 12),
    knuckle: new T.SphereGeometry(1, 14, 10),
  };
}

function part(parent, g, m, pos, rot, scale) {
  const o = new T.Mesh(g, m);
  if (pos) o.position.set(...pos);
  if (rot) o.rotation.set(...rot);
  if (scale) o.scale.set(...scale);
  parent.add(o);
  return o;
}

// Unit arc reactor (housing diameter ~1.2) facing +z.
function makeReactor() {
  const g = new T.Group();
  part(g, geo.torus, mat.steel);
  part(g, geo.disc, mat.light, [0, 0, 0.02], null, [0.8, 0.8, 1]);
  part(g, geo.thin, mat.dark, [0, 0, 0.05]);
  for (let k = 0; k < 10; k++) {   // copper coils around the core
    const a = k / 10 * Math.PI * 2;
    part(g, geo.box, mat.copper, [Math.cos(a) * 0.36, Math.sin(a) * 0.36, 0.06], [0, 0, a - Math.PI / 2], [0.05, 0.12, 0.04]);
  }
  return g;
}

function makeSuit() {
  const root = new T.Group();
  const r = { kind: "suit", root, reactor: makeReactor(), body: modelReady() ? makeBodyRig(root) : null, k: 0, pts: null };
  root.add(r.reactor);
  hide(r.reactor);
  scene.add(root);
  return r;
}

function makeHandRig() {
  const root = new T.Group();
  const r = { kind: "hand", root, k: 0, pts: null, onModel: false,
    plate: part(root, geo.box, mat.red), disc: part(root, geo.disc, mat.light),
    bones: FINGER_BONES.map(() => part(root, geo.bone, mat.red)),
    knuckles: Array.from({ length: 20 }, () => part(root, geo.knuckle, mat.gold)) };
  for (const o of root.children) hide(o);
  scene.add(root);
  return r;
}

// ---------------------------------------------------------------- posing

const vec = (x, y, z) => new T.Vector3(x, y, z);
// World vector from landmark a to b, in this scene's axes.
const worldDir = (w, a, b) => vec(-(w[b].x - w[a].x), -(w[b].y - w[a].y), -(w[b].z - w[a].z));
const basis = (x, y, z) => new T.Quaternion().setFromRotationMatrix(new T.Matrix4().makeBasis(x, y, z));
const along = d => new T.Quaternion().setFromUnitVectors(vec(0, 1, 0), d.clone().normalize());   // +y onto d

function hide(o) { o.visible = false; o.userData.fresh = true; }

// Ease a part toward its target (snapping when it first appears) to damp landmark jitter.
function settle(o, pos, quat, scale, a) {
  if (o.userData.fresh) {
    o.position.copy(pos); o.quaternion.copy(quat); o.scale.copy(scale);
    o.userData.fresh = false;
  } else {
    o.position.lerp(pos, a); o.quaternion.slerp(quat, a); o.scale.lerp(scale, a);
  }
  o.visible = true;
}

function poseSuit(r, p, gauntlets, a) {
  const L = p.lm, w = p.world;
  // pixels per metre: on-screen lengths over the same world lengths flattened onto the image plane
  let px = 0, m = 0;
  for (const [i, j] of [[11, 12], [7, 8], [11, 23], [12, 24]]) {
    if (L[i].v < 0.5 || L[j].v < 0.5) continue;
    px += dist(L[i], L[j]);
    m += Math.hypot(w[i].x - w[j].x, w[i].y - w[j].y);
  }
  if (m > 0.05) r.k = r.k ? r.k + (px / m - r.k) * 0.3 : px / m;
  if (!r.k) { hide(r.reactor); if (r.body) hideBody(r.body); return; }
  const k = r.k;
  // landmarks as screen x/y plus world depth, eased toward their new spots to damp jitter
  const target = L.map((l, j) => vec(l.x, -l.y, -w[j].z * k));
  if (!r.pts) r.pts = target;
  else r.pts.forEach((v, j) => v.lerp(target[j], a));
  const P = j => r.pts[j].clone();

  if (r.body) {
    poseBody(r.body, P, gauntlets, a);
    const face = clamp(vec(0, 0, 1).applyQuaternion(r.body.headFrame).z * 2, 0, 1);   // eyes glow toward the viewer
    const size = P(7).distanceTo(P(8));
    for (const n of ["LeftEye", "RightEye"]) {
      const e = boneScreen(r.body, n);
      if (e) glows.push({ x: e.x, y: e.y, r: size * 0.13, rgb: "190,240,255", a: 0.85 * face });
    }
  }

  // Arc reactor: on the sternum, in the torso's frame from the shoulders and hips.
  if (L[11].v > 0.5 && L[12].v > 0.5) {
    const tx = worldDir(w, 11, 12).normalize();
    const up = vec(-(w[11].x + w[12].x - w[23].x - w[24].x) / 2, -(w[11].y + w[12].y - w[23].y - w[24].y) / 2,
      -(w[11].z + w[12].z - w[23].z - w[24].z) / 2);
    const ty = up.sub(tx.clone().multiplyScalar(up.dot(tx))).normalize();
    const tz = vec(0, 0, 0).crossVectors(tx, ty);
    const shoulders = P(11).add(P(12)).multiplyScalar(0.5), hips = P(23).add(P(24)).multiplyScalar(0.5);
    const at = shoulders.lerp(hips, 0.27).addScaledVector(tz, REACTOR_OUT * k);   // out on the chest surface
    const s = Math.hypot(w[11].x - w[12].x, w[11].y - w[12].y, w[11].z - w[12].z) * k * 0.2;
    if (tz.z > 0.1) {   // facing away, it would be on their back
      settle(r.reactor, at, basis(tx, ty, tz), vec(s, s, s), a);
      glows.push({ x: at.x, y: -at.y, r: s * 1.1, rgb: "150,220,255", a: 0.9 * clamp(tz.z * 2, 0, 1) });
    } else hide(r.reactor);
  } else hide(r.reactor);
}

// The hand's landmarks as hybrid points (r.pts), eased toward where they are now.
function trackHand(r, h, a) {
  const wl = h.world, pts = h.pts;
  const px = dist(pts[0], pts[9]) + dist(pts[5], pts[17]);
  const m = Math.hypot(wl[9].x - wl[0].x, wl[9].y - wl[0].y) + Math.hypot(wl[17].x - wl[5].x, wl[17].y - wl[5].y);
  if (m > 0.02) r.k = r.k ? r.k + (px / m - r.k) * 0.3 : px / m;
  if (!r.k) { r.pts = null; return; }
  // depth: the owner's pose wrist places the hand, its own world landmarks shape it
  let base = 150;
  const suit = h.owner && rigs.get(h.owner);
  if (suit?.k && h.owner.world) {
    const wi = dist(pts[0], h.owner.lm[15]) < dist(pts[0], h.owner.lm[16]) ? 15 : 16;
    base = -h.owner.world[wi].z * suit.k;
  }
  const target = pts.map((q, j) => vec(q.x, -q.y, base - (wl[j].z - wl[0].z) * r.k));
  if (!r.pts) r.pts = target;
  else r.pts.forEach((v, j) => v.lerp(target[j], a));
}

// The repulsor glow, plus the simple hand armour when the hand isn't worn by a suit model's gauntlet.
function poseHand(r, h) {
  if (!r.pts) { for (const o of r.root.children) hide(o); return; }
  const k = r.k, P = j => r.pts[j].clone();

  // back plate in the palm's frame: fingers along +y, palm normal (the repulsor side) along +z
  const fdir = P(9).sub(P(0)), across = P(17).sub(P(5));
  const fy = fdir.clone().normalize();
  const n = vec(h.n.x, -h.n.y, -h.n.z);
  let fz = n.sub(fy.clone().multiplyScalar(n.dot(fy)));
  if (fz.length() < 1e-3) fz = vec(0, 0, 0).crossVectors(across, fy);
  fz.normalize();
  const fx = vec(0, 0, 0).crossVectors(fy, fz);
  const q = basis(fx, fy, fz), thick = 0.028 * k;
  const centre = P(0).lerp(P(9), 0.55);
  const discAt = centre.clone().addScaledVector(fz, thick / 2 + 0.5);
  const ds = across.length() * 0.55;
  const power = Math.max(h.openGlow, h.charge);
  glows.push({ x: discAt.x, y: -discAt.y, r: ds * 1.1, rgb: "150,225,255", a: (0.25 + 0.75 * power) * clamp(fz.z, 0, 1) });
  if (r.onModel) { for (const o of r.root.children) hide(o); return; }   // the gauntlet is the hand
  settle(r.plate, centre, q, vec(across.length() * 1.3, fdir.length() * 1.05, thick), 1);
  settle(r.disc, discAt, q, vec(ds, ds, 1), 1);

  FINGER_BONES.forEach(([i, j], b) => {
    const A = P(i), d = P(j).sub(A), len = d.length();
    if (len < 0.5) { hide(r.bones[b]); return; }
    const rad = (i < 5 ? 0.0125 : 0.0105) * k;   // thumb a little thicker
    settle(r.bones[b], A.addScaledVector(d, 0.5), along(d), vec(rad, len, rad), 1);
  });
  const kr = 0.0118 * k, unit = new T.Quaternion();
  r.knuckles.forEach((o, i) => settle(o, P(i + 1), unit, vec(kr, kr, kr), 1));
}

export function updateArmor(dt) {
  if (!T) return;
  glows = [];
  if (!settings.armor) { resetArmor(); return; }
  const a = 1 - Math.exp(-dt * 25);
  const live = new Set();
  const armoured = hands.filter(h => h.world && clock - h.lastSeen <= 300);
  for (const h of armoured) {
    if (!rigs.has(h)) rigs.set(h, makeHandRig());
    const r = rigs.get(h);
    trackHand(r, h, a);
    r.onModel = false;
    live.add(h);
  }
  for (const p of persons) {
    if (!p.lm || !p.world || !isFresh(p)) continue;
    if (!rigs.has(p)) rigs.set(p, makeSuit());
    const suit = rigs.get(p);
    // the gauntlets the tracked hands go into (mirrored: the person's right wrist is the model's left)
    const gauntlets = {};
    for (const h of armoured) {
      const hr = rigs.get(h);
      if (h.owner !== p || !hr.pts || !suit.body) continue;
      gauntlets[dist(h.pts[0], p.lm[16]) < dist(h.pts[0], p.lm[15]) ? "Left" : "Right"] = hr.pts.map(v => v.clone());
      hr.onModel = true;
    }
    poseSuit(suit, p, gauntlets, a);
    live.add(p);
  }
  for (const h of armoured) {
    const r = rigs.get(h);
    if (r.onModel && !rigs.get(h.owner)?.body?.holder.visible) r.onModel = false;   // suit not drawn this frame
    poseHand(r, h);
  }
  for (const [key, r] of rigs) if (!live.has(key)) { scene.remove(r.root); rigs.delete(key); }
}

// Is this hand drawn as the suit's gauntlet (so its tracking lines would only hide it)?
export const inGauntlet = h => !!(renderer && settings.armor && rigs.get(h)?.onModel);

export function resetArmor() {
  for (const r of rigs.values()) scene.remove(r.root);
  rigs.clear();
  glows = [];
}

// Paint the suit into the 2D canvas (with its current transform, so it shakes with everything else).
export function drawArmor() {
  if (!renderer || !settings.armor || !rigs.size) return;
  if (drawn.w !== W || drawn.h !== H || drawn.dpr !== DPR) {
    drawn = { w: W, h: H, dpr: DPR };
    renderer.setPixelRatio(DPR);
    renderer.setSize(W, H, false);
    camera.right = W; camera.bottom = -H;
    camera.updateProjectionMatrix();
  }
  renderer.render(scene, camera);
  ctx.drawImage(renderer.domElement, 0, 0, W, H);
}

// Eyes, reactor core and palm repulsors glow (drawn additively over the suit).
export function drawArmorGlows() {
  if (!renderer || !settings.armor) return;
  for (const g of glows) {
    if (g.a <= 0.02) continue;
    glow(g.x, g.y, g.r, [[0, `rgba(255,255,255,${g.a})`], [0.3, `rgba(${g.rgb},${0.6 * g.a})`], [1, `rgba(${g.rgb},0)`]]);
  }
}

const JOINTS = ["Head", "HeadTop_End", "Neck", "Spine2", "Hips", "LeftArm", "RightArm", "LeftForeArm", "RightForeArm",
  "LeftHand", "RightHand", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot"];

// Where every piece is, in screen pixels, for tests and debugging.
export function armorDebug() {
  const info = o => {
    const f = vec(0, 0, 1).applyQuaternion(o.quaternion), u = vec(0, 1, 0).applyQuaternion(o.quaternion);
    return { visible: o.visible, x: o.position.x, y: -o.position.y, size: o.scale.x,
      forward: [f.x, f.y, f.z], axis: [u.x, u.y, u.z] };
  };
  const out = { status: { ...armorStatus }, suits: [], hands: [] };
  for (const [key, r] of rigs) {
    if (r.kind === "suit") {
      const b = r.body;
      let body = null;
      if (b) {
        const joints = {};
        for (const n of JOINTS) joints[n] = boneScreen(b, n);
        const f = vec(0, 0, 1).applyQuaternion(b.headFrame);
        body = { visible: b.holder.visible, joints, headForward: [f.x, f.y, f.z],
          gauntlet: { ...b.gauntlet } };
      }
      out.suits.push({ num: key.num, reactor: info(r.reactor), body });
    } else out.hands.push({ owner: key.owner?.num ?? null, onModel: r.onModel, plate: info(r.plate), disc: info(r.disc) });
  }
  return out;
}
