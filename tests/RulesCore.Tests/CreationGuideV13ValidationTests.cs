using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>Content schema v13 (ADR-024): validation of creation guides and packs, and the validate.requires-v13 rung.</summary>
public class CreationGuideV13ValidationTests
{
    private static Guid Id(int n) => Guid.Parse($"6a7e1300-0000-4000-8000-{n:D12}");

    private static readonly Guid ClassId = Id(1);
    private static readonly Guid BackgroundId = Id(2);
    private static readonly Guid MaceId = Id(3);
    private static readonly Guid PackId = Id(4);
    private static readonly Guid FeatureId = Id(5);
    private static readonly Guid DraftItemId = Id(6);
    private static readonly Guid OldFamilyItemId = Id(7);
    private static readonly Guid PlainItemId = Id(8);
    private static readonly Guid GuideContentId = Id(100);

    private static ContentRevision Rev(Guid id, ContentKind kind, string family = RulesFamilies.Srd521, RevisionStatus status = RevisionStatus.Published,
        int version = 13, IReadOnlyList<Effect>? effects = null) => new()
        {
            ContentId = id, RevisionId = Id(1000 + (int)(id.ToByteArray()[^1])), SchemaVersion = version, Kind = kind, Name = $"Fixture {kind} {id.ToByteArray()[^1]}",
            RulesFamilies = [family], Provenance = new(Fixtures.Source2024), Status = status, Effects = effects ?? [],
        };

    private static readonly InMemoryContentCatalog Catalog = new(Fixtures.Pack().Sources,
    [
        Rev(ClassId, ContentKind.Class),
        Rev(BackgroundId, ContentKind.Background),
        Rev(MaceId, ContentKind.Item),
        Rev(PlainItemId, ContentKind.Item),
        Rev(PackId, ContentKind.Item, effects: [new ItemEffect { Id = "item", Contains = [new PackLine(new GuideTarget(MaceId), 2)] }]),
        Rev(FeatureId, ContentKind.Feature),
        Rev(DraftItemId, ContentKind.Item, status: RevisionStatus.Draft),
        Rev(OldFamilyItemId, ContentKind.Item, RulesFamilies.Srd51),
    ]);

    private static GuideTarget T(Guid id) => new(id);

    private static GuideForEffect For(Guid? id = null) => new() { Id = "for", Content = T(id ?? ClassId) };

    private static PrimaryAbilitiesEffect Primary(params string[] abilities) => new() { Id = "primary", Abilities = abilities };

    private static KitLine Item(Guid? id = null, int? quantity = null) => new(Item: T(id ?? MaceId), Quantity: quantity);

    private static StartingEquipmentEffect Kit(params KitPick[] picks) => new() { Id = "kit", Picks = picks };

    private static StartingEquipmentEffect Kit(params KitLine[] lines) => Kit(new KitPick("p", "Pick", [new KitOption("a", lines)]));

    private static ContentRevision Guide(int version, params Effect[] effects) => Rev(GuideContentId, ContentKind.CreationGuide, status: RevisionStatus.Draft, version: version, effects: [.. effects]);

    private static ValidationReport Validate(params Effect[] effects) => ContentValidator.Validate(Guide(13, effects), Catalog);

    private static ValidationReport ValidateGuide(StartingEquipmentEffect kit) => Validate(For(), kit);

    private static ValidationReport ValidateItem(params PackLine[] lines) =>
        ContentValidator.Validate(Rev(Id(50), ContentKind.Item, status: RevisionStatus.Draft, effects: [new ItemEffect { Id = "item", Contains = lines }]), Catalog);

    private static void AssertError(ValidationReport report, string code) => Assert.Contains(report.Errors, e => e.Code == code);

    private static void AssertNoCode(ValidationReport report, string code) => Assert.DoesNotContain(report.Errors, e => e.Code == code);

