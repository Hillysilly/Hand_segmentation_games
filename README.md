# Repulsor HUD

Webcam superpowers in the browser: MediaPipe hand and pose tracking drives Iron Man repulsors and a
full armour suit, a Goku aura you power up by flexing and yelling, a Spinjitzu tornado, elemental
balls and Spider-Man webs.

## Run

```
python serve.py            # http://localhost:8000/
```

- `http://localhost:8000/` uses the webcam and mic.
- `http://localhost:8000/?sim=duel` plays a scripted scenario with no camera (any name from
  `js/sim/scenarios.js`).

Use `serve.py` rather than `python -m http.server`: it serves `.js` with a JavaScript MIME type
(Python on Windows sometimes doesn't, and the browser then refuses the modules) and turns caching off.

### Let Chrome use the GPU

Tracking and the suit run on the GPU, so Chrome must be allowed to use it, or everything lags.

1. In Chrome, open `chrome://settings/system` and turn on **Use graphics acceleration when
   available**, then relaunch Chrome.
2. On a laptop with two GPUs (e.g. integrated AMD/Intel plus an NVIDIA card), point Chrome at the
   fast one: Windows **Settings > System > Display > Graphics**, add
   `C:\Program Files\Google\Chrome\Application\chrome.exe`, choose **Options > High performance**
   and save. (This is the registry value `C:\Program Files\Google\Chrome\Application\chrome.exe` =
   `GpuPreference=2;` under `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`.)
3. Fully quit Chrome (including any copy left running in the background) and reopen it.
4. Check `chrome://gpu`: the WebGL renderer line should name the fast card, and the HUD's
   `MS` line shows how long each stage takes.

Body tracking currently runs on the CPU either way (its GPU version returns empty masks), so expect
roughly 55–70 ms per frame from it even on a good GPU.

## Suit model

The suit is a rigged humanoid glTF posed every frame from the tracked body (`js/powers/suitmodel.js`).
The default is `assets/models/suit.glb`: "Iron-Man Mark 85 | Rigged" by 9A Films / Nihar Arora
(https://sketchfab.com/3d-models/iron-man-mark-85-rigged-dde1085c464d4f8da259fe6669ae4dd2),
licensed CC BY 4.0, with its textures shrunk to 1024 px JPEG. It is fan art of a Marvel design, so
keep it to personal use. Without it the three.js example Xbot (`assets/models/xbot.glb`) is used,
painted red and gold. To use another model:

1. Get a humanoid model and rig it with Mixamo (upload, auto-rig, download as FBX in T-pose), or
   start from one that already has `mixamorig` bone names.
2. Convert it to `.glb` (Blender: File > Export > glTF Binary) and save it as `assets/models/suit.glb`.
   It's picked up automatically; `?model=<url>` loads any other file.

The model needs Mixamo bone names (Hips, Spine, Spine2, Neck, Head, Left/Right Shoulder, Arm, ForeArm,
Hand, UpLeg, Leg, Foot; Sketchfab's `_NN` suffixes are fine); if any are missing the browser console says which, and the suit falls
back to just the reactor and hand armour.

NANOTECH (key N): hold a hand flat on your chest for a moment and the suit flows out of the arc
reactor over your body, chest first and helmet last; do it again to put it away. Every vertex of
the model has its distance from the reactor baked in (over the skeleton), and the shader draws
only what the front has reached, with liquid silver at the edge. Turn NANOTECH off to just wear the
suit.

CLOAK (key C) paints the room back over your body so only the suit shows. It learns the room from
what the camera sees around you, so step out of shot (or move around) once at the start.

## Test

```
python tests/run.py                  # whole suite, headless Chrome
python tests/run.py --filter armor   # scenarios whose name contains "armor"
python tests/run.py --headed         # watch it run
```

Each scenario in `js/sim/scenarios.js` scripts people (a parametric 3D body in `js/sim/body.js`),
the mic level and settings over time. The simulator turns that into the same landmark, world
landmark and segmentation mask results MediaPipe produces, so the real update and render code runs
unchanged, at a fixed 30 fps with a seeded RNG. Checks assert on game state at given times, and
snapshots are saved to `tests/output/` with `report.json`. Exit code 0 = all passed, 1 = a scenario
failed, 2 = the run broke. Open `http://localhost:8000/tests/` to run the same suite in a browser.

## Layout

| Path | What |
|---|---|
| `index.html`, `css/style.css` | page and styles |
| `js/main.js` | start screen, input choice, frame loop |
| `js/tracking.js` | webcam + mic through MediaPipe (live input) |
| `js/engine.js` | one frame: poll input, update; scene reset |
| `js/update.js`, `js/render.js` | game logic and drawing per frame |
| `js/state.js`, `js/config.js` | shared state, constants, settings |
| `js/people.js`, `js/hands.js` | pose / hand tracks, gestures, palm direction |
| `js/combat.js`, `js/effects.js`, `js/gfx.js` | blasts and damage, particles, canvas helpers |
| `js/audio.js` | synthesized sounds, mic loudness |
| `js/powers/*.js` | ironman, armor (three.js suit) + suitmodel (posing the rigged model) + cloak (erasing the body), goku, spinjitzu, ninja, spider |
| `assets/models/` | suit models (`xbot.glb` default, `suit.glb` yours) |
| `js/sim/` | simulated body, input source and scenarios |
| `tests/` | browser test runner and the headless driver |
