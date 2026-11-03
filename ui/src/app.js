import '@fluentui/web-components/button.js';
import '@fluentui/web-components/badge.js';
import { setTheme } from '@fluentui/web-components/theme/set-theme.js';
import { webDarkTheme, webLightTheme } from '@fluentui/tokens';
import './styles.css';

const root = document.documentElement;
const button = document.getElementById('theme-toggle');
const systemDark = window.matchMedia('(prefers-color-scheme: dark)');
let preference = null;
try { preference = localStorage.getItem('cockpit-theme'); } catch { /* Storage can be unavailable. */ }

function updateTheme() {
  const dark = preference === 'dark' || preference !== 'light' && systemDark.matches;
  root.dataset.theme = dark ? 'dark' : 'light';
  setTheme(dark ? webDarkTheme : webLightTheme);
  button.setAttribute('aria-label', dark ? 'Switch to light mode' : 'Switch to dark mode');
  button.title = button.getAttribute('aria-label');
  button.firstElementChild.textContent = dark ? '☀' : '◐';
}

button.addEventListener('click', () => {
  preference = root.dataset.theme === 'dark' ? 'light' : 'dark';
  try { localStorage.setItem('cockpit-theme', preference); } catch { /* Keep the current page usable. */ }
  updateTheme();
});
systemDark.addEventListener('change', updateTheme);
updateTheme();
button.hidden = false;

const sdkFilter = document.querySelector('[data-sdk-filter]');
if (sdkFilter) {
  const versionInput = sdkFilter.querySelector('#sdk-version-filter');
  const previewInput = sdkFilter.querySelector('#sdk-preview-filter');
  const rcInput = sdkFilter.querySelector('#sdk-rc-filter');
  const supportedInput = sdkFilter.querySelector('#sdk-supported-filter');
  const latestThreeInput = sdkFilter.querySelector('#sdk-latest-three-filter');
  const lifecycleInputs = [...sdkFilter.querySelectorAll('input[name="lifecycle"]')];
  const toggleFilters = [
    [previewInput, 'preview'],
    [rcInput, 'rc'],
    [supportedInput, 'supported'],
    [latestThreeInput, 'latest3'],
  ];
  const resultsCount = document.querySelector('[data-sdk-results]');
  const emptyState = document.querySelector('[data-sdk-empty]');
  const rows = [...document.querySelectorAll('[data-sdk-row]')];
  const groups = [...document.querySelectorAll('[data-sdk-group]')];

  function filterSdkVersions() {
    const query = versionInput.value.trim().toLowerCase();
    const terms = query.split(/\s+/).filter(Boolean);
    const lifecycle = new Set(lifecycleInputs.filter(input => input.checked).map(input => input.value));
    let visibleCount = 0;
    const latestThreeCounts = new Map();
    for (const row of rows) {
      const search = row.dataset.search.toLowerCase();
      const matchesQuery = !terms.length || terms.every(t => search.includes(t));
      const matchesPreview = previewInput.checked || row.dataset.preview !== 'true';
      const matchesRc = rcInput.checked || row.dataset.rc !== 'true';
      const matchesSupport = !supportedInput.checked || row.dataset.supported === 'true';
      const matchesLifecycle = lifecycle.has(row.dataset.lifecycle);
      const matchesFilters = matchesQuery && matchesPreview && matchesRc && matchesSupport && matchesLifecycle;
      let withinLatestThree = true;
      if (matchesFilters && latestThreeInput.checked) {
        const channelCount = latestThreeCounts.get(row.dataset.channel) ?? 0;
        withinLatestThree = channelCount < 3;
        latestThreeCounts.set(row.dataset.channel, channelCount + 1);
      }
      row.hidden = !matchesFilters || !withinLatestThree;
      if (!row.hidden) visibleCount++;
    }

    for (const group of groups) {
      const groupRows = [...group.querySelectorAll('[data-sdk-row]')];
      const groupVisibleCount = groupRows.reduce((count, row) => count + Number(!row.hidden), 0);
      group.hidden = groupVisibleCount === 0;
      group.querySelector('[data-sdk-group-count]').textContent = groupVisibleCount;
      if (query && groupVisibleCount > 0) group.open = true;
    }

    resultsCount.textContent = visibleCount;
    emptyState.hidden = visibleCount > 0;

    const selectedTab = document.querySelector('.tool-tabs [role="tab"][aria-selected="true"] .tool-tab-count');
    if (selectedTab) selectedTab.textContent = visibleCount;

    const url = new URL(window.location.href);
    if (versionInput.value.trim()) url.searchParams.set('q', versionInput.value.trim());
    else url.searchParams.delete('q');
    url.searchParams.delete('previews');
    for (const [input, parameter] of toggleFilters) {
      if (input.checked) url.searchParams.set(parameter, 'true');
      else url.searchParams.delete(parameter);
    }
    url.searchParams.set('lifecycle-filter', 'true');
    url.searchParams.delete('lifecycle');
    for (const value of lifecycle) url.searchParams.append('lifecycle', value);
    history.replaceState(null, '', url);
    for (const tab of document.querySelectorAll('.tool-tabs [role="tab"]')) {
      const tabUrl = new URL(tab.href);
      if (versionInput.value.trim()) tabUrl.searchParams.set('q', versionInput.value.trim());
      else tabUrl.searchParams.delete('q');
      tabUrl.searchParams.delete('previews');
      for (const [input, parameter] of toggleFilters) {
        if (input.checked) tabUrl.searchParams.set(parameter, 'true');
        else tabUrl.searchParams.delete(parameter);
      }
      tabUrl.searchParams.set('lifecycle-filter', 'true');
      tabUrl.searchParams.delete('lifecycle');
      for (const value of lifecycle) tabUrl.searchParams.append('lifecycle', value);
      tab.href = tabUrl;
    }
  }

  versionInput.addEventListener('input', filterSdkVersions);
  for (const [input] of toggleFilters) input.addEventListener('change', filterSdkVersions);
  for (const input of lifecycleInputs) input.addEventListener('change', filterSdkVersions);
  sdkFilter.addEventListener('submit', event => {
    event.preventDefault();
    filterSdkVersions();
  });
  filterSdkVersions();
}

