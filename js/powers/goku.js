// Goku: flex both arms and yell to power up through three aura stages, each adding max health.
import { STAGE_HEALTH, settings } from "../config.js";
import { ctx, W, H, fx, hands, shockwaves, floaters } from "../state.js";
import { dist, rand, clamp, rgb, videoRect } from "../util.js";
import { spawn, burst } from "../effects.js";
import { jagged, strokePts } from "../gfx.js";
import { sfx, mic } from "../audio.js";
import { isFresh, maskAt, torsoCenter, maxHealth } from "../people.js";

// The aura is a shell well outside the body: for each direction out from the torso we find how
// far the silhouette reaches, smooth that into an envelope, push it out by a margin, and trace
// its edge as a ring of sharp, upward-licking flame tips. The inside stays mostly clear.
const ENV_N = 72;
const FLEX_SECONDS = 2;    // hold a flex this long to power up

// Flex: both arms bent at the elbow with the elbows pushed out past the shoulders
// (double-biceps, or the bent-arm power-up stance).
export function isFlexing(L) {
  if (![11, 12, 13, 14, 15, 16].every(i => L[i].v > 0.5)) return false;
  const sw = dist(L[11], L[12]), mx = (L[11].x + L[12].x) / 2;
  return [[11, 13, 15], [12, 14, 16]].every(([s, e, w]) => {
    const ax = L[s].x - L[e].x, ay = L[s].y - L[e].y, bx = L[w].x - L[e].x, by = L[w].y - L[e].y;
    const cos = (ax * bx + ay * by) / ((Math.hypot(ax, ay) * Math.hypot(bx, by)) || 1);
    const bent = Math.acos(clamp(cos, -1, 1)) < 2.0;   // elbow angle under ~115°
    const flared = Math.abs(L[e].x - mx) > Math.abs(L[s].x - mx) + 0.25 * sw;
    return bent && flared;
  });
}

// Aura stages, each reached by flexing and yelling: 1 yellow, 2 blue, 3 silver-white. Colors are "r,g,b".
export const AURA_STAGES = [null, {
  name: "POWER UP!", fill: "255,220,20", haze: "255,210,0", glow: "255,225,0", line: "255,235,20",
  core: "255,255,150", energy: "255,235,70", streak: "255,245,140", flash: "255,235,80",
}, {
  name: "MAX POWER!", fill: "40,165,255", haze: "0,140,255", glow: "60,190,255", line: "90,210,255",
  core: "225,250,255", energy: "110,210,255", streak: "185,235,255", flash: "120,205,255",
}, {
  name: "ULTRA INSTINCT!", fill: "205,220,255", haze: "190,205,255", glow: "235,240,255", line: "245,248,255",
  core: "255,255,255", energy: "225,235,255", streak: "245,248,255", flash: "235,240,255",
}];

// While charging: energy gets pulled into the body, speed lines rise, the screen starts to shake.
function chargeUpEffects(p, dt, k, pal) {
  const c = torsoCenter(p.lm), sw = dist(p.lm[11], p.lm[12]);
  const [er, eg, eb] = rgb(pal.energy), [sr, sg, sb] = rgb(pal.streak);
  for (let n = (30 + 120 * k) * dt; n > 0; n--) {
    if (Math.random() > n) break;
    const a = rand(0, Math.PI * 2), rr = sw * rand(1.2, 2.2), life = rand(0.3, 0.5);
    spawn({ x: c.x + Math.cos(a) * rr, y: c.y + Math.sin(a) * rr, vx: -Math.cos(a) * rr / life,
      vy: -Math.sin(a) * rr / life, drag: 1, life, size: rand(1.5, 3), r: er, g: eg, b: eb });
  }
  for (let n = (10 + 50 * k) * dt; n > 0; n--) {
    if (Math.random() > n) break;
    spawn({ x: c.x + rand(-1.3, 1.3) * sw, y: c.y + sw * rand(0.3, 1.5), vy: rand(-1100, -700),
      drag: 1, life: rand(0.25, 0.4), size: rand(1, 2), r: sr, g: sg, b: sb });
  }
  fx.shake = Math.max(fx.shake, 1 + 5 * k);
}

