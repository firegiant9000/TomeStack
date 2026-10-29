using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// <c>content.coverage</c> (Import Lab, D62 L1): which sources to read. With no source, every source; with a content id, that
/// one content (a class with its levels). <see cref="IncludeDrafts"/> shows a content whose newest revision is a draft as that draft.
/// </summary>
public sealed record CoverageRequest(Guid? SourceId = null, Guid? ContentId = null, bool IncludeDrafts = false);

/// <summary>What the request asked for, echoed back.</summary>
public sealed record CoverageScope(Guid? SourceId, Guid? ContentId, bool IncludeDrafts);

/// <summary>Effects by how far TomeStack really goes with them (the capability registry's effective value). Unsupported is the fourth state: the type is not known.</summary>
public readonly record struct CoverageCounts(int Automatic, int Assisted, int Reference, int Unsupported)
{
    public static CoverageCounts operator +(CoverageCounts a, CoverageCounts b) =>
        new(a.Automatic + b.Automatic, a.Assisted + b.Assisted, a.Reference + b.Reference, a.Unsupported + b.Unsupported);
}

/// <param name="EffectId">Null for the text-only line of a feature that has no effects.</param>
public sealed record EffectCoverage(
    string? EffectId, string Type, AutomationStatus Author, AutomationStatus? Ceiling, AutomationStatus? Effective, string? Reason, string? Evidence, bool Unsupported);

/// <param name="Status">Published, or draft (only when the request included drafts).</param>
/// <param name="MissingMechanic">The feature may need a mechanic: it has an unsupported effect, or no effects and text that sounds like one. Advice, never a gate.</param>
public sealed record FeatureCoverage(
    Guid ContentId, Guid RevisionId, string Name, ContentKind Kind, RevisionStatus Status, CoverageCounts Totals, bool MissingMechanic, IReadOnlyList<EffectCoverage> Effects);

public sealed record LevelCoverage(int Level, IReadOnlyList<FeatureCoverage> Features);

public sealed record ClassCoverage(Guid ContentId, string Name, CoverageCounts Totals, IReadOnlyList<LevelCoverage> Levels);

public sealed record SourceCoverage(Guid SourceId, string Name, CoverageCounts Totals, IReadOnlyList<ClassCoverage> Classes, IReadOnlyList<FeatureCoverage> Other);

public sealed record CoverageReport(CoverageScope Scope, CoverageCounts Totals, IReadOnlyList<SourceCoverage> Sources);

public sealed partial class TomeStackApp
{
    /// <summary><c>content.coverage</c> (D62 L1): what the engine does with each effect of the installed content. Read-only.</summary>
    public CoverageReport Coverage(CoverageRequest request) => global::TomeStack.AppService.Coverage.Report(_store, request);
}

/// <summary>
/// <c>content.coverage</c> (Import Lab, D62 L1; design 5): for each source, what its content gets from the engine, counted from the
/// capability registry (<see cref="CapabilityRegistry.Effective"/>, never the ceiling alone). Read-only, needs no character.
/// Rows are per content id, so the two rules families (and a name used in both) are never merged.
/// </summary>
public static class Coverage
{
    /// <summary>
    /// Generic English phrases that often mark a rule TomeStack could automate. A feature with no effects whose text holds one is
    /// listed as "may need a mechanic". Advice only, never a gate; none is taken from a book.
    /// </summary>
    public static readonly string[] MechanicPatterns = ["once per", "regain", "expend", "as a reaction", "bonus action"];

    private static readonly Capability TextEntry = CapabilityRegistry.Entries.Single(e => e is { EffectType: CapabilityRegistry.TextType, Field: null });

