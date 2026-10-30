// The suit body: a rigged humanoid glTF (Mixamo bone names) posed every frame from the tracked
// landmarks. Drop your own rigged model at assets/models/suit.glb (or pass ?model=<url>) and it is
// used instead of the default Xbot, which gets an Iron Man paint job.
//
// Posing works in the armour scene's pixel space (see armor.js) on "hybrid" points: screen x/y from
// the 2D landmarks, depth from the world landmarks, so the model lines up with the person on screen.
// The screen is mirrored, so the model's left side follows the person's right side, which turns the
// mirror image back into a proper (unreflected) body.
//   - torso, neck and head: whole-frame rotations from the hip line, shoulder line and ears/nose
//   - arms and legs: each bone swung onto its landmark segment and stretched to its length, with
//     the upper bone twisted so the elbow / knee bends in the plane the landmarks bend in
//   - hands: the gauntlets, every finger bone swung onto the hand tracker's landmarks; without hand
//     tracking, just the hand's angle from the pose's wrist / index / pinky

const MODELS = ["assets/models/suit.glb", "assets/models/xbot.glb"];
const ROOT_URL = new URL("../../", import.meta.url);
const NOSE_TILT = 0.15;   // radians the nose sits below the ear line
const HEAD_PER_EAR = 1.5; // helmet width per ear-to-ear distance: a little bigger than the real head, to cover it
const GAUNTLET = 1.15;    // gauntlet size over the tracked hand's, to cover it
// [upper, lower, end] bones and the [pose] landmarks they follow, per model side
const LIMBS = {
  Left: { arm: [12, 14, 16], leg: [24, 26, 28], foot: 32, hand: [16, 18, 20] },
  Right: { arm: [11, 13, 15], leg: [23, 25, 27], foot: 31, hand: [15, 17, 19] },
};
// model finger bones 1..4 and the hand landmark at bone 1 (the next three follow it)
const FINGERS = [["Thumb", 1], ["Index", 5], ["Middle", 9], ["Ring", 13], ["Pinky", 17]];
const REQUIRED = ["Hips", "Spine", "Spine2", "Neck", "Head",
  ..."Shoulder Arm ForeArm Hand UpLeg Leg Foot".split(" ").flatMap(b => [`Left${b}`, `Right${b}`])];

let T = null, template = null, clone = null;
export const modelStatus = { url: "", error: "", skipped: "" };   // skipped: models that failed before `url` loaded

// "mixamorig:LeftArm", "mixamorigLeftArm" and Sketchfab's "mixamorig:LeftArm_09" are all "LeftArm"
const boneName = n => n.replace(/^.*?mixamorig[:_]?/i, "").replace(/_\d+$/, "");

export async function loadSuitModel(three) {
  T = three;
  try {
    const [{ GLTFLoader }, { DRACOLoader }, utils] = await Promise.all([
      import("three/addons/loaders/GLTFLoader.js"), import("three/addons/loaders/DRACOLoader.js"),
      import("three/addons/utils/SkeletonUtils.js")]);
    clone = utils.clone;
    const loader = new GLTFLoader();
    loader.setDRACOLoader(new DRACOLoader().setDecoderPath("https://www.gstatic.com/draco/versioned/decoders/1.5.6/"));
    const wanted = new URLSearchParams(location.search).get("model");
    const errors = [];
    for (const url of wanted ? [wanted] : MODELS) {
      try {
        const gltf = await loader.loadAsync(new URL(url, ROOT_URL).href);
        template = prepare(gltf.scene, url);
        modelStatus.url = url;
        modelStatus.error = "";
        modelStatus.skipped = errors.join("; ");
        if (errors.length) console.warn(`suit model fallback: ${modelStatus.skipped}`);
        return true;
      } catch (e) {
        errors.push(`${url}: ${e.message || e}`);
      }
    }
    modelStatus.error = errors.join("; ");
  } catch (e) {
    modelStatus.error = `model loaders failed to load: ${e.message}`;
  }
  console.warn(modelStatus.error);
  return false;
}

