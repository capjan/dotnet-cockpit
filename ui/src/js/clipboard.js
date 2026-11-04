const SELECTOR = '.tool-command-pill, .copy-btn, .drawer-sha, .drawer-path';

export function setupClipboard() {
  const live = document.createElement('div');
  live.className = 'sr-only';
  live.setAttribute('role', 'status');
  document.body.append(live);

  document.addEventListener('click', event => {
    const target = event.target.closest(SELECTOR);
    if (!target) return;
    const text = (target.dataset.copy || target.textContent || '').trim();
    if (!text) return;
    navigator.clipboard?.writeText(text).then(() => {
      target.classList.add('copied');
      live.textContent = 'Copied to clipboard';
      setTimeout(() => {
        target.classList.remove('copied');
        live.textContent = '';
      }, 1200);
    }).catch(() => {});
  });
}
