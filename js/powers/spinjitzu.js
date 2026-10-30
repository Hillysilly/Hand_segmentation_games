// Spinjitzu: a full 360 (or both arms swung across the body) summons a tornado around you.
import { settings } from "../config.js";
import { ctx, H, shockwaves, floaters } from "../state.js";
import { dist, rand, rgb, mixRgb } from "../util.js";
import { spawn, burst } from "../effects.js";
import { sfx } from "../audio.js";
import { isFresh, torsoCenter, torsoLen } from "../people.js";

const SPIN_WINDOW = 3000;   // ms allowed to complete the turn
const SPIN_SECONDS = 5;     // tornado lasts this long after a 360 or an arm swing
const SWING_WINDOW = 500;   // ms for both arms to swing across the body

// Arm swing: both wrists on one side of the body, then both on the other side within SWING_WINDOW.
function trackSwing(p, now) {
  const L = p.lm;
  if (L[15].v < 0.3 || L[16].v < 0.3) return;
  const cx = torsoCenter(L).x, tl = torsoLen(L);
  const a = (L[15].x - cx) / tl, b = (L[16].x - cx) / tl;
  const side = (x, y) => x > 0.35 && y > 0.35 ? 1 : x < -0.35 && y < -0.35 ? -1 : 0;
  const hist = p.swingHist;
  hist.push({ t: now, s: side(a, b) });
  while (hist.length && now - hist[0].t > SWING_WINDOW) hist.shift();
  const cur = hist[hist.length - 1].s;
  if (cur && hist.some(h => h.s === -cur)) {
    p.spinUntil = now + SPIN_SECONDS * 1000;
    hist.length = 0;
  }
}

// A 360 shows up as: facing the camera -> facing away -> facing it again, passing through one
// side profile on the way out and the opposite one on the way back. (Turning away and back the
// same way shows the same profile twice, so it doesn't count.) "Away" = the face landmarks lose
// visibility, or the shoulders swap sides on screen.
function trackSpin(p, now) {
  const L = p.lm;
  const faceVis = (L[0].v + L[2].v + L[5].v) / 3;
  const swapped = L[11].x > L[12].x;   // mirrored screen: facing us, the left shoulder is on the left
  const f = faceVis < 0.3 || (swapped && faceVis < 0.6) ? "back" : "front";

  // side profile: shoulders collapse to a narrow line and the nose sticks out to one side
  const ms = { x: (L[11].x + L[12].x) / 2, y: (L[11].y + L[12].y) / 2 };
  const tl = torsoLen(L);
  if (Math.abs(L[11].x - L[12].x) < 0.45 * tl && Math.abs(L[0].x - ms.x) > 0.12 * tl) {
    p.profile = Math.sign(L[0].x - ms.x);
    p.profileAt = now;
  }

  // Backup that ignores the face: a full turn narrows the shoulders to a line twice (once per
  // side view), measured against this person's usual front-on shoulder width.
  const sw = Math.abs(L[11].x - L[12].x) / tl;
  p.swRef = Math.max(sw, p.swRef * 0.999);   // decays slowly so it survives a whole spin
  p.swNow = sw / (p.swRef || 1);
  if (!p.narrow && p.swNow < 0.45) { p.narrow = true; p.narrowTimes.push(now); }
  else if (p.narrow && p.swNow > 0.75) p.narrow = false;
  while (p.narrowTimes.length && now - p.narrowTimes[0] > SPIN_WINDOW) p.narrowTimes.shift();
  if (p.narrowTimes.length >= 2) {
    p.spinUntil = now + SPIN_SECONDS * 1000;
    p.narrowTimes.length = 0;
  }

  if (f !== p.facingCand) { p.facingCand = f; p.facingSince = now; }
  if (now - p.facingSince < 80 || f === p.facing) return;
  if (f === "back") {
    p.turnedAt = now;
    p.outSide = now - p.profileAt < 1500 ? p.profile : 0;
  } else {
    const inSide = p.profileAt > p.turnedAt ? p.profile : 0;
    // if a profile was missed (fast spin) give it the benefit of the doubt
    const sameWayBack = p.outSide && inSide && p.outSide === inSide;
    if (now - p.turnedAt < SPIN_WINDOW && !sameWayBack) p.spinUntil = now + SPIN_SECONDS * 1000;
  }
  p.facing = f;
}

