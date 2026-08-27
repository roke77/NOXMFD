#!/usr/bin/env python3
"""Screenshot every MFD page, in the CLASSIC layout, for every capture in the library.

For each preview/captures/<timestamp>/ folder that has a manifest.json, renders that capture's
data through the same harness serve_web.py uses — pinned via CAPTURE_OVERRIDE (imported directly,
in-process, not a subprocess) rather than the shared CURRENT pointer, so this never disturbs a
preview server you might already have open manually — and screenshots the real NAV destinations
(everything except LYT, a picker overlay rather than a distinct page) at 1000x1000, saving them
into preview/captures/<timestamp>/screenshots/<PAGE>.png.

A capture folder that already has a screenshots/ subfolder is skipped — pass --force to redo it
(or every capture), or delete that one subfolder to redo just it on the next plain run.

Requires Playwright: `pip install playwright && python -m playwright install chromium`.

Usage:
    python tools/capture_screenshots.py            # every un-screenshotted capture in the library
    python tools/capture_screenshots.py --force     # redo every capture, even ones already done
    python tools/capture_screenshots.py 20260820_213946   # just this one capture folder
"""
import argparse
import pathlib
import sys
import threading

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import serve_web  # noqa: E402 (import must follow the sys.path insert above)

from playwright.sync_api import sync_playwright  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parent.parent
CAPTURES = ROOT / "preview" / "captures"
PORT = 8783   # separate from serve_web.py's own default 8782, so a manually-running preview
              # server is never disturbed by this script's own instance

# label -> showPage() action name. The 19 real NAV destinations, LYT excluded (a picker overlay,
# not a distinct page — see docs/user-manuals.md). showPage is a plain global in mfd.js (a classic
# script, not a module), so it's callable directly via page.evaluate() — far more robust than
# simulating physical bezel-key clicks for each one.
PAGES = [
    ("AFM", "afm"), ("AVN", "avn"),
    ("HUD", "hud"), ("KEY", "keys"), ("RTS", "rates"),
    ("EXT", "ext"),
    ("MAIN", "main"),
    ("MAP", "map"), ("WPT", "wpt"),
    ("AKF", "akf"), ("BDF", "bdf"), ("PAL", "pal"), ("MIS", "mis"), ("OBJ", "obj"),
    ("RDR", "rdr"), ("HSD", "hsd"), ("RWR", "rwr"), ("TGP", "tgp"), ("TGT", "tgt"), ("WPN", "wpn"),
]


def start_server():
    """One server for the whole run — CAPTURE_OVERRIDE is read fresh per request (see
    serve_web._manifest_path), so switching it between capture folders needs no restart."""
    srv = serve_web.Server(("127.0.0.1", PORT), serve_web.H)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    return srv


def screenshot_capture(page, folder: pathlib.Path):
    serve_web.CAPTURE_OVERRIDE = folder.name
    out_dir = folder / "screenshots"
    out_dir.mkdir(exist_ok=True)
    page.goto(f"http://127.0.0.1:{PORT}/", wait_until="load")
    page.wait_for_timeout(500)   # let the shell finish its own boot (ExtNav.load, initial showPage)
    for label, action in PAGES:
        page.evaluate(f"window.showPage({action!r})")
        page.wait_for_timeout(500)   # iframe navigation + shell->page postMessage forwarding to settle
        page.screenshot(path=str(out_dir / f"{label}.png"))
    print(f"  {folder.name}: {len(PAGES)} screenshots -> {out_dir.relative_to(ROOT)}")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                  formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("only", nargs="?", help="just this one capture folder's timestamp")
    ap.add_argument("--force", action="store_true", help="redo captures that already have screenshots/")
    args = ap.parse_args()

    if not CAPTURES.exists():
        sys.exit("ERROR: preview/captures/ doesn't exist — run capture_assets.py at least once first.")

    folders = sorted(p for p in CAPTURES.iterdir() if p.is_dir() and (p / "manifest.json").exists())
    if args.only:
        folders = [p for p in folders if p.name == args.only]
        if not folders:
            sys.exit(f"ERROR: no capture folder named '{args.only}' with a manifest.json.")
    if not args.force:
        folders = [p for p in folders if not (p / "screenshots").exists()]
    if not folders:
        print("Nothing to do — every capture already has screenshots/ (pass --force to redo).")
        return

    print(f"Screenshotting {len(folders)} capture(s), {len(PAGES)} pages each...")
    start_server()
    with sync_playwright() as pw:
        browser = pw.chromium.launch()
        page = browser.new_page(viewport={"width": 1000, "height": 1000})
        for folder in folders:
            screenshot_capture(page, folder)
        browser.close()
    print("Done.")


if __name__ == "__main__":
    main()