// Instant Client-Side Filtering for Installations
const installationFilter = document.querySelector('[data-installation-filter]');
if (installationFilter) {
  const input = installationFilter.querySelector('#installation-filter-input');
  const rows = [...document.querySelectorAll('tbody tr[data-kind]')];
  const footerShown = document.querySelector('.table-footer strong') || document.querySelector('.inventory-footer span:first-child');
  let emptyState = document.querySelector('.table-scroll .empty-state');

  if (!emptyState) {
    const tableScroll = document.querySelector('.table-scroll');
    if (tableScroll) {
      emptyState = document.createElement('div');
      emptyState.className = 'empty-state client-empty-state';
      emptyState.hidden = true;
      emptyState.innerHTML = '<h3>No matching versions</h3><p class="muted">No installed versions match your filter.</p>';
      tableScroll.appendChild(emptyState);
    }
  }

  function filterInstallations() {
    const query = input.value.trim().toLowerCase();
    const terms = query.split(/\s+/).filter(Boolean);
    let visible = 0;
    for (const row of rows) {
      const search = (row.dataset.search || row.textContent || '').toLowerCase();
      const matches = !terms.length || terms.every(t => search.includes(t));
      row.hidden = !matches;
      if (matches) visible++;
    }
    if (footerShown) footerShown.textContent = `${visible} of ${rows.length} installations shown`;
    if (emptyState) emptyState.hidden = visible > 0;

    const url = new URL(window.location.href);
    if (query) url.searchParams.set('q', input.value.trim());
    else url.searchParams.delete('q');
    history.replaceState(null, '', url);
  }

  input.addEventListener('input', filterInstallations);
  installationFilter.addEventListener('submit', (e) => {
    e.preventDefault();
    filterInstallations();
  });
}

// Instant Client-Side Filtering for Installed Tools Table
const toolsFilterInput = document.querySelector('[data-tools-table-filter]');
if (toolsFilterInput) {
  const toolRows = [...document.querySelectorAll('[data-tool-row]')];
  const activeTabCount = document.querySelector('.tool-tabs [role="tab"][aria-selected="true"] .tool-tab-count');
  let toolsEmptyState = document.querySelector('.tool-panel .empty-state.client-tools-empty');

  if (!toolsEmptyState) {
    const tableScroll = document.querySelector('.tool-panel .table-scroll');
    if (tableScroll) {
      toolsEmptyState = document.createElement('div');
      toolsEmptyState.className = 'empty-state client-empty-state client-tools-empty';
      toolsEmptyState.hidden = true;
      toolsEmptyState.innerHTML = '<h3>No matching tools</h3><p class="muted">No tools match your filter.</p>';
      tableScroll.appendChild(toolsEmptyState);
    }
  }

  function filterTools() {
    const q = toolsFilterInput.value.trim().toLowerCase();
    const terms = q.split(/\s+/).filter(Boolean);
    let count = 0;
    for (const r of toolRows) {
      const s = (r.dataset.search || r.textContent || '').toLowerCase();
      const match = !terms.length || terms.every(t => s.includes(t));
      r.hidden = !match;
      if (match) count++;
    }
    if (activeTabCount) activeTabCount.textContent = count;
    if (toolsEmptyState) toolsEmptyState.hidden = count > 0;
  }

  toolsFilterInput.addEventListener('input', filterTools);
}

