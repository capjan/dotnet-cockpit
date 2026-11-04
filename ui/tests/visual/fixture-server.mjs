// Serves the pages rendered by PageFixtureTests together with the built assets.
import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const pages = process.env.DOTNET_COCKPIT_FIXTURES;
const assets = fileURLToPath(new URL('../../../src/DotnetCockpit/wwwroot', import.meta.url));
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.png': 'image/png' };

createServer((request, response) => {
  const path = normalize(new URL(request.url, 'http://127.0.0.1').pathname);
  if (path === '/api/health') {
    response.end('{"status":"ok"}');
    return;
  }
  const file = path.startsWith('/assets/') ? join(assets, path) : join(pages, `${path}.html`);
  if (!existsSync(file) || !statSync(file).isFile()) {
    response.statusCode = 404;
    response.end();
    return;
  }
  response.setHeader('content-type', types[extname(file)] ?? 'application/octet-stream');
  createReadStream(file).pipe(response);
}).listen(5235, '127.0.0.1');
