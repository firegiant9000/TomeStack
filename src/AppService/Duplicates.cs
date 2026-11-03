using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Exports;
using TomeStack.AppService.Persistence;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Detection;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

public sealed partial class TomeStackApp
{
    /// <summary><c>content.duplicates</c> (Import Lab L6): content or candidates that look like each other, listed and never merged. Writes nothing.</summary>
    public IReadOnlyList<DuplicateGroup> ContentDuplicates(DuplicatesRequest request) => Duplicates.Find(_store, request);
}

/// <summary>How two entries of one kind and one rules family look alike.</summary>
public enum DuplicateClass
{
    /// <summary>The same name and the same rules.</summary>
    Exact,

    /// <summary>The same name, different rules. Both stay as they are (AT-Q17).</summary>
    SameNameDifferentRules,

    /// <summary>The same rules under a different name. A possibility only.</summary>
    SameRulesDifferentName,
}

/// <param name="SourceId">Null means the whole library; with <paramref name="JobId"/> it limits the library side to that source.</param>
/// <param name="JobId">Null means library against library; a job means its pending candidates against the library.</param>
public sealed record DuplicatesRequest(Guid? SourceId = null, Guid? JobId = null);

/// <summary>One side of a group: a published library entry (<see cref="ContentId"/>, <see cref="RevisionId"/>) or a pending candidate (<see cref="CandidateId"/>).</summary>
public sealed record DuplicateMember(Guid? CandidateId, Guid? ContentId, Guid? RevisionId, ContentKind Kind, string Name, Guid SourceId, string SourceName);

/// <summary>Two or more entries of one kind that look alike inside one rules family. The families are never compared with each other.</summary>
public sealed record DuplicateGroup(DuplicateClass Class, string Family, IReadOnlyList<DuplicateMember> Members);

/// <summary>
/// Import Lab L6 (design 9.2): the rules of a content entry, as a hash that ignores what does not change the rules. The effect ids
/// are removed and the keys are sorted, so two entries written in another order, or with other ids, give the same value.
/// Computed on demand and never stored.
/// </summary>
public static class Fingerprint
{
    public static string Of(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return Of(revision.Effects);
    }

    public static string Of(IReadOnlyList<Effect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return OfJson(JsonSerializer.SerializeToNode<IReadOnlyList<Effect>>(effects, RulesJson.Compact)!.AsArray());
    }

    /// <summary>The hash of effects already written as JSON: each effect's top-level <c>id</c> removed, then <see cref="Canonical"/>. Leaves <paramref name="effects"/> unchanged.</summary>
    internal static string OfJson(JsonArray effects)
    {
        var copy = (JsonArray)effects.DeepClone();
        foreach (var effect in copy.OfType<JsonObject>())
            effect.Remove("id");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(copy))));
    }

    /// <summary>The JSON text with the keys of every object, at every level, in ordinal order (arrays keep their order).</summary>
    internal static string Canonical(JsonNode node) => FoundryDnd5e.Sorted(node)!.ToJsonString();
}

/// <summary>
/// <c>content.duplicates</c> (Import Lab L6, design 9.2). Entries are compared inside one kind and one rules family, by name
/// (<see cref="CandidateDetector.Key"/>) and by <see cref="Fingerprint"/>. Only published library content counts (the latest published
/// revision of each content); a draft is not a library entry. Read-only: nothing is merged, changed or stored.
/// </summary>
public static class Duplicates
{
    private sealed record Entry(DuplicateMember Member, string Family, string NameKey, string Fingerprint, bool HasEffects);

