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

    public static ContentPack Pack() => Load<ContentPack>("fixture-pack.json");

    public static InMemoryContentCatalog Catalog() => new(Pack());

    public static Character Srd51Character() => Load<Character>("characters/srd51-quickfoot.json");

    public static Character Srd521Character() => Load<Character>("characters/srd521-courier.json");

    private static ContentReference Ref(int n) =>
        new(Guid.Parse($"5f0dc000-0000-4000-8000-00000000000{n}"), Guid.Parse($"5f0de000-0000-4000-8000-00000000000{n}"));

    private static T Load<T>(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RulesFixtures", relativePath);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), RulesJson.Options)
            ?? throw new InvalidOperationException($"Fixture {relativePath} is empty.");
    }
}