function prepare(scene, url) {
  const names = new Set();
  scene.traverse(o => { if (o.isBone) names.add(boneName(o.name)); });
  const missing = REQUIRED.filter(n => !names.has(n));
  if (missing.length) throw new Error(`not a Mixamo-style rig, missing bones: ${missing.join(", ")}`);
  const xbot = url.endsWith("xbot.glb");
  const red = new T.MeshStandardMaterial({ color: 0x9e1118, metalness: 0.85, roughness: 0.28 });
  const gold = new T.MeshStandardMaterial({ color: 0xd4a33a, metalness: 1, roughness: 0.32 });
  scene.traverse(o => {
    if (!o.isMesh) return;
    o.frustumCulled = false;   // bind-pose bounds don't follow the posed, pixel-sized body
    if (xbot) o.material = /joint/i.test(o.name) ? gold : red;
  });
  return scene;
}

export const modelReady = () => !!template;

// ---------------------------------------------------------------- math helpers

const V = () => new T.Vector3();
const Q = () => new T.Quaternion();
const mid = (a, b) => a.clone().add(b).multiplyScalar(0.5);
// Rotation whose y axis is yv and whose x axis is xv made perpendicular to it (z = x × y).
function frame(xv, yv) {
  const y = yv.clone().normalize();
  const x = xv.clone().addScaledVector(y, -xv.dot(y)).normalize();
  const z = V().crossVectors(x, y);
  return Q().setFromRotationMatrix(new T.Matrix4().makeBasis(x, y, z));
}

// ---------------------------------------------------------------- one rig per person

export function makeBodyRig(scene) {
  const holder = new T.Group();
  const model = clone(template);
  holder.add(model);
  scene.add(holder);
  const bones = {};
  model.traverse(o => { if (o.isBone) bones[boneName(o.name)] = o; });
  const rest = new Map();
  for (const b of Object.values(bones)) rest.set(b, { q: b.quaternion.clone(), p: b.position.clone(), s: b.scale.clone() });

  holder.updateMatrixWorld(true);
  const wp = n => bones[n].getWorldPosition(V());
  const wq = n => bones[n].getWorldQuaternion(Q());
  const up = mid(wp("LeftArm"), wp("RightArm")).sub(mid(wp("LeftUpLeg"), wp("RightUpLeg")));
  const body = frame(wp("LeftArm").sub(wp("RightArm")), up);
  const bodyInv = body.clone().invert();
  // each frame-driven bone's rest rotation relative to the frame that drives it
  const offset = {};
  for (const n of ["Hips", "Spine", "Spine1", "Spine2", "Neck", "Head"]) {
    if (bones[n]) offset[n] = bodyInv.clone().multiply(wq(n));
  }
  const hinge = {};
  for (const side of ["Left", "Right"]) {
    const h = `${side}Hand`;
    if (bones[`${side}HandIndex1`] && bones[`${side}HandPinky1`] && bones[`${side}HandMiddle1`]) {
      const f = frame(wp(`${side}HandPinky1`).sub(wp(`${side}HandIndex1`)), wp(`${side}HandMiddle1`).sub(wp(h)));
      offset[h] = f.invert().multiply(wq(h));
    }
    // natural bend axes, in the upper bone's own space: elbows bend forward, knees backward
    for (const [upper, lower, bend] of [[`${side}Arm`, `${side}ForeArm`, 1], [`${side}UpLeg`, `${side}Leg`, -1]]) {
      const dir = wp(lower).sub(wp(upper)).normalize();
      const fwd = V().set(0, 0, bend).applyQuaternion(body);
      hinge[upper] = V().crossVectors(dir, fwd).normalize().applyQuaternion(wq(upper).invert());
    }
  }
  const eyes = bones.LeftEye && bones.RightEye ? wp("LeftEye").distanceTo(wp("RightEye")) : 0;
  return {
    holder, bones, rest, offset, hinge, eyes, head: headWidth(model, bones, body),
    torso: up.length(), width: wp("LeftArm").distanceTo(wp("RightArm")),
    s: 0, headFrame: Q(), gauntlet: { Left: false, Right: false },
  };
}

