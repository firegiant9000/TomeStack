// @vitest-environment jsdom
// Concentration (D19): "Concentrate on {spell}" is offered for every prepared concentration spell, cantrips included.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { CharacterView, SpellEntry, TraceOrigin } from '../api/types';
import { SpellsPanel } from './SpellsPanel';

afterEach(cleanup);

const origin: TraceOrigin = { kind: 'content', rulesFamily: 'srd-5.2.1', sourceTitle: 'Fixture Pack' };
const ref = (n: number) => ({ contentId: `00000000-0000-4000-8000-${n.toString().padStart(12, '0')}`, revisionId: `00000000-0000-4000-9000-${n.toString().padStart(12, '0')}` });
const spell = (n: number, name: string, level: number, concentration: boolean, prepared = true): SpellEntry => ({
  spell: ref(n), name, level, prepared, concentration, ritual: false, attack: 'none', origin, diagnostics: [],
});

function view(concentratingOn?: number): CharacterView {
  return {
    character: {
      id: 'fixture-4', schemaVersion: 8, name: 'Fixture Caster', rulesFamily: 'srd-5.2.1', level: 3, classes: [], choices: [], crossFamilyExceptions: [],
      baseAbilities: { str: 8, dex: 14, con: 12, int: 16, wis: 10, cha: 10 }, pins: [], overrides: [], updatedAt: '2026-10-06T00:00:00Z',
      play: concentratingOn === undefined ? undefined : { temporaryHitPoints: 0, resources: [], conditions: [], exhaustion: 0, concentration: { spell: ref(concentratingOn), name: 'x' } },
    },
    sheet: {
      characterId: 'fixture-4', rulesFamily: 'srd-5.2.1', diagnostics: [], fields: [],
      spellSlots: [{ level: 1, maximum: 2, spent: 0, remaining: 2, field: 'slots.1' }],
      spellcasting: [{
        content: ref(1), name: 'Fixture Arcanist', effectId: 'spellcasting', classLevel: 3, ability: 'int', attackBonus: 5, saveDc: 13, preparation: 'prepared',
        spellList: 'fixture-arcane', slotKind: 'spellSlots', slots: [2], primary: true, origin, warnings: [],
        spells: [
          spell(10, 'Fixture Hush', 0, true), // a concentration cantrip
          spell(11, 'Fixture Veil', 1, true),
          spell(12, 'Fixture Spark', 1, false),
          spell(13, 'Fixture Shroud', 1, true, false), // concentration but not prepared
        ],
      }],
    },
  } as CharacterView;
}

const panel = (v: CharacterView, act = () => {}) => render(<SpellsPanel view={v} act={act} roll={() => {}} onSave={() => {}} />);

it('offers Concentrate on for prepared concentration spells, a cantrip included, and no other', () => {
  panel(view());
  expect(screen.getByRole('button', { name: 'Concentrate on Fixture Hush' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Concentrate on Fixture Veil' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Concentrate on Fixture Spark' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Concentrate on Fixture Shroud' })).toBeNull();
});

it('disables the button for the spell being concentrated on and sends startConcentration otherwise', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  panel(view(10), act);
  expect((screen.getByRole('button', { name: 'Concentrate on Fixture Hush' }) as HTMLButtonElement).disabled).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Concentrate on Fixture Veil' }));
  expect(act).toHaveBeenCalledWith({ action: 'startConcentration', contentId: ref(11).contentId });
});

it('filters a caster’s spells by name, keeps the Prepared and Concentrate names of the rows shown, and says how many (D30)', async () => {
  const user = userEvent.setup();
  panel(view());
  const search = screen.getByRole('searchbox', { name: /^Search .* spells by name$/ });
  await user.type(search, 'VEIL');
  expect(screen.getByText('1 of 4 spells shown')).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Concentrate on Fixture Veil' })).toBeTruthy();
  expect(screen.getByRole('checkbox', { name: 'Prepared: Fixture Veil' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Concentrate on Fixture Hush' })).toBeNull();
  await user.type(search, 'zz');
  expect(screen.getByText(/No spells match “VEILzz”/)).toBeTruthy();
  await user.clear(search);
  expect(screen.getByRole('button', { name: 'Concentrate on Fixture Hush' })).toBeTruthy();
  expect(screen.queryByText(/spells shown/)).toBeNull();
  expect(document.activeElement).toBe(search);
});
