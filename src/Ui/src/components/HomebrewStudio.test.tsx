// @vitest-environment jsdom
// Item 2: the studio can open on a given source (from the import review), and then names itself by taking focus.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord, StudioEntry } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';

vi.mock('../api/client', () => ({
  client: { listSources: vi.fn(), contentBySource: vi.fn(), publishManyPreview: vi.fn(), createHomebrewSource: vi.fn(), previewFormula: vi.fn(), listCharacters: vi.fn(), validateRevision: vi.fn() },
}));

const info = { rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1' }] } as unknown as AppInfo;
const homebrew = (id: string, title: string) => ({ id, title, rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' }) as SourceRecord;

beforeEach(() => {
  vi.mocked(client.listSources).mockResolvedValue([homebrew('a', 'Testy Alpha'), homebrew('b', 'Testy Beta')]);
  vi.mocked(client.contentBySource).mockResolvedValue([]);
});
afterEach(cleanup);

it('opens on the source it was asked for, and takes focus on its heading', async () => {
  render(<HomebrewStudio info={info} initialSourceId="b" onError={() => {}} onStatus={() => {}} />);
  const select = await screen.findByRole<HTMLSelectElement>('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(select.selectedOptions[0]?.textContent).toMatch(/^Testy Beta/));
  expect(document.activeElement).toBe(screen.getByRole('heading', { level: 2, name: 'Homebrew studio' }));
});

it('falls back to the first source when the asked-for one is not there', async () => {
  render(<HomebrewStudio info={info} initialSourceId="gone" onError={() => {}} onStatus={() => {}} />);
  const select = await screen.findByRole<HTMLSelectElement>('combobox', { name: 'Homebrew source' });
  expect(select.selectedOptions[0]?.textContent).toMatch(/^Testy Alpha/);
});

it('leaves focus alone when opened from the sidebar (the sidebar button keeps it)', async () => {
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await screen.findByRole('combobox', { name: 'Homebrew source' });
  expect(document.activeElement).toBe(document.body);
});

// Item 9: "Publish all drafts in this source…" opens the named region with the pending drafts of the source.
it('offers Publish all drafts only when a draft is pending, and opens the region named for the source', async () => {
  const user = userEvent.setup();
  const revision = (status: 'draft' | 'published', revisionId: string) => ({ contentId: 'c1', revisionId, status, effects: [] }) as unknown as StudioEntry['latest'];
  const entry = (name: string, status: 'draft' | 'published'): StudioEntry => ({
    contentId: `id-${name}`,
    name,
    kind: 'feat',
    revisions: [],
    latest: { ...revision(status, `r-${name}`), contentId: `id-${name}` },
  });
  vi.mocked(client.contentBySource).mockResolvedValue([entry('Testy Done', 'published')]);
  const view = render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await screen.findByText('Testy Done');
  expect(screen.queryByRole('button', { name: 'Publish all drafts in this source…' })).toBeNull();
  view.unmount();

  vi.mocked(client.contentBySource).mockResolvedValue([entry('Testy Done', 'published'), entry('Testy Waiting', 'draft')]);
  vi.mocked(client.publishManyPreview).mockResolvedValue({ entries: [], dependents: [], canPublish: true });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Publish all drafts in this source…' }));
  const region = screen.getByRole('region', { name: 'Publish drafts of Testy Alpha' });
  expect(document.activeElement).toBe(within(region).getByRole('heading', { name: 'Publish drafts of Testy Alpha' }));
  expect(within(region).getByRole('checkbox', { name: 'Publish Testy Waiting' })).toHaveProperty('checked', true);
  expect(within(region).queryByRole('checkbox', { name: 'Publish Testy Done' })).toBeNull();
});

// Fix round 1, item 13: another source never shows the open panel or the old source's rows.
it('closes the publish panel and drops the old rows when the source changes', async () => {
  const user = userEvent.setup();
  const draft = { contentId: 'id-w', name: 'Testy Waiting', kind: 'feat', revisions: [], latest: { contentId: 'id-w', revisionId: 'r-w', status: 'draft', effects: [] } } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [draft] : []));
  vi.mocked(client.publishManyPreview).mockResolvedValue({ entries: [], dependents: [], canPublish: true });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Publish all drafts in this source…' }));
  expect(screen.getByRole('region', { name: 'Publish drafts of Testy Alpha' })).toBeTruthy();

  // Hold the new source's rows back: until they arrive the old ones must not be shown.
  let release: (rows: StudioEntry[]) => void = () => {};
  vi.mocked(client.contentBySource).mockImplementation((id) => (id === 'b' ? new Promise((resolve) => (release = resolve)) : Promise.resolve([draft])));
  await user.selectOptions(screen.getByRole('combobox', { name: 'Homebrew source' }), 'b');
  expect(screen.queryByRole('region', { name: /^Publish drafts of/ })).toBeNull();
  expect(screen.queryByText('Testy Waiting')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Publish all drafts in this source…' })).toBeNull();
  release([]);
});

// Fix round 2, items 2 and 6.
const draftEntry = { contentId: 'id-w', name: 'Testy Waiting', kind: 'feat', revisions: [], latest: { contentId: 'id-w', revisionId: 'r-w', status: 'draft', effects: [] } } as unknown as StudioEntry;

it('creating a new source closes the open publish panel and drops the old rows', async () => {
  const user = userEvent.setup();
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [draftEntry] : []));
  vi.mocked(client.publishManyPreview).mockResolvedValue({ entries: [], dependents: [], canPublish: true });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Publish all drafts in this source…' }));
  expect(screen.getByRole('region', { name: 'Publish drafts of Testy Alpha' })).toBeTruthy();

  // The new source's rows are slow: until they arrive the old ones must not be shown.
  let release: (rows: StudioEntry[]) => void = () => {};
  vi.mocked(client.contentBySource).mockImplementation((id) => (id === 'c' ? new Promise((resolve) => (release = resolve)) : Promise.resolve([draftEntry])));
  vi.mocked(client.createHomebrewSource).mockResolvedValue(homebrew('c', 'Testy Gamma'));
  vi.mocked(client.listSources).mockResolvedValue([homebrew('a', 'Testy Alpha'), homebrew('b', 'Testy Beta'), homebrew('c', 'Testy Gamma')]);
  await user.type(screen.getByLabelText('Source title'), 'Testy Gamma');
  await user.click(screen.getByRole('checkbox', { name: 'SRD 5.1' }));
  await user.click(screen.getByRole('button', { name: 'Create source' }));

  await waitFor(() => expect(client.contentBySource).toHaveBeenCalledWith('c'));
  expect(screen.queryByRole('region', { name: /^Publish drafts of/ })).toBeNull();
  expect(screen.queryByText('Testy Waiting')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Publish all drafts in this source…' })).toBeNull();
  release([]);
});

it('ignores a slow reload of the old source that lands after a switch', async () => {
  const user = userEvent.setup();
  let releaseA: (rows: StudioEntry[]) => void = () => {};
  vi.mocked(client.contentBySource).mockImplementation((id) => (id === 'a' ? new Promise((resolve) => (releaseA = resolve)) : Promise.resolve([])));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await waitFor(() => expect(client.contentBySource).toHaveBeenCalledWith('a'));
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Homebrew source' }), 'b');
  await waitFor(() => expect(client.contentBySource).toHaveBeenCalledWith('b'));

  releaseA([draftEntry]); // A answers last
  await new Promise((resolve) => setTimeout(resolve, 50));

  expect(screen.queryByText('Testy Waiting')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Publish all drafts in this source…' })).toBeNull();
});

it('choosing the source that is already shown keeps its rows', async () => {
  const user = userEvent.setup();
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [draftEntry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await screen.findByText('Testy Waiting');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Homebrew source' }), 'a');
  expect(screen.getByText('Testy Waiting')).toBeTruthy();
});

// D61: every formula input shows its preview and is described by it.
it('a_resource_maximum_shows_its_preview_and_is_described_by_it', async () => {
  const user = userEvent.setup();
  const resource = { type: 'resource', id: 'resource-1', resourceId: 'resource-1', label: 'Testy Pool', maximum: '1' };
  const entry = {
    contentId: 'id-r',
    name: 'Testy Resource',
    kind: 'feat',
    revisions: [],
    latest: { contentId: 'id-r', revisionId: 'r-r', name: 'Testy Resource', kind: 'feat', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects: [resource] },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: 'PB', points: [{ level: 1, value: 2 }, { level: 20, value: 6 }] });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Resource' }));
  const uses = await screen.findByLabelText(/^Uses \(a number or a formula/);
  await user.clear(uses);
  await user.type(uses, 'PB');

  expect(await screen.findByText('Level 1: 2 · Level 20: 6')).toBeTruthy();
  expect(uses.getAttribute('aria-describedby')).toBe('rule-resource-1-maximum-preview');
  expect(document.getElementById('rule-resource-1-maximum-preview')?.textContent).toContain('Level 20: 6');
  expect(vi.mocked(client.previewFormula).mock.calls.at(-1)?.[0]).toBe('PB');
  expect(vi.mocked(client.previewFormula).mock.calls.at(-1)?.[2]).toBe(true); // Uses is floored at 0, as on the sheet
});

// D61 review: only the scale columns are sent, and only a scale edit asks again.
it('an_edit_outside_the_scale_columns_does_not_ask_for_the_previews_again', async () => {
  const user = userEvent.setup();
  const resource = { type: 'resource', id: 'resource-1', resourceId: 'resource-1', label: 'Testy Pool', maximum: 'PB' };
  const entry = {
    contentId: 'id-r',
    name: 'Testy Resource',
    kind: 'feat',
    revisions: [],
    latest: { contentId: 'id-r', revisionId: 'r-r', name: 'Testy Resource', kind: 'feat', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects: [resource] },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockReset();
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: 'PB', points: [{ level: 1, value: 2 }] });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Resource' }));
  await screen.findByText('Level 1: 2');
  const asked = vi.mocked(client.previewFormula).mock.calls.length;
  expect(vi.mocked(client.previewFormula).mock.calls.at(-1)?.[1]?.effects).toEqual([]); // the resource rule is not sent

  await user.type(screen.getByLabelText('Name'), ' renamed');
  await new Promise((resolve) => setTimeout(resolve, 500));
  expect(vi.mocked(client.previewFormula).mock.calls.length).toBe(asked);
});

// D43 review: what only means something with a pool or a group goes when the pool or group does.
async function openFormDraft(user: ReturnType<typeof userEvent.setup>) {
  const effects = [
    { type: 'resource', id: 'resource-1', resourceId: 'focus', label: 'Testy Focus', maximum: '5' },
    { type: 'toggleGroup', id: 'toggleGroup-1', groupId: 'one', label: 'Testy One' },
    { type: 'toggleGroup', id: 'toggleGroup-2', groupId: 'two', label: 'Testy Two' },
    { type: 'toggle', id: 'toggle-1', toggleId: 'form', label: 'Testy Form', group: 'one', resourceId: 'focus', cost: '3', resourceContent: 'other-content' },
    { type: 'toggle', id: 'toggle-2', toggleId: 'glow', label: 'Testy Glow', group: 'one', layer: 'overlay', allowedWith: [{ toggleId: 'form' }] },
  ];
  const entry = {
    contentId: 'id-f',
    name: 'Testy Forms',
    kind: 'feat',
    revisions: [],
    latest: { contentId: 'id-f', revisionId: 'r-f', name: 'Testy Forms', kind: 'feat', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: '3', points: [] });
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-f', revisionId: 'x' }, errors: [], warnings: [], canPublish: true });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Forms' }));
  const rule = (name: RegExp) => within(screen.getByRole('group', { name }));
  const checked = async () => {
    await user.click(screen.getByRole('button', { name: 'Check' }));
    await waitFor(() => expect(vi.mocked(client.validateRevision).mock.calls.length).toBeGreaterThan(0));
    const sent = vi.mocked(client.validateRevision).mock.calls.at(-1)![0] as unknown as { effects: Record<string, unknown>[] };
    return (id: string) => sent.effects.find((e) => e.id === id)!;
  };
  return { rule, checked };
}

