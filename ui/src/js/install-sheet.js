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
  const previewsToggle = dialog.querySelector('[data-install-previews]');
  const state = {
    product: dialog.dataset.product || 'SDK',
    view: dialog.dataset.view || 'latest',
    previews: dialog.dataset.previews === 'true',
  };

  function showCommand(row) {
    if (!row?.dataset.cmd) return;
    preview.textContent = row.dataset.cmd;
    copy.dataset.copy = row.dataset.cmd;
  }

  function syncButtons() {
    for (const button of filterButtons) {
      const active = button.dataset.installLifecycle === state.view;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', active ? 'true' : 'false');
    }
  }

  function matchesView(data) {
    const supported = data.supported === 'true';
    if (state.view === 'latest') return supported && data.latest === 'true';
    if (state.view === 'supported') return supported;
    if (state.view === 'lts') return supported && data.lts === 'true';
    return true;
  }

  function filter() {
    let visible = 0;
    let first = null;
    for (const row of rows) {
      const data = row.dataset;
      const isPreview = data.preview === 'true';
      const show = data.product === state.product
        && matchesView(data)
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
    button.addEventListener('click', event => {
      event.preventDefault();
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
    button.addEventListener('click', event => {
      event.preventDefault();
      state.view = button.dataset.installLifecycle;
      syncButtons();
      filter();
    });
  }

  previewsToggle?.addEventListener('click', event => {
    event.preventDefault();
    state.previews = !state.previews;
    previewsToggle.classList.toggle('active', state.previews);
    previewsToggle.setAttribute('aria-pressed', state.previews ? 'true' : 'false');
    filter();
  });

  results?.addEventListener('mouseover', event => {
    const row = event.target.closest('[data-install-row]');
    if (!row) return;
    for (const other of rows) other.classList.toggle('active', other === row);
    showCommand(row);
  });

  search?.addEventListener('input', filter);
  dialog.querySelector('[data-install-search]')?.addEventListener('submit', event => event.preventDefault());

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
      for (const name of ['install', 'tab', 'product', 'view', 'previews', 'installQ']) url.searchParams.delete(name);
    });
    open();
  }
}
