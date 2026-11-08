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
| 11/7 Sat | commit + branch; game core (HP, damage, meter, levels, power-up pose, lives); gesture library; debug menu shell | answer round 2 |
| 11/8 Sun | menus (start, mode, character select + controls, ready, countdown, pause, settings), poke buttons, HUD, overhead bars | gesture tuning; **variant batch 1:** shields, HUD |
| 11/9 Mon | enemies (robot, drone, spawn bubble, waves, Space Setup bounds), powerups, walls block hits; find CC0 models + SFX | first full PVE run; 2nd headset arrives |
| 11/10 Tue | Technoman L1-3, Ki fighter L1-3; colocation spike (shared anchors or manual) | **variant batch 2:** comet blast, charge ball, dragon fist, gauntlet candidates |
| 11/11 Wed | co-op networking: LAN room code, player sync, enemies and damage over the network | 2-headset test; **pivot checkpoint** |
| 11/12 Thu | Mage L1-3, Saber L1-3, room objects + force throw | **variant batch 3:** sword, fire pillar, rune circle, proxies |
| 11/13 Fri | Archer L1-3, Spider L1-3 (webs, web pull, spider sense, cocoon) | **variant batch 4:** bow, wings, webs, cocoon |
| 11/14 Sat | PVP (rounds, friendly fire toggle), tight aura, sound pass | long playtest |
| 11/15 Sun | balance, 72 FPS performance pass, bugs, follow-up variant rounds | playtest |
| 11/16 Mon | feature freeze, clean public repo (no suit models in history), writeup notes, video shot list | final check |

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
| HP, damage, passive healing | | | | |
| Transformation meter, power-up pose, levels 1-3 | | | | |
| Lives, respawn, grey view while dead | | | | |
| Shield (X arms), hold limit + cooldown | | | | |
| Gesture library | | | | |
| Debug menu (old controls, Variant Lab) | | | | |

### Menus and HUD

| Feature | Done | Good enough | Checked by you | Pick |
|---|---|---|---|---|
| Start + mode select | | | | |
| Character select + controls + colour + ready + countdown | | | | |
| Pause (double OK, 2 s) + settings | | | | |
| Poke buttons (keyboard-style) | | | | |
| Own HP + meter HUD | | | | |
| Overhead health bars | | | | |

### PVE

| Feature | Done | Good enough | Checked by you | Pick |
|---|---|---|---|---|
| Robot enemy (walk, bad-aim lasers) | | | | |
| Drone enemy | | | | |
| Spawn bubble + waves (5) | | | | |
| Powerups (charge, invincibility, 1.5x damage) | | | | |
| Walls block attacks | | | | |
| Room objects (proxies, HP, pick up/throw) | | | | |

### Characters

| Character | Name | L1 | L2 | L3 | Shield | Visuals | Checked by you |
|---|---|---|---|---|---|---|---|
| Technoman | | | | | | | |
| Ki fighter | | | | | | | |
| Mage | | | | | | | |
| Saber | | | | | | | |
| Archer | | | | | | | |
| Spider | | | | | | | |

### Multiplayer

| Feature | Done | Good enough | Checked by you | Notes |
|---|---|---|---|---|
| LAN room code + join | | | | |
| Colocation | | | | |
| Player sync (pose, aura, shield, attacks) | | | | |
| Co-op PVE | | | | |
| PVP (best of 3, friendly fire toggle) | | | | |

### Submission

| Item | Done | Notes |
|---|---|---|
| Clean public repo | | `suit.glb` / `spiderman.glb` must not be in its history |
| CREDITS for all downloaded assets + sounds | | |
| Writeup notes | | |
| Video shot list | | |
