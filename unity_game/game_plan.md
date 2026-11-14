# OVerall gameplan for this vr game
Goal: compete in meta dev challenge, due on 11/18, complete development by 11/16 

Key Idea: Hand tracking + segmentation for controls
          Pose tracking for aura
        
Goal by 11/16:

3 Modes:
PVP (Multiplayer)
PVE (Multiplayer)
PVE (single player)

Characters: ~10
Each with abilities that are based off of hand motions 
(2) Transformations each, that upgrade abilities

# Previous testing
Aura that tracks players is easy, and works well.
Hand tracking is easy aswell, and poses are also good

Pose segmentation for iron man suits and spiderman suits are difficult

# How to generate assets
Since this will be submitted for a competition, we need to use assets that are available to lisence

Previous experimentation shows that claude is not very good at generating assets of just prompts

If possible, use free assets from online, those will always look better

If not, claude should use inspiration from online models to create models

Finally, if none of these options are available, claude should generate the assets in the best way possible

# Documentation
For each update, fix, or info, put it in logs.
Add a seperate file called game_schedule.md that describes what is done so far and what still needs to be done. What has been checked by me, what is good enough, what is done


# Claude's role
I won't be able to test everything after each change
- For code, claude should be able to easily test
- For visuals, **Claude will make 3-4 versions that I will choose the best**
- The goal is 2-3 iterations for each model, that can be bested in parallel
    - This is much more efficient that going back and forth with claude 5-6 times for each time

# The game
Using VR passthrogh + hand/pose segmentation to create a vr game with the map being the players real environment. Walls will block attacks, players can run around furniture, ect

Players can transform/power up, which will display aura around them, and for others to see

## Player gameplay
From a delelopment perspective, there only need to be two toggles: enemies enabled/disabled, and pvp enabled/disabled.

For pvp enabled, your attacks are able to damage other players

For enemies/pve, use meta's room boundaires. Spawn enemies inside the boundaries 
The different enemies will be 
- Evil robots, that walk toward the players and near the player, they will shoot lasers, with bad aim
- Evil drones, which fly toward the player. These have smaller hitboxes

Enemy spawning may be better with depth priction, spawn near walls.

The enemies should have a spawning animations, maybe just a teleport bubble around them 

After not taking damage/attacking for a little bit, players can begin to heal
From testing, its really easy to hit people, so make base HP high and damage relatively lower, but these will need to be adjusted w/ playtesting


