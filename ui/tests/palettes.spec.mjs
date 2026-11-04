import { expect, test } from '@playwright/test';

const palettes = ['cockpit', 'macos', 'github', 'codex'];
const themes = ['light', 'dark'];
const pages = ['/', '/overview', '/setup', 'http://127.0.0.1:5234/tools'];

/** Measures text against its effective background; bars on gradients or with opacity are decorative. */
function lowContrast() {
  const parse = color => {
    const parts = color.match(/[\d.]+/g)?.map(Number);
    if (!parts || !color.startsWith('rgb')) return null;
    return { r: parts[0], g: parts[1], b: parts[2], a: parts[3] ?? 1 };
  };
  const lum = c => {
    const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };
    return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
  };
  const background = element => {
    const layers = [];
    for (let node = element; node; node = node.parentElement) {
      const color = parse(getComputedStyle(node).backgroundColor);
      if (color && color.a > 0) {
        layers.push(color);
        if (color.a >= 1) break;
      }
    }
    let base = layers.pop() ?? { r: 255, g: 255, b: 255 };
    while (layers.length) {
      const top = layers.pop();
      base = { r: top.r * top.a + base.r * (1 - top.a), g: top.g * top.a + base.g * (1 - top.a), b: top.b * top.a + base.b * (1 - top.a) };
    }
    return base;
  };
  const failures = new Set();
  for (const element of document.querySelectorAll('main *, .rail *, .topbar *')) {
    if (![...element.childNodes].some(node => node.nodeType === 3 && node.textContent.trim())) continue;
    if (element.closest('[hidden], dialog:not([open]), .sr-only, .tl-bar, .grad-text') || !element.getBoundingClientRect().width) continue;
    if (element.closest('.palette-swatch') || element.matches(':disabled')) continue;
    const style = getComputedStyle(element);
    const foreground = parse(style.color);
    if (!foreground) continue;
    const back = background(element);
    const fore = { r: foreground.r * foreground.a + back.r * (1 - foreground.a), g: foreground.g * foreground.a + back.g * (1 - foreground.a), b: foreground.b * foreground.a + back.b * (1 - foreground.a) };
    const [high, low] = [lum(fore), lum(back)].sort((a, b) => b - a);
    const ratio = (high + 0.05) / (low + 0.05);
    if (ratio < 4.5) failures.add(`${String(element.className).slice(0, 30) || element.tagName} ${ratio.toFixed(2)} "${element.textContent.trim().slice(0, 16)}"`);
  }
  return [...failures];
}

for (const palette of palettes) {
  for (const theme of themes) {
    test(`${palette} ${theme} meets 4.5:1 on every page`, async ({ page }) => {
      for (const path of pages) {
        await page.goto(path);
        await page.addStyleTag({ content: '*, *::before, *::after { transition: none !important; animation: none !important; }' });
        await page.evaluate(([p, t]) => {
          document.documentElement.dataset.palette = p;
          document.documentElement.dataset.theme = t;
        }, [palette, theme]);
        expect(await page.evaluate(lowContrast), `${path}`).toEqual([]);
      }
    });
  }
}

for (const accent of ['blue', 'red', 'orange', 'yellow', 'green', 'purple', 'pink', 'graphite']) {
  for (const theme of themes) {
    test(`macOS accent ${accent} ${theme} meets 4.5:1`, async ({ page }) => {
      for (const path of ['/', '/setup']) {
        await page.goto(path);
        await page.addStyleTag({ content: '*, *::before, *::after { transition: none !important; animation: none !important; }' });
        await page.evaluate(([a, t]) => {
          document.documentElement.dataset.palette = 'macos';
          document.documentElement.dataset.accent = a;
          document.documentElement.dataset.theme = t;
        }, [accent, theme]);
        expect(await page.evaluate(lowContrast), path).toEqual([]);
      }
    });
  }
}

test('the macOS style picks up a changed accent color when the window regains focus', async ({ page }) => {
  await page.goto('/');
  await page.evaluate(() => { document.documentElement.dataset.accent = 'blue'; });
  await page.route('**/api/accent', route => route.fulfill({ json: { accent: 'pink' } }));
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect(page.locator('html')).toHaveAttribute('data-accent', 'pink');
});

test('the palette switch applies at once and the choice survives a reload', async ({ page }) => {
  await page.goto('/setup');
  await page.locator('[data-palette-pref="github"]').click();
  await expect(page.locator('html')).toHaveAttribute('data-palette', 'github');
  await expect(page.locator('[data-palette-pref="github"]')).toHaveAttribute('aria-pressed', 'true');
  await page.waitForLoadState('networkidle');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-palette', 'github');
  await page.locator('[data-palette-pref="cockpit"]').click();
  await page.waitForLoadState('networkidle');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-palette', 'cockpit');
});

test('the palette switch works without JavaScript', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  try {
    await page.goto('/setup');
    await page.locator('[data-palette-pref="codex"]').click();
    await expect(page.locator('html')).toHaveAttribute('data-palette', 'codex');
    await page.locator('[data-palette-pref="cockpit"]').click();
    await expect(page.locator('html')).toHaveAttribute('data-palette', 'cockpit');
  } finally { await context.close(); }
});
