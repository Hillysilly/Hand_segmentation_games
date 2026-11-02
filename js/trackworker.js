// Tracking worker: runs one MediaPipe model (pose, hands or face) off the main thread, so drawing
// never waits for tracking, and the models run side by side. tracking.js starts one of these per model.
//
// A classic (not module) worker: MediaPipe's loader pulls in its wasm glue with importScripts, which
// module workers don't have. The library itself comes in through a dynamic import.
//
//   in:  { init: { kind: "pose" | "hands" | "face", vision, options, cpu?, width?, height?, points?, shapes? } }
//                                         -> { ready, delegate } or { error }
//        { id, t, bitmap }                -> { id, kind, ms, delegate, result }
// Pose's result also carries the downscaled frame's pixels ({ width, height, data }), for the cloak:
// reading them here costs this thread a memcpy, where reading a frame back on the main thread
// stalls the GPU that every model is queued on.
// width / height: run on a copy of the frame downscaled to this size (pose and face). points / shapes
// (face): which landmarks and expression scores to send back. cpu: run on the CPU, not the GPU.
"use strict";

const TASKS = { pose: "PoseLandmarker", hands: "HandLandmarker", face: "FaceLandmarker" };
let model = null, kind = "", delegate = "", lib = null, fileset = null, init = null;
let small = null, sctx = null;   // the downscaled copy of the frame
let blank = 0;                   // frames in a row with people but empty masks
const BLANK_FRAMES = 15;         // ...before giving up on GPU masks and moving pose to the CPU

const create = d => lib[TASKS[kind]].createFromOptions(fileset,
  { ...init.options, baseOptions: { ...init.options.baseOptions, delegate: d } });

async function start(msg) {
  init = msg; kind = msg.kind;
  lib = await import(`${msg.vision}/vision_bundle.mjs`);
  fileset = await lib.FilesetResolver.forVisionTasks(`${msg.vision}/wasm`);
  try {
    if (msg.cpu) throw new Error("asked for the CPU");
    model = await create("GPU"); delegate = "GPU";
  } catch (e) {
    if (!msg.cpu) console.warn(`${kind}: GPU delegate failed, using the CPU`, e);
    model = await create("CPU"); delegate = "CPU";
  }
  if (msg.width) {
    small = new OffscreenCanvas(msg.width, msg.height);
    sctx = small.getContext("2d", { willReadFrequently: kind === "pose" });   // kept in memory: read back every frame
  }
  // The first GPU frame compiles the model's shaders, which takes seconds: do it now, behind the
  // loading screen, not on the first camera frame. (Timestamp 1: real frames come later.)
  const blankFrame = new OffscreenCanvas(msg.width || 320, msg.height || 180);
  blankFrame.getContext("2d").fillRect(0, 0, blankFrame.width, blankFrame.height);
  const r = model.detectForVideo(blankFrame, 1);
  r.close?.();
}

// Are all of this result's masks empty while it found people? (Sampled: a body covers many pixels.)
const allBlank = masks => masks.every(({ data }) => {
  for (let i = 0; i < data.length; i += 7) if (data[i] > 0.5) return false;
  return true;
});

function track(bitmap, t) {
  if (kind === "hands") {
    const r = model.detectForVideo(bitmap, t);
    return { result: { landmarks: r.landmarks, worldLandmarks: r.worldLandmarks, handedness: r.handedness ?? r.handednesses }, transfer: [] };
  }
  const h = Math.round(small.width * bitmap.height / bitmap.width);
  if (small.height !== h) small.height = h;
  sctx.drawImage(bitmap, 0, 0, small.width, small.height);
  if (kind === "face") {
    const r = model.detectForVideo(small, t);
    const faces = r.faceLandmarks.map((lms, i) => ({
      pts: Object.fromEntries(init.points.map(j => [j, { x: lms[j].x, y: lms[j].y }])),
      shapes: Object.fromEntries((r.faceBlendshapes?.[i]?.categories || [])
        .filter(c => init.shapes.includes(c.categoryName)).map(c => [c.categoryName, c.score])),
    }));
    return { result: { faces }, transfer: [] };
  }
  // Masks are read from the returned result: read inside detectForVideo's callback, GPU masks come
  // back all zeros.
  const r = model.detectForVideo(small, t);
  const masks = (r.segmentationMasks || []).map(m => ({ width: m.width, height: m.height, data: m.getAsFloat32Array().slice() }));
  r.close();
  const px = sctx.getImageData(0, 0, small.width, small.height);
  const pixels = { width: px.width, height: px.height, data: px.data };
  if (delegate === "GPU") {
    blank = r.landmarks.length && masks.length && allBlank(masks) ? blank + 1 : 0;
    if (blank >= BLANK_FRAMES) {
      console.warn("pose masks empty on the GPU, moving pose to the CPU");
      delegate = "CPU";
      create("CPU").then(m => { const old = model; model = m; old.close(); });
    }
  }
  return { result: { landmarks: r.landmarks, worldLandmarks: r.worldLandmarks, masks, pixels },
    transfer: [...masks.map(m => m.data.buffer), pixels.data.buffer] };
}

onmessage = async ({ data }) => {
  if (data.init) {
    try {
      await start(data.init);
      postMessage({ ready: true, delegate });
    } catch (e) {
      postMessage({ error: String(e.stack || e) });
    }
    return;
  }
  const begin = performance.now();
  let out = { result: null, transfer: [] };
  try {
    out = track(data.bitmap, data.t);
  } catch (e) {
    console.warn(`${kind} tracking failed on a frame`, e);
  }
  data.bitmap.close();
  postMessage({ id: data.id, kind, ms: performance.now() - begin, delegate, result: out.result }, out.transfer);
};
