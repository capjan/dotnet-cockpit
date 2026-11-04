function apply(theme) {
  document.documentElement.dataset.theme = theme;
  for (const button of document.querySelectorAll('[data-theme-pref]')) {
    const active = button.dataset.themePref === theme;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', active ? 'true' : 'false');
  }
}

function applyPalette(palette) {
  document.documentElement.dataset.palette = palette;
  for (const button of document.querySelectorAll('[data-palette-pref]')) {
    const active = button.dataset.palettePref === palette;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', active ? 'true' : 'false');
  }
}

/** System Settings is another window, so coming back is the moment a new accent color can appear. */
function followSystemAccent() {
  window.addEventListener('focus', async () => {
    if (document.documentElement.dataset.accent === undefined) return;
    try {
      const { accent } = await (await fetch('/api/accent')).json();
      if (accent) document.documentElement.dataset.accent = accent;
    } catch { /* The current accent stays. */ }
  });
}

export function setupTheme() {
  followSystemAccent();
  document.addEventListener('submit', event => {
    const form = event.target.closest('[data-palette-switch]');
    const palette = event.submitter?.dataset.palettePref;
    if (!form || !palette) return;
    event.preventDefault();
    applyPalette(palette);
    fetch(form.action, { method: 'POST', body: new URLSearchParams({ palette }), redirect: 'manual' }).catch(() => {});
  });

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
