# 2026-11-04: Pose, mask and shortcut plan

Goal: put a suit or aura on a real person in the Quest 3 game. This is a plan: nothing here has been
built or tested on the headset. Speed figures marked as estimates are not measurements.

## Do these first

Cheapest and most informative first. Each one decides what happens next.

| # | Task | Needs | What it decides |
|---|---|---|---|
| 1 | **Commit `unity_game/` and `logs/`** | Nothing | Protects everything before any big change |
| 2 | **Green silhouette test:** compare the Space Setup room mesh with live depth and tint anything new green | Space Setup done, someone in the room | Whether a room scan gives a usable mask |
| 3 | **Time BlazePose lite on the Quest:** feed in camera frames, draw the 33 joints, log ms per run | One headset | Whether single-headset pose is realistic |
| 4 | **Meta SDK compatibility check:** try adding the Meta XR SDK to this project | Nothing | Whether body tracking and QuestRoomScan can be used |
| 5 | **Alignment test:** two headsets share room space and each shows a marker on the other's head | Second Quest | Whether multiplayer can work |

Then fix the aura using a real captured frame, and use the mask outline if test 2 worked. Drop the aura
if it still doesn't work after that.

## Which route for what

| Goal | Best route | Why |
|---|---|---|
| **Suit or aura on someone with a Quest** | Multiplayer | No camera AI at all: each headset tracks its own wearer |
| **Aura or suit on someone without a Quest** | Single-headset pose | Hardest route; depends on test 3 |
| **Clean body outline** | Room scan plus live depth | Cheap, no AI, and already separate per eye |
| **Erasing a person** | Textured room scan | Hard, and probably a visible ghost at first |

## Hardware reality

- **This laptop:** GTX 1650 (4 GB), Ryzen 5 4600H, 63 GB RAM. The web app tracks at about 34 FPS.
- **Quest 3:** Snapdragon XR2 Gen 2 with an Adreno 740, on battery, and it has to draw two eyes at 72 FPS.
- **Estimate:** the Quest is about 3-5x weaker in practice. The same web models would get about 7-11 FPS
  using the whole GPU, and realistically low single digits while the game runs. So it needs lighter
  models or the shortcuts below.

## Shortcuts

### Pose speed-ups (no training needed)

Combine 1-3, and add 4 only if it's still too slow. Together these might let the model run only 5-10
times a second while motion stays smooth at 72 FPS.

1. **Velocity prediction:** estimate joints from their recent speed. Runs every frame for almost nothing.
2. **Cancel head motion:** the headset knows exactly how the camera moved. Remove that first, so only the
   person's own motion is left.
3. **Adaptive rate:** run the model about 5 times a second when someone's still, and 15 when they move.
4. **Patch tracking:** follow small image patches around each joint between model runs.

Also crop to the old pose (BlazePose already does this), and always keep a regular full run so tracking
can't lock onto its own mistakes.

### Bigger shortcuts

- **Multiplayer:** each headset tracks itself and sends a skeleton. Removes pose AI entirely.
- **Depth instead of a person mask:** anything in front of the room scan is the person.
- **Bulky suit:** make it slightly bigger than the person, so it covers them with no mask or erasing.
- **Helmet:** it also hides the other player's headset.
- **Stream to the PC:** the headset sends its camera to this laptop, which tracks and sends results back.
  Adds network lag and only works near the PC.

### Hard, later

- **Tiny motion model:** learns motion from mocap data, for better prediction.
- **Old pose as model input:** see "Option 6" below.

## Single-headset pose pipeline

```
Camera frame + headset pose at capture
  -> find person and crop
  -> pose model (33 joints) + optional mask
  -> lift joints into 3D room space
  -> fix left/right (front/back)
  -> smooth, predict, lock bone lengths
  -> drive the skeleton (72 times a second)
  -> render with depth occlusion, person mask excluded
```

