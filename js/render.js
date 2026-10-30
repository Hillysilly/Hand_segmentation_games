// Drawing a frame: camera, auras, suit, hand effects, then the HUD on top.
import { POSE_CONNECTIONS, CONNECTIONS, MAX_HEALTH, settings } from "./config.js";
import { ctx, W, H, DPR, frame, fx, persons, hands, particles, shockwaves, floaters, perf, timed } from "./state.js";
import { clamp, videoRect } from "./util.js";
import { glow } from "./gfx.js";
import { mic } from "./audio.js";
import { isFresh, maxHealth } from "./people.js";
import { ring } from "./combat.js";
import { AURA_STAGES, drawAura, drawFlexGauge } from "./powers/goku.js";
import { drawTornado } from "./powers/spinjitzu.js";
import { drawFlames, drawBalls } from "./powers/ninja.js";
import { drawWebs } from "./powers/spider.js";
import { drawBeam, drawCharge, drawPalmGlow, drawAim } from "./powers/ironman.js";
import { drawArmor, drawArmorGlows, inGauntlet } from "./powers/armor.js";
import { drawCloak } from "./powers/cloak.js";

function drawSkeleton(h) {
  const p = h.pts;
  ctx.lineCap = "round";
  ctx.strokeStyle = "rgba(60,200,255,0.18)"; ctx.lineWidth = 7;
  ctx.beginPath();
  for (const [a, b] of CONNECTIONS) { ctx.moveTo(p[a].x, p[a].y); ctx.lineTo(p[b].x, p[b].y); }
  ctx.stroke();
  ctx.strokeStyle = "rgba(150,235,255,0.7)"; ctx.lineWidth = 1.5;
  ctx.stroke();
  ctx.fillStyle = "rgba(200,245,255,0.9)";
  for (const q of p) { ctx.beginPath(); ctx.arc(q.x, q.y, 2.5, 0, Math.PI * 2); ctx.fill(); }
}

function drawShockwaves() {
  for (const s of shockwaves) {
    const t = s.life / s.max, g = ring(s);
    ctx.lineWidth = Math.max(1, s.width * t);
    ctx.strokeStyle = `rgba(${s.color || "160,230,255"},${0.8 * t})`;
    ctx.beginPath(); ctx.ellipse(g.cx, g.cy, g.rx, g.ry, g.rot, 0, Math.PI * 2); ctx.stroke();
    ctx.lineWidth = Math.max(1, s.width * t * 0.3);
    ctx.strokeStyle = `rgba(255,255,255,${0.9 * t})`;
    ctx.stroke();
    if (t > 0.75) {
      glow(g.cx, g.cy, (g.rx + g.ry) / 2,
        [[0, `rgba(255,255,255,${(t - 0.75) * 3})`], [1, "rgba(100,200,255,0)"]]);
    }
  }
}

function drawParticles() {
  ctx.lineCap = "round";
  for (const q of particles) {
    const a = q.life / q.max;
    const x = q.x + (q.anchor ? q.anchor.cx : 0), y = q.y + (q.anchor ? q.anchor.cy : 0);
    ctx.strokeStyle = `rgba(${q.r | 0},${q.g | 0},${q.b | 0},${a})`;
    ctx.lineWidth = q.size;
    ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x - q.vx * 0.025, y - q.vy * 0.025); ctx.stroke();
  }
}

// Draw one of a person's mask-sized canvases mirrored over the video.
function drawMaskLayer(img, alpha) {
  const r = videoRect();
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.translate(r.x + r.w, r.y); ctx.scale(-1, 1);
  ctx.drawImage(img, 0, 0, r.w, r.h);
  ctx.restore();
}

function drawPoseSkeleton(p) {
  const L = p.lm, [r, g, b] = p.color;
  ctx.lineCap = "round";
  ctx.strokeStyle = `rgba(${r},${g},${b},0.75)`; ctx.lineWidth = 2;
  ctx.beginPath();
  for (const [a, c] of POSE_CONNECTIONS) {
    if (L[a].v < 0.5 || L[c].v < 0.5) continue;
    ctx.moveTo(L[a].x, L[a].y); ctx.lineTo(L[c].x, L[c].y);
  }
  ctx.stroke();
  ctx.fillStyle = `rgba(${r},${g},${b},0.95)`;
  for (const q of L) {
    if (q.v < 0.5) continue;
    ctx.beginPath(); ctx.arc(q.x, q.y, 3, 0, Math.PI * 2); ctx.fill();
  }
}