function transform(p) {
  p.stage++;
  p.health += STAGE_HEALTH;   // each stage raises max health and adds that much on top
  p.flexCharge = 0;
  p.flexReady = false;   // let go of the flex before charging the next stage
  const pal = AURA_STAGES[p.stage], c = torsoCenter(p.lm);
  fx.flash = 1; fx.flashColor = pal.flash;
  fx.shake = Math.max(fx.shake, 30);
  for (let k = 0; k < 3; k++) {
    shockwaves.push({ x: c.x, y: c.y, nx: 0, ny: 0, facing: 1, d: 0, r0: 40, r: 40,
      speed: 1800 * (1 - k * 0.22), life: 0.9 - k * 0.15, max: 0.9 - k * 0.15, width: 30 * (1 - k * 0.3),
      color: pal.energy, damage: null });
  }
  burst(c.x, c.y, 260, 1600, rgb(pal.energy));
  floaters.push({ x: c.x, y: p.y - 20, text: pal.name, life: 1.4, max: 1.4, big: true, color: pal.flash });
  floaters.push({ x: c.x, y: p.y + 30, text: `+${STAGE_HEALTH} HP`, life: 1.4, max: 1.4, big: false, color: pal.flash });
  sfx.transform();
}

// Power gauge around the torso while charging, in the color of the stage being charged.
// Flexing without yelling shows the gauge with a prompt to yell.
export function drawFlexGauge(p) {
  if (!p.lm || p.stage >= AURA_STAGES.length - 1) return;
  const prompt = p.flexing && p.flexReady && !mic.yelling;
  if (p.flexCharge <= 0.01 && !prompt) return;
  const pal = AURA_STAGES[p.stage + 1];
  const c = torsoCenter(p.lm), R = dist(p.lm[11], p.lm[12]) * 1.05;
  ctx.lineCap = "round";
  ctx.strokeStyle = `rgba(${pal.streak},0.25)`; ctx.lineWidth = 6;
  ctx.beginPath(); ctx.arc(c.x, c.y, R, 0, Math.PI * 2); ctx.stroke();
  ctx.strokeStyle = `rgba(${pal.line},0.95)`;
  ctx.beginPath(); ctx.arc(c.x, c.y, R, -Math.PI / 2, -Math.PI / 2 + p.flexCharge * Math.PI * 2); ctx.stroke();
  ctx.font = "bold 14px Consolas, monospace";
  ctx.textAlign = "center";
  ctx.fillStyle = `rgba(${pal.streak},0.95)`;
  ctx.fillText(prompt ? (mic.ready ? "YELL!" : "NO MIC // CAN'T POWER UP")
    : `POWERING UP ${Math.round(p.flexCharge * 100)}%`, c.x, c.y - R - 12);
  ctx.textAlign = "left";
}

function updateEnvelope(p) {
  const L = p.lm, r = videoRect();
  const c = torsoCenter(L);
  const raw = new Float32Array(ENV_N), maxT = Math.hypot(W, H);
  for (let k = 0; k < ENV_N; k++) {
    const a = k / ENV_N * Math.PI * 2, dx = Math.cos(a), dy = Math.sin(a);
    let reach = 0;
    for (let t = 0; t < maxT; t += 8) {
      const x = c.x + dx * t, y = c.y + dy * t;
      if (x < 0 || y < 0 || x >= W || y >= H) continue;
      if (maskAt(p, x, y, r) > 127) reach = t;
    }
    raw[k] = reach;
  }
  // widen (max over neighbours) then soften, so gaps between limbs don't make deep notches
  const wide = new Float32Array(ENV_N), soft = new Float32Array(ENV_N);
  for (let k = 0; k < ENV_N; k++) {
    let m = 0;
    for (let j = -3; j <= 3; j++) m = Math.max(m, raw[(k + j + ENV_N) % ENV_N]);
    wide[k] = m;
  }
  for (let k = 0; k < ENV_N; k++) {
    let s = 0;
    for (let j = -2; j <= 2; j++) s += wide[(k + j + ENV_N) % ENV_N];
    soft[k] = s / 5;
  }
  if (!p.env) { p.env = soft; p.envC = c; return; }
  for (let k = 0; k < ENV_N; k++) p.env[k] += (soft[k] - p.env[k]) * 0.35;
  p.envC.x += (c.x - p.envC.x) * 0.35;
  p.envC.y += (c.y - p.envC.y) * 0.35;
}

