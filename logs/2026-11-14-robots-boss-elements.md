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

## Character select redone (late morning)

The user asked for: a symbol per character in boxes on the left; top right (quadrant 1) a default 3D model doing
the level 1/2/3 moves, cycling, with manual stepping; under it the name and the ability list, scrolling.

- Model: the user picked the Quaternius mannequin (Universal Animation Library, CC0) over the Library 2 female
  mannequin and KayKit's Prototype Pete. Library 2 turned out to use a different skeleton (UE-style `pelvis`,
  `hand_l` against Library 1's Rigify `DEF-` bones), so only Library 1's 46 clips are used.
- Icons: game-icons.net by Lorc (CC BY 3.0, credited), chosen by the user "for now".
- `CharacterShowcase`: a 12 cm mannequin in the player's colour in front of the screen, with a robot to hit and
  a small version of each move's effect (24 moves, 3 per character). `Arena.Showcase.cs` places it, runs the
  level arrows and scrolls the ability list to the level shown unless you've scrolled in the last 6 s.
- Pitfalls hit: a saved Animation keeps clips but not the names given with AddClip (they come back as
  `Rig|Punch_Cross`), and RemoveClip in the editor left null entries; `AnimationUtility.SetAnimationClips` and
  short names given at runtime fixed it. The game's laser beam and the robot's eye glows are sized for the room,
  so the stage draws its own beam and shrinks the eyes.
- PC renders: `Logs/play_select_*.png`. EditMode 169/169, PlayMode 13/13, APK built (no headset to install).

## Real webs for Webline (midday)

The user asked for the web shots to be actual webs, from a model found online. Candidates: Quaternius "Cobweb"
(CC0, poly.pizza), Google Poly "Spider web" (CC BY, strung between plant stems), OpenGameArt web textures (2D).
Used the Quaternius cobweb: it's a corner cobweb (curved), so it's squashed to a quarter of its depth and always
shown front-on to the shooter. The shot hides its energy ball and flies the web, opening and spinning; on a hit
the web sticks to the robot (through the shot's OnHit) and fades after 3 s. Black at level 3. The select screen's
web moves use it too. `Game/WebStrand.cs`, `Shaders/WebModel.shader`, `Resources/Props/Web` (PropModels).
PC renders `Logs/play_web_*.png`. EditMode 169/169, PlayMode 14/14, APK built.


## Evening notes from the headset (first install of the day's build)

The headset had a build signed with another key, so the old app was uninstalled; its PlayerPrefs (best scores,
picked variants) were backed up first and restored with `run-as`. The user's notes after playing:

- **Tight aura lags: removed.** Reverted the tight-aura commit (GlowTimer, TightAura, both shaders, the mask
  readout, the net Glow flag); Technoman level 3's old hand glow is back.
- **Damage flash showed edges: now a sphere.** `HurtFlash` draws the inside of a 0.6 m sphere on the head with
  `Repulsor/Hurt Vignette` (clear within 18 degrees, full red from 55), so it runs past the view on every side.
- **Tornado needs real swirling.** `SwirlDetector` now tracks each hand's direction of travel turning steadily
  one way round (waves and shakes reverse and cancel out); one circle arms it, two charge it. Two new tests.
- **Select screen: no robot, accurate poses, hand signs.** The mannequin plays its idle clip and the arms are
  posed over it each LateUpdate (two-bone reach to keyed wrist positions, hands turned and fingers shaped by
  `HandRig`) from `ShowcasePoses`, written from the detectors' rules. Dragon fist and the laser sword keep their
  clips. A HAND SIGN box shows the move's hand shape on the XR Hands sample hand (Unity, Package Distribution
  License; same joint names as the game's), from behind for palm-out shapes, the side for pointing ones, above
  for the spider sign, the front for the cup. `HandSignTests` reads each sign back into a HandPose and checks the
  game's own classifier sees it (open, fist, gun, spider, cup, snap, and the two-hand triangle). Renders:
  `Logs/play_select_*`, `Logs/play_signview_*`.
- **Enemies:** the yellow gunner (Robot, "Stan") is out of the waves and the boss's helpers; wave 1 is dashers and
  drones; the Variant Lab's practice dummy is a dasher. The Dasher now runs a learnable routine (tell with a floor
  line and landing ring, 0.3 s dash with a trail, skid, two shots, back the other way). Enemy laser hit thickness
  0.05w+0.05 became 0.035w+0.02. The boss can't be webbed. Lumen's force grabs enemies (same hold, swing and fling
  as the web; opening the hand drops it). `EnemyRoutineTests` covers the routine and the grabs.
