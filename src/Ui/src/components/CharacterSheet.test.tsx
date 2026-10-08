// @vitest-environment jsdom
// The sheet's tabs (ADR-014): Spells only when there is something to show, a tab that is not offered falls back to Play,
// "Report a gap" switches to Notes and focuses the note text every time it is pressed, and choosing Notes by hand keeps
// focus on the tab. The client is mocked; values are invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterSheet as SheetModel, CharacterView, DerivedValue, FeatureEntry, RollRecord } from '../api/types';
import { CharacterSheet } from './CharacterSheet';

vi.mock('../api/client', () => ({
  client: {
    attachment: vi.fn(),
    listGapNotes: vi.fn(),
    availableUpdates: vi.fn(),
    listContent: vi.fn(),
    snapshots: vi.fn(),
    previewExport: vi.fn(),
    info: vi.fn(),
    play: vi.fn(),
    roll: vi.fn(),
    restPreview: vi.fn(),
    rest: vi.fn(),
  },
}));

beforeEach(() => {
  vi.mocked(client.restPreview).mockResolvedValue({ kind: 'shortRest', changes: [], manual: [], basis: 'fixture' });
  vi.mocked(client.rest).mockReset();
  vi.mocked(client.listGapNotes).mockResolvedValue([]);
  vi.mocked(client.availableUpdates).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.snapshots).mockResolvedValue({ items: [], hasMore: false });
});
afterEach(() => {
  cleanup();
  localStorage.clear(); // the remembered tab is per character id, and these tests share one
  delete document.documentElement.dataset.compactPlay;
});

const field = (id: string, label: string, value: number, units: string): DerivedValue => ({ field: id, label, value, computedValue: value, trace: [], warnings: [], automation: 'automatic', units });

function view(sheetOver: Partial<SheetModel> = {}): CharacterView {
  const character: Character = {
    id: 'fixture-2',
    schemaVersion: 7,
    name: 'Fixture Quill',
    rulesFamily: 'srd-5.1',
    level: 1,
    classes: [],
    choices: [],
    crossFamilyExceptions: [],
    baseAbilities: { str: 10, dex: 12, con: 10, int: 14, wis: 10, cha: 10 },
    pins: [],
    overrides: [],
    updatedAt: '2026-10-03T00:00:00Z',
  };
  const sheet: SheetModel = {
    characterId: 'fixture-2',
    rulesFamily: 'srd-5.1',
    diagnostics: [],
    fields: [
      field('ability.dex.score', 'Dexterity score', 12, 'score'),
      field('ability.dex.mod', 'Dexterity modifier', 1, 'modifier'),
      field('armorClass', 'Armor Class', 11, 'score'),
      field('initiative', 'Initiative', 1, 'modifier'),
      field('proficiencyBonus', 'Proficiency bonus', 2, 'bonus'),
      field('spellAttack', 'Spell attack bonus', 0, 'modifier'),
    ],
    hitPoints: { maximum: 8, current: 8, temporary: 0 },
    ...sheetOver,
  };
  return { character, sheet };
}

const noop = () => {};
const sheetElement = (v: CharacterView, initialTab?: Parameters<typeof CharacterSheet>[0]['initialTab']) => (
  <CharacterSheet view={v} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} initialTab={initialTab} />
);
const renderSheet = (v: CharacterView, initialTab?: Parameters<typeof CharacterSheet>[0]['initialTab']) => render(sheetElement(v, initialTab));

function undoHarness() {
  const v = view();
  const damaged = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 3, temporary: 0 } } };
  const onChanged = vi.fn();
  const element = (shown: CharacterView) => <CharacterSheet view={shown} onChanged={onChanged} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />;
  return { v, damaged, onChanged, element };
}

