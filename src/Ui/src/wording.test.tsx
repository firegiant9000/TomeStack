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
import type { AppInfo, Campaign, Character, CharacterView, CandidateCheck, DerivedValue, DraftCandidate, ImportJob, RulesFamilyPolicy, SourceRecord, StoredCandidate, StudioEntry } from './api/types';
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
    checkCandidate: vi.fn(),
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
  classes: [{ class: { contentId: 'fixture-class', revisionId: 'fixture-class-r1' }, level: 1 }],
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
    features: [
      {
        content: { contentId: 'fixture-class', revisionId: 'fixture-class-r1' },
        name: 'Fixture Scout',
        kind: 'class',
        origin: { kind: 'content', rulesFamily: 'srd-5.1' },
        automation: 'assisted',
        diagnostics: [],
        effects: [{ id: 'scout-text', type: 'text', automation: 'reference', text: 'Fixture text for the scout.', available: true }],
      },
    ],
  },
};

const homebrewSource = { id: 'fixture-notebook', title: 'Fixture Notebook', publisher: 'Fixture Press', rulesFamilies: ['srd-5.1', 'srd-5.2.1'], editionVersion: 'homebrew', license: 'own work', redistributable: true, origin: 'local', shareConfirmedAt: '2026-10-01T00:00:00Z' } as SourceRecord;
const bookSource = { id: 'fixture-tome', title: 'Fixture Tome', publisher: 'Fixture Press', rulesFamilies: ['srd-5.1'], editionVersion: '1st', license: 'own copy', redistributable: false, origin: 'local' } as SourceRecord;
const revision = (status: 'draft' | 'published', revisionId: string, contentId = `fixture-${status}`) => ({ contentId, revisionId, name: `Fixture ${status}`, kind: 'feat', status, rulesFamilies: ['srd-5.1'], provenance: { sourceId: 'fixture-notebook' }, effects: [] });
const draftEntry = { contentId: 'fixture-draft', name: 'Fixture draft', kind: 'feat', revisions: [revision('draft', 'r1')], latest: revision('draft', 'r1') } as unknown as StudioEntry;
const publishedEntry = { contentId: 'fixture-published', name: 'Fixture published', kind: 'feat', revisions: [revision('published', 'r1'), revision('draft', 'r2', 'fixture-published')], latest: revision('draft', 'r2', 'fixture-published'), latestPublished: revision('published', 'r1') } as unknown as StudioEntry;
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
  const found = (n: number, name: string): StoredCandidate => ({
    id: `fixture-cand-${n}`,
    jobId: 'job-a',
    status: 'pending',
    etag: `etag-${n}`,
    updatedAt: '2026-10-14T00:00:00Z',
    candidate: {
      id: `fixture-cand-${n}`,
      sourceId: 'fixture-notebook',
      page: { start: n },
      excerpt: `${name} 11`,
      proposedKind: 'item',
      proposedName: name,
      rulesFamilies: ['srd-5.1'],
      proposedEffects: [],
      confidence: 0.85,
      uncertainties: [],
      unresolvedReferences: [],
      fields: {},
      lowConfidenceFields: [],
    } as DraftCandidate,
  });
  vi.mocked(client.candidates).mockResolvedValue({ items: [found(1, 'Fixture Coat'), found(2, 'Fixture Lantern')], total: 2 });
  vi.mocked(client.checkCandidate).mockResolvedValue({
    candidateId: 'fixture-cand-1',
    report: { errors: [], warnings: [], canPublish: true } as unknown as CandidateCheck['report'],
    dependencies: [],
    blockers: [],
    canAccept: true,
    canAcceptAsReference: true,
  });
  vi.mocked(client.sourcePackPreview).mockResolvedValue({ fileName: 'fixture-notebook.tomestack.zip', sources: [{ sourceId: 'fixture-notebook', title: 'Fixture Notebook', publisher: 'Fixture Press', license: 'own work', redistributable: true }], revisions: 2, drafts: 1, warnings: [] });
  vi.mocked(client.campaignPackPreview).mockResolvedValue({
    fileName: 'fixture-table.tomestack-campaign.zip',
    campaignId: 'fixture-table',
    name: 'Fixture Table',
    rulesFamily: 'srd-5.2.1',
    included: [{ sourceId: 'fixture-notebook', title: 'Fixture Notebook', publisher: 'Fixture Press', license: 'own work', redistributable: true }],
    referenced: [{ sourceId: 'fixture-srd', title: 'Fixture Reference Rules', publisher: 'Fixture Press', license: 'open license', redistributable: true }],
    leftOut: [{ sourceId: 'fixture-tome', title: 'Fixture Tome', publisher: 'Fixture Press', license: 'own copy', reason: { code: 'preflight.not-redistributable', message: 'not marked as your own shareable work' } }],
    revisions: 2,
    warnings: [],
  });
  const reference = { contentId: 'fixture-draft', revisionId: 'r1' };
  const checked = { errors: [], warnings: [], canPublish: true } as unknown as CandidateCheck['report'];
  vi.mocked(client.publishManyPreview).mockResolvedValue({
    entries: [{ draft: reference, name: 'Fixture draft', kind: 'feat', report: checked, findings: [], replacesPublished: false, rePointings: [{ dependent: { contentId: 'fixture-other', revisionId: 'r1' }, dependentName: 'Fixture other', grantsMoved: 1, optionsMoved: 0 }] }],
    dependents: [],
    canPublish: true,
  });
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
  // One element only this tab shows, so the scan never runs before the tab's own content has landed.
  const specific: Record<SheetTabId, () => Promise<unknown>> = {
    play: () => screen.findByRole('checkbox', { name: /Compact view/ }),
    spells: () => screen.findByRole('heading', { name: 'Spellcasting' }),
    inventory: () => screen.findByRole('heading', { name: 'Equipment' }),
    features: async () => {
      await screen.findByRole('heading', { name: 'Things to remember yourself' });
      return screen.findAllByText('Fixture Scout');
    },
    stats: async () => {
      await screen.findByRole('heading', { name: 'Abilities' });
      return screen.findAllByText(/set by hand \(calculated/);
    },
    notes: () => screen.findByRole('heading', { name: 'Session notes' }),
    manage: () => screen.findByRole('heading', { name: 'Export' }),
  };
  expect(await specific[tab]()).toBeTruthy();
  expectPlainWords(container);
});

