using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Wave 3 item 25 (Q13, Q19; R158 to R160, R171): per-entry charges and attunement through the play, save and rest paths. Fixture items
/// only ("Fixture wand", "Fixture trinket", "Fixture plain cup"), the same in both rules families.
/// </summary>
public class ItemChargesTests
{
    private static readonly Guid WandContent = Guid.Parse("5fa5a000-0000-4000-8000-0000000000b1");
    private static readonly Guid TrinketContent = Guid.Parse("5fa5a000-0000-4000-8000-0000000000b2");
    private static readonly Guid CupContent = Guid.Parse("5fa5a000-0000-4000-8000-0000000000b3");
    private static readonly Guid RingContent = Guid.Parse("5fa5a000-0000-4000-8000-0000000000b4");

    private static ContentRevision Item(string family, Guid contentId, string name, params Effect[] effects) => new()
    {
        ContentId = contentId,
        RevisionId = Guid.Parse(contentId.ToString("D").Replace("5fa5a000", "5fa5e000", StringComparison.Ordinal) + (family == RulesFamilies.Srd51 ? "" : "")),
        SchemaVersion = 10, Kind = ContentKind.Item, Name = name, RulesFamilies = [family], Status = RevisionStatus.Published,
        Provenance = new(family == RulesFamilies.Srd51 ? Guid.Parse("5f0d5100-0000-4000-8000-000000000001") : Guid.Parse("5f0d5210-0000-4000-8000-000000000001"), new(3)),
        Effects = effects,
    };

    private sealed record Setup(TempApp Temp, Guid Id, ContentReference Wand, ContentReference Trinket, ContentReference Cup, ContentReference Ring);

    /// <summary>
    /// A level 3 caster plus four installed items: a wand (attunement, 7 charges, back on a long rest by 1d6+1, +2 Stealth), a trinket
    /// (an item effect with neither), a plain cup (no item effect) and a ring (attunement only).
    /// </summary>
    private static Setup Start(string family, string? regain = "1d6+1", RestPeriod? period = RestPeriod.LongRest, TimeProvider? time = null, params EquipmentEntry[] equipment)
    {
        var temp = new TempApp(time: time);
        ContentReference Install(ContentRevision revision)
        {
            temp.App.Store.AddRevision(revision);
            return revision.Reference;
        }
        var wand = Install(Item(family, WandContent, "Fixture wand",
            new ModifierEffect { Id = "sneak", Operation = ModifierOperation.Bonus, Target = FieldIds.Skill("stealth"), Value = "2" },
            new ItemEffect { Id = "use", RequiresAttunement = true, Charges = new() { Max = 7, Recovery = period is null ? null : new() { Period = period.Value, Regain = regain } } }));
        var trinket = Install(Item(family, TrinketContent, "Fixture trinket", new ItemEffect { Id = "use" }));
        var cup = Install(Item(family, CupContent, "Fixture plain cup"));
        var ring = Install(Item(family, RingContent, "Fixture ring", new ItemEffect { Id = "use", RequiresAttunement = true }));
        var arcanist = new ContentReference(Guid.Parse("5f5dc000-0000-4000-8000-000000000001"), Guid.Parse("5f5de000-0000-4000-8000-000000000001"));
        var character = new Character
        {
            Id = Guid.NewGuid(), Name = "Test Wielder", RulesFamily = family, Level = 3, Classes = [new(arcanist, 3)],
            BaseAbilities = new(8, 14, 12, 16, 10, 10), Equipment = equipment.Length == 0 ? [] : [.. equipment.Select(e => e.Item == default! ? e : e)],
        };
        return new(temp, temp.App.SaveCharacter(character).Character.Id, wand, trinket, cup, ring);
    }

    private static CharacterView Save(Setup s, params EquipmentEntry[] equipment) =>
        s.Temp.App.SaveCharacter(s.Temp.App.GetCharacter(s.Id).Character with { Equipment = equipment });

    private static CharacterView Get(Setup s) => s.Temp.App.GetCharacter(s.Id);

