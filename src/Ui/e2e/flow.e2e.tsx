// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell): create -> sheet -> override
// -> export -> import. Only the transport differs from the desktop app: HTTP to loopback instead of the WebView2
// bridge, so export takes the download fallback instead of the native Save dialog.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { client } from '../src/api/client';
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

  // M2 item 2, play: resources with calculated maximums, explicit spending, hit points, conditions and rolls.
  const resources = within(sheet).getByRole('region', { name: 'Resources' });
  expect(within(resources).getByRole('heading', { name: 'Rages: 3 of 3' })).toBeTruthy();
  expect(within(resources).getByRole('heading', { name: 'Stonecunning: 2 of 2' })).toBeTruthy();
  await user.click(within(resources).getByRole('button', { name: 'Spend 1 Rages' }));
  await waitFor(() => expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Rages: 2 of 3' })).toBeTruthy());

  const hp = screen.getByRole('region', { name: /^Hit points:/ });
  await user.type(within(hp).getByRole('spinbutton', { name: 'Amount' }), '5');
  await user.click(within(hp).getByRole('button', { name: 'Set temporary hit points' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 35 of 35, 5 temporary' })).toBeTruthy());
  await user.type(within(screen.getByRole('region', { name: /^Hit points:/ })).getByRole('spinbutton', { name: 'Amount' }), '12');
  await user.click(within(screen.getByRole('region', { name: /^Hit points:/ })).getByRole('button', { name: 'Take damage' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 28 of 35' })).toBeTruthy());

  await user.click(screen.getByRole('checkbox', { name: 'Poisoned' }));
  await waitFor(() => expect(screen.getByRole<HTMLInputElement>('checkbox', { name: 'Poisoned' }).checked).toBe(true));

  // A feature roll shows its record, and rolling spends nothing.
  await user.click(screen.getByRole('checkbox', { name: /Critical hit/ }));
  await user.click(screen.getByRole('button', { name: 'Roll Frenzy extra damage (Rage Damage +2: 2d6) (2d6)' }));
  const lastRoll = screen.getByRole('region', { name: 'Last roll' });
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Frenzy extra damage.*: \d+ \(2d6, critical\)/));
  expect(lastRoll.textContent).toMatch(/d6 \d \(critical\)/); // doubled dice are marked
  expect(lastRoll.textContent).toMatch(/Frenzy \(System Reference Document 5\.2\.1, p\. \d+\)/);

  // A d20 test with advantage keeps one die and drops the other.
  await user.click(screen.getByRole('radio', { name: 'Advantage' }));
  const strSave = screen.getByRole('region', { name: /^Strength saving throw:/ });
  await user.click(within(strSave).getByRole('heading'));
  await user.click(within(strSave).getByRole('button', { name: 'Roll Strength saving throw' }));
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Strength saving throw \(d20 test\): \d+ \(1d20, advantage\)/));
  expect(lastRoll.textContent).toMatch(/\(dropped\)/);
  expect(lastRoll.textContent).toMatch(/Strength saving throw \+5/);
  expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Rages: 2 of 3' })).toBeTruthy();

  // Features list their automation status.
  const features = screen.getByRole('region', { name: 'Features' });
  expect(within(features).getByText('Danger Sense').closest('li')!.textContent).toMatch(/reference only|assisted/);

  // M2 item 3, long rest (D01): preview first, cancel changes nothing, then confirm.
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  let rest = await screen.findByRole('region', { name: 'Long rest' });
  await waitFor(() => expect(document.activeElement).toBe(within(rest).getByRole('heading', { name: 'Long rest' })));
  expect(within(rest).getByRole('checkbox', { name: /^Hit points: 28 → 35/ })).toBeTruthy();
  expect(within(rest).getByRole('checkbox', { name: /^Rages: 2 → 3/ })).toBeTruthy();
  await user.click(within(rest).getByRole('button', { name: 'Cancel rest' }));
  expect(screen.getByRole('heading', { name: 'Hit points: 28 of 35' })).toBeTruthy();

  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  rest = await screen.findByRole('region', { name: 'Long rest' });
  await user.click(await within(rest).findByRole('checkbox', { name: /^Rages: 2 → 3/ })); // untick: keep Rages as they are
  await user.click(within(rest).getByRole('button', { name: 'Finish long rest' }));
  expect((await screen.findByRole('status')).textContent).toMatch(/Long rest finished: 1 change applied/);
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 35 of 35' })).toBeTruthy());
  expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Rages: 2 of 3' })).toBeTruthy();

  // Short rest (D01 follow-up): spend a hit die rolled at the table (5 + Con 2 = 7), and Rage regains one use (2024).
  const hpPanel = () => screen.getByRole('region', { name: /^Hit points:/ });
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '10');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Take damage' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 25 of 35' })).toBeTruthy());
  expect(hpPanel().textContent).toMatch(/Hit dice: d12 3 of 3/);
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  rest = await screen.findByRole('region', { name: 'Short rest' });
  await waitFor(() => expect(document.activeElement).toBe(within(rest).getByRole('heading', { name: 'Short rest' })));
  expect(await within(rest).findByRole('checkbox', { name: /^Rages: 2 → 3/ })).toBeTruthy();
  await user.type(within(rest).getByRole('spinbutton', { name: 'd12 rolled at the table' }), '5');
  await user.click(within(rest).getByRole('button', { name: 'Add d12' }));
  const spend = await within(rest).findByRole('list', { name: 'Hit dice to spend' });
  expect(spend.textContent).toMatch(/Rolled 5, Constitution modifier \+2: 7 hit point\(s\)\. Hit points 25 → 32/);
  await user.click(within(rest).getByRole('button', { name: 'Finish short rest' }));
  expect((await screen.findByRole('status')).textContent).toMatch(/Short rest finished: 2 changes applied/);
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 32 of 35' })).toBeTruthy());
  expect(hpPanel().textContent).toMatch(/Hit dice: d12 2 of 3/);
  expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Rages: 3 of 3' })).toBeTruthy();

  // Heroic Inspiration (2024) and death saving throws (SPEC C-05): a 20 at the table regains 1 hit point.
  await user.click(within(hpPanel()).getByRole('checkbox', { name: 'Heroic Inspiration' }));
  await waitFor(() => expect(within(hpPanel()).getByRole<HTMLInputElement>('checkbox', { name: 'Heroic Inspiration' }).checked).toBe(true));
  expect(screen.queryByRole('region', { name: /^Death saving throws/ })).toBeNull();
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '40');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Take damage' }));
  const deathSaves = await screen.findByRole('region', { name: /^Death saving throws: 0 of 3 successes, 0 of 3 failures/ });
  await user.click(within(deathSaves).getByRole('button', { name: 'Add a failure (damage at 0)' }));
  await screen.findByRole('region', { name: /^Death saving throws: 0 of 3 successes, 1 of 3 failures/ });
  await user.type(within(screen.getByRole('region', { name: /^Death saving throws/ })).getByRole('spinbutton', { name: 'd20 rolled at the table' }), '20');
  await user.click(within(screen.getByRole('region', { name: /^Death saving throws/ })).getByRole('button', { name: 'Record this roll' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 1 of 35' })).toBeTruthy());
  expect(screen.queryByRole('region', { name: /^Death saving throws/ })).toBeNull(); // regaining hit points cleared them
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '34');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Heal' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 35 of 35' })).toBeTruthy());

  // M2 item 4: armor replaces Unarmored Defense (13); a shield adds to it. Original fixture equipment.
  const equipment = () => screen.getByRole('region', { name: 'Equipment' });
  await waitFor(() => expect(within(equipment()).getByRole('option', { name: /^Fixture Scale Vest/ })).toBeTruthy());
  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^Fixture Scale Vest/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip Fixture Scale Vest' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: /^Armor Class: 15/ })).toBeTruthy()); // 14 + Dex 1
  const ac = screen.getByRole('region', { name: /^Armor Class:/ });
  await user.click(within(ac).getByRole('heading'));
  expect(within(ac).getByRole('table').textContent).toMatch(/Unarmored Defense.*not used: it applies only while no armor is worn/);

  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^Fixture Kite Shield/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip Fixture Kite Shield' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: /^Armor Class: 17/ })).toBeTruthy());
  await user.click(within(equipment()).getByRole('checkbox', { name: 'Equip Fixture Scale Vest' })); // take the armor off
  await waitFor(() => expect(screen.getByRole('heading', { name: /^Armor Class: 15/ })).toBeTruthy()); // Unarmored Defense 13 + shield 2
});

