#!/usr/bin/env python3
"""Shell harness over HTTP — exercises the real src/web/ pages in a browser without the game.

Serves the real src/web/ MFD shell and its web assets, so the UI can be driven end-to-end
without extracting C# blobs. The MAP iframe receives tools/preview-mock.js,
which supplies the synthetic/captured /stream data that the shell forwards to page iframes.

  /                  -> src/web/shell/classic/mfd.html
  /f35               -> src/web/shell/f35/f35.html      (the F-35 layout — see docs/layouts.md)
  /thrl-demo         -> tools/thrl-demo.html    (standalone THRL slider demo, no shell/mock needed)
  /config            -> preview runtime URLs        (localhost/LAN URL for this harness port)
  /map-view[?bare]   -> src/web/pages/map/map.html      (the base map iframe; mock injected here)
  /wpt               -> src/web/pages/wpt/wpt.html   (showcase route seeded into localStorage)
  /<page>            -> src/web/pages/<page>/<page>.html  (any page, e.g. /wpn /tgt)
  /weapon?...        -> captured weapon icon, or a mock 2:1 icon
  /hud-cat-icon?cat= -> captured HUD OPTIONS category glyph, or a mock icon
  /airframe[-layout] -> captured AVN silhouette assets when available
  /assets/<x>        -> src/web/<x>, falling back to preview/assets/<x> captures
  else               -> preview/<x>                 (*.js, manifest, ...)

The MAP page is the only EventSource('/stream') consumer, so the mock (which stubs /stream,
/map, /icon, /weapon) is injected into it here. The shell loads /map-view?bare absolutely.

/ and /f35 poll /__reload-token (max mtime across src/web/ + preview-mock.js) and reload the whole
shell on change, so an edit shows up without an alt-tab-and-refresh — see docs/live-reload.md.

Usage:
    python tools/serve_web.py            # serve on http://127.0.0.1:8782
    python tools/serve_web.py --port N
    python tools/serve_web.py --host 0.0.0.0   # also reachable from the LAN (needs a firewall allow)
    python tools/serve_web.py --open

Run tools/capture_assets.py while in-game to populate preview/captures/<timestamp>/ with real
assets — every run adds a new dated folder (a library, not one slot), and preview/captures/CURRENT
names whichever one is live here. To switch which capture is live, just overwrite that file with a
different folder's name; no server restart needed, it's read fresh on every request. Falls back to
the older single-slot preview/assets/manifest.json if neither CURRENT nor its target exist.
Ctrl+C to stop.
"""
import argparse
import base64
import hashlib
import http.server
import json
import os
import pathlib
import posixpath
import re
import socket
import socketserver
import sys
import time
import urllib.parse
import uuid
import webbrowser

import keybinds_source

REPO = pathlib.Path(__file__).resolve().parent.parent
WEB = REPO / "src" / "web"
PREV = REPO / "preview"
MOCK = REPO / "tools" / "preview-mock.js"
CAPTURES = PREV / "captures"
LEGACY_MANIFEST = PREV / "assets" / "manifest.json"   # pre-library single-slot capture, still honored

# Pin one specific preview/captures/<name>/ folder, bypassing CURRENT entirely — set directly by a
# script that imports this module (capture_screenshots.py drives one capture at a time this way,
# without touching the shared CURRENT pointer a manually-running server elsewhere might depend on)
# or via --capture on the CLI. None means "follow CURRENT", the normal behavior.
CAPTURE_OVERRIDE = None


def _manifest_path():
    """CAPTURE_OVERRIDE if set, else whichever capture CURRENT names, else the old single-slot
    preview/assets/manifest.json. Resolved per-call (not cached) so switching either takes effect
    on the very next request — no server restart to preview a different capture."""
    if CAPTURE_OVERRIDE:
        p = CAPTURES / CAPTURE_OVERRIDE / "manifest.json"
        if p.exists():
            return p
    cur = CAPTURES / "CURRENT"
    if cur.exists():
        name = cur.read_text(encoding="utf-8").strip()
        p = CAPTURES / name / "manifest.json"
        if name and p.exists():
            return p
    return LEGACY_MANIFEST

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

# Mock DOC kneeboard images (issue #82) — the real files live on disk (DocEndpoint.cs), so there's
# nothing to capture in-game; this is just a fixed mock list + one placeholder graphic per name, so
# the preview harness can exercise the index/image/NEXT/PREV flow without a real BepInEx install.
DOC_MOCK_FILES = ['heartland-diagram.png', 'kadena-approach.png', 'checklist-startup.jpg']
DOC_IMAGE_SVG_TEMPLATE = (
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300">'
    '<rect width="400" height="300" fill="none" stroke="#39ff14" stroke-width="3"/>'
    '<text x="200" y="155" fill="#39ff14" font-size="18" text-anchor="middle" '
    'font-family="monospace">{name}</text></svg>')

# Mock TGP feed frame (the real one is a captured still off /tgp.mjpg — see capture_assets.py).
TGP_SVG = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 240">'
           '<rect width="320" height="240" fill="none" stroke="#39ff14" stroke-width="3"/>'
           '<text x="160" y="128" fill="#39ff14" font-size="20" text-anchor="middle" '
           'font-family="monospace">NO CAPTURE</text></svg>')

MIME = {'.css': 'text/css', '.js': 'text/javascript', '.woff2': 'font/woff2', '.html': 'text/html',
        '.json': 'application/json', '.png': 'image/png', '.svg': 'image/svg+xml', '.jpg': 'image/jpeg'}


def _mime(rel):
    return MIME.get(os.path.splitext(rel)[1], 'application/octet-stream')


def _capture_injection():
    """If a capture exists, a <script> exposing the real frame + assets."""
    m = _manifest()
    if not m:
        return ""
    frame = json.dumps(m.get("frame", {})).replace("</", "<\\/")
    assets = json.dumps(m.get("assets", {})).replace("</", "<\\/")
    return ("<script>\n"
            f"window.__PREVIEW_FRAME__ = {frame};\n"
            f"window.__PREVIEW_ASSETS__ = {assets};\n"
            "</script>\n")


def _manifest():
    p = _manifest_path()
    if not p.exists():
        return {}
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def _asset_ref(key):
    ref = (_manifest().get("assets") or {}).get(key)
    return ref if isinstance(ref, str) else None


def _asset_json(key):
    val = (_manifest().get("assets") or {}).get(key)
    return val if isinstance(val, dict) else None


def _captured_or(key, fallback_fn):
    """A capture's inlined JSON (hud-options/wpt-options/rates-config) if present, else the
    harness's own hand-authored mock — same fallback shape every other captured asset uses."""
    val = _asset_json(key)
    return json.dumps(val).encode('utf-8') if val is not None else fallback_fn()


def _preview_asset_path(ref):
    rel = posixpath.normpath(ref).lstrip('/\\')
    fp = (PREV / pathlib.Path(*rel.split('/'))).resolve()
    try:
        fp.relative_to(PREV.resolve())
    except ValueError:
        return None
    return fp


def _map_page(telemetry_only=False):
    """The MAP page (src/web/pages/map/map.html) with the mock (+ any capture) injected before
    </head>, so its EventSource('/stream') and /map,/icon,/weapon fetches resolve in the browser.
    Built fresh per request so edits to map.html / the mock show up on reload."""
    page = WEB / "services" / "telemetry-tap.html" if telemetry_only else WEB / "pages" / "map" / "map.html"
    html = page.read_text(encoding="utf-8")
    mock = MOCK.read_text(encoding="utf-8").strip()
    injection = _capture_injection() + mock + "\n" + _wpt_seed_script()
    return html.replace("</head>", injection + "</head>", 1).encode("utf-8")


def _tgt_page():
    """The TGT page with a fetch wrapper that, once a /command POST lands, nudges the MAP iframe's
    mock stream (preview-mock.js) to re-poll /__preview-push right away — TGT's header sort
    (tgt.sort) otherwise waits up to that poll's 1.5s interval to show."""
    html = (WEB / "pages" / "tgt" / "tgt.html").read_text(encoding="utf-8")
    nudge = ("<script>(function(){const f=window.fetch.bind(window);window.fetch=function(i,o){"
             "const p=f(i,o);if(String(i).indexOf('/command')===0)p.then(function(){"
             "try{new BroadcastChannel('preview-push').postMessage(1);}catch(e){}});return p;};})();</script>\n")
    return html.replace("</head>", nudge + "</head>", 1).encode("utf-8")


def _wpt_page():
    """The WPT page (src/web/pages/wpt/wpt.html) with the showcase route seeded before </head> —
    unlike map.html above it has no /stream mock to inject, just the localStorage seed, since WPT
    can be opened standalone without MAP ever loading (the seed can't rely on map.html having run
    first)."""
    html = (WEB / "pages" / "wpt" / "wpt.html").read_text(encoding="utf-8")
    return html.replace("</head>", _wpt_seed_script() + "</head>", 1).encode("utf-8")


