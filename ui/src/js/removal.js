import { esc } from './dom.js';
import { openModal } from './dialogs.js';
import { markBusy } from './confirm.js';

const MODES = {
  superseded: { filter: item => item.isSuperseded, noun: 'older' },
  previews: { filter: item => item.isPreview, noun: 'preview' },
  eol: { filter: item => item.isEol, noun: 'out-of-support' },
};

export function setupRemoval() {
  const dialog = document.getElementById('remove-dialog');
  const dataElement = document.getElementById('versions-data');
  if (!dialog || !dataElement) return;
  let allItems = [];
  try { allItems = JSON.parse(dataElement.textContent || '[]'); } catch { /* The links keep working through /confirm. */ }

  const title = document.getElementById('remove-dialog-title');
  const subtitle = document.getElementById('remove-dialog-subtitle');
  const list = document.getElementById('remove-dialog-items');
  const modes = document.getElementById('remove-dialog-modes');
  const modeButtons = [...modes.querySelectorAll('[data-remove-mode]')];
  const command = document.getElementById('remove-dialog-cmd');
  const copy = document.getElementById('remove-dialog-copy');
  const confirm = document.getElementById('remove-dialog-confirm');
  const confirmText = document.getElementById('remove-dialog-confirm-text');
  const cancel = document.getElementById('remove-dialog-cancel');
  const form = document.getElementById('remove-dialog-form');
  const inputs = document.getElementById('remove-dialog-form-inputs');
  let items = [];

  function badge(item) {
    if (item.isEol) return '<span class="badge bad sm">End of life</span>';
    if (item.isPreview) return '<span class="badge sm">Preview</span>';
    return '';
  }

  function renderList() {
    list.innerHTML = items.map((item, index) => `
      <label class="remove-item-row">
        <input type="checkbox" checked data-remove-idx="${index}">
        <span class="remove-item-copy">
          <strong>${esc(item.name)} <code>${esc(item.displayVersion)}</code></strong>
          ${item.location ? `<code>${esc(item.location)}</code>` : ''}
        </span>
        ${badge(item)}
      </label>`).join('');
  }

  function selected() {
    return [...list.querySelectorAll('input:checked')].map(box => items[Number(box.dataset.removeIdx)]);
  }

  function sync() {
    const chosen = selected();
    const text = chosen.length ? chosen.map(item => item.command).join('\n\n') : '# Select at least one item';
    command.textContent = text;
    copy.dataset.copy = chosen.length ? text : '';
    confirmText.textContent = `Remove ${chosen.length} ${chosen.length === 1 ? 'item' : 'items'}`;
    confirm.disabled = chosen.length === 0;
    subtitle.textContent = `${chosen.length} of ${items.length} selected.`;
    inputs.innerHTML = chosen.map(item => `<input type="hidden" name="items" value="${esc(item.version)}|${esc(item.kind)}">`).join('');
  }

  list.addEventListener('change', sync);

  function selectMode(mode) {
    for (const button of modeButtons) {
      const active = button.dataset.removeMode === mode;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', active ? 'true' : 'false');
    }
    items = allItems.filter(MODES[mode].filter);
    renderList();
    sync();
  }

  function openChannel(channel) {
    items = allItems.filter(item => item.channel === channel);
    if (!items.length) return false;
    title.textContent = `Remove .NET ${channel}?`;
    modes.hidden = true;
    renderList();
    sync();
    openModal(dialog, cancel);
    return true;
  }

  function openCleanup(mode) {
    let hasAny = false;
    for (const button of modeButtons) {
      const available = allItems.some(MODES[button.dataset.removeMode].filter);
      button.hidden = !available;
      hasAny ||= available;
    }
    if (!hasAny) return;
    title.textContent = 'Clean up .NET installations';
    modes.hidden = false;
    const preferred = allItems.some(MODES[mode]?.filter ?? (() => false)) ? mode : modeButtons.find(button => !button.hidden).dataset.removeMode;
    selectMode(preferred);
    openModal(dialog, cancel);
  }

  for (const button of modeButtons) button.addEventListener('click', () => selectMode(button.dataset.removeMode));

  cancel.addEventListener('click', () => dialog.close());
  confirm.addEventListener('click', () => {
    if (!selected().length) return;
    dialog.setAttribute('data-busy', 'true');
    cancel.disabled = true;
    form.submit();
    markBusy(confirm, 'Removing…');
  });
  dialog.addEventListener('cancel', event => {
    if (dialog.hasAttribute('data-busy')) event.preventDefault();
  });

  document.addEventListener('click', event => {
    const channel = event.target.closest('[data-remove-channel]');
    if (channel) {
      event.preventDefault();
      if (!openChannel(channel.dataset.removeChannel)) window.location.href = channel.href;
      return;
    }
    const cleanup = event.target.closest('[data-cleanup-action]');
    if (cleanup) {
      event.preventDefault();
      openCleanup(cleanup.dataset.cleanupAction);
    }
  });

  if (new URLSearchParams(window.location.search).get('cleanup') === '1') {
    const cleanupButton = document.querySelector('[data-cleanup-action]');
    if (cleanupButton) openCleanup(cleanupButton.dataset.cleanupAction);
  }
}