const srd = (n: number) => ({
  contentId: `52c00000-0000-4000-8000-${String(n).padStart(12, '0')}`,
  revisionId: `52e00000-0000-4000-8000-${String(n).padStart(12, '0')}`,
});

it('authors a homebrew subclass in the studio, plays it, and reviews an update', async () => {
  const user = userEvent.setup();
  // Setup through the client: an SRD 5.2.1 Barbarian 3 without a subclass (the builder test covers building one).
  await client.createCharacter({
    name: 'E2E Storm',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 15, dex: 13, con: 14, int: 8, wis: 12, cha: 10 },
    pins: [srd(1), srd(2)],
    classes: [{ class: srd(11), level: 3 }],
    choices: [
      { source: srd(2), choiceId: 'soldier-ability-scores', selected: [srd(4)] },
      { source: srd(11), choiceId: 'barbarian-skills', selected: [srd(21), srd(22)] },
      { source: srd(25), choiceId: 'primal-knowledge-skill', selected: [srd(17)] },
    ],
  });
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);

  // A homebrew source for 2024 rules.
  const newSource = await screen.findByRole('form', { name: 'New homebrew source' });
  await user.type(within(newSource).getByRole('textbox', { name: 'Source title' }), 'E2E Homebrew');
  await user.click(within(newSource).getByRole('checkbox', { name: 'SRD 5.2.1 (2024 rules)' }));
  await user.click(within(newSource).getByRole('button', { name: 'Create source' }));
  await screen.findByRole('heading', { name: 'Content in E2E Homebrew' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(editor()).getByRole('group', { name });
  async function publish(name: string) {
    await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe(`Published ${name}.`));
  }

  // Feature 1: a class resource, its long-rest recovery, and a limited-use action that spends it.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Storm Ward');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  await user.type(within(rule(/^Rule 1: Resource/)).getByRole('textbox', { name: 'Resource name' }), 'Storm charges');
  await user.click(within(editor()).getByRole('button', { name: 'Add recovery' }));
  await user.click(within(editor()).getByRole('button', { name: 'Add roll or action' }));
  const action = rule(/^Rule 3: Roll or action/);
  await user.type(within(action).getByRole('textbox', { name: 'Roll name' }), 'Storm bolt');
  await user.clear(within(action).getByRole('textbox', { name: /^Dice/ }));
  await user.type(within(action).getByRole('textbox', { name: /^Dice/ }), '1d8 + 2');
  await user.selectOptions(within(action).getByRole('combobox', { name: /Uses a resource/ }), 'Storm charges');
  await publish('E2E Storm Ward');

  // Feature 2: reference-only text.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Sky Lore');
  await user.type(within(editor()).getByRole('textbox', { name: /^Description/ }), 'You can read tomorrow’s weather.');
  await publish('E2E Sky Lore');

  // The subclass: offered in the SRD Barbarian's subclass choice, a modifier, and both features at level 3.
  await user.click(screen.getByRole('button', { name: 'New subclass' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'Path of the E2E Storm');
  const offered = within(editor()).getByRole('combobox', { name: 'Offered in the choice' });
  await waitFor(() => expect(within(offered).getByRole('option', { name: /^Barbarian: Level 3: Barbarian Subclass/ })).toBeTruthy());
  await user.selectOptions(offered, within(offered).getByRole('option', { name: /^Barbarian: Level 3: Barbarian Subclass/ }));
  await user.click(within(editor()).getByRole('button', { name: 'Add modifier' })); // default: Initiative +1
  await user.click(within(editor()).getByRole('button', { name: 'Grant a feature' }));
  await user.selectOptions(within(rule(/^Rule 2: Granted feature/)).getByRole('combobox', { name: 'Feature' }), 'E2E Storm Ward (published)');
  await user.click(within(editor()).getByRole('button', { name: 'Grant a feature' }));
  await user.selectOptions(within(rule(/^Rule 3: Granted feature/)).getByRole('combobox', { name: 'Feature' }), 'E2E Sky Lore (published)');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect(await within(editor()).findByText('No problems found. It can be published.')).toBeTruthy();
  await publish('Path of the E2E Storm');

  // Play it: the homebrew subclass is an option of the SRD choice.
  await user.click(screen.getByRole('button', { name: /^E2E Storm/ }));
  let sheet = await screen.findByRole('article', { name: 'E2E Storm' });
  await user.click(within(sheet).getByRole('button', { name: 'Make choices' }));
  await pick(user, /^Barbarian: choose 1/, /^Path of the E2E Storm/);
  await user.click(await screen.findByRole('button', { name: 'Save choices' }));
  sheet = await screen.findByRole('article', { name: 'E2E Storm' });
  expect(within(sheet).getByRole('heading', { name: /^Initiative: \+2/ })).toBeTruthy(); // Dex +1, homebrew +1
  const resources = within(sheet).getByRole('region', { name: 'Resources' });
  expect(within(resources).getByRole('heading', { name: 'Storm charges: 2 of 2' })).toBeTruthy();
  expect(within(within(sheet).getByRole('region', { name: 'Features' })).getByText('E2E Sky Lore').closest('li')!.textContent).toMatch(/reference only/);
  await user.click(within(sheet).getByRole('button', { name: 'Roll Storm bolt (1d8 + 2)' }));
  await user.click(await screen.findByRole('button', { name: 'Spend 1 Storm charges (2 left)' }));
  await waitFor(() => expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Storm charges: 1 of 2' })).toBeTruthy());

  // Republish with +3 initiative: the character keeps +2 until the reviewed update is applied.
  await user.click(screen.getByRole('button', { name: 'Homebrew studio' }));
  await user.click(await screen.findByRole('button', { name: 'Edit Path of the E2E Storm' }));
  const modifier = rule(/^Rule 1: Modifier/);
  await user.clear(within(modifier).getByRole('textbox', { name: /^Value/ }));
  await user.type(within(modifier).getByRole('textbox', { name: /^Value/ }), '3');
  await publish('Path of the E2E Storm');
  await user.click(await screen.findByRole('button', { name: 'Review update for E2E Storm' }));
  const review = await screen.findByRole('region', { name: 'Update E2E Storm: Path of the E2E Storm' });
  const values = await within(review).findByRole('table', { name: 'Calculated values that change' });
  expect(values.textContent).toMatch(/Initiative24/);
  await user.click(within(review).getByRole('button', { name: 'Apply update' }));
  // The status line already holds the publish message; wait for the update's own message instead of the first match.
  await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/Updated E2E Storm/), { timeout: 5000 });
  await user.click(screen.getByRole('button', { name: /^E2E Storm/ }));
  expect(await screen.findByRole('heading', { name: /^Initiative: \+4/ })).toBeTruthy();
});

