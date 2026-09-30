using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// Transport-neutral JSON command protocol (ADR-006). Request:
/// <c>{ "id": "1", "command": "character.get", "payload": { ... } }</c>. Response:
/// <c>{ "id": "1", "ok": true, "result": ... }</c> or <c>{ "id": "1", "ok": false, "error": { "code", "message", "diagnostics", "correlationId" } }</c>.
/// Unexpected failures return a generic message and a correlation id; details go to the local error log only.
/// </summary>
public sealed class CommandDispatcher(TomeStackApp app, IErrorLog? errorLog = null, IHostServices? host = null)
{
    private readonly IErrorLog _errorLog = errorLog ?? app.ErrorLog;

    /// <summary>Base64 package payloads dominate; this bounds a 50 MB package plus envelope.</summary>
    public const int MaxRequestChars = 72 * 1024 * 1024;

    public static IReadOnlyList<string> Commands { get; } =
    [
        "app.info", "content.list", "campaign.list", "campaign.save", "campaign.delete","content.validate", "content.saveDraft", "content.publish", "content.revisions", "content.affected",
        "content.bySource", "content.diagnose", "content.sandbox", "content.compare", "content.tree", "content.feedback", "source.list", "source.createHomebrew",
        "source.attachment", "source.attachPdf", "source.attachPdfData", "source.detachPreview", "source.detach", "source.openPage", "source.importPages",
        "character.list", "character.get", "character.create", "character.save", "character.choose", "character.preview", "character.previewChoice",
        "character.play", "character.restPreview", "character.rest", "character.reviewUpdate", "character.applyUpdate", "character.updates", "character.mechanics", "roll",
        "character.archivePreview", "character.archive", "character.unarchive",
        "character.snapshot", "character.snapshots", "character.restorePreview", "character.restoreSnapshot",
        "gap.list", "gap.listAll", "gap.add", "gap.setStatus", "gap.delete",
        "import.start", "import.status", "import.list", "import.cancel", "import.resume", "import.audit", "import.search", "import.page", "import.candidates",
        "import.candidate.check", "import.candidate.edit", "import.candidate.accept", "import.candidate.ignore",
        "package.exportPreview", "package.export", "package.saveAs", "package.preview", "package.apply",
        "library.backupPreview", "library.backupSaveAs", "library.restoreChoose", "library.restoreApply",
    ];

    /// <summary>
    /// M2.1: backup files chosen in the native Open dialog, by the token the UI got with the preview. The path stays here;
    /// only the file name reaches the page.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, ChosenBackup> _chosenBackups = new();

    public string Dispatch(string requestJson)
    {
        ArgumentNullException.ThrowIfNull(requestJson);
        string? id = null;
        string command = "(unparsed)";
        try
        {
            if (requestJson.Length > MaxRequestChars)
                return Error(null, "request.too-large", "Request exceeds the size limit.");
            var request = JsonSerializer.Deserialize<CommandRequest>(requestJson, RulesJson.Compact)
                ?? throw new JsonException("Empty request.");
            id = request.Id;
            command = request.Command ?? throw new JsonException("Request has no command.");
            var result = Execute(command, request.Payload);
            return JsonSerializer.Serialize(new { id, ok = true, result }, RulesJson.Compact);
        }
        catch (AppValidationException ex)
        {
            return Error(id, ex.Code, ex.Message, ex.Problems);
        }
        catch (PackageException ex)
        {
            return Error(id, "package", ex.Message, ex.Errors);
        }
        catch (JsonException ex)
        {
            // System.Text.Json messages name a JSON path and position, never a file path.
            return Error(id, "bad-request", ex.Message);
        }
        catch (UnknownCommandException ex)
        {
            return Error(id, "bad-request", ex.Message);
        }
        catch (FormatException)
        {
            return Error(id, "bad-request", "The request contains a value in the wrong format (for example, package data that is not base64).");
        }
        catch (Exception ex)
        {
            // Transport boundary: one failing command must not take down the shell or host. The message of an
            // unexpected exception can contain paths or internals, so it stays in the local log.
            var correlationId = FileErrorLog.NewCorrelationId();
            _errorLog.Record(correlationId, command, ex);
            return Error(id, "internal", $"Something went wrong. Reference {correlationId}; details are in the local error log.", correlationId: correlationId);
        }
    }

