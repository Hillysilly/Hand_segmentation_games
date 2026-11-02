// Spider-Man: the web-shooter sign fires webs into the room. Each web is a ray from the hand along
// its aim, in 3D, that sticks where it meets the room (see room.js: floor, ceiling, walls, or the
// "glass" in front of the camera when fired straight at it). The web lies flat on that surface in
// perspective, the thread sags a little and thins with distance, and webs on surfaces behind people
// are drawn behind them.
import { ctx, W, H, DPR, persons } from "../state.js";
import { rand, dist, videoRect, rayToEdge } from "../util.js";
import { isFresh } from "../people.js";
import { sfx } from "../audio.js";
import { hitRoom, project, unproject, GLASS } from "../room.js";

export const WEB_COOLDOWN = 500;   // ms between webs while holding the sign
const WEB_LIFE = 10;               // seconds a web stays
const SAG = 0.04;                  // thread sag, as a share of its length
const SHADOW = 0.025;              // metres the web's shadow falls below it on the surface
export const webs = [];

const sub = (a, b) => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });
const add = (a, b, s = 1) => ({ x: a.x + b.x * s, y: a.y + b.y * s, z: a.z + b.z * s });
const cross = (a, b) => ({ x: a.y * b.z - a.z * b.y, y: a.z * b.x - a.x * b.z, z: a.x * b.y - a.y * b.x });
const norm = a => { const l = Math.hypot(a.x, a.y, a.z) || 1; return { x: a.x / l, y: a.y / l, z: a.z / l }; };

// Where the hand is in the room, and which way it points (wrist to middle knuckle, a little
// scattered): in 3D from its own world landmarks, mirrored like the screen. (Its outline on screen
// isn't enough: pointing away from the camera, that's foreshortened toward the middle of the view.)
function aim(h) {
  const p = h.owner;
  let z = 1.5;   // no owner to measure: a guess
  if (p?.depth && p.world) {
    const wrist = dist(h.pts[0], p.lm[15]) < dist(h.pts[0], p.lm[16]) ? 15 : 16;
    z = p.depth + p.world[wrist].z;
  }
  const from = unproject(h.pts[9].x, h.pts[9].y, z);
  const scatter = () => rand(-0.15, 0.15);
  let dir;
  if (h.world) {
    const w = h.world;
    dir = norm({ x: -(w[9].x - w[0].x), y: w[9].y - w[0].y, z: w[9].z - w[0].z });
  } else {
    const a = Math.atan2(h.pts[9].y - h.pts[0].y, h.pts[9].x - h.pts[0].x);
    dir = { x: Math.cos(a), y: Math.sin(a), z: 0 };
  }
  return { from, dir: norm({ x: dir.x + scatter(), y: dir.y + scatter(), z: dir.z + scatter() }) };
}

// A thread zips out from the palm and a web unfurls where it lands.
export function shootWeb(h, now) {
  h.lastWeb = now;
  const { from, dir } = aim(h);
  const hit = hitRoom(from, dir);
  if (!hit) return;
  if (hit.surface === "glass") {
    // Fired at the camera: it splats on the glass somewhere along its path across the screen
    // (where it'd really cross the glass is mostly out of view, that close to the lens).
    const a = project(from), b = project({ x: from.x + dir.x * 0.1, y: from.y + dir.y * 0.1, z: from.z + dir.z * 0.1 });
    const l = Math.hypot(b.x - a.x, b.y - a.y) || 1, dx = (b.x - a.x) / l, dy = (b.y - a.y) / l;
    const d = Math.max(80, rayToEdge(a.x, a.y, dx, dy) * rand(0.45, 0.95));
    hit.at = unproject(a.x + dx * d, a.y + dy * d, GLASS);
  }
  // two directions along the surface, to lay the web out on it
  const n = hit.normal, u = norm(cross(n, Math.abs(n.y) > 0.9 ? { x: 0, y: 0, z: 1 } : { x: 0, y: 1, z: 0 }));
  const v = cross(n, u);
  const count = 9 + (Math.random() * 4 | 0), base = rand(0, Math.PI * 2);
  const centre = project(hit.at);
  webs.push({
    at: hit.at, surface: hit.surface, from, u, v, born: now,
    x: centre.x, y: centre.y,   // where it is on screen
    // behind its shooter: drawn behind people
    behind: hit.surface !== "glass" && hit.at.z > from.z + 0.2,
    R: hit.surface === "glass" ? rand(0.03, 0.05) : rand(0.25, 0.4),   // metres
    spokes: Array.from({ length: count }, (_, i) => ({ a: base + (i + rand(-0.25, 0.25)) / count * Math.PI * 2, len: rand(0.85, 1.1) })),
    rings: 5 + (Math.random() * 3 | 0),
  });
  if (webs.length > 25) webs.shift();
  sfx.thwip();
}

export function updateWebs(now) {
  for (let i = webs.length - 1; i >= 0; i--) if ((now - webs[i].born) / 1000 > WEB_LIFE) webs.splice(i, 1);
}

