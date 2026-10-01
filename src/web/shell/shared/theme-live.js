// Live colour-theme apply (CFG > UI, issue 105), shared by both shells (mfd.js, f35.js). Every page
// imports the active theme at load (theme.css → /colors-override.css). When the plugin's 'themes'
// SSE event reports a change, the shell calls apply(window, css): each same-origin document it
// hosts, nested iframes and extension pages included, gets that imported sheet's rules replaced, so
// no page needs code of its own. Each window then receives a 'no-theme' event, which MAP uses to
// drop its cached canvas colours.
//
// ponytail: a frame that fetched /colors-override.css just before a change but finished loading
// after this walk keeps the old theme until the next change or reload; fixing it means re-applying
// on every frame's load event.
(function (root) {
  var OVERRIDE_HREF = '/colors-override.css';

  // The CSSStyleSheet behind theme.css's @import of the override, searched through nested imports.
  function overrideSheet(sheets) {
    for (var i = 0; i < sheets.length; i++) {
      var rules;
      try { rules = sheets[i].cssRules; } catch (e) { continue; }   // cross-origin sheet (e.g. Google Fonts)
      for (var j = 0; j < rules.length; j++) {
        var rule = rules[j];
        if (typeof rule.href !== 'string' || !rule.styleSheet) continue;
        if (rule.href.slice(-OVERRIDE_HREF.length) === OVERRIDE_HREF) return rule.styleSheet;
        var nested = overrideSheet([rule.styleSheet]);
        if (nested) return nested;
      }
    }
    return null;
  }

  function applyToDocument(doc, css) {
    var sheet = overrideSheet(doc.styleSheets);
    if (sheet) {
      try {
        while (sheet.cssRules.length) sheet.deleteRule(0);
        if (css) sheet.insertRule(css, 0);
        return;
      } catch (e) { /* not a single rule — fall through to a style element */ }
    }
    // A document whose import is missing (or failed) gets the theme as its own last style element.
    var el = doc.getElementById('no-theme-live');
    if (!el) {
      el = doc.createElement('style');
      el.id = 'no-theme-live';
      doc.head.appendChild(el);
    }
    el.textContent = css;
  }

  function apply(win, css) {
    var doc;
    try { doc = win.document; applyToDocument(doc, css || ''); } catch (e) { return; }   // cross-origin or unloaded
    try { win.dispatchEvent(new win.Event('no-theme')); } catch (e) { /* no Event constructor (tests) */ }
    var frames = doc.querySelectorAll('iframe');
    for (var i = 0; i < frames.length; i++) {
      if (frames[i].contentWindow) apply(frames[i].contentWindow, css);
    }
  }

  var api = { apply: apply, applyToDocument: applyToDocument };
  root.ThemeLive = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : this);
