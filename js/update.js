// Per-frame game logic: advance every person, hand, power and effect by dt.
import { settings, MAX_HEALTH } from "./config.js";
import { persons, hands, prune, timed, unseenFor } from "./state.js";
import { dist } from "./util.js";
import { sfx } from "./audio.js";
import { burst, updateParticles, updateFloaters, decayScreenFx } from "./effects.js";
import { isFresh } from "./people.js";
import { ownerOf } from "./hands.js";
import { updateShockwaves } from "./combat.js";
import { isFlexing, updateGoku } from "./powers/goku.js";
import { updateSpin } from "./powers/spinjitzu.js";
import { handFlames, trackThrow, updateBalls, updateFlames } from "./powers/ninja.js";
import { WEB_COOLDOWN, shootWeb, updateWebs } from "./powers/spider.js";
import { updateRepulsor, updateBeam } from "./powers/ironman.js";
import { updateArmor } from "./powers/armor.js";
import { updateNanotech } from "./powers/nanotech.js";
import { updateUnmask } from "./powers/unmask.js";
import { updateRoom } from "./room.js";
import { updateCloak } from "./powers/cloak.js";

export function update(dt, now) {
  let maxCharge = 0, beams = 0, maxSpin = 0;

  prune(persons, p => unseenFor(p.lastSeen) < 1500);
  updateRoom(dt);
  for (const p of persons) {
    p.hitFlash = Math.max(0, p.hitFlash - dt * 2.5);
    p.flexing = settings.goku && p.ko === 0 && p.lm && isFresh(p) && isFlexing(p.lm);
    maxSpin = Math.max(maxSpin, updateSpin(p, dt, now));
    if (p.ko > 0) {
      p.ko -= dt;
      if (p.ko <= 0) { p.ko = 0; p.health = MAX_HEALTH; }
    }
    // trailing segment drains down to health, and refills along with it on respawn
    p.shown += (p.health - p.shown) * Math.min(1, dt * (p.health > p.shown ? 4 : 2));
  }

  prune(hands, h => {
    if (unseenFor(h.lastSeen) <= 300) return true;
    // Hand vanished: spill any stored charge as a fizzle.
    burst(h.cx, h.cy, Math.round(h.charge * 40), 250, [120, 220, 255], { life: 0.5 });
    return false;
  });

  for (const h of hands) {
    const p = h.pts;
    h.cx = (p[0].x + p[5].x + p[9].x + p[13].x + p[17].x) / 5;
    h.cy = (p[0].y + p[5].y + p[9].y + p[13].y + p[17].y) / 5;
    h.ps = Math.max(30, dist(p[0], p[9]));
    h.owner = ownerOf(h);
    const disabled = h.owner && h.owner.ko > 0;
    // Repulsor charging and firing are Iron Man only, and pause while the owner flexes:
    // the fists in a flex would otherwise charge (and later fire) blue repulsors.
    // ...and while the hand is held on the chest for the nanotech suit-up
    const armed = !disabled && settings.ironman && !h.owner?.flexing && !h.owner?.nano?.hold;

    updateRepulsor(h, dt, armed);
    maxCharge = Math.max(maxCharge, h.charge);

    // ninja: elemental flames on the hands, and a fast punch / throw shoots a ball
    if (settings.spinjitzu && !disabled) {
      handFlames(h, dt);
      trackThrow(h, now);
    }

    // spider: holding the web-shooter sign fires a web every WEB_COOLDOWN
    if (settings.spiderman && !disabled && h.gesture === "web" && now - h.lastWeb > WEB_COOLDOWN) shootWeb(h, now);

    if (updateBeam(h, dt, armed)) beams++;
  }

  updateBalls(dt);
  updateFlames(dt);
  updateWebs(now);
  for (const p of persons) maxCharge = Math.max(maxCharge, updateGoku(p, dt));

  updateParticles(dt);
  updateShockwaves(dt);
  updateFloaters(dt);
  decayScreenFx(dt);
  timed("nano", () => updateNanotech(dt));
  updateUnmask(dt);
  timed("suit", () => updateArmor(dt));
  timed("cloak", updateCloak);

  sfx.setCharge(maxCharge);
  sfx.setBeam(beams);
  sfx.setWind(maxSpin);
}