def _reload_token():
    """max(mtime) across every served web asset (see docs/live-reload.md) — a cheap comparable
    value that changes whenever a saved edit would change what the browser sees. Includes MOCK
    since the MAP page's mock injection is as much "what a browser sees" as src/web/ itself.
    Also includes the CURRENT pointer file (so re-pointing which capture is live triggers a
    reload) and the currently-active capture folder itself (so a fresh capture_assets.py run —
    new/updated icons, map.jpg, screenshots — does too), rather than every capture in the library:
    old captures aren't served, so their mtimes are noise a rebuild wouldn't touch anyway."""
    newest = MOCK.stat().st_mtime
    for fp in WEB.rglob("*"):
        if fp.is_file():
            newest = max(newest, fp.stat().st_mtime)
    cur = CAPTURES / "CURRENT"
    if cur.exists():
        newest = max(newest, cur.stat().st_mtime)
    manifest = _manifest_path()
    if manifest.exists():
        for fp in manifest.parent.rglob("*"):
            if fp.is_file():
                newest = max(newest, fp.stat().st_mtime)
    return newest


_RELOAD_WATCHER_SCRIPT = """
<script>
(function () {
  var last = null;
  function poll() {
    fetch('/__reload-token', { cache: 'no-store' }).then(function (r) { return r.text(); })
      .then(function (t) {
        if (last !== null && t !== last) { window.location.reload(); return; }
        last = t;
      }).catch(function () { /* server restarting or unreachable — next poll retries */ });
  }
  poll();
  setInterval(poll, 750);
})();
</script>
"""


def _shell_page(fp):
    """A top-level shell page (mfd.html / f35.html) with the live-reload watcher spliced before
    </head> — see docs/live-reload.md. Built fresh per request, same as _map_page()/_wpt_page(),
    so edits to the shell itself show up on reload too. Only the two top-level shells get this:
    a full-page reload there already re-fetches every iframe/pane beneath it, so injecting the
    same watcher into every individual page would just mean duplicate reload triggers."""
    html = fp.read_text(encoding="utf-8")
    return html.replace("</head>", _RELOAD_WATCHER_SCRIPT + "</head>", 1).encode("utf-8")


def _detect_lan_ip():
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.connect(("8.8.8.8", 65530))
            ip = sock.getsockname()[0]
        return "" if not ip or ip.startswith("127.") or ip.startswith("0.") else ip
    except OSError:
        return ""


def _plugin_version():
    """Read <Version> straight from NOXMFD.csproj so the harness mock never drifts from the real
    plugin version the DLL would actually report."""
    m = re.search(r"<Version>([^<]+)</Version>", (REPO / "NOXMFD.csproj").read_text(encoding="utf-8"))
    return m.group(1) if m else "0.0.0"


def _config(port):
    lan_ip = _detect_lan_ip()
    return json.dumps({
        "localhost": f"http://localhost:{port}",
        "lanUrl": f"http://{lan_ip}:{port}" if lan_ip else "",
        "port": port,
        "version": _plugin_version(),
    }).encode("utf-8")


def _preview_push(query):
    """Change-gated snapshots for preview-mock.js's synthetic SSE connection."""
    previous = urllib.parse.parse_qs(query)
    snapshots = {
        "sqd": _squad_state(),
        "td-state": _td_state(),
        "wpt-options": _captured_or("wpt-options", _wpt_options),
        "keybinds-config": _keybinds_config(),
        "hud-options": _captured_or("hud-options", _hud_options),
        "server-players": _server_players(),
        "themes": _themes_state(),
    }
    hashes = {}
    events = {}
    for name, payload in snapshots.items():
        digest = hashlib.sha1(payload).hexdigest()[:12]
        hashes[name] = digest
        if previous.get(name, [""])[0] != digest:
            events[name] = json.loads(payload)
    return json.dumps({"hashes": hashes, "events": events, "tgtSort": TGT_SORT}).encode("utf-8")


# Mock of the plugin's /hud-options (TelemetryServer.RefreshHudOptions). A real in-game snapshot,
# so the HUD page can be built and eyeballed in the harness. The write side (hud.set/hud.mode) has
# no mock — commands POST and are swallowed — so toggles here won't change this response; that path
# is only testable in game.
def _hud_options():
    veh = ["TRUCK", "UGV", "LCV", "AFV", "MBT", "ART", "AAA", "IR_SAM", "R_SAM", "RDR"]
    bld = ["CIV", "FAC", "RDR", "DEP", "HGR", "DEF", "AMMO"]
    current = PRESETS[PRESET_STATE["current"] - 1]
    return json.dumps({
        "mode": 1,  # GUN
        "modes": ["NAV", "GUN", "A2A", "A2G", "EW", "LOG"],
        "categories": [False, True, False, False, True, False, False],
        "vehicles":  [{"n": n, "on": True} for n in veh],
        "buildings": [{"n": n, "on": True} for n in bld],
        # native-HUD declutter flags (HudDeclutterConfig) — true = that widget is hidden. One hidden
        # here so the off state is visible in the harness. The write side (declutter.set) has no mock.
        "declutter": {"weapon": False, "minimap": True, "boxes": False, "feed": False},
        # Current HUD preset (issue #50 follow-up) — index/name only, stateful via PRESET_STATE/
        # PRESETS below so the preset label follows a save/rename/load in the harness.
        "preset": {"index": current["index"], "name": current["name"]},
    }).encode("utf-8")


# Mock of the plugin's /wpt-options (RouteStore.RoutesJson, docs/hud-waypoint-indicator.md) — a
# real snapshot pulled from a local mod session's BepInEx/config/com.roque.NOXMFD.routes.json
# (2026-08-19), so MAP/WPT have real routes to render in the harness instead of "NO ROUTES YET".
# Static, same as _hud_options — the write side (wpt.* commands) has no mock, POSTs are swallowed,
# so editing here won't change this response; that path is only testable in game. sharedBy/
# sharedWithSquad/pendingShared (docs/squadron-transport.md) are sample data purely so the
# accept/reject, read-only-shared-route, and SQD-label UI states have something to render — same
# reasoning as _SQD's roster mock.
#
# This whole mock is a squad MEMBER's view, not the leader's (see the SQD page's own mock role) —
# sharedWithSquad is a LEADER-only flag (RouteStore.ShareRoute/BroadcastIfShared never set it for
# anyone else), so the pilot's own two routes below (RT-8F6A9, RT-3F9C1) must NOT carry it. The
# only route that legitimately gets the SQD label here is RT-6CA67, accepted FROM the leader
# (sharedBy), same as it would be for a real member.
def _wpt_options():
    return json.dumps({
        # Routes remain available for R+/R- while the no-route stop makes steer-point guidance and
        # the S+/S- relabeling visible by default.
        "activeRouteId": None,
        "activeSteerPointId": "s_preview_ip",
        "routes": [
            {
                # This pilot's OWN route, made locally — not shared, no SQD label (see file header).
                "id": "r_6422161b6ecd4e0e8bc0a99c627ef397", "name": "RT-8F6A9", "nextIndex": 4,
                "sharedBy": "",
                "waypoints": [
                    {"id": "w_df45b83cc4e5402cbf3be75e38f6103b", "name": "", "x": 7526.1,  "z": 8584.3},
                    {"id": "w_7f942f59e2104b50b8776839711810c3", "name": "", "x": 18355.2, "z": 8135.4},
                    {"id": "w_5226ee45a18244d0a4dee68cf235232c", "name": "", "x": 20805.4, "z": 16982.0},
                    {"id": "w_16ec2f57b0c54914aae8ec4ebf17e4d3", "name": "", "x": 18504.9, "z": 22462.1},
                    {"id": "w_9254e5131a064af7bb83f32dc85f89ac", "name": "", "x": 8741.8,  "z": 23303.7},
                    {"id": "w_f492fa611502406fb594bed119f4eebc", "name": "", "x": 24847.2, "z": 26224.2},
                    {"id": "w_588fe2417e7f4badb016d8b41a473cda", "name": "", "x": 28691.2, "z": 16196.3},
                ],
            },
            {
                "id": "r_1dd9973ab97c415e8256e829921a2320", "name": "RT-6CA67", "nextIndex": 4,
                "sharedBy": "Foxtrot",
                "waypoints": [
                    {"id": "w_5eb7026834e0487982503ddf63a8ac07", "name": "", "x": 7113.0,  "z": 8578.7},
                    {"id": "w_5d989cdca24146deadf7d72527295197", "name": "", "x": 21153.2, "z": 7571.6},
                    {"id": "w_23d2e0ac003e4bb2ab8758283d7be41d", "name": "", "x": 27480.7, "z": 18649.8},
                    {"id": "w_befad7e5757848928451f81123c0a6cf", "name": "", "x": 19925.4, "z": 29130.0},
                    {"id": "w_9242d06d1e20411eac306da4b9a0078e", "name": "", "x": 6829.6,  "z": 21828.5},
                ],
            },
            {
                # Also this pilot's OWN route, made locally, not active — again no SQD label; a
                # member has no way to put one on a route of their own (see file header).
                "id": "r_3f9c1a2b4e5d4c8ba17092836451affc", "name": "RT-3F9C1", "nextIndex": 2,
                "sharedBy": "",
                "waypoints": [
                    {"id": "w_a15c8e2f9b3a4d6c8e0f1a2b3c4d5e6f", "name": "IP",  "x": 9820.4,  "z": 11230.7},
                    {"id": "w_b26d9f3a0c4b5e7d9f1a2b3c4d5e6f70", "name": "",    "x": 15410.2, "z": 14880.5},
                    {"id": "w_c37e0a4b1d5c6f8e0a2b3c4d5e6f7081", "name": "",    "x": 21005.9, "z": 9720.3},
                ],
            },
        ],
        "steerPoints": [
            {"id": "s_preview_ip", "name": "INGRESS", "x": 7526.1, "z": 8584.3,
             "sharedBy": "", "sharedWithSquad": False},
            {"id": "s_preview_tanker", "name": "TANKER", "x": 13200.0, "z": 11800.0,
             "sharedBy": "Foxtrot", "sharedWithSquad": False},
        ],
        "pendingShared": [
            {"id": "r_sample_pending_share", "name": "RT-9B21C", "fromName": "Foxtrot", "waypointCount": 6},
        ],
        "pendingSharedSteerPoints": [
            {"id": "s_sample_pending", "name": "HOLD NORTH", "fromName": "Foxtrot", "x": 20500.0, "z": 27800.0},
        ],
    }).encode("utf-8")


