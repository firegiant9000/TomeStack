using System.Text.Json.Nodes;
using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (MECH-X item 3): the <c>itemMod</c> effect, typed only from v12. Below v12 it stays an unknown effect
/// and writes back exactly as the v11 build wrote it (the hashes below were captured from that build before any change, and
/// are never updated to make a test pass).
/// </summary>
public class ItemModSchemaTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private const string Head = "{\"contentId\":\"6a7e0100-0000-4000-8000-000000000011\",\"revisionId\":\"6a7e0100-0000-4000-8000-000000000111\",\"schemaVersion\":";

    private const string Middle = ",\"kind\":\"item\",\"name\":\"Fixture Keen Rune\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[";

    private const string Before = "{\"type\":\"modifier\",\"id\":\"a\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"1\",\"stacking\":\"stack\",\"automation\":\"automatic\",\"timing\":\"always\"}";

    private const string After = "{\"type\":\"modifier\",\"id\":\"b\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"1\",\"stacking\":\"stack\",\"automation\":\"automatic\",\"timing\":\"always\"}";

    private const string Initiative = "{\"type\":\"modifier\",\"id\":\"x\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"1\"";

    /// <summary>An item revision whose effects are a modifier, <paramref name="mod"/>, then a modifier (the middle position).</summary>
    private static string Item(int version, string mod) => Head + version + Middle + Before + "," + mod + "," + After + "]}";

    private static ContentRevision Read(string json) => RevisionHashProperties.Read(json);

    private static ValidationReport Validate(ContentRevision revision) => ContentValidator.Validate(revision, Catalog);

    private static ModifierEffect Nested(string id, string target = "initiative") =>
        new() { Id = id, Operation = ModifierOperation.Bonus, Target = target, Value = "1" };

    private static ItemModEffect Mod(int? bonus = 1, params ModifierEffect[] modifiers) =>
        new() { Id = "m1", AppliesTo = ModTarget.Weapon, Bonus = bonus, Modifiers = modifiers.Length == 0 ? null : modifiers };

    private static ContentRevision Built(int version, ContentKind kind = ContentKind.Item, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse("6a7e0100-0000-4000-8000-000000000011"), RevisionId = Guid.Parse("6a7e0100-0000-4000-8000-000000000111"),
        SchemaVersion = version, Kind = kind, Name = "Fixture Keen Rune", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Guid.Parse("6a7e0000-0000-4000-8000-000000000001")), Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ValidationReport ValidateMod(ItemModEffect mod, ContentKind kind = ContentKind.Item, int version = 12) =>
        Validate(Built(version, kind, mod));

    [Fact]
    public void A_v12_item_mod_reads()
    {
        var revision = Read(Item(12, "{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"armor\",\"bonus\":1,\"modifiers\":[" + Initiative + "}]}"));

        var mod = Assert.IsType<ItemModEffect>(revision.Effects[1]);
        Assert.Equal(ModTarget.Armor, mod.AppliesTo);
        Assert.Equal(1, mod.Bonus);
        var nested = Assert.Single(mod.Modifiers!);
        Assert.Equal("initiative", nested.Target);
        Assert.Null(mod.Extensions);
        Assert.Null(nested.Extensions);
    }

    /// <summary>Captured on the v11 build (75b1fb67) before any change: the revision hash with the mod written in the middle.</summary>
    [Theory]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon\",\"bonus\":1}", "09bce7af44383045da3d89ec36affb109b70779438e6327223cec11eef4a76fd")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"ring\"}", "90da12c4d989d98f672affbe6a3b03b6b7958fb6e476a65e0f2309b256a60be9")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon\",\"modifiers\":[{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"content\"}]}", "7451f851f924d22da591826ac04fa94de70b390a3d2f26456abae9b6d60d6048")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon\",\"modifiers\":[{\"type\":\"modifier\",\"id\":\"x\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"1\",\"toggleContent\":\"6a7e0100-0000-4000-8000-000000000999\"}]}", "96cf7519ef7b83e876615394ea51373975783ec4e66de4cc7c02a818ff976f98")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon\",\"modifiers\":[{\"type\":\"modifier\",\"id\":\"x\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"1\",\"zzz\":1}]}", "eec4bce438b5cd3c6fca2afe91ce6b751aded9f66f36558225e9ce7240dd2462")]
    public void Below_v12_an_item_mod_stays_unknown_byte_for_byte(string mod, string hash)
    {
        var json = Item(11, mod);
        var revision = Read(json);

        var stored = RevisionHashProperties.Write(revision);

        Assert.Equal(json, stored);
        Assert.Equal(hash, RevisionHashProperties.Hash(stored));
        Assert.IsType<UnknownEffect>(revision.Effects[1]);
        var report = Validate(revision);
        Assert.Contains(report.Warnings, w => w.Code == "validate.effect-unsupported");
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.effect-malformed");
        var without = Validate(Read(Head + 11 + Middle + Before + "," + After + "]}"));
        Assert.Equal(without.RequiredSchemaVersion, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void An_item_mod_bonus_is_a_whole_number_from_0_to_10(int bonus, bool valid)
    {
        var report = ValidateMod(Mod(bonus));
        Assert.Equal(valid, report.Errors.All(e => e.Code != "validate.item-mod-bonus"));
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.requires-v12");
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(9, true)]
    public void An_item_mod_holds_at_most_8_modifiers(int count, bool tooMany)
    {
        var modifiers = Enumerable.Range(0, count).Select(i => Nested("n" + i)).ToArray();
        Assert.Equal(tooMany, ValidateMod(Mod(null, modifiers)).Errors.Any(e => e.Code == "validate.item-mod-modifiers"));
    }

    [Fact]
    public void An_item_mod_belongs_on_an_item()
    {
        var report = ValidateMod(Mod(), ContentKind.Feature);
        Assert.Contains(report.Errors, e => e.Code == "validate.item-mod-kind" && e.Message.Contains("feature content", StringComparison.Ordinal));
        Assert.DoesNotContain(ValidateMod(Mod()).Errors, e => e.Code == "validate.item-mod-kind");
    }

    [Fact]
    public void An_item_is_at_most_one_mod()
    {
        Assert.Contains(Validate(Built(12, ContentKind.Item, Mod(), Mod() with { Id = "m2" })).Errors, e => e.Code == "validate.item-mod-duplicate");
        Assert.DoesNotContain(ValidateMod(Mod()).Errors, e => e.Code == "validate.item-mod-duplicate");
    }

    [Fact]
    public void A_mod_cannot_also_be_a_weapon_or_armor()
    {
        var weapon = new WeaponEffect { Id = "w", Category = WeaponCategory.Simple, Attack = WeaponAttack.Melee, Damage = "1d6", DamageType = "slashing", WeaponKey = "blade" };
        var armor = new ArmorEffect { Id = "a", Category = ArmorCategory.Light, ArmorClass = 11 };
        Assert.Contains(Validate(Built(12, ContentKind.Item, Mod(), weapon)).Errors, e => e.Code == "validate.item-mod-host");
        Assert.Contains(Validate(Built(12, ContentKind.Item, Mod(), armor)).Errors, e => e.Code == "validate.item-mod-host");
        Assert.DoesNotContain(Validate(Built(12, ContentKind.Item, Mod(), new ItemEffect { Id = "i", RequiresAttunement = true })).Errors, e => e.Code == "validate.item-mod-host");
    }

    [Fact]
    public void A_mod_with_nothing_to_apply_is_a_warning()
    {
        var empty = ValidateMod(Mod(null));
        Assert.Contains(empty.Warnings, w => w.Code == "validate.item-mod-empty");
        Assert.DoesNotContain(empty.Errors, e => e.Code == "validate.item-mod-empty");
        Assert.DoesNotContain(ValidateMod(Mod(0)).Warnings, w => w.Code == "validate.item-mod-empty");
        Assert.DoesNotContain(ValidateMod(Mod(null, Nested("n"))).Warnings, w => w.Code == "validate.item-mod-empty");
    }

    [Theory]
    [InlineData("toggleContent")]
    [InlineData("toggle")]
    [InlineData("whileArmored")]
    [InlineData("timing")]
    [InlineData("resource")]
    [InlineData("assisted")]
    public void Nested_modifier_restrictions(string restriction)
    {
        var nested = restriction switch
        {
            "toggleContent" => Nested("n") with { Toggle = "t", ToggleContent = Guid.Parse("6a7e0100-0000-4000-8000-000000000999") },
            "toggle" => Nested("n") with { Toggle = "t", Timing = EffectTiming.WhileActive },
            "whileArmored" => Nested("n") with { WhileArmored = true },
            "timing" => Nested("n") with { Timing = EffectTiming.WhileActive },
            "resource" => Nested("n", "resource.pool"),
            _ => Nested("n") with { Automation = AutomationStatus.Assisted },
        };

        var error = Assert.Single(ValidateMod(Mod(null, nested)).Errors, e => e.Code == "validate.item-mod-modifier");
        Assert.Contains("Item mod 'm1': modifier 'n' may only set operation, target, value and text (always on, automatic).", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_modifier_still_passes_the_top_level_modifier_checks()
    {
        var report = ValidateMod(Mod(null, Nested("n", "nope")));
        Assert.Contains(report.Errors, e => e.Code == "validate.unknown-target");
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.item-mod-modifier");
        Assert.DoesNotContain(ValidateMod(Mod(null, Nested("n"))).Errors, e => e.Code is "validate.unknown-target" or "validate.item-mod-modifier");
    }

    /// <summary>The nested modifier is typed (S8: a direct <c>IReadOnlyList&lt;ModifierEffect&gt;</c>), so an unknown key stays in its extension data and writes back.</summary>
    [Fact]
    public void Nested_modifier_unknown_key_round_trips()
    {
        var json = Item(12, "{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon\",\"modifiers\":[" + Initiative + ",\"zzz\":1}]}");
        var revision = Read(json);

        var first = RevisionHashProperties.Write(revision);
        var second = RevisionHashProperties.Write(Read(first));

        Assert.Equal(first, second);
        Assert.Equal(RevisionHashProperties.Hash(first), RevisionHashProperties.Hash(second));
        Assert.Contains("\"timing\":\"always\",\"zzz\":1}],\"automation\"", first, StringComparison.Ordinal);
        var mod = Assert.IsType<ItemModEffect>(revision.Effects[1]);
        Assert.True(mod.Modifiers![0].Extensions!.ContainsKey("zzz"));
    }

    /// <summary>A nested toggleContent is typed (it is not stripped in a v12 mod), blocks with the nested-modifier error and keeps its bytes.</summary>
    [Fact]
    public void A_nested_toggle_content_blocks_and_keeps_its_bytes()
    {
        var json = Item(12, "{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon\",\"modifiers\":[" + Initiative + ",\"toggleContent\":\"6a7e0100-0000-4000-8000-000000000999\"}]}");
        var revision = Read(json);

        var first = RevisionHashProperties.Write(revision);

        Assert.Equal(first, RevisionHashProperties.Write(Read(first)));
        Assert.Contains("\"toggleContent\":\"6a7e0100-0000-4000-8000-000000000999\"", first, StringComparison.Ordinal);
        Assert.Contains(Validate(revision).Errors, e => e.Code == "validate.item-mod-modifier");
    }

    [Theory]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"ring\"}")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon, armor\"}")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":2}")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"bonus\":1}")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon\",\"bonus\":\"x\"}")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon\",\"bonus\":1.5}")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon\",\"modifiers\":[{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"content\"}]}")]
    [InlineData("{\"type\":\"itemMod\",\"id\":\"m1\",\"appliesTo\":\"weapon\",\"modifiers\":{}}")]
    public void Unreadable_item_mod_blocks_from_v12(string mod)
    {
        var report = Validate(Read(Item(12, mod)));

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed" && e.Message.Contains("type 'itemMod'", StringComparison.Ordinal));
        Assert.Equal(12, report.RequiredSchemaVersion);
        Assert.DoesNotContain(Validate(Read(Item(11, mod))).Errors, e => e.Code == "validate.effect-malformed");
    }

    [Fact]
    public void Item_mod_requires_v12()
    {
        var old = Validate(Built(11, ContentKind.Item, Mod()));
        Assert.Contains(old.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("an item mod", StringComparison.Ordinal));

        var ok = Validate(Built(12, ContentKind.Item, Mod()));
        Assert.DoesNotContain(ok.Errors, e => e.Code == "validate.requires-v12");
        Assert.Equal(12, ok.RequiredSchemaVersion);
    }

    /// <summary>
    /// The six revisions of tests/RulesFixtures/golden/item-mod-revisions.json (the golden harness holds one revision per version, so
    /// these have their own file and hashes, pinned in GoldenRevisions.cs).
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Item_mod_goldens_keep_their_hashes(int index)
    {
        var golden = GoldenRevisions.ItemModJson(index);
        var stored = RevisionHashProperties.Write(Read(golden));

        Assert.True(GoldenRevisions.ItemModHashes[index] == RevisionHashProperties.Hash(stored), $"Item mod golden {index} now hashes to {RevisionHashProperties.Hash(stored)}.");
        Assert.Equal(stored, RevisionHashProperties.Write(Read(stored)));
        Assert.Null(RevisionHashProperties.UnknownPartsChanged(12, JsonNode.Parse(golden)!.AsObject(), stored));
        Assert.All(Read(stored).Effects, e => Assert.IsNotType<UnknownEffect>(e));
        Assert.All(Validate(Read(stored)).Errors, e => Assert.Equal("validate.source-missing", e.Code)); // the fixture catalog has no source; nothing else is wrong
        if (index < 3)
            Assert.IsType<ItemModEffect>(Read(stored).Effects[0]);
    }

    [Fact]
    public void Item_mod_schema_version_is_current()
    {
        Assert.Equal(12, ContentRevision.ItemModSchemaVersion);
        Assert.Equal(ContentRevision.ItemModSchemaVersion, ContentRevision.CurrentSchemaVersion);
        Assert.Equal(ContentRevision.WeaponBonusSchemaVersion, ContentRevision.ItemModSchemaVersion);
        Assert.Equal(10, ItemModEffect.MaxBonus);
    }
}
