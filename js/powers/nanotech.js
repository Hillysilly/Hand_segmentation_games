// Nanotech suit-up: hold a hand flat on your chest and the suit flows out from the arc reactor over
// your body; do it again to send it back in.
//
// Each person's suit has a level from 0 (no suit) to 1 (fully on). The flowing itself happens on the
// 3D model (see suitmodel.js): every vertex knows how far it is from the reactor along the body, and
// the shader shows only what the front has reached, with a band of liquid silver at the front.
import { settings, wearing } from "../config.js";
import { persons, hands } from "../state.js";
import { dist, clamp } from "../util.js";
import { isFresh } from "../people.js";
import { sfx } from "../audio.js";

const HOLD = 0.6;        // seconds the hand stays on the chest to trigger
const ON_CHEST = 0.32;   // how near the chest the hand must be, in shoulder widths
const FORM = 2.2;        // seconds for the suit to cover the body
const RETRACT = 1.4;     // ...and to pull back in
const CLOAK_FROM = 0.9;  // the cloak fades the real body out over the last stretch of forming

const level = p => (settings.nanotech ? p.nano?.level ?? 0 : 1);
// How much of the suit this person has on (0..1); the suit isn't drawn at all at 0. Only Iron Man's
// suit forms with nanotech; Spider-Man's is simply on.
export const suitLevel = p => {
  const wear = wearing();
  return wear === "ironman" ? level(p) : wear ? 1 : 0;
};
// How strongly the cloak hides this person's real body (0..1): only once the suit has nearly formed.
export const cloakFade = p => clamp((suitLevel(p) - CLOAK_FROM) / (1 - CLOAK_FROM), 0, 1);

export function resetNanotech() {
  for (const p of persons) p.nano = null;
}

// The arc reactor's spot on the chest, in screen space.
function chest(L) {
  const sx = (L[11].x + L[12].x) / 2, sy = (L[11].y + L[12].y) / 2;
  const hx = (L[23].x + L[24].x) / 2, hy = (L[23].y + L[24].y) / 2;
  return { x: sx + (hx - sx) * 0.27, y: sy + (hy - sy) * 0.27 };
}

// Is one of their hands held on their chest? Either tracked hand, or the pose's wrists and index
// knuckles (the hand tracker loses a hand flat against the body more often than the pose does).
function handOnChest(p) {
  const L = p.lm, c = chest(L), near = ON_CHEST * dist(L[11], L[12]);
  for (const j of [15, 16, 19, 20]) if (L[j].v > 0.5 && dist(L[j], c) < near) return true;
  return hands.some(hd => hd.owner === p && hd.cx !== undefined && dist({ x: hd.cx, y: hd.cy }, c) < near);
}

export function updateNanotech(dt) {
  for (const p of persons) {
    if (!p.nano) p.nano = { level: 0, dir: 0, hold: 0 };
    const n = p.nano;
    if (!settings.nanotech || wearing() !== "ironman" || !p.lm || !isFresh(p) || p.ko > 0) { n.hold = 0; continue; }
    if (handOnChest(p)) {
      if (n.hold >= 0 && (n.hold += dt) >= HOLD) {
        n.dir = n.level < 0.5 && n.dir <= 0 ? 1 : -1;
        n.hold = -1;   // trigger once per press: wait for the hand to come off
        sfx.whoosh();
        if (sfx.ready) sfx.tone("triangle", n.dir > 0 ? 300 : 1400, n.dir > 0 ? 1400 : 300, n.dir > 0 ? FORM : RETRACT, 0.06);
      }
    } else n.hold = 0;
    if (n.dir) {
      n.level = clamp(n.level + n.dir * dt / (n.dir > 0 ? FORM : RETRACT), 0, 1);
      if (n.level === 0 || n.level === 1) n.dir = 0;
    }
  }
}
