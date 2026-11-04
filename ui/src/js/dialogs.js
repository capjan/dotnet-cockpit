export function revealScriptOnlyControls() {
  document.documentElement.classList.add('js');
  for (const element of document.querySelectorAll('[data-js-only]')) element.hidden = false;
}

// Browsers without <dialog closedby="any"> get the same light dismiss through a pointer check.
export function setupDialogs() {
  // Native disclosure menus close on outside click and Escape like every other menu.
  document.addEventListener('click', event => {
    for (const menu of document.querySelectorAll('details.menu[open]')) {
      if (!menu.contains(event.target)) menu.open = false;
    }
  });
  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    const menu = document.querySelector('details.menu[open]');
    if (!menu) return;
    event.stopPropagation();
    menu.open = false;
    menu.querySelector('summary')?.focus();
  }, true);

  if ('closedBy' in HTMLDialogElement.prototype) return;
  const outside = (event, dialog) => {
    const box = dialog.getBoundingClientRect();
    return event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom;
  };
  for (const dialog of document.querySelectorAll('dialog[closedby="any"]')) {
    let pressedOutside = false;
    dialog.addEventListener('pointerdown', event => { pressedOutside = outside(event, dialog); });
    dialog.addEventListener('pointerup', event => {
      if (pressedOutside && outside(event, dialog) && !dialog.hasAttribute('data-busy')) dialog.close();
    });
  }
}

/** Opens a sheet that the server may already have rendered open for browsers without JavaScript. */
export function openModal(dialog, focusTarget) {
  if (dialog.open) dialog.close();
  dialog.showModal();
  focusTarget?.focus();
}
