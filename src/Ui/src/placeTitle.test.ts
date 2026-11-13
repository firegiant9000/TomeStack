import { expect, it } from 'vitest';
import type { Screen } from './App';
import type { CharacterView, PackagePreview } from './api/types';
import { placeOf, windowTitle } from './placeTitle';

const viewNamed = (name: string) => ({ character: { name } }) as unknown as CharacterView;
const view = viewNamed('Wren Ashdown');

const cases: [Screen, string][] = [
  [{ kind: 'home' }, 'Your characters'],
  [{ kind: 'add' }, 'Add a character'],
  [{ kind: 'add', start: 'import' }, 'Add a character'],
  [{ kind: 'builder', mode: { kind: 'create' } }, 'New character'],
  [{ kind: 'builder', mode: { kind: 'levelUp', view } }, 'Level up Wren Ashdown'],
  [{ kind: 'builder', mode: { kind: 'choices', view } }, 'Choices for Wren Ashdown'],
  [{ kind: 'studio' }, 'Homebrew'],
  [{ kind: 'sources' }, 'Books and PDFs'],
  [{ kind: 'campaigns' }, 'Campaigns'],
  [{ kind: 'gaps' }, 'Gap notes'],
  [{ kind: 'backups' }, 'Backups'],
  [{ kind: 'settings' }, 'Settings'],
  [{ kind: 'ddb-import' }, 'From a character sheet (PDF or text)'],
  [{ kind: 'sheet', view }, 'Wren Ashdown'],
  [{ kind: 'import', fileName: 'fixture.json', base64: '', preview: {} as PackagePreview }, 'Import fixture.json'],
];

it.each(cases)('placeOf %j', (screen, place) => {
  expect(placeOf(screen)).toBe(place);
});

it('a sheet without a usable name is "Character"', () => {
  expect(placeOf({ kind: 'sheet', view: viewNamed('  ') })).toBe('Character');
});

it('the title joins the place and TomeStack with an en dash (U+2013)', () => {
  expect(windowTitle({ kind: 'sheet', view })).toBe('Wren Ashdown – TomeStack');
});
