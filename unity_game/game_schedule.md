# Game schedule

Development deadline is **11/16**, and the entry is due 11/18 (video + GitHub + writeup). The spec is in
`game_plan.md`, and daily notes are in `logs/`. Work happens on the `v1-game` branch.

Columns:

- **Done:** Claude built it and it runs.
- **Good enough:** Claude checked it, with tests, PC renders or headset logs.
- **Checked by you:** you saw it in the headset and approved it.
- **Pick:** for visual items, the variant letter you chose (A/B/C/D).

## Day plan

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
| Game title | POWER UP (placeholder) |

## Your headset checklist for 11/8

The new build is installed. Pick it up and move around until it finds its position, then launch Repulsor Test.

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
5. **Variant batches 1-2:** DEBUG > VARIANT LAB.
   - SHIELDS tab: poke A-E, then SAVE PICK for each character you care about.
   - DRAGON FIST tab: A comet, B three twisting comets, C punch wave, D both. Poke to fire one, then SAVE PICK.
   PC renders are in `unity_game/Logs/preview_shield_*.png`.
6. HUD: is it readable, in a good spot, too big or too small?
7. **When the second Quest arrives:** install the same build on both (`build_and_run.ps1 -NoBuild` with each plugged
   in), same Wi-Fi. One does CO-OP > CREATE ROOM, the other JOIN ROOM with the code. CALIBRATE: both touch the same
   table corner with the right index finger and hold still, then a second point. The coloured dots should sit on the
   other person's head and hands. Then both pick characters and READY.

## Model candidates for enemies (pick one per enemy)

All of these say CC0 on their pages. I'll check each licence file when downloading.

| # | Model | For | Link |
|---|---|---|---|
| R1 | Quaternius Animated LowPoly Robot (14 animations, including walk and shoot) | robot | [itch.io](https://quaternius.itch.io/lowpoly-robot), [OpenGameArt](https://opengameart.org/content/animated-lowpoly-robot) |
| R2 | Quaternius Animated Mech Pack | robot | OpenGameArt, CC0 |
| R3 | Quaternius Sci-Fi Essentials Kit robot enemies | robot or drone | [quaternius.com](https://quaternius.com/packs/scifiessentialskit.html) |
| D1 | Quaternius Ultimate Space Kit enemies (check for a flyer) | drone | [quaternius.com](https://quaternius.com/packs/ultimatespacekit.html) |

## Features

### Already built before 11/7

| Feature | Done | Good enough | Checked by you | Notes |
|---|---|---|---|---|
| Hand lasers + stopping at surfaces | ✅ | ✅ | ✅ | |
| Repulsor blasts | ✅ | ✅ | | |
| Shockwave | ✅ | | | needs to stop at walls |
| Auras: Fire Shells, Spike Shells, Light Halo, Sun Halo, Rune Circle | ✅ | ✅ | | |
| Person tracking (BlazePose) + depth hiding | ✅ | ✅ | | suits tested on PC replay only |
| Colour wheel | ✅ | | | moves to character select |

### Core

| Feature | Done | Good enough | Checked by you | Notes |
|---|---|---|---|---|
| HP, damage, passive healing | ✅ | ✅ | | unit tests |
| Transformation meter, power-up pose, levels 1-3 | ✅ | ✅ | | unit tests; pose thresholds untested on real hands |
| Lives, respawn, grey view while dead | ✅ | ✅ | | |
| Shield (X arms), hold limit + cooldown | ✅ | ✅ | | 5 bubble styles; flame style needs another pass |
| Gesture library | ✅ | ✅ | | 13 detectors with made-up-hand tests; **needs tuning with your hands** |
| Debug menu (old controls, Variant Lab) | ✅ | | | also the gesture readout, FPS, spawn/hurt/meter/powerup buttons |

### Menus and HUD

| Feature | Done | Good enough | Checked by you | Pick |
|---|---|---|---|---|
| Start + mode select | ✅ | ✅ | | PC render; co-op and PVP buttons disabled until multiplayer |
| Character select + controls + colour + ready + countdown | ✅ | ✅ | | PC render |
| Pause (double OK, 2 s) + settings | ✅ | ✅ | | PC render |
| Poke buttons (keyboard-style) | ✅ | | | sink into a hollow, click at 9 mm; feel untested |
| Own HP + meter HUD | ✅ | ✅ | | PC render; one design so far |
| Overhead health bars | ✅ | | | on stand-in orbs |

### PVE

| Feature | Done | Good enough | Checked by you | Pick |
|---|---|---|---|---|
| Stand-in orb enemies with HP | ✅ | | | until robots exist |
| Robot enemy (walk, bad-aim lasers) | ✅ | | | placeholder body until you pick a model (see candidates below) |
| Drone enemy | ✅ | | | weaves at head height, smaller; placeholder body |
| Spawn bubble + waves (5) | ✅ | | | purple bubble grows, enemy fades in |
| Powerups (charge, invincibility, 1.5x damage) | ✅ | | | touch to take; no glow on you yet |
| Walls block attacks | ✅ | | | laser, blasts, shockwave and enemy bolts stop at live-depth surfaces |
| Room objects (proxies, HP, pick up/throw) | ✅ | | | Space Setup furniture boxes; a glowing copy flies and breaks on impact; **needs Space Setup done** |

### Characters

| Character | Name | L1 | L2 | L3 | Shield | Visuals | Checked by you |
|---|---|---|---|---|---|---|---|
| Technoman | | ✅ laser | ✅ +repulsor | ✅ AOE repulsor | ✅ hex | no gauntlets / tight aura yet | |
| Ki fighter | | ✅ comet (yellow) | ✅ energy ball | ✅ dragon fist (4 looks in Variant Lab) | flame (needs work) | aura 2 at L2, blue at L3 | |
| Mage | | ✅ fireball | force: needs room objects | ✅ fire pillar (placeholder look) | thin | rune aura L2-3 | |
| Saber | | ✅ finger-gun blaster + aim line | ✅ laser sword | force: needs room objects | glass | light aura | |
| Archer | | ✅ crossbow (reloads) | ✅ bow + aim line | ✅ smoke bomb (blinds enemies), piercing arrows | (knight shield: needs model) | wings: needs model | |
| Spider | | | | | | | |

### Multiplayer

| Feature | Done | Good enough | Checked by you | Notes |
|---|---|---|---|---|
| LAN room code + join | ✅ | ✅ | | UDP; the code carries the host's address (same Wi-Fi); loopback test passes |
| Colocation | ✅ manual | | | both touch the same 2 real points; shared anchors not tried |
| Player sync (pose, aura, shield, attacks) | ✅ partial | | | HP bar, level, shield, head and palm markers, blasts; no remote aura yet |
| Co-op PVE | ✅ | | | host runs enemies and waves, client sees copies and its hits count; ends when both are out |
| PVP (best of 3, friendly fire toggle) | ✅ | | | victim's headset applies hits; levels reset each round; co-op friendly fire toggle in pause |

### Submission

| Item | Done | Notes |
|---|---|---|
| Clean public repo | | `suit.glb` / `spiderman.glb` must not be in its history |
| CREDITS for all downloaded assets + sounds | | |
| Writeup notes | | |
| Video shot list | | |
