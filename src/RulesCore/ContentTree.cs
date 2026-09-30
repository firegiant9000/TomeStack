namespace TomeStack.RulesCore;

public enum TreeNodeKind { Content, Level, Choice, Resource, Roll, Recovery, Toggle, Scale, Missing }

/// <param name="Id">Unique in the tree: the positions from the root (<c>0.2.1</c>), never user text.</param>
/// <param name="Content">What the node shows: for a <see cref="TreeNodeKind.Content"/> node, that revision; otherwise the revision it belongs to.</param>
/// <param name="EffectId">The effect the node stands for, which belongs to <paramref name="Owner"/>.</param>
/// <param name="Owner">The revision that holds <paramref name="EffectId"/> (for a granted content, the one that grants it).</param>
/// <param name="Note">Why a branch stops or differs, such as "Not followed" or "a newer revision exists".</param>
public sealed record TreeNode(
    string Id, TreeNodeKind Kind, string Label, ContentReference? Content, string? EffectId, IReadOnlyList<TreeNode> Children,
    string? Note = null, ContentReference? Owner = null);

/// <param name="Truncated">The tree hit <see cref="ContentTree.MaxNodes"/> or <see cref="ContentTree.MaxDepth"/>, so some branches are cut.</param>
public sealed record ContentTreeView(TreeNode Root, bool Truncated);

/// <summary>
/// M5 slice 5 (B19, the relationship graph): one content's relationships as a tree, read-only. It shows, as the calculator
/// reads them:
/// <list type="bullet">
/// <item>a class's grants and choices grouped by the class level they apply from;</item>
/// <item>the exact revision each grant or declared option pins (with a note when a newer one exists), and the current
/// revision of content that extends a choice;</item>
/// <item>each content's resources (the first definition of an id; repeats are ignored), with the rolls and toggles that
/// spend them and the recoveries that restore them, then its other rolls and toggles and its class columns;</item>
/// <item>what never applies: a granted content's own grants, a grant that is not automatic or not always applied, a grant
/// that names nothing, and a recovery for a resource the content does not define.</item>
/// </list>
/// Content already on the path is shown once more, with no children, so cycles end. Bounded (SPEC Q-02): each
/// revision's lookups are built once; labels are cut to <see cref="MaxLabel"/> characters; <see cref="MaxDepth"/> levels;
/// and no new node once <see cref="MaxNodes"/> exist (the ancestors still open then are completed, so at most
/// <see cref="MaxNodes"/> + <see cref="MaxDepth"/>).
/// </summary>
public static class ContentTree
{
    public const int MaxNodes = 5_000;

    public const int MaxDepth = 12;

    public const int MaxLabel = 120;

    /// <summary>The calculator refuses a draft (<c>content.unpublished</c>), and publishing makes a new revision, so a pin on a draft stays a draft.</summary>
    public const string DraftPinNote = "Draft: characters get nothing from it until it is published; publishing creates a new revision, so re-point this grant";

    public const string DraftExtensionNote = "Draft: not offered to characters until published";

