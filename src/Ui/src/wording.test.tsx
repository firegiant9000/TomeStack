// @vitest-environment jsdom
// Wave 3 acceptance 5: the words a player reads. Each main screen is rendered inside RulesLabelProvider with a mocked client,
// and its text, aria-labels and titles must hold none of the internal words (ids, "candidates", "revisions", "overrides",
// "packages", "rules family"). Fixture names are invented.
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from './App';
import { client } from './api/client';
import type { AppInfo, Campaign, Character, CharacterView, DerivedValue, ImportJob, RulesFamilyPolicy, SourceRecord, StudioEntry } from './api/types';
import { CampaignsPanel } from './components/CampaignsPanel';
import { CharacterBuilder } from './components/CharacterBuilder';
import { CharacterSheet } from './components/CharacterSheet';
import { HomebrewStudio } from './components/HomebrewStudio';
import { SourcesPanel } from './components/SourcesPanel';
import { RulesLabelProvider } from './rulesLabel';
import { sheetTabIds, type SheetTabId } from './sheetTab';

vi.mock('./api/client', () => ({
  client: {
    info: vi.fn(),
    listCharacters: vi.fn(),
    getCharacter: vi.fn(),
    attachment: vi.fn(),
    listGapNotes: vi.fn(),
    availableUpdates: vi.fn(),
    listContent: vi.fn(),
    draftCounts: vi.fn(),
    listCampaigns: vi.fn(),
    snapshots: vi.fn(),
    previewExport: vi.fn(),
    packagePreflight: vi.fn(),
    packageTargets: vi.fn(),
    restPreview: vi.fn(),
    listSources: vi.fn(),
    listImports: vi.fn(),
    candidates: vi.fn(),
    contentBySource: vi.fn(),
    contentDuplicates: vi.fn(),
    feedback: vi.fn(),
    previewFormula: vi.fn(),
    validateRevision: vi.fn(),
    publishManyPreview: vi.fn(),
    sourcePackPreview: vi.fn(),
    campaignPackPreview: vi.fn(),
  },
}));

const patterns = [/srd-5\./i, /\bcandidates?\b/i, /\brevisions?\b/i, /\boverrides?\b/i, /\bpackages?\b/i, /rules family/i];

/** The text a player reads, plus the aria-label and title values a screen reader reads. */
function readable(container: HTMLElement): string[] {
  const attributes = Array.from(container.querySelectorAll('[aria-label], [title]')).flatMap((el) => [el.getAttribute('aria-label'), el.getAttribute('title')]);
  return [container.textContent ?? '', ...attributes.filter((v): v is string => !!v)];
}
function expectPlainWords(container: HTMLElement) {
  for (const text of readable(container)) for (const pattern of patterns) expect(text, `${pattern} in: ${text.slice(0, 200)}`).not.toMatch(pattern);
}

const families = [
  { id: 'srd-5.1', displayName: 'SRD 5.1 (2014 rules)', shortLabel: '2014 rules' },
  { id: 'srd-5.2.1', displayName: 'SRD 5.2.1 (2024 rules)', shortLabel: '2024 rules' },
] as unknown as RulesFamilyPolicy[];
const info = { version: 'test', schemaVersion: 1, rulesFamilies: families, warnings: [], fields: [] } as unknown as AppInfo;
const labelled = (ui: ReactElement) => <RulesLabelProvider families={families}>{ui}</RulesLabelProvider>;
const noop = () => {};

