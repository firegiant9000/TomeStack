using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

internal static class Fixtures
{
    public static readonly Guid Source2014 = Guid.Parse("5f0d5100-0000-4000-8000-000000000001");
    public static readonly Guid Source2024 = Guid.Parse("5f0d5210-0000-4000-8000-000000000001");
    public static readonly Guid SourceShared = Guid.Parse("5f0d5000-0000-4000-8000-000000000001");

    public static readonly ContentReference Quickfoot = Ref(1);
    public static readonly ContentReference Courier = Ref(2);
    public static readonly ContentReference Wanderer = Ref(3);
    public static readonly ContentReference KeenReflexes = Ref(4);
    public static readonly ContentReference UnreviewedTrick = Ref(5);
    public static readonly ContentReference StarSense = Ref(6);

    public static readonly ContentReference Wayfarer = M1Ref(1);
    public static readonly ContentReference Watchful = M1Ref(2);
    public static readonly ContentReference KeenSenses2014 = M1Ref(3);
    public static readonly ContentReference KeenSenses2024 = M1Ref(4);

    public static ContentPack Pack() => Load<ContentPack>("fixture-pack.json");

    public static ContentPack M1Pack() => Load<ContentPack>("fixture-pack-m1.json");

    public static InMemoryContentCatalog Catalog() => new(Pack());

    /// <summary>M0 and M1 fixture packs together (M1 content cites M0 family sources).</summary>
    public static InMemoryContentCatalog M1Catalog()
    {
        var (m0, m1) = (Pack(), M1Pack());
        return new([.. m0.Sources, .. m1.Sources], [.. m0.Revisions, .. m1.Revisions]);
    }

    public static Character Srd51Character() => Load<Character>("characters/srd51-quickfoot.json");

    public static Character Srd521Character() => Load<Character>("characters/srd521-courier.json");

    public static Character Load(string characterFile) => Load<Character>($"characters/{characterFile}");

    private static ContentReference M1Ref(int n) =>
        new(Guid.Parse($"5f1dc000-0000-4000-8000-00000000000{n}"), Guid.Parse($"5f1de000-0000-4000-8000-00000000000{n}"));

    private static ContentReference Ref(int n) =>
        new(Guid.Parse($"5f0dc000-0000-4000-8000-00000000000{n}"), Guid.Parse($"5f0de000-0000-4000-8000-00000000000{n}"));

    private static T Load<T>(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RulesFixtures", relativePath);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), RulesJson.Options)
            ?? throw new InvalidOperationException($"Fixture {relativePath} is empty.");
    }
}
