#!/usr/bin/env python3
"""Generate a standalone, game-free preview of the MFD.

The live server uses the MFD shell as the index page. This script writes:

    preview/index.html       # MFD shell (extracted from src/MfdPage.cs)
    preview/main.html         # MAIN split-pane card (from src/MainPage.cs)

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
    python tools/build_preview.py            # writes preview/index.html (+ preview/main.html)
    python tools/build_preview.py --open     # ...and opens the MFD in your browser
    python tools/build_preview.py --mfd      # alias for --open

Re-run after editing MfdPage.cs / MainPage.cs (or the mock, or re-capturing) to refresh.
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
MFD = ROOT / "src" / "MfdPage.cs"
MAIN = ROOT / "src" / "MainPage.cs"
# MAP migrated to web/pages/map/ and WPN + TGL + TGP + AVN + RWR to web/pages/{wpn,tgl,tgp,avn,rwr}/
# (http-served via /assets + the /map-view route); no longer C# const blobs. The file:// preview
# can't load http-served pages (todo/src-architecture.md); use tools/serve_web.py to drive the
# shell + all pages over http (it injects the mock into the map page itself).
MOCK = ROOT / "tools" / "preview-mock.js"
MANIFEST = ROOT / "preview" / "assets" / "manifest.json"
OUT = ROOT / "preview" / "index.html"
OUT_MAIN = ROOT / "preview" / "main.html"
OLD_OUT_MFD = ROOT / "preview" / "mfd.html"
OLD_OUT_MAP = ROOT / "preview" / "map-view.html"

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
    mock = MOCK.read_text(encoding="utf-8").strip()
    injection = capture_injection()
    OUT.parent.mkdir(parents=True, exist_ok=True)

    source = "captured game assets" if injection else "synthetic mock (run capture_assets.py while in-game for real assets)"
    print(f"Data source: {source}")

    # Real URLs for the MAIN info-box, so the preview shows the actual addresses you'd use in
    # dev (and the LAN one works from a tablet on the same Wi-Fi) instead of a baked-in mock.
    lan_ip = detect_lan_ip()
    localhost_url = f"http://localhost:{PREVIEW_PORT}"
    lan_url = f"http://{lan_ip}:{PREVIEW_PORT}" if lan_ip else ""
    print(f"Info-box URLs: {localhost_url}" + (f"  +  {lan_url}" if lan_url else "  (no LAN IP detected)"))

    # MFD index page: its central screen embeds /map-view?bare (always) and, in split
    # mode, /main?bare for the pane iframes. The map is now http-served (serve_web's
    # /map-view route, which injects the mock), so /map-view?bare is left ABSOLUTE — it
    # only resolves over the serve_web harness, not file://. /main is still a generated
    # bare preview, so its JS src literal is rewritten to the local file. The mock is also
    # injected here so any direct shell fetches resolve.
    if MFD.exists():
        mfd = extract_html(MFD.read_text(encoding="utf-8"))
        # The split-mode pane iframes set their src to /main?bare in JS; rewrite that
        # literal so the preview points at the generated bare MAIN page (still a blob).
        mfd = mfd.replace("'/main?bare'", "'main.html?bare'")
        # '/map-view?bare' (base map iframe + split panes) and '/wpn?bare' etc. intentionally
        # NOT rewritten — MAP/WPN/TGL/TGP/AVN/RWR are http-served from web/ now, so they only
        # resolve over tools/serve_web.py (not the file:// MFD preview). See todo/src-architecture.md.
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

    # (MAP + WPN + TGL + TGP + AVN + RWR bare pages removed — migrated to web/pages/, http-served,
    #  so they are no longer extracted into the file:// preview. Use tools/serve_web.py over http.)

    if OLD_OUT_MFD.exists():
        OLD_OUT_MFD.unlink()
        print(f"Removed stale {OLD_OUT_MFD.relative_to(ROOT)}")
    if OLD_OUT_MAP.exists():
        OLD_OUT_MAP.unlink()
        print(f"Removed stale {OLD_OUT_MAP.relative_to(ROOT)}")

    if "--mfd" in sys.argv or "--open" in sys.argv:
        webbrowser.open(OUT.as_uri())


if __name__ == "__main__":
    main()