// Smoothed funnel placement: centered on the torso, from the feet (or screen bottom) to above the head.
function updateTornadoFrame(p) {
  const L = p.lm, c = torsoCenter(L);
  const ms = { x: (L[11].x + L[12].x) / 2, y: (L[11].y + L[12].y) / 2 };
  const tl = Math.max(30, dist(ms, { x: (L[23].x + L[24].x) / 2, y: (L[23].y + L[24].y) / 2 }));
  const feetVisible = L[27].v > 0.3 || L[28].v > 0.3;
  const bottom = feetVisible ? Math.min(H + 20, Math.max(L[27].y, L[28].y) + tl * 0.15) : H + 20;
  const t = { cx: c.x, top: Math.min(L[0].y, p.y) - tl * 0.7, bottom, tl };
  if (!p.tw) { p.tw = t; return; }
  for (const k in t) p.tw[k] += (t[k] - p.tw[k]) * 0.2;
}

// Funnel radius at height t (0 = feet, 1 = top): narrow at the bottom, flaring out above.
const tornadoRadius = (p, t) => 1.2 * (p.tw.tl * 0.35 + p.tw.tl * 1.05 * Math.pow(t, 1.4)) * (0.6 + 0.4 * p.spin);
const tornadoSway = (p, y, now) => Math.sin(y * 0.012 + now * 0.003) * p.tw.tl * 0.035;
const SPIN_RATE = 0.012;    // radians per ms

function startSpinjitzu(p) {
  p.spinOn = true;
  const c = torsoCenter(p.lm), col = settings.spinColor;
  shockwaves.push({ x: c.x, y: c.y, nx: 0, ny: 0, facing: 1, d: 0, r0: 30, r: 30, speed: 1500,
    life: 0.6, max: 0.6, width: 18, color: col, damage: null });
  burst(c.x, c.y, 120, 1100, rgb(col));
  floaters.push({ x: c.x, y: p.y - 20, text: "SPINJITZU!", life: 1.3, max: 1.3, big: true, color: col });
  sfx.whoosh();
}

// Debris flung off the spinning wall.
function tornadoDebris(p, dt) {
  const { cx, top, bottom } = p.tw, [r, g, b] = rgb(settings.spinColor);
  for (let n = 60 * p.spin * dt; n > 0; n--) {
    if (Math.random() > n) break;
    const t = Math.random(), y = bottom - t * (bottom - top), R = tornadoRadius(p, t);
    const th = rand(0, Math.PI * 2);
    spawn({ x: cx + R * Math.sin(th), y, vx: -Math.cos(th) * R * 4 + rand(-40, 40), vy: rand(-260, -80),
      life: rand(0.3, 0.7), size: rand(1.5, 3), r, g, b, drag: 0.95 });
  }
}

