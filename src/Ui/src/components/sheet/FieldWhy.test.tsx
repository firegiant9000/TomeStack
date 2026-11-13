// @vitest-environment jsdom
// FieldWhy is the body FieldCard shows under a number: the plain steps, the trace table and the warnings. Values are invented.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it } from 'vitest';
import type { DerivedValue } from '../../api/types';
import { explainSteps } from '../../explain';
import { expectNoAxeViolations } from '../../test/axe';
import { FieldWhy } from './FieldWhy';

afterEach(cleanup);

const armorClass = (warnings: DerivedValue['warnings'] = []): DerivedValue => ({
  field: 'armorClass',
  label: 'Armor Class',
  value: 12,
  computedValue: 12,
  automation: 'automatic',
  units: 'score',
  warnings,
  trace: [
    { order: 1, operation: 'base', description: 'Unarmored', amount: 10, result: 10, origin: { kind: 'rulesPolicy', rulesFamily: 'srd-5.1' } },
    { order: 2, operation: 'add', description: 'Dexterity modifier', amount: 2, result: 12, field: 'ability.dex.mod', origin: { kind: 'characterChoice', rulesFamily: 'srd-5.1' } },
  ],
});

const labels = new Map([['ability.dex.mod', 'Dexterity modifier']]);

it('lists the same steps the field card lists, and the trace table', () => {
  const field = armorClass();
  render(<FieldWhy field={field} label={field.label} labels={labels} />);
  const list = screen.getByRole('list', { name: 'Why Armor Class', hidden: true });
  const shown = within(list)
    .getAllByRole('listitem', { hidden: true })
    .map((li) => li.textContent);
  expect(shown).toEqual(explainSteps(field.trace));
  expect(shown[0]).toContain('Starts at 10');
  const table = screen.getByRole('table', { name: 'How armor class is calculated' });
  expect(within(table).getAllByRole('row')).toHaveLength(3);
  expect(within(table).getByText('Dexterity modifier', { selector: 'td:nth-child(2)' })).toBeTruthy();
  expect(screen.queryByRole('list', { name: 'Armor Class warnings' })).toBeNull();
});

it('lists the warnings under the label', () => {
  const field = armorClass([{ code: 'fixture.warning', message: 'Invented warning text' }]);
  render(<FieldWhy field={field} label={field.label} labels={labels} />);
  const list = screen.getByRole('list', { name: 'Armor Class warnings' });
  expect(within(list).getByText('Invented warning text')).toBeTruthy();
});

it('reaches the steps disclosure by keyboard and has no accessibility violations', async () => {
  const user = userEvent.setup();
  const field = armorClass();
  const { container } = render(<FieldWhy field={field} label={field.label} labels={labels} />);
  await user.tab();
  expect(document.activeElement?.textContent).toContain('Why?');
  expect(document.activeElement?.tagName).toBe('SUMMARY'); // the native disclosure; jsdom does not toggle it on Enter
  await expectNoAxeViolations(container);
});
