using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Wave 3 item 25 (Q13, Q19; R158 to R160, R171): an equipped v10 item that needs attunement applies only while an entry of it is
/// attuned; charges are per equipment entry, shown for carried and equipped entries alike; a rest returns them by the item's
/// recovery rule. Fixture items only, side by side per rules family.
/// </summary>
public class ItemAttunementTests
{
    private static readonly Guid WandContent = Guid.Parse("5fa5a000-0000-4000-8000-0000000000a1");
    private static readonly Guid PlainContent = Guid.Parse("5fa5a000-0000-4000-8000-0000000000a2");

    private static ContentRevision Item(string family, Guid contentId, int schema, string name, params Effect[] effects) => new()
    {
        ContentId = contentId,
        RevisionId = Guid.Parse(contentId.ToString("D").Replace("5fa5a000", "5fa5e000", StringComparison.Ordinal)),
        SchemaVersion = schema, Kind = ContentKind.Item, Name = name, RulesFamilies = [family], Status = RevisionStatus.Published,
        Provenance = new(family == RulesFamilies.Srd51 ? Fixtures.Source2014 : Fixtures.Source2024, new(3)),
        Effects = effects,
    };

    private static ModifierEffect Stealth() => new() { Id = "sneak", Operation = ModifierOperation.Bonus, Target = FieldIds.Skill("stealth"), Value = "2" };

    private static ItemEffect Use(bool? attune, int? charges = null, RestPeriod? period = null, string? regain = null) => new()
    {
        Id = "use",
        RequiresAttunement = attune,
        Charges = charges is null ? null : new() { Max = charges.Value, Recovery = period is null ? null : new() { Period = period.Value, Regain = regain } },
    };

    /// <summary>"Fixture wand": needs attunement, 7 charges that come back on a long rest by 1d6+1, +2 to Stealth.</summary>
    internal static ContentRevision Wand(string family, RestPeriod? period = RestPeriod.LongRest, string? regain = "1d6+1", int charges = 7, bool attune = true) =>
        Item(family, WandContent, 10, "Fixture wand", Stealth(), Use(attune ? true : null, charges, period, regain));

    internal static ContentReference Reference(ContentRevision revision) => revision.Reference;

    private static Character Hero(string family, params EquipmentEntry[] equipment) =>
        Fixtures.Load(family == RulesFamilies.Srd51 ? "srd51-ash-m1.json" : "srd521-ash-m1.json") with { Equipment = equipment };

    private static CharacterSheet Sheet(Character character, params ContentRevision[] items) =>
        CharacterCalculator.Calculate(character, ToggleAndCostTests.CatalogWith(items));

    private static Guid Id(int n) => Guid.Parse($"5fa5b000-0000-4000-8000-{n:D12}");

    public static TheoryData<string> Families => new(RulesFamilies.Srd51, RulesFamilies.Srd521);

