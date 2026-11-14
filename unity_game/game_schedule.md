# Game schedule

Development deadline is **11/16**, and the entry is due 11/18 (video + GitHub + writeup). The spec is in
`game_plan.md`, and daily notes are in `logs/`. Work happens on the `v1-game` branch.

Columns:

- **Done:** Claude built it and it runs.
- **Good enough:** Claude checked it, with tests, PC renders or headset logs.
- **Checked by you:** you saw it in the headset and approved it.
- **Pick:** for visual items, the variant letter you chose (A/B/C/D).

## Status, 11/14 morning (built overnight, not yet seen in the headset)

New since 11/8 (details in `logs/2026-11-14-robots-boss-elements.md`):

- **7 robot kinds**, each its own model: gunner, drone, brawler (punches), rocketeer (missiles that hurt robots
  too), dasher, sniper, tank. They fire **red lasers** with an aim line first. **Hitboxes** match the models.
- **Spawning** across the whole room, far from you: Space Setup's floor if there is one, otherwise a depth
  scan (runs all match, plus **SCAN ROOM** on the title screen).
- **Boss**: a 2.3 m Trilobite walker with a bar at the top of your view: laser fans, a ring that chases you
  with strikes, missiles, helpers. Last wave of SOLO, or **BOSS FIGHT** in the mode menu.
- **Valkyr**: crossbow and longbow models that charge up, arrow and bolt shots, knight shield.
- **Tempest** (Elementalist) and **Terra** (Earth), the two new classes from the plan.
- **Shield**: 5 s instead of 3, quicker to come back, looser X, a clear SHIELD BROKEN.
- **Tight aura**: a glow hugging the body, for 6 s after a transformation, for a powerup's length, and for good
  on Technoman at level 3 (as the plan says). On yourself: a glowing outline on your hands and forearms with
  afterimages. On the person the camera sees (the other player, or you in a mirror): the pose model's body mask,
  grown and glowing, leaving afterimages as it updates.

The APK is built; install it with `build_and_run.ps1 -NoBuild` when the headset is plugged in.

### Headset checklist for this build

1. **Shield**: hold the X. Does it come up? What was broken before? It now lasts 5 s.
2. **SCAN ROOM** (title screen): look around; do dots cover your floor? Then SOLO: do robots come from all over
   the room, far from you? The log says where each came from.
3. **Lasers**: is the red aim line easy to see and dodge? Are the hits fair?
4. **Hitboxes**: shoot robots in the head and legs; do hits land?
5. **Robot kinds**: DEBUG > SPAWN ENEMY gives random kinds. Brawler punches (step back), missiles (shoot them).
6. **BOSS FIGHT**: is it readable, too hard or too easy? 700 HP is a guess.
7. **Valkyr**: do the crossbow and bow sit in your hands right? (orientation is guessed from renders)
8. **Tempest and Terra**: try each gesture; tell me which miss or misfire.
9. **Tight aura** (added 11/14 morning): DEBUG > INVINCIBLE 8s (or transform). Do your hands and forearms get a
   glowing outline, with afterimages when you swing them? Stand in front of a mirror: does your reflection get
   the glowing body outline? Is it too strong to see your hands through? Does the frame rate hold?

## Status, end of 11/8 session

Most of the Claude-side work planned for 11/7-11/14 is built and installed on the headset. 138 of 138 unit tests pass, plus 4 PC tests: every character through a wave, co-op as host and as client, and a PVP round as host. **None of it has been seen in the headset yet.**

The bottleneck now is your testing:
- gesture thresholds
- poke feel
- HUD readability
- frame rate
- the two-headset test

Then picking models and variants.

Suggested re-plan:

| Day | Claude | You |
|---|---|---|
| 11/9 | fix whatever your first session finds; fit picked models (enemies, gauntlets) | checklist below; download picked models |
| 11/10 | two-headset fixes; frame rate pass; remaining looks (wings, knight shield) | co-op test with the second Quest |
| 11/11 | **pivot checkpoint**: what to cut or polish | PVP test |
| 11/12-11/15 | polish, balance, bugs, sound | playtests |
| 11/16 | freeze, clean repo, writeup notes | final check |

## Day plan (original)

The pivot checkpoint is the evening of **11/11**: anything behind schedule gets re-planned or cut.

