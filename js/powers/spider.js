// Spider-Man: the web-shooter sign fires webs that stick to the screen.
import { ctx, W, H } from "../state.js";
import { rand, rayToEdge } from "../util.js";
import { sfx } from "../audio.js";

export const WEB_COOLDOWN = 500;   // ms between webs while holding the sign
const WEB_LIFE = 10;               // seconds a web stays on screen
export const webs = [];

// A thread zips out from the palm along the hand's direction (wrist -> middle knuckle, with a little
// spread) and a web unfurls where it lands, stuck to the screen.
export function shootWeb(h, now) {
  h.lastWeb = now;
  const a = Math.atan2(h.pts[9].y - h.pts[0].y, h.pts[9].x - h.pts[0].x) + rand(-0.35, 0.35);
  const dx = Math.cos(a), dy = Math.sin(a), sx = h.pts[9].x, sy = h.pts[9].y;
  const d = Math.max(80, rayToEdge(sx, sy, dx, dy) * rand(0.45, 0.95));
  const n = 9 + (Math.random() * 4 | 0), base = rand(0, Math.PI * 2);
  webs.push({
    x: sx + dx * d, y: sy + dy * d, sx, sy, born: now,
    R: Math.min(W, H) * rand(0.08, 0.15),
    spokes: Array.from({ length: n }, (_, i) => ({ a: base + (i + rand(-0.25, 0.25)) / n * Math.PI * 2, len: rand(0.85, 1.1) })),
    rings: 5 + (Math.random() * 3 | 0),
  });
  if (webs.length > 25) webs.shift();
  sfx.thwip();
}

export function updateWebs(now) {
  for (let i = webs.length - 1; i >= 0; i--) if ((now - webs[i].born) / 1000 > WEB_LIFE) webs.splice(i, 1);
}

function webPath(w, g) {
  ctx.beginPath();
  for (const s of w.spokes) {
    ctx.moveTo(w.x, w.y);
    ctx.lineTo(w.x + Math.cos(s.a) * w.R * s.len * g, w.y + Math.sin(s.a) * w.R * s.len * g);
  }
  // spiral rings: threads between neighbouring spokes, sagging toward the center
  for (let k = 1; k <= w.rings; k++) {
    const f = k / w.rings * g;
    w.spokes.forEach((s, i) => {
      const t = w.spokes[(i + 1) % w.spokes.length];
      const ax = w.x + Math.cos(s.a) * w.R * s.len * f, ay = w.y + Math.sin(s.a) * w.R * s.len * f;
      const bx = w.x + Math.cos(t.a) * w.R * t.len * f, by = w.y + Math.sin(t.a) * w.R * t.len * f;
      const mx = (ax + bx) / 2, my = (ay + by) / 2;
      ctx.moveTo(ax, ay);
      ctx.quadraticCurveTo(mx + (w.x - mx) * 0.18, my + (w.y - my) * 0.18, bx, by);
    });
  }
}

export function drawWebs(now) {
  ctx.lineCap = "round"; ctx.lineJoin = "round";
  for (const w of webs) {
    const age = (now - w.born) / 1000;
    const travel = Math.min(1, age / 0.12);   // thread reaches the target in 0.12 s
    if (age < 0.4) {
      const tx = w.sx + (w.x - w.sx) * travel, ty = w.sy + (w.y - w.sy) * travel;
      ctx.globalAlpha = 1 - age / 0.4;
      ctx.strokeStyle = "rgba(255,255,255,0.95)"; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.moveTo(w.sx, w.sy); ctx.lineTo(tx, ty); ctx.stroke();
    }
    if (travel < 1) continue;
    const g = Math.min(1, (age - 0.12) / 0.15);   // then the web unfurls
    ctx.globalAlpha = Math.min(1, WEB_LIFE - age);   // fades over its last second
    webPath(w, g);
    ctx.strokeStyle = "rgba(0,0,0,0.3)"; ctx.lineWidth = 3; ctx.stroke();   // contrast on bright backgrounds
    ctx.strokeStyle = "rgba(255,255,255,0.9)"; ctx.lineWidth = 1.4; ctx.stroke();
  }
  ctx.globalAlpha = 1;
}