it('choosing Nothing for the pool also drops the cost and the pool content', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openFormDraft(user);
  await user.selectOptions(rule(/^Rule 4: Toggle/).getByRole('combobox', { name: 'Turning it on spends' }), 'Nothing');
  expect(rule(/^Rule 4: Toggle/).queryByRole('textbox', { name: /^Cost/ })).toBeNull();
  const sent = await checked();
  expect(sent('toggle-1')).toMatchObject({ toggleId: 'form' });
  expect(sent('toggle-1').resourceId).toBeUndefined();
  expect(sent('toggle-1').cost).toBeUndefined();
  expect(sent('toggle-1').resourceContent).toBeUndefined();
});

it('removing the pool drops the cost of the toggles that spent it', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openFormDraft(user);
  await user.click(rule(/^Rule 1: Resource/).getByRole('button', { name: 'Remove rule 1' }));
  const sent = await checked();
  expect(sent('toggle-1').resourceId).toBeUndefined();
  expect(sent('toggle-1').cost).toBeUndefined();
  expect(sent('toggle-1').resourceContent).toBeUndefined();
});

// D43 slice 3: upkeep, durations and pools in the studio.
async function openUpkeepDraft(user: ReturnType<typeof userEvent.setup>, extra: Record<string, unknown>[] = []) {
  const effects = [
    { type: 'resource', id: 'resource-1', resourceId: 'focus', label: 'Testy Focus', maximum: '7' },
    { type: 'toggle', id: 'toggle-2', toggleId: 'stance', label: 'Testy Stance' },
    { type: 'upkeep', id: 'upkeep-3', toggle: 'stance', at: 'turnStart', resourceId: 'focus', change: 'spend', amount: '3' },
    ...extra,
  ];
  const entry = {
    contentId: 'id-u',
    name: 'Testy Upkeep',
    kind: 'feat',
    revisions: [],
    latest: { contentId: 'id-u', revisionId: 'r-u', name: 'Testy Upkeep', kind: 'feat', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: '3', points: [] });
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-u', revisionId: 'x' }, errors: [], warnings: [], canPublish: true });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Upkeep' }));
  const rule = (name: RegExp) => within(screen.getByRole('group', { name }));
  const checked = async () => {
    await user.click(screen.getByRole('button', { name: 'Check' }));
    await waitFor(() => expect(vi.mocked(client.validateRevision).mock.calls.length).toBeGreaterThan(0));
    const sent = vi.mocked(client.validateRevision).mock.calls.at(-1)![0] as unknown as { effects: Record<string, unknown>[] };
    return { effect: (id: string) => sent.effects.find((e) => e.id === id), count: sent.effects.length };
  };
  return { rule, checked };
}

