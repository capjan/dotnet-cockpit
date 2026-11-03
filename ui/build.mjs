import { build } from 'esbuild';
import { copyFile, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const outdir = fileURLToPath(new URL('../src/DotnetCockpit/wwwroot', import.meta.url));
await rm(outdir, { recursive: true, force: true });
await build({
  absWorkingDir: fileURLToPath(new URL('.', import.meta.url)),
  entryPoints: { 'assets/app': 'src/app.js' },
  outdir,
  bundle: true,
  minify: true,
  format: 'esm',
  target: 'es2022',
  logLevel: 'info',
});
await copyFile('src/assets/logo.png', `${outdir}/assets/logo.png`);
