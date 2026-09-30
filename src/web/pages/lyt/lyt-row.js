// A saved layout as a row: its slot badge, a thumbnail of the panes (gray where the layout leaves
// one out of the SOI rotation), its name and its split / pages — then whatever controls the host
// adds. The LYT page's SAVED list and the CLASSIC shell's save and load popups share it
// (lyt-rows.css is the look). `row` comes from LytSlots.describeLayout.
//
// Classic <script> (global LytRow), not a module, for the shell's sake.
(function (root) {
  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  function slotBadge(n) {
    const slot = el('span', 'lyt-slot');
    slot.appendChild(el('span', 'lyt-slot-label', 'LAYOUT'));
    slot.appendChild(el('span', 'lyt-slot-num', n == null ? '–' : String(n)));
    if (n == null) slot.classList.add('none');
    return slot;
  }

  function thumb(row) {
    const t = el('span', 'lyt-row-thumb');
    row.rects.forEach(function (r, i) {
      const pane = el('span', 'lyt-pane' + (row.soi[i] ? '' : ' out'), row.pages[i]);
      // 1px gutters between panes come from the border; the rect is the pane's share of the thumb.
      pane.style.cssText = 'left:' + r[0] * 100 + '%;top:' + r[1] * 100 + '%;width:' + r[2] * 100 + '%;height:' + r[3] * 100 + '%';
      t.appendChild(pane);
    });
    return t;
  }

  // controls: elements appended after the text, in order.
  function build(row, controls) {
    const li = el('li', 'lyt-row');
    li.appendChild(slotBadge(row.slot));
    li.appendChild(thumb(row));
    const text = el('span', 'lyt-row-text');
    text.appendChild(el('span', 'lyt-row-name', row.name));
    text.appendChild(el('span', 'lyt-row-sub', row.label + (row.pages.length ? ' · ' + row.pages.join(' | ') : '')));
    li.appendChild(text);
    (controls || []).forEach(function (c) { li.appendChild(c); });
    return li;
  }

  // A slot with no layout yet.
  function buildEmpty(n, controls) {
    const li = el('li', 'lyt-row empty');
    li.appendChild(slotBadge(n));
    li.appendChild(el('span', 'lyt-row-thumb'));
    const text = el('span', 'lyt-row-text');
    text.appendChild(el('span', 'lyt-row-name', 'EMPTY'));
    li.appendChild(text);
    (controls || []).forEach(function (c) { li.appendChild(c); });
    return li;
  }

  function iconButton(glyph, label, onClick, extraCls) {
    const b = el('button', 'lyt-icon' + (extraCls ? ' ' + extraCls : ''), glyph);
    b.type = 'button';
    b.title = label;
    b.setAttribute('aria-label', label);
    b.addEventListener('click', onClick);
    return b;
  }

  // The pencil: name and SOI-rotation toggles in the layout dialog (layout-edit-dialog.js).
  // done() runs once the change is sent.
  function edit(row, done) {
    LayoutEditDialog.open({
      title: 'EDIT LAYOUT ' + row.pos,
      name: row.name,
      // Unreadable data has no panes to choose from.
      panes: (row.soi || []).map(function (on, i) { return { label: row.sides[i], on: on }; }),
      onSubmit: function (name, flags) {
        // An arrangement that can't be read is renamed only; its data is left as it is.
        const saved = row.state
          ? LayoutStore.update(row.id, name, Object.assign({}, row.state, row.soi ? { soi: flags } : {}))
          : LayoutStore.rename(row.id, name);
        saved.then(done).catch(LayoutStore.warn('edit'));
      },
    });
  }

  // A saved layout with its controls: the slot's key box, edit, delete and LOAD. h.onLoad(row) runs
  // on LOAD; h.changed() after an edit or delete has been sent.
  function buildSaved(row, h) {
    const load = el('button', 'lyt-load', 'LOAD');
    load.type = 'button';
    load.addEventListener('click', function () { h.onLoad(row); });
    return build(row, [
      // The slot's Layout N key box; beyond slot 5 there is nothing to bind.
      row.slot == null ? el('span', 'lyt-noslot', 'NO KEY SLOT') : LayoutKeybinds.slotBox(row.slot, 'SET KEY'),
      iconButton('✎', 'Edit ' + row.name, function () { edit(row, h.changed); }),
      iconButton('×', 'Delete ' + row.name, function () {
        LayoutStore.remove(row.id).then(h.changed).catch(LayoutStore.warn('delete'));
      }, 'danger'),
      load,
    ]);
  }

  // The SAVED section's rows: every saved layout, then empty slots up to the five that have keys.
  function renderList(ol, layouts, h) {
    ol.textContent = '';
    LytSlots.describeLayouts(layouts).forEach(function (row) { ol.appendChild(buildSaved(row, h)); });
    for (let n = layouts.length + 1; n <= LytSlots.SLOT_COUNT; n++) ol.appendChild(buildEmpty(n));
  }

  root.LytRow = { build: build, buildEmpty: buildEmpty, renderList: renderList };
})(typeof self !== 'undefined' ? self : this);
