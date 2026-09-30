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
    /// v7 (M6 slice 1; the number is settled on the M5+M6 integration branch and final when it merges to main, ROADMAP "Package format numbers"): the
    /// <see cref="PackageScope.Source"/> scope (a source pack: shareable homebrew sources and their published revisions,
    /// with <see cref="RevisionOrder"/> and <see cref="Attestations"/>), and library backups whose sources carry
    /// <c>importDerived</c>, <c>origin</c> and <c>shareConfirmedAt</c>. Older builds refuse v7, so none drops the flag.
    /// v8 (M6 slice 2; settled with v7): the <see cref="PackageScope.Campaign"/> scope
    /// (a campaign pack: one campaign profile, and the sources of it that pass the source-pack guard with their published
    /// revisions; allowed sources it leaves out are listed in <see cref="Omitted"/>).
    /// v9 (M6 slice 3; settled with v7 and v8): library backups that keep installed extensions (<c>extensions/&lt;sha256&gt;.zip</c>,
    /// without their grants; ADR-011 "Storage and backup"). A backup with no extension is still written as v7.
    /// </summary>
    public const int CurrentFormatVersion = 9;

    /// <summary>M6 slice 3 (settled on the integration branch): the first library-backup version that may carry installed extensions.</summary>
    public const int LibraryExtensionsFormatVersion = 9;

    /// <summary>Character packages (backup and share) have not changed since v5, so they stay readable by 0.3.0.</summary>
    public const int CharacterFormatVersion = 5;

    /// <summary>M6 slice 1: library backups carry the sources' import-derived flag and origin from v7 on.</summary>
    public const int LibraryFormatVersion = 7;

    /// <summary>M6 slice 1: the first version with <see cref="PackageScope.Source"/>.</summary>
    public const int SourceFormatVersion = 7;

    /// <summary>M6 slice 2 (settled on the integration branch): the first version with <see cref="PackageScope.Campaign"/>.</summary>
    public const int CampaignFormatVersion = 8;

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

    /// <summary>
    /// Source packs only (v7): for each source, the sender's statement that it is their own work and when they confirmed
    /// it. The receiver sees it as the sender's claim; TomeStack cannot verify it.
    /// </summary>
    public IReadOnlyList<SourceAttestation>? Attestations { get; init; }

    public string AttachmentPolicy { get; init; } = CharacterAttachmentPolicy;

    public const string CharacterAttachmentPolicy = "PDF attachments are never included in packages; linked pages must be re-attached on the receiving machine.";

    public const string LibraryAttachmentPolicy = "A full backup includes the PDFs TomeStack keeps a copy of. Linked PDFs stay where they are and are not included. Text read from PDFs is not included; import it again.";
}

/// <summary>ADR-007 (D03). A backup includes everything and must not be shared; a share leaves out non-redistributable sources.</summary>
public enum ExportPurpose { Backup, Share }

/// <summary>
/// M2.1. <see cref="Characters"/>: chosen characters and what they need (a character backup or share). <see cref="Library"/>:
/// the whole data folder, including drafts, unused homebrew and managed PDFs ("Back up everything"); always a backup.
/// <see cref="Source"/> (M6 slice 1, v7): a source pack, always a share: homebrew sources their author marked as shareable,
/// with their published revisions and nothing else. <see cref="Campaign"/> (M6 slice 2, v8): a campaign pack, always a
/// share: one campaign profile and the shareable content of its allowed sources, never characters or gap notes.
/// </summary>
public enum PackageScope { Characters, Library, Source, Campaign }

/// <param name="Statement">The text the author confirmed (<see cref="TomeStackApp.OwnWorkStatement"/>).</param>
/// <param name="ConfirmedAt">When they confirmed it on their machine. No user or machine name is recorded.</param>
public sealed record SourceAttestation(Guid SourceId, string Statement, DateTimeOffset ConfirmedAt);

/// <summary>What <c>package.sourcePackPreview</c> would write; nothing is written.</summary>
/// <param name="Sources">The sources, with the license notice each carries.</param>
/// <param name="Revisions">Published revisions included (superseded ones too, so pinned characters keep working).</param>
/// <param name="Warnings">Content the pack refers to but does not carry (another source's), which the receiver must have.</param>
/// <param name="Drafts">Drafts of these sources that stay on this machine (a pack never carries them).</param>
public sealed record SourcePackPreview(string FileName, IReadOnlyList<LicenseNotice> Sources, int Revisions, int Drafts, IReadOnlyList<Diagnostic> Warnings);

