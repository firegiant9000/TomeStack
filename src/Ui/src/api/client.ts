import { detectTransport, type Transport } from './transport';
import type {
  AppInfo,
  Character,
  CharacterSummary,
  CharacterView,
  ContentOption,
  CreateCharacterRequest,
  ExportedPackage,
  ImportResult,
  PackagePreview,
  RulesFamilyId,
} from './types';

/** Typed application client. Components use this, never the transport or fetch directly. */
export function createClient(transport: Transport) {
  const call = <T>(command: string, payload?: unknown) => transport(command, payload) as Promise<T>;
  return {
    info: () => call<AppInfo>('app.info'),
    listContent: (rulesFamily: RulesFamilyId) => call<ContentOption[]>('content.list', { rulesFamily }),
    listCharacters: () => call<CharacterSummary[]>('character.list'),
    getCharacter: (id: string) => call<CharacterView>('character.get', { id }),
    createCharacter: (request: CreateCharacterRequest) => call<CharacterView>('character.create', request),
    saveCharacter: (character: Character) => call<CharacterView>('character.save', character),
    exportCharacters: (characterIds: string[]) => call<ExportedPackage>('package.export', { characterIds }),
    previewImport: (base64: string) => call<PackagePreview>('package.preview', { base64 }),
    applyImport: (base64: string) => call<ImportResult>('package.apply', { base64 }),
  };
}

export type TomeStackClient = ReturnType<typeof createClient>;

export const client: TomeStackClient = createClient(detectTransport());