export function drawTornado(p, now) {
  if (p.spin < 0.02 || !p.tw) return;
  const s = p.spin, col = settings.spinColor, { cx, top, bottom } = p.tw, span = bottom - top;
  const R = t => tornadoRadius(p, t), sway = y => tornadoSway(p, y, now);
  const phase = now * SPIN_RATE;

  // body of the funnel: painted (not added as light) so it actually covers what's behind it
  ctx.save();
  ctx.globalCompositeOperation = "source-over";
  ctx.beginPath();
  for (let y = bottom; y >= top; y -= 10) ctx.lineTo(cx + sway(y) - R((bottom - y) / span), y);
  for (let y = top; y <= bottom; y += 10) ctx.lineTo(cx + sway(y) + R((bottom - y) / span), y);
  ctx.closePath();
  // light tint of the color for the body, dark shade for the swirls on top of it
  const light = mixRgb(col, "255,255,255", 0.4), dark = mixRgb(col, "0,0,0", 0.55);
  const body = ctx.createLinearGradient(0, top, 0, bottom);
  body.addColorStop(0, `rgba(${light},${0.5 * s})`); body.addColorStop(1, `rgba(${light},${0.3 * s})`);
  ctx.fillStyle = body; ctx.fill();

  // Bold dark spiral ribbons winding up the funnel (painted, so they read darker than the body).
  // Each drifts in and out, up to 15% beyond the funnel edge. Near side solid, far side faint.
  ctx.lineCap = "round"; ctx.lineJoin = "round";
  const N = 9;
  for (let i = 0; i < N; i++) {
    const front = new Path2D(), back = new Path2D();
    let prev = null;
    for (let y = bottom; y >= top - 1; y -= 5) {
      const t = (bottom - y) / span, th = i / N * Math.PI * 2 + t * 6 - phase * (1 + t * 0.5);
      const out = 1 + 0.15 * (0.5 + 0.5 * Math.sin(t * 9 + i * 1.7 + now * 0.002));
      const x = cx + sway(y) + R(t) * out * Math.sin(th), near = Math.cos(th) > 0;
      const path = near ? front : back;
      if (!prev || prev.near !== near) path.moveTo(prev ? prev.x : x, prev ? prev.y : y);
      path.lineTo(x, y);
      prev = { x, y, near };
    }
    ctx.lineWidth = 3; ctx.strokeStyle = `rgba(${dark},${0.35 * s})`; ctx.stroke(back);
    ctx.lineWidth = 6; ctx.strokeStyle = `rgba(${dark},${0.95 * s})`; ctx.stroke(front);
    ctx.lineWidth = 1.2; ctx.strokeStyle = `rgba(${col},${0.6 * s})`; ctx.stroke(front);   // crisp edge
  }
  ctx.restore();

  // rings rising up the funnel (near half bright, far half dim)
  for (let k = 0; k < 6; k++) {
    const t = (k / 6 + now * 0.0006) % 1, y = bottom - t * span, r = R(t), x = cx + sway(y);
    const fade = s * (1 - t * 0.6);
    ctx.lineWidth = 2; ctx.strokeStyle = `rgba(${col},${0.2 * fade})`;
    ctx.beginPath(); ctx.ellipse(x, y, r, r * 0.16, 0, Math.PI, Math.PI * 2); ctx.stroke();
    ctx.lineWidth = 3; ctx.strokeStyle = `rgba(${col},${0.7 * fade})`;
    ctx.beginPath(); ctx.ellipse(x, y, r, r * 0.16, 0, 0, Math.PI); ctx.stroke();
  }

  ctx.save();
  ctx.globalCompositeOperation = "source-over";
  const [cr, cg, cb] = rgb(col), [dr, dg, db] = rgb(dark);

  // Solid dark wind streaks wrapping around the funnel and sweeping with the spin: thick at the
  // leading edge, tapering along the tail. A point at angle a sits at x = R·sin(a), and on the
  // near side (cos a > 0) slightly lower, matching the rings' perspective.
  ctx.lineCap = "round";
  for (const st of [...p.streaks].sort((a, b) => Math.cos(a.th) - Math.cos(b.th))) {
    const y0 = bottom - st.t * span, r = R(st.t) * st.rr, x0 = cx + sway(y0);
    const fadeIn = Math.min(1, st.t * 8, (1 - st.t) * 8);
    const SEG = 8;
    for (let k = 0; k < SEG; k++) {
      const a0 = st.th + st.len * k / SEG, a1 = st.th + st.len * (k + 1) / SEG;   // leading edge first
      const depth = Math.cos((a0 + a1) / 2), shade = 0.8 + 0.2 * depth;
      ctx.globalAlpha = s * fadeIn * (depth > 0 ? 0.95 : 0.4);
      ctx.strokeStyle = `rgb(${dr * shade | 0},${dg * shade | 0},${db * shade | 0})`;
      ctx.lineWidth = st.width * (1 - k / SEG * 0.85);
      ctx.beginPath();
      ctx.moveTo(x0 + r * Math.sin(a0), y0 + r * 0.16 * Math.cos(a0));
      ctx.lineTo(x0 + r * Math.sin(a1), y0 + r * 0.16 * Math.cos(a1));
      ctx.stroke();
    }
  }

  // solid chunks riding the funnel: drawn opaque, big and bright in front, small and dim behind
  const sorted = [...p.orbiters].sort((a, b) => Math.cos(a.th) - Math.cos(b.th));   // far ones first
  for (const o of sorted) {
    const y = bottom - o.t * span, depth = Math.cos(o.th);
    const x = cx + sway(y) + R(o.t) * o.rr * Math.sin(o.th);
    const size = o.size * (0.75 + 0.35 * depth), shade = 0.55 + 0.45 * depth;
    ctx.globalAlpha = s * (depth > 0 ? 1 : 0.45) * Math.min(1, o.t * 8, (1 - o.t) * 8);
    ctx.fillStyle = `rgb(${cr * shade | 0},${cg * shade | 0},${cb * shade | 0})`;
    ctx.beginPath(); ctx.arc(x, y, size, 0, Math.PI * 2); ctx.fill();
    if (depth > 0) {   // glint on the near ones
      ctx.fillStyle = "rgba(255,255,255,0.85)";
      ctx.beginPath(); ctx.arc(x - size * 0.3, y - size * 0.3, size * 0.35, 0, Math.PI * 2); ctx.fill();
    }
  }
  ctx.restore();
}