it('attaches a PDF to a source, offers the cited page on a feature, and removes it after a warning', async () => {
  const user = userEvent.setup();
  // Setup through the client: a homebrew source, a published feat citing page 7, and a character using it.
  const source = await client.createHomebrewSource('E2E Book', ['srd-5.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feat',
    name: 'E2E Cited Feat',
    rulesFamilies: ['srd-5.1'],
    provenance: { sourceId: source.id, page: { start: 7 } },
    status: 'draft',
    summary: 'A feat that cites page 7.',
    effects: [],
  });
  const feat = (await client.publish(draft)).published;
  await client.createCharacter({ name: 'E2E Reader', rulesFamily: 'srd-5.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [feat] });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Sources' }));
  const book = await screen.findByRole('listitem', { name: 'E2E Book' });
  expect(within(book).getByText('No PDF attached.')).toBeTruthy();

  // DevHost has no native Open dialog, so the browser file picker sends the bytes (a managed copy).
  await user.click(within(book).getByRole('button', { name: 'Attach PDF…' }));
  const pdf = new File([new TextEncoder().encode('%PDF-1.4\n% e2e\n%%EOF\n')], 'e2e-book.pdf', { type: 'application/pdf' });
  await user.upload(screen.getByLabelText('PDF file'), pdf);
  // The upload hashes and copies the file, then reloads every source: allow more than the 1 s default under load.
  await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'E2E Book' })).getByText(/PDF: e2e-book\.pdf .*copy in TomeStack.*available/)).toBeTruthy(), { timeout: 5000 });

  // SPEC I-03: pages 3-4 become a draft reference entry (nothing is extracted; it stays inactive until published).
  const pages = within(screen.getByRole('listitem', { name: 'E2E Book' })).getByRole('group', { name: 'Import pages of E2E Book as reference' });
  await user.type(within(pages).getByRole('spinbutton', { name: 'First page' }), '3');
  await user.type(within(pages).getByRole('spinbutton', { name: 'Last page (optional)' }), '4');
  await user.type(within(pages).getByRole('textbox', { name: 'Title (optional)' }), 'E2E Chapter');
  await user.click(within(pages).getByRole('button', { name: 'Import pages' }));
  expect((await screen.findByRole('status')).textContent).toMatch(/Draft reference entry "E2E Chapter" created/);
  const drafts = await client.contentBySource(source.id);
  expect(drafts.find((e) => e.name === 'E2E Chapter')?.revisions[0]).toMatchObject({ status: 'draft', provenance: { page: { start: 3, end: 4 } } });

  // The feature offers its cited page; opening needs the desktop app's viewer, which DevHost does not have.
  await user.click(screen.getByRole('button', { name: /^E2E Reader/ }));
  const open = await screen.findByRole('button', { name: 'Open E2E Cited Feat, p. 7' });
  await user.click(open);
  expect((await screen.findByRole('alert')).textContent).toMatch(/needs the TomeStack desktop app/);

  // Removing says what breaks, and keeps the content.
  await user.click(screen.getByRole('button', { name: 'Sources' }));
  await user.click(within(await screen.findByRole('listitem', { name: 'E2E Book' })).getByRole('button', { name: 'Remove PDF…' }));
  const confirm = await screen.findByRole('alertdialog', { name: 'Remove e2e-book.pdf?' });
  // The draft reference entry cites pages in it too.
  expect(confirm.textContent).toMatch(/2 entries cite pages in it \(E2E Chapter, E2E Cited Feat\)/);
  await user.click(within(confirm).getByRole('button', { name: 'Remove PDF' }));
  await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'E2E Book' })).getByText('No PDF attached.')).toBeTruthy());
  await user.click(screen.getByRole('button', { name: /^E2E Reader/ }));
  await screen.findByRole('article', { name: 'E2E Reader' });
  expect(screen.queryByRole('button', { name: 'Open E2E Cited Feat, p. 7' })).toBeNull();
  expect(screen.getByText('E2E Cited Feat')).toBeTruthy();
});

