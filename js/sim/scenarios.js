// Scripted scenarios for the simulator. Each one is a little play: who's in frame and how they
// move over time, what the mic hears, and (for the test suite) what should be true at given times.
//
//   people:    functions t -> pose (see body.js), or null while that person is out of frame
//   mic:       t -> level in dBFS
//   settings:  overrides on top of the defaults
//   checks:    [time, description, w => true | "why it failed"]; w is the world (see tests/runner.js)
//   snapshots: [time, name] frames saved as PNGs by the test runner
//
// Open the app with ?sim=<name> to watch one live without a camera.
import { CURL } from "./body.js";

const PI = Math.PI;
const QUIET = t => -62 + 1.5 * Math.sin(t * 13);   // room tone
const TALK = t => -40 + 3 * Math.sin(t * 9);       // talking: well above the room, below a yell
const YELL = t => -14 + 2 * Math.sin(t * 11);

// Value of a timeline [[t0, v0], [t1, v1], ...] at time t (steps, no blending).
const at = (t, steps) => steps.reduce((v, [t0, x]) => (t >= t0 ? x : v), steps[0][1]);

// Arms
const DOWN = { upper: [0.15, 0], fore: [0.12, 0.3], hand: { palm: "in", curl: CURL.relaxed } };
const FLEX = { upper: [PI / 2, 0], fore: [PI * 0.95, 0], hand: { palm: "in", curl: CURL.fist } };
// straight out to the side with the hand bent back, palm facing outward: the repulsor stance
const AIM = curl => ({ upper: [PI / 2, 0], fore: [PI / 2, 0], hand: { dir: [PI, 0], palm: "out", curl } });
// upper arm hanging forward, forearm across the body: the hand flat on the chest
const ON_CHEST = { upper: [0.1, 0.6], fore: [-2.07, 0], hand: { palm: "back", curl: CURL.open } };
// a fist on top of the head (gripping a mask), and a fist pulled down past the chest
const GRIP = { upper: [PI, 0.2], fore: [-PI / 2, 0.2], hand: { palm: "down", curl: CURL.fist } };
const PULLED = { upper: [0.2, 0.3], fore: [0.3, 0.6], hand: { palm: "in", curl: CURL.fist } };
const WEB_SIGN = { upper: [1.3, 0.3], fore: [1.5, 0.4], hand: { palm: "front", curl: CURL.web } };

// Framings: whole body for two players, close-up for the suit.
const body = (o = {}) => ({ x: 0.5, y: 0.85, height: 2.4, left: DOWN, right: DOWN, ...o });
const closeUp = (o = {}) => body({ y: 1.0, height: 1.4, ...o });
// Two players: P1 on the screen's left (the image is mirrored, so x = 0.7), P2 on the right.
const P1 = o => body({ x: 0.7, ...o });
const P2 = o => body({ x: 0.3, ...o });

// Check helpers: pass with true, fail with a reason.
const eq = (got, want, what = "") => got === want || `${what} expected ${want}, got ${got}`;
const gt = (got, than, what = "") => got > than || `${what} expected > ${than}, got ${fmt(got)}`;
const lt = (got, than, what = "") => got < than || `${what} expected < ${than}, got ${fmt(got)}`;
const fmt = v => typeof v === "number" ? Math.round(v * 100) / 100 : v;
const all = (...results) => results.find(r => r !== true) ?? true;

// The newest web landed on one of these surfaces (and is behind its shooter, or not, if given).
function newestWeb(w, surfaces, behind) {
  const web = w.webs[w.webs.length - 1];
  if (!web) return "no webs";
  return all(surfaces.includes(web.surface) || `landed on the ${web.surface}`,
    behind === undefined || web.behind === behind || `behind: ${web.behind}`);
}

// Average frame-to-frame movement (px) of a set of points sampled over time.
const jitter = { suit: [], raw: [] };
function spread(samples) {
  let sum = 0, n = 0;
  for (let i = 1; i < samples.length; i++) {
    samples[i].forEach((p, k) => { sum += Math.hypot(p.x - samples[i - 1][k].x, p.y - samples[i - 1][k].y); n++; });
  }
  return n ? sum / n : 0;
}