// Command Palette (⌘K)
const cmdkTrigger = document.getElementById('cmdk-trigger');
const cmdkDialog = document.getElementById('cmdk-dialog');
const cmdkScrim = document.getElementById('cmdk-scrim');
const cmdkInput = document.getElementById('cmdk-input');

if (cmdkTrigger && cmdkDialog && cmdkScrim && cmdkInput) {
  let activeIndex = -1;

  function getVisibleOptions() {
    return [...cmdkDialog.querySelectorAll('.opt:not([hidden])')];
  }

  function updateActiveOption(options, newIndex) {
    options.forEach(opt => opt.classList.remove('selected'));
    if (newIndex >= 0 && newIndex < options.length) {
      activeIndex = newIndex;
      options[activeIndex].classList.add('selected');
      options[activeIndex].scrollIntoView({ block: 'nearest' });
    } else {
      activeIndex = -1;
    }
  }

  function populateCmdkDynamicItems() {
    const dynGrp = document.getElementById('cmdk-dynamic-grp');
    const dynList = document.getElementById('cmdk-dynamic-list');
    if (!dynGrp || !dynList) return;
    const items = [];

    const vDataEl = document.getElementById('versions-data');
    if (vDataEl) {
      try {
        const vMap = JSON.parse(vDataEl.textContent || '{}');
        for (const v of Object.values(vMap)) {
          const badge = v.isEol ? '<span class="badge bad sm">EOL</span>' : (v.hasPatch ? '<span class="badge warn sm">Patch</span>' : '<span class="badge ok sm">Installed</span>');
          items.push(`<a class="opt" href="/?selected=${encodeURIComponent(v.version)}#inventory" data-cmdk-inspect="${v.version}"><div><b>Inspect ${v.name} ${v.version}</b> <small>· .NET ${v.channel}</small></div>${badge}</a>`);
          if (v.canRemove) {
            items.push(`<a class="opt" href="/?selected=${encodeURIComponent(v.version)}#inventory" data-cmdk-remove="${v.version}"><div><b>Remove ${v.name} ${v.version}</b> <small>· ${v.command}</small></div><span class="badge bad sm">Remove</span></a>`);
          }
        }
      } catch {}
    }

    for (const row of document.querySelectorAll('tr[data-tool-row]')) {
      const name = row.querySelector('.tool-name strong')?.textContent?.trim();
      const cmd = row.querySelector('.tool-command-pill code')?.textContent?.trim();
      if (name) {
        items.push(`<a class="opt" href="#${row.closest('section')?.id || 'main'}" data-cmdk-tool="${name}"><div><b>Tool: ${name}</b> <small>· command: ${cmd || name}</small></div><span class="badge sm">Tool</span></a>`);
      }
    }

    if (items.length > 0) {
      dynList.innerHTML = items.join('');
      dynGrp.hidden = false;
    } else {
      dynList.innerHTML = '';
      dynGrp.hidden = true;
    }
  }

  function openCmdk() {
    populateCmdkDynamicItems();
    cmdkDialog.hidden = false;
    cmdkScrim.hidden = false;
    cmdkInput.value = '';
    filterCmdk('');
    cmdkInput.focus();
  }

  function closeCmdk() {
    cmdkDialog.hidden = true;
    cmdkScrim.hidden = true;
    activeIndex = -1;
  }

  function filterCmdk(query) {
    const q = query.trim().toLowerCase();
    const options = [...cmdkDialog.querySelectorAll('.opt')];
    for (const opt of options) {
      const text = opt.textContent.toLowerCase();
      opt.hidden = Boolean(q && !text.includes(q));
    }
    const visible = getVisibleOptions();
    updateActiveOption(visible, visible.length > 0 ? 0 : -1);
  }

  cmdkTrigger.addEventListener('click', openCmdk);
  cmdkTrigger.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      openCmdk();
    }
  });

  cmdkScrim.addEventListener('click', closeCmdk);

  cmdkInput.addEventListener('input', (e) => filterCmdk(e.target.value));

  cmdkInput.addEventListener('keydown', (e) => {
    const visible = getVisibleOptions();
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      const next = activeIndex + 1 < visible.length ? activeIndex + 1 : 0;
      updateActiveOption(visible, next);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      const prev = activeIndex - 1 >= 0 ? activeIndex - 1 : visible.length - 1;
      updateActiveOption(visible, prev);
    } else if (e.key === 'Enter') {
      if (activeIndex >= 0 && visible[activeIndex]) {
        e.preventDefault();
        visible[activeIndex].click();
      }
    } else if (e.key === 'Escape') {
      closeCmdk();
    }
  });

  window.addEventListener('keydown', (e) => {
    if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      if (cmdkDialog.hidden) openCmdk();
      else closeCmdk();
    } else if (e.key === 'Escape' && !cmdkDialog.hidden) {
      closeCmdk();
    }
  });
}

