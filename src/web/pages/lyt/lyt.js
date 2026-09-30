// LYT page (docs/lyt-rework.md; lyt.html for the message contract). Renders the SAVED rows from
// /layout-options and asks the shell for the two things only it can do: switch shell, apply a layout.

const rowsEl = document.getElementById('lyt-rows');

function toShell(msg) { window.parent.postMessage(Object.assign({ mfd: true, type: 'lyt-act' }, msg), '*'); }

document.querySelectorAll('.lyt-shell').forEach((card) => {
  card.addEventListener('click', () => toShell({ act: card.dataset.shell }));
});

// The SAVE / LOAD key boxes stand in for their placeholders.
document.querySelectorAll('[data-bind]').forEach((slot) => {
  slot.replaceWith(LayoutKeybinds.slotBox(slot.dataset.bind, 'SET KEY'));
});

function refresh() {
  LayoutStore.list().then((data) => {
    if (data.failed) return;   // can't reach the game: keep what is shown rather than an empty library
    LytRow.renderList(
      rowsEl,
      (data.layouts || []).filter((l) => l.shell === 'classic'),
      { onLoad: (row) => toShell({ act: 'load', id: row.id }), changed: refreshSoon },
    );
  });
}

// A save/rename/delete is queued for the plugin's main thread, so the list is read a moment after
// the command's response rather than at it.
let refreshTimer = null;
function refreshSoon() {
  clearTimeout(refreshTimer);
  refreshTimer = setTimeout(refresh, 300);
}

window.addEventListener('message', (e) => {
  const m = e.data;
  if (m && m.mfd === true && m.type === 'layouts-changed') refreshSoon();
});

refresh();
