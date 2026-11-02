// Live input: webcam frames through MediaPipe's hand and pose landmarkers, plus the microphone.
//
// Tracking runs in worker threads, one per model (see trackworker.js), so drawing never waits for it
// and the models run side by side. Each camera frame is copied, sent to pose and hands, and shown
// once both results are back, so the picture, the masks and the landmarks all line up. If workers
// can't run the models, tracking falls back to the main thread.
//
// Face tracking (for the Spider-Man suit's eyes) is a third worker, kept cheap: it starts the first
// time someone wears Spider-Man, only gets frames while they do, only every FACE_EVERY-th frame, at
// FACE_WIDTH, and frames never wait for it (its expressions land whenever they're ready). It runs on
// the CPU: on the GPU it queues behind (and slows) pose and hands, which every frame does wait for.
import { POSE_WIDTH, wearing } from "./config.js";
import { frame, timed, record, tracker } from "./state.js";
import { sfx, mic } from "./audio.js";
import { ingestPoses } from "./people.js";
import { ingestHands } from "./hands.js";
import { ingestFaces, FACE_POINTS, FACE_SHAPES } from "./face.js";

const VISION = "https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14";
const HAND_MODEL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task";
const POSE_MODEL = "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task";
const HAND_OPTIONS = {
  baseOptions: { modelAssetPath: HAND_MODEL },
  runningMode: "VIDEO",
  numHands: 4,
  minHandDetectionConfidence: 0.5,
  minHandPresenceConfidence: 0.5,
  minTrackingConfidence: 0.5,
};
const POSE_OPTIONS = {
  baseOptions: { modelAssetPath: POSE_MODEL },
  runningMode: "VIDEO",
  numPoses: 4,
  outputSegmentationMasks: true,
  minPoseDetectionConfidence: 0.5,
  minPosePresenceConfidence: 0.5,
  minTrackingConfidence: 0.5,
};
const FACE_MODEL = "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task";
const FACE_OPTIONS = {
  baseOptions: { modelAssetPath: FACE_MODEL },
  runningMode: "VIDEO",
  numFaces: 4,
  outputFaceBlendshapes: true,
  minFaceDetectionConfidence: 0.5,
  minFacePresenceConfidence: 0.5,
  minTrackingConfidence: 0.5,
};
const FACE_WIDTH = 640;   // face tracking runs on the frame downscaled to this width
const FACE_EVERY = 2;     // ...and on every this-many-th tracked frame
const POOL = 4;   // frame copies in rotation: shown, waiting to be shown, being tracked, spare

const withDelegate = (o, delegate) => ({ ...o, baseOptions: { ...o.baseOptions, delegate } });

// Start a tracking worker for one model; resolves once its model has loaded. aspect: the camera's
// height over width, for the downscaled copies pose and face run on.
function startWorker(kind, aspect) {
  return new Promise((resolve, reject) => {
    const worker = new Worker(new URL("./trackworker.js", import.meta.url));
    worker.onerror = e => reject(new Error(e.message || `${kind} worker failed to start`));
    worker.onmessage = ({ data }) => {
      if (data.error) reject(new Error(data.error));
      else if (data.ready) resolve({ worker, delegate: data.delegate });
    };
    const width = { pose: POSE_WIDTH, face: FACE_WIDTH }[kind];
    worker.postMessage({ init: { kind, vision: VISION, options: { pose: POSE_OPTIONS, hands: HAND_OPTIONS, face: FACE_OPTIONS }[kind],
      cpu: kind === "face", width, height: width && Math.round(width * aspect), points: FACE_POINTS, shapes: FACE_SHAPES } });
  }).catch(e => { throw Object.assign(e, { kind }); });
}

// A pose result from a worker, in the shape ingestPoses expects from MediaPipe.
const poseResult = r => ({ landmarks: r.landmarks, worldLandmarks: r.worldLandmarks,
  segmentationMasks: r.masks.map(m => ({ width: m.width, height: m.height, getAsFloat32Array: () => m.data })) });

