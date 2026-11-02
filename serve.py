"""Local dev server for the HUD.

Serves this folder on localhost with JavaScript module MIME types set explicitly (Python on Windows
can serve .js as text/plain, which browsers refuse to run as a module) and caching off, so a
refresh always picks up edits.

    python serve.py [port]        # default 8000
    python serve.py --lan [port]  # HTTPS on the local network, for other devices

--lan listens on every network interface over HTTPS, since browsers only allow the camera and mic on
a secure page and plain http to a LAN address isn't one. The certificate is self-signed (made with
openssl, which comes with Git for Windows), so each device warns once before opening the page. It's
kept in ~/.repulsor-hud, outside the served folder, so its key can't be downloaded.
"""
import functools
import http.server
import os
import pathlib
import shutil
import socket
import ssl
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent
CERTS = pathlib.Path.home() / ".repulsor-hud"


class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {
        **http.server.SimpleHTTPRequestHandler.extensions_map,
        ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".css": "text/css",
        ".json": "application/json", ".png": "image/png", ".svg": "image/svg+xml",
    }

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


class TLSServer(http.server.ThreadingHTTPServer):
    """Does the TLS handshake in each request's own thread, so one slow client can't hold up the rest."""
    context = None

    def finish_request(self, request, client_address):
        super().finish_request(self.context.wrap_socket(request, server_side=True), client_address)

    def handle_error(self, request, client_address):
        # A device that hasn't accepted the certificate yet hangs up mid-handshake: not worth a traceback.
        if not isinstance(sys.exc_info()[1], (ssl.SSLError, ConnectionError)):
            super().handle_error(request, client_address)


def make_server(port=8000, handler=Handler, host="127.0.0.1", tls=None):
    """tls: an ssl.SSLContext to serve HTTPS with, or None for plain HTTP."""
    cls = TLSServer if tls else http.server.ThreadingHTTPServer
    server = cls((host, port), functools.partial(handler, directory=str(ROOT)))
    server.context = tls
    return server


def lan_ip():
    """This machine's address on the local network: the interface it would route outside traffic through."""
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
        try:
            s.connect(("10.255.255.255", 1))   # UDP: picks a route, sends nothing
            return s.getsockname()[0]
        except OSError:
            return "127.0.0.1"


def openssl():
    found = shutil.which("openssl")
    if found:
        return found
    git = pathlib.Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Git"
    for exe in (git / "mingw64/bin/openssl.exe", git / "usr/bin/openssl.exe"):
        if exe.exists():
            return str(exe)
    raise SystemExit("--lan needs openssl to make its certificate: install Git for Windows, or put openssl on PATH")


def certificate(ip):
    """A self-signed certificate for this address, made once per address (a new network gets a new one)."""
    CERTS.mkdir(exist_ok=True)
    cert, key = CERTS / f"cert-{ip}.pem", CERTS / f"key-{ip}.pem"
    if not (cert.exists() and key.exists()):
        subprocess.run([openssl(), "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "825",
                        "-keyout", str(key), "-out", str(cert), "-subj", "/CN=Repulsor HUD",
                        "-addext", f"subjectAltName=IP:{ip},IP:127.0.0.1,DNS:localhost"],
                       check=True, capture_output=True)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(cert, key)
    return context


if __name__ == "__main__":
    args = sys.argv[1:]
    lan = "--lan" in args
    args = [a for a in args if a != "--lan"]
    port = int(args[0]) if args else 8000
    print(f"Serving {ROOT}")
    if lan:
        ip = lan_ip()
        server = make_server(port, host="0.0.0.0", tls=certificate(ip))
        print(f"  this machine:   https://localhost:{port}/")
        print(f"  other devices:  https://{ip}:{port}/")
        print("  The certificate is self-signed: on each device choose Advanced > Proceed once.")
        print("  If other devices can't connect, allow Python through Windows Firewall on private networks.")
    else:
        server = make_server(port)
        print(f"  app:       http://localhost:{port}/")
        print(f"  simulator: http://localhost:{port}/?sim=duel")
        print(f"  tests:     http://localhost:{port}/tests/")
    server.serve_forever()