it('offers Undo for the last play change and sends the inverse through the play command (D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged, element } = undoHarness();
  vi.mocked(client.play).mockResolvedValueOnce(damaged).mockResolvedValue(v);
  const { rerender } = render(element(v));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(damaged));
  rerender(element(damaged)); // the app shows the returned view
  const undo = screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change: damage 1' });
  expect(undo.disabled).toBe(false);
  await user.click(undo);
  await waitFor(() => expect(client.play).toHaveBeenLastCalledWith('fixture-2', { action: 'setHitPoints', amount: 8 }));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true));
  // WCAG 2.4.3: the button is disabled now, so focus is on the hit points heading, not <body>.
  expect(document.activeElement?.id).toBe('hp-heading');
});

it('shows the applied state and keeps focus when a later undo step fails (D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged } = undoHarness();
  const first = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 5, temporary: 0 } } };
  const onError = vi.fn();
  const play = { temporaryHitPoints: 0, resources: [], conditions: [], exhaustion: 0 };
  const before = { ...v, character: { ...v.character, play: { ...play, concentration: { spell: { contentId: 's', revisionId: 'r' }, name: 'Fixture Ward' } } } };
  const after = { ...damaged, character: { ...v.character, play } };
  vi.mocked(client.play).mockResolvedValueOnce(after).mockResolvedValueOnce(first).mockRejectedValueOnce(new Error('play.spell-not-prepared'));
  const shown = (x: CharacterView) => <CharacterSheet view={x} onChanged={onChanged} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />;
  const { rerender } = render(shown(before));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(after));
  rerender(shown(after));
  await user.click(screen.getByRole('button', { name: /^Undo last change: damage 1/ }));
  await waitFor(() => expect(onError).toHaveBeenCalled());
  expect(onChanged).toHaveBeenLastCalledWith(first);
  expect(document.activeElement).not.toBe(document.body);
  expect(document.activeElement?.id).toBe('hp-heading');
});

it('offers no Undo for play changes that overlapped, since the stored "before" is not the state they changed (D23)', async () => {
  const user = userEvent.setup();
  const { v, onChanged, element } = undoHarness();
  const hit = (current: number): CharacterView => ({ ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current, temporary: 0 } } });
  const [first, second] = [hit(7), hit(6)];
  let settleFirst: (view: CharacterView) => void = noop;
  let settleSecond: (view: CharacterView) => void = noop;
  vi.mocked(client.play)
    .mockReturnValueOnce(new Promise<CharacterView>((resolve) => { settleFirst = resolve; }))
    .mockReturnValueOnce(new Promise<CharacterView>((resolve) => { settleSecond = resolve; }));
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  settleFirst(first);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(first));
  settleSecond(second);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(second));
  rerender(element(second));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
});

const pending = () => {
  let settle: (view: CharacterView) => void = noop;
  const promise = new Promise<CharacterView>((resolve) => {
    settle = resolve;
  });
  return { promise, settle: (view: CharacterView) => settle(view) };
};

it('withdraws an offered Undo as soon as another play change starts, so the two cannot interleave (D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged, element } = undoHarness();
  const next = pending();
  vi.mocked(client.play).mockResolvedValueOnce(damaged).mockReturnValueOnce(next.promise);
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(damaged));
  rerender(element(damaged));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change: damage 1' }).disabled).toBe(false);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' })); // still in flight
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
  next.settle(damaged);
});

it('offers no Undo for a play change made while an Undo was running (D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged, element } = undoHarness();
  const [undoStep, hit] = [pending(), pending()];
  const seven = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 7, temporary: 0 } } };
  vi.mocked(client.play).mockResolvedValueOnce(damaged).mockReturnValueOnce(undoStep.promise).mockReturnValueOnce(hit.promise);
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(damaged));
  rerender(element(damaged));
  await user.click(screen.getByRole('button', { name: 'Undo last change: damage 1' }));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' })); // while the undo step is in flight
  undoStep.settle(v);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(v));
  hit.settle(seven);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(seven));
  rerender(element(seven));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
});

