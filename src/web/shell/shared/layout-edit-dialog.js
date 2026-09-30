// The layout dialog: one popup for editing a saved layout (LYT page) and for saving the current
// arrangement (SAVE LAYOUT key, CLASSIC shell). Same scrim/dialog/entry/buttons as the TGT and HUD
// SAVE PRESET dialog (shared/preset-dialog.css), plus a lit toggle per pane for its place in the SOI
// rotation. Styles: layout-edit-dialog.css.
//
// Classic <script>, not a module, same as layout-store.js — a plain global, no build step.
//
//   LayoutEditDialog.open({
//     title,                     the dialog's heading ("EDIT LAYOUT 2")
//     name,                      the text entry's initial value
//     pendingKey,                a box under the entry that records a key and hands it to onSubmit
//                                (for when the slot is only known at submit time)
//     panes: [{label, on}],      one lit toggle each; omit for none
//     notice,                    instead of the form: just this text and a CLOSE button
//     message,                   a line of text under the toggles (optional)
//     buildList(choose),         returns an element shown under the message — the list of slots to
//                                choose from; its radio buttons call choose(id, submitLabel), which
//                                also relabels the green button. Submitting then needs a choice, and
//                                onSubmit gets it as a third argument.
//     submitLabel,               the green button's label (default SAVE)
//     onSubmit(name, flags, id, key), flags = each toggle's state, in order; id = the list's choice;
//                                key = the pendingKey box's key name ('' when none was set)
//   })
//
// CANCEL, Escape or a click on the scrim closes it; Enter in the entry submits.
(function (root) {
  let dom = null;       // built on first open
  let current = null;   // {spec, flags} while open

  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  function build() {
    const scrim = el('div', 'preset-kp-scrim led-scrim');
    scrim.hidden = true;
    const box = el('div', 'preset-kp');
    box.setAttribute('role', 'dialog');
    const title = el('div', 'preset-kp-title');
    const input = el('input', 'preset-kp-input');
    input.type = 'text'; input.maxLength = 60; input.autocomplete = 'off'; input.spellcheck = false;
    const error = el('div', 'preset-kp-error');
    const soi = el('div', 'led-soi');
    soi.appendChild(el('div', 'preset-kp-title', 'INCLUDE IN SOI ROTATION'));
    const toggles = el('div', 'led-toggles');
    soi.appendChild(toggles);
    const key = el('div', 'led-key');
    const keyTitle = el('div', 'preset-kp-title');
    key.appendChild(keyTitle);
    const message = el('div', 'led-message');
    const list = el('div', 'led-list');
    const actions = el('div', 'preset-kp-actions led-actions');
    const cancel = el('button', 'preset-kp-btn', 'CANCEL');
    const submit = el('button', 'preset-kp-btn save');
    cancel.type = submit.type = 'button';
    actions.appendChild(cancel);
    actions.appendChild(submit);
    [title, input, error, key, soi, message, list, actions].forEach(function (n) { box.appendChild(n); });
    scrim.appendChild(box);
    document.body.appendChild(scrim);

    input.addEventListener('input', function () { setError(''); });
    cancel.addEventListener('click', close);
    submit.addEventListener('click', send);
    scrim.addEventListener('pointerdown', function (e) { if (e.target === scrim) close(); });
    document.addEventListener('keydown', function (e) {
      if (!current) return;
      // A key still held from the press that opened the dialog repeats into the entry; it must not submit.
      if (e.key === 'Enter' && e.target === input) { e.preventDefault(); if (!e.repeat) send(); }
      else if (e.key === 'Escape') { e.preventDefault(); close(); }
    });
    return { scrim, box, title, message, list, input, error, soi, toggles, key, keyTitle, cancel, submit };
  }

  function setError(text) {
    dom.input.classList.toggle('bad', !!text);
    dom.error.textContent = text;
  }

  function close() {
    current = null;
    if (dom) dom.scrim.hidden = true;
  }

  function send() {
    const spec = current.spec;
    const name = dom.input.value.trim();
    if (!name) { setError('Enter a name.'); return; }
    if (spec.buildList && current.chosen == null) { setError('Pick a layout to replace.'); return; }
    const flags = current.flags, chosen = current.chosen;
    const key = current.pendingBox ? current.pendingBox.value() : '';
    close();
    spec.onSubmit(name, flags, chosen, key);
  }

  function open(spec) {
    if (!dom) dom = build();
    current = { spec: spec, chosen: null, pendingBox: null, flags: (spec.panes || []).map(function (p) { return !!p.on; }) };
    dom.title.textContent = spec.title;
    dom.input.value = spec.name || '';
    dom.input.placeholder = 'LAYOUT NAME';
    setError('');
    const form = !spec.notice;
    dom.input.hidden = dom.error.hidden = dom.submit.hidden = !form;
    dom.cancel.textContent = form ? 'CANCEL' : 'CLOSE';
    dom.soi.hidden = !(form && spec.panes && spec.panes.length);
    dom.toggles.textContent = '';
    (spec.panes || []).forEach(function (p, i) {
      const t = el('button', 'led-toggle', p.label);
      t.type = 'button';
      t.setAttribute('aria-pressed', String(current.flags[i]));
      t.addEventListener('click', function () {
        current.flags[i] = !current.flags[i];
        t.setAttribute('aria-pressed', String(current.flags[i]));
      });
      dom.toggles.appendChild(t);
    });
    // A fresh key box each time, so it starts unset.
    [].slice.call(dom.key.querySelectorAll('.layout-modal-kb')).forEach(function (n) { n.remove(); });
    dom.key.hidden = !(form && spec.pendingKey);
    if (form && spec.pendingKey) {
      dom.keyTitle.textContent = 'KEY FOR THIS LAYOUT';
      current.pendingBox = LayoutKeybinds.pendingKeyBox('SET KEY');
      dom.key.appendChild(current.pendingBox.el);
    }
    dom.message.textContent = spec.notice || spec.message || '';
    dom.message.hidden = !(spec.notice || spec.message);
    dom.list.textContent = '';
    dom.list.hidden = !spec.buildList;
    if (spec.buildList) {
      dom.list.appendChild(spec.buildList(function (id, label) {
        current.chosen = id;
        if (label) dom.submit.textContent = label;
        setError('');
      }));
    }
    dom.box.classList.toggle('led-wide', !!spec.buildList);
    dom.submit.textContent = spec.submitLabel || 'SAVE';
    dom.scrim.hidden = false;
    if (form) { dom.input.focus(); dom.input.select(); }
  }

  root.LayoutEditDialog = { open: open, close: close, isOpen: function () { return !!current; } };
})(typeof self !== 'undefined' ? self : this);