    public static ContentTreeView Build(ContentGraph graph, Guid contentId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var nodes = 0;
        var truncated = false;
        var parts = new Dictionary<ContentReference, Parts>();
        Parts PartsOf(ContentRevision revision)
        {
            if (!parts.TryGetValue(revision.Reference, out var p))
                parts[revision.Reference] = p = new Parts(revision);
            return p;
        }
        bool Room(int depth)
        {
            if (nodes < MaxNodes && depth <= MaxDepth)
                return true;
            truncated = true;
            return false;
        }
        TreeNode Node(string id, TreeNodeKind kind, string label, ContentReference? content, string? effectId, IReadOnlyList<TreeNode> children, string? note = null, ContentReference? owner = null)
        {
            nodes++;
            return new(id, kind, Cut(label), content, effectId, children, note is null ? null : Cut(note), owner);
        }

        // The revision a grant or declared option names: exactly that one, as the calculator admits it.
        (ContentRevision? Revision, string? Note) Pinned(ContentReference reference)
        {
            var current = graph.Current.GetValueOrDefault(reference.ContentId);
            if (current?.RevisionId == reference.RevisionId)
                return (current, current.Status == RevisionStatus.Published ? null : DraftPinNote);
            var pinned = graph.PublishedRevisions(reference.ContentId).FirstOrDefault(r => r.RevisionId == reference.RevisionId);
            return pinned is null ? (null, null) : (pinned, current is null ? null : "A newer revision exists; characters get this one");
        }

        // Content that extends a choice is its current revision, which is not offered while it is a draft.
        (ContentRevision? Revision, string? Note) Extending(Guid contentId)
        {
            var current = graph.Current.GetValueOrDefault(contentId);
            return (current, current is not null && current.Status != RevisionStatus.Published ? DraftExtensionNote : null);
        }

        // Each owner's extension edges by choice, built once, so a choice reads its own and does not scan every edge.
        var extensionLookups = new Dictionary<Guid, ILookup<string, GraphEdge>>();
        ILookup<string, GraphEdge> ExtensionsOf(Guid ownerId)
        {
            if (!extensionLookups.TryGetValue(ownerId, out var lookup))
                extensionLookups[ownerId] = lookup = graph.From(ownerId).Where(e => e.Kind == GraphEdgeKind.Extension && e.ChoiceId is not null)
                    .ToLookup(e => e.ChoiceId!, StringComparer.Ordinal);
            return lookup;
        }

        TreeNode ContentNode(string id, ContentRevision revision, int depth, bool asRoot, HashSet<Guid> path, string? note = null)
        {
            var label = $"{Named(revision.Name, "(no name)")} ({revision.Kind.ToString().ToLowerInvariant()}, {revision.Status.ToString().ToLowerInvariant()})";
            if (path.Contains(revision.ContentId))
                return Node(id, TreeNodeKind.Content, label, revision.Reference, null, [], "Already shown above");
            path.Add(revision.ContentId);
            try
            {
                var p = PartsOf(revision);
                var children = new List<TreeNode>();
                string Next() => $"{id}.{children.Count}";

                foreach (var (level, links) in p.ByLevel)
                {
                    if (!Room(depth + 1))
                        break;
                    var levelId = Next();
                    var levelChildren = new List<TreeNode>();
                    foreach (var link in links)
                    {
                        if (!Room(depth + 2))
                            break;
                        levelChildren.Add(Link($"{levelId}.{levelChildren.Count}", revision, link, depth + 2, asRoot, path));
                    }
                    var levelLabel = level is { } l
                        ? revision.Kind is ContentKind.Class or ContentKind.Subclass ? $"Class level {l}" : $"From level {l}"
                        : "Always";
                    children.Add(Node(levelId, TreeNodeKind.Level, levelLabel, revision.Reference, null, levelChildren));
                }

                foreach (var resource in p.Resources)
                {
                    if (!Room(depth + 1))
                        break;
                    var resourceId = Next();
                    var uses = new List<TreeNode>();
                    foreach (var roll in p.RollsOf(resource.ResourceId))
                    {
                        if (!Room(depth + 2))
                            break;
                        uses.Add(Node($"{resourceId}.{uses.Count}", TreeNodeKind.Roll, $"Roll: {Named(roll.Label, roll.Id)} ({Named(roll.Dice, "")}), spends it", revision.Reference, roll.Id, [], owner: revision.Reference));
                    }
                    foreach (var toggle in p.TogglesOf(resource.ResourceId))
                    {
                        if (!Room(depth + 2))
                            break;
                        uses.Add(Node($"{resourceId}.{uses.Count}", TreeNodeKind.Toggle, $"Toggle: {Named(toggle.Label, toggle.Id)}, turning it on spends one", revision.Reference, toggle.Id, [], owner: revision.Reference));
                    }
                    foreach (var recovery in p.RecoveriesOf(resource.ResourceId))
                    {
                        if (!Room(depth + 2))
                            break;
                        var when = recovery.On == RestPeriod.LongRest ? "long rest" : "short rest";
                        uses.Add(Node($"{resourceId}.{uses.Count}", TreeNodeKind.Recovery, $"Recovery on a {when}: {Named(recovery.Amount, "")}", revision.Reference, recovery.Id, [], owner: revision.Reference));
                    }
                    children.Add(Node(resourceId, TreeNodeKind.Resource, $"Resource: {Named(resource.Label, resource.ResourceId)} (uses {Named(resource.Maximum, "")})", revision.Reference, resource.Id, uses, owner: revision.Reference));
                }
                foreach (var duplicate in p.DuplicateResources)
                {
                    if (!Room(depth + 1))
                        break;
                    children.Add(Node(Next(), TreeNodeKind.Resource, $"Resource: {Named(duplicate.Label, duplicate.ResourceId)}", revision.Reference, duplicate.Id, [], "Ignored: an earlier resource has the same id", revision.Reference));
                }
                foreach (var roll in p.LooseRolls)
                {
                    if (!Room(depth + 1))
                        break;
                    var spends = roll.ResourceId is null ? "" : roll.ResourceContent is { } holder && holder != revision.ContentId
                        ? $", spends '{Named(roll.ResourceId, "")}' of {Named(graph.Current.GetValueOrDefault(holder)?.Name, "content that is not installed")}"
                        : $", spends '{Named(roll.ResourceId, "")}', which this content does not define";
                    children.Add(Node(Next(), TreeNodeKind.Roll, $"Roll: {Named(roll.Label, roll.Id)} ({Named(roll.Dice, "")}){spends}", revision.Reference, roll.Id, [], owner: revision.Reference));
                }
                foreach (var toggle in p.LooseToggles)
                {
                    if (!Room(depth + 1))
                        break;
                    var spends = toggle.ResourceId is null ? "" : $", spends '{Named(toggle.ResourceId, "")}', which this content does not define";
                    children.Add(Node(Next(), TreeNodeKind.Toggle, $"Toggle: {Named(toggle.Label, toggle.Id)}{spends}", revision.Reference, toggle.Id, [], owner: revision.Reference));
                }
                foreach (var recovery in p.OrphanRecoveries)
                {
                    if (!Room(depth + 1))
                        break;
                    children.Add(Node(Next(), TreeNodeKind.Recovery, $"Recovery of '{Named(recovery.ResourceId, "")}'", revision.Reference, recovery.Id, [], "Never applies: this content does not define that resource", revision.Reference));
                }
                foreach (var scale in p.Scales)
                {
                    if (!Room(depth + 1))
                        break;
                    children.Add(Node(Next(), TreeNodeKind.Scale, $"Class column: {Named(scale.Label, scale.ScaleId)} (SCALE.{Named(scale.ScaleId, "")}: {string.Join(", ", scale.Values.Take(Character.MaxLevel))})", revision.Reference, scale.Id, [], owner: revision.Reference));
                }
                return Node(id, TreeNodeKind.Content, label, revision.Reference, null, children, note);
            }
            finally
            {
                path.Remove(revision.ContentId);
            }
        }

        TreeNode Link(string id, ContentRevision owner, Effect link, int depth, bool asRoot, HashSet<Guid> path)
        {
            if (link is GrantEffect grant)
            {
                var named = $"Grant '{Named(grant.Id, "")}'";
                if (grant.Content is not { } target)
                    return Node(id, TreeNodeKind.Missing, named, owner.Reference, grant.Id, [], "Never applies: it names no content", owner.Reference);
                if (grant.Automation != AutomationStatus.Automatic || grant.Timing != EffectTiming.Always)
                    return Node(id, TreeNodeKind.Content, named, target, grant.Id, [], "Never applied: it is not automatic, or not always on", owner.Reference);
                var (granted, pinNote) = Pinned(target);
                if (granted is null)
                    return Node(id, TreeNodeKind.Missing, $"{named}: revision {target.RevisionId} is not installed or not published", target, grant.Id, [], owner: owner.Reference);
                if (!asRoot)
                    return Node(id, TreeNodeKind.Content, $"Grants {Named(granted.Name, "(no name)")}", granted.Reference, grant.Id, [], "Not followed: the content that grants it is itself granted, and grants are one level deep", owner.Reference);
                var node = ContentNode(id, granted, depth, asRoot: false, path, pinNote);
                return node with { Label = Cut($"Grants {node.Label}"), EffectId = grant.Id, Owner = owner.Reference };
            }
            var choice = (ChoiceEffect)link;
            var options = new List<TreeNode>();
            var declared = choice.Options.Distinct().Select(o => (Reference: (ContentReference?)o, ContentId: o.ContentId));
            var extensions = ExtensionsOf(owner.ContentId)[choice.ChoiceId].Select(e => (Reference: (ContentReference?)null, ContentId: e.To));
            foreach (var (reference, optionId) in declared.Concat(extensions))
            {
                if (!Room(depth + 1))
                    break;
                var optionNodeId = $"{id}.{options.Count}";
                var (option, pinNote) = reference is { } pinned ? Pinned(pinned) : Extending(optionId);
                options.Add(option is null
                    ? Node(optionNodeId, TreeNodeKind.Missing, $"An option ({optionId}) is not installed or not published", reference, null, [])
                    : ContentNode(optionNodeId, option, depth + 1, asRoot: true, path, pinNote));
            }
            return Node(id, TreeNodeKind.Choice, $"Choice: {Named(choice.Text, choice.ChoiceId)} (pick {choice.Count})", owner.Reference, choice.Id, options,
                options.Count == 0 ? "Nothing to pick yet" : null, owner.Reference);
        }

        var root = graph.Current.TryGetValue(contentId, out var rootRevision)
            ? ContentNode("0", rootRevision, 0, asRoot: true, [])
            : Node("0", TreeNodeKind.Missing, $"Content {contentId} is not installed", null, null, []);
        return new ContentTreeView(root, truncated);
    }

