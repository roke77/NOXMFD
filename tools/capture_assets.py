#!/usr/bin/env python3
"""Capture the real game-served assets from a running session, into their own
timestamped folder — NOT the live preview/assets/ the mocked pages actually
serve from. This never overwrites the current mock scenario; review what got
captured (preview/captures/<timestamp>/manifest.json + the PNGs alongside it)
and copy over only the specific entries you want, one at a time.

Run this WHILE Nuclear Option is running with a mission loaded and you are flying
(so /stream serves real telemetry and the map/icons have been extracted by the
mod). It pulls, from http://localhost:5005:

  • one real telemetry frame  (/stream)         → the static scenario
                                                   (includes AKF's kill feed/rank/funds, live —
                                                   score a few kills first if you want it populated)
  • the map image             (/map)            → map.png
  • each unit type's icon      (/icon?type=...)  → icon_*.png
                                                   (from your own aircraft, current HUD/radar
                                                   contacts, AND every type the plugin has actually
                                                   captured this session per /icon-types — that last
                                                   one is what picks up squadmates' aircraft, which
                                                   are never "contacts" but the plugin already has
                                                   their icon from just being spawned in the mission)
  • each weapon's icon         (/weapon?name=..) → weapon_*.png
  • HUD vehicle/building icons (/tgt-icon, /building-icon) → tgt_icon_*.png, building_icon_*.png
  • HUD category-row glyphs   (/hud-cat-icon)              → hud_cat_icon_*.png

...and writes manifest.json describing them, all under one new
preview/captures/<timestamp>/ folder. To bring a capture into the live preview,
tell Claude which entries you want — it'll copy just those PNGs into
preview/assets/ and merge their keys into preview/assets/manifest.json.
"""
import datetime
import json
import pathlib
import sys
import urllib.error
import urllib.parse
import urllib.request

BASE = "http://localhost:5005"
ROOT = pathlib.Path(__file__).resolve().parent.parent
CAPTURES = ROOT / "preview" / "captures"   # each run gets its own subfolder here — preview/assets/
                                            # (the LIVE mocked data serve_web.py actually reads) is
                                            # never touched by this script anymore.
PING_LIMIT = 30   # give up after this many "no mission" pings


def grab_frame():
    """Read /stream until a real (non-ping) telemetry frame arrives."""
    url = BASE + "/stream"
    try:
        resp = urllib.request.urlopen(url, timeout=15)
    except urllib.error.URLError as e:
        sys.exit(f"ERROR: can't reach {url}\n  Is the game running with the mod? ({e})")

    pings = 0
    for raw in resp:
        line = raw.decode("utf-8", "replace").strip()
        if not line.startswith("data:"):
            continue
        try:
            d = json.loads(line[5:].strip())
        except json.JSONDecodeError:
            continue
        if d.get("ping"):
            pings += 1
            print(f"  ...connected, waiting for a mission (ping {pings}/{PING_LIMIT})")
            if pings >= PING_LIMIT:
                resp.close()
                sys.exit("ERROR: connected but no mission loaded. Load a mission and fly, then retry.")
            continue
        if "world" in d:
            resp.close()
            return d
    sys.exit("ERROR: stream ended before a real frame arrived")


def fetch(path):
    """GET a binary asset; return bytes or None on 404."""
    try:
        with urllib.request.urlopen(BASE + path, timeout=10) as r:
            return r.read()
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return None
        raise


