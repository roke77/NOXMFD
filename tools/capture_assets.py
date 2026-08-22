#!/usr/bin/env python3
"""Capture real game-served assets from a running session for the static preview to replay.

Run while Nuclear Option is running with a mission loaded and flying (so /stream
serves telemetry and the map/icons exist). Pulls from http://localhost:5005:
one telemetry frame (/stream), map image (/map), unit/weapon/BDF-PAL icons (the
unit types include every type the plugin has captured this session per
/icon-types, which is what picks up squadmates' aircraft — never a "contact"
but already captured server-side from just being spawned in the mission), HUD
vehicle/building/category icons, airframe silhouette, one TGP frame, and the
raw HUD/WPT/RTS config (inlined into the manifest).

Each run writes its own timestamped folder under preview/captures/ — a library,
not a single overwritten slot:

    preview/captures/20260820_193045/manifest.json + assets
    preview/captures/CURRENT                 <- names the live capture

serve_web.py reads whichever capture CURRENT names (falls back to the older
single-slot preview/assets/manifest.json). This script points CURRENT at the
capture it just took; to switch to an older one, overwrite CURRENT with that
folder's name (no restart needed).

Then run (or leave running):

    python tools/serve_web.py --open

Units/weapons/ships with no in-game icon 404 and are skipped — the preview
falls back to the same square placeholder the real HUD uses.
"""
import json
import pathlib
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = "http://localhost:5005"
ROOT = pathlib.Path(__file__).resolve().parent.parent
LIBRARY = ROOT / "preview" / "captures"
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


def fetch_tgp_frame():
    """One still JPEG out of the live /tgp.mjpg feed.

    ponytail: scans raw bytes for a JPEG SOI/EOI pair instead of parsing the
    multipart boundary properly — good enough to grab one frame out of a feed
    this mod itself produces (a well-formed encoder, not adversarial input);
    a real multipart parser would be the upgrade if this ever needs to handle
    a feed from somewhere else. Gives up after ~2MB so a feed that never
    resolves (TGP not active) doesn't hang the capture.
    """
    try:
        resp = urllib.request.urlopen(BASE + "/tgp.mjpg", timeout=10)
    except (urllib.error.URLError, OSError):
        # OSError (not just URLError) because a server that accepts the connection but never
        # writes a response within the timeout — exactly "TGP not active, feed never starts" —
        # surfaces as a bare socket timeout here, not wrapped in URLError.
        return None
    buf = b""
    try:
        while len(buf) < 2_000_000:
            chunk = resp.read(65536)
            if not chunk:
                break
            buf += chunk
            start = buf.find(b"\xff\xd8")
            if start == -1:
                continue
            end = buf.find(b"\xff\xd9", start)
            if end != -1:
                return buf[start:end + 2]
    except (urllib.error.URLError, OSError):
        return None
    finally:
        resp.close()
    return None


