# Repulsor Test (Meta Quest 3)

A mixed-reality test game and the start of the Quest version of the webcam prototype in the parent
folder. You see your real room through passthrough, orange orbs float in it, and you shoot them with
your bare hands. An "OK" sign (thumb and index pinched, other fingers up) switches weapon:

- **Repulsor**: make a fist to charge, snap your hand open to fire a shot from your palm.
- **Laser**: hold your palm out; a beam burns through whatever it stays on.
- **Shockwave**: charge and fire like the repulsor; a widening ring destroys everything it reaches.
- **Aura**: hold your palm at someone or something to light flames around it, and they follow a person who moves. Hold a fist to put them out.
  It stays where you lit it, because it doesn't track people yet.

These follow the rules in `js/powers/ironman.js`.

## Requirements

- Unity 6000.6.7f1 with Android Build Support (includes the SDK, NDK and JDK)
- Quest 3 in Developer Mode, connected by USB, with USB debugging allowed
- Hand tracking on: headset **Settings > Movement tracking > Hand and body tracking**

## Build and run

```powershell
.\build_and_run.ps1             # build, install, run
.\build_and_run.ps1 -NoBuild    # reinstall the last build
.\build_and_run.ps1 -Log        # then follow the game's log
```

Close the Unity Editor first, because only one Unity can have the project open. From inside the
Editor, use **Repulsor > Build APK**, or **File > Build and Run** with the Quest selected.

To play it again later, find **Repulsor Test** in the headset's library under **Unknown Sources**.

## Tests

Gesture rules and hit detection have no XR dependency, so they run without a headset:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe' -batchmode -nographics `
  -projectPath . -runTests -testPlatform EditMode -testResults Logs\tests.xml
```

Or in the Editor: **Window > General > Test Runner**.

## Project layout

| Path | What |
|---|---|
| `Assets/Scripts/RepulsorGesture.cs` | charge and fire rules, fist and open classification |
| `Assets/Scripts/HandRepulsor.cs` | reads one tracked hand, fires, draws the charge orb and aim line |
| `Assets/Scripts/AimLock.cs` | remembers the fist's aim, because tracking jumps as the hand opens |
| `Assets/Scripts/Weapons.cs`, `Shockwave.cs` | weapon list, pinch to switch, laser trigger, shockwave front |
| `Assets/Scripts/LaserBeamView.cs`, `RoomDim.cs`, `Assets/Shaders/` | the laser's look: beam, burst, sparks and room dimming |
| `Assets/Scripts/RoomSense.cs` | finds real surfaces from live depth, so the laser stops at them |
| `Assets/Scripts/AuraView.cs`, `Assets/Scripts/AuraTracker.cs`, `Assets/Scripts/ColorPicker.cs`, `Assets/Shaders/AuraShell.shader`, `Assets/Shaders/AuraHalo.shader` | five aura looks to compare (OK sign while lit) in any colour from a hand-worked wheel, cut by live depth, that follow a person found by a model |
| `Assets/Scripts/RepulsorGame.cs` | targets, score, HUD, sound |
| `Assets/Scripts/Blast.cs`, `ElectricBall.cs`, `Target.cs`, `GameMath.cs`, `Sfx.cs` | comet shots, the crackling ball, orbs, hit math, synthesized sounds |
| `Assets/Editor/ProjectSetup.cs` | configures the project and generates the scene, prefabs and materials |
| `Assets/Editor/QuestBuild.cs` | APK build |
| `Assets/Tests/EditMode/` | unit tests |

**Repulsor > Set Up Project** regenerates `Assets/Scenes/Main.unity` and the prefabs, so running it
again discards hand edits to those. Once you start editing the scene by hand, stop running it, or
split the scene generation out of it.

## Stack

Unity's own packages, on OpenXR: AR Foundation with Unity OpenXR: Meta for passthrough, XR Hands for
hand tracking, and URP on Vulkan. The Meta XR SDK isn't used.

The laser stops at real surfaces using the Meta Quest **Raycasts** feature, and the aura hides behind
real objects using **Occlusion**. Both read live depth, so they need the `USE_SCENE` permission but no
Space Setup. It only knows about what the headset can see
at that moment. The **Planes** and **Meshing** features add a saved room model, for surfaces outside
the headset's view, and these need a completed Space Setup.

## Debugging

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
& $adb logcat -s Unity    # the game's lines are tagged [Repulsor]
```

**Repulsor > Preview Laser** renders the beam to `Logs/laser_preview.png` without a headset. To see it
on the headset with no hands, run `adb shell setprop debug.repulsor.laserdemo 1` and relaunch the game
(development builds only). Clear it with the same command and `0`.
