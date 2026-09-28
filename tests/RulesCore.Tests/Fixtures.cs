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
    public static readonly ContentReference Warden = M1Ref(10);
    public static readonly ContentReference WardenGuard = M1Ref(11);
    public static readonly ContentReference WardenStride = M1Ref(12);
    public static readonly ContentReference Scholar = M1Ref(13);
    public static readonly ContentReference Hardy = M1Ref(14);
    public static readonly ContentReference Bracers = M1Ref(15);
    public static readonly ContentReference WardenAthletics = M1Ref(16);
    public static readonly ContentReference WardenSurvival = M1Ref(17);
    public static readonly ContentReference WardenNature = M1Ref(18);
    public static readonly ContentReference PathOfThorns = M1Ref(19);
    public static readonly ContentReference Thorns = M1Ref(20);
    public static readonly ContentReference Crossroads = M1Ref(21);
    public static readonly ContentReference CrossroadsStr = M1Ref(22);
    public static readonly ContentReference CrossroadsDex = M1Ref(23);
    public static readonly ContentReference CrossroadsWis2014 = M1Ref(24);
    public static readonly ContentReference IronGrip = M1Ref(25);

    // M2 spellcasting fixtures (fixture-pack-m2-spells.json): invented casters and spells.
    public static readonly ContentReference Arcanist = SpellRef(1);
    public static readonly ContentReference Chanter = SpellRef(2);
    public static readonly ContentReference Oathbinder = SpellRef(3);
    public static readonly ContentReference Spark = SpellRef(11);
    public static readonly ContentReference FrostRing = SpellRef(12);
    public static readonly ContentReference Veil = SpellRef(13);
    public static readonly ContentReference EmberWave = SpellRef(14);
    public static readonly ContentReference MendingWord = SpellRef(15);

    public static ContentPack SpellPack() => Load<ContentPack>("fixture-pack-m2-spells.json");

    // M2 combat fixtures (fixture-pack-m2-combat.json): invented weapons and "Fixture Duelist".
    public static readonly ContentReference Longblade = CombatRef(1);
    public static readonly ContentReference Needle = CombatRef(2);
    public static readonly ContentReference Slingbow = CombatRef(3);
    public static readonly ContentReference Duelist = CombatRef(11);
    public static readonly ContentReference DuelistAcrobatics = CombatRef(21);
    public static readonly ContentReference DuelistAthletics = CombatRef(22);

    public static ContentPack CombatPack() => Load<ContentPack>("fixture-pack-m2-combat.json");

    // M3 B2 fixtures (fixture-pack-m3-effects.json).
    public static readonly ContentReference RadiantStance = new(Guid.Parse("5f8dc000-0000-4000-8000-000000000001"), Guid.Parse("5f8de000-0000-4000-8000-000000000001"));
    public static readonly ContentReference BorrowedSpark = new(Guid.Parse("5f8dc000-0000-4000-8000-000000000002"), Guid.Parse("5f8de000-0000-4000-8000-000000000002"));

    public static ContentPack EffectsPack() => Load<ContentPack>("fixture-pack-m3-effects.json");

    /// <summary>Every fixture pack: M0, M1, spellcasting and combat.</summary>
    public static InMemoryContentCatalog AllCatalog()
    {
        ContentPack[] packs = [Pack(), M1Pack(), SpellPack(), CombatPack(), EffectsPack()];
        return new([.. packs.SelectMany(p => p.Sources)], [.. packs.SelectMany(p => p.Revisions)]);
    }

    private static ContentReference CombatRef(int n) =>
        new(Guid.Parse($"5f6dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"5f6de000-0000-4000-8000-{n:D12}"));

    /// <summary>The M0, M1 and spellcasting fixture packs together.</summary>
    public static InMemoryContentCatalog SpellCatalog()
    {
        var (m0, m1, spells) = (Pack(), M1Pack(), SpellPack());
        return new([.. m0.Sources, .. m1.Sources, .. spells.Sources], [.. m0.Revisions, .. m1.Revisions, .. spells.Revisions]);
    }

    private static ContentReference SpellRef(int n) =>
        new(Guid.Parse($"5f5dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"5f5de000-0000-4000-8000-{n:D12}"));

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
        new(Guid.Parse($"5f1dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"5f1de000-0000-4000-8000-{n:D12}"));

    private static ContentReference Ref(int n) =>
        new(Guid.Parse($"5f0dc000-0000-4000-8000-00000000000{n}"), Guid.Parse($"5f0de000-0000-4000-8000-00000000000{n}"));

    private static T Load<T>(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RulesFixtures", relativePath);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), RulesJson.Options)
            ?? throw new InvalidOperationException($"Fixture {relativePath} is empty.");
    }
}
