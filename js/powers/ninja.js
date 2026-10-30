// Ninja: elemental flames on the hands, and a fast punch / throw shoots an elemental ball.
import { settings } from "../config.js";
import { ctx, W, H, persons, floaters, fx } from "../state.js";
import { clamp, rand, rgb, videoRect } from "../util.js";
import { burst } from "../effects.js";
import { glow } from "../gfx.js";
import { sfx } from "../audio.js";
import { bodyAt } from "../people.js";
import { applyDamage } from "../combat.js";

const THROW_SPEED = 10;       // palm sizes per second that counts as a punch / throw
const THROW_COOLDOWN = 450;   // ms between shots per hand
const BALL_DAMAGE = 12;
export const flames = [];     // flame puffs (on hands and trailing balls), in the Spinjitzu color
export const balls = [];

// Soft flame puff sprite per color: white-hot center fading out through the color.
const spriteCache = new Map();
function flameSprite(col) {
  let s = spriteCache.get(col);
  if (s) return s;
  s = document.createElement("canvas");
  s.width = s.height = 64;
  const c = s.getContext("2d"), g = c.createRadialGradient(32, 32, 0, 32, 32, 32);
  g.addColorStop(0, "rgba(255,255,255,1)");
  g.addColorStop(0.25, `rgba(${col},0.9)`);
  g.addColorStop(1, `rgba(${col},0)`);
  c.fillStyle = g; c.fillRect(0, 0, 64, 64);
  spriteCache.set(col, s);
  return s;
}

function spawnFlame(x, y, size, vx, vy, life) {
  if (flames.length < 900) flames.push({ x, y, size, vx, vy, life, max: life });
}

const FLAME_POINTS = [0, 4, 8, 12, 16, 20, 5, 9, 13, 17];   // wrist, fingertips, knuckles

export function handFlames(h, dt) {
  for (let n = 80 * dt; n > 0; n--) {
    if (Math.random() > n) break;
    const q = h.pts[FLAME_POINTS[Math.random() * FLAME_POINTS.length | 0]];
    spawnFlame(q.x + rand(-4, 4), q.y + rand(-4, 4), h.ps * rand(0.15, 0.3),
      rand(-30, 30), rand(-220, -90), rand(0.25, 0.5));
  }
}

// A throw is the palm moving faster than THROW_SPEED over the last ~0.1 s; the ball flies that way.
export function trackThrow(h, now) {
  const hist = h.hist;
  hist.push({ t: now, x: h.cx, y: h.cy });
  while (hist.length && now - hist[0].t > 150) hist.shift();
  const old = hist[0], span = (now - old.t) / 1000;
  if (span < 0.06 || now - h.lastThrow < THROW_COOLDOWN) return;
  const vx = (h.cx - old.x) / span, vy = (h.cy - old.y) / span, speed = Math.hypot(vx, vy);
  if (speed < THROW_SPEED * h.ps) return;
  h.lastThrow = now;
  hist.length = 0;
  const v = clamp(speed * 1.2, 900, 2400);
  balls.push({ x: h.cx, y: h.cy, vx: vx / speed * v, vy: vy / speed * v, r: h.ps * 0.35, owner: h.owner, life: 3 });
  sfx.fireball();
}

export function updateBalls(dt) {
  const r = videoRect(), col = rgb(settings.spinColor);
  for (let i = balls.length - 1; i >= 0; i--) {
    const b = balls[i];
    b.x += b.vx * dt; b.y += b.vy * dt; b.life -= dt;
    for (let k = 0; k < 3; k++) {   // fiery trail
      spawnFlame(b.x + rand(-0.4, 0.4) * b.r, b.y + rand(-0.4, 0.4) * b.r, b.r * rand(0.6, 1.1),
        -b.vx * 0.08 + rand(-30, 30), -b.vy * 0.08 + rand(-60, 10), rand(0.2, 0.4));
    }
    let hit = null;
    if (persons.length >= 2) {
      for (const p of persons) if (p !== b.owner && p.ko === 0 && bodyAt(p, b.x, b.y, r)) { hit = p; break; }
    }
    const off = b.x < -b.r || b.y < -b.r || b.x > W + b.r || b.y > H + b.r;
    if (!hit && !off && b.life > 0) continue;
    if (hit) {
      hit.hitFlash = 1;
      floaters.push({ x: hit.cx + rand(-20, 20), y: hit.y, text: `-${BALL_DAMAGE}`, life: 1.1, max: 1.1, big: false });
      sfx.hit();
      applyDamage(hit, BALL_DAMAGE);
    } else {
      sfx.pop();
    }
    burst(clamp(b.x, 0, W), clamp(b.y, 0, H), 50, 700, col);
    fx.shake = Math.max(fx.shake, hit ? 10 : 4);
    balls.splice(i, 1);
  }
}

export function updateFlames(dt) {
  for (let i = flames.length - 1; i >= 0; i--) {
    const f = flames[i];
    f.life -= dt;
    if (f.life <= 0) { flames.splice(i, 1); continue; }
    f.x += f.vx * dt; f.y += f.vy * dt;
    f.vx *= 0.96;
  }
}

export function drawFlames() {
  const sprite = flameSprite(settings.spinColor);
  for (const f of flames) {
    const k = f.life / f.max, s = f.size * (0.4 + 0.6 * k);   // shrink and fade as they rise
    ctx.globalAlpha = Math.min(1, k * 1.5);
    ctx.drawImage(sprite, f.x - s, f.y - s, s * 2, s * 2);
  }
  ctx.globalAlpha = 1;
}

export function drawBalls(now) {
  const col = settings.spinColor, sprite = flameSprite(col);
  for (const b of balls) {
    const s = b.r * 1.3 * (1 + 0.1 * Math.sin(now * 0.04));
    glow(b.x, b.y, b.r * 2.6, [[0, `rgba(${col},0.55)`], [1, `rgba(${col},0)`]]);
    ctx.drawImage(sprite, b.x - s, b.y - s, s * 2, s * 2);
    glow(b.x, b.y, b.r * 0.6, [[0, "rgba(255,255,255,1)"], [1, "rgba(255,255,255,0)"]]);
  }
}