Steps up to the left/right fix run on a slower loop. The rest runs every frame.

1. **Capture:** one camera image with its timestamp and the camera pose at that moment. Using the current
   head pose instead would make the suit slide when you turn your head.
2. **Find the person:** run a detector only when needed. Otherwise crop around the old pose.
3. **Pose:** run in Sentis, split across frames so it never stalls rendering.
4. **Lift to 3D:** shoot a ray through the torso pixels and use real depth to get distance D, as a median
   of several points. Place each joint along its own ray at D + its relative depth from the model.
5. **Front or back:** `right = rightShoulder - leftShoulder`, `up = shoulderMid - hipMid`,
   `forward = Cross(right, up)`. There's no separate back mode. The model faces whichever way the
   left/right labels say, so wrong labels mean a backwards suit. Guard against swaps with:
   - Face visibility: a confident nose and eyes suggest they're facing you
   - Continuity: a sudden 180 degree flip in 100 ms means the labels swapped
   - Hips as a second vote
   - A short hold before accepting a real turn (`spinjitzu.js` debounces at 80 ms)
   - Side-on is the weak spot, so trust the last direction and the nose there
6. **Clean up:** One Euro filter (`filter.js`) in world space, lock bone lengths after the first seconds,
   pin feet to the floor, predict forward.
7. **Drive the skeleton:** port from `suitmodel.js`: `frame()` for the torso, `poseLimb()` for arms and
   legs, ears and nose for the head. **Remove the web version's mirror mapping**, or the suit will be
   inside out.
8. **Render:** a normal 3D mesh, so the back shows by itself. Use the person mask to ignore depth on the
   person, or their own depth cuts holes in the suit. Elsewhere use depth, so real objects hide the suit.

Risks: pose model speed, lag of perhaps 80-150 ms (estimate), side-on poses, hidden joints, depth errors.

## Pose models

| Model | Status | Notes |
|---|---|---|
| **BlazePose, Unity version** | **Try first** | Published by Unity for its inference engine. Apache-2.0. 256x256 crop, 33 keypoints. Detector 15.1 MB; landmarks lite 5.53 MB, full 12.8 MB, heavy 55.2 MB |
| **CenterTrack** | Backup | Takes the old pose as input, has a pose model, MIT code. Not built for mobile; datasets are mostly non-commercial; weights unchecked |
| **DeciWatch** | Partly useful | Public code, but it fills in between sampled frames, which needs a later frame and adds lag |
| **RTMPose, MoveNet** | Alternatives | Work frame by frame, with no old-pose input |
| **HMD-Poser** | Not for this | Tracks the wearer, not someone else |

To check for the Unity model: that it works with the installed `com.unity.sentis` 2.2.0, and whether it
includes the 3D world joints and the mask. Its card only describes the keypoints.

### Option 6: model takes the old pose as input

- A real research approach, and it should also reduce left/right swaps.
- Worth it for learning or a portfolio. Not worth it just to finish the game.
- A possible gap, not confirmed by a proper review: third-person tracking from a headset, using known
  camera motion, running live on the device.
- First experiment: add old-pose input channels to an existing small model, train on simulated data, and
  see if it matches the original's accuracy at a much smaller size. Needs a rented GPU.
- It has to beat the no-training shortcuts above, or it isn't worth doing.

## Masks

In the web app the mask comes from the pose model itself (`outputSegmentationMasks: true`), so they run as
one step. The heavy parts around it are reading it back from the GPU and the cloak logic. The split was
never measured, and could be by running the web app with masks on and off.

| Job on the Quest | Difficulty |
|---|---|
| Rough shape from depth inside the person's box | Easy to medium |
| Mask from a model | Medium |
| Lining up with each eye (camera-based mask) | Hard |
| Keeping up with movement at 5-10 FPS | Medium to hard |
| Sharp edges | Hard |
| Erasing the person | Very hard |

