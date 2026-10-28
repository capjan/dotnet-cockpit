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
