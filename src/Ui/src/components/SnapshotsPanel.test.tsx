// @vitest-environment jsdom
// The restore confirmation names the coin change (D21: coins roll back with a snapshot, notes stay). The client is mocked; values are invented.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, RestorePreview, SnapshotSummary } from '../api/types';
import { SnapshotsPanel } from './SnapshotsPanel';

vi.mock('../api/client', () => ({ client: { snapshots: vi.fn(), restorePreview: vi.fn() } }));

const character = {
  id: 'fixture-4',
  schemaVersion: 8,
  name: 'Fixture Purse',
  rulesFamily: 'srd-5.1',
  level: 1,
  classes: [],
  choices: [],
  crossFamilyExceptions: [],
  baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
  pins: [],
  overrides: [],
  updatedAt: '2026-10-06T00:00:00Z',
} as Character;

const snapshot: SnapshotSummary = { id: 's1', characterId: 'fixture-4', createdAt: '2026-10-06T10:00:00Z', reason: 'manual', label: 'Before the heist', name: 'Fixture Purse', level: 1 };
const preview = (over: Partial<RestorePreview>): RestorePreview => ({ token: 't1', snapshot, fields: [], newDiagnostics: [], added: [], removed: [], playChanges: false, ...over });

beforeEach(() => vi.mocked(client.snapshots).mockResolvedValue({ items: [snapshot], hasMore: false }));
afterEach(cleanup);

async function openPreview(p: RestorePreview) {
  vi.mocked(client.restorePreview).mockResolvedValue(p);
  const user = userEvent.setup();
  render(<SnapshotsPanel character={character} onChanged={() => {}} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: /^Restore Before the heist/ }));
  return screen.findByRole('region', { name: /^Restore Before the heist\?/ });
}

it('names the coins the restore brings back and the coins now', async () => {
  const region = await openPreview(preview({ currencyNow: { cp: 0, sp: 0, ep: 0, gp: 40, pp: 0 }, currencyAfter: { cp: 0, sp: 12, ep: 0, gp: 3, pp: 0 } }));
  expect(region.textContent).toContain('Coins go back to 3 gp, 12 sp (now 40 gp).');
});

it('says nothing about coins when the restore leaves them as they are', async () => {
  const region = await openPreview(preview({}));
  expect(region.textContent).not.toContain('Coins');
});
