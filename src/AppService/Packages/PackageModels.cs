using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// <c>manifest.json</c> of a portable TomeStack package (SPEC P-02). Documented in docs/features/package-format.md.
/// </summary>
public sealed record PackageManifest
{
    public const string FormatName = "tomestack.package";
    public const int CurrentFormatVersion = 1;

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

public sealed record PackageItem(string Kind, Guid Id, string Name, PackageItemAction Action, string? Detail = null);

public sealed record PackagePreview(
    bool CanApply,
    PackageManifest? Manifest,
    IReadOnlyList<PackageItem> Items,
    IReadOnlyList<Diagnostic> Errors,
    IReadOnlyList<Diagnostic> Warnings);

public sealed record ImportResult(int Added, int Replaced, int Unchanged, IReadOnlyList<Guid> Characters);

public sealed record ExportResult(string FileName, byte[] Content, PackageManifest Manifest);

public sealed class PackageException(IReadOnlyList<Diagnostic> errors)
    : Exception(string.Join(" ", errors.Select(e => e.Message)))
{
    public IReadOnlyList<Diagnostic> Errors { get; } = errors;
}
