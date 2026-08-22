// Boot fill-bar + typewriter URL reveal, shared by both shells (docs/refactor-scan.md step 1) — the
// two pieces mfd.js's runBootLoading/typewriterUrls and f35.js's runStripBoot/typeStripUrls each
// hand-rolled a copy of, f35.js's own comment already calling its copy a "port" of the bezel's.
//
// The two callers aren't byte-identical, just close: f35's strip has no equivalent of the bezel info
// box's centred, width-freezing container (its .ms-url already handles overflow via CSS
// text-overflow: ellipsis), so that behavior is opt-in via opts.pinWidthEl rather than forced on
// every caller. mfd.js also re-triggers typewriterUrls a second time if /config lands after boot
// already revealed the rows; f35.js's typeStripUrls never re-runs (single /config fetch at load) —
// the token-based supersede-guard below covers both, since it's a no-op for a caller that only ever
// calls once.
//
// Classic <script>, not a module, same as layout-store.js/layout-modal.js — a plain global, no
// build step.
(function (root) {
  // Fills fillEl's width 0% -> 100% in 5% steps every 50ms (a 60ms CSS transition on the fill
  // smooths each step into a continuous sweep, like the EW Jammer bar) and calls onComplete once,
  // at 100%.
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

  // Types each element in `lines` out character-by-character with a blinking cursor, one line after
  // another. Each line keeps its FULL text laid out the whole time — a visible "done" prefix + an
  // invisible "rest" suffix (visibility:hidden, so it still reserves space) — so neither width nor
  // height shifts as the text appears. Caches each element's original text in its own dataset.url
  // the first time it's seen, so a second call on the same elements (a re-run superseding an
  // in-flight type) re-types the real text rather than whatever partial spans the first run left
  // behind. opts.pinWidthEl, if given, gets its rendered width frozen for the duration — belt-and-
  // suspenders against the cursor glyph nudging a centred box; omit it for a container that already
  // handles overflow on its own (e.g. via CSS ellipsis).
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
      cur.style.display = '';                // reveal the blinking cursor on this line
      let i = 0;
      const timer = setInterval(function () {
        if (myToken !== revealToken) { clearInterval(timer); return; }
        i++;
        done.textContent = full.slice(0, i);
        rest.textContent = full.slice(i);
        if (i >= full.length) {
          clearInterval(timer);
          el.textContent = full;             // collapse spans back to plain text
          typeLine(idx + 1);                 // chain to the next line
        }
      }, 32);
    }
    typeLine(0);
  }

  const api = { runBootFill: runBootFill, typewriterReveal: typewriterReveal };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.BootReveal = api;
})(typeof self !== 'undefined' ? self : this);
