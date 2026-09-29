namespace TomeStack.RulesCore;

/// <summary>How one content brings in another (<see cref="ContentGraph"/>).</summary>
public enum GraphEdgeKind
{
    /// <summary>A <c>grant</c> of content. Followed only from a root, one level deep, as the calculator does.</summary>
    Grant,

    /// <summary>An option a <c>choice</c> lists. Chosen content is a root.</summary>
    Option,

    /// <summary>A revision that adds itself to a choice (content v4 <c>extendsChoice</c>). Chosen content is a root.</summary>
    Extension,
}

/// <param name="EffectId">The grant or choice effect of <paramref name="From"/>; empty when the extended choice does not exist.</param>
/// <param name="Pinned">The exact revision a grant or option names; null for an extension, which names no revision.</param>
public sealed record GraphEdge(GraphEdgeKind Kind, Guid From, Guid To, string EffectId, string? ChoiceId, int? Level, ContentReference? Pinned);

/// <summary>
/// One way a class reaches a content: through which subclass, if any, and whether the content arrives as a root (a class
/// or chosen content, whose grants are followed) or as granted content (whose own grants are not).
/// </summary>
public sealed record GraphReach(Guid ContentId, Guid ClassId, Guid? SubclassId, bool AsRoot);

/// <summary>
/// M5 slice 2 (B02): the relationships between content revisions, read-only. It holds the <em>current</em> revision of
/// each content (the author's revision under study, else the newest published one), the grant, option and extension
/// edges between them, and every way each class reaches each content. The reach follows the calculator's rules
/// (<c>CharacterCalculator</c> content resolution): grants only from a root and one level deep, and choices from anything
/// reached. Level gates are not applied: the graph asks what a class can ever reach, not what one character has.
/// It is pure and never writes; the debugger (<see cref="ContentDebugger"/>) and the relationship view read it.
/// </summary>
public sealed class ContentGraph
{
    /// <summary>Bounds on the reach walk, so hostile content cannot make it unbounded (SPEC Q-02): states visited, and edges examined.</summary>
    public const int MaxReachStates = 200_000;

    /// <inheritdoc cref="MaxReachStates"/>
    public const int MaxEdgeSteps = 2_000_000;

    private readonly Dictionary<Guid, List<GraphEdge>> _from;
    private readonly Dictionary<Guid, List<GraphEdge>> _to;
    private readonly Dictionary<Guid, List<GraphReach>> _reaches;
    private readonly Dictionary<Guid, List<ContentRevision>> _published;

