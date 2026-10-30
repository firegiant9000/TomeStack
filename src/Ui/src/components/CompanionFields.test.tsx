// @vitest-environment jsdom
// MECH-X item 4 (T5): the companion editor.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, expect, it } from 'vitest';
import type { ContentRevision, Effect } from '../api/types';
import { CompanionFields } from './CompanionFields';

afterEach(cleanup);

type Companion = Extract<Effect, { type: 'companion' }>;

const guid = '3f2504e0-4f89-41d3-9a0c-0305e82c3301';
const start: Companion = { type: 'companion', id: 'companion-1', companionId: guid, label: 'Pet', armorClass: '13', hitPointMaximum: '5 * LEVEL' };
const attack = (id: string, label: string) => ({ type: 'attack', id, attackId: `${id}-key`, label, kind: 'melee', abilities: ['str'] }) as unknown as Effect;
const pool = (id: string, resourceId: string) => ({ type: 'resource', id, resourceId, label: resourceId, maximum: '2' }) as unknown as Effect;
const revisionOf = (effects: Effect[]) => ({ effects }) as unknown as ContentRevision;

function Harness(props: { start: Companion; effects: Effect[]; onSeen: (effect: Companion) => void }) {
  const [effect, setEffect] = useState(props.start);
  return (
    <CompanionFields
      effect={effect}
      revision={revisionOf([effect, ...props.effects])}
      onChange={(e) => {
        props.onSeen(e as Companion);
        setEffect(e as Companion);
      }}
      onProblem={() => {}}
    />
  );
}

const record = (seen: { last?: Companion }) => (effect: Companion) => {
  seen.last = effect;
};

it('labels every field and shows the stored values', () => {
  render(<Harness start={{ ...start, speed: '30' }} effects={[attack('attack-1', 'Bite'), pool('resource-1', 'pool-a')]} onSeen={() => {}} />);
  expect((screen.getByRole('textbox', { name: 'Label' }) as HTMLInputElement).value).toBe('Pet');
  expect((screen.getByRole('textbox', { name: 'Armor Class formula' }) as HTMLInputElement).value).toBe('13');
  expect((screen.getByRole('textbox', { name: 'Hit point maximum formula' }) as HTMLInputElement).value).toBe('5 * LEVEL');
  expect((screen.getByRole('textbox', { name: 'Speed (optional)' }) as HTMLInputElement).value).toBe('30');
  expect(within(screen.getByRole('group', { name: 'Attacks' })).getByRole('checkbox', { name: 'Bite' })).toBeTruthy();
  expect(within(screen.getByRole('group', { name: 'Pools' })).getByRole('checkbox', { name: 'pool-a' })).toBeTruthy();
});

it('checking an attack writes its effect id, and unchecking the last removes the key', async () => {
  const user = userEvent.setup();
  const seen: { last?: Companion } = {};
  render(<Harness start={start} effects={[attack('attack-1', 'Bite'), attack('attack-2', 'Claw')]} onSeen={record(seen)} />);
  const box = within(screen.getByRole('group', { name: 'Attacks' }));
  await user.click(box.getByRole('checkbox', { name: 'Bite' }));
  expect(seen.last?.attacks).toEqual(['attack-1']);
  await user.click(box.getByRole('checkbox', { name: 'Claw' }));
  expect(seen.last?.attacks).toEqual(['attack-1', 'attack-2']);
  await user.click(box.getByRole('checkbox', { name: 'Bite' }));
  await user.click(box.getByRole('checkbox', { name: 'Claw' }));
  expect('attacks' in seen.last!).toBe(false);
});

it('checking a pool writes its resource id, and unchecking removes the key', async () => {
  const user = userEvent.setup();
  const seen: { last?: Companion } = {};
  render(<Harness start={start} effects={[pool('resource-1', 'pool-a')]} onSeen={record(seen)} />);
  const box = within(screen.getByRole('group', { name: 'Pools' }));
  await user.click(box.getByRole('checkbox', { name: 'pool-a' }));
  expect(seen.last?.resourceIds).toEqual(['pool-a']);
  await user.click(box.getByRole('checkbox', { name: 'pool-a' }));
  expect('resourceIds' in seen.last!).toBe(false);
});

it('keeps companionId verbatim through every edit', async () => {
  const user = userEvent.setup();
  const seen: { last?: Companion } = {};
  render(<Harness start={start} effects={[attack('attack-1', 'Bite')]} onSeen={record(seen)} />);
  await user.type(screen.getByRole('textbox', { name: 'Label' }), 's');
  expect(seen.last).toMatchObject({ label: 'Pets', companionId: guid });
  await user.type(screen.getByRole('textbox', { name: 'Speed (optional)' }), '40');
  expect(seen.last).toMatchObject({ speed: '40', companionId: guid });
  await user.click(screen.getByRole('checkbox', { name: 'Bite' }));
  expect(seen.last?.companionId).toBe(guid);
  expect(screen.queryByDisplayValue(guid)).toBeNull();
});

it('an emptied speed removes the key', async () => {
  const user = userEvent.setup();
  const seen: { last?: Companion } = {};
  render(<Harness start={{ ...start, speed: '30' }} effects={[]} onSeen={record(seen)} />);
  await user.clear(screen.getByRole('textbox', { name: 'Speed (optional)' }));
  expect('speed' in seen.last!).toBe(false);
});

it('says to add an attack first when the revision has none', () => {
  render(<Harness start={start} effects={[]} onSeen={() => {}} />);
  expect(screen.getByText('Add an attack to this content first.')).toBeTruthy();
});
