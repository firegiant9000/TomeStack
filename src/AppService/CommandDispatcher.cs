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
        "app.info", "content.list", "content.validate", "content.saveDraft", "content.publish", "content.revisions", "content.affected",
        "content.bySource", "source.list", "source.createHomebrew",
        "source.attachment", "source.attachPdf", "source.attachPdfData", "source.detachPreview", "source.detach", "source.openPage",
        "character.list", "character.get", "character.create", "character.save", "character.choose", "character.preview", "character.previewChoice",
        "character.play", "character.restPreview", "character.rest", "character.reviewUpdate", "character.applyUpdate", "roll",
        "package.exportPreview", "package.export", "package.saveAs", "package.preview", "package.apply",
    ];

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
        "content.list" => app.ListContent(Payload<RulesFamilyPayload>(payload).RulesFamily),
        "content.validate" => Validate(Payload<ContentPayload>(payload)),
        "content.saveDraft" => app.SaveDraft(Payload<ContentPayload>(payload).Revision ?? throw new JsonException("content.saveDraft needs a revision.")),
        "content.publish" => app.Publish(Payload<ContentPayload>(payload).Reference ?? throw new JsonException("content.publish needs a reference.")),
        "content.revisions" => app.ListRevisions(Payload<ContentIdPayload>(payload).ContentId),
        "content.affected" => app.AffectedCharacters(Payload<ContentIdPayload>(payload).ContentId),
        "content.bySource" => app.ContentBySource(Payload<SourceIdPayload>(payload).SourceId),
        "source.list" => app.ListSources(),
        "source.createHomebrew" => app.CreateHomebrewSource(Payload<HomebrewSourceRequest>(payload)),
        "source.attachment" => (object?)app.GetAttachment(Payload<SourceIdPayload>(payload).SourceId) ?? new { attached = false },
        "source.attachPdf" => AttachPdf(Payload<AttachPayload>(payload)),
        "source.attachPdfData" => AttachPdfData(Payload<AttachDataPayload>(payload)),
        "source.detachPreview" => app.PreviewDetach(Payload<SourceIdPayload>(payload).SourceId),
        "source.detach" => Detach(Payload<DetachPayload>(payload)),
        "source.openPage" => OpenPage(Payload<OpenPagePayload>(payload)),
        "character.reviewUpdate" => ReviewUpdate(Payload<UpdatePayload>(payload)),
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
        "package.exportPreview" => PreviewExport(Payload<ExportPayload>(payload)),
        "package.export" => ExportPackage(Payload<ExportPayload>(payload)),
        "package.saveAs" => SavePackageAs(Payload<ExportPayload>(payload)),
        "package.preview" => app.PreviewImport(Convert.FromBase64String(Payload<PackagePayload>(payload).Base64)),
        "package.apply" => ApplyImport(Payload<PackagePayload>(payload)),
        _ => throw new UnknownCommandException(command),
    };

    private object Validate(ContentPayload payload)
    {
        var report = app.ValidateContent(payload.Reference, payload.Revision);
        return new { report.Revision, report.Errors, report.Warnings, report.CanPublish };
    }

    private RestPreview PreviewRest(RestPreviewPayload payload) => app.PreviewRest(payload.CharacterId, payload.Kind);

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

    private sealed record RulesFamilyPayload(string RulesFamily);

    private sealed record IdPayload(Guid Id);

    private sealed record RestPreviewPayload(Guid CharacterId, RestPeriod Kind = RestPeriod.LongRest);

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
}