it('shows different allowed content for two campaign profiles, and records a reasoned exception', async () => {
  const user = userEvent.setup();
  render(<App />);
  const campaignsButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Campaigns' });
  await waitFor(() => expect(campaignsButton.disabled).toBe(false));
  await user.click(campaignsButton);

  async function createCampaign(name: string, sources: RegExp[]) {
    await user.click(await screen.findByRole('button', { name: 'New campaign' }));
    const form = screen.getByRole('form', { name: 'Campaign' });
    await user.type(within(form).getByRole('textbox', { name: 'Campaign name' }), name);
    await user.click(within(form).getByRole('radio', { name: /SRD 5\.2\.1/ }));
    for (const source of sources) await user.click(within(form).getByRole('checkbox', { name: source }));
    await user.click(within(form).getByRole('button', { name: 'Save campaign' }));
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe(`Saved the campaign ${name}.`));
  }
  await createCampaign('E2E Strict', [/^System Reference Document 5\.2\.1/]);
  await createCampaign('E2E Open', [/^System Reference Document 5\.2\.1/, /^TomeStack Fixtures: 2024 Family/]);

  await user.click(screen.getByRole('button', { name: 'New character' }));
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Campaigner');
  const campaignSelect = screen.getByRole('combobox', { name: 'Campaign' });
  const courier = () => screen.getByRole<HTMLInputElement>('radio', { name: /^Fixture Courier/ });
  const soldier = () => screen.getAllByRole<HTMLInputElement>('radio', { name: /^Soldier/ }).find((r) => !r.disabled);

  // Profile 1: SRD only. The fixture background is listed but not allowed; the SRD one is.
  await user.selectOptions(campaignSelect, 'E2E Strict (srd-5.2.1)');
  await waitFor(() => expect(courier().disabled).toBe(true));
  expect(courier().closest('label')!.textContent).toMatch(/not allowed in this campaign/);
  expect(soldier()).toBeTruthy();

  // Profile 2: SRD and the 2024 fixtures. The same background is allowed.
  await user.selectOptions(campaignSelect, 'E2E Open (srd-5.2.1)');
  await waitFor(() => expect(courier().disabled).toBe(false));

  // Back to profile 1, with a deliberate exception: a reason is required before outside content can be picked.
  await user.selectOptions(campaignSelect, 'E2E Strict (srd-5.2.1)');
  await waitFor(() => expect(courier().disabled).toBe(true));
  const sources = screen.getByRole('group', { name: 'Campaign sources' });
  await user.click(within(sources).getByRole('checkbox', { name: 'Use content from outside the campaign' }));
  expect(courier().disabled).toBe(true); // no reason yet
  await user.type(within(sources).getByRole('textbox', { name: /^Reason/ }), 'DM approved');
  await waitFor(() => expect(courier().disabled).toBe(false));
  await user.click(courier());
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));

  const sheet = await screen.findByRole('article', { name: 'E2E Campaigner' });
  expect(within(sheet).getByText('Campaign: E2E Strict', { selector: '.tag' })).toBeTruthy();
  const notes = within(sheet).getByRole('region', { name: 'Campaign: E2E Strict' });
  expect(notes.textContent).toMatch(/Fixture Courier.*used by exception: DM approved/);
});

