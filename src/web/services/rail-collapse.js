// Collapsible sidebar toggle, shared by KEY's section rail and the UI page's theme list. The
// collapsed state is the pane's choice once made: the toggle saves it (per browser, localStorage
// under storageKey) and a saved value wins over the width. With nothing saved, the sidebar starts
// collapsed on a pane 1152px wide or less and open otherwise, following the width as it changes.
// The page's CSS draws the 'collapsed' class.
function railCollapse(el, toggle, storageKey) {
  var narrow = window.matchMedia('(max-width: 1152px)');
  function saved() {
    try {
      var v = localStorage.getItem(storageKey);
      return v === '1' ? true : v === '0' ? false : null;
    } catch (e) { return null; }
  }
  function set(collapsed) {
    el.classList.toggle('collapsed', collapsed);
    toggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
  }
  function applyDefault() {
    var pref = saved();
    set(pref === null ? narrow.matches : pref);
  }
  toggle.addEventListener('click', function () {
    var collapsed = !el.classList.contains('collapsed');
    try { localStorage.setItem(storageKey, collapsed ? '1' : '0'); } catch (e) {}
    set(collapsed);
  });
  narrow.addEventListener('change', applyDefault);
  applyDefault();
}
