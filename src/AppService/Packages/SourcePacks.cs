using System.IO.Compression;
using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// M6 slice 1 (ROADMAP "M6 plan", LIVING_SPECS D14 item 6): source packs, and the rules every package follows for the
/// import-derived flag and a source's origin. A source pack (<see cref="PackageScope.Source"/>, format v7) carries homebrew
/// sources that their author marked as shareable on this machine, with every published revision of each, in stored
/// order. It never carries characters, drafts, gap notes, campaigns, PDFs or text read from PDFs. Documented in
/// docs/features/package-format.md ("Source packs") and docs/schemas/package-manifest.v7.schema.json.
/// </summary>
public sealed partial class PackageService
{
    /// <summary>At most this many sources in one pack: a pack is a deliberate share, not a library dump.</summary>
    public const int MaxPackSources = 50;

    public const string SourceAttachmentPolicy = "A source pack never includes PDFs, drafts, characters, gap notes or text read from PDFs.";

    /// <summary>The bundled SRD sources: never in a source pack, never replaced by any package, never import-derived.</summary>
    private IReadOnlySet<Guid> _bundledSources = new HashSet<Guid>();

    internal void SetBundledSources(IReadOnlySet<Guid> sourceIds) => _bundledSources = sourceIds;

    private sealed record SourcePackPlan(
        List<SourceRecord> Sources, List<ContentRevision> Revisions, int Drafts, List<Diagnostic> Warnings, string FileName);

    /// <summary><c>package.sourcePackPreview</c>: what a source pack of <paramref name="sourceIds"/> would hold. Writes nothing.</summary>
    public SourcePackPreview PreviewSourcePack(IReadOnlyList<Guid> sourceIds)
    {
        var plan = PlanSourcePack(sourceIds);
        return new(plan.FileName, [.. plan.Sources.Select(Notice)], plan.Revisions.Count, plan.Drafts, plan.Warnings);
    }

    /// <summary><c>package.sourcePackExport</c> / <c>package.sourcePackSaveAs</c>: the pack's bytes.</summary>
    public ExportResult ExportSourcePack(IReadOnlyList<Guid> sourceIds)
    {
        var plan = PlanSourcePack(sourceIds);
        var files = new SortedDictionary<string, (string Kind, byte[] Bytes)>(StringComparer.Ordinal);
        foreach (var source in plan.Sources)
            files[$"sources/{source.Id:D}.json"] = ("source", Json(ForSourcePack(source)));
        foreach (var revision in plan.Revisions)
            files[$"content/{revision.RevisionId:D}.json"] = ("contentRevision", Json(revision));
        CheckPackLimits(files, "Share fewer sources at a time.");

        var createdAt = time.GetUtcNow();
        var manifest = new PackageManifest
        {
            FormatVersion = PackageManifest.SourceFormatVersion,
            Scope = PackageScope.Source,
            Purpose = ExportPurpose.Share,
            CreatedAt = createdAt,
            AppVersion = typeof(PackageService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            Entries = [.. files.Select(f => new PackageEntry(f.Key, f.Value.Kind, Hash(f.Value.Bytes), f.Value.Bytes.LongLength))],
            Notices = [.. plan.Sources.Select(Notice)],
            RevisionOrder = [.. plan.Revisions.Select(r => r.RevisionId)],
            Attestations = [.. plan.Sources.Select(s => new SourceAttestation(s.Id, TomeStackApp.OwnWorkStatement, s.ShareConfirmedAt!.Value))],
            AttachmentPolicy = SourceAttachmentPolicy,
        };
        return new ExportResult(plan.FileName, Zip(manifest, files), manifest);
    }

    /// <summary>The reader's limits, checked before anything is written: a pack nobody can import is worse than none.</summary>
    private static void CheckPackLimits(SortedDictionary<string, (string Kind, byte[] Bytes)> files, string advice)
    {
        if (files.Count + 1 > MaxEntries || files.Values.Any(f => f.Bytes.LongLength > MaxEntryBytes) || files.Values.Sum(f => f.Bytes.LongLength) > MaxTotalBytes - (1024 * 1024))
        {
            throw new PackageException([new("pack.too-large", $"These sources are larger than a package can hold ({MaxEntries:N0} entries, {MaxEntryBytes / (1024 * 1024)} MB per revision, {MaxTotalBytes / (1024 * 1024)} MB in all). Nothing was written. {advice}")]);
        }
    }

    /// <summary>The package's bytes: the manifest first, then the entries in path order, all stamped with its creation time.</summary>
    private static byte[] Zip(PackageManifest manifest, SortedDictionary<string, (string Kind, byte[] Bytes)> files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, ManifestPath, Json(manifest), manifest.CreatedAt);
            foreach (var (path, file) in files)
                WriteEntry(zip, path, file.Bytes, manifest.CreatedAt);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// The guard (D14 item 6) runs here, at every source-pack export, not only when a source is marked: each source must
    /// be homebrew made on this machine, not import-derived, and marked as shareable by its author.
    /// </summary>
    private SourcePackPlan PlanSourcePack(IReadOnlyList<Guid> sourceIds)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        var ids = sourceIds.Distinct().ToList();
        if (ids.Count is 0 or > MaxPackSources)
            throw new PackageException([new("pack.sources-required", $"Choose 1 to {MaxPackSources} sources to share.")]);

        var errors = new List<Diagnostic>();
        var sources = new List<SourceRecord>();
        foreach (var id in ids)
        {
            var source = store.FindSource(id);
            if (ShareProblem(id, source) is { } problem)
                errors.Add(problem);
            else
                sources.Add(source!);
        }
        if (errors.Count > 0)
            throw new PackageException(errors);

        var (revisions, drafts, warnings) = PackContent(sources, errors);
        foreach (var empty in sources.Where(s => !revisions.Any(r => r.Provenance.SourceId == s.Id)))
            errors.Add(new("pack.source-empty", $"'{empty.Title}' has no published content to share yet."));
        if (errors.Count > 0)
            throw new PackageException(errors);

        var name = sources.Count == 1 ? SafeFileName(sources[0].Title) : "homebrew-sources";
        return new SourcePackPlan(sources, revisions, drafts, warnings, $"{name}-source-pack.tomestack.zip");
    }