// Width of the model's head across the body (x of frame f), from the vertices skinned mostly to it.
function headWidth(model, bones, f) {
  const x = V().set(1, 0, 0).applyQuaternion(f), v = V();
  let lo = Infinity, hi = -Infinity;
  model.traverse(m => {
    if (!m.isSkinnedMesh) return;
    const at = [bones.Head, bones.HeadTop_End].map(b => m.skeleton.bones.indexOf(b)).filter(i => i >= 0);
    if (!at.length) return;
    const pos = m.geometry.attributes.position, si = m.geometry.attributes.skinIndex, sw = m.geometry.attributes.skinWeight;
    for (let i = 0; i < pos.count; i++) {
      let w = 0;
      for (let c = 0; c < 4; c++) if (at.includes(si.getComponent(i, c))) w += sw.getComponent(i, c);
      if (w < 0.5) continue;
      m.applyBoneTransform(i, v.fromBufferAttribute(pos, i)).applyMatrix4(m.matrixWorld);
      const d = v.dot(x);
      if (d < lo) lo = d;
      if (d > hi) hi = d;
    }
  });
  return hi > lo ? hi - lo : 0;
}

export function removeBodyRig(scene, r) { scene.remove(r.holder); }

// Point the bone's world rotation at q.
function setWorld(bone, q) {
  bone.quaternion.copy(bone.parent.getWorldQuaternion(Q()).invert().multiply(q));
}

// Rotate a bone by d in world space (about its own joint).
function rotateWorld(bone, d) {
  const w = bone.getWorldQuaternion(Q());
  setWorld(bone, d.clone().multiply(w));
}

// Scale a bone's offset from its parent so the parent -> bone segment is `len` long.
function stretchTo(parent, bone, len, base) {
  const cur = parent.getWorldPosition(V()).distanceTo(bone.getWorldPosition(V()));
  if (cur > 1e-6) bone.position.copy(base).multiplyScalar(Math.min(1.6, Math.max(0.6, len / cur)));
}

// Swing upper -> lower -> end onto the points A -> B -> C, stretched to fit, twisting the upper
// bone so the joint bends the way the points do.
function poseLimb(r, [up, lo, end], A, B, C) {
  const b = r.bones, rest = r.rest;
  stretchTo(b[up], b[lo], A.distanceTo(B), rest.get(b[lo]).p);
  stretchTo(b[lo], b[end], B.distanceTo(C), rest.get(b[end]).p);
  const u = B.clone().sub(A).normalize(), f = C.clone().sub(B).normalize();
  const cur = b[lo].getWorldPosition(V()).sub(b[up].getWorldPosition(V())).normalize();
  rotateWorld(b[up], Q().setFromUnitVectors(cur, u));
  const n = V().crossVectors(u, f), bent = n.length();
  if (bent > 0.15 && r.hinge[up]) {
    const h = r.hinge[up].clone().applyQuaternion(b[up].getWorldQuaternion(Q()));
    h.addScaledVector(u, -h.dot(u));
    n.normalize();
    const angle = Math.atan2(u.dot(V().crossVectors(h, n)), h.dot(n));
    rotateWorld(b[up], Q().setFromAxisAngle(u, angle * Math.min(1, (bent - 0.15) / 0.3)));
  }
  const cur2 = b[end].getWorldPosition(V()).sub(b[lo].getWorldPosition(V())).normalize();
  rotateWorld(b[lo], Q().setFromUnitVectors(cur2, f));
}

// Fit a gauntlet to the tracked hand: H is the 21 hand landmarks as hybrid points.
function poseGauntlet(r, side, H) {
  const b = r.bones, hand = b[`${side}Hand`], middle = b[`${side}HandMiddle1`];
  setWorld(hand, frame(H[17].clone().sub(H[5]), H[9].clone().sub(H[0])).multiply(r.offset[`${side}Hand`]));
  const cur = hand.getWorldPosition(V()).distanceTo(middle.getWorldPosition(V()));
  if (cur > 1e-6) hand.scale.multiplyScalar(Math.min(2, Math.max(0.5, H[0].distanceTo(H[9]) * GAUNTLET / cur)));
  for (const [f, at] of FINGERS) {
    for (let n = 1; n <= 3; n++) {
      const bone = b[`${side}Hand${f}${n}`], next = b[`${side}Hand${f}${n + 1}`];
      if (!bone || !next) break;
      const now = next.getWorldPosition(V()).sub(bone.getWorldPosition(V())).normalize();
      rotateWorld(bone, Q().setFromUnitVectors(now, H[at + n].clone().sub(H[at + n - 1]).normalize()));
    }
  }
}