    private object Execute(string command, JsonElement? payload) => command switch
    {
        "app.info" => app.GetInfo(),
        "content.list" => ListContent(Payload<RulesFamilyPayload>(payload)),
        "campaign.list" => app.ListCampaigns(),
        "campaign.save" => app.SaveCampaign(Payload<Campaign>(payload)),
        "campaign.delete" => DeleteCampaign(Payload<IdPayload>(payload)),
        "content.validate" => Validate(Payload<ContentPayload>(payload)),
        "content.saveDraft" => app.SaveDraft(Payload<ContentPayload>(payload).Revision ?? throw new JsonException("content.saveDraft needs a revision.")),
        "content.publish" => app.Publish(Payload<ContentPayload>(payload).Reference ?? throw new JsonException("content.publish needs a reference.")),
        "content.revisions" => app.ListRevisions(Payload<ContentIdPayload>(payload).ContentId),
        "content.affected" => app.AffectedCharacters(Payload<ContentIdPayload>(payload).ContentId),
        "content.bySource" => app.ContentBySource(Payload<SourceIdPayload>(payload).SourceId),
        "content.diagnose" => Diagnose(Payload<DiagnoseRequest>(payload)),
        "content.sandbox" => Sandbox(Payload<SandboxRequest>(payload)),
        "content.compare" => app.Compare(Payload<CompareRequest>(payload)),
        "content.tree" => app.Tree(Payload<DiagnoseRequest>(payload)),
        "content.feedback" => app.Feedback(Payload<DiagnoseRequest>(payload)),
        "source.list" => app.ListSources(),
        "source.createHomebrew" => app.CreateHomebrewSource(Payload<HomebrewSourceRequest>(payload)),
        "source.attachment" => (object?)app.GetAttachment(Payload<SourceIdPayload>(payload).SourceId) ?? new { attached = false },
        "source.attachPdf" => AttachPdf(Payload<AttachPayload>(payload)),
        "source.attachPdfData" => AttachPdfData(Payload<AttachDataPayload>(payload)),
        "source.detachPreview" => app.PreviewDetach(Payload<SourceIdPayload>(payload).SourceId),
        "source.detach" => Detach(Payload<DetachPayload>(payload)),
        "source.openPage" => OpenPage(Payload<OpenPagePayload>(payload)),
        "source.importPages" => app.ImportPages(Payload<PageImportRequest>(payload)),
        "character.reviewUpdate" => ReviewUpdate(Payload<UpdatePayload>(payload)),
        "character.updates" => app.AvailableUpdates(Payload<CharacterIdPayload>(payload).CharacterId),
        "character.applyUpdate" => ApplyUpdate(Payload<UpdatePayload>(payload)),
        "roll" => app.Roll(Payload<RollCommand>(payload)),
        "character.list" => app.ListCharacters(),
        "character.get" => app.GetCharacter(Payload<IdPayload>(payload).Id),
        "character.create" => app.CreateCharacter(Payload<CreateCharacterRequest>(payload)),
        "character.save" => app.SaveCharacter(Payload<Character>(payload)),
        "character.choose" => app.Choose(Payload<ChooseRequest>(payload)),
        "character.preview" => app.Preview(Payload<Character>(payload)),
        "character.previewChoice" => app.PreviewChoice(Payload<PreviewChoiceRequest>(payload)),
        "character.play" => app.Play(Payload<PlayCommand>(payload)),
        "character.restPreview" => PreviewRest(Payload<RestPreviewPayload>(payload)),
        "character.rest" => app.Rest(Payload<RestRequest>(payload)),
        "character.mechanics" => app.Mechanics(Payload<MechanicsRequest>(payload)),
        "character.archivePreview" => app.PreviewArchive(Payload<CharacterIdPayload>(payload).CharacterId),
        "character.archive" => app.Archive(Payload<ArchiveRequest>(payload)),
        "character.unarchive" => app.Unarchive(Payload<CharacterIdPayload>(payload).CharacterId),
        "character.snapshot" => app.Snapshot(Payload<SnapshotRequest>(payload)),
        "character.snapshots" => ListSnapshots(Payload<SnapshotsPayload>(payload)),
        "character.restorePreview" => app.PreviewRestore(Payload<RestorePreviewRequest>(payload)),
        "character.restoreSnapshot" => app.RestoreSnapshot(Payload<RestoreSnapshotRequest>(payload)),
        "gap.list" => app.ListGapNotes(Payload<CharacterIdPayload>(payload).CharacterId),
        "gap.listAll" => app.ListAllGapNotes(),
        "import.start" => app.StartImport(Payload<ImportStartRequest>(payload)),
        "import.status" => app.ImportStatus(Payload<ImportJobRequest>(payload).JobId),
        "import.list" => app.ListImports(payload is { ValueKind: JsonValueKind.Object } ? Payload<ImportListRequest>(payload).SourceId : null),
        "import.cancel" => app.CancelImport(Payload<ImportJobRequest>(payload).JobId),
        "import.resume" => app.ResumeImport(Payload<ImportJobRequest>(payload).JobId),
        "import.audit" => app.ImportAudit(Payload<ImportJobRequest>(payload).JobId),
        "import.search" => app.SearchImportedText(Payload<ImportSearchRequest>(payload)),
        "import.page" => app.ImportedPage(Payload<ImportPageRequest>(payload)),
        "import.candidates" => app.ListCandidates(Payload<ImportCandidatesRequest>(payload)),
        "import.candidate.check" => app.CheckCandidate(Payload<CandidateRequest>(payload).CandidateId),
        "import.candidate.edit" => app.EditCandidate(Payload<CandidateEditRequest>(payload)),
        "import.candidate.accept" => app.AcceptCandidate(Payload<CandidateAcceptRequest>(payload)),
        "import.candidate.ignore" => app.IgnoreCandidate(Payload<CandidateRequest>(payload).CandidateId),
        "gap.add" => app.AddGapNote(Payload<AddGapNoteRequest>(payload)),
        "gap.setStatus" => app.SetGapNoteStatus(Payload<GapNoteStatusRequest>(payload)),
        "gap.delete" => DeleteGapNote(Payload<DeleteGapNoteRequest>(payload)),
        "package.exportPreview" => PreviewExport(Payload<ExportPayload>(payload)),
        "package.export" => ExportPackage(Payload<ExportPayload>(payload)),
        "package.saveAs" => SavePackageAs(Payload<ExportPayload>(payload)),
        "package.preview" => app.PreviewImport(Convert.FromBase64String(Payload<PackagePayload>(payload).Base64)),
        "package.apply" => ApplyImport(Payload<PackagePayload>(payload)),
        "library.backupPreview" => app.PreviewLibraryBackup(),
        "library.backupSaveAs" => SaveLibraryBackupAs(),
        "library.restoreChoose" => ChooseLibraryRestore(),
        "library.restoreApply" => ApplyLibraryRestore(Payload<RestorePayload>(payload)),
        _ => throw new UnknownCommandException(command),
    };

