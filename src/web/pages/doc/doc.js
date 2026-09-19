// DOM/fetch glue for DOC (issue #82). Stepping logic itself lives in doc-cycle.js (pure, tested).

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
    row.className = 'doc-row';
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
