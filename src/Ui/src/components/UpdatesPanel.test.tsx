// @vitest-environment jsdom
// Updates panel states: checking, none, the check failed, and offers. The client is mocked; values are invented.
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterView, UpdateOffer } from '../api/types';
import { expectNoAxeViolations } from '../test/axe';
import { UpdatesPanel } from './UpdatesPanel';

vi.mock('../api/client', () => ({ client: { availableUpdates: vi.fn() } }));

const character = {
  id: 'fixture-updates',
  schemaVersion: 8,
  name: 'Fixture Lark',
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
const view = { character } as CharacterView;
const offer: UpdateOffer = {
  from: { contentId: 'fixture-feat', revisionId: 'r1' },
  to: { contentId: 'fixture-feat', revisionId: 'r2' },
  name: 'Fixture Feat',
  role: 'grant',
  sourceTitle: 'Fixture Notebook',
  bundled: false,
} as UpdateOffer;

afterEach(() => {
  cleanup();
  vi.mocked(client.availableUpdates).mockReset();
});

function renderPanel(showEmpty: boolean, onError = vi.fn()) {
  return { onError, ...render(<UpdatesPanel view={view} onChanged={() => {}} onError={onError} onStatus={() => {}} showEmpty={showEmpty} />) };
}

it('says it is checking while the request is pending', async () => {
  vi.mocked(client.availableUpdates).mockReturnValue(new Promise(() => {}));
  const { container } = renderPanel(true);
  expect(await screen.findByText('Checking for newer versions…')).toBeTruthy();
  await expectNoAxeViolations(container);
});

it('says there are none once the check returns nothing', async () => {
  vi.mocked(client.availableUpdates).mockResolvedValue([]);
  const { container, onError } = renderPanel(true);
  expect(await screen.findByText('No newer versions of the content this character uses.')).toBeTruthy();
  expect(onError).not.toHaveBeenCalled();
  await expectNoAxeViolations(container);
});

it('says the check failed, not "none", and reports the error once', async () => {
  vi.mocked(client.availableUpdates).mockRejectedValue(new Error('offline'));
  const { container, onError } = renderPanel(true);
  expect(await screen.findByText("Couldn't check for newer versions.")).toBeTruthy();
  expect(screen.queryByText(/No newer versions/)).toBeNull();
  expect(screen.queryByText(/Checking for newer versions/)).toBeNull();
  expect(onError).toHaveBeenCalledTimes(1);
  await expectNoAxeViolations(container);
});

it('lists an offer under "Updates available"', async () => {
  vi.mocked(client.availableUpdates).mockResolvedValue([offer]);
  const { container } = renderPanel(false);
  expect(await screen.findByRole('heading', { name: 'Updates available' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Review update: Fixture Feat' })).toBeTruthy();
  await expectNoAxeViolations(container);
});

it('renders nothing without showEmpty when there are no offers', async () => {
  vi.mocked(client.availableUpdates).mockResolvedValue([]);
  const { container } = renderPanel(false);
  await vi.waitFor(() => expect(client.availableUpdates).toHaveBeenCalled());
  expect(container.textContent).toBe('');
});
