// Iron Man repulsors: a fist charges, snapping open blasts, holding the palm open fires a beam.
import { BEAM_DPS } from "../config.js";
import { ctx } from "../state.js";
import { clamp, rand } from "../util.js";
import { spawn, burst } from "../effects.js";
import { glow, line, bolt } from "../gfx.js";
import { blast, beamFor, beamDamage } from "../combat.js";

export function updateRepulsor(h, dt, armed) {
  if (h.gesture === "fist" && armed) {
    h.charge = Math.min(1, h.charge + dt / 1.1);
    // particles get sucked into the fist
    const n = 2 + h.charge * 4;
    for (let k = 0; k < n; k++) {
      const a = Math.random() * Math.PI * 2, rr = h.ps * rand(1.0, 1.7), life = rand(0.25, 0.4);
      spawn({ anchor: h, x: Math.cos(a) * rr, y: Math.sin(a) * rr,
        vx: -Math.cos(a) * rr / life, vy: -Math.sin(a) * rr / life, drag: 1, life,
        size: rand(1, 2.5), r: 120, g: 225, b: 255 });
    }
  } else if (h.gesture === "open" && h.charge > 0.2 && armed) {
    blast(h);
  } else {
    h.charge = Math.max(0, h.charge - dt * 0.7);  // grace period so fist -> open still fires
  }
  h.openGlow += ((h.gesture === "open" && armed ? 1 : 0) - h.openGlow) * Math.min(1, dt * 8);
}

// Holding the palm open fires a continuous beam. Returns whether it's on.
export function updateBeam(h, dt, armed) {
  h.openTime = h.gesture === "open" ? h.openTime + dt : 0;
  const firing = h.openTime > 0.35 && armed;
  h.beam += ((firing ? 1 : 0) - h.beam) * Math.min(1, dt * 12);
  if (h.beam <= 0.05) return false;
  const b = h.geom = beamFor(h);
  if (b.target) beamDamage(b.target, BEAM_DPS * h.beam * dt);
  if (b.len > 5) {
    // sparks spraying back off whatever the beam hits
    const back = Math.atan2(-b.dy, -b.dx), hot = b.target ? [255, 120, 80] : [170, 235, 255];
    burst(b.ex, b.ey, b.target ? 5 : 3, 650, hot, { grav: 900, life: rand(0.25, 0.6), drag: 0.97 }, back, 1.1);
    if (Math.random() < 0.6) {
      const t = Math.random();
      spawn({ x: b.x + (b.ex - b.x) * t, y: b.y + (b.ey - b.y) * t, vx: rand(-40, 40), vy: rand(-80, -10),
        life: rand(0.3, 0.7), size: rand(1, 2), r: 140, g: 220, b: 255, drag: 0.98 });
    }
  }
  return true;
}

export function drawBeam(h, now) {
  if (h.beam <= 0.05 || !h.geom) return;
  const b = h.geom;
  const w = h.ps * 0.16 * h.beam;
  const fl = 0.85 + 0.12 * Math.sin(now * 0.06) + Math.random() * 0.1;
  const facing = 1 - b.side;   // ~1 when the palm points at the camera

  if (b.len > 1) {
    ctx.lineCap = "round";
    line(b.x, b.y, b.ex, b.ey, w * 6 * fl, "rgba(40,140,255,0.07)");
    line(b.x, b.y, b.ex, b.ey, w * 3 * fl, "rgba(60,170,255,0.18)");
    line(b.x, b.y, b.ex, b.ey, w * 1.5 * fl, "rgba(140,220,255,0.55)");
    line(b.x, b.y, b.ex, b.ey, w * 0.55 * fl, "rgba(240,252,255,0.95)");

    // two thin strands spiralling around the beam
    const nx = -b.dy, ny = b.dx;
    ctx.lineWidth = Math.max(1, w * 0.15);
    ctx.strokeStyle = "rgba(170,235,255,0.5)";
    for (const phase of [0, Math.PI]) {
      ctx.beginPath();
      for (let s = 0; s <= b.len; s += 10) {
        const o = Math.sin(s * 0.04 - now * 0.025 + phase) * w * 1.1;
        const px = b.x + b.dx * s + nx * o, py = b.y + b.dy * s + ny * o;
        s === 0 ? ctx.moveTo(px, py) : ctx.lineTo(px, py);
      }
      ctx.stroke();
    }
    const hot = b.target ? "255,150,90" : "140,220,255";
    glow(b.ex, b.ey, w * 4 * fl, [[0, "rgba(255,255,255,0.9)"], [0.25, `rgba(${hot},0.5)`], [1, "rgba(0,80,255,0)"]]);
  }
  // muzzle glow, much bigger when the beam is coming straight at the viewer
  glow(b.x, b.y, w * (3 + facing * 6) * fl, [[0, "rgba(255,255,255,0.95)"],
    [0.25, "rgba(150,225,255,0.6)"], [1, "rgba(0,100,255,0)"]]);
}

