#!/usr/bin/env python3
"""Capture the real game-served assets from a running session, so the static
preview can replay them instead of the synthetic mocks.

Run this WHILE Nuclear Option is running with a mission loaded and you are flying
(so /stream serves real telemetry and the map/icons have been extracted by the
mod). It pulls, from http://localhost:5005:

  • one real telemetry frame  (/stream)         → the static scenario
  • the map image             (/map)            → assets/map.png
  • each unit type's icon      (/icon?type=...)  → assets/icon_*.png
  • each weapon's icon         (/weapon?name=..) → assets/weapon_*.png

...and writes preview/assets/manifest.json describing them. Then run:

    python tools/build_preview.py --open

Units/weapons that have no icon in-game simply 404 and are skipped — the preview
shows the same square fallback the real HUD uses for them.
"""
import json
import pathlib
import sys
import urllib.error
import urllib.parse
import urllib.request

BASE = "http://localhost:5005"
ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "preview" / "assets"
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
    # Start clean so stale assets from a previous capture don't linger.
    ASSETS.mkdir(parents=True, exist_ok=True)
    for old in ASSETS.glob("*.png"):
        old.unlink()
    (ASSETS / "manifest.json").unlink(missing_ok=True)

    frame = grab_frame()
    print(f"\nCaptured frame: {frame.get('name')}  —  {frame.get('mapName')} / {frame.get('mission')}")

    assets = {}

    data = fetch("/map")
    if not data:
        sys.exit("ERROR: /map returned no image yet — wait for the map to load in-game, then retry.")
    (ASSETS / "map.png").write_bytes(data)
    assets["map"] = "assets/map.png"
    print(f"  map     saved   ({len(data):,} bytes)")

    # Icons: the player aircraft plus every distinct contact type.
    types = list(dict.fromkeys([frame.get("name", "")] + [c["t"] for c in frame.get("contacts", [])]))
    for i, t in enumerate(t for t in types if t):
        data = fetch("/icon?type=" + urllib.parse.quote(t))
        if not data:
            print(f"  icon    (none)  {t}")
            continue
        fn = f"icon_{i}.png"
        (ASSETS / fn).write_bytes(data)
        assets["icon:" + t] = "assets/" + fn
        print(f"  icon    saved   {t}")

    # Weapon icons, keyed by loadout display name.
    weapons = list(dict.fromkeys(w["n"] for w in frame.get("loadout", [])))
    for i, n in enumerate(weapons):
        data = fetch("/weapon?name=" + urllib.parse.quote(n))
        if not data:
            print(f"  weapon  (none)  {n}")
            continue
        fn = f"weapon_{i}.png"
        (ASSETS / fn).write_bytes(data)
        assets["weapon:" + n] = "assets/" + fn
        print(f"  weapon  saved   {n}")

    (ASSETS / "manifest.json").write_text(
        json.dumps({"frame": frame, "assets": assets}, indent=2), encoding="utf-8")
    print(f"\nWrote {len(assets)} assets + manifest to {ASSETS.relative_to(ROOT)}")
    print("Now run:  python tools/build_preview.py --open")


if __name__ == "__main__":
    main()
