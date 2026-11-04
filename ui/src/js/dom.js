export function esc(value) {
  const map = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
  return String(value ?? '').replace(/[&<>"']/g, ch => map[ch]);
}

export function matchesTerms(text, query) {
  const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const haystack = text.toLowerCase();
  return terms.every(term => haystack.includes(term));
}

export function isPlainClick(event) {
  return !(event.metaKey || event.ctrlKey || event.shiftKey || event.altKey);
}

export function replaceQuery(mutate) {
  const url = new URL(window.location.href);
  mutate(url);
  history.replaceState(null, '', url);
}
