import js from '@eslint/js';
import { defineConfig, globalIgnores } from 'eslint/config';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import tseslint from 'typescript-eslint';

const transportOnly = 'Use the api client; only src/api/transport.ts talks to the transport.';

export default defineConfig([
  globalIgnores(['dist', 'node_modules']),
  {
    files: ['**/*.{ts,tsx,js}'],
    extends: [js.configs.recommended, tseslint.configs.recommended, reactHooks.configs.flat.recommended],
    languageOptions: { ecmaVersion: 2022, globals: { ...globals.browser, ...globals.node } },
    rules: {
      'no-restricted-globals': [
        'error',
        ...['fetch', 'XMLHttpRequest', 'WebSocket', 'EventSource'].map((name) => ({ name, message: transportOnly })),
      ],
      'no-restricted-properties': [
        'error',
        ...['window', 'globalThis', 'self'].flatMap((object) =>
          ['fetch', 'XMLHttpRequest', 'WebSocket', 'EventSource', 'chrome'].map((property) => ({ object, property, message: transportOnly })),
        ),
      ],
    },
  },
  {
    // The transport, and the e2e harness that health-checks DevHost from Node, are the only fetch and bridge users.
    files: ['src/api/transport.ts', 'e2e/devhost.setup.ts'],
    rules: { 'no-restricted-globals': 'off', 'no-restricted-properties': 'off' },
  },
]);
