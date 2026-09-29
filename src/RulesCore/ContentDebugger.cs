namespace TomeStack.RulesCore;

/// <summary>How much a debugger finding matters. Only validation errors are errors: they block publishing.</summary>
public enum FindingSeverity { Error, Warning, Note }

/// <summary>One thing the debugger found, on a revision under study and, when it concerns one, on one of its effects.</summary>
public sealed record DebugFinding(string Code, FindingSeverity Severity, string Message, ContentReference Content, string ContentName, string? EffectId = null);

/// <param name="Scope">The revisions studied, in order.</param>
/// <param name="Truncated">The graph walk hit its bounds, so the findings that need it were left out.</param>
public sealed record DebugReport(IReadOnlyList<ContentReference> Scope, IReadOnlyList<DebugFinding> Findings, bool Truncated = false)
{
    public int Errors => Findings.Count(f => f.Severity == FindingSeverity.Error);

    public int Warnings => Findings.Count(f => f.Severity == FindingSeverity.Warning);
}

/// <summary>
/// M5 slice 2 (B02, the homebrew debugger): explains why homebrew does not work as meant, before any character has it.
/// It merges <see cref="ContentValidator"/> (missing references, invalid formulas, levels above 20, …) with findings that
/// need the whole <see cref="ContentGraph"/>: resources nothing spends or recovers, recoveries and rolls whose resource
/// is not in their revision, grants that are never followed, content no class or choice reaches, choices with nothing to
/// pick, extensions of a choice the class no longer offers, scales that are read but undefined, defined but unread, or
/// defined by a class and its subclass, and grants of an older revision. Only validation errors are errors; the rest are
/// warnings and notes. Read-only: it writes nothing and changes no calculation. Every lookup is built once per report,
/// so the cost grows with the content's size, not its square (SPEC Q-02).
/// </summary>
public static class ContentDebugger
{
    /// <summary>
    /// Validation codes the graph findings state more exactly, so each problem is listed once: a recovery counts only for
    /// a resource of its own revision (the calculator never looks elsewhere), and the scale checks cover drafts too.
    /// </summary>
    private static readonly HashSet<string> Superseded = new(StringComparer.Ordinal)
    {
        "validate.recovery-resource", "validate.scale-unknown", "validate.scale-duplicate-older",
    };

    /// <param name="scope">The revisions to report on (each the current revision of its content).</param>
    /// <param name="revisionsInOrder">Every stored revision, in insertion order.</param>
    /// <param name="catalog">For validation; the scope is validated together, so its drafts may name each other.</param>
    /// <param name="context">
    /// Revisions that take part in the graph as current revisions but are not reported on, such as the other drafts of
    /// the source an unsaved revision belongs to. The scope wins over them.
    /// </param>
    public static DebugReport Diagnose(
        IReadOnlyList<ContentRevision> scope, IEnumerable<ContentRevision> revisionsInOrder, IContentCatalog catalog, IEnumerable<ContentRevision>? context = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(revisionsInOrder);
        ArgumentNullException.ThrowIfNull(catalog);
        var studied = scope.Select(r => r.ContentId).ToHashSet();
        return Diagnose(scope, ContentGraph.Build(revisionsInOrder, [.. (context ?? []).Where(r => !studied.Contains(r.ContentId)), .. scope]), catalog);
    }

    /// <summary>The same over a graph already built, whose current revisions include the scope (tests build it with lower bounds).</summary>
    public static DebugReport Diagnose(IReadOnlyList<ContentRevision> scope, ContentGraph graph, IContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(catalog);
        var index = new Index(graph);
        // The scope is validated together (so its drafts may name each other), keyed once; the first of a repeated reference counts.
        var batch = new Dictionary<ContentReference, ContentRevision>();
        foreach (var revision in scope)
            batch.TryAdd(revision.Reference, revision);
        var findings = new List<DebugFinding>();
        foreach (var revision in scope)
        {
            var own = new List<DebugFinding>();
            void Add(string code, FindingSeverity severity, string message, string? effectId = null) =>
                own.Add(new(code, severity, message, revision.Reference, revision.Name, effectId));

            var report = ContentValidator.Validate(revision, catalog, batch);
            foreach (var error in report.Errors)
                Add(error.Code, FindingSeverity.Error, error.Message, error.EffectId);
            foreach (var warning in report.Warnings.Where(w => !Superseded.Contains(w.Code)))
                Add(warning.Code, FindingSeverity.Warning, warning.Message, warning.EffectId);

            Resources(revision, index, Add);
            Links(revision, graph, own, Add);
            // Reach findings would be false where the bounded walk never got to; they are left out and the report says so.
            if (!graph.Truncated)
            {
                Reach(revision, graph, index, Add);
                Scales(revision, graph, index, own, Add);
            }
            Pins(revision, graph, Add);
            findings.AddRange(own.OrderBy(f => f.Severity));
        }
        return new DebugReport([.. scope.Select(r => r.Reference)], findings, graph.Truncated);
    }