# Mock of the plugin's /rates-config, so MAP CFG/TGP CFG controls have something to initialize
# from in the harness. The write
# side (rates.set) has no mock — commands POST and are swallowed — so moving a slider/button here
# won't change this response; that path is only testable in game.
def _rates_config():
    return json.dumps({
        "fastHz": 10,
        "contactHz": 4,
        "tgpHz": 15,
        "tgpResolution": "native",
        "tgpJpegQuality": "mid",
        "tgpQuality": "native",
        "tgpSuppressNative": False,
        "mapShowPlayerNames": False
    }).encode("utf-8")


# Stateful mock of the plugin's ThemeStore (CFG > UI, issue 105): /themes, /colors-override.css,
# the "themes" push and the theme.* commands, so the UI page and the shells' live repaint work in
# the harness. The token list is read from ThemeColors.cs so it can't drift from the plugin's.
THEME_TOKENS = re.findall(r'"(--no-[\w-]+)"', re.search(r'Tokens\s*=\s*\{(.*?)\};',
                          (REPO / "src" / "plugin" / "Stores" / "ThemeColors.cs").read_text(encoding="utf-8"), re.S).group(1))
# Saved themes to start from, unlike the folder's Dusk and Monokai: Arctic (ice blue on navy), Dracula
# (draculatheme.com's palette), Elite (Elite Dangerous' orange HUD) and Star (Star Citizen's blue UI).
THEMES = [
    {"id": "t_arctic", "name": "Arctic", "colors": {
        "--no-green-rgb": "#5ce1ff", "--no-white-rgb": "#e8f6ff", "--no-red-rgb": "#ff5c7a",
        "--no-amber-rgb": "#ffd166", "--no-gray-rgb": "#4a6275", "--no-bg": "#06121c",
        "--no-panel-border": "#1c3a4f", "--no-ink": "#041018", "--no-squad-rgb": "#9b8cff",
        "--no-route-cyan": "#b8f2ff", "--no-target-orange": "#ffa94d"}},
    {"id": "t_dracula", "name": "Dracula", "colors": {
        "--no-green-rgb": "#50fa7b", "--no-white-rgb": "#f8f8f2", "--no-red-rgb": "#ff5555",
        "--no-amber-rgb": "#ffb86c", "--no-gray-rgb": "#6272a4", "--no-bg": "#282a36",
        "--no-panel-border": "#44475a", "--no-ink": "#282a36", "--no-squad-rgb": "#8be9fd",
        "--no-purple-rgb": "#bd93f9", "--no-blue-rgb": "#8be9fd", "--no-friendly-blue": "#8be9fd",
        "--no-hud-friendly": "#8be9fd", "--no-threat-white": "#f8f8f2", "--no-threat-yellow": "#f1fa8c",
        "--no-threat-red": "#ff5555", "--no-jam-yellow-rgb": "#f1fa8c", "--no-route-cyan": "#8be9fd",
        "--no-reached-gray": "#6272a4", "--no-target-orange": "#ffb86c", "--no-hsd-pink-rgb": "#ff79c6",
        "--no-hsd-yellow-rgb": "#f1fa8c"}},
    {"id": "t_elite", "name": "Elite", "colors": {
        "--no-green-rgb": "#ff6600", "--no-white-rgb": "#ffd3a0", "--no-red-rgb": "#ff1f3d",
        "--no-amber-rgb": "#ffcc00", "--no-gray-rgb": "#6b4a2b", "--no-bg": "#0a0603",
        "--no-panel-border": "#3d2205", "--no-ink": "#0a0603", "--no-squad-rgb": "#29b6ff",
        "--no-purple-rgb": "#c06bff", "--no-blue-rgb": "#3fa9ff", "--no-friendly-blue": "#3fa9ff",
        "--no-hud-friendly": "#3fa9ff", "--no-route-cyan": "#4fc3ff", "--no-target-orange": "#ffe14d"}},
    {"id": "t_star", "name": "Star", "colors": {
        "--no-green-rgb": "#35b6ec", "--no-white-rgb": "#e6f4ff", "--no-red-rgb": "#ff4d4d",
        "--no-amber-rgb": "#ffbb00", "--no-gray-rgb": "#4f6f82", "--no-bg": "#0d1a24",
        "--no-panel-border": "#1c4d6e", "--no-ink": "#0d1a24", "--no-squad-rgb": "#9bc3d1",
        "--no-purple-rgb": "#a98bff", "--no-blue-rgb": "#3d7bff", "--no-friendly-blue": "#4f9dff",
        "--no-hud-friendly": "#35b6ec", "--no-route-cyan": "#7fe3ff", "--no-target-orange": "#ff8a3d"}},
]
THEME_STATE = {"active": "default"}
# The plugin's drop-in themes folder (BepInEx/plugins/NOXMFD/themes) stands in as preview/themes here
# (gitignored with the rest of preview/): read on start and on theme.rescan, read-only like DEFAULT.
THEMES_DIR = REPO / "preview" / "themes"
# Option tokens (the SOI line's style and width): token → [(word, served CSS)], from ThemeColors.Options.
THEME_OPTIONS = {tok: [tuple((o + "=" + o).split("=")[:2]) for o in re.findall(r'"([^"]+)"', body)]
                 for tok, body in re.findall(r'\["(--no-[\w-]+)"\]\s*=\s*new\[\]\s*\{([^}]*)\}',
                                             (REPO / "src" / "plugin" / "Stores" / "ThemeColors.cs").read_text(encoding="utf-8"))}


def _theme_norm(token, value):
    """ThemeColors.Normalize: lowercase #rrggbb, or an option token's word; None when invalid."""
    if token not in THEME_TOKENS or not isinstance(value, str):
        return None
    if token in THEME_OPTIONS:
        return next((w for w, _ in THEME_OPTIONS[token] if w == value.lower()), None)
    return value.lower() if re.fullmatch(r"#[0-9a-fA-F]{6}", value) else None


# Theme files key colours by role name (ThemeColors.FileKeys, index for index with Tokens).
THEME_FILE_KEYS = dict(zip(re.findall(r'"([a-z0-9-]+)"', re.search(r'FileKeys\s*=\s*\{(.*?)\};',
                       (REPO / "src" / "plugin" / "Stores" / "ThemeColors.cs").read_text(encoding="utf-8"), re.S).group(1)),
                       THEME_TOKENS))
FILE_THEMES = []


def _scan_theme_folder():
    found = []
    if THEMES_DIR.is_dir():
        for fp in sorted(THEMES_DIR.glob("*.json"), key=lambda f: f.name.lower())[:50]:
            try:
                data = json.loads(fp.read_text(encoding="utf-8"))
            except (OSError, ValueError):
                continue
            if not isinstance(data, dict):
                continue
            colors = {THEME_FILE_KEYS[k.lower()]: _theme_norm(THEME_FILE_KEYS[k.lower()], v)
                      for k, v in (data.get("colors") or {}).items()
                      if k.lower() in THEME_FILE_KEYS and _theme_norm(THEME_FILE_KEYS[k.lower()], v)}
            name = str(data.get("name") or fp.stem).strip()[:32]
            if colors and name:
                found.append({"id": "f_" + fp.name.lower(), "name": name, "colors": colors, "file": True})
    FILE_THEMES[:] = found
    if THEME_STATE["active"] != "default" and not _theme_active():
        THEME_STATE["active"] = "default"


def _theme_active():
    return next((t for t in THEMES + FILE_THEMES if t["id"] == THEME_STATE["active"]), None)


def _theme_css(colors):
    parts = []
    for token in THEME_TOKENS:
        hexv = colors.get(token)
        if not hexv:
            continue
        if token in THEME_OPTIONS:
            val = dict(THEME_OPTIONS[token]).get(hexv, hexv)
        else:
            val = ", ".join(str(int(hexv[i:i + 2], 16)) for i in (1, 3, 5)) if token.endswith("-rgb") else hexv
        parts.append(f"{token}:{val};")
    return ":root{" + "".join(parts) + "}" if parts else ""


def _theme_code(t):
    payload = json.dumps({"n": t["name"], "c": t["colors"]}, separators=(",", ":"))
    return "NOXT1:" + base64.b64encode(payload.encode("utf-8")).decode("ascii")


def _themes_state():
    active = _theme_active()
    return json.dumps({
        "active": active["id"] if active else "default",
        "css": _theme_css(active["colors"]) if active else "",
        "themes": [dict(t, code=_theme_code(t)) for t in THEMES + FILE_THEMES],
    }).encode("utf-8")


def _theme_unique(name, exclude=None):
    taken = {"DEFAULT"} | {t["name"] for t in THEMES + FILE_THEMES if t["id"] != exclude}
    if name not in taken:
        return name
    n = 2
    while f"{name} ({n})" in taken:
        n += 1
    return f"{name} ({n})"


