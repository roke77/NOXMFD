namespace NORoksMFD
{
    // A hardware-style Multi-Function Display: a rugged bezel with clickable buttons
    // on all four sides, wrapping the existing map (served at /map-view?bare) in
    // the central screen. Served at /. The bezel is hardware-gray; the screen inside keeps
    // the green HUD theme because it's the existing page in an iframe.
    internal static class MfdPage
    {
        internal static readonly string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>NO Roks MFD</title>
<link rel="icon" type="image/svg+xml" href="data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'><rect x='1' y='1' width='30' height='30' rx='4' fill='%233b3f45'/><rect x='6' y='6' width='20' height='20' rx='1' fill='%23050a05'/><g fill='%23c8ccd0'><rect x='2.5' y='8' width='2' height='2.5'/><rect x='2.5' y='14.75' width='2' height='2.5'/><rect x='2.5' y='21.5' width='2' height='2.5'/><rect x='27.5' y='8' width='2' height='2.5'/><rect x='27.5' y='14.75' width='2' height='2.5'/><rect x='27.5' y='21.5' width='2' height='2.5'/></g><path d='M11 16h10' stroke='%2339ff14' stroke-width='2' stroke-linecap='square'/></svg>">
<style>
  /* Self-hosted Share Tech Mono (embedded woff2) so the MFD needs no internet. */
  @font-face {
    font-family: 'Share Tech Mono';
    font-style: normal;
    font-weight: 400;
    font-display: swap;
    src: url(data:font/woff2;base64,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) format('woff2');
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body { height: 100%; }
  body {
    background: #0d0f11;
    font-family: 'Courier New', monospace;
    overflow: hidden;
  }

  .mfd { position: fixed; inset: 0; display: flex; padding: 0; }

  /* Bezel: top strip / [left · screen · right] / bottom strip */
  .bezel {
    position: relative;
    flex: 1;
    display: grid;
    grid-template-rows: auto 1fr auto;
    gap: 10px;
    padding: 10px;
    border-radius: 14px;
    background: linear-gradient(160deg, #3b3f45, #26282c);
    box-shadow: inset 0 1px 0 #5a5f66, inset 0 -2px 8px #15161a, 0 6px 22px rgba(0,0,0,0.55);
  }

  /* Strips share the screen's 3-column grid: side-key-width gutter / screen / side-key-width
     gutter. The generated top/bottom key banks live in the centre cell so they align with
     the screen, while standalone controls can sit in the side gutter. */
  .strip { display: grid; grid-template-columns: 36px minmax(0, 1fr) 36px; gap: 10px; align-items: center; }
  .strip .center { display: flex; gap: 6px; min-width: 0; }
  .strip .center { grid-column: 2; }
  .strip-action.right { grid-column: 3; justify-self: center; }
  .mid   { display: grid; grid-template-columns: auto 1fr auto; gap: 10px; min-height: 0; }

  .keys   { display: flex; gap: 6px; }
  /* Vertical column: ridges + keys spread top-to-bottom; 6px inset matches the screen's
     padding so the first/last ridge line up with the map (iframe) top/bottom edges. */
  .keys.v { flex-direction: column; justify-content: space-between; gap: 0; padding: 6px 0; }
  .keys.v .key { flex: 0 0 auto; width: 36px; height: 46px; }   /* generic line-select keys */
  /* Horizontal row: same separator/key/separator pattern, rotated to spread across
     the screen width. */
  .keys.h { flex: 1 1 auto; justify-content: space-between; gap: 0; padding: 0 6px; min-width: 0; }
  .keys.h .key { flex: 0 0 auto; width: 46px; height: 36px; }
  .keys.h .key.icon { width: 46px; height: 36px; }

  /* White horizontal line marking inside each generic (side) key */
  .keys.v .key::before {
    content: '';
    width: 16px; height: 2px;
    background: #e8eaed;
    box-shadow: 0 0 2px rgba(255,255,255,0.35);
    border-radius: 1px;
  }
  /* White vertical line marking inside each generic top/bottom key. Icon keys suppress it. */
  .keys.h .key::before {
    content: '';
    width: 2px; height: 16px;
    background: #e8eaed;
    box-shadow: 0 0 2px rgba(255,255,255,0.35);
    border-radius: 1px;
  }
  .keys.h .key.icon::before { display: none; }

  /* Engraved separator ridge between side keys (visual only, not clickable) */
  .keys.v .sep { display: flex; align-items: center; }
  .keys.v .sep::before {
    content: '';
    width: 100%; height: 2px;
    background: #16181b;
    box-shadow: 0 1px 0 rgba(255,255,255,0.06), 0 -1px 0 rgba(0,0,0,0.45);
    border-radius: 1px;
  }
  .keys.h .sep { display: flex; align-items: center; }
  .keys.h .sep::before {
    content: '';
    width: 2px; height: 100%;
    background: #16181b;
    box-shadow: 1px 0 0 rgba(255,255,255,0.06), -1px 0 0 rgba(0,0,0,0.45);
    border-radius: 1px;
  }

  /* Beveled gunmetal keys */
  .key {
    appearance: none;
    display: flex;
    align-items: center;
    justify-content: center;
    border: 1px solid #202225;
    border-radius: 4px;
    background: linear-gradient(#4b4f56, #313438);
    box-shadow: inset 0 1px 0 #62666d, inset 0 -2px 3px rgba(0,0,0,0.4);
    cursor: pointer;
    color: #c8ccd0;
    font-family: inherit;
    font-size: 14px;
    line-height: 1;
    padding: 0;
    user-select: none;
  }
  .key:hover { background: linear-gradient(#565b63, #393c42); }
  /* Pressed / briefly "lit" — glows HUD-green to tie into the screen theme */
  .key:active, .key.lit {
    background: linear-gradient(#2a2c30, #3a3e44);
    box-shadow: inset 0 2px 5px rgba(0,0,0,0.6), 0 0 7px #39ff14;
    border-color: #39ff14;
    color: #39ff14;
  }
  .key.icon { width: 36px; height: 30px; }
  .key.sun  { color: #ffffff; }

  /* Plain square outline */
  .ic-square {
    width: 14px; height: 14px;
    border: 1px solid currentColor;
    border-radius: 1px;
  }
  /* 2x1 icon: square split top/bottom (two stacked rows) */
  .ic-2x1 {
    position: relative;
    width: 14px; height: 14px;
    border: 1px solid currentColor;
    border-radius: 1px;
  }
  .ic-2x1::before {
    content: '';
    position: absolute;
    left: 0; right: 0;
    top: 50%;
    height: 1px;
    background: currentColor;
  }
  /* 1x2 icon: square split left/right (two side-by-side columns) */
  .ic-1x2 {
    position: relative;
    width: 14px; height: 14px;
    border: 1px solid currentColor;
    border-radius: 1px;
  }
  .ic-1x2::before {
    content: '';
    position: absolute;
    top: 0; bottom: 0;
    left: 50%;
    width: 1px;
    background: currentColor;
  }
  /* Fullscreen icon: four corner brackets pointing outward (inline SVG) */
  .ic-fullscreen {
    display: inline-block;
    width: 14px; height: 14px;
    background-color: currentColor;
    -webkit-mask: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 14 14'><path fill='none' stroke='black' stroke-width='1.6' stroke-linecap='square' d='M1 5V1H5 M9 1H13V5 M13 9V13H9 M5 13H1V9'/></svg>") center/contain no-repeat;
            mask: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 14 14'><path fill='none' stroke='black' stroke-width='1.6' stroke-linecap='square' d='M1 5V1H5 M9 1H13V5 M13 9V13H9 M5 13H1V9'/></svg>") center/contain no-repeat;
  }
  .ic-pin {
    display: inline-block;
    width: 14px; height: 14px;
    background-color: currentColor;
    -webkit-mask: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'><path fill='none' stroke='black' stroke-width='1.8' stroke-linejoin='miter' d='M12 2.8 19.2 10 12 17.2 4.8 10Z'/><path fill='none' stroke='black' stroke-width='1.8' stroke-linecap='square' d='M12 17.2V22.4'/></svg>") center/contain no-repeat;
            mask: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'><path fill='none' stroke='black' stroke-width='1.8' stroke-linejoin='miter' d='M12 2.8 19.2 10 12 17.2 4.8 10Z'/><path fill='none' stroke='black' stroke-width='1.8' stroke-linecap='square' d='M12 17.2V22.4'/></svg>") center/contain no-repeat;
  }
  .ic-swap {
    display: inline-block;
    width: 14px; height: 14px;
    background-color: currentColor;
    -webkit-mask: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 14 14'><path fill='none' stroke='black' stroke-width='1.5' stroke-linecap='square' stroke-linejoin='round' d='M3 4H10 M8 2L10 4L8 6 M11 10H4 M6 8L4 10L6 12'/></svg>") center/contain no-repeat;
            mask: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 14 14'><path fill='none' stroke='black' stroke-width='1.5' stroke-linecap='square' stroke-linejoin='round' d='M3 4H10 M8 2L10 4L8 6 M11 10H4 M6 8L4 10L6 12'/></svg>") center/contain no-repeat;
  }
  /* Wide layout icon: box split into a wide left pane and a narrow right pane */
  .ic-split {
    position: relative;
    width: 18px; height: 12px;
    border: 1px solid currentColor;
    border-radius: 1px;
  }
  .ic-split::before {
    content: '';
    position: absolute;
    top: 0; bottom: 0;
    left: 66%;
    width: 1px;
    background: currentColor;
  }
  /* Left/right 2/3 icon: square split into a wide left pane and a narrow right pane */
  .ic-lr23 {
    position: relative;
    width: 14px; height: 14px;
    border: 1px solid currentColor;
    border-radius: 1px;
  }
  .ic-lr23::before {
    content: '';
    position: absolute;
    top: 0; bottom: 0;
    left: 66%;
    width: 1px;
    background: currentColor;
  }
  /* Power icon: broken ring + top stroke (IEC standby symbol) */
  .ic-power {
    display: inline-block;
    width: 14px; height: 14px;
    background-color: currentColor;
    -webkit-mask: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'><path fill='none' stroke='black' stroke-width='2.2' stroke-linecap='round' stroke-linejoin='round' d='M6 7a9 9 0 1 0 12 0 M12 3v9'/></svg>") center/contain no-repeat;
            mask: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'><path fill='none' stroke='black' stroke-width='2.2' stroke-linecap='round' stroke-linejoin='round' d='M6 7a9 9 0 1 0 12 0 M12 3v9'/></svg>") center/contain no-repeat;
  }

  /* Inset screen recess holding the map iframe */
  .screen {
    position: relative;
    border-radius: 6px;
    background: #05080a;
    padding: 6px;
    box-shadow: inset 0 0 0 1px #000, inset 0 0 14px rgba(0,0,0,0.85);
    min-width: 0;
    min-height: 0;
  }
  .screen iframe {
    width: 100%;
    height: 100%;
    border: 0;
    display: block;
    border-radius: 3px;
    background: #060a06;
  }

  /* Split layout: when .screen.split is on, the single map iframe + overlay (the
     normal single-pane stack) are hidden and we render two stacked iframes with a
     3px white divider between them. Each pane is its own iframe per the Strategy A
     plan in todo/mfd-split-screen.md — pages live inside iframes; the shell still
     owns bezel labels and click dispatch. */
  .split-container {
    position: absolute;
    inset: 6px;
    border-radius: 3px;
    overflow: hidden;
    display: none;
    flex-direction: column;
  }
  .screen.split > iframe[title="map"] { display: none; }
  .screen.split > .split-container { display: flex; }
  /* In split mode the overlay stays visible — it's where the per-pane bezel labels
     paint on top of the pane iframes. Its OPAQUE background and per-page content
     panels (info-box, wpn-panel, …) must NOT show, though; the panes own that
     content now. We disable the opaque background regardless of class, and any
     direct content child is force-hidden. */
  .screen.split > .overlay { background: transparent !important; }
  .screen.split > .overlay > .info-box,
  .screen.split > .overlay > .wpn-panel,
  .screen.split > .overlay > .tgp-panel,
  .screen.split > .overlay > .tgl-panel,
  .screen.split > .overlay > .avn-panel { display: none !important; }
  .split-pane {
    flex: 1 1 50%;
    min-height: 0;
    width: 100%;
    border: 0;
    display: block;
    background: #000;
  }
  .split-divider {
    flex: 0 0 2px;
    width: 100%;
    background: #ffffff;
  }

  /* Per-page line-select overlay inside the screen. Item labels are positioned by JS to
     line up with their assigned bezel keys. Transparent on the MAP page (overlays the map, which
     stays interactive), opaque black on the MAIN page (covers the map). pointer-events:none
     because the physical bezel keys are the controls — the labels are purely visual. */
  .overlay {
    position: absolute;
    inset: 6px;
    border-radius: 3px;
    pointer-events: none;
  }
  .overlay.opaque { background: #000; }

  /* Top-right indicator stack (PINNED, FOLLOW…). flex-direction:row-reverse means the
     first DOM child sits at the right edge and subsequent children stack to its left —
     so renderIndicators() appends chips in activation order and the second activated
     ends up to the LEFT of the first. */
  .mfd-indicators {
    position: absolute;
    top: 10px; right: 12px;
    display: flex;
    flex-direction: row-reverse;
    gap: 6px;
    pointer-events: none;
    z-index: 2;
  }
  .mfd-indicator {
    background: rgba(6,10,6,0.78);
    border: 1px solid #ffaa00;
    padding: 5px 9px;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    font-size: 11px;
    letter-spacing: 1px;
    color: #ffaa00;
    user-select: none;
  }
  .overlay-item {
    position: absolute;
    color: #d4d8dc;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    font-size: 21px;
    font-weight: 900;
    letter-spacing: 2px;
    white-space: nowrap;
  }
  /* Bank-specific label anchors; JS supplies the cross-axis coordinate. */
  .overlay-item.left  { left: 16px; transform: translateY(-50%); }
  .overlay-item.right { right: 16px; transform: translateY(-50%); }
  .overlay-item.top { transform: translate(-50%, 0); }
  .overlay-item.bottom { transform: translate(-50%, -100%); }
  /* Keep the reduced menu-item size in portrait too. Earlier this scaled the label up with
     viewport height (clamp(21px, 2.4vh, 34px)), but on a tall portrait screen that grew the
     text well past the 21px landscape size — so the 2/3 reduction only looked applied in
     landscape. Pin portrait to the same 21px so menu items read identically in both
     orientations. */
  body.portrait .overlay-item { font-size: 21px; }

  /* MAIN page "about" card — name + URL + live connection status. Hidden on MAP page. */
  .info-box {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    display: none;
    min-width: 280px;
    /* Sized off vmax (the longer viewport edge) so the card stays the same physical size
       whether the device is portrait or landscape — it no longer resizes on rotation. */
    padding: clamp(22px, 2.4vmax, 35px) clamp(36px, 4vmax, 58px);
    border: 1px solid #39ff14;
    background: rgba(6, 10, 6, 0.9);
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    text-align: center;
    letter-spacing: 2px;
    box-shadow: 0 0 12px rgba(57, 255, 20, 0.25);
  }
  .info-box.show       { display: block; }
  .info-box .ib-title  { font-size: clamp(28px, 3.12vmax, 45px); font-weight: 900; margin-bottom: clamp(14px, 1.6vmax, 22px); }
  .info-box .ib-url    { font-size: clamp(14px, 1.6vmax, 22px); color: #4aaa4a; margin-bottom: clamp(14px, 1.6vmax, 22px); }
  .info-box .ib-status { font-size: clamp(14px, 1.6vmax, 22px); font-weight: bold; }
  .info-box .ib-status.connected    { color: #39ff14; }
  .info-box .ib-status.disconnected { color: #ff4040; }
  .info-box .ib-status.waiting      { color: #ffaa00; }

  /* WPN page — stacks the player's loadout one weapon per line-select key (keys 1..N;
     key 0 is the MAIN back button). Each row is positioned + sized to fit the slot
     between the two separator ridges flanking its key, so the icon fills the maximum
     vertical space after the name + ammo lines. The countermeasures panel sits at the
     top centre, aligned vertically with key[0]'s slot. */
  .wpn-panel {
    position: absolute;
    inset: 0;
    display: none;
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
  }
  .wpn-panel.show { display: block; }

  /* TGP page — fills the screen with the live MJPEG feed from the player's targeting cam.
     The empty placeholder mirrors the .wpn-empty style so it reads the same as the WPN
     page's NO LOADOUT state. */
  /* Centred 3:2 box sized to the source (TargetCam renders 360×240). Shrinking the panel
     drops the upscale ratio, which cuts the bilinear blur from #1. The surrounding screen
     stays black because the parent .overlay is opaque on this page. */
  .tgp-panel {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    width: 100%;
    aspect-ratio: 3 / 2;
    max-width: 100%;
    max-height: 100%;
    display: none;
    background: #000;
  }
  .tgp-panel.show { display: block; }
  .tgp-img {
    display: block;
    width: 100%;
    height: 100%;
    object-fit: contain;
    /* Browser-default bilinear upscale — the source is 360×240 native, so without smoothing
       the blocky nearest-neighbour pixels are obvious at MFD size. */
    image-rendering: auto;
  }
  /* Hide the <img> when the feed is dead — MJPEG keeps the last frame buffered in the
     element, so without this the player would see a frozen stale picture instead of the
     NO TARGET placeholder. */
  .tgp-panel:not(.has-feed) .tgp-img { visibility: hidden; }
  .tgp-empty {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    color: #1a4a1a;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    font-size: 22px;
    letter-spacing: 3px;
    pointer-events: none;
  }
  .tgp-panel.has-feed .tgp-empty { display: none; }

  /* AVN page — avionics. Aircraft name pinned to key[0]'s row at the top centre; the
     damage silhouette fills the rest. The silhouette is composed of:
       (a) .avn-bg  — a transparent PNG of the white aircraft outline from the cockpit's
                      StatusDisplay (served at /airframe?type=...&part=__bg)
       (b) .avn-part — one per UnitPart with a UI segment. Each is a CSS-masked div tinted
                      by the game's exact damage formula (see renderAvnParts). The mask
                      image is the part's solid silhouette PNG so transparency = no draw. */
  .avn-panel {
    position: absolute;
    inset: 0;
    display: none;
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
  }
  .avn-panel.show { display: block; }
  .avn-name {
    position: absolute;
    left: 50%;
    transform: translate(-50%, -50%);
    color: #39ff14;
    font-size: 32px;
    font-weight: 900;
    letter-spacing: 2px;
    white-space: nowrap;
    z-index: 2;
  }
  body.portrait .avn-name { font-size: clamp(32px, 3.6vh, 51px); }
  /* Silhouette frame: positioned by JS to sit just under the name (top edge = sep[1])
     and stretch down to the bottom strip (bottom edge = last sep). object-fit: contain
     preserves aspect ratio inside whatever frame we give it; child parts are positioned
     against the .avn-frame so their normalized layout (cx,cy,w,h) is unaffected by the
     letterboxing the contain produces — they overlap the same pixels as the bg. */
  .avn-frame {
    position: absolute;
    left: 0; right: 0;
    overflow: hidden;
  }
  .avn-bg, .avn-parts {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    pointer-events: none;
  }
  /* The bg is sized to fit (contain) the frame; .avn-parts is sized to MATCH the bg's
     rendered box so per-part placement still aligns when letterboxing kicks in. */
  .avn-bg     { max-width: 100%; max-height: 100%; display: block; }
  .avn-parts  { display: block; }
  /* One per UnitPart segment. The CSS mask is the per-part silhouette PNG; the background
     colour is what we tint it with — set per-render so we can match the game's formula.
     The source PNGs come straight from the game's UI Image sprites: a white silhouette on
     an opaque dark background. There's no alpha channel to mask against, so we use
     luminance mode — white pixels reveal the tint, dark pixels stay hidden. That keeps
     the parts shaped like their actual airframe segment instead of square boxes. */
  .avn-part {
    position: absolute;
    transform: translate(-50%, -50%);
    background-color: #ffffff;            /* overridden inline per part */
    -webkit-mask-repeat: no-repeat;
            mask-repeat: no-repeat;
    -webkit-mask-size: 100% 100%;
            mask-size: 100% 100%;
    -webkit-mask-position: center;
            mask-position: center;
    -webkit-mask-source-type: luminance;  /* Safari / older Chromium */
            mask-mode: luminance;          /* Standard */
    pointer-events: none;
  }

  /* Failure-indicator labels (L ENG FIRE, R ENG FIRE, FUEL LOW…). One per entry in the
     captured layout's `failures` array, positioned by cx/cy against the silhouette frame.
     The DOM nodes are always there; visibility flips per snapshot based on which messages
     are in avnData.failures (i.e. the matching GameObject is activeSelf in the cockpit). */
  .avn-failure {
    position: absolute;
    transform: translate(-50%, -50%);
    display: none;
    color: #ff4040;
    font-weight: 900;
    letter-spacing: 1px;
    white-space: nowrap;
    text-align: center;
    pointer-events: none;
    text-shadow: 0 0 4px rgba(255, 64, 64, 0.5);
  }
  .avn-failure.active { display: block; }

  /* AVN side bars — FUEL (left) and THROTTLE (right). Live outside .avn-frame so they
     can sit in the panel's horizontal letterbox space without being clipped by the
     frame's overflow:hidden. JS (layoutAvnBars) positions them flush against the
     silhouette's measured edges and matches their height to the silhouette's height,
     so they always read as "tied to" the aircraft visual rather than the full screen. */
  .avn-vbar {
    position: absolute;
    display: none;        /* shown only once layoutAvnBars has placed it */
    flex-direction: column;
    align-items: center;
    width: 42px;
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    text-shadow: 0 0 4px rgba(57, 255, 20, 0.35);
    pointer-events: none;
    z-index: 3;
  }
  .avn-vbar.placed { display: flex; }
  /* Label + value share the same scale — half the aircraft-name text so both read at a
     glance from across the cockpit. Mirrors the name's portrait clamp. The tube-facing
     edge of each gets 20px of breathing room so the text isn't crowded against the
     border. */
  .avn-vbar-label,
  .avn-vbar-value {
    font-size: 16px;
    font-weight: 900;
    letter-spacing: 2px;
    line-height: 1;
    white-space: nowrap;
  }
  .avn-vbar-value { padding: 0 0 20px 0; }
  .avn-vbar-label { padding: 20px 0 0 0; }
  body.portrait .avn-vbar-label,
  body.portrait .avn-vbar-value { font-size: clamp(16px, 1.8vh, 25.5px); }
  /* Tube: thin bordered column, fill grows from the bottom. Black background gives the fill
     a strong silhouette; the inner 1px box-shadow adds the recessed-instrument look. */
  /* Tube is a 3-sided box: top + bottom + the outside edge facing away from the silhouette
     are drawn; the inside edge (facing the silhouette) is left open. Mirrors the in-game
     cockpit fuel-bar style where the container hugs the gauge label and opens toward the
     cockpit centre. .fuel opens to the right, .thr opens to the left — assignments live
     in the per-side blocks below. */
  /* Tube is a 3-sided box: top + bottom + the outside edge facing away from the silhouette
     are drawn; the inside edge (facing the silhouette) is left open. Mirrors the in-game
     cockpit fuel-bar style where the container hugs the gauge label and opens toward the
     cockpit centre. .fuel opens to the right, .thr opens to the left — assignments live
     in the per-side blocks below. */
  .avn-vbar-tube {
    position: relative;
    flex: 1 1 auto;
    width: 28px;
    background: #050a05;
    box-sizing: border-box;
    padding: 6px;        /* insets the fill so segments don't touch the borders */
    overflow: hidden;
  }
  .avn-vbar.fuel .avn-vbar-tube {
    border: 2px solid #39ff14;
    border-right: none;
  }
  .avn-vbar.thr .avn-vbar-tube {
    border: 2px solid #39ff14;
    border-left: none;
  }
  /* Throttle reads as a continuous analogue gauge — no segmented tile pattern and no
     midpoint marker. The fill grows as one solid green column inside the inset region. */
  .avn-vbar.thr .avn-vbar-tube::after,
  .avn-vbar.thr .avn-vbar-tube::before { content: none; }
  /* Solid fill base, inset 6px from each border to match the .avn-vbar-tube padding so
     the green segments float inside the container with generous breathing room. Height
     % is set inline by paintAvnBar(). The segmented look comes from the divider overlay
     below — keeping the fill solid lets the dividers stay aligned to the tube regardless
     of how full it is. */
  .avn-vbar-fill {
    position: absolute;
    left: 6px; right: 6px; bottom: 6px;
    height: 0%;
    background: #39ff14;
    transition: height 200ms linear, background-color 150ms linear;
  }
  /* Horizontal dividers every ~10% of the tube height, 6px thick so each green segment
     reads as a distinct tile with clear separation from its neighbours. The overlay
     covers the tube's full padding box (inset:0) so the dividers stay anchored to the
     tube even though the fill is inset. */
  .avn-vbar-tube::after {
    content: '';
    position: absolute;
    inset: 0;
    background-image: repeating-linear-gradient(
      to top,
      transparent 0,
      transparent calc(10% - 6px),
      #050a05 calc(10% - 6px),
      #050a05 10%
    );
    pointer-events: none;
  }
  /* Brighter horizontal line marking the midpoint. The repeating gradient direction is
     "to top", so each segment gap sits ABOVE its 10% mark from the bottom — equivalent
     to BELOW the corresponding mark in CSS top-coords. The gap that brackets the 50%
     midpoint therefore spans top:50% → top:50% + 6px. We centre the bright line inside
     that gap rather than at exactly 50%, so it doesn't visually touch the segment
     immediately above. Thin (1.5px) to leave clean breathing space within the 6px gap. */
  .avn-vbar-tube::before {
    content: '';
    position: absolute;
    left: 0; right: 0;
    top: 50%;
    height: 1.5px;
    margin-top: 2.25px;      /* gap centre at top:50%+3px, line centred there */
    background: #39ff14;
    z-index: 2;
    pointer-events: none;
  }
  /* Side tick marks at 0/25/50/75/100% — small green notches outside the tube on the
     bar's outer side, drawn via gradients on the tube's left/right wrapper. */
  .avn-vbar-ticks {
    position: absolute;
    top: 0; bottom: 0;
    width: 5px;
    pointer-events: none;
  }
  .avn-vbar.fuel .avn-vbar-ticks { right: 100%; margin-right: 2px; }
  .avn-vbar.thr  .avn-vbar-ticks { left:  100%; margin-left:  2px; }
  .avn-vbar-ticks::before {
    content: '';
    position: absolute;
    inset: 0;
    background-image:
      linear-gradient(#39ff14, #39ff14),
      linear-gradient(#39ff14, #39ff14),
      linear-gradient(#39ff14, #39ff14),
      linear-gradient(#39ff14, #39ff14),
      linear-gradient(#39ff14, #39ff14);
    background-repeat: no-repeat;
    background-size: 100% 1px;
    background-position:
      0 0%,
      0 25%,
      0 50%,
      0 75%,
      0 100%;
  }
  /* State colours. Caution = amber, critical = red. The dividers (::after) stay dark and
     readable on top of any fill colour because they're drawn black with high alpha. */
  .avn-vbar.caution  .avn-vbar-fill { background: #ffaa00; }
  .avn-vbar.critical .avn-vbar-fill { background: #ff4040; }
  .avn-vbar.caution  { color: #ffaa00; }
  .avn-vbar.critical { color: #ff4040; }
  /* No-data state: dimmed frame + label, empty tube, em-dash value. Mirrors the colour
     scheme used by .avn-empty so dim states read consistently across the AVN page. */
  .avn-vbar.na {
    color: #1a4a1a;
    text-shadow: none;
  }
  .avn-vbar.na .avn-vbar-tube { border-color: #1a4a1a; }
  .avn-vbar.na .avn-vbar-tube::before { background: #1a4a1a; }
  .avn-vbar.na .avn-vbar-ticks::before { background-image:
      linear-gradient(#1a4a1a, #1a4a1a),
      linear-gradient(#1a4a1a, #1a4a1a),
      linear-gradient(#1a4a1a, #1a4a1a),
      linear-gradient(#1a4a1a, #1a4a1a),
      linear-gradient(#1a4a1a, #1a4a1a); }
  .avn-vbar.na .avn-vbar-fill { display: none; }

  /* Shown when no aircraft data is available (no name in the latest snapshot).
     Mirrors .tgl-empty / .tgp-empty / .wpn-empty so all "no data" placeholders
     read identically across MFD pages. */
  .avn-empty {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    color: #1a4a1a;
    font-size: 22px;
    letter-spacing: 3px;
    text-align: center;
    pointer-events: none;
  }

  /* TGL page — target list. Rows are positioned over the left & right key columns by JS. */
  .tgl-panel {
    position: absolute;
    inset: 0;
    display: none;
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
  }
  .tgl-panel.show { display: block; }
  .tgl-empty {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    color: #1a4a1a;
    font-size: 22px;
    letter-spacing: 3px;
    pointer-events: none;
  }
  .tgl-panel.has-targets .tgl-empty { display: none; }
  .tg-item {
    position: absolute;
    padding: 2px 6px;
    line-height: 1.15;
    overflow: hidden;
    pointer-events: none;
    display: flex;
    flex-direction: column;
    justify-content: center;
  }
  .tg-item.left  { left: 0;  text-align: left;  align-items: flex-start; }
  .tg-item.right { right: 0; text-align: right; align-items: flex-end;   }
  /* Per-row faction palette via CSS variables. Default = enemy (red — matches the
     factionColors[2] used on the map). Friendly / neutral classes override; name takes
     the bright tone, GRID/RNG the dim tone. */
  .tg-item              { --tg-name: #ff4040; --tg-meta: #8a2828; }
  .tg-item.f-friendly   { --tg-name: #4da6ff; --tg-meta: #2a5a8a; }
  .tg-item.f-neutral    { --tg-name: #ffffff; --tg-meta: #888888; }
  /* Font sizes are set inline by renderTgl() so the row fills its slot height; the name
     ends up 5/3 the meta size (i.e. "2/3 bigger"). Line-height is tight so the two lines
     reach the slot's top and bottom. */
  .tg-name { font-weight: bold; white-space: nowrap; line-height: 1.0; color: var(--tg-name); }
  /* GRID and RNG are stacked below the name and dimmed to de-emphasise vs the bright name. */
  .tg-grid,
  .tg-rng  { white-space: nowrap; line-height: 1.0; color: var(--tg-meta); }

  .wpn-empty {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    color: #1a4a1a;
    font-size: 22px;
    letter-spacing: 3px;
  }
  /* Weapon row — name on top, ammo below. Constrained to the LEFT HALF of the screen;
     the RIGHT HALF is reserved for the .wp-sel-icon image of the currently-selected
     weapon (see below). Each row is positioned by JS to span its line-select key slot. */
  .wp-item {
    position: absolute;
    left: 90px;                            /* clear the left line-select label gutter */
    right: calc(45% + 10px);               /* name list takes the left 55%; image the right 45% */
    display: flex;
    flex-direction: column;
    justify-content: center;
    padding: 4px 0;
    box-sizing: border-box;
    min-height: 0;
  }
  .wp-name {
    align-self: flex-start;
    max-width: 100%;
    padding: 0 6px;
    margin-left: -6px;
    font-size: 28px; font-weight: 900; letter-spacing: 1px;
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .wp-ammo {
    font-size: 20px; color: #4aaa4a; letter-spacing: 1px; margin-top: 2px;
  }
  .wp-ammo span { color: #39ff14; font-weight: 900; }

  /* Big icon of the currently-selected weapon. Sits in the RIGHT HALF of the screen
     and spans the full vertical area covered by line-select keys 1..5 (sized by JS to
     match the same separator gaps the rows use). Game weapon icons are 2:1 horizontal;
     object-fit:contain lets them grow to the largest size that fits without crop. */
  .wp-sel-icon-wrap {
    position: absolute;
    /* No horizontal margin; occupies the right 45% of the panel (55% goes to the name
       list). (JS still adds a 20px gap top/bottom.) */
    left: 55%;
    right: 0;
    display: none;
    align-items: center;
    justify-content: center;
    pointer-events: none;
    overflow: hidden;
    padding: 10px;            /* inner breathing room so the icon doesn't touch the edges */
    box-sizing: border-box;
    /* Establishes a CSS-size container so the rotated image can size off cqw / cqh. */
    container-type: size;
  }
  .wp-sel-icon-wrap.show { display: flex; }
  /* Source icons are 2:1 horizontal. Rotate 90deg so the long axis runs vertically and
     fills the tall right half; swap the image's logical width/height (via cqh/cqw of the
     wrap) so object-fit:contain has the right "rotated" box to fit into. */
  .wp-sel-icon {
    display: block;
    width:  100cqh;
    height: 100cqw;
    object-fit: contain;
    transform: rotate(90deg);
  }
  /* Landscape: the right pane is wide and short, so show the weapon icon in its native
     horizontal orientation — undo the portrait rotate-to-fit-vertical trick (and the
     cqh/cqw axis swap that paired with it). */
  body.landscape .wp-sel-icon {
    width:  100cqw;
    height: 100cqh;
    transform: none;
  }
  /* Portrait: the screen is tall + narrow, so the weapon names crammed against the 50%
     midline and ellipsised. Keep the 50/50 name/image split, but reclaim the wasted left
     gutter — the 90px offset clears the line-select label column, which the weapon rows
     (keys 1..5) don't actually overlap, so in portrait we pull the names flush-left to
     match the MAIN label's left edge, giving them the full left half. */
  body.portrait .wp-item { left: 18px; }

  /* Countermeasures panel — centred at the top of the WPN page, in key[0]'s slot.
     Two columns (IR Flares | Radar Jammer) separated by a thin green vertical line. */
  .cm-panel {
    position: absolute;
    left: 50%;
    transform: translateX(-50%);
    width: 60%;
    max-width: 520px;
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    display: grid;
    grid-template-columns: 1fr 1px 1fr;
    grid-template-rows: auto auto auto;
    align-content: center;       /* cluster the three rows in the middle of the slot */
    column-gap: 14px;
    row-gap: 4px;
  }
  /* Match the weapon-name font (.wp-name) so the heading line reads at the same weight. */
  .cm-title { font-size: 18px; font-weight: 900; letter-spacing: 1px; white-space: nowrap; }
  .cm-title .cm-label { padding: 0 6px; }
  .cm-flares-title { grid-column: 1; grid-row: 1; text-align: right; }
  .cm-flares-body  {
    grid-column: 1; grid-row: 2 / span 2;
    min-height: 0;
    display: flex;
    align-items: stretch;
    justify-content: flex-end;     /* icon hugs the right edge; the count sits left of it */
    gap: 10px;
  }
  /* IR flare icon: 4×4 grid of hollow circles drawn inline as SVG, so it never depends on
     a server-served image. currentColor lets the empty-state class re-tint to red. */
  .cm-flares-icon  {
    flex: 0 0 auto;
    min-height: 0; min-width: 0;
    height: 100%;
    aspect-ratio: 1 / 1;
    color: #39ff14;
    display: flex;
    align-items: center;
  }
  .cm-flares-icon.empty { color: #ff4040; }
  .cm-flares-svg { display: block; width: 100%; height: 100%; }
  /* Spent flare slot — stays hollow, but the ring goes muted green.
     Reading order (top-left → bottom-right) is "first spent". */
  .cm-flares-svg .flare-dot.spent { stroke: #1a4a1a; }
  .cm-sep          { grid-column: 2; grid-row: 1 / span 3; width: 1px; background: #1a4a1a; }   /* muted green */
  .cm-jammer-title { grid-column: 3; grid-row: 1; }
  /* justify-self: start prevents the cell from stretching — its width is driven by the
     inner .cm-bar, whose width is set in JS to match the kJ readout above. */
  .cm-jammer-bar   { grid-column: 3; grid-row: 3; align-self: center; justify-self: start; }
  /* Big-text readouts (flares count, jammer kJ). The font-size is set by renderCm() so
     the glyphs fill the available cell height. */
  .cm-big {
    min-height: 0; min-width: 0;
    font-weight: 900;
    letter-spacing: 1px;
    color: #39ff14;
    line-height: 1;
    display: flex;
    align-items: center;
    white-space: nowrap;
  }
  #cm-flares-val { flex: 0 0 auto; justify-content: flex-end; }
  /* Jammer readout: hug its own content (number + "kJ") so the capacitor bar below can
     width-match it. gap = fixed px separation between number and unit (so the space
     between "400" and "kJ" stays constant as the font scales). */
  #cm-jammer-val {
    grid-column: 3; grid-row: 2;
    align-self: stretch;
    justify-content: flex-start;
    width: fit-content;
    gap: 8px;
  }

  /* Depleted countermeasure (count === 0 with a positive capacity) — label + value go red. */
  .cm-title.empty .cm-label,
  .cm-big.empty             { color: #ff4040; }

  /* Currently selected — invert the label bar. Depleted + selected uses red as the bar. */
  .cm-title.sel       .cm-label { background: #39ff14; color: #060a06; }
  .cm-title.empty.sel .cm-label { background: #ff4040; color: #060a06; }
  /* Capacitor bar — thin green outline + green fill keyed to ewKJ / ewKJMax. */
  .cm-bar {
    width: 100%;
    height: 12px;
    border: 1px solid #39ff14;
    border-radius: 3px;
    background: rgba(57, 255, 20, 0.08);
    box-sizing: border-box;
    overflow: hidden;          /* clip the fill to the rounded outline */
  }
  .cm-bar-fill {
    width: 0%;
    height: 100%;
    background: #39ff14;
    transition: width 120ms linear;
  }

  /* Depleted ammo (a === 0 && f > 0) — name + ammo go red. */
  .wp-item.empty .wp-name,
  .wp-item.empty .wp-ammo,
  .wp-item.empty .wp-ammo span { color: #ff4040; }

  /* Currently selected — invert the name (text on a solid bar) in the same color as the
     text would otherwise be. Depleted + selected uses red as the bar color. */
  .wp-item.sel .wp-name             { background: #39ff14; color: #060a06; }
  .wp-item.empty.sel .wp-name       { background: #ff4040; color: #060a06; }

  /* Decorative Phillips-head corner screws. The artwork is a photo of a real screw (white
     background knocked out to transparency), embedded as a base64 data URI so it ships with
     the page — no separate asset request, works in-game and in the dev preview alike. This
     just sizes + pins one to each corner; the cross orientation alternates per corner so the
     four don't look machine-stamped identical. pointer-events:none — purely cosmetic. */
  .screw {
    position: absolute;
    width: 30px; height: 30px;
    background: url("data:image/png;base64,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") center / contain no-repeat;
    filter: drop-shadow(0 1px 1.5px rgba(0, 0, 0, 0.55));
    pointer-events: none;
    z-index: 1;
  }
  /* Centered in the ~36px corner square where the top strip and side key column meet
     (28px in from each bezel edge → 13px offset for a 30px screw). */
  .screw.tl { top: 13px; left: 13px;  transform: rotate(-14deg); }
  .screw.tr { top: 13px; right: 13px; transform: rotate(28deg); }
  .screw.bl { bottom: 13px; left: 13px;  transform: rotate(52deg); }
  .screw.br { bottom: 13px; right: 13px; transform: rotate(-37deg); }
</style>
</head>
<body>

<div class="mfd">
  <div class="bezel">
    <!-- Decorative corner screws — background image set in CSS (.screw). -->
    <span class="screw tl"></span><span class="screw tr"></span>
    <span class="screw bl"></span><span class="screw br"></span>

    <div class="strip top">
      <div class="center">
        <div class="keys h" id="keys-top"></div>
      </div>
    </div>

    <div class="mid">
      <div class="keys v" id="keys-left"></div>
      <div class="screen" id="screen">
        <iframe src="/map-view?bare" title="map"></iframe>
        <div class="split-container" id="split-container">
          <iframe class="split-pane" id="pane-top" title="top pane"></iframe>
          <div class="split-divider"></div>
          <iframe class="split-pane" id="pane-bot" title="bottom pane"></iframe>
        </div>
        <div class="overlay" id="overlay">
          <div class="mfd-indicators" id="mfd-indicators"></div>
          <div class="info-box" id="info-box">
            <div class="ib-title">NO ROKS MFD</div>
            <div class="ib-url">http://localhost:5005</div>
            {{LAN_URL_BLOCK}}
            <div class="ib-status disconnected" id="ib-status">&#9679; DISCONNECTED</div>
          </div>
          <div class="wpn-panel" id="wpn-panel">
            <div class="wpn-empty" id="wpn-empty">&mdash; NO LOADOUT &mdash;</div>
            <div class="cm-panel" id="cm-panel">
              <div class="cm-title cm-flares-title" id="cm-flares-title"><span class="cm-label">IR Flares</span></div>
              <div class="cm-flares-body">
                <div class="cm-big" id="cm-flares-val">&mdash;</div>
                <div class="cm-flares-icon" id="cm-flares-icon">
                  <svg class="cm-flares-svg" viewBox="0 0 100 100" preserveAspectRatio="xMidYMid meet" aria-hidden="true">
                    <g fill="none" stroke="currentColor" stroke-width="3">
                      <circle class="flare-dot" cx="12.5" cy="12.5" r="9"/><circle class="flare-dot" cx="37.5" cy="12.5" r="9"/><circle class="flare-dot" cx="62.5" cy="12.5" r="9"/><circle class="flare-dot" cx="87.5" cy="12.5" r="9"/>
                      <circle class="flare-dot" cx="12.5" cy="37.5" r="9"/><circle class="flare-dot" cx="37.5" cy="37.5" r="9"/><circle class="flare-dot" cx="62.5" cy="37.5" r="9"/><circle class="flare-dot" cx="87.5" cy="37.5" r="9"/>
                      <circle class="flare-dot" cx="12.5" cy="62.5" r="9"/><circle class="flare-dot" cx="37.5" cy="62.5" r="9"/><circle class="flare-dot" cx="62.5" cy="62.5" r="9"/><circle class="flare-dot" cx="87.5" cy="62.5" r="9"/>
                      <circle class="flare-dot" cx="12.5" cy="87.5" r="9"/><circle class="flare-dot" cx="37.5" cy="87.5" r="9"/><circle class="flare-dot" cx="62.5" cy="87.5" r="9"/><circle class="flare-dot" cx="87.5" cy="87.5" r="9"/>
                    </g>
                  </svg>
                </div>
              </div>
              <div class="cm-sep"></div>
              <div class="cm-title cm-jammer-title" id="cm-jammer-title"><span class="cm-label">EW Jammer</span></div>
              <div class="cm-jammer-bar"><div class="cm-bar"><div class="cm-bar-fill" id="cm-bar-fill"></div></div></div>
              <div class="cm-big" id="cm-jammer-val">&mdash;</div>
            </div>
          </div>
          <div class="tgp-panel" id="tgp-panel">
            <div class="tgp-empty">&mdash; NO LOCK &mdash;</div>
            <img class="tgp-img" id="tgp-img" alt="">
          </div>
          <div class="tgl-panel" id="tgl-panel">
            <div class="tgl-empty">&mdash; NO TARGETS &mdash;</div>
          </div>
          <div class="avn-panel" id="avn-panel">
            <div class="avn-name" id="avn-name"></div>
            <div class="avn-frame" id="avn-frame">
              <img class="avn-bg" id="avn-bg" alt="">
              <div class="avn-parts" id="avn-parts"></div>
            </div>
            <div class="avn-empty" id="avn-empty">&mdash; NO DATA &mdash;</div>
            <div class="avn-vbar fuel" id="avn-fuel-bar">
              <div class="avn-vbar-value" id="avn-fuel-val">&mdash;</div>
              <div class="avn-vbar-tube">
                <div class="avn-vbar-fill" id="avn-fuel-fill"></div>
                <div class="avn-vbar-ticks"></div>
              </div>
              <div class="avn-vbar-label">FUEL</div>
            </div>
            <div class="avn-vbar thr" id="avn-thr-bar">
              <div class="avn-vbar-value" id="avn-thr-val">&mdash;</div>
              <div class="avn-vbar-tube">
                <div class="avn-vbar-fill" id="avn-thr-fill"></div>
                <div class="avn-vbar-ticks"></div>
              </div>
              <div class="avn-vbar-label">THRL</div>
            </div>
          </div>
        </div>
      </div>
      <div class="keys v" id="keys-right"></div>
    </div>

    <div class="strip bottom">
      <div class="center">
        <div class="keys h" id="keys-bottom"></div>
      </div>
    </div>
  </div>
</div>

<script>
// Generate the line-select keys around the screen (easy to tune).
const COUNTS = { 'keys-left': 6, 'keys-right': 6, 'keys-top': 4, 'keys-bottom': 4 };
function addSep(c) { const s = document.createElement('div'); s.className = 'sep'; c.appendChild(s); }
function addKey(c) { const b = document.createElement('button'); b.className = 'key'; b.type = 'button'; c.appendChild(b); }

// Pattern: ridge, key, ridge, key, … ridge — separators top & bottom so keys sit centered.
for (const id in COUNTS) {
  const container = document.getElementById(id);
  addSep(container);
  for (let i = 0; i < COUNTS[id]; i++) {
    addKey(container);
    addSep(container);
  }
}

const keyBanks = {
  left:   document.querySelectorAll('#keys-left .key'),
  right:  document.querySelectorAll('#keys-right .key'),
  top:    document.querySelectorAll('#keys-top .key'),
  bottom: document.querySelectorAll('#keys-bottom .key'),
};
const leftKeys  = keyBanks.left;    // compatibility aliases for side-specific renderers
const rightKeys = keyBanks.right;
// Fixed-control icon banks. The top row holds page-independent functions; the bottom row
// holds layout controls. Both are wired once at startup and excluded from clearKeyActions,
// so they survive page switches. Entries without an `action` render the icon but do nothing
// yet — placeholders to be wired later (left/right splits, power).
const layoutIcons = [
  { cls: 'ic-square', title: 'Full view',            action: 'unsplit' },
  { cls: 'ic-2x1',    title: 'Split top/bottom',     action: 'split' },
  { cls: 'ic-1x2',    title: 'Split left/right' },
  { cls: 'ic-lr23',   title: 'Split left/right 2/3' },
];
const functionIcons = [
  { cls: 'ic-power',      title: 'Power' },
  { cls: 'ic-fullscreen', title: 'Fullscreen', action: 'fll' },
  { cls: 'ic-swap',       title: 'Swap',       action: 'swap' },
  { cls: 'ic-pin',        title: 'Pin',        action: 'pin' },
];
function applyIconBank(bankName, icons) {
  icons.forEach(function(icon, i) {
    const key = keyBanks[bankName][i];
    if (!key) return;
    key.classList.add('icon');
    key.title = icon.title;
    if (icon.action) key.dataset.action = icon.action;
    const span = document.createElement('span');
    span.className = icon.cls;
    span.setAttribute('aria-hidden', 'true');
    key.appendChild(span);
  });
}
applyIconBank('top', functionIcons);
applyIconBank('bottom', layoutIcons);
const overlayEl = document.getElementById('overlay');
const mapFrame  = document.querySelector('.screen > iframe[title="map"]');
const screenEl  = document.getElementById('screen');
const paneIframes = [document.getElementById('pane-top'), document.getElementById('pane-bot')];
const infoBox   = document.getElementById('info-box');
const ibStatus  = document.getElementById('ib-status');
const wpnPanel  = document.getElementById('wpn-panel');
const wpnEmptyEl= document.getElementById('wpn-empty');
const tgpPanel  = document.getElementById('tgp-panel');
const tgpImg    = document.getElementById('tgp-img');
// has-feed is driven by the SSE tgpActive flag (mirrored from the map iframe) — MJPEG only
// fires 'load' once, so we can't use it to detect frame stalls. The 'error' handler still
// covers the hard case where the MJPEG connection breaks outright.
tgpImg.addEventListener('error', function() { tgpPanel.classList.remove('has-feed'); });
const sepEls      = document.querySelectorAll('#keys-left .sep');   // 0 = above key[0], i+1 = below key[i]
const rightSepEls = document.querySelectorAll('#keys-right .sep');  // same indexing, right column
const tglPanel    = document.getElementById('tgl-panel');
const avnPanel    = document.getElementById('avn-panel');
const avnNameEl   = document.getElementById('avn-name');
const avnFrame    = document.getElementById('avn-frame');
const avnBg       = document.getElementById('avn-bg');
const avnPartsEl  = document.getElementById('avn-parts');
const avnEmptyEl  = document.getElementById('avn-empty');
const avnFuelBar  = document.getElementById('avn-fuel-bar');
const avnFuelFill = document.getElementById('avn-fuel-fill');
const avnFuelVal  = document.getElementById('avn-fuel-val');
const avnThrBar   = document.getElementById('avn-thr-bar');
const avnThrFill  = document.getElementById('avn-thr-fill');
const avnThrVal   = document.getElementById('avn-thr-val');
const cmPanel       = document.getElementById('cm-panel');
const cmFlaresTitle = document.getElementById('cm-flares-title');
const cmJammerTitle = document.getElementById('cm-jammer-title');
const cmFlaresVal   = document.getElementById('cm-flares-val');
const cmJammerVal   = document.getElementById('cm-jammer-val');
const cmFlaresIcon  = document.getElementById('cm-flares-icon');
const cmBarFill     = document.getElementById('cm-bar-fill');
const flareDots     = cmFlaresIcon.querySelectorAll('.flare-dot');

// ── Pages ─────────────────────────────────────────────────────────────────────────
// Which page is in view (MAP, MAIN, WPN…) and the line-select items each page shows.
// Every item names a label, the key bank/slot it aligns to, and the action its key
// fires. The MAP page overlays its items on top of the (still-interactive) map; the
// MAIN page draws an opaque panel over it.
const PAGES = {
  map: {
    opaque: false,
    items: [
      { label: 'MAIN', key: 0, action: 'main' },   // → MAIN page
      { label: 'FLW',  key: 1, action: 'flw'  },   // → toggle map follow
      { label: 'Z+',   key: 2, action: 'zin'  },   // → map zoom in
      { label: 'Z-',   key: 3, action: 'zout' },   // → map zoom out
    ],
  },
  main: {
    opaque: true,
    items: [
      { label: 'AVN', key: 0, action: 'avn' },      // → AVN page
      { label: 'MAP', key: 1, action: 'map' },      // → MAP page
      { label: 'RWR', key: 2, action: 'rwr' },      // → RWR page
      { label: 'TGL', key: 3, action: 'tgl' },      // → TGL page (target list)
      { label: 'TGP', key: 4, action: 'tgp' },      // → TGP page
      { label: 'WPN', key: 5, action: 'wpn' },      // → WPN page
    ],
  },
  wpn: {
    opaque: true,
    // No static items: renderWpn() owns left-key-0 (MAIN on page 0, PREV after) and
    // right-key-0 (NEXT when more than WPN_MAX_DISPLAY weapons exist).
    items: [],
  },
  tgp: {
    opaque: true,
    items: [
      { label: 'MAIN', key: 0, action: 'main' },    // ← back to MAIN
    ],
  },
  tgl: {
    opaque: true,
    // No static items: renderTgl() owns left-key-0 (MAIN on page 0, PREV after)
    // and right-key-0 (NEXT when overflow), since both depend on the live page state.
    items: [],
  },
  avn: {
    opaque: true,
    items: [
      { label: 'MAIN', key: 0, action: 'main' },     // ← back to MAIN
    ],
  },
};
let currentPage = 'map';

// ── Split-screen state ──────────────────────────────────────────────────────────────
// When splitMode is on, the screen renders two stacked iframes (the panes) instead
// of the single map iframe + overlay panels. Each pane has its own currentPage;
// the shell still owns the bezel labels and dispatches clicks to the right pane.
// See todo/mfd-split-screen.md — Strategy A, implementation sequence steps 1-4.
let splitMode = false;
// [topPage, botPage]. Step 3 of the implementation sequence seeds both panes with
// MAIN on entry; per-pane navigation updates this from MAIN's L0..L2 / R0..R2 keys.
let panePages = ['main', 'main'];

// Latest connection status mirrored from the map iframe — kept so we can push the
// current value to a freshly-loaded pane iframe (its onload may fire AFTER the
// shell has already received and forwarded the last status broadcast).
let lastStatusCls  = 'disconnected';
let lastStatusText = '● DISCONNECTED';

// Split-mode line-select layouts per page. Each entry is one pane-local label;
// physical key index = slot + paneOffset (paneOffset = 0 for top, 3 for bottom).
// Only pages we've remapped via the interview in todo/mfd-split-screen.md appear
// here. Pages without an entry render no labels in split mode (yet).
const SPLIT_PAGES = {
  main: {
    // Initial mapping scope per the user — only AVN and TGP are wired today. Other
    // destinations (MAP/RWR/TGL/WPN) come in subsequent interview rounds and stay
    // hidden until their bare pages exist.
    items: [
      { side: 'left',  slot: 0, label: 'AVN', action: 'avn' },
      { side: 'right', slot: 1, label: 'TGP', action: 'tgp' },
    ],
  },
  // AVN / TGP in a split pane each expose a single MAIN back-button on their pane's
  // top-left slot (L0 for top, physically L3 for bottom). Clicking it navigates ONLY
  // that pane back to MAIN, leaving the other pane untouched.
  avn: {
    items: [
      { side: 'left', slot: 0, label: 'MAIN', action: 'main' },
    ],
  },
  tgp: {
    items: [
      { side: 'left', slot: 0, label: 'MAIN', action: 'main' },
    ],
  },
};

// URL for each iframe-served page. Only MAIN is wired today (interview step 4);
// other entries land as we remap each page. Pages without an entry render
// 'about:blank' on navigation — a no-op signal rather than a crash.
const PAGE_URL = {
  main: '/main?bare',
  avn:  '/avn?bare',
  tgp:  '/tgp?bare',
};
function paneUrl(page) { return PAGE_URL[page] || 'about:blank'; }

function applySplitMode() {
  screenEl.classList.toggle('split', splitMode);
  if (splitMode) {
    paneIframes[0].src = paneUrl(panePages[0]);
    paneIframes[1].src = paneUrl(panePages[1]);
    renderSplitLabels();
    renderIndicators();
  } else {
    // Drop iframe sources so they stop holding resources while hidden.
    paneIframes[0].removeAttribute('src');
    paneIframes[1].removeAttribute('src');
    // Re-render the single-pane layout for whatever page was current before.
    showPage(currentPage);
  }
}

// Place per-pane labels for both panes' current pages. The top pane occupies
// physical keys L0..L2 / R0..R2 (paneOffset = 0); the bottom pane occupies
// L3..L5 / R3..R5 (paneOffset = 3). Labels are tagged with data-pane so the
// click dispatcher knows which pane to update.
function renderSplitLabels() {
  clearKeyActions();
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });
  for (let paneIdx = 0; paneIdx < 2; paneIdx++) {
    const page = panePages[paneIdx];
    const def = SPLIT_PAGES[page];
    if (!def) continue;
    const paneOffset = paneIdx === 0 ? 0 : 3;
    const paneTag = paneIdx === 0 ? 'top' : 'bot';
    def.items.forEach(function(item) {
      placeOverlayLabel(item.side, item.slot + paneOffset, item.label, item.action);
      const physicalKey = keyBanks[item.side][item.slot + paneOffset];
      if (physicalKey) physicalKey.dataset.pane = paneTag;
    });
  }
}

function paneNavigate(paneIdx, page) {
  panePages[paneIdx] = page;
  paneIframes[paneIdx].src = paneUrl(page);
  renderSplitLabels();
}

// Forwarding from shell → pane iframes. The shell already mirrors all the data
// streams from the map iframe (status, avn, tgp, etc.); this just relays the
// latest snapshot to whichever pane needs it.
function forwardStatusToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'main') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage(
      { mfd: true, type: 'status', cls: lastStatusCls, text: lastStatusText }, '*');
  });
}
function forwardAvnToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'avn') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({
      mfd: true, type: 'avn',
      name: avnData.name,
      parts: avnData.parts,
      failures: avnData.failures,
      fuel: avnData.fuel,
      throttle: avnData.throttle,
    }, '*');
  });
}
function forwardTgpToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'tgp') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({ mfd: true, type: 'tgp', active: tgpActive }, '*');
  });
}

// ── App-wide orientation ─────────────────────────────────────────────────────────────
// A media query INSIDE an iframe evaluates against that iframe's own box, so a split
// pane (wide + short) would wrongly read landscape even when the device is portrait.
// To keep portrait/landscape rules tied to the WHOLE APP regardless of split state, the
// shell is the single source of truth: it reads the window orientation, tags its own
// <body class="portrait|landscape">, and forwards the value to each pane iframe so they
// tag their own <body> identically. Bare pages key their orientation CSS off that class
// instead of @media (orientation).
const orientMq = window.matchMedia('(orientation: portrait)');
function appOrientation() { return orientMq.matches ? 'portrait' : 'landscape'; }
function applyShellOrientation() {
  document.body.classList.toggle('portrait',  orientMq.matches);
  document.body.classList.toggle('landscape', !orientMq.matches);
}
function forwardOrientationToPane(iframe) {
  if (iframe && iframe.contentWindow)
    iframe.contentWindow.postMessage({ mfd: true, type: 'orient', orientation: appOrientation() }, '*');
}
function broadcastOrientation() { paneIframes.forEach(forwardOrientationToPane); }
orientMq.addEventListener('change', function() { applyShellOrientation(); broadcastOrientation(); });
applyShellOrientation();

// On pane iframe load, push the latest snapshot for whichever page that pane is
// rendering — the page may have been mid-update at the moment its iframe started
// loading — plus the current app orientation (every bare page can use it).
paneIframes.forEach(function(iframe, idx) {
  iframe.addEventListener('load', function() {
    if (!splitMode) return;
    forwardOrientationToPane(iframe);
    const page = panePages[idx];
    if      (page === 'main') forwardStatusToPanes();
    else if (page === 'avn')  forwardAvnToPanes();
    else if (page === 'tgp')  forwardTgpToPanes();
  });
});

// Top-right indicator stack (PINNED + FOLLOW). pinnedPage tracks which page (if any)
// is currently pinned; followOn mirrors the map iframe's follow state (broadcast via
// postMessage). indicatorOrder records the chronological order indicators were turned
// on — the first activated stays at the right edge and later arrivals stack to its
// left, matching how chips render with flex-direction:row-reverse on #mfd-indicators.
const indicatorsEl = document.getElementById('mfd-indicators');
let pinnedPage    = null;
let followOn      = false;
let indicatorOrder = [];   // subset of ['pinned','follow'] in activation order
// Last non-pinned page we left to jump to pinnedPage via SWAP. Lets the second SWAP
// press return there. Cleared whenever the pin itself changes (re-pin or unpin) since
// the partner relationship is tied to the current pin.
let swapPartner   = null;

function indicatorVisible(name) {
  if (name === 'pinned') return pinnedPage !== null && currentPage === pinnedPage;
  if (name === 'follow') return currentPage === 'map' && followOn;
  return false;
}

function renderIndicators() {
  indicatorsEl.innerHTML = '';
  indicatorOrder.forEach(function(name) {
    if (!indicatorVisible(name)) return;
    const el = document.createElement('div');
    el.className = 'mfd-indicator';
    el.textContent = name === 'pinned' ? 'PINNED' : 'FOLLOW';
    indicatorsEl.appendChild(el);
  });
}

// Latest loadout snapshot mirrored from the map iframe (postMessage). Even when WPN isn't
// in view we keep it fresh, so opening the page renders immediately without a round-trip.
let wpnData      = { items: [], selWeapon: null };
let wpnNamesKey  = null;     // weapon-name signature — only rebuild the DOM when it changes
let wpnAmmoEls   = [];       // ammo text nodes, aligned with wpnData.items
let wpnItemEls   = [];       // .wp-item containers, aligned with wpnData.items
let wpnSelIconWrap = null;   // .wp-sel-icon-wrap holder (right-half big icon)
let wpnSelIconImg  = null;   // <img> inside the wrap; src tracks wpnData.selWeapon
let wpnSelIconKey  = null;   // last src we set, so we don't trigger no-op reloads
let wpnPage = 0;             // 0-indexed page for the weapon list pagination
const WPN_MAX_DISPLAY = 5;   // weapons per page = 5 line-select slots (keys 1..5)

// Latest countermeasures snapshot mirrored from the map iframe.
let cmData = { flares: -1, flaresMax: -1, ewKJ: -1, ewKJMax: -1, cmCat: 0 };

// Latest TGP feed state mirrored from the map iframe. False until the first frame is
// produced, and back to false during the 3-second post-loss hold's expiry.
let tgpActive = false;

// Latest target list mirrored from the map iframe. Whole list is kept in memory; the
// renderer shows TGL_MAX_DISPLAY per page (left key 1..5 then right key 1..5) and
// pages through them with PREV/NEXT on the side keys. tglPage = 0-indexed page.
let tglData = { targets: [] };
let tglPage = 0;
const TGL_MAX_DISPLAY = 10;

// Latest avionics snapshot mirrored from the map iframe. name = aircraft display name (also
// the key for /airframe + /airframe-layout); parts = the live HP list from the snapshot;
// failures = list of failure-message strings currently active (e.g. ["LEFT ENGINE FIRE"]).
let avnData = { name: null, parts: null, failures: null, fuel: -1, throttle: -1 };

// Known failure messages and how to render them on the AVN page. Keys MUST match the
// GameObject name in the cockpit's StatusDisplay.failureIndicators list — that's the
// string the server emits in snapshot.failures. The displayed text + position imitate
// the in-cockpit prefab visually but aren't read from it: keeping styling and layout in
// the frontend means new aircraft don't need any server-side change to render correctly.
// cx/cy are normalized (0..1) against the silhouette frame; cy > 1 lets the label sit
// just below the silhouette, matching where the cockpit places L/R engine fire warnings.
const AVN_FAILURE_DEFS = {
  'LEFT ENGINE FIRE':  { text: 'L ENG FIRE', cx: 0.20, cy: 0.78 },
  'RIGHT ENGINE FIRE': { text: 'R ENG FIRE', cx: 0.80, cy: 0.78 },
};

// Per-aircraft-type layout descriptor + part-name → DOM element map. Layouts are fetched
// once per aircraft type (and cached forever) since they're static prefab data; the part
// elements are rebuilt whenever the layout changes (i.e. the player swaps aircraft).
let avnLayoutType    = null;                    // type string the current DOM is built for
let avnLayoutCache   = Object.create(null);     // type → {bg, parts:[...], failures:[...]} | 'pending' | 'fail'
let avnPartEls       = Object.create(null);     // partName → .avn-part DOM element
let avnFailureEls    = Object.create(null);     // failureMessage → .avn-failure DOM element

function clearKeyActions() {
  // Only the page-dynamic banks (left/right) get cleared between pages. The top and bottom
  // banks hold page-independent controls (fullscreen on top; PIN, SWAP, layout… on bottom)
  // whose actions are wired once at startup and must survive page switches.
  ['left', 'right'].forEach(function(bank) {
    keyBanks[bank].forEach(function(k) {
      delete k.dataset.action;
      delete k.dataset.pane;     // split-mode tag; harmless to clear unconditionally
    });
  });
}

function placeOverlayLabel(bankName, keyIndex, label, action) {
  const side = bankName || 'left';
  const bank = keyBanks[side];
  const k = bank && bank[keyIndex];
  if (!k) return;

  if (action) k.dataset.action = action;
  const el = document.createElement('div');
  el.className = 'overlay-item ' + side;
  el.textContent = label;

  const oRect = overlayEl.getBoundingClientRect();
  const kr = k.getBoundingClientRect();
  if (side === 'top' || side === 'bottom') {
    el.style.left = (kr.left + kr.width / 2 - oRect.left) + 'px';
    el.style.top = (side === 'top' ? 16 : oRect.height - 16) + 'px';
  } else {
    el.style.top = (kr.top + kr.height / 2 - oRect.top) + 'px';
  }
  overlayEl.appendChild(el);
}

// Render a page: set the overlay background, (re)assign key actions, and position
// each item label next to its physical key.
function showPage(name) {
  currentPage = name;
  const page = PAGES[name];
  overlayEl.classList.toggle('opaque', page.opaque);
  infoBox.classList.toggle('show', name === 'main');
  wpnPanel.classList.toggle('show', name === 'wpn');
  tgpPanel.classList.toggle('show', name === 'tgp');
  tglPanel.classList.toggle('show', name === 'tgl');
  avnPanel.classList.toggle('show', name === 'avn');
  // Start the MJPEG fetch only while the TGP page is in view; clearing src closes the
  // long-lived multipart connection so the mod can stop encoding frames if no one's watching.
  if (name === 'tgp') {
    if (!tgpImg.src) tgpImg.src = '/tgp.mjpg';
    // Reflect whatever the latest SSE flag said — opening the page mid-loss-hold should
    // show the live feed, opening it with no target should show NO TARGET immediately.
    tgpPanel.classList.toggle('has-feed', tgpActive);
  } else {
    tgpImg.removeAttribute('src');
    tgpPanel.classList.remove('has-feed');
  }
  if (name === 'wpn') { renderCm(); }   // cm-panel positions itself; doesn't need overlay-items

  clearKeyActions();
  // Only wipe dynamic line-select labels; static children (info-box) stay put.
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });

  page.items.forEach(function(item) {
    placeOverlayLabel(item.side || 'left', item.key, item.label, item.action);
  });

  // TGL and WPN own their own labels (PREV/MAIN + NEXT) because they depend on the
  // page state; run after the generic label sweep so they don't get clobbered.
  if (name === 'wpn') { renderWpn(); }
  if (name === 'tgl') { renderTgl(); }
  if (name === 'avn') { renderAvn(); }

  renderIndicators();
}

// Renders the AVN page. Three layers:
//   - .avn-name pinned to the vertical centre of left key[0] (top row).
//   - .avn-frame stretched from just below the name (sep[1]) to just above the bottom
//     strip (last sep), spanning the full screen width.
//   - .avn-bg + .avn-parts inside the frame; bg fits with object-fit:contain, parts are
//     positioned + sized against the bg's rendered box so cx/cy/w/h stay aligned.
// Re-runs on resize via showPage(currentPage) — re-positioning is the main reason.
function renderAvn() {
  const type = avnData.name;

  // No-data state: hide the aircraft name + silhouette frame entirely and show the
  // centered "— NO DATA —" placeholder (matches WPN / TGL / TGP empty states).
  if (!type) {
    avnNameEl.style.display = 'none';
    avnFrame.style.display  = 'none';
    avnEmptyEl.style.display = '';
    avnFuelBar.classList.remove('placed');
    avnThrBar .classList.remove('placed');
    return;
  }
  avnNameEl.style.display  = '';
  avnFrame.style.display   = '';
  avnEmptyEl.style.display = 'none';
  avnNameEl.textContent = type;

  // Vertical placement: name aligned with key[0]'s centre; frame top under sep[1].
  const k = leftKeys[0];
  if (!k) return;
  const panelRect = avnPanel.getBoundingClientRect();
  const kr = k.getBoundingClientRect();
  avnNameEl.style.top = (kr.top + kr.height / 2 - panelRect.top) + 'px';

  if (sepEls.length >= 2) {
    const frameTopSep = sepEls[1].getBoundingClientRect();   // sep below key[0]
    const frameBotSep = sepEls[sepEls.length - 1].getBoundingClientRect();
    avnFrame.style.top    = (frameTopSep.bottom - panelRect.top) + 'px';
    avnFrame.style.height = (frameBotSep.top - frameTopSep.bottom) + 'px';
  }

  avnBg.style.display = '';
  avnPartsEl.style.display = '';

  // Kick off / re-use the layout fetch. Once loaded, build the part DOM and start tinting.
  ensureAvnLayout(type);
  const layout = avnLayoutCache[type];
  if (!layout || typeof layout === 'string') return;     // pending or failed
  if (avnLayoutType !== type) buildAvnParts(type, layout);

  // Size the .avn-parts container to match the bg's *rendered* box (object-fit:contain
  // leaves letterboxing — we want parts to land on the bg pixels, not the letterbox).
  fitAvnPartsToBg();
  sizeAvnFailures();

  // Apply the live HP tint per part.
  paintAvnDamage();
  // Toggle failure labels (e.g. L/R ENG FIRE) according to the latest snapshot.
  paintAvnFailures();
  // Side bars (FUEL / THROTTLE) — placement re-reads the silhouette's measured bounds.
  layoutAvnBars();
  paintAvnBars();
}

// Fetch the silhouette layout for an aircraft type, if not already cached / in flight.
function ensureAvnLayout(type) {
  if (avnLayoutCache[type] !== undefined) return;
  avnLayoutCache[type] = 'pending';

  // Background PNG comes from the same endpoint, swap immediately so the player sees
  // the outline as soon as it loads even before the parts arrive.
  avnBg.src = '/airframe?type=' + encodeURIComponent(type) + '&part=__bg';

  fetch('/airframe-layout?type=' + encodeURIComponent(type))
    .then(function(r) { if (!r.ok) throw new Error('layout ' + r.status); return r.json(); })
    .then(function(j) { avnLayoutCache[type] = j; if (currentPage === 'avn') renderAvn(); })
    .catch(function()  { avnLayoutCache[type] = 'fail'; });
}

// Build one .avn-part div per layout entry. The CSS mask is the part's silhouette PNG —
// background-color sets the tint, mask alpha sets the shape. Reused across renders; only
// rebuilt when the aircraft type changes.
function buildAvnParts(type, layout) {
  avnPartsEl.innerHTML = '';
  avnPartEls = Object.create(null);
  if (!layout || !Array.isArray(layout.parts)) { avnLayoutType = type; return; }

  for (const p of layout.parts) {
    const el = document.createElement('div');
    el.className = 'avn-part';
    el.dataset.rt = p.rt;                                    // red threshold from the prefab
    el.style.left   = (p.cx * 100).toFixed(3) + '%';
    el.style.top    = (p.cy * 100).toFixed(3) + '%';
    el.style.width  = (p.w  * 100).toFixed(3) + '%';
    el.style.height = (p.h  * 100).toFixed(3) + '%';
    // Compose the transform: centre on (left, top), then apply per-part scale flip
    // (so symmetric sprites reused across L/R parts get drawn mirrored to match the
    // game's RectTransform.scale), then rotate. Unity Z rotation is CCW positive, CSS
    // rotate() is CW positive, so we negate.
    const sx = (p.sx === -1) ? -1 : 1;
    const sy = (p.sy === -1) ? -1 : 1;
    const parts = ['translate(-50%, -50%)'];
    if (sx !== 1 || sy !== 1) parts.push('scale(' + sx + ',' + sy + ')');
    if (p.r)                   parts.push('rotate(' + (-p.r).toFixed(1) + 'deg)');
    el.style.transform = parts.join(' ');
    const url = '/airframe?type=' + encodeURIComponent(type) + '&part=' + encodeURIComponent(p.n);
    el.style.webkitMaskImage = 'url("' + url + '")';
    el.style.maskImage       = 'url("' + url + '")';
    avnPartsEl.appendChild(el);
    avnPartEls[p.n] = el;
  }
  // Failure labels are positioned + styled here by the frontend, not driven by the
  // captured layout. The server sends just a list of currently-active message strings
  // (the GameObject names — e.g. "LEFT ENGINE FIRE") and we render them faithfully to
  // the in-cockpit display. AVN_FAILURE_DEFS (above the page script) defines what to
  // draw for each known message: the displayed text and the position relative to the
  // silhouette. Messages without a def are ignored — add new ones there as we
  // encounter them.
  avnFailureEls = Object.create(null);
  for (const key in AVN_FAILURE_DEFS) {
    const def = AVN_FAILURE_DEFS[key];
    const el = document.createElement('div');
    el.className = 'avn-failure';
    el.textContent = def.text;
    el.style.left = (def.cx * 100).toFixed(3) + '%';
    el.style.top  = (def.cy * 100).toFixed(3) + '%';
    avnPartsEl.appendChild(el);
    avnFailureEls[key] = el;
  }
  avnLayoutType = type;
}

// Font size for failure labels — scales with the silhouette so they stay legible at
// any MFD size. Re-run on resize / silhouette refit.
function sizeAvnFailures() {
  const h = avnPartsEl.getBoundingClientRect().height;
  if (h <= 0) return;
  const px = Math.max(11, h * 0.045);             // ~4.5% of silhouette height
  for (const name in avnFailureEls) {
    avnFailureEls[name].style.fontSize = px.toFixed(1) + 'px';
  }
}

// Bar geometry shared by the placement (layoutAvnBars) and the portrait frame inset
// (applyAvnFrameInset) so they always agree on where the bars sit. .avn-vbar has a fixed
// 42px CSS width; edgeInset matches the clamp in layoutAvnBars (bar's outer offset from the
// panel edge). AVN_BAR_SILHOUETTE_GAP is the breathing room we keep between a bar's inner
// edge and the silhouette in portrait.
const AVN_BAR_W = 42;
const AVN_BAR_SILHOUETTE_GAP = 15;
function avnBarGap() { return Math.max(8, Math.round(avnPanel.getBoundingClientRect().width * 0.012)); }
function avnBarEdgeInset() { return avnBarGap() + 7; }   // 7 ≈ tick gutter (5px) + 2px margin

// In portrait the silhouette would fill the full frame width and slide under the FUEL/
// THROTTLE bars pinned at the panel edges. Pull the frame in on each side by the bar zone
// plus AVN_BAR_SILHOUETTE_GAP so the silhouette (bg + part masks, both sized to the frame)
// stays clear of the bars. Landscape flanks a narrow silhouette with room to spare, so the
// inset is cleared there.
function applyAvnFrameInset() {
  if (document.body.classList.contains('portrait')) {
    const inset = avnBarEdgeInset() + AVN_BAR_W + AVN_BAR_SILHOUETTE_GAP;
    avnFrame.style.left  = inset + 'px';
    avnFrame.style.right = inset + 'px';
  } else {
    avnFrame.style.left  = '';
    avnFrame.style.right = '';
  }
}

// .avn-bg uses max-width/height: 100% (object-fit:contain on an <img>), so its rendered
// box is at most the frame. Mirror that box onto .avn-parts so children placed by % land
// on the bg pixels, not on the letterbox border.
function fitAvnPartsToBg() {
  applyAvnFrameInset();
  const fr = avnFrame.getBoundingClientRect();
  if (!fr.width || !fr.height || !avnBg.naturalWidth || !avnBg.naturalHeight) {
    avnPartsEl.style.width = fr.width + 'px';
    avnPartsEl.style.height = fr.height + 'px';
    return;
  }
  const imgAspect = avnBg.naturalWidth / avnBg.naturalHeight;
  const frAspect  = fr.width / fr.height;
  let w, h;
  if (imgAspect > frAspect) { w = fr.width;  h = fr.width  / imgAspect; }
  else                      { h = fr.height; w = fr.height * imgAspect; }
  avnPartsEl.style.width  = w + 'px';
  avnPartsEl.style.height = h + 'px';
}
// Trigger a re-fit once the bg PNG resolves (naturalWidth becomes known then).
avnBg.addEventListener('load', function() {
  if (currentPage !== 'avn') return;
  fitAvnPartsToBg();
  sizeAvnFailures();
  layoutAvnBars();
  paintAvnBars();
});

// Apply the game's damage formula per part:
//   condition = max((hp - redThreshold) / (100 - redThreshold), 0)
//   color = rgb(255, 255*min(condition*2,1), 0)  → green→yellow→red as hp drops
//   alpha = 1 - condition                         → undamaged parts fade out
//   detached → rgb(178, 0, 64)                    → the "lost part" magenta
// (See decompiled/PartStatusDisplay.cs StatusDisplay_OnDamage.)
// Toggle the .active class on each failure-label DOM node from avnData.failures (the
// live list of currently-active failure messages reported by the snapshot).
function paintAvnFailures() {
  const set = Object.create(null);
  if (Array.isArray(avnData.failures))
    for (const name of avnData.failures) set[name] = true;
  for (const name in avnFailureEls) {
    avnFailureEls[name].classList.toggle('active', !!set[name]);
  }
}

function paintAvnDamage() {
  const map = Object.create(null);
  if (Array.isArray(avnData.parts)) {
    for (const p of avnData.parts) map[p.n] = p;
  }
  for (const name in avnPartEls) {
    const el = avnPartEls[name];
    const data = map[name];
    const rt = +el.dataset.rt || 30;
    if (data && data.d) {
      el.style.backgroundColor = 'rgb(178, 0, 64)';
      el.style.opacity = '1';
      continue;
    }
    const hp = data ? data.hp : 100;
    const cond = Math.max((hp - rt) / (100 - rt), 0);
    const g = Math.min(cond * 2, 1);
    el.style.backgroundColor = 'rgb(255,' + Math.round(g * 255) + ',0)';
    el.style.opacity = (1 - cond).toFixed(3);     // 0 (healthy) → invisible; 1 (red zone) → fully tinted
  }
}

// Position the FUEL and THROTTLE bars beside the silhouette. Horizontal anchor uses the
// silhouette's measured edges (partsRect) so the bars sit flush against the aircraft
// graphic; vertical extent uses the frame's full height instead — silhouette height can
// shrink dramatically on portrait viewports (object-fit:contain letterboxes top/bottom),
// which would otherwise make the tube look short and almost horizontal. Frame height
// stays tall on every viewport, keeping the bars unmistakably vertical.
function layoutAvnBars() {
  const partsRect = avnPartsEl.getBoundingClientRect();
  const frameRect = avnFrame.getBoundingClientRect();
  if (!partsRect.width || !partsRect.height || !frameRect.height) {
    avnFuelBar.classList.remove('placed');
    avnThrBar .classList.remove('placed');
    return;
  }
  const panelRect = avnPanel.getBoundingClientRect();
  const gap = avnBarGap();
  const topInPanel = frameRect.top - panelRect.top;

  const barW = avnFuelBar.offsetWidth || AVN_BAR_W;
  const edgeInset = avnBarEdgeInset();             // 7 ≈ tick gutter width (5px) + its 2px margin
  const edgePos = panelRect.width - barW - edgeInset;   // flush against the panel edge

  // Portrait: the silhouette fills the width (and is inset to clear the bars — see
  // applyAvnFrameInset), so pin the bars to the panel edges. Landscape: flank the narrow
  // silhouette, anchoring to its measured edges, clamped so a bar can never spill outside.
  const portrait = document.body.classList.contains('portrait');
  let fuelRight, thrLeft;
  if (portrait) {
    fuelRight = edgePos;
    thrLeft   = edgePos;
  } else {
    fuelRight = Math.max(edgeInset, Math.min(panelRect.right - (partsRect.left - gap), edgePos));
    thrLeft   = Math.max(edgeInset, Math.min((partsRect.right + gap) - panelRect.left, edgePos));
  }

  // Portrait: shorten the bars to 80% of the frame height and re-center them vertically so
  // they read tighter against the aircraft. Landscape keeps the full silhouette height.
  const barH   = portrait ? frameRect.height * 0.8 : frameRect.height;
  const barTop = topInPanel + (frameRect.height - barH) / 2;

  avnFuelBar.style.right  = fuelRight + 'px';
  avnFuelBar.style.top    = barTop + 'px';
  avnFuelBar.style.height = barH + 'px';
  avnFuelBar.classList.add('placed');

  avnThrBar.style.left   = thrLeft + 'px';
  avnThrBar.style.top    = barTop + 'px';
  avnThrBar.style.height = barH + 'px';
  avnThrBar.classList.add('placed');
}

// Paint both bars from the latest snapshot. paintAvnBar handles the -1 sentinel by
// switching the bar into its dim .na state, so no extra guard is needed here.
function paintAvnBars() {
  paintAvnBar(avnFuelBar, avnFuelFill, avnFuelVal, avnData.fuel,     0.25, 0.10);
  paintAvnBar(avnThrBar,  avnThrFill,  avnThrVal,  avnData.throttle, null, null);
}

function paintAvnBar(barEl, fillEl, valEl, value01, cautionAt, criticalAt) {
  barEl.classList.remove('na', 'caution', 'critical');
  if (typeof value01 !== 'number' || value01 < 0) {
    barEl.classList.add('na');
    fillEl.style.height = '0%';
    valEl.textContent = '--';
    return;
  }
  const v = Math.max(0, Math.min(1, value01));
  if      (criticalAt !== null && v <= criticalAt) barEl.classList.add('critical');
  else if (cautionAt  !== null && v <= cautionAt)  barEl.classList.add('caution');
  fillEl.style.height = (v * 100).toFixed(1) + '%';
  valEl.textContent = Math.round(v * 100) + '%';
}

// Render the WPN page from the cached loadout. Each weapon row is absolutely positioned
// to fill the slot of one line-select key (starting at key[1] — key[0] is the MAIN back
// button), so the icon stretches to the maximum height available below name + ammo.
// Rebuilds the DOM (and refetches icons) only when the set of weapons changes; ammo
// text + selected highlight refresh in place.
function renderWpn() {
  // Clear any nav labels from a prior render; we rebuild them below based on wpnPage.
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });
  delete leftKeys [0].dataset.action;
  delete rightKeys[0].dataset.action;

  const list    = wpnData.items || [];
  const total   = list.length;
  const maxPage = Math.max(0, Math.ceil(total / WPN_MAX_DISPLAY) - 1);
  if (wpnPage > maxPage) wpnPage = maxPage;
  if (wpnPage < 0)       wpnPage = 0;

  const start   = wpnPage * WPN_MAX_DISPLAY;
  const trimmed = list.slice(start, start + WPN_MAX_DISPLAY);

  // Nav buttons: MAIN/PREV on left key 0, NEXT on right key 0 when there's overflow.
  placeOverlayLabel('left', 0, wpnPage > 0 ? 'PREV' : 'MAIN', wpnPage > 0 ? 'wpn-prev' : 'main');
  if (start + trimmed.length < total) placeOverlayLabel('right', 0, 'NEXT', 'wpn-next');

  if (!trimmed.length) {
    wpnEmptyEl.style.display = '';
    if (wpnNamesKey !== '') {
      wpnNamesKey = ''; wpnAmmoEls = []; wpnItemEls = [];
      wpnPanel.querySelectorAll('.wp-item').forEach(function(el) { el.remove(); });
    }
    if (wpnSelIconWrap) wpnSelIconWrap.classList.remove('show');
    wpnSelIconKey = null;
    return;
  }
  wpnEmptyEl.style.display = 'none';

  const key = trimmed.map(function(w) { return w.n; }).join('|');
  if (key !== wpnNamesKey) {
    wpnNamesKey = key;
    wpnAmmoEls = [];
    wpnItemEls = [];
    wpnPanel.querySelectorAll('.wp-item').forEach(function(el) { el.remove(); });
    for (const w of trimmed) {
      const item = document.createElement('div');
      item.className = 'wp-item';

      const name = document.createElement('div');
      name.className = 'wp-name';
      name.textContent = w.n;
      item.appendChild(name);

      const ammo = document.createElement('div');
      ammo.className = 'wp-ammo';
      item.appendChild(ammo);
      wpnAmmoEls.push(ammo);

      wpnItemEls.push(item);
      wpnPanel.appendChild(item);
    }
  }

  // Lazy-build the big selected-weapon icon on the right half (one image, swapped src
  // whenever the selected weapon changes). The wrap is reused across rebuilds.
  if (!wpnSelIconWrap) {
    wpnSelIconWrap = document.createElement('div');
    wpnSelIconWrap.className = 'wp-sel-icon-wrap';
    wpnSelIconImg = document.createElement('img');
    wpnSelIconImg.className = 'wp-sel-icon';
    wpnSelIconImg.alt = '';
    wpnSelIconImg.onerror = function() { wpnSelIconImg.style.visibility = 'hidden'; };
    wpnSelIconImg.onload  = function() { wpnSelIconImg.style.visibility = ''; };
    wpnSelIconWrap.appendChild(wpnSelIconImg);
    wpnPanel.appendChild(wpnSelIconWrap);
  }

  // Position each row to span between the separators flanking its line-select key.
  const panelRect = wpnPanel.getBoundingClientRect();
  for (let i = 0; i < wpnItemEls.length; i++) {
    const top = sepEls[i + 1].getBoundingClientRect();
    const bot = sepEls[i + 2].getBoundingClientRect();
    wpnItemEls[i].style.top    = (top.bottom - panelRect.top) + 'px';
    wpnItemEls[i].style.height = (bot.top - top.bottom) + 'px';
  }

  // Position the big icon over the full vertical span of the side keys (sep[1] just
  // below key 0 → sep[last] just above the bottom strip). Always spans the whole keys-1..5
  // area regardless of how many weapons the player actually has, so the icon gets all the
  // available height even when only a few rows are populated. +20/-40 = 20px outer margin
  // top + bottom (mirrors the 10px horizontal outer margin on the wrap but with extra
  // breathing room above and below the bordered box).
  if (sepEls.length >= 3) {
    const topRect = sepEls[1].getBoundingClientRect();
    const botRect = sepEls[sepEls.length - 1].getBoundingClientRect();
    wpnSelIconWrap.style.top    = (topRect.bottom - panelRect.top + 20) + 'px';
    wpnSelIconWrap.style.height = (botRect.top - topRect.bottom - 40) + 'px';
  }

  // Refresh ammo text + selected/depleted highlights in place (cheap, no DOM rebuild).
  for (let i = 0; i < trimmed.length && i < wpnAmmoEls.length; i++) {
    const w = trimmed[i];
    wpnAmmoEls[i].innerHTML = (w.f > 0) ? ('<span>' + w.a + '</span> / ' + w.f) : '';
    wpnItemEls[i].classList.toggle('sel',   w.n === wpnData.selWeapon);
    wpnItemEls[i].classList.toggle('empty', w.f > 0 && w.a === 0);
  }

  // Update the right-side icon src when the selection changes (no-op if same).
  const sel = wpnData.selWeapon;
  if (sel) {
    if (sel !== wpnSelIconKey) {
      wpnSelIconKey = sel;
      wpnSelIconImg.style.visibility = '';            // un-hide before the new load resolves
      wpnSelIconImg.src = '/weapon?name=' + encodeURIComponent(sel);
    }
    wpnSelIconWrap.classList.add('show');
  } else {
    wpnSelIconWrap.classList.remove('show');
    wpnSelIconKey = null;
  }
}

// Renders the countermeasures panel: positions it in key[0]'s slot and refreshes the
// flares count, capacitor bar, and EW kJ text.
function renderCm() {
  // Position: top = bottom of sep[0] (above key[0]), height = top of sep[1] (below key[0]).
  if (sepEls.length >= 2) {
    const sep0 = sepEls[0].getBoundingClientRect();
    const sep1 = sepEls[1].getBoundingClientRect();
    const panelRect = wpnPanel.getBoundingClientRect();
    cmPanel.style.top    = (sep0.bottom - panelRect.top) + 'px';
    cmPanel.style.height = (sep1.top - sep0.bottom) + 'px';
  }

  cmFlaresVal.textContent = (cmData.flares >= 0) ? cmData.flares : '—';
  // Split the kJ readout into two spans so the gap between number and unit stays at a
  // fixed pixel value (set via the flex gap on #cm-jammer-val), independent of font size.
  if (cmData.ewKJ >= 0) {
    cmJammerVal.innerHTML = '<span>' + Math.round(cmData.ewKJ) + '</span><span>kJ</span>';
  } else {
    cmJammerVal.textContent = '—';
  }

  const pct = (cmData.ewKJMax > 0 && cmData.ewKJ >= 0)
            ? Math.max(0, Math.min(1, cmData.ewKJ / cmData.ewKJMax))
            : 0;
  cmBarFill.style.width = (pct * 100) + '%';

  // Selection + depletion highlights (mirror the weapon-row treatment).
  const flaresEmpty = cmData.flaresMax > 0 && cmData.flares === 0;
  const jammerEmpty = cmData.ewKJMax  > 0 && cmData.ewKJ   === 0;
  cmFlaresTitle.classList.toggle('sel',   cmData.cmCat === 1);
  cmFlaresTitle.classList.toggle('empty', flaresEmpty);
  cmFlaresVal  .classList.toggle('empty', flaresEmpty);
  cmFlaresIcon .classList.toggle('empty', flaresEmpty);

  // Mute the first N dots to visualise spent flares (1 dot = 1/16th of flaresMax).
  // When fully depleted, leave all dots hollow — the .empty state already reds out the icon.
  const knowFlares = !flaresEmpty && cmData.flaresMax > 0 && cmData.flares >= 0;
  const spentDots  = knowFlares
    ? Math.floor((cmData.flaresMax - cmData.flares) * flareDots.length / cmData.flaresMax)
    : 0;
  flareDots.forEach(function(d, i) { d.classList.toggle('spent', i < spentDots); });
  cmJammerTitle.classList.toggle('sel',   cmData.cmCat === 2);
  cmJammerTitle.classList.toggle('empty', jammerEmpty);
  cmJammerVal  .classList.toggle('empty', jammerEmpty);

  // Size the big readouts + IR icon. Grid rows are auto-sized (no 1fr), so cells no
  // longer stretch — derive a target glyph height from the slot height directly. This
  // keeps the title/value/bar visually clustered instead of spread across the slot.
  // Grid is `1fr 1px 1fr` with 14px column-gap, so each column track is:
  const colW = Math.max(0, (cmPanel.clientWidth - 1 - 14 * 2) / 2);
  const slotH = cmPanel.getBoundingClientRect().height;
  const targetH = slotH * 0.55;
  const iconSize = Math.max(0, Math.min(targetH, colW * 0.5));
  cmFlaresIcon.style.width  = iconSize + 'px';
  cmFlaresIcon.style.height = iconSize + 'px';
  function fitText(el, maxH, maxW) {
    if (maxH < 4 || maxW < 4) return;
    let size = Math.floor(maxH * 0.8);
    el.style.fontSize = size + 'px';
    const w = el.scrollWidth;
    if (w > maxW && w > 0) {
      size = Math.max(8, Math.floor(size * maxW / w));
      el.style.fontSize = size + 'px';
    }
  }
  const flaresUsableW = Math.max(0, colW - iconSize - 10);  // 10 = CSS gap
  fitText(cmFlaresVal, targetH, flaresUsableW);
  fitText(cmJammerVal, targetH, colW);

  // Match the capacitor bar's width to the kJ readout so they read as one grouped unit.
  // Measured after fitTextHeight so the readout's font is already at its final size.
  const cmBarEl = cmBarFill.parentElement;
  cmBarEl.style.width = Math.ceil(cmJammerVal.getBoundingClientRect().width) + 'px';
}

// Renders the TGL page from the cached target list. Each page shows up to TGL_MAX_DISPLAY
// targets — 1..5 down the left column, 6..10 down the right. Left key 0 is MAIN on the
// first page and PREV on later pages; right key 0 is NEXT when there are more targets
// past the current page. Also owns those nav labels (showPage skips them for TGL).
function renderTgl() {
  // Tear down any previously-rendered rows + nav labels; small enough to rebuild from scratch.
  tglPanel.querySelectorAll('.tg-item').forEach(function(el) { el.remove(); });
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });
  delete leftKeys[0].dataset.action;
  delete rightKeys[0].dataset.action;

  const targets = tglData.targets || [];
  const total   = targets.length;
  const maxPage = Math.max(0, Math.ceil(total / TGL_MAX_DISPLAY) - 1);
  if (tglPage > maxPage) tglPage = maxPage;
  if (tglPage < 0)       tglPage = 0;

  const start = tglPage * TGL_MAX_DISPLAY;
  const list  = targets.slice(start, start + TGL_MAX_DISPLAY);
  tglPanel.classList.toggle('has-targets', list.length > 0);

  // Place the nav labels (PREV/MAIN on left key 0, NEXT on right key 0 when overflowing).
  placeOverlayLabel('left', 0, tglPage > 0 ? 'PREV' : 'MAIN', tglPage > 0 ? 'tgl-prev' : 'main');
  if (start + list.length < total) placeOverlayLabel('right', 0, 'NEXT', 'tgl-next');

  if (!list.length) return;

  // Format range as "8,4 km" (European decimal comma) when given a number; pass strings through.
  function fmtRng(r) {
    if (typeof r === 'number' && isFinite(r)) return r.toFixed(1).replace('.', ',') + ' km';
    return (r != null ? String(r) : '—') + (typeof r === 'string' && /km$/i.test(r) ? '' : '');
  }

  const panelRect = tglPanel.getBoundingClientRect();
  for (let i = 0; i < list.length; i++) {
    const onLeft = i < 5;
    const slot   = onLeft ? (i + 1) : (i - 5 + 1);   // key index inside the column (1..5)
    const col    = onLeft ? sepEls : rightSepEls;
    if (slot + 1 >= col.length) continue;            // safety, shouldn't trigger with 5 slots

    const t   = list[i];
    const top = col[slot].getBoundingClientRect();
    const bot = col[slot + 1].getBoundingClientRect();

    const slotH = bot.top - top.bottom;
    // Each side gets half the panel width. Left and right meet (or overlap) at the centre —
    // user explicitly accepts overlap in exchange for losing the dead black band.
    const sideW = Math.max(40, panelRect.width * 0.5);

    const row = document.createElement('div');
    // Faction class drives the palette: 1 = friendly (blue), 0 = neutral (white), anything
    // else (including missing) defaults to enemy (green).
    const factionCls = t.f === 1 ? ' f-friendly' : t.f === 0 ? ' f-neutral' : '';
    row.className = 'tg-item ' + (onLeft ? 'left' : 'right') + factionCls;
    row.style.top    = (top.bottom - panelRect.top) + 'px';
    row.style.height = slotH + 'px';
    row.style.width  = sideW + 'px';

    // Initial sizes by slot height. Name is 5/3 the meta size ("2/3 bigger"). Three lines
    // stacked (name + GRID + RNG) — shrunk below if any line overflows the column width.
    // The 0.115 factor is 2/3 of the original 0.1725 — matches the reduction applied to
    // the white line-select labels so target text reads at the same relative scale.
    let metaPx = Math.max(8, slotH * 0.115);
    let namePx = metaPx * (5 / 3);

    const name = document.createElement('div');
    name.className = 'tg-name';
    name.style.fontSize = namePx.toFixed(1) + 'px';
    name.textContent = t.n || '—';
    row.appendChild(name);

    const grid = document.createElement('div');
    grid.className = 'tg-grid';
    grid.style.fontSize = metaPx.toFixed(1) + 'px';
    grid.textContent = 'GRID: ' + (t.g != null ? String(t.g) : '—');
    row.appendChild(grid);

    const rng = document.createElement('div');
    rng.className = 'tg-rng';
    rng.style.fontSize = metaPx.toFixed(1) + 'px';
    rng.textContent = 'RNG: ' + fmtRng(t.r);
    row.appendChild(rng);

    tglPanel.appendChild(row);

    // Shrink to fit horizontally: scale both sizes by the tightest line.
    const avail = row.clientWidth;
    if (avail > 0) {
      const widest = Math.max(name.scrollWidth, grid.scrollWidth, rng.scrollWidth);
      if (widest > avail) {
        const k = avail / widest;
        namePx *= k; metaPx *= k;
        name.style.fontSize = namePx.toFixed(1) + 'px';
        grid.style.fontSize = metaPx.toFixed(1) + 'px';
        rng .style.fontSize = metaPx.toFixed(1) + 'px';
      }
    }
  }
}


// The map iframe broadcasts status + loadout + cm via postMessage; mirror onto the
// info-box (MAIN page), the cached wpnData + cmData (WPN page).
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'status') {
    lastStatusCls  = m.cls;
    lastStatusText = m.text;
    ibStatus.className = 'ib-status ' + m.cls;
    ibStatus.textContent = m.text;
    if (splitMode) forwardStatusToPanes();
  } else if (m.type === 'loadout') {
    wpnData = { items: m.items || [], selWeapon: m.selWeapon || null };
    if (currentPage === 'wpn') renderWpn();
  } else if (m.type === 'cm') {
    cmData = {
      flares:    typeof m.flares    === 'number' ? m.flares    : -1,
      flaresMax: typeof m.flaresMax === 'number' ? m.flaresMax : -1,
      ewKJ:      typeof m.ewKJ      === 'number' ? m.ewKJ      : -1,
      ewKJMax:   typeof m.ewKJMax   === 'number' ? m.ewKJMax   : -1,
      cmCat:     m.cmCat || 0
    };
    if (currentPage === 'wpn') renderCm();
  } else if (m.type === 'tgp') {
    tgpActive = !!m.active;
    // Only matters while the TGP page is in view — outside it the panel is hidden anyway.
    if (currentPage === 'tgp') tgpPanel.classList.toggle('has-feed', tgpActive);
    if (splitMode) forwardTgpToPanes();
  } else if (m.type === 'avn') {
    avnData = {
      name: m.name || null,
      parts: Array.isArray(m.parts) ? m.parts : null,
      failures: Array.isArray(m.failures) ? m.failures : null,
      fuel:     typeof m.fuel     === 'number' ? m.fuel     : -1,
      throttle: typeof m.throttle === 'number' ? m.throttle : -1,
    };
    if (currentPage === 'avn') {
      // Live repaint is cheap; only run a full re-layout when the aircraft type changes.
      if (avnLayoutType !== avnData.name) renderAvn();
      else { paintAvnDamage(); paintAvnFailures(); paintAvnBars(); }
    }
    if (splitMode) forwardAvnToPanes();
  } else if (m.type === 'follow') {
    // Map iframe broadcasts its follow state on toggle / mission clear. Tracked at the
    // MFD level so the FOLLOW chip lives in the same stack as PINNED instead of being
    // anchored to the iframe's own top-right corner.
    const on = !!m.on;
    if (on === followOn) return;
    followOn = on;
    if (on) { if (indicatorOrder.indexOf('follow') === -1) indicatorOrder.push('follow'); }
    else    { indicatorOrder = indicatorOrder.filter(function(x) { return x !== 'follow'; }); }
    renderIndicators();
  } else if (m.type === 'targets') {
    // Mirror the full target list. The renderer slices to TGL_MAX_DISPLAY; if any of the
    // first 10 got deselected, the next held-back targets slide in on the next render.
    tglData = { targets: Array.isArray(m.items) ? m.items : [] };
    if (currentPage === 'tgl') renderTgl();
  }
});

// Drive the map iframe without reaching into it (keeps the map a standalone component;
// also works cross-origin under file://).
function mapSend(action) {
  if (mapFrame && mapFrame.contentWindow)
    mapFrame.contentWindow.postMessage({ mfd: true, action: action }, '*');
}

function mfdButton(el) {
  el.classList.add('lit');                                   // brief press feedback
  setTimeout(function() { el.classList.remove('lit'); }, 150);

  // Split-mode line-select keys carry a data-pane tag (top/bot). The action on
  // them names a destination page; clicking navigates ONLY that pane.
  if (splitMode && el.dataset.pane && el.dataset.action) {
    const paneIdx = el.dataset.pane === 'top' ? 0 : 1;
    paneNavigate(paneIdx, el.dataset.action);
    return;
  }

  switch (el.dataset.action) {
    case 'main': showPage('main'); mapSend('status-request'); break;   // pull fresh status on open
    case 'map':  showPage('map');  break;
    case 'wpn':       wpnPage = 0; showPage('wpn'); break;   // fresh entry — start on page 0
    case 'wpn-prev':  wpnPage--;   showPage('wpn'); break;   // renderWpn clamps on overshoot
    case 'wpn-next':  wpnPage++;   showPage('wpn'); break;
    case 'tgp':  showPage('tgp');  break;
    case 'tgl':       tglPage = 0; showPage('tgl'); break;   // fresh entry — always start on page 0
    case 'tgl-prev':  tglPage--;   showPage('tgl'); break;   // renderTgl clamps if we overshoot
    case 'tgl-next':  tglPage++;   showPage('tgl'); break;
    case 'avn':  showPage('avn');  break;
    case 'flw':  mapSend('toggle-follow'); break;
    case 'zin':  mapSend('zoom-in');  break;
    case 'zout': mapSend('zoom-out'); break;
    case 'fll':  toggleFullscreen(); break;
    case 'split':
      // One-way: enter split if not already. Pressing 2×1 while already split is a no-op.
      // Collapse back to single uses the dedicated square (1×1) button below.
      if (splitMode) break;
      splitMode = true;
      // Carry the full-view page into the TOP pane; the BOTTOM pane defaults to MAIN.
      // Pages without a bare iframe version yet (no PAGE_URL entry) fall back to MAIN
      // so the top pane is never blank.
      panePages = [PAGE_URL[currentPage] ? currentPage : 'main', 'main'];
      applySplitMode();
      break;
    case 'unsplit':
      // One-way: collapse split back to single. No-op if already in single mode.
      // The full-screen pane adopts whatever the TOP pane was showing.
      if (!splitMode) break;
      splitMode = false;
      currentPage = panePages[0];
      applySplitMode();
      break;
    case 'swap':
      // Toggle between the pinned page and the last page we swapped from.
      //   - On a non-pinned page: remember it as the partner, jump to pinned.
      //   - On the pinned page with a known partner: jump back to the partner.
      //   - Otherwise (nothing pinned, or on pinned with no partner yet): no-op.
      if (pinnedPage === null) break;
      if (currentPage === pinnedPage) {
        if (swapPartner === null) break;
        showPage(swapPartner);
      } else {
        swapPartner = currentPage;
        showPage(pinnedPage);
      }
      break;
    case 'pin':
      // MENU ('main') page is not pinnable per design.
      if (currentPage === 'main') break;
      if (pinnedPage === currentPage) {
        // Toggle off: unpin and drop the chip from the activation order.
        pinnedPage = null;
        indicatorOrder = indicatorOrder.filter(function(x) { return x !== 'pinned'; });
      } else {
        // First time on, or switching the pin to a new page: append so we land to the
        // LEFT of any chip that was activated earlier (FOLLOW), and to the right of any
        // chip activated later in the same session.
        pinnedPage = currentPage;
        if (indicatorOrder.indexOf('pinned') === -1) indicatorOrder.push('pinned');
      }
      // The partner is tied to the previous pin — drop it whenever the pin itself
      // changes so a fresh SWAP cycle starts from the next non-pinned page.
      swapPartner = null;
      renderIndicators();
      break;
  }
}

// Toggle the browser's fullscreen mode on the whole page. Webkit prefix is for older Safari.
function toggleFullscreen() {
  const d = document, el = d.documentElement;
  if (!d.fullscreenElement && !d.webkitFullscreenElement) {
    (el.requestFullscreen || el.webkitRequestFullscreen || function(){}).call(el);
  } else {
    (d.exitFullscreen || d.webkitExitFullscreen || function(){}).call(d);
  }
}

// Event delegation covers both generated keys and standalone controls.
document.querySelector('.mfd').addEventListener('click', function(e) {
  const k = e.target.closest('.key');
  if (k) mfdButton(k);
});

window.addEventListener('resize', function() {
  // Orientation can flip on resize without matchMedia's 'change' always firing in every
  // environment, so refresh + re-broadcast here too (resize is guaranteed to fire).
  applyShellOrientation();
  broadcastOrientation();
  // Re-align labels to the (moved) bezel keys. In split mode the labels belong to the
  // per-pane layout, so re-run renderSplitLabels — calling showPage(currentPage) here
  // would clobber the split bezel with the single-pane page's full 6-item layout.
  if (splitMode) renderSplitLabels();
  else           showPage(currentPage);
});
showPage('main');   // start on the MAIN page
</script>
</body>
</html>
""";
    }
}
