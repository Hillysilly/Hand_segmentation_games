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
Force powers, similar to the star wars one
Level 3:
Making a triangle symbol with both hands will place an aimable aura circule on the floor. WHen they collapse their fingers together, it will shoot a fire pillar above where their hands were, dealing damage


Visuals:
They wil have the current aura which has the star underneath at level 2 and level 3

Transformations:
Circular, fire shockwave on both transformations


### Summoner
WIP

# Future work
Drawing 3d models over people is hard, but maybe a future task

# Claude's Carifications

Answer inline under each question (or just reply with numbers). Every question has a **default**, which is what
I'll do if you don't answer, so "defaults OK except 4, 9, 18" is a fine reply.

## A. The competition

1. Which Meta challenge is it, and do you have a link to its rules? I need to know:
   - whether it requires a Horizon Store / App Lab submission. Store review can take days, so the build might
     have to go in before 11/16.
   - what has to be submitted: video, screenshots, description, build. 
   - whether specific Meta features are required or score extra (Meta XR SDK, shared spatial anchors, Scene
     API). We use OpenXR + Unity's Meta package rather than the Meta XR SDK, so check that this is allowed.
   - **Default:** I'll assume an APK plus a video, with no required SDK. 

I think any development, unity is fine on the website, I think its only video + github and writeup
2. Will judges play it alone, or watch a video? If they play alone on one headset, single-player PVE is what
   gets judged, and multiplayer mostly shows up in the video. That should set the priority between modes.
   **Default:** judges play solo, so single-player PVE comes first.
Based of video I think, they won't be playing it

3. Is the trailer/demo video part of "done by 11/16", and who records it? **Default:** you record it on 11/16,
   and I write a shot list.
No, code mostly by 11/16, I'll get 2 days

## B. Scope

4. The plan says ~10 characters, but describes 5, plus a Summoner marked WIP. How many for 11/16?
   **Default:** 3 characters fully finished, with the rest after the deadline. See the feasibility notes below.
I think  the current 5 are all feasible

5. What order should the characters come in? **Default:** Technoman (most of it exists), Ki fighter (its aura
   exists), Mage, Saber/Force, Archer, Spider.

Sure
6. What order should the modes come in? **Default:** PVE solo, then PVP with 2 players, then PVE co-op.
PVE, then co-op

7. What's the maximum number of players? **Default:** 2. That's also all we can test.
Yes, 2 is fine


## C. Multiplayer

8. Do you have a second Quest 3 now, and a second person who can test with you this week? Multiplayer can't be
   checked without both. **Default:** none. If so, multiplayer drops to a stretch goal.

Will get in 2 days, so do the singleplayer first
9. Will players always be in the same physical room? **Default:** yes. Both headsets on the same Wi-Fi, LAN
   only, no cloud accounts, and the "room code" is a 4-digit code found over the LAN. Playing over the internet
   would need Unity Relay/Lobby (a Unity Gaming Services account), and "walls block attacks" stops making sense
   when players are in different rooms.

Yes, same physical rooms, so LAN is safe assumption 
10. How should the two headsets agree where things are (colocation)? Options:
    - Meta shared spatial anchors. Both players need Meta accounts with sharing enabled, and I still have to
      confirm that Unity's Meta OpenXR package supports anchor sharing on our versions.
    - Manual calibration: both players touch the same real point (for example a table corner) with an index
      finger, then face the same way.
    - **Default:** spike shared anchors on day 2, and fall back to manual calibration if they fight us.

Ok, default sounds good

11. How should other players see your aura, shield, wings and so on? Options:
    - (a) your headset sends your head and hand poses, and the body is estimated from those
    - (b) their headset finds you with the camera tracker (BlazePose, which now fits suits on the PC replay)
    - (c) a mix: network for placement, camera for the tight aura and suits
    - **Default:** (c). Network-only is the reliable baseline, and the camera adds detail when it locks on.

Default is good

12. In multiplayer, does "pause" pause the game for everyone, or only open your menu while the game goes on?
    **Default:** it only opens your menu, and the game doesn't stop. Only the host can change gameplay settings.

Default is good


## D. Gameplay rules

13. What does a PVE game look like? Waves? Endless? A timer? What's the win condition? **Default:** 5 waves,
    each bigger than the last, then a win screen with a score. A run lasts about 5 minutes.

Default is good

14. Lives and respawn: where do you come back, and what do you see while dead? **Default:** 3 lives and a
    5-second timer, respawning where you are. The view dims to grey while you're dead.

Come back where you are physcially in the room, the default is fine for respawn

15. Reviving teammates: how does it work? **Default:** a player at 0 HP is downed for 15 seconds. A teammate
    reviving them holds a hand over them for 3 seconds. Otherwise they lose a life.

