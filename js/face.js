// Face expressions, for the Spider-Man suit's eyes: how open each eye is and how the brows sit.
//
// Openness comes from the eyelid landmarks (lid gap over eye width, against the person's own usual
// open eye), and which eye is which from where it sits in the image, so nothing hangs on the face
// model's left/right naming. The brows come from its expression scores, averaged across both sides.
import { persons, frame } from "./state.js";
import { dist, toScreen, videoRect } from "./util.js";

// The face landmarks used: nose tip, then per eye [corner, corner, upper lid, lower lid]
export const FACE_POINTS = [1, 33, 133, 159, 145, 263, 362, 386, 374];
const EYES = [[33, 133, 159, 145], [263, 362, 386, 374]];
// ...and the expression scores
export const FACE_SHAPES = ["browDownLeft", "browDownRight", "browInnerUp"];

// faces: [{ pts: { landmark: { x, y } } (normalised, raw frame), shapes: { name: score } }]
export function ingestFaces(faces, now) {
  const r = videoRect(), aspect = frame.w / frame.h;
  for (const f of faces) {
    const nose = toScreen(f.pts[1], r);
    let p = null, best = Infinity;
    for (const q of persons) {
      if (!q.lm) continue;
      const d = dist(nose, q.lm[0]);
      if (d < best && d < dist(q.lm[7], q.lm[8]) * 0.8 + 20) { best = d; p = q; }
    }
    if (!p) continue;
    const at = i => ({ x: f.pts[i].x * aspect, y: f.pts[i].y });
    const gap = EYES.map(([a, b, up, down]) => dist(at(up), at(down)) / (dist(at(a), at(b)) || 1));
    // the person's right eye is the one further left in the (unmirrored) frame
    const [right, left] = f.pts[33].x + f.pts[133].x < f.pts[263].x + f.pts[362].x ? gap : [gap[1], gap[0]];
    const st = p.face ||= { usual: 0, rest: { down: 1, up: 1 }, open: [1, 1], browDown: 0, browUp: 0, at: 0 };
    // their usual open eye: drifts toward what it sees, ignoring blinks and squints
    for (const g of [right, left]) {
      if (!st.usual) st.usual = g;
      else if (g > st.usual * 0.7) st.usual += (g - st.usual) * 0.01;
    }
    // [the suit's left, its right]: the screen is a mirror, so the suit's left eye is their right
    st.open = [right / st.usual, left / st.usual];
    // brows against their resting face (some faces score a frown at rest): the rest level drops
    // quickly to a relaxed brow and creeps up only slowly, so a held frown still counts
    const s = f.shapes;
    const brow = (score, key) => {
      const r = st.rest[key] += (score - st.rest[key]) * (score < st.rest[key] ? 0.2 : 0.002);
      return Math.min(1, Math.max(0, (score - r) / (1 - r || 1) * 1.3));
    };
    st.browDown = brow(((s.browDownLeft || 0) + (s.browDownRight || 0)) / 2, "down");
    st.browUp = brow(s.browInnerUp || 0, "up");
    st.at = now;
  }
}
