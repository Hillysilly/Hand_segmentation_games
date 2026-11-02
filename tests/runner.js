// Test runner: plays every scenario through the real update and render code with a seeded RNG and
// a fixed 30 fps clock, evaluates its checks, and keeps its snapshots. Open /tests/ in a browser to
// watch (?filter=name runs a subset), or run tests/run.py for a headless run into tests/output/.
// ?bench draws every frame too, and reports how long each stage takes.
import { settings, DEFAULT_SETTINGS } from "../js/config.js";
import { canvas, ctx, setCanvas, setSize, persons, hands, shockwaves, particles, perf, timed } from "../js/state.js";
import { step, resetWorld } from "../js/engine.js";
import { render } from "../js/render.js";
import { initArmor, armorDebug, armorStatus } from "../js/powers/armor.js";
import { cloakCoverage } from "../js/powers/cloak.js";
import { webs } from "../js/powers/spider.js";
import { balls } from "../js/powers/ninja.js";
import { SimSource } from "../js/sim/sim.js";
import { scenarios } from "../js/sim/scenarios.js";

window.__started = true;
const DT = 1 / 30, T0 = 10000;   // scenario time 0 is clock 10 s, so "never seen" timestamps stay stale
const params = new URLSearchParams(location.search);
const auto = params.has("auto"), filter = params.get("filter"), bench = params.has("bench");
const summaryEl = document.getElementById("summary"), resultsEl = document.getElementById("results");

setCanvas(document.getElementById("view"));
setSize(1280, 720, 1);

// mulberry32: small seeded PRNG, so every run draws the same particles and flicker.
function seeded(seed) {
  return () => {
    seed = seed + 0x6D2B79F5 | 0;
    let t = Math.imul(seed ^ seed >>> 15, 1 | seed);
    t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
    return ((t ^ t >>> 14) >>> 0) / 4294967296;
  };
}

// What checks get to look at.
function world(now) {
  return {
    persons, hands, shockwaves, particles, webs, balls,
    p: n => persons.find(p => p.num === n),
    armor: armorDebug(),
    cloak: cloakCoverage(),
    // Does the suit actually show up on screen at this piece? Renders with and without it.
    drawsArmor(piece) {
      if (!armorStatus.webgl) return `SKIP: ${armorStatus.error || "no WebGL"}`;
      const x = Math.round(piece.x), y = Math.round(piece.y);
      const suits = { armor: settings.armor, spiderman: settings.spiderman };
      const sample = on => {
        Object.assign(settings, on ? suits : { armor: false, spiderman: false });
        render(now, 30);
        return ctx.getImageData(x, y, 1, 1).data;
      };
      const off = sample(false), on = sample(true);
      const d = Math.hypot(on[0] - off[0], on[1] - off[1], on[2] - off[2]);
      return d > 40 || `the suit barely changes the pixel at (${x}, ${y}): difference ${Math.round(d)}`;
    },
  };
}

async function runScenario(sc) {
  const started = performance.now();
  Math.random = seeded(1234);
  // the suit is simply on unless a scenario is about suiting up
  Object.assign(settings, DEFAULT_SETTINGS, { nanotech: false }, sc.settings);
  resetWorld();
  const src = new SimSource(sc);
  src.attach();
  const checks = (sc.checks || []).map(([t, desc, fn]) => ({ t, desc, fn })).sort((a, b) => a.t - b.t);
  const snaps = (sc.snapshots || []).map(([t, name]) => ({ t, name }));
  const results = [], images = [];
  const steps = Math.round(sc.duration / DT);
  for (let i = 0; i <= steps; i++) {
    const t = i * DT, now = T0 + t * 1000;
    const due = list => list.filter(x => !x.done && t >= x.t - 1e-6);
    const dueSnaps = due(snaps), dueChecks = due(checks);
    // only paint the fake camera when it's seen (or the scenario looks at every frame)
    src.drawFrames = bench || sc.paintFrames || dueSnaps.length > 0 || dueChecks.length > 0;
    if (bench && i === 15) for (const k in perf) delete perf[k];   // drop warm-up (shader compiles)
    step(src, DT, now, t);
    if (bench) timed("draw", () => render(now, 30));
    for (const s of dueSnaps) {
      render(now, 30);
      images.push({ name: `${sc.name}--${s.name}`, data: canvas.toDataURL("image/png") });
      s.done = true;
    }
    for (const c of dueChecks) {
      let r;
      try { r = c.fn(world(now)); } catch (e) { r = `threw: ${e.message}`; }
      const status = r === true ? "pass" : String(r).startsWith("SKIP") ? "skip" : "fail";
      results.push({ t: c.t, desc: c.desc, status, detail: r === true ? "" : String(r) });
      c.done = true;
    }
    if (i % 30 === 29) await new Promise(r => setTimeout(r));   // let the page repaint
  }
  for (const c of checks) if (!c.done) results.push({ t: c.t, desc: c.desc, status: "fail", detail: "after the scenario ended" });
  return { name: sc.name, about: sc.about, passed: results.every(r => r.status !== "fail"), checks: results,
    snapshots: images, ms: Math.round(performance.now() - started) };
}

function show(r) {
  const li = document.createElement("li");
  li.className = r.passed ? "pass" : "fail";
  const head = document.createElement("div");
  head.innerHTML = `<span class="name"></span> <span class="about"></span> <span class="about">(${r.ms ?? 0} ms)</span>`;
  head.querySelector(".name").textContent = `${r.passed ? "PASS" : "FAIL"} ${r.name}`;
  head.querySelector(".about").textContent = `— ${r.about || ""}`;
  li.append(head);
  for (const c of r.checks) {
    const d = document.createElement("div");
    d.className = `check ${c.status}`;
    d.textContent = `${c.status.toUpperCase().padEnd(4)} @${c.t.toFixed(2)}s ${c.desc}${c.detail ? ": " + c.detail : ""}`;
    li.append(d);
  }
  if (r.error) { const pre = document.createElement("pre"); pre.textContent = r.error; li.append(pre); }
  if (r.snapshots.length) {
    const shots = document.createElement("div");
    shots.className = "shots";
    for (const s of r.snapshots) { const img = new Image(); img.src = s.data; img.title = s.name; shots.append(img); }
    li.append(shots);
  }
  resultsEl.append(li);
}

async function main() {
  summaryEl.textContent = "loading three.js…";
  const armor = { ...(await initArmor()) };
  const list = filter ? scenarios.filter(s => s.name.includes(filter)) : scenarios;
  const out = [];
  for (const sc of list) {
    summaryEl.textContent = `running ${sc.name} (${out.length + 1}/${list.length})…`;
    let r;
    try {
      r = await runScenario(sc);
    } catch (e) {
      r = { name: sc.name, about: sc.about, passed: false, error: String(e.stack || e), checks: [], snapshots: [] };
    }
    out.push(r);
    show(r);
  }
  const failed = out.filter(r => !r.passed).length;
  summaryEl.textContent = `${out.length - failed}/${out.length} scenarios passed` +
    (armor.webgl ? "" : ` · suit not drawn: ${armor.error}`);
  if (auto) {
    await fetch("/__results", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ armor, perf, passed: failed === 0, scenarios: out }) });
  }
}

main().catch(e => window.__fatal(String(e.stack || e)));