it('offers no Undo for a play change whose reply came after another view was shown, such as a rest (D23)', async () => {
  const user = userEvent.setup();
  const { v, onChanged, element } = undoHarness();
  const hit = pending();
  const rested = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 8, temporary: 0 } } };
  const seven = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 7, temporary: 0 } } };
  vi.mocked(client.play).mockReturnValueOnce(hit.promise);
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  rerender(element(rested)); // a rest's result lands while the damage is in flight
  hit.settle(seven);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(seven));
  rerender(element(seven));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
});

it('does not offer Undo once another view is shown, such as a rest result (D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged, element } = undoHarness();
  vi.mocked(client.play).mockResolvedValueOnce(damaged);
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(damaged));
  rerender(element(damaged));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change: damage 1' }).disabled).toBe(false);
  rerender(element({ ...v })); // a long rest healed the character: a different view object
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
});

it('offers a compact view on Play that is remembered as a preference (D20)', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  const toggle = screen.getByRole<HTMLInputElement>('checkbox', { name: /^Compact view/ });
  expect(toggle.checked).toBe(false);
  await user.click(toggle);
  expect(localStorage.getItem('tomestack.compactPlay')).toBe('on');
  expect(document.documentElement.dataset.compactPlay).toBe('on');
});

it('marks proficient and expert fields on Stats and lists passive scores and Speed (D24)', () => {
  const base = view().sheet.fields;
  renderSheet(
    view({
      fields: [
        ...base,
        { ...field('skill.stealth', 'Stealth', 5, 'modifier'), mark: 'expertise' },
        field('skill.perception', 'Perception', 1, 'modifier'),
        field('passive.perception', 'Passive Perception', 11, 'score'),
        field('speed', 'Speed', 30, 'feet'),
      ],
    }),
    'stats',
  );
  expect(screen.getByRole('heading', { name: 'Stealth: +5 · expertise' })).toBeTruthy();
  expect(screen.getByRole('heading', { name: 'Perception: +1' })).toBeTruthy(); // no mark when no grant
  expect(screen.getByRole('heading', { name: /^Passive Perception: 11/ })).toBeTruthy(); // unsigned
  expect(screen.getByRole('heading', { name: 'Speed: 30 ft.' })).toBeTruthy();
});

it('groups Features by what granted them, then by kind, and drops none (D25)', () => {
  const ref = (n: number) => ({ contentId: `00000000-0000-4000-8000-00000000c${n}00`, revisionId: `00000000-0000-4000-8000-00000000d${n}00` });
  const origin = { kind: 'content' as const, rulesFamily: 'srd-5.1' as const };
  const entry = (n: number, name: string, kind: FeatureEntry['kind'], grantedByName?: string): FeatureEntry => ({
    content: ref(n), name, kind, origin, automation: 'reference', effects: [], diagnostics: [], grantedBy: grantedByName ? ref(9) : undefined, grantedByName,
  });
  renderSheet(
    view({
      features: [entry(1, 'Fixture Fighter', 'class'), entry(2, 'Fixture Rage', 'feature', 'Fixture Fighter'), entry(3, 'Fixture Alert', 'feat', 'Fixture Wayfarer'), entry(4, 'Fixture Torch', 'item')],
    }),
    'features',
  );
  expect(screen.getAllByRole('heading', { level: 4 }).map((h) => h.textContent)).toEqual(['Classes', 'Items', 'From Fixture Fighter', 'From Fixture Wayfarer']);
  expect(Array.from(document.querySelectorAll('.feature .option-name')).map((n) => n.textContent)).toEqual(['Fixture Fighter', 'Fixture Torch', 'Fixture Rage', 'Fixture Alert']);
});

