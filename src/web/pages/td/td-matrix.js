// TD assignment matrix — the pure rules behind td.js (docs/target-designator.md), split out per
// src/web/README.md's "pure sibling module" pattern so they're checkable without a DOM.
//
// `assignments` is TdStore's own served shape: { "<target id>": ["<member SteamID>", ...] }. Members
// are keyed by SteamID, passed around as strings (a 17-digit id overflows a JS number; issue #107).
// The toggles mirror TdStore.ToggleCell/ToggleRow/ToggleColumn exactly — td.js applies them locally
// for instant feedback, then the plugin's pushed td-state replaces the local copy. Each returns a
// new object and never mutates its input.

function copy(assignments) {
  const out = {};
  Object.keys(assignments || {}).forEach(function (k) { out[k] = assignments[k].slice(); });
  return out;
}

export function has(assignments, id, member) {
  const members = (assignments || {})[String(id)];
  return !!members && members.indexOf(member) !== -1;
}

function set(out, id, member, on) {
  const key = String(id);
  const members = (out[key] || []).filter(function (m) { return m !== member; });
  if (on) members.push(member);
  if (members.length) out[key] = members; else delete out[key];
}

export function toggleCell(assignments, id, member) {
  const out = copy(assignments);
  set(out, id, member, !has(assignments, id, member));
  return out;
}

// All-or-nothing: fills the gaps, or empties a row that already has every member.
export function toggleRow(assignments, id, members) {
  const full = members.every(function (m) { return has(assignments, id, m); });
  const out = copy(assignments);
  members.forEach(function (m) { set(out, id, m, !full); });
  return out;
}

// Same rule down a column, over the targets currently on the table.
export function toggleColumn(assignments, member, ids) {
  const full = ids.every(function (id) { return has(assignments, id, member); });
  const out = copy(assignments);
  ids.forEach(function (id) { set(out, id, member, !full); });
  return out;
}

// The target ids assigned to one member, in table order.
export function memberIds(assignments, member, ids) {
  return ids.filter(function (id) { return has(assignments, id, member); });
}

// A member's DESIGNATE status for its column head. The leader's own column (`marker`) is a tag and is
// never sent. `sent` is TdStore's { "<SteamID>": [ids] } of what each member was last sent; a member
// counts as SENT only while the table's assignment for them matches that list exactly.
export function memberStatus(assignments, sent, member, ids, marker) {
  const mine = memberIds(assignments, member, ids);
  if (marker) return { n: mine.length, status: 'MARKER', waiting: false };
  const last = (sent || {})[member];
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
