const CHORDS = { o: '/overview', v: '/', t: '/tools', s: '/setup', p: '/preflight' };

export function setupShortcuts() {
  const palette = document.getElementById('cmdk-dialog');
  let waitingForChord = false;
  let timer = null;

  window.addEventListener('keydown', event => {
    if (event.metaKey || event.ctrlKey || event.altKey) return;
    const active = document.activeElement;
    if (active?.matches('input, textarea, select') || active?.isContentEditable) return;
    if (document.querySelector('dialog[open]') || (palette && !palette.hidden)) return;

    const key = event.key.toLowerCase();
    if (waitingForChord) {
      waitingForChord = false;
      clearTimeout(timer);
      if (CHORDS[key]) {
        event.preventDefault();
        window.location.href = CHORDS[key];
      }
      return;
    }
    if (key === 'g') {
      waitingForChord = true;
      timer = setTimeout(() => { waitingForChord = false; }, 900);
    } else if (key === 't') {
      event.preventDefault();
      const opener = document.getElementById('open-add-tool-btn');
      if (opener) opener.click();
      else window.location.href = '/tools?add=1';
    } else if (key === 'p') {
      event.preventDefault();
      window.location.href = '/preflight';
    }
  });
}