it('keeps a resource linked to its recovery when the resource is renamed in the studio', async () => {
  const user = userEvent.setup();
  await client.createHomebrewSource('E2E Renames', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Renames/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Renames/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Renames' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Fury');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  const name = within(within(editor()).getByRole('group', { name: /^Rule 1: Resource/ })).getByRole('textbox', { name: 'Resource name' });
  await user.type(name, 'Fury');
  await user.click(within(editor()).getByRole('button', { name: 'Add recovery' }));
  // Renamed after the recovery points at it: the recovery must still restore this resource.
  await user.type(name, ' points');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));

  const results = await within(editor()).findByRole('region', { name: 'Check results' });
  expect(results.textContent).not.toMatch(/does not define/);
});

it('drops picks that do not fit when the rules family changes, in the builder and in a campaign', async () => {
  const user = userEvent.setup();
  render(<App />);
  const newCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));

  // Builder: an SRD 5.1 species, then SRD 5.2.1. The 5.1 pick is cleared instead of staying checked but disabled.
  await user.click(newCharacter);
  await user.click(screen.getByRole('radio', { name: /SRD 5\.1/ }));
  const species = screen.getByRole('group', { name: 'Species' });
  const halfOrc = () => within(species).getByRole<HTMLInputElement>('radio', { name: /^Half-Orc/ });
  await waitFor(() => expect(halfOrc().disabled).toBe(false));
  await user.click(halfOrc());
  expect(halfOrc().checked).toBe(true);
  await user.click(screen.getByRole('radio', { name: /SRD 5\.2\.1/ }));
  await waitFor(() => expect(halfOrc().disabled).toBe(true));
  expect(halfOrc().checked).toBe(false);
  expect(within(species).getByRole<HTMLInputElement>('radio', { name: 'None' }).checked).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Cancel' }));

  // Campaign: an SRD 5.1 source, then SRD 5.2.1. The hidden 5.1 source is not saved with the campaign.
  await user.click(screen.getByRole('button', { name: 'Campaigns' }));
  await user.click(await screen.findByRole('button', { name: 'New campaign' }));
  const form = screen.getByRole('form', { name: 'Campaign' });
  await user.type(within(form).getByRole('textbox', { name: 'Campaign name' }), 'E2E Switched');
  await user.click(within(form).getByRole('radio', { name: /SRD 5\.1/ }));
  await user.click(within(form).getByRole('checkbox', { name: /^System Reference Document 5\.1/ }));
  await user.click(within(form).getByRole('radio', { name: /SRD 5\.2\.1/ }));
  await user.click(within(form).getByRole('button', { name: 'Save campaign' }));
  await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Saved the campaign E2E Switched.'));
  const saved = screen.getAllByRole('listitem').find((li) => li.textContent?.startsWith('E2E Switched'))!;
  expect(saved.textContent).toMatch(/0 allowed sources/);
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