| Day | Claude | You (headset, 1-2 h) |
|---|---|---|
| 11/7 Sat | ✅ commit + branch (core and gestures slipped to 11/8, now done); game core (HP, damage, meter, levels, power-up pose, lives); gesture library; debug menu shell | answer round 2 |
| 11/8 Sun | ✅ core, gestures, ✅ menus (start, mode, character select + controls, ready, countdown, pause, settings), poke buttons, HUD, overhead bars | gesture tuning; **variant batch 1:** shields, HUD |
| 11/9 Mon | enemies (robot, drone, spawn bubble, waves, Space Setup bounds), powerups, walls block hits; find CC0 models + SFX | first full PVE run; 2nd headset arrives |
| 11/10 Tue | Technoman L1-3, Ki fighter L1-3; colocation spike (shared anchors or manual) | **variant batch 2:** comet blast, charge ball, dragon fist, gauntlet candidates |
| 11/11 Wed | co-op networking: LAN room code, player sync, enemies and damage over the network | 2-headset test; **pivot checkpoint** |
| 11/12 Thu | Mage L1-3, Saber L1-3, room objects + force throw | **variant batch 3:** sword, fire pillar, rune circle, proxies |
| 11/13 Fri | Archer L1-3, Spider L1-3 (webs, web pull, spider sense, cocoon) | **variant batch 4:** bow, wings, webs, cocoon |
| 11/14 Sat | PVP (rounds, friendly fire toggle), tight aura, sound pass | long playtest |
| 11/15 Sun | balance, 72 FPS performance pass, bugs, follow-up variant rounds | playtest |
| 11/16 Mon | feature freeze, clean public repo (no suit models in history), writeup notes, video shot list | final check |

## Live tuning (no rebuild)

If a gesture misfires or menus are hard to reach, tell me what happens. I push a small file to the headset and it
takes effect within 2 seconds. For example, `MenuLayout.Distance = 0.38` or `HandPose.ThumbOutAbove = 0.5`. All
current values are written to `files/tuning_current.txt` on the headset when the game starts.

## Names (pick one per character, or write your own)

The first option is used as the placeholder for now.

| Character | Options |
|---|---|
| Technoman | Technoman, Voltframe, Arc Knight |
| Ki fighter | Ryuu, Kaizen, Solar Monk |
| Mage | Emberwitch, Pyra, Hexweaver |
| Saber | Lumen, Starblade, Vex Solari |
| Archer | Valkyr, Aethon, Skyhunter |
| Spider | Webline, Silkstrike, Arachne |
| Elementalist | Tempest, Zephyr, Stormcaller |
| Earth | Terra, Bedrock, Quarry |
| Game title | POWER UP (placeholder) |

## Headset checklist from 11/8 (older build; the 11/14 one is at the top)

The new build is installed. Pick it up and move around until it finds its position, then launch Repulsor Test.
If Space Setup hasn't been done in this room, do it first (headset Settings > Environment setup). The title screen
says how much furniture the game found, which the force powers need.

1. **Menus:** poke START, SOLO, a character, a colour, READY. Do the keys feel right? Are they too close, too far or too low?
2. **Gesture readout:** pause with both hands in an OK sign for 2 s, then DEBUG > GESTURES: ON. Try each one and
   watch which turn green and what fires:
   - fist, open, OK, finger gun, spider sign, cup
   - X arms, power-up pose (fists at your sides), hands together then push
   - triangle then fists, bow, finger-gun flick, punch
   Tell me which miss or misfire; the readings are also in the log.
3. **Match:** Technoman. Robots (red pills) and drones (orange discs) appear in purple bubbles and fire slow red
   bolts you can dodge or block with the X shield. Laser them until the meter is full, then do the power-up pose
   (level 2 adds repulsors, level 3 makes them explode). Touch the floating powerup balls.
   In the pause menu, DEBUG has HIT ME, KNOCK ME OUT, FILL METER and SPAWN POWERUP.
4. **Ki fighter (Ryuu):** fist and snap open for yellow comets. Use FILL METER and the pose to reach level 2:
   hold your hands together to charge a ball, then push both forward. Level 3: punch fast for a dragon fist.
   Then try the others the same way (FILL METER, power-up pose); the countdown and each level-up show what's new:
   - Mage: level 2, hold a palm out for the flamethrower; level 3, triangle, then fists for the fire pillar.
   - Saber: finger gun, then a fist for the sword.
   - Archer: fists together, then pull one back for the bow.
   - Spider: index and little finger out to shoot webs (hold to keep firing, with an aim line). Level 2: web an
     enemy and swing your arm to smash it into others, swing fast to fling it; furniture you'd grab lights up.