it('an upkeep rule shows its fields as words and sends what was chosen', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openUpkeepDraft(user);
  const upkeep = rule(/^Rule 3: Upkeep/);
  expect((upkeep.getByRole('combobox', { name: 'Toggle' }) as HTMLSelectElement).value).toBe('stance');
  expect(upkeep.getByRole('option', { name: 'While Testy Stance is on' })).toBeTruthy();
  expect((upkeep.getByRole('combobox', { name: 'When' }) as HTMLSelectElement).value).toBe('turnStart');
  expect((upkeep.getByRole('combobox', { name: 'Pool' }) as HTMLSelectElement).value).toBe('focus');
  expect((upkeep.getByRole('combobox', { name: 'Change' }) as HTMLSelectElement).value).toBe('spend');

  await user.selectOptions(upkeep.getByRole('combobox', { name: 'When' }), 'turnEnd');
  await user.selectOptions(upkeep.getByRole('combobox', { name: 'If the pool is short' }), 'end');
  const sent = await checked();
  expect(sent.effect('upkeep-3')).toMatchObject({ type: 'upkeep', toggle: 'stance', at: 'turnEnd', resourceId: 'focus', change: 'spend', amount: '3', ifShort: 'end' });
});

it('the shortfall choice is only for a spend that belongs to a toggle', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openUpkeepDraft(user);
  const upkeep = rule(/^Rule 3: Upkeep/);
  await user.selectOptions(upkeep.getByRole('combobox', { name: 'If the pool is short' }), 'end');
  await user.selectOptions(upkeep.getByRole('combobox', { name: 'Toggle' }), 'Always (no toggle)');
  expect(upkeep.queryByRole('combobox', { name: 'If the pool is short' })).toBeNull();
  expect((await checked()).effect('upkeep-3')!.ifShort).toBeUndefined();

  await user.selectOptions(upkeep.getByRole('combobox', { name: 'Toggle' }), 'While Testy Stance is on');
  await user.selectOptions(upkeep.getByRole('combobox', { name: 'If the pool is short' }), 'end');
  await user.selectOptions(upkeep.getByRole('combobox', { name: 'Change' }), 'regain');
  expect(upkeep.queryByRole('combobox', { name: 'If the pool is short' })).toBeNull();
  const sent = await checked();
  expect(sent.effect('upkeep-3')).toMatchObject({ change: 'regain' });
  expect(sent.effect('upkeep-3')!.ifShort).toBeUndefined();
});

