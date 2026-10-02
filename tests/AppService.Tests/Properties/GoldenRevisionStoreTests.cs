using System.Text.Json;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;
using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.AppService.Tests.Properties;

/// <summary>
/// T3 property 1, the store's side: the golden revision of each content schema version, stored by <see cref="SqliteStore"/>,
/// gets exactly the golden hash, and reading it back and storing it again is "unchanged", not an immutability conflict.
/// </summary>
public class GoldenRevisionStoreTests
{
    public static TheoryData<int> Versions => [.. Enumerable.Range(1, ContentRevision.CurrentSchemaVersion)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void The_store_hashes_each_golden_revision_to_its_golden_hash(int version)
    {
        using var temp = new TempApp();
        var revision = JsonSerializer.Deserialize<ContentRevision>(GoldenRevisions.Json(version), RulesJson.Compact)!;
        var store = temp.App.Store;

        Assert.True(store.AddRevision(revision));
        Assert.Equal(GoldenRevisions.Hashes[version - 1], store.RevisionHash(revision.RevisionId));
        Assert.False(store.AddRevision(store.FindRevision(revision.Reference)!)); // the same bytes again: unchanged
    }
}
