import { detectTransport, type CallOptions, type Transport } from './transport';
import type {
  AppInfo,
  Character,
  CharacterSummary,
  CharacterView,
  ContentOption,
  CreateCharacterRequest,
  ExportedPackage,
  ExportPreview,
  ExportPurpose,
  ImportResult,
  PackagePreview,
  RulesFamilyId,
  SaveOutcome,
  SourceChoice,
} from './types';

/** Typed application client. Components use this, never the transport or fetch directly. */
export function createClient(transport: Transport) {
  const call = <T>(command: string, payload?: unknown, options?: CallOptions) => transport(command, payload, options) as Promise<T>;
  return {
    info: () => call<AppInfo>('app.info'),
    listContent: (rulesFamily: RulesFamilyId) => call<ContentOption[]>('content.list', { rulesFamily }),
    listCharacters: () => call<CharacterSummary[]>('character.list'),
    getCharacter: (id: string) => call<CharacterView>('character.get', { id }),
    createCharacter: (request: CreateCharacterRequest) => call<CharacterView>('character.create', request),
    saveCharacter: (character: Character) => call<CharacterView>('character.save', character),
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
