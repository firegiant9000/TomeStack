using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// <c>manifest.json</c> of a portable TomeStack package (SPEC P-02). Documented in docs/features/package-format.md.
/// </summary>
public sealed record PackageManifest
{
    public const string FormatName = "tomestack.package";

    /// <summary>
    /// v2 (ADR-003): content entries use content schemaVersion 2 (typed effects). v1 packages still import, and their
    /// revisions are upcast. Builds that only know v1 refuse v2 with a clear message instead of misreading effects.
    /// </summary>
    public const int CurrentFormatVersion = 2;

    public string Format { get; init; } = FormatName;
    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public required DateTimeOffset CreatedAt { get; init; }
    public required string AppVersion { get; init; }
    public IReadOnlyList<Guid> Characters { get; init; } = [];
    public IReadOnlyList<PackageEntry> Entries { get; init; } = [];
    public IReadOnlyList<LicenseNotice> Notices { get; init; } = [];
    public string AttachmentPolicy { get; init; } = "PDF attachments are never included in packages; linked pages must be re-attached on the receiving machine.";
}

public sealed record PackageEntry(string Path, string Kind, string Sha256, long Size);

public sealed record LicenseNotice(Guid SourceId, string Title, string Publisher, string License, bool Redistributable, string? Attribution);

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