    /// <summary>
    /// What a pack of <paramref name="sources"/> carries (source and campaign packs): every published revision of each, in
    /// stored order. A content that another source also holds is added to <paramref name="errors"/>; content they refer to
    /// in a source the pack does not carry (and that is not bundled) is a warning.
    /// </summary>
    private (List<ContentRevision> Revisions, int Drafts, List<Diagnostic> Warnings) PackContent(IReadOnlyCollection<SourceRecord> sources, List<Diagnostic> errors)
    {
        var inPack = sources.Select(s => s.Id).ToHashSet();
        var all = store.ListRevisionsInOrder();
        var revisions = all.Where(r => inPack.Contains(r.Provenance.SourceId) && r.Status == RevisionStatus.Published).ToList();
        var drafts = all.Count(r => inPack.Contains(r.Provenance.SourceId) && r.Status != RevisionStatus.Published);
        // A content's revisions belong to one source. One that another source also holds cannot leave with this one, or
        // the receiver would get part of its history under the wrong license.
        var contentIds = revisions.Select(r => r.ContentId).ToHashSet();
        foreach (var spanning in all.Where(r => contentIds.Contains(r.ContentId) && !inPack.Contains(r.Provenance.SourceId)).DistinctBy(r => r.ContentId))
            errors.Add(new("pack.content-spans-sources", $"'{spanning.Name}' has revisions in another source too, so it cannot be shared from this one.", spanning.Reference));

        var warnings = new List<Diagnostic>();
        var bySource = all.GroupBy(r => r.ContentId).ToDictionary(g => g.Key, g => g.Last().Provenance.SourceId);
        var outside = revisions.SelectMany(References)
            .Select(contentId => bySource.TryGetValue(contentId, out var sourceId) ? sourceId : (Guid?)null)
            .OfType<Guid>()
            .Where(sourceId => !inPack.Contains(sourceId) && !_bundledSources.Contains(sourceId))
            .Distinct();
        foreach (var sourceId in outside)
        {
            var title = store.FindSource(sourceId)?.Title ?? sourceId.ToString();
            warnings.Add(new("pack.reference-outside", $"Content in this pack refers to content from '{title}', which the pack does not carry. Whoever imports it needs that source too."));
        }
        return (revisions, drafts, warnings);
    }

