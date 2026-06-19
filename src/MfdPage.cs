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
  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body { height: 100%; }
  body {
    background: #0d0f11;
    font-family: 'Courier New', monospace;
    overflow: hidden;
  }

  .mfd { position: fixed; inset: 0; display: flex; padding: 14px; }

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
  .key.sun  { color: #ffcc66; }

  /* Burger (menu) icon: three stacked bars, drawn from one bar + two shadows */
  .ic-burger {
    width: 16px; height: 2px;
    background: currentColor;
    box-shadow: 0 -5px 0 currentColor, 0 5px 0 currentColor;
  }
  /* 2x1 layout icon: a box split into a wide (2/3) left pane and a narrow (1/3) right pane */
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
  /* 2x2 grid icon: a square split into four equal cells */
  .ic-grid {
    position: relative;
    width: 16px; height: 16px;
    border: 1px solid currentColor;
    border-radius: 1px;
  }
  .ic-grid::before, .ic-grid::after { content: ''; position: absolute; background: currentColor; }
  .ic-grid::before { left: 50%; top: 0; bottom: 0; width: 1px; }
  .ic-grid::after  { top: 50%; left: 0; right: 0; height: 1px; }

  /* Inset screen recess holding the map iframe */
  .screen {
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
        <button class="key icon" type="button" title="Menu"><span class="ic-burger"></span></button>
      </div>
      <div class="center right">
        <button class="key icon" type="button" title="Brightness down">&minus;</button>
        <button class="key icon sun" type="button" title="Brightness">&#9728;</button>
        <button class="key icon" type="button" title="Brightness up">+</button>
      </div>
      <div class="corner">
        <button class="key icon" type="button" title="Grid layout"><span class="ic-grid"></span></button>
      </div>
    </div>

    <div class="mid">
      <div class="keys v" id="keys-left"></div>
      <div class="screen"><iframe src="/?bare" title="map"></iframe></div>
      <div class="keys v" id="keys-right"></div>
    </div>

    <div class="strip bottom">
      <div class="corner">
        <button class="key icon" type="button" title="Display">&#9744;</button>
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

// Buttons are clickable but do nothing yet — just a brief "lit" press feedback.
function mfdButton(el) {
  el.classList.add('lit');
  setTimeout(function() { el.classList.remove('lit'); }, 150);
  // TODO: wire real actions here.
}

// Event delegation covers both generated keys and the corner controls.
document.querySelector('.mfd').addEventListener('click', function(e) {
  const k = e.target.closest('.key');
  if (k) mfdButton(k);
});
</script>
</body>
</html>
""";
    }
}