function drawSilhouette(p) {
  // a cloaked body is replaced by the suit, so its outline would just trace the old body
  if (settings.silhouettes && isFresh(p) && !(settings.armor && settings.cloak)) drawMaskLayer(p.edge, 0.6);
}

function drawHitGlow(p) {
  if (p.hitFlash <= 0.01) return;
  if (isFresh(p)) drawMaskLayer(p.fill, 0.55 * p.hitFlash);
  else glow(p.cx, p.cy, p.w * 1.1, [[0, `rgba(255,60,40,${0.6 * p.hitFlash})`], [1, "rgba(255,0,0,0)"]]);
}

function drawReticle(h, now) {
  const R = h.ps * 1.25, rot = now / 1000;
  ctx.strokeStyle = "rgba(90,220,255,0.55)";
  ctx.lineWidth = 1.5;
  for (let i = 0; i < 3; i++) {
    const a = rot * 0.8 + i * Math.PI * 2 / 3;
    ctx.beginPath(); ctx.arc(h.cx, h.cy, R, a, a + 1.3); ctx.stroke();
  }
  ctx.lineWidth = 1;
  for (let i = 0; i < 4; i++) {
    const a = -rot * 1.3 + i * Math.PI / 2;
    ctx.beginPath(); ctx.arc(h.cx, h.cy, R * 1.1, a, a + 0.35); ctx.stroke();
  }
  if (h.charge > 0.01) {
    ctx.strokeStyle = h.charge >= 1 ? "rgba(255,255,255,0.95)" : "rgba(160,240,255,0.9)";
    ctx.lineWidth = 4;
    ctx.beginPath();
    ctx.arc(h.cx, h.cy, R * 0.9, -Math.PI / 2, -Math.PI / 2 + h.charge * Math.PI * 2);
    ctx.stroke();
  }

  // callout label
  const ax = h.cx + Math.cos(-0.7) * R, ay = h.cy + Math.sin(-0.7) * R;
  const lx = ax + 30, ly = ay - 30;
  ctx.strokeStyle = "rgba(90,220,255,0.6)"; ctx.lineWidth = 1;
  ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(lx, ly); ctx.lineTo(lx + 140, ly); ctx.stroke();
  const disabled = h.owner && h.owner.ko > 0;
  const state = disabled ? "OFFLINE"
    : h.owner?.flexing ? "POWERING UP"
    : h.gesture === "web" && settings.spiderman ? "WEB SHOOTER"
    : !settings.ironman || h.gesture === "web" ? "TRACKING"
    : { fist: h.charge >= 1 ? "CHARGED" : "CHARGING", open: h.beam > 0.5 ? "BEAM" : "REPULSOR",
        none: "TRACKING" }[h.gesture];
  const who = persons.length >= 2 && h.owner ? `P${h.owner.num} ` : "";
  ctx.fillStyle = "rgba(170,240,255,0.95)";
  ctx.font = "12px Consolas, monospace";
  ctx.fillText(`${who}${h.label} // ${state}`, lx + 2, ly - 6);
  if (settings.ironman) {
    ctx.fillStyle = "rgba(120,210,240,0.75)";
    ctx.fillText(`PWR ${String(Math.round(h.charge * 100)).padStart(3, "0")}%`, lx + 2, ly + 14);
  }
}

function healthColor(frac) {
  return frac > 0.5 ? [90, 220, 255] : frac > 0.25 ? [255, 190, 60] : [255, 60, 50];
}