// Preflight Welcome Banner & Skip Checkbox
const preflightBanner = document.getElementById('preflight-banner');
const dismissPreflightBtn = document.getElementById('dismiss-preflight-btn');
if (preflightBanner) {
  try {
    if (localStorage.getItem('cockpit-preflight-dismissed') !== 'true') {
      preflightBanner.hidden = false;
    }
  } catch {}
  if (dismissPreflightBtn) {
    dismissPreflightBtn.addEventListener('click', () => {
      preflightBanner.hidden = true;
      try { localStorage.setItem('cockpit-preflight-dismissed', 'true'); } catch {}
    });
  }
}

const skipPreflightCheckbox = document.getElementById('skip-preflight-checkbox');
if (skipPreflightCheckbox) {
  try {
    const isDismissed = localStorage.getItem('cockpit-preflight-dismissed') === 'true';
    skipPreflightCheckbox.checked = isDismissed || localStorage.getItem('cockpit-preflight-dismissed') === null;
    skipPreflightCheckbox.addEventListener('change', () => {
      if (skipPreflightCheckbox.checked) {
        localStorage.setItem('cockpit-preflight-dismissed', 'true');
      } else {
        localStorage.removeItem('cockpit-preflight-dismissed');
      }
    });
  } catch {}
}

// Quick Copy Affordance for Tool Commands, Paths, Checksums, and Snippets
document.addEventListener('click', (e) => {
  const target = e.target.closest('.tool-command-pill, code.installation-path, .tool-cli-preview code, .sdk-checksum code, .copy-btn, .drawer-sha, .drawer-path');
  if (!target) return;
  const text = (target.dataset.copy || target.textContent || '').trim();
  if (!text) return;
  navigator.clipboard?.writeText(text).then(() => {
    const origTitle = target.getAttribute('title') || '';
    target.setAttribute('title', 'Copied to clipboard!');
    target.classList.add('copied');
    setTimeout(() => {
      target.classList.remove('copied');
      if (origTitle) target.setAttribute('title', origTitle);
      else target.removeAttribute('title');
    }, 1200);
  }).catch(() => {});
});

// Versions Hub: Instant Client-Side Filter & Detail Drawer
const versionsDataEl = document.getElementById('versions-data');
let versionsData = {};
if (versionsDataEl) {
  try { versionsData = JSON.parse(versionsDataEl.textContent || '{}'); } catch {}
}

const versionFilterInput = document.getElementById('installed-version-filter');
const versionRows = [...document.querySelectorAll('tr[data-kind]')];
const channelHeaders = [...document.querySelectorAll('tr.channel-group-header-row')];
const versionsCount = document.querySelector('[data-versions-count]');
const drawer = document.getElementById('version-drawer');
const drawerCloseBtn = document.getElementById('drawer-close');

function getRowVersion(row) {
  return row.querySelector('[data-inspect]')?.dataset.inspect ||
         row.querySelector('.version-badge')?.textContent.trim() || '';
}

function getRowChannel(row) {
  return row.querySelector('[data-inspect]')?.dataset.channel ||
         getRowVersion(row).split('.')[0] || '';
}

