// LOAD LAYOUT (CLASSIC shell): the LYT page's SAVED section as a popup — every saved layout with its
// key box, edit, delete and LOAD, plus the empty slots (rows: pages/lyt/lyt-row.js). The dialog
// frame is the shared SAVE PRESET one (shared/preset-dialog.css, layout-edit-dialog.css).
//
//   LayoutLoadDialog.open(load)   load(id) applies that layout; the dialog closes first.
//
// Classic <script>, not a module, same as layout-store.js.
(function (root) {
  let dom = null;
  let load = null;
  let timer = null;

  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  function close() { if (dom) dom.scrim.hidden = true; }
  function isOpen() { return !!dom && !dom.scrim.hidden; }

  function refresh() {
    LayoutStore.list().then(function (data) {
      if (data.failed) {
        dom.ol.textContent = '';
        dom.ol.appendChild(el('li', 'led-message', "Can't reach the game, so saved layouts can't be read right now."));
        return;
      }
      LytRow.renderList(dom.ol, (data.layouts || []).filter(function (l) { return l.shell === 'classic'; }), handlers);
    });
  }

  // An edit or delete is queued for the plugin's main thread, so the list is read a moment after it.
  const handlers = {
    onLoad: function (row) { close(); load(row.id); },
    changed: function () { clearTimeout(timer); timer = setTimeout(refresh, 300); },
  };

  function build() {
    const scrim = el('div', 'preset-kp-scrim');
    scrim.hidden = true;
    const box = el('div', 'preset-kp led-wide');
    box.setAttribute('role', 'dialog');
    const list = el('div', 'led-list');
    const ol = el('ol');
    list.appendChild(ol);
    const actions = el('div', 'preset-kp-actions led-actions led-actions-one');
    const cancel = el('button', 'preset-kp-btn', 'CLOSE');
    cancel.type = 'button';
    cancel.addEventListener('click', close);
    actions.appendChild(cancel);
    [el('div', 'preset-kp-title', 'LOAD LAYOUT'), list, actions].forEach(function (n) { box.appendChild(n); });
    scrim.appendChild(box);
    document.body.appendChild(scrim);
    scrim.addEventListener('pointerdown', function (e) { if (e.target === scrim) close(); });
    // Escape belongs to whatever is on top: a key box mid-capture stops it in the capture phase, and
    // the edit dialog over this one closes itself first.
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && isOpen() && !LayoutEditDialog.isOpen()) { e.preventDefault(); close(); }
    });
    return { scrim: scrim, ol: ol };
  }

  function open(loadFn) {
    if (!dom) dom = build();
    load = loadFn;
    dom.scrim.hidden = false;
    refresh();
  }

  root.LayoutLoadDialog = { open: open, close: close, isOpen: isOpen };
})(typeof self !== 'undefined' ? self : this);
