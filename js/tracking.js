// Live input: webcam frames through MediaPipe's hand and pose landmarkers, plus the microphone.
import { HandLandmarker, PoseLandmarker, FilesetResolver } from
  "https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14/vision_bundle.mjs";
import { POSE_WIDTH } from "./config.js";
import { frame, timed } from "./state.js";
import { sfx, mic } from "./audio.js";
import { ingestPoses } from "./people.js";
import { ingestHands } from "./hands.js";

const WASM_PATH = "https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14/wasm";
const HAND_MODEL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task";
const POSE_MODEL = "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task";

async function createModels() {
  const fileset = await FilesetResolver.forVisionTasks(WASM_PATH);
  const make = delegate => Promise.all([
    HandLandmarker.createFromOptions(fileset, {
      baseOptions: { modelAssetPath: HAND_MODEL, delegate },
      runningMode: "VIDEO",
      numHands: 4,
      minHandDetectionConfidence: 0.5,
      minHandPresenceConfidence: 0.5,
      minTrackingConfidence: 0.5,
    }),
    // Pose stays on CPU: GPU segmentation masks can read back as all zeros on some machines,
    // and the lite model on a 320px frame is cheap anyway.
    PoseLandmarker.createFromOptions(fileset, {
      baseOptions: { modelAssetPath: POSE_MODEL, delegate: "CPU" },
      runningMode: "VIDEO",
      numPoses: 4,
      outputSegmentationMasks: true,
      minPoseDetectionConfidence: 0.5,
      minPosePresenceConfidence: 0.5,
      minTrackingConfidence: 0.5,
    }),
  ]);
  try {
    return await make("GPU");
  } catch (e) {
    console.warn("GPU delegate failed, falling back to CPU", e);
    return await make("CPU");
  }
}

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
    [src.hand, src.pose] = await createModels();
    frame.img = video;
    return src;
  }

  constructor(video) {
    this.video = video;
    this.lastTime = -1;
    // Tracking takes tens of ms and the video keeps playing meanwhile, so the screen shows a copy of
    // the frame the trackers saw (not the live video): the picture, the masks and the landmarks
    // then all line up.
    this.snap = document.createElement("canvas");
    this.snapCtx = this.snap.getContext("2d");
    this.poseCanvas = document.createElement("canvas");
    this.poseCtx = this.poseCanvas.getContext("2d");
    this.poseCanvas.width = POSE_WIDTH;
    this.poseCanvas.height = Math.round(POSE_WIDTH * video.videoHeight / video.videoWidth);
  }

  poll(now) {
    const v = this.video;
    if (v.readyState < 2 || v.currentTime === this.lastTime) return;
    this.lastTime = v.currentTime;
    frame.w = v.videoWidth || 1280; frame.h = v.videoHeight || 720;
    if (this.snap.width !== frame.w || this.snap.height !== frame.h) {
      this.snap.width = frame.w; this.snap.height = frame.h;
    }
    this.snapCtx.drawImage(v, 0, 0, frame.w, frame.h);
    frame.img = this.snap;
    this.poseCtx.drawImage(this.snap, 0, 0, this.poseCanvas.width, this.poseCanvas.height);
    // masks are only valid inside the callback, so they're copied out there
    timed("pose", () => this.pose.detectForVideo(this.poseCanvas, now, result => ingestPoses(result, now)));
    ingestHands(timed("hands", () => this.hand.detectForVideo(this.snap, now)), now);
  }
}
