const MIN_INTERVAL_MS = 3000;
const AFTER_LOAD_MS = 400;

/**
 * Pages answer from the server's memory, which may be a moment old. So each page compares itself once after
 * loading, and again when the person comes back: when the window regains focus or the install sheet closes,
 * for instance after an installer ran elsewhere. Nothing polls in the background.
 */
export function setupLiveInventory() {
  const known = document.body.dataset.revision;
  if (known === undefined) return;
  let lastCheck = 0;

  async function check() {
    const now = Date.now();
    if (now - lastCheck < MIN_INTERVAL_MS) return;
    lastCheck = now;
    let revision;
    try {
      const response = await fetch(`/api/revision?page=${encodeURIComponent(window.location.pathname)}`);
      if (!response.ok) return;
      ({ revision } = await response.json());
    } catch {
      return;
    }
    if (revision === known) return;
    // A running confirmation or removal must not be interrupted; it reloads the page by itself.
    if (document.querySelector('dialog[data-busy], #confirm-dialog[open], #remove-dialog[open], #add-tool-dialog[open]')) return;
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

  setTimeout(check, AFTER_LOAD_MS);
  window.addEventListener('focus', check);
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') check();
  });
  document.getElementById('install-dotnet-dialog')?.addEventListener('close', () => {
    lastCheck = 0;
    check();
  });
}
