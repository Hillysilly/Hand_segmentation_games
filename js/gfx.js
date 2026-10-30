// Canvas drawing primitives shared by the effects.
import { ctx } from "./state.js";
import { rand } from "./util.js";

export function glow(x, y, r, stops) {
  const g = ctx.createRadialGradient(x, y, 0, x, y, r);
  stops.forEach(([o, c]) => g.addColorStop(o, c));
  ctx.fillStyle = g;
  ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fill();
}

export function line(x1, y1, x2, y2, w, style) {
  ctx.lineWidth = w; ctx.strokeStyle = style;
  ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();
}

// Points of a jagged electric arc between two points.
export function jagged(x1, y1, x2, y2, segs, jitter) {
  const pts = [[x1, y1]];
  const nx = -(y2 - y1), ny = x2 - x1, nl = Math.hypot(nx, ny) || 1;
  for (let s = 1; s < segs; s++) {
    const t = s / segs, o = rand(-jitter, jitter);
    pts.push([x1 + (x2 - x1) * t + nx / nl * o, y1 + (y2 - y1) * t + ny / nl * o]);
  }
  pts.push([x2, y2]);
  return pts;
}

export function strokePts(pts, style, width) {
  ctx.strokeStyle = style; ctx.lineWidth = width;
  ctx.beginPath();
  pts.forEach(([x, y], i) => i ? ctx.lineTo(x, y) : ctx.moveTo(x, y));
  ctx.stroke();
}

export function bolt(x1, y1, x2, y2, segs, jitter) {
  strokePts(jagged(x1, y1, x2, y2, segs, jitter), ctx.strokeStyle, ctx.lineWidth);
}