def _theme_add(name, colors):
    if len(THEMES) >= 20:
        return False
    t = {"id": f"t_{uuid.uuid4().hex}", "name": _theme_unique(name[:32]), "colors": colors}
    THEMES.append(t)
    THEME_STATE["active"] = t["id"]
    return True


_scan_theme_folder()


def _theme_command(env):
    cmd, bind = env.get("cmd", ""), env.get("bind", "")
    if cmd == "theme.rescan":
        _scan_theme_folder()
        return True
    active = _theme_active()
    name = (env.get("wname") or "").strip()
    if cmd == "theme.create" and name:
        return _theme_add(name, dict(active["colors"]) if active and bind != "default" else {})
    if cmd == "theme.import":
        try:
            code = (env.get("text") or "").strip()
            data = json.loads(base64.b64decode(code[len("NOXT1:"):]).decode("utf-8")) if code.startswith("NOXT1:") else None
        except ValueError:
            data = None
        if not data or not str(data.get("n", "")).strip():
            return False
        colors = {k: _theme_norm(k, v) for k, v in (data.get("c") or {}).items() if _theme_norm(k, v)}
        return _theme_add(str(data["n"]).strip(), colors)
    if cmd == "theme.select" and (bind == "default" or any(t["id"] == bind for t in THEMES + FILE_THEMES)):
        THEME_STATE["active"] = bind
        return True
    row = next((t for t in THEMES if t["id"] == bind), None)
    if cmd == "theme.rename" and row and name:
        row["name"] = _theme_unique(name[:32], bind)
        return True
    if cmd == "theme.delete" and row:
        THEMES.remove(row)
        if THEME_STATE["active"] == bind:
            THEME_STATE["active"] = "default"
        return True
    token, value = env.get("group") or "", env.get("text") or ""
    if active and active.get("file"):
        active = None   # folder themes are read-only
    if cmd == "theme.set-color" and active and _theme_norm(token, value):
        active["colors"][token] = _theme_norm(token, value)
        return True
    if cmd == "theme.reset-color" and active:
        if token:
            active["colors"].pop(token, None)
        else:
            active["colors"].clear()
        return True
    return False


def _rates_config_merged():
    val = _asset_json("rates-config")
    if val is None:
        return _rates_config()
    merged = {
        "fastHz": 10,
        "contactHz": 4,
        "tgpHz": 15,
        "tgpResolution": "native",
        "tgpJpegQuality": "mid",
        "tgpQuality": "native",
        "tgpSuppressNative": False,
        "mapShowPlayerNames": False
    }
    merged.update(val)
    return json.dumps(merged).encode("utf-8")


# Stateful mock of the plugin's /squad + /server-players + sqd.* commands
# (docs/squadron-transport.md), so the SQD page can be exercised here without Steam or a second
# real player. `ready` is True so the page renders; the real plugin reports False on a non-Steam
# launch and shows the unavailable notice instead. The transport itself is NOT simulated between
# separate processes — there's only one browser here — so an invited mock player "accepts" on a
# short countdown (see _SQD["pending"]) rather than a real accept round-trip, just enough to
# exercise both the "awaiting response" and "joined" UI states. sqd.send is accepted and dropped,
# since there is no real peer here to receive it.
#
# Default state below is this pilot as the squad LEADER of a 4-player squad "TALON" flight 1
# (self + 3 members) — exercises the leader-only UI (callsign section, INVITE roster, DISBAND, MAKE
# LEADER on every member row) without any interaction needed. sqd.create's `index` field carries
# the flight number (issue #42, Squadron Callsign System) the same way the real plugin's
# CommandDispatcher reads it. NOTE this deliberately does NOT match
# _wpt_options()'s own default scenario, which is a squad MEMBER who accepted a route from leader
# "Foxtrot" — a leader can't hold a pending/accepted share from themselves, so once one of these
# two mocks needs to show the leader's own view, "SQD and WPT agree on one persona" can't hold for
# both at once. Foxtrot is kept as an ordinary member here (rather than invented as someone new)
# so the SQD roster still visually connects to WPT's cast, even though the leadership assignment
# itself now differs between the two pages' default states.
_SERVER_PLAYERS = [
    {"id": "76561198000000005", "name": "Widow"},
    {"id": "76561198000000006", "name": "Reaper"},
]
# Aircraft for the match players, shown once one joins the squad (sample data, real captured icons).
_SERVER_PLAYER_AIRCRAFT = {"Widow": "SAH-46 Chicane", "Reaper": "FS-20 Vortex"}
_SQD_SELF = "76561198000000001"
_SQD_SELF_NAME = "Falcon"   # only meaningful while role == leader — see _squad_state's selfName
# Only meaningful while role == leader too (see _squad_state's selfAircraft). All four aircraft
# below now have real captured icons in preview/assets/manifest.json (from actual in-game capture
# sessions — see preview/captures/), pulled in from live captures rather than guessed names.
_SQD_SELF_AIRCRAFT = "EW-25 Medusa"
_SQD = {
    # leaderId/leaderName stay "" while role == leader — Squad.cs never sets them for its own
    # leader (BuildStateJson), only for a MEMBER's view of who leads them.
    "role": "leader", "leaderId": "", "leaderName": "", "callsign": "TALON", "flight": 1,
    "members": [
        {"id": "76561198000000002",   "name": "Foxtrot", "slot": 2, "aircraft": "KR-67 Ifrit"},
        {"id": "76561198000000003",   "name": "Ghost",   "slot": 3, "aircraft": "FS-12 Revoker"},
        {"id": "76561198000000004",   "name": "Havoc",   "slot": 4, "aircraft": "FS-12 Revoker"},
    ],
    "pendingSent": {}, "pendingInvites": [],
    "noticeSeq": 0, "notice": "",
}
_SQD_ACCEPT_POLLS = 2   # how many /squad reads a pending mock invite stays "awaiting response"


def _squad_state():
    # Countdown any pending mock invites toward auto-accept — see the module comment above.
    accepted = []
    for peer, left in list(_SQD["pendingSent"].items()):
        left -= 1
        if left <= 0:
            accepted.append(peer)
            del _SQD["pendingSent"][peer]
        else:
            _SQD["pendingSent"][peer] = left
    for peer in accepted:
        name = next((p["name"] for p in _SERVER_PLAYERS if p["id"] == peer), peer)
        if not any(m["id"] == peer for m in _SQD["members"]):
            # Mirrors Squad.HandleAccept: the lowest free slot from 2 up, so a hole fills first.
            taken = {m["slot"] for m in _SQD["members"]}
            slot = next(n for n in range(2, len(taken) + 3) if n not in taken)
            _SQD["members"].append({"id": peer, "name": name, "slot": slot, "aircraft": _SERVER_PLAYER_AIRCRAFT.get(name, "")})
            _SQD["members"].sort(key=lambda m: m["slot"])

    state = {
        "role": _SQD["role"], "self": _SQD_SELF, "selfName": _SQD_SELF_NAME,
        "selfAircraft": _SQD_SELF_AIRCRAFT if _SQD["role"] == "leader" else "",
        "leaderId": _SQD["leaderId"], "leaderName": _SQD["leaderName"],
        # Never actually exercised — this mock's role never flips to "member" (no simulated
        # incoming invite exists to accept), see the module comment above.
        "leaderAircraft": "",
        "callsign": _SQD["callsign"],
        "flight": _SQD["flight"],
        "members": _SQD["members"],
        "pendingInvites": _SQD["pendingInvites"],
        "pendingSent": [
            {"id": pid, "name": next((p["name"] for p in _SERVER_PLAYERS if p["id"] == pid), pid)}
            for pid in _SQD["pendingSent"]
        ],
        "noticeSeq": _SQD["noticeSeq"], "notice": _SQD["notice"],
    }
    return json.dumps({"ready": True, "state": state}).encode("utf-8")


def _server_players():
    # Anyone already a member or pending isn't offered again — same "don't invite twice" rule
    # sqd.js applies client-side, mirrored here so the mock behaves like the real roster would once
    # a real invited player disappeared from FactionHQ's list.
    taken = {m["id"] for m in _SQD["members"]} | set(_SQD["pendingSent"])
    return json.dumps([p for p in _SERVER_PLAYERS if p["id"] not in taken]).encode("utf-8")


