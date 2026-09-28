using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>ADR-005: a managed copy in the data folder (default) or a link to the file where it is.</summary>
public enum AttachmentMode { Managed, Linked }

/// <summary>
/// ADR-005 attachment record. <paramref name="Sha256"/> is null only for a linked file that was missing when it was
/// recorded (database migration v3). <paramref name="LinkedPath"/> is machine-local: never sent to the UI and never in a
/// character package. Only a full library backup carries it (a personal file, ADR-007 item 10), and a restore accepts
/// it only as a full path to a PDF on a local drive (<c>PackageService.IsSafeLinkedPath</c>; no network or device path).
/// </summary>
public sealed record Attachment(Guid AttachmentId, string? Sha256, string OriginalFileName, long ByteLength, AttachmentMode Mode, string? LinkedPath, DateTimeOffset CreatedAt);

/// <summary>What the UI may know about a source's PDF: no paths.</summary>
/// <param name="Status"><c>available</c>, <c>missing</c> (re-attach it) or <c>changed</c> (a linked file whose contents differ; pages may have moved).</param>
public sealed record AttachmentInfo(Guid SourceId, Guid AttachmentId, string OriginalFileName, long ByteLength, AttachmentMode Mode, string Status);

/// <summary>SPEC S-04: what removing the attachment breaks. The content itself stays.</summary>
public sealed record DetachPreview(Guid SourceId, string OriginalFileName, int PageLinks, IReadOnlyList<string> ContentNames);

public sealed record AttachOutcome(bool Attached, AttachmentInfo? Attachment);

public sealed record OpenPageOutcome(bool Opened, int Page, IReadOnlyList<Diagnostic> Warnings);

/// <summary>M2 item 6, ADR-005, SPEC S-04: PDF attachments and page navigation.</summary>
public sealed partial class TomeStackApp
{
    /// <summary><c>source.attachment</c>: the source's PDF, or null. A linked file is checked for presence only (cheap).</summary>
    public AttachmentInfo? GetAttachment(Guid sourceId)
    {
        var source = FindSourceOrThrow(sourceId);
        if (source.AttachmentId is not { } id || _store.FindAttachment(id) is not { } attachment)
            return null;
        return Info(sourceId, attachment, checkHash: false);
    }

    /// <summary><c>source.attachPdfData</c>: attaches PDF bytes as a managed copy (browser development and tests; the desktop uses the native dialog).</summary>
    public AttachmentInfo AttachPdf(Guid sourceId, string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var source = FindSourceOrThrow(sourceId);
        using var stream = new MemoryStream(content, writable: false);
        return Attach(source, () => AttachmentFiles.ImportManaged(_store, stream, fileName, _time.GetUtcNow()));
    }

    /// <summary>Attaches a file the user chose in the native dialog: copied (managed, the default) or linked.</summary>
    internal AttachmentInfo AttachPdfFile(Guid sourceId, string path, AttachmentMode mode)
    {
        var source = FindSourceOrThrow(sourceId);
        return Attach(source, () =>
        {
            if (mode == AttachmentMode.Linked)
                return AttachmentFiles.Link(_store, path, _time.GetUtcNow());
            using var stream = File.OpenRead(path);
            return AttachmentFiles.ImportManaged(_store, stream, Path.GetFileName(path), _time.GetUtcNow());
        });
    }

    /// <summary><c>source.detachPreview</c>: the page links that stop working. Nothing changes.</summary>
    public DetachPreview PreviewDetach(Guid sourceId)
    {
        var source = FindSourceOrThrow(sourceId);
        var attachment = source.AttachmentId is { } id ? _store.FindAttachment(id) : null;
        if (attachment is null)
            throw new AppValidationException([new("attachment.none", $"'{source.Title}' has no PDF attached.")]);
        // Sorted, so the confirmation reads the same every time (storage order is not meaningful).
        var cited = _store.ListRevisions().Where(r => r.Provenance.SourceId == sourceId && r.Provenance.Page is not null)
            .Select(r => r.Name).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        return new(sourceId, attachment.OriginalFileName, cited.Count, cited);
    }