function drawPlayer(p, now) {
  const visible = now - p.lastSeen < 300 ? 1 : 0.35;
  ctx.globalAlpha = visible;

  // corner brackets around the face
  const L = p.w * 0.22, pad = p.w * 0.08;
  const x0 = p.x - pad, y0 = p.y - pad, x1 = p.x + p.w + pad, y1 = p.y + p.h + pad;
  ctx.strokeStyle = p.ko > 0 ? "rgba(255,80,60,0.8)" : "rgba(90,220,255,0.6)";
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(x0, y0 + L); ctx.lineTo(x0, y0); ctx.lineTo(x0 + L, y0);
  ctx.moveTo(x1 - L, y0); ctx.lineTo(x1, y0); ctx.lineTo(x1, y0 + L);
  ctx.moveTo(x0, y1 - L); ctx.lineTo(x0, y1); ctx.lineTo(x0 + L, y1);
  ctx.moveTo(x1 - L, y1); ctx.lineTo(x1, y1); ctx.lineTo(x1, y1 - L);
  ctx.stroke();

  // health bar
  const bw = Math.max(140, p.w * 1.4), bh = 12;
  const bx = p.cx - bw / 2, by = y0 - 28;
  const max = maxHealth(p), baseFrac = MAX_HEALTH / max;
  const frac = p.health / max, shownFrac = clamp(p.shown / max, 0, 1);
  const [r, g, b] = healthColor(frac);
  ctx.fillStyle = "rgba(0,15,25,0.7)";
  ctx.fillRect(bx, by, bw, bh);
  if (shownFrac > frac) {
    ctx.fillStyle = "rgba(255,240,220,0.85)";
    ctx.fillRect(bx + bw * frac, by, bw * (shownFrac - frac), bh);
  }
  const lowPulse = frac <= 0.25 && p.ko === 0 ? 0.6 + 0.4 * Math.sin(now * 0.015) : 1;
  ctx.fillStyle = `rgba(${r},${g},${b},${0.9 * lowPulse})`;
  ctx.fillRect(bx, by, bw * Math.min(frac, shownFrac), bh);
  // health above the base 100 comes from the aura, so it's drawn in the aura's color
  const fill = Math.min(frac, shownFrac);
  if (p.stage > 0 && fill > baseFrac) {
    ctx.fillStyle = `rgba(${AURA_STAGES[p.stage].line},0.95)`;
    ctx.fillRect(bx + bw * baseFrac, by, bw * (fill - baseFrac), bh);
  }
  ctx.strokeStyle = "rgba(0,0,0,0.5)"; ctx.lineWidth = 1;
  ctx.beginPath();
  const ticks = max / 10;   // one tick per 10 HP, so a powered-up bar shows more segments
  for (let k = 1; k < ticks; k++) { const tx = bx + bw * k / ticks; ctx.moveTo(tx, by); ctx.lineTo(tx, by + bh); }
  ctx.stroke();
  ctx.strokeStyle = `rgba(${r},${g},${b},0.9)`;
  ctx.strokeRect(bx - 0.5, by - 0.5, bw + 1, bh + 1);

  ctx.font = "bold 13px Consolas, monospace";
  ctx.fillStyle = `rgb(${p.color.join(",")})`;
  ctx.fillText(`P${p.num}`, bx, by - 6);
  ctx.fillStyle = "rgba(200,245,255,0.95)";
  ctx.textAlign = "right";
  ctx.fillText(p.ko > 0 ? `REBOOT ${p.ko.toFixed(1)}s` : `${Math.ceil(p.health)}`, bx + bw, by - 6);
  ctx.textAlign = "left";
  ctx.globalAlpha = 1;
}

function drawFloaters() {
  ctx.textAlign = "center";
  for (const f of floaters) {
    const a = Math.min(1, f.life / f.max * 2);
    ctx.font = f.big ? "bold 64px Consolas, monospace" : "bold 26px Consolas, monospace";
    ctx.fillStyle = `rgba(${f.color || (f.big ? "255,90,60" : "255,120,90")},${a})`;
    ctx.fillText(f.text, f.x, f.y);
  }
  ctx.textAlign = "left";
}

