// Vite middleware for the accessibility run (4.1). The browser page is served by Vite, so its `/api/command` calls land
// here; Node forwards them to the DevHost with the token and no Origin header (the DevHost only allows port 5173 and
// answers no CORS preflight). The token stays in Node and never reaches the page.
import type { IncomingMessage, ServerResponse } from 'node:http';
import type { Plugin } from 'vite';

function readBody(req: IncomingMessage): Promise<Buffer> {
  return new Promise((done, fail) => {
    const chunks: Buffer[] = [];
    req.on('data', (chunk: Buffer) => chunks.push(chunk));
    req.on('end', () => done(Buffer.concat(chunks)));
    req.on('error', fail);
  });
}

function reply(res: ServerResponse, status: number, body: string): void {
  res.statusCode = status;
  res.setHeader('Content-Type', 'application/json');
  res.end(body);
}

export function devhostProxy(): Plugin {
  return {
    name: 'tomestack-devhost-proxy',
    configureServer(server) {
      server.middlewares.use('/api/command', (req, res, next) => {
        if (req.method !== 'POST') return next();
        void (async () => {
          const base = process.env.TOMESTACK_E2E_DEVHOST_URL;
          const token = process.env.TOMESTACK_E2E_DEVHOST_TOKEN;
          if (!base || !token) {
            reply(res, 503, JSON.stringify({ error: 'DevHost is not running: TOMESTACK_E2E_DEVHOST_URL and TOMESTACK_E2E_DEVHOST_TOKEN are unset (run through vitest.a11y.config.ts).' }));
            return;
          }
          try {
            const body = await readBody(req);
            const upstream = await fetch(`${base}/api/command`, {
              method: 'POST',
              headers: { 'X-TomeStack-Token': token, 'Content-Type': 'application/json' },
              body: new Uint8Array(body),
            });
            reply(res, upstream.status, await upstream.text());
          } catch (error) {
            reply(res, 502, JSON.stringify({ error: `DevHost proxy failed: ${error instanceof Error ? error.message : String(error)}` }));
          }
        })();
      });
    },
  };
}