def _squad_command(env):
    cmd = env.get("cmd") or ""
    if not cmd.startswith("sqd."):
        return
    peer = str(env.get("peer") or "").strip()
    if cmd == "sqd.create":
        if _SQD["role"] != "none" or _SQD["pendingInvites"]:
            return
        name = str(env.get("name") or "").strip()[:20]
        try:
            flight = int(env.get("index") or 0)
        except (TypeError, ValueError):
            flight = 0
        if name and 1 <= flight <= 9:
            _SQD["role"] = "leader"
            _SQD["callsign"] = name
            _SQD["flight"] = flight
    elif cmd == "sqd.invite":
        if _SQD["role"] != "leader":
            return
        if peer and peer not in _SQD["pendingSent"] and not any(m["id"] == peer for m in _SQD["members"]):
            _SQD["pendingSent"][peer] = _SQD_ACCEPT_POLLS
    elif cmd == "sqd.set-callsign":
        if _SQD["role"] == "member":
            return
        name = str(env.get("name") or "").strip()[:20]
        try:
            flight = int(env.get("index") or 0)
        except (TypeError, ValueError):
            flight = 0
        if name and 1 <= flight <= 9:
            _SQD["callsign"] = name
            _SQD["flight"] = flight
    elif cmd == "sqd.kick":
        if _SQD["role"] != "leader":
            return
        _SQD["members"] = [m for m in _SQD["members"] if m["id"] != peer]
    elif cmd == "sqd.move-member":
        # Mirrors Squad.MoveMember: into the neighbouring slot one step up (-1) or down (+1),
        # swapping with whoever holds it, within 2..the highest held slot.
        if _SQD["role"] != "leader":
            return
        members = _SQD["members"]
        member = next((m for m in members if m["id"] == peer), None)
        try:
            dir_ = int(env.get("index") or 0)
        except (TypeError, ValueError):
            return
        if member is None or dir_ == 0:
            return
        target = member["slot"] + dir_
        if 2 <= target <= members[-1]["slot"]:
            other = next((m for m in members if m["slot"] == target), None)
            if other:
                other["slot"] = member["slot"]
            member["slot"] = target
            members.sort(key=lambda m: m["slot"])
    elif cmd in ("sqd.leave", "sqd.disband"):
        _SQD["role"] = "none"; _SQD["leaderId"] = ""; _SQD["leaderName"] = ""; _SQD["callsign"] = ""
        _SQD["flight"] = 1; _SQD["members"] = []; _SQD["pendingSent"] = {}
    elif cmd == "sqd.relinquish":
        # From OUR client's point of view, handing off leadership (auto or to a chosen member) ends
        # the same way leaving does: we're no longer in the squad. There's no second real client
        # here to become the new leader.
        _SQD["role"] = "none"; _SQD["leaderId"] = ""; _SQD["leaderName"] = ""; _SQD["callsign"] = ""
        _SQD["flight"] = 1; _SQD["members"] = []; _SQD["pendingSent"] = {}
    elif cmd in ("sqd.accept", "sqd.decline"):
        # Removes just the acted-on invite by leaderId (peer) — mirrors Squad.cs's queue. Normally a
        # no-op here (this mock's role never flips to "member" on its own, see the module comment
        # above), but lets a manually-seeded _SQD["pendingInvites"] scenario be exercised correctly.
        _SQD["pendingInvites"] = [i for i in _SQD["pendingInvites"] if i["leaderId"] != peer]


# Stateful mock of the plugin's /td-state + td.* commands (issue #47, docs/target-designator.md).
# _SQD's default role is "leader" with 3 members (see its own comment), so TD shows the leader's
# matrix. There's no second client to receive a designation, so the mock loops every td.designate
# back to this same browser as a pending one — the TGT page's dock then shows it, and ADD /
# REPLACE / DISMISS can be exercised here too (the leader never receives one in the real plugin).
# With several member lists sent at once, the last one wins, same as a repeat DESIGNATE replacing
# the previous one. ADD/REPLACE only record the ids as accepted: nothing is selected in-game here.
_TD = {"assignments": {}, "sent": {}, "accepted": [], "designated": []}


def _td_state():
    return json.dumps({"ready": True, "state": _TD}).encode("utf-8")


def _td_set(tid, slot, on):
    # Mirrors TdStore.Set: an emptied target drops out entirely.
    key = str(tid)
    slots = [s for s in _TD["assignments"].get(key, []) if s != slot]
    if on:
        slots.append(slot)
    if slots:
        _TD["assignments"][key] = slots
    else:
        _TD["assignments"].pop(key, None)


def _td_has(tid, slot):
    return slot in _TD["assignments"].get(str(tid), [])


def _td_command(env):
    cmd = env.get("cmd") or ""
    if not cmd.startswith("td."):
        return
    try:
        tid = int(env.get("id") or 0)
        slot = int(env.get("index") or 0)
    except (TypeError, ValueError):
        return
    if cmd == "td.cell" and tid and slot > 0:
        _td_set(tid, slot, not _td_has(tid, slot))
    elif cmd == "td.row" and tid:
        # Mirrors TdStore.ToggleRow: every squad slot (leader = 1), all-or-nothing.
        every = [1] + [m["slot"] for m in _SQD["members"]]
        full = all(_td_has(tid, s) for s in every)
        for s in every:
            _td_set(tid, s, not full)
    elif cmd == "td.column" and slot > 0:
        # Mirrors TdStore.ToggleColumn over the page's own id list.
        try:
            ids = [int(i) for i in json.loads(env.get("text") or "[]")]
        except (TypeError, ValueError):
            return
        full = all(_td_has(i, slot) for i in ids)
        for i in ids:
            _td_set(i, slot, not full)
    elif cmd == "td.clear":
        _TD["assignments"] = {}
    elif cmd == "td.designate" and slot > 0:
        try:
            rows = json.loads(env.get("text") or "[]")
        except ValueError:
            return
        _TD["sent"][str(slot)] = sorted(r["id"] for r in rows if isinstance(r, dict) and "id" in r)
        _TD["designated"] = rows
    elif cmd == "td.accept":
        _TD["accepted"] = sorted(set(_TD["accepted"]) | {r["id"] for r in _TD["designated"]})
        _TD["designated"] = []
    elif cmd == "td.dismiss":
        _TD["designated"] = []


# WPT showcase route (issue #38) — a real route drawn by hand in this harness (6 waypoints, a loop
# roughly SE -> N -> W -> back), captured from localStorage so the preview always has something to
# look at instead of an empty "long-press the map" state. Unlike hud/rates/keybinds above, WPT has
# no server-side mock at all — routes are pure client-side localStorage (waypoints-store.js) — so
# this is injected as a <script> that seeds the SAME key the real page reads, not a GET response.
# Seeded only when the key is still empty: a fresh harness always shows the showcase route, but
# once a session edits/adds to it (or clears it), reloading won't fight those real localStorage
# writes back — same "first-visit example, then it's yours" feel a real pilot would get in-game.
WPT_DEMO_ROUTE = {
    "version": 1,
    "activeRouteId": "r_8f68c6aa-f702-4cde-8346-9983657f4ede",
    "routes": [{
        "id": "r_8f68c6aa-f702-4cde-8346-9983657f4ede",
        "name": "RT-8DB0B",
        "nextIndex": 1,   # waypoint 1 already reached, waypoint 2 is NEXT — a more interesting default
        "waypoints": [
            {"id": "w_5ee0d36c-cd0a-4d92-b906-140a785b26be", "name": "", "x": 13299.039341351592, "z": 19932.654304620228},
            {"id": "w_5077c5ef-ed0a-494b-bf35-0fb740061c7a", "name": "", "x": 17110.641554942384, "z": 5775.264886543118},
            {"id": "w_0da486af-baf4-41ea-8532-f146030f93fe", "name": "", "x": 14478.821143745561, "z": -6067.935618675401},
            {"id": "w_edc7b708-3aee-4889-95cc-0dd0ceb0bcf0", "name": "", "x": 1092.8274529657938, "z": -15052.434463700829},
            {"id": "w_9ec17fb2-14b8-460c-b354-274f9eb32937", "name": "", "x": -4896.838732212251, "z": 3597.2052423722635},
            {"id": "w_9625bcb8-827c-42d5-9de2-48288eb57a4c", "name": "", "x": -17965.20005917049, "z": 32293.135249762665},
        ],
    }],
}


def _wpt_seed_script():
    payload = json.dumps(json.dumps(WPT_DEMO_ROUTE)).replace("</", "<\\/")
    return ("<script>\n"
            "try { if (!localStorage.getItem('noxmfd.map.waypoints')) "
            f"localStorage.setItem('noxmfd.map.waypoints', {payload}); }} catch (e) {{}}\n"
            "</script>\n")


# Stateful mock of the plugin's /layout-options + layout.* commands (LayoutStore.cs, issue #51 —
# save/load layout), one example layout per shell to start — same shape as the KEYBINDS mock below
# (a mutable Python list layout.save/rename/delete actually edit, not a static snapshot), so LOAD's
# picker rename/delete round-trips are drivable in the harness, not just swallowed. `data` is a
# JSON-encoded STRING field, matching the plugin's own wire shape (LayoutStore never parses the
# arrangement's shape, so it stores/returns it as opaque text, same as wpt.import's pasted blob) —
# the browser JSON.parses it when a picked layout is applied.
LAYOUTS = [
    {"id": "l_demo_f35", "name": "F-35 demo", "shell": "f35",
     "data": json.dumps({"cells": [{"span": 2, "ate": "right"}, {"span": 1}, {"span": 1}],
                          "pages": ["wpn", "main", "map"]})},
] + [
    # Four CLASSIC layouts: a full view, then three splits (h / v / vwr), so the LYT page shows
    # Layout 1-4 saved with a different thumbnail each and slot 5 empty (LOAD's list shows Layout
    # Preset boxes on the same first five rows, issue #90). Two of them leave a pane out of the SOI
    # rotation (`soi`, one flag per pane) so the gray panes in the thumbnails show too.
    {"id": f"l_demo_classic_{n}", "name": name, "shell": "classic",
     "data": json.dumps({"splitMode": variant is not None, **({"splitVariant": variant} if variant else {}),
                          "pages": pages, **({"soi": soi} if soi else {})})}
    for n, (name, variant, pages, soi) in enumerate([
        ("CLASSIC FULL MAP", None, ["map"], None),
        ("CENTER MAP TGT", "h", ["map", "tgt"], [True, False]),
        ("LEFT RWR AVN", "v", ["rwr", "avn"], None),
        ("RIGHT WPN", "vwr", ["hsd", "wpn"], [False, True]),
    ], start=1)
]


