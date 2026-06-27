#!/usr/bin/env python3
"""Shell harness over HTTP — verify migrated web/ pages in a browser without the game.

Serves the real (build_preview-generated) MFD shell plus the migrated web/ assets, so the
full-view surgery (WPN, TGL, …) can be driven end-to-end. The shell's mocked /stream
(preview-mock.js, injected by build_preview) supplies the loadout / CM / targets, which the
shell forwards to the #page-frame iframe.

  /                  -> preview/index.html         (the build_preview shell)
  /config            -> preview runtime URLs        (localhost/LAN URL for this harness port)
  /map-view[?bare]   -> web/pages/map/map.html      (the base map iframe; mock injected here)
  /<page>            -> web/pages/<page>/<page>.html  (any migrated page, e.g. /wpn /tgl)
  /weapon?...        -> a mock 2:1 weapon icon      (the frame fetches this directly)
  /assets/<x>        -> web/<x>                     (font.css, theme.css, woff2, page css/js)
  else               -> preview/<x>                 (*.js, manifest, ...)

The MAP page is the only EventSource('/stream') consumer, so the mock (which stubs /stream,
/map, /icon, /weapon) is injected into it here — exactly what build_preview used to do when the
map was a generated preview/map-view.html. The shell loads /map-view?bare absolutely.

Usage:
    python tools/serve_web.py            # serve on http://127.0.0.1:8782
    python tools/serve_web.py --port N

Run `python tools/build_preview.py` first to populate preview/. Ctrl+C to stop.
"""
import argparse
import http.server
import json
import os
import pathlib
import posixpath
import socket
import socketserver

REPO = pathlib.Path(__file__).resolve().parent.parent
WEB = REPO / "web"
PREV = REPO / "preview"
MOCK = REPO / "tools" / "preview-mock.js"
MANIFEST = PREV / "assets" / "manifest.json"

WEAPON_SVG = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">'
              '<rect width="200" height="100" fill="none" stroke="#39ff14" stroke-width="3"/>'
              '<circle cx="30" cy="50" r="18" fill="#39ff14"/>'
              '<rect x="60" y="40" width="120" height="20" fill="#39ff14"/>'
              '<text x="100" y="92" fill="#39ff14" font-size="14" text-anchor="middle" '
              'font-family="monospace">WPN</text></svg>')

MIME = {'.css': 'text/css', '.js': 'text/javascript', '.woff2': 'font/woff2', '.html': 'text/html',
        '.json': 'application/json', '.png': 'image/png', '.svg': 'image/svg+xml', '.jpg': 'image/jpeg'}


def _mime(rel):
    return MIME.get(os.path.splitext(rel)[1], 'application/octet-stream')


def _capture_injection():
    """If a capture exists, a <script> exposing the real frame + assets (mirrors build_preview)."""
    if not MANIFEST.exists():
        return ""
    m = json.loads(MANIFEST.read_text(encoding="utf-8"))
    frame = json.dumps(m.get("frame", {})).replace("</", "<\\/")
    assets = json.dumps(m.get("assets", {})).replace("</", "<\\/")
    return ("<script>\n"
            f"window.__PREVIEW_FRAME__ = {frame};\n"
            f"window.__PREVIEW_ASSETS__ = {assets};\n"
            "</script>\n")


def _map_page():
    """The MAP page (web/pages/map/map.html) with the mock (+ any capture) injected before
    </head>, so its EventSource('/stream') and /map,/icon,/weapon fetches resolve in the browser.
    Built fresh per request so edits to map.html / the mock show up on reload."""
    html = (WEB / "pages" / "map" / "map.html").read_text(encoding="utf-8")
    mock = MOCK.read_text(encoding="utf-8").strip()
    return html.replace("</head>", _capture_injection() + mock + "\n</head>", 1).encode("utf-8")


def _detect_lan_ip():
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.connect(("8.8.8.8", 65530))
            ip = sock.getsockname()[0]
        return "" if not ip or ip.startswith("127.") or ip.startswith("0.") else ip
    except OSError:
        return ""


def _config(port):
    lan_ip = _detect_lan_ip()
    return json.dumps({
        "localhost": f"http://localhost:{port}",
        "lanUrl": f"http://{lan_ip}:{port}" if lan_ip else "",
        "port": port,
    }).encode("utf-8")


class H(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        path = self.path.split('?', 1)[0]
        if path in ('/', '/index.html'):
            return self._file(PREV / 'index.html', 'text/html; charset=utf-8')
        if path == '/config':
            return self._send(_config(self.server.server_address[1]), 'application/json; charset=utf-8')
        if path == '/map-view':
            try:
                return self._send(_map_page(), 'text/html; charset=utf-8')
            except OSError as e:
                return self.send_error(404, str(e))
        if path == '/weapon':
            return self._send(WEAPON_SVG.encode('utf-8'), 'image/svg+xml')
        if path.startswith('/assets/'):
            rel = posixpath.normpath(path[len('/assets/'):]).lstrip('/\\')
            return self._file(WEB.joinpath(*rel.split('/')), _mime(rel))
        # Any migrated page: /<name> -> web/pages/<name>/<name>.html (wpn, tgl, ...).
        name = path.lstrip('/')
        page = WEB / 'pages' / name / f'{name}.html'
        if name and '/' not in name and page.exists():
            return self._file(page, 'text/html; charset=utf-8')
        rel = posixpath.normpath(path.lstrip('/')).lstrip('/\\')
        return self._file(PREV.joinpath(*rel.split('/')), _mime(rel))

    def _file(self, fp, mime):
        try:
            body = pathlib.Path(fp).read_bytes()
        except OSError:
            return self.send_error(404, str(fp))
        self._send(body, mime)

    def _send(self, body, mime):
        self.send_response(200)
        self.send_header('Content-Type', mime)
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *a):
        pass


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--port", type=int, default=int(os.environ.get("PORT", 8782)),
                    help="port to bind (default $PORT or 8782)")
    args = ap.parse_args()
    if not (PREV / "index.html").exists():
        raise SystemExit("ERROR: preview/index.html missing — run `python tools/build_preview.py` first.")
    with socketserver.TCPServer(("127.0.0.1", args.port), H) as s:
        print(f"serving on http://127.0.0.1:{args.port}")
        try:
            s.serve_forever()
        except KeyboardInterrupt:
            print("\nStopped.")


if __name__ == "__main__":
    main()
