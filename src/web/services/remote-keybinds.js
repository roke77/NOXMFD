// Optional remote keybind listener (docs/remote-keybinds.md). Each browser opts in locally via
// localStorage; when enabled, the focused shell/page document translates configured KEY-page binds
// into the same /command calls the in-game keybind path already uses.
(function (root) {
  const Keymap = (typeof module !== 'undefined' && module.exports)
    ? require('../pages/keybinds/keybinds-keymap.js') : root.KeybindsKeymap;

  const STORAGE_KEY = 'noxmfd.remoteKeybinds.enabled';
  const REPEAT_MS = 120;
  const BIND_POLL_MS = 3000;
  const CURSOR_KEEPALIVE_MS = 50;
  const FIRE_KEEPALIVE_MS = 50;

  let enabled = false;
  let samePc = false;
  let bindsByKey = Object.create(null);
  let cursorByKey = Object.create(null);
  let fireByKey = Object.create(null);
  let active = Object.create(null);
  let cursorActive = Object.create(null);
  let fireActive = Object.create(null);
  let listeners = [];
  let pollTimer = null;
  let cursorTimer = null;
  let fireTimer = null;

  function post(cmd, args) {
    if (typeof fetch !== 'function') return Promise.resolve();
    return fetch('/command', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(Object.assign({ cmd: cmd }, args || {}))
    });
  }

  function commandForBind(id) {
    switch (id) {
      case 'cycle-guns':     return { cmd: 'weapon.cycle', args: { group: 'guns' } };
      case 'cycle-missiles': return { cmd: 'weapon.cycle', args: { group: 'missiles' } };
      case 'cycle-bombs':    return { cmd: 'weapon.cycle', args: { group: 'bombs' } };
      case 'flares':         return { cmd: 'cm.deploy', args: { group: 'flares' }, repeat: true };
      case 'jammer':         return { cmd: 'cm.deploy', args: { group: 'jammer' }, repeat: true };
      case 'gear-up':        return { cmd: 'gear.set', args: { group: 'up' } };
      case 'gear-down':      return { cmd: 'gear.set', args: { group: 'down' } };
      case 'map-follow':        return { cmd: 'map.action', args: { wname: 'toggle-follow' } };
      // Zoom In/Out (Cursor Zoom In/Out, docs/tgp-manual-control.md's PAD Cursor consolidation
      // plan) has no entry here: it's held-style like Cursor Up/Down/Left/Right, needing the same
      // cursorByKey/cursor.set-style live plumbing those use, not this fire-and-forget command
      // map. Remote-keybind support for it needs that same press/release transport built first.
      case 'map-route-next':    return { cmd: 'map.action', args: { wname: 'route-next' } };
      case 'map-route-prev':    return { cmd: 'map.action', args: { wname: 'route-prev' } };
      case 'map-waypoint-next': return { cmd: 'map.action', args: { wname: 'waypoint-next' } };
      case 'map-waypoint-prev': return { cmd: 'map.action', args: { wname: 'waypoint-prev' } };
      case 'tgt-next':     return { cmd: 'map.action', args: { wname: 'tgt-next' } };
      case 'tgt-prev':     return { cmd: 'map.action', args: { wname: 'tgt-prev' } };
      case 'tgt-datalink': return { cmd: 'map.action', args: { wname: 'tgt-datalink' } };
      case 'tgt-stale':    return { cmd: 'map.action', args: { wname: 'tgt-stale' } };
      case 'soi-next':     return { cmd: 'soi.next' };
      case 'soi-prev':     return { cmd: 'soi.prev' };
      case 'soi-nav-up':   return { cmd: 'soi.action', args: { wname: 'up' } };
      case 'soi-nav-down': return { cmd: 'soi.action', args: { wname: 'down' } };
      case 'soi-select':   return { cmd: 'soi.action', args: { wname: 'select' } };
      case 'master-arms-on':  return { cmd: 'master-arms.set', args: { on: true } };
      case 'master-arms-off': return { cmd: 'master-arms.set', args: { on: false } };
      case 'radar-on':  return { cmd: 'avn.set', args: { group: 'radar', on: true } };
      case 'radar-off': return { cmd: 'avn.set', args: { group: 'radar', on: false } };
      case 'engine-on':  return { cmd: 'avn.set', args: { group: 'eng', on: true } };
      case 'engine-off': return { cmd: 'avn.set', args: { group: 'eng', on: false } };
      case 'combat-mode-aa': return { cmd: 'combat-mode.set', args: { group: 'aa' } };
      case 'combat-mode-ag': return { cmd: 'combat-mode.set', args: { group: 'ag' } };
      case 'hud-preset-1': return { cmd: 'preset.load', args: { index: 1 } };
      case 'hud-preset-2': return { cmd: 'preset.load', args: { index: 2 } };
      case 'hud-preset-3': return { cmd: 'preset.load', args: { index: 3 } };
      case 'hud-preset-4': return { cmd: 'preset.load', args: { index: 4 } };
      case 'hud-preset-5': return { cmd: 'preset.load', args: { index: 5 } };
      default: return null;
    }
  }

  function cursorRoleForBind(id) {
    switch (id) {
      case 'cursor-left': return 'left';
      case 'cursor-right': return 'right';
      case 'cursor-up': return 'up';
      case 'cursor-down': return 'down';
      case 'cursor-select': return 'select';
      default: return null;
    }
  }

  function fireRoleForBind(id) {
    switch (id) {
      case 'gun-trigger': return 'gun';
      case 'weapon-release': return 'release';
      case 'jammer-pod': return 'jammer-pod';
      default: return null;
    }
  }

  function buildKeyMap(binds) {
    const map = Object.create(null);
    (binds || []).forEach(function (b) {
      if (!b || !b.key) return;
      const spec = commandForBind(b.id);
      if (!spec) return;
      map[b.key] = { id: b.id, spec: spec };
    });
    return map;
  }

  function buildCursorKeyMap(binds) {
    const map = Object.create(null);
    (binds || []).forEach(function (b) {
      if (!b || !b.key) return;
      const role = cursorRoleForBind(b.id);
      if (!role) return;
      map[b.key] = role;
    });
    return map;
  }

  function buildFireKeyMap(binds) {
    const map = Object.create(null);
    (binds || []).forEach(function (b) {
      if (!b || !b.key) return;
      const role = fireRoleForBind(b.id);
      if (!role) return;
      map[b.key] = role;
    });
    return map;
  }

  function cursorStateFromActive(held) {
    const x = (held.right ? 1 : 0) - (held.left ? 1 : 0);
    const y = (held.down ? 1 : 0) - (held.up ? 1 : 0);
    return { x: x, y: y, on: !!held.select };
  }

  function fireGroupsFromActive(held) {
    return {
      gun: !!held.gun,
      release: !!held.release,
      'jammer-pod': !!held['jammer-pod']
    };
  }

  function refresh() {
    if (typeof fetch !== 'function') return Promise.resolve();
    return fetch('/keybinds-config', { cache: 'no-store' }).then(function (r) { return r.json(); })
      .then(function (cfg) {
        samePc = !!cfg.remoteKeybindsSamePc;
        bindsByKey = buildKeyMap(cfg.binds || []);
        cursorByKey = buildCursorKeyMap(cfg.binds || []);
        fireByKey = buildFireKeyMap(cfg.binds || []);
        notify();
      }).catch(function () {});
  }

  function readEnabled() {
    try { return root.localStorage && root.localStorage.getItem(STORAGE_KEY) === '1'; }
    catch (e) { return false; }
  }

  function writeEnabled(on) {
    try {
      if (root.localStorage) root.localStorage.setItem(STORAGE_KEY, on ? '1' : '0');
    } catch (e) {}
  }

  function notify() {
    listeners.slice().forEach(function (fn) {
      try { fn(state()); } catch (e) {}
    });
  }

  function state() {
    return {
      enabled: enabled,
      samePc: samePc,
      remoteCapableCount: Object.keys(bindsByKey).length + Object.keys(cursorByKey).length + Object.keys(fireByKey).length
    };
  }

  function setEnabled(on) {
    enabled = !!on;
    writeEnabled(enabled);
    if (enabled) startPolling();
    else {
      clearActive();
      bindsByKey = Object.create(null);
      cursorByKey = Object.create(null);
      fireByKey = Object.create(null);
      stopPolling();
    }
    notify();
  }

  function onChange(fn) {
    listeners.push(fn);
    fn(state());
    return function () { listeners = listeners.filter(function (x) { return x !== fn; }); };
  }

  function editableTarget(target) {
    if (!target) return false;
    const tag = (target.tagName || '').toLowerCase();
    return tag === 'input' || tag === 'textarea' || tag === 'select' || target.isContentEditable;
  }

  function trigger(item) {
    post(item.spec.cmd, item.spec.args).catch(function () {});
  }

  function sendCursorState() {
    post('cursor.set', cursorStateFromActive(cursorActive)).catch(function () {});
  }

  function cursorIsActive() {
    return !!(cursorActive.left || cursorActive.right || cursorActive.up || cursorActive.down || cursorActive.select);
  }

  function ensureCursorTimer() {
    if (cursorTimer == null) {
      cursorTimer = root.setInterval(sendCursorState, CURSOR_KEEPALIVE_MS);
    }
  }

  function stopCursorTimer() {
    if (cursorTimer != null) root.clearInterval(cursorTimer);
    cursorTimer = null;
  }

  function setCursorRole(role, on) {
    cursorActive[role] = !!on;
    if (cursorIsActive()) ensureCursorTimer();
    else stopCursorTimer();
    sendCursorState();
  }

  function sendFireState() {
    const groups = fireGroupsFromActive(fireActive);
    Object.keys(groups).forEach(function (group) {
      if (groups[group]) post('fire.set', { group: group, on: true }).catch(function () {});
    });
  }

  function fireIsActive() {
    return !!(fireActive.gun || fireActive.release || fireActive['jammer-pod']);
  }

  function ensureFireTimer() {
    if (fireTimer == null) {
      fireTimer = root.setInterval(sendFireState, FIRE_KEEPALIVE_MS);
    }
  }

  function stopFireTimer() {
    if (fireTimer != null) root.clearInterval(fireTimer);
    fireTimer = null;
  }

  function setFireRole(role, on) {
    fireActive[role] = !!on;
    if (fireIsActive()) ensureFireTimer();
    else stopFireTimer();
    post('fire.set', { group: role, on: !!on }).catch(function () {});
  }

  function keydown(e) {
    if (!enabled || editableTarget(e.target)) return;
    const key = Keymap && Keymap.codeToKey ? Keymap.codeToKey(e.code) : null;
    if (!key) return;
    const cursorRole = cursorByKey[key];
    const fireRole = fireByKey[key];
    const item = bindsByKey[key];
    if (!cursorRole && !fireRole && !item) return;
    e.preventDefault();
    if (cursorRole && !cursorActive[cursorRole]) {
      if (cursorRole === 'select') post('cursor.select').catch(function () {});
      setCursorRole(cursorRole, true);
    }
    if (fireRole && !fireActive[fireRole]) {
      setFireRole(fireRole, true);
    }
    if (item && !active[key]) {
      trigger(item);
      active[key] = item.spec.repeat
        ? root.setInterval(function () { trigger(item); }, REPEAT_MS)
        : true;
    }
  }

  function keyup(e) {
    const key = Keymap && Keymap.codeToKey ? Keymap.codeToKey(e.code) : null;
    if (!key) return;
    const cursorRole = cursorByKey[key];
    const fireRole = fireByKey[key];
    if (cursorRole && cursorActive[cursorRole]) setCursorRole(cursorRole, false);
    if (fireRole && fireActive[fireRole]) setFireRole(fireRole, false);
    if (active[key]) {
      if (active[key] !== true) root.clearInterval(active[key]);
      delete active[key];
    }
  }

  function clearActive() {
    Object.keys(active).forEach(function (key) {
      if (active[key] !== true) root.clearInterval(active[key]);
    });
    active = Object.create(null);
    if (cursorIsActive()) {
      cursorActive = Object.create(null);
      stopCursorTimer();
      sendCursorState();
    } else {
      cursorActive = Object.create(null);
      stopCursorTimer();
    }
    if (fireIsActive()) {
      const wasActive = Object.assign({}, fireActive);
      fireActive = Object.create(null);
      stopFireTimer();
      Object.keys(wasActive).forEach(function (role) {
        if (wasActive[role]) post('fire.set', { group: role, on: false }).catch(function () {});
      });
    } else {
      fireActive = Object.create(null);
      stopFireTimer();
    }
  }

  function startPolling() {
    refresh();
    if (pollTimer == null) pollTimer = root.setInterval(refresh, BIND_POLL_MS);
  }

  function stopPolling() {
    if (pollTimer != null) root.clearInterval(pollTimer);
    pollTimer = null;
  }

  function install() {
    if (typeof document === 'undefined') return;
    enabled = readEnabled();
    document.addEventListener('keydown', keydown);
    document.addEventListener('keyup', keyup);
    root.addEventListener('blur', clearActive);
    root.addEventListener('storage', function (e) {
      if (e.key === STORAGE_KEY) setEnabled(e.newValue === '1');
    });
    root.addEventListener('message', function (e) {
      if (!e.data || e.data.type !== 'remote-keybinds-enabled') return;
      setEnabled(!!e.data.enabled);
    });
    if (enabled) startPolling();
  }

  const api = {
    STORAGE_KEY: STORAGE_KEY,
    commandForBind: commandForBind,
    cursorRoleForBind: cursorRoleForBind,
    fireRoleForBind: fireRoleForBind,
    buildKeyMap: buildKeyMap,
    buildCursorKeyMap: buildCursorKeyMap,
    buildFireKeyMap: buildFireKeyMap,
    cursorStateFromActive: cursorStateFromActive,
    fireGroupsFromActive: fireGroupsFromActive,
    refresh: refresh,
    isEnabled: function () { return enabled; },
    setEnabled: setEnabled,
    onChange: onChange,
    state: state
  };

  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else {
    root.RemoteKeybinds = api;
    install();
  }
})(typeof self !== 'undefined' ? self : this);
