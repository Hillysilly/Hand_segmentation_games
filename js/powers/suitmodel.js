// The suit bodies: rigged humanoid glTFs (Mixamo bone names) posed every frame from the tracked
// landmarks. Two suits: Iron Man (assets/models/suit.glb, or ?model=<url>; else the Xbot with an
// Iron Man paint job) and Spider-Man (assets/models/spiderman.glb).
//
// Posing works in the armour scene's pixel space (see armor.js) on "hybrid" points: screen x/y from
// the 2D landmarks, depth from the world landmarks, so the model lines up with the person on screen.
// The screen is mirrored, so the model's left side follows the person's right side, which turns the
// mirror image back into a proper (unreflected) body.
//   - torso, neck and head: whole-frame rotations from the hip line, shoulder line and ears/nose
//   - arms and legs: each bone swung onto its landmark segment and stretched to its length, with
//     the upper bone twisted so the elbow / knee bends in the plane the landmarks bend in
//   - nanotech: every vertex carries its distance from the arc reactor along the body, and the
//     shader draws only what the suit-up front has reached (see setNano)
//   - eyes: a suit with eye lenses (Spider-Man) squints, blinks, widens and frowns with the face
//     tracker's expression (see setEyes)
//   - mask: a suit with eye lenses (Spider-Man) can take its mask off, peeling from the neck up
//     (see setMaskOff)
//   - hands: the gauntlets, every finger bone swung onto the hand tracker's landmarks; without hand
//     tracking, just the hand's angle from the pose's wrist / index / pinky

import { steady } from "../filter.js";

// Each suit: the files to try in order, and how wide its head is drawn per ear-to-ear distance (a
// helmet a little bigger than the real head, to cover it; a mask hugs it).
const SUITS = {
  ironman: { files: ["assets/models/suit.glb", "assets/models/xbot.glb"], headPerEar: 1.5 },
  spiderman: { files: ["assets/models/spiderman.glb"], headPerEar: 1.3 },
};
const ROOT_URL = new URL("../../", import.meta.url);
const NOSE_TILT = 0.15;   // radians the nose sits below the ear line
const REACTOR = /reactor/i;  // material name of a model's own arc reactor
const LENS = /^eyes/i;       // material name of a suit's eye lenses, which follow the face's expression
const LENS_PARTS = /^(eyes|glass)/i;   // ...and the glass over them, which gets the same lids
const EYES_EASE = 0.04;      // seconds the lenses take to follow the face (fast enough for a blink)
const EYES_STALE = 400;      // ms without a face before the lenses relax back to neutral
const OPEN_MAX = 1.15;       // widest the lenses open (any more and they'd outgrow their frames)
// nanotech front, in units of the farthest vertex's distance from the reactor
const NANO_EDGE = 0.05;      // width of the liquid-silver band behind the front
const NANO_PLATES = 0.035;   // how far the ragged front runs ahead of or behind where it is
const NANO_REACH = 1 + NANO_EDGE + NANO_PLATES;   // front position at level 1: everything formed
// how slowly the suit travels up the neck and down the legs (1 = as fast as everywhere else): the
// helmet closes near the end, as in the films, and the legs keep pace with the arms
const NANO_PACE = { Neck: 2.2, LeftUpLeg: 0.7, RightUpLeg: 0.7 };
const GAUNTLET = 1.15;    // gauntlet size over the tracked hand's, to cover it
// [upper, lower, end] bones and the [pose] landmarks they follow, per model side
const LIMBS = {
  Left: { arm: [12, 14, 16], leg: [24, 26, 28], foot: 32, hand: [16, 18, 20] },
  Right: { arm: [11, 13, 15], leg: [23, 25, 27], foot: 31, hand: [15, 17, 19] },
};
// model finger bones 1..4 and the hand landmark at bone 1 (the next three follow it)
const FINGERS = [["Thumb", 1], ["Index", 5], ["Middle", 9], ["Ring", 13], ["Pinky", 17]];
const SPINE = ["Spine", "Spine1", "Spine2", "Spine3"];   // as many of these as the rig has, hips to chest
const REQUIRED = ["Hips", "Spine", "Spine2", "Neck", "Head",
  ..."Shoulder Arm ForeArm Hand UpLeg Leg Foot".split(" ").flatMap(b => [`Left${b}`, `Right${b}`])];

