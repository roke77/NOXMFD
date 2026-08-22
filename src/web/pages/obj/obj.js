// Read-only replica of the game's active-objectives list, driven by the shell over postMessage.
// Only local interaction is the expand/collapse toggle.

const listEl = document.getElementById('obj-list');

const STATUS_LABEL = ['Not Started', 'Running', 'Complete'];

let state = { present: false, items: [] };
let builtKey = '';   // last-rendered item signature; skips DOM rebuild on percent/position-only updates
const collapsed = new Set();   // objective names currently collapsed; default is expanded

function buildPosRow() {
  const row = document.createElement('div');
  row.className = 'obj-pos-row';
  const name = document.createElement('div');
  name.className = 'obj-pos-name';
  const grid = document.createElement('div');
  grid.className = 'obj-pos-grid';
  const dist = document.createElement('div');
  dist.className = 'obj-pos-dist';
  row.appendChild(name);
  row.appendChild(grid);
  row.appendChild(dist);
  return row;
}

function buildRows() {
  // Signature includes position-row count so a changed sub-row list also triggers a rebuild.
  const sig = state.items.map(o => o.n + ':' + (o.pos ? o.pos.length : 0)).join('|');
  if (sig === builtKey) return;
  builtKey = sig;

  listEl.innerHTML = '';
  state.items.forEach(function (o) {
    const hasPos = Array.isArray(o.pos) && o.pos.length > 0;

    const row = document.createElement('div');
    row.className = 'obj-row';

    const head = document.createElement('div');
    head.className = 'obj-row-head';
    const name = document.createElement('div');
    name.className = 'obj-name';
    const status = document.createElement('div');
    status.className = 'obj-status';
    const pct = document.createElement('div');
    pct.className = 'obj-pct';
    head.appendChild(name);
    head.appendChild(status);
    head.appendChild(pct);

    if (hasPos) {
      const toggle = document.createElement('button');
      toggle.className = 'obj-toggle';
      toggle.type = 'button';
      toggle.addEventListener('click', function () {
        if (collapsed.has(o.n)) collapsed.delete(o.n); else collapsed.add(o.n);
        row.classList.toggle('collapsed', collapsed.has(o.n));
      });
      head.appendChild(toggle);
    }
    row.appendChild(head);

    if (hasPos) {
      const posList = document.createElement('div');
      posList.className = 'obj-positions';
      o.pos.forEach(function () { posList.appendChild(buildPosRow()); });
      row.appendChild(posList);
      row.classList.toggle('collapsed', collapsed.has(o.n));
    }

    listEl.appendChild(row);
  });
}

function fmtRange(km) {
  return (typeof km === 'number' ? km : 0).toFixed(km >= 10 ? 0 : 1) + 'km';
}

function paint() {
  document.body.classList.toggle('unavailable', !state.present);
  if (!state.present) return;

  buildRows();
  const rows = listEl.children;
  for (let i = 0; i < rows.length && i < state.items.length; i++) {
    const o = state.items[i], row = rows[i];
    const complete = o.s === 2;
    row.classList.toggle('complete', complete);
    row.querySelector('.obj-name').textContent = o.n || '';
    row.querySelector('.obj-status').textContent = STATUS_LABEL[o.s] || STATUS_LABEL[0];
    row.querySelector('.obj-pct').textContent = Math.round((o.p || 0) * 100) + '%';

    const posRows = row.querySelectorAll('.obj-pos-row');
    const positions = Array.isArray(o.pos) ? o.pos : [];
    for (let j = 0; j < posRows.length && j < positions.length; j++) {
      const p = positions[j], pr = posRows[j];
      pr.querySelector('.obj-pos-name').textContent = p.n || '';
      pr.querySelector('.obj-pos-grid').textContent = p.g || '—';
      pr.querySelector('.obj-pos-dist').textContent = fmtRange(p.r);
    }
  }
}

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true || m.type !== 'obj') return;
  state = { present: !!m.present, items: Array.isArray(m.items) ? m.items : [] };
  paint();
});

paint();   // starts in unavailable state until the first frame arrives
