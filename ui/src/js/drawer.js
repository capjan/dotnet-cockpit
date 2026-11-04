import { replaceQuery } from './dom.js';

/** Fills a detail drawer from the server-rendered template of a row. */
export function setupDrawer(drawerId, selectedValue) {
  const drawer = document.getElementById(drawerId);
  if (!drawer) return;
  const rows = () => [...document.querySelectorAll('tr[data-drawer-key]')];
  const templateFor = key => [...document.querySelectorAll('template[data-drawer-content]')].find(t => t.dataset.drawerContent === key);
  let currentKey = rows().find(row => row.classList.contains('row-selected'))?.dataset.drawerKey ?? null;
  let returnFocus = null;

  function open(key, opener) {
    const template = templateFor(key);
    if (!template) return;
    returnFocus = opener ?? returnFocus;
    drawer.replaceChildren(template.content.cloneNode(true));
    drawer.hidden = false;
    currentKey = key;
    replaceQuery(url => url.searchParams.set('selected', selectedValue(key)));
    for (const row of rows()) row.classList.toggle('row-selected', row.dataset.drawerKey === key);
    drawer.querySelector('[data-drawer-close]')?.focus({ preventScroll: true });
  }

  function close() {
    drawer.hidden = true;
    drawer.replaceChildren();
    currentKey = null;
    replaceQuery(url => url.searchParams.delete('selected'));
    for (const row of rows()) row.classList.remove('row-selected');
    returnFocus?.focus({ preventScroll: true });
    returnFocus = null;
  }

  document.addEventListener('click', event => {
    if (event.target.closest('[data-drawer-close]')) {
      event.preventDefault();
      close();
      return;
    }
    const inspect = event.target.closest('[data-inspect]');
    if (inspect) {
      event.preventDefault();
      for (const menu of document.querySelectorAll('.tool-context-menu:not([hidden])')) menu.hidden = true;
      open(inspect.dataset.inspect, inspect);
      return;
    }
    const row = event.target.closest('tr[data-drawer-key]');
    if (!row || event.target.closest('a, button, form, input, label, .tool-command-pill, .tool-more-wrap')) return;
    open(row.dataset.drawerKey, row.querySelector('[data-inspect]'));
  });

  window.addEventListener('keydown', event => {
    if (drawer.hidden || document.querySelector('dialog[open]') || !document.getElementById('cmdk-dialog')?.hidden) return;
    if (event.key === 'Escape') {
      close();
      return;
    }
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
    if (event.target.closest?.('input, textarea, select')) return;
    const visible = rows().filter(row => !row.hidden && !row.closest('[hidden]'));
    if (!visible.length) return;
    event.preventDefault();
    const index = visible.findIndex(row => row.dataset.drawerKey === currentKey);
    const next = visible[(index + (event.key === 'ArrowDown' ? 1 : visible.length - 1)) % visible.length] ?? visible[0];
    open(next.dataset.drawerKey, next.querySelector('[data-inspect]'));
    next.scrollIntoView({ block: 'nearest' });
  });
}
