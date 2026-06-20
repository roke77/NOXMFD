namespace NOTelemetryReader
{
    // A hardware-style Multi-Function Display: a rugged bezel with clickable (no-op) buttons
    // on all four sides plus corner controls, wrapping the existing map (served at /?bare) in
    // the central screen. Served at /mfd. The bezel is hardware-gray; the screen inside keeps
    // the green HUD theme because it's the existing page in an iframe.
    internal static class MfdPage
    {
        internal static readonly string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>NO Telemetry — MFD</title>
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
    padding: 18px;
    border-radius: 14px;
    background: linear-gradient(160deg, #3b3f45, #26282c);
    box-shadow: inset 0 1px 0 #5a5f66, inset 0 -2px 8px #15161a, 0 6px 22px rgba(0,0,0,0.55);
  }

  /* Strips share the screen's 3-column grid, so corner controls align with the side
     columns and the centre cell lines up exactly with the map screen. */
  .strip { display: grid; grid-template-columns: auto 1fr auto; gap: 10px; align-items: center; }
  .strip .center { display: flex; gap: 6px; min-width: 0; }
  .strip .center.right { justify-content: flex-end; }   /* pin cluster to the map's right edge */
  .mid   { display: grid; grid-template-columns: auto 1fr auto; gap: 10px; min-height: 0; }

  .keys   { display: flex; gap: 6px; }
  /* Vertical column: ridges + keys spread top-to-bottom; 6px inset matches the screen's
     padding so the first/last ridge line up with the map (iframe) top/bottom edges. */
  .keys.v { flex-direction: column; justify-content: space-between; gap: 0; padding: 6px 0; }
  .keys.v .key { flex: 0 0 auto; width: 36px; height: 46px; }   /* generic line-select keys */
  .corner { display: flex; gap: 6px; }

  /* White horizontal line marking inside each generic (side) key */
  .keys.v .key::before {
    content: '';
    width: 16px; height: 2px;
    background: #e8eaed;
    box-shadow: 0 0 2px rgba(255,255,255,0.35);
    border-radius: 1px;
  }

  /* Engraved separator ridge between side keys (visual only, not clickable) */
  .keys.v .sep { display: flex; align-items: center; }
  .keys.v .sep::before {
    content: '';
    width: 100%; height: 2px;
    background: #16181b;
    box-shadow: 0 1px 0 rgba(255,255,255,0.06), 0 -1px 0 rgba(0,0,0,0.45);
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

  /* Per-page line-select overlay inside the screen. Item labels are positioned by JS to
     line up with the left side keys. Transparent on the MAP page (overlays the map, which
     stays interactive), opaque black on the MAIN page (covers the map). pointer-events:none
     because the physical bezel keys are the controls — the labels are purely visual. */
  .overlay {
    position: absolute;
    inset: 6px;
    border-radius: 3px;
    pointer-events: none;
  }
  .overlay.opaque { background: #000; }
  .overlay-item {
    position: absolute;
    left: 16px;
    transform: translateY(-50%);
    color: #d4d8dc;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    font-size: 32px;
    font-weight: 900;
    letter-spacing: 2px;
  }
  /* On portrait viewports (mobile rotated upright) the screen grows much taller, which
     spreads the line-select keys further apart and makes a fixed 32px label look small
     in the now-bigger empty space. Scale the label with viewport height so it keeps the
     same visual prominence as in landscape. Clamped 32-64px so it never goes below the
     landscape baseline or grows absurdly large. */
  @media (orientation: portrait) {
    .overlay-item { font-size: clamp(32px, 3.6vh, 51px); }
  }

  /* MAIN page "about" card — name + URL + live connection status. Hidden on MAP page. */
  .info-box {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    display: none;
    min-width: 280px;
    padding: 22px 36px;
    border: 1px solid #39ff14;
    background: rgba(6, 10, 6, 0.9);
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    text-align: center;
    letter-spacing: 2px;
    box-shadow: 0 0 12px rgba(57, 255, 20, 0.25);
  }
  .info-box.show       { display: block; }
  .info-box .ib-title  { font-size: 28px; font-weight: 900; margin-bottom: 14px; }
  .info-box .ib-url    { font-size: 14px; color: #4aaa4a; margin-bottom: 14px; }
  .info-box .ib-status { font-size: 14px; font-weight: bold; }
  .info-box .ib-status.connected    { color: #39ff14; }
  .info-box .ib-status.disconnected { color: #ff4040; }
  .info-box .ib-status.waiting      { color: #ffaa00; }
  /* Portrait: scale the MAIN-page info card with viewport height so the box stays
     readable on tall screens. The three text rows scale in lockstep (preserves the
     original 28/14/14 ratio); padding scales too so the card grows proportionally,
     not just the text. */
  @media (orientation: portrait) {
    .info-box            { padding: clamp(22px, 2.4vh, 35px) clamp(36px, 4vh, 58px); }
    .info-box .ib-title  { font-size: clamp(28px, 3.12vh, 45px); margin-bottom: clamp(14px, 1.6vh, 22px); }
    .info-box .ib-url    { font-size: clamp(14px, 1.6vh, 22px); margin-bottom: clamp(14px, 1.6vh, 22px); }
    .info-box .ib-status { font-size: clamp(14px, 1.6vh, 22px); }
  }

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
  /* Font sizes are set inline by renderTgl() so the row fills its slot height; the name
     ends up 5/3 the meta size (i.e. "2/3 bigger"). Line-height is tight so the two lines
     reach the slot's top and bottom. */
  .tg-name { font-weight: bold; white-space: nowrap; line-height: 1.0; }
  /* GRID and RNG are stacked below the name and dimmed to de-emphasise vs the bright name. */
  .tg-grid,
  .tg-rng  { white-space: nowrap; line-height: 1.0; color: #2a8a2a; }

  .wpn-empty {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    color: #1a4a1a;
    font-size: 22px;
    letter-spacing: 3px;
  }
  /* Two-column grid: the weapon icon spans both rows on the left (taking the full slot
     height); the name and ammo stack on the right, one per row. Game weapon icons are
     2:1 — the aspect-ratio constraint sizes the icon column off the row height. */
  .wp-item {
    position: absolute;
    left: 90px;          /* clear the left line-select label gutter */
    right: 30px;
    display: grid;
    grid-template-columns: auto 1fr;
    grid-template-rows: 1fr 1fr;
    column-gap: 14px;
    padding: 4px 0;
    box-sizing: border-box;
    min-height: 0;
  }
  .wp-icon-wrap {
    grid-column: 1;
    grid-row: 1 / span 2;
    height: 100%;
    aspect-ratio: 2 / 1;
    min-height: 0;
  }
  .wp-icon {
    display: block;
    width: 100%; height: 100%;
    object-fit: contain;
    object-position: left center;
  }
  .wp-name {
    grid-column: 2; grid-row: 1;
    justify-self: start;                   /* hug text so .sel background is tight */
    align-self: end;                       /* anchor to bottom of row 1, near the centreline */
    max-width: 100%;
    padding: 0 6px;
    margin-left: -6px;
    font-size: 28px; font-weight: 900; letter-spacing: 1px;
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .wp-ammo {
    grid-column: 2; grid-row: 2;
    align-self: start;                     /* anchor to top of row 2, near the centreline */
    font-size: 20px; color: #4aaa4a; letter-spacing: 1px; margin-top: 2px;
  }
  .wp-ammo span { color: #39ff14; font-weight: 900; }

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
    grid-template-rows: auto 1fr auto;
    column-gap: 14px;
    row-gap: 3px;
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
  .cm-jammer-bar   { grid-column: 3; grid-row: 3; align-self: center; }
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
  #cm-jammer-val { grid-column: 3; grid-row: 2; align-self: stretch; justify-content: flex-start; }

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

  /* Decorative corner screws */
  .screw {
    position: absolute;
    width: 9px; height: 9px;
    border-radius: 50%;
    background: radial-gradient(circle at 35% 35%, #6b7077, #26282c);
    box-shadow: inset 0 0 2px #000;
  }
  .screw.tl { top: 6px; left: 6px; }
  .screw.tr { top: 6px; right: 6px; }
  .screw.bl { bottom: 6px; left: 6px; }
  .screw.br { bottom: 6px; right: 6px; }
</style>
</head>
<body>

<div class="mfd">
  <div class="bezel">
    <span class="screw tl"></span><span class="screw tr"></span>
    <span class="screw bl"></span><span class="screw br"></span>

    <div class="strip top">
      <div class="corner">
        <button class="key icon" type="button" title="Main"><span class="ic-square"></span></button>
      </div>
      <div class="center right">
        <button class="key icon" type="button" data-action="fll" title="Fullscreen"><span class="ic-fullscreen" aria-hidden="true"></span></button>
      </div>
      <div class="corner">
        <button class="key icon" type="button" title="Layout"><span class="ic-1x2"></span></button>
      </div>
    </div>

    <div class="mid">
      <div class="keys v" id="keys-left"></div>
      <div class="screen">
        <iframe src="/?bare" title="map"></iframe>
        <div class="overlay" id="overlay">
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
        </div>
      </div>
      <div class="keys v" id="keys-right"></div>
    </div>

    <div class="strip bottom">
      <div class="corner">
        <button class="key icon" type="button" title="Layout"><span class="ic-2x1"></span></button>
      </div>
      <div class="center"></div>
      <div class="corner">
        <button class="key icon" type="button" title="Layout"><span class="ic-split"></span></button>
      </div>
    </div>
  </div>
</div>

<script>
// Generate the line-select keys down the left and right sides (easy to tune).
// The top strip keeps only the labelled corner controls; there is no bottom strip.
const COUNTS = { 'keys-left': 6, 'keys-right': 6 };
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

const leftKeys = document.querySelectorAll('#keys-left .key');
const overlayEl = document.getElementById('overlay');
const mapFrame  = document.querySelector('.screen iframe');
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
// Every item names a label, the left key it aligns to (0 = topmost), and the action its
// key fires. The MAP page overlays its items on top of the (still-interactive) map; the
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
      { label: 'MAP', key: 0, action: 'map' },      // → MAP page
      { label: 'WPN', key: 1, action: 'wpn' },      // → WPN page
      { label: 'AVN', key: 2, action: 'avn' },      // → AVN page
      { label: 'RWR', key: 3, action: 'rwr' },      // → RWR page
      { label: 'TGP', key: 4, action: 'tgp' },      // → TGP page
      { label: 'TGL', key: 5, action: 'tgl' },      // → TGL page (target list)
    ],
  },
  wpn: {
    opaque: true,
    items: [
      { label: 'MAIN', key: 0, action: 'main' },    // ← back to MAIN
    ],
  },
  tgp: {
    opaque: true,
    items: [
      { label: 'MAIN', key: 0, action: 'main' },    // ← back to MAIN
    ],
  },
  tgl: {
    opaque: true,
    items: [
      { label: 'MAIN', key: 0, action: 'main' },    // ← back to MAIN
    ],
  },
};
let currentPage = 'map';

// Latest loadout snapshot mirrored from the map iframe (postMessage). Even when WPN isn't
// in view we keep it fresh, so opening the page renders immediately without a round-trip.
let wpnData      = { items: [], selWeapon: null };
let wpnNamesKey  = null;     // weapon-name signature — only rebuild the DOM when it changes
let wpnAmmoEls   = [];       // ammo text nodes, aligned with wpnData.items
let wpnItemEls   = [];       // .wp-item containers, aligned with wpnData.items

// Latest countermeasures snapshot mirrored from the map iframe.
let cmData = { flares: -1, flaresMax: -1, ewKJ: -1, ewKJMax: -1, cmCat: 0 };

// Latest TGP feed state mirrored from the map iframe. False until the first frame is
// produced, and back to false during the 3-second post-loss hold's expiry.
let tgpActive = false;

// Latest target list mirrored from the map iframe. Whole list is kept in memory; only the
// first 10 are displayed (left key 1..5, then right key 1..5). The MAIN back button owns
// left key 0; the future NEXT button will own right key 0.
let tglData = { targets: [] };
const TGL_MAX_DISPLAY = 10;

// Render a page: set the overlay background, (re)assign the left keys' actions, and
// position each item label next to its key.
function showPage(name) {
  currentPage = name;
  const page = PAGES[name];
  overlayEl.classList.toggle('opaque', page.opaque);
  infoBox.classList.toggle('show', name === 'main');
  wpnPanel.classList.toggle('show', name === 'wpn');
  tgpPanel.classList.toggle('show', name === 'tgp');
  tglPanel.classList.toggle('show', name === 'tgl');
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
  if (name === 'wpn') { renderWpn(); renderCm(); }
  if (name === 'tgl') { renderTgl(); }

  leftKeys.forEach(function(k) { delete k.dataset.action; });
  // Only wipe dynamic line-select labels; static children (info-box) stay put.
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });

  const oRect = overlayEl.getBoundingClientRect();
  page.items.forEach(function(item) {
    const k = leftKeys[item.key];
    if (!k) return;
    k.dataset.action = item.action;
    const el = document.createElement('div');
    el.className = 'overlay-item';
    el.textContent = item.label;
    const kr = k.getBoundingClientRect();
    el.style.top = (kr.top + kr.height / 2 - oRect.top) + 'px';
    overlayEl.appendChild(el);
  });
}

// Render the WPN page from the cached loadout. Each weapon row is absolutely positioned
// to fill the slot of one line-select key (starting at key[1] — key[0] is the MAIN back
// button), so the icon stretches to the maximum height available below name + ammo.
// Rebuilds the DOM (and refetches icons) only when the set of weapons changes; ammo
// text + selected highlight refresh in place.
function renderWpn() {
  const list    = wpnData.items || [];
  // Weapons fill keys 1..N (key 0 reserved for the MAIN back button). Each slot is the
  // gap between sep[i+1] (above) and sep[i+2] (below) — sep[0] is above key[0].
  const maxSlots = Math.max(0, sepEls.length - 2);
  const trimmed  = list.slice(0, maxSlots);

  if (!trimmed.length) {
    wpnEmptyEl.style.display = '';
    if (wpnNamesKey !== '') {
      wpnNamesKey = ''; wpnAmmoEls = []; wpnItemEls = [];
      wpnPanel.querySelectorAll('.wp-item').forEach(function(el) { el.remove(); });
    }
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

      const wrap = document.createElement('div');
      wrap.className = 'wp-icon-wrap';
      const img = document.createElement('img');
      img.className = 'wp-icon';
      img.alt = '';
      img.onerror = function() { img.style.visibility = 'hidden'; };   // no icon for this weapon
      img.src = '/weapon?name=' + encodeURIComponent(w.n);
      wrap.appendChild(img);
      item.appendChild(wrap);

      wpnItemEls.push(item);
      wpnPanel.appendChild(item);
    }
  }

  // Position each row to span between the separators flanking its line-select key.
  const panelRect = wpnPanel.getBoundingClientRect();
  for (let i = 0; i < wpnItemEls.length; i++) {
    const top = sepEls[i + 1].getBoundingClientRect();
    const bot = sepEls[i + 2].getBoundingClientRect();
    wpnItemEls[i].style.top    = (top.bottom - panelRect.top) + 'px';
    wpnItemEls[i].style.height = (bot.top - top.bottom) + 'px';
  }

  // Refresh ammo text + selected/depleted highlights in place (cheap, no DOM rebuild).
  for (let i = 0; i < trimmed.length && i < wpnAmmoEls.length; i++) {
    const w = trimmed[i];
    wpnAmmoEls[i].innerHTML = (w.f > 0) ? ('<span>' + w.a + '</span> / ' + w.f) : '';
    wpnItemEls[i].classList.toggle('sel',   w.n === wpnData.selWeapon);
    wpnItemEls[i].classList.toggle('empty', w.f > 0 && w.a === 0);
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
  cmJammerVal.textContent = (cmData.ewKJ   >= 0) ? (Math.round(cmData.ewKJ) + ' kJ') : '—';

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

  // Size the big readouts so the glyphs fill their cells' available height. Measure each
  // value's containing cell (the parent that's sized by the grid track) and scale to ~80%
  // of that — leaves some breathing room and accounts for line-height.
  function fitTextHeight(el, h) {
    if (h > 4) el.style.fontSize = Math.floor(h * 0.8) + 'px';
  }
  fitTextHeight(cmFlaresVal, cmFlaresVal.parentElement.getBoundingClientRect().height);
  fitTextHeight(cmJammerVal, cmJammerVal.getBoundingClientRect().height);
}

// Renders the TGL page from the cached target list. The first 10 targets are displayed,
// 1..5 down the left column (key 0 reserved for MAIN) and 6..10 down the right column
// (key 0 reserved for the future NEXT button). Anything past 10 stays in tglData.targets
// and is ignored until a displayed target drops out.
function renderTgl() {
  // Tear down any previously-rendered rows; the list is small, so rebuild beats diffing.
  tglPanel.querySelectorAll('.tg-item').forEach(function(el) { el.remove(); });

  const list = (tglData.targets || []).slice(0, TGL_MAX_DISPLAY);
  tglPanel.classList.toggle('has-targets', list.length > 0);
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
    row.className = 'tg-item ' + (onLeft ? 'left' : 'right');
    row.style.top    = (top.bottom - panelRect.top) + 'px';
    row.style.height = slotH + 'px';
    row.style.width  = sideW + 'px';

    // Initial sizes by slot height. Name is 5/3 the meta size ("2/3 bigger"). Three lines
    // stacked (name + GRID + RNG) — shrunk below if any line overflows the column width.
    let metaPx = Math.max(8, slotH * 0.1725);
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
    ibStatus.className = 'ib-status ' + m.cls;
    ibStatus.textContent = m.text;
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

  switch (el.dataset.action) {
    case 'main': showPage('main'); mapSend('status-request'); break;   // pull fresh status on open
    case 'map':  showPage('map');  break;
    case 'wpn':  showPage('wpn');  break;
    case 'tgp':  showPage('tgp');  break;
    case 'tgl':  showPage('tgl');  break;
    case 'flw':  mapSend('toggle-follow'); break;
    case 'zin':  mapSend('zoom-in');  break;
    case 'zout': mapSend('zoom-out'); break;
    case 'fll':  toggleFullscreen(); break;
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

// Event delegation covers both generated keys and the corner controls.
document.querySelector('.mfd').addEventListener('click', function(e) {
  const k = e.target.closest('.key');
  if (k) mfdButton(k);
});

window.addEventListener('resize', function() { showPage(currentPage); });   // re-align labels
showPage('main');   // start on the MAIN page
</script>
</body>
</html>
""";
    }
}
