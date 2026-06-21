namespace NORoksMFD
{
    // Bare TGP page served at /tgp. Renders the targeting-pod MJPEG feed full-iframe
    // with the "NO LOCK" placeholder when no target is being tracked. Used by the
    // split-screen layout when a pane shows TGP; the shell forwards the live tgpActive
    // flag (mirrored from the map iframe's SSE feed) via postMessage so the panel
    // toggles its has-feed class in lockstep with single-pane behaviour.
    internal static class TgpPage
    {
        public const string Html = """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>NO Roks MFD — TGP</title>
<style>
  html, body { margin: 0; height: 100%; background: #000; overflow: hidden; }
  body {
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    position: relative;
  }
  .tgp-panel {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    width: 100%;
    aspect-ratio: 3 / 2;
    max-width: 100%;
    max-height: 100%;
    background: #000;
  }
  .tgp-img {
    display: block;
    width: 100%;
    height: 100%;
    object-fit: contain;
    image-rendering: auto;
  }
  /* Hide the <img> when the feed is dead — MJPEG buffers the last frame, so without
     this the player would see a frozen stale picture instead of the NO LOCK
     placeholder. */
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
</style>
</head>
<body>
  <div class="tgp-panel" id="tgp-panel">
    <div class="tgp-empty">&mdash; NO LOCK &mdash;</div>
    <img class="tgp-img" id="tgp-img" alt="">
  </div>
<script>
const tgpPanel = document.getElementById('tgp-panel');
const tgpImg   = document.getElementById('tgp-img');
// Start the MJPEG connection immediately; the server only emits frames while a
// target is locked, so the img stays hidden until tgpActive flips true.
tgpImg.src = '/tgp.mjpg';
tgpImg.addEventListener('error', function() { tgpPanel.classList.remove('has-feed'); });
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'tgp') {
    tgpPanel.classList.toggle('has-feed', !!m.active);
  } else if (m.type === 'orient') {
    // App-wide orientation forwarded by the shell — drives body.portrait/.landscape so
    // any orientation rules track the device, not the (wide+short) pane box.
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
  }
});
</script>
</body>
</html>
""";
    }
}