// Sparks drifting up inside the shell.
function updateAura(p, dt) {
  if (!isFresh(p) || !p.lm || p.auraLevel < 0.02) return;
  updateEnvelope(p);
  const c = p.envC;
  for (let n = p.auraLevel * 25 * dt; n > 0; n--) {
    if (Math.random() > n) break;
    const k = Math.random() * ENV_N | 0, a = k / ENV_N * Math.PI * 2, rr = p.env[k] * rand(0.4, 1.1);
    spawn({ x: c.x + Math.cos(a) * rr, y: c.y + Math.sin(a) * rr, vx: rand(-15, 15), vy: rand(-380, -200),
      life: rand(0.3, 0.6), size: rand(1.5, 3), ...sparkColor(p), drag: 0.99 });
  }
}

function auraPath(p, now) {
  const L = p.lm, lvl = p.auraLevel, c = p.envC;
  const sw = Math.max(dist(L[11], L[12]), p.w);
  const margin = sw * (0.3 + 0.35 * lvl);       // empty space between body and aura edge
  const spikeLen = sw * (0.3 + 0.45 * lvl);
  const n = 28;
  const envAt = a => {
    const f = ((a / (Math.PI * 2)) % 1 + 1) % 1 * ENV_N, k0 = Math.floor(f), t = f - k0;
    return p.env[k0 % ENV_N] * (1 - t) + p.env[(k0 + 1) % ENV_N] * t + margin;
  };
  // valleys sit on the envelope; the whole ring creeps slowly so the flames seem to flow upward
  const drift = now * 0.00025;
  const base = [];
  for (let j = 0; j < n; j++) {
    const a = (j / n + drift) * Math.PI * 2, rr = envAt(a);
    base.push({ x: c.x + Math.cos(a) * rr, y: c.y + Math.sin(a) * rr });
  }
  ctx.beginPath();
  ctx.moveTo(base[0].x, base[0].y);
  for (let j = 0; j < n; j++) {
    const A = base[j], B = base[(j + 1) % n];
    const am = ((j + 0.5) / n + drift) * Math.PI * 2, ox = Math.cos(am), oy = Math.sin(am);
    // flames lean upward on the top and sides; below the waist they just point outward and shrink
    let tx = ox, ty = oy - 1.1 * (1 - Math.max(0, oy));
    const tl = Math.hypot(tx, ty) || 1;
    tx /= tl; ty /= tl;
    const up = Math.max(0, -oy);
    const flick = 0.7 + 0.25 * Math.sin(now * 0.02 + j * 2.3) + 0.15 * Math.sin(now * 0.047 + j * 5.1)
      + Math.random() * 0.06;
    const len = spikeLen * (0.45 + 0.9 * up) * (1 - 0.6 * Math.max(0, oy)) * flick;
    const mx = (A.x + B.x) / 2, my = (A.y + B.y) / 2;
    const T = { x: mx + tx * len, y: my + ty * len };
    // flame-lick: the leading side bulges out, the trailing side curls in, meeting at a sharp tip
    ctx.quadraticCurveTo((A.x + T.x) / 2 + ox * len * 0.2, (A.y + T.y) / 2 + oy * len * 0.2, T.x, T.y);
    ctx.quadraticCurveTo((T.x + B.x) / 2 - tx * len * 0.2, (T.y + B.y) / 2 - ty * len * 0.2, B.x, B.y);
  }
  ctx.closePath();
}

// Current aura colors. While charging toward the next stage, the aura flickers into its color
// more and more often; before the first stage it flickers in yellow.
function auraPalette(p) {
  const next = AURA_STAGES[p.stage + 1];
  if (p.stage === 0) return AURA_STAGES[1];
  if (next && Math.random() < p.flexCharge * 0.7) return next;
  return AURA_STAGES[p.stage];
}

function sparkColor(p) {
  const [r, g, b] = rgb(AURA_STAGES[Math.max(1, p.stage)].energy);
  return { r, g, b };
}