function openVersionDrawer(version) {
  const data = versionsData[version];
  if (!data || !drawer) return;

  drawer.hidden = false;

  versionRows.forEach(row => {
    const isThis = getRowVersion(row) === version;
    row.classList.toggle('row-selected', isThis);
    row.setAttribute('aria-selected', isThis ? 'true' : 'false');
  });

  const eyebrow = document.getElementById('drawer-eyebrow');
  const title = document.getElementById('drawer-title');
  const badge = document.getElementById('drawer-badge');
  const alert = document.getElementById('drawer-alert');
  const channel = document.getElementById('drawer-channel');
  const runtime = document.getElementById('drawer-runtime');
  const released = document.getElementById('drawer-released');
  const location = document.getElementById('drawer-location');
  const patchSection = document.getElementById('drawer-patch-section');
  const command = document.getElementById('drawer-command');
  const downloadBtn = document.getElementById('drawer-download-btn');
  const removeForm = document.getElementById('drawer-remove-form');

  if (eyebrow) eyebrow.textContent = `.NET ${data.channel} · ${data.kind === 'sdk' ? 'SDK' : 'Runtime'}`;
  if (title) title.textContent = data.version;

  if (badge) {
    if (data.hasPatch) badge.innerHTML = '<span class="badge warn">Patch available</span>';
    else if (data.isEol) badge.innerHTML = '<span class="badge bad">End of life</span>';
    else badge.innerHTML = '<span class="badge ok">Up to date</span>';
  }

  if (alert) {
    if (data.isEol) {
      alert.className = 'drawer-alert drawer-alert-bad';
      alert.innerHTML = `<strong>🛡 .NET ${data.channel} has reached End of Life</strong><p class="muted">Security updates are no longer released for this channel. We recommend upgrading to a supported LTS release.</p>`;
    } else if (data.hasPatch) {
      alert.className = 'drawer-alert drawer-alert-warn';
      alert.innerHTML = `<strong>⏱ Newer patch ${data.patchVersion} available</strong><p class="muted">A newer servicing update is available with security and reliability fixes.</p>`;
    } else {
      alert.className = 'drawer-alert drawer-alert-ok';
      alert.innerHTML = `<strong>✓ Running latest release for .NET ${data.channel}</strong><p class="muted">This version is actively supported and up to date.</p>`;
    }
  }

  const channelType = data.isLts ? 'Long Term Support (LTS)' : 'Standard Term Support (STS)';
  if (channel) channel.textContent = `.NET ${data.channel} · ${channelType}`;
  if (runtime) runtime.textContent = data.includedRuntime || (data.kind === 'sdk' ? `${data.channel}.0` : 'Included');
  if (released) released.textContent = data.released || 'Official release';
  if (location) {
    location.textContent = data.location;
    const copyBtn = location.parentElement?.querySelector('.copy-btn');
    if (copyBtn) copyBtn.dataset.copy = data.location;
  }

  if (patchSection) {
    if (data.hasPatch) {
      patchSection.hidden = false;
      const cvesHtml = (data.cves || []).map(c => `<a class="cve-pill" href="${c.url || '#'}" target="_blank" rel="noopener">${c.id}</a>`).join('');
      const shaShort = (data.patchSha && data.patchSha.length > 32) ? data.patchSha.slice(0, 32) + '...' : (data.patchSha || '');
      patchSection.innerHTML = `
        <h4 class="drawer-section-title">Newest patch</h4>
        <div class="drawer-patch-box">
          <div class="drawer-patch-header">
            <strong>${data.patchVersion}</strong>
            <span class="muted text-xs">${data.patchDate || ''}</span>
          </div>
          <div class="drawer-cve-list">
            ${data.cves?.length ? `<span class="badge warn sm">🛡 ${data.cves.length} security fixes</span>` : ''}
            ${cvesHtml}
          </div>
          <div class="drawer-code-box">
            <code class="drawer-sha">${shaShort}</code>
            <button class="button icon ghost copy-btn" data-copy="${data.patchSha || ''}" type="button" title="Copy SHA-512">📋</button>
          </div>
        </div>`;
    } else {
      patchSection.hidden = true;
      patchSection.innerHTML = '';
    }
  }

  if (command) {
    command.textContent = data.command;
    const cmdCopyBtn = command.parentElement?.querySelector('.copy-btn') || command.closest('.drawer-command-box')?.querySelector('.copy-btn');
    if (cmdCopyBtn) cmdCopyBtn.dataset.copy = data.command;
  }

  if (downloadBtn) {
    if (data.hasPatch && data.patchUrl) {
      downloadBtn.hidden = false;
      downloadBtn.href = data.patchUrl;
    } else {
      downloadBtn.hidden = true;
    }
  }

  if (removeForm) {
    if (data.canRemove) {
      removeForm.innerHTML = `
        <form method="post" action="/installation/remove">
          <input type="hidden" name="version" value="${data.version}">
          <input type="hidden" name="kind" value="${data.kind}">
          <button class="button danger drawer-remove-btn" type="submit"><svg class="i xs" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg> Remove</button>
        </form>`;
    } else {
      removeForm.innerHTML = '';
    }
  }
}

function closeVersionDrawer() {
  if (drawer) drawer.hidden = true;
  versionRows.forEach(row => {
    row.classList.remove('row-selected');
    row.setAttribute('aria-selected', 'false');
  });
}