    /// <summary>
    /// The guard (D14 item 6), shared by source packs and campaign packs (M6 slice 2): why a source may not leave in a pack,
    /// or null when it is homebrew made on this machine, not import-derived, and marked as shareable by its author.
    /// </summary>
    private Diagnostic? ShareProblem(Guid id, SourceRecord? source) => source switch
    {
        null => new Diagnostic("pack.source-missing", $"Source {id} is not installed."),
        _ when _bundledSources.Contains(id) => new Diagnostic("pack.source-bundled", $"'{source.Title}' is bundled with TomeStack; everyone has it already, so a pack never carries it."),
        { ImportDerived: true } => new Diagnostic("pack.source-import-derived", $"'{source.Title}' holds material imported from a PDF, so it is never shared."),
        { Origin: SourceOrigin.Received } => new Diagnostic("pack.source-received", $"'{source.Title}' came from someone else's package, so you cannot share it as your own work."),
        { Redistributable: false } or { ShareConfirmedAt: null } => new Diagnostic("pack.source-not-shareable", $"'{source.Title}' is not marked as shareable. Mark it as shareable (you confirm it is your own work) first."),
        _ => null,
    };

    /// <summary>The content ids a revision refers to: granted content, choice options, the choice it extends, a roll's resource.</summary>
    private static IEnumerable<Guid> References(ContentRevision revision) =>
        revision.Effects.OfType<GrantEffect>().Where(g => g.Content is not null).Select(g => g.Content!.ContentId)
            .Concat(revision.Effects.OfType<ChoiceEffect>().SelectMany(c => c.Options).Select(o => o.ContentId))
            .Concat(revision.Effects.OfType<RollEffect>().Select(r => r.ResourceContent).OfType<Guid>())
            .Concat(revision.ExtendsChoice is { } extends ? new[] { extends.ContentId } : [])
            .Where(id => id != revision.ContentId);

    /// <summary>
    /// A source as a source pack writes it: no machine-local field, and nothing only this machine may set. The receiver
    /// records the origin ("received") itself.
    /// </summary>
    private static SourceRecord ForSourcePack(SourceRecord source) =>
        source with { PdfRef = null, AttachmentId = null, ImportDerived = null, Origin = null, ShareConfirmedAt = null, Sha256 = null };

    /// <summary>
    /// A source as a character package (v5) writes it. The M6 fields are left out, so 0.3.x builds read the package as
    /// before. That drops nothing that matters: the receiver records every new source as received (never markable as its
    /// own), and an import-derived source is never redistributable, so it stays out of every share.
    /// </summary>
    private static SourceRecord ForCharacterPackage(SourceRecord source) =>
        source with { PdfRef = null, AttachmentId = null, ImportDerived = null, Origin = null, ShareConfirmedAt = null };

    /// <summary>
    /// How an imported source record is stored (every scope; M6 slice 1). Only a full library restore brings back what
    /// this machine recorded (origin, the share confirmation), because the file is the user's own. Everything else is
    /// someone else's word: a new source is received, the import-derived flag only goes up, and no import raises an
    /// existing source's redistributable flag. A source that ends up with a PDF is import-derived, which also covers a
    /// v6 backup written before the flag existed.
    /// </summary>
    private SourceRecord Merged(SourceRecord imported, SourceRecord? local, PackageScope scope, Guid? attachmentId)
    {
        var library = scope == PackageScope.Library;
        // A backup whose source had a PDF is evidence even when the PDF does not come back (an unsafe linked path).
        var derived = imported.ImportDerived == true || local?.ImportDerived == true || attachmentId is not null
            || (library && imported.AttachmentId is not null);
        var merged = imported with
        {
            AttachmentId = attachmentId,
            ImportDerived = derived ? true : null,
            // A source already here keeps what this machine knows about it, an unknown (pre-v8) origin included: only a
            // source that is new here is recorded as received (review fix: a re-import of your own backup must not relabel
            // your homebrew).
            Origin = local is not null ? local.Origin : library ? imported.Origin : SourceOrigin.Received,
            Redistributable = library || local is null ? imported.Redistributable : imported.Redistributable && local.Redistributable,
            ShareConfirmedAt = library ? imported.ShareConfirmedAt : local?.ShareConfirmedAt,
        };
        if (derived || !merged.Redistributable || merged.Origin == SourceOrigin.Received)
            merged = merged with { ShareConfirmedAt = null };
        return derived ? merged with { Redistributable = false } : merged;
    }

