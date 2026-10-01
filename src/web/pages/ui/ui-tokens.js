// The CFG > UI colour panel's editable tokens (issue 105): groups, row labels and panel order. The
// plugin accepts exactly these (ThemeColors.Tokens in src/plugin/Stores/ThemeColors.cs), in this
// order; ui-tokens.test.js fails if the two lists drift apart. A "-rgb" token is a colors.css triple
// whose solid colour, washes and calculated shades all follow it. A row is [token, label], plus for
// the SOI rows the theme-file key and, for a row picked from options, ThemeColors.Options' list
// ("word" or "word=CSS value"; the first is the default).
(function (root) {
  var GROUPS = [
    { title: 'CORE PALETTE', tokens: [
      ['--no-green-rgb',     'PRIMARY'],
      ['--no-white-rgb',     'INSTRUMENT'],
      ['--no-red-rgb',       'ALERT'],
      ['--no-amber-rgb',     'CAUTION'],
      ['--no-gray-rgb',      'INACTIVE'],
      ['--no-bg',            'BACKGROUND'],
      ['--no-panel-border',  'PANEL BORDER'],
      ['--no-ink',           'TEXT ON HIGHLIGHT'],
      ['--no-label-rgb',     'NAV LABEL'],
    ] },
    { title: 'ACCENTS', tokens: [
      ['--no-squad-rgb',     'SQUAD'],
      ['--no-purple-rgb',    'MOD CONTROLS'],
      ['--no-blue-rgb',      'MOD ACCENT'],
      ['--no-friendly-blue', 'FRIENDLY (TGT / TD)'],
      ['--no-hud-friendly',  'FRIENDLY (HUD)'],
    ] },
    { title: 'THREATS', tokens: [
      ['--no-threat-white',  'SEARCH'],
      ['--no-threat-yellow', 'TRACK'],
      ['--no-threat-red',    'LOCK'],
      ['--no-jam-yellow-rgb','JAMMING'],
    ] },
    { title: 'MAP & SCOPE', tokens: [
      ['--no-route-cyan',    'ROUTE'],
      ['--no-reached-gray',  'FLOWN ROUTE'],
      ['--no-target-orange', 'TARGET'],
      ['--no-neutral-gray',  'NEUTRAL'],
      ['--no-nuclear-orange-rgb', 'NUCLEAR ZONE'],
      ['--no-hsd-pink-rgb',  'HSD SYMBOLOGY'],
      ['--no-hsd-yellow-rgb','HSD AA RINGS'],
    ] },
    { title: 'SOI', tokens: [
      ['--no-soi',           'COLOUR', 'soi'],
      ['--no-soi-style',     'STYLE',  'soi-style', ['solid', 'dashed', 'dotted', 'double']],
      ['--no-soi-width',     'WIDTH',  'soi-width', ['sm=2px', 'md=3px', 'lg=4px']],
    ] },
  ];

  var api = { GROUPS: GROUPS };
  root.UiTokens = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : this);
