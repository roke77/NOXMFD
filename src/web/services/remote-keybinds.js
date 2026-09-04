// Optional remote keybind listener (docs/remote-keybinds.md). Each browser opts in locally via
// localStorage; when enabled, the focused shell/page document translates configured KEY-page binds
// into the same /command calls the in-game keybind path already uses.
(function (root) {
  const Keymap = (typeof module !== 'undefined' && module.exports)
    ? require('../pages/keybinds/keybinds-keymap.js') : root.KeybindsKeymap;

  const STORAGE_KEY = 'noxmfd.remoteKeybinds.enabled';
  const REPEAT_MS = 120;
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
  let latestConfig = null;
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
      // Cursor Zoom In/Out has no entry here: held-style like Cursor Up/Down/Left/Right, so it's
      // handled by fireRoleForBind's 'zoom-in'/'zoom-out' groups (fire.set transport) instead of
      // this fire-and-forget command map.
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
      case 'power-on':  return { cmd: 'power.set', args: { on: true } };
      case 'power-off': return { cmd: 'power.set', args: { on: false } };
      case 'cursor-deselect': return { cmd: 'map.action', args: { wname: 'cursor-deselect' } };
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
      // TD's 9 Assign binds (issue #47, squad-leader-only): the physical keybind is tap-vs-hold
      // (tap assigns and clears the leader's selection, hold assigns and retains it for chaining
      // onto another slot — TdStore.Assign's `retain` param, confusingly carried over `/command`'s
      // `on` field). A remote keydown has no hold-vs-tap distinction here (same limitation as
      // Combat Mode A/A · A/G below only remoting their tap outcome), so this always sends the tap
      // (non-retaining) behavior. A no-op server-side unless the sender is the squad leader.
      case 'td-assign-1': return { cmd: 'td.assign', args: { index: 1, on: false } };
      case 'td-assign-2': return { cmd: 'td.assign', args: { index: 2, on: false } };
      case 'td-assign-3': return { cmd: 'td.assign', args: { index: 3, on: false } };
      case 'td-assign-4': return { cmd: 'td.assign', args: { index: 4, on: false } };
      case 'td-assign-5': return { cmd: 'td.assign', args: { index: 5, on: false } };
      case 'td-assign-6': return { cmd: 'td.assign', args: { index: 6, on: false } };
      case 'td-assign-7': return { cmd: 'td.assign', args: { index: 7, on: false } };
      case 'td-assign-8': return { cmd: 'td.assign', args: { index: 8, on: false } };
      case 'td-assign-9': return { cmd: 'td.assign', args: { index: 9, on: false } };
      // TGP Keybinds (docs/tgp-manual-control.md) — one-shot toggles/actions on the manual TGP
      // camera and full-screen view, same shape as the Immersion Options row above. Point Track,
      // Manual Control Reset, and Mark Steer Point reuse the exact `/command` names the TGP page's
      // own TRK/RST/STP bezel buttons already send (CommandDispatcher.cs); the rest are new
      // dispatcher entries added alongside this. Pan/Tilt (the PAD Cursor binds) and Zoom Axis are
      // excluded here on purpose: continuous/held controls covered by cursorRoleForBind and the
      // 'zoom-in'/'zoom-out' fire groups instead, not a one-shot command.
      case 'tgp-manual-toggle':           return { cmd: 'tgp.manual-toggle' };
      case 'tgp-manual-reset':            return { cmd: 'tgp.manual-reset' };
      case 'tgp-point-track':             return { cmd: 'tgp.point-track' };
      case 'tgp-manual-snap-headtracker': return { cmd: 'tgp.snap-headtracker' };
      case 'tgp-manual-ir-toggle':        return { cmd: 'tgp.ir-toggle' };
      case 'tgp-mark-steerpoint':         return { cmd: 'tgp.mark-steerpoint' };
      case 'tgp-fullscreen-toggle':       return { cmd: 'tgp.fullscreen-toggle' };
      case 'tgp-fullscreen-hud-toggle':   return { cmd: 'tgp.fullscreen-hud-toggle' };
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
      case 'weapon-release-single': return 'release-single';
      case 'jammer-pod': return 'jammer-pod';
      // Cursor Zoom In/Out (docs/tgp-manual-control.md's PAD Cursor consolidation plan): held
      // state, not a one-shot command, since Keybinds.Poll() drives the manual TGP camera's zoom
      // rate at whatever cadence the key stays down — same held-group transport as the fire binds
      // above, just a different named group on the server side (RemoteInputState.SetFire/GetFire
      // don't care what a group is "for", so this reuses the exact same fire.set plumbing).
      case 'cursor-zoom-in': return 'zoom-in';
      case 'cursor-zoom-out': return 'zoom-out';
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
      'release-single': !!held['release-single'],
      'jammer-pod': !!held['jammer-pod'],
      'zoom-in': !!held['zoom-in'],
      'zoom-out': !!held['zoom-out']
    };
  }

  function fetchConfig() {
    if (typeof fetch !== 'function') return Promise.resolve();
    return fetch('/keybinds-config', { cache: 'no-store' }).then(function (r) { return r.json(); })
      .then(function (cfg) {
        // Send through the same path as a later SSE update so sibling modules and child frames see
        // the bootstrap without each issuing their own request.
        root.postMessage({ mfd: true, type: 'keybinds-config-push', data: cfg }, '*');
      }).catch(function () {});
  }

  function applyConfig(cfg) {
    latestConfig = cfg;
    samePc = !!cfg.remoteKeybindsSamePc;
    bindsByKey = buildKeyMap(cfg.binds || []);
    cursorByKey = buildCursorKeyMap(cfg.binds || []);
    fireByKey = buildFireKeyMap(cfg.binds || []);
    notify();
  }

  function sendConfig(target) {
    if (latestConfig && target) target.postMessage({ mfd: true, type: 'keybinds-config-push', data: latestConfig }, '*');
  }

  function broadcastConfig() {
    if (typeof document === 'undefined') return;
    [].slice.call(document.querySelectorAll('iframe')).forEach(function (frame) { sendConfig(frame.contentWindow); });
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
    if (!enabled) {
      clearActive();
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
    return !!(fireActive.gun || fireActive.release || fireActive['release-single'] ||
              fireActive['jammer-pod'] || fireActive['zoom-in'] || fireActive['zoom-out']);
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
      const m = e.data;
      if (!m) return;
      if (m.type === 'remote-keybinds-enabled') {
        setEnabled(!!m.enabled);
      } else if (m.mfd === true && m.type === 'keybinds-config-push') {
        applyConfig(m.data || {});
        if (root.parent === root) broadcastConfig();
      } else if (m.mfd === true && m.type === 'keybinds-config-request' && root.parent === root) {
        sendConfig(e.source);
      }
    });
    if (root.parent === root) {
      // Shells already have a telemetry tap that immediately supplies the initial SSE snapshot.
      // Standalone pages fetch once; a shell only falls back if its stream never answers.
      const hasTelemetryTap = !!document.querySelector('iframe[title="map"], #map-tap');
      if (hasTelemetryTap) root.setTimeout(function () { if (!latestConfig) fetchConfig(); }, 1500);
      else fetchConfig();
    } else {
      root.parent.postMessage({ mfd: true, type: 'keybinds-config-request' }, '*');
    }
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
    applyConfig: applyConfig,
    refresh: fetchConfig,
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