it('character sheet, manage tab, sharing the character', async () => {
  const user = userEvent.setup();
  vi.mocked(client.previewExport).mockResolvedValue({
    purpose: 'share',
    fileName: 'fixture-wren.tomestack.zip',
    characters: ['fixture-wording'],
    included: [],
    omitted: [{ sourceId: 'fixture-tome', title: 'Fixture Tome', publisher: 'Fixture Press', license: 'own copy', revisions: [{ reference: { contentId: 'fixture-boots', revisionId: 'r1' }, name: 'Fixture Boots', characters: ['fixture-wording'] }] }],
    gapNotes: 0,
  });
  vi.mocked(client.packagePreflight).mockResolvedValue({
    included: [{ contentId: 'fixture-lamp', revisionId: 'r1', name: 'Fixture Lamp', sourceName: 'Fixture Notebook', redistributable: true, origin: 'local' }],
    omitted: [{ contentId: 'fixture-boots', revisionId: 'r1', name: 'Fixture Boots', sourceName: 'Fixture Tome', redistributable: false, origin: 'local', reason: 'preflight.not-redistributable' }],
    blocking: [],
    notices: ['Fixture Notebook, Fixture Press, own work'],
  });
  const { container } = render(labelled(<CharacterSheet view={view} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} initialTab="manage" rulesFamilies={families} />));
  await user.click(await screen.findByRole('radio', { name: /^Share with someone/ }));
  expect(await screen.findByRole('region', { name: 'Left out of the shared TomeStack file' })).toBeTruthy();
  expect(await screen.findByRole('region', { name: 'Preflight: shared TomeStack file' })).toBeTruthy();
  await screen.findByText(/Left out: 1\./);
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
  await user.click(await screen.findByRole('button', { name: 'Fixture Coat' }));
  expect(await screen.findByRole('button', { name: 'Keep, with its rules' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Keep as text only' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Fixture Lantern' })).toBeTruthy();
  expectPlainWords(container);
  // Sharing the notebook as a TomeStack file: the preview, with its preflight.
  await user.click(screen.getByRole('checkbox', { name: 'Fixture Notebook' }));
  await user.click(screen.getByRole('button', { name: 'Preview file' }));
  expect(await screen.findByRole('region', { name: 'TomeStack file preview' })).toBeTruthy();
  await screen.findByText(/2 published versions/);
  expect(await screen.findByRole('region', { name: 'Preflight: TomeStack file' })).toBeTruthy();
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
  // The published entry has two versions, so its editor holds the compare panel.
  await user.click(screen.getByRole('button', { name: 'Edit Fixture published' }));
  expect((await screen.findAllByRole('option', { name: /^Version 1 \(published/ })).length).toBeGreaterThan(0);
  expect(screen.getByRole('heading', { name: 'Compare versions' })).toBeTruthy();
  expect(screen.getAllByRole('option', { name: 'The version on screen (unsaved)' }).length).toBeGreaterThan(0);
  expectPlainWords(container);
  // Making every draft available: the bulk panel with its preview.
  await user.click(screen.getByRole('button', { name: 'Make all drafts in this source available to characters…' }));
  expect(await screen.findByRole('heading', { name: 'Make drafts of Fixture Notebook available to characters' })).toBeTruthy();
  await waitFor(() => expect(client.publishManyPreview).toHaveBeenCalled());
  await screen.findByText(/No problems; 1 draft ready\./, undefined, { timeout: 3000 });
  expectPlainWords(container);
});

it('Campaigns, with the share form', async () => {
  const user = userEvent.setup();
  const { container } = render(labelled(<CampaignsPanel rulesFamilies={families} onError={noop} onStatus={noop} />));
  expect(await screen.findByRole('button', { name: 'Edit Fixture Table' })).toBeTruthy();
  const share = await screen.findByRole('button', { name: 'Share Fixture Table…' });
  expectPlainWords(container);
  await user.click(share);
  expect(await screen.findByRole('region', { name: 'Left out of the TomeStack file' })).toBeTruthy();
  await screen.findByText(/carried with/);
  expect(await screen.findByRole('region', { name: 'Preflight: TomeStack file' })).toBeTruthy();
  expectPlainWords(container);
});
