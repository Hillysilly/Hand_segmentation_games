"""Local dev server for the HUD.

Serves this folder on localhost with JavaScript module MIME types set explicitly (Python on Windows
can serve .js as text/plain, which browsers refuse to run as a module) and caching off, so a
refresh always picks up edits.

    python serve.py [port]        # default 8000
"""
import functools
import http.server
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent


class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {
        **http.server.SimpleHTTPRequestHandler.extensions_map,
        ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".css": "text/css",
        ".json": "application/json", ".png": "image/png", ".svg": "image/svg+xml",
    }

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


def make_server(port=8000, handler=Handler, host="127.0.0.1"):
    return http.server.ThreadingHTTPServer((host, port), functools.partial(handler, directory=str(ROOT)))


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8000
    server = make_server(port)
    print(f"Serving {ROOT}")
    print(f"  app:       http://localhost:{port}/")
    print(f"  simulator: http://localhost:{port}/?sim=duel")
    print(f"  tests:     http://localhost:{port}/tests/")
    server.serve_forever()
