// DOM/fetch glue for DOC (issue #82). Stepping logic itself lives in doc-cycle.js (pure, tested).
import { createPadCursor } from '/assets/services/pad-cursor.js';

if (window.parent !== window) {
  var back = document.querySelector('.doc-back');
  if (back) back.remove();
}

var indexEl   = document.getElementById('doc-index');
var rowsEl    = document.getElementById('doc-rows');
var emptyEl   = document.getElementById('doc-empty');
var imageEl   = document.getElementById('doc-image');
var imageImg  = document.getElementById('doc-image-img');
var imageName = document.getElementById('doc-image-name');

// The file currently open in the image view, or null while showing the index — also what
// doc-next/doc-prev step from, and the gate that makes them no-ops from the index (same "does
// nothing until there's something to act on" shape as MAP's S-/W- with no active route).
var currentName = null;

function renderRows(files) {
  rowsEl.innerHTML = '';
  files.forEach(function (name) {
    var row = document.createElement('div');
    row.className = 'doc-row pad-hoverable';
    row.textContent = name;
    row.onclick = function () { showImage(name); };
    rowsEl.appendChild(row);
  });
  emptyEl.style.display = files.length ? 'none' : 'block';
}

// Index-vs-image is page-internal UI state the shell has no other way to know (no game telemetry
// carries it), so it's reported up on every change — the shell needs it to decide whether to show
// INDX/PREV/NEXT on the bezel at all (mfd.js's docView/'doc-view' handling, docs/doc-page.md).
// A no-op when opened standalone (window.parent is this same window; posting to self would just
// loop back into the listener below for nothing).
function reportView() {
  if (window.parent !== window) {
    window.parent.postMessage({ mfd: true, type: 'doc-view', view: currentName === null ? 'index' : 'image' }, '*');
  }
}

function showIndexView() {
  currentName = null;
  imageEl.style.display = 'none';
  indexEl.style.display = '';
  reportView();
  fetch('/doc-list')
    .then(function (r) { return r.json(); })
    .then(renderRows)
    .catch(function () { renderRows([]); });
}

function showImage(name) {
  currentName = name;
  indexEl.style.display = 'none';
  imageEl.style.display = '';
  imageName.textContent = name;
  imageImg.src = '/doc-image?name=' + encodeURIComponent(name);
  reportView();
}

// NEXT/PREV re-read the live folder listing (issue #82's own decision — see doc.html's header
// comment) rather than reusing whatever list the index last rendered, so a file added or removed
// since is reflected immediately.
function cycle(dir) {
  if (currentName === null) return;   // no-op from the index — nothing open to step from
  fetch('/doc-list')
    .then(function (r) { return r.json(); })
    .then(function (files) {
      var next = DocCycle.step(files, currentName, dir);
      if (next === null) showIndexView();   // the folder emptied out from under us
      else showImage(next);
    })
    .catch(function () {});
}

window.addEventListener('message', function (e) {
  var m = e.data;
  if (!m || !m.mfd) return;
  if (m.action === 'doc-indx') showIndexView();
  else if (m.action === 'doc-next') cycle(1);
  else if (m.action === 'doc-prev') cycle(-1);
});

showIndexView();

// ── PAD cursor (docs/page-cursor.md, issue #104) ──────────────────────────────────────
// The same crosshair TGT/SQD use, live while DOC is the SOI's focused surface: move it with the
// Cursor binds and press Select on a file name to open it. Every file row has a real onclick, so
// Select is just a synthetic click at the crosshair's point. #pad-cursor is position:fixed
// (doc.css), since this page scrolls as a whole.
var CURSORABLE = '.pad-hoverable';
var cursor = createPadCursor({
  el: document.getElementById('pad-cursor'),
  clampRect: function () { return { dx: 0, dy: 0, dw: window.innerWidth, dh: window.innerHeight }; },
  onSelect: function (x, y) {
    var raw = document.elementFromPoint(x, y);
    var el = raw && raw.closest(CURSORABLE);
    if (el) el.click();
  },
  onMove: padCursorMoveAt,
});

// Hover feedback: the shared .pad-hoverable/.pad-hover pair (theme.css). The index is rebuilt on
// every visit, so a stale hoveredEl just fails the `===` check and is replaced on the next move.
var hoveredEl = null;
function padCursorMoveAt(x, y) {
  var raw = x == null ? null : document.elementFromPoint(x, y);
  var el = raw && raw.closest(CURSORABLE);
  if (el === hoveredEl) return;
  if (hoveredEl) hoveredEl.classList.remove('pad-hover');
  hoveredEl = el;
  if (hoveredEl) hoveredEl.classList.add('pad-hover');
}

// Cursor Zoom In/Out scroll the page (a long file list, or a diagram taller than the pane), as on
// WPT/TGT/SQD. The shell forwards these only once DOC is in PAD_CURSOR_PAGES (mfd.js/f35.js).
var SCROLL_STEP = 60;   // ponytail: flat constant tuned by feel, like pad-cursor.js's own SPEED
window.addEventListener('message', function (e) {
  var m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.action === 'cursor-focus') cursor.setFocus(!!m.on, window.innerWidth / 2, window.innerHeight / 2);
  else if (m.action === 'cursor') cursor.setVector(m.x, m.y);
  else if (m.action === 'cursor-select') cursor.select();
  else if (m.action === 'zoom-in') window.scrollBy({ top: SCROLL_STEP });
  else if (m.action === 'zoom-out') window.scrollBy({ top: -SCROLL_STEP });
});