    /// <summary>One <c>ListRevisionsInOrder()</c> read per call, grouped in memory.</summary>
    public static CoverageReport Report(SqliteStore store, CoverageRequest request)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceId is { } wanted && store.FindSource(wanted) is null)
            throw new AppValidationException([new("source.not-found", $"Source {wanted} is not installed.")]);

        var all = store.ListRevisionsInOrder();
        var (chosen, drafts) = Choose(all, request.IncludeDrafts);
        Guid? home = null;
        if (request.ContentId is { } contentId)
        {
            if (!chosen.TryGetValue(contentId, out var shown) && !drafts.TryGetValue(contentId, out shown))
            {
                var message = all.Any(r => r.ContentId == contentId)
                    ? $"Content {contentId} has no published revision. Include drafts to see its draft."
                    : $"Content {contentId} is not installed.";
                throw new AppValidationException([new("content.not-found", message)], "content.not-found");
            }
            home = shown.Provenance.SourceId; // a class is read with the features of its own source
            if (request.SourceId is { } asked && asked != home)
                throw new AppValidationException([new("content.not-found", $"Content {contentId} is not in source {asked}.")], "content.not-found");
        }

        bool InScope(ContentRevision r) =>
            (request.SourceId is null || r.Provenance.SourceId == request.SourceId) && (home is null || r.Provenance.SourceId == home);
        var inScope = chosen.Values.Where(InScope).ToList();
        var draftScope = drafts.Values.Where(InScope).ToList();
        // Drafts are listed beside the published rows and never counted: the walk sees the published revisions only (the latest of
        // each content, so a subclass that dropped its extendsChoice no longer joins the class); validation sees everything.
        var walkCatalog = new InMemoryContentCatalog([], chosen.Values);
        var validationCatalog = new InMemoryContentCatalog([], all);
        var validated = new Dictionary<Guid, HashSet<string>>(); // draft revision id -> effect ids with a validation error
        var rows = inScope.ToDictionary(r => r.ContentId, r => Row(r, validationCatalog, validated));
        var draftRows = draftScope.ToDictionary(r => r.ContentId, r => Row(r, validationCatalog, validated));

        var sourceNames = store.ListSources().ToDictionary(s => s.Id, s => s.Title);
        var sources = new List<SourceCoverage>();
        foreach (var sourceId in inScope.Concat(draftScope).Select(r => r.Provenance.SourceId).Distinct())
        {
            sources.Add(Source(
                sourceId, sourceNames.GetValueOrDefault(sourceId) ?? sourceId.ToString("D"),
                [.. inScope.Where(r => r.Provenance.SourceId == sourceId)], [.. draftScope.Where(r => r.Provenance.SourceId == sourceId).Select(r => draftRows[r.ContentId])],
                rows, draftRows, walkCatalog, request.ContentId));
        }
        if (request.SourceId is { } only && sources.Count == 0)
            sources.Add(new(only, sourceNames.GetValueOrDefault(only) ?? only.ToString("D"), default, [], []));

        sources.Sort((a, b) => Compare(a.Name, a.SourceId, b.Name, b.SourceId));
        var totals = sources.Aggregate(default(CoverageCounts), (sum, s) => sum + s.Totals);
        return new(new(request.SourceId, request.ContentId, request.IncludeDrafts), totals, sources);
    }

    /// <summary>
    /// The latest published revision of each content by store order (rowid), and, with drafts included, each content's newest revision
    /// when that is a draft. A published revision keeps its draft behind it in the store, so a draft only shows while it is newer.
    /// </summary>
    internal static (Dictionary<Guid, ContentRevision> Published, Dictionary<Guid, ContentRevision> Drafts) Choose(IReadOnlyList<ContentRevision> all, bool includeDrafts)
    {
        var published = new Dictionary<Guid, ContentRevision>();
        var newest = new Dictionary<Guid, ContentRevision>();
        foreach (var revision in all)
        {
            newest[revision.ContentId] = revision;
            if (revision.Status == RevisionStatus.Published)
                published[revision.ContentId] = revision;
        }
        var drafts = includeDrafts
            ? newest.Where(n => n.Value.Status == RevisionStatus.Draft).ToDictionary(n => n.Key, n => n.Value)
            : [];
        return (published, drafts);
    }

    private static IEnumerable<FeatureCoverage> InOrder(IEnumerable<FeatureCoverage> rows) =>
        rows.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.ContentId).ThenByDescending(f => f.Status);

    private static SourceCoverage Source(
        Guid sourceId, string name, List<ContentRevision> contents, List<FeatureCoverage> draftsHere, Dictionary<Guid, FeatureCoverage> rows,
        Dictionary<Guid, FeatureCoverage> draftRows, InMemoryContentCatalog catalog, Guid? only)
    {
        var classes = new List<ClassCoverage>();
        var placed = new HashSet<Guid>();
        var ids = contents.Select(c => c.ContentId).ToHashSet();
        foreach (var cls in contents.Where(r => r.Kind == ContentKind.Class))
        {
            var byLevel = new SortedDictionary<int, List<FeatureCoverage>>();
            var counted = new HashSet<Guid>();
            var total = default(CoverageCounts);
            void Place(int level, FeatureCoverage row)
            {
                if (!byLevel.TryGetValue(level, out var list))
                    byLevel[level] = list = [];
                if (list.Any(f => f.RevisionId == row.RevisionId))
                    return;
                list.Add(row);
                if (row.Status == RevisionStatus.Published && counted.Add(row.ContentId))
                    total += row.Totals;
            }

            // The class's own rules (hit die, spellcasting, tables, ungated grants) are its row, listed at level 1 under its name.
            var own = rows[cls.ContentId];
            if (own.Effects.Count > 0)
                Place(1, own);
            foreach (var (level, contentId, _) in ClassLevels.Walk(cls, catalog))
            {
                // Only a feature of this same source sits under the class: a foreign one stays in its own source's report, so no
                // content is counted in two sources.
                if (rows.TryGetValue(contentId, out var row) && row.Kind != ContentKind.Class && ids.Contains(contentId))
                {
                    Place(level, row);
                    if (draftRows.TryGetValue(contentId, out var draft))
                        Place(level, draft); // beside the published row, never counted
                    placed.Add(contentId);
                }
            }
            classes.Add(new(cls.ContentId, cls.Name, total, [.. byLevel.Select(l => new LevelCoverage(l.Key, [.. InOrder(l.Value)]))]));
        }

        var other = InOrder(
            contents.Where(r => r.Kind != ContentKind.Class && !placed.Contains(r.ContentId)).Select(r => rows[r.ContentId])
                .Concat(draftsHere.Where(d => !placed.Contains(d.ContentId)))).ToList();
        classes.Sort((a, b) => Compare(a.Name, a.ContentId, b.Name, b.ContentId));

        if (only is { } asked)
        {
            // One content asked for: a class with its levels, else the one feature (and its draft), wherever it sits.
            classes = [.. classes.Where(c => c.ContentId == asked)];
            other = [.. InOrder(
                contents.Where(r => r.ContentId == asked && r.Kind != ContentKind.Class).Select(r => rows[r.ContentId])
                    .Concat(draftsHere.Where(d => d.ContentId == asked)))];
            var shown = classes.Aggregate(default(CoverageCounts), (sum, c) => sum + c.Totals)
                + other.Where(f => f.Status == RevisionStatus.Published).Aggregate(default(CoverageCounts), (sum, f) => sum + f.Totals);
            return new(sourceId, name, shown, classes, other);
        }

        // Each published content once, wherever it is listed, so a feature under two classes is not counted twice. Drafts never count.
        var totals = contents.Aggregate(default(CoverageCounts), (sum, r) => sum + rows[r.ContentId].Totals);
        return new(sourceId, name, totals, classes, other);
    }

    private static int Compare(string a, Guid aId, string b, Guid bId)
    {
        var byName = StringComparer.OrdinalIgnoreCase.Compare(a, b);
        if (byName == 0)
            byName = StringComparer.Ordinal.Compare(a, b);
        return byName != 0 ? byName : aId.CompareTo(bId);
    }

    private static FeatureCoverage Row(ContentRevision revision, InMemoryContentCatalog catalog, Dictionary<Guid, HashSet<string>> validated)
    {
        // A class's level-gated content grants and choices are its structure, shown as the features they place, not counted as effects.
        var effects = revision.Kind == ContentKind.Class
            ? revision.Effects.Where(e => e is not GrantEffect { Grant: GrantKind.Content, Level: not null } && e is not ChoiceEffect { Level: not null }).ToList()
            : [.. revision.Effects];
        var failing = Failing(revision, catalog, validated);
        var lines = new List<EffectCoverage>();
        var totals = default(CoverageCounts);
        foreach (var effect in effects)
        {
            // The same rule as the mechanics report: a diagnostic on the effect means it is not applied as written. A stored
            // published revision passed validation when it was published, so only a draft is checked here.
            var effective = CapabilityRegistry.Effective(effect, effect.Automation, failing.Contains(effect.Id));
            lines.Add(new(effect.Id, effect.Type, effect.Automation, effective.Ceiling, effective.Status, effective.Reason, effective.Evidence, effective.Unsupported));
            totals += effective switch
            {
                { Unsupported: true } => new CoverageCounts(0, 0, 0, 1),
                { Status: AutomationStatus.Automatic } => new CoverageCounts(1, 0, 0, 0),
                { Status: AutomationStatus.Assisted } => new CoverageCounts(0, 1, 0, 0),
                _ => new CoverageCounts(0, 0, 1, 0),
            };
        }
        var textOnly = revision.Effects.Count == 0 && revision.Kind != ContentKind.Class;
        if (textOnly)
        {
            // A feature with no effects is its text alone: one reference mechanic, as the mechanics report counts it.
            lines.Add(new(null, TextEntry.EffectType, AutomationStatus.Reference, TextEntry.Ceiling, TextEntry.Ceiling, TextEntry.Reason, TextEntry.Evidence, false));
            totals += new CoverageCounts(0, 0, 1, 0);
        }
        var missing = lines.Any(l => l.Unsupported)
            || (revision.Effects.Count == 0 && revision.Summary is { } text && MechanicPatterns.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)));
        return new(revision.ContentId, revision.RevisionId, revision.Name, revision.Kind, revision.Status, totals, missing, lines);
    }

    /// <summary>The ids of the effects of a draft that fail validation (a formula that does not parse, a bad reference); empty for a published revision.</summary>
    private static HashSet<string> Failing(ContentRevision revision, InMemoryContentCatalog catalog, Dictionary<Guid, HashSet<string>> validated)
    {
        if (revision.Status != RevisionStatus.Draft)
            return [];
        if (!validated.TryGetValue(revision.RevisionId, out var ids))
        {
            ids = [.. ContentValidator.Validate(revision, catalog).Errors.Select(e => e.EffectId).OfType<string>()];
            validated[revision.RevisionId] = ids;
        }
        return ids;
    }
}