    /// <summary>One whole-library read (<c>ListRevisionsInOrder()</c>), grouped in memory.</summary>
    public static IReadOnlyList<DuplicateGroup> Find(SqliteStore store, DuplicatesRequest request)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceId is { } wanted && store.FindSource(wanted) is null)
            throw new AppValidationException([new("source.not-found", $"Source {wanted} is not installed.")]);
        if (request.JobId is { } job && store.FindImportJob(job) is null)
            throw new AppValidationException([new("import.not-found", $"Import job {job} does not exist.")]);

        var sourceNames = store.ListSources().ToDictionary(s => s.Id, s => s.Title);
        string SourceName(Guid id) => sourceNames.GetValueOrDefault(id) ?? id.ToString("D");

        var (published, _) = Coverage.Choose(store.ListRevisionsInOrder(), includeDrafts: false);
        var library = new List<Entry>();
        foreach (var revision in published.Values)
        {
            // With a job and a source, the library side is that source; with a source alone, a group is kept below when it touches it.
            if (request.JobId is not null && request.SourceId is { } only && revision.Provenance.SourceId != only)
                continue;
            var member = new DuplicateMember(null, revision.ContentId, revision.RevisionId, revision.Kind, revision.Name, revision.Provenance.SourceId, SourceName(revision.Provenance.SourceId));
            library.AddRange(EntriesOf(member, revision.RulesFamilies, revision.Effects));
        }

        var entries = library;
        if (request.JobId is { } jobId)
        {
            foreach (var stored in store.QueryImportCandidates(jobId, new ImportCandidateFilter(Status: CandidateStatus.Pending), 0, null).Items)
            {
                var current = CandidateQuarantine.WithTypedEffects(stored.Current);
                var member = new DuplicateMember(stored.Id, null, null, current.ProposedKind, current.ProposedName, current.SourceId, SourceName(current.SourceId));
                entries.AddRange(EntriesOf(member, current.RulesFamilies, current.ProposedEffects));
            }
        }

        var groups = new List<(DuplicateGroup Group, string NameKey, string Fingerprint)>();
        foreach (var bucket in entries.GroupBy(e => (e.Family, e.Member.Kind)))
        {
            var items = bucket.ToList();
            foreach (var same in items.GroupBy(e => (e.NameKey, e.Fingerprint)).Where(g => g.Count() > 1))
                groups.Add((Make(DuplicateClass.Exact, bucket.Key.Family, same), same.Key.NameKey, same.Key.Fingerprint));
            foreach (var named in items.GroupBy(e => e.NameKey).Where(g => g.Select(e => e.Fingerprint).Distinct().Count() > 1))
                groups.Add((Make(DuplicateClass.SameNameDifferentRules, bucket.Key.Family, named), named.Key, ""));
            // An entry with no effects has no rules to share: text-only entries are not "the same rules".
            foreach (var rules in items.Where(e => e.HasEffects).GroupBy(e => e.Fingerprint).Where(g => g.Select(e => e.NameKey).Distinct().Count() > 1))
                groups.Add((Make(DuplicateClass.SameRulesDifferentName, bucket.Key.Family, rules), "", rules.Key));
        }

        bool Wanted(DuplicateGroup g) =>
            request.JobId is not null ? g.Members.Any(m => m.CandidateId is not null)
            : request.SourceId is { } source ? g.Members.Any(m => m.SourceId == source)
            : true;

        return [.. groups.Where(g => Wanted(g.Group))
            .OrderBy(g => g.Group.Class).ThenBy(g => g.Group.Family, StringComparer.Ordinal).ThenBy(g => g.Group.Members[0].Kind)
            .ThenBy(g => g.NameKey, StringComparer.Ordinal).ThenBy(g => g.Fingerprint, StringComparer.Ordinal)
            .ThenBy(g => g.Group.Members[0].ContentId ?? g.Group.Members[0].CandidateId)
            .Select(g => g.Group)];
    }

    /// <summary>One entry per rules family: a revision written for both families is compared inside each, never across.</summary>
    private static IEnumerable<Entry> EntriesOf(DuplicateMember member, IReadOnlyList<string> families, IReadOnlyList<Effect> effects)
    {
        // An entry with no name has nothing to compare by (and an empty key would make every unnamed entry one group).
        if (member.Name is null || CandidateDetector.Key(member.Name).Length == 0)
            return [];
        var key = CandidateDetector.Key(member.Name);
        var fingerprint = Fingerprint.Of(effects);
        return families.Distinct().Select(family => new Entry(member, family, key, fingerprint, effects.Count > 0));
    }

    private static DuplicateGroup Make(DuplicateClass duplicateClass, string family, IEnumerable<Entry> members) =>
        new(duplicateClass, family, [.. members.Select(e => e.Member).OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.SourceName, StringComparer.Ordinal)
            .ThenBy(m => m.ContentId).ThenBy(m => m.CandidateId)]);
}
