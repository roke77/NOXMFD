#!/usr/bin/env python3
"""Generate a standalone, game-free preview of the MFD.

The live server uses the MFD shell as the index page. This script writes:

    preview/index.html       # MFD shell (extracted from src/MfdPage.cs)

The shell gets tools/preview-mock.js injected, which stubs the game's /stream,
/map, /icon and /weapon so the preview runs in a plain browser — no game, no
server. The MAP page is now a real web file (web/pages/map/map.html) served over
HTTP by tools/serve_web.py (which injects the mock itself), so it is no longer
extracted here; the shell's base map iframe loads /map-view?bare absolutely,
which serve_web resolves. (WPN/TGL/TGP/AVN/RWR migrated likewise — see
todo/src-architecture.md. The file:// preview of the shell can't load these
http-served pages; use serve_web.py to exercise the full shell over http.)

If you have captured real game assets (tools/capture_assets.py while in-game),
preview/assets/manifest.json is injected too, so the preview replays the real map,
icons, weapon names/icons, contacts and loadout. Otherwise the mock's built-in
synthetic scenario is used.

Usage:
    python tools/build_preview.py            # writes preview/index.html
    python tools/build_preview.py --open     # ...and opens the MFD in your browser
    python tools/build_preview.py --mfd      # alias for --open

Re-run after editing MfdPage.cs (or the mock, or re-capturing) to refresh.
"""
import json
import pathlib
import sys
import webbrowser

ROOT = pathlib.Path(__file__).resolve().parent.parent

MFD = ROOT / "src" / "MfdPage.cs"
# MAP + MAIN migrated to web/pages/{map,main}/ and WPN + TGL + TGP + AVN + RWR to
# web/pages/{wpn,tgl,tgp,avn,rwr}/ (http-served via /assets + their routes); no longer C# const
# blobs. The file:// preview can't load http-served pages or /config (todo/src-architecture.md);
# use tools/serve_web.py to drive the shell + all pages over http.
MOCK = ROOT / "tools" / "preview-mock.js"
MANIFEST = ROOT / "preview" / "assets" / "manifest.json"
OUT = ROOT / "preview" / "index.html"
OLD_OUT_MFD = ROOT / "preview" / "mfd.html"
OLD_OUT_MAP = ROOT / "preview" / "map-view.html"
OLD_OUT_MAIN = ROOT / "preview" / "main.html"

DELIM = '"""'


def capture_injection() -> str:
    """If a capture exists, build a <script> that exposes the real frame + assets."""
    if not MANIFEST.exists():
        return ""
    m = json.loads(MANIFEST.read_text(encoding="utf-8"))
    # `</` is escaped so a stray sequence in the data can't close the <script> early.
    frame = json.dumps(m.get("frame", {})).replace("</", "<\\/")
    assets = json.dumps(m.get("assets", {})).replace("</", "<\\/")
    return ("<script>\n"
            f"window.__PREVIEW_FRAME__ = {frame};\n"
            f"window.__PREVIEW_ASSETS__ = {assets};\n"
            "</script>\n")


def extract_html(cs: str) -> str:
    """Pull the raw-string-literal body (the page) out of a .cs page holder."""
    open_i = cs.find(DELIM)
    if open_i == -1:
        sys.exit('ERROR: opening """ not found in page holder')
    start = cs.index("\n", open_i) + 1        # content begins on the line after the opening """
    close_i = cs.rfind(DELIM)                 # closing """ of `""";`
    if close_i <= start:
        sys.exit('ERROR: closing """ not found in page holder')
    return cs[start:close_i].rstrip("\n ")


def main() -> None:
    mock = MOCK.read_text(encoding="utf-8").strip()
    injection = capture_injection()
    OUT.parent.mkdir(parents=True, exist_ok=True)

    source = "captured game assets" if injection else "synthetic mock (run capture_assets.py while in-game for real assets)"
    print(f"Data source: {source}")

    # MFD index page: its central screen embeds /map-view?bare (always) and, in split
    # mode, /main?bare for pane iframes. Migrated pages and /config are http-served by
    # tools/serve_web.py, so their absolute routes are intentionally left alone. The mock is
    # injected here so any direct shell fetches resolve once serve_web hosts the preview shell.
    if MFD.exists():
        mfd = extract_html(MFD.read_text(encoding="utf-8"))
        mfd = mfd.replace("</head>", injection + mock + "\n</head>", 1)
        OUT.write_text(mfd, encoding="utf-8")
        print(f"Wrote {OUT.relative_to(ROOT)}  ({len(mfd):,} bytes)")

    # (MAP + MAIN + WPN + TGL + TGP + AVN + RWR bare pages removed — migrated to web/pages/, http-served,
    #  so they are no longer extracted into the file:// preview. Use tools/serve_web.py over http.)

    if OLD_OUT_MFD.exists():
        OLD_OUT_MFD.unlink()
        print(f"Removed stale {OLD_OUT_MFD.relative_to(ROOT)}")
    if OLD_OUT_MAP.exists():
        OLD_OUT_MAP.unlink()
        print(f"Removed stale {OLD_OUT_MAP.relative_to(ROOT)}")
    if OLD_OUT_MAIN.exists():
        OLD_OUT_MAIN.unlink()
        print(f"Removed stale {OLD_OUT_MAIN.relative_to(ROOT)}")

    if "--mfd" in sys.argv or "--open" in sys.argv:
        webbrowser.open(OUT.as_uri())


if __name__ == "__main__":
    main()
