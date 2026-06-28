#!/usr/bin/env python3
"""Shell harness over HTTP — verify migrated src/web/ pages in a browser without the game.

Serves the real src/web/ MFD shell plus the migrated web assets, so the UI can be driven
end-to-end without extracting C# blobs. The MAP iframe receives tools/preview-mock.js,
which supplies the synthetic/captured /stream data that the shell forwards to page iframes.

  /                  -> src/web/shell/mfd.html
  /config            -> preview runtime URLs        (localhost/LAN URL for this harness port)
  /map-view[?bare]   -> src/web/pages/map/map.html      (the base map iframe; mock injected here)
  /<page>            -> src/web/pages/<page>/<page>.html  (any migrated page, e.g. /wpn /tgl)
  /weapon?...        -> captured weapon icon, or a mock 2:1 icon
  /airframe[-layout] -> captured AVN silhouette assets when available
  /assets/<x>        -> src/web/<x>, falling back to preview/assets/<x> captures
  else               -> preview/<x>                 (*.js, manifest, ...)

The MAP page is the only EventSource('/stream') consumer, so the mock (which stubs /stream,
/map, /icon, /weapon) is injected into it here. The shell loads /map-view?bare absolutely.

Usage:
    python tools/serve_web.py            # serve on http://127.0.0.1:8782
    python tools/serve_web.py --port N
    python tools/serve_web.py --open

Run tools/capture_assets.py while in-game to populate preview/assets/ with real assets.
Ctrl+C to stop.
"""
import argparse
import http.server
import json
import os
import pathlib
import posixpath
import socket
import socketserver
import urllib.parse
import webbrowser

REPO = pathlib.Path(__file__).resolve().parent.parent
WEB = REPO / "src" / "web"
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
    """If a capture exists, a <script> exposing the real frame + assets."""
    if not MANIFEST.exists():
        return ""
    m = json.loads(MANIFEST.read_text(encoding="utf-8"))
    frame = json.dumps(m.get("frame", {})).replace("</", "<\\/")
    assets = json.dumps(m.get("assets", {})).replace("</", "<\\/")
    return ("<script>\n"
            f"window.__PREVIEW_FRAME__ = {frame};\n"
            f"window.__PREVIEW_ASSETS__ = {assets};\n"
            "</script>\n")


