// Runs before the test modules: if one fails to load or parse, report it (and in headless runs
// tell run.py) instead of leaving the page hanging until the timeout.
(function () {
  var auto = /[?&]auto\b/.test(location.search);
  window.__fatal = function (msg) {
    if (window.__reported) return;
    window.__reported = true;
    document.addEventListener("DOMContentLoaded", show);
    if (document.readyState !== "loading") show();
    function show() { document.getElementById("summary").textContent = "FATAL: " + msg; }
    if (auto) fetch("/__results", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fatal: msg }) });
  };
  addEventListener("error", function (e) {
    if (window.__started) return;   // once running, the runner catches errors per scenario
    var src = e.target && e.target.src;
    window.__fatal(src ? "failed to load " + src : (e.message || "error") + (e.filename ? " (" + e.filename + ":" + e.lineno + ")" : ""));
  }, true);
})();
