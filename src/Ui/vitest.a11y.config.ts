import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { playwright } from '@vitest/browser-playwright';
import { devhostProxy } from './a11y/devhostProxy.ts';

// 4.1: axe with colour contrast in the installed Edge, against the real DevHost (see e2e/devhost.setup.ts). Run with
// `npm run test:a11y` after `dotnet build TomeStack.slnx -c Release`. One project per colour scheme: globalSetup runs once
// per project, so each scheme gets its own DevHost and its own proxy plugin reading that DevHost (keyed by project name).
const scheme = (colorScheme: 'light' | 'dark') => ({
  plugins: [react(), devhostProxy(colorScheme)],
  // The Help screen imports docs/guides/*.md?raw, as in vite.config.ts.
  server: { fs: { allow: ['.', '../../docs/guides'] } },
  test: {
    name: colorScheme,
    include: ['a11y/**/*.a11y.tsx'],
    globalSetup: ['e2e/devhost.setup.ts'],
    fileParallelism: false,
    testTimeout: 60_000,
    hookTimeout: 60_000,
    browser: {
      enabled: true,
      headless: true,
      viewport: { width: 1280, height: 800 },
      instances: [
        {
          browser: 'chromium' as const,
          provider: playwright({ launchOptions: { channel: 'msedge' }, contextOptions: { colorScheme } }),
        },
      ],
    },
  },
});

export default defineConfig({ test: { projects: [scheme('light'), scheme('dark')] } });