5. **Variant batches 1-2:** DEBUG > VARIANT LAB. Use < and > to switch item.
   - HUD: A low right, B low centre, C top centre. Saved as soon as you poke one.
   - SHIELDS: poke A-E, then SAVE PICK for each character you care about.
   - DRAGON FIST: A comet, B three twisting comets, C punch wave, D both. Poke to fire one, then SAVE PICK.
   - ENERGY BALL: A crackle, B plasma, C fireball, D star (the default, your pick). PC render: `unity_game/Logs/preview_energy_balls.png`.
   - FIRE PILLAR: settled on C BOTH (11/8), no longer in the lab.
   - ENEMIES: robot A George, B Leela, C Mike, D Stan (default), E Classic, F Shell, G the old pill; drone A eye drone
     (default), B the old disc. All have glowing red eyes. PC render: `unity_game/Logs/preview_enemies.png`.
   - COMET (Ki fighter's blast): A comet, B streak, C orb. PC render: `unity_game/Logs/preview_comets.png`.
   PC renders are in `unity_game/Logs/preview_shield_*.png`.
6. HUD: is it readable, in a good spot, too big or too small?
7. **When the second Quest arrives:** install the same build on both (`build_and_run.ps1 -NoBuild` with each plugged
   in), same Wi-Fi. One does CO-OP > CREATE ROOM, the other JOIN ROOM with the code. CALIBRATE: both touch the same
   table corner with the right index finger and hold still, then a second point. The coloured dots should sit on the
   other person's head and hands. Then both pick characters and READY.

## Models (each enemy kind has its own since 11/14; VARIANT LAB > ENEMIES shows them all)

All CC0 from OpenGameArt; licence files are next to the models in `Assets/Enemies/`.

| Pick | Model | Source |
|---|---|---|
| Robots A-D | Quaternius Animated Mech Pack: George, Leela, Mike, Stan (walk, idle, shoot) | [OpenGameArt](https://opengameart.org/content/animated-mech-pack) |
| Robot E | Quaternius Animated Robot (walk, idle, punch) | [OpenGameArt](https://opengameart.org/content/animated-lowpoly-robot) |
| Robot F, drone A | Quaternius Sci-Fi Essentials Kit: QuadShell, EyeDrone | [OpenGameArt](https://opengameart.org/content/sci-fi-essentials-kit) |
| Boss | Quaternius Sci-Fi Essentials Kit: Trilobite | [OpenGameArt](https://opengameart.org/content/sci-fi-essentials-kit) |
| Valkyr bows, arrow, knight shield | Quaternius LowPoly Medieval Weapons | [OpenGameArt](https://opengameart.org/node/88080) |
| Valkyr crossbow, bolt | KayKit Adventurers (Kay Lousberg) | [itch.io](https://kaylousberg.itch.io/kaykit-adventurers) |
| Terra's rocks and spikes | Kenney Nature Kit | [OpenGameArt](https://opengameart.org/content/nature-kit) |

## Known gaps

- **Assets still needed:** Technoman's gauntlets and the Valkyr's wings (the knight shield is in since 11/14).
- **Other player's aura:** in multiplayer it's a body-shaped shell, not the real aura.
- **Effect sounds** are synthesized. The Kenney CC0 pack needs a browser download.

## Gauntlet candidates for Technoman level 2 (pick one, or let me build my own)

| # | Model | Licence | Link |
|---|---|---|---|
| G1 | Sci-fi Glove (low poly) | CC-BY: credit needed, allowed | [Sketchfab](https://sketchfab.com/3d-models/sci-fi-glove-aa87260a746142e1bd4266cd363c189c) |
| G2 | Meshy "shields & armor" gauntlets | marked CC0, but AI-generated, so check each model page | [Meshy](https://www.meshy.ai/subcategory/shields-armor) |
| G3 | Sci-fi glove on free3d | licence not shown, so check the page | [free3d](https://free3d.com/3d-model/sci-fi-glove-5311.html) |

Sketchfab needs a login to download. If you grab G1 as glTF into `assets/models/`, I'll fit it to the tracked hand joints.

## Features

### Already built before 11/7

| Feature | Done | Good enough | Checked by you | Notes |
|---|---|---|---|---|
| Hand lasers + stopping at surfaces | ✅ | ✅ | ✅ | |
| Repulsor blasts | ✅ | ✅ | | |
| Shockwave | ✅ | | | stops at walls now |
| Auras: Fire Shells, Spike Shells, Light Halo, Sun Halo, Rune Circle | ✅ | ✅ | | |
| Person tracking (BlazePose) + depth hiding | ✅ | ✅ | | suits tested on PC replay only |
| Colour wheel | ✅ | | | moves to character select |

### Core

| Feature | Done | Good enough | Checked by you | Notes |
|---|---|---|---|---|
| HP, damage, passive healing | ✅ | ✅ | | unit tests |
| Transformation meter, power-up pose, levels 1-3 | ✅ | ✅ | | unit tests; pose thresholds untested on real hands |
| Lives, respawn, grey view while dead | ✅ | ✅ | | |
| Shield (X arms), hold limit + cooldown | ✅ | ✅ | | 5 bubble styles; flame reworked to wispy layered noise |
| Gesture library | ✅ | ✅ | | 13 detectors with made-up-hand tests; **needs tuning with your hands** |
| Debug menu (old controls, Variant Lab) | ✅ | | | also the gesture readout, FPS, spawn/hurt/meter/powerup buttons |

### Menus and HUD

| Feature | Done | Good enough | Checked by you | Pick |
|---|---|---|---|---|
| Start + mode select | ✅ | ✅ | | PC render; title shows the Space Setup status |
| Character select + controls + colour + ready + countdown | ✅ | ✅ | | PC render |
| Pause (double OK, 2 s) + settings | ✅ | ✅ | | PC render |
| Poke buttons (keyboard-style) | ✅ | | | sink into a hollow, click at 9 mm; feel untested |
| Own HP + meter HUD | ✅ | ✅ | | PC render; 3 positions in Variant Lab; shows partner HP in co-op |
| Overhead health bars | ✅ | | | on robots and drones |

### PVE

| Feature | Done | Good enough | Checked by you | Pick |
|---|---|---|---|---|
| Robot enemy (walk, bad-aim lasers) | ✅ | | | slow dodgeable bolts; steers round furniture; CC0 mech models with red eyes, walk and shoot animations |
| Drone enemy | ✅ | | | weaves at head height, smaller; CC0 eye drone with a red eye |
| Spawn bubble + waves (5) | ✅ | | | spawn out of the nearest wall; purple bubble; score and best score per character |
| Powerups (charge, invincibility, 1.5x damage) | ✅ | | | touch to take; your attacks turn red or gold, and others see a glow |
| Walls block attacks | ✅ | | | laser, blasts, shockwave and enemy bolts stop at live-depth surfaces |
| Room objects (proxies, HP, pick up/throw) | ✅ | | | Space Setup furniture boxes; a glowing copy flies and breaks on impact; **needs Space Setup done** |

### Characters

| Character | Name | L1 | L2 | L3 | Shield | Visuals | Checked by you |
|---|---|---|---|---|---|---|---|
| Technoman | | ✅ laser | ✅ +repulsor | ✅ AOE repulsor | ✅ hex | L3 glow: your hands glow, others see a body glow; no gauntlets yet | |
| Ki fighter | | ✅ comet (yellow) | ✅ energy ball | ✅ dragon fist (4 looks in Variant Lab) | flame | aura 2 at L2, blue at L3 | |
| Mage | | ✅ fireball | ✅ flamethrower (palm out and hold) | ✅ fire pillar (inferno with rings) | thin | rune aura L2-3 | |
| Saber | | ✅ finger-gun blaster + aim line | ✅ laser sword | force: needs room objects | glass | light aura | |
| Archer | | ✅ crossbow model that charges (reloads) | ✅ longbow model, string, nocked arrow + aim line | ✅ smoke bomb (blinds enemies), piercing arrows | knight shield model | wings: needs model; white ring shockwave | |
| Spider | | ✅ web shots with strands, spider sense | ✅ web pull (furniture) | ✅ dark webs, more damage | web cocoon | no gloves yet | |
| Tempest (Elementalist) | | ✅ palm-push gust, knockback | ✅ chain lightning, 5 s then rest | ✅ swirl-and-push tornado that pulls | wind | wind aura L2-3 | |
| Terra (Earth) | | ✅ rock orbs, stone hands | ✅ uppercut rock spikes | ✅ sticky rocks, snap to blow | rock | crater L2, orbiting rocks L3 | |

### Multiplayer

| Feature | Done | Good enough | Checked by you | Notes |
|---|---|---|---|---|
| LAN room code + join | ✅ | ✅ | | UDP; the code carries the host's address (same Wi-Fi); loopback test passes |
| Colocation | ✅ manual | | | both touch the same 2 real points; shared anchors not tried |
| Player sync (pose, aura, shield, attacks) | ✅ | | | HP bar, level, shield, powerup glow, an aura stand-in (body shell), head and palm markers, blasts |
| Co-op PVE | ✅ | ✅ both sides on PC | | host runs enemies and waves, client sees copies and its hits count; ends when both are out. PC tests: the game as host with a test client, and as client with a test host; enemies, hits and start all go through |
| PVP (best of 3, friendly fire toggle) | ✅ | ✅ host side on PC | | victim's headset applies hits; levels reset each round; co-op friendly fire toggle in pause |

### Submission

| Item | Done | Notes |
|---|---|---|
| Clean public repo | | `suit.glb` / `spiderman.glb` must not be in its history |
| CREDITS for all downloaded assets + sounds | | |
| Writeup notes | | |
| Video shot list | draft | `unity_game/video_shot_list.md` |
