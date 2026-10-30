// Repulsor blasts and beams: shockwave rings, hit tests, damage and knockouts.
import { MAX_DAMAGE, KO_SECONDS } from "./config.js";
import { W, H, persons, shockwaves, floaters, fx } from "./state.js";
import { clamp, rand, rayToEdge, videoRect } from "./util.js";
import { burst } from "./effects.js";
import { sfx } from "./audio.js";
import { isFresh, maskAt, bodyAt } from "./people.js";

export function blast(h) {
  const power = h.charge;
  h.charge = 0;
  fx.flash = Math.max(fx.flash, 0.35 + power * 0.65); fx.flashColor = "170,230,255";
  fx.shake = Math.max(fx.shake, 8 + power * 22);
  sfx.blast(power);
  const base = h.ps * 0.4, { x: nx, y: ny } = h.n, facing = Math.abs(h.n.z);
  for (let k = 0; k < 3; k++) {
    shockwaves.push({ x: h.cx, y: h.cy, nx, ny, facing, d: 0, r0: base, r: base,
      speed: (700 + power * 1300) * (1 - k * 0.22),
      life: 0.9 - k * 0.15, max: 0.9 - k * 0.15, width: 26 * (power + 0.4) * (1 - k * 0.3),
      // only the lead ring carries damage
      damage: k === 0 ? { owner: h.owner, power, hit: new Set() } : null });
  }
  // debris sprays in a cone along the palm direction, all around when the palm faces the camera
  burst(h.cx, h.cy, Math.round(80 + power * 220), 1400 * (0.4 + power), [150, 230, 255], {},
    Math.atan2(ny, nx), Math.PI * (0.2 + 0.8 * facing));
}

// A shockwave is a ring perpendicular to the palm normal travelling along it, so on screen
// it's an ellipse squashed along the normal's projection and drifting in that direction.
export function ring(s) {
  return { cx: s.x + s.nx * s.d, cy: s.y + s.ny * s.d, rot: Math.atan2(s.ny, s.nx),
    rx: s.r * Math.max(0.15, s.facing), ry: s.r };
}

// Beam runs from the palm along the projected palm normal to the screen edge, or to the first
// opponent's body in the way. It shortens as the palm turns toward the camera, since then it's
// shooting at the viewer rather than across the screen.
export function beamFor(h) {
  const side = Math.hypot(h.n.x, h.n.y) || 1e-6;
  const dx = h.n.x / side, dy = h.n.y / side;
  let len = rayToEdge(h.cx, h.cy, dx, dy) * clamp((side - 0.15) / 0.35, 0, 1) * h.beam;
  let target = null;
  if (persons.length >= 2) {
    const r = videoRect();
    for (let t = 0; t < len && !target; t += 6) {
      const x = h.cx + dx * t, y = h.cy + dy * t;
      for (const p of persons) {
        if (p !== h.owner && p.ko === 0 && bodyAt(p, x, y, r)) { len = t; target = p; break; }
      }
    }
  }
  return { x: h.cx, y: h.cy, ex: h.cx + dx * len, ey: h.cy + dy * len, dx, dy, len, side, target };
}

// Does a shockwave ring touch this person's head or silhouette?
function ringHits(p, g) {
  if (insideRing(p.cx, p.cy, g, p.w / 2)) return true;
  if (!isFresh(p)) return false;
  const r = videoRect(), c = Math.cos(g.rot), s = Math.sin(g.rot);
  for (let k = 0; k < 48; k++) {
    const a = k / 48 * Math.PI * 2, ex = g.rx * Math.cos(a), ey = g.ry * Math.sin(a);
    if (maskAt(p, g.cx + ex * c - ey * s, g.cy + ex * s + ey * c, r) > 127) return true;
  }
  return false;
}

function insideRing(x, y, g, pad) {
  const dx = x - g.cx, dy = y - g.cy, c = Math.cos(g.rot), sn = Math.sin(g.rot);
  const u = dx * c + dy * sn, v = -dx * sn + dy * c;
  return (u / (g.rx + pad)) ** 2 + (v / (g.ry + pad)) ** 2 <= 1;
}

function damage(p, s) {
  const dmg = s.damage;
  dmg.hit.add(p);
  if (p.ko > 0) return;
  // Falls off with distance from where the blast went off.
  const d = Math.hypot(p.cx - s.x, p.cy - s.y);
  const amount = Math.round(MAX_DAMAGE * dmg.power * clamp(1 - d / (Math.max(W, H) * 0.8), 0.15, 1));
  if (amount <= 0) return;
  p.hitFlash = 1;
  fx.shake = Math.max(fx.shake, 6 + amount * 0.4);
  floaters.push({ x: p.cx + rand(-20, 20), y: p.y, text: `-${amount}`, life: 1.1, max: 1.1, big: false });
  burst(p.cx, p.cy, 30 + amount, 700, [255, 70, 50], { grav: 600 });
  sfx.hit();
  applyDamage(p, amount);
}

export function beamDamage(p, amount) {
  if (p.ko > 0) return;
  p.hitFlash = Math.max(p.hitFlash, 0.6);
  p.beamAcc += amount;
  if (p.beamAcc >= 5) {
    floaters.push({ x: p.cx + rand(-30, 30), y: p.y, text: `-${Math.round(p.beamAcc)}`,
      life: 0.8, max: 0.8, big: false });
    p.beamAcc = 0;
    sfx.hit();
  }
  applyDamage(p, amount);
}

export function applyDamage(p, amount) {
  p.health = Math.max(0, p.health - amount);
  if (p.health === 0) {
    p.beamAcc = 0;
    p.ko = KO_SECONDS;
    floaters.push({ x: p.cx, y: p.cy, text: "K.O.", life: 1.6, max: 1.6, big: true });
    burst(p.cx, p.cy, 160, 1200, [255, 120, 60], { grav: 400 });
    sfx.ko();
  }
}

export function updateShockwaves(dt) {
  for (let i = shockwaves.length - 1; i >= 0; i--) {
    const s = shockwaves[i];
    s.life -= dt;
    if (s.life <= 0) { shockwaves.splice(i, 1); continue; }
    s.d += s.speed * dt;
    s.r = s.r0 + s.d * (0.3 + 0.7 * s.facing);
    s.speed *= Math.pow(0.96, dt * 60);
    if (s.damage && persons.length >= 2) {
      const g = ring(s);
      for (const p of persons) {
        if (p === s.damage.owner || s.damage.hit.has(p)) continue;
        if (ringHits(p, g)) damage(p, s);
      }
    }
  }
}
