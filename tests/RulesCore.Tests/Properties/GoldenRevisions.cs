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

    /// <summary>
    /// The hashes of the two v12 revisions in tests/RulesFixtures/golden/payment-revisions.json (MECH-X item 1: "Fixture Bloodprice
    /// Rite" and "Fixture Hourglass Ward"), kept apart because the harness above holds one revision per version. Never update these
    /// to make a test pass.
    /// </summary>
    public static readonly string[] PaymentHashes =
    [
        "fe7c4ca8cae4454325f80925cf9fe0adcb6e86c0c5b6dc414ae58c07b2694458", // "Fixture Bloodprice Rite"
        "3ac62e1b9e877821290a660403a64fe4cfe38a29634690772f09db8ca97463ae", // "Fixture Hourglass Ward"
    ];

    /// <summary>
    /// The hash of the v12 revision "Fixture Shifting Repertoire", the first of tests/RulesFixtures/golden/choice-revisions.json (MECH-X
    /// item 2; the rest of that file are its original options). Never update this to make a test pass.
    /// </summary>
    public static readonly string[] ChoiceHashes =
    [
        "751caa6c057f6c008b0b4d930c41852b59e546db9ce008ef3e1661c6bc738ae8", // "Fixture Shifting Repertoire"
    ];

    /// <summary>
    /// The hashes of the six v12 revisions of tests/RulesFixtures/golden/item-mod-revisions.json (MECH-X item 3: "Fixture Keen Rune",
    /// "Fixture Warding Plating", "Fixture Steady Lining", then the hosts "Fixture Longblade", "Fixture Scale Coat", "Fixture Round
    /// Guard"). Never update these to make a test pass.
    /// </summary>
    public static readonly string[] ItemModHashes =
    [
        "f6f2af29da104190a2bf9db13a7098c7b8ecb2face40177636a42c8a3b6e893c", // "Fixture Keen Rune"
        "e69d9e4afdcc0ab4285f4c92d1b442f08b70843ec17ed0ce1f05b50e4883efb9", // "Fixture Warding Plating"
        "428684ed9432c4dcd25e8ad55dbdbfdaa8999dfa1f751e2bdf2c77920b4e349d", // "Fixture Steady Lining"
        "36d0cab0bc4190ba1b99a270075e0096e31222ce0244249766a6f98844d2af8b", // "Fixture Longblade"
        "a41d0c185c8d934f11a7096b1458458157b2289a811472b403dbbe0d741a4493", // "Fixture Scale Coat"
        "44d633ecd75353e4530245d6ed9d8db7a36bda3696f52ff49bd840885047caf8", // "Fixture Round Guard"
    ];

    /// <summary>The <paramref name="index"/>th revision of the item mod golden file, as written in the fixture.</summary>
    public static string ItemModJson(int index) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "golden", "item-mod-revisions.json")))!.AsArray()[index]!.ToJsonString();

    /// <summary>The <paramref name="index"/>th revision of the choice golden file, as written in the fixture.</summary>
    public static string ChoiceJson(int index) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "golden", "choice-revisions.json")))!.AsArray()[index]!.ToJsonString();

    /// <summary>The <paramref name="index"/>th revision of the payment golden file, as written in the fixture.</summary>
    public static string PaymentJson(int index) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "golden", "payment-revisions.json")))!.AsArray()[index]!.ToJsonString();

    /// <summary>The golden revision of content schema <paramref name="version"/>, as written in the fixture.</summary>
    public static string Json(int version) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "golden", "revisions.json")))!.AsArray()[version - 1]!.ToJsonString();
}