    private delegate void AddFinding(string code, FindingSeverity severity, string message, string? effectId = null);

    /// <summary>The lookups every check reads, built once per report.</summary>
    private sealed class Index
    {
        private readonly ContentGraph _graph;
        private readonly Dictionary<Guid, List<(string EffectId, string Identifier)>> _identifiers = [];

        public Index(ContentGraph graph)
        {
            _graph = graph;
            // Resources another content's rolls spend (content v6 resourceContent), by the content that defines them.
            foreach (var revision in graph.Current.Values)
            {
                foreach (var roll in revision.Effects.OfType<RollEffect>().Where(r => r.ResourceContent is { } holder && holder != revision.ContentId && r.ResourceId is not null))
                    Set(SpentElsewhere, roll.ResourceContent!.Value).Add(roll.ResourceId!);
            }
            // The SCALE ids read by what each class reaches, and by what reaches it through each subclass.
            foreach (var (contentId, _) in graph.Current)
            {
                var reaches = graph.Reaches(contentId);
                if (reaches.Count == 0)
                    continue;
                var reads = ScaleReads(contentId).Select(r => r.ScaleId).ToHashSet(StringComparer.Ordinal);
                foreach (var reach in reaches)
                {
                    Set(ReadByClass, reach.ClassId).UnionWith(reads);
                    if (reach.SubclassId is { } subclass)
                        Set(ReadBySubclass, subclass).UnionWith(reads);
                }
            }
        }

        public Dictionary<Guid, HashSet<string>> SpentElsewhere { get; } = [];

        public Dictionary<Guid, HashSet<string>> ReadByClass { get; } = [];

        public Dictionary<Guid, HashSet<string>> ReadBySubclass { get; } = [];

        /// <summary>The identifiers each formula of a content's current revision reads, parsed once.</summary>
        public List<(string EffectId, string Identifier)> Identifiers(Guid contentId)
        {
            if (!_identifiers.TryGetValue(contentId, out var list))
                _identifiers[contentId] = list = _graph.Current.TryGetValue(contentId, out var revision) ? [.. ContentGraph.Identifiers(revision)] : [];
            return list;
        }

        public IEnumerable<(string EffectId, string ScaleId)> ScaleReads(Guid contentId) =>
            Identifiers(contentId).Where(i => FormulaIdentifiers.IsScale(i.Identifier)).Select(i => (i.EffectId, FormulaIdentifiers.ScaleId(i.Identifier))).Distinct();

        /// <summary>The scale ids a class or subclass defines that calculate: automatic ones, as <c>CollectScales</c> takes.</summary>
        public HashSet<string> Defined(Guid? contentId)
        {
            if (contentId is not { } id)
                return [];
            if (!_defined.TryGetValue(id, out var set))
                _defined[id] = set = ActiveScales(_graph.Current.GetValueOrDefault(id)).Select(s => s.ScaleId).ToHashSet(StringComparer.Ordinal);
            return set;
        }

        private readonly Dictionary<Guid, HashSet<string>> _defined = [];

        private static HashSet<string> Set(Dictionary<Guid, HashSet<string>> map, Guid key)
        {
            if (!map.TryGetValue(key, out var set))
                map[key] = set = new(StringComparer.Ordinal);
            return set;
        }
    }

    private static IEnumerable<ScaleEffect> ActiveScales(ContentRevision? revision) =>
        revision?.Effects.OfType<ScaleEffect>().Where(s => s.Automation == AutomationStatus.Automatic) ?? [];