### Powerups
Around the map, near the player, powerups will spawn that can 
- Increase the power charge
- temporary invicibity w/ glowing effect (like mario's star)
- 1.5x damage with glowing red aura

### Gameplay settings
PVE:
- n lives
- respawn timer
- Revive teammates
PVP: 
- best of n


## HUD/Settings
### The start screen
Upon entering, the player sees a start screen, but still with pass through

They are taken to a mode selection screen. For multiplayer, they can either create a room or enter room join code

Once in, they will select a character and press "Ready". When all players are ready, the game will start with a countdown. Players can change the color of their character on this screen aswell.

THe controls will be on this screen aswell.
The controls are unique for each charcter, so make sure it is not just {key} does {ability}
### Pause menu
To open the pause menu, the player should make an OK sign with **both** hands for 2 seconds.
There, they can exit, back to the Ready screen, adjust volume, color, ect.

Additionally, gameplay settings will be on this screen, that can be toggled

### The game visuals
To transform, the players must deal damage to fill up a transformation meter. Afterward, they are able to transform. THis bar is displayed on each player's bottom right of their "screen"

Above the transformation bar will be the players own health bar. Base this off of fighting games like street fighter, but in general players should have more HP so that games last ~5 mins.

Above other players and enemies, you can see their health bar. This should follow the player around, and only appear when you look toward that player/enemie's head, to imporve performance


# Charcters
Each charcter has 3 modes (Level 1, level 2, level 3), which increase in visuals
If aura is around a player, they should see it too, but increase the transparency

Transformations heal a player 50% of their HP, and icnrease their max hp by 50% off bas(1.5x, then 2x)


## Templates claude should make, to standardize visuals
- Lasers (adjustable size, bloom, ect)
- "tight aura" This arua should be an outline around the player that makes it look like they are glowing, Use pose segmentation for this, but its ok that is delayed, since it will make it cooler
- Fire aura: this is currently implemented in 1 and 2, use these as different options
- SHields: everything will be like a bubble shield when the player makes an "X" with their arms, but each will be slightly different
- Hand aimble blasts: like the current repulsors/shockwaves
- Spherical Shockwave transformation, upon transforming, send a spherical energy shockwave to the new aura color
- Circular shockwave: same thing as spherical, but it is just a ring 
- Gun: with two fingers pointed forward, thumb pointed up, this makes a "gun", When the user rotates up, it fires in the orignial direction (use where the arms are pointing too)
## Object interaction. 
Some charcters can interact with objects around the room, Don't mask anything in a room, just make a 3d model that matches the color/shape of the object that can be manipulated, it can be approximate. These objects will have hp proportional to their size

Ex: If spiderman webs a trashcan, make a black rectangle for now

## Charcter list
Damage and stuff can be done later, with play testing, but make everyone start at 100 HP, and make the damage values balanced

Heals are on 30s cooldown

Make blocks like smash bros, where after using for too long it goes on a cooldown

### Iron man inspired
Name: Technoman (or something)
Defense: Making an x will make a blue bubble with a hexagonal pattern

Attacks:
Level 1:
Hand lasers (currently implemented)
Level 2:
lasers upgraded
Current repulsors/shockwave (shockwave shoudln't go through walls)
Level 3:
Only lasers upgraded
Repulsors are now AOE

Visuals:
Only for person playing iron man, their hands will start looking like gauntlets at level 2,
At level 3, they will have a blue tight aura

Transformation effects:
1->2: If possible, nanotech gauntlets (similar to webapp)
2->3: as the aura apears, shockwave of the same color

All attacks have the same base color, and can be adjusted

### Spiderman inspired
Name: ???
Defense: Making an x will make a web coccoon

Abilities:
Spidy sense, the screen will glow red if there is an enemy that the player cannot see that is near them, also directional( players and mobs)

Attacks:
Level 1:
Shoots webs when making spiderman symbol
Level 2:
Can web objects around the room/walls, which they can swing at enemies/players
Break on impact, so you can't use the same wall to hit 10 enemies
Level 3:
webs turn black, deal more damage

Visuals:
Overlay spiderman suit at level 2, venom suit at level 3
since its just the hands, and we don't have spiderman's face mask, i think this would avoid copyright stuff

Transformation effects:
N/A


### goku inspired
Name: ???
Defense: Making an x will make a bubble, the same color as the aura with flames

Attacks:
Level 1:
Shoots similar to the current repulsor blasts, buut these aren't spheres, they are "comet shaped" with a tail. Look up images of goku ki blasts for inspiration
Level 2:
Repulsor blasts upgraded
putting both hands together and charging makes a massive energy ball, that can be fired
Level 3:
When punching (in real life), it will spawn a dragon fist that travels in the direction you punched

Visuals:
Use fire 2 aura, start with none, then to yellow, then to blue. No other transformation efffects

Transformations:
2->3 makes blue spherical aura

### star wars inspired
Name: ???
Defense: Making an x will make a "glassy" bubble

Attacks:
Level 1:
Gun which shoots red lasers
Level 2:
lightsaber laser sword, default blue color, which deals damage when the blade hits enemies. 
THe player makes a fist, and the lightsaber will extend out of their fist as if they were holding it
Level 3:
When making a cup with the hand, like "force choking" they can pick up objects around the room and throw them, similar to spider man

Visuals:
lighsaber should glow like current lasers do
level 2: they have the current aura 3  w/o halo(circle under feet), which gets brighter at level 3 they also get the halo

Transformations:
N/A


### mythological inspired
Name: ???
Defense: Making an x will make a shield appear infront of them (like a knights shield)

Attacks:
Level 1:
Finger gun is like a crossbow, which takes 2-3 seconds to reload, can be on both hands
Level 2:
Bow and arrow: butting both fists together far away from the body starts a bow, which can be drawn by moving one fist back, then shooting the arrow. Show a indicator to show where the arrow will go
Level 3:
Smoke bombs, which will obscure the vision of other players
Arrows become peircing aswell, for PVE

Visuals:
For the person, they will get solder gauntlets at level 2, and for everyone, it will look like they have wings at level 3

Transformations:
On the wing transformation, they will send a white, circular shockwave

### magic inspired
Name: ???
Defense: Making an x will make a thin bubble shield

Attacks:
Level 1:
They will have fireballs, similar to repulsors
Level 2:
Holding palm out will shoot flames like a flame thrower
Level 3:
Making a triangle symbol with both hands will place an aimable aura circule on the floor. WHen they collapse their fingers together, it will shoot a fire pillar above where their hands were, dealing damage


Visuals:
They wil have the current aura which has the star underneath at level 2 and level 3

Transformations:
Circular, fire shockwave on both transformations

### elementalist
Name: ???
Defense: Making an x will make a wind shield

Attacks:
Level 1:
Pushing out with a palm will shoot a gust of wind, which pushes enemies back and deals damage
Level 2:
Holding hands out flat (like palpatine from star wars) will shoot lighting that chains to enemies, Its a hold attack like the lasers, but after 5 seconds it does away
Level 3:
using both hands to make a swirling motion charges a tornado, when the user pushes out with both hands, this shoots the tornado which does damage and sucks enemies together

Visuals:
pull from online as usual, the idea is to be similar to minecraft's breeze/wind charges, btd6's druid, so pull those images as reference, but obviously fit the theme


Transformations:
at level 2, they get a white aura around them that swirls like a tornado. At level 3, the tornado swirls faster and is denser. 

### Earth inspired
Name: ???
Defense: Making an x will make a rock shield

Attacks:
Level 1:
Similar charge up attack as the ki blasts, but these will shoot rock orbs. Make them brown with orange/red cracks. Pull a 3d model from online
Level 2:
making a fist at around waist level, then quickly raising it above elbow level will make rock spikes pop up from out of the ground, with 3 total, each with a radius of about 1 ft, getting slightly larger and taller as they go
Level 3:
Make it so that the rocks stick to enemies it hits, having visible rocks on them. THen, when the player snaps, the rocks explode dealing damage.


Visuals:
try and make similar rock models around the hands, similar to the MCU's thing

Transformations:
At level 2 the player has a crater aura following them on the ground. At level 3, they get a swirling rock aura made of small rock particles with hints of gold and silver  




### Summoner
WIP

## Attacks with no character yet

throwing swords at people

somtehing with snapping

clapping power

elemental


# Future work
Drawing 3d models over people is hard, but maybe a future task


# NO headset work
- setup blender and tripo mcp to generate 3d models
- Make more 3d model variants for different attacks, things don't look that good right now
- brainstorm gameplay designs
    - improve enemy spawn locations
    - 3d models for bosses, ect
    - Each player has healing ability
- Change camera to 3d person perspetive

# Claude's Clarifications

Moved to `claude_questions.md`, where Q1-59 are all answered. Any question without an answer takes its default.

# Claude's feasibility notes

**Update 11/7:** you consider the plan feasible, since 5 hours a day is a lower bound. The target is now all 5 characters, in the order PVE solo, then co-op, then PVP. `game_schedule.md` has the day plan. The pivot checkpoint is the evening of 11/11: whatever isn't on track by then gets cut.


**Short version: the whole plan does not fit in 11/7-11/16. About half of it does.** That's 10 days at about
5 Claude-hours a day, so ~50 hours. Coding hours aren't the tightest limit, though. The real limits are:

- **headset test time:** only you can check feel, gestures and how things look in the headset
- **multiplayer testing:** it needs two headsets and two people

Rough estimates (Claude hours, not counting your test time):

| Piece | Hours | Risk | Notes |
|---|---|---|---|
| Game core: HP, damage, meter, levels, death/respawn, rounds | 4 | low | plain code, unit-testable |
| Gesture library (X, double OK, finger gun, fist, cup, triangle, bow, punch, charge, power-up) | 5 + tuning | **med** | each one needs your hands to tune thresholds |
| Menus: start, mode, room code, character select, ready, countdown, pause, settings | 5 | low | |
| HUD + overhead bars | 2 | low | |
| Templates: shields (3-4 variants), ring/sphere shockwave, gun, comet blast | 7 | low | lasers, blasts, shockwave and auras already exist |
| Tight aura (segmentation) | 4 | **med** | depends on Q38 |
| Enemies: robot + drone, AI, spawn bubble, lasers, Space Setup bounds | 6 | med | needs CC0 models |
| Walls block attacks (saved room + live depth) | 2 | low | live depth already works for orbs and lasers |
| Powerups | 2 | low | |
| Room objects: proxies, pick up/throw, web pull | 5 | med | |
| Each character (3 levels + visuals + transformations) | 4-6 each | med | 5 characters ≈ 25 |
| Multiplayer: LAN lobby, colocation, sync players/shots/damage/auras, PVP + co-op | 15-20 | **high** | nothing networked exists today, and it can't be tested alone |
| Sound, balancing, performance at 72 FPS, bugfixing, video build | 6 | | |
| **Total** | **~90-100** | | about 2x the budget |

**What I'd commit to for 11/16:**

- **Must:**
  - single-player PVE with waves, 2 enemy types, powerups, shields and transformations
  - menus, HUD and pause
  - **3 characters** with all 3 levels: Technoman, Ki fighter, and Mage or Saber
  - walls blocking attacks
- **Should:** 2-player colocated PVP over LAN. Networking changes how everything else is written, so the game
  core will be built host-authoritative from day 1 (single player is a host with nobody joined), and I'll spike
  colocation on day 2. **Go/no-go on the evening of 11/9:** if the spike isn't working by then, multiplayer
  moves after the deadline and that time goes to characters and polish.
- **Could:** a 4th character, co-op PVE with revives, object interaction beyond force throw, tight aura.
- **After the deadline:** characters 5-10, Spider suit overlays, smoke bombs, the Summoner.

**Proposed days** (these go into `game_schedule.md` once you've answered):

| Day | Claude | You (headset) |
|---|---|---|
| 11/7 Sat | commit current work, game core, gesture library foundation | answer questions |
| 11/8 Sun | menus + HUD; multiplayer spike (LAN + colocation) | gesture tuning pass |
| 11/9 Mon | Technoman L1-3; shield variants (round 1) | pick shields; **MP go/no-go** |
| 11/10 Tue | enemies, spawning, powerups, walls block | first full PVE run |
| 11/11 Wed | Ki fighter; visuals round 2 | pick variants |
| 11/12 Thu | Mage or Saber; room objects / force throw | |
| 11/13 Fri | multiplayer gameplay (if go) or 4th character | 2-headset test |
| 11/14 Sat | balance, sound; **feature freeze** | long playtest |
| 11/15 Sun | bugs, 72 FPS performance, clean repo | playtest |
| 11/16 Mon | submission build, video shot list | record video |

**The biggest risks, in order:**

1. multiplayer colocation on our OpenXR stack (unproven)
2. gesture reliability: false triggers between similar hand shapes in a fast fight
3. the store/submission process timeline (Q1)
4. frame rate once enemies, auras and the camera tracker all run together (tracking alone holds 72 FPS today)


Since its a video demo, I think that focus on make 3-4 characters really good. I'll see how the timeline goes, but I think the current work is doable