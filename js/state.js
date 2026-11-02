// Shared mutable state. Modules import these as live bindings; only the setters here reassign them.

export let canvas = null, ctx = null;
export let W = 0, H = 0, DPR = 1;
export let clock = 0;   // timestamp of the current frame

// The picture behind everything: the camera video (or the simulator's drawn frame) and its pixel size;
// seen: when the newest tracked frame came in. pixels: that frame at the pose masks' size
// ({ width, height, data } RGBA), when the tracker sent it, so nothing has to read it back from the GPU.
export const frame = { img: null, w: 1280, h: 720, seen: 0, pixels: null };

// How long before the newest tracked frame something (a person, a hand) was last seen: 0 if it's in
// that frame. Counted from the newest frame, not the clock, so tracking running slow (the screen
// waits for it too) doesn't make everyone look lost, only missing from the frames that did arrive.
export const unseenFor = lastSeen => frame.seen - lastSeen;

export const fx = { flash: 0, shake: 0, flashColor: "170,230,255" };

export const persons = [];
export const hands = [];
export const particles = [];
export const shockwaves = [];
export const floaters = [];   // floating damage numbers and callouts

export function setCanvas(c) {
  canvas = c;
  ctx = c.getContext("2d");
}

export function setSize(w, h, dpr) {
  W = w; H = h; DPR = dpr;
  canvas.width = Math.round(W * DPR);
  canvas.height = Math.round(H * DPR);
}

export function setClock(t) { clock = t; }

// Milliseconds each stage of a frame takes (smoothed), shown in the HUD to find what's slow.
export const perf = {};
export function record(name, ms) {
  perf[name] = perf[name] === undefined ? ms : perf[name] + (ms - perf[name]) * 0.1;
}
export function timed(name, fn) {
  const t = performance.now(), out = fn();
  record(name, performance.now() - t);
  return out;
}

// How fast the camera is being tracked, and where (shown in the HUD).
export const tracker = { fps: 0, where: "" };

// Drop items in place; the lists are shared, so they're never reassigned.
export function prune(list, keep) {
  let j = 0;
  for (const x of list) if (keep(x)) list[j++] = x;
  list.length = j;
}