    private object Validate(ContentPayload payload)
    {
        var report = app.ValidateContent(payload.Reference, payload.Revision);
        return new { report.Revision, report.Errors, report.Warnings, report.CanPublish };
    }

    private object Diagnose(DiagnoseRequest payload)
    {
        var report = app.Diagnose(payload);
        return new { report.Scope, report.Findings, report.Errors, report.Warnings, report.Truncated };
    }

    private object Sandbox(SandboxRequest payload)
    {
        var result = app.Sandbox(payload);
        var validation = new { result.Validation.Revision, result.Validation.Errors, result.Validation.Warnings, result.Validation.CanPublish };
        return new { result.View, result.Draft, result.Changes, Validation = validation };
    }

    private IReadOnlyList<ContentOption> ListContent(RulesFamilyPayload payload) => app.ListContent(payload.RulesFamily, payload.CampaignId);

    private object DeleteCampaign(IdPayload payload)
    {
        app.DeleteCampaign(payload.Id);
        return new { deleted = true };
    }

    private object DeleteGapNote(DeleteGapNoteRequest payload)
    {
        app.DeleteGapNote(payload);
        return new { deleted = true };
    }

    private SnapshotPage ListSnapshots(SnapshotsPayload payload) => app.Snapshots(payload.CharacterId, payload.Before);

    private RestPreview PreviewRest(RestPreviewPayload payload) => app.PreviewRest(payload.CharacterId, payload.Kind, payload.HitDice);

    /// <summary>ADR-005: the native Open dialog picks the PDF; its path never crosses the bridge.</summary>
    private AttachOutcome AttachPdf(AttachPayload payload)
    {
        if (host is null || !host.CanOpenFiles)
            throw new AppValidationException([new("host.unsupported", "This host has no native Open dialog.")], "unsupported");
        var path = host.ChooseOpenFile("PDF document", ".pdf");
        return path is null ? new AttachOutcome(false, null) : new AttachOutcome(true, app.AttachPdfFile(payload.SourceId, path, payload.Mode));
    }

