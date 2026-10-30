// Small math and screen-space helpers.
import { W, H, frame } from "./state.js";

export const dist = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
export const rand = (a, b) => a + Math.random() * (b - a);
export const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
export const rgb = s => s.split(",").map(Number);
// Blend color a toward color b by f (0..1), as an "r,g,b" string.
export const mixRgb = (a, b, f) => { const x = rgb(a), y = rgb(b); return x.map((v, i) => Math.round(v + (y[i] - v) * f)).join(","); };

// Rect the camera frame occupies on screen ("cover" fit).
export function videoRect() {
  const s = Math.max(W / frame.w, H / frame.h);
  const w = frame.w * s, h = frame.h * s;
  return { x: (W - w) / 2, y: (H - h) / 2, w, h };
}

// Normalized landmark -> mirrored screen coords.
export const toScreen = (lm, r) => ({ x: r.x + (1 - lm.x) * r.w, y: r.y + lm.y * r.h });

// Where a ray from (x,y) along unit (dx,dy) leaves the screen.
export function rayToEdge(x, y, dx, dy) {
  let t = Infinity;
  if (dx > 1e-6) t = Math.min(t, (W - x) / dx); else if (dx < -1e-6) t = Math.min(t, -x / dx);
  if (dy > 1e-6) t = Math.min(t, (H - y) / dy); else if (dy < -1e-6) t = Math.min(t, -y / dy);
  return isFinite(t) ? Math.max(0, t) : 0;
}

// Pair detections with existing tracks, closest first. cost() returns null when too far apart.
export function greedyMatch(dets, tracks, cost) {
  const pairs = [];
  dets.forEach((d, i) => tracks.forEach(t => {
    const c = cost(d, t);
    if (c !== null) pairs.push([c, i, t]);
  }));
  pairs.sort((a, b) => a[0] - b[0]);
  const match = new Map(), taken = new Set();
  for (const [, i, t] of pairs) {
    if (match.has(i) || taken.has(t)) continue;
    match.set(i, t); taken.add(t);
  }
  return match;
}