const field = (id: string, label: string, value: number, units: string): DerivedValue => ({ field: id, label, value, computedValue: value, trace: [], warnings: [], automation: 'automatic', units });
const character: Character = {
  id: 'fixture-wording',
  schemaVersion: 7,
  name: 'Fixture Wren',
  rulesFamily: 'srd-5.1',
  level: 1,
  classes: [],
  choices: [],
  crossFamilyExceptions: [],
  baseAbilities: { str: 10, dex: 12, con: 10, int: 14, wis: 10, cha: 10 },
  pins: [],
  overrides: [{ field: 'armorClass', value: 15 }, { field: 'spellAttack', value: 3 }],
  updatedAt: '2026-10-03T00:00:00Z',
};
const view: CharacterView = {
  character,
  sheet: {
    characterId: 'fixture-wording',
    rulesFamily: 'srd-5.1',
    diagnostics: [],
    fields: [
      field('ability.dex.score', 'Dexterity score', 12, 'score'),
      field('ability.dex.mod', 'Dexterity modifier', 1, 'modifier'),
      { ...field('armorClass', 'Armor Class', 15, 'score'), computedValue: 11, override: { field: 'armorClass', value: 15 } },
      field('initiative', 'Initiative', 1, 'modifier'),
      field('proficiencyBonus', 'Proficiency bonus', 2, 'bonus'),
      { ...field('spellAttack', 'Spell attack bonus', 3, 'modifier'), computedValue: 0, override: { field: 'spellAttack', value: 3 } },
    ],
    hitPoints: { maximum: 8, current: 8, temporary: 0 },
  },
};

const homebrewSource = { id: 'fixture-notebook', title: 'Fixture Notebook', publisher: 'Fixture Press', rulesFamilies: ['srd-5.1', 'srd-5.2.1'], editionVersion: 'homebrew', license: 'own work', redistributable: true, origin: 'local' } as SourceRecord;
const bookSource = { id: 'fixture-tome', title: 'Fixture Tome', publisher: 'Fixture Press', rulesFamilies: ['srd-5.1'], editionVersion: '1st', license: 'own copy', redistributable: false, origin: 'local' } as SourceRecord;
const revision = (status: 'draft' | 'published', revisionId: string) => ({ contentId: `fixture-${status}`, revisionId, name: `Fixture ${status}`, kind: 'feat', status, rulesFamilies: ['srd-5.1'], provenance: { sourceId: 'fixture-notebook' }, effects: [] });
const draftEntry = { contentId: 'fixture-draft', name: 'Fixture draft', kind: 'feat', revisions: [revision('draft', 'r1')], latest: revision('draft', 'r1') } as unknown as StudioEntry;
const publishedEntry = { contentId: 'fixture-published', name: 'Fixture published', kind: 'feat', revisions: [revision('published', 'r1'), revision('draft', 'r2')], latest: revision('draft', 'r2'), latestPublished: revision('published', 'r1') } as unknown as StudioEntry;
const job = { id: 'job-a', sourceId: 'fixture-notebook', firstPage: 1, wholeDocument: true, status: 'completed', pagesDone: 6, pagesFailed: 0, pagesFromOcr: 0, pagesWithoutText: 0, candidates: 3 } as ImportJob;
const campaign: Campaign = { id: 'fixture-table', name: 'Fixture Table', rulesFamily: 'srd-5.2.1', allowedSources: ['fixture-notebook'] };

beforeEach(() => {
  vi.mocked(client.info).mockResolvedValue(info);
  vi.mocked(client.listCharacters).mockResolvedValue([{ id: 'fixture-wording', name: 'Fixture Wren', rulesFamily: 'srd-5.1', updatedAt: '2026-10-03T00:00:00Z', level: 1 }]);
  vi.mocked(client.getCharacter).mockResolvedValue(view);
  vi.mocked(client.attachment).mockResolvedValue(undefined as unknown as Awaited<ReturnType<typeof client.attachment>>);
  vi.mocked(client.listGapNotes).mockResolvedValue([]);
  vi.mocked(client.availableUpdates).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.draftCounts).mockResolvedValue({});
  vi.mocked(client.listCampaigns).mockResolvedValue([campaign]);
  vi.mocked(client.snapshots).mockResolvedValue({ items: [], hasMore: false });
  vi.mocked(client.restPreview).mockResolvedValue({ kind: 'shortRest', changes: [], manual: [], basis: 'fixture' });
  vi.mocked(client.packageTargets).mockRejectedValue(new Error('not offered'));
  vi.mocked(client.packagePreflight).mockResolvedValue({ included: [], omitted: [], blocking: [], notices: [] });
  vi.mocked(client.listSources).mockResolvedValue([homebrewSource, bookSource]);
  vi.mocked(client.listImports).mockImplementation(async (id) => (id === 'fixture-notebook' ? [job] : []));
  vi.mocked(client.candidates).mockResolvedValue({ items: [], total: 0 });
  vi.mocked(client.contentBySource).mockImplementation(async (id) => (id === 'fixture-notebook' ? [draftEntry, publishedEntry] : []));
  vi.mocked(client.contentDuplicates).mockResolvedValue([]);
  vi.mocked(client.feedback).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: '3', points: [] });
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'fixture-draft', revisionId: 'r1' }, errors: [], warnings: [], canPublish: true });
});
afterEach(() => {
  cleanup();
  localStorage.clear();
});

