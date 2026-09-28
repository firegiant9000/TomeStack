// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell): create -> sheet -> override
// -> export -> import. Only the transport differs from the desktop app: HTTP to loopback instead of the WebView2
// bridge, so export takes the download fallback instead of the native Save dialog.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { downloadBase64 } from '../src/files';

vi.mock('../src/api/client', async (importOriginal) => {
  const { inject } = await import('vitest');
  const { createHttpTransport } = await import('../src/api/transport');
  const original = await importOriginal<typeof import('../src/api/client')>();
  const { endpoint, token } = inject('devHost');
  return { ...original, client: original.createClient(createHttpTransport(endpoint, { 'X-TomeStack-Token': token })) };
});

vi.mock('../src/files', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../src/files')>()),
  downloadBase64: vi.fn(),
}));

afterEach(cleanup);

function bytesOf(base64: string): Uint8Array<ArrayBuffer> {
  return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
}

it('creates a character, shows its traced sheet, overrides, exports and re-imports it', async () => {
  const user = userEvent.setup();
  render(<App />);

  // Create ("New character" is disabled until app.info has loaded, so a click is never silently ignored)
  const newCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));
  await user.click(newCharacter);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Pell');
  await user.click(screen.getByRole('radio', { name: /SRD 5\.1/ }));
  await user.click(await screen.findByRole('checkbox', { name: /Fixture Quickfoot/ }));
  await user.click(screen.getByRole('button', { name: 'Create and save' }));

  // Sheet with a source-aware trace: Dex 14 + 2 (Fixture Quickfoot, species under 2014 rules) = 16 -> +3
  const sheet = await screen.findByRole('article', { name: 'E2E Pell' });
  await waitFor(() => expect(document.activeElement).toBe(within(sheet).getByRole('heading', { level: 2, name: 'E2E Pell' })));
  const initiative = within(sheet).getByRole('region', { name: /^Initiative:/ });
  expect(within(initiative).getByRole('heading').textContent).toContain('+3');
  expect(within(sheet).getByRole('heading', { name: /^Dexterity score: 16/ })).toBeTruthy();
  await user.click(within(initiative).getByRole('heading')); // expand the field (summary)
  const trace = within(initiative).getByRole('table', { name: 'How initiative is calculated' });
  expect(within(trace).getByText(/TomeStack Fixtures: 2014 Family, p\. 1/)).toBeTruthy();

  // Override (labeled, keeps the computed value)
  await user.type(within(initiative).getByRole('spinbutton', { name: 'Override value' }), '9');
  await user.type(within(initiative).getByRole('textbox', { name: 'Reason (optional)' }), 'Table ruling');
  await user.click(within(initiative).getByRole('button', { name: 'Apply override' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: /^Initiative:/ }).textContent).toContain('overridden (calculated +3)'));

  // Export (DevHost has no native dialog: package.saveAs -> unsupported -> download fallback)
  await user.click(screen.getByRole('button', { name: 'Export package' }));
  await waitFor(() => expect(downloadBase64).toHaveBeenCalledTimes(1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[0]!;
  expect(fileName).toBe('E2E-Pell-personal-backup.tomestack.zip'); // default purpose: personal backup (ADR-007)

  // Share: the preview says what is left out (nothing here: the fixture sources may be shared), then exports.
  await user.click(screen.getByRole('radio', { name: /Share with someone/ }));
  const leftOut = await screen.findByRole('region', { name: 'Left out of the shared package' });
  expect(leftOut.textContent).toMatch(/Nothing is left out/);
  await user.click(screen.getByRole('button', { name: 'Export package' }));
  await waitFor(() => expect(downloadBase64).toHaveBeenCalledTimes(2));
  expect(vi.mocked(downloadBase64).mock.calls[1]![0]).toBe('E2E-Pell.tomestack.zip');

  // Import: preview first, then apply. The character already exists, so the local copy is backed up.
  const file = new File([bytesOf(base64)], fileName, { type: 'application/zip' });
  await user.upload(screen.getByLabelText('Package file'), file);
  const preview = await screen.findByRole('region', { name: `Import ${fileName}` });
  expect(within(preview).getByText(/already exists and will be replaced/)).toBeTruthy();
  await user.click(within(preview).getByRole('button', { name: 'Apply import' }));

  const status = await screen.findByRole('status');
  expect(status.getAttribute('role')).toBe('status');
  expect(status.textContent).toMatch(/1 replaced/);
  expect(status.textContent).toMatch(/backed up to backups\/pre-import-/);
  await waitFor(() => expect(screen.getByRole('heading', { name: /^Initiative:/ }).textContent).toContain('overridden (calculated +3)'));
});

it('reaches the primary actions by keyboard alone', async () => {
  const user = userEvent.setup();
  render(<App />);
  const newCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));

  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'New character' }));
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Import package…' }));

  await user.keyboard('{Shift>}{Tab}{/Shift}{Enter}');
  expect(await screen.findByRole('heading', { name: 'New character' })).toBeTruthy();
  expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Name' }));
});
