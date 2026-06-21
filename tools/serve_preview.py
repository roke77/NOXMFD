#!/usr/bin/env python3
"""Serve the generated preview/ folder over HTTP so the HUD / MFD page can be opened in
a browser without `file://` security quirks (CSS mask-image and other subresource
fetches between sibling file:// URLs are blocked in Chromium).

Usage:
    python tools/serve_preview.py            # serve on http://localhost:8777
    python tools/serve_preview.py --open     # ...and open the MFD page in your browser
    python tools/serve_preview.py --port 9000

Run tools/build_preview.py first to actually populate preview/. Ctrl+C to stop.
"""
import argparse
import http.server
import pathlib
import socketserver
import sys
import webbrowser

ROOT = pathlib.Path(__file__).resolve().parent.parent
PREVIEW = ROOT / "preview"


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--port", type=int, default=8777, help="port to bind (default 8777)")
    ap.add_argument("--open", action="store_true", help="open MFD page in browser on start")
    args = ap.parse_args()

    if not (PREVIEW / "mfd.html").exists():
        sys.exit(
            f"ERROR: {PREVIEW / 'mfd.html'} not found.\n"
            f"  Run `python tools/build_preview.py` first."
        )

    # Handler must serve from preview/, not the cwd.
    handler = lambda *a, **kw: http.server.SimpleHTTPRequestHandler(
        *a, directory=str(PREVIEW), **kw
    )

    url = f"http://localhost:{args.port}"
    with socketserver.TCPServer(("", args.port), handler) as srv:
        print(f"Serving {PREVIEW.relative_to(ROOT)} at {url}/")
        print(f"  HUD :  {url}/index.html")
        print(f"  MFD :  {url}/mfd.html")
        print("Press Ctrl+C to stop.")
        if args.open:
            webbrowser.open(f"{url}/mfd.html")
        try:
            srv.serve_forever()
        except KeyboardInterrupt:
            print("\nStopped.")


if __name__ == "__main__":
    main()