Don't do this actually

16. PVP rounds: does a round end at 0 HP? Do transformation levels reset each round? **Default:** yes and yes,
    best of 3.

Default

17. Healing: the plan mentions passive healing after a while out of combat, and also "heals are on a 30s
    cooldown". Is there an active heal ability (which gesture?), or is the 30 s about the passive healing?
    **Default:** passive only. After 5 s without dealing or taking damage, you heal 3 HP a second. No active
    heal.

Passive only, active healing was an idea I cut out

18. Transforming: once the meter is full, what does the player do to transform? **Default:** a "power up" pose,
    with both fists clenched at your sides held for 1 second. It's easy to detect and looks good in a video.

Yes, powerup pose, I forgot to say

19. Does the meter fill only from damage you deal, or also from damage you take? Does dying lose a level?
    **Default:** only from damage dealt (enemies and players). Dying drops you one level, never below 1.

Dying goes to 0, only damage delt

20. HP: "everyone starts at 100 HP" sits next to "players have more HP so games last ~5 minutes". **Default:**
    show 100 HP and scale damage to fit, at about 2-5 HP per hit, so a 1v1 lasts several minutes. Level 2 =
    150 max HP and level 3 = 200, as written.

Default is good
21. The shield (X with arms): does it block attacks from every direction? Can you attack while holding it? What
    are the hold limit and cooldown? **Default:** a full bubble that blocks everything, with no attacking while
    held. It lasts 3 s of holding, then breaks for a 6 s cooldown, and recharges while not held (like Smash).

Default is good

22. Is there friendly fire in co-op? **Default:** no.

By default, not, can turn on

23. Walls block attacks: should this use the room from Meta's Space Setup (the saved mesh and boxes), live depth,
    or both? **Default:** both. The saved room decides hits, and live depth hides the visuals. That means
    players must have done Space Setup, and the game will ask them to run it if they haven't.

Both, as long as no performance issues. What is currently there with the iron man lasers is pretty good

24. Robots "walk toward the player": should they path around furniture? **Default:** simple steering that slides
    around Space Setup boxes, with no full navmesh. Drones fly straight at you and dodge sideways a little.

Start simple with the default, may update

25. Powerups: how are they picked up? How often do they appear, and for how long do they last? **Default:**
    touch one with a hand. One appears every 30-45 s within 2 m of a player. Invincibility lasts 8 s, 1.5x
    damage lasts 10 s, and "power charge" adds 30% to the transformation meter.

Default is good

## E. Gestures and controls

26. "X with your arms": hand tracking only gives hands. It can't see elbows. **Default:** wrists crossed in front
    of the chest, with forearms (estimated from wrist direction) crossing and palms facing inward. Please
    check that this is the X you mean.

This X is fine

27. Today the OK sign switches aura style and palm-to-face opens the colour wheel. These are test gestures, and
    the OK sign clashes with the new pause gesture. **Default:** remove both from the game. Colour moves to the
    character-select screen, and the test gestures stay only in a dev build.

Add a debug menu with all the old stuff, with those controls, switch the new controls to default

28. How should players press menu buttons? **Default:** poke them with an index finger (panels within reach),
    with a hand ray plus pinch as a fallback for far panels.

Poke, make it similar to how the keyboard works in VR, with the snap/hollow

29. Finger gun: does "rotate up to fire" mean an upward flick of the wrist, like recoil? **Default:** yes. The
    shot goes along the direction the finger pointed just before the flick, blended with the
    shoulder-to-hand line.

Yes, also show a blue line for where you would be shooting. Use where the arm is pointing aswell 

30. Ki fighter. **Default:** for the level 2 charge ball, hold your hands together to charge (up to 2 s) and
    push both hands forward fast to fire. For the level 3 dragon fist, make a fist and punch faster than about
    2.5 m/s.

Sounds good

31. Archer bow. **Default:** touch both fists together at arm's length to make the bow. The front fist holds it,
    the back fist pulls the string, and opening the back hand shoots. Does that match what you meant?

Yes

32. Mage triangle. **Default:** making a triangle with both thumbs and index fingers puts the circle on the floor
    where the line from your head through the triangle lands. Moving your hands aims it. Closing both hands into
    fists (the "collapse") fires the pillar.

YEs

33. Force pick-up (Saber level 3, Mage level 2). **Default:** a cupped hand pointed at a room object picks it up,
    it follows the hand, and a fast throw motion launches it. Does Mage level 2 do the same as Saber?

yes

## F. Visuals and HUD

34. In VR, a HUD fixed to the bottom-right of your view is uncomfortable and partly out of sight. Options:
    - (a) a panel at the lower right that lazily follows your head
    - (b) a display on the back of your left wrist
    - (c) both
    - **Default:** (a), with the HP bar styled like a fighting game.