it('offers Spells to a caster, or when a spell field has a value, and opens on Play', () => {
  renderSheet(view());
  expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Play', 'Inventory', 'Features', 'Stats', 'Notes', 'Manage']);
  expect(screen.getByRole('tab', { name: 'Play' }).getAttribute('aria-selected')).toBe('true');
  cleanup();
  renderSheet(view({ spellcasting: [] , spellSlots: [{ level: 1, maximum: 2, spent: 0, remaining: 2, field: 'spellSlots.1' }], fields: [...view().sheet.fields, field('spellSlots.1', 'Level 1 spell slots', 2, 'score')] }));
  expect(screen.getByRole('tab', { name: 'Spells' })).toBeTruthy(); // a spell field with a value counts, even with no caster entry
  cleanup();
  const ref = { contentId: '00000000-0000-4000-8000-0000000000a1', revisionId: '00000000-0000-4000-8000-0000000000b1' };
  renderSheet(
    view({
      spellcasting: [
        {
          content: ref,
          name: 'Fixture Arcanist',
          effectId: 'fixture-spellcasting',
          classLevel: 1,
          ability: 'int',
          attackBonus: 4,
          saveDc: 12,
          preparation: 'prepared',
          spellList: 'fixture-list',
          slotKind: 'spellSlots',
          slots: [2],
          primary: true,
          origin: { kind: 'content', rulesFamily: 'srd-5.1', content: ref, contentName: 'Fixture Arcanist' },
          spells: [],
          warnings: [],
        },
      ],
    }),
  );
  expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Play', 'Spells', 'Inventory', 'Features', 'Stats', 'Notes', 'Manage']);
});

it('falls back to Play when the requested tab is not offered', () => {
  renderSheet(view(), 'spells');
  expect(screen.getByRole('tab', { name: 'Play' }).getAttribute('aria-selected')).toBe('true');
  expect(screen.getByRole('tabpanel', { name: 'Play' })).toBeTruthy();
});

it('"Report a gap" switches to Notes, pre-fills About and focuses the text, every time it is pressed', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Stats' }));
  const ac = screen.getByRole('region', { name: /^Armor Class:/ });
  await user.click(within(ac).getByRole('heading'));
  await user.click(within(ac).getByRole('button', { name: 'Report a gap: Armor Class' }));
  expect(screen.getByRole('tab', { name: 'Notes' }).getAttribute('aria-selected')).toBe('true');
  const form = screen.getByRole('form', { name: 'New gap note' });
  const text = within(form).getByRole('textbox', { name: /^What was missing or wrong/ });
  await waitFor(() => expect(document.activeElement).toBe(text));
  expect((within(form).getByRole('combobox', { name: /^About/ }) as HTMLSelectElement).value).toBe('field:armorClass');

  // A second report, after leaving Notes and moving focus away, must move focus to the text box again.
  await user.click(screen.getByRole('tab', { name: 'Stats' }));
  text.blur();
  await user.click(within(screen.getByRole('region', { name: /^Initiative:/ })).getByRole('heading'));
  await user.click(within(screen.getByRole('region', { name: /^Initiative:/ })).getByRole('button', { name: 'Report a gap: Initiative' }));
  await waitFor(() => expect(document.activeElement).toBe(text));
  expect((within(form).getByRole('combobox', { name: /^About/ }) as HTMLSelectElement).value).toBe('field:initiative');
});

it('keeps typed text in the gap note box when switching tabs and back', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Notes' }));
  // Choosing Notes by hand keeps focus on the tab (APG); only "Report a gap" moves focus into the text box.
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Notes' }));
  const form = screen.getByRole('form', { name: 'New gap note' });
  const text = within(form).getByRole('textbox', { name: /^What was missing or wrong/ }) as HTMLTextAreaElement;
  await user.type(text, 'Fixture draft: the rule text is unclear');
  await user.click(screen.getByRole('tab', { name: 'Stats' }));
  await user.click(screen.getByRole('tab', { name: 'Notes' }));
  expect((within(screen.getByRole('form', { name: 'New gap note' })).getByRole('textbox', { name: /^What was missing or wrong/ }) as HTMLTextAreaElement).value).toBe('Fixture draft: the rule text is unclear');
});