// The web's threads as a path on c: spokes from the centre, and rings between neighbouring spokes
// sagging toward the centre, all laid on the surface and seen in perspective. drop: metres to shift
// it down the surface (for its shadow).
function webPath(c, w, g, drop = 0) {
  const centre = add(w.at, w.v, -drop);
  const on = (s, f) => add(add(centre, w.u, Math.cos(s.a) * w.R * s.len * f), w.v, Math.sin(s.a) * w.R * s.len * f);
  const mid = project(centre);
  c.beginPath();
  for (const s of w.spokes) {
    const e = project(on(s, g));
    c.moveTo(mid.x, mid.y); c.lineTo(e.x, e.y);
  }
  for (let k = 1; k <= w.rings; k++) {
    const f = k / w.rings * g;
    w.spokes.forEach((s, i) => {
      const A = on(s, f), B = on(w.spokes[(i + 1) % w.spokes.length], f);
      const M = add(add(A, sub(B, A), 0.5), sub(centre, add(A, sub(B, A), 0.5)), 0.18);   // sags toward the centre
      const a = project(A), b = project(B), m = project(M);
      c.moveTo(a.x, a.y);
      c.quadraticCurveTo(m.x, m.y, b.x, b.y);
    });
  }
}

// Thread width in pixels for something at depth z (metres).
const thick = (z, base) => Math.max(0.6, Math.min(3.5, base * 2 / z));

function drawWeb(c, w, now) {
  const age = (now - w.born) / 1000;
  if (age < 0.12) return;   // the thread is still on its way
  const g = Math.min(1, (age - 0.12) / 0.15);   // then the web unfurls
  c.globalAlpha = Math.min(1, WEB_LIFE - age);  // fades over its last second
  if (w.surface !== "glass") {   // its shadow on the surface
    webPath(c, w, g, SHADOW);
    c.strokeStyle = "rgba(0,0,0,0.35)"; c.lineWidth = thick(w.at.z, 2); c.stroke();
  }
  webPath(c, w, g);
  c.strokeStyle = "rgba(0,0,0,0.3)"; c.lineWidth = thick(w.at.z, 2.2); c.stroke();   // contrast on bright backgrounds
  c.strokeStyle = "rgba(255,255,255,0.9)"; c.lineWidth = thick(w.at.z, 1.1); c.stroke();
  c.globalAlpha = 1;
}

// The thread from the hand, for its first moments: sagging, thinning into the distance.
function drawThread(w, now) {
  const age = (now - w.born) / 1000;
  if (age >= 0.4) return;
  const travel = Math.min(1, age / 0.12);   // reaches the target in 0.12 s
  const len = Math.hypot(...Object.values(sub(w.at, w.from)));
  const N = 16;
  ctx.globalAlpha = 1 - age / 0.4;
  ctx.strokeStyle = "rgba(255,255,255,0.95)";
  let prev = project(w.from);
  for (let i = 1; i <= N; i++) {
    const t = i / N * travel;
    const P = add(add(w.from, sub(w.at, w.from), t), { x: 0, y: 1, z: 0 }, SAG * len * 4 * t * (1 - t));
    const q = project(P);
    ctx.lineWidth = thick(P.z, 1.6);
    ctx.beginPath(); ctx.moveTo(prev.x, prev.y); ctx.lineTo(q.x, q.y); ctx.stroke();
    prev = q;
  }
  ctx.globalAlpha = 1;
}

// Webs on surfaces behind people: drawn on their own layer, with the people cut out of it, under
// everything that's in front.
let back = null;
export function drawWebsBehind(now) {
  const list = webs.filter(w => w.behind);
  if (!list.length) return;
  const cw = Math.round(W * DPR), ch = Math.round(H * DPR);
  if (!back || back.width !== cw || back.height !== ch) {
    back = document.createElement("canvas");
    back.width = cw; back.height = ch;
  }
  const c = back.getContext("2d");
  c.setTransform(1, 0, 0, 1, 0, 0);
  c.clearRect(0, 0, cw, ch);
  c.setTransform(DPR, 0, 0, DPR, 0, 0);
  c.lineCap = "round"; c.lineJoin = "round";
  for (const w of list) drawWeb(c, w, now);
  const r = videoRect();
  c.globalCompositeOperation = "destination-out";
  for (const p of persons) {
    if (!isFresh(p) || !p.fill) continue;
    c.save();
    c.translate(r.x + r.w, r.y); c.scale(-1, 1);
    c.drawImage(p.fill, 0, 0, r.w, r.h);
    c.restore();
  }
  c.globalCompositeOperation = "source-over";
  ctx.drawImage(back, 0, 0, W, H);
}

// Everything else: the webs in front of people, and every web's thread.
export function drawWebs(now) {
  ctx.lineCap = "round"; ctx.lineJoin = "round";
  for (const w of webs) if (!w.behind) drawWeb(ctx, w, now);
  for (const w of webs) drawThread(w, now);
}
