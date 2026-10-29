namespace TomeStack.RulesCore.Tests;

/// <summary>
/// MECH-X item 3 (R49 to R52, R57): item mods apply per equipment entry through a side list that only the attack, Armor Class and
/// modifier collectors read; a mod is a leaf (never active). Original fixtures: <c>fixture-pack-item-mods.json</c> and
/// <c>characters/pm-tinker-l3.json</c> (a level 3 Duelist with two longblades, a scale coat and a round guard, and two bound blades).
/// </summary>
public class ItemModCalculationTests
{
    private static ContentReference R(int n) =>
        new(Guid.Parse($"6a7e00f3-0000-4000-8000-{n:X12}"), Guid.Parse($"6a7e00f3-0000-4000-8000-{0x100 + n:X12}"));

    private static readonly ContentReference Keen = R(0x01);
    private static readonly ContentReference Plating = R(0x02);
    private static readonly ContentReference Lining = R(0x03);
    private static readonly ContentReference Riddle = R(0x04);
    private static readonly ContentReference Longblade = R(0x11);
    private static readonly ContentReference Coat = R(0x12);
    private static readonly ContentReference Guard = R(0x13);
    private static readonly ContentReference Bound = R(0x14);
    private static readonly ContentReference Whisper = R(0x21);

    private static InMemoryContentCatalog Catalog(params ContentRevision[] extra)
    {
        var packs = new[] { Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.ItemModsPack() };
        return new([.. packs.SelectMany(p => p.Sources)], [.. packs.SelectMany(p => p.Revisions), .. extra]);
    }

    private static Character Tinker(params EquipmentEntry[] equipment) =>
        equipment.Length == 0 ? Fixtures.Load("pm-tinker-l3.json") : Fixtures.Load("pm-tinker-l3.json") with { Equipment = equipment };

    private static CharacterSheet Sheet(Character character, params ContentRevision[] extra) => CharacterCalculator.Calculate(character, Catalog(extra));

    private static EquipmentEntry Worn(ContentReference item, bool attuned = false, params ContentReference[] mods) =>
        new(item, Equipped: true, Id: Guid.NewGuid(), Attuned: attuned ? true : null, Mods: mods);

    private static int Count(CharacterSheet sheet, string code) => sheet.Diagnostics.Count(d => d.Code == code);

