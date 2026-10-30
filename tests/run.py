"""Run the simulation test suite headlessly.

Serves the project, opens tests/index.html in headless Chrome, waits for the page to post its
results, then writes tests/output/report.json plus one PNG per snapshot. Exits 1 if any scenario
fails, 2 if the run itself broke (page error, timeout, no Chrome).

    python tests/run.py [--filter NAME] [--chrome PATH] [--timeout SECONDS] [--headed] [--bench]
"""
import argparse
import base64
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import threading
import urllib.parse

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
from serve import ROOT, Handler, make_server  # noqa: E402

OUT = ROOT / "tests" / "output"
CHROME_CANDIDATES = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    os.path.expandvars(r"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "google-chrome", "google-chrome-stable", "chromium", "chromium-browser",
]


def find_chrome(explicit):
    for c in [explicit, os.environ.get("CHROME")] + CHROME_CANDIDATES:
        if not c:
            continue
        path = c if os.path.isfile(c) else shutil.which(c)
        if path:
            return path
    return None


class ResultsHandler(Handler):
    def do_POST(self):
        if self.path != "/__results":
            self.send_error(404)
            return
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        self.server.results = json.loads(body)
        self.send_response(204)
        self.end_headers()
        self.server.done.set()

    def log_message(self, *args):
        pass


def write_outputs(report):
    if OUT.exists():
        shutil.rmtree(OUT)
    OUT.mkdir(parents=True)
    for sc in report["scenarios"]:
        for shot in sc.pop("snapshots", []):
            data = shot["data"].split(",", 1)[1]
            (OUT / f"{shot['name']}.png").write_bytes(base64.b64decode(data))
            sc.setdefault("images", []).append(f"{shot['name']}.png")
    (OUT / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")


def print_report(report):
    armor = report.get("armor", {})
    if not armor.get("webgl"):
        print(f"note: suit not drawn in this browser ({armor.get('error') or 'no WebGL'}); drawing checks skipped")
    for sc in report["scenarios"]:
        print(f"{'PASS' if sc['passed'] else 'FAIL'}  {sc['name']}  ({sc.get('ms', 0)} ms)")
        for c in sc["checks"]:
            if c["status"] != "pass":
                print(f"      {c['status'].upper():4}  @{c['t']:.2f}s  {c['desc']}: {c['detail']}")
        if sc.get("error"):
            print("      " + sc["error"].replace("\n", "\n      "))
    failed = sum(not sc["passed"] for sc in report["scenarios"])
    total = len(report["scenarios"])
    if report.get("perf"):
        print("ms per frame: " + "  ".join(f"{k} {v:.2f}" for k, v in report["perf"].items()))
    print(f"\n{total - failed}/{total} scenarios passed. Snapshots and report.json in {OUT}")
    return failed


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--filter", help="only scenarios whose name contains this")
    ap.add_argument("--chrome", help="path to Chrome / Chromium / Edge")
    ap.add_argument("--timeout", type=float, default=300, help="seconds to wait for results")
    ap.add_argument("--headed", action="store_true", help="show the browser window")
    ap.add_argument("--bench", action="store_true", help="draw every frame and report per-stage timings")
    args = ap.parse_args()

    chrome = find_chrome(args.chrome)
    if not chrome:
        print("Chrome not found; pass --chrome PATH or set CHROME")
        return 2

    server = make_server(0, ResultsHandler)
    server.done, server.results = threading.Event(), None
    threading.Thread(target=server.serve_forever, daemon=True).start()
    query = {"auto": "1", **({"filter": args.filter} if args.filter else {}), **({"bench": "1"} if args.bench else {})}
    url = f"http://127.0.0.1:{server.server_address[1]}/tests/?{urllib.parse.urlencode(query)}"

    profile = tempfile.mkdtemp(prefix="hud-tests-")
    cmd = [chrome, f"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check",
           "--disable-extensions", "--window-size=1280,900", "--autoplay-policy=no-user-gesture-required"]
    if not args.headed:
        # software WebGL so the suit renders without a GPU
        cmd += ["--headless=new", "--use-angle=swiftshader", "--enable-unsafe-swiftshader"]
    print(f"running {url}")
    proc = subprocess.Popen(cmd + [url], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        finished = server.done.wait(args.timeout)
    finally:
        proc.terminate()
        try:
            proc.wait(10)
        except subprocess.TimeoutExpired:
            proc.kill()
        server.shutdown()
        shutil.rmtree(profile, ignore_errors=True)

    if not finished:
        print(f"timed out after {args.timeout:.0f} s waiting for results")
        return 2
    report = server.results
    if "fatal" in report:
        print(f"test page failed: {report['fatal']}")
        return 2
    write_outputs(report)
    return 1 if print_report(report) else 0


if __name__ == "__main__":
    sys.exit(main())