// Chunks and streaks orbit with the funnel's rotation (faster higher up) while slowly rising,
// then recycle at the bottom.
function ride(list, want, make, dt) {
  while (list.length < want) list.push(make());
  if (list.length > want) list.length = want;
  for (const o of list) {
    o.th -= SPIN_RATE * dt * 1000 * (1 + o.t * 0.5);
    o.t += o.rise * dt;
    if (o.t > 1) { o.t = 0; o.th = rand(0, Math.PI * 2); }
  }
}

function updateOrbiters(p, dt) {
  const on = p.spin > 0.05;
  ride(p.orbiters, on ? Math.round(36 * p.spin) : 0, () => ({ th: rand(0, Math.PI * 2), t: Math.random(),
    rr: rand(0.75, 1.0), size: rand(2.5, 5.5), rise: rand(0.15, 0.35) }), dt);
  ride(p.streaks, on ? Math.round(22 * p.spin) : 0, () => ({ th: rand(0, Math.PI * 2), t: Math.random(),
    rr: rand(1.0, 1.15), len: rand(0.6, 1.4), width: rand(3, 7), rise: rand(0.08, 0.2) }), dt);
}

// A completed 360 (or a fast two-arm swing) runs the tornado for SPIN_SECONDS. Returns its strength.
export function updateSpin(p, dt, now) {
  if (p.lm && isFresh(p)) { trackSpin(p, now); trackSwing(p, now); updateTornadoFrame(p); }
  const wantSpin = settings.spinjitzu && p.ko === 0 && p.tw && now < p.spinUntil ? 1 : 0;
  if (wantSpin && !p.spinOn) startSpinjitzu(p);
  p.spin += (wantSpin - p.spin) * Math.min(1, dt * (wantSpin ? 4 : 1.5));
  if (!wantSpin && p.spin < 0.05) p.spinOn = false;
  if (p.spin > 0.2) tornadoDebris(p, dt);
  updateOrbiters(p, dt);
  return p.spin;
}
