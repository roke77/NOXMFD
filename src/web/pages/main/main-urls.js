// The URL rows of the classic MAIN card: the full-view card in the shell (mfd.js setInfoUrls) and
// the split-pane card (main.js). With a name URL, that is the only address shown, plus a gray
// "URL not working? Check here" hint whose link reveals the PC (localhost) and NET (LAN IP)
// addresses. Without one (LAN access off, or no usable PC name) both plain addresses show as rows.
//
// Classic <script> (global MainUrls), not a module: the shell has no module graph.
(function (root) {
  // Pure: /config → { urls: rows to type out, alts: [{ label, url }] behind the hint }.
  function plan(cfg) {
    const c = cfg || {};
    const local = c.localhost || 'http://localhost:5005';
    if (!c.nameUrl) return { urls: c.lanUrl ? [local, c.lanUrl] : [local], alts: [] };
    const alts = [{ label: 'PC', url: local }];
    if (c.lanUrl) alts.push({ label: 'NET', url: c.lanUrl });
    return { urls: [c.nameUrl], alts: alts };
  }

  function div(className, text) {
    const el = document.createElement('div');
    el.className = className;
    if (text) el.textContent = text;
    return el;
  }

  // Replaces the card's URL rows, inserted before `beforeEl` (the status line). Rows are .ib-url,
  // so the shell's typewriter still finds them; the hint and its reveal are not typed.
  function render(cfg, beforeEl) {
    const parent = beforeEl.parentNode;
    [].slice.call(parent.querySelectorAll('.ib-url, .ib-hint, .ib-alt')).forEach(function (el) { el.remove(); });
    const p = plan(cfg);
    p.urls.forEach(function (url) { parent.insertBefore(div('ib-url', url), beforeEl); });
    if (!p.alts.length) return;

    const alt = div('ib-alt');
    alt.id = 'ib-alt';
    alt.hidden = true;
    p.alts.forEach(function (a) { alt.appendChild(div('ib-alt-url', a.label + ': ' + a.url)); });

    const link = document.createElement('button');
    link.type = 'button';
    link.className = 'ib-hint-link';
    link.textContent = 'here';
    link.setAttribute('aria-controls', alt.id);
    link.setAttribute('aria-expanded', 'false');
    link.addEventListener('click', function () {
      alt.hidden = !alt.hidden;
      link.setAttribute('aria-expanded', String(!alt.hidden));
    });

    const hint = div('ib-hint', 'URL not working? Check ');
    hint.appendChild(link);
    parent.insertBefore(hint, beforeEl);
    parent.insertBefore(alt, beforeEl);
  }

  const api = { plan: plan, render: render };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.MainUrls = api;
})(typeof self !== 'undefined' ? self : this);