if (drawerCloseBtn) {
  drawerCloseBtn.addEventListener('click', (e) => {
    e.preventDefault();
    closeVersionDrawer();
  });
}

// Click listener on rows & inspect buttons
document.addEventListener('click', (e) => {
  const row = e.target.closest('tr[data-kind]');
  if (row) {
    // If clicking a form button or link, let it proceed
    if (e.target.closest('form, a:not(.inspect-btn)')) return;
    const version = getRowVersion(row);
    if (version) {
      e.preventDefault();
      openVersionDrawer(version);
    }
    return;
  }

  const inspectBtn = e.target.closest('.inspect-btn');
  if (inspectBtn) {
    const version = inspectBtn.dataset.inspect;
    if (version) {
      e.preventDefault();
      openVersionDrawer(version);
    }
  }
});

// Arrow key navigation between rows when drawer is open
window.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && drawer && !drawer.hidden) {
    closeVersionDrawer();
    return;
  }

  if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && drawer && !drawer.hidden) {
    const visibleRows = versionRows.filter(r => !r.hidden);
    if (!visibleRows.length) return;
    const currentIndex = visibleRows.findIndex(r => r.classList.contains('row-selected'));
    let nextIndex = 0;
    if (currentIndex >= 0) {
      nextIndex = e.key === 'ArrowDown'
        ? (currentIndex + 1 < visibleRows.length ? currentIndex + 1 : 0)
        : (currentIndex - 1 >= 0 ? currentIndex - 1 : visibleRows.length - 1);
    }
    e.preventDefault();
    const nextRow = visibleRows[nextIndex];
    const nextVersion = getRowVersion(nextRow);
    if (nextVersion) {
      openVersionDrawer(nextVersion);
      nextRow.scrollIntoView({ block: 'nearest' });
    }
  }
});

// Instant client-side search for versions
if (versionFilterInput) {
  versionFilterInput.addEventListener('input', () => {
    const query = versionFilterInput.value.trim().toLowerCase();
    const terms = query.split(/\s+/).filter(Boolean);
    let visibleCount = 0;
    const channelVisibility = new Map();

    for (const row of versionRows) {
      const version = getRowVersion(row);
      const text = `${version} ${row.textContent}`.toLowerCase();
      const matches = !terms.length || terms.every(t => text.includes(t));
      row.hidden = !matches;
      if (matches) {
        visibleCount++;
        channelVisibility.set(getRowChannel(row), true);
      }
    }

    for (const header of channelHeaders) {
      const channel = header.dataset.channelHeader;
      header.hidden = !channelVisibility.has(channel);
    }

    if (versionsCount) versionsCount.textContent = `${visibleCount} of ${versionRows.length} installations shown`;

    const emptyRow = document.querySelector('tr.empty-row');
    if (emptyRow) emptyRow.hidden = visibleCount > 0;
  });
}