    [Fact]
    public void A_valid_guide_has_no_errors()
    {
        var report = Validate(For(), Primary("wis", "con"), Kit(
            new KitPick("weapon", "Weapon", [new KitOption("a", [Item(quantity: 1)]), new KitOption("b", [new KitLine(AnyOf: "weapon.martial")], "weapon.warhammer")]),
            new KitPick("gear", null, [new KitOption("a", [new KitLine(Pack: T(PackId)), new KitLine(Coins: new KitCoins { Gp = 10 })])])));
        Assert.Empty(report.Errors);
        Assert.Equal(13, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_guide_for_a_background_is_valid()
    {
        var report = ContentValidator.Validate(Guide(13, For(BackgroundId)), Catalog);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void A_guide_effect_on_another_kind_is_refused() =>
        AssertError(ContentValidator.Validate(Rev(Id(60), ContentKind.Feature, status: RevisionStatus.Draft, effects: [Primary("str")]), Catalog), "validate.guide-kind");

    [Fact]
    public void A_non_guide_effect_on_a_guide_is_refused()
    {
        var modifier = new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = "initiative", Value = "2" };
        AssertError(Validate(For(), modifier), "validate.guide-kind");
    }

    [Fact]
    public void A_guide_without_a_target_is_refused() => AssertError(Validate(Primary("str")), "validate.guide-target");

    [Fact]
    public void A_guide_with_two_targets_is_refused() =>
        AssertError(Validate(For(), new GuideForEffect { Id = "for2", Content = T(BackgroundId) }), "validate.guide-target");

    [Theory]
    [MemberData(nameof(BadTargets))]
    public void A_target_that_is_not_a_published_class_or_background_of_the_family_is_refused(Guid target) =>
        AssertError(Validate(For(target)), "validate.guide-target");

    public static TheoryData<Guid> BadTargets() => new()
    {
        FeatureId, // wrong kind
        Id(999), // not installed
        MaceId, // an item
    };

    [Fact]
    public void A_target_of_another_rules_family_is_refused()
    {
        var other = Rev(Id(70), ContentKind.Class, RulesFamilies.Srd51);
        var catalog = new InMemoryContentCatalog(Fixtures.Pack().Sources, [other]);
        AssertError(ContentValidator.Validate(Guide(13, For(Id(70))), catalog), "validate.guide-target");
    }

    [Theory]
    [InlineData]
    [InlineData("str", "dex", "con")]
    [InlineData("wisdom")]
    [InlineData("WIS")]
    [InlineData("wis", "wis")]
    public void Abilities_that_are_not_one_or_two_ability_keys_are_refused(params string[] abilities) =>
        AssertError(Validate(For(), Primary(abilities)), "validate.guide-abilities");

    [Theory]
    [MemberData(nameof(BadItems))]
    public void An_item_line_that_is_not_a_published_item_of_the_family_is_refused(Guid item) =>
        AssertError(ValidateGuide(Kit(Item(item))), "validate.guide-item");

    public static TheoryData<Guid> BadItems() => new()
    {
        Id(999), // not installed
        DraftItemId, // not published
        OldFamilyItemId, // another rules family
        ClassId, // not an item
    };

    [Fact]
    public void A_pack_line_whose_item_has_no_contains_is_refused() =>
        AssertError(ValidateGuide(Kit(new KitLine(Pack: T(PlainItemId)))), "validate.guide-item");

    [Theory]
    [InlineData("weapon.simple")]
    [InlineData("armor.shield")]
    public void Every_listed_any_of_category_is_valid(string category) =>
        AssertNoCode(ValidateGuide(Kit(new KitLine(AnyOf: category))), "validate.guide-category");

    [Theory]
    [InlineData("weapon.exotic")]
    [InlineData("tool.smith")]
    [InlineData("")]
    public void An_any_of_that_is_not_a_category_is_refused(string category) =>
        AssertError(ValidateGuide(Kit(new KitLine(AnyOf: category))), "validate.guide-category");

    [Theory]
    [InlineData("shield")]
    [InlineData("weapon.")]
    [InlineData("")]
    public void A_requires_proficiency_that_is_not_a_weapon_or_armor_key_is_refused(string key) =>
        AssertError(ValidateGuide(Kit(new KitPick("p", null, [new KitOption("a", [Item()], key)]))), "validate.guide-category");

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(-1)]
    public void A_line_quantity_outside_1_to_999_is_refused(int quantity) =>
        AssertError(ValidateGuide(Kit(Item(quantity: quantity))), "validate.guide-quantity");

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(999)]
    public void An_absent_or_in_range_quantity_is_valid(int? quantity) =>
        AssertNoCode(ValidateGuide(Kit(Item(quantity: quantity))), "validate.guide-quantity");

    [Fact]
    public void More_than_12_picks_are_refused()
    {
        var picks = Enumerable.Range(0, 13).Select(i => new KitPick($"p{i}", null, [new KitOption("a", [Item()])])).ToArray();
        AssertError(ValidateGuide(Kit(picks)), "validate.guide-picks");
    }

    [Fact]
    public void Twelve_picks_are_valid()
    {
        var picks = Enumerable.Range(0, 12).Select(i => new KitPick($"p{i}", null, [new KitOption("a", [Item()])])).ToArray();
        AssertNoCode(ValidateGuide(Kit(picks)), "validate.guide-picks");
    }

    [Fact]
    public void More_than_20_options_in_a_pick_are_refused()
    {
        var options = Enumerable.Range(0, 21).Select(i => new KitOption($"o{i}", [Item()])).ToArray();
        AssertError(ValidateGuide(Kit(new KitPick("p", null, options))), "validate.guide-picks");
    }

    [Fact]
    public void A_pick_with_no_options_is_refused() =>
        AssertError(ValidateGuide(Kit(new KitPick("p", null, []))), "validate.guide-picks");

    [Fact]
    public void Duplicate_pick_ids_are_refused() =>
        AssertError(ValidateGuide(Kit(new KitPick("p", null, [new KitOption("a", [Item()])]), new KitPick("p", null, [new KitOption("a", [Item()])]))), "validate.guide-picks");

    [Fact]
    public void Duplicate_option_ids_in_a_pick_are_refused() =>
        AssertError(ValidateGuide(Kit(new KitPick("p", null, [new KitOption("a", [Item()]), new KitOption("a", [Item()])]))), "validate.guide-picks");

