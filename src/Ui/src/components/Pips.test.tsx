// @vitest-environment jsdom
// Pips beside a "N of M" text (slots, resources): decoration only, hidden from assistive tech, never text.
import { cleanup, render } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { Pips } from './Pips';

afterEach(cleanup);

it('draws one pip per unit, filled for the remaining ones, hidden from assistive tech', () => {
  const { container } = render(<Pips filled={2} total={4} />);
  const row = container.querySelector('.pips')!;
  expect(row.getAttribute('aria-hidden')).toBe('true');
  expect(row.textContent).toBe('');
  expect(Array.from(row.querySelectorAll<HTMLElement>('.pip')).map((p) => p.dataset.filled)).toEqual(['true', 'true', 'false', 'false']);
});

it('caps the drawing at twelve pips and counts the rest in an attribute', () => {
  const { container } = render(<Pips filled={15} total={20} />);
  expect(container.querySelectorAll('.pip')).toHaveLength(12);
  expect(container.querySelector<HTMLElement>('.pip-more')!.dataset.more).toBe('8');
});

it('renders nothing for a total of zero', () => {
  const { container } = render(<Pips filled={0} total={0} />);
  expect(container.querySelector('.pips')).toBeNull();
});