// The suit should sit on the body: the model's head on the person's head, its shoulders, elbows and
// wrists on theirs (mirrored: the model's left follows the person's right), the reactor on the chest.
function suitFits(w, num) {
  const suit = w.armor.suits.find(s => s.num === num), p = w.p(num);
  if (!suit || !p) return `no suit for P${num}`;
  if (!suit.body) return `no suit model: ${w.armor.status.error}`;
  const L = p.lm, J = suit.body.joints, sw = Math.hypot(L[11].x - L[12].x, L[11].y - L[12].y);
  const head = { x: (J.Head.x + J.HeadTop_End.x) / 2, y: (J.Head.y + J.HeadTop_End.y) / 2 };
  const ear = { x: (L[7].x + L[8].x) / 2, y: (L[7].y + L[8].y) / 2 };
  const near = (what, a, b, tol) => {
    const d = Math.hypot(a.x - b.x, a.y - b.y);
    return d < tol * sw || `${what} ${fmt(d)}px off (allowed ${fmt(tol * sw)})`;
  };
  const shoulderY = (L[11].y + L[12].y) / 2, hipY = (L[23].y + L[24].y) / 2;
  return all(
    suit.body.visible || "suit hidden",
    near("head", head, ear, 0.3),
    near("left shoulder", J.LeftArm, L[12], 0.08), near("right shoulder", J.RightArm, L[11], 0.08),
    near("left elbow", J.LeftForeArm, L[14], 0.08), near("right elbow", J.RightForeArm, L[13], 0.08),
    near("left wrist", J.LeftHand, L[16], 0.08), near("right wrist", J.RightHand, L[15], 0.08),
    suit.wear !== "ironman" || suit.reactor.visible || "reactor hidden",
    suit.wear !== "ironman" || (suit.reactor.y > shoulderY && suit.reactor.y < hipY) || "reactor not on the chest",
  );
}

