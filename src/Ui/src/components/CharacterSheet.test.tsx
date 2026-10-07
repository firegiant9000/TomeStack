// @vitest-environment jsdom
// The sheet's tabs (ADR-014): Spells only when there is something to show, a tab that is not offered falls back to Play,
// "Report a gap" switches to Notes and focuses the note text every time it is pressed, and choosing Notes by hand keeps
// focus on the tab. The client is mocked; values are invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterSheet as SheetModel, CharacterView, DerivedValue, FeatureEntry } from '../api/types';
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
  },
}));

beforeEach(() => {
  vi.mocked(client.listGapNotes).mockResolvedValue([]);
  vi.mocked(client.availableUpdates).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.snapshots).mockResolvedValue({ items: [], hasMore: false });
});
afterEach(() => {
  cleanup();
  localStorage.clear(); // the remembered tab is per character id, and these tests share one
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
  expect(print.getAttribute('aria-controls')).toBe('print-preview');
  await user.click(print);
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