def main():
    ts = time.strftime("%Y%m%d_%H%M%S")
    assets_dir = LIBRARY / ts
    rel_prefix = f"captures/{ts}/"

    # Grab the live frame FIRST. If the server is unreachable / no mission loaded,
    # bail before creating anything — a failed capture shouldn't leave an empty
    # folder cluttering the library, and CURRENT should keep pointing at the last
    # good one.
    frame = grab_frame()
    print(f"\nCaptured frame: {frame.get('name')}  —  {frame.get('mapName')} / {frame.get('mission')}")

    akf = frame.get("akf") or {}
    akf_all, akf_player = len(akf.get("all") or []), len(akf.get("player") or [])
    print(f"  akf     captured  {akf_all} all / {akf_player} player feed lines, rank {akf.get('rank', 0)}")
    if akf_all == 0:
        print("  NOTE: AKF feed is empty — score/take a few kills in-game, then capture again if you"
              " want the AKF page preview populated.")

    assets_dir.mkdir(parents=True, exist_ok=True)
    assets = {}

    data = fetch("/map")
    if not data:
        sys.exit("ERROR: /map returned no image yet — wait for the map to load in-game, then retry.")
    (assets_dir / "map.png").write_bytes(data)
    assets["map"] = rel_prefix + "map.png"
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
        (assets_dir / fn).write_bytes(data)
        assets["icon:" + t] = rel_prefix + fn
        print(f"  icon    saved   {t}")

    # Weapon icons, keyed by loadout display name.
    weapons = list(dict.fromkeys(w["n"] for w in frame.get("loadout", [])))
    for i, n in enumerate(weapons):
        data = fetch("/weapon?name=" + urllib.parse.quote(n))
        if not data:
            print(f"  weapon  (none)  {n}")
            continue
        fn = f"weapon_{i}.png"
        (assets_dir / fn).write_bytes(data)
        assets["weapon:" + n] = rel_prefix + fn
        print(f"  weapon  saved   {n}")

    # BDF/PAL ship-type icons, keyed by the "n" field the bdf/pal telemetry blocks' ships
    # array already carries (docs/bdf-page.md) — same shape as icon:/weapon: above.
    ships = list(dict.fromkeys(
        s["n"] for block in (frame.get("bdf") or {}, frame.get("pal") or {})
        for s in (block.get("ships") or []) if s.get("n")
    ))
    for i, n in enumerate(ships):
        data = fetch("/bdf-icon?type=" + urllib.parse.quote(n))
        if not data:
            print(f"  bdf-icon (none)  {n}")
            continue
        fn = f"bdf_icon_{i}.png"
        (assets_dir / fn).write_bytes(data)
        assets["bdf-icon:" + n] = rel_prefix + fn
        print(f"  bdf-icon saved   {n}")

    # HUD OPTIONS type-sprite icons: one per vehicle type and per building type. The names come from
    # /hud-options (the same endpoint the HUD page reads); vehicles are served at /tgt-icon (shared
    # with the TGT filter), buildings at /building-icon (a separate keyspace — "RDR" is both).
    hud = fetch("/hud-options")
    if hud:
        opts = json.loads(hud.decode("utf-8"))
        assets["hud-options"] = opts   # inlined JSON (like airframe-layout below), not a file —
                                        # lets the HUD page render real mode/category/type state
                                        # instead of the harness's hand-authored mock.
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
                (assets_dir / fn).write_bytes(data)
                assets[key + ":" + name] = rel_prefix + fn
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
            (assets_dir / fn).write_bytes(data)
            assets["hud-cat-icon:" + label] = rel_prefix + fn
            print(f"  hud-cat-icon saved   {label}")
    else:
        print("  hud-options (none)  — HUD page will fall back to the harness mock")

    # WPT route library (RouteStore.RoutesJson) — inlined, same treatment as hud-options above, so
    # WPT/MAP show your actual saved routes instead of the harness's fixed showcase route.
    wpt = fetch("/wpt-options")
    if wpt:
        assets["wpt-options"] = json.loads(wpt.decode("utf-8"))
        print("  wpt-options saved")
    else:
        print("  wpt-options (none)  — WPT page will fall back to the harness mock")

    # RTS live refresh-rate config — inlined the same way.
    rates = fetch("/rates-config")
    if rates:
        assets["rates-config"] = json.loads(rates.decode("utf-8"))
        print("  rates-config saved")
    else:
        print("  rates-config (none)  — RTS page will fall back to the harness mock")

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
                (assets_dir / fn).write_bytes(bg)
                assets[f"airframe:{af_type}|__bg"] = rel_prefix + fn
                print(f"  airframe bg     saved   ({len(bg):,} bytes)")
            saved = 0
            for i, p in enumerate(layout.get("parts", [])):
                name = p.get("n")
                if not name: continue
                data = fetch(f"/airframe?type={urllib.parse.quote(af_type)}&part={urllib.parse.quote(name)}")
                if not data: continue
                fn = f"airframe_part_{i}.png"
                (assets_dir / fn).write_bytes(data)
                assets[f"airframe:{af_type}|{name}"] = rel_prefix + fn
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
                (assets_dir / fn).write_bytes(front_bg)
                assets[f"airframe:{af_type}|__front"] = rel_prefix + fn
                print(f"  airframe front  saved   ({len(front_bg):,} bytes)")
        else:
            print(f"  airframe front  (none)  for '{af_type}' — capture again once you're in the cockpit")

    # TGP: one still frame out of the live MJPEG feed, so the page shows the real cockpit camera
    # instead of a blank iframe (the harness never handled /tgp.mjpg at all before this).
    tgp = fetch_tgp_frame()
    if tgp:
        (assets_dir / "tgp.jpg").write_bytes(tgp)
        assets["tgp"] = rel_prefix + "tgp.jpg"
        print(f"  tgp     saved   ({len(tgp):,} bytes)")
    else:
        print("  tgp     (none)  — lock a target with TGP active, then capture again")

    (assets_dir / "manifest.json").write_text(
        json.dumps({"frame": frame, "assets": assets}, indent=2), encoding="utf-8")
    (LIBRARY / "CURRENT").write_text(ts, encoding="utf-8")
    print(f"\nWrote {len(assets)} assets + manifest to preview/captures/{ts}/")
    print(f"CURRENT now points at it. Run:  python tools/serve_web.py --open")


if __name__ == "__main__":
    main()