export function drawCharge(h, now) {
  const c = h.charge;
  if (c <= 0.01) return;
  const pulse = 1 + 0.08 * Math.sin(now * 0.03);
  const r = h.ps * (0.12 + 0.32 * c) * pulse;
  glow(h.cx, h.cy, r * 3, [[0, "rgba(255,255,255,1)"], [0.18, "rgba(170,240,255,0.9)"],
    [0.45, "rgba(40,170,255,0.35)"], [1, "rgba(0,80,255,0)"]]);
  if (c > 0.6) {
    ctx.strokeStyle = `rgba(180,240,255,${0.4 + c * 0.5})`;
    ctx.lineWidth = 1.5;
    const arcs = Math.round(c * 5);
    for (let k = 0; k < arcs; k++) {
      const a = Math.random() * Math.PI * 2, rr = r * rand(1.6, 3.2);
      bolt(h.cx, h.cy, h.cx + Math.cos(a) * rr, h.cy + Math.sin(a) * rr, 6, r * 0.35);
    }
  }
}

export function drawPalmGlow(h, now) {
  const o = h.openGlow;
  if (o <= 0.02) return;
  const r = h.ps * 0.55 * (1 + 0.05 * Math.sin(now * 0.02));
  glow(h.cx, h.cy, r, [[0, `rgba(255,255,255,${0.85 * o})`], [0.3, `rgba(140,225,255,${0.5 * o})`],
    [1, "rgba(0,120,255,0)"]]);
  // spinning ring lies flat on the palm, tilted the same way the blast will be
  ctx.strokeStyle = `rgba(140,230,255,${0.6 * o})`;
  ctx.lineWidth = 2;
  const spin = now * 0.002, tilt = Math.atan2(h.n.y, h.n.x), squash = Math.max(0.2, Math.abs(h.n.z));
  for (let k = 0; k < 6; k++) {
    const a = spin + k * Math.PI / 3;
    ctx.beginPath(); ctx.ellipse(h.cx, h.cy, r * 0.6 * squash, r * 0.6, tilt, a, a + 0.6); ctx.stroke();
  }
}

// Dashed aim line along the palm direction while charging or open (hidden when facing the camera).
export function drawAim(h, now) {
  const side = Math.hypot(h.n.x, h.n.y);
  const o = Math.max(h.openGlow, h.charge > 0.2 ? 1 : 0) * clamp((side - 0.3) / 0.3, 0, 1) * (1 - h.beam);
  if (o <= 0.02) return;
  const len = h.ps * 4 * side;
  ctx.setLineDash([6, 10]);
  ctx.lineDashOffset = -now * 0.06;
  ctx.lineWidth = 2;
  ctx.strokeStyle = `rgba(140,230,255,${0.5 * o})`;
  ctx.beginPath(); ctx.moveTo(h.cx, h.cy); ctx.lineTo(h.cx + h.n.x * len, h.cy + h.n.y * len); ctx.stroke();
  ctx.setLineDash([]);
}