it('equips a weapon: the attack uses finesse and proficiency, rolls, and actions are grouped', async () => {
  // SPEC C-02, C-04 with the original fixtures "Fixture Duelist" (simple and martial weapons) and "Fixture Needle" (finesse).
  const user = userEvent.setup();
  render(<App />);
  const newCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));
  await user.click(newCharacter);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Blade');
  const scores = screen.getByRole('group', { name: 'Base ability scores' });
  for (const [label, value] of [['Strength', 14], ['Dexterity', 16]] as const) {
    const input = within(scores).getByRole('spinbutton', { name: label });
    await user.clear(input);
    await user.type(input, String(value));
  }
  await user.click(await screen.findByRole('radio', { name: /^Fixture Duelist/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  await pick(user, /^Fixture Duelist: choose 2/, /^Duelist Skill: Acrobatics/);
  await pick(user, /^Fixture Duelist: choose 2/, /^Duelist Skill: Insight/);
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  await screen.findByRole('article', { name: 'E2E Blade' });

  const equipment = () => screen.getByRole('region', { name: 'Equipment' });
  await waitFor(() => expect(within(equipment()).getByRole('option', { name: /^Fixture Needle/ })).toBeTruthy());
  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^Fixture Needle/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip Fixture Needle' }));

  // Dex +3 (finesse beats Str +2) + PB 2 = +5; damage 1d4 + 3.
  const actions = () => screen.getByRole('region', { name: 'Attacks and actions' });
  await waitFor(() => expect(within(actions()).getByText(/\+5 to hit, 1d4\+3 piercing/)).toBeTruthy());
  await user.click(within(actions()).getByRole('button', { name: 'Roll Fixture Needle attack' }));
  const lastRoll = screen.getByRole('region', { name: 'Last roll' });
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Fixture Needle attack: \d+ \(1d20\)/));
  expect(lastRoll.textContent).toMatch(/Fixture Needle to hit \+5/);
  const reactions = within(actions()).getByRole('region', { name: 'Reactions' });
  expect(within(reactions).getByRole('button', { name: 'Roll Riposte damage (1d6)' })).toBeTruthy();
});

