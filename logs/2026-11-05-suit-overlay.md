# 2026-11-05: Suit overlay on a real person

**Where it stands:** Iron Man and Spider-Man fit a person in a recorded headset clip, and on the Quest both hold
72 FPS with tracking at about 17 runs a second. That's replayed pictures on a still headset. Nobody real has
worn a suit yet.

Branch: `quest-suit-overlay`.

## How it works

1. **Find the person:** BlazePose's detector finds someone. It searches the whole image and three zoomed
   windows in turn, because someone far away is too small to find otherwise.
2. **Read the body:** BlazePose's landmark model reads 33 joints from a square crop around them. Each result
   aims the next crop, so the slower search only runs to find someone new.
3. **Place it in the room:** live depth gives the distance, or body size if depth has no answer. Each joint
   sits on its own line of sight, so the suit lines up with the person.
4. **Pose the suit:** `SuitRig` is a port of the web version's `suitmodel.js`, without its mirroring.
5. **Glide between bodies:** bodies arrive less often than frames, so `SuitView` smooths the joints and
   reaches ahead to make up for how old each body is.
6. **Draw it:** `Suit.shader` is one light and a simple room reflection. It hides behind real things only if
   they're at least 30 cm in front of the suit, because the wearer is in the depth too.

## Test results, on the PC

Test clip: 13 seconds recorded in the headset, filmed in a mirror, with the headset hiding the face. A Unity
editor run on a GTX 1650 PC, so these timings **don't predict the Quest**.

| Setup | Frames tracked of 132 | Landmark run |
|---|---|---|
| Lite model, CPU | **98** | 17 ms |
| Lite model, GPU | 96 | 16 ms |
| Full model, CPU | 89 | 21 ms |

The lite model did at least as well as the full one here, so the game uses it. The missed frames at the start
and end are mostly where the Home menu covers the view. Only one clip was tested, and nobody turned their back.

| Suit | Triangles before | After | Vertices |
|---|---|---|---|
| Iron Man | 639,859 | 93,213 | 92,014 |
| Spider-Man | 195,498 | 99,985 | 60,512 |

## Test results, on the Quest

Each run was about 22 seconds, with the headset on a desk and the recorded clip replayed through the tracker. No
hands were in view and targets were cleared, so nothing else in the game was running.

| Style | FPS (target 72) | Tracking runs a second | Each run | Holds up the frame |
|---|---|---|---|---|
| Iron Man | 72 | 17 | 45 ms | 3 ms |
| Spider-Man | 73 | 18 | 43 ms | 3 ms |
| Spike Shells | 73 | 4 | 49 ms | 3 ms |
| Fire Shells | 73 | 4 | 49 ms | 3 ms |
| Suit style, live camera, nobody in view | 72 | 18 | 40 ms | 4 ms |

The first run showed 61 FPS, but that was the test: it decoded a JPEG on the main thread for each run, which took
12-19 ms. Pictures are now decoded once at the start. Reading the live camera holds up the frame for only 2 ms.

## What went wrong along the way

- **Tilted crops:** the detector's idea of up depends on the face, and the headset hid it, so the crop came out
  on its side. Crops now start upright, then follow the real hip-to-shoulder line. This took tracked frames
  from 29 to 48.
- **Too small to find:** searching zoomed windows took it from 48 to 98.
- **Left-handed maths:** Unity's handedness flipped two cross products from the web version, which put the
  head upside down.
- **Wrong arm that wasn't:** bones were within 1-3 cm of the joints, but the mesh drew old poses. Skinned meshes
  refresh in the game loop, which batch rendering skips. Only the test tool was wrong.
- **JPEG format on the headset:** a loaded JPEG is stored differently on Android than in the editor, so reading its
  raw data ran off the end and crashed the game. `BodyTracker.CopyPicture` reads colours instead.
- **Non-VR build changes `QuestURP.asset`:** it strips VR shaders, which could break the real game.
  `BuildBench` now puts the file back.

## Keeping suits private

Models and everything made from them are in `unity_game/Assets/Suits`, which git ignores. `SuitLab.Prepare`
rebuilds them from `assets/models`. Footage and frames are in the ignored `Logs` folder.

**`suit.glb`, `spiderman.glb` and `xbot.glb` are already in the history on GitHub's `main`**, from before this
work. No commit on this branch changes them. Removing them from GitHub means rewriting history.

## To run it

1. Put the headset on, or move it around for a few seconds so it finds its position.
2. Select AURA and light it on someone at least 1.5 m away.
3. Make an OK sign until the HUD says IronMan or SpiderMan, styles 6 and 7.

For a test with nobody in the headset, push pictures to the app's `files/frames` folder and write
`2.0 6 #FF8000 replay` to `files/aura_test.txt`. Remove that file before playing. `RepulsorBench` is a separate
non-VR benchmark app, but it stalls with nobody wearing the headset, because the headset stops drawing its window.

## Not known yet

- Speed with a real person, a moving head and the weapons all in play
- How the headset's temperature affects speed over a long session
- Whether the suit lines up in real depth. Only flat video was tested, with an assumed camera field of view
- How well it hides behind hands and furniture
- Whether it holds someone facing away or side-on. This detector leans on the face and upper body
- How much it lags with someone real. Tracking now runs about 17 times a second, against 4 for the aura
- The camera lens offset in `PersonDetector` is an estimate

## Suit offset and soft edges

First worn test: the suit followed the person but sat off them. Cause: the camera looks 11.1 degrees below the head (`android.lens.poseRotation` in `dumpsys media.camera`) and the code assumed 0, about 0.59 m too high at 3 m. Hips read 1.45 m above the floor, which fits. Lens position and scale were already right. Fixed with `PersonDetector.LensRotation`.

Hand edges over the ball were blocky from one depth sample per pixel. `EnvironmentDepth.hlsl` now tests four depth pixels and blends the results. Checked in a PC render with a fake hand (`LaserPreview.RenderOcclusion`). 65 of 65 tests pass. Neither fix has been seen on the headset, and frame rate with them hasn't been measured.

Later that evening, after hearing the hands glitched and the person showed behind the suit: `JointSmoother` steadies joints by speed and holds unseen ones against their shoulder or hip, `SuitRig.PoseHand` turns hands to the knuckle points, up to 80 degrees from the forearm. A 3 cm mesh padding was tried and removed: the user found it far too puffy. On the clip at an assumed 18 Hz, wrist jumps went from 39 to 25 cm at the 95th percentile and 82 to 50 cm at worst. Both models already rest with curled fingers, so don't add curl. The person's outline will likely still show. The tracker lost the body in 34 of 132 clip frames. 68 of 68 tests pass. Not yet seen on the headset.
