// Spider-Man's mask comes off the way you'd pull it off: grab it (a fist on your head) and pull it
// down past your chest. Do the reverse (a fist below your chest pulled up onto your head) to put it
// back on. The mask peels away in the shader (see suitmodel.js), and the cloak leaves a hole over
// the head so your real one shows (see cloak.js).
import { wearing } from "../config.js";
import { persons, hands, clock } from "../state.js";
import { dist, clamp } from "../util.js";
import { isFresh } from "../people.js";
import { sfx } from "../audio.js";

const GRIP = 1.4;     // how near the middle of the head a fist must be, in head widths (a flexed fist is ~3 out)
const PULL = 1.5;     // seconds to finish the pull once gripped
const PEEL = 0.35;    // seconds the mask takes to come off or go on

// How much of the mask is on (1 on, 0 off).
export const maskLevel = p => p.unmask?.level ?? 1;

// The head's middle, its width, and the chest line, on screen.
function landmarks(L) {
  const head = { x: (L[7].x + L[8].x) / 2, y: (L[7].y + L[8].y) / 2 };
  const width = Math.max(dist(L[7], L[8]), dist(L[11], L[12]) * 0.4);
  const chest = (L[11].y + L[12].y) / 2 + ((L[23].y + L[24].y) / 2 - (L[11].y + L[12].y) / 2) * 0.27;
  return { head, width, chest };
}

export function updateUnmask(dt) {
  for (const p of persons) {
    const m = p.unmask ||= { level: 1, dir: 0, grip: null };
    if (wearing() !== "spiderman") { Object.assign(m, { level: 1, dir: 0, grip: null }); continue; }
    if (m.dir) {
      m.level = clamp(m.level + m.dir * dt / PEEL, 0, 1);
      if (m.level === 0 || m.level === 1) m.dir = 0;
    }
    if (!p.lm || !isFresh(p) || p.flexing) { m.grip = null; continue; }   // a flex's fists aren't a grip
    const { head, width, chest } = landmarks(p.lm);
    const fists = hands.filter(h => h.owner === p && h.gesture === "fist" && h.cx !== undefined);
    const onHead = h => dist({ x: h.cx, y: h.cy }, head) < GRIP * width;
    const belowChest = h => h.cy > chest;
    const on = m.level > 0.5;
    const start = on ? onHead : belowChest, end = on ? belowChest : onHead;
    // A pull in progress: done when a fist gets to the other end in time. Any of their fists, not
    // just the gripping one: a fast pull can lose the hand's track for a moment. But not one that
    // was already there when they gripped (the other hand resting at the hip, say).
    if (m.grip) {
      if (clock - m.grip.at > PULL * 1000) m.grip = null;
      else if (fists.some(h => end(h) && !m.grip.already.includes(h))) {
        m.dir = on ? -1 : 1;
        m.grip = null;
        sfx.whoosh();
      }
    }
    // a new grip: on the head while the mask's on, below the chest while it's off
    if (!m.grip && !m.dir && fists.some(start)) m.grip = { at: clock, already: fists.filter(end) };
  }
}

// Where the head is, for the cloak to leave it showing: a centre and radii on screen.
export function headZone(p) {
  const { head, width } = landmarks(p.lm);
  return { x: head.x, y: head.y + width * 0.25, rx: width * 0.85, ry: width * 1.45 };
}