/// <summary>What <c>package.campaignPackPreview</c> would write (M6 slice 2); nothing is written.</summary>
/// <param name="Included">Allowed sources the pack carries, with their published revisions (each passed the source-pack guard).</param>
/// <param name="Referenced">Allowed sources bundled with TomeStack: referenced by id, never copied.</param>
/// <param name="LeftOut">Allowed sources the pack leaves out and lists in <c>omitted[]</c>, each with why.</param>
/// <param name="Revisions">Published revisions carried.</param>
/// <param name="Warnings">Content the pack refers to but does not carry, which the receiver must have.</param>
public sealed record CampaignPackPreview(
    string FileName, Guid CampaignId, string Name, string RulesFamily,
    IReadOnlyList<LicenseNotice> Included, IReadOnlyList<LicenseNotice> Referenced, IReadOnlyList<LeftOutSource> LeftOut,
    int Revisions, IReadOnlyList<Diagnostic> Warnings);

/// <param name="Reason">Why it is left out, as a diagnostic (<c>pack.source-not-shareable</c>, <c>pack.source-received</c>, …).</param>
public sealed record LeftOutSource(Guid SourceId, string Title, string Publisher, string License, Diagnostic Reason);

/// <summary>
/// M6 slice 2: a character in a campaign that a campaign pack would replace, and what "use the imported one" would newly
/// make not allowed for it. Shown in the import preview; nothing is changed.
/// </summary>
/// <param name="NotAllowed">Names of the content it uses that the imported profile does not allow (and the local one does).</param>
/// <param name="RulesFamily">The imported profile's rules family, when it differs from the character's.</param>
public sealed record CampaignImpact(Guid CampaignId, Guid CharacterId, string CharacterName, IReadOnlyList<string> NotAllowed, string? RulesFamily = null);

/// <summary>What "Back up everything" would write (<c>library.backupPreview</c>); nothing is written.</summary>
/// <param name="ManagedPdfs">PDFs TomeStack keeps a copy of (included), and their total size.</param>
/// <param name="LinkedPdfs">PDFs left where they are: their records are included, the files are not.</param>
/// <param name="Unreadable">Managed PDFs that are missing or damaged on disk and would be left out, by source title.</param>
/// <param name="Extensions">M6 slice 3: installed extensions kept (their files, not their grants).</param>
public sealed record LibraryBackupPreview(
    string FileName, int Characters, int Campaigns, int GapNotes, int Sources, int PublishedRevisions, int DraftRevisions,
    int ManagedPdfs, long ManagedPdfBytes, int LinkedPdfs, IReadOnlyList<string> Unreadable, int Extensions = 0);

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

/// <param name="CampaignImpact">
/// M6 slice 2, campaign packs: for a campaign that differs from the local one, the local characters whose content "use the
/// imported one" would make not allowed. Empty otherwise.
/// </param>
public sealed record PackagePreview(
    bool CanApply,
    PackageManifest? Manifest,
    IReadOnlyList<PackageItem> Items,
    IReadOnlyList<Diagnostic> Errors,
    IReadOnlyList<Diagnostic> Warnings,
    IReadOnlyList<CampaignImpact>? CampaignImpact = null);

/// <param name="BackupFile">
/// Path relative to the data directory of the pre-import backup: a character package when characters were replaced, or a
/// copy of the database (<c>pre-import-*.db</c>) before a source pack changed anything (M6 slice 1).
/// </param>
/// <param name="DatabaseCopy">
/// M6 slice 2: a copy of the database (<c>pre-import-*.db</c>) taken because the import replaces a campaign, when
/// <paramref name="BackupFile"/> is the character package of replaced characters (which holds only their campaigns).
/// </param>
public sealed record ImportResult(int Added, int Replaced, int Unchanged, IReadOnlyList<Guid> Characters, string? BackupFile = null, string? DatabaseCopy = null);

public sealed record ExportResult(string FileName, byte[] Content, PackageManifest Manifest);

public sealed class PackageException(IReadOnlyList<Diagnostic> errors)
    : Exception(string.Join(" ", errors.Select(e => e.Message)))
{
    public IReadOnlyList<Diagnostic> Errors { get; } = errors;
}
