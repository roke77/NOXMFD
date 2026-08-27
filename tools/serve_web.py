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
    python tools/serve_web.py --open

Run tools/capture_assets.py while in-game to populate preview/captures/<timestamp>/ with real
assets — every run adds a new dated folder (a library, not one slot), and preview/captures/CURRENT
names whichever one is live here. To switch which capture is live, just overwrite that file with a
different folder's name; no server restart needed, it's read fresh on every request. Falls back to
the older single-slot preview/assets/manifest.json if neither CURRENT nor its target exist.
Ctrl+C to stop.
"""
import argparse
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


def _map_page():
    """The MAP page (src/web/pages/map/map.html) with the mock (+ any capture) injected before
    </head>, so its EventSource('/stream') and /map,/icon,/weapon fetches resolve in the browser.
    Built fresh per request so edits to map.html / the mock show up on reload."""
    html = (WEB / "pages" / "map" / "map.html").read_text(encoding="utf-8")
    mock = MOCK.read_text(encoding="utf-8").strip()
    injection = _capture_injection() + mock + "\n" + _wpt_seed_script()
    return html.replace("</head>", injection + "</head>", 1).encode("utf-8")


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
        "declutter": {"weapon": False, "minimap": True, "boxes": False},
        # Current HUD preset (issue #50 follow-up) — index/name only, stateful via PRESET_STATE/
        # PRESETS below so the bottom label follows a save/rename/load in the harness.
        "preset": {"index": current["index"], "name": current["name"]},
    }).encode("utf-8")


# Mock of the plugin's /wpt-options (RouteStore.RoutesJson, docs/hud-waypoint-indicator.md) — a
# real snapshot pulled from a local mod session's BepInEx/config/com.roque.NOXMFD.routes.json, so
# MAP/WPT have real routes to render in the harness instead of "NO ROUTES YET". Static, same as
# _hud_options — the write side (wpt.* commands) has no mock, POSTs are swallowed, so editing here
# won't change this response; that path is only testable in game.
def _wpt_options():
    return json.dumps({
        "activeRouteId": "r_1dd9973ab97c415e8256e829921a2320",
        "routes": [
            {
                "id": "r_6422161b6ecd4e0e8bc0a99c627ef397", "name": "RT-8F6A9", "nextIndex": 4,
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
                "waypoints": [
                    {"id": "w_5eb7026834e0487982503ddf63a8ac07", "name": "", "x": 7113.0,  "z": 8578.7},
                    {"id": "w_5d989cdca24146deadf7d72527295197", "name": "", "x": 21153.2, "z": 7571.6},
                    {"id": "w_23d2e0ac003e4bb2ab8758283d7be41d", "name": "", "x": 27480.7, "z": 18649.8},
                    {"id": "w_befad7e5757848928451f81123c0a6cf", "name": "", "x": 19925.4, "z": 29130.0},
                    {"id": "w_9242d06d1e20411eac306da4b9a0078e", "name": "", "x": 6829.6,  "z": 21828.5},
                ],
            },
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
        "tgpSuppressNative": False
    }).encode("utf-8")


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
        "tgpSuppressNative": False
    }
    merged.update(val)
    return json.dumps(merged).encode("utf-8")


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
    {"id": "l_demo_classic", "name": "Classic split demo", "shell": "classic",
     "data": json.dumps({"splitMode": True, "splitVariant": "h", "pages": ["rwr", "main"],
                          "pinnedPage": "rwr"})},
    {"id": "l_demo_f35", "name": "F-35 demo", "shell": "f35",
     "data": json.dumps({"cells": [{"span": 2, "ate": "right"}, {"span": 1}, {"span": 1}],
                          "pages": ["wpn", "main", "map"]})},
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
    {"id": "jammer-pod", "section": "COUNTERMEASURES", "label": "Jamming Pod",
     "description": "Select + activate a weapon-mounted radar jamming pod. HOLD to keep jamming. With another weapon selected, the first press only switches to it — press again to activate.",
     "key": "", "joyButton": -1, "joyNum": 0},
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
    {"id": "map-route-next", "section": "MAP", "label": "Next Route",
     "description": "Switch the focused MAP display's active waypoint route to the next one (R+).",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "map-route-prev", "section": "MAP", "label": "Previous Route",
     "description": "Switch the focused MAP display's active waypoint route to the previous one (R-).",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "map-waypoint-next", "section": "MAP", "label": "Next Waypoint",
     "description": "Manually step the focused MAP display's active route to the next waypoint (W+).",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "map-waypoint-prev", "section": "MAP", "label": "Previous Waypoint",
     "description": "Manually step the focused MAP display's active route to the previous waypoint (W-).",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "tgt-next", "section": "TGT", "label": "Next Target",
     "description": "Highlight the next locked target on the focused TGT display.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "tgt-prev", "section": "TGT", "label": "Previous Target",
     "description": "Highlight the previous locked target on the focused TGT display.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "tgt-datalink", "section": "TGT", "label": "Clear Datalink",
     "description": "Deselect the datalink-only locks on the focused TGT display — same as tapping its DATALINK button.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "tgt-stale", "section": "TGT", "label": "Clear Stale",
     "description": "Deselect the stale locks on the focused TGT display — same as tapping its STALE button.",
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
    {"id": "soi-select", "section": "SOI", "label": "Nav Select",
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
    # PAD Cursor zoom (docs/tgp-manual-control.md's PAD Cursor consolidation plan) — one bind pair
    # drives both the manual TGP camera's zoom (while it holds SOI) and every other display's
    # MAP-style zoom.
    {"id": "cursor-zoom-in", "section": "CURSOR", "label": "Cursor Zoom In",
     "description": "Zoom in the manual TGP camera while it holds SOI. Otherwise, zooms in on the "
                     "focused MAP display — on a scrollable page, scrolls it up instead.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "cursor-zoom-out", "section": "CURSOR", "label": "Cursor Zoom Out",
     "description": "Zoom out the manual TGP camera while it holds SOI. Otherwise, zooms out on "
                     "the focused MAP display — on a scrollable page, scrolls it down instead.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "cursor-zoom-axis", "section": "CURSOR", "label": "Cursor Zoom Axis",
     "description": "Calibrated analog axis (e.g. a HOTAS slider) — moving the axis jumps the "
                     "manual TGP camera's zoom to that absolute position, min to max. Cursor Zoom "
                     "In/Out still work while the axis is stationary. Only acts while the manual "
                     "TGP camera holds SOI.",
     "axis": -1, "axisNum": 0, "axisInvert": False},
    # TGP manual control keybinds (docs/tgp-manual-control.md) — lifecycle only; pan/tilt/zoom live
    # on the CURSOR block above instead (the PAD Cursor consolidation plan).
    {"id": "tgp-manual-toggle", "section": "TGP", "label": "Manual Control Toggle",
     "description": "Toggle manual TGP pointing on/off. Centers on the aircraft's nose at "
                     "minimum zoom on entry. Auto-exits on a real target lock, aircraft loss, "
                     "or a landing-gear/cam conflict.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "tgp-manual-reset", "section": "TGP", "label": "Manual Control Reset",
     "description": "Recenter the TGP manual camera on the aircraft's forward direction at "
                     "minimum zoom.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "tgp-point-track", "section": "TGP", "label": "Point Track",
     "description": "Lock the TGP manual camera onto whatever it's currently pointed at — it "
                     "holds that world point steady as the aircraft moves, instead of a fixed "
                     "direction. Press again to release; Pan/Tilt nudges the point and "
                     "redesignates on release. Only acts while TGP manual control is on.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "tgp-manual-ir-toggle", "section": "TGP", "label": "Toggle IR",
     "description": "Switch the active TGP camera between COLOR and IR — the manual camera, or a "
                     "real unit lock. The game normally switches this automatically by time of "
                     "day/distance/the \"always IR\" setting; this bind overrides that with your "
                     "own choice, which sticks until you flip it again.",
     "key": "", "joyButton": -1, "joyNum": 0},
    # Layout keybinds (issue #51 follow-up) — SAVE/LOAD LAYOUT. Key-only: no joyButton/joyNum
    # fields at all (mirrors the axis-only rows omitting key/joyButton) — browser-side only,
    # deliberately no joystick/HOTAS, so the page renders one wide key cell for these two.
    {"id": "layout-save", "section": "LAYOUT", "label": "Save Layout",
     "description": "Save the current screen layout under a name.",
     "key": ""},
    {"id": "layout-load", "section": "LAYOUT", "label": "Load Layout",
     "description": "Load a previously saved screen layout.",
     "key": ""},
    # HUD preset keybinds (issue #50 follow-up) — unlike layout-save/load above, these ARE real
    # binds (joyButton/joyNum present): pressing one directly recalls that numbered preset.
    *[{"id": f"hud-preset-{n}", "section": "HUD PRESETS", "label": f"HUD Preset {n}",
       "description": f"Load HUD preset {n}'s saved filters onto the HUD page.",
       "key": "", "joyButton": -1, "joyNum": 0} for n in range(1, 6)],
    # Immersion keybinds (docs/radar-master-arms.md, issue #32) — deliberately last, so the
    # "Immersion options" block (this section + the three settings below) reads as one group at
    # the bottom of the page.
    {"id": "master-arms-on", "section": "IMMERSION OPTIONS", "label": "Master Arms ON",
     "description": "Arm — weapons/countermeasures free to fire.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "master-arms-off", "section": "IMMERSION OPTIONS", "label": "Master Arms OFF",
     "description": "Disarm — weapons/countermeasures blocked.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "radar-on", "section": "IMMERSION OPTIONS", "label": "Radar ON",
     "description": "Turn the radar on.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "radar-off", "section": "IMMERSION OPTIONS", "label": "Radar OFF",
     "description": "Turn the radar off.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "engine-on", "section": "IMMERSION OPTIONS", "label": "Engine ON",
     "description": "Turn the engine on.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "engine-off", "section": "IMMERSION OPTIONS", "label": "Engine OFF",
     "description": "Turn the engine off.", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "combat-mode-aa", "section": "IMMERSION OPTIONS", "label": "A/A",
     "description": "Tap to restrict Cycle Missile to air-to-air missiles only, and disable Cycle "
                     "Bombs. Hold to reset to ALL (unrestricted).", "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "combat-mode-ag", "section": "IMMERSION OPTIONS", "label": "A/G",
     "description": "Tap to restrict Cycle Missile to air-to-ground missiles only. Hold to reset to "
                     "ALL (unrestricted).", "key": "", "joyButton": -1, "joyNum": 0},
]
KB_STATE = {"capturing": None, "capturingKind": None, "armed_at": 0.0, "bgInput": False,
            "radarOnOnStart": True, "engineOnOnStart": True, "masterArmsOnOnStart": True,
            "hudFiltersOnCombatMode": False}


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
    notes = {"MAP": "Follow / Zoom In / Zoom Out / Next & Previous Route / Next & Previous Waypoint "
                    "are direct binds for what the bezel's FLW, Z+/Z-, R+/R- and W+/W- keys already "
                    "do on the focused MAP display.",
             "TGT": "Next/Previous highlight a row instead of moving the crosshair — moving Cursor "
                    "Up/Down/Left/Right (or its axis) clears the highlight and hands Cursor Select "
                    "back to the crosshair. While a row is highlighted, Cursor Select deselects it. "
                    "Datalink/Stale mirror the DATALINK/STALE buttons.",
             "SOI": "One display at a time is the sensor of interest — it rings itself in white, and "
                    "these keys drive it. Nothing is focused until you press SOI Next or Prev; from "
                    "there they cycle through the open displays.",
             "CURSOR": "Moves a cursor over whichever focused display has one (MAP, for now) and "
                       "selects what it's on. Cursor Horizontal/Vertical are the same movement as an "
                       "analog HOTAS axis — bind either or both; a deflected axis overrides its two keys.",
             "TGP": "Manual pointing of the targeting-pod camera, independent of the "
                       "game's own auto-lock. Pan/Tilt Axis are the same movement as an analog HOTAS "
                       "axis — bind either or both; a deflected axis overrides its two keys. Zoom Axis "
                       "is different: moving a calibrated slider jumps zoom to that absolute level, "
                       "while Zoom In/Out still work between axis moves. Point Track locks the camera "
                       "onto whatever it's aimed at; Pan/Tilt nudges and redesignates on release. Off by default; "
                       "toggling on centers at minimum zoom, and auto-exits the moment a real target "
                       "locks, the aircraft is lost, or gear/landing cam takes over.",
             "WEAPONS": "Cycle keys select the last soft-selected weapon of their type, or the first "
                        "in the list. Repeated presses cycle to the next one, skipping depleted "
                        "weapons. Cycling to a different type leaves the current one soft-selected.",
             "LAYOUT": "Keyboard only, no joystick/HOTAS. Acts on whichever browser window has focus "
                       "when pressed, and applies to every connected browser.",
             "IMMERSION OPTIONS": "A/A and A/G each restrict Cycle Missile on a tap; hold either one "
                        "to reset to ALL (unrestricted). Every other bind here is a plain dedicated "
                        "action."}
    return json.dumps({"binds": KEYBINDS, "notes": notes,
                       "capturing": KB_STATE["capturing"],
                       "capturingKind": KB_STATE["capturingKind"],
                       "bgInput": KB_STATE["bgInput"],
                       "radarOnOnStart": KB_STATE["radarOnOnStart"],
                       "engineOnOnStart": KB_STATE["engineOnOnStart"],
                       "masterArmsOnOnStart": KB_STATE["masterArmsOnOnStart"],
                       "hudFiltersOnCombatMode": KB_STATE["hudFiltersOnCombatMode"]}).encode("utf-8")


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
    elif cmd == "keybind.set-radar-on-start":
        KB_STATE["radarOnOnStart"] = bool(env.get("on", False))
    elif cmd == "keybind.set-engine-on-start":
        KB_STATE["engineOnOnStart"] = bool(env.get("on", False))
    elif cmd == "keybind.set-master-arms-on-start":
        KB_STATE["masterArmsOnOnStart"] = bool(env.get("on", False))
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
            _layout_command(env)
            _preset_command(env)
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
        path = self.path.split('?', 1)[0]
        if path in ('/', '/index.html'):
            return self._send(_shell_page(WEB / 'shell' / 'classic' / 'mfd.html'), 'text/html; charset=utf-8')
        if path == '/f35':
            return self._send(_shell_page(WEB / 'shell' / 'f35' / 'f35.html'), 'text/html; charset=utf-8')
        if path == '/__reload-token':
            return self._send(str(_reload_token()).encode('utf-8'), 'text/plain; charset=utf-8')
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
        if path == '/keybinds-config':
            return self._send(_keybinds_config(), 'application/json; charset=utf-8')
        if path == '/rates-config':
            return self._send(_rates_config_merged(), 'application/json; charset=utf-8')
        if path == '/map-view':
            try:
                return self._send(_map_page(), 'text/html; charset=utf-8')
            except OSError as e:
                return self.send_error(404, str(e))
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
    ap.add_argument("--open", action="store_true", help="open the shell in a browser on start")
    ap.add_argument("--capture", default=None,
                    help="serve this one preview/captures/<name>/ folder instead of following CURRENT")
    args = ap.parse_args()
    if not (WEB / "shell" / "classic" / "mfd.html").exists():
        raise SystemExit("ERROR: src/web/shell/classic/mfd.html missing.")
    global CAPTURE_OVERRIDE
    CAPTURE_OVERRIDE = args.capture
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