let T = null, clone = null;
const templates = {};   // suit -> its loaded model, to clone for each person
// per suit: { url, error, skipped }; skipped: files that failed before `url` loaded
export const modelStatus = {};

// "mixamorig:LeftArm", "mixamorigLeftArm" and Sketchfab's "mixamorig:LeftArm_09" are all "LeftArm";
// some rigs call the hips the pelvis, and the top of the head Head_end
const ALIAS = { Pelvis: "Hips", Head_end: "HeadTop_End" };
const boneName = n => {
  const b = n.replace(/^.*?mixamorig[:_]?/i, "").replace(/_\d+$/, "");
  return ALIAS[b] || b;
};

// Load every suit's model; true if at least one loaded.
export async function loadSuitModel(three) {
  T = three;
  let loader;
  try {
    const [{ GLTFLoader }, { DRACOLoader }, utils] = await Promise.all([
      import("three/addons/loaders/GLTFLoader.js"), import("three/addons/loaders/DRACOLoader.js"),
      import("three/addons/utils/SkeletonUtils.js")]);
    clone = utils.clone;
    loader = new GLTFLoader();
    loader.setDRACOLoader(new DRACOLoader().setDecoderPath("https://www.gstatic.com/draco/versioned/decoders/1.5.6/"));
  } catch (e) {
    for (const kind in SUITS) modelStatus[kind] = { url: "", error: `model loaders failed to load: ${e.message}`, skipped: "" };
    return false;
  }
  const wanted = new URLSearchParams(location.search).get("model");
  await Promise.all(Object.entries(SUITS).map(async ([kind, suit]) => {
    const st = modelStatus[kind] = { url: "", error: "", skipped: "" };
    const errors = [];
    for (const url of kind === "ironman" && wanted ? [wanted] : suit.files) {
      try {
        const gltf = await loader.loadAsync(new URL(url, ROOT_URL).href);
        templates[kind] = prepare(gltf.scene, url);
        st.url = url;
        st.skipped = errors.join("; ");
        if (errors.length) console.warn(`${kind} model fallback: ${st.skipped}`);
        return;
      } catch (e) {
        errors.push(`${url}: ${e.message || e}`);
      }
    }
    st.error = errors.join("; ");
    console.warn(`no ${kind} model: ${st.error}`);
  }));
  return Object.keys(templates).length > 0;
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
    else if (REACTOR.test(o.material.name)) {   // light up the model's own arc reactor
      o.material.emissive = new T.Color(0xbfefff);
      o.material.emissiveMap = o.material.map;
      o.material.emissiveIntensity = 1.6;
    }
  });
  bakeNanoDist(scene);
  scene.userData.lenses = findLenses(scene);
  if (scene.userData.lenses) bakeMask(scene);
  return scene;
}

// Which vertices are the mask (skinned mostly to the head) and how high up it each one is, as a
// "maskAt" attribute: 1 at the bottom of the head to 2 at the top, 0 for everything else (so a mesh
// without the attribute, which reads as 0, is never part of the mask).
function bakeMask(scene) {
  const parts = [];
  let lo = Infinity, hi = -Infinity;
  const v = V();
  scene.traverse(m => {
    if (!m.isSkinnedMesh) return;
    const head = m.skeleton.bones.map((b, i) => (["Head", "HeadTop_End"].includes(boneName(b.name)) ? i : -1)).filter(i => i >= 0);
    const pos = m.geometry.attributes.position, si = m.geometry.attributes.skinIndex, sw = m.geometry.attributes.skinWeight;
    const ys = new Float32Array(pos.count).fill(NaN);
    for (let i = 0; i < pos.count; i++) {
      let w = 0;
      for (let c = 0; c < 4; c++) if (head.includes(si.getComponent(i, c))) w += sw.getComponent(i, c);
      if (w < 0.5) continue;
      ys[i] = m.applyBoneTransform(i, v.fromBufferAttribute(pos, i)).applyMatrix4(m.matrixWorld).y;
      lo = Math.min(lo, ys[i]); hi = Math.max(hi, ys[i]);
    }
    parts.push([m, ys]);
  });
  for (const [m, ys] of parts) {
    m.geometry.setAttribute("maskAt", new T.BufferAttribute(ys.map(y => (Number.isNaN(y) ? 0 : 1 + (y - lo) / (hi - lo || 1))), 1));
  }
}