it('opens on the remembered tab, and an explicit initialTab wins over it', () => {
  localStorage.setItem('tomestack.sheetTab.fixture-2', 'stats');
  renderSheet(view());
  expect(screen.getByRole('tab', { name: 'Stats' }).getAttribute('aria-selected')).toBe('true');
  cleanup();
  renderSheet(view(), 'notes');
  expect(screen.getByRole('tab', { name: 'Notes' }).getAttribute('aria-selected')).toBe('true');
  expect(localStorage.getItem('tomestack.sheetTab.fixture-2')).toBe('stats'); // opening on a deep link does not rewrite the memory
});

it('remembers the tab chosen, so the next opening of the same character starts there', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Manage' }));
  cleanup();
  renderSheet(view());
  expect(screen.getByRole('tab', { name: 'Manage' }).getAttribute('aria-selected')).toBe('true');
});

it('renders the print preview after the summary and before the body, matching the grid order', async () => {
  vi.mocked(client.previewExport).mockResolvedValue({ included: [] } as unknown as Awaited<ReturnType<typeof client.previewExport>>);
  vi.mocked(client.info).mockResolvedValue({ version: '0.0.0-fixture' } as unknown as Awaited<ReturnType<typeof client.info>>);
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('button', { name: 'Print…' }));
  const article = document.querySelector('article.sheet') as HTMLElement;
  expect(Array.from(article.children).map((c) => `${c.tagName.toLowerCase()}.${c.className.split(' ')[0]}`)).toEqual(['header.sheet-header', 'section.sheet-summary', 'section.print-sheet', 'div.sheet-body']);
});

it('moves focus to the print preview heading when the preview opens (investigation 2026-10-06 item 9)', async () => {
  vi.mocked(client.previewExport).mockResolvedValue({ included: [] } as unknown as Awaited<ReturnType<typeof client.previewExport>>);
  vi.mocked(client.info).mockResolvedValue({ version: '0.0.0-fixture' } as unknown as Awaited<ReturnType<typeof client.info>>);
  const user = userEvent.setup();
  renderSheet(view());
  const print = screen.getByRole('button', { name: 'Print…' });
  expect(print.getAttribute('aria-controls')).toBeNull(); // 4.1.2: only while the preview exists
  await user.click(print);
  expect(print.getAttribute('aria-controls')).toBe('print-preview');
  const preview = screen.getByRole('region', { name: 'Print preview' });
  expect(preview.id).toBe('print-preview');
  await waitFor(() => expect(document.activeElement).toBe(within(preview).getByRole('heading', { name: 'Print character' })));
});

it('moves focus to Play when the open tab stops being offered', async () => {
  const user = userEvent.setup();
  const fields = view().sheet.fields;
  const withOverride = view({ fields: fields.map((f) => (f.field === 'spellAttack' ? { ...f, override: { field: 'spellAttack', value: 3 }, value: 3 } : f)) });
  const { rerender } = renderSheet(withOverride);
  await user.click(screen.getByRole('tab', { name: 'Spells' }));
  const card = screen.getByRole('region', { name: /^Spell attack bonus:/ });
  await user.click(within(card).getByRole('heading'));
  within(card).getByRole('button', { name: 'Report a gap: Spell attack bonus' }).focus();
  expect(screen.getByRole('tabpanel', { name: 'Spells' }).contains(document.activeElement)).toBe(true);

  rerender(sheetElement(view())); // the save that removed the override: no spell field has a value any more
  expect(screen.queryByRole('tab', { name: 'Spells' })).toBeNull();
  expect(screen.getByRole('tab', { name: 'Play' }).getAttribute('aria-selected')).toBe('true');
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Play' }));
});

