// @vitest-environment jsdom
// "Save for an older TomeStack" (D85) in the campaign pack form: the target control, the change region and the confirmation.
// The client is mocked; values are invented.
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Campaign, CampaignPackPreview, RulesFamilyPolicy } from '../api/types';
import { blockedHint, confirmLabel, describedBy, exportChange, failedLine, firstRadio, packageTargetsFixture } from '../test/downConversion';
import { CampaignsPanel } from './CampaignsPanel';

vi.mock('../api/client', () => ({
  client: {
    listCampaigns: vi.fn(),
    listSources: vi.fn(),
    packageTargets: vi.fn(),
    campaignPackPreview: vi.fn(),
    saveCampaignPackAs: vi.fn(),
    exportCampaignPack: vi.fn(),
    packagePreflight: vi.fn(),
  },
}));

afterEach(cleanup);

const campaign: Campaign = { id: 'compat-table', name: 'Compat Table', rulesFamily: 'srd-5.1', allowedSources: [] };
const base: CampaignPackPreview = {
  fileName: 'compat.tomestack-campaign.zip',
  campaignId: 'compat-table',
  name: 'Compat Table',
  rulesFamily: 'srd-5.1',
  included: [],
  referenced: [],
  leftOut: [],
  revisions: 0,
  warnings: [],
};
const changesFor: Record<string, ReturnType<typeof exportChange>[]> = {};
const previewOf = (target?: string | null): CampaignPackPreview =>
  target ? { ...base, target, changes: changesFor[target] ?? [], changesHash: `hash-${target}` } : base;

beforeEach(() => {
  for (const key of Object.keys(changesFor)) delete changesFor[key];
  vi.mocked(client.listCampaigns).mockResolvedValue([campaign]);
  vi.mocked(client.listSources).mockResolvedValue([]);
  vi.mocked(client.packageTargets).mockReset();
  vi.mocked(client.packageTargets).mockResolvedValue(packageTargetsFixture);
  vi.mocked(client.campaignPackPreview).mockReset();
  vi.mocked(client.campaignPackPreview).mockImplementation(async (_id, target) => previewOf(target));
  vi.mocked(client.saveCampaignPackAs).mockReset();
  vi.mocked(client.saveCampaignPackAs).mockResolvedValue({ saved: true, fileName: 'compat.zip' } as Awaited<ReturnType<typeof client.saveCampaignPackAs>>);
  vi.mocked(client.packagePreflight).mockResolvedValue({ included: [], omitted: [], blocking: [], notices: [] });
});

const families = [{ id: 'srd-5.1', displayName: 'SRD 5.1' }] as unknown as RulesFamilyPolicy[];

