import { detectTransport, type CallOptions, type Transport } from './transport';
import type {
  AffectedCharacter,
  AppInfo,
  ArchivePreview,
  AttachmentInfo,
  Campaign,
  CandidateCheck,
  CandidateEdit,
  CandidateFilter,
  ImportJob,
  ImportSearchHit,
  StoredCandidate,
  DetachPreview,
  OpenPageOutcome,
  ContentRevision,
  PublishResult,
  SourceRecord,
  StudioEntry,
  UpdateOffer,
  UpdateReview,
  Character,
  CharacterSummary,
  CharacterView,
  ContentOption,
  ContentReference,
  CreateCharacterRequest,
  ExportedPackage,
  ExportPreview,
  ExportPurpose,
  GapNote,
  GapNoteListing,
  GapNoteStatus,
  GapTarget,
  ImportResult,
  HitDieRoll,
  LibraryBackupOutcome,
  LibraryBackupPreview,
  LibraryRestoreChoice,
  LibraryRestoreResult,
  PackagePreview,
  PlayAction,
  RestPeriod,
  RestPreview,
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
    /** With a campaign, every option says whether the campaign allows its source (SPEC P-01). */
    listContent: (rulesFamily: RulesFamilyId, campaignId?: string) => call<ContentOption[]>('content.list', { rulesFamily, campaignId }),
    listCampaigns: () => call<Campaign[]>('campaign.list'),
    /** Creates (empty id) or updates a campaign profile. */
    saveCampaign: (campaign: Campaign) => call<Campaign>('campaign.save', campaign),
    deleteCampaign: (id: string) => call<{ deleted: boolean }>('campaign.delete', { id }),
    /** Schema, reference, formula and cycle problems for a stored revision; writes nothing. */
    validateContent: (reference: ContentReference) => call<ValidationReport>('content.validate', { reference }),
    /** The same checks for an unsaved revision (the studio's "Check"). */
    validateRevision: (revision: ContentRevision) => call<ValidationReport>('content.validate', { revision }),
    /** Stores an inactive draft under a new revision id (drafts are insert-only too). */
    saveDraft: (revision: ContentRevision) => call<ContentReference>('content.saveDraft', { revision }),
    /** Validates again and publishes a new immutable revision; no character changes (SPEC I-06). */
    publish: (reference: ContentReference) => call<PublishResult>('content.publish', { reference }),
    contentRevisions: (contentId: string) => call<ContentRevision[]>('content.revisions', { contentId }),
    affected: (contentId: string) => call<AffectedCharacter[]>('content.affected', { contentId }),
    contentBySource: (sourceId: string) => call<StudioEntry[]>('content.bySource', { sourceId }),
    listSources: () => call<SourceRecord[]>('source.list'),
    createHomebrewSource: (title: string, rulesFamilies: RulesFamilyId[]) =>
      call<SourceRecord>('source.createHomebrew', { title, rulesFamilies }),
    /** The source's PDF (no path), or `{ attached: false }`. */
    attachment: async (sourceId: string) => {
      const result = await call<AttachmentInfo | { attached: false }>('source.attachment', { sourceId });
      return 'attachmentId' in result ? result : undefined;
    },
    /** Native Open dialog in the shell (fails with `unsupported` elsewhere). No timeout: it waits for the dialog. */
    attachPdf: (sourceId: string, mode: 'managed' | 'linked') =>
      call<{ attached: boolean; attachment?: AttachmentInfo }>('source.attachPdf', { sourceId, mode }, { timeoutMs: null }),
    /** Browser development: the chosen file's bytes, copied into the managed library. */
    attachPdfData: (sourceId: string, fileName: string, base64: string) =>
      call<AttachmentInfo>('source.attachPdfData', { sourceId, fileName, base64 }, { timeoutMs: null }),
    detachPreview: (sourceId: string) => call<DetachPreview>('source.detachPreview', { sourceId }),
    /** Removes the PDF after the preview was shown; content stays (SPEC S-04). */
    detach: (sourceId: string) => call<{ detached: boolean }>('source.detach', { sourceId, confirm: true }),
    /** Opens the cited page in the shell's offline PDF viewer (fails with `unsupported` outside the desktop app). */
    openPage: (sourceId: string, page: number) => call<OpenPageOutcome>('source.openPage', { sourceId, page }),
    /** SPEC I-03: a page range (or the whole document) of an attached PDF as a draft reference-only entry; nothing is extracted. */
    importPages: (sourceId: string, request: { start?: number; end?: number; title?: string; wholeDocument?: boolean }) =>
      call<ContentRevision>('source.importPages', { sourceId, ...request }),
    // ---- PDF import (M4): extraction jobs, search and candidate review. Accepting creates a draft only (ADR-004). ----
    startImport: (sourceId: string, request: { firstPage?: number; lastPage?: number; wholeDocument?: boolean }) =>
      call<ImportJob>('import.start', { sourceId, ...request }),
    importStatus: (jobId: string) => call<ImportJob>('import.status', { jobId }),
    listImports: (sourceId: string) => call<ImportJob[]>('import.list', { sourceId }),
    cancelImport: (jobId: string) => call<ImportJob>('import.cancel', { jobId }, { timeoutMs: null }),
    resumeImport: (jobId: string) => call<ImportJob>('import.resume', { jobId }),
    /** SPEC I-03: pages of one source whose extracted text contains the query; writes nothing. */
    searchImport: (sourceId: string, query: string) => call<ImportSearchHit[]>('import.search', { sourceId, query }),
    candidates: (jobId: string, filter: CandidateFilter = {}) => call<StoredCandidate[]>('import.candidates', { jobId, ...filter }),
    /** Validation, dependencies and blockers of accepting; writes nothing. */
    checkCandidate: (candidateId: string) => call<CandidateCheck>('import.candidate.check', { candidateId }),
    editCandidate: (candidateId: string, edit: CandidateEdit) => call<StoredCandidate>('import.candidate.edit', { candidateId, ...edit }),
    /** Only the "Accept" buttons call this; the result is a draft revision, never active until published. */
    acceptCandidate: (candidateId: string, asReference: boolean) =>
      call<StoredCandidate>('import.candidate.accept', { candidateId, asReference, confirm: true }),
    ignoreCandidate: (candidateId: string) => call<StoredCandidate>('import.candidate.ignore', { candidateId }),
    /** What moving a character to another revision would change; writes nothing. */
    reviewUpdate: (characterId: string, from: ContentReference, to: ContentReference) =>
      call<UpdateReview>('character.reviewUpdate', { characterId, from, to }),
    /** Newer revisions of content the character uses (M3 C7); writes nothing. */
    availableUpdates: (characterId: string) => call<UpdateOffer[]>('character.updates', { characterId }),
    /** Applies a reviewed update; only the "Apply update" button calls this. */
    applyUpdate: (characterId: string, from: ContentReference, to: ContentReference) =>
      call<CharacterView>('character.applyUpdate', { characterId, from, to, confirm: true }),
    listCharacters: () => call<CharacterSummary[]>('character.list'),
    /** What archiving would do (SPEC C-08); writes nothing. */
    archivePreview: (characterId: string) => call<ArchivePreview>('character.archivePreview', { characterId }),
    /** Only the "Archive" confirmation calls this; nothing is deleted. */
    archiveCharacter: (characterId: string) => call<CharacterSummary>('character.archive', { characterId, confirm: true }),
    unarchiveCharacter: (characterId: string) => call<CharacterSummary>('character.unarchive', { characterId }),
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
    /** What a rest would change; writes nothing (D01). A short rest spends `hitDice`, each with what the die shows. */
    restPreview: (characterId: string, kind: RestPeriod, hitDice: HitDieRoll[] = []) =>
      call<RestPreview>('character.restPreview', { characterId, kind, hitDice }),
    /** Applies exactly the previewed rest (same hit dice), minus the unticked changes. Only the "Finish … rest" button calls this. */
    rest: (characterId: string, kind: RestPeriod, basis: string, skip: string[], hitDice: HitDieRoll[] = []) =>
      call<CharacterView>('character.rest', { characterId, kind, basis, skip, hitDice, confirm: true }),
    /** Rolls and returns the record; never changes the character, even when the roll names a resource (SPEC C-04). */
    roll: (characterId: string, target: RollTarget) => call<RollRecord>('roll', { characterId, ...target }),
    /** The character's gap notes, open first (M3 B3). Stored locally; never sent anywhere. */
    listGapNotes: (characterId: string) => call<GapNote[]>('gap.list', { characterId }),
    /** Every character's notes with the character's name, open first (M3 C5). Writes nothing. */
    listAllGapNotes: () => call<GapNoteListing[]>('gap.listAll'),
    /** Only the note's "Save note" button calls this. */
    addGapNote: (characterId: string, target: GapTarget, text: string) => call<GapNote>('gap.add', { characterId, target, text }),
    setGapNoteStatus: (id: string, status: GapNoteStatus) => call<GapNote>('gap.setStatus', { id, status }),
    /** Only the "Delete note" confirmation calls this. */
    deleteGapNote: (id: string) => call<{ deleted: boolean }>('gap.delete', { id, confirm: true }),
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
    // ---- full library backup (M2.1): native dialogs only; no path or backup bytes cross the bridge ----
    libraryBackupPreview: () => call<LibraryBackupPreview>('library.backupPreview'),
    /** Native Save dialog, then writes everything (PDFs included). No timeout: it waits for the dialog and the copy. */
    saveLibraryBackup: () => call<LibraryBackupOutcome>('library.backupSaveAs', undefined, { timeoutMs: null }),
    /** Native Open dialog, then checks the whole file (every PDF too); writes nothing. */
    chooseLibraryRestore: () => call<LibraryRestoreChoice>('library.restoreChoose', undefined, { timeoutMs: null }),
    /** Only the preview's "Restore" button calls this. */
    applyLibraryRestore: (token: string, sourceChoices: Record<string, SourceChoice> = {}) =>
      call<LibraryRestoreResult>('library.restoreApply', { token, sourceChoices, confirm: true }, { timeoutMs: null }),
  };
}

export type TomeStackClient = ReturnType<typeof createClient>;

export const client: TomeStackClient = createClient(detectTransport());