it('builds a spellcaster: picks spells in the builder, casts one, rolls a spell attack and a long rest restores the slot', async () => {
  // D04 (M2 spellcasting) with the original fixture caster "Fixture Arcanist" (invented tables: 2 level 1 slots at level 1).
  const user = userEvent.setup();
  render(<App />);
  const newCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));
  await user.click(newCharacter);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Sage');
  await user.click(screen.getByRole('radio', { name: /SRD 5\.2\.1/ }));
  const intelligence = within(screen.getByRole('group', { name: 'Base ability scores' })).getByRole('spinbutton', { name: 'Intelligence' });
  await user.clear(intelligence);
  await user.type(intelligence, '16');
  await user.click(await screen.findByRole('radio', { name: /^Fixture Arcanist/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));

  // Int 16 (+3) at level 1: 3 cantrips, max(1, 3 + 1) = 4 prepared. Only spells on the caster's list and castable levels.
  let picker = await screen.findByRole('group', { name: /^Fixture Arcanist spells \(0 of 3 cantrips, 0 of 4 prepared spells\)/ });
  expect(within(picker).queryByRole('checkbox', { name: /Fixture Mending Word/ })).toBeNull(); // another list
  expect(within(picker).queryByRole('checkbox', { name: /Fixture Ember Wave/ })).toBeNull(); // level 3: no slot yet
  await user.click(within(picker).getByRole('checkbox', { name: /^Fixture Spark/ }));
  picker = await screen.findByRole('group', { name: /^Fixture Arcanist spells \(1 of 3 cantrips/ });
  await user.click(within(picker).getByRole('checkbox', { name: /^Fixture Frost Ring/ }));
  await screen.findByRole('group', { name: /^Fixture Arcanist spells \(1 of 3 cantrips, 1 of 4 prepared spells\)/ });
  await user.click(screen.getByRole('button', { name: 'Create and save' }));

  const sheet = await screen.findByRole('article', { name: 'E2E Sage' });
  const spells = () => within(screen.getByRole('article', { name: 'E2E Sage' })).getByRole('region', { name: 'Spells and slots' });
  expect(within(spells()).getByRole('heading', { name: 'Fixture Arcanist (level 1, Intelligence): spell attack +5, save DC 13' })).toBeTruthy();
  expect(within(spells()).getByRole('heading', { name: 'Level 1 slots: 2 of 2' })).toBeTruthy();
  expect(within(sheet).getByRole('heading', { name: /^Spell attack bonus: \+5/ })).toBeTruthy();

  // Casting spends a slot (a confirmed play change); rolling a spell spends nothing.
  await user.click(within(spells()).getByRole('button', { name: 'Cast Fixture Frost Ring (spend a slot)' }));
  await waitFor(() => expect(within(spells()).getByRole('heading', { name: 'Level 1 slots: 1 of 2' })).toBeTruthy());
  await user.click(within(spells()).getByRole('button', { name: 'Roll Fixture Spark attack' }));
  const lastRoll = screen.getByRole('region', { name: 'Last roll' });
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Fixture Spark \(spell attack\): \d+ \(1d20\)/));
  expect(lastRoll.textContent).toMatch(/Fixture Arcanist spell attack \+5/);
  expect(within(spells()).getByRole('heading', { name: 'Level 1 slots: 1 of 2' })).toBeTruthy();

  // The long rest proposes the slot back.
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const rest = await screen.findByRole('region', { name: 'Long rest' });
  expect(await within(rest).findByRole('checkbox', { name: /^Level 1 spell slots: 1 → 2/ })).toBeTruthy();
  await user.click(within(rest).getByRole('button', { name: 'Finish long rest' }));
  await waitFor(() => expect(within(spells()).getByRole('heading', { name: 'Level 1 slots: 2 of 2' })).toBeTruthy());
});