it('puts lost focus on the selected tab, not always Play, when Spells appears', () => {
  const { rerender } = renderSheet(view(), 'manage');
  expect(screen.getByRole('tab', { name: 'Manage' }).getAttribute('aria-selected')).toBe('true');
  (document.activeElement as HTMLElement | null)?.blur();
  expect(document.activeElement).toBe(document.body);

  const fields = view().sheet.fields;
  rerender(sheetElement(view({ fields: fields.map((f) => (f.field === 'spellAttack' ? { ...f, override: { field: 'spellAttack', value: 3 }, value: 3 } : f)) }), 'manage'));
  expect(screen.getByRole('tab', { name: 'Spells' })).toBeTruthy();
  expect(screen.getByRole('tab', { name: 'Manage' }).getAttribute('aria-selected')).toBe('true');
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Manage' }));
});

it('says so on the Features tab when the character has no features', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Features' }));
  expect(within(screen.getByRole('tabpanel', { name: 'Features' })).getByText('No features yet.')).toBeTruthy();
});

it('falls back from a remembered tab that is not offered, without taking focus from the heading or rewriting the memory', () => {
  localStorage.setItem('tomestack.sheetTab.fixture-2', 'spells');
  renderSheet(view());
  expect(screen.getByRole('tab', { name: 'Play' }).getAttribute('aria-selected')).toBe('true');
  expect(document.activeElement).toBe(screen.getByRole('heading', { level: 2 }));
  expect(localStorage.getItem('tomestack.sheetTab.fixture-2')).toBe('spells');
});

it('keeps the header, print preview slot and summary as direct children of the article, with everything else in one body wrapper (ADR-015)', () => {
  renderSheet(view());
  const article = screen.getByRole('article');
  const children = Array.from(article.children).map((c) => `${c.tagName.toLowerCase()}.${c.className.split(' ')[0]}`);
  expect(children).toEqual(['header.sheet-header', 'section.sheet-summary', 'div.sheet-body']);
  const body = article.querySelector('.sheet-body')!;
  expect(within(body as HTMLElement).getByRole('tablist', { name: 'Sheet sections' })).toBeTruthy();
});

const childShape = (article: HTMLElement) => Array.from(article.children).map((c) => `${c.tagName.toLowerCase()}.${c.className.split(' ')[0]}`);

it('opens a rest from the header above the body, focuses its heading, and returns focus to the opener on cancel (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  const short = screen.getByRole('button', { name: 'Short rest…' });
  expect(short.getAttribute('aria-controls')).toBeNull(); // 4.1.2: only while the panel exists
  expect(short.getAttribute('aria-expanded')).toBe('false');
  expect(within(screen.getByRole('tabpanel', { name: 'Play' })).queryByRole('button', { name: /rest…$/ })).toBeNull();
  await user.click(short);
  expect(childShape(screen.getByRole('article'))).toEqual(['header.sheet-header', 'section.sheet-summary', 'div.rest-sheet', 'div.sheet-body']);
  const panel = screen.getByRole('region', { name: 'Short rest' });
  expect(panel.parentElement!.id).toBe('rest-panel');
  expect(short.getAttribute('aria-controls')).toBe('rest-panel');
  await waitFor(() => expect(document.activeElement).toBe(within(panel).getByRole('heading', { name: 'Short rest' })));
  expect(short.getAttribute('aria-expanded')).toBe('true');
  await user.click(within(panel).getByRole('button', { name: 'Cancel rest' }));
  expect(screen.queryByRole('region', { name: 'Short rest' })).toBeNull();
  expect(document.activeElement).toBe(short);
  expect(short.getAttribute('aria-expanded')).toBe('false');
});

it('switches from a short rest to a long rest without writing (Review Focus 3)', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const panel = screen.getByRole('region', { name: 'Long rest' });
  await waitFor(() => expect(document.activeElement).toBe(within(panel).getByRole('heading', { name: 'Long rest' })));
  expect(screen.queryByRole('region', { name: 'Short rest' })).toBeNull();
  expect(client.rest).not.toHaveBeenCalled();
});

