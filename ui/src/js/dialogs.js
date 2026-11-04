export function revealScriptOnlyControls() {
  for (const element of document.querySelectorAll('[data-js-only]')) element.hidden = false;
}

// Browsers without <dialog closedby="any"> get the same light dismiss through a pointer check.
export function setupDialogs() {
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
