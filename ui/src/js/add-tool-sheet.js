import { esc } from './dom.js';
import { openModal } from './dialogs.js';

export function setupAddToolSheet() {
  const dialog = document.getElementById('add-tool-dialog');
  if (!dialog) return;
  const inlineSearch = document.getElementById('tool-search-section');
  if (inlineSearch?.dataset.hasQuery === 'false') inlineSearch.hidden = true;

  const search = document.getElementById('sheet-search-input');
  const results = document.getElementById('sheet-results');
  const preview = document.getElementById('sheet-cli-preview');
  const copy = document.getElementById('sheet-cli-copy');
  const scopeButtons = [...dialog.querySelectorAll('[data-sheet-scope]')];
  const firstRow = results.querySelector('[data-pkg-id]');
  let scope = 'global';
  let focused = { id: firstRow?.dataset.pkgId, version: firstRow?.dataset.pkgVer };
  let timer = null;

  function showCommand() {
    if (!focused.id) return;
    const lines = [`dotnet tool install --${scope} \\`, `  ${focused.id}${focused.version ? ' \\' : ''}`];
    if (focused.version) lines.push(`  --version ${focused.version}`);
    const text = lines.join('\n');
    preview.textContent = text;
    copy.dataset.copy = text;
  }

  function message(html) {
    results.innerHTML = `<div class="empty-state">${html}</div>`;
  }

  async function runSearch(rawQuery) {
    const query = rawQuery.trim();
    if (!query) {
      message('<p class="muted">Type a package name to search NuGet.org.</p>');
      return;
    }
    message('<p class="muted">Searching NuGet.org…</p>');
    try {
      const response = await fetch(`/api/tools/search?q=${encodeURIComponent(query)}`);
      const data = await response.json();
      if (search.value.trim() !== query) return;
      if (data.error) {
        message(`<h3>Search failed</h3><p>${esc(data.error)}</p>`);
        return;
      }
      const items = data.results || [];
      if (!items.length) {
        message(`<h3>No tools found</h3><p class="muted">Nothing on NuGet.org matches “${esc(query)}”.</p>`);
        return;
      }
      focused = { id: items[0].packageId, version: items[0].version };
      showCommand();
      results.innerHTML = items.map((item, index) => `
        <div class="sheet-result-item ${index === 0 ? 'active' : ''}" data-pkg-id="${esc(item.packageId)}" data-pkg-ver="${esc(item.version)}">
          <span class="tool-monogram tool-logo has-img"><img src="/api/tools/icon?packageId=${encodeURIComponent(item.packageId)}&version=${encodeURIComponent(item.version)}" alt="" class="tool-logo-img" width="36" height="36" loading="lazy"></span>
          <div class="sheet-result-body">
            <div class="sheet-result-top">
              <strong>${esc(item.packageId)}</strong>
              <code class="muted">${esc(item.version)}</code>
              ${item.verified ? '<span class="badge ok sm">Verified</span>' : ''}
            </div>
            <div class="sheet-result-meta muted text-xs">
              <span>by ${esc(item.authors)}</span>
              <span>${esc(item.downloads)} downloads</span>
            </div>
          </div>
          <form method="post" action="/tools/install" data-busy="Installing…">
            <input type="hidden" name="packageId" value="${esc(item.packageId)}">
            <input type="hidden" name="version" value="${esc(item.version)}">
            <input type="hidden" name="q" value="${esc(query)}">
            <input type="hidden" name="scope" value="${esc(scope)}" data-sheet-form-scope>
            <button type="submit" class="button ${index === 0 ? 'primary' : ''} sm">Install</button>
          </form>
        </div>`).join('');
    } catch {
      message('<h3>Search failed</h3><p class="muted">NuGet.org could not be searched. Try again.</p>');
    }
  }

  for (const button of scopeButtons) {
    button.addEventListener('click', () => {
      if (button.disabled) return;
      scope = button.dataset.sheetScope;
      for (const other of scopeButtons) other.classList.toggle('active', other === button);
      for (const input of dialog.querySelectorAll('[data-sheet-form-scope]')) input.value = scope;
      showCommand();
    });
  }

  results.addEventListener('mouseover', event => {
    const row = event.target.closest('[data-pkg-id]');
    if (!row) return;
    for (const other of results.querySelectorAll('[data-pkg-id]')) other.classList.toggle('active', other === row);
    focused = { id: row.dataset.pkgId, version: row.dataset.pkgVer };
    showCommand();
  });

  search.addEventListener('input', () => {
    clearTimeout(timer);
    timer = setTimeout(() => runSearch(search.value), 260);
  });
  search.addEventListener('keydown', event => {
    if (event.key !== 'Enter') return;
    event.preventDefault();
    clearTimeout(timer);
    runSearch(search.value);
  });

  document.addEventListener('click', event => {
    if (event.target.closest('#open-add-tool-btn')) {
      event.preventDefault();
      openModal(dialog, search);
    } else if (event.target.closest('#close-add-tool-btn')) {
      dialog.close();
    }
  });

  const params = new URLSearchParams(window.location.search);
  if (params.get('add') === '1') {
    openModal(dialog, search);
    if (search.value.trim()) runSearch(search.value);
  }
}
