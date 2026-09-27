import js from '@eslint/js';
import { defineConfig, globalIgnores } from 'eslint/config';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import tseslint from 'typescript-eslint';

export default defineConfig([
  globalIgnores(['dist', 'node_modules']),
  {
    files: ['**/*.{ts,tsx,js}'],
    extends: [js.configs.recommended, tseslint.configs.recommended, reactHooks.configs.flat.recommended],
    languageOptions: { ecmaVersion: 2022, globals: { ...globals.browser, ...globals.node } },
    rules: {
      'no-restricted-globals': [
        'error',
        { name: 'fetch', message: 'Use the api client; only src/api/transport.ts talks to the transport.' },
      ],
    },
  },
  {
    files: ['src/api/transport.ts'],
    rules: { 'no-restricted-globals': 'off' },
  },
]);