async function openShare(onError: (e: unknown) => void = () => {}) {
  const user = userEvent.setup();
  render(<CampaignsPanel rulesFamilies={families} onError={onError} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Share Compat Table…' }));
  await screen.findByRole('region', { name: 'Left out of the campaign pack' });
  return user;
}
const save = () => screen.getByRole('button', { name: 'Save campaign pack…' }) as HTMLButtonElement;

it('re-runs the preview for the chosen release and again without a target for the first choice', async () => {
  const user = await openShare();
  expect(vi.mocked(client.campaignPackPreview).mock.lastCall).toEqual(['compat-table']);
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await waitFor(() => expect(vi.mocked(client.campaignPackPreview).mock.lastCall).toEqual(['compat-table', '0.6.0']));
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  await user.click(screen.getByRole('radio', { name: firstRadio }));
  await waitFor(() => expect(vi.mocked(client.campaignPackPreview).mock.lastCall).toEqual(['compat-table']));
});

it('keeps focus on the radio that was chosen while the preview reloads', async () => {
  const user = await openShare();
  const radio = await screen.findByRole('radio', { name: '0.6.0' });
  await user.click(radio);
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  expect(document.activeElement).toBe(radio);
});

it('disables Save while something blocks, and describes why', async () => {
  changesFor['0.6.0'] = [exportChange('blocks', 'Compat Table needs rules 0.6.0 lacks.')];
  const user = await openShare();
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await screen.findByText('Compat Table needs rules 0.6.0 lacks.');
  expect(save().disabled).toBe(true);
  expect(describedBy(save())).toBe(blockedHint);
});

it('needs the tick for a dropped item, and a new preview clears it', async () => {
  changesFor['0.6.0'] = [exportChange('dropped', 'Compat Tide Stance is left out.')];
  changesFor['0.5.0'] = [exportChange('dropped', 'Compat Steady Hands is left out.')];
  const user = await openShare();
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await user.click(await screen.findByRole('checkbox', { name: confirmLabel }));
  expect(save().disabled).toBe(false);
  await user.click(screen.getByRole('radio', { name: '0.5.0' }));
  await screen.findByText('Compat Steady Hands is left out.');
  expect((screen.getByRole('checkbox', { name: confirmLabel }) as HTMLInputElement).checked).toBe(false);
  expect(save().disabled).toBe(true);
});

it('sends the release and the preview hash when a release is chosen, and neither for the first choice', async () => {
  const user = await openShare();
  // Save waits for the sharing preflight's report too (D62 L5).
  await waitFor(() => expect(save().disabled).toBe(false));
  await user.click(save());
  expect(vi.mocked(client.saveCampaignPackAs).mock.lastCall).toEqual(['compat-table']);
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  await waitFor(() => expect(save().disabled).toBe(false));
  await user.click(save());
  expect(vi.mocked(client.saveCampaignPackAs).mock.lastCall).toEqual(['compat-table', '0.6.0', 'hash-0.6.0']);
});

it('says which TomeStack opens the default file, and shows nothing when the service does not say', async () => {
  vi.mocked(client.campaignPackPreview).mockResolvedValue({ ...base, opensIn: '0.5.0' });
  await openShare();
  expect(await screen.findByText('This file opens in TomeStack 0.5.0 or newer.')).toBeTruthy();
  cleanup();
  vi.mocked(client.campaignPackPreview).mockResolvedValue(previewOf());
  await openShare();
  expect(screen.queryByRole('region', { name: 'Compatibility' })).toBeNull();
});

it('shows no target control, and reports nothing, when the targets cannot be read', async () => {
  vi.mocked(client.packageTargets).mockRejectedValue(new Error('unsupported'));
  const onError = vi.fn();
  await openShare(onError);
  await waitFor(() => expect(client.packageTargets).toHaveBeenCalled());
  expect(screen.queryByText('Which TomeStack will open it?')).toBeNull();
  expect(save().disabled).toBe(false);
  expect(onError).not.toHaveBeenCalled();
});

it('lets the last chosen release win when an earlier reply arrives late', async () => {
  changesFor['0.5.0'] = [exportChange('dropped', 'Compat Steady Hands is left out.')];
  const user = await openShare();
  let late: (p: CampaignPackPreview) => void = () => {};
  vi.mocked(client.campaignPackPreview).mockReturnValueOnce(new Promise<CampaignPackPreview>((r) => (late = r)));
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await user.click(screen.getByRole('radio', { name: '0.5.0' }));
  await screen.findByText('Compat Steady Hands is left out.');
  late(previewOf('0.6.0'));
  await new Promise((r) => setTimeout(r, 20));
  expect(screen.getByRole('region', { name: 'Changed for TomeStack 0.5.0' })).toBeTruthy();
  expect(screen.queryByRole('region', { name: 'Changed for TomeStack 0.6.0' })).toBeNull();
  await user.click(screen.getByRole('checkbox', { name: confirmLabel }));
  await user.click(save());
  expect(vi.mocked(client.saveCampaignPackAs).mock.lastCall).toEqual(['compat-table', '0.5.0', 'hash-0.5.0']);
});

it('says so plainly, and has no Save, when the preview for a chosen release fails', async () => {
  vi.mocked(client.campaignPackPreview).mockImplementation(async (_id, target) => {
    if (target) throw new Error('boom');
    return previewOf();
  });
  const onError = vi.fn();
  const user = await openShare(onError);
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  expect(await screen.findByText(failedLine('0.6.0'))).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Save campaign pack…' })).toBeNull();
  expect(onError).not.toHaveBeenCalled();
});