    /// <summary>
    /// A resource's uses change through what spends it (a roll or a toggle of its revision, or a roll elsewhere that names
    /// it with <c>resourceContent</c>) and what recovers it (a recovery of its own revision, the only place the calculator
    /// looks).
    /// </summary>
    private static void Resources(ContentRevision revision, Index index, AddFinding add)
    {
        var defined = revision.Effects.OfType<ResourceEffect>().Select(r => r.ResourceId).ToHashSet(StringComparer.Ordinal);
        var recovered = revision.Effects.OfType<RecoveryEffect>().Select(r => r.ResourceId).ToHashSet(StringComparer.Ordinal);
        var ownRolls = revision.Effects.OfType<RollEffect>().Where(r => r.ResourceId is not null && (r.ResourceContent is null || r.ResourceContent == revision.ContentId)).ToList();
        var spent = ownRolls.Select(r => r.ResourceId!)
            .Concat(revision.Effects.OfType<ToggleEffect>().Where(t => t.ResourceId is not null).Select(t => t.ResourceId!))
            .Concat(index.SpentElsewhere.GetValueOrDefault(revision.ContentId) ?? [])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var recovery in revision.Effects.OfType<RecoveryEffect>().Where(r => !defined.Contains(r.ResourceId)))
            add("debug.recovery-orphan", FindingSeverity.Warning, $"Recovery '{recovery.Id}' restores '{recovery.ResourceId}', which this revision does not define. A recovery counts only for a resource of its own revision, so it never applies.", recovery.Id);
        foreach (var roll in ownRolls.Where(r => !defined.Contains(r.ResourceId!)))
            add("debug.roll-resource-unknown", FindingSeverity.Warning, $"Roll '{roll.Id}' spends '{roll.ResourceId}', which this revision does not define, so the action cannot be used from the sheet. Define the resource here, or name the content that defines it.", roll.Id);
        foreach (var resource in revision.Effects.OfType<ResourceEffect>().Where(r => r.Automation != AutomationStatus.Reference && !recovered.Contains(r.ResourceId) && !spent.Contains(r.ResourceId)))
            add("debug.resource-dead", FindingSeverity.Warning, $"Resource '{resource.ResourceId}' is never spent by a roll or toggle and never recovered, so its uses change only by hand. Add a roll that spends it, or a recovery.", resource.Id);
    }

    /// <summary>
    /// Edges that never apply, whatever reaches the revision: a granted content's own grants, a choice with nothing to
    /// pick, and an extension of a choice the extended content's current revision no longer offers.
    /// </summary>
    private static void Links(ContentRevision revision, ContentGraph graph, List<DebugFinding> own, AddFinding add)
    {
        var incoming = graph.To(revision.ContentId);
        // Granted content arrives as non-root: its own content grants are not followed (grant.nested-ignored at calculation).
        if (incoming.Any(e => e.Kind == GraphEdgeKind.Grant) && !incoming.Any(e => e.Kind != GraphEdgeKind.Grant))
        {
            var by = graph.Current.GetValueOrDefault(incoming.First(e => e.Kind == GraphEdgeKind.Grant).From)?.Name ?? "another content";
            foreach (var grant in revision.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content))
                add("debug.grant-nested", FindingSeverity.Warning, $"Grant '{grant.Id}' never applies: this content is granted by '{by}', and grants are followed one level deep. Grant it from '{by}' instead, or offer this content in a choice.", grant.Id);
        }

        foreach (var choice in revision.Effects.OfType<ChoiceEffect>().Where(c => c.Options.Count == 0))
        {
            if (!graph.From(revision.ContentId).Any(e => e.Kind == GraphEdgeKind.Extension && e.ChoiceId == choice.ChoiceId))
                add("debug.choice-empty", FindingSeverity.Warning, $"Choice '{choice.ChoiceId}' has no options and nothing extends it yet, so it offers nothing to pick. Author a subclass (or other content) offered in this choice.", choice.Id);
        }

        // Validation accepts a choice that any revision had; the calculator offers extensions only on the one it calculates.
        if (revision.ExtendsChoice is { } extends && graph.Current.GetValueOrDefault(extends.ContentId) is { } target
            && !target.Effects.OfType<ChoiceEffect>().Any(c => c.ChoiceId == extends.ChoiceId)
            && !own.Any(f => f.Code == "validate.extends-choice-unknown"))
        {
            add("debug.extension-choice-missing", FindingSeverity.Warning, $"'{target.Name}' in its current revision no longer offers choice '{extends.ChoiceId}' (an older revision did), so this is offered only to characters still on that older revision.");
        }
    }

    /// <summary>What reaches the revision: a subclass needs something that offers it, and content that reads its class (CLASS_LEVEL or a scale) must be reached from a class.</summary>
    private static void Reach(ContentRevision revision, ContentGraph graph, Index index, AddFinding add)
    {
        var incoming = graph.To(revision.ContentId);
        var reached = graph.Reaches(revision.ContentId);
        if (revision.Kind == ContentKind.Subclass)
        {
            if (incoming.Count == 0 && reached.Count == 0)
                add("debug.subclass-unreachable", FindingSeverity.Warning, "No class or feature offers this subclass in a choice, so no character can take it. Choose the class choice it is offered in.");
        }
        else if (revision.Kind != ContentKind.Class && reached.Count == 0 && ClassContext(index.Identifiers(revision.ContentId)) is { } reads)
        {
            add("debug.feature-unreachable", FindingSeverity.Warning, incoming.Count == 0
                ? $"Nothing grants or offers this content, but it {reads}, which only works inside a class. Grant it from a class or subclass."
                : $"It {reads}, which only works inside a class, but no class reaches it (what grants or offers it is not part of a class).");
        }
    }

    /// <summary>
    /// How a revision depends on being inside a class, in words; null when it does not. A level gate is not one: outside
    /// a class it counts character levels.
    /// </summary>
    private static string? ClassContext(List<(string EffectId, string Identifier)> identifiers)
    {
        if (identifiers.Any(i => FormulaIdentifiers.IsScale(i.Identifier)))
            return "reads a class column (SCALE)";
        return identifiers.Any(i => i.Identifier == FormulaIdentifiers.ClassLevel) ? "reads CLASS_LEVEL" : null;
    }

    /// <summary>
    /// Scales, per class that reaches the revision (ADR-010): each formula's <c>SCALE.&lt;id&gt;</c> must be defined by the
    /// class or the subclass it comes through; each scale should be read by something in its class; and a class and its
    /// subclass never define the same id. Only automatic scales calculate.
    /// </summary>
    private static void Scales(ContentRevision revision, ContentGraph graph, Index index, List<DebugFinding> own, AddFinding add)
    {
        var reached = graph.Reaches(revision.ContentId);

        // Read but undefined, in any way a class reaches it. (Content no class reaches is reported by Reach.)
        foreach (var (effectId, scaleId) in index.ScaleReads(revision.ContentId))
        {
            var missing = reached
                .Where(reach => !index.Defined(reach.ClassId).Contains(scaleId) && !index.Defined(reach.SubclassId).Contains(scaleId))
                .Select(reach => graph.Current.GetValueOrDefault(reach.ClassId)?.Name)
                .OfType<string>()
                .Distinct()
                .ToList();
            if (missing.Count > 0)
                add("debug.scale-undefined", FindingSeverity.Warning, $"Effect '{effectId}' reads SCALE.{scaleId}, which is not defined for '{string.Join("', '", missing)}' (by the class, or by the subclass it comes through), so the formula is unavailable there.", effectId);
        }

        // Defined but read by nothing in the class (a class's scale) or through the subclass (a subclass's own).
        var readers = revision.Kind == ContentKind.Class ? index.ReadByClass.GetValueOrDefault(revision.ContentId)
            : revision.Kind == ContentKind.Subclass && reached.Count > 0 ? index.ReadBySubclass.GetValueOrDefault(revision.ContentId) ?? []
            : null;
        foreach (var scale in ActiveScales(revision).Where(s => readers is not null || revision.Kind == ContentKind.Class))
        {
            if (readers?.Contains(scale.ScaleId) != true)
                add("debug.scale-unused", FindingSeverity.Note, $"Scale '{scale.ScaleId}' is defined, but no formula of the class, its subclasses or their features reads SCALE.{scale.ScaleId}. It still shows as a column of the class table.", scale.Id);
        }

        // A subclass and the class it is offered in define the same id: the class's column wins (scale.duplicate).
        if (revision.Kind != ContentKind.Subclass)
            return;
        foreach (var classId in reached.Where(r => r.SubclassId == revision.ContentId).Select(r => r.ClassId).Distinct())
        {
            var classCurrent = graph.Current.GetValueOrDefault(classId);
            var classScales = index.Defined(classId);
            var older = graph.PublishedRevisions(classId).Where(r => r.RevisionId != classCurrent?.RevisionId).ToList();
            foreach (var scale in ActiveScales(revision))
            {
                if (classScales.Contains(scale.ScaleId))
                {
                    // Validation says so (as an error) when the subclass extends the class's choice; listed once.
                    if (!own.Any(f => f.Code == "validate.scale-duplicate" && f.EffectId == scale.Id))
                        add("debug.scale-collision", FindingSeverity.Warning, $"Scale id '{scale.ScaleId}' is also defined by '{classCurrent!.Name}', a class this subclass is offered in. A class and its subclasses share one set of scale ids, and the class's column wins.", scale.Id);
                }
                else if (older.FirstOrDefault(r => ActiveScales(r).Any(s => s.ScaleId == scale.ScaleId)) is { } clash)
                {
                    add("debug.scale-collision-older", FindingSeverity.Warning, $"Scale id '{scale.ScaleId}' is also defined by an older revision of '{clash.Name}'; characters still on that revision use the class's column.", scale.Id);
                }
            }
        }
    }

    /// <summary>A grant or option that names an older revision than the newest published one: characters get the older one.</summary>
    private static void Pins(ContentRevision revision, ContentGraph graph, AddFinding add)
    {
        foreach (var edge in graph.From(revision.ContentId).Where(e => e.Pinned is not null))
        {
            var published = graph.PublishedRevisions(edge.To);
            if (published.Count > 0 && published[^1].RevisionId != edge.Pinned!.RevisionId && published.Any(r => r.RevisionId == edge.Pinned.RevisionId))
                add("debug.reference-stale", FindingSeverity.Note, $"Effect '{edge.EffectId}' names an older revision of '{published[^1].Name}'; characters get that older revision. Pick the newest one and publish again to use it.", edge.EffectId);
        }
    }
}
