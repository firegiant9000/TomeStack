using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>Import Lab L5: which share-purpose export a preflight is for. Backups and personal sheets have their own rules.</summary>
public enum PreflightKind { Package, SourcePack, CampaignPack, Sheet }

/// <param name="Selection">Character ids for <c>package</c> and <c>sheet</c>, source ids for <c>sourcePack</c>, the campaign id for <c>campaignPack</c>.</param>
public sealed record PreflightRequest(PreflightKind Kind, IReadOnlyList<Guid> Selection);

/// <summary>
/// One line of a preflight. A revision row names the content; a source-level row (a blocked source, a PDF) has the source or
/// attachment id as <see cref="ContentId"/> and an empty <see cref="RevisionId"/>.
/// </summary>
/// <param name="Origin">"bundled", "local", "received" or "unknown".</param>
/// <param name="Reason">One of the <c>preflight.*</c> codes on <see cref="Preflight"/>; null for an included row. The UI says it in words.</param>
public sealed record PreflightRow(
    Guid ContentId, Guid RevisionId, string Name, string SourceName, bool Redistributable, bool? ImportDerived, string Origin, string? Reason);

/// <summary>What a share export would carry (<see cref="Included"/>), leave out (<see cref="Omitted"/>), or refuse to go ahead with (<see cref="Blocking"/>).</summary>
public sealed record PreflightReport(
    IReadOnlyList<PreflightRow> Included, IReadOnlyList<PreflightRow> Omitted, IReadOnlyList<PreflightRow> Blocking, IReadOnlyList<string> Notices);

/// <summary>
/// Import Lab L5 (design 9.1, <c>package.preflight</c>): the one rule for what may leave the machine in a share. The share
/// exports (a character package, a source pack, a campaign pack, a sheet) ask the helpers here when they export, so the
/// report and the export cannot disagree and nothing is trusted from an earlier preview. Read-only: it writes nothing.
/// Backups (the library backup, a package with purpose Backup) and a personal sheet keep their own rules and do not come through here.
/// </summary>
public static class Preflight
{
    public const string ImportDerivedReason = "preflight.import-derived";
    public const string NotRedistributableReason = "preflight.not-redistributable";
    public const string DraftReason = "preflight.draft";
    public const string AttachmentReason = "preflight.attachment";
    public const string NoticeMissingReason = "preflight.notice-missing";

    /// <summary>A source with no published content: nothing to share (a pack of only drafts).</summary>
    public const string EmptyReason = "preflight.empty";

    /// <summary>A content whose revisions sit in another source too, so it cannot leave with one of them.</summary>
    public const string SpansSourcesReason = "preflight.spans-sources";

    /// <summary>The source is not installed here (a campaign's pending source, or content whose source record is gone).</summary>
    public const string SourceMissingReason = "preflight.source-missing";

    /// <summary>A bundled source: everyone has it already, so a source pack never carries it.</summary>
    public const string BundledReason = "preflight.bundled";

    /// <summary>A source that came from someone else's package: not this person's to share as their own work.</summary>
    public const string ReceivedReason = "preflight.received";

    /// <summary>At most this many sources in one pack: a pack is a deliberate share, not a library dump.</summary>
    public const int MaxPackSources = 50;

