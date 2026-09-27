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
  await user.click(await screen.findByRole('radio', { name: /^Fixture Quickfoot/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  expect(await screen.findByText('Nothing to choose at this level.')).toBeTruthy();
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

/** Ticks one option of the choice whose legend starts with `legend`. */
async function pick(user: ReturnType<typeof userEvent.setup>, legend: RegExp, option: RegExp) {
  const group = await screen.findByRole('group', { name: legend });
  await user.click(within(group).getByRole('checkbox', { name: option }));
}

it('builds an SRD 5.2.1 Barbarian as drafts: create, cancel a level-up, level to 3 with a subclass', async () => {
  const user = userEvent.setup();
  render(<App />);
  const newCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));
  await user.click(newCharacter);

  // Basics: the M1 acceptance character Brenna (Str 15, Dex 13, Con 14, Int 8, Wis 12, Cha 10)
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Brenna');
  await user.click(screen.getByRole('radio', { name: /SRD 5\.2\.1/ }));
  const scores = screen.getByRole('group', { name: 'Base ability scores' });
  for (const [label, value] of [['Strength', 15], ['Dexterity', 13], ['Constitution', 14], ['Intelligence', 8], ['Wisdom', 12], ['Charisma', 10]] as const) {
    const input = within(scores).getByRole('spinbutton', { name: label });
    await user.clear(input);
    await user.type(input, String(value));
  }
  // Both families' options are listed (SPEC S-02); the SRD 5.1 ones are disabled here.
  const enabledRadio = (name: RegExp) => screen.getAllByRole<HTMLInputElement>('radio', { name }).find((r) => !r.disabled)!;
  await screen.findByRole('radio', { name: /^Dwarf/ });
  await user.click(enabledRadio(/^Dwarf/));
  await user.click(enabledRadio(/^Soldier/));
  await user.click(enabledRadio(/^Barbarian/));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));

  // Level-1 choices are offered and flagged until answered; the subclass (level 3) is not offered yet.
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'New character' })));
  expect(await screen.findByText(/2 choices still to make/)).toBeTruthy();
  expect(screen.queryByRole('group', { name: /^Barbarian: choose 1/ })).toBeNull();
  await pick(user, /^Soldier: choose 1/, /^Soldier Ability Scores: Strength \+2, Constitution \+1/);
  await pick(user, /^Barbarian: choose 2/, /^Barbarian Skill: Perception/);
  await pick(user, /^Barbarian: choose 2/, /^Barbarian Skill: Survival/);
  expect(await screen.findByText('All choices are made.')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Create and save' }));

  let sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  expect(within(sheet).getByRole('heading', { name: /^Strength score: 17/ })).toBeTruthy();
  expect(within(sheet).queryByRole('heading', { name: 'Choices to make' })).toBeNull();

  // A cancelled level-up draft changes nothing.
  await user.click(within(sheet).getByRole('button', { name: 'Level up' }));
  await user.click(await screen.findByRole('radio', { name: /Barbarian \(level 1 → 2\)/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  expect(await screen.findByText('All choices are made.')).toBeTruthy(); // level 2 offers no new choice
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  expect((await screen.findByRole('status')).textContent).toMatch(/Draft discarded/);
  sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  expect(within(sheet).getByText('Level 1')).toBeTruthy();

  // Level 2, then 3: the subclass and Primal Knowledge choices appear at level 3.
  for (const next of [2, 3]) {
    await user.click(within(await screen.findByRole('article', { name: 'E2E Brenna' })).getByRole('button', { name: 'Level up' }));
    await user.click(await screen.findByRole('radio', { name: new RegExp(`Barbarian \\(level ${next - 1} → ${next}\\)`) }));
    await user.click(screen.getByRole('button', { name: 'Next: choices' }));
    if (next === 3) {
      await pick(user, /^Barbarian: choose 1/, /^Path of the Berserker/);
      // Leave Primal Knowledge open: the save is allowed and the sheet flags it.
      expect(await screen.findByText(/1 choice still to make/)).toBeTruthy();
    }
    await user.click(await screen.findByRole('button', { name: 'Save level-up' }));
  }

  sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  expect(within(sheet).getByText('Level 3')).toBeTruthy();
  const open = within(sheet).getByRole('heading', { name: 'Choices to make' }).parentElement!;
  expect(open.textContent).toMatch(/Primal Knowledge: choose 1 \(0 chosen\)/);

  // Answer it from the sheet: Survival is already chosen elsewhere, so pick Animal Handling.
  await user.click(within(open).getByRole('button', { name: 'Make choices' }));
  await pick(user, /^Primal Knowledge: choose 1/, /^Barbarian Skill: Animal Handling/);
  await user.click(await screen.findByRole('button', { name: 'Save choices' }));

  // The M1 acceptance values for Brenna (m1-acceptance.md): AC 13, HP 35, Animal Handling +3.
  sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  expect(within(sheet).getByRole('heading', { name: /^Armor Class: 13/ })).toBeTruthy();
  expect(within(sheet).getByRole('heading', { name: /^Hit point maximum: 35/ })).toBeTruthy();
  expect(within(sheet).getByRole('heading', { name: /^Animal Handling: \+3/ })).toBeTruthy();
  expect(within(sheet).queryByRole('heading', { name: 'Choices to make' })).toBeNull();
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