it('"all" is offered for a regain and goes back to a number when the change is a spend again', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openUpkeepDraft(user);
  const upkeep = rule(/^Rule 3: Upkeep/);
  await user.selectOptions(upkeep.getByRole('combobox', { name: 'Change' }), 'regain');
  const amount = upkeep.getByRole('textbox', { name: /^Amount \(all, a number or a formula\)/ });
  await user.clear(amount);
  await user.type(amount, 'all');
  expect((await checked()).effect('upkeep-3')).toMatchObject({ change: 'regain', amount: 'all' });
  await user.selectOptions(upkeep.getByRole('combobox', { name: 'Change' }), 'spend');
  expect((upkeep.getByRole('textbox', { name: /^Amount \(a number or a formula\)/ }) as HTMLInputElement).value).toBe('1');
});

it('Add upkeep adds a rule that spends from the first pool at the start of the turn; it needs a pool', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openUpkeepDraft(user);
  await user.click(screen.getByRole('button', { name: 'Add upkeep' }));
  expect((rule(/^Rule 4: Upkeep/).getByRole('combobox', { name: 'Pool' }) as HTMLSelectElement).value).toBe('focus');
  expect((await checked()).effect('upkeep-4')).toMatchObject({ type: 'upkeep', at: 'turnStart', resourceId: 'focus', change: 'spend', amount: '1' });
  await user.click(rule(/^Rule 1: Resource/).getByRole('button', { name: 'Remove rule 1' }));
  expect(screen.queryByRole('group', { name: /: Upkeep/ })).toBeNull(); // both upkeeps spent from the removed pool
  expect((screen.getByRole('button', { name: 'Add upkeep' }) as HTMLButtonElement).disabled).toBe(true);
});

