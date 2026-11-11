import { expect, test } from 'vitest';
import type { RulesFamilyPolicy } from '../api/types';
import { inspirationLabel } from '../format';

const families = [
  { id: 'srd-5.1', displayName: 'SRD 5.1 (2014 rules)', inspirationName: 'Inspiration' },
  { id: 'srd-5.2.1', displayName: 'SRD 5.2.1 (2024 rules)', inspirationName: 'Heroic Inspiration' },
] as RulesFamilyPolicy[];

test('inspirationLabel returns the name from the family policy', () => {
  expect(inspirationLabel(families, 'srd-5.1')).toBe('Inspiration');
  expect(inspirationLabel(families, 'srd-5.2.1')).toBe('Heroic Inspiration');
});

test('inspirationLabel falls back to Inspiration for an unknown family or an empty list', () => {
  expect(inspirationLabel(families, 'srd-9')).toBe('Inspiration');
  expect(inspirationLabel([], 'srd-5.2.1')).toBe('Inspiration');
});
