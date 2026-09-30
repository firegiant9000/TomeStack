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
  SourcePackPreview,
  CampaignPackPreview,
  ExtensionInstallPreview,
  ExtensionRunPreview,
  ExtensionRunRequest,
  InstalledExtension,
  ExportTarget,
  SheetPurpose,
  VttExportPreview,
  SourceRecord,
  StudioEntry,
  UpdateOffer,
  UpdateReview,
  Character,
  CharacterSummary,
  CharacterView,
  CompareRequest,
  ContentComparison,
  ContentTreeView,
  ContentOption,
  ContentReference,
  CreateCharacterRequest,
  DebugReport,
  DesignHint,
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
  RestorePreview,
  RestoreResult,
  RulesFamilyId,
  SandboxRequest,
  SnapshotPage,
  SnapshotSummary,
  SandboxView,
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
    /** The homebrew debugger (M5 slice 2): a whole source, drafts included, or one unsaved revision. Writes nothing. */
    diagnoseSource: (sourceId: string) => call<DebugReport>('content.diagnose', { sourceId }),
    diagnoseRevision: (revision: ContentRevision) => call<DebugReport>('content.diagnose', { revision }),
    /** "Try it" (M5 slice 3): a draft on an unsaved copy or a blank character, calculated as if published. Saves nothing. */
    sandbox: (request: SandboxRequest) => call<SandboxView>('content.sandbox', request),
    /** Diff two revisions of one content and run both on unsaved copies (M5 slice 4). Writes and applies nothing. */
    compare: (request: CompareRequest) => call<ContentComparison>('content.compare', request),
    /** The relationship tree of the unsaved revision on screen, among its source's drafts (M5 slice 5). Writes nothing. */
    tree: (revision: ContentRevision) => call<ContentTreeView>('content.tree', { revision }),
    /** Design hints for the unsaved revision on screen (M5 slice 7), asked only while the setting is on. Writes nothing. */
    feedback: (revision: ContentRevision) => call<DesignHint[]>('content.feedback', { revision }),
    listSources: () => call<SourceRecord[]>('source.list'),
    createHomebrewSource: (title: string, rulesFamilies: RulesFamilyId[]) =>
      call<SourceRecord>('source.createHomebrew', { title, rulesFamilies }),
    /**
     * M6 slice 1, "Mark as shareable": `shareable` needs `confirmOwnWork` (the author's statement that the source is their
     * own work). The service refuses bundled, import-derived and received sources. Stopping sharing needs no confirmation.
     */
    setShareable: (sourceId: string, shareable: boolean, confirmOwnWork = false) =>
      call<SourceRecord>('source.setShareable', { sourceId, shareable, confirmOwnWork }),
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
    /** M5 slice 8: snapshots, taken by hand; a restore needs the preview's one-use token and a confirmation. */
    takeSnapshot: (characterId: string, label?: string) => call<SnapshotSummary>('character.snapshot', { characterId, label }),
    snapshots: (characterId: string, before?: string) => call<SnapshotPage>('character.snapshots', { characterId, before }),
    restorePreview: (characterId: string, snapshotId: string) => call<RestorePreview>('character.restorePreview', { characterId, snapshotId }),
    restoreSnapshot: (token: string) => call<RestoreResult>('character.restoreSnapshot', { token, confirm: true }),
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
    // ---- source packs (M6 slice 1): only sources you marked as shareable; import goes through previewImport/applyImport ----
    sourcePackPreview: (sourceIds: string[]) => call<SourcePackPreview>('package.sourcePackPreview', { sourceIds }),
    exportSourcePack: (sourceIds: string[]) => call<ExportedPackage>('package.sourcePackExport', { sourceIds }),
    /** Native Save dialog in the shell; `unsupported` elsewhere (DevHost). No timeout, as for saveExportAs. */
    saveSourcePackAs: (sourceIds: string[]) =>
      call<SaveOutcome>('package.sourcePackSaveAs', { sourceIds }, { timeoutMs: null }),
    // ---- campaign packs (M6 slice 2): the profile and the shareable content of its allowed sources ----
    campaignPackPreview: (campaignId: string) => call<CampaignPackPreview>('package.campaignPackPreview', { campaignId }),
    exportCampaignPack: (campaignId: string) => call<ExportedPackage>('package.campaignPackExport', { campaignId }),
    /** Native Save dialog in the shell; `unsupported` elsewhere (DevHost). No timeout, as for saveExportAs. */
    saveCampaignPackAs: (campaignId: string) =>
      call<SaveOutcome>('package.campaignPackSaveAs', { campaignId }, { timeoutMs: null }),
    // ---- extensions (M6 slice 3, ADR-011): declarative only; every run previews first ----
    listExtensions: () => call<InstalledExtension[]>('extension.list'),
    /** Checks an extension file and shows what it asks for; installs nothing. */
    previewExtensionInstall: (base64: string) => call<ExtensionInstallPreview>('extension.installPreview', { base64 }),
    /** Native Open dialog, then the same check. `unsupported` elsewhere (DevHost). */
    chooseExtensionInstall: () =>
      call<{ chosen: boolean; fileName?: string; preview?: ExtensionInstallPreview }>('extension.installChoose', undefined, { timeoutMs: null }),
    /** Only the install preview's button calls this, with the permissions the user ticked. */
    installExtension: (token: string, grants: string[]) => call<InstalledExtension>('extension.install', { token, grants, confirm: true }),
    /** The install review of an installed extension, read from its stored file (to grant permissions again after a restore). */
    reviewExtension: (extensionId: string) => call<ExtensionInstallPreview>('extension.review', { extensionId }),
    setExtensionEnabled: (extensionId: string, enabled: boolean) => call<InstalledExtension>('extension.setEnabled', { extensionId, enabled }),
    /** Only the remove confirmation calls this. Drafts the extension made stay. */
    removeExtension: (extensionId: string) => call<{ removed: boolean }>('extension.remove', { extensionId, confirm: true }),
    /** Native Open dialog for an import hook's file; the path never reaches the page. `unsupported` elsewhere. */
    chooseExtensionInput: () => call<{ chosen: boolean; token?: string; fileName?: string }>('extension.chooseInput', undefined, { timeoutMs: null }),
    previewExtensionRun: (request: ExtensionRunRequest) => call<ExtensionRunPreview>('extension.runPreview', request),
    /** Only the import preview's button calls this. */
    runExtensionImport: (token: string) => call<{ sourceId: string; sourceTitle: string; drafts: number }>('extension.runImport', { token, confirm: true }),
    /** Browser development: the previewed output as base64. */
    runExtensionExport: (token: string) => call<{ fileName: string; base64: string }>('extension.runExport', { token }),
    /** Native Save dialog; `unsupported` elsewhere. No timeout: it waits for the dialog. */
    saveExtensionOutputAs: (token: string) => call<SaveOutcome>('extension.runSaveAs', { token }, { timeoutMs: null }),
    // ---- export adapters (M6 slice 4, ADR-012): a file to import by hand; nothing is uploaded ----
    previewVttExport: (characterId: string, target: ExportTarget, purpose: SheetPurpose) =>
      call<VttExportPreview>('export.preview', { characterId, target, purpose }),
    /** Native Save dialog; `unsupported` elsewhere (DevHost). No timeout: it waits for the dialog. */
    saveVttExportAs: (token: string) => call<SaveOutcome>('export.saveAs', { token }, { timeoutMs: null }),
    /** Browser development: the previewed file as base64. */
    downloadVttExport: (token: string) => call<{ fileName: string; base64: string }>('export.download', { token }),
    previewImport: (base64: string) => call<PackagePreview>('package.preview', { base64 }),
    /** `campaignChoices`: for a campaign pack whose campaign differs from yours (M6 slice 2). */
    applyImport: (base64: string, sourceChoices: Record<string, SourceChoice> = {}, campaignChoices: Record<string, SourceChoice> = {}) =>
      call<ImportResult>('package.apply', { base64, sourceChoices, campaignChoices }),
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
