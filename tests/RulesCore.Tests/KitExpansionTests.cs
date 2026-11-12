using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>Content schema v13 (ADR-024): <see cref="KitExpansion"/> turns a kit and the player's choices into equipment and coins. Original fixtures (tests/RulesFixtures/fixture-pack-kits.json).</summary>
public class KitExpansionTests
{
    private static Guid C(int n) => Guid.Parse($"7a13c000-0000-4000-8000-{n:D12}");

    private static readonly Guid Cudgel = C(1301);
    private static readonly Guid Glaive = C(1302);
    private static readonly Guid Rope = C(1303);
    private static readonly Guid Lantern = C(1304);
    private static readonly Guid Satchel = C(1305);
    private static readonly Guid GuideId = C(1306);

    private static readonly ContentPack Pack = JsonSerializer.Deserialize<ContentPack>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "fixture-pack-kits.json")), RulesJson.Options)!;

    private static InMemoryContentCatalog Catalog() => new(Pack);

    private static StartingEquipmentEffect FixtureKit() =>
        Pack.Revisions.Single(r => r.ContentId == GuideId).Effects.OfType<StartingEquipmentEffect>().Single();

    private static StartingEquipmentEffect Kit(params KitPick[] picks) => new() { Id = "kit", Picks = picks };

    private static KitPick Pick(string id, params KitLine[] lines) => new(id, null, [new KitOption("a", lines)]);

    private static KitLine Item(Guid id, int? quantity = null) => new(Item: new GuideTarget(id), Quantity: quantity);

    private static int Count(KitResult result, Guid item) => result.Equipment.Where(e => e.Item.ContentId == item).Sum(e => e.Quantity);

    private static void AssertCode(KitResult result, string code) => Assert.Contains(result.Problems, p => p.Code == code);

    [Fact]
    public void Options_a_of_the_fixture_guide_give_the_expected_entries()
    {
        var result = KitExpansion.Expand(FixtureKit(), [new("weapon", "a"), new("funds", "a")], Catalog());

        Assert.Empty(result.Problems);
        Assert.Equal(new Currency(Gp: 50), result.Coins);
        Assert.Equal(3, result.Equipment.Count);
        Assert.Equal(1, Count(result, Cudgel));
        Assert.Equal(5, Count(result, Rope)); // 3 from the rope pick, 2 from the satchel
        Assert.Equal(1, Count(result, Lantern));
        Assert.Equal(0, Count(result, Satchel)); // the pack itself is never added
        Assert.Equal(0, Count(result, Glaive));
        Assert.All(result.Equipment, e =>
        {
            Assert.False(e.Equipped);
            Assert.NotNull(e.Id);
        });
        Assert.Equal(result.Equipment.Count, result.Equipment.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Entries_pin_the_newest_published_revision()
    {
        var result = KitExpansion.Expand(Kit(Pick("p", Item(Cudgel))), [], Catalog());

        var entry = Assert.Single(result.Equipment);
        Assert.Equal(new ContentReference(Cudgel, Guid.Parse("7a13e000-0000-4000-8000-000000001301")), entry.Item);
    }

    [Fact]
    public void A_coins_option_gives_coins_and_no_items()
    {
        var funds = FixtureKit().Picks.Single(p => p.Id == "funds");
        var result = KitExpansion.Expand(Kit(funds), [new("funds", "a")], Catalog());

        Assert.Empty(result.Problems);
        Assert.Empty(result.Equipment);
        Assert.Equal(50, result.Coins.Gp);
        Assert.Equal(0, result.Coins.Sp);
    }

    [Fact]
    public void A_pick_with_two_options_follows_the_choice()
    {
        var funds = FixtureKit().Picks.Single(p => p.Id == "funds");
        var result = KitExpansion.Expand(Kit(funds), [new("funds", "b")], Catalog());

        Assert.Equal(1, Count(result, Lantern));
        Assert.Equal(0, result.Coins.Gp);
    }

    [Fact]
    public void A_pack_adds_its_contents_not_itself()
    {
        var result = KitExpansion.Expand(Kit(Pick("p", new KitLine(Pack: new GuideTarget(Satchel)))), [], Catalog());

        Assert.Empty(result.Problems);
        Assert.Equal(2, Count(result, Rope));
        Assert.Equal(1, Count(result, Lantern));
        Assert.Equal(2, result.Equipment.Count);
    }

    [Fact]
    public void A_duplicate_item_across_picks_merges_into_one_entry()
    {
        var result = KitExpansion.Expand(Kit(Pick("x", Item(Rope, 2)), Pick("y", Item(Rope, 4))), [], Catalog());

        var entry = Assert.Single(result.Equipment);
        Assert.Equal(6, entry.Quantity);
    }

    [Fact]
    public void A_null_quantity_counts_as_one()
    {
        var result = KitExpansion.Expand(Kit(Pick("x", Item(Rope), Item(Rope))), [], Catalog());

        Assert.Equal(2, Assert.Single(result.Equipment).Quantity);
    }

    [Fact]
    public void A_summed_quantity_clamps_to_the_maximum()
    {
        var result = KitExpansion.Expand(Kit(Pick("x", Item(Rope, EquipmentEntry.MaxQuantity), Item(Rope, EquipmentEntry.MaxQuantity), Item(Rope, int.MaxValue))), [], Catalog());

        Assert.Equal(EquipmentEntry.MaxQuantity, Assert.Single(result.Equipment).Quantity);
    }

    [Fact]
    public void Coins_sum_across_lines_and_denominations()
    {
        var result = KitExpansion.Expand(Kit(Pick("x", new KitLine(Coins: new KitCoins { Gp = 5, Sp = 3 }), new KitLine(Coins: new KitCoins { Gp = 7 }))), [], Catalog());

        Assert.Equal(new Currency(Sp: 3, Gp: 12), result.Coins);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Coins_that_overflow_clamp_with_a_diagnostic()
    {
        var big = new KitCoins { Gp = int.MaxValue };
        var result = KitExpansion.Expand(Kit(Pick("x", new KitLine(Coins: big), new KitLine(Coins: big))), [], Catalog());

        Assert.Equal(Currency.MaxCoins, result.Coins.Gp);
        AssertCode(result, "kit.coins-capped");
    }

    [Fact]
    public void An_unresolved_item_skips_only_that_line()
    {
        var result = KitExpansion.Expand(Kit(Pick("x", Item(Guid.NewGuid()), Item(Lantern))), [], Catalog());

        AssertCode(result, "kit.item-unresolved");
        Assert.Equal(1, Count(result, Lantern));
        Assert.Single(result.Equipment);
    }

    [Fact]
    public void A_line_naming_a_non_item_is_unresolved()
    {
        var result = KitExpansion.Expand(Kit(Pick("x", Item(GuideId), Item(Lantern))), [], Catalog());

        AssertCode(result, "kit.item-unresolved");
        Assert.Single(result.Equipment);
    }

    [Fact]
    public void A_pack_line_naming_a_plain_item_is_unresolved()
    {
        var result = KitExpansion.Expand(Kit(Pick("x", new KitLine(Pack: new GuideTarget(Rope)), Item(Lantern))), [], Catalog());

        AssertCode(result, "kit.item-unresolved");
        Assert.Single(result.Equipment);
    }

    [Fact]
    public void A_draft_item_is_unresolved()
    {
        var draft = Pack.Revisions.Single(r => r.ContentId == Rope) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft };
        var catalog = new InMemoryContentCatalog(Pack.Sources, [.. Pack.Revisions.Where(r => r.ContentId != Rope), draft]);

        var result = KitExpansion.Expand(Kit(Pick("x", Item(Rope))), [], catalog);

        AssertCode(result, "kit.item-unresolved");
        Assert.Empty(result.Equipment);
    }

    [Fact]
    public void A_pack_that_names_itself_or_another_pack_does_not_loop_or_expand()
    {
        var self = Guid.NewGuid();
        var other = Guid.NewGuid();
        ContentRevision PackRevision(Guid id, params PackLine[] lines) => Pack.Revisions.Single(r => r.ContentId == Satchel) with
        {
            ContentId = id, RevisionId = Guid.NewGuid(), Effects = [new ItemEffect { Id = "item", Contains = lines }],
        };
        var catalog = new InMemoryContentCatalog(Pack.Sources,
        [
            .. Pack.Revisions,
            PackRevision(self, new PackLine(new GuideTarget(self)), new PackLine(new GuideTarget(Rope)), new PackLine(new GuideTarget(other))),
            PackRevision(other, new PackLine(new GuideTarget(Lantern))),
        ]);

        var result = KitExpansion.Expand(Kit(Pick("x", new KitLine(Pack: new GuideTarget(self)))), [], catalog);

        Assert.Equal(2, result.Problems.Count(p => p.Code == "kit.item-unresolved"));
        Assert.Equal(1, Count(result, Rope));
        Assert.Single(result.Equipment);
    }

    [Fact]
    public void A_missing_choice_skips_the_pick_and_keeps_the_rest()
    {
        var result = KitExpansion.Expand(FixtureKit(), [], Catalog());

        Assert.Equal(2, result.Problems.Count(p => p.Code == "kit.choice-missing")); // weapon and funds each offer two options
        Assert.Equal(0, Count(result, Cudgel));
        Assert.Equal(5, Count(result, Rope));
        Assert.Equal(1, Count(result, Lantern)); // from the satchel
    }

    [Fact]
    public void A_choice_naming_an_unknown_option_is_a_missing_choice()
    {
        var result = KitExpansion.Expand(Kit(Pick("x", Item(Rope))), [new("x", "zzz")], Catalog());

        AssertCode(result, "kit.choice-missing");
        Assert.Empty(result.Equipment);
    }

    [Fact]
    public void A_choice_naming_an_unknown_pick_is_ignored()
    {
        var result = KitExpansion.Expand(Kit(Pick("x", Item(Rope))), [new("nope", "a")], Catalog());

        Assert.Empty(result.Problems);
        Assert.Single(result.Equipment);
    }

    [Fact]
    public void An_any_of_line_takes_the_chosen_item_in_its_category()
    {
        var result = KitExpansion.Expand(FixtureKit(), [new("weapon", "b", new Dictionary<int, Guid> { [0] = Glaive }), new("funds", "a")], Catalog());

        Assert.Empty(result.Problems);
        Assert.Equal(1, Count(result, Glaive));
        Assert.Equal(0, Count(result, Cudgel));
    }

    [Fact]
    public void An_any_of_choice_outside_its_category_skips_that_line()
    {
        var result = KitExpansion.Expand(FixtureKit(), [new("weapon", "b", new Dictionary<int, Guid> { [0] = Cudgel })], Catalog());

        AssertCode(result, "kit.any-of-mismatch");
        Assert.Equal(0, Count(result, Cudgel));
        Assert.Equal(5, Count(result, Rope));
    }

    [Fact]
    public void An_any_of_line_with_no_chosen_item_is_a_missing_choice()
    {
        var result = KitExpansion.Expand(FixtureKit(), [new("weapon", "b")], Catalog());

        AssertCode(result, "kit.choice-missing");
        Assert.Equal(5, Count(result, Rope));
    }

    [Fact]
    public void An_unknown_category_never_matches()
    {
        var kit = Kit(Pick("x", new KitLine(AnyOf: "weapon.nonsense")));
        var result = KitExpansion.Expand(kit, [new("x", "a", new Dictionary<int, Guid> { [0] = Cudgel })], Catalog());

        AssertCode(result, "kit.any-of-mismatch");
    }

    [Fact]
    public void Unvalidated_shapes_do_not_throw()
    {
        var kit = Kit(new KitPick("empty", null, []), Pick("blank", new KitLine()));

        var result = KitExpansion.Expand(kit, [], Catalog());

        Assert.Empty(result.Equipment);
    }

    [Fact]
    public void Null_elements_are_skipped_without_throwing()
    {
        var pack = Pack.Revisions.Single(r => r.ContentId == Satchel) with
        {
            ContentId = C(1399), RevisionId = Guid.NewGuid(), Effects = [new ItemEffect { Id = "item", Contains = [null!, new PackLine(new GuideTarget(Rope))] }],
        };
        var catalog = new InMemoryContentCatalog(Pack.Sources, [.. Pack.Revisions, pack]);
        var kit = Kit(
            null!,
            new KitPick("n", null, [null!]),
            new KitPick("m", null, [null!, new KitOption("a", [Item(Lantern)])]),
            new KitPick("p", null, [new KitOption("a", [null!, Item(Rope), new KitLine(Pack: new GuideTarget(C(1399)))])]));

        var result = KitExpansion.Expand(kit, [null!, new("m", "a"), new("p", "a")], catalog);

        Assert.Equal(1, Count(result, Lantern));
        Assert.Equal(2, Count(result, Rope)); // the plain line and the pack's one good line
        var merged = KitExpansion.Merge(null!, result, null!);
        Assert.Equal(2, merged.Equipment.Count);
        Assert.Equal(2, Count(merged, Rope));
    }

    [Fact]
    public void A_pack_line_quantity_multiplies_its_contents()
    {
        var result = KitExpansion.Expand(Kit(Pick("p", new KitLine(Pack: new GuideTarget(Satchel), Quantity: 2))), [], Catalog());

        Assert.Equal(4, Count(result, Rope)); // 2 x (2 rope)
        Assert.Equal(2, Count(result, Lantern)); // 2 x (null = 1)
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void A_pack_quantity_product_clamps_to_the_maximum_once()
    {
        var result = KitExpansion.Expand(Kit(Pick("p", new KitLine(Pack: new GuideTarget(Satchel), Quantity: int.MaxValue))), [], Catalog());

        Assert.Equal(EquipmentEntry.MaxQuantity, Count(result, Rope));
        Assert.Equal(EquipmentEntry.MaxQuantity, Count(result, Lantern));
        Assert.Equal(2, result.Problems.Count(p => p.Code == "kit.quantity-capped"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_line_quantity_below_one_skips_the_line_and_keeps_the_rest(int quantity)
    {
        var kit = Kit(Pick("x",
            Item(Rope, quantity),
            new KitLine(Pack: new GuideTarget(Satchel), Quantity: quantity),
            new KitLine(AnyOf: "weapon.simple", Quantity: quantity),
            Item(Lantern)));

        var result = KitExpansion.Expand(kit, [new("x", "a", new Dictionary<int, Guid> { [2] = Cudgel })], Catalog());

        Assert.Equal(3, result.Problems.Count(p => p.Code == "kit.item-unresolved"));
        Assert.Equal(0, Count(result, Rope));
        Assert.Equal(0, Count(result, Cudgel));
        Assert.Equal(1, Count(result, Lantern));
        Assert.Single(result.Equipment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_contained_quantity_below_one_skips_that_contained_line(int quantity)
    {
        var pack = Pack.Revisions.Single(r => r.ContentId == Satchel) with
        {
            ContentId = C(1398), RevisionId = Guid.NewGuid(),
            Effects = [new ItemEffect { Id = "item", Contains = [new PackLine(new GuideTarget(Rope), quantity), new PackLine(new GuideTarget(Lantern), 2)] }],
        };
        var catalog = new InMemoryContentCatalog(Pack.Sources, [.. Pack.Revisions, pack]);

        var result = KitExpansion.Expand(Kit(Pick("x", new KitLine(Pack: new GuideTarget(C(1398)), Quantity: 3))), [], catalog);

        AssertCode(result, "kit.item-unresolved");
        Assert.Equal(0, Count(result, Rope));
        Assert.Equal(6, Count(result, Lantern));
        Assert.Single(result.Equipment);
    }

    [Fact]
    public void Every_validation_category_is_handled_by_the_kit_category_test()
    {
        foreach (var key in ContentValidator.GuideCategories)
            Assert.True(KitExpansion.CategoryTests.ContainsKey(key), $"KitExpansion does not handle category '{key}'.");
        Assert.Equal(ContentValidator.GuideCategories.Order(StringComparer.Ordinal), KitExpansion.CategoryTests.Keys.Order(StringComparer.Ordinal));
        Assert.False(KitExpansion.InCategory(Pack.Revisions.Single(r => r.ContentId == Rope), "weapon.nonsense"));
    }

    [Fact]
    public void A_new_id_factory_makes_entry_ids_deterministic()
    {
        var counter = 0;
        Guid Next() => C(9000 + counter++);

        var result = KitExpansion.Expand(Kit(Pick("x", Item(Rope), Item(Lantern), Item(Rope))), [], Catalog(), Next);

        Assert.Equal([C(9000), C(9001)], result.Equipment.Select(e => e.Id!.Value));
        Assert.Equal(2, counter); // a merged duplicate takes no new id
        var merged = KitExpansion.Merge(result, KitExpansion.Expand(Kit(Pick("y", Item(Rope))), [], Catalog(), Next));
        Assert.Equal(C(9000), merged.Equipment.Single(e => e.Item.ContentId == Rope).Id);// the first part's id is kept
    }

    [Fact]
    public void A_capped_quantity_is_reported_once_per_item()
    {
        var max = EquipmentEntry.MaxQuantity;
        var result = KitExpansion.Expand(Kit(Pick("x", Item(Rope, max), Item(Rope, max), Item(Rope, 5), Item(Lantern, max))), [], Catalog());

        var capped = Assert.Single(result.Problems, p => p.Code == "kit.quantity-capped");
        Assert.Equal(Rope, capped.Content!.ContentId);
        Assert.Equal(max, Count(result, Rope));
        Assert.Equal(max, Count(result, Lantern));
    }

    [Fact]
    public void Merge_reports_a_capped_quantity_once_per_item()
    {
        var full = KitExpansion.Expand(Kit(Pick("x", Item(Rope, EquipmentEntry.MaxQuantity))), [], Catalog());

        var merged = KitExpansion.Merge(full, full, full);

        Assert.Single(merged.Problems, p => p.Code == "kit.quantity-capped");
    }

    [Fact]
    public void With_duplicate_choices_for_one_pick_the_first_wins()
    {
        var kit = Kit(new KitPick("x", null, [new KitOption("a", [Item(Rope)]), new KitOption("b", [Item(Lantern)])]));

        var result = KitExpansion.Expand(kit, [new("x", "b"), new("x", "a")], Catalog());

        Assert.Equal(1, Count(result, Lantern));
        Assert.Equal(0, Count(result, Rope));
    }

    [Fact]
    public void With_duplicate_option_ids_the_first_wins()
    {
        var kit = Kit(new KitPick("x", null, [new KitOption("a", [Item(Rope)]), new KitOption("a", [Item(Lantern)])]));

        var result = KitExpansion.Expand(kit, [new("x", "a")], Catalog());

        Assert.Equal(1, Count(result, Rope));
        Assert.Equal(0, Count(result, Lantern));
    }

    [Fact]
    public void Merge_sums_items_and_coins_and_joins_problems()
    {
        var one = KitExpansion.Expand(Kit(Pick("x", Item(Rope, 2), Item(Guid.NewGuid()), new KitLine(Coins: new KitCoins { Gp = 10 }))), [], Catalog());
        var two = KitExpansion.Expand(Kit(Pick("y", Item(Rope, 3), Item(Lantern), new KitLine(Coins: new KitCoins { Gp = 5, Cp = 1 }))), [], Catalog());

        var merged = KitExpansion.Merge(one, two);

        Assert.Equal(5, Count(merged, Rope));
        Assert.Equal(2, merged.Equipment.Count);
        Assert.Equal(new Currency(Cp: 1, Gp: 15), merged.Coins);
        Assert.Single(merged.Problems);
    }

    [Fact]
    public void Merge_clamps_like_expand()
    {
        var full = KitExpansion.Expand(Kit(Pick("x", Item(Rope, EquipmentEntry.MaxQuantity), new KitLine(Coins: new KitCoins { Gp = Currency.MaxCoins }))), [], Catalog());

        var merged = KitExpansion.Merge(full, full);

        Assert.Equal(EquipmentEntry.MaxQuantity, Assert.Single(merged.Equipment).Quantity);
        Assert.Equal(Currency.MaxCoins, merged.Coins.Gp);
        AssertCode(merged, "kit.coins-capped");
    }

    [Fact]
    public void Merge_of_nothing_is_empty()
    {
        var merged = KitExpansion.Merge();

        Assert.Empty(merged.Equipment);
        Assert.Empty(merged.Problems);
        Assert.Equal(new Currency(), merged.Coins);
    }
}
