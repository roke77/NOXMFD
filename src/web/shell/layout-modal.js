// Shared modal primitive (issue #51) — the first true overlay dialog in src/web/. The closest
// precedent before this was WPT's inline editRow (src/web/pages/wpt/wpt.js), an in-place text-input
// swap, not an overlay. Both shells (mfd.js, f35.js) load this and build SAVE's name-prompt and
// LOAD's layout-picker list from the one primitive here, rather than each rolling its own.
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

  function isOpen() { return !!openEl; }

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

  // LOAD's picker: a list of {id,name,...} items; picking one calls onPick(item) and closes.
  function pickList(titleText, items, onPick) {
    const body = document.createElement('div');
    body.className = 'layout-modal-body';
    if (!items.length) {
      const empty = document.createElement('div');
      empty.className = 'layout-modal-empty';
      empty.textContent = 'No saved layouts yet.';
      body.appendChild(empty);
    } else {
      const listEl = document.createElement('div');
      listEl.className = 'layout-modal-list';
      items.forEach(function (item) {
        const row = document.createElement('button');
        row.type = 'button';
        row.className = 'layout-modal-item';
        row.textContent = item.name;
        row.addEventListener('click', function () { close(); onPick(item); });
        listEl.appendChild(row);
      });
      body.appendChild(listEl);
    }
    body.appendChild(makeActions([{ label: 'Cancel', onClick: close }]));
    open(titleText, body);
  }

  const api = { open: open, close: close, isOpen: isOpen, prompt: prompt, pickList: pickList };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutModal = api;
})(typeof self !== 'undefined' ? self : this);
