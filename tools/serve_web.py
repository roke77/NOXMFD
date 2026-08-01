#!/usr/bin/env python3
"""Shell harness over HTTP — verify migrated src/web/ pages in a browser without the game.

Serves the real src/web/ MFD shell plus the migrated web assets, so the UI can be driven
end-to-end without extracting C# blobs. The MAP iframe receives tools/preview-mock.js,
which supplies the synthetic/captured /stream data that the shell forwards to page iframes.

  /                  -> src/web/shell/mfd.html
  /f35               -> src/web/shell/f35/f35.html      (the F-35 layout — see docs/layouts.md)
  /config            -> preview runtime URLs        (localhost/LAN URL for this harness port)
  /map-view[?bare]   -> src/web/pages/map/map.html      (the base map iframe; mock injected here)
  /<page>            -> src/web/pages/<page>/<page>.html  (any migrated page, e.g. /wpn /tgt)
  /weapon?...        -> captured weapon icon, or a mock 2:1 icon
  /hud-cat-icon?cat= -> captured HUD OPTIONS category glyph, or a mock icon
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
import time
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

# Mock TGT vehicle-type icon (the real ones are captured from Encyclopedia.i.vehicleTypes in-game).
# A simple square glyph so the preview shows the icon slot filled; the label carries the real name.
TGT_ICON_SVG = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">'
                '<rect x="3" y="3" width="18" height="18" rx="2" fill="none" stroke="#39ff14" '
                'stroke-width="2"/><circle cx="12" cy="12" r="3.5" fill="#39ff14"/></svg>')

# Mock BDF ship-type icon + faction logo (the real ones are captured from Encyclopedia.i.shipTypes /
# Faction.factionColorLogo in-game — see docs/bdf-page.md). A diamond glyph so the preview shows the
# icon slot filled for both the ship row and the header logo; labels/counts carry the real meaning.
BDF_ICON_SVG = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">'
                '<polygon points="12,2 22,12 12,22 2,12" fill="none" stroke="#39ff14" '
                'stroke-width="2"/></svg>')

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


# Mock of the plugin's /hud-options (TelemetryServer.RefreshHudOptions). A real in-game snapshot,
# so the HUD page can be built and eyeballed in the harness. The write side (hud.set/hud.mode) has
# no mock — commands POST and are swallowed — so toggles here won't change this response; that path
# is only testable in game.
def _hud_options():
    veh = ["TRUCK", "UGV", "LCV", "AFV", "MBT", "ART", "AAA", "IR_SAM", "R_SAM", "RDR"]
    bld = ["CIV", "FAC", "RDR", "DEP", "HGR", "DEF", "AMMO"]
    return json.dumps({
        "mode": 1,  # GUN
        "modes": ["NAV", "GUN", "A2A", "A2G", "EW", "LOG"],
        "categories": [False, True, False, False, True, False, False],
        "vehicles":  [{"n": n, "on": True} for n in veh],
        "buildings": [{"n": n, "on": True} for n in bld],
        # native-HUD declutter flags (HudDeclutterConfig) — true = that widget is hidden. One hidden
        # here so the off state is visible in the harness. The write side (declutter.set) has no mock.
        "declutter": {"weapon": False, "minimap": True, "boxes": False},
    }).encode("utf-8")


# Stateful mock of the plugin's /keybinds-config + keybind.* commands, so the /keybinds page's
# whole flow (render, keyboard set, joystick arm-capture) is drivable in the harness. Arming a
# joystick capture "captures" a fake button ~1.5s later (simulated on the next poll after the
# deadline — no threads).
KEYBINDS = [
    {"id": "flares", "section": "COUNTERMEASURES", "label": "Flares",
     "description": "Select + deploy IR flares. Tap to pop a set, hold to keep popping.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "jammer", "section": "COUNTERMEASURES", "label": "Jammer",
     "description": "Select + activate the radar jammer. HOLD to jam.",
     "key": "J", "joyButton": 3, "joyNum": 2},
    {"id": "cycle-guns", "section": "WEAPONS", "label": "Cycle Guns",
     "description": "Select a gun.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "cycle-missiles", "section": "WEAPONS", "label": "Cycle Missiles",
     "description": "Select a missile or rocket.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "cycle-bombs", "section": "WEAPONS", "label": "Cycle Bombs",
     "description": "Select a bomb.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "gun-trigger", "section": "WEAPONS", "label": "Gun Trigger",
     "description": "Fire your gun; HOLD for continuous fire. With a non-gun selected, the first press only switches to the gun — press again to fire.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "weapon-release", "section": "WEAPONS", "label": "Weapon Release",
     "description": "Release your missile/bomb; HOLD to keep releasing. With a gun selected, the first press only switches to it — press again to release.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "gear-up", "section": "GEAR", "label": "Gear Up",
     "description": "Raise the landing gear.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "gear-down", "section": "GEAR", "label": "Gear Down",
     "description": "Lower the landing gear.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "map-follow", "section": "MAP", "label": "Follow",
     "description": "Toggle FLW on the focused MAP display.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "map-zoom-in", "section": "MAP", "label": "Zoom In",
     "description": "Zoom in on the focused MAP display.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "map-zoom-out", "section": "MAP", "label": "Zoom Out",
     "description": "Zoom out on the focused MAP display.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "soi-next", "section": "SOI", "label": "SOI Next",
     "description": "Move focus to the next display.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "soi-prev", "section": "SOI", "label": "SOI Prev",
     "description": "Move focus to the previous display.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "soi-nav-up", "section": "SOI", "label": "Nav Up",
     "description": "Move the cursor up the focused display's key labels.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "soi-nav-down", "section": "SOI", "label": "Nav Down",
     "description": "Move the cursor down the focused display's key labels.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "soi-select", "section": "SOI", "label": "Select",
     "description": "Press the label the cursor is on, as if you had clicked that key.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "cursor-up", "section": "CURSOR", "label": "Cursor Up",
     "description": "Move the cursor up. Only acts while a display with a cursor is focused.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "cursor-down", "section": "CURSOR", "label": "Cursor Down",
     "description": "Move the cursor down. Only acts while a display with a cursor is focused.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "cursor-left", "section": "CURSOR", "label": "Cursor Left",
     "description": "Move the cursor left. Only acts while a display with a cursor is focused.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "cursor-right", "section": "CURSOR", "label": "Cursor Right",
     "description": "Move the cursor right. Only acts while a display with a cursor is focused.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "cursor-select", "section": "CURSOR", "label": "Cursor Select",
     "description": "Select whatever the cursor is on. Only acts while a display with a cursor is focused.",
     "key": "", "joyButton": -1, "joyNum": 0},
    # Axis-only (docs/map-cursor.md): no key/joyButton/joyNum fields at all — the real server omits
    # them for an axis-capable bind too, and keybinds.js renders one wide cell instead of empty
    # key/joy cells when a row has no "key" field.
    {"id": "cursor-axis-h", "section": "CURSOR", "label": "Cursor Horizontal",
     "description": "Analog axis (HOTAS mini-stick/hat) driving the cursor left/right — overrides "
                     "Cursor Left/Right when deflected. Only acts while a display with a cursor "
                     "is focused.",
     "axis": -1, "axisNum": 0, "axisInvert": False},
    {"id": "cursor-axis-v", "section": "CURSOR", "label": "Cursor Vertical",
     "description": "Analog axis driving the cursor up/down — overrides Cursor Up/Down when "
                     "deflected. Only acts while a display with a cursor is focused.",
     "axis": -1, "axisNum": 0, "axisInvert": False},
]
KB_STATE = {"capturing": None, "capturingKind": None, "armed_at": 0.0, "bgInput": False}


def _keybinds_config():
    # simulate the plugin capturing a stick button/axis 1.5s after arming
    if KB_STATE["capturing"] and time.monotonic() - KB_STATE["armed_at"] > 1.5:
        for b in KEYBINDS:
            if b["id"] == KB_STATE["capturing"]:
                if KB_STATE["capturingKind"] == "axis":
                    b["axis"], b["axisNum"] = 3, 1
                else:
                    b["joyButton"], b["joyNum"] = 7, 1
        KB_STATE["capturing"] = None
        KB_STATE["capturingKind"] = None
    notes = {"MAP": "Follow / Zoom In / Zoom Out are direct binds for what the bezel's FLW and Z+/Z- "
                    "keys already do on the focused MAP display.",
             "SOI": "One display at a time is the sensor of interest — it rings itself in white, and "
                    "these keys drive it. Nothing is focused until you press SOI Next or Prev; from "
                    "there they cycle through the open displays.",
             "CURSOR": "Moves a cursor over whichever focused display has one (MAP, for now) and "
                       "selects what it's on. Cursor Horizontal/Vertical are the same movement as an "
                       "analog HOTAS axis — bind either or both; a deflected axis overrides its two keys.",
             "WEAPONS": "Cycle keys select the last soft-selected weapon of their type, or the first "
                        "in the list. Repeated presses cycle to the next one, skipping depleted "
                        "weapons. Cycling to a different type leaves the current one soft-selected."}
    return json.dumps({"binds": KEYBINDS, "notes": notes,
                       "capturing": KB_STATE["capturing"],
                       "capturingKind": KB_STATE["capturingKind"],
                       "bgInput": KB_STATE["bgInput"]}).encode("utf-8")


def _keybinds_command(env):
    cmd, bind = env.get("cmd", ""), env.get("bind", "")
    row = next((b for b in KEYBINDS if b["id"] == bind), None)
    if cmd == "keybind.set-key" and row is not None:
        key = env.get("key", "")
        row["key"] = "" if key in ("", "None") else key
    elif cmd == "keybind.arm-joy" and row is not None:
        KB_STATE.update(capturing=bind, capturingKind="joy", armed_at=time.monotonic())
    elif cmd == "keybind.cancel-joy":
        KB_STATE["capturing"] = None
        KB_STATE["capturingKind"] = None
    elif cmd == "keybind.clear-joy" and row is not None:
        row["joyButton"] = -1
        row["joyNum"] = 0
    elif cmd == "keybind.arm-axis" and row is not None:
        KB_STATE.update(capturing=bind, capturingKind="axis", armed_at=time.monotonic())
    elif cmd == "keybind.cancel-axis":
        KB_STATE["capturing"] = None
        KB_STATE["capturingKind"] = None
    elif cmd == "keybind.clear-axis" and row is not None:
        row["axis"] = -1
        row["axisNum"] = 0
        row["axisInvert"] = False
    elif cmd == "keybind.set-axis-invert" and row is not None:
        row["axisInvert"] = bool(env.get("on", False))
    elif cmd == "keybind.set-bg-input":
        KB_STATE["bgInput"] = bool(env.get("on", False))
    else:
        return False
    return True


class H(http.server.SimpleHTTPRequestHandler):
    def do_POST(self):
        # /command mock: keybind.* commands mutate the keybind mock state; everything else is
        # swallowed with 204, mirroring the plugin's fire-and-forget contract.
        if self.path.split('?', 1)[0] == '/command':
            try:
                n = int(self.headers.get('Content-Length') or 0)
                env = json.loads(self.rfile.read(n) or b'{}')
            except (ValueError, OSError):
                env = {}
            _keybinds_command(env)
            self.send_response(204)
            self.end_headers()
            return
        self.send_error(404)

    def do_GET(self):
        path = self.path.split('?', 1)[0]
        if path in ('/', '/index.html'):
            return self._file(WEB / 'shell' / 'mfd.html', 'text/html; charset=utf-8', cache=True)
        if path == '/f35':
            return self._file(WEB / 'shell' / 'f35' / 'f35.html', 'text/html; charset=utf-8', cache=True)
        if path == '/config':
            return self._send(_config(self.server.server_address[1]), 'application/json; charset=utf-8')
        if path == '/hud-options':
            return self._send(_hud_options(), 'application/json; charset=utf-8')
        if path == '/keybinds-config':
            return self._send(_keybinds_config(), 'application/json; charset=utf-8')
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
        if path in ('/tgt-icon', '/building-icon'):
            # Real captured type sprite if a capture ran (manifest key 'tgt-icon:<t>' /
            # 'building-icon:<t>'); otherwise the generic placeholder, so both the vehicle and
            # building chips show *an* icon in the mock harness.
            typ = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('type', [''])[0]
            ref = _asset_ref(path.lstrip('/') + ':' + typ)
            if ref:
                fp = _preview_asset_path(ref)
                if fp and fp.exists():
                    return self._file(fp, 'image/png')
            return self._send(TGT_ICON_SVG.encode('utf-8'), 'image/svg+xml')
        if path == '/hud-cat-icon':
            # Real captured category-row glyph if a capture ran (manifest key
            # 'hud-cat-icon:<CAT>'); otherwise the same generic placeholder as the vehicle/building
            # chips above, so the HUD page's category rows show *an* icon in the mock harness.
            cat = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('cat', [''])[0]
            ref = _asset_ref('hud-cat-icon:' + cat)
            if ref:
                fp = _preview_asset_path(ref)
                if fp and fp.exists():
                    return self._file(fp, 'image/png')
            return self._send(TGT_ICON_SVG.encode('utf-8'), 'image/svg+xml')
        if path == '/bdf-icon':
            return self._send(BDF_ICON_SVG.encode('utf-8'), 'image/svg+xml')
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
        # Any migrated page: /<name> -> src/web/pages/<name>/<name>.html (wpn, tgt, ...).
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
    # Threaded: the shell opens several connections at once (shell + map/page iframes + assets).
    # A single-threaded server serialises them and stalls if any one handler blocks; ThreadingTCPServer
    # keeps every reload responsive. daemon_threads so Ctrl+C exits without waiting on open sockets.
    class Server(socketserver.ThreadingTCPServer):
        daemon_threads = True
        # On Windows SO_REUSEADDR lets a SECOND instance bind the same port while the first is
        # alive — stale servers then keep answering with old code. Windows doesn't need the flag
        # to rebind after a normal exit, so only use it on POSIX (where it just skips TIME_WAIT).
        allow_reuse_address = os.name != "nt"
    with Server(("127.0.0.1", args.port), H) as s:
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
