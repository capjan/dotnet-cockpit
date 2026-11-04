import { openModal } from './dialogs.js';

/** While a change runs nothing can be clicked twice, and the button says what is happening. */
export function markBusy(button, label) {
  if (!button) return;
  button.dataset.idleHtml = button.innerHTML;
  button.classList.add('is-busy');
  button.setAttribute('aria-busy', 'true');
  if (label) button.textContent = label;
  // Disabling synchronously would drop the button's name and value from the submission.
  setTimeout(() => { button.disabled = true; });
}

function setupBusyForms() {
  document.addEventListener('submit', event => {
    const form = event.target;
    if (event.defaultPrevented || form.method !== 'post') return;
    const button = event.submitter ?? form.querySelector('button[type="submit"]');
    markBusy(button, form.dataset.busy || 'Working…');
    form.setAttribute('aria-busy', 'true');
  });
  // Coming back through the history restores the page as it was left.
  window.addEventListener('pageshow', event => {
    if (!event.persisted) return;
    for (const button of document.querySelectorAll('.is-busy')) {
      button.disabled = false;
      button.classList.remove('is-busy');
      button.removeAttribute('aria-busy');
      if (button.dataset.idleHtml) button.innerHTML = button.dataset.idleHtml;
    }
    for (const dialog of document.querySelectorAll('dialog[data-busy]')) dialog.removeAttribute('data-busy');
  });
}

export function setupConfirm() {
  setupBusyForms();
  // A result message belongs to the action, not to the address, so reloading must not repeat it.
  const current = new URL(window.location.href);
  if (current.searchParams.has('notice') || current.searchParams.has('changed')) {
    current.searchParams.delete('notice');
    current.searchParams.delete('changed');
    history.replaceState(null, '', current);
  }
  const dialog = document.getElementById('confirm-dialog');
  if (!dialog) return;
  const form = document.getElementById('confirm-dialog-form');
  const fields = document.getElementById('confirm-dialog-fields');
  const submit = document.getElementById('confirm-dialog-submit');
  const cancel = document.getElementById('confirm-dialog-cancel');
  const icon = document.getElementById('confirm-dialog-icon');
  const commandBox = document.getElementById('confirm-dialog-command-box');
  const command = document.getElementById('confirm-dialog-command');
  const copy = document.getElementById('confirm-dialog-copy');

  document.addEventListener('submit', event => {
    const trigger = event.target.closest('form[data-confirm]');
    if (!trigger) return;
    event.preventDefault();
    const data = trigger.dataset;

    document.getElementById('confirm-dialog-title').textContent = data.confirmTitle;
    document.getElementById('confirm-dialog-body').textContent = data.confirmBody;
    document.getElementById('confirm-dialog-admin').hidden = data.confirmAdmin !== 'true';
    command.textContent = data.confirmCommand;
    copy.dataset.copy = data.confirmCommand;
    commandBox.hidden = !data.confirmCommand;
    icon.hidden = data.confirmDanger !== 'true';
    submit.textContent = data.confirmLabel;
    submit.className = `button ${data.confirmDanger === 'true' ? 'danger' : 'primary'}`;
    form.action = data.confirmPost;
    form.dataset.busy = data.confirmBusy;

    fields.replaceChildren();
    for (const [name, value] of new FormData(trigger)) {
      if (name === 'action') continue;
      const input = document.createElement('input');
      input.type = 'hidden';
      input.name = name;
      input.value = value;
      fields.append(input);
    }
    openModal(dialog);
  });

  cancel.addEventListener('click', () => dialog.close());
  form.addEventListener('submit', () => {
    dialog.setAttribute('data-busy', 'true');
    cancel.disabled = true;
  });
  dialog.addEventListener('cancel', event => {
    if (dialog.hasAttribute('data-busy')) event.preventDefault();
  });
}
