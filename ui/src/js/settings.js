export function setupSettings() {
  for (const form of document.querySelectorAll('[data-settings-toggle]')) {
    form.addEventListener('change', () => {
      fetch(form.action, { method: 'POST', body: new URLSearchParams(new FormData(form)), redirect: 'manual' }).catch(() => {});
    });
  }

  const skip = document.getElementById('skip-preflight-checkbox');
  if (skip) {
    skip.addEventListener('change', () => {
      const body = new URLSearchParams();
      if (skip.checked) body.set('skip', 'true');
      fetch('/settings/preflight', { method: 'POST', body, redirect: 'manual' }).catch(() => {});
    });
  }

  const links = [...document.querySelectorAll('.settings-nav-item')];
  if (!links.length || !('IntersectionObserver' in window)) return;
  const sections = new Map(links.map(link => [document.querySelector(link.getAttribute('href'))?.closest('section'), link]).filter(([section]) => section));
  const visible = new Set();
  const update = () => {
    const current = [...sections.keys()].find(section => visible.has(section));
    if (!current) return;
    for (const link of links) {
      const active = link === sections.get(current);
      link.classList.toggle('active', active);
      if (active) link.setAttribute('aria-current', 'true');
      else link.removeAttribute('aria-current');
    }
  };
  const observer = new IntersectionObserver(entries => {
    for (const entry of entries) {
      if (entry.isIntersecting) visible.add(entry.target);
      else visible.delete(entry.target);
    }
    update();
  }, { rootMargin: '-20% 0px -60% 0px' });
  for (const section of sections.keys()) observer.observe(section);
}