// The two eye lenses' centres and half sizes [cx, cy, half width, half height], in the lens mesh's
// own space (the model's left eye on +x), or null if the suit has none.
function findLenses(scene) {
  const sides = [[], []];   // [model's left (+x), right]
  scene.traverse(m => {
    if (!m.isMesh || !LENS.test(m.material.name)) return;
    const pos = m.geometry.attributes.position;
    for (let i = 0; i < pos.count; i++) sides[pos.getX(i) > 0 ? 0 : 1].push([pos.getX(i), pos.getY(i)]);
  });
  if (!sides[0].length || !sides[1].length) return null;
  return sides.map(pts => {
    const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
    const [x0, x1, y0, y1] = [Math.min(...xs), Math.max(...xs), Math.min(...ys), Math.max(...ys)];
    return [(x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2, (y1 - y0) / 2];
  });
}

// A lens's material (already taught nanotech), with eyelids painted over it in the frame's glossy
// black: the upper lid comes down as the eye closes (the lower one comes up a little), a frown
// tips the upper lid down at the inner corner, raised inner brows tip it up. Nothing moves, so the
// mask never shows a gap. At their usual openness the lid sits just inside the lens's top, leaving
// room to open wider. u holds this rig's eye uniforms.
function lensMaterial(m, u) {
  const nano = m.onBeforeCompile;
  m.onBeforeCompile = (sh, renderer) => {
    nano(sh, renderer);
    Object.assign(sh.uniforms, u);
    sh.vertexShader = sh.vertexShader
      .replace("#include <common>", "#include <common>\nvarying vec2 vLensPos;")
      .replace("#include <begin_vertex>", "#include <begin_vertex>\nvLensPos = position.xy;");
    sh.fragmentShader = sh.fragmentShader
      .replace("#include <common>", `#include <common>
uniform vec4 uLensL, uLensR;   // centre and half size of the model's left / right lens
uniform vec2 uOpen;            // how open its left / right eye is (1 = their usual)
uniform vec2 uBrow;            // frown, raised inner brows
varying vec2 vLensPos;`)
      .replace("#include <color_fragment>", `#include <color_fragment>
float lensLid;
{
  bool left = vLensPos.x > 0.0;
  vec4 lens = left ? uLensL : uLensR;
  float open = left ? uOpen.x : uOpen.y;
  vec2 n = (vLensPos - lens.xy) / lens.zw;   // -1..1 across the lens
  float inner = clamp(0.5 - (left ? 0.5 : -0.5) * n.x, 0.0, 1.0);   // 1 at the nose side
  float top = open < 1.0 ? -0.35 + (open - 0.1) * 1.444 : 0.95 + (open - 1.0) * 0.4;
  top += inner * (uBrow.y * 0.25 - uBrow.x * 0.9);
  float bottom = -1.0 + 0.3 * clamp(1.0 - open, 0.0, 1.0);
  lensLid = max(smoothstep(top - 0.05, top + 0.05, n.y), 1.0 - smoothstep(bottom - 0.05, bottom + 0.05, n.y));
  diffuseColor.rgb = mix(diffuseColor.rgb, vec3(0.025), lensLid);
}`)
      .replace("#include <roughnessmap_fragment>", `#include <roughnessmap_fragment>
roughnessFactor = mix(roughnessFactor, 0.3, lensLid);`);
  };
  return m;
}

// Ease the rig's lenses toward this face's expression (p.face from face.js; neutral once it's
// stale). now in ms, dt in seconds.
export function setEyes(r, face, now, dt) {
  if (!r.eyes3) return;
  const fresh = face && now - face.at < EYES_STALE;
  const k = 1 - Math.exp(-dt / EYES_EASE), u = r.eyes3;
  const toward = (v, t) => v + (t - v) * k;
  const open = side => Math.min(OPEN_MAX, Math.max(0.1, fresh ? face.open[side] : 1));
  u.uOpen.value.set(toward(u.uOpen.value.x, open(0)), toward(u.uOpen.value.y, open(1)));
  u.uBrow.value.set(toward(u.uBrow.value.x, fresh ? face.browDown : 0), toward(u.uBrow.value.y, fresh ? face.browUp : 0));
}

// Every vertex's distance from the arc reactor over the body, as a 0..1 "nanoDist" attribute: straight
// through the torso, then out along the skeleton (up the neck, down the arms and legs), so the
// nanotech suit spreads from the reactor like a flood fill over the body. Blended by skin weight, so
// it runs smoothly across the joints. Also records the model's height, which scales the plate
// pattern in the shader.
function bakeNanoDist(scene) {
  scene.updateMatrixWorld(true);
  const bones = {};
  scene.traverse(o => { if (o.isBone) bones[boneName(o.name)] = o; });
  const at = b => b.getWorldPosition(V());
  const R = reactorWorld(scene)?.centre ?? at(bones.Spine2);
  const torso = new Set(["Hips", ...SPINE].map(n => bones[n]).filter(Boolean));
  // limb / neck bone -> [distance from the reactor to its joint over the skeleton, pace along it]
  const reach = new Map();
  const out = (b, d, k) => {
    reach.set(b, [d, k]);
    for (const c of b.children) if (c.isBone && !torso.has(c)) out(c, d + k * at(b).distanceTo(at(c)), k);
  };
  for (const b of torso) {
    for (const c of b.children) {
      if (!c.isBone || torso.has(c)) continue;
      const k = NANO_PACE[boneName(c.name)] ?? 1;
      out(c, at(b).distanceTo(R) + k * at(b).distanceTo(at(c)), k);
    }
  }
  const v = V(), meshes = [];
  let far = 0;
  scene.traverse(m => {
    if (!m.isSkinnedMesh) return;
    const pos = m.geometry.attributes.position, si = m.geometry.attributes.skinIndex, sw = m.geometry.attributes.skinWeight;
    const d = new Float32Array(pos.count);
    for (let i = 0; i < pos.count; i++) {
      m.applyBoneTransform(i, v.fromBufferAttribute(pos, i)).applyMatrix4(m.matrixWorld);
      let sum = 0, wsum = 0;
      for (let c = 0; c < 4; c++) {
        const w = sw.getComponent(i, c);
        if (!w) continue;
        const b = m.skeleton.bones[si.getComponent(i, c)];
        const r = reach.get(b);
        sum += w * (r ? r[0] + r[1] * v.distanceTo(at(b)) : v.distanceTo(R));
        wsum += w;
      }
      d[i] = wsum ? sum / wsum : v.distanceTo(R);
      if (d[i] > far) far = d[i];
    }
    meshes.push([m, d]);
  });
  for (const [m, d] of meshes) {
    for (let i = 0; i < d.length; i++) d[i] /= far || 1;
    m.geometry.setAttribute("nanoDist", new T.BufferAttribute(d, 1));
  }
  // height in the meshes' own units, which is what the shader's positions are in
  let tall = 0;
  for (const [m] of meshes) {
    m.geometry.computeBoundingBox();
    tall = Math.max(tall, m.geometry.boundingBox.max.y - m.geometry.boundingBox.min.y);
  }
  scene.userData.height = tall || 1;
}

// The suit's materials, taught the nanotech front: hide what it hasn't reached yet, and turn the band
// just behind it into glowing liquid silver. u holds this rig's uniforms.
function nanoMaterial(base, u) {
  const m = base.clone();
  m.onBeforeCompile = sh => {
    Object.assign(sh.uniforms, u);
    sh.vertexShader = sh.vertexShader
      .replace("#include <common>", "#include <common>\nattribute float nanoDist, maskAt;\nvarying float vNanoDist, vMaskAt;\nvarying vec3 vNanoPos;")
      .replace("#include <begin_vertex>", "#include <begin_vertex>\nvNanoDist = nanoDist;\nvMaskAt = maskAt;\nvNanoPos = position;");
    sh.fragmentShader = sh.fragmentShader
      .replace("#include <common>", `#include <common>
uniform float uNanoFront, uNanoTime, uNanoScale, uMaskOff;
varying float vNanoDist, vMaskAt;
varying vec3 vNanoPos;
float nanoHash(vec3 c) { return fract(sin(dot(c, vec3(12.9898, 78.233, 37.719))) * 43758.5453); }
// smooth value noise, 0..1
float nanoNoise(vec3 p) {
  vec3 i = floor(p), f = fract(p);
  f = f * f * (3.0 - 2.0 * f);
  vec2 e = vec2(1.0, 0.0);
  return mix(mix(mix(nanoHash(i), nanoHash(i + e.xyy), f.x), mix(nanoHash(i + e.yxy), nanoHash(i + e.xxy), f.x), f.y),
             mix(mix(nanoHash(i + e.yyx), nanoHash(i + e.xyx), f.x), mix(nanoHash(i + e.yxx), nanoHash(i + e.xxx), f.x), f.y), f.z);
}`)
      .replace("#include <clipping_planes_fragment>", `#include <clipping_planes_fragment>
vec3 nanoP = vNanoPos * uNanoScale;
// the front runs a bit ahead here and behind there, so it's ragged: broad tongues of it with a fine
// grainy edge, smooth noise rather than cells so nothing pops in as a block. Only while it forms:
// the noise is most of this shader's cost, and once formed there's no front.
float nanoBehind = 1.0;
if (uNanoFront < ${NANO_REACH.toFixed(4)}) {
  float nanoRag = 0.6 * nanoNoise(nanoP * 14.0) + 0.3 * nanoNoise(nanoP * 45.0) + 0.1 * nanoNoise(nanoP * 140.0);
  nanoBehind = uNanoFront - vNanoDist - ${NANO_PLATES.toFixed(4)} * (nanoRag * 2.0 - 1.0);
  if (nanoBehind < 0.0) discard;
}
// the mask peeling off, from the neck up (vMaskAt: 1 at the bottom of the head, 2 at the top)
if (vMaskAt > 0.5 && vMaskAt - 1.0 < uMaskOff * 1.1 - 0.05) discard;
#ifndef FLAT_SHADED
// While it forms, the gaps would show the suit's inside (the far wall of the shell, seen from
// within): skip surfaces facing away from the camera, so the real body shows through instead. By
// the model's normals (view space, camera down -z), not triangle winding, which models get wrong.
if (uNanoFront < ${NANO_REACH.toFixed(4)} && vNormal.z < 0.0) discard;
#endif
float nanoSilver = 1.0 - smoothstep(0.0, ${NANO_EDGE.toFixed(4)}, nanoBehind);
float nanoHot = 1.0 - smoothstep(0.0, ${(NANO_EDGE * 0.35).toFixed(4)}, nanoBehind);`)
      .replace("#include <color_fragment>", `#include <color_fragment>
diffuseColor.rgb = mix(diffuseColor.rgb, vec3(0.86, 0.88, 0.92), nanoSilver);`)
      .replace("#include <roughnessmap_fragment>", `#include <roughnessmap_fragment>
roughnessFactor = mix(roughnessFactor, 0.12, nanoSilver);`)
      .replace("#include <metalnessmap_fragment>", `#include <metalnessmap_fragment>
metalnessFactor = mix(metalnessFactor, 1.0, nanoSilver);`)
      .replace("#include <emissivemap_fragment>", `#include <emissivemap_fragment>
float nanoRipple = 0.55 + 0.45 * sin(vNanoDist * 220.0 - uNanoTime * 16.0 + nanoP.x * 60.0);
totalEmissiveRadiance += vec3(0.75, 0.85, 1.0) * (nanoHot * 3.0 + nanoSilver * nanoRipple * 0.6);`);
  };
  return m;
}

// How far the mask is off (0 on, 1 off), for a suit that has one.
export function setMaskOff(r, off) { r.nano.uMaskOff.value = off; }

// Set how far the nanotech suit has formed (level 0..1) on this rig; t is the time in seconds.
export function setNano(r, level, t) {
  r.nano.uNanoFront.value = level * NANO_REACH;
  r.nano.uNanoTime.value = t;
}

export const modelReady = kind => !!templates[kind];

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

export function makeBodyRig(scene, kind) {
  const holder = new T.Group();
  const template = templates[kind];
  const model = clone(template);
  holder.add(model);
  scene.add(holder);
  const bones = {};
  model.traverse(o => { if (o.isBone) bones[boneName(o.name)] = o; });
  const rest = new Map();
  for (const b of Object.values(bones)) rest.set(b, { q: b.quaternion.clone(), p: b.position.clone(), s: b.scale.clone() });
  const nano = { uNanoFront: { value: NANO_REACH }, uNanoTime: { value: 0 }, uNanoScale: { value: 1 / template.userData.height },
    uMaskOff: { value: 0 } };
  const lenses = template.userData.lenses;
  const eyes3 = lenses && { uLensL: { value: new T.Vector4(...lenses[0]) }, uLensR: { value: new T.Vector4(...lenses[1]) },
    uOpen: { value: new T.Vector2(1, 1) }, uBrow: { value: new T.Vector2(0, 0) } };
  const own = new Map();   // this rig's copy of each material, carrying its own nanotech front (and eyes)
  model.traverse(m => {
    if (!m.isMesh || !m.geometry.attributes.nanoDist) return;
    if (!own.has(m.material)) {
      const mat = nanoMaterial(m.material, nano);
      own.set(m.material, eyes3 && LENS_PARTS.test(m.material.name) ? lensMaterial(mat, eyes3) : mat);
    }
    m.material = own.get(m.material);
  });

  holder.updateMatrixWorld(true);
  const wp = n => bones[n].getWorldPosition(V());
  const wq = n => bones[n].getWorldQuaternion(Q());
  const up = mid(wp("LeftArm"), wp("RightArm")).sub(mid(wp("LeftUpLeg"), wp("RightUpLeg")));
  const body = frame(wp("LeftArm").sub(wp("RightArm")), up);
  const bodyInv = body.clone().invert();
  // each frame-driven bone's rest rotation relative to the frame that drives it
  const offset = {};
  for (const n of ["Hips", ...SPINE, "Neck", "Head"]) {
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
    kind, holder, bones, rest, offset, hinge, eyes, nano, eyes3, headPerEar: SUITS[kind].headPerEar,
    spine: SPINE.filter(n => bones[n]), head: headWidth(model, bones, body), reactor: findReactor(model),
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

// The model's own arc reactor, if it has one: the bone it rides on, and its centre and rim in that
// bone's space.
function findReactor(model) {
  const r = reactorWorld(model);
  return r && { bone: r.bone, centre: r.bone.worldToLocal(r.centre), rim: r.bone.worldToLocal(r.rim) };
}

// ...and in world space, in the rest pose.
function reactorWorld(model) {
  const v = V(), lo = V().setScalar(Infinity), hi = V().setScalar(-Infinity);
  const weight = new Map();
  model.traverse(m => {
    if (!m.isSkinnedMesh || !REACTOR.test(m.material.name)) return;
    const pos = m.geometry.attributes.position, si = m.geometry.attributes.skinIndex, sw = m.geometry.attributes.skinWeight;
    for (let i = 0; i < pos.count; i += 7) {
      m.applyBoneTransform(i, v.fromBufferAttribute(pos, i)).applyMatrix4(m.matrixWorld);
      lo.min(v); hi.max(v);
      for (let c = 0; c < 4; c++) {
        const b = m.skeleton.bones[si.getComponent(i, c)];
        weight.set(b, (weight.get(b) || 0) + sw.getComponent(i, c));
      }
    }
  });
  if (!weight.size) return null;
  const bone = [...weight].sort((a, b) => b[1] - a[1])[0][0];
  const centre = mid(lo, hi);
  return { bone, centre, rim: centre.clone().add(V().set((hi.x - lo.x) / 2, 0, 0)) };
}

// Screen position and radius of the model's own arc reactor (null if it has none).
export function reactorPoint(r) {
  if (!r.reactor || !r.holder.visible) return null;
  const { bone, centre, rim } = r.reactor;
  const c = bone.localToWorld(centre.clone()), e = bone.localToWorld(rim.clone());
  return { x: c.x, y: -c.y, size: c.distanceTo(e) };
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
export function poseBody(r, P, hands, dt) {
  const b = r.bones;
  for (const [bone, s] of r.rest) { bone.quaternion.copy(s.q); bone.position.copy(s.p); bone.scale.copy(s.s); }
  r.holder.visible = true;

  const hips = mid(P(23), P(24)), shoulders = mid(P(11), P(12)), up = shoulders.clone().sub(hips);
  const width = P(12).distanceTo(P(11));
  const s = (up.length() + width) / (r.torso + r.width);
  r.s = steady(r.s, s, dt);
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
  // the spine bones turn by steps from the hips' frame to the chest's
  const drive = { Hips: hipF, Neck: along(chestF, headF, 0.5), Head: headF };
  r.spine.forEach((n, i) => { drive[n] = along(hipF, chestF, (i + 1) / r.spine.length); });
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
    const k = P(8).distanceTo(P(7)) * r.headPerEar / (r.head * r.s);
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
