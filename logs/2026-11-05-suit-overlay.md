# 2026-11-05: Suit overlay on a real person

**Where it stands:** Iron Man and Spider-Man fit a real person in a recorded headset clip, on the PC. The
whole path is built and installed in the game as two extra aura styles, but **it hasn't run on the Quest
yet**, so its speed there is unknown. The headset was still, so it refused to start apps.

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

## What went wrong along the way

- **Tilted crops:** the detector's idea of up depends on the face, and the headset hid it, so the crop came out
  on its side. Crops now start upright, then follow the real hip-to-shoulder line. This took tracked frames
  from 29 to 48.
- **Too small to find:** searching zoomed windows took it from 48 to 98.
- **Left-handed maths:** Unity's handedness flipped two cross products from the web version, which put the
  head upside down.
- **Wrong arm that wasn't:** bones were within 1-3 cm of the joints, but the mesh drew old poses. Skinned meshes
  refresh in the game loop, which batch rendering skips. Only the test tool was wrong.
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
3. Make an OK sign until the HUD says IronMan or SpiderMan, styles 7 and 8.

For a test with nobody in the headset, push pictures to the app's `files/frames` folder and write
`2.0 7 #FF8000 replay` to `files/aura_test.txt`. `RepulsorBench` is a separate non-VR benchmark app, but the
headset wouldn't start it while it couldn't track itself.

## Not known yet

- Speed on the Quest: the model, the suit, and whether it holds 72 FPS
- Whether the suit lines up in real depth. Only flat video was tested, with an assumed camera field of view
- How well it hides behind hands and furniture
- Whether it holds someone facing away or side-on. This detector leans on the face and upper body
- How much it lags. The aura took about half a second at 4 runs a second; suits ask for up to 20 a second
- The camera lens offset in `PersonDetector` is an estimate