- **Asked: masking your hands with preset 3D models.** Answered in chat; not built.
- EditMode 165/165, PlayMode 18/18.

## Late evening

- **Boss strike warning:** `TrackingStrike` calls `Arena.WarnPlayer` each frame while you stand in its ring;
  `HurtFlash.Warn` tints the whole view a little (the vignette's clear centre is pulled in) and pulses.
- **Orange damage on health bars:** `Ui.Bar`'s trailing bar is now orange (`Bar.DamageColor`), waits 0.6 s after
  the last hit and drains at 0.8 a second, so a held attack's damage stays orange; on for every health bar.
- **Lumen crush:** a fist held 0.12 s on an enemy held by the force deals `Arena.CrushDamage` (50). Tested.
- **Hand models:** searched Sketchfab's public API for downloadable CC0/CC BY models per character and for
  tornadoes; 4 picks each on a page with codes (T, M, K, L, V, W, S, E, N). Free themed gloves and gauntlets are
  rarely rigged, so unrigged picks favour separate plates per finger joint (to be bound to the tracked bones).
  Spider-Man and Fortnite models were skipped (someone else's characters). Sketchfab downloads need the user's
  account. Waiting on picks.
- **Muted tests:** `Tests/PlayMode/Muted.cs` silences every PlayMode test (user's request).
- Installed on the headset (saved data kept: same signing key now). It was asleep, so no on-device check yet.
- EditMode 165/165, PlayMode 18/18.

## Level 3 hand models and the tornado (night)

The user picked T2, M1, L1, V2, S1 and N1 and downloaded them as GLBs into `assets/hands` (the "glove.glb" is
Rouker's "glove", M4, not RedRocket's "Leather Gloves", M1; used as is, asked about it). None are rigged.
- `HandSkinViews` renders each model face-on with a (u, v) grid; wrist, knuckles, tips and thumb were read off
  those pictures into `HandSkinBuilder.Specs`. The builder finds each point's depth from the mesh, turns the model
  into hand space (wrist at 0, fingers +Z, palm -Y, real size), mirrors left-hand sources (the energy gauntlet is a
  left hand), puts the middle finger joints at the centre of a thin slice of the finger's own vertices, binds each
  vertex to its nearest bone (blending near joints) with a forearm bone for sleeves (cut to 24 cm), simplifies the
  Stormcaller by vertex clustering (227k to 47k triangles), and saves `Resources/HandSkins/{Name}_R/_L`.
  A first attempt fitting from three rough hints (wrist, finger and palm directions) put bones outside the fingers;
  the hand-read landmarks fixed it.
- `HandSkin` drives the skeleton from the tracked joints each frame (bone directions, the hand scaled to yours);
  `Arena.HandSkins` wears them at level 3 for Technoman, Emberwitch, Lumen, Valkyr and Tempest, with a guessed elbow
  for the sleeve. Renders: `Logs/play_skin_*`; fit pictures `Logs/handskin_fit_*`.
- Rouker's glove stretches a little in finger-gun and spider poses (its fingers are modelled curled and packed).
- Tornado: "Animated Tornado" (Anthony Yanez) as `Props/Tornado`, drawn with the web shader in white at 45%,
  spun and swayed in code; the select screen's tornado too. Render `Logs/play_web_tornado.png`.
- Credits moved to `Assets/Resources/Credits.txt` and shown in the game (title screen > CREDITS).
- EditMode 165/165, PlayMode 21/21. Installed on the headset.

## 11/15: 10-round story and the mech boss

- `WavePlan`: 10 rounds (`StoryRounds`), one new kind every round or two (`Introduced`: drone 1, dasher 2,
  brawler 3, rocketeer 5, sniper 7, tank 8), a little rarer in its first round; announced on the HUD as NEW ENEMY
  with its blurb. `EnemiesInWave` is now 3 + 1.6 per round (4 up to 17). Round 10 spawns `WavePlan.FinalBoss`.
- `EnemyKind.Mech` (GOLIATH, 900 HP, 2.7 m) with `MechBrain` (Game/MechBoss.cs): swing (0.8 s wind-up, red fan
  on the floor, 16 damage, 2-3 in a row, never twice running so standing close still sees everything), arm
  lasers (paired beams per arm, 4-5 volleys), shoulder rockets (6-8), jump slam (crouch, 1.1 s leap of up to 5 m
  to where you stood, 20 damage within 1.3 m, then a shockwave out to 4.5 m at 5 m/s doing 9). The shockwave is
  dodged by a real jump (head 10 cm above the slowly learnt standing height), the shield facing it, or range.
  Enraged below 40%; calls two helpers once below 60%. Bosses share `IBossBrain`; `EnemySpec.IsBoss` covers both.
- Mech model candidates (Sketchfab, CC BY, animated) added to the shortlist page as B1-B4; a stand-in
  (`Resources/Enemies/MechBoss`, Quaternius "Mike" scaled) is used until one is picked. Trilobite stays in the code
  (debug spawns) but neither the story nor BOSS FIGHT uses it now.
- Co-op: the mech's swing, slam and shockwave are worked out on the host only (lasers and rockets are sent).
- EditMode 165/165, PlayMode 22/22 (`MechBossTests` runs the routine and pictures the warnings). Installed.
- Mech model: the user picked B3, "The Morser 16" (anonDutch, CC BY 4.0, 19k triangles, 87 bones, walking and
  running clips, a blade on the right arm and a gun on the left). Imported through EnemyModels as a GLB (glTFast's
  animation method set to legacy through its serialized import settings; its materials moved onto URP Lit with
  their colour, normal and glow maps). No attack clips, so MechBrain poses the arms after the walk each frame:
  the blade arm draws back and up through the wind-up and sweeps across at the strike; the gun arm aims at you
  while firing and kicks back per volley; lasers come from its `Gun_FX` bone. With no idle clip, standing still
  holds the walk's pose (EnemyLook). Pictures `Logs/play_mech_*.png`.

## 11/15: lighting, walls, tracking, effects (from the user's list and the headset log)

Pulled `game_log.txt` from the headset (the 26-minute session of 11/14 21:28) and measured it:
- Hands dropped out 259 times (median 0.2 s; 60% under 0.3 s, 85% under 1 s). The weapon layer reset the charge,
  beam or flames on every one. Now a hand lost for under 0.3 s coasts: `HandInput.CoastSeconds` keeps the pose
  (velocity measured over the gap when it returns, so no spike), `HandRepulsor.LostGraceSeconds` keeps the weapons.
- About 40% of gesture changes were flickers through None lasting 40-60 ms (Open-None-Open 392 times, Fist-None-Fist
  95). `GestureClassifier.Classify(..., last)` leans its thresholds 0.12 toward the current shape and `GestureHold`
  keeps a shape through 80 ms of None; both hand readers use them.
- 38 of 166 spawns used the fallback ("map not ready"); the head position jumped 4 times, clearing the map each time.
- The camera read costs about 2 ms, so the room-light sample (once a second outside fights) is negligible.
- Frame rate: median 72 FPS; with the tight aura on the slowest frames doubled (28 ms against 14).
- The enemy "jumped 5 m at age 0.01" lines are the debug spawn button moving them, not a bug.
- "Laser off, surface found on 0/N frames" was all flamethrower use (it doesn't look for a surface): not a bug.

Walls. Found: the depth scan cleared cells only 30 cm short of what it hit and checked one cell of clearance whatever
the enemy's size; the fallback preferred directions the depth couldn't measure (they scored as open) and measured
one thin ray; dashes, knockback, the tornado's pull and the mech's leap only avoided furniture boxes. Now:
`RoomMap.TryPick` takes the enemy's radius and has a strict tier (floor seen in or beside the cell, every cell
within radius + 25 cm seen open and not solid, connected to a player by flood fill, straight line to the player
clear) and a relaxed tier for small rooms (radius clearance, 60% of the minimum distance). Scan rays are jittered
so floor hits cover the floor instead of rings. `RoomMap.Walled` (solid cell, or off Space Setup's floor) feeds
`Arena.Steer`. The fallback uses three rays as wide as the enemy within 40 degrees, treats unmeasured directions as
short, and stops radius + 30 cm short. `Arena.TickBuried` flags an enemy in a solid cell or hidden behind a
surface per the depth (middle and top, twice running); it then runs straight at the player. Fail-safe:
`RepulsorGame.WallAhead/ReachPast` lets shots, lasers, gusts, the dragon fist and thrown furniture carry on
through a wall to an enemy on the line at or past it. Tests: `RoomSafetyTests`.
Note: enemies are not hidden by real walls (URP Lit has no occlusion), so one in a wall is visible.

Lighting. The scene had no light at all (default skybox ambient). `RoomLight` adds a directional key light and
trilight ambient, tinted and scaled from the camera picture's average (`PersonDetector.TrySampleLight`,
`RoomLight.Estimate`). Blob shadows: `Repulsor/Blob Shadow` on a quad under each enemy, smaller and fainter with
height. The hit flash went from 3x to 1.7x white (it blew out under the new light).

Effects reviewed in the capture renders and improved: boss warnings (`FloorMark`: filled disc or fan whose inner
fill reaches the rim at the moment of the hit; mech swing, mech slam, the old boss's strikes), the mech's sword
sweep (`LineFade` arcs), a second ring and thinner taper on its shockwave, the gust (three crescents turning about
the line of flight instead of three ring sprites), the tornado (1.25 wide to 2 tall, 28% opacity; it filled the
view and went opaque). Not changed: dash line, powerups, missiles, smoke, lightning, shields.
EditMode 175/175, PlayMode 22/22.

## 11/15 afternoon

- Gunner (EnemyKind.Robot, model Stan) restored: earlier "remove the yellowish robot" was applied to the gunner by
  mistake; the yellow one is the brawler ("Classic"). `EnemyKinds.Introduced`: gunner 1, drone 2, dasher 3,
  rocketeer 5, sniper 7, tank 8; brawler never. Boss helpers: drone/gunner/dasher.
- Saber: `CutWithBlade` sweeps the blade between frames (tip and mid-blade segments) so a swing hits even when the
  blade passes through in one frame; a slash deals `Lerp(9, 20, tip speed / 2.5 m/s)` once per 0.35 s per enemy;
  held inside, 6 dps. Test `TheLaserSwordHitsHardOnTheSlashNotWhileHeldIn` (slash 20, held 1.5).
- Hand skins: `HandSkinBuilder.FitCurl` replaces Refine (middle joints at slice centres bent bones 16-29 degrees in
  straight fingers). Knuckle and tip stay; the chain curls evenly toward the palm, 0-60 degrees per joint, the
  angle that best fits the finger's own vertices. `HandSkinCheck` prints the bind bends. Emberwitch (Rouker glove)
  still stretches in tight poses: its fingers are modelled curled and packed.
- Select screen: `CharacterShowcase` takes a figure height (life size there); it stands 1.2 m beyond and 1.1 m right
  of the menu on the floor. The panel's well is the hand-sign box (sign hand scale 0.48 / 0.34).
- Grab test made robust to wave spawns (`TestCanGrab` sets other enemies aside).
- Tornado shortlist TN1-TN6 added to the hand shortlist page.
EditMode 175/175, PlayMode 23/23. Installed.
- Tornado: TN3 ("Tornado 2", Jacob Quintana, CC BY 4.0) replaces N1. `TornadoModel` (editor) builds
  Resources/Props/Tornado: drops the cloud deck (clouds_19) and floor swirls (Object_42/45), funnel 1 m tall, legacy
  looping animation (93 s, started at a random time in game), `Repulsor/CloudModel` shader (its texture, unlit,
  see-through, edges fade side-on), alpha 0.5, scaled (1, 1.9, 1) x size. Previews Logs/preview_tornado_*.png.
  EditMode 175/175, PlayMode 23/23. Installed.
- Gloves removed (the user: still don't look good). Deleted Resources/HandSkins, Characters/HandSkins (5 GLBs),
  HandSkinBuilder/HandSkins/HandSkinViews/HandSkinCheck and their credits; APK 222 -> 193 MB. Level 3 now shows a
  hand aura for every character (`Arena.HandAura.cs`): the XR sample hand (Showcase/Left|RightHand) driven by
  `HandSkin`, drawn with `Repulsor/HandAura` (two additive shells pushed out 3 mm and 11 mm along normals: a
  silhouette outline and rising value-noise flicker; fades out past the wrist via _WristPos/_WristFwd), tinted
  AttackColor. Technoman's old ellipsoid hand glow is gone. Capture test `HandAuraCaptureTests` (Logs/play_aura_*).
  Select screen hint label widened (its ">" wrapped). EditMode 175/175, PlayMode 23/23. Installed.
- Join by IP: the user's headsets were 172.96.99.x and 172.96.116.x, and the room code only carries the last
  number (the joiner fills in its own first three). The host's lobby shows its IP big; the join keypad has a "."
  key and takes an address (`NetProtocol.TryParseAddress`) or a code; the last address is offered again
  (PlayerPrefs LastHostAddress). Installed on 2G97C5ZH9300P6 (headsets also seen: 2G0YC5ZG8V05SS, 2G97C5ZH920328).
- Hand aura overlap fixed: `Repulsor/HandAura` is one shell (8 mm) with a depth-only first pass (SRPDefaultUnlit,
  ZWrite, ColorMask 0) and the glow on the nearest surface only (UniversalForward, ZTest LEqual), queue
  Transparent+50 so its depth hides only itself. Capture `play_aura_*_alone.png`.
- Multiplayer (protocol version 2, both headsets need this build):
  - Shots carry a look (`ShotLook`: ki ball + style, arrow, bolt, rock, web, comet + style); `Arena.NextShot` before
    FireBlast, `TakeShotLook` in BlastFired; `RepulsorGame.FiringComet` marks repulsor shots.
  - Player updates carry each hand's beam (`HandBeam`: laser, flames, blade, lightning, from/to), from
    `HandRepulsor.ShownBeam` and `ChainLightning.ShownNow`; drawn by `Arena.NetLooks` (laser views, flamethrowers,
    lightning bolts).
  - `NetMessage.Effect` (`NetEffect`): dragon fist, fire pillar, gust, tornado, rock spikes, explosion (snap), smoke.
    Replayed with no damage; `RepulsorGame.Strike` ignores damage <= 0 (so replays never hit or report hits).
  - The other player's real aura: a copy of the AuraView style object with _AuraPower/_AuraTint/_AuraFoot/
    _AuraFloorHidden set per renderer (MaterialPropertyBlock), plus a second `ElementAura`. The stand-in shell is gone.
  - Their shield wraps the whole body (it was placed like your own, round the upper body).
  - GOLIATH: `NetMessage.EnemyMove`; MechBrain.Tell on swing (facing, wind-up), jump (from, to), lasers, rockets,
    summon, rest; the client's copy `Replay`s and `TickProxy`s it (wind-up mark, sweep, jump arc, slam, shockwave),
    hurting only its own player. Host swings now check the host's own head (before: the nearest player's, so a
    swing at the client hurt the host).
  - Tests: NetTests round trip; CoopTests.ClientPlaysTheMechsSwingAndDrawsThePartnersLooks (HP 100 -> 84;
    Logs/play_coop_remote.png). EditMode 177/177, PlayMode 24/24. Built; no headset connected to install.
- Character select readiness: localReady/remoteReady were never cleared at a match start, so after a match (or the
  pause menu's CHARACTER SELECT) both were still ready and the first lobby message started the next match. Now
  cleared at StartMatch and on arriving at character select.
- Multiplayer pause: Paused/PauseReady/Choosing ride on the player update; `Arena.TickMultiplayerPause` pauses you
  when the other pauses (a fresh, not-ready pause), resumes when both are ready (READY on the pause screen or the
  pause gesture again), follows the other to character select. Time stops on both (timeScale 0; the network runs
  on unscaled time). Test `CoopTests.PausingPausesBothAndBothMustBeReady`.
- Ryu: the dragon fist was sent twice (as its effect and as the comets it is made of); its shockwave styles did
  real damage when replayed on the other headset (in PVP, to the puncher). Now one effect, and replayed shockwaves
  are harmless (`Shockwave.Harmless`, `RepulsorGame.Replaying`). The user's "Ryu starts to break after a bit" is
  not yet diagnosed: needs the headset log.
  EditMode 177/177, PlayMode 25/25. Built; no headset connected.
