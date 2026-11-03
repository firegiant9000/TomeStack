using TomeStack.ImportWorker.Detection;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// Import Lab (L3): the names of the published content of some rules families, compared the way detection compares them
/// (<see cref="CandidateDetector.Key"/>). One set is built per request, so a candidate edit with many unresolved references reads
/// the library once instead of once per reference.
/// </summary>
public sealed class InstalledNames
{
    private readonly HashSet<string> _keys;

    private InstalledNames(HashSet<string> keys) => _keys = keys;

    /// <summary>A name is installed when a published revision of one of <paramref name="families"/> has it.</summary>
    public static InstalledNames Build(IEnumerable<ContentRevision> library, IReadOnlyList<string> families)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(families);
        return new(library
            .Where(r => r.Status == RevisionStatus.Published && r.RulesFamilies.Any(families.Contains))
            .Select(r => CandidateDetector.Key(r.Name))
            .ToHashSet(StringComparer.Ordinal));
    }

    public bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _keys.Contains(CandidateDetector.Key(name));
    }
}