it('discards the picked hit dice when switching from a short rest to a long rest', async () => {
  const user = userEvent.setup();
  renderSheet(view({ hitDice: [{ die: 8, total: 2, spent: 0, remaining: 2, classes: ['Fixture'] }] }));
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  const picked = { kind: 'shortRest' as const, basis: 'with-die', manual: [], changes: [{ id: 'hitDie:0', kind: 'hitDie', label: 'Spend a d8', from: 3, to: 8, reason: 'Fixture', die: 8, amount: 5 }] as unknown as Awaited<ReturnType<typeof client.restPreview>>['changes'] };
  vi.mocked(client.restPreview).mockImplementation(async (_id, kind, dice = []) => (dice.length > 0 ? picked : { kind, changes: [], manual: [], basis: 'fixture' }));
  await user.type(await screen.findByRole('spinbutton', { name: 'd8 rolled at the table' }), '5');
  await user.click(screen.getByRole('button', { name: 'Add d8' }));
  expect(await screen.findByRole('button', { name: /^Remove/ })).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const panel = screen.getByRole('region', { name: 'Long rest' });
  await waitFor(() => expect(vi.mocked(client.restPreview).mock.lastCall).toEqual(['fixture-2', 'longRest', []]));
  expect(within(panel).queryByRole('button', { name: /^Remove/ })).toBeNull();
  expect(client.rest).not.toHaveBeenCalled();
});

it('toggles the rest panel closed from its own opener and keeps focus there (D29)', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  const short = screen.getByRole('button', { name: 'Short rest…' });
  await user.click(short);
  expect(screen.getByRole('region', { name: 'Short rest' })).toBeTruthy();
  await user.click(short);
  expect(screen.queryByRole('region', { name: 'Short rest' })).toBeNull();
  expect(short.getAttribute('aria-expanded')).toBe('false');
  expect(document.activeElement).toBe(short);
});

async function startFinish(user: ReturnType<typeof userEvent.setup>, onStatus: () => void) {
  const v = view();
  let resolveRest: (value: CharacterView) => void = () => {};
  vi.mocked(client.rest).mockReturnValue(new Promise<CharacterView>((r) => (resolveRest = r)));
  render(<CharacterSheet view={v} onChanged={noop} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  const short = screen.getByRole('button', { name: 'Short rest…' });
  await user.click(short);
  const finish = await screen.findByRole('button', { name: 'Finish short rest' });
  await waitFor(() => expect((finish as HTMLButtonElement).disabled).toBe(false));
  await user.click(finish);
  return { short, resolve: () => resolveRest(v) };
}

it('a late finish of a closed rest neither closes the open rest nor moves focus (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  const { short, resolve } = await startFinish(user, onStatus);
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const longPanel = screen.getByRole('region', { name: 'Long rest' });
  await waitFor(() => expect(document.activeElement).toBe(within(longPanel).getByRole('heading', { name: 'Long rest' })));
  resolve();
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('Short rest finished: 0 changes applied.'));
  expect(screen.getByRole('region', { name: 'Long rest' })).toBeTruthy();
  expect(document.activeElement).not.toBe(short);
});

it('a late finish after Cancel does not steal focus (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  const { short, resolve } = await startFinish(user, onStatus);
  await user.click(screen.getByRole('button', { name: 'Cancel rest' }));
  expect(document.activeElement).toBe(short);
  const long = screen.getByRole('button', { name: 'Long rest…' });
  long.focus();
  resolve();
  await waitFor(() => expect(onStatus).toHaveBeenCalled());
  expect(document.activeElement).toBe(long);
});

it('fetches the rest proposal again when the character changes under an open rest (D29)', async () => {
  const user = userEvent.setup();
  const v = view();
  const { rerender } = renderSheet(v);
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  await waitFor(() => expect(client.restPreview).toHaveBeenCalledTimes(1));
  vi.mocked(client.restPreview).mockClear();
  rerender(sheetElement({ ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 7, temporary: 0 } } }));
  await waitFor(() => expect(client.restPreview).toHaveBeenCalledTimes(1));
});

