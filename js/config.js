// Tunables and constant tables shared across the app.

export const POSE_WIDTH = 320;     // pose runs on a downscaled frame; masks come back at this size
export const PLAYER_COLORS = [[90, 220, 255], [255, 170, 60], [120, 255, 140], [255, 100, 220]];

export const MAX_HEALTH = 100;
export const STAGE_HEALTH = 50;    // extra max health per Goku aura stage (100 → 150 → 200 → 250)
export const MAX_DAMAGE = 45;      // full-charge blast at point-blank range
export const KO_SECONDS = 3;
export const BEAM_DPS = 35;        // health per second while the beam is on someone

// Landmark indices: 0 wrist, [pip, tip] per non-thumb finger below.
export const FINGERS = [
  { pip: 6,  tip: 8  },  // index
  { pip: 10, tip: 12 },  // middle
  { pip: 14, tip: 16 },  // ring
  { pip: 18, tip: 20 },  // pinky
];
export const POSE_CONNECTIONS = [
  [11,12],[11,13],[13,15],[12,14],[14,16],[11,23],[12,24],[23,24],[23,25],[25,27],[24,26],[26,28],
  [27,29],[29,31],[27,31],[28,30],[30,32],[28,32],[15,17],[15,19],[15,21],[17,19],[16,18],[16,20],
  [16,22],[18,20],[0,1],[1,2],[2,3],[3,7],[0,4],[4,5],[5,6],[6,8],[9,10],
];
export const CONNECTIONS = [
  [0,1],[1,2],[2,3],[3,4],[0,5],[5,6],[6,7],[7,8],[5,9],[9,10],[10,11],[11,12],
  [9,13],[13,14],[14,15],[15,16],[13,17],[17,18],[18,19],[19,20],[0,17],
];

export const DEFAULT_SETTINGS = { ironman: true, armor: true, cloak: true, goku: true, spinjitzu: true, spinColor: "255,70,40",
  spiderman: true, video: true, skeleton: true, silhouettes: true, muted: false };
export const settings = { ...DEFAULT_SETTINGS };
