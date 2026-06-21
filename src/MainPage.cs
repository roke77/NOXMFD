namespace NORoksMFD
{
    // Bare MAIN page served at /main. Renders only the centred info-box card
    // (NO ROKS MFD / URL / connection-status placard). Used by the split-screen
    // layout so MAIN can be embedded in either pane via an <iframe>; the shell
    // handles bezel labels + navigation, so this page only needs to display the
    // card and listen for status updates the shell pushes via postMessage.
    internal static class MainPage
    {
        public const string Html = """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>NO Roks MFD — MAIN</title>
<style>
  html, body { margin: 0; height: 100%; background: #000; overflow: hidden; }
  body {
    display: flex;
    align-items: center;
    justify-content: center;
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
  }
  /* Card sized smaller than the single-pane info-box because the iframe lives in
     half the screen height in split mode — clamps scale with the iframe's own vh,
     so the card gracefully shrinks as the pane height drops. */
  .info-box {
    min-width: 200px;
    padding: clamp(14px, 2vh, 26px) clamp(22px, 3vh, 44px);
    border: 1px solid #39ff14;
    background: rgba(6, 10, 6, 0.9);
    box-shadow: 0 0 12px rgba(57, 255, 20, 0.25);
    text-align: center;
  }
  .ib-title  { font-size: clamp(20px, 2.6vh, 32px); font-weight: 900; margin-bottom: clamp(8px, 1.2vh, 14px); }
  .ib-url    { font-size: clamp(11px, 1.4vh, 18px); color: #4aaa4a; margin-bottom: clamp(8px, 1.2vh, 14px); }
  .ib-status { font-size: clamp(11px, 1.4vh, 18px); font-weight: bold; }
  .ib-status.connected    { color: #39ff14; }
  .ib-status.disconnected { color: #ff4040; }
  .ib-status.waiting      { color: #ffaa00; }
</style>
</head>
<body>
  <div class="info-box">
    <div class="ib-title">NO ROKS MFD</div>
    <div class="ib-url">http://localhost:5005</div>
    {{LAN_URL_BLOCK}}
    <div class="ib-status disconnected" id="ib-status">&#9679; DISCONNECTED</div>
  </div>
<script>
// Status comes from the shell via postMessage — the shell already mirrors it from
// the embedded map iframe (the SSE source), so this page doesn't open its own
// stream. The shell pushes the latest status both on every change and on this
// iframe's 'load' event.
const ibStatus = document.getElementById('ib-status');
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'status') {
    ibStatus.className = 'ib-status ' + m.cls;
    ibStatus.textContent = m.text;
  } else if (m.type === 'orient') {
    // App-wide orientation forwarded by the shell — a pane iframe can't read it from its
    // own (wide+short) box, so the shell tells it. Drives body.portrait/.landscape rules.
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
