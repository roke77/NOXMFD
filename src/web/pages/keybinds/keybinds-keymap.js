// Key naming for the KEY page, split out of keybinds.js so it carries no DOM refs and can be
// unit-checked in Node (see keybinds-keymap.test.js).
//
// Two halves of one job, which is why they live together: codeToKey turns a browser
// KeyboardEvent.code into the Unity KeyCode name the plugin stores (keybind.set-key), and
// displayKey renders one of those stored names compactly for the bind row. Anything codeToKey can
// produce, displayKey has to render — the test holds them to that.
//
// A bind's key can also be a modifier chord: Ctrl/Alt/Shift names joined with '+' before the main
// key, in that fixed order ("LeftControl+LeftAlt+Alpha1" — Keybinds.cs's KeyName writes the same
// form). Modifiers are always stored as the Left* name and match either side, because a browser
// event only reports ctrlKey/altKey/shiftKey, not which side is held.
(function (root) {
  // Letters/digits/F-keys/numpad are mechanical; the rest enumerated. Escape is reserved (it
  // cancels capture) and mouse buttons are not capturable — clicking is how this page is driven.
  const CODE2KEY = {
    Space: 'Space', Tab: 'Tab', Enter: 'Return', Backspace: 'Backspace', Delete: 'Delete',
    Insert: 'Insert', Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown',
    ArrowUp: 'UpArrow', ArrowDown: 'DownArrow', ArrowLeft: 'LeftArrow', ArrowRight: 'RightArrow',
    ShiftLeft: 'LeftShift', ShiftRight: 'RightShift', ControlLeft: 'LeftControl',
    ControlRight: 'RightControl', AltLeft: 'LeftAlt', AltRight: 'RightAlt',
    CapsLock: 'CapsLock', ScrollLock: 'ScrollLock', Pause: 'Pause',
    Minus: 'Minus', Equal: 'Equals', BracketLeft: 'LeftBracket', BracketRight: 'RightBracket',
    Backslash: 'Backslash', Semicolon: 'Semicolon', Quote: 'Quote', Backquote: 'BackQuote',
    Comma: 'Comma', Period: 'Period', Slash: 'Slash',
    NumpadDivide: 'KeypadDivide', NumpadMultiply: 'KeypadMultiply', NumpadSubtract: 'KeypadMinus',
    NumpadAdd: 'KeypadPlus', NumpadDecimal: 'KeypadPeriod', NumpadEnter: 'KeypadEnter'
  };

  // null means "not bindable" — the caller ignores the keypress rather than storing a bad name.
  function codeToKey(code) {
    if (/^Key[A-Z]$/.test(code))    return code.slice(3);                  // KeyA → A
    if (/^Digit[0-9]$/.test(code))  return 'Alpha'  + code.slice(5);       // Digit1 → Alpha1
    if (/^Numpad[0-9]$/.test(code)) return 'Keypad' + code.slice(6);       // Numpad1 → Keypad1
    if (/^F([1-9]|1[0-5])$/.test(code)) return code;                       // F1..F15
    return CODE2KEY[code] || null;
  }

  // Compact display form of a Unity KeyCode name ("Alpha1" → "1", "LeftShift" → "L-SHIFT", ...).
  // ORDER MATTERS: the Alpha/Keypad prefixes and the Left/Right contractions must all run before
  // the camelCase split, or "KeypadDivide" reads "KEYPAD DIVIDE" instead of "NUM DIVIDE".
  function displayKey(k) {
    return k
      .replace(/^Alpha/, '')
      .replace(/^Keypad/, 'NUM ')
      .replace(/^Left(Shift|Control|Alt|Arrow|Bracket)$/, 'L-$1')
      .replace(/^Right(Shift|Control|Alt|Arrow|Bracket)$/, 'R-$1')
      .replace(/([a-z])([A-Z])/g, '$1 $2')
      .toUpperCase();
  }

  // Chord modifiers, in stored order: [event flag, stored name, display name].
  const MODS = [['ctrlKey', 'LeftControl', 'CTRL'], ['altKey', 'LeftAlt', 'ALT'], ['shiftKey', 'LeftShift', 'SHIFT']];
  const MOD_CODES = /^(Control|Alt|Shift)(Left|Right)$/;
  function isModifierCode(code) { return MOD_CODES.test(code || ''); }

  // Stored name for a keydown: the held modifiers + the main key. Pressing a modifier on its own
  // names just that key ("LeftAlt"), so a lone modifier stays bindable. null = not bindable.
  function eventToKey(e) {
    const main = codeToKey(e.code);
    if (!main) return null;
    if (isModifierCode(e.code)) return main;
    return MODS.filter(function (m) { return e[m[0]]; }).map(function (m) { return m[1]; })
      .concat(main).join('+');
  }

  // Names to look a keydown up by, most specific first: the chord, then the bare main key. A bind on
  // the bare key still fires with a modifier held unless a chord bind claims that press — the same
  // "most specific wins" rule Keybinds.cs applies in game (Shadowed).
  function eventKeys(e) {
    const chord = eventToKey(e);
    if (!chord) return [];
    const bare = codeToKey(e.code);
    return chord === bare ? [chord] : [chord, bare];
  }

  // Capture step for a bind cell (KEY page, LOAD LAYOUT's slot box), fed each keydown AND keyup:
  //   { key }     — capture finished with this stored name (null = the key isn't bindable)
  //   { pending } — only modifiers held so far; show this label and keep listening
  //   null        — nothing to do (a non-modifier released, e.g. one held from before capture)
  // A modifier released before any other key finishes the capture as that lone modifier.
  function captureStep(e) {
    if (!isModifierCode(e.code)) return e.type === 'keyup' ? null : { key: eventToKey(e) };
    if (e.type === 'keydown') return { pending: pendingLabel(e) };
    const mine = e.code.replace(/(Left|Right)$/, '');
    const held = MODS.filter(function (m) { return e[m[0]] && m[1].indexOf(mine) < 0; })
      .map(function (m) { return m[1]; });
    return { key: held.concat(codeToKey(e.code)).join('+') };
  }
  function pendingLabel(e) {
    const held = MODS.filter(function (m) { return e[m[0]]; }).map(function (m) { return m[2]; });
    return held.length ? held.join('+') + '+…' : null;
  }

  // Display of a stored name, chord or not: "LeftAlt+Alpha1" → "ALT+1", "LeftAlt" → "L-ALT".
  function displayName(k) {
    const parts = k.split('+');
    const main = displayKey(parts.pop());
    return parts.map(function (p) {
      const m = MODS.find(function (x) { return x[1] === p; });
      return m ? m[2] : displayKey(p);
    }).concat(main).join('+');
  }

  const api = { CODE2KEY, codeToKey, displayKey, displayName, eventToKey, eventKeys, captureStep, isModifierCode };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.KeybindsKeymap = api;
})(typeof self !== 'undefined' ? self : this);