**What a mask is for here:** stopping a person's own depth from cutting holes in their suit, and giving the
aura a real outline. It can't remove someone, because the game can't edit passthrough.

## Room scan idea

Scan the empty room, then treat anything closer than the scan as a person:

```
if live depth is closer than the scan by more than a few cm -> person
otherwise -> room
```

**Gets easier:** no AI for the mask, it runs at the depth rate, furniture is ignored, and eye alignment is
mostly solved because depth comes as one slice per eye with its own projection.

**Stays hard:**
- Edges are only as sharp as the depth, so fingers and hair will be blobby
- Feet on the floor and people leaning on walls partly vanish
- Scan errors may be larger than the threshold (the main thing test 2 checks)
- Moved furniture or pets count as people unless pose tracking filters them

### QuestRoomScan

An open-source (MIT) Quest 3 scanner that gives a textured, saved, re-anchored room mesh, which loads in
under a second. It has no person mask or runtime depth comparison, so that part is ours to build. It needs
Meta's SDK and MRUK 205+, Unity 6.2+ and the Quest 3 depth sensor. Use it if the Space Setup mesh is too
rough, or to try erasing people, which would still need passthrough brightness matching.

## Multiplayer plan

Each headset tracks its wearer with Meta's body tracking and sends joints to the others.

1. Alignment test: colocation discovery plus shared anchors, showing a marker on the other's head
2. Add the Movement SDK and confirm it works with this project (main technical risk)
3. Send the skeleton and draw it as a stick figure on the person
4. Move the aura onto the body, then the suit, then shooting each other

**Bonus:** each person's own mic can detect their yell, so the flex-and-yell power-up works, and the aura
follows people automatically.

**Limits:** everyone needs a Quest, you only see your own arms and body, Wi-Fi adds a little lag, and legs
are less reliable.

**The second headset needs:** Developer Mode, USB debugging allowed for this PC, the same Wi-Fi as the
first, and Enhanced Spatial Services on. Someone has to wear it, or you swap between them.

## Facts to remember

- The game **can't edit passthrough**, only draw over it.
- **No measured Quest speed** was found for any pose or mask model. The one Quest segmentation paper was
  simulation only, so test 3 still has to happen.
- Don't publish `suit.glb` or `spiderman.glb`; credit doesn't cover Marvel's designs. They're in git
  history, so a public release needs a fresh repo.
- "Open Reality" wasn't found in search, so it needs a link or a different spelling.

## Sources

- [Unity: BlazePose for Inference Engine](https://huggingface.co/unity/inference-engine-blaze-pose)
- [QuestRoomScan](https://github.com/genesisinteractive/QuestRoomScan)
- [CenterTrack](https://github.com/xingyizhou/CenterTrack) and its [paper](https://www.ecva.net/papers/eccv_2020/papers_ECCV/papers/123490460.pdf)
- [DeciWatch](https://arxiv.org/pdf/2203.08713)
- [BlazePose paper](https://arxiv.org/pdf/2006.10204)
- [HMD-Poser](https://arxiv.org/pdf/2403.03561)
- [Efficient online multi-person 2D pose tracking](https://typeset.io/pdf/efficient-online-multi-person-2d-pose-tracking-with-gkdm42u7ly.pdf)
- [Pose2Sim](https://github.com/perfanalytics/pose2sim)
- [Quest 3 compositing feasibility study](https://arxiv.org/html/2509.18929v1)
- [Meta: Passthrough Camera API](https://developers.meta.com/horizon/documentation/unity/unity-pca-overview/)
- [Meta: Movement SDK body tracking](https://developers.meta.com/horizon/documentation/unity/move-body-tracking/)
- [Meta: Shared spatial anchors](https://developers.meta.com/horizon/documentation/unity/unity-shared-spatial-anchors/)
- [MediaPipe pose landmarker](https://developers.google.com/edge/mediapipe/solutions/vision/pose_landmarker)
