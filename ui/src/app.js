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
    const lifecycle = new Set(lifecycleInputs.filter(input => input.checked).map(input => input.value));
    let visibleCount = 0;
    const latestThreeCounts = new Map();
    for (const row of rows) {
      const matchesQuery = row.dataset.search.toLowerCase().includes(query);
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
