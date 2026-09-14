// Keep the configuration DOM in the TGP document; hash navigation never reloads the iframe.
(function () {
  let loading = null;
  let ready = false;
  const host = document.createElement('div');
  host.id = 'tgp-config-host';
  document.body.appendChild(host);
  async function loadConfig() {
    const response = await fetch('/tgpcfg');
    if (!response.ok) throw new Error('TGP configuration unavailable');
    const doc = new DOMParser().parseFromString(await response.text(), 'text/html');
    const panel = doc.getElementById('tcfg-panel');
    if (!panel) throw new Error('TGP configuration panel missing');
    panel.querySelector('.tcfg-back')?.setAttribute('href', '#');
    host.replaceChildren(document.importNode(panel, true));
    await new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = '/assets/pages/tgpcfg/tgpcfg.js';
      script.onload = resolve;
      script.onerror = () => { script.remove(); reject(new Error('TGP configuration script unavailable')); };
      document.body.appendChild(script);
    });
    ready = true;
  }
  function showView() {
    const config = location.hash === '#cfg';
    document.documentElement.classList.toggle('tgp-config-open', config);
    host.hidden = !config;
    if (!config) return;
    if (ready) window.dispatchEvent(new Event('tgp-config-refresh'));
    else if (!loading) {
      loading = loadConfig().catch(error => {
        console.warn('[tgp]', error);
        host.textContent = 'TGP CFG unavailable — return to TGP and retry.';
      }).finally(() => { loading = null; });
    }
  }
  window.addEventListener('hashchange', showView);
  showView();
})();
