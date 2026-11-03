namespace TomeStack.RulesCore;

public static partial class CharacterCalculator
{
    // ---- graph ----------------------------------------------------------------------------------------------

    private static IEnumerable<(string From, string To, Modifier? Via)> Edges(IEnumerable<Modifier> modifiers)
    {
        foreach (var spec in Specs)
        {
            foreach (var dependency in spec.Reads)
                yield return (dependency, spec.Id, null);
        }
        foreach (var modifier in modifiers)
        {
            foreach (var read in modifier.ReadsFields)
                yield return (read, modifier.Effect.Target, modifier);
        }
    }

    /// <summary>
    /// Tarjan SCC. Base edges are acyclic by construction, so every cycle runs through at least one effect edge. Disable
    /// every effect whose edge lies inside a strongly connected component (or reads its own target), except an edge
    /// that base edges already imply (for example a Dex modifier effect reading the Dex score): it adds no
    /// reachability, so it cannot close a cycle, and the graph stays acyclic without it.
    /// </summary>
    private static void RemoveCycles(List<Modifier> modifiers, List<Diagnostic> diagnostics, Dictionary<string, List<Diagnostic>> warnings, HashSet<string> manual)
    {
        var edges = Edges(modifiers).ToList();
        var adjacency = Specs.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to, _) in edges)
            adjacency[from].Add(to);

        var component = StronglyConnectedComponents(adjacency);
        var cyclic = edges
            .Where(e => e.Via is not null && (e.From == e.To || (component[e.From] == component[e.To] && ComponentSize(component, component[e.From]) > 1 && !BaseReaches(e.From, e.To))))
            .Select(e => e.Via!)
            .Distinct()
            .ToList();

        foreach (var modifier in cyclic)
        {
            var members = component.Where(c => c.Value == component[modifier.Effect.Target]).Select(c => c.Key).OrderBy(f => SpecIndex[f]).ToList();
            var cycle = members.Count > 1 ? string.Join(" → ", [.. members, members[0]]) : $"{modifier.Effect.Target} → {modifier.Effect.Target}";
            var revision = modifier.Content.Revision;
            var diagnostic = new Diagnostic(
                "effect.dependency-cycle",
                $"'{revision.Name}' effect '{modifier.Effect.Id}' is disabled: its formula '{modifier.Formula.Source}' reads {string.Join(", ", modifier.ReadsFields)}, which depends on {modifier.Effect.Target} (cycle: {cycle}).",
                revision.Reference,
                modifier.Effect.Id);
            diagnostics.Add(diagnostic);
            warnings[modifier.Effect.Target].Add(diagnostic);
            manual.Add(modifier.Effect.Target);
            modifiers.Remove(modifier);
        }
    }

    private static int ComponentSize(Dictionary<string, int> component, int id) => component.Count(c => c.Value == id);

    /// <summary>True if <paramref name="to"/> depends on <paramref name="from"/> through base inputs alone (a path of length ≥ 1).</summary>
    private static bool BaseReaches(string from, string to)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([from]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var dependent in Specs.Where(s => s.Reads.Contains(current, StringComparer.Ordinal)).Select(s => s.Id))
            {
                if (dependent == to)
                    return true;
                if (seen.Add(dependent))
                    pending.Push(dependent);
            }
        }
        return false;
    }

    private static Dictionary<string, int> StronglyConnectedComponents(Dictionary<string, List<string>> adjacency)
    {
        var index = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var component = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 0;

        // Recursion depth is bounded by the fixed field count (not by user content).
        void Visit(string node)
        {
            indices[node] = lowLinks[node] = index++;
            stack.Push(node);
            onStack.Add(node);
            foreach (var target in adjacency[node])
            {
                if (!indices.ContainsKey(target))
                {
                    Visit(target);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[target]);
                }
                else if (onStack.Contains(target))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], indices[target]);
                }
            }
            if (lowLinks[node] != indices[node])
                return;
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component[member] = next;
            }
            while (member != node);
            next++;
        }

        foreach (var node in adjacency.Keys)
        {
            if (!indices.ContainsKey(node))
                Visit(node);
        }
        return component;
    }

    /// <summary>Kahn's algorithm; ties break by declaration order, so the order is deterministic.</summary>
    private static List<string> TopologicalOrder(IEnumerable<Modifier> modifiers)
    {
        var incoming = Specs.ToDictionary(s => s.Id, _ => 0, StringComparer.Ordinal);
        var outgoing = Specs.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to, _) in Edges(modifiers))
        {
            outgoing[from].Add(to);
            incoming[to]++;
        }
        var ready = new SortedSet<int>(Specs.Where(s => incoming[s.Id] == 0).Select(s => SpecIndex[s.Id]));
        var order = new List<string>();
        while (ready.Count > 0)
        {
            var id = Specs[ready.Min].Id;
            ready.Remove(ready.Min);
            order.Add(id);
            foreach (var target in outgoing[id])
            {
                if (--incoming[target] == 0)
                    ready.Add(SpecIndex[target]);
            }
        }
        return order.Count == Specs.Count
            ? order
            : throw new InvalidOperationException("Dependency cycle survived cycle removal."); // unreachable by construction
    }
}
