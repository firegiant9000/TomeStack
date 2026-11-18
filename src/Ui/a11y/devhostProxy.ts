// Vite middleware for the accessibility run (4.1). The browser page is served by Vite, so its `/api/command` calls land
// here; Node forwards them to the DevHost with the token and no Origin header (the DevHost only allows port 5173 and
// answers no CORS preflight). The token stays in Node and never reaches the page.
import type { IncomingMessage, ServerResponse } from 'node:http';
import type { Plugin } from 'vite';

const maxBody = 1024 * 1024;

class TooLarge extends Error {}

function readBody(req: IncomingMessage): Promise<Buffer> {
  return new Promise((done, fail) => {
    const chunks: Buffer[] = [];
    let size = 0;
    let settled = false;
    const settle = (action: () => void): void => {
      if (settled) return;
      settled = true;
      action();
    };
    req.on('data', (chunk: Buffer) => {
      if (settled) return; // over the cap: the rest is read and dropped, never buffered
      size += chunk.length;
      if (size > maxBody) {
        chunks.length = 0;
        settle(() => fail(new TooLarge()));
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', () => settle(() => done(Buffer.concat(chunks))));
    req.on('error', (error) => settle(() => fail(error)));
    req.on('close', () => settle(() => fail(new Error('Request closed before its body ended.'))));
  });
}

function reply(res: ServerResponse, status: number, body: string): void {
  res.statusCode = status;
  res.setHeader('Content-Type', 'application/json');
  res.end(body);
}

/** `projectName` picks the DevHost that project's globalSetup started (env keys written by e2e/devhost.setup.ts). */
export function devhostProxy(projectName: string): Plugin {
  return {
    name: 'tomestack-devhost-proxy',
    configureServer(server) {
      server.middlewares.use('/api/command', (req, res, next) => {
        if (req.method !== 'POST' || (req.url !== '/' && req.url !== '')) return next();
        void (async () => {
          const base = process.env[`TOMESTACK_E2E_DEVHOST_URL_${projectName}`];
          const token = process.env[`TOMESTACK_E2E_DEVHOST_TOKEN_${projectName}`];
          if (!base || !token) {
            reply(res, 503, JSON.stringify({ error: `DevHost for project ${projectName} is not running: its TOMESTACK_E2E_DEVHOST_* variables are unset (run through vitest.a11y.config.ts).` }));
            return;
          }
          if (!/^application\/json\s*(;|$)/i.test(req.headers['content-type'] ?? '')) {
            reply(res, 415, JSON.stringify({ error: 'Content-Type must be application/json.' }));
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
            if (error instanceof TooLarge) {
              // Reply first, then close once the reply is flushed, so the client sees the 413 and not a reset.
              res.setHeader('Connection', 'close');
              res.once('finish', () => req.destroy());
              reply(res, 413, JSON.stringify({ error: 'Request body over 1 MB.' }));
            }
            else reply(res, 502, JSON.stringify({ error: `DevHost proxy failed: ${error instanceof Error ? error.message : String(error)}` }));
          }
        })();
      });
    },
  };
}
