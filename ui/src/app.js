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
  button.firstElementChild.innerHTML = dark
    ? '<svg class="i sm" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2m-7.1-14.1 1.4 1.4m11.4 11.4 1.4 1.4M2 12h2m16 0h2M4.9 19.1l1.4-1.4m11.4-11.4 1.4-1.4"/></svg>'
    : '<svg class="i sm" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5Z"/></svg>';
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

const versionFilterForm = document.querySelector('[data-versions-filter]');
const versionFilterInput = document.getElementById('installed-version-filter');
const versionRows = [...document.querySelectorAll('tr[data-kind]')];
const channelCards = [...document.querySelectorAll('tbody.channel-card[data-channel-card]')];
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

  const url = new URL(window.location.href);
  url.searchParams.set('selected', version);
  history.replaceState(null, '', url);

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
  const runtimeLabel = document.getElementById('drawer-runtime-label');
  const runtime = document.getElementById('drawer-runtime');
  const released = document.getElementById('drawer-released');
  const location = document.getElementById('drawer-location');
  const patchSection = document.getElementById('drawer-patch-section');
  const command = document.getElementById('drawer-command');
  const downloadBtn = document.getElementById('drawer-download-btn');
  const downloadLabel = document.getElementById('drawer-download-label');
  const removeForm = document.getElementById('drawer-remove-form');

  if (eyebrow) eyebrow.textContent = `.NET ${data.channel} · ${data.kind === 'sdk' ? 'SDK' : 'Runtime'}`;
  if (title) {
    const shortVer = data.displayVersion || data.version;
    if (data.version && data.version.startsWith(shortVer) && data.version.length > shortVer.length) {
      title.innerHTML = `${shortVer}<span class="sr-only">${data.version.slice(shortVer.length)}</span>`;
    } else {
      title.textContent = data.version;
    }
  }

  if (badge) {
    if (data.hasPatch) badge.innerHTML = '<span class="badge warn">Patch available</span>';
    else if (data.isEol) badge.innerHTML = '<span class="badge bad">End of life</span>';
    else badge.innerHTML = '<span class="badge ok">Up to date</span>';
  }

  if (alert) {
    if (data.isEol) {
      alert.className = 'drawer-alert drawer-alert-bad';
      alert.innerHTML = `<strong>🛡 .NET ${data.channel} has reached End of Life</strong><p class="muted">After May 14, 2024 there are no more security fixes. We recommend upgrading to a supported LTS release.</p>`;
    } else if (data.hasPatch && data.patchInstalled) {
      alert.className = 'drawer-alert drawer-alert-warn';
      alert.innerHTML = `<strong>⏱ Newer release ${data.patchVersion} is already installed</strong><p class="muted">You already have ${data.patchVersion} installed side-by-side in the .NET ${data.channel} channel. You can remove ${data.displayVersion || data.version} if no global.json pins it.</p>`;
    } else if (data.hasPatch) {
      alert.className = 'drawer-alert drawer-alert-warn';
      alert.innerHTML = `<strong>⏱ Newer release ${data.patchVersion} available</strong><p class="muted">A newer update in the .NET ${data.channel} channel includes security and reliability fixes.</p>`;
    } else if (data.channel === '9' || data.channel === '8') {
      alert.className = 'drawer-alert drawer-alert-warn';
      alert.innerHTML = `<strong>⏱ .NET ${data.channel} support ends Nov 10, 2026</strong><p class="muted">After Nov 10, 2026 there are no more security fixes. .NET 10 (LTS) is already installed.</p>`;
    } else {
      alert.className = 'drawer-alert drawer-alert-ok';
      alert.innerHTML = `<strong>✓ Running latest release for .NET ${data.channel}</strong><p class="muted">Active support until Nov 14, 2028. Your installation is fully patched and up to date.</p>`;
    }
  }

  const channelType = data.isPreview ? 'Preview Release' : (data.isLts ? 'Long Term Support' : 'Standard Term Support');
  if (channel) channel.textContent = `${data.channel}.0 · ${channelType}`;
  if (runtimeLabel) runtimeLabel.textContent = data.kind === 'sdk' ? 'Included runtime' : 'Frameworks';
  if (runtime) runtime.textContent = data.includedRuntime || (data.kind === 'sdk' ? `${data.channel}.0 + ASP.NET Core` : data.description || '.NET · ASP.NET Core');
  if (released) released.textContent = data.released || 'Official release';
  if (location) {
    location.textContent = data.location;
    const copyBtn = location.parentElement?.querySelector('.copy-btn');
    if (copyBtn) copyBtn.dataset.copy = data.location;
  }

  if (patchSection) {
    if (data.pkgUrl || data.pkgSha) {
      patchSection.hidden = false;
      const kicker = data.hasPatch ? 'Newest patch' : 'Release package & security';
      const pkgVer = data.pkgVersion || data.patchVersion || data.displayVersion || data.version;
      const pkgDate = data.pkgDate || data.released || '';
      const pkgRt = data.pkgRuntime || `${data.channel}.0`;
      const pkgSize = data.pkgSize || (data.kind === 'sdk' ? '211 MB' : '44 MB');
      const cvesHtml = (data.cves || []).map(c => `<a class="cve-pill" href="${c.url || '#'}" target="_blank" rel="noopener">${c.id}</a>`).join('');
      const cveBadge = data.cves?.length
        ? `<span class="badge bad sm">🛡 ${data.cves.length} security ${data.cves.length === 1 ? 'fix' : 'fixes'}</span>`
        : '<span class="badge ok sm">✓ 0 open CVEs in this release</span>';
      const shaShort = (data.pkgSha && data.pkgSha.length > 12) ? data.pkgSha.slice(0, 4) + '…' + data.pkgSha.slice(-4) : (data.pkgSha || '');
      const pkgName = `dotnet-${data.kind === 'sdk' ? 'sdk' : 'runtime'}-${pkgVer}-osx-arm64.pkg`;
      patchSection.innerHTML = `
        <div class="drawer-patch-box">
          <h4 id="drawer-patch-kicker" class="drawer-section-title">${kicker}</h4>
          <div class="drawer-patch-header">
            <strong id="drawer-patch-version">${pkgVer}</strong>
            <span id="drawer-patch-date" class="muted text-xs">${pkgDate} · Runtime ${pkgRt}</span>
          </div>
          <div id="drawer-cves" class="drawer-cve-list">
            ${cveBadge}
            ${cvesHtml}
          </div>
          <div class="drawer-code-box">
            <span class="drawer-pkg-meta"><svg class="i xs" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m12 3 8 4.5v9L12 21l-8-4.5v-9L12 3Z"/><path d="m12 12 8-4.5M12 12v9M12 12 4 7.5"/></svg> <span class="drawer-pkg-name">${pkgName}</span> · ${pkgSize} · SHA-512 <code id="drawer-patch-sha" class="drawer-sha">${shaShort}</code></span>
            <button class="button icon ghost copy-btn" data-copy="${data.pkgSha || ''}" type="button" title="Copy SHA-512"><svg class="i" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg></button>
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
    if (data.pkgUrl) {
      downloadBtn.hidden = false;
      downloadBtn.href = data.pkgUrl;
      const needsPatchDownload = Boolean(data.hasPatch && !data.patchInstalled);
      downloadBtn.classList.toggle('primary', needsPatchDownload);
      if (downloadLabel) {
        downloadLabel.textContent = needsPatchDownload
          ? 'Download & open installer'
          : (data.hasPatch && data.patchInstalled ? 'Download .pkg installer' : 'Re-download .pkg installer');
      }
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
  const url = new URL(window.location.href);
  if (url.searchParams.has('selected')) {
    url.searchParams.delete('selected');
    history.replaceState(null, '', url);
  }
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

// Click listener on rows, inspect buttons, and channel collapse buttons
document.addEventListener('click', (e) => {
  const collapseBtn = e.target.closest('[data-collapse-channel]');
  if (collapseBtn) {
    e.preventDefault();
    const tbody = collapseBtn.closest('tbody.channel-card');
    if (tbody) tbody.classList.toggle('collapsed');
    return;
  }

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
  function runVersionFilter() {
    const query = versionFilterInput.value.trim().toLowerCase();
    const terms = query.split(/\s+/).filter(Boolean);
    let visibleCount = 0;
    const channelVisibility = new Set();

    for (const row of versionRows) {
      const version = getRowVersion(row);
      const text = `${version} ${row.textContent}`.toLowerCase();
      const matches = !terms.length || terms.every(t => text.includes(t));
      row.hidden = !matches;
      if (matches) {
        visibleCount++;
        channelVisibility.add(getRowChannel(row));
      }
    }

    for (const card of channelCards) {
      const channel = card.dataset.channelCard;
      card.hidden = !channelVisibility.has(channel);
    }

    if (versionsCount) versionsCount.textContent = `${visibleCount} of ${versionRows.length} installations shown`;

    const emptyRow = document.querySelector('tr.empty-row');
    if (emptyRow) emptyRow.hidden = visibleCount > 0;

    const url = new URL(window.location.href);
    if (query) url.searchParams.set('q', versionFilterInput.value.trim());
    else url.searchParams.delete('q');
    history.replaceState(null, '', url);
  }

  versionFilterInput.addEventListener('input', runVersionFilter);
  if (versionFilterForm) {
    versionFilterForm.addEventListener('submit', (e) => {
      e.preventDefault();
      runVersionFilter();
    });
  }
}

// Fallback for <dialog closedby="any"> light-dismiss using pointer bounding-box check
if (!('closedBy' in HTMLDialogElement.prototype)) {
  const outside = (e, dialog) => {
    const r = dialog.getBoundingClientRect();
    return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
  };
  for (const dialog of document.querySelectorAll('dialog[closedby="any"]')) {
    let pressedOutside = false;
    dialog.addEventListener('pointerdown', (e) => { pressedOutside = outside(e, dialog); });
    dialog.addEventListener('pointerup', (e) => { if (pressedOutside && outside(e, dialog)) dialog.close(); });
  }
}

// Safe Removal Confirmation Dialog (<dialog id="remove-dialog" closedby="any">)
const removeDialog = document.getElementById('remove-dialog');
if (removeDialog) {
  let pendingRemoveForm = null;
  let activeChannelItems = [];
  const cancelBtn = document.getElementById('remove-dialog-cancel');
  const confirmBtn = document.getElementById('remove-dialog-confirm');
  const multiForm = document.getElementById('remove-dialog-form');
  const multiFormInputs = document.getElementById('remove-dialog-form-inputs');

  function syncChannelRemoveDialogState() {
    if (!activeChannelItems.length) return;
    const itemsEl = document.getElementById('remove-dialog-items');
    const cmdEl = document.getElementById('remove-dialog-cmd');
    const copyEl = document.getElementById('remove-dialog-copy');
    const confirmTextEl = document.getElementById('remove-dialog-confirm-text');
    const subEl = document.getElementById('remove-dialog-subtitle');

    const checkedBoxes = itemsEl ? [...itemsEl.querySelectorAll('input[type="checkbox"][data-remove-idx]:checked')] : [];
    const selectedItems = checkedBoxes.map(cb => activeChannelItems[Number(cb.dataset.removeIdx)]).filter(Boolean);

    const combinedCmd = selectedItems.length
      ? selectedItems.map(i => i.command).join('\n')
      : '# Select at least one item to remove';
    if (cmdEl) cmdEl.textContent = combinedCmd;
    if (copyEl) copyEl.dataset.copy = combinedCmd;
    if (confirmTextEl) {
      confirmTextEl.textContent = selectedItems.length === 1
        ? `Remove 1 item`
        : `Remove ${selectedItems.length} items`;
    }
    if (confirmBtn) confirmBtn.disabled = selectedItems.length === 0;
    if (subEl && activeChannelItems[0]) {
      const ch = activeChannelItems[0].channel;
      const isEolCh = activeChannelItems.some(i => i.isEol);
      subEl.textContent = isEolCh
        ? `${selectedItems.length} ${selectedItems.length === 1 ? 'item' : 'items'}, out of support since May 14, 2024.`
        : `${selectedItems.length} ${selectedItems.length === 1 ? 'item' : 'items'} in .NET ${ch} selected for removal.`;
    }
    if (multiFormInputs) {
      multiFormInputs.innerHTML = selectedItems
        .map(item => `<input type="hidden" name="items" value="${item.version}|${item.kind}">`)
        .join('');
    }
  }

  function openChannelRemoveDialog(channel) {
    const chItems = Object.values(versionsData).filter(v => v.channel === channel && v.canRemove);
    if (!chItems.length) return;

    activeChannelItems = chItems;
    pendingRemoveForm = multiForm || [...document.querySelectorAll('form[action="/installation/remove"]')]
      .find(f => f.querySelector('input[name="version"]')?.value === chItems[0].version) || null;

    const titleEl = document.getElementById('remove-dialog-title');
    const itemsEl = document.getElementById('remove-dialog-items');
    const warnEl = document.getElementById('remove-dialog-warn');

    if (titleEl) titleEl.textContent = `Remove .NET ${channel} from this Mac?`;
    if (itemsEl) {
      itemsEl.innerHTML = chItems.map((item, idx) => {
        const badgeHtml = item.isEol
          ? '<span class="badge bad sm">EOL</span>'
          : `<span class="badge sm">${(item.kind || 'sdk').toUpperCase()}</span>`;
        return `
        <label class="remove-item-row">
          <input type="checkbox" checked data-remove-idx="${idx}" aria-label="Select ${item.name} ${item.version} for removal">
          <div class="remove-item-copy">
            <strong>${item.name} <code>${item.displayVersion || item.version}</code>${item.kind === 'runtime' ? ` <span class="muted text-xs">· ${item.description || '.NET · ASP.NET Core'}</span>` : ''}</strong>
            <code>${item.location || 'Managed global installation'}</code>
          </div>
          ${badgeHtml}
        </label>`;
      }).join('');
      itemsEl.querySelectorAll('input[type="checkbox"][data-remove-idx]').forEach(cb => {
        cb.addEventListener('change', syncChannelRemoveDialogState);
      });
    }
    if (warnEl) {
      warnEl.innerHTML = `Removal runs through Microsoft’s dotnet-core-uninstall. Projects targeting <code>net${channel}.0</code> will no longer build on this machine.`;
    }
    syncChannelRemoveDialogState();
    removeDialog.showModal();
  }

  document.addEventListener('submit', (e) => {
    const form = e.target.closest('form[action="/installation/remove"]');
    if (!form || form.dataset.confirmed === 'true' || form.id === 'remove-dialog-form') return;
    e.preventDefault();
    activeChannelItems = [];
    pendingRemoveForm = form;
    if (confirmBtn) confirmBtn.disabled = false;

    const version = form.querySelector('input[name="version"]')?.value || '';
    const kind = form.querySelector('input[name="kind"]')?.value || 'sdk';
    const data = versionsData[version] || {
      version,
      displayVersion: version,
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

    if (titleEl) titleEl.textContent = `Remove ${data.name} ${data.displayVersion || data.version} from this Mac?`;
    if (subEl) subEl.textContent = data.isEol ? `1 item · out of support (.NET ${data.channel})` : `1 item · .NET ${data.channel} (${data.kind.toUpperCase()})`;
    if (itemsEl) {
      const badge = data.isEol ? '<span class="badge bad sm">EOL</span>' : `<span class="badge sm">${data.kind.toUpperCase()}</span>`;
      itemsEl.innerHTML = `
        <div class="remove-item-row">
          <input type="checkbox" checked disabled aria-label="Selected for removal">
          <div class="remove-item-copy">
            <strong>${data.name} <code>${data.displayVersion || data.version}</code></strong>
            <code>${data.location || 'Managed global installation'}</code>
          </div>
          ${badge}
        </div>`;
    }
    if (warnEl) {
      warnEl.innerHTML = `Removal runs through Microsoft’s dotnet-core-uninstall. Projects targeting <code>net${data.channel}.0</code> may no longer build or run unless another compatible version is installed.`;
    }
    if (cmdEl) cmdEl.textContent = data.command;
    if (copyEl) copyEl.dataset.copy = data.command;
    if (confirmTextEl) confirmTextEl.textContent = `Remove ${data.displayVersion || data.version}`;

    removeDialog.showModal();
  });

  if (cancelBtn) {
    cancelBtn.addEventListener('click', () => {
      pendingRemoveForm = null;
      activeChannelItems = [];
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

  // Channel-level "Remove channel" button
  document.addEventListener('click', (e) => {
    const chBtn = e.target.closest('[data-remove-channel]');
    if (!chBtn) return;
    e.preventDefault();
    openChannelRemoveDialog(chBtn.dataset.removeChannel);
  });

  // Auto-open channel removal modal if ?removeChannel= is in the URL (e.g. from Overview)
  const initialUrl = new URL(window.location.href);
  const removeChannelParam = initialUrl.searchParams.get('removeChannel');
  if (removeChannelParam) {
    initialUrl.searchParams.delete('removeChannel');
    history.replaceState(null, '', initialUrl);
    openChannelRemoveDialog(removeChannelParam);
  }
}

// NuGet Tool Installation Sheet (<dialog id="add-tool-dialog" closedby="any">)
const addToolDialog = document.getElementById('add-tool-dialog');
const openAddToolBtn = document.getElementById('open-add-tool-btn');
const closeAddToolBtn = document.getElementById('close-add-tool-btn');

if (addToolDialog) {
  const inlineToolSearch = document.getElementById('tool-search-section');
  if (inlineToolSearch && inlineToolSearch.dataset.hasQuery === 'false') {
    inlineToolSearch.hidden = true;
  }

  let sheetScope = 'global';
  let lastFocusedPkg = { packageId: 'dotnet-coverage', version: '18.1.0' };
  let searchTimer = null;
  const searchInput = document.getElementById('sheet-search-input');
  const resultsBox = document.getElementById('sheet-results');
  const cliPreview = document.getElementById('sheet-cli-preview');
  const cliCopy = document.getElementById('sheet-cli-copy');
  const scopeBtns = [...addToolDialog.querySelectorAll('[data-sheet-scope]')];

  function updateCliPreview(pkgId, version) {
    const p = pkgId || lastFocusedPkg?.packageId || '<package-id>';
    const v = version || lastFocusedPkg?.version || '';
    const flag = sheetScope === 'local' ? '--local' : '--global';
    const cmd = v ? `dotnet tool install ${flag} ${p} --version ${v}` : `dotnet tool install ${flag} ${p}`;
    if (cliPreview) cliPreview.textContent = cmd;
    if (cliCopy) cliCopy.dataset.copy = cmd;
  }

  function getToolFallbackDesc(packageId, authors) {
    const id = (packageId || '').toLowerCase();
    const map = {
      'dotnet-ef': 'Entity Framework Core CLI migrations, DbContext scaffolding, and database management tools.',
      'dotnet-coverage': 'Cross-platform CLI tool to collect code coverage data and merge coverage reports.',
      'dotnet-reportgenerator-globaltool': 'Turns XML coverage reports (OpenCover, Cobertura, Coverlet) into readable HTML dashboards.',
      'coverlet.console': 'Cross-platform code coverage framework for .NET as a global or local CLI tool.',
      'dotnet-outdated-tool': 'Displays outdated NuGet packages across .NET solutions and projects with one command.',
      'csharpier': 'Opinionated code formatter for C# and XML projects.',
      'dotnet-format': 'Code formatter for .NET that applies style preferences from .editorconfig.',
      'dotnet-trace': 'Cross-platform performance profiler collecting EventPipe traces from running .NET processes.',
      'dotnet-counters': 'Performance monitoring tool for ad-hoc health monitoring and performance investigation.',
      'dotnet-dump': 'Collect and analyze Windows and Linux process dumps without a native debugger.'
    };
    return map[id] || `.NET CLI tool package published by ${authors || 'NuGet'}.`;
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
          <span class="tool-monogram tool-logo has-img"><img src="/api/tools/icon?packageId=${encodeURIComponent(item.packageId)}&version=${encodeURIComponent(item.version)}" alt="" class="tool-logo-img" width="36" height="36" loading="lazy"></span>
          <div class="sheet-result-body">
            <div class="sheet-result-top">
              <strong>${item.packageId}</strong>
              <code class="muted">${item.version}</code>
              ${item.verified ? '<span class="badge ok sm">✓ Verified</span>' : ''}
            </div>
            <span class="muted text-xs">${getToolFallbackDesc(item.packageId, item.authors)}</span>
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

  if (new URLSearchParams(window.location.search).get('add') === '1' || window.location.hash === '#tool-search-title') {
    addToolDialog.showModal();
    searchInput?.focus();
  }
}

// Install .NET SDK or Runtime Sheet (<dialog id="install-dotnet-dialog" closedby="any">)
const installDotnetDialog = document.getElementById('install-dotnet-dialog');
const openInstallDotnetBtn = document.getElementById('open-install-dotnet-btn');
const closeInstallDotnetBtn = document.getElementById('close-install-dotnet-btn');

if (installDotnetDialog) {
  let activeProduct = 'SDK';
  let onlyLatest = true;
  let onlySupported = true;
  let onlyLts = false;
  let includePreviews = false;
  const searchInput = document.getElementById('install-dotnet-search-input');
  const resultsBox = document.getElementById('install-dotnet-results');
  const emptyState = document.getElementById('install-dotnet-empty');
  const cliPreview = document.getElementById('install-dotnet-cli-preview');
  const cliCopy = document.getElementById('install-dotnet-cli-copy');
  const productBtns = [...installDotnetDialog.querySelectorAll('[data-install-product]')];
  const lifecycleBtns = [...installDotnetDialog.querySelectorAll('[data-install-lifecycle]')];
  const channelGroups = [...installDotnetDialog.querySelectorAll('[data-install-channel-group]')];
  const rows = [...installDotnetDialog.querySelectorAll('[data-install-row]')];

  function updateInstallCliPreview(row) {
    if (!row) return;
    const cmd = row.dataset.cmd || '';
    if (cliPreview && cmd) cliPreview.textContent = cmd;
    if (cliCopy && cmd) cliCopy.dataset.copy = cmd;
  }

  function syncInstallFilterButtons() {
    const isAllUnfiltered = !onlyLatest && !onlySupported && !onlyLts;
    for (const btn of lifecycleBtns) {
      const val = btn.dataset.installLifecycle;
      let isActive = false;
      if (val === 'latest') isActive = onlyLatest;
      else if (val === 'supported') isActive = onlySupported;
      else if (val === 'lts') isActive = onlyLts;
      else if (val === 'previews') isActive = includePreviews;
      else if (val === 'all') isActive = isAllUnfiltered;
      btn.classList.toggle('active', isActive);
      btn.setAttribute('aria-pressed', isActive ? 'true' : 'false');
    }
  }

  function filterInstallRows() {
    const query = (searchInput?.value || '').trim().toLowerCase();
    const terms = query.split(/\s+/).filter(Boolean);
    let visibleCount = 0;
    let firstVisible = null;

    for (const row of rows) {
      const matchesProduct = row.dataset.product === activeProduct;
      const isSupported = row.dataset.supported === 'true';
      const isLts = row.dataset.lts === 'true';
      const isPre = row.dataset.preview === 'true';
      const isLatest = row.dataset.latest === 'true';

      const matchesLatest = !onlyLatest || isLatest;
      const matchesSupported = !onlySupported || isSupported || (includePreviews && isPre);
      const matchesLts = !onlyLts || isLts;
      const matchesPreviews = includePreviews || !isPre;

      const searchStr = (row.dataset.search || '').toLowerCase();
      const matchesQuery = !terms.length || terms.every(t => searchStr.includes(t));

      const show = matchesProduct && matchesLatest && matchesSupported && matchesLts && matchesPreviews && matchesQuery;
      row.hidden = !show;
      if (show) {
        visibleCount++;
        if (!firstVisible) firstVisible = row;
      } else {
        row.classList.remove('active');
      }
    }

    for (const group of channelGroups) {
      const groupRows = [...group.querySelectorAll('[data-install-row]')];
      const visibleInGroup = groupRows.filter(r => !r.hidden).length;
      group.hidden = visibleInGroup === 0;
      const countEl = group.querySelector('[data-install-channel-count]');
      if (countEl) {
        countEl.textContent = visibleInGroup === 1 ? '1 release' : `${visibleInGroup} releases`;
      }
    }

    if (emptyState) emptyState.hidden = visibleCount > 0;
    const currentActive = rows.find(r => !r.hidden && r.classList.contains('active'));
    if (!currentActive && firstVisible) {
      firstVisible.classList.add('active');
      updateInstallCliPreview(firstVisible);
    }
  }

  for (const btn of productBtns) {
    btn.addEventListener('click', () => {
      activeProduct = btn.dataset.installProduct || 'SDK';
      productBtns.forEach(b => b.classList.toggle('active', b === btn));
      filterInstallRows();
    });
  }

  for (const btn of lifecycleBtns) {
    btn.addEventListener('click', () => {
      const val = btn.dataset.installLifecycle;
      if (val === 'latest') {
        onlyLatest = !onlyLatest;
      } else if (val === 'supported') {
        onlySupported = !onlySupported;
      } else if (val === 'lts') {
        onlyLts = !onlyLts;
      } else if (val === 'previews') {
        includePreviews = !includePreviews;
      } else if (val === 'all') {
        const alreadyAll = !onlyLatest && !onlySupported && !onlyLts;
        if (alreadyAll) {
          onlyLatest = true;
          onlySupported = true;
        } else {
          onlyLatest = false;
          onlySupported = false;
          onlyLts = false;
        }
      }
      syncInstallFilterButtons();
      filterInstallRows();
    });
  }

  if (resultsBox) {
    resultsBox.addEventListener('mouseover', (e) => {
      const row = e.target.closest('[data-install-row]');
      if (!row || row.hidden) return;
      rows.forEach(r => r.classList.toggle('active', r === row));
      updateInstallCliPreview(row);
    });
  }

  if (searchInput) {
    searchInput.addEventListener('input', filterInstallRows);
    searchInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        filterInstallRows();
      }
    });
  }

  if (openInstallDotnetBtn) {
    openInstallDotnetBtn.addEventListener('click', () => {
      installDotnetDialog.showModal();
      searchInput?.focus();
    });
  }

  if (closeInstallDotnetBtn) {
    closeInstallDotnetBtn.addEventListener('click', () => installDotnetDialog.close());
  }

  const initUrl = new URL(window.location.href);
  const installParam = initUrl.searchParams.get('install');
  const tabParam = initUrl.searchParams.get('tab');
  if (installParam === '1' || installParam === 'open' || installParam === 'true' || tabParam === 'all-releases' || tabParam === 'all') {
    initUrl.searchParams.delete('install');
    initUrl.searchParams.delete('tab');
    history.replaceState(null, '', initUrl);
    installDotnetDialog.showModal();
    searchInput?.focus();
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

// Command Palette direct page actions & Global Keyboard Shortcuts (G+O/V/T/S/P, T, P)
if (cmdkDialog) {
  cmdkDialog.addEventListener('click', (e) => {
    const inspectOpt = e.target.closest('[data-cmdk-inspect]');
    if (inspectOpt && typeof openVersionDrawer === 'function' && drawer) {
      e.preventDefault();
      cmdkDialog.hidden = true;
      if (cmdkScrim) cmdkScrim.hidden = true;
      openVersionDrawer(inspectOpt.dataset.cmdkInspect);
      return;
    }
    const installOpt = e.target.closest('[data-cmdk-install-dotnet]');
    if (installOpt && installDotnetDialog) {
      e.preventDefault();
      cmdkDialog.hidden = true;
      if (cmdkScrim) cmdkScrim.hidden = true;
      installDotnetDialog.showModal();
      document.getElementById('install-dotnet-search-input')?.focus();
      return;
    }
    const removeOpt = e.target.closest('[data-cmdk-remove]');
    if (removeOpt) {
      const ver = removeOpt.dataset.cmdkRemove;
      const form = [...document.querySelectorAll('form[action="/installation/remove"]')]
        .find(f => f.querySelector('input[name="version"]')?.value === ver);
      if (form) {
        e.preventDefault();
        cmdkDialog.hidden = true;
        if (cmdkScrim) cmdkScrim.hidden = true;
        form.requestSubmit();
      }
    }
  });
}

let gChordActive = false;
let gChordTimer = null;
window.addEventListener('keydown', (e) => {
  if (e.metaKey || e.ctrlKey || e.altKey) return;
  const tag = document.activeElement?.tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || document.activeElement?.isContentEditable) return;
  if (document.querySelector('dialog[open]') || (cmdkDialog && !cmdkDialog.hidden)) return;

  const key = e.key.toLowerCase();
  if (gChordActive) {
    gChordActive = false;
    clearTimeout(gChordTimer);
    if (key === 'o') { e.preventDefault(); window.location.href = '/overview'; }
    else if (key === 'v') { e.preventDefault(); window.location.href = '/'; }
    else if (key === 't') { e.preventDefault(); window.location.href = '/tools'; }
    else if (key === 's') { e.preventDefault(); window.location.href = '/setup'; }
    else if (key === 'p') { e.preventDefault(); window.location.href = '/preflight'; }
    return;
  }

  if (key === 'g') {
    gChordActive = true;
    clearTimeout(gChordTimer);
    gChordTimer = setTimeout(() => { gChordActive = false; }, 900);
  } else if (key === 't') {
    e.preventDefault();
    if (addToolDialog) {
      addToolDialog.showModal();
      document.getElementById('sheet-search-input')?.focus();
    } else {
      window.location.href = '/tools?add=1';
    }
  } else if (key === 'p') {
    e.preventDefault();
    window.location.href = '/preflight';
  }
});

// Sidebar Count Persistence & Tools Row Context Menu (⋯)
try {
  const navVerEl = document.querySelector('[data-nav-versions-count]');
  if (navVerEl) {
    if (!navVerEl.hidden && navVerEl.textContent.trim()) {
      sessionStorage.setItem('cockpit-versions-count', navVerEl.textContent.trim());
    } else {
      const saved = sessionStorage.getItem('cockpit-versions-count');
      if (saved) { navVerEl.textContent = saved; navVerEl.hidden = false; }
    }
  }
  const navToolsEl = document.querySelector('[data-nav-tools-count]');
  if (navToolsEl) {
    if (!navToolsEl.hidden && navToolsEl.textContent.trim()) {
      sessionStorage.setItem('cockpit-tools-count', navToolsEl.textContent.trim());
    } else {
      const saved = sessionStorage.getItem('cockpit-tools-count');
      if (saved) { navToolsEl.textContent = saved; navToolsEl.hidden = false; }
    }
  }
  const navIssuesEl = document.querySelector('[data-nav-issues-badge]');
  const navPfEl = document.querySelector('[data-nav-pf-badge]');
  if (navIssuesEl) {
    if (!navIssuesEl.hidden && navIssuesEl.textContent.trim()) {
      sessionStorage.setItem('cockpit-issues-count', navIssuesEl.textContent.trim());
    } else {
      const saved = sessionStorage.getItem('cockpit-issues-count');
      if (saved && Number(saved) > 0) {
        navIssuesEl.textContent = saved;
        navIssuesEl.hidden = false;
        if (navPfEl) { navPfEl.textContent = saved; navPfEl.hidden = false; }
      }
    }
  }
} catch {}

document.addEventListener('click', (e) => {
  const menuBtn = e.target.closest('[data-tool-menu-btn]');
  const openMenus = [...document.querySelectorAll('.tool-context-menu:not([hidden])')];

  if (menuBtn) {
    e.preventDefault();
    e.stopPropagation();
    const wrap = menuBtn.closest('.tool-more-wrap');
    const menu = wrap?.querySelector('.tool-context-menu');
    const wasOpen = menu && !menu.hidden;
    for (const m of openMenus) {
      m.hidden = true;
      m.closest('.tool-more-wrap')?.querySelector('[data-tool-menu-btn]')?.setAttribute('aria-expanded', 'false');
    }
    if (menu && !wasOpen) {
      menu.hidden = false;
      menuBtn.setAttribute('aria-expanded', 'true');
    }
    return;
  }

  const updateItem = e.target.closest('[data-tool-menu-update]');
  if (updateItem) {
    e.preventDefault();
    const row = updateItem.closest('tr[data-tool-row]');
    const form = row?.querySelector('form.tool-update-form') || row?.querySelector('form[action="/tools/update"]');
    if (form) form.requestSubmit();
    for (const m of openMenus) m.hidden = true;
    return;
  }

  const uninstallItem = e.target.closest('[data-tool-menu-uninstall]');
  if (uninstallItem) {
    e.preventDefault();
    const row = uninstallItem.closest('tr[data-tool-row]');
    const form = row?.querySelector('form.tool-uninstall-form') || row?.querySelector('form[action="/tools/uninstall"]');
    if (form) form.requestSubmit();
    for (const m of openMenus) m.hidden = true;
    return;
  }

  if (openMenus.length && !e.target.closest('.tool-context-menu')) {
    for (const m of openMenus) {
      m.hidden = true;
      m.closest('.tool-more-wrap')?.querySelector('[data-tool-menu-btn]')?.setAttribute('aria-expanded', 'false');
    }
  }
});

// Tool Details Drawer (#tool-drawer on /tools)
const toolDrawer = document.getElementById('tool-drawer');
const toolDrawerCloseBtn = document.getElementById('tool-drawer-close');
const toolsDataEl = document.getElementById('tools-data');

if (toolDrawer && toolsDataEl) {
  let toolsData = {};
  try {
    toolsData = JSON.parse(toolsDataEl.textContent || '{}');
  } catch {}

  const toolRows = [...document.querySelectorAll('tr[data-tool-row]')];

  function findToolData(keyOrId) {
    if (!keyOrId) return null;
    if (toolsData[keyOrId]) return toolsData[keyOrId];
    const lower = keyOrId.toLowerCase();
    for (const [k, v] of Object.entries(toolsData)) {
      if (k.toLowerCase() === lower || (v.packageId && v.packageId.toLowerCase() === lower)) return v;
    }
    return null;
  }

  function openToolDrawer(keyOrId) {
    const data = findToolData(keyOrId);
    if (!data) return;

    const url = new URL(window.location.href);
    url.searchParams.set('selected', data.packageId);
    history.replaceState(null, '', url);

    toolDrawer.hidden = false;

    toolRows.forEach(row => {
      const rowKey = row.dataset.toolKey || row.dataset.toolId || row.querySelector('.tool-name strong')?.textContent?.trim();
      const isSelected = Boolean(rowKey && (rowKey.toLowerCase() === (data.key || '').toLowerCase() || (!keyOrId.includes(':') && (row.dataset.toolId || '').toLowerCase() === data.packageId.toLowerCase())));
      row.classList.toggle('row-selected', isSelected);
      row.setAttribute('aria-selected', isSelected ? 'true' : 'false');
    });

    const eyebrow = document.getElementById('tool-drawer-eyebrow');
    const logo = document.getElementById('tool-drawer-logo');
    const title = document.getElementById('tool-drawer-title');
    const badges = document.getElementById('tool-drawer-badges');
    const desc = document.getElementById('tool-drawer-description');
    const tags = document.getElementById('tool-drawer-tags');
    const commands = document.getElementById('tool-drawer-commands');
    const pkg = document.getElementById('tool-drawer-pkg');
    const ver = document.getElementById('tool-drawer-ver');
    const downloads = document.getElementById('tool-drawer-downloads');
    const authors = document.getElementById('tool-drawer-authors');
    const verified = document.getElementById('tool-drawer-verified');
    const license = document.getElementById('tool-drawer-license');
    const scope = document.getElementById('tool-drawer-scope');
    const project = document.getElementById('tool-drawer-project');
    const pathTitle = document.getElementById('tool-drawer-path-title');
    const pathEl = document.getElementById('tool-drawer-path');
    const pathCopy = document.getElementById('tool-drawer-path-copy');
    const updateCmd = document.getElementById('tool-drawer-update-cmd');
    const updateCopy = document.getElementById('tool-drawer-update-copy');
    const uninstallCmd = document.getElementById('tool-drawer-uninstall-cmd');
    const uninstallCopy = document.getElementById('tool-drawer-uninstall-copy');
    const nugetBtn = document.getElementById('tool-drawer-nuget-btn');
    const updateForm = document.getElementById('tool-drawer-update-form');
    const removeForm = document.getElementById('tool-drawer-remove-form');

    if (eyebrow) eyebrow.textContent = data.eyebrow;
    if (logo && data.logoHtml) logo.innerHTML = data.logoHtml;
    if (title) title.textContent = data.packageId;
    if (badges) {
      badges.innerHTML = `
        <span class="v-current">${data.version}</span>
        <span class="badge ${data.scope === 'local' ? '' : 'ok'} sm">${data.scope === 'local' ? 'Local' : 'Global'}</span>
        ${data.isVerified ? '<span class="badge ok sm">✓ Verified</span>' : ''}
        ${data.hasUpdate && data.latestVersion ? `<span class="badge warn sm">↑ ${data.latestVersion} available</span>` : ''}`;
    }
    if (desc) desc.textContent = data.fullDescription || data.summary;
    if (tags) {
      tags.innerHTML = (data.tags || []).map(t => `<span class="tool-tag-pill">${t}</span>`).join('');
    }
    if (commands) {
      const clipSvg = '<svg class="i" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg>';
      commands.innerHTML = (data.commands || []).map(cmd => `
        <div class="drawer-code-box tool-drawer-cmd-item">
          <div class="tool-drawer-cmd-main">
            <code>${cmd}</code>
            <span class="muted text-xs">CLI command</span>
          </div>
          <button class="button icon ghost copy-btn" data-copy="${cmd}" type="button" title="Copy command">${clipSvg}</button>
        </div>`).join('');
    }
    if (pkg) pkg.textContent = data.packageId;
    if (ver) {
      if (data.hasUpdate && data.latestVersion && ver.parentElement) {
        ver.parentElement.innerHTML = `<code id="tool-drawer-ver">${data.version}</code> <span class="muted text-xs">(latest: <code>${data.latestVersion}</code>)</span>`;
      } else if (ver.parentElement) {
        ver.parentElement.innerHTML = `<code id="tool-drawer-ver">${data.version}</code>`;
      }
    }
    if (downloads) downloads.textContent = data.downloadsFull || data.downloads || 'N/A';
    if (authors) authors.textContent = data.authors;
    if (verified) verified.textContent = data.isVerified ? '✓ Verified prefix on NuGet.org' : 'Standard NuGet package';
    if (license) license.textContent = data.license;
    if (scope) scope.textContent = data.scopeLabel;
    if (project && data.projectUrl) {
      project.innerHTML = `<a href="${data.projectUrl}" target="_blank" rel="noopener">${data.projectLabel || 'Repository'} ↗</a>`;
    }
    if (pathTitle) pathTitle.textContent = data.pathTitle;
    if (pathEl) pathEl.textContent = data.storePath;
    if (pathCopy) pathCopy.dataset.copy = data.storePath;
    if (updateCmd) updateCmd.textContent = data.updateCmd;
    if (updateCopy) updateCopy.dataset.copy = data.updateCmd;
    if (uninstallCmd) uninstallCmd.textContent = data.uninstallCmd;
    if (uninstallCopy) uninstallCopy.dataset.copy = data.uninstallCmd;
    if (nugetBtn) nugetBtn.href = data.nugetUrl;
    if (updateForm) {
      if (data.hasUpdate) {
        const refreshSvg = '<svg class="i xs" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16"/><path d="M16 16h5v5"/></svg>';
        updateForm.innerHTML = `
          <form method="post" action="/tools/update">
            <input type="hidden" name="packageId" value="${data.packageId}">
            <input type="hidden" name="scope" value="${data.scope}">
            <button class="button primary" type="submit">${refreshSvg} Update to ${data.latestVersion || 'latest'}</button>
          </form>`;
      } else {
        updateForm.innerHTML = '';
      }
    }
    if (removeForm) {
      const trashSvg = '<svg class="i xs" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>';
      removeForm.innerHTML = `
        <form method="post" action="/tools/uninstall">
          <input type="hidden" name="packageId" value="${data.packageId}">
          <input type="hidden" name="scope" value="${data.scope}">
          <button class="button danger drawer-remove-btn" type="submit">${trashSvg} Uninstall tool</button>
        </form>`;
    }
  }

  function closeToolDrawer() {
    toolDrawer.hidden = true;
    const url = new URL(window.location.href);
    if (url.searchParams.has('selected')) {
      url.searchParams.delete('selected');
      history.replaceState(null, '', url);
    }
    toolRows.forEach(row => {
      row.classList.remove('row-selected');
      row.setAttribute('aria-selected', 'false');
    });
  }

  if (toolDrawerCloseBtn) {
    toolDrawerCloseBtn.addEventListener('click', (e) => {
      e.preventDefault();
      closeToolDrawer();
    });
  }

  document.addEventListener('click', (e) => {
    const inspectBtn = e.target.closest('[data-tool-inspect]');
    if (inspectBtn) {
      const id = inspectBtn.dataset.toolInspect;
      if (id) {
        e.preventDefault();
        for (const m of document.querySelectorAll('.tool-context-menu:not([hidden])')) m.hidden = true;
        openToolDrawer(id);
      }
      return;
    }

    const cmdkTool = e.target.closest('[data-cmdk-tool]');
    if (cmdkTool) {
      const id = cmdkTool.dataset.cmdkTool;
      if (id) {
        e.preventDefault();
        if (cmdkDialog) cmdkDialog.hidden = true;
        if (cmdkScrim) cmdkScrim.hidden = true;
        openToolDrawer(id);
      }
      return;
    }

    const row = e.target.closest('tr[data-tool-row]');
    if (row) {
      if (e.target.closest('form, .tool-more-wrap, a:not(.tool-inspect-btn)')) return;
      const key = row.dataset.toolKey || row.dataset.toolId || row.querySelector('.tool-name strong')?.textContent?.trim();
      if (key) {
        e.preventDefault();
        openToolDrawer(key);
      }
    }
  });

  window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !toolDrawer.hidden) {
      closeToolDrawer();
      return;
    }

    if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && !toolDrawer.hidden) {
      const visibleRows = toolRows.filter(r => !r.hidden);
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
      const nextKey = nextRow.dataset.toolKey || nextRow.dataset.toolId || nextRow.querySelector('.tool-name strong')?.textContent?.trim();
      if (nextKey) {
        openToolDrawer(nextKey);
        nextRow.scrollIntoView({ block: 'nearest' });
      }
    }
  });
}
