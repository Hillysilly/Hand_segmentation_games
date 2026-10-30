// Particles, floating text, and the screen flash / shake.
import { particles, floaters, fx } from "./state.js";
import { rand } from "./util.js";

export function spawn(o) {
  if (particles.length > 1400) return;
  particles.push(Object.assign({ vx: 0, vy: 0, drag: 0.94, grav: 0, size: 2, life: 0.5,
    r: 255, g: 200, b: 160, anchor: null }, o, { max: o.life ?? 0.5 }));
}

export function burst(x, y, n, speed, color, extra = {}, dir = 0, spread = Math.PI) {
  for (let k = 0; k < n; k++) {
    const a = dir + rand(-spread, spread), s = rand(0.2, 1) * speed;
    spawn(Object.assign({ x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s, life: rand(0.4, 1.0),
      size: rand(1.5, 3.5), r: color[0], g: color[1], b: color[2], drag: 0.93 }, extra));
  }
}

export function updateParticles(dt) {
  for (let i = particles.length - 1; i >= 0; i--) {
    const q = particles[i];
    q.life -= dt;
    if (q.life <= 0) { particles.splice(i, 1); continue; }
    const d = Math.pow(q.drag, dt * 60);
    q.vx *= d; q.vy = q.vy * d + q.grav * dt;
    q.x += q.vx * dt; q.y += q.vy * dt;
  }
}

export function updateFloaters(dt) {
  for (let i = floaters.length - 1; i >= 0; i--) {
    const f = floaters[i];
    f.life -= dt;
    if (f.life <= 0) floaters.splice(i, 1); else f.y -= dt * (f.big ? 20 : 60);
  }
}

export function decayScreenFx(dt) {
  fx.flash *= Math.exp(-dt * 5);
  fx.shake *= Math.exp(-dt * 7);
}
