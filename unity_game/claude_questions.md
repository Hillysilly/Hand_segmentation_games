# Claude's questions

Clarifying questions on `game_plan.md`, all answered as of 11/7. **Any question without a written answer takes its default.**

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

## Round 2 (11/7)

Same format: each question has a default.

51. You said PVE first, then co-op. Where does PVP go: third, or cut from v1? **Default:** third. Once co-op
    networking works, PVP is mostly a friendly-fire switch plus rounds, so about 2 hours.
52. "Dying goes to 0": does that mean the meter drops to 0 and you go back to level 1 (100 max HP)?
    **Default:** yes.
53. Co-op lives with no revives. **Default:** each player has their own lives, and the game ends when both are
    out. A player with no lives left watches in the grey view until the game ends.
54. Do abilities add up across levels? For example, does Saber at level 3 still have the finger gun and the
    sword? **Default:** yes, they add up. "Upgraded" replaces the older version of that same attack.
55. Some gestures aren't specified yet. Defaults:
    - Technoman lasers and repulsors use today's gestures.
    - The Ki fighter's comet blast and the Mage's fireball use today's repulsor gesture.
    - Spider webs: one web each time you form the spider sign, with a 0.4 s cooldown.
    - Archer smoke bomb: a closed-fist underhand throw, with a 15 s cooldown.
56. Spider suit on the *other* player: the camera tracker can now fit a full suit on another person (it works on
    the PC replay and holds 72 FPS on the Quest). Should the other player get a full suit with our own design, or
    should everyone get webbed gloves only? **Default:** you see webbed gloves on yourself. The other player gets
    a full suit when the camera locks on, as a stretch goal after co-op.
    No, other person gets nothing, I tested earlier and this doesn't work, it would be a end goal
57. Gauntlets: what if I can't find a sci-fi gauntlet model we're allowed to use? **Default:** skip the
    gauntlets, and show level 2 through a brighter hand glow and upgraded beams. Either way, you'll see the 3-4
    candidate models I find first.
    If you can't find, make one yourself is ok, I'll see how it use
58. Names. **Default:** I propose 3 original names per character in `game_schedule.md`, and you pick.
Ok
59. How do you open the debug menu? **Default:** a "Debug" button at the bottom of the pause menu. It holds:
    - the old-controls toggle (OK sign switches the aura, palm-to-face opens the colour wheel)
    - aura style cycling
    - the Variant Lab
    - an FPS overlay
    - buttons to spawn an enemy or a powerup
    - invincibility
OK