    /// <summary>
    /// A source pack's own rules, on top of every package's (M6 slice 1). The pack must hold only what its sender could
    /// share: published revisions of its own sources, each marked shareable, none bundled, none import-derived. It may not
    /// add content to a source you made here, or new revisions to a content that belongs to another source here.
    /// </summary>
    private void CheckSourcePack(ParsedPackage parsed, List<Diagnostic> errors, List<Diagnostic> warnings)
    {
        // Newest means last stored. A revision this machine already has keeps its place, so when the sender's newest is
        // already here and the pack adds older ones, one of those becomes the newest here. Say so (review fix).
        foreach (var content in parsed.Revisions.GroupBy(r => r.ContentId))
        {
            if (store.RevisionHash(content.Last().RevisionId) is not null && content.Any(r => store.RevisionHash(r.RevisionId) is null))
                warnings.Add(new("pack.newest-changes", $"'{content.Last().Name}': the pack's newest revision is already here, and the pack adds older ones. Afterwards an older one counts as the newest: new picks and update offers use it. Characters keep the revision they pin.", content.Last().Reference));
        }
        var packSources = parsed.Sources.ToDictionary(s => s.Id);
        var attested = (parsed.Manifest.Attestations ?? []).Where(a => a is not null).Select(a => a.SourceId).ToHashSet();
        foreach (var source in parsed.Sources)
        {
            if (_bundledSources.Contains(source.Id))
                errors.Add(new("pack.source-bundled", $"The pack carries the bundled source '{source.Title}', which no source pack may carry."));
            else if (source.ImportDerived == true || !source.Redistributable)
                errors.Add(new("pack.source-not-shareable", $"The pack carries '{source.Title}', which its sender did not mark as shareable."));
            else if (!attested.Contains(source.Id))
                errors.Add(new("pack.attestation-missing", $"The pack carries '{source.Title}' without its author's statement that it is their own work."));
            if (!parsed.Revisions.Any(r => r.Provenance.SourceId == source.Id))
                errors.Add(new("pack.source-empty", $"The pack carries '{source.Title}' with no content."));
            // Adding to a source you made here is refused for every package (package.own-source, BuildPreview).
        }
        // A content's revisions belong to one source, inside the pack as against this machine (the export refuses it too).
        foreach (var split in parsed.Revisions.GroupBy(r => r.ContentId).Where(g => g.Select(r => r.Provenance.SourceId).Distinct().Count() > 1))
            errors.Add(new("pack.content-conflict", $"'{split.Last().Name}' has revisions in more than one of the pack's sources.", split.Last().Reference));
        foreach (var revision in parsed.Revisions)
        {
            if (revision.Status != RevisionStatus.Published)
                errors.Add(new("pack.draft-not-allowed", $"'{revision.Name}' is a draft; a source pack carries only published content.", revision.Reference));
            if (!packSources.ContainsKey(revision.Provenance.SourceId))
                errors.Add(new("pack.revision-source", $"'{revision.Name}' belongs to a source the pack does not carry.", revision.Reference));
            else if (store.RevisionsOf(revision.ContentId).FirstOrDefault(r => r.Provenance.SourceId != revision.Provenance.SourceId) is { } other)
                errors.Add(new("pack.content-conflict", $"'{revision.Name}' would add a revision to '{other.Name}', which belongs to another source here.", revision.Reference));
        }
    }

    /// <summary>
    /// Reads only <c>format</c> and <c>formatVersion</c> from the manifest before anything else, so a package from a newer
    /// TomeStack (a scope or field this build does not know) is refused as newer, not as damaged.
    /// </summary>
    private static bool ManifestVersionReadable(byte[] manifestBytes, List<Diagnostic> errors)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestBytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("formatVersion", out var version) || !version.TryGetInt32(out var number))
            {
                return true; // the full read reports what is missing
            }
            if (format.GetString() != PackageManifest.FormatName || number is < 1 or > PackageManifest.CurrentFormatVersion)
            {
                errors.Add(new("package.unsupported-format", $"Package format '{format.GetString()}' v{number} is not supported by this version (v{PackageManifest.CurrentFormatVersion}). Update TomeStack to import it."));
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            return true; // the full read reports it as invalid JSON
        }
    }
}
