# 2026-11-14 (overnight, no headset): robot roster, boss, room spawning, Valkyr models, Tempest and Terra

The user asked for this before leaving for about 8 hours. Nothing was tried in the headset; everything was
checked with unit tests, PlayMode tests on the PC and PC renders. The APK is built (`Builds/RepulsorTest.apk`)
but not installed: no headset was plugged in.

## Robots

- Seven kinds, each with its own CC0 model (`Core/EnemyKinds.cs`, `Game/Enemy.cs`):

  | Kind | Model | Fights by |
  |---|---|---|
  | Gunner | Stan | walking in, lasers |
  | Drone | EyeDrone | weaving at head height, quick weak lasers |
  | Brawler | Classic robot | running in, a 0.6 s wind-up (fists glow), then a punch if you're still in reach |
  | Rocketeer | George | missiles that arc in and blow up robots too; they can be shot down |
  | Dasher | Leela | dashing sideways every 2 s, double shots |
  | Sniper | Mike | keeping 3.3 m away, a 1.5 s aim that follows you for 0.9 s, then a 14-damage beam |
  | Tank | QuadShell | slow, 90 HP, a fan of three lasers |

- Waves add kinds as they go (`WavePlan`): wave 1 gunners and drones, brawlers from 2, dashers and rocketeers
  from 3, snipers and tanks from 4.
- **Lasers instead of red dots** (`EnemyAttacks.cs`): a thin red aim line that flickers faster as it charges
  (it shows exactly where the beam goes, so you can step out), then the same beam as the player's hand laser,
  in red, for 0.3 s. Walls and a raised shield stop it; the shield flashes.
- **Hitboxes** are standing capsules matched to each model. The old ground robot was a 0.28 m ball at 0.65 m:
  it covered 0.37-0.93 m of a 1.3 m model, so shots at the head (where you naturally aim, at the eyes) missed.
  All attacks (blasts, lasers, sword, flames, webs, explosions, aim assist) now test the capsule (`HitTest`).

## Spawning

- `RoomMap`: open floor, far from every player. Sources, best first:
  1. Space Setup's floor outline. The OpenXR **plane feature was off**; it's on now, and `RoomSense` adds an
     `ARPlaneManager`.
  2. A depth scan: six depth rays a frame mark floor cells open (or blocked where they hit something standing).
     It runs all match, and on a new **SCAN ROOM** screen from the title, which shows the found floor as dots.
- If the map knows too little, the old "ahead, with the most room" choice is used. The log says which:
  `[Arena] Robot spawned 4.1 m away (depth scan)`.

## Boss

- The sci-fi kit's Trilobite (Quaternius, CC0, same style as the robots), 2.3 m tall, 700 HP, a health bar
  across the top of your view.
- Routine (`Boss.cs`): walk in, then a fan of 7 lasers and a second fan aimed into its gaps (three fans when
  under a third of its health), then 5 tracking strikes (a red ring on the floor chases where you were 0.7 s
  ago, at walking speed, and a beam slams down on it every 1.1 s), then 4 missiles, and under 60% it calls two
  helpers.
- It's the last wave of a solo match, or straight away from **BOSS FIGHT (solo)** in the mode menu.

## Valkyr

- Crossbow (KayKit, CC0) in each finger-gun hand. As it reloads a bolt slides onto it and its glow grows; when
  ready it flares. The shot flies as the bolt.
- Longbow (Quaternius, CC0) in the front fist, the string drawn to the back hand, an arrow nocked. The arrowhead
  glows and a ring spins up with the draw. The shot flies as the arrow.
- The X shield is now the Quaternius knight shield, with a faint glow.

## Tempest (Elementalist) and Terra (Earth), from the plan

- Tempest: palm push = gust (knockback), both hands out flat = chain lightning (5 s, then a 3 s rest), circle
  both hands then push = tornado that pulls enemies together. Wind shield, whirling wind aura at 2 and 3.
- Terra: fist then open = rock orb (Kenney rock, procedural glowing cracks), rock chunks on the backs of your
  hands; uppercut from the waist = three growing rock spikes; at 3, orbs stick and a snap blows them up. Rock
  shield, crater ring at 2, orbiting gold-flecked rocks at 3.
- New detectors are in `Gestures/ElementGestures.cs`, all unit-tested on made-up hands, none tried on real ones.

## Shield

No code bug found: the 11/8 logs show the X detected and the shield coming up. What probably felt broken: it
drained in 3 s and then stayed broken for 6 s with only small HUD text. Now: 5 s, 4 s cooldown, recharges
faster, a looser X with a longer dropout grace, a burst and a SHIELD BROKEN message when it breaks.
**Ask the user what exactly broke.**

## Checks

- EditMode 163/163, PlayMode 9/9 (every enemy kind fighting, a 20 s boss fight through its whole routine,
  every elemental and archer effect, every character through a wave).
- PC renders: `Logs/play_roster.png`, `Logs/play_boss_*.png`, `Logs/play_effects_*.png` (from
  `Tests/PlayMode/CaptureTests.cs`), `Logs/preview_enemies.png`, `Logs/preview_prop*.png`.

## Tight aura (morning, user back)

The user asked for the plan's "tight aura": the segmentation mask, slightly larger, glowing, and fine if it lags
(afterimages). It shows after a powerup and right after a transformation; when to make it permanent is open
(for now Technoman at level 3, as the plan says). `Core/GlowTimer.cs`, `Game/TightAura.cs`.

- **The mask was already there**: the BlazePose landmark model has a 256x256 body mask output (`Identity_2`)
  that was never read. `BodyTracker` reads it when asked; `PersonDetector` places it as a square at the hips'
  distance, facing the camera as it was. `BodyGlow` draws it grown and glowing (`BodyGlow.shader`); each new
  mask stays where it was seen and the old ones fade, which makes the afterimages.
- It's drawn on whoever the camera sees: the other player while their glow is up (a new flag in the player
  message), or, playing alone, you in a mirror. The person tracker only runs while it's needed.
- **On yourself** the headset can't see your body, so `HandGlow` outlines your hands and forearms from the hand
  joints. The first try (a glowing shell per bone) showed every bone's outline; now each pixel finds how close
  its line of sight passes to the bones (`TightGlow.shader`), so the hand gets one outline. Moving hands leave
  afterimages. This replaces Technoman level 3's blob on each hand.
- Checked on the PC with the saved mirror pictures in `Logs/frames` (`TightAuraCaptureTests`, pictures in
  `Logs/play_tight_*.png`): the person was found in all six pictures and the glow lines up with them.
- EditMode 169/169, PlayMode 12/12, APK built (not installed, no headset).
