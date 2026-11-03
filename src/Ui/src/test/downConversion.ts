// Invented fixtures for the "Save for an older TomeStack" panels: the releases on offer and the changes a copy lists.
import type { ExportChange, PackageTargets } from '../api/types';

export const packageTargetsFixture: PackageTargets = {
  current: '0.7.1',
  currentRow: '0.7.0',
  previousMinor: '0.6.0',
  targets: [
    { release: '0.6.0', characterSchema: 9, contentSchema: 10 },
    { release: '0.5.0', characterSchema: 8, contentSchema: 9 },
  ],
};

export const firstRadio = '0.7.0 or newer (everything)';
export const confirmLabel = 'I understand this copy leaves out the items listed, and my own copy is unchanged';
export const blockedHint = 'This file is not made while something blocks it.';
export const failedLine = (target: string) => `Could not check what the copy for TomeStack ${target} leaves out.`;

export function exportChange(action: ExportChange['action'], message = `Compat ${action} message`): ExportChange {
  return { entry: 'content/compat-tide-stance', kind: 'content', id: 'compat-tide-stance', name: 'Compat Tide Stance', action, code: `code.${action}`, message };
}

/** The accessible description of an element, resolved by id (jest-dom is not installed). */
export function describedBy(element: HTMLElement): string | undefined {
  const id = element.getAttribute('aria-describedby');
  return id ? document.getElementById(id)?.textContent ?? undefined : undefined;
}