    private static CharacterView Spend(Setup s, Guid entry, int amount = 1) => s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true, Amount: amount, EntryId: entry));

    private static CharacterView Regain(Setup s, Guid entry, int amount = 1) => s.Temp.App.Play(new(s.Id, PlayActionKind.RegainCharge, Confirm: true, Amount: amount, EntryId: entry));

    private static string CodeOf(Action act) => Assert.Throws<AppValidationException>(act).Problems[0].Code;

    private static ItemUseValue Use(CharacterView view, Guid entry) => view.Sheet.ItemUses!.Single(u => u.EntryId == entry);

    private static Guid Id(int n) => Guid.Parse($"5fa5b000-0000-4000-8000-{n:D12}");

    private static JsonElement Dispatch(Setup s, string command, object payload) =>
        JsonDocument.Parse(new CommandDispatcher(s.Temp.App).Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;

    // ---- R158 and R159: ids and second copies, through the save path --------------------------------------------------------

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R158_an_entry_gets_an_id_when_created_only_for_an_item_with_charges_or_attunement(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;

        var view = Save(s, new EquipmentEntry(s.Wand), new EquipmentEntry(s.Ring, Equipped: true), new EquipmentEntry(s.Trinket), new EquipmentEntry(s.Cup));

        var equipment = view.Character.Equipment;
        Assert.NotNull(equipment[0].Id);
        Assert.NotNull(equipment[1].Id);
        Assert.NotEqual(equipment[0].Id, equipment[1].Id);
        Assert.Null(equipment[2].Id); // an item effect with neither attunement nor charges
        Assert.Null(equipment[3].Id); // no item effect at all
        // The plain entries write no v9 field (Q20).
        var json = TempApp.Json(equipment[3]);
        Assert.DoesNotContain("\"id\"", json, StringComparison.Ordinal);
        Assert.Contains(Use(view, equipment[0].Id!.Value).EntryKey, TempApp.Json(view.Sheet), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R158_an_id_once_given_is_kept_by_later_saves_and_a_supplied_id_is_honoured(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var first = Save(s, new EquipmentEntry(s.Wand), new EquipmentEntry(s.Ring, Id: Id(9)));

        var again = Save(s, first.Character.Equipment[0] with { Equipped = true }, first.Character.Equipment[1]);

        Assert.Equal(first.Character.Equipment[0].Id, again.Character.Equipment[0].Id);
        Assert.Equal(Id(9), again.Character.Equipment[1].Id);
        Assert.True(again.Character.Equipment[0].Equipped);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R159_a_second_copy_is_allowed_only_for_charges_or_attunement_items(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;

        var view = Save(s, new EquipmentEntry(s.Wand), new EquipmentEntry(s.Wand), new EquipmentEntry(s.Ring), new EquipmentEntry(s.Ring));

        Assert.Equal(4, view.Character.Equipment.Count);
        Assert.Equal(4, view.Character.Equipment.Select(e => e.Id).Distinct().Count());
        Assert.Equal("character.equipment-duplicate", CodeOf(() => Save(s, new EquipmentEntry(s.Cup), new EquipmentEntry(s.Cup))));
        Assert.Equal("character.equipment-duplicate", CodeOf(() => Save(s, new EquipmentEntry(s.Trinket), new EquipmentEntry(s.Trinket))));
        // An id supplied by the caller does not make a plain item repeatable.
        Assert.Equal("character.equipment-duplicate", CodeOf(() => Save(s, new EquipmentEntry(s.Cup, Id: Id(1)), new EquipmentEntry(s.Cup, Id: Id(2)))));
        Assert.Equal(2, Get(s).Character.Equipment.Count(e => e.Item == s.Wand)); // the refusals saved nothing
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R159_the_item_applies_once_when_two_copies_are_equipped_and_attunement_gates_it(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var bare = Get(s).Sheet.Field(FieldIds.Skill("stealth")).Value;

        var view = Save(s, new EquipmentEntry(s.Wand, Equipped: true), new EquipmentEntry(s.Wand, Equipped: true));
        Assert.Equal(bare, view.Sheet.Field(FieldIds.Skill("stealth")).Value);
        Assert.Single(view.Sheet.Diagnostics, d => d.Code == "item.not-attuned");

        view = Save(s, view.Character.Equipment[0] with { Attuned = true }, view.Character.Equipment[1]);
        Assert.Equal(bare + 2, view.Sheet.Field(FieldIds.Skill("stealth")).Value);
        Assert.DoesNotContain(view.Sheet.Diagnostics, d => d.Code == "item.not-attuned");
    }

    // ---- R160: a save never reverts a spend --------------------------------------------------------------------------------

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R160_a_save_carrying_an_older_charges_spent_keeps_the_stored_value(string family)
    {
        // R182: a save from before a spend is now refused outright (see the R182 tests); this is the current-copy case: the payload names
        // the stored version but carries older spent counts, and the stored counts win.
        var clock = new MutableTime(TempApp.Now);
        var s = Start(family, time: clock);
        using var _ = s.Temp;
        Tick(clock);
        var first = Save(s, new EquipmentEntry(s.Wand), new EquipmentEntry(s.Wand)).Character;
        var (a, b) = (first.Equipment[0].Id!.Value, first.Equipment[1].Id!.Value);
        Tick(clock);
        Spend(s, a, 2);
        Tick(clock);
        Spend(s, b, 1);
        Tick(clock);
        var stale = Get(s).Character with { Equipment = first.Equipment }; // the current version stamp with older spent counts (none)

        var saved = s.Temp.App.SaveCharacter(stale with { Equipment = [stale.Equipment[0] with { Equipped = true, Attuned = true, Quantity = 2 }, stale.Equipment[1] with { ChargesSpent = 0 }] });

        Assert.Equal((2, true, true, 2), (saved.Character.Equipment[0].ChargesSpent, saved.Character.Equipment[0].Equipped, saved.Character.Equipment[0].Attuned == true, saved.Character.Equipment[0].Quantity));
        Assert.Equal(1, saved.Character.Equipment[1].ChargesSpent);
        Assert.Equal(7 - 2, Use(saved, a).ChargesLeft);
        Assert.Equal(7 - 1, Use(saved, b).ChargesLeft);

        // A stale save that says more was spent does not win either: the stored value is the truth.
        var higher = s.Temp.App.SaveCharacter(saved.Character with { Equipment = [saved.Character.Equipment[0] with { ChargesSpent = 6 }, saved.Character.Equipment[1]] });
        Assert.Equal(2, higher.Character.Equipment[0].ChargesSpent);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R160_a_save_may_add_and_remove_entries_and_a_new_entry_keeps_what_it_carries(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var first = Save(s, new EquipmentEntry(s.Wand), new EquipmentEntry(s.Ring));
        var wandId = first.Character.Equipment[0].Id!.Value;
        Spend(s, wandId);

        var added = Save(s, Get(s).Character.Equipment[0], new EquipmentEntry(s.Wand, Id: Id(5), ChargesSpent: 3)); // the ring is removed, a second wand added

        Assert.Equal(2, added.Character.Equipment.Count);
        Assert.DoesNotContain(added.Character.Equipment, e => e.Item == s.Ring);
        Assert.Equal(1, added.Character.Equipment[0].ChargesSpent);
        Assert.Equal(3, added.Character.Equipment[1].ChargesSpent);
    }

    // ---- play: spend and regain per entry ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void Review_focus_4_two_copies_of_one_wand_have_separate_counts_and_both_are_listed(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var view = Save(s, new EquipmentEntry(s.Wand, Equipped: true, Attuned: true), new EquipmentEntry(s.Wand));
        var (equipped, carried) = (view.Character.Equipment[0].Id!.Value, view.Character.Equipment[1].Id!.Value);

        view = Spend(s, equipped, 3);
        view = Spend(s, carried);

        Assert.Equal(2, view.Sheet.ItemUses!.Count);
        Assert.Equal((7, 3, 4), (Use(view, equipped).ChargesMax!.Value, Use(view, equipped).ChargesSpent, Use(view, equipped).ChargesLeft!.Value));
        Assert.Equal((7, 1, 6), (Use(view, carried).ChargesMax!.Value, Use(view, carried).ChargesSpent, Use(view, carried).ChargesLeft!.Value));
        Assert.Equal((3, 1), (view.Character.Equipment[0].ChargesSpent, view.Character.Equipment[1].ChargesSpent));

        // Regain on one copy leaves the other alone; the inverse of a spend is the regain of the same amount (D23).
        view = Regain(s, equipped, 3);
        Assert.Null(view.Character.Equipment[0].ChargesSpent);
        Assert.Equal(1, view.Character.Equipment[1].ChargesSpent);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void Spend_and_regain_are_refused_at_nothing_left_and_when_full_and_change_nothing(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var view = Save(s, new EquipmentEntry(s.Wand));
        var id = view.Character.Equipment[0].Id!.Value;
        var before = TempApp.Json(Get(s).Character);

        Assert.Equal("play.charges-full", CodeOf(() => Regain(s, id)));
        Assert.Equal("play.charges-none-left", CodeOf(() => Spend(s, id, 8)));
        Spend(s, id, 7);
        Assert.Equal("play.charges-none-left", CodeOf(() => Spend(s, id)));
        Assert.Equal("play.charges-full", CodeOf(() => Regain(s, id, 8))); // more than were spent
        Assert.Equal("play.amount-out-of-range", CodeOf(() => Spend(s, id, 0)));
        Assert.Equal(7, Get(s).Character.Equipment[0].ChargesSpent);

        Regain(s, id, 7);
        Assert.Equal(before, TempApp.Json(Get(s).Character));
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void An_unknown_entry_or_an_entry_without_charges_is_refused_and_nothing_is_written(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var view = Save(s, new EquipmentEntry(s.Wand), new EquipmentEntry(s.Ring), new EquipmentEntry(s.Trinket), new EquipmentEntry(s.Cup));
        var before = TempApp.Json(Get(s).Character);

        Assert.Equal("play.entry-unknown", CodeOf(() => Spend(s, Id(404))));
        Assert.Equal("play.entry-unknown", CodeOf(() => Spend(s, view.Character.Equipment[1].Id!.Value))); // attunement only: no charges
        Assert.Equal("play.entry-unknown", CodeOf(() => s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true)))); // names nothing
        Assert.Equal("play.entry-unknown", CodeOf(() => s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true, ContentId: s.Cup.ContentId, EntryIndex: 3)))); // a plain item
        Assert.Equal("play.confirmation-required", CodeOf(() => s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, EntryId: view.Character.Equipment[0].Id))));
        Assert.Equal(before, TempApp.Json(Get(s).Character));
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R158_an_id_less_charged_entry_gets_its_id_on_first_use_and_no_other_entry_changes(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        // As an import from elsewhere could leave it: stored straight, without the save path's ids.
        var stored = Get(s).Character with { Equipment = [new EquipmentEntry(s.Cup), new EquipmentEntry(s.Wand), new EquipmentEntry(s.Trinket)] };
        s.Temp.App.Store.SaveCharacter(stored);

        var view = s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true, ContentId: s.Wand.ContentId, EntryIndex: 1));

        Assert.NotNull(view.Character.Equipment[1].Id);
        Assert.Equal(1, view.Character.Equipment[1].ChargesSpent);
        Assert.Equal(view.Character.Equipment[1].Id!.Value.ToString("D"), view.Sheet.ItemUses!.Single().EntryKey);
        Assert.Null(view.Character.Equipment[0].Id);
        Assert.Null(view.Character.Equipment[2].Id);
        Assert.Null(view.Character.Equipment[0].ChargesSpent);
        Assert.Null(view.Character.Equipment[2].ChargesSpent);
        // The index must name the item it was meant for.
        Assert.Equal("play.entry-unknown", CodeOf(() => s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true, ContentId: s.Wand.ContentId, EntryIndex: 0))));
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R182_a_stale_id_less_copy_is_refused_where_pairing_used_to_hand_it_the_stored_id_and_spent_count(string family)
    {
        var clock = new MutableTime(TempApp.Now);
        var s = Start(family, time: clock);
        using var _ = s.Temp;
        s.Temp.App.Store.SaveCharacter(Get(s).Character with { Equipment = [new EquipmentEntry(s.Wand), new EquipmentEntry(s.Wand)] });
        var stale = Get(s).Character; // both copies still id-less
        clock.Now = clock.Now.AddSeconds(5);
        s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true, Amount: 2, ContentId: s.Wand.ContentId, EntryIndex: 0));
        var before = TempApp.Json(Get(s).Character);

        var code = CodeOf(() => s.Temp.App.SaveCharacter(stale with { Equipment = [new EquipmentEntry(s.Wand, Equipped: true), new EquipmentEntry(s.Wand)] }));

        Assert.Equal("character.stale", code);
        Assert.Equal(before, TempApp.Json(Get(s).Character));
    }

    // ---- R161/R182: compare-and-set on save. A save names the version it edited by its updatedAt. These use a moving clock:
    // a fixed one stamps every write alike and would hide a stale save. ----------------------------------------------------

    private static void Tick(MutableTime clock) => clock.Now = clock.Now.AddSeconds(5);

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R182_a_stale_save_after_a_spend_is_refused_with_character_stale_and_the_spent_count_stays(string family)
    {
        var clock = new MutableTime(TempApp.Now);
        var s = Start(family, time: clock);
        using var _ = s.Temp;
        Tick(clock);
        var wandId = Save(s, new EquipmentEntry(s.Wand)).Character.Equipment[0].Id!.Value;
        var stale = Get(s).Character;
        Tick(clock);
        Spend(s, wandId, 2); // the play path stamps UpdatedAt too
        var before = TempApp.Json(Get(s).Character);
        Tick(clock);

        var refused = Assert.Throws<AppValidationException>(() => s.Temp.App.SaveCharacter(stale with { Equipment = [stale.Equipment[0] with { ChargesSpent = 0, Equipped = true }] }));

        Assert.Equal("character.stale", refused.Code);
        Assert.Equal("character.stale", refused.Problems[0].Code);
        Assert.DoesNotContain("Test Wielder", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, TempApp.Json(Get(s).Character)); // nothing written
        Assert.Equal(2, Get(s).Character.Equipment[0].ChargesSpent);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R182_a_save_made_from_the_current_copy_is_accepted_and_still_cannot_change_the_spent_count(string family)
    {
        var clock = new MutableTime(TempApp.Now);
        var s = Start(family, time: clock);
        using var _ = s.Temp;
        Tick(clock);
        var wandId = Save(s, new EquipmentEntry(s.Wand), new EquipmentEntry(s.Wand)).Character.Equipment[0].Id!.Value;
        Tick(clock);
        Spend(s, wandId, 2);
        var current = Get(s).Character;
        Tick(clock);

        // R160 holds for a current payload that says otherwise (a hand-built one): only play and rest change a spend.
        var saved = s.Temp.App.SaveCharacter(current with { Equipment = [current.Equipment[0] with { Equipped = true, ChargesSpent = 0 }, current.Equipment[1] with { ChargesSpent = 5 }] });

        Assert.Equal((2, true), (saved.Character.Equipment[0].ChargesSpent, saved.Character.Equipment[0].Equipped));
        Assert.Null(saved.Character.Equipment[1].ChargesSpent);
        Assert.Equal(clock.Now, saved.Character.UpdatedAt);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R182_a_stale_id_less_copy_shifted_out_of_its_stored_position_is_refused_not_reset(string family)
    {
        var clock = new MutableTime(TempApp.Now);
        var s = Start(family, time: clock);
        using var _ = s.Temp;
        Tick(clock);
        s.Temp.App.Store.SaveCharacter(Get(s).Character with { Equipment = [new EquipmentEntry(s.Ring), new EquipmentEntry(s.Wand)], UpdatedAt = clock.Now }); // both id-less, as an older copy holds them
        var stale = Get(s).Character;
        Tick(clock);
        var spent = s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true, Amount: 2, ContentId: s.Wand.ContentId, EntryIndex: 1));
        var before = TempApp.Json(Get(s).Character);
        Tick(clock);

        var code = CodeOf(() => s.Temp.App.SaveCharacter(stale with { Equipment = [new EquipmentEntry(s.Wand), .. stale.Equipment] }));

        Assert.Equal("character.stale", code);
        Assert.Equal(before, TempApp.Json(Get(s).Character));
        Assert.Equal(2, spent.Character.Equipment[1].ChargesSpent);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R182_a_delete_and_re_add_of_a_charged_item_at_the_same_position_is_a_new_entry_with_full_charges(string family)
    {
        var clock = new MutableTime(TempApp.Now);
        var s = Start(family, time: clock);
        using var _ = s.Temp;
        Tick(clock);
        var wandId = Save(s, new EquipmentEntry(s.Wand)).Character.Equipment[0].Id!.Value;
        Tick(clock);
        Spend(s, wandId, 2);
        Tick(clock);

        // The payload is current (it names the stored version), so the id-less wand at index 0 is one the player added.
        var saved = s.Temp.App.SaveCharacter(Get(s).Character with { Equipment = [new EquipmentEntry(s.Wand)] });

        Assert.Null(saved.Character.Equipment[0].ChargesSpent);
        Assert.NotEqual(wandId, saved.Character.Equipment[0].Id!.Value);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R182_updatedAt_round_trips_at_100_ns_through_the_real_store_and_the_json_the_page_sends_back(string family)
    {
        var clock = new MutableTime(TempApp.Now.AddTicks(1234567)); // the finest precision the clock gives
        var s = Start(family, time: clock);
        using var _ = s.Temp;
        var created = Get(s).Character;
        Assert.Equal(clock.Now, created.UpdatedAt);
        Assert.Equal(1234567, (created.UpdatedAt - TempApp.Now).Ticks);
        clock.Now = clock.Now.AddTicks(7);

        // The page sends the character back as it got it: parsed from JSON text, not the in-memory record.
        var viaJson = JsonSerializer.Deserialize<Character>(JsonSerializer.Serialize(created, RulesJson.Compact), RulesJson.Compact)!;
        var response = Dispatch(s, "character.save", viaJson with { Name = "Test Wielder II" });

        Assert.True(response.GetProperty("ok").GetBoolean());
        Assert.Equal(clock.Now, Get(s).Character.UpdatedAt);
        Assert.Equal("Test Wielder II", Get(s).Character.Name);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R182_through_the_dispatcher_a_stale_save_is_a_character_stale_error_with_no_text_of_the_character(string family)
    {
        var clock = new MutableTime(TempApp.Now);
        var s = Start(family, time: clock);
        using var _ = s.Temp;
        var stale = Get(s).Character;
        Tick(clock);
        s.Temp.App.SaveCharacter(stale with { Name = "Changed Elsewhere" });
        Tick(clock);

        var response = Dispatch(s, "character.save", stale with { Name = "Test Wielder Late" });

        Assert.False(response.GetProperty("ok").GetBoolean());
        var text = response.GetProperty("error").ToString();
        Assert.Equal("character.stale", response.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("Test Wielder", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Changed Elsewhere", text, StringComparison.Ordinal);
        Assert.Equal("Changed Elsewhere", Get(s).Character.Name);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R182_a_new_character_is_not_compared_and_a_save_of_the_stored_copy_goes_through(string family)
    {
        var clock = new MutableTime(TempApp.Now);
        var s = Start(family, time: clock);
        using var _ = s.Temp;
        Tick(clock);

        var other = s.Temp.App.CreateCharacter(new("Test Other", family, new(8, 14, 12, 16, 10, 10), null, Classes: Get(s).Character.Classes)); // no stored row: no version to compare
        Assert.Equal(clock.Now, other.Character.UpdatedAt);
        Tick(clock);
        var saved = s.Temp.App.SaveCharacter(Get(s).Character with { Name = "Test Wielder" });
        Assert.Equal(clock.Now, saved.Character.UpdatedAt);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R175_a_save_with_a_null_item_where_the_stored_entry_has_an_id_is_refused_with_its_code_and_writes_nothing(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        Save(s, new EquipmentEntry(s.Wand));
        var before = TempApp.Json(Get(s).Character);

        var problems = Assert.Throws<AppValidationException>(() => s.Temp.App.SaveCharacter(Get(s).Character with { Equipment = [new EquipmentEntry(null!)] })).Problems;

        Assert.Equal("character.empty-entry", problems[0].Code);
        Assert.Equal(before, TempApp.Json(Get(s).Character));
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R175_a_new_id_less_copy_at_another_position_gets_a_fresh_id_and_full_charges(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var wandId = Save(s, new EquipmentEntry(s.Ring), new EquipmentEntry(s.Wand)).Character.Equipment[1].Id!.Value;
        Spend(s, wandId, 2);

        // The wand is removed and a new copy added at the front: it is a new entry, not the stale one.
        var saved = s.Temp.App.SaveCharacter(Get(s).Character with { Equipment = [new EquipmentEntry(s.Wand), Get(s).Character.Equipment[0]] });

        Assert.Null(saved.Character.Equipment[0].ChargesSpent);
        Assert.NotEqual(wandId, saved.Character.Equipment[0].Id!.Value);
        Assert.DoesNotContain(saved.Character.Equipment, e => e.Id == wandId);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void Refusal_messages_for_charges_never_quote_the_item_name_or_the_dice(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var id = Save(s, new EquipmentEntry(s.Wand)).Character.Equipment[0].Id!.Value;
        Spend(s, id, 4);
        var messages = new List<string>
        {
            Assert.Throws<AppValidationException>(() => Spend(s, id, 8)).Problems[0].Message,
            Assert.Throws<AppValidationException>(() => Regain(s, id, 8)).Problems[0].Message,
            Assert.Throws<AppValidationException>(() => s.Temp.App.PreviewRest(s.Id, RestPeriod.LongRest, null, [new(id.ToString("D"), 9)])).Problems[0].Message,
        };
        var all = Start(family, regain: null);
        using var _2 = all.Temp;
        var allId = Save(all, new EquipmentEntry(all.Wand)).Character.Equipment[0].Id!.Value;
        messages.Add(Assert.Throws<AppValidationException>(() => all.Temp.App.Roll(new(all.Id, ChargeEntry: allId.ToString("D")))).Problems[0].Message);

        Assert.Equal(4, messages.Count);
        foreach (var message in messages)
        {
            Assert.DoesNotContain("Fixture wand", message, StringComparison.Ordinal);
            Assert.DoesNotContain("1d6", message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void The_commands_reach_the_service_through_the_dispatcher_with_codes_only(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var id = Save(s, new EquipmentEntry(s.Wand)).Character.Equipment[0].Id!.Value;

        var ok = Dispatch(s, "character.play", new { characterId = s.Id, action = "spendCharge", entryId = id, confirm = true });
        Assert.True(ok.GetProperty("ok").GetBoolean(), ok.ToString());
        Assert.Equal(1, ok.GetProperty("result").GetProperty("character").GetProperty("equipment")[0].GetProperty("chargesSpent").GetInt32());

        var refused = Dispatch(s, "character.play", new { characterId = s.Id, action = "regainCharge", entryId = id, amount = 5, confirm = true });
        Assert.Equal("play.charges-full", refused.GetProperty("error").GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    // ---- rests ---------------------------------------------------------------------------------------------------------------

    private static string Key(CharacterView view, int index) => view.Character.Equipment[index].Id!.Value.ToString("D");

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void A_long_rest_waits_for_the_regain_roll_then_applies_it_through_the_play_path_capped_at_the_spent_count(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var view = Save(s, new EquipmentEntry(s.Wand, Equipped: true, Attuned: true), new EquipmentEntry(s.Wand));
        var (a, b) = (view.Character.Equipment[0].Id!.Value, view.Character.Equipment[1].Id!.Value);
        Spend(s, a, 5);
        Spend(s, b, 1);
        var (ka, kb) = (a.ToString("D"), b.ToString("D"));

        var asking = s.Temp.App.PreviewRest(s.Id, RestPeriod.LongRest);
        var ask = asking.Changes.Single(c => c.Kind == RestChangeKind.Charges && c.ResourceId == ka);
        Assert.Equal(("1d6+1", (int?)null), (ask.Dice, ask.SpentAfter));
        // A rest that is confirmed without the rolls changes nothing about the charges.
        s.Temp.App.Rest(new(s.Id, RestPeriod.LongRest, Confirm: true, Basis: asking.Basis));
        Assert.Equal((5, 1), (Get(s).Character.Equipment[0].ChargesSpent, Get(s).Character.Equipment[1].ChargesSpent));

        ChargeRoll[] rolls = [new(ka, 4), new(kb, 6)];
        var preview = s.Temp.App.PreviewRest(s.Id, RestPeriod.LongRest, null, rolls);
        Assert.Equal((2, 6, 1, 4), Tuple(preview, ka));
        Assert.Equal((6, 7, 0, 1), Tuple(preview, kb)); // rolled 6, only 1 was spent
        Assert.NotEqual(asking.Basis, preview.Basis);
        Assert.Equal("rest.preview-stale", CodeOf(() => s.Temp.App.Rest(new(s.Id, RestPeriod.LongRest, Confirm: true, Basis: preview.Basis, ChargeRolls: [new(ka, 3), new(kb, 6)]))));

        var rested = s.Temp.App.Rest(new(s.Id, RestPeriod.LongRest, Confirm: true, Basis: preview.Basis, ChargeRolls: rolls));

        Assert.Equal((1, (int?)null), (rested.Character.Equipment[0].ChargesSpent, rested.Character.Equipment[1].ChargesSpent));
        Assert.Equal((true, true), (rested.Character.Equipment[0].Equipped, rested.Character.Equipment[0].Attuned == true)); // nothing else about the entry moved
        Assert.Equal(6, Use(rested, a).ChargesLeft);
        Assert.Equal(7, Use(rested, b).ChargesLeft);
    }

    private static (int From, int To, int? SpentAfter, int? Amount) Tuple(RestPreview preview, string key)
    {
        var change = preview.Changes.Single(c => c.Kind == RestChangeKind.Charges && c.ResourceId == key);
        return (change.From, change.To, change.SpentAfter, change.Amount);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void A_null_regain_brings_everything_back_and_a_null_recovery_nothing_and_an_unticked_change_is_skipped(string family)
    {
        var all = Start(family, regain: null);
        using var _ = all.Temp;
        var view = Save(all, new EquipmentEntry(all.Wand), new EquipmentEntry(all.Wand));
        var (a, b) = (view.Character.Equipment[0].Id!.Value, view.Character.Equipment[1].Id!.Value);
        Spend(all, a, 4);
        Spend(all, b, 2);

        var preview = all.Temp.App.PreviewRest(all.Id, RestPeriod.LongRest);
        Assert.Equal((3, 7, 0, 4), Tuple(preview, a.ToString("D")));
        Assert.All(preview.Changes.Where(c => c.Kind == RestChangeKind.Charges), c => Assert.Null(c.Dice));

        var rested = all.Temp.App.Rest(new(all.Id, RestPeriod.LongRest, Confirm: true, Basis: preview.Basis, Skip: [$"charges:{b:D}"]));
        Assert.Equal((null, 2), (rested.Character.Equipment[0].ChargesSpent, rested.Character.Equipment[1].ChargesSpent));

        var none = Start(family, period: null);
        using var _2 = none.Temp;
        var id = Save(none, new EquipmentEntry(none.Wand)).Character.Equipment[0].Id!.Value;
        Spend(none, id, 3);
        Assert.DoesNotContain(none.Temp.App.PreviewRest(none.Id, RestPeriod.LongRest).Changes, c => c.Kind == RestChangeKind.Charges);
        Assert.DoesNotContain(none.Temp.App.PreviewRest(none.Id, RestPeriod.ShortRest).Changes, c => c.Kind == RestChangeKind.Charges);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void A_short_rest_returns_charges_only_for_an_item_that_recovers_on_a_short_rest(string family)
    {
        var s = Start(family, regain: null, period: RestPeriod.ShortRest);
        using var _ = s.Temp;
        var id = Save(s, new EquipmentEntry(s.Wand)).Character.Equipment[0].Id!.Value;
        Spend(s, id, 3);

        Assert.DoesNotContain(s.Temp.App.PreviewRest(s.Id, RestPeriod.LongRest).Changes, c => c.Kind == RestChangeKind.Charges);
        var preview = s.Temp.App.PreviewRest(s.Id, RestPeriod.ShortRest);
        var rested = s.Temp.App.Rest(new(s.Id, RestPeriod.ShortRest, Confirm: true, Basis: preview.Basis));

        Assert.Null(rested.Character.Equipment[0].ChargesSpent);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void A_charge_roll_the_dice_cannot_show_or_for_no_such_entry_is_refused_with_a_code(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var id = Save(s, new EquipmentEntry(s.Wand)).Character.Equipment[0].Id!.Value;
        Spend(s, id, 4);
        var before = TempApp.Json(Get(s).Character);

        Assert.Equal("rest.charge-roll-invalid", CodeOf(() => s.Temp.App.PreviewRest(s.Id, RestPeriod.LongRest, null, [new(id.ToString("D"), 9)])));
        Assert.Equal("rest.charge-roll-unknown", CodeOf(() => s.Temp.App.PreviewRest(s.Id, RestPeriod.LongRest, null, [new("nope", 3)])));
        var response = Dispatch(s, "character.restPreview", new { characterId = s.Id, kind = "longRest", chargeRolls = new[] { new { entry = id.ToString("D"), roll = 3 } } });
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal(1, response.GetProperty("result").GetProperty("changes").EnumerateArray().Count(c => c.GetProperty("kind").GetString() == "charges"));
        Assert.Equal(before, TempApp.Json(Get(s).Character));
    }

    // ---- rolling the regain dice ---------------------------------------------------------------------------------------------

    private sealed class ScriptedRandom(params int[] faces) : IRandomSource
    {
        private int _next;

        public int Next(int sides) => faces[_next++];
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void The_regain_dice_are_rolled_through_the_dice_roller_and_change_nothing(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var id = Save(s, new EquipmentEntry(s.Wand)).Character.Equipment[0].Id!.Value;
        Spend(s, id, 4);
        s.Temp.App.Random = new ScriptedRandom(3);
        var before = TempApp.Json(Get(s).Character);

        var record = s.Temp.App.Roll(new(s.Id, ChargeEntry: id.ToString("D")));

        Assert.Equal(("1d6+1", 4), (record.Formula, record.Total));
        Assert.Equal("charges.regain", record.Provenance!.RollId);
        Assert.Contains("Fixture wand", record.Provenance.Label, StringComparison.Ordinal);
        Assert.Equal(before, TempApp.Json(Get(s).Character));
        Assert.Equal("roll.charges-unknown", CodeOf(() => s.Temp.App.Roll(new(s.Id, ChargeEntry: "nope"))));
        Assert.Equal("roll.ambiguous", CodeOf(() => s.Temp.App.Roll(new(s.Id, ChargeEntry: id.ToString("D"), HitDie: 8))));

        var all = Start(family, regain: null);
        using var _2 = all.Temp;
        var allId = Save(all, new EquipmentEntry(all.Wand)).Character.Equipment[0].Id!.Value;
        Assert.Equal("roll.charges-no-dice", CodeOf(() => all.Temp.App.Roll(new(all.Id, ChargeEntry: allId.ToString("D")))));
    }

    // ---- older content ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void A_v9_item_keeps_its_old_behaviour_through_the_service(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var json = """{"contentId":"5fa5a000-0000-4000-8000-0000000000b9","revisionId":"5fa5e000-0000-4000-8000-0000000000b9","schemaVersion":9,"kind":"item","name":"Fixture old band","rulesFamilies":["FAMILY"],"provenance":{"sourceId":"SOURCE","page":{"start":3}},"status":"published","effects":[{"id":"sneak","type":"modifier","operation":"bonus","target":"skill.stealth","value":"2"},{"id":"use","type":"item","requiresAttunement":true,"charges":{"max":3}}]}"""
            .Replace("FAMILY", family, StringComparison.Ordinal)
            .Replace("SOURCE", family == RulesFamilies.Srd51 ? "5f0d5100-0000-4000-8000-000000000001" : "5f0d5210-0000-4000-8000-000000000001", StringComparison.Ordinal);
        var old = JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Options)!;
        s.Temp.App.Store.AddRevision(old);
        var bare = Get(s).Sheet.Field(FieldIds.Skill("stealth")).Value;

        var view = Save(s, new EquipmentEntry(old.Reference, Equipped: true));

        // Below v10 the item effect is unknown (Task 1): no attunement gate, no charges, no id; the +2 applies as soon as it is equipped.
        Assert.Equal(bare + 2, view.Sheet.Field(FieldIds.Skill("stealth")).Value);
        Assert.Null(view.Character.Equipment[0].Id);
        Assert.Null(view.Sheet.ItemUses);
        Assert.DoesNotContain(view.Sheet.Diagnostics, d => d.Code == "item.not-attuned");
        Assert.Equal("play.entry-unknown", CodeOf(() => s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true, ContentId: old.ContentId, EntryIndex: 0))));
        // A second copy of it is refused like any plain item.
        Assert.Equal("character.equipment-duplicate", CodeOf(() => Save(s, new EquipmentEntry(old.Reference), new EquipmentEntry(old.Reference))));
    }

    // ---- R184: R159 is enforced only for copies a payload adds ---------------------------------------------------------------

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R184_a_stored_character_with_two_ided_copies_of_an_item_whose_revision_is_absent_can_still_play_and_save(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        // As a share or import leaves it: the item's revision never arrived, the copies carry their ids, and the store wrote it.
        var absent = new ContentReference(Guid.Parse("5fa5a000-0000-4000-8000-0000000000c1"), Guid.Parse("5fa5e000-0000-4000-8000-0000000000c1"));
        s.Temp.App.Store.SaveCharacter(Get(s).Character with { Equipment = [new(absent, Id: Id(1)), new(absent, Id: Id(2))] });

        var damaged = s.Temp.App.Play(new(s.Id, PlayActionKind.Damage, Confirm: true, Amount: 1));
        var saved = s.Temp.App.SaveCharacter(Get(s).Character);

        Assert.Equal(2, damaged.Character.Equipment.Count);
        Assert.Equal([Id(1), Id(2)], saved.Character.Equipment.Select(e => e.Id));
        // A third copy the payload adds is still refused (R159): it is not a copy the stored character already holds.
        Assert.Equal("character.equipment-duplicate", CodeOf(() => Save(s, [.. Get(s).Character.Equipment, new EquipmentEntry(absent, Id: Id(3))])));
        Assert.Equal(2, Get(s).Character.Equipment.Count);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R184_a_new_second_copy_of_a_plain_item_is_still_refused_next_to_a_stored_one(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        var stored = Save(s, new EquipmentEntry(s.Cup)).Character.Equipment;

        Assert.Equal("character.equipment-duplicate", CodeOf(() => Save(s, [.. stored, new EquipmentEntry(s.Cup)])));
        Assert.Equal("character.equipment-duplicate", CodeOf(() => Save(s, [.. stored, new EquipmentEntry(s.Cup, Id: Id(5))])));
        Assert.Single(Get(s).Character.Equipment);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void R184_an_update_to_a_revision_that_drops_the_item_effect_does_not_wedge_later_writes(string family)
    {
        var s = Start(family);
        using var _ = s.Temp;
        Save(s, new EquipmentEntry(s.Wand), new EquipmentEntry(s.Wand));
        var wand = s.Temp.App.Store.FindRevision(s.Wand)!;
        var plain = s.Temp.App.Publish(s.Temp.App.SaveDraft(wand with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, Effects = [] })).Published;

        var updated = s.Temp.App.ApplyUpdate(s.Id, s.Wand, plain, confirm: true);

        Assert.All(updated.Character.Equipment, e => Assert.Equal(plain, e.Item));
        var damaged = s.Temp.App.Play(new(s.Id, PlayActionKind.Damage, Confirm: true, Amount: 1));
        var saved = s.Temp.App.SaveCharacter(Get(s).Character);
        Assert.Equal(2, damaged.Character.Equipment.Count);
        Assert.Equal(2, saved.Character.Equipment.Count);
    }
}
