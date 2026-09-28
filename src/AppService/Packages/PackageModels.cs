using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// <c>manifest.json</c> of a portable TomeStack package (SPEC P-02). Documented in docs/features/package-format.md.
/// </summary>
public sealed record PackageManifest
{
    public const string FormatName = "tomestack.package";

    /// <summary>
    /// v6 (M2.1): <see cref="Scope"/>. A <see cref="PackageScope.Library"/> backup is the whole data folder: every
    /// source (with its PDF link), every revision including drafts (in <see cref="RevisionOrder"/>), attachment records
    /// (<c>attachments/</c>) and the managed PDFs themselves (<c>files/</c>). Only "Restore full backup" reads it; an
    /// ordinary import refuses it, and older builds refuse v6.
    /// v5 (M3 B3): <c>gaps/</c> entries (session gap notes), in backups only. A share package never has them, and an
    /// import refuses one that does. Older builds refuse v5 instead of rejecting the unknown path mid-preview.
    /// v4 (M2 items 5–7): <c>campaigns/</c> entries (SPEC P-01), and entries may use content schema v4 and character
    /// schema v4. Older builds refuse v4 instead of dropping the campaign or misreading the entries.
    /// v3 (ADR-007, D03): <see cref="Purpose"/> and <see cref="Omitted"/>. A share package may leave out pinned
    /// revisions; older builds would reject those pins, so they refuse v3 instead. v2 (ADR-003): content entries use
    /// content schemaVersion 2 (typed effects). v1 and v2 packages still import (as backups), and v1 revisions are upcast.
    /// </summary>
    public const int CurrentFormatVersion = 6;

    /// <summary>Character packages (backup and share) have not changed since v5, so they stay readable by 0.3.0.</summary>
    public const int CharacterFormatVersion = 5;

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

    /// <summary>Absent before v6, and not written for character packages (they stay v5): <see cref="PackageScope.Characters"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public PackageScope Scope { get; init; } = PackageScope.Characters;

    /// <summary>
    /// Library backups only: the revision IDs in the order they were stored. The newest published revision of each content
    /// is the last one stored (SPEC I-06), so a restore adds them in this order, not in entry order.
    /// </summary>
    public IReadOnlyList<Guid>? RevisionOrder { get; init; }

    public string AttachmentPolicy { get; init; } = CharacterAttachmentPolicy;

    public const string CharacterAttachmentPolicy = "PDF attachments are never included in packages; linked pages must be re-attached on the receiving machine.";

    public const string LibraryAttachmentPolicy = "A full backup includes the PDFs TomeStack keeps a copy of. Linked PDFs stay where they are and are not included. Text read from PDFs is not included; import it again.";
}

/// <summary>ADR-007 (D03). A backup includes everything and must not be shared; a share leaves out non-redistributable sources.</summary>
public enum ExportPurpose { Backup, Share }

/// <summary>
/// M2.1. <see cref="Characters"/>: chosen characters and what they need (a character backup or share). <see cref="Library"/>:
/// the whole data folder, including drafts, unused homebrew and managed PDFs ("Back up everything"); always a backup.
/// </summary>
public enum PackageScope { Characters, Library }

/// <summary>What "Back up everything" would write (<c>library.backupPreview</c>); nothing is written.</summary>
/// <param name="ManagedPdfs">PDFs TomeStack keeps a copy of (included), and their total size.</param>
/// <param name="LinkedPdfs">PDFs left where they are: their records are included, the files are not.</param>
/// <param name="Unreadable">Managed PDFs that are missing or damaged on disk and would be left out, by source title.</param>
public sealed record LibraryBackupPreview(
    string FileName, int Characters, int Campaigns, int GapNotes, int Sources, int PublishedRevisions, int DraftRevisions,
    int ManagedPdfs, long ManagedPdfBytes, int LinkedPdfs, IReadOnlyList<string> Unreadable);

/// <param name="Warnings">Anything left out (a damaged PDF copy); the backup is still complete otherwise.</param>
public sealed record LibraryBackupResult(string FileName, long Bytes, LibraryBackupPreview Contents, IReadOnlyList<Diagnostic> Warnings);

/// <param name="SafetyCopy">Path relative to the data folder of the database copy taken before anything changed, when there was data to replace.</param>
/// <param name="PdfsCopied">Managed PDFs copied into this data folder (ones already here are not copied again).</param>
public sealed record LibraryRestoreResult(int Added, int Replaced, int Unchanged, int PdfsCopied, string? SafetyCopy, IReadOnlyList<Diagnostic> Warnings);

public sealed record PackageEntry(string Path, string Kind, string Sha256, long Size);

public sealed record LicenseNotice(Guid SourceId, string Title, string Publisher, string License, bool Redistributable, string? Attribution, string? ModificationNotice = null);

/// <summary>A source left out of a share package, so the receiver knows what to obtain themselves.</summary>
public sealed record OmittedSource(Guid SourceId, string Title, string Publisher, string License, IReadOnlyList<OmittedRevision> Revisions);

/// <param name="Characters">The exported characters that pin this revision.</param>
public sealed record OmittedRevision(ContentReference Reference, string Name, IReadOnlyList<Guid> Characters);

/// <summary>What an export with a given purpose would contain, before anything is written (<c>package.exportPreview</c>).</summary>
/// <param name="GapNotes">The number of gap notes included: all of the characters' notes in a backup, always 0 in a share (M3 B3).</param>
public sealed record ExportPreview(ExportPurpose Purpose, string FileName, IReadOnlyList<Guid> Characters, IReadOnlyList<LicenseNotice> Included, IReadOnlyList<OmittedSource> Omitted, int GapNotes = 0);

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
