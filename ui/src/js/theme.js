function apply(theme) {
  document.documentElement.dataset.theme = theme;
  for (const button of document.querySelectorAll('[data-theme-pref]')) {
    const active = button.dataset.themePref === theme;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', active ? 'true' : 'false');
  }
}

export function setupTheme() {
  document.addEventListener('submit', event => {
    const form = event.target.closest('[data-theme-switch]');
    if (!form) return;
    const button = event.submitter;
    if (!button?.dataset.themePref) return;
    event.preventDefault();
    const theme = button.dataset.themePref;
    apply(theme);
    fetch(form.action, { method: 'POST', body: new URLSearchParams({ theme }), redirect: 'manual' }).catch(() => {});
  });
}