    private static ContentRevision Item(string name, int n, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"6a7e00f4-0000-4000-8000-{n:X12}"), RevisionId = Guid.Parse($"6a7e00f4-0000-4000-8000-{0x100 + n:X12}"),
        SchemaVersion = 12, Kind = ContentKind.Item, Name = name, RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Guid.Parse("6a7e0000-0000-4000-8000-000000000001")), Status = RevisionStatus.Published, Effects = effects,
    };

    [Fact]
    public void A_mod_adds_to_its_own_copy_only()
    {
        var sheet = Sheet(Tinker());

        var rows = sheet.Attacks!.Where(a => a.Item == Longblade).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal([0, 1], rows.Select(r => r.EntryIndex));
        Assert.Equal(1, rows[1].ToHit - rows[0].ToHit);
        Assert.Equal(("1d8+2", "1d8+3"), (rows[0].Damage, rows[1].Damage));
        Assert.DoesNotContain(rows[0].Trace, t => t.Description.EndsWith("(mod)", StringComparison.Ordinal));
        var toHit = Assert.Single(rows[1].Trace, t => t.Description == "Fixture Keen Rune: +1 to hit (mod)");
        Assert.Equal(("add", 1, rows[1].ToHit, Keen), (toHit.Operation, toHit.Amount, toHit.Result, toHit.Origin.Content));
        Assert.Single(rows[1].Trace, t => t.Description == "Fixture Keen Rune: +1 damage (mod)");
        Assert.Equal(rows[1].ToHit, rows[1].Trace[^1].Result);
    }

    [Fact]
    public void A_host_with_no_modded_entry_keeps_one_row_without_an_entry_index()
    {
        var sheet = Sheet(Tinker(Worn(Longblade), Worn(Longblade)));

        var row = Assert.Single(sheet.Attacks!, a => a.Item == Longblade);
        Assert.Null(row.EntryIndex);
    }

    [Fact]
    public void A_modded_copy_does_not_double_its_items_own_modifiers()
    {
        var plain = Sheet(Tinker(Worn(Longblade))).Field(FieldIds.Initiative).Value;
        var modded = Sheet(Tinker(Worn(Longblade), Worn(Longblade, false, Keen))).Field(FieldIds.Initiative).Value;
        var none = Sheet(Tinker(Worn(Coat))).Field(FieldIds.Initiative).Value;

        Assert.Equal(none + 1, plain); // the Longblade's own initiative +1, once
        Assert.Equal(plain, modded);
    }

    [Fact]
    public void An_armor_mod_raises_armor_class_and_its_nested_modifier_applies()
    {
        var plain = Sheet(Tinker(Worn(Coat)));
        var sheet = Sheet(Tinker(Worn(Coat, false, Plating)));

        var ac = sheet.Field(FieldIds.ArmorClass);
        Assert.Equal(plain.Field(FieldIds.ArmorClass).Value + 1, ac.Value);
        var step = Assert.Single(ac.Trace, t => t.Origin.Content == Plating);
        Assert.Equal(("add", 1, "Fixture Warding Plating"), (step.Operation, step.Amount, step.Origin.ContentName));
        Assert.Contains("Fixture Warding Plating", step.Description, StringComparison.Ordinal);
        Assert.Equal(plain.Field(FieldIds.Save(Ability.Dex)).Value + 1, sheet.Field(FieldIds.Save(Ability.Dex)).Value);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code.StartsWith("item-mod.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_shield_mod_applies()
    {
        // The plating moved onto the round guard (a shield): its bonus joins the shield's own +2.
        var plain = Sheet(Tinker(Worn(Coat), Worn(Guard)));
        var sheet = Sheet(Tinker(Worn(Coat), Worn(Guard, false, Plating)));

        Assert.Equal(plain.Field(FieldIds.ArmorClass).Value + 1, sheet.Field(FieldIds.ArmorClass).Value);
        Assert.Single(sheet.Field(FieldIds.ArmorClass).Trace, t => t.Origin.Content == Plating);
    }

    [Fact]
    public void A_coat_carried_twice_with_one_mod_gives_no_multiple_armor_diagnostic()
    {
        var plain = Sheet(Tinker(Worn(Coat)));
        var sheet = Sheet(Tinker(Worn(Coat), Worn(Coat, false, Plating)));

        Assert.Equal(0, Count(sheet, "equipment.multiple-armor"));
        Assert.Equal(plain.Field(FieldIds.ArmorClass).Value + 1, sheet.Field(FieldIds.ArmorClass).Value);
    }

    [Fact]
    public void A_mod_on_an_unattuned_copy_adds_nothing_while_another_copy_is_attuned()
    {
        var blocked = Sheet(Tinker(Worn(Bound, true), Worn(Bound, false, Lining)));
        var plain = Sheet(Tinker(Worn(Bound, true), Worn(Bound)));

        Assert.Equal(1, Count(blocked, "item-mod.host-not-attuned"));
        Assert.Equal(plain.Field(FieldIds.Initiative).Value, blocked.Field(FieldIds.Initiative).Value);
        Assert.Single(blocked.Attacks!, a => a.Item == Bound);

        var attuned = Sheet(Tinker(Worn(Bound, true), Worn(Bound, true, Lining)));

        Assert.Equal(0, Count(attuned, "item-mod.host-not-attuned"));
        Assert.Equal(plain.Field(FieldIds.Initiative).Value + 1, attuned.Field(FieldIds.Initiative).Value);
        Assert.Equal([0, 1], attuned.Attacks!.Where(a => a.Item == Bound).Select(a => a.EntryIndex));
    }

    [Fact]
    public void The_fixture_character_calculates_with_one_host_row_per_copy()
    {
        // pm-tinker-l3: bound blade A is attuned, B (with the lining) is not, so B adds nothing and the host keeps one row.
        var sheet = Sheet(Tinker());

        Assert.Equal(1, Count(sheet, "item-mod.host-not-attuned"));
        Assert.Equal(0, Count(sheet, "equipment.multiple-armor"));
        var row = Assert.Single(sheet.Attacks!, a => a.Item == Bound);
        Assert.Null(row.EntryIndex);
    }

    [Fact]
    public void A_mod_on_a_carried_entry_adds_nothing()
    {
        var plain = Sheet(Tinker(Worn(Coat)));
        var carried = Sheet(Tinker(Worn(Coat), new EquipmentEntry(Longblade, Equipped: false, Id: Guid.NewGuid(), Mods: [Keen]), new EquipmentEntry(Coat, Equipped: false, Id: Guid.NewGuid(), Mods: [Plating])));

        Assert.Equal(plain.Field(FieldIds.ArmorClass).Value, carried.Field(FieldIds.ArmorClass).Value);
        Assert.DoesNotContain(carried.Attacks!, a => a.Item == Longblade);
        Assert.DoesNotContain(carried.Diagnostics, d => d.Code.StartsWith("item-mod.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mod_with_no_host_effect_says_so()
    {
        // The keen rune (weapon) on a scale coat (armor): nothing to add to.
        var plain = Sheet(Tinker(Worn(Coat)));
        var sheet = Sheet(Tinker(Worn(Coat, false, Keen)));

        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.Code == "item-mod.no-host-effect");
        Assert.Equal(Keen, diagnostic.Content);
        Assert.Equal(plain.Field(FieldIds.ArmorClass).Value, sheet.Field(FieldIds.ArmorClass).Value);
    }

    [Fact]
    public void A_mod_is_a_leaf()
    {
        var sheet = Sheet(Tinker(Worn(Longblade, false, Riddle)));

        Assert.DoesNotContain(sheet.Diagnostics, d => d.EffectId == "riddle-choice");
        Assert.DoesNotContain(sheet.Choices ?? [], c => c.Source == Riddle);
        Assert.DoesNotContain(sheet.Active ?? [], r => r == Riddle || r == Whisper);
        Assert.Equal(1, Count(sheet, "item-mod.effect-ignored"));
        // The rune's own weapon bonus still applies to the copy it is on.
        var row = Assert.Single(sheet.Attacks!, a => a.Item == Longblade);
        Assert.Contains(row.Trace, t => t.Description == "Fixture Riddle Rune: +1 to hit (mod)");
        // Its granted feature's initiative +5 never arrives; the longblade's own +1 does.
        Assert.Equal(Sheet(Tinker(Worn(Longblade))).Field(FieldIds.Initiative).Value, sheet.Field(FieldIds.Initiative).Value);
    }

    [Fact]
    public void Something_that_is_not_a_mod_is_refused()
    {
        var missing = new ContentReference(Guid.Parse("6a7e00f5-0000-4000-8000-000000000001"), Guid.Parse("6a7e00f5-0000-4000-8000-000000000101"));
        var draft = Item("Fixture Draft Rune", 0x31, new ItemModEffect { Id = "draft-rune", AppliesTo = ModTarget.Weapon, Bonus = 1 }) with { Status = RevisionStatus.Draft };
        var sheet = Sheet(Tinker(Worn(Longblade, false, Whisper, missing, draft.Reference), Worn(Longblade, false, Whisper, missing, draft.Reference)), draft);

        var notAMod = Assert.Single(sheet.Diagnostics, d => d.Code == "item-mod.not-a-mod");
        Assert.Equal(Whisper, notAMod.Content);
        Assert.Contains("Fixture Rune Whisper", notAMod.Message, StringComparison.Ordinal);
        Assert.Equal(1, Count(sheet, "content.missing"));
        Assert.Equal(1, Count(sheet, "content.unpublished"));
        // Nothing was added to the attack: two copies, no modded entry, one plain row.
        Assert.Null(Assert.Single(sheet.Attacks!, a => a.Item == Longblade).EntryIndex);
    }

    [Fact]
    public void The_same_mod_on_two_entries_applies_twice()
    {
        var plain = Sheet(Tinker(Worn(Longblade), Worn(Longblade)));
        var sheet = Sheet(Tinker(Worn(Longblade, false, Lining), Worn(Longblade, false, Lining)));

        Assert.Equal(plain.Field(FieldIds.Initiative).Value + 2, sheet.Field(FieldIds.Initiative).Value);
        Assert.Equal(2, sheet.Attacks!.Count(a => a.Item == Longblade));
    }

    [Fact]
    public void Item_uses_list_mods_and_hosts()
    {
        var potion = Item("Fixture Plain Tonic", 0x32, new ModifierEffect { Id = "tonic", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" });
        var sheet = Sheet(Tinker(Worn(Longblade), Worn(Longblade, false, Keen), Worn(Coat, false, Plating), new EquipmentEntry(potion.Reference, Equipped: true)), potion);

        var uses = sheet.ItemUses!;
        Assert.Equal(3, uses.Count); // the tonic has no host effect, no mods, no attunement and no charges: no row
        Assert.Equal(["weapon"], uses[0].Hosts);
        Assert.Empty(uses[0].Mods);
        var keen = Assert.Single(uses[1].Mods);
        Assert.Equal(("Fixture Keen Rune", ModTarget.Weapon, 1, Keen), (keen.Name, keen.AppliesTo, keen.Bonus, keen.Reference));
        Assert.Equal(["armor"], uses[2].Hosts);
        Assert.Equal(ModTarget.Armor, Assert.Single(uses[2].Mods).AppliesTo);
        Assert.All(uses, u => Assert.False(u.RequiresAttunement));
    }

    [Fact]
    public void A_mod_the_catalog_cannot_find_is_still_listed_so_it_can_be_removed()
    {
        var missing = new ContentReference(Guid.Parse("6a7e00f5-0000-4000-8000-000000000002"), Guid.Parse("6a7e00f5-0000-4000-8000-000000000102"));
        var sheet = Sheet(Tinker(Worn(Longblade, false, missing)));

        var mod = Assert.Single(Assert.Single(sheet.ItemUses!).Mods);
        Assert.Equal((missing, "(not available)"), (mod.Reference, mod.Name));
        Assert.Equal(1, Count(sheet, "content.missing"));
    }
}