// Pose the model onto one person. P(j) gives pose landmark j as a hybrid point; hands[side], when
// the hand tracker has that hand, gives its 21 landmarks as hybrid points.
export function poseBody(r, P, hands, a) {
  const b = r.bones;
  for (const [bone, s] of r.rest) { bone.quaternion.copy(s.q); bone.position.copy(s.p); bone.scale.copy(s.s); }
  r.holder.visible = true;

  const hips = mid(P(23), P(24)), shoulders = mid(P(11), P(12)), up = shoulders.clone().sub(hips);
  const width = P(12).distanceTo(P(11));
  const s = (up.length() + width) / (r.torso + r.width);
  r.s = r.s ? r.s + (s - r.s) * a : s;
  r.holder.scale.setScalar(r.s);
  r.holder.position.set(0, 0, 0);

  // torso and head as whole frames (x from the person's right to left side = the model's left)
  const hipF = frame(P(24).sub(P(23)), up), chestF = frame(P(12).sub(P(11)), up);
  const x = P(8).sub(P(7)).normalize(), earMid = mid(P(7), P(8));
  const z = P(0).sub(earMid);
  z.addScaledVector(x, -z.dot(x)).normalize().applyAxisAngle(x, -NOSE_TILT);
  const headF = Q().setFromRotationMatrix(new T.Matrix4().makeBasis(x, V().crossVectors(z, x), z));
  r.headFrame.copy(headF);
  const along = (f0, f1, t) => f0.clone().slerp(f1, t);
  const drive = { Hips: hipF, Spine: along(hipF, chestF, 1 / 3), Spine1: along(hipF, chestF, 2 / 3),
    Spine2: chestF, Neck: along(chestF, headF, 0.5), Head: headF };
  for (const [n, f] of Object.entries(drive)) if (b[n] && r.offset[n]) setWorld(b[n], f.clone().multiply(r.offset[n]));

  // the model's shoulders to the person's width, then its shoulder midpoint onto theirs
  const cw = b.LeftArm.getWorldPosition(V()).distanceTo(b.RightArm.getWorldPosition(V()));
  for (const side of ["Left", "Right"]) {
    const arm = b[`${side}Arm`], cl = arm.getWorldPosition(V()).distanceTo(b[`${side}Shoulder`].getWorldPosition(V()));
    if (cl > 1e-6) arm.position.multiplyScalar(Math.min(2, Math.max(0.5, (cl + (width - cw) / 2) / cl)));
  }
  const at = mid(b.LeftArm.getWorldPosition(V()), b.RightArm.getWorldPosition(V()));
  r.holder.position.copy(shoulders.sub(at));
  if (r.head) {
    const k = P(8).distanceTo(P(7)) * HEAD_PER_EAR / (r.head * r.s);
    b.Head.scale.multiplyScalar(Math.min(2, Math.max(0.7, k)));
  }

  for (const side of ["Left", "Right"]) {
    const L = LIMBS[side];
    const H = r.offset[`${side}Hand`] ? hands[side] : null;   // a gauntlet needs the hand's rest frame
    r.gauntlet[side] = !!H;
    poseLimb(r, [`${side}Arm`, `${side}ForeArm`, `${side}Hand`], P(L.arm[0]), P(L.arm[1]), H ? H[0] : P(L.arm[2]));
    poseLimb(r, [`${side}UpLeg`, `${side}Leg`, `${side}Foot`], P(L.leg[0]), P(L.leg[1]), P(L.leg[2]));
    const foot = b[`${side}Foot`], toe = b[`${side}ToeBase`];
    if (toe) {
      const cur = toe.getWorldPosition(V()).sub(foot.getWorldPosition(V())).normalize();
      rotateWorld(foot, Q().setFromUnitVectors(cur, P(L.foot).sub(P(L.leg[2])).normalize()));
    }
    if (H) poseGauntlet(r, side, H);
    else if (r.offset[`${side}Hand`]) {
      const [w, pinky, index] = L.hand.map(P);
      setWorld(b[`${side}Hand`], frame(pinky.clone().sub(index), mid(pinky, index).sub(w)).multiply(r.offset[`${side}Hand`]));
    }
  }
}

export function hideBody(r) { r.holder.visible = false; }

// Screen position of a bone (and whether it exists).
export function boneScreen(r, n) {
  const bone = r.bones[n];
  if (!bone) return null;
  const v = bone.getWorldPosition(V());
  return { x: v.x, y: -v.y, z: v.z };
}
