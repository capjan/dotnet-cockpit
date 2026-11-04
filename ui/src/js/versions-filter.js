import { isPlainClick, matchesTerms, replaceQuery } from './dom.js';

export function setupVersionsFilter() {
  const input = document.getElementById('installed-version-filter');
  if (!input) return;
  const form = input.closest('form');
  const kindField = form.querySelector('input[name="kind"]');
  const links = [...document.querySelectorAll('[data-version-kind]')];
  const rows = [...document.querySelectorAll('tr[data-kind]')];
  const cards = [...document.querySelectorAll('tbody.channel-card[data-channel-card]')];
  const counter = document.querySelector('[data-versions-count]');
  const emptyRow = document.querySelector('tr.empty-row');
  let activeKind = kindField.value;

  function matchesKind(row) {
    if (activeKind === 'sdks') return row.dataset.kind === 'sdk';
    if (activeKind === 'runtimes') return row.dataset.kind === 'runtime';
    if (activeKind === 'previews') return row.dataset.preview === 'true';
    return true;
  }

  function run() {
    let visible = 0;
    for (const row of rows) {
      row.hidden = !(matchesKind(row) && matchesTerms(row.textContent, input.value));
      if (!row.hidden) visible++;
    }
    for (const card of cards) card.hidden = card.querySelectorAll('tr[data-kind]:not([hidden])').length === 0;
    if (counter) counter.textContent = `${visible} of ${rows.length} installations shown`;
    if (emptyRow) emptyRow.hidden = visible > 0;
    replaceQuery(url => {
      url.hash = '';
      if (input.value.trim()) url.searchParams.set('q', input.value.trim());
      else url.searchParams.delete('q');
      if (activeKind !== 'all') url.searchParams.set('kind', activeKind);
      else url.searchParams.delete('kind');
    });
  }

  function syncLinks() {
    for (const link of links) {
      const active = link.dataset.versionKind === activeKind;
      link.classList.toggle('active', active);
      if (active) link.setAttribute('aria-current', 'page');
      else link.removeAttribute('aria-current');
    }
    kindField.value = activeKind;
  }

  for (const link of links) {
    link.addEventListener('click', event => {
      if (!isPlainClick(event)) return;
      event.preventDefault();
      const clicked = link.dataset.versionKind;
      activeKind = clicked === activeKind ? 'all' : clicked;
      syncLinks();
      run();
    });
  }

  input.addEventListener('input', run);
  form.addEventListener('submit', event => {
    event.preventDefault();
    run();
  });

  document.addEventListener('click', event => {
    const collapse = event.target.closest('[data-collapse-channel]');
    if (!collapse) return;
    event.preventDefault();
    const collapsed = collapse.closest('tbody.channel-card')?.classList.toggle('collapsed');
    collapse.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
  });
}
