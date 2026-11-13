import type { Screen } from './App';

/** The place a screen names: the window title is `${placeOf(screen)} – TomeStack` (Wave 3 spec 3.11, row 12). */
export function placeOf(screen: Screen): string {
  switch (screen.kind) {
    case 'home':
      return 'Your characters';
    case 'add':
      return 'Add a character';
    case 'builder':
      return screen.mode.kind === 'create'
        ? 'New character'
        : screen.mode.kind === 'levelUp'
          ? `Level up ${screen.mode.view.character.name}`
          : `Choices for ${screen.mode.view.character.name}`;
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
      return screen.view.character.name.trim() || 'Character';
    case 'import':
      return `Import ${screen.fileName}`;
    default: {
      const unreachable: never = screen;
      return unreachable;
    }
  }
}

export function windowTitle(screen: Screen): string {
  return `${placeOf(screen)} – TomeStack`;
}
