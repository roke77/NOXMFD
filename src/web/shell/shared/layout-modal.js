// Shared modal primitive — the only overlay dialog in src/web/ (contrast WPT's inline editRow in
// src/web/pages/wpt/wpt.js, an in-place text-input swap, not an overlay). Both shells (mfd.js,
// f35.js) load this and build SAVE's name-prompt and LOAD's layout-picker list from the one
// primitive here, rather than each rolling its own.
//
// Classic <script>, not a module, same as layout-store.js — a plain global, no build step.
(function (root) {
  let openEl = null;   // the current modal's backdrop, or null — also doubles as "one is open"

  function close() {
    if (!openEl) return;
    openEl.remove();
    openEl = null;
    document.removeEventListener('keydown', onKey, true);
  }

  function onKey(e) {
    if (e.key === 'Escape') { e.preventDefault(); close(); }
  }

  // contentEl: the panel's body, built by the caller (prompt/pickList below, or a future one-off).
  function open(titleText, contentEl) {
    close();   // only one at a time
    const backdrop = document.createElement('div');
    backdrop.className = 'layout-modal-backdrop';
    backdrop.addEventListener('mousedown', function (e) { if (e.target === backdrop) close(); });

    const panel = document.createElement('div');
    panel.className = 'layout-modal';
    const title = document.createElement('div');
    title.className = 'layout-modal-title';
    title.textContent = titleText;
    panel.appendChild(title);
    panel.appendChild(contentEl);
    backdrop.appendChild(panel);
    document.body.appendChild(backdrop);
    document.addEventListener('keydown', onKey, true);
    openEl = backdrop;
    return panel;
  }

  function makeActions(buttons) {
    const actions = document.createElement('div');
    actions.className = 'layout-modal-actions';
    buttons.forEach(function (spec) {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'layout-modal-btn' + (spec.primary ? ' primary' : '');
      b.textContent = spec.label;
      b.addEventListener('click', spec.onClick);
      actions.appendChild(b);
    });
    return actions;
  }

  // SAVE's name-prompt: a text input + Cancel/Save. onSubmit(name) fires on Enter or Save; an
  // empty/whitespace-only name is rejected inline instead of saving a blank one.
  function prompt(titleText, onSubmit) {
    const body = document.createElement('div');
    body.className = 'layout-modal-body';
    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'layout-modal-input';
    input.placeholder = 'Layout name';
    input.maxLength = 60;
    const err = document.createElement('div');
    err.className = 'layout-modal-error';

    function submit() {
      const name = input.value.trim();
      if (!name) { err.textContent = 'Enter a name.'; return; }
      onSubmit(name);
    }
    input.addEventListener('keydown', function (e) { if (e.key === 'Enter') submit(); });

    body.appendChild(input);
    body.appendChild(err);
    body.appendChild(makeActions([
      { label: 'Cancel', onClick: close },
      { label: 'Save', primary: true, onClick: submit },
    ]));
    open(titleText, body);
    input.focus();
  }

  // LOAD's picker: a list of {id,name,...} items, refetched fresh each time it needs to redraw
  // (fetchItems() returns a Promise<items>) so a rename/delete shows up without closing the modal.
  //   opts.onPick(item)          — required. Picking a row closes the modal and applies it.
  //   opts.onRename(item, name)  — optional. Adds a pencil button per row: an inline text-input
  //                                 swap (mirrors WPT's own editRow — wpt.js), Enter/✓ to commit.
  //   opts.onDelete(item)        — optional. Adds a "×" button per row, no confirm step (mirrors
  //                                 WPT's own route/waypoint delete — low-stakes, easy to redo).
  // Both mutation callbacks return a promise; the list redraws (fetchItems again) once it resolves.
  function pickList(titleText, fetchItems, opts) {
    const body = document.createElement('div');
    body.className = 'layout-modal-body';
    const listEl = document.createElement('div');
    listEl.className = 'layout-modal-list';
    body.appendChild(listEl);
    body.appendChild(makeActions([{ label: 'Cancel', onClick: close }]));

    function refresh() { fetchItems().then(draw); }

    function draw(items) {
      listEl.textContent = '';
      if (!items.length) {
        const empty = document.createElement('div');
        empty.className = 'layout-modal-empty';
        empty.textContent = 'No saved layouts yet.';
        listEl.appendChild(empty);
        return;
      }
      items.forEach(function (item) { listEl.appendChild(buildRow(item)); });
    }

    function buildRow(item) {
      const row = document.createElement('div');
      row.className = 'layout-modal-row';

      const name = document.createElement('button');
      name.type = 'button';
      name.className = 'layout-modal-item';
      // item.display is optional (HUD presets use it for a "PRESET N: name" row label distinct from
      // the raw name a rename edits) — falls back to name when unset.
      name.textContent = item.display != null ? item.display : item.name;
      name.addEventListener('click', function () { close(); opts.onPick(item); });
      row.appendChild(name);

      if (opts.onRename) {
        const edit = document.createElement('button');
        edit.type = 'button';
        edit.className = 'layout-modal-row-btn';
        edit.textContent = '✎';
        edit.title = 'Rename';
        edit.addEventListener('click', function () { editRow(row, item); });
        row.appendChild(edit);
      }
      if (opts.onDelete) {
        const del = document.createElement('button');
        del.type = 'button';
        del.className = 'layout-modal-row-btn';
        del.textContent = '×';
        del.title = 'Delete';
        del.addEventListener('click', function () { opts.onDelete(item).then(refresh); });
        row.appendChild(del);
      }
      return row;
    }

    // Swaps a row for a text input + ✓, in place — same shape as wpt.js's editRow. Enter commits;
    // Escape or losing the rename discards by just redrawing from the last fetch.
    function editRow(row, item) {
      row.textContent = '';
      const input = document.createElement('input');
      input.type = 'text';
      input.className = 'layout-modal-input';
      input.maxLength = 60;
      input.value = item.name;
      const save = document.createElement('button');
      save.type = 'button';
      save.className = 'layout-modal-row-btn';
      save.textContent = '✓';
      save.title = 'Save';
      function commit() {
        const name = input.value.trim();
        if (!name) return;
        opts.onRename(item, name).then(refresh);
      }
      save.addEventListener('click', commit);
      // No local Escape handling: the modal's own document-level Escape listener is capture-phase
      // (runs before any listener on this input) and already closes the whole modal.
      input.addEventListener('keydown', function (e) { if (e.key === 'Enter') commit(); });
      row.appendChild(input);
      row.appendChild(save);
      input.focus();
      input.select();
    }

    open(titleText, body);
    refresh();
  }

  const api = { open: open, close: close, prompt: prompt, pickList: pickList };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutModal = api;
})(typeof self !== 'undefined' ? self : this);
