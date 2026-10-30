// Side panel of power toggles, plus the keyboard shortcuts.
import { settings } from "./config.js";
import { sfx } from "./audio.js";

const KEY_SETTINGS = { i: "ironman", a: "armor", n: "nanotech", c: "cloak", g: "goku", j: "spinjitzu", w: "spiderman", v: "video",
  h: "skeleton", s: "silhouettes", m: "muted" };

const hexToRgb = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16)).join(",");
const rgbToHex = c => "#" + c.split(",").map(n => (+n).toString(16).padStart(2, "0")).join("");

export function initPanel() {
  const panelEl = document.getElementById("panel");
  const tabEl = document.getElementById("tab");
  const toggles = [...document.querySelectorAll("[data-setting]")];
  const swatches = [...document.querySelectorAll(".swatch")];
  const spinPick = document.getElementById("spinPick");
  const spinCard = document.getElementById("spinCard");

  function syncPanel() {
    for (const el of toggles) {
      const on = settings[el.dataset.setting];
      el.setAttribute("aria-pressed", String(el.hasAttribute("data-invert") ? !on : on));
    }
    // the Spinjitzu card and swatches follow the chosen tornado color
    for (const sw of swatches) sw.setAttribute("aria-pressed", String(sw.dataset.color === settings.spinColor));
    spinPick.value = rgbToHex(settings.spinColor);
    spinCard.style.setProperty("--accent", `rgb(${settings.spinColor})`);
    spinCard.style.setProperty("--glow", `rgba(${settings.spinColor},0.2)`);
  }

  function setSetting(key, value) {
    settings[key] = value;
    if (key === "muted") sfx.setMuted(value);
    syncPanel();
  }

  function togglePanel() {
    const open = panelEl.classList.toggle("collapsed") === false;
    tabEl.setAttribute("aria-expanded", String(open));
  }

  for (const sw of swatches) {
    sw.addEventListener("click", () => { setSetting("spinColor", sw.dataset.color); sw.blur(); });
  }
  spinPick.addEventListener("input", () => setSetting("spinColor", hexToRgb(spinPick.value)));
  for (const el of toggles) {
    el.addEventListener("click", () => {
      setSetting(el.dataset.setting, !settings[el.dataset.setting]);
      el.blur();   // keep keyboard shortcuts from re-clicking the focused button
    });
  }
  tabEl.addEventListener("click", () => { togglePanel(); tabEl.blur(); });
  syncPanel();

  addEventListener("keydown", e => {
    const k = e.key.toLowerCase();
    if (KEY_SETTINGS[k]) setSetting(KEY_SETTINGS[k], !settings[KEY_SETTINGS[k]]);
    else if (k === "p") togglePanel();
    else if (k === "f") {
      if (document.fullscreenElement) document.exitFullscreen();
      else document.documentElement.requestFullscreen?.();
    }
  });
}