    private AttachmentInfo AttachPdfData(AttachDataPayload payload) =>
        app.AttachPdf(payload.SourceId, payload.FileName, Convert.FromBase64String(payload.Base64));

    private object Detach(DetachPayload payload)
    {
        app.Detach(payload.SourceId, payload.Confirm);
        return new { detached = true };
    }

    private OpenPageOutcome OpenPage(OpenPagePayload payload) => app.OpenPage(host, payload.SourceId, payload.Page);

    private UpdateReview ReviewUpdate(UpdatePayload payload) => app.ReviewUpdate(payload.CharacterId, payload.From, payload.To);

    private CharacterView ApplyUpdate(UpdatePayload payload) => app.ApplyUpdate(payload.CharacterId, payload.From, payload.To, payload.Confirm);

    private ExportPreview PreviewExport(ExportPayload payload) => app.PreviewExport(payload.CharacterIds, payload.Purpose);

    private object ExportPackage(ExportPayload payload)
    {
        var export = app.ExportCharacters(payload.CharacterIds, payload.Purpose);
        return new { export.FileName, Base64 = Convert.ToBase64String(export.Content), export.Manifest };
    }

    /// <summary>
    /// Exports and writes the package where the user chooses in a native Save dialog. The page never supplies
    /// a path and the package bytes never cross the bridge.
    /// </summary>
    private SaveOutcome SavePackageAs(ExportPayload payload)
    {
        if (host is null)
            throw new AppValidationException([new("host.unsupported", "This host has no native Save dialog.")], "unsupported");
        var export = app.ExportCharacters(payload.CharacterIds, payload.Purpose);
        var path = host.ChooseSaveLocation(export.FileName, "TomeStack package", ".tomestack.zip");
        if (path is null)
            return new SaveOutcome(false, null);

        var temporary = path + ".partial";
        try
        {
            File.WriteAllBytes(temporary, export.Content);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw new AppValidationException([new("package.save-failed", $"Could not save {Path.GetFileName(path)}. Check that the folder exists and is writable, then try again.")]);
        }
        return new SaveOutcome(true, Path.GetFileName(path));
    }

    /// <summary>
    /// M2.1 "Back up everything": the native Save dialog picks the file, and the backup streams to <c>&lt;file&gt;.partial</c>
    /// first, so a cancelled or failed write never leaves a half backup under the chosen name.
    /// </summary>
    private object SaveLibraryBackupAs()
    {
        if (host is null)
            throw new AppValidationException([new("host.unsupported", "Backing up everything needs the desktop app's Save dialog.")], "unsupported");
        var preview = app.PreviewLibraryBackup();
        var path = host.ChooseSaveLocation(preview.FileName, "TomeStack full backup", ".tomestack.zip");
        if (path is null)
            return new { saved = false };