it('returns focus to the opener and reports the result when a rest finishes (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  const v = view();
  const onChanged = vi.fn();
  const onStatus = vi.fn();
  vi.mocked(client.restPreview).mockResolvedValue({
    kind: 'longRest',
    changes: [{ id: 'hp', kind: 'hitPoints', label: 'Hit points', from: 3, to: 8, reason: 'Fixture reason' }] as unknown as Awaited<ReturnType<typeof client.restPreview>>['changes'],
    manual: [],
    basis: 'fixture',
  });
  vi.mocked(client.rest).mockResolvedValue(v);
  render(<CharacterSheet view={v} onChanged={onChanged} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  const long = screen.getByRole('button', { name: 'Long rest…' });
  await user.click(long);
  const finish = await screen.findByRole('button', { name: 'Finish long rest' });
  await waitFor(() => expect((finish as HTMLButtonElement).disabled).toBe(false));
  await user.click(finish);
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('Long rest finished: 1 change applied.'));
  expect(client.rest).toHaveBeenCalledWith('fixture-2', 'longRest', 'fixture', [], []);
  expect(onChanged).toHaveBeenCalledWith(v);
  expect(screen.queryByRole('region', { name: 'Long rest' })).toBeNull();
  expect(document.activeElement).toBe(long);
});

const rollRecord = (label: string, total: number, sides = 20): RollRecord => ({
  formula: `1d${sides}`, mode: 'normal', critical: false, dice: [{ term: 0, sides, value: total, kept: true, fromCritical: false }],
  diceTotal: total, expressionConstant: 0, modifiers: [], total, provenance: { rollId: 'fixture', label },
});

it('moves the replaced Last roll into Previous rolls (D28)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.roll).mockReset();
  vi.mocked(client.roll).mockResolvedValueOnce(rollRecord('Dexterity check', 9)).mockResolvedValueOnce(rollRecord('Dexterity check', 15));
  renderSheet(view());
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  expect(screen.queryByText(/^Previous rolls/)).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  const log = await screen.findByText('Previous rolls (1)');
  expect(within(screen.getByRole('region', { name: 'Last roll' })).getByText(/Dexterity check: 15/)).toBeTruthy();
  expect(log.closest('details')!.textContent).toMatch(/Dexterity check: 9/);
});

it('logs a rest hit die without showing it, and keeps the log newest first by time when the Last roll is replaced (D28)', async () => {
  const user = userEvent.setup();
  let clock = 1_760_000_000_000;
  vi.spyOn(Date, 'now').mockImplementation(() => (clock += 60_000));
  vi.mocked(client.roll).mockReset();
  vi.mocked(client.roll)
    .mockResolvedValueOnce(rollRecord('Roll A', 9))
    .mockResolvedValueOnce(rollRecord('Hit die H', 5, 8))
    .mockResolvedValueOnce(rollRecord('Roll B', 15));
  vi.mocked(client.restPreview).mockResolvedValue({ kind: 'shortRest', changes: [], manual: [], basis: 'fixture' });
  renderSheet(view({ hitDice: [{ die: 8, total: 2, spent: 0, remaining: 2, classes: ['Fixture'] }] }));
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  await user.click(await screen.findByRole('button', { name: 'Roll a d8' }));
  const lastRoll = screen.getByRole('region', { name: 'Last roll' });
  expect(within(lastRoll).getByText(/Roll A: 9/)).toBeTruthy();
  expect(within(lastRoll).queryByText(/Hit die H/)).toBeNull();
  expect((await screen.findByText('Previous rolls (1)')).closest('details')!.textContent).toMatch(/Hit die H: 5/);
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  const log = (await screen.findByText('Previous rolls (2)')).closest('details')!;
  expect(within(lastRoll).getByText(/Roll B: 15/)).toBeTruthy();
  const items = within(log).getAllByRole('listitem').map((li) => li.textContent ?? '');
  expect(items[0]).toMatch(/Hit die H: 5/);
  expect(items[1]).toMatch(/Roll A: 9/);
  vi.mocked(Date.now).mockRestore();
});
