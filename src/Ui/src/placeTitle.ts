import type { Screen } from './App';
import type { BuilderMode } from './components/CharacterBuilder';

/** The name shown for a character; a blank name reads "Character". */
export function characterNameOf(name: string): string {
  return name.trim() || 'Character';
}

/** The import screen's heading and place name (no file name: it would put a path-like string in the window title). */
export const IMPORT_PLACE = 'Import a TomeStack file';

/** The builder's heading and place name, so the h1 and the window title cannot drift. */
export function builderTitle(mode: BuilderMode): string {
  switch (mode.kind) {
    case 'create':
      return 'New character';
    case 'levelUp':
      return `Level up ${characterNameOf(mode.view.character.name)}`;
    default:
      return `Choices for ${characterNameOf(mode.view.character.name)}`;
  }
}

/** The place a screen names: the window title is `${placeOf(screen)} – TomeStack` (Wave 3 spec 3.11, row 12). */
export function placeOf(screen: Screen): string {
  switch (screen.kind) {
    case 'home':
      return 'Your characters';
    case 'add':
      return 'Add a character';
    case 'builder':
      return builderTitle(screen.mode);
    case 'studio':
      return 'Homebrew';
    case 'sources':
      return 'Books and PDFs';
    case 'campaigns':
      return 'Campaigns';
    case 'gaps':
      return 'Gap notes';
    case 'backups':
      return 'Backups';
    case 'settings':
      return 'Settings';
    case 'ddb-import':
      return 'From a character sheet (PDF or text)';
    case 'sheet':
      return characterNameOf(screen.view.character.name);
    case 'import':
      return IMPORT_PLACE;
    default: {
      const unreachable: never = screen;
      return unreachable;
    }
  }
}

export function windowTitle(screen: Screen): string {
  return `${placeOf(screen)} – TomeStack`;
}
