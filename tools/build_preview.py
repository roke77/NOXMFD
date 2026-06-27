#!/usr/bin/env python3
"""Compatibility helper for the old generated-preview workflow.

The MFD frontend now lives in real files under web/ and is served directly by
tools/serve_web.py. There are no C# page blobs left to extract into preview/.
This script removes stale generated HTML files so old previews are not mistaken
for the current UI, then points you at the live HTTP harness.

Usage:
    python tools/build_preview.py
    python tools/build_preview.py --open   # opens the default serve_web URL
"""
import pathlib
import sys
import webbrowser


ROOT = pathlib.Path(__file__).resolve().parent.parent
PREVIEW = ROOT / "preview"
STALE = ("index.html", "mfd.html", "map-view.html", "main.html")
DEFAULT_URL = "http://127.0.0.1:8782/"


def main() -> None:
    removed = []
    for name in STALE:
        fp = PREVIEW / name
        if fp.exists():
            fp.unlink()
            removed.append(fp.relative_to(ROOT).as_posix())

    if removed:
        print("Removed stale generated preview files:")
        for name in removed:
            print(f"  {name}")
    else:
        print("No stale generated preview files found.")

    print("No build step is needed now. Run: python tools/serve_web.py --open")
    if "--open" in sys.argv or "--mfd" in sys.argv:
        webbrowser.open(DEFAULT_URL)


if __name__ == "__main__":
    main()
