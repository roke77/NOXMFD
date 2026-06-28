// TGP page — targeting-pod MJPEG feed. A pure reactive renderer driven by the shell over
// postMessage; single source of truth for BOTH layouts (full-screen iframe + split pane).
// The full-view overlay twin in MfdPage.cs is gone. See tgp.html for the message contract.

const tgpPanel = document.getElementById('tgp-panel');
const tgpImg   = document.getElementById('tgp-img');

// Start the MJPEG connection immediately; the server only emits frames while a target is
// locked, so the img stays hidden (NO LOCK shown) until the shell forwards active:true.
// MJPEG fires 'load' only once, so it can't detect a stall — but 'error' still covers the
// hard case where the connection breaks outright.
tgpImg.src = '/tgp.mjpg';
tgpImg.addEventListener('error', function() { tgpPanel.classList.remove('has-feed'); });

window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'tgp') {
    tgpPanel.classList.toggle('has-feed', !!m.active);
  } else if (m.type === 'orient') {
    // App-wide orientation forwarded by the shell — drives body.portrait/.landscape so any
    // orientation rules track the device, not the (wide+short) pane box.
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
  }
});
