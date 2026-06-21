#!/usr/bin/env python3
"""Generate a standalone, game-free preview of the MFD.

The live server now uses the MFD as the index page. The MFD shell still embeds
the map renderer from src/ClientPage.cs in bare mode, so this script writes:

    preview/index.html       # MFD shell
    preview/map-view.html    # embedded map/HUD client, used as ?bare by the MFD

Both pages get tools/preview-mock.js injected, which stubs the game's /stream,
/map, /icon and /weapon so the preview runs in a plain browser — no game, no
server.

If you have captured real game assets (tools/capture_assets.py while in-game),
preview/assets/manifest.json is injected too, so the preview replays the real map,
icons, weapon names/icons, contacts and loadout. Otherwise the mock's built-in
synthetic scenario is used.

Usage:
    python tools/build_preview.py            # writes preview/index.html (+ preview/map-view.html)
    python tools/build_preview.py --open     # ...and opens the MFD in your browser
    python tools/build_preview.py --mfd      # alias for --open

Re-run after editing ClientPage.cs / MfdPage.cs (or the mock, or re-capturing) to refresh.
"""
import json
import pathlib
import sys
import webbrowser

ROOT = pathlib.Path(__file__).resolve().parent.parent
CLIENT = ROOT / "src" / "ClientPage.cs"
MFD = ROOT / "src" / "MfdPage.cs"
MAIN = ROOT / "src" / "MainPage.cs"
AVN = ROOT / "src" / "AvnPage.cs"
TGP = ROOT / "src" / "TgpPage.cs"
MOCK = ROOT / "tools" / "preview-mock.js"
MANIFEST = ROOT / "preview" / "assets" / "manifest.json"
OUT = ROOT / "preview" / "index.html"
OUT_MAP = ROOT / "preview" / "map-view.html"
OUT_MAIN = ROOT / "preview" / "main.html"
OUT_AVN = ROOT / "preview" / "avn.html"
OUT_TGP = ROOT / "preview" / "tgp.html"
OLD_OUT_MFD = ROOT / "preview" / "mfd.html"

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
    map_html = extract_html(CLIENT.read_text(encoding="utf-8"))
    mock = MOCK.read_text(encoding="utf-8").strip()
    if "</head>" not in map_html:
        sys.exit("ERROR: no </head> in the extracted map page — cannot inject the mock")

    injection = capture_injection()
    map_page = map_html.replace("</head>", injection + mock + "\n</head>", 1)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT_MAP.write_text(map_page, encoding="utf-8")

    source = "captured game assets" if injection else "synthetic mock (run capture_assets.py while in-game for real assets)"
    print(f"Wrote {OUT_MAP.relative_to(ROOT)}  ({len(map_page):,} bytes)")
    print(f"Data source: {source}")

    # MFD index page: its central screen embeds /map-view?bare (always) and, in split
    # mode, /main?bare for the pane iframes. Over file:// neither resolves, so we point
    # them at the generated bare previews instead. The mock is also injected so the
    # MFD's own /weapon fetches (used by the WPN page) resolve.
    if MFD.exists():
        mfd = extract_html(MFD.read_text(encoding="utf-8"))
        mfd = mfd.replace('src="/map-view?bare"', 'src="map-view.html?bare"')
        # The split-mode pane iframes set their src to /main?bare in JS; rewrite the
        # string literals so the file:// preview points at the generated bare pages.
        mfd = mfd.replace("'/main?bare'", "'main.html?bare'")
        mfd = mfd.replace("'/avn?bare'",  "'avn.html?bare'")
        mfd = mfd.replace("'/tgp?bare'",  "'tgp.html?bare'")
        # The LAN URL block is substituted by the live server; the file:// preview has no
        # server, so substitute a mock URL so the line still renders.
        mfd = mfd.replace(
            "{{LAN_URL_BLOCK}}",
            '<div class="ib-url">http://192.168.1.42:5005</div>',
        )
        mfd = mfd.replace("</head>", injection + mock + "\n</head>", 1)
        OUT.write_text(mfd, encoding="utf-8")
        print(f"Wrote {OUT.relative_to(ROOT)}  ({len(mfd):,} bytes)")

    # MAIN page (split-mode pane content). Pure static — no mock injection needed.
    if MAIN.exists():
        main_html = extract_html(MAIN.read_text(encoding="utf-8"))
        main_html = main_html.replace(
            "{{LAN_URL_BLOCK}}",
            '<div class="ib-url">http://192.168.1.42:5005</div>',
        )
        OUT_MAIN.write_text(main_html, encoding="utf-8")
        print(f"Wrote {OUT_MAIN.relative_to(ROOT)}  ({len(main_html):,} bytes)")

    # AVN bare page (split-mode pane content). Needs the mock injected so the page's
    # /airframe + /airframe-layout fetches resolve against captured assets in preview.
    if AVN.exists():
        avn_html = extract_html(AVN.read_text(encoding="utf-8"))
        avn_html = avn_html.replace("</head>", injection + mock + "\n</head>", 1)
        OUT_AVN.write_text(avn_html, encoding="utf-8")
        print(f"Wrote {OUT_AVN.relative_to(ROOT)}  ({len(avn_html):,} bytes)")

    # TGP bare page (split-mode pane content). The mock intercepts /tgp.mjpg so the
    # in-browser preview shows the NO LOCK placeholder cleanly.
    if TGP.exists():
        tgp_html = extract_html(TGP.read_text(encoding="utf-8"))
        tgp_html = tgp_html.replace("</head>", injection + mock + "\n</head>", 1)
        OUT_TGP.write_text(tgp_html, encoding="utf-8")
        print(f"Wrote {OUT_TGP.relative_to(ROOT)}  ({len(tgp_html):,} bytes)")

    if OLD_OUT_MFD.exists():
        OLD_OUT_MFD.unlink()
        print(f"Removed stale {OLD_OUT_MFD.relative_to(ROOT)}")

    if "--mfd" in sys.argv or "--open" in sys.argv:
        webbrowser.open(OUT.as_uri())


if __name__ == "__main__":
    main()
