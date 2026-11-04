import { esc } from './dom.js';

async function loadIndex() {
  const items = [];
  const read = async url => {
    try {
      const response = await fetch(url);
      return response.ok ? await response.json() : null;
    } catch {
      return null;
    }
  };
  const [installation, tools] = await Promise.all([read('/api/installation'), read('/api/tools')]);
  for (const sdk of installation?.sdks ?? []) {
    items.push({ title: `.NET SDK ${sdk.version}`, detail: 'Installed version', href: `/?selected=${encodeURIComponent(sdk.version)}#inventory`, tag: 'SDK' });
  }
  const runtimeVersions = new Map();
  for (const runtime of installation?.runtimes ?? []) {
    if (!runtimeVersions.has(runtime.version)) runtimeVersions.set(runtime.version, []);
    runtimeVersions.get(runtime.version).push(runtime.name);
  }
  for (const [version, names] of runtimeVersions) {
    items.push({ title: `.NET Runtime ${version}`, detail: names.join(', '), href: `/?selected=${encodeURIComponent(version)}#inventory`, tag: 'Runtime' });
  }
  for (const [scope, list] of [['global', tools?.global?.tools], ['local', tools?.local?.tools]]) {
    for (const tool of list ?? []) {
      items.push({
        title: tool.packageId,
        detail: `${tool.version} · ${(tool.commands ?? []).join(', ')}`,
        href: `/tools?scope=${scope}&selected=${encodeURIComponent(tool.packageId)}`,
        tag: scope === 'local' ? 'Local tool' : 'Global tool',
      });
    }
  }
  return items;
}

export function setupPalette() {
  const trigger = document.getElementById('cmdk-trigger');
  const dialog = document.getElementById('cmdk-dialog');
  const scrim = document.getElementById('cmdk-scrim');
  const input = document.getElementById('cmdk-input');
  if (!trigger || !dialog || !scrim || !input) return;
  const group = document.getElementById('cmdk-dynamic-grp');
  const list = document.getElementById('cmdk-dynamic-list');
  const empty = document.getElementById('cmdk-empty');
  const nuget = document.getElementById('cmdk-nuget');
  const groups = [...dialog.querySelectorAll('.grp')];
  let index = null;
  let activeIndex = -1;
  let opener = null;

  const visibleOptions = () => [...dialog.querySelectorAll('.opt:not([hidden])')];

  function setActive(options, next) {
    for (const option of options) option.classList.remove('selected');
    activeIndex = next >= 0 && next < options.length ? next : -1;
    if (activeIndex >= 0) {
      options[activeIndex].classList.add('selected');
      options[activeIndex].scrollIntoView({ block: 'nearest' });
    }
  }

  function filter() {
    const query = input.value.trim().toLowerCase();
    for (const option of dialog.querySelectorAll('.opt')) {
      option.hidden = Boolean(query && !option.textContent.toLowerCase().includes(query));
    }
    // Installed items only appear once there is something to search for, to keep the first view short.
    if (!query) for (const option of list.querySelectorAll('.opt')) option.hidden = true;
    for (const section of groups) section.hidden = section.querySelector('.opt:not([hidden])') === null;
    const options = visibleOptions();
    empty.hidden = options.length > 0 || !query;
    nuget.href = `/tools?add=1&q=${encodeURIComponent(input.value.trim())}`;
    setActive(options, options.length ? 0 : -1);
  }

  async function open() {
    opener = document.activeElement;
    dialog.hidden = false;
    scrim.hidden = false;
    input.value = '';
    filter();
    input.focus();
    if (index === null) {
      index = await loadIndex();
      list.innerHTML = index.map(item => `<a class="opt" href="${esc(item.href)}"><div><b>${esc(item.title)}</b> <small>· ${esc(item.detail)}</small></div><span class="badge sm">${esc(item.tag)}</span></a>`).join('');
      group.hidden = false;
      filter();
    }
  }

  function close() {
    dialog.hidden = true;
    scrim.hidden = true;
    activeIndex = -1;
    opener?.focus?.();
  }

  trigger.addEventListener('click', open);
  scrim.addEventListener('click', close);
  input.addEventListener('input', filter);
  input.addEventListener('keydown', event => {
    const options = visibleOptions();
    if (event.key === 'ArrowDown' && options.length) {
      event.preventDefault();
      setActive(options, (activeIndex + 1) % options.length);
    } else if (event.key === 'ArrowUp' && options.length) {
      event.preventDefault();
      setActive(options, (activeIndex - 1 + options.length) % options.length);
    } else if (event.key === 'Enter') {
      if (activeIndex >= 0) {
        event.preventDefault();
        options[activeIndex].click();
      } else if (!empty.hidden) {
        event.preventDefault();
        nuget.click();
      }
    }
  });

  window.addEventListener('keydown', event => {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      if (dialog.hidden) open();
      else close();
    } else if (event.key === 'Escape' && !dialog.hidden) {
      event.stopPropagation();
      close();
    } else if (event.key === 'Tab' && !dialog.hidden) {
      // The palette is modal, so focus stays inside it.
      const focusable = [...dialog.querySelectorAll('input, a.opt:not([hidden]), #cmdk-nuget')].filter(element => element.offsetParent !== null);
      const current = focusable.indexOf(document.activeElement);
      event.preventDefault();
      focusable[(current + (event.shiftKey ? -1 : 1) + focusable.length) % focusable.length]?.focus();
    }
  }, true);
}
