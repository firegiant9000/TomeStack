using TomeStack.AppService;
using TomeStack.RulesCore;

namespace TomeStack.Benchmarks;

/// <summary>The bundled SRD packs, loaded once, as an in-memory catalog (no database), and lookups by name and kind.</summary>
internal static class Srd
{
    private static readonly Lazy<ContentPack[]> LazyPacks = new(() => [.. TomeStackApp.BundledPacks.Select(TomeStackApp.LoadBundledPack)]);

    public static IReadOnlyList<ContentPack> Packs => LazyPacks.Value;

    public static IEnumerable<ContentRevision> Revisions => Packs.SelectMany(p => p.Revisions);

    /// <summary>Each family's packs share one source record, so sources are taken once.</summary>
    public static InMemoryContentCatalog Catalog() => new([.. Packs.SelectMany(p => p.Sources).DistinctBy(s => s.Id)], Revisions);

    /// <summary>The newest published revision of <paramref name="kind"/> named <paramref name="name"/> in <paramref name="family"/>.</summary>
    public static ContentRevision Find(string family, ContentKind kind, string name) =>
        Revisions.Last(r => r.Status == RevisionStatus.Published && r.Kind == kind && r.Name == name && r.RulesFamilies.Contains(family));

    /// <summary>Every published spell of <paramref name="family"/> on <paramref name="list"/>.</summary>
    public static IEnumerable<ContentRevision> SpellsOn(string family, string list) =>
        Revisions.Where(r => r.Status == RevisionStatus.Published && r.Kind == ContentKind.Spell && r.RulesFamilies.Contains(family)
            && r.Effects.OfType<SpellEffect>().Any(s => s.Lists.Contains(list)));

    public static IEnumerable<ContentRevision> Items<T>(string family) where T : Effect =>
        Revisions.Where(r => r.Status == RevisionStatus.Published && r.Kind == ContentKind.Item && r.RulesFamilies.Contains(family) && r.Effects.OfType<T>().Any());
}