def main():
    out = CAPTURES / datetime.datetime.now().strftime("%Y%m%d_%H%M%S")

    # Grab the live frame FIRST, before creating anything on disk — if the server is unreachable
    # or no mission is loaded, bail out with nothing written at all.
    frame = grab_frame()
    print(f"\nCaptured frame: {frame.get('name')}  —  {frame.get('mapName')} / {frame.get('mission')}")
    out.mkdir(parents=True, exist_ok=True)

    # AKF (docs/akf-page.md) rides the same /stream frame as everything else — no separate capture
    # step needed, preview-mock.js already forwards frame['akf'] verbatim. But it's a snapshot of
    # whatever's accumulated in the CURRENT mission by capture time, so an early capture just means
    # an empty feed — flag that here instead of leaving it a silent, confusing blank page in preview.
    akf = frame.get("akf") or {}
    akf_all, akf_player = len(akf.get("all") or []), len(akf.get("player") or [])
    print(f"  akf     captured  {akf_all} all / {akf_player} player feed lines, rank {akf.get('rank', 0)}")
    if akf_all == 0:
        print("  NOTE: AKF feed is empty — score/take a few kills in-game, then capture again if you"
              " want the AKF page preview populated.")

    assets = {}

    data = fetch("/map")
    if not data:
        sys.exit("ERROR: /map returned no image yet — wait for the map to load in-game, then retry.")
    (out / "map.png").write_bytes(data)
    assets["map"] = "map.png"
    print(f"  map     saved   ({len(data):,} bytes)")

    # Icons: the player aircraft, every distinct contact type, AND every type the plugin has
    # actually captured server-side this session (/icon-types) — that last source is what makes
    # squadmates' aircraft show up here even though they're never a "contact" (radar/HUD
    # detection): AssetCapture.TryCaptureIcon runs off ScanWorld's FindObjectsByType<Unit> scan,
    # every Unit actually spawned in the mission, not just ones on the player's own sensors, so the
    # plugin already has them captured; this script previously had no way to ask for them.
    icon_types_data = fetch("/icon-types")
    known_types = json.loads(icon_types_data.decode("utf-8")) if icon_types_data else []
    types = list(dict.fromkeys(
        [frame.get("name", "")] + [c["t"] for c in frame.get("contacts", [])] + known_types))
    for i, t in enumerate(t for t in types if t):
        data = fetch("/icon?type=" + urllib.parse.quote(t))
        if not data:
            print(f"  icon    (none)  {t}")
            continue
        fn = f"icon_{i}.png"
        (out / fn).write_bytes(data)
        assets["icon:" + t] = fn
        print(f"  icon    saved   {t}")

    # Weapon icons, keyed by loadout display name.
    weapons = list(dict.fromkeys(w["n"] for w in frame.get("loadout", [])))
    for i, n in enumerate(weapons):
        data = fetch("/weapon?name=" + urllib.parse.quote(n))
        if not data:
            print(f"  weapon  (none)  {n}")
            continue
        fn = f"weapon_{i}.png"
        (out / fn).write_bytes(data)
        assets["weapon:" + n] = fn
        print(f"  weapon  saved   {n}")

    # HUD OPTIONS type-sprite icons: one per vehicle type and per building type. The names come from
    # /hud-options (the same endpoint the HUD page reads); vehicles are served at /tgt-icon (shared
    # with the TGT filter), buildings at /building-icon (a separate keyspace — "RDR" is both).
    hud = fetch("/hud-options")
    if hud:
        opts = json.loads(hud.decode("utf-8"))
        for endpoint, group, key in (("/tgt-icon", "vehicles", "tgt-icon"),
                                     ("/building-icon", "buildings", "building-icon")):
            for i, item in enumerate(opts.get(group) or []):
                name = item.get("n")
                if not name:
                    continue
                data = fetch(endpoint + "?type=" + urllib.parse.quote(name))
                if not data:
                    print(f"  {key:12s} (none)  {name}")
                    continue
                fn = f"{key.replace('-', '_')}_{i}.png"
                (out / fn).write_bytes(data)
                assets[key + ":" + name] = fn
                print(f"  {key:12s} saved   {name}")

        # HUD OPTIONS category-row glyphs — AIRCRAFT/MISSILES/VEHICLES/BUILDINGS/SHIPS, keyed by the
        # same fixed label the page and the plugin's capture both use. Unlike vehicles/buildings
        # above, /hud-options carries no per-category name to discover these from (the game exposes
        # none), so the five labels are hardcoded here too. FRIENDLY/ENEMY have no icon in game.
        for i, label in enumerate(("AIRCRAFT", "MISSILES", "VEHICLES", "BUILDINGS", "SHIPS")):
            data = fetch("/hud-cat-icon?cat=" + urllib.parse.quote(label))
            if not data:
                print(f"  hud-cat-icon (none)  {label}")
                continue
            fn = f"hud_cat_icon_{i}.png"
            (out / fn).write_bytes(data)
            assets["hud-cat-icon:" + label] = fn
            print(f"  hud-cat-icon saved   {label}")

    # AVN airframe silhouette: background PNG + one PNG per UI segment + the layout JSON.
    # All one-shot per aircraft type — keyed by frame.name. If the layout 404s the airframe
    # capture is silently skipped (e.g. capturing during the brief window before the cockpit
    # StatusDisplay has been built).
    af_type = frame.get("name", "")
    if af_type:
        layout_bytes = fetch("/airframe-layout?type=" + urllib.parse.quote(af_type))
        if layout_bytes:
            try:
                layout = json.loads(layout_bytes.decode("utf-8"))
            except json.JSONDecodeError:
                layout = None
        else:
            layout = None
        if layout:
            assets["airframe-layout:" + af_type] = layout      # inlined JSON, not a file
            bg = fetch(f"/airframe?type={urllib.parse.quote(af_type)}&part=__bg")
            if bg:
                fn = "airframe_bg.png"
                (out / fn).write_bytes(bg)
                assets[f"airframe:{af_type}|__bg"] = fn
                print(f"  airframe bg     saved   ({len(bg):,} bytes)")
            saved = 0
            for i, p in enumerate(layout.get("parts", [])):
                name = p.get("n")
                if not name: continue
                data = fetch(f"/airframe?type={urllib.parse.quote(af_type)}&part={urllib.parse.quote(name)}")
                if not data: continue
                fn = f"airframe_part_{i}.png"
                (out / fn).write_bytes(data)
                assets[f"airframe:{af_type}|{name}"] = fn
                saved += 1
            print(f"  airframe parts  saved   {saved} for '{af_type}'")
        else:
            print(f"  airframe layout (none)  for '{af_type}' — capture again once you're in the cockpit")

        # AFM frontal silhouette: the cockpit's weapon-station-armed panel's nose-on image + its
        # pylon-marker layout. Same shape as the __bg block above, just under the "<type>__front"
        # key the real endpoints use (see AssetCapture.TryCaptureFrontalSilhouette). Pylon marker
        # COLORS need no capture step — they ride the live /stream frame itself (frame["pylons"]),
        # already saved into manifest.json below.
        front_type = af_type + "__front"
        front_layout_bytes = fetch("/airframe-layout?type=" + urllib.parse.quote(front_type))
        if front_layout_bytes:
            try:
                front_layout = json.loads(front_layout_bytes.decode("utf-8"))
            except json.JSONDecodeError:
                front_layout = None
        else:
            front_layout = None
        if front_layout:
            assets["airframe-layout:" + front_type] = front_layout   # inlined JSON, not a file
            front_bg = fetch(f"/airframe?type={urllib.parse.quote(af_type)}&part=__front")
            if front_bg:
                fn = "airframe_front.png"
                (out / fn).write_bytes(front_bg)
                assets[f"airframe:{af_type}|__front"] = fn
                print(f"  airframe front  saved   ({len(front_bg):,} bytes)")
        else:
            print(f"  airframe front  (none)  for '{af_type}' — capture again once you're in the cockpit")

    (out / "manifest.json").write_text(
        json.dumps({"frame": frame, "assets": assets}, indent=2), encoding="utf-8")
    print(f"\nWrote {len(assets)} assets + manifest to {out.relative_to(ROOT)}")
    print("This is a standalone capture — preview/assets/ (the live mock data) was NOT touched.")
    print("Tell Claude which of these you want brought into the live preview and it'll merge them in.")


if __name__ == "__main__":
    main()
