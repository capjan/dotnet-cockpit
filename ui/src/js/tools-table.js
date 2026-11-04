import { matchesTerms } from './dom.js';

function closeMenus() {
  for (const menu of document.querySelectorAll('.tool-context-menu:not([hidden])')) {
    menu.hidden = true;
    menu.closest('.tool-more-wrap')?.querySelector('[data-tool-menu-btn]')?.setAttribute('aria-expanded', 'false');
  }
}

export function setupToolsTable() {
  const filter = document.querySelector('[data-tools-table-filter]');
  if (filter) {
    const rows = [...document.querySelectorAll('[data-tool-row]')];
    const count = document.querySelector('[data-tools-count]');
    const empty = document.querySelector('[data-tools-empty]');
    filter.addEventListener('input', () => {
      let visible = 0;
      for (const row of rows) {
        row.hidden = !matchesTerms(row.dataset.search || row.textContent, filter.value);
        if (!row.hidden) visible++;
      }
      if (count) {
        count.textContent = `${visible} of ${rows.length} tools shown`;
        count.hidden = !filter.value.trim();
      }
      if (empty) empty.hidden = visible > 0;
    });
  }

  document.addEventListener('click', event => {
    const trigger = event.target.closest('[data-tool-menu-btn]');
    if (trigger) {
      event.preventDefault();
      const menu = trigger.closest('.tool-more-wrap')?.querySelector('.tool-context-menu');
      const wasOpen = menu && !menu.hidden;
      closeMenus();
      if (menu && !wasOpen) {
        menu.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
        menu.querySelector('button, a')?.focus();
      }
      return;
    }
    for (const [attribute, selector] of [['[data-tool-menu-update]', 'form.tool-update-form'], ['[data-tool-menu-uninstall]', 'form.tool-uninstall-form']]) {
      const item = event.target.closest(attribute);
      if (item) {
        event.preventDefault();
        item.closest('tr')?.querySelector(selector)?.requestSubmit();
        closeMenus();
        return;
      }
    }
    if (!event.target.closest('.tool-context-menu')) closeMenus();
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    const open = document.querySelector('.tool-context-menu:not([hidden])');
    if (!open) return;
    event.stopPropagation();
    const trigger = open.closest('.tool-more-wrap')?.querySelector('[data-tool-menu-btn]');
    closeMenus();
    trigger?.focus();
  }, true);
}
