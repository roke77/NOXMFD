#!/usr/bin/env python3
"""Shell harness over HTTP — verify migrated src/web/ pages in a browser without the game.

Serves the real src/web/ MFD shell plus the migrated web assets, so the UI can be driven
end-to-end without extracting C# blobs. The MAP iframe receives tools/preview-mock.js,
which supplies the synthetic/captured /stream data that the shell forwards to page iframes.

  /                  -> src/web/shell/classic/mfd.html
  /f35               -> src/web/shell/f35/f35.html      (the F-35 layout — see docs/layouts.md)
  /thrl-demo         -> tools/thrl-demo.html    (standalone THRL slider demo, no shell/mock needed)
  /config            -> preview runtime URLs        (localhost/LAN URL for this harness port)
  /map-view[?bare]   -> src/web/pages/map/map.html      (the base map iframe; mock injected here)
  /wpt               -> src/web/pages/wpt/wpt.html   (showcase route seeded into localStorage)
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
    injection = _capture_injection() + mock + "\n" + _wpt_seed_script()
    return html.replace("</head>", injection + "</head>", 1).encode("utf-8")


def _wpt_page():
    """The WPT page (src/web/pages/wpt/wpt.html) with the showcase route seeded before </head> —
    unlike map.html above it has no /stream mock to inject, just the localStorage seed, since WPT
    can be opened standalone without MAP ever loading (the seed can't rely on map.html having run
    first)."""
    html = (WEB / "pages" / "wpt" / "wpt.html").read_text(encoding="utf-8")
    return html.replace("</head>", _wpt_seed_script() + "</head>", 1).encode("utf-8")


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


# Mock of the plugin's /rates-config (cfg-rates experiment, issue #39), so the /rates page's two
# sliders have something to initialize from in the harness. The write side (rates.set) has no
# mock — commands POST and are swallowed — so moving a slider here won't change this response;
# that path is only testable in game.
def _rates_config():
    return json.dumps({"fastHz": 10, "tgpHz": 15}).encode("utf-8")


# Stateful mock of the plugin's /squadron + squadron.* commands (docs/squadron-transport.md), so the
# WPT page's squadron block can be exercised here without Steam or a second player. `ready` is True
# so the section renders; the real plugin reports False on a non-Steam launch and hides it. The
# transport itself is NOT mocked — squadron.send is accepted and dropped, since there is no peer
# here to receive it.
_SQUADRON = {"ready": True, "self": "76561198000000001", "peers": []}


def _squadron_state():
    return json.dumps(_SQUADRON).encode("utf-8")


def _squadron_command(env):
    cmd = env.get("cmd") or ""
    if not cmd.startswith("squadron."):
        return
    peer = str(env.get("peer") or "").strip()
    if cmd == "squadron.add":
        # Same digits-only shape the plugin's TryPeer enforces, so the harness rejects what the real
        # build would reject rather than accepting ids that only work here.
        if peer.isdigit() and peer != _SQUADRON["self"] and peer not in _SQUADRON["peers"]:
            _SQUADRON["peers"].append(peer)
    elif cmd == "squadron.remove":
        if peer in _SQUADRON["peers"]:
            _SQUADRON["peers"].remove(peer)
    elif cmd == "squadron.clear":
        _SQUADRON["peers"].clear()


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
    {"id": "map-zoom-in", "section": "MAP", "label": "Zoom In",
     "description": "Zoom in on the focused MAP display.",
     "key": "", "joyButton": -1, "joyNum": 0},
    {"id": "map-zoom-out", "section": "MAP", "label": "Zoom Out",
     "description": "Zoom out on the focused MAP display.",
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
            "radarOnOnStart": True, "engineOnOnStart": True, "masterArmsOnOnStart": True}


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
             "WEAPONS": "Cycle keys select the last soft-selected weapon of their type, or the first "
                        "in the list. Repeated presses cycle to the next one, skipping depleted "
                        "weapons. Cycling to a different type leaves the current one soft-selected.",
             "IMMERSION OPTIONS": "A/A and A/G each restrict Cycle Missile on a tap; hold either one "
                        "to reset to ALL (unrestricted). Every other bind here is a plain dedicated "
                        "action."}
    return json.dumps({"binds": KEYBINDS, "notes": notes,
                       "capturing": KB_STATE["capturing"],
                       "capturingKind": KB_STATE["capturingKind"],
                       "bgInput": KB_STATE["bgInput"],
                       "radarOnOnStart": KB_STATE["radarOnOnStart"],
                       "engineOnOnStart": KB_STATE["engineOnOnStart"],
                       "masterArmsOnOnStart": KB_STATE["masterArmsOnOnStart"]}).encode("utf-8")


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
            _squadron_command(env)
            self.send_response(204)
            self.end_headers()
            return
        self.send_error(404)

    def do_GET(self):
        path = self.path.split('?', 1)[0]
        if path in ('/', '/index.html'):
            return self._file(WEB / 'shell' / 'classic' / 'mfd.html', 'text/html; charset=utf-8', cache=True)
        if path == '/f35':
            return self._file(WEB / 'shell' / 'f35' / 'f35.html', 'text/html; charset=utf-8', cache=True)
        if path == '/thrl-demo':
            return self._file(REPO / 'tools' / 'thrl-demo.html', 'text/html; charset=utf-8')
        if path == '/config':
            return self._send(_config(self.server.server_address[1]), 'application/json; charset=utf-8')
        if path == '/hud-options':
            return self._send(_hud_options(), 'application/json; charset=utf-8')
        if path == '/keybinds-config':
            return self._send(_keybinds_config(), 'application/json; charset=utf-8')
        if path == '/rates-config':
            return self._send(_rates_config(), 'application/json; charset=utf-8')
        if path == '/squadron':
            return self._send(_squadron_state(), 'application/json; charset=utf-8')
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
    if not (WEB / "shell" / "classic" / "mfd.html").exists():
        raise SystemExit("ERROR: src/web/shell/classic/mfd.html missing.")
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
