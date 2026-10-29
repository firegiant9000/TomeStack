using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Published revisions are insert-only (CONTRIBUTING.md; gotcha 2026-09-28: bundled packs are seeded on every start, so a
/// changed revision would fail as immutable). New bundled content adds revisions; it never changes or drops one that
/// shipped. The baseline lists every revision bundled on main at 66d5515 with the SHA-256 the store records for it.
/// </summary>
public class BundledContentInsertOnlyTests
{
    private sealed record Baseline(string Commit, IReadOnlyList<string> Packs, IReadOnlyList<BaselineRevision> Revisions);

    private sealed record BaselineRevision(Guid RevisionId, string Sha256);

    private static readonly Baseline Main = TempApp.LoadFixture<Baseline>("golden/revisions-baseline-66d5515.json");

    [Fact]
    public void Every_revision_bundled_at_66d5515_is_still_seeded_with_the_same_hash()
    {
        using var temp = new TempApp();

        Assert.Equal(1320, Main.Revisions.Count);
        Assert.All(Main.Revisions, r => Assert.Equal(r.Sha256, temp.App.Store.RevisionHash(r.RevisionId)));
    }

    [Fact]
    public void Every_pack_bundled_at_66d5515_is_still_bundled()
    {
        Assert.All(Main.Packs, p => Assert.Contains($"TomeStack.Content.{p}", TomeStackApp.BundledPacks));
    }

    [Fact]
    public void Bundled_ids_are_unique_and_a_content_id_never_spans_two_families()
    {
        var revisions = TomeStackApp.BundledPacks.SelectMany(p => TomeStackApp.LoadBundledPack(p).Revisions).ToList();

        Assert.Equal(revisions.Count, revisions.Select(r => r.RevisionId).Distinct().Count());
        Assert.All(revisions.GroupBy(r => r.ContentId), g => Assert.Single(g.Select(r => string.Join(",", r.RulesFamilies)).Distinct()));
    }
}
