using System.Text.Json.Nodes;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// T3 golden revisions (tests/RulesFixtures/golden/revisions.json): one original revision per content schema version, 1 to
/// 9, and the hash of each as the store writes it. Shared with AppService.Tests, which checks the store's own hash path.
/// </summary>
internal static class GoldenRevisions
{
    /// <summary>
    /// A change here means every stored revision of that version would get a new hash: never update these to make a test
    /// pass; find what changed the serializer output.
    /// </summary>
    public static readonly string[] Hashes =
    [
        "3ac8c72026f7a90a8394aa300b156a67619e582dfd185ce0253da4e231c2a874", // v1, as upcast to v2 on read
        "527a981f073ee832892c3a0cf78a38a019de349b875f327983b0affeb9ae66b4",
        "ad41846f41e808c2d5c64c999bb79532b94e5962f2d8fe88812146221519c211",
        "2c58028031a77815e138b41e90a6ef273afa42c0c9d85352fd54049ac06b9a52",
        "6145fe2490041ed2c50677f9fbe2090c2a68bc00571c4fda1168b57d617129d0",
        "9eb8d40c6e2be19d480cda502d0035b1c724ac1ea98ee14f1e76bfbd5a8dffb5",
        "e334f59cdb39fbdc9c5ba9df6a9cd3968cfac473f534aa2adbe3969a4cc0e7a2",
        "2e1e8b8e9d4a812dc168ca37a1f18911f3888f42c3fddc42f10d3715f54d47e2",
        "cbeac04ebf9be1df0e1f40ce15b4388ff12fecdf7c5d03a7c6a05718a6b3ff9f",
    ];

    /// <summary>The golden revision of content schema <paramref name="version"/>, as written in the fixture.</summary>
    public static string Json(int version) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "golden", "revisions.json")))!.AsArray()[version - 1]!.ToJsonString();
}
