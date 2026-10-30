// Cloak: paints the room back over the people in suits, so the suit replaces them instead of being
// pasted on top. The room behind them (a "clean plate") is learned from the camera frame wherever
// nobody is standing, a little every frame, so a spot gets filled in once they have moved off it or
// stepped out of shot. Until then the real body shows there.
//
// The body's mask is padded (more while they move: the mask trails fast limbs and motion blur) and
// its edge feathered, so no sliver of the real body shows around the suit.
//
// Learning is careful, because anything of a person that gets into the plate is later painted back
// over them as a ghost: it skips anywhere someone has been in the last second (suited or not, or
// just lost by the tracker), skips frames where someone's mask glitches (and keeps using their last
// good mask meanwhile), and only takes pixels that have held still since the last look.
import { POSE_WIDTH, settings } from "../config.js";
import { ctx, frame, persons, clock } from "../state.js";
import { videoRect, dist } from "../util.js";
import { isFresh } from "../people.js";
import { cloakFade } from "./nanotech.js";

const LEARN_EVERY = 4;   // frames between looks at the room
const LEARN = 0.3;       // how far the plate moves toward what it sees, per look
const GROW = 4;          // mask pixels the body is always widened by, to cover the halo around its edge
const LEAD = 0.07;       // ...plus how far they move in this many seconds
const MAX_GROW = 16;     // ...up to this many in all
const FEATHER = 3;       // mask pixels the padded edge fades out over
const KEEP_OUT = 1;      // seconds a spot stays off-limits for learning after anyone was on it
const KEEP_GROW = 8;     // mask pixels the off-limits zone reaches past their mask
const STILL = 18;        // most a pixel may change (per channel, 0..255) between looks to count as room
const DROPOUT = 0.6;     // a mask this much smaller than the person's usual is a glitch
const MOVERS = [0, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26];   // landmarks whose speed sets the padding

let w = 0, h = 0, grab, gctx, out, octx, outImg, plate, seen, body, tmp, keep, prev;
let lastClock = 0;
let shown = false;       // anything to paint this frame
let frames = 0, dq = null;
const motion = new WeakMap();   // person -> { pts, t, speed } for their speed in screen px/s
const masks = new WeakMap();    // person -> { area, good } their usual mask area and last good mask

function resize(mw, mh) {
  w = mw; h = mh;
  grab = document.createElement("canvas");
  grab.width = w; grab.height = h;
  gctx = grab.getContext("2d", { willReadFrequently: true });
  out = document.createElement("canvas");
  out.width = w; out.height = h;
  octx = out.getContext("2d");
  outImg = octx.createImageData(w, h);
  plate = new Float32Array(w * h * 3);
  seen = new Uint8Array(w * h);
  body = new Uint8Array(w * h);
  tmp = new Uint8Array(w * h);
  keep = new Uint8Array(w * h);
  prev = null;
  dq = new Int32Array(Math.max(w, h));
}

export function resetCloak() { w = h = 0; shown = false; frames = 0; }

// Look at the room wherever nobody has been lately and nothing is moving. Reading the frame back
// from the GPU is the slow part (a few ms for a camera frame), hence only every LEARN_EVERY frames.
function learn(img) {
  gctx.drawImage(img, 0, 0, w, h);
  const px = gctx.getImageData(0, 0, w, h).data, o = outImg.data, last = prev;
  prev = px;
  if (!last) return;   // nothing to compare with yet
  for (let i = 0; i < body.length; i++) {
    if (keep[i] >= 20) continue;   // somebody is or was just here
    const q = i * 4;
    if (Math.abs(px[q] - last[q]) > STILL || Math.abs(px[q + 1] - last[q + 1]) > STILL ||
      Math.abs(px[q + 2] - last[q + 2]) > STILL) continue;   // changing: not just the room
    const c = i * 3, k = seen[i] ? LEARN : 1;
    o[q] = plate[c] += (px[q] - plate[c]) * k;
    o[q + 1] = plate[c + 1] += (px[q + 1] - plate[c + 1]) * k;
    o[q + 2] = plate[c + 2] += (px[q + 2] - plate[c + 2]) * k;
    seen[i] = 1;
  }
}

// One pass of a sliding-window max of radius r along `lines` lines of n values (`step` apart, lines
// `lineStep` apart). A monotonic queue keeps the cost the same whatever the radius.
function maxPass(src, dst, n, step, lines, lineStep, r) {
  for (let l = 0, base = 0; l < lines; l++, base += lineStep) {
    let head = 0, tail = 0;
    for (let j = 0; j < n + r; j++) {
      if (j < n) {
        const v = src[base + j * step];
        while (tail > head && src[base + dq[tail - 1] * step] <= v) tail--;
        dq[tail++] = j;
      }
      const i = j - r;
      if (i < 0) continue;
      while (dq[head] < i - r) head++;
      dst[base + i * step] = src[base + dq[head] * step];
    }
  }
}

// ...and of a box blur of radius r, with a running sum.
function blurPass(src, dst, n, step, lines, lineStep, r) {
  for (let l = 0, base = 0; l < lines; l++, base += lineStep) {
    let sum = 0;
    for (let j = 0; j < n + r; j++) {
      if (j < n) sum += src[base + j * step];
      const i = j - r;
      if (i < 0) continue;
      if (i - r - 1 >= 0) sum -= src[base + (i - r - 1) * step];
      dst[base + i * step] = sum / (Math.min(n - 1, i + r) - Math.max(0, i - r) + 1);
    }
  }
}

