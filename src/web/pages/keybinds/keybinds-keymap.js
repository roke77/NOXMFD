// Key naming for the KEY page, split out of keybinds.js so it carries no DOM refs and can be
// unit-checked in Node (see keybinds-keymap.test.js).
//
// Two halves of one job, which is why they live together: codeToKey turns a browser
// KeyboardEvent.code into the Unity KeyCode name the plugin stores (keybind.set-key), and
// displayKey renders one of those stored names compactly for the bind row. Anything codeToKey can
// produce, displayKey has to render — the test holds them to that.
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

  const api = { CODE2KEY, codeToKey, displayKey };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.KeybindsKeymap = api;
})(typeof self !== 'undefined' ? self : this);
