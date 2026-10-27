using System.Text.Json.Nodes;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// T3 golden revisions (tests/RulesFixtures/golden/revisions.json): one original revision per content schema version, 1 to
/// 12, and the hash of each as the store writes it. Shared with AppService.Tests, which checks the store's own hash path.
/// </summary>
internal static class GoldenRevisions
{
    /// <summary>
    /// A change here means every stored revision of that version would get a new hash: never update these to make a test
    /// pass; find what changed the serializer output.
    /// </summary>
    public static readonly string[] Hashes =
    [
        "85800203fb31987f44f2d99f3a8f43c54bd4b4817e0e0d560695dca98debb897", // v1, as upcast to v2 on read
        "b202a68281be4291c6966e21c9faffb6406aaed1018704bfe9e2ccc88f549352",
        "f3e2aa4e0650cf957c441edf7c902fe5985665f1c530582fd8c3aff4d4edcc84",
        "646186370a605b8b4c533abfe2b21a7c8a1ea600a0dfbf3b8074040e51d5402e",
        "a3ccf4e08cd5c71c3a6fd2e491d6ffbe072982d1a8420d212dcf77dfd8b52e4b",
        "054178f31cbe4519f7d05e04769612b32844f985a418b2d5abc65209f30b89a1",
        "76e246dfd1ccf4e1e5e27e0127977d764797c95418fb3a12b02b082963e698b6",
        "2e1e8b8e9d4a812dc168ca37a1f18911f3888f42c3fddc42f10d3715f54d47e2",
        "fd177e2950867210d00ff9be73d2557b46b45df97b5bae103c28691e51a35916",
        "153d03675f52e19b2a428f51ff56e3d1c7f87b6a4d7eeb54e6c2d47f5fd960db", // v10, added by Wave 3 (the v1 to v9 hashes above are unchanged)
        "e907e5131d88bdd187132961d3f1eb47fd95eda74f5bb0a9621bd084d3891ade", // v11, added by play mechanics (D43); the v1 to v10 hashes above are unchanged
        "15ba5fdeeff9356c6314a4604135f581a3aa0246614367e8cc975858053ad012", // v12, added by MECH-X item 0 (weapon.bonus); the v1 to v11 hashes above are unchanged
    ];

    /// <summary>The golden revision of content schema <paramref name="version"/>, as written in the fixture.</summary>
    public static string Json(int version) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "golden", "revisions.json")))!.AsArray()[version - 1]!.ToJsonString();
}
