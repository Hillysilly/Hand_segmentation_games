# Vision Testing

Two projects that turn your body into a game controller.

## Tin Can Takedown (Meta Quest 3), `unity_game/`

A mixed-reality game played with your hands, no controllers. Robots spawn in your real room, hide
behind your real walls, and walk round them to reach you.

- 8 characters (Techno, Brawler, Emberwitch, Lumen, Valkyr, Spider Boy, Tempest, Terra), each with 3
  moves made from hand signs and gestures (finger guns, fist-and-snap blasts, a lightsaber, a bow, webs,
  lightning, rock spikes).
- Solo or co-op against 20 rounds of robots, with bosses at rounds 10 and 20; or PVP. Two headsets
  connect over Wi-Fi by IP address.
- 5 levels with power-up auras, Easy/Normal/Hard, and a soundtrack.

Built with Unity 6, OpenXR, AR Foundation, XR Hands and URP. Build and install on a connected Quest
with `unity_game/build_and_run.ps1` (Unity must be closed). Asset credits are in
`unity_game/Assets/Resources/Credits.txt` and in the game's CREDITS screen.

## Repulsor HUD (browser), the repo root

The original tests: webcam superpowers in the browser. MediaPipe hand and pose tracking drives Iron Man
repulsors and a full armour suit, a Goku aura, a Spinjitzu tornado, elemental balls and Spider-Man webs.

```
python serve.py            # http://localhost:8000/   (webcam and mic)
python tests/run.py        # simulated scenarios in headless Chrome
```

`?sim=duel` (any scenario in `js/sim/scenarios.js`) plays without a camera. Tracking runs on the GPU,
so turn on Chrome's graphics acceleration. The suit models are CC BY 4.0 fan art of Marvel designs:
personal use only.
