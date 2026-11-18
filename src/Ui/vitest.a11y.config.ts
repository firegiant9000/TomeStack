import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { playwright } from '@vitest/browser-playwright';
import { devhostProxy } from './a11y/devhostProxy';

// 4.1: axe with colour contrast in the installed Edge, against the real DevHost (see e2e/devhost.setup.ts). Run with
// `npm run test:a11y` after `dotnet build TomeStack.slnx -c Release`. One browser instance per colour scheme.
const scheme = (colorScheme: 'light' | 'dark') =>
  ({
    browser: 'chromium',
    name: colorScheme,
    provider: playwright({ launchOptions: { channel: 'msedge' }, contextOptions: { colorScheme } }),
  }) as const;

export default defineConfig({
  plugins: [react(), devhostProxy()],
  // The Help screen imports docs/guides/*.md?raw, as in vite.config.ts.
  server: { fs: { allow: ['.', '../../docs/guides'] } },
  test: {
    include: ['a11y/**/*.a11y.tsx'],
    globalSetup: ['e2e/devhost.setup.ts'],
    fileParallelism: false,
    testTimeout: 60_000,
    hookTimeout: 60_000,
    browser: {
      enabled: true,
      headless: true,
      viewport: { width: 1280, height: 800 },
      instances: [scheme('light'), scheme('dark')],
    },
  },
});