def _layout_options():
    return json.dumps({"layouts": LAYOUTS}).encode("utf-8")


# Unique-name dedup, mirroring LayoutStore.UniqueName (append " (2)", " (3)", ... on collision).
def _unique_layout_name(name, exclude_id):
    taken = {l["name"] for l in LAYOUTS if l["id"] != exclude_id}
    if name not in taken:
        return name
    n = 2
    while f"{name} ({n})" in taken:
        n += 1
    return f"{name} ({n})"


def _layout_command(env):
    cmd, bind = env.get("cmd", ""), env.get("bind", "")
    if cmd == "layout.save":
        name = (env.get("wname") or "").strip()
        data = env.get("text") or ""
        if not name or not data:
            return False
        try:
            json.loads(data)
        except ValueError:
            return False
        LAYOUTS.append({"id": f"l_{uuid.uuid4().hex}", "name": _unique_layout_name(name, None),
                         "shell": env.get("group", ""), "data": data})
        return True
    row = next((l for l in LAYOUTS if l["id"] == bind), None)
    if cmd == "layout.rename" and row is not None:
        name = (env.get("wname") or "").strip()
        if not name:
            return False
        row["name"] = _unique_layout_name(name, bind)
        return True
    if cmd == "layout.update" and row is not None:
        name, data = (env.get("wname") or "").strip(), env.get("text") or ""
        try:
            json.loads(data)
        except ValueError:
            return False
        if not name or not data:
            return False
        row["name"], row["data"] = _unique_layout_name(name, bind), data
        return True
    if cmd == "layout.delete" and row is not None:
        LAYOUTS.remove(row)
        return True
    return False


# Stateful mock of HudPresetStore (issue #50 follow-up) — 5 fixed numbered slots, so the SAVE/LOAD/
# rename/delete round-trip and the bottom "PRESET N: name" label are drivable in the harness, same
# reasoning as LAYOUTS above. Unlike LayoutStore, there's no data blob from the browser to store:
# the real plugin captures categories/vehicles/buildings straight from the live HUDOptions
# singleton, which this harness has no stateful equivalent of (hud.set/hud.mode are unmocked — see
# _hud_options above) — so "save" here just tags the slot as having data, without real filter
# values behind it. The name/current-slot/list machinery this feature actually adds — the part
# testable without the game — is fully exercised regardless.
PRESETS = [{"index": i, "name": "", "hasData": False} for i in range(1, 6)]
PRESET_STATE = {"current": 1}


def _hud_presets_options():
    return json.dumps({"current": PRESET_STATE["current"], "presets": PRESETS}).encode("utf-8")


def _preset_command(env):
    cmd = env.get("cmd", "")
    if cmd == "preset.save":
        name = (env.get("wname") or "").strip()
        if not name:
            return False
        index = env.get("index", 0)
        if index and not 1 <= index <= 5:
            return False
        if index:
            PRESET_STATE["current"] = index
        slot = PRESETS[PRESET_STATE["current"] - 1]
        slot["name"], slot["hasData"] = name, True
        return True
    index = env.get("index", 0)
    slot = PRESETS[index - 1] if 1 <= index <= 5 else None
    if cmd == "preset.rename" and slot is not None:
        name = (env.get("wname") or "").strip()
        if not name:
            return False
        slot["name"] = name
        return True
    if cmd == "preset.delete" and slot is not None:
        slot["name"], slot["hasData"] = "", False
        return True
    if cmd == "preset.load" and slot is not None:
        PRESET_STATE["current"] = index
        return True
    return False


# Stateful mock of TgtPresetStore (issue #78) — same shape as PRESETS/PRESET_STATE above, applied to
# TargetListSelector's filters instead of HUDOptions'. The bottom "PRESET N: name" label on the TGT
# page rides the static `tgt` block in preview-mock.js (a client-side mock, unlike /hud-options),
# which this harness has no way to update from a server-side command — so, like every other tgt.*
# write command here, SAVE/LOAD/rename/delete round-trip against this state and are verifiable via
# the LOAD list they drive, but won't visibly move the page's own preset label; that path is only
# testable in game.
TGT_PRESETS = [{"index": i, "name": "", "hasData": False} for i in range(1, 6)]
TGT_PRESET_STATE = {"current": 1}

# Mock of TargetSort.cs's shared TGT column sort — tgt.sort sets it; preview-mock.js reads it back
# off /__preview-push and re-sorts its own mock target list the same way the plugin would.
TGT_SORT = {"key": "", "dir": 1}


def _tgt_sort_command(env):
    if env.get("cmd") != "tgt.sort" or env.get("key", "") not in ("", "n", "src", "r"):
        return False
    TGT_SORT["key"] = env.get("key", "")
    TGT_SORT["dir"] = -1 if (env.get("index") or 1) < 0 else 1
    return True


def _tgt_presets_options():
    return json.dumps({"current": TGT_PRESET_STATE["current"], "presets": TGT_PRESETS}).encode("utf-8")


def _tgt_preset_command(env):
    cmd = env.get("cmd", "")
    if cmd == "tgt-preset.save":
        name = (env.get("wname") or "").strip()
        if not name:
            return False
        index = env.get("index", 0)
        if index and not 1 <= index <= 5:
            return False
        if index:
            TGT_PRESET_STATE["current"] = index
        slot = TGT_PRESETS[TGT_PRESET_STATE["current"] - 1]
        slot["name"], slot["hasData"] = name, True
        return True
    index = env.get("index", 0)
    slot = TGT_PRESETS[index - 1] if 1 <= index <= 5 else None
    if cmd == "tgt-preset.rename" and slot is not None:
        name = (env.get("wname") or "").strip()
        if not name:
            return False
        slot["name"] = name
        return True
    if cmd == "tgt-preset.delete" and slot is not None:
        slot["name"], slot["hasData"] = "", False
        return True
    if cmd == "tgt-preset.load" and slot is not None:
        TGT_PRESET_STATE["current"] = index
        return True
    return False


# Stateful mock of SoiFocus's per-surface include/exclude set (issue #58) — cid -> set of excluded
# pane indices. There's no ring/cycle simulation in this harness (SOI focus itself is unmocked,
# same pre-existing gap /soi-instances has here), but the LOAD LAYOUT checkboxes' own fetch/set
# round-trip — what this feature actually adds on the wire — is fully exercisable without one.
SOI_EXCLUDED = {}


def _soi_excluded_json(cid):
    return json.dumps({"excluded": sorted(SOI_EXCLUDED.get(cid, set()))}).encode("utf-8")


def _soi_command(env):
    if env.get("cmd") != "soi.include":
        return False
    cid = env.get("cid") or ""
    if not cid:
        return False
    panes = SOI_EXCLUDED.setdefault(cid, set())
    if env.get("on"):
        panes.discard(env.get("n", 0))
    else:
        panes.add(env.get("n", 0))
    return True


