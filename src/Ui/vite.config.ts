import { readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

const devHostPort = process.env.TOMESTACK_DEV_PORT ?? '5178';
const devDataDir =
  process.env.TOMESTACK_DATA_DIR ?? join(process.env.LOCALAPPDATA ?? join(homedir(), 'AppData', 'Local'), 'TomeStack-dev');

/** The dev host writes a fresh token per launch; read it per request so restart order does not matter. */
function readDevToken(): string {
  try {
    return readFileSync(join(devDataDir, 'devhost.token'), 'utf8').trim();
  } catch {
    return '';
  }
}

/**
 * Production builds run inside WebView2 from a virtual host with no network access needed.
 * The CSP forbids any remote origin; it is omitted in dev because Vite's HMR injects inline scripts.
 */
function contentSecurityPolicy(): Plugin {
  return {
    name: 'tomestack-csp',
    apply: 'build',
    transformIndexHtml: () => [
      {
        tag: 'meta',
        attrs: {
          'http-equiv': 'Content-Security-Policy',
          content:
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'",
        },
        injectTo: 'head-prepend',
      },
    ],
  };
}

export default defineConfig({
  plugins: [react(), contentSecurityPolicy()],
  base: './',
  build: { outDir: 'dist', emptyOutDir: true, sourcemap: false },
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: `http://127.0.0.1:${devHostPort}`,
        configure: (proxy) => {
          proxy.on('proxyReq', (proxyReq) => {
            proxyReq.setHeader('X-TomeStack-Token', readDevToken());
          });
        },
      },
    },
  },
});