// Safe Removal Confirmation Dialog (<dialog id="remove-dialog" closedby="any">)
const removeDialog = document.getElementById('remove-dialog');
if (removeDialog) {
  let pendingRemoveForm = null;
  const cancelBtn = document.getElementById('remove-dialog-cancel');
  const confirmBtn = document.getElementById('remove-dialog-confirm');

  if (!('closedBy' in HTMLDialogElement.prototype)) {
    removeDialog.addEventListener('click', (e) => {
      if (e.target === removeDialog) removeDialog.close();
    });
  }

  document.addEventListener('submit', (e) => {
    const form = e.target.closest('form[action="/installation/remove"]');
    if (!form || form.dataset.confirmed === 'true') return;
    e.preventDefault();
    pendingRemoveForm = form;

    const version = form.querySelector('input[name="version"]')?.value || '';
    const kind = form.querySelector('input[name="kind"]')?.value || 'sdk';
    const data = versionsData[version] || {
      version,
      kind,
      name: kind === 'sdk' ? '.NET SDK' : '.NET Runtime',
      channel: version.split('.')[0] || '',
      location: '',
      isEol: false,
      command: `sudo dotnet-core-uninstall remove ${version} --${kind === 'sdk' ? 'sdk' : 'runtime'}`
    };

    const titleEl = document.getElementById('remove-dialog-title');
    const subEl = document.getElementById('remove-dialog-subtitle');
    const itemsEl = document.getElementById('remove-dialog-items');
    const warnEl = document.getElementById('remove-dialog-warn');
    const cmdEl = document.getElementById('remove-dialog-cmd');
    const copyEl = document.getElementById('remove-dialog-copy');
    const confirmTextEl = document.getElementById('remove-dialog-confirm-text');

    if (titleEl) titleEl.textContent = `Remove ${data.name} ${data.version} from this machine?`;
    if (subEl) subEl.textContent = data.isEol ? `1 item · out of support (.NET ${data.channel})` : `1 item · .NET ${data.channel} (${data.kind.toUpperCase()})`;
    if (itemsEl) {
      const badge = data.isEol ? '<span class="badge bad sm">EOL</span>' : `<span class="badge sm">${data.kind.toUpperCase()}</span>`;
      itemsEl.innerHTML = `
        <div class="remove-item-row">
          <input type="checkbox" checked disabled aria-label="Selected for removal">
          <div class="remove-item-copy">
            <strong>${data.name} <code>${data.version}</code></strong>
            <code>${data.location || 'Managed global installation'}</code>
          </div>
          ${badge}
        </div>`;
    }
    if (warnEl) {
      warnEl.textContent = `Removal runs through Microsoft's dotnet-core-uninstall. Projects targeting net${data.channel}.0 may no longer build or run unless another compatible version is installed.`;
    }
    if (cmdEl) cmdEl.textContent = data.command;
    if (copyEl) copyEl.dataset.copy = data.command;
    if (confirmTextEl) confirmTextEl.textContent = `Remove ${data.version}`;

    removeDialog.showModal();
  });

  if (cancelBtn) {
    cancelBtn.addEventListener('click', () => {
      pendingRemoveForm = null;
      removeDialog.close();
    });
  }

  if (confirmBtn) {
    confirmBtn.addEventListener('click', () => {
      if (pendingRemoveForm) {
        pendingRemoveForm.dataset.confirmed = 'true';
        removeDialog.close();
        pendingRemoveForm.submit();
      }
    });
  }
}

// NuGet Tool Installation Sheet (<dialog id="add-tool-dialog" closedby="any">)
const addToolDialog = document.getElementById('add-tool-dialog');
const openAddToolBtn = document.getElementById('open-add-tool-btn');
const closeAddToolBtn = document.getElementById('close-add-tool-btn');

