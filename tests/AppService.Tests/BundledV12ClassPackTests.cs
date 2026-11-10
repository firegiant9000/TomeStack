using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// C2-6: the v12 re-authoring packs (<c>srd-&lt;family&gt;-&lt;class&gt;-v12.json</c>) only add revisions. Each is a new published
/// revision of content an earlier bundled pack already holds, and what a new class or subclass revision grants exists.
/// </summary>
public class BundledV12ClassPackTests
{
    [Theory]
    [InlineData("TomeStack.Content.srd-5.1-barbarian-v12.json")]
    [InlineData("TomeStack.Content.srd-5.2.1-barbarian-v12.json")]
    public void Every_revision_is_new_and_v12(string resource)
    {
        Assert.Contains(resource, TomeStackApp.BundledPacks);
        var index = TomeStackApp.BundledPacks.ToList().IndexOf(resource);
        var pack = TomeStackApp.LoadBundledPack(resource);
        var earlier = TomeStackApp.BundledPacks.Take(index).SelectMany(p => TomeStackApp.LoadBundledPack(p).Revisions).ToList();
        var others = TomeStackApp.BundledPacks.Where(p => p != resource).SelectMany(p => TomeStackApp.LoadBundledPack(p).Revisions).ToList();
        var allRevisionIds = TomeStackApp.BundledPacks.SelectMany(p => TomeStackApp.LoadBundledPack(p).Revisions).Select(r => r.RevisionId).ToHashSet();

        Assert.NotEmpty(pack.Revisions);
        Assert.Equal(pack.Revisions.Count, pack.Revisions.Select(r => r.RevisionId).Distinct().Count());
        Assert.All(pack.Revisions, r =>
        {
            Assert.Equal(12, r.SchemaVersion);
            Assert.Equal(RevisionStatus.Published, r.Status);
            Assert.DoesNotContain(others, o => o.RevisionId == r.RevisionId);
            // The new id rule: the old id's prefix through "8000-", then 000012, then the content id's last six digits.
            Assert.Equal($"000012{r.ContentId.ToString("D")[^6..]}", r.RevisionId.ToString("D")[^12..]);
            // A new revision of content an earlier pack of the same family already holds.
            Assert.Contains(earlier, e => e.ContentId == r.ContentId && e.RulesFamilies.SequenceEqual(r.RulesFamilies));
            foreach (var reference in Granted(r))
                Assert.Contains(reference.RevisionId, allRevisionIds);
        });
    }

    private static IEnumerable<ContentReference> Granted(ContentRevision revision)
    {
        if (revision.Kind is not (ContentKind.Class or ContentKind.Subclass))
            yield break;
        foreach (var effect in revision.Effects)
        {
            if (effect is GrantEffect { Content: { } content })
                yield return content;
            if (effect is ChoiceEffect choice)
                foreach (var option in choice.Options)
                    yield return option;
        }
    }
}