export const scenarios = [
  {
    name: "idle",
    about: "One person standing still: tracked, nothing fires",
    duration: 3,
    people: [() => body()],
    checks: [
      [2.9, "one person, two hands", w => all(eq(w.persons.length, 1, "people"), eq(w.hands.length, 2, "hands"))],
      [2.9, "no blasts, beams, webs or power-ups", w => all(eq(w.shockwaves.length, 0, "shockwaves"),
        eq(w.hands.filter(h => h.beam > 0.05).length, 0, "beams"), eq(w.webs.length, 0, "webs"), eq(w.p(1).stage, 0, "stage"))],
      [2.9, "full health", w => eq(w.p(1).health, 100)],
    ],
    snapshots: [[2.5, "idle"]],
  },
  {
    name: "flex-silent",
    about: "Flexing without a sound doesn't power up, and the flexed fists don't charge repulsors",
    duration: 4,
    people: [() => body({ left: FLEX, right: FLEX })],
    mic: QUIET,
    checks: [
      [3.5, "flex detected", w => w.p(1).flexing || "not flexing"],
      [3.5, "no charge without a yell", w => all(eq(w.p(1).stage, 0, "stage"), lt(w.p(1).flexCharge, 0.05, "charge"))],
      [3.5, "fists don't charge while flexing", w => eq(w.hands.filter(h => h.charge > 0).length, 0, "charging hands")],
    ],
    snapshots: [[3.5, "flex-silent"]],
  },
  {
    name: "flex-talking",
    about: "Talking while flexing isn't loud enough to count as a yell",
    duration: 4.5,
    people: [() => body({ left: FLEX, right: FLEX })],
    mic: t => (t < 0.5 ? QUIET(t) : TALK(t)),
    checks: [[4.4, "no power-up from talking", w => all(eq(w.p(1).stage, 0, "stage"), lt(w.p(1).flexCharge, 0.05, "charge"))]],
  },
  {
    name: "flex-yell",
    about: "Flex and yell: stage 1 after ~2 s, max health up to 150",
    duration: 3.5,
    people: [() => body({ left: FLEX, right: FLEX })],
    mic: t => (t < 0.5 ? QUIET(t) : YELL(t)),
    checks: [
      [0.45, "nothing before the yell", w => eq(w.p(1).stage, 0, "stage")],
      [1.5, "charging while yelling", w => gt(w.p(1).flexCharge, 0.3, "charge")],
      [3.2, "stage 1", w => eq(w.p(1).stage, 1, "stage")],
      [3.2, "health 150", w => eq(w.p(1).health, 150, "health")],
    ],
    snapshots: [[1.6, "charging"], [3.4, "powered"]],
  },
  {
    name: "three-stages",
    about: "Flex+yell, release, three times: stage 3 and 250 max health",
    duration: 9.5,
    people: [t => body(at(t, [[0, { left: FLEX, right: FLEX }], [2.8, {}], [3.3, { left: FLEX, right: FLEX }],
      [5.9, {}], [6.4, { left: FLEX, right: FLEX }]]))],
    mic: t => (t < 0.4 ? QUIET(t) : YELL(t)),
    checks: [
      [2.7, "stage 1", w => eq(w.p(1).stage, 1, "stage")],
      [5.8, "stage 2", w => eq(w.p(1).stage, 2, "stage")],
      [9.3, "stage 3 at 250 health", w => all(eq(w.p(1).stage, 3, "stage"), eq(w.p(1).health, 250, "health"))],
    ],
    snapshots: [[9.4, "ultra-instinct"]],
  },
  {
    name: "repulsor-blast",
    about: "P1 charges a fist and snaps it open: the blast hits P2",
    duration: 3,
    people: [
      t => P1({ right: AIM(at(t, [[0, CURL.relaxed], [0.3, CURL.fist], [1.6, CURL.open], [1.8, CURL.relaxed]])) }),
      () => P2(),
    ],
    checks: [
      [1.5, "fist fully charged", w => gt(Math.max(...w.hands.map(h => h.charge)), 0.95, "charge")],
      [1.75, "blast fired", w => gt(w.shockwaves.length, 0, "shockwaves")],
      [2.9, "P2 took blast damage", w => lt(w.p(2).health, 100, "P2 health")],
      [2.9, "P1 unhurt", w => eq(w.p(1).health, 100, "P1 health")],
    ],
    snapshots: [[1.7, "blast"]],
  },
  {
    name: "repulsor-beam",
    about: "P1 holds the palm open toward P2: the beam locks on and drains health",
    duration: 3.2,
    people: [t => P1({ right: AIM(t < 0.3 ? CURL.relaxed : CURL.open) }), () => P2()],
    checks: [
      [1.5, "beam on P2", w => w.hands.some(h => h.beam > 0.5 && h.geom?.target === w.p(2)) || "beam not on P2"],
      [3.1, "P2 drained", w => lt(w.p(2).health, 60, "P2 health")],
    ],
    snapshots: [[2.0, "beam"]],
  },
  {
    name: "ko-resets-aura",
    about: "A powered-up P2 gets K.O.'d: the aura and its bonus health are lost, respawn at 100",
    duration: 12,
    people: [
      t => P1({ right: AIM(t >= 3 && t < 8.5 ? CURL.open : CURL.relaxed) }),
      t => P2(t < 2.8 ? { left: FLEX, right: FLEX } : {}),
    ],
    mic: t => (t < 0.4 ? QUIET(t) : t < 2.8 ? YELL(t) : QUIET(t)),
    checks: [
      [2.7, "P2 powered up to 150", w => all(eq(w.p(2).stage, 1, "stage"), eq(w.p(2).health, 150, "health"))],
      [8.6, "P2 K.O.'d and back to stage 0", w => all(gt(w.p(2).ko, 0, "ko"), eq(w.p(2).stage, 0, "stage"))],
      [11.9, "P2 respawned at 100", w => all(eq(w.p(2).ko, 0, "ko"), eq(w.p(2).health, 100, "health"))],
    ],
    snapshots: [[4.5, "beam-vs-aura"]],
  },
  {
    name: "web",
    about: "Holding the web-shooter sign fires webs",
    duration: 2,
    people: [t => body({ right: t < 0.3 ? DOWN : WEB_SIGN })],
    checks: [[1.9, "webs fired", w => gt(w.webs.length, 1, "webs")]],
    snapshots: [[1.9, "webs"]],
  },
  {
    name: "web-room",
    about: "Webs land where they'd hit the room: a side wall, the floor behind, the back wall (behind the person), the camera's glass",
    duration: 4,
    paintFrames: true,
    // the web-shooter sign held up, the hand aimed: [out, toward the camera]
    people: [t => body({ right: { upper: [1.3, 0.3], fore: [1.5, 0.4],
      hand: { dir: at(t, [[0, [PI / 2, 0]], [1, [0.25, -0.6]], [2, [PI * 0.75, -1.0]], [3, [PI / 2, 1.45]]]), palm: "front", curl: CURL.web } } })],
    checks: [
      [0.95, "aimed sideways: a side wall", w => newestWeb(w, ["left", "right"])],
      [1.95, "aimed down and away: the floor, behind them", w => newestWeb(w, ["floor"], true)],
      [2.95, "aimed away: the back wall, behind them", w => newestWeb(w, ["back"], true)],
      [3.95, "aimed at the camera: the glass", w => newestWeb(w, ["glass"], false)],
    ],
    snapshots: [[0.95, "side-wall"], [1.95, "floor"], [2.95, "back-wall"], [3.95, "glass"]],
  },
  {
    name: "spin",
    about: "A full 360 turn starts the Spinjitzu tornado",
    duration: 3,
    people: [t => body({ yaw: t < 0.5 ? 0 : t < 2 ? (t - 0.5) / 1.5 * 2 * PI : 0, hideHands: t > 0.5 && t < 2 })],
    checks: [
      [0.45, "no tornado yet", w => eq(w.p(1).spinOn, false, "spinOn")],
      [2.6, "tornado on", w => eq(w.p(1).spinOn, true, "spinOn")],
    ],
    snapshots: [[1.0, "side-on"], [1.25, "turned-away"], [2.6, "tornado"]],
  },
  {
    name: "throw",
    about: "A fast arm swing throws an elemental ball",
    duration: 2,
    people: [t => body({ right: { ...DOWN, upper: [t < 1 ? 0.3 : 0.3 + Math.min(1, (t - 1) / 0.2) * 1.9, 0] } })],
    checks: [
      [0.95, "nothing thrown while still", w => eq(w.balls.length, 0, "balls")],
      [1.4, "ball thrown", w => gt(w.balls.length, 0, "balls")],
    ],
  },
  {
    name: "armor-closeup",
    about: "The suit model sits on the body with its gauntlets on the tracked hands, and its head turns with theirs",
    duration: 3,
    people: [t => closeUp({ left: FLEX, right: FLEX, headYaw: t < 1.5 ? 0 : 0.6 })],
    checks: [
      [1.4, "suit on the body", w => suitFits(w, 1)],
      [1.4, "head faces the camera", w => gt(w.armor.suits[0].body.headForward[2], 0.85, "forward z")],
      [1.4, "both hands in the gauntlets", w => eq(w.armor.hands.filter(h => h.onModel && !h.plate.visible).length, 2, "gauntlet hands")],
      [1.4, "gauntlets on the tracked wrists", w => all(...["Left", "Right"].map(s => {
        const g = w.armor.suits[0].body, at = g.joints[`${s}Hand`];
        const near = w.hands.some(h => Math.hypot(h.pts[0].x - at.x, h.pts[0].y - at.y) < 25);
        return (g.gauntlet[s] && near) || `${s} gauntlet ${g.gauntlet[s] ? `at (${at.x | 0}, ${at.y | 0}), off the wrists` : "not posed"}`;
      }))],
      [1.4, "suit covers the stomach", w => {
        const j = w.armor.suits[0].body.joints;
        return w.drawsArmor({ x: (j.Spine2.x + j.Hips.x) / 2, y: (j.Spine2.y + j.Hips.y) / 2 });
      }],
      [2.9, "head turned to screen left with the person's", w => lt(w.armor.suits[0].body.headForward[0], -0.3, "forward x")],
    ],
    snapshots: [[1.4, "suit-front"], [2.9, "suit-head-turned"]],
  },
  {
    name: "jitter",
    about: "Standing still with noisy tracking: the suit holds steady instead of shaking with the landmarks",
    duration: 3,
    people: [() => closeUp({ left: FLEX, right: FLEX, noise: { xy: 0.002, z: 0.03 } })],
    checks: [
      // sample the suit's joints and the raw landmarks they follow over the last second and a half
      ...Array.from({ length: 45 }, (_, i) => [1.5 + i / 30, "sample", w => {
        const J = w.armor.suits[0].body.joints, L = w.p(1).lm;
        jitter.suit.push([J.LeftForeArm, J.RightForeArm, J.LeftHand, J.RightHand, J.Head]);
        jitter.raw.push([L[14], L[13], L[16], L[15], { x: (L[7].x + L[8].x) / 2, y: (L[7].y + L[8].y) / 2 }]);
        return true;
      }]),
      [2.99, "suit at most a third as shaky as the landmarks", w => {
        const s = spread(jitter.suit), r = spread(jitter.raw);
        jitter.suit = []; jitter.raw = [];
        return s < r / 3 || `suit moves ${fmt(s)}px per frame vs landmarks ${fmt(r)}px`;
      }],
    ],
    snapshots: [[2.5, "still"]],
  },
  {
    name: "nanotech",
    about: "A hand held on the chest flows the suit out from the reactor; doing it again takes it back in",
    duration: 6.2,
    settings: { nanotech: true },
    people: [t => closeUp({ right: (t > 0.5 && t < 1.4) || (t > 3.8 && t < 4.7) ? ON_CHEST : DOWN })],
    checks: [
      [0.45, "no suit yet", w => all(eq(w.p(1).nano.level, 0, "level"), eq(w.armor.suits[0]?.body.visible ?? false, false, "suit shown"))],
      [1.0, "hand on the chest isn't enough on its own", w => eq(w.p(1).nano.level, 0, "level")],
      [1.6, "suit forming", w => all(gt(w.p(1).nano.level, 0.1, "level"), lt(w.p(1).nano.level, 0.6, "level"))],
      [1.6, "formed from the chest out: chest covered, hands not yet", w => {
        const j = w.armor.suits[0].body.joints;
        return all(w.drawsArmor({ x: (j.Spine2.x + j.Hips.x) / 2, y: (j.Spine2.y + j.Hips.y) / 2 }),
          w.drawsArmor(j.LeftHand) !== true || "hand already covered");
      }],
      [3.6, "suit fully on", w => all(eq(w.p(1).nano.level, 1, "level"), suitFits(w, 1))],
      [3.6, "one arc reactor", w => w.armor.suits[0].reactor.own || "the model's reactor isn't the one shown"],
      [6.1, "suit back in the reactor", w => all(eq(w.p(1).nano.level, 0, "level"), eq(w.armor.suits[0].body.visible, false, "suit shown"))],
    ],
    snapshots: [[1.0, "hand-on-chest"], [1.5, "forming-start"], [2.0, "forming"], [2.5, "forming-late"], [2.9, "helmet-closing"], [3.6, "formed"], [5.0, "retracting"]],
  },
  {
    name: "cloak",
    about: "The room is learned while nobody is in shot, then painted over the body so only the suit shows",
    duration: 2.5,
    people: [t => (t < 0.5 ? null : body({ right: t < 1.5 ? DOWN : AIM(CURL.open) }))],
    checks: [
      [0.4, "nobody to cloak yet", w => eq(w.cloak, 0, "covered share")],
      [1.4, "body painted out", w => gt(w.cloak, 0.95, "covered share")],
    ],
    snapshots: [[1.4, "cloaked"], [2.4, "cloaked-aim"]],
  },
  {
    name: "cloak-ghost",
    about: "A glitched mask for a few frames doesn't teach the cloak the person as room (it would paint them back as a ghost)",
    duration: 2.5,
    paintFrames: true,
    people: [t => body({ maskGlitch: t >= 1 && t < 1.17 })],
    checks: [[2.4, "none of the body learned as room", w => lt(w.cloak, 0.05, "body share learned")]],
  },
  {
    name: "spiderman-suit",
    about: "With SPIDER-MAN switched on last, people wear the Spider-Man suit: straight away, no reactor, no Iron Man hands",
    duration: 2,
    settings: { prefer: "spiderman" },
    paintFrames: true,
    people: [t => closeUp({ left: FLEX, right: t < 1 ? FLEX : AIM(CURL.web) })],
    checks: [
      [0.5, "Spider-Man suit on, straight away", w => all(eq(w.armor.suits[0]?.wear, "spiderman", "suit"),
        eq(w.armor.status.spiderman, "assets/models/spiderman.glb", "model"), suitFits(w, 1))],
      [0.5, "no arc reactor", w => eq(w.armor.suits[0].reactor.visible, false, "reactor shown")],
      [0.5, "his own hands on the tracked hands, no Iron Man hand armour", w => all(
        eq(w.armor.hands.filter(h => h.onModel).length, 2, "hands in the suit"),
        eq(w.armor.hands.filter(h => h.plate.visible).length, 0, "Iron Man hand plates"))],
      [0.5, "suit covers the stomach", w => {
        const j = w.armor.suits[0].body.joints;
        return w.drawsArmor({ x: (j.Spine2.x + j.Hips.x) / 2, y: (j.Spine2.y + j.Hips.y) / 2 });
      }],
    ],
    snapshots: [[0.5, "spidey-flex"], [1.9, "spidey-web"]],
  },
  {
    name: "spidey-eyes",
    about: "Spider-Man's lenses follow the face: squint and frown, a wink (mirrored onto his other eye), surprise",
    duration: 4,
    settings: { prefer: "spiderman" },
    paintFrames: true,
    people: [t => closeUp({ face: at(t, [[0, { open: [1, 1] }], [1, { open: [0.35, 0.35], frown: 0.8 }],
      [2, { open: [0.05, 1] }], [3, { open: [1.3, 1.3], raise: 0.9 }]]) })],
    checks: [
      [0.9, "neutral: lenses as modelled", w => {
        const e = w.armor.suits[0]?.body?.eyes;
        return !e ? "no lens expression" : all(lt(Math.abs(e.open[0] - 1), 0.1, "left open - 1"), lt(Math.abs(e.open[1] - 1), 0.1, "right open - 1"), lt(e.frown, 0.1, "frown"));
      }],
      [1.9, "squint and frown", w => {
        const e = w.armor.suits[0].body.eyes;
        return all(lt(e.open[0], 0.5, "left open"), lt(e.open[1], 0.5, "right open"), gt(e.frown, 0.6, "frown"));
      }],
      [2.9, "a wink with their right eye closes his left lens (the mirror)", w => {
        const e = w.armor.suits[0].body.eyes;
        return all(lt(e.open[0], 0.3, "his left lens open"), gt(e.open[1], 0.8, "his right lens open"));
      }],
      [3.9, "surprise: wide eyes, raised brows", w => {
        const e = w.armor.suits[0].body.eyes;
        return all(gt(e.open[0], 1.08, "left open"), gt(e.open[1], 1.08, "right open"), gt(e.raise, 0.7, "raised"));
      }],
    ],
    snapshots: [[0.9, "neutral"], [1.9, "squint"], [2.9, "wink"], [3.9, "surprise"]],
  },
  {
    name: "unmask",
    about: "Spider-Man pulls his mask off (a fist on the head, pulled down past the chest) and back on (the reverse); a flex isn't a pull",
    duration: 5.5,
    settings: { prefer: "spiderman" },
    paintFrames: true,
    people: [t => body(at(t, [[0, {}], [0.4, { left: FLEX, right: FLEX }], [1.3, { left: PULLED, right: PULLED }],
      [2.0, { right: GRIP }], [2.6, { right: PULLED }], [4.0, { right: GRIP }], [4.8, {}]]))],
    checks: [
      [1.95, "flexing and dropping the fists leaves the mask on", w => eq(w.p(1).unmask.level, 1, "mask")],
      [3.5, "gripped and pulled down: mask off", w => eq(w.p(1).unmask.level, 0, "mask")],
      [3.5, "the real head shows", w => {
        const J = w.armor.suits[0].body.joints;
        return w.drawsArmor({ x: (J.Head.x + J.HeadTop_End.x) / 2, y: (J.Head.y + J.HeadTop_End.y) / 2 }) !== true || "the mask still covers the head";
      }],
      [3.5, "the body's still in the suit", w => {
        const J = w.armor.suits[0].body.joints;
        return w.drawsArmor({ x: (J.Spine2.x + J.Hips.x) / 2, y: (J.Spine2.y + J.Hips.y) / 2 });
      }],
      [5.4, "pulled back up: mask on", w => eq(w.p(1).unmask.level, 1, "mask")],
    ],
    snapshots: [[2.4, "gripping"], [3.5, "unmasked"], [5.4, "masked-again"]],
  },
  {
    name: "armor-off",
    about: "With the suit switched off, nothing is rigged",
    duration: 1,
    settings: { armor: false, spiderman: false },
    people: [() => closeUp()],
    checks: [[0.9, "no armour", w => eq(w.armor.suits.length + w.armor.hands.length, 0, "rigs")]],
  },
  {
    name: "duel",
    about: "Showcase: an armoured P1 blasts and beams while P2 powers up",
    duration: 8,
    people: [
      t => P1({ right: AIM(at(t, [[0, CURL.relaxed], [0.4, CURL.fist], [1.6, CURL.open], [1.8, CURL.relaxed],
        [3, CURL.open], [5.2, CURL.relaxed]])) }),
      t => P2(at(t, [[0, {}], [0.5, { left: FLEX, right: FLEX }], [3.1, {}], [3.6, { left: FLEX, right: FLEX }], [6.3, {}]])),
    ],
    mic: t => (t < 0.4 || (t > 3.1 && t < 3.6) ? QUIET(t) : YELL(t)),
    checks: [
      [6.5, "P2 reached stage 2", w => eq(w.p(2).stage, 2, "stage")],
      [6.5, "P2 took damage", w => lt(w.p(2).health, 200, "P2 health")],
    ],
    snapshots: [[1.7, "duel-blast"], [4.5, "duel-beam"]],
  },
];
