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

To use it from other devices on your network, run `python serve.py --lan` and open the
`https://<this machine's IP>:8000/` address it prints. It serves HTTPS because browsers only allow the
camera and mic on a secure page, with a self-signed certificate (made once by `openssl`, which comes
with Git for Windows, and kept in `~/.repulsor-hud`), so each device needs **Advanced > Proceed**
once. Windows Firewall must let Python accept connections on private networks (**Windows Security >
Firewall & network protection > Allow an app through firewall**). Each device runs the tracking
itself, so it needs its own decent GPU, plus internet for the MediaPipe library and models.

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
3. On a gaming laptop, run it plugged into the laptop's own (full-wattage) charger, with Windows'
   power mode on **Best performance** and the maker's performance mode on (e.g. NitroSense on Acer).
   Otherwise, when the GPU speeds up the laptop can clamp the CPU to its lowest clock (seen on an
   Acer Nitro 5: 13% of its normal speed for ~10 s at a time), and body and hand tracking go from
   ~60 ms to 500+ ms a frame: a second of lag, and the suit flickering out. Optionally, also set
   **NVIDIA Control Panel > Manage 3D settings > Program Settings > Chrome > Power management mode**
   to **Prefer maximum performance**, so the GPU doesn't drop to a low-power clock mid-session.
4. Fully quit Chrome (including any copy left running in the background) and reopen it.
5. Check `chrome://gpu`: the WebGL renderer line should name the fast card, and the HUD's
   `MS` line shows how long each stage takes.

Tracking runs on the GPU in two background threads (`js/trackworker.js`), one for body tracking and
one for hands, so drawing stays at 60 fps however long tracking takes. The HUD's `TRACK` line shows
the tracking rate and where each model runs (about 34 fps with both on a GTX 1650). The first launch
compiles the models' GPU shaders, which can take 10+ seconds on the loading screen; Chrome caches
them after that. If the threads can't start, tracking falls back to the main thread (`MAIN THREAD`
in the HUD), with body tracking on the CPU.

## Suit model

The suit is a rigged humanoid glTF posed every frame from the tracked body (`js/powers/suitmodel.js`).
The default is `assets/models/suit.glb`: "Iron-Man Mark 85 | Rigged" by 9A Films / Nihar Arora
(https://sketchfab.com/3d-models/iron-man-mark-85-rigged-dde1085c464d4f8da259fe6669ae4dd2),
licensed CC BY 4.0, with its textures shrunk to 1024 px JPEG. It is fan art of a Marvel design, so
keep it to personal use. Without it the three.js example Xbot (`assets/models/xbot.glb`) is used,
painted red and gold.

The Spider-Man suit (SPIDER-MAN on; with SUIT on too, whichever was switched on last) is
`assets/models/spiderman.glb`: "Spider-Man (2017; Homecoming - Tech Suit)" by Mr. P
(https://sketchfab.com/3d-models/spider-man-2017-homecoming-tech-suit-6f58018044e147c08b8c2d7f552f46d1),
licensed CC BY 4.0, textures re-encoded as JPEG. Also fan art of a Marvel design: personal use only.
It's simply on: nanotech, the arc reactor and the repulsor glows are Iron Man's.

His lenses follow your face: squint, blink, wink (mirrored, like the rest of the suit), frown, or
widen your eyes. A third tracking thread runs MediaPipe's face landmarker (`js/face.js`), started the
first time someone wears Spider-Man, fed only while they do, every second frame at 640 px wide, and
never holding a frame back; it costs about 2 tracking fps. The lids are painted onto the lenses in
the shader (`lensMaterial` in `js/powers/suitmodel.js`), measured against your own resting face.

Pull the mask off: make a fist on your head and pull it down past your chest (`js/powers/unmask.js`).
The mask peels away from the neck up and your real head shows (the cloak leaves it uncovered). A
fist below your chest pulled up onto your head puts it back on.

To use another model:

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

Webs land in the room (`js/room.js`, `js/powers/spider.js`): the room is a box guessed from the
people in it (a typical webcam's field of view, your distance from your size, the floor from your
feet or hips, a back wall behind the farthest anyone has stood). Each web flies from your hand along
its 3D aim and lies flat on the surface it hits, in perspective; webs behind you are drawn behind
you, and webs fired at the camera splat on the lens. The HUD's ROOM line shows the guess. Real
furniture isn't known, so a web aimed at a shelf lands on the wall behind it.

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