// Pad the mask m by `grow` pixels, then fade its edge out over `feather` more. The blur eats into
// the padding, not the body.
function pad(m, grow, feather) {
  maxPass(m, tmp, w, 1, h, w, grow + feather);
  maxPass(tmp, m, h, w, w, 1, grow + feather);
  if (!feather) return;
  blurPass(m, tmp, w, 1, h, w, feather);
  blurPass(tmp, m, h, w, w, 1, feather);
}

// This person's mask, unless it has glitched (shrunk well below their usual size for a frame or
// two, which happens): then their last good one. Reports the glitch so learning can skip the frame.
function steadyMask(p) {
  const m = p.mask;
  let area = 0;
  for (let i = 0; i < m.length; i++) if (m[i] > 127) area++;
  const st = masks.get(p);
  if (st && area < st.area * DROPOUT) return { mask: st.good, glitch: true };
  if (!st) masks.set(p, { area, good: m.slice() });
  else { st.area += (area - st.area) * 0.1; st.good.set(m); }
  return { mask: m, glitch: false };
}

// How fast this person is moving (screen px/s): their fastest joint, held briefly at its peak so
// the padding doesn't snap back between frames.
function speedOf(p) {
  const pts = MOVERS.map(j => ({ x: p.lm[j].x, y: p.lm[j].y, v: p.lm[j].v }));
  const m = motion.get(p);
  motion.set(p, { pts, t: clock, speed: m?.speed ?? 0 });
  if (!m || clock <= m.t) return m?.speed ?? 0;
  const dt = (clock - m.t) / 1000;
  let fast = 0;
  pts.forEach((q, k) => { if (q.v > 0.5 && m.pts[k].v > 0.5) fast = Math.max(fast, dist(q, m.pts[k]) / dt); });
  const speed = Math.max(fast, m.speed * Math.exp(-dt / 0.3));
  motion.get(p).speed = speed;
  return speed;
}

export function updateCloak() {
  shown = false;
  const img = frame.img;
  if (!settings.armor || !settings.cloak || !img || (img.readyState !== undefined && img.readyState < 2)) return;
  const tracked = persons.filter(p => p.mask);   // anyone seen in the last moment, suited or not
  const mw = tracked[0]?.mw || POSE_WIDTH, mh = tracked[0]?.mh || Math.round(POSE_WIDTH * frame.h / frame.w);
  if (mw !== w || mh !== h) resize(mw, mh);
  const dt = lastClock ? Math.min(0.2, (clock - lastClock) / 1000) : 0;
  lastClock = clock;

  // everyone's masks: the off-limits zone for learning, which fades out KEEP_OUT after they leave
  body.fill(0);
  let glitch = false;
  const now = new Map();
  for (const p of tracked) {
    if (p.mw !== w || p.mh !== h) continue;
    const s = steadyMask(p);
    glitch ||= s.glitch && isFresh(p);
    now.set(p, s.mask);
    for (let i = 0; i < body.length; i++) if (s.mask[i] > body[i]) body[i] = s.mask[i];
  }
  if (now.size) pad(body, KEEP_GROW, 0);
  const fade = dt * 255 / KEEP_OUT;
  for (let i = 0; i < keep.length; i++) keep[i] = Math.max(body[i], keep[i] - fade);

  // the suited bodies: what gets painted over
  body.fill(0);
  let grow = GROW, any = false;
  const toMask = w / videoRect().w;   // screen px -> mask px
  for (const [p, m] of now) {
    const f = isFresh(p) ? cloakFade(p) : 0;
    if (!f) continue;
    any = true;
    if (p.lm) grow = Math.max(grow, Math.min(MAX_GROW, GROW + speedOf(p) * toMask * LEAD));
    for (let i = 0; i < body.length; i++) {
      const v = f < 1 ? m[i] * f : m[i];
      if (v > body[i]) body[i] = v;
    }
  }
  if (any) pad(body, Math.round(grow), FEATHER);

  const o = outImg.data;
  if (frames++ % LEARN_EVERY === 0 && !glitch) learn(img);
  for (let i = 0; i < body.length; i++) {   // paint over the bodies, wherever the room is known
    const a = seen[i] ? body[i] : 0;
    o[i * 4 + 3] = a;
    if (a) shown = true;
  }
  if (shown) octx.putImageData(outImg, 0, 0);
}

// Paint the learned room over the bodies, mirrored like the video.
export function drawCloak() {
  if (!shown || !settings.armor || !settings.cloak) return;
  const r = videoRect();
  ctx.save();
  ctx.translate(r.x + r.w, r.y); ctx.scale(-1, 1);
  ctx.drawImage(out, 0, 0, r.w, r.h);
  ctx.restore();
}

// Share of body pixels the cloak can paint over (0..1), for tests.
export function cloakCoverage() {
  let n = 0, hid = 0;
  for (let i = 0; i < body.length; i++) if (body[i] > 127) { n++; if (seen[i]) hid++; }
  return n ? hid / n : 0;
}
