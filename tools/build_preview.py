#!/usr/bin/env python3
"""Generate a standalone, game-free preview of the telemetry HUD.

The HUD's HTML/JS lives inside src/ClientPage.cs (the single source of truth that
ships in the mod). This script extracts that exact markup and injects
tools/preview-mock.js, which stubs the game's /stream, /map, /icon and /weapon so
the page runs in a plain browser — no game, no server.

If you have captured real game assets (tools/capture_assets.py while in-game),
preview/assets/manifest.json is injected too, so the preview replays the real map,
icons, weapon names/icons, contacts and loadout. Otherwise the mock's built-in
synthetic scenario is used.

Usage:
    python tools/build_preview.py            # writes preview/index.html (+ preview/mfd.html)
    python tools/build_preview.py --open     # ...and opens the HUD in your browser
    python tools/build_preview.py --mfd      # ...and opens the MFD-framed page instead

Re-run after editing ClientPage.cs / MfdPage.cs (or the mock, or re-capturing) to refresh.
"""
import json
import pathlib
import sys
import webbrowser

ROOT = pathlib.Path(__file__).resolve().parent.parent
CLIENT = ROOT / "src" / "ClientPage.cs"
MFD = ROOT / "src" / "MfdPage.cs"
MOCK = ROOT / "tools" / "preview-mock.js"
MANIFEST = ROOT / "preview" / "assets" / "manifest.json"
OUT = ROOT / "preview" / "index.html"
OUT_MFD = ROOT / "preview" / "mfd.html"

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
    """Pull the raw-string-literal body (the page) out of ClientPage.cs."""
    open_i = cs.find(DELIM)
    if open_i == -1:
        sys.exit('ERROR: opening """ not found in ClientPage.cs')
    start = cs.index("\n", open_i) + 1        # content begins on the line after the opening """
    close_i = cs.rfind(DELIM)                 # closing """ of `""";`
    if close_i <= start:
        sys.exit('ERROR: closing """ not found in ClientPage.cs')
    return cs[start:close_i].rstrip("\n ")


def main() -> None:
    html = extract_html(CLIENT.read_text(encoding="utf-8"))
    mock = MOCK.read_text(encoding="utf-8").strip()
    if "</head>" not in html:
        sys.exit("ERROR: no </head> in the extracted page — cannot inject the mock")

    injection = capture_injection()
    page = html.replace("</head>", injection + mock + "\n</head>", 1)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(page, encoding="utf-8")

    source = "captured game assets" if injection else "synthetic mock (run capture_assets.py while in-game for real assets)"
    print(f"Wrote {OUT.relative_to(ROOT)}  ({len(page):,} bytes)")
    print(f"Data source: {source}")

    # MFD page: same idea, but its central screen is an iframe at /?bare. Over file://
    # that won't resolve, so point it at the generated bare map preview instead. The mock
    # is also injected so the MFD's own /weapon fetches (used by the WPN page) resolve.
    if MFD.exists():
        mfd = extract_html(MFD.read_text(encoding="utf-8"))
        mfd = mfd.replace('src="/?bare"', 'src="index.html?bare"')
        mfd = mfd.replace("</head>", injection + mock + "\n</head>", 1)
        OUT_MFD.write_text(mfd, encoding="utf-8")
        print(f"Wrote {OUT_MFD.relative_to(ROOT)}  ({len(mfd):,} bytes)")

    if "--mfd" in sys.argv:
        webbrowser.open(OUT_MFD.as_uri())
    elif "--open" in sys.argv:
        webbrowser.open(OUT.as_uri())


if __name__ == "__main__":
    main()