    /// <summary>
    /// <c>source.detach</c> (SPEC S-04): removes the attachment after the confirmation that described what breaks. All
    /// content stays; a managed file is deleted when no other source uses the same PDF. Refused without <c>confirm</c>.
    /// </summary>
    public void Detach(Guid sourceId, bool confirm)
    {
        if (!confirm)
            throw new AppValidationException([new("attachment.confirmation-required", "Removing a PDF breaks its page links. Confirm to remove it; the content stays.")]);
        var source = FindSourceOrThrow(sourceId);
        if (source.AttachmentId is not { } id || _store.FindAttachment(id) is not { } attachment)
            throw new AppValidationException([new("attachment.none", $"'{source.Title}' has no PDF attached.")]);
        CancelImportsOf(sourceId); // M4 D2: a running import reads the file; it stops first, and its pages stay
        _store.InTransaction(() =>
        {
            _store.UpsertSource(source with { AttachmentId = null });
            if (_store.SourcesUsing(id) == 0)
                _store.DeleteAttachment(id);
        });
        if (attachment.Mode == AttachmentMode.Managed && attachment.Sha256 is { } sha)
            AttachmentFiles.DeleteManagedIfUnused(_store, sha);
    }

    /// <summary>
    /// <c>source.openPage</c> (SPEC S-04, MVP "open the cited page from a feature offline"): the shell opens the PDF at
    /// the page. It works without text extraction. A missing file is an error; a linked file whose contents changed
    /// opens with a warning, because the page numbers may have moved.
    /// </summary>
    internal OpenPageOutcome OpenPage(IHostServices? host, Guid sourceId, int page)
    {
        var source = FindSourceOrThrow(sourceId);
        if (page < 1)
            throw new AppValidationException([new("attachment.page-invalid", "Pages start at 1.")]);
        if (source.AttachmentId is not { } id || _store.FindAttachment(id) is not { } attachment)
            throw new AppValidationException([new("attachment.none", $"'{source.Title}' has no PDF attached. Attach it on the Sources screen to open its pages.")]);
        var info = Info(sourceId, attachment, checkHash: true);
        if (info.Status == "missing")
            throw new AppValidationException([new("attachment.missing", $"The PDF for '{source.Title}' ({attachment.OriginalFileName}) is missing. Attach it again.")]);
        if (host is null || !host.CanOpenFiles)
            throw new AppValidationException([new("host.unsupported", "This host cannot open PDFs; use the desktop app.")], "unsupported");
        var warnings = info.Status == "changed"
            ? new List<Diagnostic> { new("attachment.changed", $"{attachment.OriginalFileName} changed since it was linked; page {page} may not be the cited page.") }
            : [];
        var opened = host.OpenPdf(AttachmentFiles.PathOf(_store, attachment), page, $"{source.Title}, p. {page}");
        return new(opened, page, warnings);
    }

    private AttachmentInfo Attach(SourceRecord source, Func<Attachment> create)
    {
        Attachment attachment;
        try
        {
            attachment = create();
        }
        catch (AttachmentException ex)
        {
            throw new AppValidationException([new(ex.Code, ex.Message)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppValidationException([new("attachment.unreadable", "The PDF could not be read. Check that it is not open elsewhere and try again.")]);
        }
        var previous = source.AttachmentId;
        _store.InTransaction(() => _store.UpsertSource(source with { AttachmentId = attachment.AttachmentId, PdfRef = null }));
        if (previous is { } old && old != attachment.AttachmentId && _store.FindAttachment(old) is { } replaced && _store.SourcesUsing(old) == 0)
        {
            _store.InTransaction(() => _store.DeleteAttachment(old));
            if (replaced.Mode == AttachmentMode.Managed && replaced.Sha256 is { } sha)
                AttachmentFiles.DeleteManagedIfUnused(_store, sha);
        }
        return Info(source.Id, attachment, checkHash: false);
    }

    private AttachmentInfo Info(Guid sourceId, Attachment attachment, bool checkHash)
    {
        var path = attachment.Mode == AttachmentMode.Managed && attachment.Sha256 is null ? null : AttachmentFiles.PathOf(_store, attachment);
        var status = path is null || !File.Exists(path) || attachment.Sha256 is null ? "missing" : "available";
        if (status == "available" && checkHash && attachment.Mode == AttachmentMode.Linked)
        {
            try
            {
                status = AttachmentFiles.HashFile(path!).Sha256 == attachment.Sha256 ? "available" : "changed";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AttachmentException)
            {
                status = "changed";
            }
        }
        return new(sourceId, attachment.AttachmentId, attachment.OriginalFileName, attachment.ByteLength, attachment.Mode, status);
    }

    private SourceRecord FindSourceOrThrow(Guid sourceId) =>
        _store.FindSource(sourceId) ?? throw new AppValidationException([new("source.not-found", $"Source {sourceId} is not installed.")]);
}