export class CameraSource {
  // Ask for the camera and mic, load the models; status(text) reports progress.
  static async start(video, status) {
    status("requesting camera…");
    const stream = await navigator.mediaDevices.getUserMedia({
      video: { width: { ideal: 1280 }, height: { ideal: 720 }, facingMode: "user" }, audio: false,
    });
    video.srcObject = stream;
    await video.play();
    status("requesting microphone…");
    try {
      // auto gain off so a yell actually reads louder than talking
      mic.start(await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: true, noiseSuppression: false, autoGainControl: false },
      }), sfx.ac);
    } catch (e) {
      console.warn("no microphone, Goku can't power up", e);
    }
    status("loading tracking models…");
    const src = new CameraSource(video);
    src.aspect = video.videoHeight / video.videoWidth;
    const started = await Promise.allSettled([startWorker("pose", src.aspect), startWorker("hands", src.aspect)]);
    if (started.every(s => s.status === "fulfilled")) src.useWorkers(started[0].value, started[1].value);
    else {
      for (const s of started) if (s.status === "fulfilled") s.value.worker.terminate();
      console.warn("tracking workers failed, tracking on the main thread instead",
        started.filter(s => s.status === "rejected").map(s => s.reason));
      await src.loadInline();
    }
    frame.img = video;
    return src;
  }

  constructor(video) {
    this.video = video;
    this.lastTime = -1;
    // Tracking takes tens of ms and the video keeps playing meanwhile, so the screen shows a copy of
    // the frame the trackers saw (not the live video).
    this.pool = Array.from({ length: POOL }, () => document.createElement("canvas"));
    this.next = 0;
    this.id = 0;
    this.busy = null;    // the frame the workers are tracking
    this.done = null;    // a tracked frame, to show and ingest on the next poll
    this.doneAt = 0;
    this.face = { state: "off", worker: null, busy: false, result: null };   // state: off, starting, ready, failed
  }

  useWorkers(pose, hands) {
    this.workers = { pose: pose.worker, hands: hands.worker };
    for (const w of Object.values(this.workers)) w.onmessage = ({ data }) => this.arrived(data);
    tracker.where = `POSE ${pose.delegate}  HANDS ${hands.delegate}  THREADED`;
  }

  // Copy the newest camera frame into the next canvas of the pool.
  grab() {
    const v = this.video;
    if (v.readyState < 2 || v.currentTime === this.lastTime) return null;
    this.lastTime = v.currentTime;
    frame.w = v.videoWidth || 1280; frame.h = v.videoHeight || 720;
    const c = this.pool[this.next++ % POOL];
    if (c.width !== frame.w || c.height !== frame.h) { c.width = frame.w; c.height = frame.h; }
    c.getContext("2d").drawImage(v, 0, 0, frame.w, frame.h);
    return c;
  }

  // Send the newest camera frame to both workers, unless they're still on the last one.
  send() {
    if (this.busy) return;
    const canvas = this.grab();
    if (!canvas) return;
    const job = this.busy = { id: ++this.id, canvas, t: performance.now(), results: {} };
    for (const w of Object.values(this.workers)) {
      createImageBitmap(canvas).then(bitmap => w.postMessage({ id: job.id, t: job.t, bitmap }, [bitmap]));
    }
    const face = this.face;
    if (face.state === "ready" && !face.busy && job.id % FACE_EVERY === 0 && wearing() === "spiderman") {
      face.busy = true;
      createImageBitmap(canvas).then(bitmap => face.worker.postMessage({ id: job.id, t: job.t, bitmap }, [bitmap]));
    }
  }

  // Start face tracking (the first time someone wears Spider-Man), in the background.
  startFace() {
    const face = this.face;
    face.state = "starting";
    startWorker("face", this.aspect).then(({ worker, delegate }) => {
      worker.onmessage = ({ data }) => {
        face.busy = false;
        record("face", data.ms);
        if (data.result) face.result = data.result;
      };
      Object.assign(face, { worker, state: "ready" });
      tracker.where += `  FACE ${delegate}`;
    }, e => {
      face.state = "failed";
      console.warn("face tracking failed to start, Spider-Man's eyes won't follow yours", e);
    });
  }

  arrived(msg) {
    const job = this.busy;
    if (!job || msg.id !== job.id) return;
    job.results[msg.kind] = msg.result;
    record(msg.kind, msg.ms);
    if (msg.kind === "pose") tracker.where = tracker.where.replace(/POSE \w+/, `POSE ${msg.delegate}`);
    if (!("pose" in job.results && "hands" in job.results)) return;   // (face never holds a frame back)
    this.busy = null;
    const now = performance.now();
    if (this.doneAt) tracker.fps += (1000 / (now - this.doneAt) - tracker.fps) * 0.1;
    this.doneAt = now;
    this.done = job;
    this.send();   // straight on to the next frame, without waiting for the next poll
  }

  poll(now) {
    if (!this.workers) { this.pollInline(now); return; }
    const job = this.done;
    if (job) {
      this.done = null;
      frame.img = job.canvas;
      frame.pixels = job.results.pose?.pixels ?? null;
      if (job.results.pose) ingestPoses(poseResult(job.results.pose), now);
      if (job.results.hands) ingestHands(job.results.hands, now);
    }
    if (this.face.result) { ingestFaces(this.face.result.faces, now); this.face.result = null; }
    if (this.face.state === "off" && wearing() === "spiderman") this.startFace();
    this.send();
  }

  // ---------------------------------------------------------------- fallback: on the main thread

  // Pose stays on the CPU here (GPU masks are only safe to read the way the worker does).
  async loadInline() {
    const lib = await import(`${VISION}/vision_bundle.mjs`);
    const fileset = await lib.FilesetResolver.forVisionTasks(`${VISION}/wasm`);
    const pose = lib.PoseLandmarker.createFromOptions(fileset, withDelegate(POSE_OPTIONS, "CPU"));
    let handDelegate = "GPU";
    this.hand = await lib.HandLandmarker.createFromOptions(fileset, withDelegate(HAND_OPTIONS, "GPU")).catch(e => {
      console.warn("GPU delegate failed, hands on the CPU", e);
      handDelegate = "CPU";
      return lib.HandLandmarker.createFromOptions(fileset, withDelegate(HAND_OPTIONS, "CPU"));
    });
    this.pose = await pose;
    this.poseCanvas = document.createElement("canvas");
    this.poseCtx = this.poseCanvas.getContext("2d");
    this.poseCanvas.width = POSE_WIDTH;
    this.poseCanvas.height = Math.round(POSE_WIDTH * this.video.videoHeight / this.video.videoWidth);
    // compile the GPU shaders now, behind the loading screen (timestamp 1: real frames come later)
    this.hand.detectForVideo(this.poseCanvas, 1);
    this.pose.detectForVideo(this.poseCanvas, 1, () => {});
    tracker.where = `POSE CPU  HANDS ${handDelegate}  MAIN THREAD`;
  }

  pollInline(now) {
    const snap = this.grab();
    if (!snap) return;
    frame.img = snap;
    this.poseCtx.drawImage(snap, 0, 0, this.poseCanvas.width, this.poseCanvas.height);
    // masks are only valid inside the callback, so they're copied out there
    timed("pose", () => this.pose.detectForVideo(this.poseCanvas, now, result => ingestPoses(result, now)));
    ingestHands(timed("hands", () => this.hand.detectForVideo(snap, now)), now);
    const t = performance.now();
    if (this.doneAt) tracker.fps += (1000 / (t - this.doneAt) - tracker.fps) * 0.1;
    this.doneAt = t;
  }
}
