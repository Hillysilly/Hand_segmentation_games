// One frame of the app, shared by the live loop, the simulator and the tests: read the input
// source, then advance everything. Drawing is separate (render.js) so tests can skip it.
import { setClock, persons, hands, particles, shockwaves, floaters, fx } from "./state.js";
import { mic } from "./audio.js";
import { update } from "./update.js";
import { flames, balls } from "./powers/ninja.js";
import { webs } from "./powers/spider.js";
import { resetArmor } from "./powers/armor.js";
import { resetCloak } from "./powers/cloak.js";
import { resetNanotech } from "./powers/nanotech.js";

// source.poll(now, t) feeds this frame's detections in through ingestPoses / ingestHands.
export function step(source, dt, now, t) {
  setClock(now);
  source.poll(now, t);
  mic.update(dt);
  update(dt, now);
}

// Empty the scene: no people, hands or effects, and a fresh mic noise floor. Settings are left alone.
export function resetWorld() {
  for (const list of [persons, hands, particles, shockwaves, floaters, flames, balls, webs]) list.length = 0;
  Object.assign(fx, { flash: 0, shake: 0, flashColor: "170,230,255" });
  mic.reset();
  resetArmor();
  resetCloak();
  resetNanotech();
}
