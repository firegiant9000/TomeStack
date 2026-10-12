// @vitest-environment jsdom
// Concentration (D19): the panel appears only while concentrating, names the spell, shows the pending save and offers the
// roll (no state change) and the two confirmed outcomes.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import type { CharacterView, PlayState } from '../api/types';
import { ConcentrationPanel, HitPointsPanel } from './PlayPanels';

afterEach(cleanup);

const spell = { contentId: '00000000-0000-4000-8000-000000000001', revisionId: '00000000-0000-4000-8000-000000000002' };
function view(play?: Partial<PlayState>): CharacterView {
  return {
    character: {
      id: 'fixture-3', schemaVersion: 8, name: 'Fixture Caster', rulesFamily: 'srd-5.2.1', level: 3, classes: [], choices: [], crossFamilyExceptions: [],
      baseAbilities: { str: 8, dex: 14, con: 12, int: 16, wis: 10, cha: 10 }, pins: [], overrides: [], updatedAt: '2026-10-06T00:00:00Z',
      play: play ? { temporaryHitPoints: 0, resources: [], conditions: [], exhaustion: 0, ...play } : undefined,
    },
    sheet: { characterId: 'fixture-3', rulesFamily: 'srd-5.2.1', diagnostics: [], fields: [], hitPoints: { maximum: 20, current: 13, temporary: 0 } },
  };
}

it('renders nothing when the character is not concentrating', () => {
  const { container } = render(<ConcentrationPanel view={view()} act={() => {}} roll={() => {}} />);
  expect(container.textContent).toBe('');
});

it('names the spell, and after damage offers the save roll and the two confirmed outcomes', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  const roll = vi.fn();
  render(<ConcentrationPanel view={view({ concentration: { spell, name: 'Fixture Ward', pendingSaveDc: 12 } })} act={act} roll={roll} />);
  expect(screen.getByRole('heading', { name: 'Concentration: Fixture Ward, Constitution saving throw DC 12 pending' })).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Roll Constitution saving throw' }));
  expect(roll).toHaveBeenCalledWith({ field: 'save.con' });
  await user.click(screen.getByRole('button', { name: 'Kept concentration' }));
  expect(act).toHaveBeenCalledWith({ action: 'clearConcentrationCheck' });
  await user.click(screen.getByRole('button', { name: 'End concentration' }));
  expect(act).toHaveBeenCalledWith({ action: 'endConcentration' });
});

// Focus (WCAG 2.4.3): each confirmed outcome removes the focused button. A stateful harness applies the change like the sheet does,
// with the Hit points panel beside it as the stable target that survives "End concentration".
function Harness() {
  const [play, setPlay] = useState<Partial<PlayState>>({ concentration: { spell, name: 'Fixture Ward', pendingSaveDc: 12 } });
  const v = view(play);
  const act = (a: { action: string }) =>
    setPlay(a.action === 'endConcentration' ? {} : { concentration: { spell, name: 'Fixture Ward' } });
  return (
    <>
      <HitPointsPanel view={v} act={() => {}} />
      <ConcentrationPanel view={v} act={act} roll={() => {}} />
    </>
  );
}

it('moves focus to the panel heading after "Kept concentration"', async () => {
  const user = userEvent.setup();
  render(<Harness />);
  await user.click(screen.getByRole('button', { name: 'Kept concentration' }));
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Concentration: Fixture Ward' }));
});

it('moves focus to the Hit points heading after "End concentration" removes the panel', async () => {
  const user = userEvent.setup();
  render(<Harness />);
  await user.click(screen.getByRole('button', { name: 'End concentration' }));
  expect(screen.queryByRole('heading', { name: /^Concentration:/ })).toBeNull();
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: /^Hit points:/ }));
});

it('offers only End concentration when no save is pending', () => {
  render(<ConcentrationPanel view={view({ concentration: { spell, name: 'Fixture Ward' } })} act={() => {}} roll={() => {}} />);
  expect(screen.getByRole('heading', { name: 'Concentration: Fixture Ward' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Kept concentration' })).toBeNull();
  expect(screen.getByRole('button', { name: 'End concentration' })).toBeTruthy();
});