    [Theory]
    [MemberData(nameof(Families))]
    public void An_equipped_unattuned_item_that_needs_attunement_applies_nothing_and_the_sheet_says_not_attuned(string family)
    {
        var wand = Wand(family);
        var before = Sheet(Hero(family)).Field(FieldIds.Skill("stealth")).Value;

        var sheet = Sheet(Hero(family, new EquipmentEntry(wand.Reference, Equipped: true, Id: Id(1))), wand);

        Assert.Equal(before, sheet.Field(FieldIds.Skill("stealth")).Value);
        var note = Assert.Single(sheet.Diagnostics, d => d.Code == "item.not-attuned");
        Assert.Equal(wand.Reference, note.Content);
        Assert.DoesNotContain(wand.Reference, sheet.Active!);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void An_equipped_attuned_item_applies_its_effects(string family)
    {
        var wand = Wand(family);
        var before = Sheet(Hero(family)).Field(FieldIds.Skill("stealth")).Value;

        var sheet = Sheet(Hero(family, new EquipmentEntry(wand.Reference, Equipped: true, Id: Id(1), Attuned: true)), wand);

        Assert.Equal(before + 2, sheet.Field(FieldIds.Skill("stealth")).Value);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "item.not-attuned");
        Assert.Contains(wand.Reference, sheet.Active!);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void A_pinned_item_that_needs_attunement_is_gated_like_an_equipped_one_and_the_message_quotes_no_name(string family)
    {
        var wand = Wand(family);
        var hero = Hero(family);
        var before = Sheet(hero).Field(FieldIds.Skill("stealth")).Value;
        var pinned = hero with { Pins = [.. hero.Pins, wand.Reference] };

        // Pinned alone, not attuned: nothing applies.
        var bare = Sheet(pinned, wand);
        Assert.Equal(before, bare.Field(FieldIds.Skill("stealth")).Value);
        var note = Assert.Single(bare.Diagnostics, d => d.Code == "item.not-attuned");
        Assert.Equal(wand.Reference, note.Content);
        Assert.DoesNotContain("Fixture wand", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(wand.Reference, bare.Active!);

        // Pinned and equipped, not attuned: still nothing, and the note appears once.
        var both = Sheet(pinned with { Equipment = [new EquipmentEntry(wand.Reference, Equipped: true, Id: Id(1))] }, wand);
        Assert.Equal(before, both.Field(FieldIds.Skill("stealth")).Value);
        Assert.Single(both.Diagnostics, d => d.Code == "item.not-attuned");

        // Pinned with an attuned entry: it applies.
        var attuned = Sheet(pinned with { Equipment = [new EquipmentEntry(wand.Reference, Equipped: true, Id: Id(1), Attuned: true)] }, wand);
        Assert.Equal(before + 2, attuned.Field(FieldIds.Skill("stealth")).Value);
        Assert.DoesNotContain(attuned.Diagnostics, d => d.Code == "item.not-attuned");
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void An_attuned_item_that_is_not_equipped_applies_nothing_and_is_not_reported_as_unattuned(string family)
    {
        var wand = Wand(family);
        var before = Sheet(Hero(family)).Field(FieldIds.Skill("stealth")).Value;

        var sheet = Sheet(Hero(family, new EquipmentEntry(wand.Reference, Equipped: false, Id: Id(1), Attuned: true)), wand);

        Assert.Equal(before, sheet.Field(FieldIds.Skill("stealth")).Value);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "item.not-attuned");
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void An_item_that_does_not_need_attunement_applies_when_equipped_whatever_is_recorded(string family)
    {
        var wand = Wand(family, attune: false);
        var before = Sheet(Hero(family)).Field(FieldIds.Skill("stealth")).Value;

        var sheet = Sheet(Hero(family, new EquipmentEntry(wand.Reference, Equipped: true, Id: Id(1))), wand);

        Assert.Equal(before + 2, sheet.Field(FieldIds.Skill("stealth")).Value);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "item.not-attuned");
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void A_v9_item_revision_applies_when_equipped_exactly_as_before_and_its_item_effect_stays_unknown(string family)
    {
        // A revision below v10 has no typed item effect (Task 1): the key is an unknown effect, so there is no gate and no charges.
        var json = """{"contentId":"CONTENT","revisionId":"5fa5e000-0000-4000-8000-0000000000a2","schemaVersion":9,"kind":"item","name":"Fixture old wand","rulesFamilies":["FAMILY"],"provenance":{"sourceId":"SOURCE","page":{"start":3}},"status":"published","effects":[{"id":"sneak","type":"modifier","operation":"bonus","target":"skill.stealth","value":"2"},{"id":"use","type":"item","requiresAttunement":true,"charges":{"max":3}}]}"""
            .Replace("CONTENT", PlainContent.ToString("D"), StringComparison.Ordinal)
            .Replace("FAMILY", family, StringComparison.Ordinal)
            .Replace("SOURCE", (family == RulesFamilies.Srd51 ? Fixtures.Source2014 : Fixtures.Source2024).ToString("D"), StringComparison.Ordinal);
        var old = JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Options)!;
        Assert.IsType<UnknownEffect>(old.Effects[1]);
        var before = Sheet(Hero(family)).Field(FieldIds.Skill("stealth")).Value;

        var sheet = Sheet(Hero(family, new EquipmentEntry(old.Reference, Equipped: true)), old);

        Assert.Equal(before + 2, sheet.Field(FieldIds.Skill("stealth")).Value);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "item.not-attuned");
        Assert.Null(sheet.ItemUses);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void An_item_equipped_twice_applies_once_and_one_attuned_entry_is_enough(string family)
    {
        var wand = Wand(family);
        var before = Sheet(Hero(family)).Field(FieldIds.Skill("stealth")).Value;

        var both = Sheet(Hero(family, new EquipmentEntry(wand.Reference, true, Id: Id(1), Attuned: true), new EquipmentEntry(wand.Reference, true, Id: Id(2), Attuned: true)), wand);
        var one = Sheet(Hero(family, new EquipmentEntry(wand.Reference, true, Id: Id(1)), new EquipmentEntry(wand.Reference, true, Id: Id(2), Attuned: true)), wand);
        var none = Sheet(Hero(family, new EquipmentEntry(wand.Reference, true, Id: Id(1)), new EquipmentEntry(wand.Reference, true, Id: Id(2))), wand);

        Assert.Equal(before + 2, both.Field(FieldIds.Skill("stealth")).Value);
        Assert.Equal(before + 2, one.Field(FieldIds.Skill("stealth")).Value);
        Assert.Equal(before, none.Field(FieldIds.Skill("stealth")).Value);
        Assert.Single(none.Diagnostics, d => d.Code == "item.not-attuned");
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void Review_focus_4_two_copies_of_one_wand_have_separate_charges_and_both_are_listed(string family)
    {
        var wand = Wand(family);
        var sheet = Sheet(Hero(family,
            new EquipmentEntry(wand.Reference, Equipped: true, Id: Id(1), Attuned: true, ChargesSpent: 3),
            new EquipmentEntry(wand.Reference, Equipped: false, Id: Id(2), ChargesSpent: 1)), wand);

        var uses = sheet.ItemUses!;
        Assert.Equal(2, uses.Count);
        var (equipped, carried) = (uses[0], uses[1]);
        Assert.Equal((Id(1), true, true, true, 7, 3, 4), (equipped.EntryId, equipped.Equipped, equipped.RequiresAttunement, equipped.Attuned, equipped.ChargesMax, equipped.ChargesSpent, equipped.ChargesLeft));
        Assert.Equal((Id(2), false, true, false, 7, 1, 6), (carried.EntryId, carried.Equipped, carried.RequiresAttunement, carried.Attuned, carried.ChargesMax, carried.ChargesSpent, carried.ChargesLeft));
        Assert.Equal(("Fixture wand", Id(1).ToString("D"), 0), (equipped.Name, equipped.EntryKey, equipped.EntryIndex));
        Assert.Equal(Id(2).ToString("D"), carried.EntryKey);
        Assert.Equal(new ItemChargeRecoveryInfo(RestPeriod.LongRest, "1d6+1"), equipped.Recovery);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void An_entry_without_an_id_is_keyed_by_its_position_and_the_spent_count_never_goes_below_zero_left(string family)
    {
        var wand = Wand(family);
        var sheet = Sheet(Hero(family, new EquipmentEntry(PlainRef(family), true), new EquipmentEntry(wand.Reference, false, ChargesSpent: 50)), wand);

        var use = Assert.Single(sheet.ItemUses!);
        Assert.Equal(("#1", null, 1, 0), (use.EntryKey, use.EntryId, use.EntryIndex, use.ChargesLeft));
        Assert.Equal(50, use.ChargesSpent);
    }

    private static ContentReference PlainRef(string family) => Fixtures.Load(family == RulesFamilies.Srd51 ? "srd51-ash-m1.json" : "srd521-ash-m1.json").Pins[0];

    [Theory]
    [MemberData(nameof(Families))]
    public void Items_without_attunement_or_charges_are_not_listed_and_nothing_is_written_for_them(string family)
    {
        var plain = Item(family, PlainContent, 10, "Fixture trinket", Stealth(), new ItemEffect { Id = "use" });
        var sheet = Sheet(Hero(family, new EquipmentEntry(plain.Reference, true)), plain);

        Assert.Null(sheet.ItemUses);
        Assert.DoesNotContain("itemUses", JsonSerializer.Serialize(sheet, RulesJson.Options), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void The_mechanics_inventory_no_longer_lists_the_item_effect_as_unsupported(string family)
    {
        var wand = Wand(family);
        var sheet = Sheet(Hero(family, new EquipmentEntry(wand.Reference, true, Id: Id(1), Attuned: true)), wand);

        var report = MechanicsInventory.Of(sheet, new HashSet<Guid> { wand.Provenance.SourceId });

        Assert.Contains(report.Mechanics, m => m.EffectType == ItemEffect.TypeName);
        Assert.DoesNotContain(report.Unsupported, m => m.EffectType == ItemEffect.TypeName);
    }

    // ---- rests ---------------------------------------------------------------------------------------------------------------

    private static RestChange? ChargeChange(RestPlan plan, Guid entry) => plan.Changes.SingleOrDefault(c => c.Kind == RestChangeKind.Charges && c.ResourceId == entry.ToString("D"));

    private static (Character Character, CharacterSheet Sheet) Spent(string family, ContentRevision wand, int spent1, int spent2)
    {
        var character = Hero(family,
            new EquipmentEntry(wand.Reference, true, Id: Id(1), Attuned: true, ChargesSpent: spent1),
            new EquipmentEntry(wand.Reference, false, Id: Id(2), ChargesSpent: spent2));
        return (character, Sheet(character, wand));
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void A_long_rest_waits_for_the_regain_roll_then_returns_that_many_capped_at_the_spent_count(string family)
    {
        var wand = Wand(family);
        var (character, sheet) = Spent(family, wand, 5, 1);

        var waiting = RestPlanner.LongRest(character, sheet);
        var ask = ChargeChange(waiting, Id(1))!;
        Assert.Equal("1d6+1", ask.Dice);
        Assert.Null(ask.SpentAfter);
        Assert.Equal((2, 2), (ask.From, ask.To)); // 7 - 5 left; nothing is known until the roll is in

        var rolled = RestPlanner.LongRest(character, sheet, [new(Id(1).ToString("D"), 4), new(Id(2).ToString("D"), 6)]);
        var first = ChargeChange(rolled, Id(1))!;
        Assert.Equal((2, 6, 1, 4), (first.From, first.To, first.SpentAfter, first.Amount));
        var second = ChargeChange(rolled, Id(2))!;
        Assert.Equal((6, 7, 0, 1), (second.From, second.To, second.SpentAfter, second.Amount)); // rolled 6, only 1 was spent
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void A_null_regain_brings_every_charge_back_on_that_rest_and_a_null_recovery_none(string family)
    {
        var all = Wand(family, RestPeriod.LongRest, null);
        var (character, sheet) = Spent(family, all, 5, 0);
        var change = ChargeChange(RestPlanner.LongRest(character, sheet), Id(1))!;
        Assert.Equal((2, 7, 0, null), (change.From, change.To, change.SpentAfter, change.Dice));

        var none = Wand(family, null);
        var (character2, sheet2) = Spent(family, none, 5, 0);
        Assert.Null(ChargeChange(RestPlanner.LongRest(character2, sheet2), Id(1)));
        Assert.Null(ChargeChange(RestPlanner.ShortRest(character2, sheet2, []), Id(1)));
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void An_unreadable_regain_is_a_manual_step_that_quotes_no_item_name(string family)
    {
        var wand = Wand(family, RestPeriod.LongRest, "not dice");
        var (character, sheet) = Spent(family, wand, 5, 0);

        var plan = RestPlanner.LongRest(character, sheet);

        var step = Assert.Single(plan.Manual, m => m.Code == "rest.recover-by-hand");
        Assert.Equal(wand.Reference, step.Content);
        Assert.DoesNotContain("Fixture wand", step.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not dice", step.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void Only_the_rest_of_the_items_period_returns_charges(string family)
    {
        var wand = Wand(family, RestPeriod.ShortRest, null);
        var (character, sheet) = Spent(family, wand, 5, 0);

        Assert.Null(ChargeChange(RestPlanner.LongRest(character, sheet), Id(1)));
        Assert.NotNull(ChargeChange(RestPlanner.ShortRest(character, sheet, []), Id(1)));
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void A_charge_change_is_applied_to_the_equipment_unless_unticked_and_a_roll_not_yet_in_changes_nothing(string family)
    {
        var wand = Wand(family);
        var (character, sheet) = Spent(family, wand, 5, 3);
        var key1 = Id(1).ToString("D");
        var key2 = Id(2).ToString("D");
        var plan = RestPlanner.LongRest(character, sheet, [new(key1, 3)]);

        var applied = RestPlanner.ApplyEquipment(character.Equipment, plan, new HashSet<string>());
        Assert.Equal(2, applied[0].ChargesSpent); // 5 - 3
        Assert.Equal(3, applied[1].ChargesSpent); // no roll yet for the second entry: unchanged
        Assert.Equal((true, true, Id(1)), (applied[0].Equipped, applied[0].Attuned, applied[0].Id)); // nothing else moves

        var skipped = RestPlanner.ApplyEquipment(character.Equipment, plan, new HashSet<string> { $"charges:{key1}" });
        Assert.Equal(5, skipped[0].ChargesSpent);

        var all = Wand(family, RestPeriod.LongRest, null);
        var (c2, s2) = Spent(family, all, 5, 3);
        var back = RestPlanner.ApplyEquipment(c2.Equipment, RestPlanner.LongRest(c2, s2), new HashSet<string>());
        Assert.Null(back[0].ChargesSpent);
        Assert.Null(back[1].ChargesSpent);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void An_entry_without_an_id_gets_one_when_a_rest_changes_it(string family)
    {
        var wand = Wand(family, RestPeriod.LongRest, null);
        var character = Hero(family, new EquipmentEntry(wand.Reference, false, ChargesSpent: 4));
        var plan = RestPlanner.LongRest(character, Sheet(character, wand));

        Assert.Equal("#0", Assert.Single(plan.Changes, c => c.Kind == RestChangeKind.Charges).ResourceId);
        var applied = Assert.Single(RestPlanner.ApplyEquipment(character.Equipment, plan, new HashSet<string>()));
        Assert.NotNull(applied.Id);
        Assert.Null(applied.ChargesSpent);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void Charge_rolls_are_checked_against_the_entries_and_the_dice(string family)
    {
        var wand = Wand(family);
        var (_, sheet) = Spent(family, wand, 5, 0);
        var key1 = Id(1).ToString("D");
        var key2 = Id(2).ToString("D");

        Assert.Empty(RestPlanner.CheckChargeRolls(sheet, RestPeriod.LongRest, [new(key1, 2)]));
        Assert.Empty(RestPlanner.CheckChargeRolls(sheet, RestPeriod.LongRest, [new(key1, 7)]));
        Assert.Equal("rest.charge-roll-invalid", Assert.Single(RestPlanner.CheckChargeRolls(sheet, RestPeriod.LongRest, [new(key1, 1)])).Code); // 1d6+1 shows 2 to 7
        Assert.Equal("rest.charge-roll-invalid", Assert.Single(RestPlanner.CheckChargeRolls(sheet, RestPeriod.LongRest, [new(key1, 8)])).Code);
        Assert.Equal("rest.charge-roll-unknown", Assert.Single(RestPlanner.CheckChargeRolls(sheet, RestPeriod.LongRest, [new("nope", 3)])).Code);
        Assert.Equal("rest.charge-roll-unknown", Assert.Single(RestPlanner.CheckChargeRolls(sheet, RestPeriod.ShortRest, [new(key1, 3)])).Code); // wrong rest
        Assert.Equal("rest.charge-roll-unknown", Assert.Single(RestPlanner.CheckChargeRolls(sheet, RestPeriod.LongRest, [new(key2, 3)])).Code); // nothing spent on it
        Assert.Equal("rest.charge-roll-unknown", RestPlanner.CheckChargeRolls(sheet, RestPeriod.LongRest, [new(key1, 3), new(key1, 4)]).Single().Code); // twice
    }

    [Fact]
    public void Validation_refuses_a_repeated_item_unless_every_copy_has_an_id()
    {
        var spark = Fixtures.Spark;
        var plain = Fixtures.Srd521Character();

        Assert.Contains((plain with { Equipment = [new(spark), new(spark)] }).Validate(), p => p.Code == "character.equipment-duplicate");
        Assert.Contains((plain with { Equipment = [new(spark, Id: Id(1)), new(spark)] }).Validate(), p => p.Code == "character.equipment-duplicate");
        Assert.DoesNotContain((plain with { Equipment = [new(spark, Id: Id(1)), new(spark, Id: Id(2))] }).Validate(), p => p.Code == "character.equipment-duplicate");
    }

    [Fact]
    public void A_dice_expression_knows_the_lowest_and_highest_total_it_can_show()
    {
        foreach (var (source, min, max) in new[] { ("1d6+1", 2, 7), ("2d4", 2, 8), ("1d4-1", 0, 3), ("1d8+1d6+2", 4, 16), ("2d6-1d4", -2, 11) })
        {
            Assert.True(DiceExpression.TryParse(source, out var expression, out _));
            Assert.Equal((min, max), (expression!.Minimum, expression.Maximum));
        }
    }
}