    [Fact]
    public void A_line_with_no_kind_is_refused() => AssertError(ValidateGuide(Kit(new KitLine())), "validate.guide-picks");

    [Fact]
    public void A_line_with_two_kinds_is_refused() =>
        AssertError(ValidateGuide(Kit(new KitLine(Item: T(MaceId), AnyOf: "weapon.simple"))), "validate.guide-picks");

    [Fact]
    public void A_stray_quantity_on_a_line_that_is_not_an_item_is_ignored() =>
        Assert.Empty(ValidateGuide(Kit(new KitLine(Quantity: 5, AnyOf: "weapon.simple"))).Errors);

    [Theory]
    [InlineData(-1, null)]
    [InlineData(1_000_001, null)]
    [InlineData(10, -5)]
    public void A_coin_outside_0_to_the_maximum_is_refused(int gp, int? sp) =>
        AssertError(ValidateGuide(Kit(new KitLine(Coins: new KitCoins { Gp = gp, Sp = sp }))), "validate.guide-coins");

    [Fact]
    public void A_coins_line_that_gives_nothing_is_refused()
    {
        AssertError(ValidateGuide(Kit(new KitLine(Coins: new KitCoins()))), "validate.guide-coins");
        AssertError(ValidateGuide(Kit(new KitLine(Coins: new KitCoins { Gp = 0, Sp = 0 }))), "validate.guide-coins");
    }

    [Fact]
    public void A_coins_line_with_one_positive_coin_is_valid() =>
        Assert.Empty(ValidateGuide(Kit(new KitLine(Coins: new KitCoins { Cp = 5, Gp = 0 }))).Errors);

    [Fact]
    public void A_contained_reference_that_is_not_an_item_is_refused() =>
        AssertError(ValidateItem(new PackLine(T(FeatureId))), "validate.item-contains");

    [Fact]
    public void A_contained_reference_that_is_not_installed_is_refused() =>
        AssertError(ValidateItem(new PackLine(T(Id(999)))), "validate.item-contains");

    [Fact]
    public void A_contained_pack_is_refused() => AssertError(ValidateItem(new PackLine(T(PackId))), "validate.item-contains");

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void A_contained_quantity_outside_1_to_999_is_refused(int quantity) =>
        AssertError(ValidateItem(new PackLine(T(MaceId), quantity)), "validate.item-contains");

    [Fact]
    public void More_than_30_contained_lines_are_refused() =>
        AssertError(ValidateItem([.. Enumerable.Range(0, 31).Select(_ => new PackLine(T(MaceId)))]), "validate.item-contains");

    [Fact]
    public void A_pack_of_30_valid_lines_is_valid()
    {
        var report = ValidateItem([.. Enumerable.Range(0, 30).Select(_ => new PackLine(T(MaceId), 3))]);
        Assert.Empty(report.Errors);
        Assert.Equal(13, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Several_problems_each_report_their_own_code()
    {
        var report = Validate(Primary("nope"), Kit(new KitLine()));
        AssertError(report, "validate.guide-target");
        AssertError(report, "validate.guide-abilities");
        AssertError(report, "validate.guide-picks");
    }

    [Fact]
    public void A_guide_below_v13_fails_requires_v13_for_the_kind_and_each_effect()
    {
        var report = ContentValidator.Validate(Guide(12, For(), Primary("str"), Kit(Item())), Catalog);
        var messages = report.Errors.Where(e => e.Code == "validate.requires-v13").Select(e => e.Message).ToList();
        var message = Assert.Single(messages);
        Assert.Contains("creationGuide", message, StringComparison.Ordinal);
        Assert.Contains("guideFor", message, StringComparison.Ordinal);
        Assert.Contains("primaryAbilities", message, StringComparison.Ordinal);
        Assert.Contains("startingEquipment", message, StringComparison.Ordinal);
        Assert.Equal(13, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_pack_with_contains_below_v13_fails_requires_v13()
    {
        var item = Rev(Id(51), ContentKind.Item, status: RevisionStatus.Draft, version: 12, effects: [new ItemEffect { Id = "item", Contains = [new PackLine(T(MaceId))] }]);
        var report = ContentValidator.Validate(item, Catalog);
        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v13" && e.Message.Contains("contains", StringComparison.Ordinal));
        Assert.Equal(13, report.RequiredSchemaVersion);
    }

    [Fact]
    public void An_unreadable_guide_effect_in_a_v13_revision_is_malformed()
    {
        var revision = RevisionHashProperties.Read(
            "{\"contentId\":\"6a7e1300-0000-4000-8000-000000000101\",\"revisionId\":\"6a7e1300-0000-4000-8000-000000001101\",\"schemaVersion\":13,\"kind\":\"creationGuide\",\"name\":\"Fixture guide\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"5f0d5210-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[{\"type\":\"primaryAbilities\",\"id\":\"primary\",\"abilities\":\"wis\"}]}");
        Assert.IsType<UnknownEffect>(revision.Effects.Single());
        AssertError(ContentValidator.Validate(revision, Catalog), "validate.effect-malformed");
    }
}