    public static PreflightReport Plan(SqliteStore store, PreflightRequest request, IReadOnlySet<Guid>? bundled = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Selection);
        bundled ??= new HashSet<Guid>();
        return request.Kind switch
        {
            PreflightKind.Package => ForCharacters(store, request.Selection, bundled, strict: true),
            PreflightKind.Sheet => ForCharacters(store, request.Selection, bundled, strict: false),
            PreflightKind.SourcePack => ForSourcePack(store, request.Selection, bundled),
            PreflightKind.CampaignPack => ForCampaignPack(store, request.Selection, bundled),
            _ => throw new PackageException([new("preflight.kind-unknown", $"Preflight kind '{request.Kind}' is not supported.")]),
        };
    }

    // ---- The rules, as the exporters use them ----

    /// <summary>
    /// Whether a source's content may go out in a character share or a shared sheet: redistributable and not import-derived
    /// (M6 slice 1, <see cref="SourceRecord.MayBeShared"/>), or bundled with the app (everyone has it).
    /// </summary>
    public static bool MayShareContent(SourceRecord source, IReadOnlySet<Guid> bundled)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bundled);
        return source.MayBeShared || bundled.Contains(source.Id);
    }

    /// <summary>
    /// The guard (D14 item 6), shared by source packs and campaign packs: why a source may not leave in a pack, or null
    /// when it is homebrew made on this machine, not import-derived, and marked as shareable by its author.
    /// </summary>
    internal static Diagnostic? ShareProblem(Guid id, SourceRecord? source, IReadOnlySet<Guid> bundled) => source switch
    {
        null => new Diagnostic("pack.source-missing", $"Source {id} is not installed."),
        _ when bundled.Contains(id) => new Diagnostic("pack.source-bundled", $"'{source.Title}' is bundled with TomeStack; everyone has it already, so a pack never carries it."),
        { ImportDerived: true } => new Diagnostic("pack.source-import-derived", $"'{source.Title}' holds imported material (from a PDF or a Markdown file), so it is never shared."),
        { Origin: SourceOrigin.Received } => new Diagnostic("pack.source-received", $"'{source.Title}' came from someone else's package, so you cannot share it as your own work."),
        { Redistributable: false } or { ShareConfirmedAt: null } => new Diagnostic("pack.source-not-shareable", $"'{source.Title}' is not marked as shareable. Mark it as shareable (you confirm it is your own work) first."),
        _ => null,
    };

    /// <summary>A character share's omission step: sources that may not be shared leave <paramref name="resolved"/>, listed with their characters.</summary>
    internal static List<OmittedSource> OmitUnshareable(ResolvedCharacters resolved)
    {
        var omitted = new List<OmittedSource>();
        // M6 slice 1: an import-derived source is never shared, whatever its redistributable flag says.
        foreach (var source in resolved.Sources.Values.Where(s => !s.MayBeShared).OrderBy(s => s.Id))
        {
            var left = resolved.Revisions.Values.Where(r => r.Provenance.SourceId == source.Id).OrderBy(r => r.RevisionId).ToList();
            omitted.Add(new(source.Id, source.Title, source.Publisher, source.License,
            [
                .. left.Select(r => new OmittedRevision(
                    r.Reference, r.Name, [.. resolved.Characters.Where(c => c.AllReferences().Contains(r.Reference)).Select(c => c.Id)])),
            ]));
            foreach (var revision in left)
                resolved.Revisions.Remove(revision.RevisionId);
            resolved.Sources.Remove(source.Id);
        }
        return omitted;
    }

    internal sealed record ResolvedCharacters(
        List<Character> Characters, Dictionary<Guid, ContentRevision> Revisions, Dictionary<Guid, SourceRecord> Sources, List<Diagnostic> Errors);

    /// <summary>Resolves characters, their pinned revisions (and what those grant, transitively) and those revisions' sources.</summary>
    internal static ResolvedCharacters Resolve(SqliteStore store, IEnumerable<Guid> characterIds)
    {
        var errors = new List<Diagnostic>();
        var characters = new List<Character>();
        foreach (var id in characterIds.Distinct())
        {
            if (store.FindCharacter(id) is { } character)
                characters.Add(character);
            else
                errors.Add(new("export.character-missing", $"Character {id} does not exist."));
        }

        var revisions = new Dictionary<Guid, ContentRevision>();
        foreach (var pin in characters.SelectMany(c => c.AllReferences()))
        {
            if (store.FindRevision(pin) is { } revision)
                revisions[revision.RevisionId] = revision;
            else
                errors.Add(new("export.revision-missing", $"Pinned revision {pin.RevisionId} is missing; repair the character before exporting.", pin));
        }
        // Content granted by included content (class features, a background's feat) is needed to calculate the same
        // sheet on the receiving machine. Follow grants transitively; a granted revision missing here is already shown
        // as missing on this machine's sheet, so it is skipped rather than failing the export.
        var pending = new Queue<ContentRevision>(revisions.Values);
        while (pending.Count > 0)
        {
            foreach (var grant in pending.Dequeue().Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content && g.Content is not null))
            {
                if (!revisions.ContainsKey(grant.Content!.RevisionId) && store.FindRevision(grant.Content) is { } granted)
                {
                    revisions[granted.RevisionId] = granted;
                    pending.Enqueue(granted);
                }
            }
        }
        // MECH-X item 1 (R38, S8): a v12 roll that casts a spell without a slot needs that spell on the receiving machine. The sheet
        // uses the revision the character pins, else the newest published one (CastSpellsOf), so the export carries the same.
        foreach (var roll in revisions.Values.Where(r => r.SchemaVersion >= ContentRevision.PaymentSchemaVersion).SelectMany(r => r.Effects.OfType<RollEffect>()).Where(r => r.CastsSpell is not null).ToList())
        {
            var spellId = roll.CastsSpell!.Value;
            if (revisions.Values.Any(r => r.ContentId == spellId))
                continue; // a pinned (or granted) revision of it is already in
            if (store.ListRevisions(spellId).LastOrDefault(r => r.Status == RevisionStatus.Published) is { } newest)
                revisions[newest.RevisionId] = newest;
        }

        var sources = new Dictionary<Guid, SourceRecord>();
        foreach (var revision in revisions.Values)
        {
            if (store.FindSource(revision.Provenance.SourceId) is { } source)
                sources[source.Id] = source;
            else
                errors.Add(new("export.source-missing", $"Source {revision.Provenance.SourceId} for '{revision.Name}' is missing.", revision.Reference));
        }
        return new(characters, revisions, sources, errors);
    }

    /// <summary>The 1 to <see cref="MaxPackSources"/> distinct source ids of a source pack.</summary>
    internal static List<Guid> RequirePackSources(IReadOnlyList<Guid> sourceIds)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        var ids = sourceIds.Distinct().ToList();
        if (ids.Count is 0 or > MaxPackSources)
            throw new PackageException([new("pack.sources-required", $"Choose 1 to {MaxPackSources} sources to share.")]);
        return ids;
    }

    /// <summary>Each source of a source pack is either shareable (returned) or has a guard problem.</summary>
    internal static (List<SourceRecord> Shareable, List<(Guid Id, SourceRecord? Source, Diagnostic Problem)> Problems) SplitPackSources(
        SqliteStore store, IEnumerable<Guid> ids, IReadOnlySet<Guid> bundled)
    {
        List<SourceRecord> shareable = [];
        List<(Guid, SourceRecord?, Diagnostic)> problems = [];
        foreach (var id in ids)
        {
            var source = store.FindSource(id);
            if (ShareProblem(id, source, bundled) is { } problem)
                problems.Add((id, source, problem));
            else
                shareable.Add(source!);
        }
        return (shareable, problems);
    }

    internal sealed record PackRevisions(
        IReadOnlyList<ContentRevision> All, List<ContentRevision> Published, List<ContentRevision> Drafts, List<ContentRevision> Spanning);

    /// <summary>
    /// What a pack of <paramref name="sources"/> carries: every published revision of each, in stored order. A content that
    /// another source also holds is <c>Spanning</c>: its revisions belong to one source, so it cannot leave with this one, or
    /// the receiver would get part of its history under the wrong license.
    /// </summary>
    internal static PackRevisions PackContentOf(SqliteStore store, IReadOnlyCollection<SourceRecord> sources)
    {
        var inPack = sources.Select(s => s.Id).ToHashSet();
        var all = store.ListRevisionsInOrder();
        var published = all.Where(r => inPack.Contains(r.Provenance.SourceId) && r.Status == RevisionStatus.Published).ToList();
        var drafts = all.Where(r => inPack.Contains(r.Provenance.SourceId) && r.Status != RevisionStatus.Published).ToList();
        var contentIds = published.Select(r => r.ContentId).ToHashSet();
        var spanning = all.Where(r => contentIds.Contains(r.ContentId) && !inPack.Contains(r.Provenance.SourceId)).DistinctBy(r => r.ContentId).ToList();
        return new(all, published, drafts, spanning);
    }

    internal static Campaign RequireCampaign(SqliteStore store, Guid campaignId) =>
        store.FindCampaign(campaignId) ?? throw new PackageException([new("pack.campaign-missing", $"Campaign {campaignId} does not exist.")]);

    /// <summary>
    /// Sorts a campaign's allowed sources: bundled ones are referenced; ones that pass the guard and have published content are
    /// carried; every other one is left out, with why.
    /// </summary>
    internal static (List<SourceRecord> Included, List<SourceRecord> Referenced, List<LeftOutSource> LeftOut) SortCampaignSources(
        SqliteStore store, Campaign campaign, IReadOnlySet<Guid> bundled)
    {
        var pending = (campaign.PendingSources ?? []).ToDictionary(p => p.SourceId);
        var all = store.ListRevisionsInOrder();
        var withContent = all.Where(r => r.Status == RevisionStatus.Published).Select(r => r.Provenance.SourceId).ToHashSet();
        // A content whose revisions sit in two sources cannot leave with one of them (as for a source pack). Here that
        // source is left out, not the whole pack refused (review fix): the profile is what is being shared.
        var spanning = all.GroupBy(r => r.ContentId).Where(g => g.Select(r => r.Provenance.SourceId).Distinct().Count() > 1)
            .SelectMany(g => g.Select(r => (Source: r.Provenance.SourceId, Content: g.Last())))
            .DistinctBy(s => s.Source).ToDictionary(s => s.Source, s => s.Content);
        List<SourceRecord> included = [], referenced = [];
        List<LeftOutSource> leftOut = [];
        // Titles and publishers have no length limit here, but a pending entry does: the pack names each one as the receiver
        // will store it (review fix).
        LeftOutSource Left(Guid id, string? title, string? publisher, string? license, Diagnostic reason)
        {
            var named = PendingSource.Clamped(id, title, publisher, license);
            return new(id, named.Title, named.Publisher, named.License, reason);
        }
        foreach (var id in campaign.AllowedSources.Distinct())
        {
            var source = store.FindSource(id);
            if (source is not null && bundled.Contains(id))
                referenced.Add(source);
            else if (source is null)
            {
                // Not installed here: pending from an earlier campaign pack, so its name is known, or unknown.
                var known = pending.GetValueOrDefault(id);
                leftOut.Add(Left(id, known?.Title, known?.Publisher, known?.License,
                    new("pack.source-missing", $"'{known?.Title ?? id.ToString()}' is not installed here, so the pack only names it.")));
            }
            else if (ShareProblem(id, source, bundled) is { } problem)
                leftOut.Add(Left(id, source.Title, source.Publisher, source.License, problem));
            else if (!withContent.Contains(id))
                leftOut.Add(Left(id, source.Title, source.Publisher, source.License, new("pack.source-empty", $"'{source.Title}' has no published content to share yet.")));
            else if (spanning.TryGetValue(id, out var split))
                leftOut.Add(Left(id, source.Title, source.Publisher, source.License, new("pack.content-spans-sources", $"'{split.Name}' has revisions in '{source.Title}' and in another source, so '{source.Title}' cannot be shared from here.", split.Reference)));
            else
                included.Add(source);
        }
        return (included, referenced, leftOut);
    }

    // ---- The report ----

    private static string OriginOf(SourceRecord? source, IReadOnlySet<Guid> bundled) =>
        source is null ? "unknown"
        : bundled.Contains(source.Id) ? "bundled"
        : source.Origin switch { SourceOrigin.Local => "local", SourceOrigin.Received => "received", _ => "unknown" };

    private static string ReasonOf(SourceRecord? source) =>
        source is null ? SourceMissingReason
        : source.ImportDerived == true ? ImportDerivedReason
        : source.Origin == SourceOrigin.Received ? ReceivedReason
        : NotRedistributableReason;

    /// <summary>A guard diagnostic (<see cref="ShareProblem"/> and the pack plans) in a preflight reason.</summary>
    private static string ReasonOf(Diagnostic problem) => problem.Code switch
    {
        "pack.source-import-derived" => ImportDerivedReason,
        "pack.source-empty" => EmptyReason,
        "pack.content-spans-sources" => SpansSourcesReason,
        "pack.source-missing" => SourceMissingReason,
        "pack.source-bundled" => BundledReason,
        "pack.source-received" => ReceivedReason,
        _ => NotRedistributableReason, // not marked as shareable, no attestation
    };

    private static PreflightRow RevisionRow(ContentRevision revision, SourceRecord? source, IReadOnlySet<Guid> bundled, string? reason) =>
        new(revision.ContentId, revision.RevisionId, revision.Name, source?.Title ?? "an unknown source", source?.Redistributable ?? false, source?.ImportDerived, OriginOf(source, bundled), reason);

    private static PreflightRow SourceRow(Guid id, SourceRecord? source, string name, IReadOnlySet<Guid> bundled, string reason) =>
        new(id, Guid.Empty, name, source?.Title ?? name, source?.Redistributable ?? false, source?.ImportDerived, OriginOf(source, bundled), reason);

    /// <summary>ADR-009 d: a PDF and the text read from it never travel in a share; a selection that reaches one lists it.</summary>
    private static IEnumerable<PreflightRow> AttachmentRows(IEnumerable<SourceRecord> sources, IReadOnlySet<Guid> bundled) =>
        sources.Where(s => s.AttachmentId is not null || s.PdfRef is not null).OrderBy(s => s.Id)
            .Select(s => SourceRow(s.AttachmentId ?? s.Id, s, $"PDF attached to '{s.Title}', and text read from it", bundled, AttachmentReason));

    /// <summary>One license notice per source that travels (never a second for the same source).</summary>
    private static List<string> NoticesOf(IEnumerable<SourceRecord> sources) =>
        [.. sources.DistinctBy(s => s.Id).OrderBy(s => s.Title, StringComparer.Ordinal).ThenBy(s => s.Id)
            .Select(s => string.IsNullOrWhiteSpace(s.Attribution) ? $"{s.Title} ({s.Publisher}): {s.License}" : $"{s.Title} ({s.Publisher}): {s.License}. {s.Attribution}")];

    private static IReadOnlyList<PreflightRow> Ordered(IEnumerable<PreflightRow> rows) =>
        [.. rows.OrderBy(r => r.SourceName, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.ContentId).ThenBy(r => r.RevisionId)];

    private static PreflightReport ForCharacters(SqliteStore store, IReadOnlyList<Guid> characterIds, IReadOnlySet<Guid> bundled, bool strict)
    {
        var resolved = Resolve(store, characterIds);
        // A character package refuses a missing pin or source (as its export does); a sheet is built from what calculates.
        if (strict && resolved.Errors.Count > 0)
            throw new PackageException(resolved.Errors);
        var sources = resolved.Sources.Values.ToList();
        var everything = resolved.Revisions.Values.ToList();
        var included = new List<PreflightRow>();
        var omitted = new List<PreflightRow>();
        var notices = new List<SourceRecord>();
        if (strict)
        {
            // The same step the character share takes: what leaves the set is what is omitted.
            var left = OmitUnshareable(resolved);
            var leftIds = left.SelectMany(o => o.Revisions).Select(r => r.Reference.RevisionId).ToHashSet();
            var bySource = sources.ToDictionary(s => s.Id);
            foreach (var revision in everything)
            {
                bySource.TryGetValue(revision.Provenance.SourceId, out var source);
                if (leftIds.Contains(revision.RevisionId))
                    omitted.Add(RevisionRow(revision, source, bundled, ReasonOf(source)));
                else
                    included.Add(RevisionRow(revision, source, bundled, null));
            }
            notices.AddRange(resolved.Sources.Values);
        }
        else
        {
            foreach (var revision in everything)
            {
                resolved.Sources.TryGetValue(revision.Provenance.SourceId, out var source);
                if (source is not null && MayShareContent(source, bundled))
                {
                    included.Add(RevisionRow(revision, source, bundled, null));
                    notices.Add(source);
                }
                else
                    omitted.Add(RevisionRow(revision, source, bundled, ReasonOf(source)));
            }
        }
        omitted.AddRange(AttachmentRows(sources, bundled));
        return new(Ordered(included), Ordered(omitted), [], NoticesOf(notices));
    }

    private static PreflightReport ForSourcePack(SqliteStore store, IReadOnlyList<Guid> selection, IReadOnlySet<Guid> bundled)
    {
        var ids = RequirePackSources(selection);
        var (shareable, problems) = SplitPackSources(store, ids, bundled);
        var blocking = problems.Select(p => SourceRow(p.Id, p.Source, p.Source?.Title ?? p.Id.ToString(), bundled, ReasonOf(p.Problem))).ToList();
        var content = PackContentOf(store, shareable);
        var bySource = shareable.ToDictionary(s => s.Id);
        blocking.AddRange(content.Spanning.Select(s => RevisionRow(s, store.FindSource(s.Provenance.SourceId), bundled, SpansSourcesReason)));
        blocking.AddRange(shareable.Where(s => !content.Published.Any(r => r.Provenance.SourceId == s.Id))
            .Select(s => SourceRow(s.Id, s, s.Title, bundled, EmptyReason)));
        var omitted = content.Drafts.Select(d => RevisionRow(d, bySource[d.Provenance.SourceId], bundled, DraftReason)).ToList();
        omitted.AddRange(AttachmentRows(shareable.Concat(problems.Select(p => p.Source).OfType<SourceRecord>()), bundled));
        var included = content.Published.Select(r => RevisionRow(r, bySource[r.Provenance.SourceId], bundled, null));
        return new(Ordered(included), Ordered(omitted), Ordered(blocking), NoticesOf(shareable));
    }

    private static PreflightReport ForCampaignPack(SqliteStore store, IReadOnlyList<Guid> selection, IReadOnlySet<Guid> bundled)
    {
        if (selection.Count != 1)
            throw new PackageException([new("pack.campaign-required", "Choose one campaign to share.")]);
        var campaign = RequireCampaign(store, selection[0]);
        var (included, _, leftOut) = SortCampaignSources(store, campaign, bundled);
        var content = PackContentOf(store, included);
        var bySource = included.ToDictionary(s => s.Id);
        // A campaign pack leaves a source out rather than refusing: the profile is what is being shared.
        var omitted = leftOut.Select(l => SourceRow(l.SourceId, store.FindSource(l.SourceId), l.Title, bundled, ReasonOf(l.Reason))).ToList();
        omitted.AddRange(content.Drafts.Select(d => RevisionRow(d, bySource[d.Provenance.SourceId], bundled, DraftReason)));
        omitted.AddRange(AttachmentRows(included.Concat(leftOut.Select(l => store.FindSource(l.SourceId)).OfType<SourceRecord>()), bundled));
        var blocking = content.Spanning.Select(s => RevisionRow(s, store.FindSource(s.Provenance.SourceId), bundled, SpansSourcesReason));
        var rows = content.Published.Select(r => RevisionRow(r, bySource[r.Provenance.SourceId], bundled, null));
        return new(Ordered(rows), Ordered(omitted), Ordered(blocking), NoticesOf(included));
    }
}