# Built from the plugin's real keybind registry (src/plugin/Input/Keybinds.cs) via
# keybinds_source.py, so this preview can never drift from it the way a hand-maintained copy of
# the bind list did. Adding a keybind is still just adding one Def()/DefFree()/DefKeyOnly()/
# AddAxis() call in Keybinds.cs; nothing here needs to change to pick it up.
#
# Stateful mock of the plugin's /keybinds-config + keybind.* commands, so the /keybinds page's
# whole flow (render, keyboard set, joystick arm-capture) is drivable in the harness. Arming a
# joystick capture "captures" a fake button ~1.5s later (simulated on the next poll after the
# deadline — no threads).
keybinds_source.self_check(REPO)
KEYBINDS, _KEYBIND_NOTES = keybinds_source.load_keybinds(REPO)
# Seed already-bound examples so the preview shows what a bound row looks like: keys, joystick
# buttons pinned to a stick ("J1" = stick, "J2" = throttle), chords (stored "LeftShift+S" form), an
# inverted axis, and a mix of unbound rows so the unset state shows too.
# Each button number is used once per stick, as a real setup would have it.
_KEYBIND_SEEDS = {
    # 01 Systems
    "power-on": {"key": "P"}, "power-off": {"key": "LeftShift+P"},
    "radar-on": {"key": "R"}, "radar-off": {"key": "LeftShift+R"},
    "engine-on": {"key": "E", "joyButton": 11, "joyNum": 2},
    "master-arms-on": {"key": "M", "joyButton": 12, "joyNum": 2},
    "gear-up": {"key": "LeftShift+G", "joyButton": 14, "joyNum": 1}, "gear-down": {"key": "G", "joyButton": 15, "joyNum": 1},
    # 02 Combat
    "combat-mode-aa": {"key": "Alpha1", "joyButton": 9, "joyNum": 1}, "combat-mode-ag": {"key": "Alpha2", "joyButton": 10, "joyNum": 1},
    "cycle-guns": {"joyButton": 5, "joyNum": 1}, "cycle-missiles": {"joyButton": 6, "joyNum": 1},
    "gun-trigger": {"joyButton": 1, "joyNum": 1}, "weapon-release": {"key": "Space", "joyButton": 2, "joyNum": 1},
    "weapon-release-single": {"joyButton": 3, "joyNum": 1},
    "flares": {"key": "F", "joyButton": 4, "joyNum": 1}, "jammer": {"key": "J", "joyButton": 3, "joyNum": 2},
    # 03 Sensors
    "tgt-next": {"key": "RightBracket", "joyButton": 7, "joyNum": 2}, "tgt-prev": {"key": "LeftBracket", "joyButton": 8, "joyNum": 2},
    "tgp-manual-toggle": {"key": "T", "joyButton": 9, "joyNum": 2}, "tgp-point-track": {"joyButton": 10, "joyNum": 2},
    "tgp-manual-ir-toggle": {"key": "I"}, "tgp-fullscreen-toggle": {"key": "F9"},
    # 04 Display control
    "soi-next": {"key": "Tab", "joyButton": 5, "joyNum": 2}, "soi-prev": {"key": "LeftShift+Tab", "joyButton": 6, "joyNum": 2},
    "soi-nav-up": {"joyButton": 16, "joyNum": 2}, "soi-nav-down": {"joyButton": 17, "joyNum": 2},
    "soi-select": {"joyButton": 18, "joyNum": 2},
    "cursor-up": {"key": "UpArrow"}, "cursor-down": {"key": "DownArrow"},
    "cursor-left": {"key": "LeftArrow"}, "cursor-right": {"key": "RightArrow"},
    "cursor-select": {"key": "Return", "joyButton": 1, "joyNum": 2},
    "cursor-deselect": {"key": "Backspace", "joyButton": 2, "joyNum": 2},
    "cursor-zoom-in": {"key": "Equals"}, "cursor-zoom-out": {"key": "Minus"},
    "cursor-axis-h": {"axis": 3, "axisNum": 2}, "cursor-axis-v": {"axis": 4, "axisNum": 2, "axisInvert": True},
    # 05 Navigation
    "map-follow": {"key": "L"}, "map-waypoint-next": {"key": "Period"}, "map-waypoint-prev": {"key": "Comma"},
    # 06 MFD setup
    "layout-save": {"key": "LeftShift+S"}, "layout-load": {"key": "LeftShift+L"},
    "layout-preset-1": {"key": "F1"}, "layout-preset-2": {"joyButton": 7, "joyNum": 1},
    "layout-preset-3": {"key": "LeftControl+Alpha3"},
    "hud-preset-1": {"joyButton": 19, "joyNum": 2}, "hud-preset-2": {"joyButton": 20, "joyNum": 2},
    "tgt-preset-1": {"key": "F5"}, "tgt-preset-2": {"key": "F6"},
    "units-toggle": {"key": "U"},
}
for _b in KEYBINDS:
    _b.update(_KEYBIND_SEEDS.get(_b["id"], {}))
KB_STATE = {"conflict": None, "ask": False, "lastPress": {"seq": 0, "joy": 0, "button": 0}, "capturing": None, "capturingKind": None, "armed_at": 0.0, "bgInput": False,
            "rejected": {"seq": 0, "bind": "", "by": ""},
            "radarOnOnStart": True, "engineOnOnStart": True, "masterArmsOnOnStart": True,
            "powerOnOnStart": True, "hudFiltersOnCombatMode": False}


def _keybinds_config():
    # simulate the plugin capturing a stick button/axis 1.5s after arming
    if KB_STATE["capturing"] and time.monotonic() - KB_STATE["armed_at"] > 1.5:
        if KB_STATE["capturing"] == "__search__":   # the KEY page's search-by-press probe
            KB_STATE["lastPress"] = {"seq": KB_STATE["lastPress"]["seq"] + 1, "joy": 2, "button": 11}
        for b in KEYBINDS:
            if b["id"] == KB_STATE["capturing"]:
                if KB_STATE["capturingKind"] == "axis":
                    b["axis"], b["axisNum"] = 3, 1
                elif not _kb_taken(b, lambda o: o.get("joyButton") == 5):
                    # the simulated stick always "presses" J1 B5, which Cycle Guns uses in the seeds
                    others = [o for o in KEYBINDS if o is not b and o.get("joyButton") == 5 and o.get("joyNum") == 1]
                    if KB_STATE["ask"] and others:
                        KB_STATE["conflict"] = {"bind": b["id"], "label": b["label"], "kind": "joy", "key": "",
                                                "joy": 1, "button": 5, "with": [o["label"] for o in others]}
                    else:
                        b["joyButton"], b["joyNum"] = 5, 1
        KB_STATE["capturing"] = None
        KB_STATE["capturingKind"] = None
    return json.dumps({"binds": KEYBINDS, "notes": _KEYBIND_NOTES,
                       "capturing": KB_STATE["capturing"],
                       "capturingKind": KB_STATE["capturingKind"],
                       "rejected": KB_STATE["rejected"],
                       "lastPress": KB_STATE["lastPress"],
                       "conflict": KB_STATE["conflict"],
                       "bgInput": KB_STATE["bgInput"],
                       "radarOnOnStart": KB_STATE["radarOnOnStart"],
                       "engineOnOnStart": KB_STATE["engineOnOnStart"],
                       "masterArmsOnOnStart": KB_STATE["masterArmsOnOnStart"],
                       "powerOnOnStart": KB_STATE["powerOnOnStart"],
                       "hudFiltersOnCombatMode": KB_STATE["hudFiltersOnCombatMode"]}).encode("utf-8")


# Mirrors KeybindConflict.cs: a key/button already used by another bind is refused when a Layout
# Preset slot is on either side, and the refusal is named in the config's `rejected`.
def _kb_taken(row, uses):
    slot = row["id"].startswith("layout-preset-")
    other = next((o for o in KEYBINDS if o is not row
                  and (slot or o["id"].startswith("layout-preset-")) and uses(o)), None)
    if other is None:
        return False
    r = KB_STATE["rejected"]
    KB_STATE["rejected"] = {"seq": r["seq"] + 1, "bind": row["id"], "by": other["label"]}
    return True


def _keybinds_command(env):
    cmd, bind = env.get("cmd", ""), env.get("bind", "")
    row = next((b for b in KEYBINDS if b["id"] == bind), None)
    if cmd == "keybind.set-key" and row is not None:
        key = env.get("key", "")
        if key in ("", "None"):
            row["key"] = ""
        elif not _kb_taken(row, lambda o: o.get("key") == key):
            others = [o for o in KEYBINDS if o is not row and o.get("key") == key]
            if env.get("on") and others:
                KB_STATE["conflict"] = {"bind": row["id"], "label": row["label"], "kind": "key", "key": key,
                                        "joy": 0, "button": 0, "with": [o["label"] for o in others]}
            else:
                row["key"] = key
    elif cmd == "keybind.resolve" and KB_STATE["conflict"]:
        c, KB_STATE["conflict"] = KB_STATE["conflict"], None
        target = next((b for b in KEYBINDS if b["id"] == c["bind"]), None)
        if target is not None and env.get("group") in ("keep", "replace"):
            if env.get("group") == "replace":
                for o in KEYBINDS:
                    if o is target or o["label"] not in c["with"]:
                        continue
                    if c["kind"] == "key":
                        o["key"] = ""
                    else:
                        o["joyButton"], o["joyNum"] = -1, 0
            if c["kind"] == "key":
                target["key"] = c["key"]
            else:
                target["joyButton"], target["joyNum"] = c["button"], c["joy"]
    elif cmd == "keybind.arm-joy" and (row is not None or bind == "__search__"):
        KB_STATE.update(capturing=bind, capturingKind="joy", armed_at=time.monotonic(), ask=bool(env.get("on")))
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
    elif cmd == "keybind.set-radar-on-start":
        KB_STATE["radarOnOnStart"] = bool(env.get("on", False))
    elif cmd == "keybind.set-engine-on-start":
        KB_STATE["engineOnOnStart"] = bool(env.get("on", False))
    elif cmd == "keybind.set-master-arms-on-start":
        KB_STATE["masterArmsOnOnStart"] = bool(env.get("on", False))
    elif cmd == "keybind.set-power-on-start":
        KB_STATE["powerOnOnStart"] = bool(env.get("on", False))
    elif cmd == "keybind.set-hud-filters-on-combat-mode":
        KB_STATE["hudFiltersOnCombatMode"] = bool(env.get("on", False))
    else:
        return False
    return True