export function drawAura(p, now) {
  if (!p.env || p.auraLevel < 0.02) return;
  const a = Math.min(1, p.auraLevel * 1.4);
  ctx.lineJoin = "miter"; ctx.miterLimit = 12;
  auraPath(p, now);
  const pal = auraPalette(p);
  ctx.fillStyle = `rgba(${pal.fill},${0.32 * a})`;   // body of the aura
  ctx.fill();
  // border: a soft blurred halo around a solid, brighter line
  ctx.strokeStyle = `rgba(${pal.haze},${0.12 * a})`; ctx.lineWidth = 26; ctx.stroke();
  ctx.strokeStyle = `rgba(${pal.haze},${0.22 * a})`; ctx.lineWidth = 15; ctx.stroke();
  ctx.save();
  ctx.shadowColor = `rgba(${pal.glow},${a})`; ctx.shadowBlur = 18;
  ctx.strokeStyle = `rgba(${pal.line},${a})`; ctx.lineWidth = 6; ctx.stroke();
  ctx.restore();
  ctx.strokeStyle = `rgba(${pal.core},${a})`; ctx.lineWidth = 2; ctx.stroke();
  ctx.lineJoin = "round";

  // Lightning crackles off the body at every stage: more bolts at higher stages and as the
  // aura flares. White-hot core inside a glow in the stage's color, sometimes with a fork.
  if (p.stage > 0 && p.edgeList.length) {
    const r = videoRect(), pal = AURA_STAGES[p.stage];
    const count = Math.random() < 0.3 + 0.5 * p.auraLevel ? 1 + (Math.random() * (1 + p.stage) | 0) : 0;
    ctx.lineCap = "round"; ctx.lineJoin = "round";
    for (let k = 0; k < count; k++) {
      const i = p.edgeList[Math.random() * p.edgeList.length | 0];
      const x = r.x + (1 - ((i % p.mw) + 0.5) / p.mw) * r.w, y = r.y + (((i / p.mw) | 0) + 0.5) / p.mh * r.h;
      const ang = -Math.PI / 2 + rand(-1.4, 1.4), len = p.w * rand(0.4, 1.2);
      const arcs = [jagged(x, y, x + Math.cos(ang) * len, y + Math.sin(ang) * len, 8, len * 0.18)];
      if (Math.random() < 0.5) {
        const [fx, fy] = arcs[0][4], fa = ang + rand(-1, 1), fl = len * 0.45;
        arcs.push(jagged(fx, fy, fx + Math.cos(fa) * fl, fy + Math.sin(fa) * fl, 4, fl * 0.2));
      }
      for (const pts of arcs) {
        strokePts(pts, `rgba(${pal.glow},0.5)`, 5);
        strokePts(pts, "rgba(255,255,255,0.95)", 1.4);
      }
    }
  }
}

// Flex both arms and yell to charge up a stage (yellow, then blue, then white, letting go
// and flexing again between each). Flexing in silence only holds the charge as it slowly drains.
// The aura stays lit, flaring with hand charge / beams, until a K.O. or the power is switched off.
// Returns this person's charge, for the charge-up sound.
export function updateGoku(p, dt) {
  if (!settings.goku || p.ko > 0) p.stage = 0;
  p.health = Math.min(p.health, maxHealth(p));   // losing the aura loses its bonus health
  const flexing = p.flexing;
  if (!flexing) p.flexReady = true;
  if (flexing && p.flexReady && p.stage < AURA_STAGES.length - 1) {
    if (mic.yelling) {
      p.flexCharge = Math.min(1, p.flexCharge + dt / FLEX_SECONDS);
      chargeUpEffects(p, dt, p.flexCharge, AURA_STAGES[p.stage + 1]);
      if (p.flexCharge >= 1) transform(p);
    } else p.flexCharge = Math.max(0, p.flexCharge - dt * 0.4);
  } else {
    if (flexing && p.stage > 0) chargeUpEffects(p, dt, 1, AURA_STAGES[p.stage]);   // surge
    p.flexCharge = Math.max(0, p.flexCharge - dt * 1.5);
  }
  const charge = p.flexCharge;

  let power = flexing ? 1 : 0;   // flexing while powered up flares it to the max
  for (const h of hands) if (h.owner === p) power = Math.max(power, h.charge, h.beam * 0.8);
  const target = p.stage > 0 ? 0.55 + 0.45 * power
    : p.flexCharge * (0.45 + 0.35 * Math.random());   // flickers in while charging
  p.auraLevel += (target - p.auraLevel) * Math.min(1, dt * 6);
  updateAura(p, dt);
  return charge;
}
