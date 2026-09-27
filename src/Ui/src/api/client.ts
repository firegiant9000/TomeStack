import { detectTransport, type CallOptions, type Transport } from './transport';
import type {
  AppInfo,
  Character,
  CharacterSummary,
  CharacterView,
  ContentOption,
  ContentReference,
  CreateCharacterRequest,
  ExportedPackage,
  ExportPreview,
  ExportPurpose,
  ImportResult,
  PackagePreview,
  PlayAction,
  RollRecord,
  RollTarget,
  RulesFamilyId,
  SaveOutcome,
  SourceChoice,
  ValidationReport,
} from './types';

/** Typed application client. Components use this, never the transport or fetch directly. */
export function createClient(transport: Transport) {
  const call = <T>(command: string, payload?: unknown, options?: CallOptions) => transport(command, payload, options) as Promise<T>;
  return {
    info: () => call<AppInfo>('app.info'),
    listContent: (rulesFamily: RulesFamilyId) => call<ContentOption[]>('content.list', { rulesFamily }),
    /** Schema, reference, formula and cycle problems for a stored revision; writes nothing. */
    validateContent: (reference: ContentReference) => call<ValidationReport>('content.validate', { reference }),
    listCharacters: () => call<CharacterSummary[]>('character.list'),
    getCharacter: (id: string) => call<CharacterView>('character.get', { id }),
    createCharacter: (request: CreateCharacterRequest) => call<CharacterView>('character.create', request),
    saveCharacter: (character: Character) => call<CharacterView>('character.save', character),
    /** Records the options picked for one choice; an empty list clears it (SPEC C-01). */
    choose: (characterId: string, source: ContentReference, choiceId: string, selected: ContentReference[]) =>
      call<CharacterView>('character.choose', { characterId, source, choiceId, selected }),
    /** The sheet of an unsaved builder draft, checked like a save; writes nothing (SPEC C-07). */
    preview: (draft: Character) => call<CharacterView>('character.preview', draft),
    /** A choice answered on an unsaved draft, checked like `choose`; the result is the next draft. Writes nothing. */
    previewChoice: (draft: Character, source: ContentReference, choiceId: string, selected: ContentReference[]) =>
      call<CharacterView>('character.previewChoice', { draft, source, choiceId, selected }),
    /** One play-state change; `confirm` is always sent because only a deliberate button press calls this (SPEC C-05). */
    play: (characterId: string, action: PlayAction) => call<CharacterView>('character.play', { characterId, ...action, confirm: true }),
    /** Rolls and returns the record; never changes the character, even when the roll names a resource (SPEC C-04). */
    roll: (characterId: string, target: RollTarget) => call<RollRecord>('roll', { characterId, ...target }),
    /** What an export would contain and leave out, without writing anything (ADR-007). */
    previewExport: (characterIds: string[], purpose: ExportPurpose) =>
      call<ExportPreview>('package.exportPreview', { characterIds, purpose }),
    exportCharacters: (characterIds: string[], purpose: ExportPurpose = 'backup') =>
      call<ExportedPackage>('package.export', { characterIds, purpose }),
    /**
     * Native Save dialog in the shell; fails with code `unsupported` on hosts without one (DevHost). No timeout: the
     * response waits for the user to close the dialog.
     */
    saveExportAs: (characterIds: string[], purpose: ExportPurpose = 'backup') =>
      call<SaveOutcome>('package.saveAs', { characterIds, purpose }, { timeoutMs: null }),
    previewImport: (base64: string) => call<PackagePreview>('package.preview', { base64 }),
    applyImport: (base64: string, sourceChoices: Record<string, SourceChoice> = {}) =>
      call<ImportResult>('package.apply', { base64, sourceChoices }),
  };
}

export type TomeStackClient = ReturnType<typeof createClient>;

export const client: TomeStackClient = createClient(detectTransport());