it('removing the toggle an upkeep named makes the upkeep forget it', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openUpkeepDraft(user);
  await user.click(rule(/^Rule 2: Toggle/).getByRole('button', { name: 'Remove rule 2' }));
  const sent = await checked();
  expect(sent.effect('upkeep-3')).toMatchObject({ type: 'upkeep', resourceId: 'focus' });
  expect(sent.effect('upkeep-3')!.toggle).toBeUndefined();
  expect(sent.effect('upkeep-3')!.toggleContent).toBeUndefined();
  expect(sent.effect('upkeep-3')!.ifShort).toBeUndefined();
});

it('removing the pool removes the upkeeps that spent from it, and keeps the others', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openUpkeepDraft(user, [
    { type: 'resource', id: 'resource-4', resourceId: 'calm', label: 'Testy Calm', maximum: '2' },
    { type: 'upkeep', id: 'upkeep-5', at: 'turnEnd', resourceId: 'calm', change: 'regain', amount: 'all' },
  ]);
  await user.click(rule(/^Rule 1: Resource/).getByRole('button', { name: 'Remove rule 1' }));
  const sent = await checked();
  expect(sent.effect('upkeep-3')).toBeUndefined();
  expect(sent.effect('upkeep-5')).toMatchObject({ resourceId: 'calm' });
  expect(sent.count).toBe(3);
});

it('a resource can count up with a full message, and be an allowance; turning counting off drops the message', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openUpkeepDraft(user);
  const resource = rule(/^Rule 1: Resource/);
  expect(resource.queryByRole('textbox', { name: /^When full/ })).toBeNull(); // only with counting up
  await user.click(resource.getByRole('checkbox', { name: /^Counts up/ }));
  await user.type(resource.getByRole('textbox', { name: /^When full/ }), 'Testy strain is at its limit');
  await user.click(resource.getByRole('checkbox', { name: /^Allowance/ }));
  expect((await checked()).effect('resource-1')).toMatchObject({ counts: 'up', resourceFull: { text: 'Testy strain is at its limit' }, allowance: true });

  await user.click(resource.getByRole('checkbox', { name: /^Counts up/ }));
  expect(resource.queryByRole('textbox', { name: /^When full/ })).toBeNull();
  const sent = (await checked()).effect('resource-1')!;
  expect(sent.counts).toBeUndefined();
  expect(sent.resourceFull).toBeUndefined();
  expect(sent.allowance).toBe(true);
});

it('a toggle has a duration in rounds, a formula with a preview, and empty means until switched off', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openUpkeepDraft(user);
  const toggle = rule(/^Rule 2: Toggle/);
  const duration = toggle.getByRole('textbox', { name: /^Lasts \(rounds/ });
  await user.type(duration, '2 + PB');
  expect(duration.getAttribute('aria-describedby')).toBe('rule-toggle-2-duration-preview');
  expect((await checked()).effect('toggle-2')).toMatchObject({ duration: '2 + PB' });
  await user.clear(duration);
  expect((await checked()).effect('toggle-2')!.duration).toBeUndefined();
});

it('moving a toggle to another group forgets the forms it was allowed with', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openFormDraft(user);
  await user.selectOptions(rule(/^Rule 5: Toggle/).getByRole('combobox', { name: 'Form group' }), 'Testy Two');
  const sent = await checked();
  expect(sent('toggle-2').group).toBe('two');
  expect(sent('toggle-2').allowedWith).toBeUndefined();
});