    private ContentGraph(
        Dictionary<Guid, ContentRevision> current, List<GraphEdge> edges, Dictionary<Guid, List<GraphReach>> reaches,
        Dictionary<Guid, List<ContentRevision>> published, bool truncated)
    {
        Current = current;
        Edges = edges;
        _from = edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.ToList());
        _to = edges.GroupBy(e => e.To).ToDictionary(g => g.Key, g => g.ToList());
        _reaches = reaches;
        _published = published;
        Truncated = truncated;
    }

    /// <summary>The current revision of each content, by content id.</summary>
    public IReadOnlyDictionary<Guid, ContentRevision> Current { get; }

    public IReadOnlyList<GraphEdge> Edges { get; }

    /// <summary>The reach walk stopped at <see cref="MaxReachStates"/>; reach-based findings may be incomplete.</summary>
    public bool Truncated { get; }

    public IReadOnlyList<GraphEdge> From(Guid contentId) => _from.GetValueOrDefault(contentId) ?? [];

    public IReadOnlyList<GraphEdge> To(Guid contentId) => _to.GetValueOrDefault(contentId) ?? [];

    /// <summary>Every way a class reaches the content (a class reaches itself). Empty: no class ever has it.</summary>
    public IReadOnlyList<GraphReach> Reaches(Guid contentId) => _reaches.GetValueOrDefault(contentId) ?? [];

    /// <summary>The published revisions of a content, oldest first.</summary>
    public IReadOnlyList<ContentRevision> PublishedRevisions(Guid contentId) => _published.GetValueOrDefault(contentId) ?? [];

    /// <param name="revisionsInOrder">Every stored revision, in the order it was added (the newest published is the last).</param>
    /// <param name="scope">
    /// Revisions under study, such as a source's latest drafts or one unsaved draft. Each is the current revision of its
    /// content, whatever its status, so the author sees the graph they are building. Their classes are walked first, so
    /// a walk that hits its bounds still covers them.
    /// </param>
    /// <param name="maxStates">The state bound (tests lower it).</param>
    /// <param name="maxEdgeSteps">The work bound: edges examined in the whole walk (tests lower it).</param>
    public static ContentGraph Build(
        IEnumerable<ContentRevision> revisionsInOrder, IEnumerable<ContentRevision>? scope = null, int maxStates = MaxReachStates, int maxEdgeSteps = MaxEdgeSteps)
    {
        ArgumentNullException.ThrowIfNull(revisionsInOrder);
        var published = new Dictionary<Guid, List<ContentRevision>>();
        foreach (var revision in revisionsInOrder.Where(r => r.Status == RevisionStatus.Published))
        {
            if (!published.TryGetValue(revision.ContentId, out var list))
                published[revision.ContentId] = list = [];
            list.Add(revision);
        }
        var current = published.ToDictionary(p => p.Key, p => p.Value[^1]);
        var scoped = new HashSet<Guid>();
        foreach (var revision in scope ?? [])
        {
            current[revision.ContentId] = revision;
            scoped.Add(revision.ContentId);
        }

        var edges = new List<GraphEdge>();
        foreach (var revision in current.Values)
        {
            foreach (var effect in revision.Effects)
            {
                switch (effect)
                {
                    // Only what the calculator follows: an automatic grant that always applies (its level gate aside).
                    case GrantEffect { Grant: GrantKind.Content, Content: { } granted, Automation: AutomationStatus.Automatic, Timing: EffectTiming.Always } grant:
                        edges.Add(new(GraphEdgeKind.Grant, revision.ContentId, granted.ContentId, grant.Id, null, grant.Level, granted));
                        break;
                    case ChoiceEffect choice:
                        foreach (var option in choice.Options.Distinct())
                            edges.Add(new(GraphEdgeKind.Option, revision.ContentId, option.ContentId, choice.Id, choice.ChoiceId, choice.Level, option));
                        break;
                }
            }
            // The calculator offers an extension only while it offers that choice, so the current revision of the extended
            // content must still have it.
            if (revision.ExtendsChoice is { } extends && extends.ContentId != revision.ContentId
                && current.GetValueOrDefault(extends.ContentId)?.Effects.OfType<ChoiceEffect>().FirstOrDefault(c => c.ChoiceId == extends.ChoiceId) is { } offered)
            {
                edges.Add(new(GraphEdgeKind.Extension, extends.ContentId, revision.ContentId, offered.Id, extends.ChoiceId, offered.Level, null));
            }
        }

        var from = edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.ToList());
        var reaches = new Dictionary<Guid, List<GraphReach>>();
        var visited = new HashSet<GraphReach>();
        var steps = 0;
        var truncated = false;
        var classes = current.Values.Where(r => r.Kind == ContentKind.Class).OrderBy(r => scoped.Contains(r.ContentId) ? 0 : 1);
        foreach (var classRevision in classes)
        {
            if (truncated)
                break;
            var pending = new Queue<GraphReach>();
            void Visit(GraphReach reach)
            {
                if (visited.Count >= maxStates)
                {
                    truncated = true;
                    return;
                }
                if (!visited.Add(reach))
                    return;
                if (!reaches.TryGetValue(reach.ContentId, out var list))
                    reaches[reach.ContentId] = list = [];
                list.Add(reach);
                pending.Enqueue(reach);
            }
            Visit(new(classRevision.ContentId, classRevision.ContentId, null, AsRoot: true));
            while (pending.Count > 0 && !truncated)
            {
                var at = pending.Dequeue();
                foreach (var edge in from.GetValueOrDefault(at.ContentId) ?? [])
                {
                    if (++steps > maxEdgeSteps)
                    {
                        truncated = true;
                        break;
                    }
                    // A granted content's own grants are not followed (grants are one level deep).
                    if (edge.Kind == GraphEdgeKind.Grant && !at.AsRoot)
                        continue;
                    // Another class brought in as content is a class of its own, not part of this one.
                    if (current.GetValueOrDefault(edge.To) is not { Kind: not ContentKind.Class } target)
                        continue;
                    var subclass = target.Kind == ContentKind.Subclass ? target.ContentId : at.SubclassId;
                    Visit(new(edge.To, at.ClassId, subclass, AsRoot: edge.Kind != GraphEdgeKind.Grant));
                    if (truncated)
                        break;
                }
            }
        }
        return new ContentGraph(current, edges, reaches, published, truncated);
    }

    /// <summary>Every formula a revision holds, with its effect id: modifier values, resource maximums, recovery amounts, roll costs and bonuses, and spellsFormula.</summary>
    public static IEnumerable<(string EffectId, string Source)> Formulas(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        foreach (var effect in revision.Effects)
        {
            switch (effect)
            {
                case ModifierEffect modifier:
                    yield return (modifier.Id, modifier.Value);
                    break;
                case ResourceEffect resource:
                    yield return (resource.Id, resource.Maximum);
                    break;
                case RecoveryEffect recovery when !string.Equals(recovery.Amount.Trim(), "all", StringComparison.OrdinalIgnoreCase):
                    yield return (recovery.Id, recovery.Amount);
                    break;
                case RollEffect roll:
                    if (roll.Cost is { } cost)
                        yield return (roll.Id, cost);
                    if (roll.Bonus is { } bonus)
                        yield return (roll.Id, bonus);
                    break;
                case SpellcastingEffect { SpellsFormula: { } spells } spellcasting:
                    yield return (spellcasting.Id, spells);
                    break;
            }
        }
    }

    /// <summary>The identifiers each formula of a revision reads, by effect id. Formulas that do not parse read nothing (validation reports them).</summary>
    public static IEnumerable<(string EffectId, string Identifier)> Identifiers(ContentRevision revision) =>
        Formulas(revision).SelectMany(f =>
            Formula.TryParse(f.Source, allowScales: true, out var formula, out _) ? formula!.Identifiers.Select(i => (f.EffectId, i)) : []);
}
