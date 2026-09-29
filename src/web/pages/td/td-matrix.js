// TD assignment matrix — the pure rules behind td.js (docs/target-designator.md), split out per
// src/web/README.md's "pure sibling module" pattern so they're checkable without a DOM.
//
// `assignments` is TdStore's own served shape: { "<target id>": [slot, ...] }. The toggles mirror
// TdStore.ToggleCell/ToggleRow/ToggleColumn exactly — td.js applies them locally for instant
// feedback, then the plugin's pushed td-state replaces the local copy. Each returns a new object
// and never mutates its input.

function copy(assignments) {
  const out = {};
  Object.keys(assignments || {}).forEach(function (k) { out[k] = assignments[k].slice(); });
  return out;
}

export function has(assignments, id, slot) {
  const slots = (assignments || {})[String(id)];
  return !!slots && slots.indexOf(slot) !== -1;
}

function set(out, id, slot, on) {
  const key = String(id);
  const slots = (out[key] || []).filter(function (s) { return s !== slot; });
  if (on) slots.push(slot);
  if (slots.length) out[key] = slots; else delete out[key];
}

export function toggleCell(assignments, id, slot) {
  const out = copy(assignments);
  set(out, id, slot, !has(assignments, id, slot));
  return out;
}

// All-or-nothing: fills the gaps, or empties a row that already has every slot.
export function toggleRow(assignments, id, slots) {
  const full = slots.every(function (s) { return has(assignments, id, s); });
  const out = copy(assignments);
  slots.forEach(function (s) { set(out, id, s, !full); });
  return out;
}

// Same rule down a column, over the targets currently on the table.
export function toggleColumn(assignments, slot, ids) {
  const full = ids.every(function (id) { return has(assignments, id, slot); });
  const out = copy(assignments);
  ids.forEach(function (id) { set(out, id, slot, !full); });
  return out;
}

// The target ids assigned to one slot, in table order.
export function slotIds(assignments, slot, ids) {
  return ids.filter(function (id) { return has(assignments, id, slot); });
}

// A slot's DESIGNATE status for its column head. Slot 1 is the leader's own marker and is never
// sent. `sent` is TdStore's { "<slot>": [ids] } of what each member was last sent; a member counts
// as SENT only while the table's assignment for them matches that list exactly.
export function slotStatus(assignments, sent, slot, ids) {
  const mine = slotIds(assignments, slot, ids);
  if (slot === 1) return { n: mine.length, status: 'MARKER', waiting: false };
  const last = (sent || {})[String(slot)];
  let status;
  if (!last) status = mine.length ? 'UNSENT' : 'EMPTY';
  else status = sameSet(last, mine) ? 'SENT' : 'CHANGED';
  return { n: mine.length, status: status, waiting: status === 'UNSENT' || status === 'CHANGED' };
}

function sameSet(a, b) {
  if (a.length !== b.length) return false;
  const s = new Set(a);
  return b.every(function (x) { return s.has(x); });
}
