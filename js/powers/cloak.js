// Cloak: paints the room back over the people in suits, so the suit replaces them instead of being
// pasted on top. The room behind them (a "clean plate") is learned from the camera frame wherever
// nobody is standing, a little every frame, so a spot gets filled in once they have moved off it or
// stepped out of shot. Until then the real body shows there.
import { POSE_WIDTH, settings } from "../config.js";
import { ctx, frame, persons } from "../state.js";
import { videoRect } from "../util.js";
import { isFresh } from "../people.js";

const LEARN_EVERY = 4;   // frames between looks at the room
const LEARN = 0.3;       // how far the plate moves toward what it sees, per look
const GROW = 3;          // mask pixels the body is widened by, to also cover the halo around its edge

let w = 0, h = 0, grab, gctx, out, octx, outImg, plate, seen, body, tmp;
let shown = false;       // anything to paint this frame
let frames = 0;

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
}

export function resetCloak() { w = h = 0; shown = false; frames = 0; }

// Look at the room wherever nobody is standing. Reading the frame back from the GPU is the slow
// part (a few ms for a camera frame), hence only every LEARN_EVERY frames.
function learn(img) {
  gctx.drawImage(img, 0, 0, w, h);
  const px = gctx.getImageData(0, 0, w, h).data, o = outImg.data;
  for (let i = 0; i < body.length; i++) {
    if (body[i] >= 20) continue;   // somebody is standing here
    const c = i * 3, q = i * 4, k = seen[i] ? LEARN : 1;
    o[q] = plate[c] += (px[q] - plate[c]) * k;
    o[q + 1] = plate[c + 1] += (px[q + 1] - plate[c + 1]) * k;
    o[q + 2] = plate[c + 2] += (px[q + 2] - plate[c + 2]) * k;
    seen[i] = 1;
  }
}

// Widen the mask by GROW pixels (a max filter, one pass per axis).
function dilate() {
  for (let y = 0, row = 0; y < h; y++, row += w) {
    for (let x = 0; x < w; x++) {
      let m = 0;
      const end = row + (x + GROW < w ? x + GROW : w - 1);
      for (let i = row + (x > GROW ? x - GROW : 0); i <= end; i++) if (body[i] > m) m = body[i];
      tmp[row + x] = m;
    }
  }
  for (let y = 0; y < h; y++) {
    const from = (y > GROW ? y - GROW : 0) * w, to = (y + GROW < h ? y + GROW : h - 1) * w;
    for (let x = 0; x < w; x++) {
      let m = 0;
      for (let i = from + x; i <= to + x; i += w) if (tmp[i] > m) m = tmp[i];
      body[y * w + x] = m;
    }
  }
}

export function updateCloak() {
  shown = false;
  const img = frame.img;
  if (!settings.armor || !settings.cloak || !img || (img.readyState !== undefined && img.readyState < 2)) return;
  const people = persons.filter(isFresh);
  const mw = people[0]?.mw || POSE_WIDTH, mh = people[0]?.mh || Math.round(POSE_WIDTH * frame.h / frame.w);
  if (mw !== w || mh !== h) resize(mw, mh);

  body.fill(0);
  for (const p of people) {
    if (p.mw !== w || p.mh !== h) continue;
    for (let i = 0; i < body.length; i++) if (p.mask[i] > body[i]) body[i] = p.mask[i];
  }
  if (people.length) dilate();

  const o = outImg.data;
  if (frames++ % LEARN_EVERY === 0) learn(img);
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
