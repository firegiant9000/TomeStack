import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// UI flow tests against the real DevHost (see e2e/devhost.setup.ts). Kept out of `npm test` because they need
// the .NET build; run with `npm run test:e2e` after `dotnet build TomeStack.slnx -c Release`.
export default defineConfig({
  plugins: [react()],
  // The Help screen imports docs/guides/*.md?raw, as in vite.config.ts.
  server: { fs: { allow: ['.', '../../docs/guides'] } },
  test: {
    include: ['e2e/**/*.e2e.tsx'],
    // One shared DevHost serves every file: sources sort by GUID and a test may rely on what an earlier test in its file
    // created. Files run one after another, as the single flow file did, so no new cross-file load or ordering appears.
    fileParallelism: false,
    environment: 'jsdom',
    globalSetup: ['e2e/devhost.setup.ts'],
    setupFiles: ['e2e/timeouts.setup.ts'],
    // 30 s left too little room for a slower CI runner (2026-09-28). A flow that needs more sets its own timeout.
    testTimeout: 60_000,
    hookTimeout: 60_000,
  },
});