if (addToolDialog) {
  let sheetScope = 'global';
  let lastFocusedPkg = null;
  let searchTimer = null;
  const searchInput = document.getElementById('sheet-search-input');
  const resultsBox = document.getElementById('sheet-results');
  const cliPreview = document.getElementById('sheet-cli-preview');
  const cliCopy = document.getElementById('sheet-cli-copy');
  const scopeBtns = [...addToolDialog.querySelectorAll('[data-sheet-scope]')];

  if (!('closedBy' in HTMLDialogElement.prototype)) {
    addToolDialog.addEventListener('click', (e) => {
      if (e.target === addToolDialog) addToolDialog.close();
    });
  }

  function updateCliPreview(pkgId, version) {
    const p = pkgId || lastFocusedPkg?.packageId || '<package-id>';
    const v = version || lastFocusedPkg?.version || '';
    const flag = sheetScope === 'local' ? '--local' : '--global';
    const cmd = v ? `dotnet tool install ${flag} ${p} --version ${v}` : `dotnet tool install ${flag} ${p}`;
    if (cliPreview) cliPreview.textContent = cmd;
    if (cliCopy) cliCopy.dataset.copy = cmd;
  }

  function getMonogram(id) {
    if (!id) return 'TL';
    const parts = id.split(/[.\-_]/).filter(p => p && p.toLowerCase() !== 'dotnet' && p.toLowerCase() !== 'tool');
    if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
    if (parts.length === 1 && parts[0].length >= 2) return parts[0].slice(0, 2).toUpperCase();
    return id.slice(0, 2).toUpperCase();
  }

  function getColorIdx(id) {
    let h = 0;
    for (const c of (id || '')) h = (h * 31) + c.toLowerCase().charCodeAt(0);
    return Math.abs(h) % 5;
  }

  async function runSheetSearch(q) {
    const query = q.trim();
    if (!query) {
      resultsBox.innerHTML = '<div class="empty-state"><p class="muted">Type a package name above and press Enter to search NuGet.org.</p></div>';
      return;
    }
    resultsBox.innerHTML = '<div class="empty-state"><p class="muted">Searching NuGet.org...</p></div>';
    try {
      const res = await fetch(`/api/tools/search?q=${encodeURIComponent(query)}`);
      const data = await res.json();
      if (data.error) {
        resultsBox.innerHTML = `<div class="empty-state error-state"><h3>Search failed</h3><p>${data.error}</p></div>`;
        return;
      }
      const items = data.results || [];
      if (!items.length) {
        resultsBox.innerHTML = `<div class="empty-state"><h3>No tools found</h3><p class="muted">No NuGet tools matched "${query}".</p></div>`;
        return;
      }
      lastFocusedPkg = items[0];
      updateCliPreview(items[0].packageId, items[0].version);
      resultsBox.innerHTML = items.map((item, idx) => `
        <div class="sheet-result-item ${idx === 0 ? 'active' : ''}" data-pkg-id="${item.packageId}" data-pkg-ver="${item.version}">
          <span class="tool-monogram monogram-${getColorIdx(item.packageId)}">${getMonogram(item.packageId)}</span>
          <div class="sheet-result-body">
            <div class="sheet-result-top">
              <strong>${item.packageId}</strong>
              <code class="muted">${item.version}</code>
              ${item.verified ? '<span class="badge ok sm">✓ Verified</span>' : ''}
            </div>
            <div class="sheet-result-meta muted text-xs">
              <span>👤 ${item.authors}</span>
              <span>↓ ${item.downloads}</span>
            </div>
          </div>
          <form method="post" action="/tools/install">
            <input type="hidden" name="packageId" value="${item.packageId}">
            <input type="hidden" name="version" value="${item.version}">
            <input type="hidden" name="q" value="${query}">
            <input type="hidden" name="scope" value="${sheetScope}" data-sheet-form-scope>
            <button type="submit" class="button ${idx === 0 ? 'primary' : ''} sm">+ Install</button>
          </form>
        </div>`).join('');
    } catch {
      resultsBox.innerHTML = '<div class="empty-state error-state"><p>Could not reach local search endpoint.</p></div>';
    }
  }

  for (const btn of scopeBtns) {
    btn.addEventListener('click', () => {
      if (btn.disabled) return;
      sheetScope = btn.dataset.sheetScope || 'global';
      scopeBtns.forEach(b => b.classList.toggle('active', b === btn));
      for (const hiddenInput of addToolDialog.querySelectorAll('[data-sheet-form-scope]')) {
        hiddenInput.value = sheetScope;
      }
      updateCliPreview();
    });
  }

  if (resultsBox) {
    resultsBox.addEventListener('mouseover', (e) => {
      const row = e.target.closest('.sheet-result-item');
      if (!row) return;
      resultsBox.querySelectorAll('.sheet-result-item').forEach(r => r.classList.toggle('active', r === row));
      updateCliPreview(row.dataset.pkgId, row.dataset.pkgVer);
    });
  }

  if (searchInput) {
    searchInput.addEventListener('input', () => {
      clearTimeout(searchTimer);
      searchTimer = setTimeout(() => runSheetSearch(searchInput.value), 260);
    });
    searchInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        clearTimeout(searchTimer);
        runSheetSearch(searchInput.value);
      }
    });
  }

  if (openAddToolBtn) {
    openAddToolBtn.addEventListener('click', () => {
      addToolDialog.showModal();
      searchInput?.focus();
    });
  }

  if (closeAddToolBtn) {
    closeAddToolBtn.addEventListener('click', () => addToolDialog.close());
  }
}

// Settings Page Controls (Appearance segmented control & Pre-flight launch toggle)
const themeSegBtns = [...document.querySelectorAll('[data-theme-pref]')];
if (themeSegBtns.length) {
  function syncThemeSegButtons() {
    const current = preference || 'system';
    for (const btn of themeSegBtns) {
      btn.classList.toggle('active', btn.dataset.themePref === current);
    }
  }
  syncThemeSegButtons();
  for (const btn of themeSegBtns) {
    btn.addEventListener('click', () => {
      const val = btn.dataset.themePref;
      if (val === 'system') {
        preference = null;
        try { localStorage.removeItem('cockpit-theme'); } catch {}
      } else {
        preference = val;
        try { localStorage.setItem('cockpit-theme', val); } catch {}
      }
      updateTheme();
      syncThemeSegButtons();
    });
  }
}

const settingsPreflightToggle = document.getElementById('settings-preflight-toggle');
if (settingsPreflightToggle) {
  try {
    settingsPreflightToggle.checked = localStorage.getItem('cockpit-preflight-dismissed') === 'false';
    settingsPreflightToggle.addEventListener('change', () => {
      localStorage.setItem('cockpit-preflight-dismissed', settingsPreflightToggle.checked ? 'false' : 'true');
    });
  } catch {}
}

for (const navLink of document.querySelectorAll('.settings-nav-item')) {
  navLink.addEventListener('click', () => {
    document.querySelectorAll('.settings-nav-item').forEach(el => el.classList.toggle('active', el === navLink));
  });
}
