// A rough 3D model of the room, for things that should sit in it (webs): a box guessed from the
// people in it, no room scanning.
//   - the camera: a pinhole with a typical laptop webcam's field of view
//   - each person's distance: from how big they look (screen size over real size, from the world
//     landmarks)
//   - the floor: from their feet, or (feet out of shot) a hip height below their hips
//   - the ceiling a room height above that, the back wall a little behind the farthest anyone has
//     stood, the side walls a little beyond where anyone has stood
//   - and a "glass" plane just in front of the camera, for things fired straight at it
// Camera space: metres, x right and y down as on screen (the mirrored view), z away from the camera.
import { persons } from "./state.js";
import { videoRect, dist } from "./util.js";
import { isFresh } from "./people.js";
import { steady } from "./filter.js";

const HFOV = 60 * Math.PI / 180;   // a typical laptop webcam's horizontal field of view
const HIP_HEIGHT = 0.9;            // metres from the floor to the hips
const ROOM_HEIGHT = 2.5;           // floor to ceiling
const BEHIND = 1.2;                // the back wall this far behind the farthest anyone has stood...
const MIN_BACK = 2.5;              // ...and at least this far from the camera
const BESIDE = 1.2;                // the side walls this far beyond where anyone has stood...
const MIN_SIDE = 1.5;              // ...and at least this far either side of the camera
export const GLASS = 0.35;         // the plane in front of the camera
const FEET = [27, 28, 29, 30, 31, 32];

// y of the floor and ceiling, z of the back wall, |x| of the side walls; defaults until someone's seen
const DEFAULT = { floor: 1.1, back: 3.5, side: 2.2 };
export const room = { ...DEFAULT, ceiling: DEFAULT.floor - ROOM_HEIGHT, seen: false };
let far = 0, wide = 0;             // the farthest and widest anyone has stood

export function resetRoom() {
  Object.assign(room, DEFAULT, { ceiling: DEFAULT.floor - ROOM_HEIGHT, seen: false });
  far = wide = 0;
}

// Focal length and centre of the (mirrored) camera image on screen, in screen pixels.
export function lens() {
  const r = videoRect();
  return { f: r.w / 2 / Math.tan(HFOV / 2), cx: r.x + r.w / 2, cy: r.y + r.h / 2 };
}

// A screen point at depth z, in camera space.
export function unproject(sx, sy, z) {
  const c = lens();
  return { x: (sx - c.cx) * z / c.f, y: (sy - c.cy) * z / c.f, z };
}

// A camera-space point on screen (z: its depth).
export function project(P) {
  const c = lens();
  return { x: c.cx + P.x * c.f / P.z, y: c.cy + P.y * c.f / P.z, z: P.z };
}

export function updateRoom(dt) {
  const { f } = lens();
  for (const p of persons) {
    if (!p.lm || !p.world || !isFresh(p)) continue;
    const L = p.lm, w = p.world;
    // their distance: screen size over real size, across the shoulders and down the torso
    let px = 0, m = 0;
    for (const [i, j] of [[11, 12], [11, 23], [12, 24]]) {
      if (L[i].v < 0.5 || L[j].v < 0.5) continue;
      px += dist(L[i], L[j]);
      m += Math.hypot(w[i].x - w[j].x, w[i].y - w[j].y);
    }
    if (px < 10 || m < 0.05) continue;
    p.depth = steady(p.depth, f * m / px, dt);
    const at = j => unproject(L[j].x, L[j].y, p.depth + w[j].z);
    // the floor: their lowest visible foot, or a hip height below their hips
    const feet = FEET.filter(j => L[j].v > 0.6).map(j => at(j).y);
    const hips = (at(23).y + at(24).y) / 2;
    const floor = feet.length ? Math.max(...feet) : hips + HIP_HEIGHT;
    room.floor = room.seen ? room.floor + (floor - room.floor) * (1 - Math.exp(-dt / 1.5)) : floor;
    room.seen = true;
    far = Math.max(far, p.depth);
    wide = Math.max(wide, Math.abs(at(11).x), Math.abs(at(12).x));
  }
  room.ceiling = room.floor - ROOM_HEIGHT;
  room.back = Math.max(MIN_BACK, far + BEHIND);
  room.side = Math.max(MIN_SIDE, wide + BESIDE);
}

// Where a ray from o along d (camera space) first meets the room: { at, normal (facing into the
// room), surface } with surface one of floor, ceiling, back, left, right, glass.
export function hitRoom(o, d) {
  const { floor, ceiling, back, side } = room;
  const planes = [
    ["floor", "y", floor, { x: 0, y: -1, z: 0 }], ["ceiling", "y", ceiling, { x: 0, y: 1, z: 0 }],
    ["back", "z", back, { x: 0, y: 0, z: -1 }], ["glass", "z", GLASS, { x: 0, y: 0, z: 1 }],
    ["left", "x", -side, { x: 1, y: 0, z: 0 }], ["right", "x", side, { x: -1, y: 0, z: 0 }],
  ];
  const e = 1e-3;
  let best = null;
  for (const [surface, axis, value, normal] of planes) {
    if (Math.abs(d[axis]) < 1e-6) continue;
    const t = (value - o[axis]) / d[axis];
    if (t <= 0.01 || (best && t >= best.t)) continue;
    const at = { x: o.x + d.x * t, y: o.y + d.y * t, z: o.z + d.z * t };
    const inside = surface === "glass" || (at.x >= -side - e && at.x <= side + e && at.y >= ceiling - e &&
      at.y <= floor + e && at.z >= GLASS - e && at.z <= back + e);
    if (inside) best = { t, at, normal, surface };
  }
  return best;
}
