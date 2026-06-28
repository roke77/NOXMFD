function setConfigUrls(cfg) {
  const box = document.querySelector('.info-box');
  const status = document.getElementById('ib-status');
  if (!box || !status || !status.parentNode) return;

  const urls = [cfg && cfg.localhost ? cfg.localhost : 'http://localhost:5005'];
  if (cfg && cfg.lanUrl) urls.push(cfg.lanUrl);

  Array.prototype.slice.call(box.querySelectorAll('.ib-url')).forEach(function(el) { el.remove(); });
  urls.forEach(function(url) {
    const el = document.createElement('div');
    el.className = 'ib-url';
    el.textContent = url;
    status.parentNode.insertBefore(el, status);
  });
}

function loadConfigUrls() {
  fetch('/config', { cache: 'no-store' })
    .then(function(r) { if (!r.ok) throw new Error('config'); return r.json(); })
    .then(setConfigUrls)
    .catch(function() {});
}

loadConfigUrls();

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
