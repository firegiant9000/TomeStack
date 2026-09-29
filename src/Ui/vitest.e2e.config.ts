import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// UI flow tests against the real DevHost (see e2e/devhost.setup.ts). Kept out of `npm test` because they need
// the .NET build; run with `npm run test:e2e` after `dotnet build TomeStack.slnx -c Release`.
export default defineConfig({
  plugins: [react()],
  test: {
    include: ['e2e/**/*.e2e.tsx'],
    environment: 'jsdom',
    globalSetup: ['e2e/devhost.setup.ts'],
    setupFiles: ['e2e/timeouts.setup.ts'],
    // The Barbarian flow alone takes ~23 s locally (2026-09-28); 30 s left too little room for a slower CI runner.
    testTimeout: 60_000,
    hookTimeout: 60_000,
  },
});
