import { esc } from './dom.js';
import { openModal } from './dialogs.js';

export function setupAddToolSheet() {
  const dialog = document.getElementById('add-tool-dialog');
  if (!dialog) return;
  const inlineSearch = document.getElementById('tool-search-section');
  // The inline form is the path without JavaScript. With it, the sheet does this job.
  if (inlineSearch) inlineSearch.hidden = true;

  const search = document.getElementById('sheet-search-input');
  const results = document.getElementById('sheet-results');
  const scopeButtons = [...dialog.querySelectorAll('[data-sheet-scope]')];
  let scope = 'global';
  let timer = null;

  // Each Install button opens the shared confirmation, which shows the exact command for that tool.
  function applyScope() {
    for (const form of dialog.querySelectorAll('form[data-confirm]')) {
      form.querySelector('input[name="scope"]').value = scope;
      form.dataset.confirmCommand = form.dataset.confirmCommand.replace(/--(global|local)/, `--${scope}`);
      form.dataset.confirmBody = results.dataset[scope === 'local' ? 'bodyLocal' : 'bodyGlobal'];
    }
  }

  function installTrigger(item) {
    const command = `dotnet tool install --${scope} \\\n  ${item.packageId} \\\n  --version ${item.version}`;
    return `<form method="get" action="/confirm" data-confirm
        data-confirm-title="Install ${esc(item.packageId)}?" data-confirm-body=""
        data-confirm-command="${esc(command)}" data-confirm-label="Install tool"
        data-confirm-busy="Installing ${esc(item.packageId)}…" data-confirm-post="/tools/install"
        data-confirm-danger="false" data-confirm-admin="false">
        <input type="hidden" name="action" value="tool-install">
        <input type="hidden" name="packageId" value="${esc(item.packageId)}">
        <input type="hidden" name="version" value="${esc(item.version)}">
        <input type="hidden" name="scope" value="${esc(scope)}">
        <button type="submit" class="button sm">Install</button>
      </form>`;
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
      results.innerHTML = items.map(item => `
        <div class="sheet-result-item" data-pkg-id="${esc(item.packageId)}">
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
          ${installTrigger(item)}
        </div>`).join('');
      applyScope();
    } catch {
      message('<h3>Search failed</h3><p class="muted">NuGet.org could not be searched. Try again.</p>');
    }
  }

  for (const button of scopeButtons) {
    button.addEventListener('click', () => {
      if (button.disabled) return;
      scope = button.dataset.sheetScope;
      for (const other of scopeButtons) other.classList.toggle('active', other === button);
      applyScope();
    });
  }

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
  if (params.get('add') === '1' || params.get('q')) {
    openModal(dialog, search);
    if (search.value.trim()) runSearch(search.value);
  }
}