class H(http.server.SimpleHTTPRequestHandler):
    def do_POST(self):
        # /command mock: keybind.* and layout.* commands mutate their own mock state; everything
        # else is swallowed with 204, mirroring the plugin's fire-and-forget contract.
        if self.path.split('?', 1)[0] == '/command':
            try:
                n = int(self.headers.get('Content-Length') or 0)
                env = json.loads(self.rfile.read(n) or b'{}')
            except (ValueError, OSError):
                env = {}
            _keybinds_command(env)
            _squad_command(env)
            _td_command(env)
            _layout_command(env)
            _preset_command(env)
            _tgt_preset_command(env)
            _tgt_sort_command(env)
            _soi_command(env)
            _theme_command(env)
            self.send_response(204)
            self.end_headers()
            return
        self.send_error(404)

    # Resolves a manifest key to a captured asset file and serves it; otherwise falls back to
    # `fallback` bytes (served as image/svg+xml, the shape every placeholder here uses) or, with
    # no fallback, a 404 carrying `not_found`. `mime=None` derives
    # the content type from the resolved file's extension (only /map needs this — it can resolve
    # to either a captured .jpg or a dropped-in .png).
    def _serve_captured(self, key, mime=None, fallback=None, not_found=None):
        ref = _asset_ref(key)
        if ref:
            fp = _preview_asset_path(ref)
            if fp and fp.exists():
                return self._file(fp, mime or _mime(str(fp)))
        if fallback is not None:
            return self._send(fallback, 'image/svg+xml')
        return self.send_error(404, not_found)

    def do_GET(self):
        parsed = urllib.parse.urlsplit(self.path)
        path = parsed.path
        if path in ('/', '/index.html'):
            return self._send(_shell_page(WEB / 'shell' / 'classic' / 'mfd.html'), 'text/html; charset=utf-8')
        if path == '/f35':
            return self._send(_shell_page(WEB / 'shell' / 'f35' / 'f35.html'), 'text/html; charset=utf-8')
        if path == '/__reload-token':
            return self._send(str(_reload_token()).encode('utf-8'), 'text/plain; charset=utf-8')
        if path == '/__preview-push':
            return self._send(_preview_push(parsed.query), 'application/json; charset=utf-8')
        if path == '/thrl-demo':
            return self._file(REPO / 'tools' / 'thrl-demo.html', 'text/html; charset=utf-8')
        if path == '/config':
            return self._send(_config(self.server.server_address[1]), 'application/json; charset=utf-8')
        if path == '/hud-options':
            return self._send(_captured_or('hud-options', _hud_options), 'application/json; charset=utf-8')
        if path == '/wpt-options':
            return self._send(_captured_or('wpt-options', _wpt_options), 'application/json; charset=utf-8')
        if path == '/layout-options':
            return self._send(_captured_or('layout-options', _layout_options), 'application/json; charset=utf-8')
        if path == '/hud-presets':
            return self._send(_hud_presets_options(), 'application/json; charset=utf-8')
        if path == '/tgt-presets':
            return self._send(_tgt_presets_options(), 'application/json; charset=utf-8')
        if path == '/soi-excluded':
            cid = urllib.parse.parse_qs(parsed.query).get('cid', [''])[0]
            return self._send(_soi_excluded_json(cid), 'application/json; charset=utf-8')
        if path == '/keybinds-config':
            return self._send(_keybinds_config(), 'application/json; charset=utf-8')
        if path == '/rates-config':
            return self._send(_rates_config_merged(), 'application/json; charset=utf-8')
        if path == '/themes':
            return self._send(_themes_state(), 'application/json; charset=utf-8')
        if path == '/colors-override.css':
            active = _theme_active()
            css = _theme_css(active["colors"]) if active else ""
            return self._send(css.encode('utf-8'), 'text/css; charset=utf-8', {'Cache-Control': 'no-store'})
        if path == '/squad':
            return self._send(_squad_state(), 'application/json; charset=utf-8')
        if path == '/td-state':
            return self._send(_td_state(), 'application/json; charset=utf-8')
        if path == '/server-players':
            return self._send(_server_players(), 'application/json; charset=utf-8')
        if path == '/map-view':
            try:
                return self._send(_map_page('telemetry=1' in self.path), 'text/html; charset=utf-8')
            except OSError as e:
                return self.send_error(404, str(e))
        if path == '/tgt':
            return self._send(_tgt_page(), 'text/html; charset=utf-8')
        if path == '/wpt':
            try:
                return self._send(_wpt_page(), 'text/html; charset=utf-8')
            except OSError as e:
                return self.send_error(404, str(e))
        if path in ('/map', '/map.png', '/map.jpg'):
            return self._serve_captured('map', not_found='no captured map')
        if path == '/icon':
            typ = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('type', [''])[0]
            return self._serve_captured('icon:' + typ, mime='image/png', not_found='no captured icon')
        if path == '/weapon':
            name = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('name', [''])[0]
            return self._serve_captured('weapon:' + name, mime='image/png', fallback=WEAPON_SVG.encode('utf-8'))
        if path in ('/tgt-icon', '/building-icon'):
            # Real captured type sprite if a capture ran (manifest key 'tgt-icon:<t>' /
            # 'building-icon:<t>'); otherwise the generic placeholder, so both the vehicle and
            # building chips show *an* icon in the mock harness.
            typ = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('type', [''])[0]
            return self._serve_captured(path.lstrip('/') + ':' + typ, mime='image/png', fallback=TGT_ICON_SVG.encode('utf-8'))
        if path == '/hud-cat-icon':
            # Real captured category-row glyph if a capture ran (manifest key
            # 'hud-cat-icon:<CAT>'); otherwise the same generic placeholder as the vehicle/building
            # chips above, so the HUD page's category rows show *an* icon in the mock harness.
            cat = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('cat', [''])[0]
            return self._serve_captured('hud-cat-icon:' + cat, mime='image/png', fallback=TGT_ICON_SVG.encode('utf-8'))
        if path == '/bdf-icon':
            # Real captured ship-type icon if a capture ran (manifest key 'bdf-icon:<t>');
            # otherwise the generic diamond placeholder. Faction logos have no HTTP endpoint at
            # all (Faction.factionColorLogo is in-game-only) so there's nothing to capture for
            # those — BDF_ICON_SVG still stands in for the header logo either way.
            typ = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('type', [''])[0]
            return self._serve_captured('bdf-icon:' + typ, mime='image/png', fallback=BDF_ICON_SVG.encode('utf-8'))
        if path == '/doc-list':
            return self._send(json.dumps(DOC_MOCK_FILES).encode('utf-8'), 'application/json; charset=utf-8')
        if path == '/doc-image':
            name = urllib.parse.parse_qs(parsed.query).get('name', [''])[0]
            if name not in DOC_MOCK_FILES:
                return self.send_error(404, 'no such doc image')
            safe = name.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
            svg = DOC_IMAGE_SVG_TEMPLATE.format(name=safe).encode('utf-8')
            return self._send(svg, 'image/svg+xml')
        if path == '/tgp.mjpg':
            # A captured still frame, served as a plain JPEG — an <img> tag can't tell the
            # difference from a live multipart stream, it just won't update. No capture yet:
            # the same placeholder shape as WEAPON_SVG/TGT_ICON_SVG.
            return self._serve_captured('tgp', mime='image/jpeg', fallback=TGP_SVG.encode('utf-8'))
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
            return self._serve_captured('airframe:' + typ + '|' + part, mime='image/png', not_found='no captured airframe part')
        if path.startswith('/assets/'):
            rel = posixpath.normpath(path[len('/assets/'):]).lstrip('/\\')
            web_fp = WEB.joinpath(*rel.split('/'))
            if web_fp.exists():
                return self._file(web_fp, _mime(rel), cache=True)
            return self._file(PREV.joinpath('assets', *rel.split('/')), _mime(rel))
        # Any page: /<name> -> src/web/pages/<name>/<name>.html (wpn, tgt, ...).
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


# Threaded: the shell opens several connections at once (shell + map/page iframes + assets). A
# single-threaded server serialises them and stalls if any one handler blocks; ThreadingTCPServer
# keeps every reload responsive. daemon_threads so Ctrl+C (or a script that never calls shutdown())
# exits without waiting on open sockets. Module-level (not main()-local) so capture_screenshots.py
# can import this module and build its own instance without going through main()/argparse at all.
class Server(socketserver.ThreadingTCPServer):
    daemon_threads = True
    # On Windows SO_REUSEADDR lets a SECOND instance bind the same port while the first is
    # alive — stale servers then keep answering with old code. Windows doesn't need the flag
    # to rebind after a normal exit, so only use it on POSIX (where it just skips TIME_WAIT).
    allow_reuse_address = os.name != "nt"

    def handle_error(self, request, client_address):
        # A real browser (capture_screenshots.py's Playwright driver, or just navigating away
        # mid-load) routinely aborts in-flight requests — a 404 probe it no longer needs, a
        # cancelled prefetch. That surfaces here as ConnectionAbortedError/ConnectionResetError on
        # whichever thread was mid-write; harmless, not a bug, but the default handler prints a
        # full traceback per occurrence and buries anything that's an actual problem. Only an
        # unexpected exception type still gets the traceback.
        exc = sys.exc_info()[1]
        if not isinstance(exc, (ConnectionAbortedError, ConnectionResetError, BrokenPipeError)):
            super().handle_error(request, client_address)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--port", type=int, default=int(os.environ.get("PORT", 8782)),
                    help="port to bind (default $PORT or 8782)")
    ap.add_argument("--host", default="127.0.0.1",
                    help="address to bind (default 127.0.0.1; 0.0.0.0 to serve other LAN devices)")
    ap.add_argument("--open", action="store_true", help="open the shell in a browser on start")
    ap.add_argument("--capture", default=None,
                    help="serve this one preview/captures/<name>/ folder instead of following CURRENT")
    args = ap.parse_args()
    if not (WEB / "shell" / "classic" / "mfd.html").exists():
        raise SystemExit("ERROR: src/web/shell/classic/mfd.html missing.")
    global CAPTURE_OVERRIDE
    CAPTURE_OVERRIDE = args.capture
    with Server((args.host, args.port), H) as s:
        url = f"http://127.0.0.1:{args.port}/"
        print(f"serving on {url}")
        lan_ip = _detect_lan_ip() if args.host != "127.0.0.1" else ""
        if lan_ip:
            print(f"LAN: http://{lan_ip}:{args.port}/")
        if args.open:
            webbrowser.open(url)
        try:
            s.serve_forever()
        except KeyboardInterrupt:
            print("\nStopped.")


if __name__ == "__main__":
    main()
