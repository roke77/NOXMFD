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

  const versionEl = document.getElementById('ib-version');
  if (versionEl && cfg && cfg.version) versionEl.textContent = 'v' + cfg.version;
}

function loadConfigUrls() {
  fetch('/config', { cache: 'no-store' })
    .then(function(r) { if (!r.ok) throw new Error('config'); return r.json(); })
    .then(setConfigUrls)
    .catch(function() {});
}

loadConfigUrls();

// Status arrives via postMessage from the shell, which already mirrors it from the
// map iframe's SSE stream — this page never opens its own SSE connection.
const ibStatus = document.getElementById('ib-status');
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'status') {
    ibStatus.className = 'ib-status mfd-status ' + m.cls;
    ibStatus.textContent = m.text;
  } else if (m.type === 'orient') {
    // Forwarded by the shell: this pane's own box is wide+short regardless of app
    // orientation, so it can't detect portrait/landscape itself.
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
  }
});
