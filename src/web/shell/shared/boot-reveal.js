// Boot fill-bar + typewriter URL reveal, shared by both shells.
// pinWidthEl is opt-in: f35's .ms-url already clips via CSS ellipsis, so only the centered
// bezel box needs its width frozen during typing.
// The token supersede-guard below is a no-op for single-call sites; mfd.js relies on it when
// /config arrives after boot already revealed the rows and re-triggers the reveal.
//
// Classic <script>, not a module (like layout-store.js/layout-modal.js) — no build step.
(function (root) {
  // Steps fillEl's width 0-100%; a CSS transition on the fill smooths each step into a sweep.
  // Calls onComplete once, at 100%.
  function runBootFill(fillEl, onComplete) {
    if (!fillEl) return;
    let pct = 0;
    fillEl.style.width = '0%';
    const timer = setInterval(function () {
      pct += 5;
      fillEl.style.width = Math.min(pct, 100) + '%';
      if (pct >= 100) { clearInterval(timer); onComplete(); }
    }, 50);
  }

  // Types each element in `lines` char-by-char with a blinking cursor, one line after another.
  // Full text stays laid out the whole time (hidden "rest" span reserves its space) so width/height
  // never shift. Caches original text in dataset.url so a re-run retypes full text, not a partial span.
  let revealToken = 0;
  function typewriterReveal(lines, opts) {
    opts = opts || {};
    if (!lines.length) return;
    const myToken = ++revealToken;
    const pinEl = opts.pinWidthEl;
    if (pinEl) pinEl.style.width = pinEl.getBoundingClientRect().width + 'px';

    lines.forEach(function (el) {
      if (el.dataset.url === undefined) el.dataset.url = el.textContent;
      const full = el.dataset.url;
      el.textContent = '';
      const done = document.createElement('span'); done.className = 'tw-done';
      const cur  = document.createElement('span'); cur.className  = 'tw-cursor'; cur.textContent = '▌'; cur.style.display = 'none';
      const rest = document.createElement('span'); rest.className = 'tw-rest';  rest.textContent = full;
      el.appendChild(done); el.appendChild(cur); el.appendChild(rest);
    });

    function typeLine(idx) {
      if (myToken !== revealToken) return;   // superseded by a newer reveal
      if (idx >= lines.length) { if (pinEl) pinEl.style.width = ''; return; }
      const el = lines[idx];
      const done = el.children[0], cur = el.children[1], rest = el.children[2];
      const full = rest.textContent;
      cur.style.display = '';
      let i = 0;
      const timer = setInterval(function () {
        if (myToken !== revealToken) { clearInterval(timer); return; }
        i++;
        done.textContent = full.slice(0, i);
        rest.textContent = full.slice(i);
        if (i >= full.length) {
          clearInterval(timer);
          el.textContent = full;
          typeLine(idx + 1);
        }
      }, 32);
    }
    typeLine(0);
  }

  const api = { runBootFill: runBootFill, typewriterReveal: typewriterReveal };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.BootReveal = api;
})(typeof self !== 'undefined' ? self : this);
