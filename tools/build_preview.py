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
import socket
import sys
import webbrowser

ROOT = pathlib.Path(__file__).resolve().parent.parent

# Port the preview is served on (tools/serve_preview.py / launch.json default). The live
# server uses 5005, but in dev mode the pages are served from here, so the info-box URLs
# are rewritten to this port so they actually resolve in the browser / from a tablet.
PREVIEW_PORT = 8777
CLIENT = ROOT / "src" / "ClientPage.cs"
MFD = ROOT / "src" / "MfdPage.cs"
MAIN = ROOT / "src" / "MainPage.cs"
AVN = ROOT / "src" / "AvnPage.cs"
TGP = ROOT / "src" / "TgpPage.cs"
WPN = ROOT / "src" / "WpnPage.cs"
TGL = ROOT / "src" / "TglPage.cs"
MOCK = ROOT / "tools" / "preview-mock.js"
MANIFEST = ROOT / "preview" / "assets" / "manifest.json"
OUT = ROOT / "preview" / "index.html"
OUT_MAP = ROOT / "preview" / "map-view.html"
OUT_MAIN = ROOT / "preview" / "main.html"
OUT_AVN = ROOT / "preview" / "avn.html"
OUT_TGP = ROOT / "preview" / "tgp.html"
OUT_WPN = ROOT / "preview" / "wpn.html"
OUT_TGL = ROOT / "preview" / "tgl.html"
OLD_OUT_MFD = ROOT / "preview" / "mfd.html"

DELIM = '"""'


def detect_lan_ip() -> str:
    """Resolve the LAN IPv4 this machine would use to reach the network — the same address
    a tablet on the same Wi-Fi sees. Mirrors the server's DetectLanIp: the UDP "connect"
    sends no packets, it just picks the outbound interface from the routing table. Returns
    "" when offline or on a loopback-only setup."""
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.connect(("8.8.8.8", 65530))
            ip = sock.getsockname()[0]
        return "" if not ip or ip.startswith("127.") or ip.startswith("0.") else ip
    except OSError:
        return ""


def fill_preview_urls(html: str, localhost_url: str, lan_url: str) -> str:
    """Rewrite the MAIN info-box URLs for the preview: the hardcoded localhost:5005 line and
    the {{LAN_URL_BLOCK}} placeholder the live server fills. Uses the real detected LAN IP +
    preview port so the LAN line actually works from another device; drops the LAN line when
    no LAN IP is available (matching the server's empty-LanUrl behaviour)."""
    html = html.replace("http://localhost:5005", localhost_url)
    lan_block = f'<div class="ib-url">{lan_url}</div>' if lan_url else ""
    return html.replace("{{LAN_URL_BLOCK}}", lan_block)


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

    # Real URLs for the MAIN info-box, so the preview shows the actual addresses you'd use in
    # dev (and the LAN one works from a tablet on the same Wi-Fi) instead of a baked-in mock.
    lan_ip = detect_lan_ip()
    localhost_url = f"http://localhost:{PREVIEW_PORT}"
    lan_url = f"http://{lan_ip}:{PREVIEW_PORT}" if lan_ip else ""
    print(f"Info-box URLs: {localhost_url}" + (f"  +  {lan_url}" if lan_url else "  (no LAN IP detected)"))

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
        mfd = mfd.replace("'/wpn?bare'",  "'wpn.html?bare'")
        mfd = mfd.replace("'/tgl?bare'",  "'tgl.html?bare'")
        # The localhost line + LAN URL block are filled by the live server in-game; for the
        # preview, point them at the real detected LAN IP on the preview port.
        mfd = fill_preview_urls(mfd, localhost_url, lan_url)
        mfd = mfd.replace("</head>", injection + mock + "\n</head>", 1)
        OUT.write_text(mfd, encoding="utf-8")
        print(f"Wrote {OUT.relative_to(ROOT)}  ({len(mfd):,} bytes)")

    # MAIN page (split-mode pane content). Pure static — no mock injection needed.
    if MAIN.exists():
        main_html = extract_html(MAIN.read_text(encoding="utf-8"))
        main_html = fill_preview_urls(main_html, localhost_url, lan_url)
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

    # WPN bare page (split-mode pane content). Needs the mock injected so the page's
    # /weapon selected-icon fetch resolves against captured assets in preview.
    if WPN.exists():
        wpn_html = extract_html(WPN.read_text(encoding="utf-8"))
        wpn_html = wpn_html.replace("</head>", injection + mock + "\n</head>", 1)
        OUT_WPN.write_text(wpn_html, encoding="utf-8")
        print(f"Wrote {OUT_WPN.relative_to(ROOT)}  ({len(wpn_html):,} bytes)")

    # TGL bare page (split-mode pane content). Pure reactive renderer driven by shell
    # postMessage — no fetches — so no mock injection is needed (like the MAIN pane).
    if TGL.exists():
        tgl_html = extract_html(TGL.read_text(encoding="utf-8"))
        OUT_TGL.write_text(tgl_html, encoding="utf-8")
        print(f"Wrote {OUT_TGL.relative_to(ROOT)}  ({len(tgl_html):,} bytes)")

    if OLD_OUT_MFD.exists():
        OLD_OUT_MFD.unlink()
        print(f"Removed stale {OLD_OUT_MFD.relative_to(ROOT)}")

    if "--mfd" in sys.argv or "--open" in sys.argv:
        webbrowser.open(OUT.as_uri())


if __name__ == "__main__":
    main()
