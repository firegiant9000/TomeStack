using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// <c>manifest.json</c> of a portable TomeStack package (SPEC P-02). Documented in docs/features/package-format.md.
/// </summary>
public sealed record PackageManifest
{
    public const string FormatName = "tomestack.package";

    /// <summary>
    /// v3 (ADR-007, D03): <see cref="Purpose"/> and <see cref="Omitted"/>. A share package may leave out pinned
    /// revisions; older builds would reject those pins, so they refuse v3 instead. v2 (ADR-003): content entries use
    /// content schemaVersion 2 (typed effects). v1 and v2 packages still import (as backups), and v1 revisions are upcast.
    /// </summary>
    public const int CurrentFormatVersion = 3;

    public string Format { get; init; } = FormatName;
    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public required DateTimeOffset CreatedAt { get; init; }
    public required string AppVersion { get; init; }

    /// <summary>Absent in v1/v2 packages, which were always complete: read as <see cref="ExportPurpose.Backup"/>.</summary>
    public ExportPurpose Purpose { get; init; } = ExportPurpose.Backup;

    public IReadOnlyList<Guid> Characters { get; init; } = [];
    public IReadOnlyList<PackageEntry> Entries { get; init; } = [];
    public IReadOnlyList<LicenseNotice> Notices { get; init; } = [];

    /// <summary>Share packages only: every source whose content was left out, and what it would have provided.</summary>
    public IReadOnlyList<OmittedSource> Omitted { get; init; } = [];

    public string AttachmentPolicy { get; init; } = "PDF attachments are never included in packages; linked pages must be re-attached on the receiving machine.";
}

/// <summary>ADR-007 (D03). A backup includes everything and must not be shared; a share leaves out non-redistributable sources.</summary>
public enum ExportPurpose { Backup, Share }

public sealed record PackageEntry(string Path, string Kind, string Sha256, long Size);

public sealed record LicenseNotice(Guid SourceId, string Title, string Publisher, string License, bool Redistributable, string? Attribution, string? ModificationNotice = null);

/// <summary>A source left out of a share package, so the receiver knows what to obtain themselves.</summary>
public sealed record OmittedSource(Guid SourceId, string Title, string Publisher, string License, IReadOnlyList<OmittedRevision> Revisions);

/// <param name="Characters">The exported characters that pin this revision.</param>
public sealed record OmittedRevision(ContentReference Reference, string Name, IReadOnlyList<Guid> Characters);

/// <summary>What an export with a given purpose would contain, before anything is written (<c>package.exportPreview</c>).</summary>
public sealed record ExportPreview(ExportPurpose Purpose, string FileName, IReadOnlyList<Guid> Characters, IReadOnlyList<LicenseNotice> Included, IReadOnlyList<OmittedSource> Omitted);

public enum PackageItemAction { Add, Unchanged, Replace, Conflict }

/// <summary>One field that differs between the local record and the package's copy.</summary>
public sealed record FieldChange(string Field, string? Local, string? Imported);

/// <param name="Changes">For a source that differs from the local record: every differing field. Apply needs a <see cref="SourceChoice"/> for it.</param>
public sealed record PackageItem(string Kind, Guid Id, string Name, PackageItemAction Action, string? Detail = null, IReadOnlyList<FieldChange>? Changes = null);

/// <summary>What to do with a package source whose metadata (e.g. license) differs from the local record.</summary>
public enum SourceChoice { KeepLocal, UseImported }

public sealed record PackagePreview(
    bool CanApply,
    PackageManifest? Manifest,
    IReadOnlyList<PackageItem> Items,
    IReadOnlyList<Diagnostic> Errors,
    IReadOnlyList<Diagnostic> Warnings);

/// <param name="BackupFile">Path relative to the data directory of the pre-import backup, when characters were replaced.</param>
public sealed record ImportResult(int Added, int Replaced, int Unchanged, IReadOnlyList<Guid> Characters, string? BackupFile = null);

public sealed record ExportResult(string FileName, byte[] Content, PackageManifest Manifest);

public sealed class PackageException(IReadOnlyList<Diagnostic> errors)
    : Exception(string.Join(" ", errors.Select(e => e.Message)))
{
    public IReadOnlyList<Diagnostic> Errors { get; } = errors;
}