        var temporary = path + ".partial";
        LibraryBackupResult result;
        var moved = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite))
            {
                result = app.WriteLibraryBackup(file);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            moved = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppValidationException([new("library.save-failed", $"Could not save {Path.GetFileName(path)}. Check that the folder exists, is writable and has enough free space, then try again.")]);
        }
        finally
        {
            // Whatever failed (the disk, the database, a limit), no half-written backup stays next to the chosen file.
            if (!moved)
                TryDelete(temporary);
        }
        return new { saved = true, fileName = Path.GetFileName(path), result.Bytes, result.Contents, result.Warnings };
    }

    /// <summary>M2.1 "Restore full backup", step 1: the native Open dialog picks the file; the result is its full check.</summary>
    private object ChooseLibraryRestore()
    {
        if (host is null || !host.CanOpenFiles)
            throw new AppValidationException([new("host.unsupported", "Restoring a full backup needs the desktop app's Open dialog.")], "unsupported");
        var path = host.ChooseOpenFile("TomeStack full backup", ".tomestack.zip");
        if (path is null)
            return new { chosen = false };
        var token = Guid.NewGuid();
        var stamp = FileStamp(path); // before the check: a file that changes while it is checked no longer matches
        var preview = app.PreviewLibraryRestore(path);
        _chosenBackups[token] = new ChosenBackup(path, stamp);
        return new { chosen = true, token, fileName = Path.GetFileName(path), preview };
    }

    /// <summary>The file's size and last write time: a restore applies only the file whose preview the user saw.</summary>
    private static (long Length, DateTime Written)? FileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record ChosenBackup(string Path, (long Length, DateTime Written)? Stamp);

    /// <summary>Step 2, after the user has read the preview: re-checks the same file and restores it.</summary>
    private LibraryRestoreResult ApplyLibraryRestore(RestorePayload payload)
    {
        if (!payload.Confirm)
            throw new AppValidationException([new("restore.confirm-required", "Show the restore preview and confirm it first.")]);
        if (!_chosenBackups.TryGetValue(payload.Token, out var chosen))
            throw new AppValidationException([new("restore.not-chosen", "Choose the backup file again.")]);
        if (chosen.Stamp is null || FileStamp(chosen.Path) != chosen.Stamp)
        {
            // A sync client or a later save replaced the file after its preview: the user never saw what it would do.
            _chosenBackups.TryRemove(payload.Token, out _);
            throw new AppValidationException([new("restore.file-changed", "The backup file changed after it was checked. Choose it again to see what it would restore.")]);
        }
        var result = app.ApplyLibraryRestore(chosen.Path, payload.SourceChoices);
        _chosenBackups.TryRemove(payload.Token, out _); // a failed attempt (say, a missing source choice) can be retried
        return result;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }

    private object ApplyImport(PackagePayload payload) =>
        app.ApplyImport(Convert.FromBase64String(payload.Base64), payload.SourceChoices);

    private static T Payload<T>(JsonElement? payload) =>
        payload is { ValueKind: JsonValueKind.Object } element
            ? element.Deserialize<T>(RulesJson.Compact) ?? throw new JsonException("Payload is null.")
            : throw new JsonException($"Command requires a {typeof(T).Name} payload object.");

    private static string Error(string? id, string code, string message, IReadOnlyList<Diagnostic>? diagnostics = null, string? correlationId = null) =>
        JsonSerializer.Serialize(new { id, ok = false, error = new { code, message, diagnostics, correlationId } }, RulesJson.Compact);

    private sealed class UnknownCommandException(string command)
        : Exception($"Unknown command '{(command.Length > 64 ? command[..64] + "…" : command)}'.");

    private sealed record CommandRequest(string Id, string Command, JsonElement? Payload);

    private sealed record RulesFamilyPayload(string RulesFamily, Guid? CampaignId = null);

    private sealed record IdPayload(Guid Id);

    private sealed record CharacterIdPayload(Guid CharacterId);

    private sealed record SnapshotsPayload(Guid CharacterId, Guid? Before = null);

    private sealed record RestPreviewPayload(Guid CharacterId, RestPeriod Kind = RestPeriod.LongRest, IReadOnlyList<HitDieRoll>? HitDice = null);

    private sealed record ContentIdPayload(Guid ContentId);

    private sealed record SourceIdPayload(Guid SourceId);

    private sealed record AttachPayload(Guid SourceId, AttachmentMode Mode = AttachmentMode.Managed);

    private sealed record AttachDataPayload(Guid SourceId, string FileName, string Base64);

    /// <param name="Confirm">Must be true: the UI shows <c>source.detachPreview</c> first (SPEC S-04).</param>
    private sealed record DetachPayload(Guid SourceId, bool Confirm = false);

    private sealed record OpenPagePayload(Guid SourceId, int Page);

    /// <param name="Confirm">Must be true to apply; the review never changes anything (SPEC I-06).</param>
    private sealed record UpdatePayload(Guid CharacterId, ContentReference From, ContentReference To, bool Confirm = false);

    /// <summary>A stored revision by <paramref name="Reference"/>, or an unsaved one inline.</summary>
    private sealed record ContentPayload(ContentReference? Reference = null, ContentRevision? Revision = null);

    /// <param name="Purpose">ADR-007: <c>backup</c> (default, everything) or <c>share</c> (leaves out non-redistributable sources).</param>
    private sealed record ExportPayload(IReadOnlyList<Guid> CharacterIds, ExportPurpose Purpose = ExportPurpose.Backup);

    private sealed record PackagePayload(string Base64, Dictionary<Guid, SourceChoice>? SourceChoices = null);

    /// <param name="Token">From <c>library.restoreChoose</c>; used once.</param>
    /// <param name="Confirm">Must be true: only the preview's "Restore" button sends it.</param>
    private sealed record RestorePayload(Guid Token, Dictionary<Guid, SourceChoice>? SourceChoices = null, bool Confirm = false);
}