function drawHud(fps) {
  const m = 22, L = 40;
  ctx.strokeStyle = "rgba(90,220,255,0.5)"; ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(m, m + L); ctx.lineTo(m, m); ctx.lineTo(m + L, m);
  ctx.moveTo(W - m - L, m); ctx.lineTo(W - m, m); ctx.lineTo(W - m, m + L);
  ctx.moveTo(m, H - m - L); ctx.lineTo(m, H - m); ctx.lineTo(m + L, H - m);
  ctx.moveTo(W - m - L, H - m); ctx.lineTo(W - m, H - m); ctx.lineTo(W - m, H - m - L);
  ctx.stroke();
  ctx.font = "12px Consolas, monospace";
  ctx.fillStyle = "rgba(150,230,255,0.85)";
  ctx.fillText(persons.length >= 2 ? "HUD // COMBAT MODE" : "HUD // ONLINE", m + 10, m + 22);
  ctx.fillText(`PEOPLE ${persons.length}   HANDS ${hands.length}   FPS ${fps.toFixed(0)}`, m + 10, m + 40);
  let line = m + 58;
  // where the frame time goes: tracking models, suit posing, cloak, 3D draw, everything else drawn
  const ms = ["pose", "hands", "suit", "cloak", "3d", "draw"].filter(k => perf[k] !== undefined);
  if (ms.length) {
    ctx.fillText(`MS ${ms.map(k => `${k.toUpperCase()} ${perf[k].toFixed(1)}`).join("  ")}`, m + 10, line);
    line += 18;
  }
  if (settings.spinjitzu && persons.length) {
    // what the spin detector sees: facing direction and shoulder width vs. front-on
    const spin = persons.map(p => `P${p.num} ${p.facing.toUpperCase()} ${Math.round(p.swNow * 100)}%`).join("  ");
    ctx.fillText(`SPIN ${spin}`, m + 10, line);
    line += 18;
  }
  if (settings.goku) {
    // mic meter for the yell power-up; the tick marks how loud counts as a yell
    ctx.fillText("MIC", m + 10, line);
    const x = m + 42, y = line - 10, bw = 120, span = 50, lo = mic.threshold - 35;
    if (mic.ready) {
      ctx.strokeStyle = "rgba(150,230,255,0.5)"; ctx.lineWidth = 1;
      ctx.strokeRect(x, y, bw, 10);
      ctx.fillStyle = mic.yelling ? "rgba(255,220,40,0.95)" : "rgba(150,230,255,0.7)";
      ctx.fillRect(x, y, bw * clamp((mic.level - lo) / span, 0, 1), 10);
      ctx.fillStyle = "rgba(255,235,120,0.95)";
      ctx.fillRect(x + bw * 35 / span - 1, y - 2, 2, 14);
      ctx.fillStyle = "rgba(150,230,255,0.85)";
    } else ctx.fillText("OFF // allow the mic to power up Goku", x, line);
    line += 18;
  }
  if (settings.muted) ctx.fillText("AUDIO MUTED", m + 10, line);
  ctx.textAlign = "right";
  ctx.fillStyle = "rgba(150,230,255,0.5)";
  ctx.fillText("P powers panel · F fullscreen", W - m - 10, H - m - 12);
  ctx.textAlign = "left";
}

export function render(now, fps) {
  const sx = (Math.random() * 2 - 1) * fx.shake, sy = (Math.random() * 2 - 1) * fx.shake;
  ctx.setTransform(DPR, 0, 0, DPR, sx * DPR, sy * DPR);
  ctx.globalCompositeOperation = "source-over";
  ctx.fillStyle = "#000";
  ctx.fillRect(-50, -50, W + 100, H + 100);

  const img = frame.img;
  if (settings.video && img && (img.readyState === undefined || img.readyState >= 2)) {
    const r = videoRect();
    ctx.save();
    ctx.translate(r.x + r.w, r.y); ctx.scale(-1, 1);
    ctx.drawImage(img, 0, 0, r.w, r.h);
    ctx.restore();
    drawCloak();
    ctx.fillStyle = "rgba(0,10,20,0.35)";  // dim so the glows pop
    ctx.fillRect(-50, -50, W + 100, H + 100);
  }

  ctx.globalCompositeOperation = "lighter";
  for (const p of persons) {
    drawTornado(p, now);
    if (isFresh(p)) drawAura(p, now);
    drawSilhouette(p);
    drawHitGlow(p);
    if (settings.skeleton && p.lm && isFresh(p)) drawPoseSkeleton(p);
  }
  // the suit is solid metal: painted over the body, under the hand effects
  ctx.globalCompositeOperation = "source-over";
  timed("3d", drawArmor);
  ctx.globalCompositeOperation = "lighter";
  drawArmorGlows();
  for (const h of hands) {
    if (settings.skeleton && !inGauntlet(h)) drawSkeleton(h);
    drawPalmGlow(h, now);
    drawAim(h, now);
    if (settings.ironman) drawCharge(h, now);
    drawBeam(h, now);
  }
  drawFlames();
  drawBalls(now);
  drawShockwaves();
  drawParticles();
  if (fx.flash > 0.01) {
    ctx.fillStyle = `rgba(${fx.flashColor},${fx.flash * 0.55})`;
    ctx.fillRect(-50, -50, W + 100, H + 100);
  }

  ctx.globalCompositeOperation = "source-over";
  drawWebs(now);
  for (const h of hands) drawReticle(h, now);
  for (const p of persons) drawFlexGauge(p);
  if (persons.length >= 2) for (const p of persons) drawPlayer(p, now);
  drawFloaters();

  ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
  const v = ctx.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.35, W / 2, H / 2, Math.max(W, H) * 0.75);
  v.addColorStop(0, "rgba(0,0,0,0)"); v.addColorStop(1, "rgba(0,10,20,0.7)");
  ctx.fillStyle = v; ctx.fillRect(0, 0, W, H);
  drawHud(fps);
}