def _manifest():
    if not MANIFEST.exists():
        return {}
    try:
        return json.loads(MANIFEST.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def _asset_ref(key):
    ref = (_manifest().get("assets") or {}).get(key)
    return ref if isinstance(ref, str) else None


def _asset_json(key):
    val = (_manifest().get("assets") or {}).get(key)
    return val if isinstance(val, dict) else None


def _preview_asset_path(ref):
    rel = posixpath.normpath(ref).lstrip('/\\')
    fp = (PREV / pathlib.Path(*rel.split('/'))).resolve()
    try:
        fp.relative_to(PREV.resolve())
    except ValueError:
        return None
    return fp


def _map_page():
    """The MAP page (src/web/pages/map/map.html) with the mock (+ any capture) injected before
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
            return self._file(WEB / 'shell' / 'mfd.html', 'text/html; charset=utf-8', cache=True)
        if path == '/config':
            return self._send(_config(self.server.server_address[1]), 'application/json; charset=utf-8')
        if path == '/map-view':
            try:
                return self._send(_map_page(), 'text/html; charset=utf-8')
            except OSError as e:
                return self.send_error(404, str(e))
        if path in ('/map', '/map.png', '/map.jpg'):
            ref = _asset_ref('map')
            if ref:
                fp = _preview_asset_path(ref)
                if fp and fp.exists():
                    return self._file(fp, _mime(str(fp)))
            return self.send_error(404, 'no captured map')
        if path == '/icon':
            typ = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('type', [''])[0]
            ref = _asset_ref('icon:' + typ)
            if ref:
                fp = _preview_asset_path(ref)
                if fp and fp.exists():
                    return self._file(fp, 'image/png')
            return self.send_error(404, 'no captured icon')
        if path == '/weapon':
            name = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('name', [''])[0]
            ref = _asset_ref('weapon:' + name)
            if ref:
                fp = _preview_asset_path(ref)
                if fp and fp.exists():
                    return self._file(fp, 'image/png')
            return self._send(WEAPON_SVG.encode('utf-8'), 'image/svg+xml')
        if path == '/airframe-layout':
            typ = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('type', [''])[0]
            layout = _asset_json('airframe-layout:' + typ)
            if layout:
                return self._send(json.dumps(layout).encode('utf-8'), 'application/json; charset=utf-8')
            return self.send_error(404, 'no captured airframe layout')
        if path == '/airframe':
            qs = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
            typ = qs.get('type', [''])[0]
            part = qs.get('part', [''])[0]
            ref = _asset_ref('airframe:' + typ + '|' + part)
            if ref:
                fp = _preview_asset_path(ref)
                if fp and fp.exists():
                    return self._file(fp, 'image/png')
            return self.send_error(404, 'no captured airframe part')
        if path.startswith('/assets/'):
            rel = posixpath.normpath(path[len('/assets/'):]).lstrip('/\\')
            web_fp = WEB.joinpath(*rel.split('/'))
            if web_fp.exists():
                return self._file(web_fp, _mime(rel), cache=True)
            return self._file(PREV.joinpath('assets', *rel.split('/')), _mime(rel))
        # Any migrated page: /<name> -> src/web/pages/<name>/<name>.html (wpn, tgl, ...).
        name = path.lstrip('/')
        page = WEB / 'pages' / name / f'{name}.html'
        if name and '/' not in name and page.exists():
            return self._file(page, 'text/html; charset=utf-8', cache=True)
        rel = posixpath.normpath(path.lstrip('/')).lstrip('/\\')
        return self._file(PREV.joinpath(*rel.split('/')), _mime(rel))

    @staticmethod
    def _etag(fp):
        st = pathlib.Path(fp).stat()
        return '"%x-%x"' % (int(st.st_mtime), st.st_size)

    def _file(self, fp, mime, cache=False):
        fp = pathlib.Path(fp)
        # Mirror the mod's ServeAssetRel caching for the real src/web assets: ETag + revalidate each
        # load (Cache-Control: no-cache), returning a bodiless 304 when the client's validator still
        # matches. The harness validates per file (mtime+size) — handy while live-editing, so an
        # edited file busts on its own — where the mod uses one build MVID across all embedded
        # assets; the browser behaviour (200 then 304) is identical either way.
        if cache:
            try:
                etag = self._etag(fp)
            except OSError:
                return self.send_error(404, str(fp))
            if self.headers.get('If-None-Match') == etag:
                self.send_response(304)
                self.send_header('ETag', etag)
                self.send_header('Cache-Control', 'no-cache')
                self.end_headers()
                return
        try:
            body = fp.read_bytes()
        except OSError:
            return self.send_error(404, str(fp))
        self._send(body, mime, {'ETag': etag, 'Cache-Control': 'no-cache'} if cache else None)

    def _send(self, body, mime, extra=None):
        self.send_response(200)
        self.send_header('Content-Type', mime)
        self.send_header('Content-Length', str(len(body)))
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *a):
        pass


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--port", type=int, default=int(os.environ.get("PORT", 8782)),
                    help="port to bind (default $PORT or 8782)")
    ap.add_argument("--open", action="store_true", help="open the shell in a browser on start")
    args = ap.parse_args()
    if not (WEB / "shell" / "mfd.html").exists():
        raise SystemExit("ERROR: src/web/shell/mfd.html missing.")
    with socketserver.TCPServer(("127.0.0.1", args.port), H) as s:
        url = f"http://127.0.0.1:{args.port}/"
        print(f"serving on {url}")
        if args.open:
            webbrowser.open(url)
        try:
            s.serve_forever()
        except KeyboardInterrupt:
            print("\nStopped.")


if __name__ == "__main__":
    main()