it('home screen', async () => {
  const { container } = render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Characters' });
  await waitFor(() => expect(list.textContent).toContain('2014 rules'));
  expectPlainWords(container);
});

it.each(sheetTabIds)('character sheet, %s tab', async (tab: SheetTabId) => {
  const { container } = render(labelled(<CharacterSheet view={view} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} initialTab={tab} rulesFamilies={families} />));
  expect(await screen.findByRole('heading', { name: 'Fixture Wren' })).toBeTruthy();
  const selected = screen.getByRole('tab', { selected: true });
  expect(selected.textContent?.length).toBeGreaterThan(0);
  expect(container.querySelector('[role="tabpanel"]')?.textContent?.length).toBeGreaterThan(0);
  await new Promise((r) => setTimeout(r, 20)); // let a tab's own fetches land
  expectPlainWords(container);
});

it('builder step 1', async () => {
  const { container } = render(labelled(<CharacterBuilder mode={{ kind: 'create' }} rulesFamilies={families} onCommitted={noop} onCancel={noop} onError={noop} />));
  expect(await screen.findByRole('textbox', { name: 'Name' })).toBeTruthy();
  const campaigns = await screen.findByRole('combobox', { name: 'Campaign' });
  await waitFor(() => expect(campaigns.textContent).toContain('Fixture Table'));
  expectPlainWords(container);
});

it('Books and PDFs, with a book import to review', async () => {
  const user = userEvent.setup();
  const { container } = render(labelled(<SourcesPanel onError={noop} onStatus={noop} onOpenStudio={noop} />));
  expect(await screen.findByRole('listitem', { name: 'Fixture Tome' })).toBeTruthy();
  expect(await screen.findByRole('button', { name: 'Review 3 entries found' })).toBeTruthy();
  expectPlainWords(container);
  await user.click(screen.getByRole('button', { name: 'Review 3 entries found' }));
  expect(await screen.findByRole('region', { name: 'Entries found in Fixture Notebook' })).toBeTruthy();
  expectPlainWords(container);
});

it('Homebrew, with a draft and a published entry', async () => {
  const user = userEvent.setup();
  const { container } = render(labelled(<HomebrewStudio info={info} onError={noop} onStatus={noop} />));
  expect(await screen.findByRole('combobox', { name: 'Book or notebook' })).toBeTruthy();
  const draftEdit = await screen.findByRole('button', { name: 'Edit Fixture draft' });
  expect(await screen.findByRole('button', { name: 'Edit Fixture published' })).toBeTruthy();
  expectPlainWords(container);
  await user.click(draftEdit);
  await screen.findByRole('group', { name: 'Rules' });
  expectPlainWords(container);
});

it('Campaigns', async () => {
  const { container } = render(labelled(<CampaignsPanel rulesFamilies={families} onError={noop} onStatus={noop} />));
  expect(await screen.findByRole('button', { name: 'Edit Fixture Table' })).toBeTruthy();
  expect(await screen.findByRole('button', { name: 'Share Fixture Table…' })).toBeTruthy();
  expectPlainWords(container);
});
