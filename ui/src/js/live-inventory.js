const MIN_INTERVAL_MS = 3000;

/**
 * An installer runs outside the browser, so the moment to look again is when the person comes back:
 * the window regains focus, or the install sheet closes. Nothing polls in the background.
 */
export function setupLiveInventory() {
  const known = document.body.dataset.inventory;
  if (known === undefined) return;
  let lastCheck = 0;

  async function check() {
    const now = Date.now();
    if (now - lastCheck < MIN_INTERVAL_MS) return;
    lastCheck = now;
    let snapshot;
    try {
      const response = await fetch('/api/installation');
      if (!response.ok) return;
      snapshot = await response.json();
    } catch {
      return;
    }
    if (snapshot.status !== 'ready' || snapshot.signature === known) return;
    // A running confirmation or removal must not be interrupted; it reloads the page by itself.
    if (document.querySelector('dialog[data-busy], #confirm-dialog[open], #remove-dialog[open]')) return;
    const url = new URL(window.location.href);
    if (url.pathname === '/') url.searchParams.set('notice', 'detected');
    const sheet = document.getElementById('install-dotnet-dialog');
    if (sheet?.open) {
      url.searchParams.set('install', '1');
      url.searchParams.set('product', sheet.querySelector('[data-install-product].active')?.dataset.installProduct === 'SDK' ? 'sdk' : 'runtime');
      url.searchParams.set('view', sheet.querySelector('[data-install-lifecycle].active')?.dataset.installLifecycle ?? 'latest');
      url.searchParams.set('previews', sheet.querySelector('[data-install-previews]')?.classList.contains('active') ? 'true' : 'false');
    }
    window.location.replace(url);
  }

  window.addEventListener('focus', check);
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') check();
  });
  document.getElementById('install-dotnet-dialog')?.addEventListener('close', () => {
    lastCheck = 0;
    check();
  });
}