    /// <summary>One revision's effects, sorted once for the tree (so a revision reached along many paths costs its size once).</summary>
    private sealed class Parts
    {
        private readonly ILookup<string, RollEffect> _rolls;
        private readonly ILookup<string, ToggleEffect> _toggles;
        private readonly ILookup<string, RecoveryEffect> _recoveries;

        public Parts(ContentRevision revision)
        {
            var effects = revision.Effects;
            ByLevel = [.. effects.Where(e => e is GrantEffect { Grant: GrantKind.Content } or ChoiceEffect)
                .GroupBy(e => e switch { GrantEffect g => g.Level, ChoiceEffect c => c.Level, _ => null })
                .OrderBy(g => g.Key ?? 0)
                .Select(g => (g.Key, (IReadOnlyList<Effect>)[.. g]))];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var resources = new List<ResourceEffect>();
            var duplicates = new List<ResourceEffect>();
            foreach (var resource in effects.OfType<ResourceEffect>())
                (seen.Add(resource.ResourceId) ? resources : duplicates).Add(resource); // the calculator uses the first definition
            Resources = resources;
            DuplicateResources = duplicates;
            var own = effects.OfType<RollEffect>().Where(r => r.ResourceId is { } id && seen.Contains(id) && (r.ResourceContent is null || r.ResourceContent == revision.ContentId)).ToList();
            _rolls = own.ToLookup(r => r.ResourceId!, StringComparer.Ordinal);
            LooseRolls = [.. effects.OfType<RollEffect>().Except(own)];
            var spending = effects.OfType<ToggleEffect>().Where(t => t.ResourceId is { } id && seen.Contains(id)).ToList();
            _toggles = spending.ToLookup(t => t.ResourceId!, StringComparer.Ordinal);
            LooseToggles = [.. effects.OfType<ToggleEffect>().Except(spending)];
            _recoveries = effects.OfType<RecoveryEffect>().Where(r => seen.Contains(r.ResourceId)).ToLookup(r => r.ResourceId, StringComparer.Ordinal);
            OrphanRecoveries = [.. effects.OfType<RecoveryEffect>().Where(r => !seen.Contains(r.ResourceId))];
            Scales = [.. effects.OfType<ScaleEffect>()];
        }

        public IReadOnlyList<(int? Level, IReadOnlyList<Effect> Links)> ByLevel { get; }

        public IReadOnlyList<ResourceEffect> Resources { get; }

        public IReadOnlyList<ResourceEffect> DuplicateResources { get; }

        public IReadOnlyList<RollEffect> LooseRolls { get; }

        public IReadOnlyList<ToggleEffect> LooseToggles { get; }

        public IReadOnlyList<RecoveryEffect> OrphanRecoveries { get; }

        public IReadOnlyList<ScaleEffect> Scales { get; }

        public IEnumerable<RollEffect> RollsOf(string resourceId) => _rolls[resourceId];

        public IEnumerable<ToggleEffect> TogglesOf(string resourceId) => _toggles[resourceId];

        public IEnumerable<RecoveryEffect> RecoveriesOf(string resourceId) => _recoveries[resourceId];
    }

    private static string Named(string? text, string fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : text.Length > 80 ? text[..80].TrimEnd() + "…" : text;

    private static string Cut(string text) => text.Length > MaxLabel ? text[..MaxLabel].TrimEnd() + "…" : text;
}
