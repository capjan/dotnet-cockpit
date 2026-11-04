import { matchesTerms, replaceQuery } from './dom.js';
import { openModal } from './dialogs.js';

export function setupInstallSheet() {
  const dialog = document.getElementById('install-dotnet-dialog');
  if (!dialog) return;
  const search = document.getElementById('install-dotnet-search-input');
  const results = document.getElementById('install-dotnet-results');
  const empty = document.getElementById('install-dotnet-empty');
  const preview = document.getElementById('install-dotnet-cli-preview');
  const copy = document.getElementById('install-dotnet-cli-copy');
  const productButtons = [...dialog.querySelectorAll('[data-install-product]')];
  const filterButtons = [...dialog.querySelectorAll('[data-install-lifecycle]')];
  const groups = [...dialog.querySelectorAll('[data-install-channel-group]')];
  const rows = [...dialog.querySelectorAll('[data-install-row]')];
  const state = { product: 'SDK', latest: true, supported: true, lts: false, previews: false };

  function showCommand(row) {
    if (!row?.dataset.cmd) return;
    preview.textContent = row.dataset.cmd;
    copy.dataset.copy = row.dataset.cmd;
  }

  function syncButtons() {
    const unfiltered = !state.latest && !state.supported && !state.lts;
    for (const button of filterButtons) {
      const key = button.dataset.installLifecycle;
      const active = key === 'all' ? unfiltered : state[key];
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', active ? 'true' : 'false');
    }
  }

  function filter() {
    let visible = 0;
    let first = null;
    for (const row of rows) {
      const data = row.dataset;
      const isPreview = data.preview === 'true';
      const show = data.product === state.product
        && (!state.latest || data.latest === 'true')
        && (!state.supported || data.supported === 'true' || (state.previews && isPreview))
        && (!state.lts || data.lts === 'true')
        && (state.previews || !isPreview)
        && matchesTerms(data.search || '', search.value);
      row.hidden = !show;
      if (show) {
        visible++;
        first ??= row;
      } else {
        row.classList.remove('active');
      }
    }
    for (const group of groups) {
      const count = group.querySelectorAll('[data-install-row]:not([hidden])').length;
      group.hidden = count === 0;
      const label = group.querySelector('[data-install-channel-count]');
      if (label) label.textContent = count === 1 ? '1 release' : `${count} releases`;
    }
    if (empty) empty.hidden = visible > 0;
    if (!rows.some(row => !row.hidden && row.classList.contains('active')) && first) {
      first.classList.add('active');
      showCommand(first);
    }
  }

  for (const button of productButtons) {
    button.addEventListener('click', () => {
      state.product = button.dataset.installProduct;
      for (const other of productButtons) {
        const active = other === button;
        other.classList.toggle('active', active);
        other.setAttribute('aria-pressed', active ? 'true' : 'false');
      }
      filter();
    });
  }

  for (const button of filterButtons) {
    button.addEventListener('click', () => {
      const key = button.dataset.installLifecycle;
      if (key === 'all') {
        const unfiltered = !state.latest && !state.supported && !state.lts;
        state.latest = unfiltered;
        state.supported = unfiltered;
        state.lts = false;
      } else {
        state[key] = !state[key];
      }
      syncButtons();
      filter();
    });
  }

  results?.addEventListener('mouseover', event => {
    const row = event.target.closest('[data-install-row]');
    if (!row) return;
    for (const other of rows) other.classList.toggle('active', other === row);
    showCommand(row);
  });

  search?.addEventListener('input', filter);
  search?.addEventListener('keydown', event => {
    if (event.key === 'Enter') event.preventDefault();
  });

  function open() {
    openModal(dialog, search);
  }

  document.addEventListener('click', event => {
    if (event.target.closest('#open-install-dotnet-btn')) {
      event.preventDefault();
      open();
    } else if (event.target.closest('#close-install-dotnet-btn')) {
      event.preventDefault();
      dialog.close();
    }
  });

  if (dialog.hasAttribute('data-open-on-load')) {
    replaceQuery(url => {
      url.searchParams.delete('install');
      url.searchParams.delete('tab');
    });
    open();
  }
}
