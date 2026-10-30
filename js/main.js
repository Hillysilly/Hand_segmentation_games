// Entry point: the start screen, choosing camera or simulator input, and the frame loop.
// Add ?sim=<scenario> to the URL to play a scripted scenario instead of using the webcam.
import { setCanvas, setSize, timed } from "./state.js";
import { sfx } from "./audio.js";
import { step } from "./engine.js";
import { render } from "./render.js";
import { initArmor } from "./powers/armor.js";
import { initPanel } from "./panel.js";
import { SimSource } from "./sim/sim.js";
import { scenarios } from "./sim/scenarios.js";

const video = document.getElementById("video");
const startEl = document.getElementById("start");
const goBtn = document.getElementById("go");
const statusEl = document.getElementById("status");
const uiEl = document.getElementById("ui");

setCanvas(document.getElementById("c"));
const resize = () => setSize(innerWidth, innerHeight, Math.min(window.devicePixelRatio || 1, 2));
addEventListener("resize", resize);
resize();

const simName = new URLSearchParams(location.search).get("sim");
const scenario = simName === null ? null : scenarios.find(s => s.name === (simName || "duel"));
if (simName !== null) {
  if (scenario) goBtn.textContent = `SIMULATE: ${scenario.name.toUpperCase()}`;
  else statusEl.textContent = `No scenario "${simName}". Try: ${scenarios.map(s => s.name).join(", ")}`;
}

let source = null, lastFrame = performance.now(), fps = 60;
function loop(now) {
  requestAnimationFrame(loop);
  const dt = Math.min(0.05, Math.max(0.001, (now - lastFrame) / 1000));
  lastFrame = now;
  fps += (1 / dt - fps) * 0.05;
  step(source, dt, now);
  timed("draw", () => render(now, fps));
}

goBtn.addEventListener("click", async () => {
  goBtn.disabled = true;
  const armor = initArmor();   // loads three.js alongside everything else
  try {
    if (!sfx.ready) sfx.init();
    if (simName !== null) {
      if (!scenario) throw new Error("unknown scenario");
      source = new SimSource(scenario);
      source.attach();
    } else {
      const { CameraSource } = await import("./tracking.js");
      source = await CameraSource.start(video, text => { statusEl.textContent = text; });
    }
    statusEl.textContent = "loading armour…";
    await armor;
    startEl.style.display = "none";
    uiEl.hidden = false;
    lastFrame = performance.now();
    requestAnimationFrame(loop);
  } catch (e) {
    console.error(e);
    statusEl.textContent = "Failed: " + (e.message || e) + (simName === null ? " (camera needs localhost or https)" : "");
    goBtn.disabled = false;
  }
});

initPanel();