a) sounds good

35. Overhead health bars only show when you look at someone's head. Performance isn't really a concern here,
    since these bars are cheap. Is the point to cut clutter? **Default:** show a bar while their head is within
    about 25° of your gaze, and also for 2 s after they're hit.

Yes that sounds good. For enemies, only show if they aren't at full hp + condition

36. Seeing your own aura: you can't see your own body through the camera. **Default:** a fainter aura around your
    hands and forearms, a ring at your feet, and a faint edge glow in your view. OK?

Like the fire aura right now, you should be able to see that, which seems like what is described

37. Aura names. The current styles are 1 Fire Shells, 2 Spike Shells, 3 Light Halo, 4 Sun Halo, 5 Rune Circle.
    Here is how I read the plan:
    - "Fire aura 1 and 2" = Fire Shells and Spike Shells
    - Ki fighter's "fire 2" = Spike Shells
    - Saber's "current aura 3 w/o halo" = Light Halo
    - Mage's "aura with the star underneath" = Rune Circle
    - Please confirm.

Sounds good

38. Tight aura: the BlazePose landmark model we already run may also output a segmentation mask, which would make
    the tight aura nearly free. I still need to verify that for our model files. If it doesn't, the fallback is
    a glow along the tracked skeleton. Is a skeleton glow acceptable if the mask doesn't work out?
    **Default:** yes.

Default

39. Visual variants (3-4 per item). **Default:** a "Variant Lab" scene in the build. You cycle A/B/C/D with the
    menu, and I also send PNG renders. You write the letter you pick in game_schedule.md. Batches will be
    grouped so that one headset session judges several items.

Sounds good

40. Spider character, IP concern: even with no mask, the red/blue webbed suit and the black suit with a white
    spider are recognisable Marvel designs. The same goes for the names, the lightsaber hum and the "Kamehameha"
    look. For a public competition entry, I'd make original designs: for example a green/black web-line suit,
    an energy blade with our own sound and colours, and original names. **Default:** original designs and
    names, inspired by the archetype only. (Note: `suit.glb` and `spiderman.glb` are in git history, so the
    submission should come from a clean repo.)

For spiderman, you only see the hands since we can't do body segmentation, so its just webbed hands which I think, though it takes inspiration, isn't enough for errors. 

Lightsaber is just a laser sword, like in beat saber, so I think thats fine

Don't say kamehameha obviously, but its just an energy blast.

Original names are good

41. Technoman's level 2 gauntlets and the "nanotech like the webapp" effect: I need a gauntlet model we're
    allowed to use. **Default:** search for a CC0/CC-BY sci-fi gauntlet. Otherwise I'll build plated armour
    on your tracked hand joints in code (I can make 3 variants of that).

do the Default, I really don't want claude to build since historically my experience is that it isn't up to standard 

42. Enemy art style: realistic or stylised low-poly? **Default:** stylised, from CC0 packs (Quaternius/Kenney
    style). They look consistent and come with animations.

Default

43. Sound: the plan mentions volume, so SFX and music? **Default:** CC0 sound effects for every attack, hit and
    spawn, plus one menu music track. Credits go in a CREDITS file.

Default


## G. Room objects

44. Objects come from Meta's Space Setup: tables, couches, storage, screens, lamps, plants and so on, each as a
    labelled box. A trashcan only exists if someone added it during setup, and most small objects won't
    have been. Is it OK to use only what Space Setup reports? **Default:** yes.

Default
45. Spider level 2 "web objects/walls and swing them at enemies": does this mean pulling a room object along the
    web and flinging it at a target, where it breaks on impact? **Default:** yes. Webbing a wall just anchors a
    web, which is a visual only, since the player can't really swing.

Yeah, no swinging

46. When a proxy object is picked up, the real one stays visible where it was, as the plan says. **Default:** no
    masking, and the proxy gets a faint outline so it reads as "the copy". OK?

NO masking yet


## H. How we work

47. How many headset test sessions a day can you do, and when? This, not coding time, is the real bottleneck.
    **Default:** two short sessions a day (about 15 min each), with test lists ready beforehand.

Probalby 1-2 hrs of testing each day

48. Can I build and install to the headset on my own whenever it's plugged in, or only when you say so?
    **Default:** on my own, but I'll never force-stop the app without checking that nobody is playing.

Default, usually headset won't be used

49. Git: `quest-suit-overlay` has a large amount of uncommitted work. **Default:** commit it as-is, then start a
    `v1-game` branch for this week.

Yeah start game branch

50. Logs: **Default:** one file per day in `logs/`, as now, and `unity_game/game_schedule.md` with these
    columns: Done (by Claude) / Good enough (Claude checked) / Checked by you.

Ok

# Claude's feasibility notes

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