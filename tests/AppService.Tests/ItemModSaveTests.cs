using System.IO.Compression;
using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// MECH-X item 3 (R51, R51a, R52, R57): mods on equipment entries through the save path. A mod is checked against the catalog only on
/// <c>character.save</c> and <c>character.create</c>; play, rests, charge changes and snapshot restores never refuse a stored pair.
/// Fixtures: <c>fixture-pack-item-mods.json</c> (installed per test) and invented items built in code.
/// </summary>
public class ItemModSaveTests
{
    private static ContentReference R(int n) =>
        new(Guid.Parse($"6a7e00f3-0000-4000-8000-{n:X12}"), Guid.Parse($"6a7e00f3-0000-4000-8000-{0x100 + n:X12}"));

    private static readonly ContentReference Keen = R(0x01);
    private static readonly ContentReference Plating = R(0x02);
    private static readonly ContentReference Lining = R(0x03);
    private static readonly ContentReference Longblade = R(0x11);
    private static readonly ContentReference Coat = R(0x12);
    private static readonly ContentReference Whisper = R(0x21);

    private sealed record Setup(TempApp Temp, Guid Id);

    private static Setup Start()
    {
        var temp = new TempApp();
        var pack = TempApp.LoadFixture<ContentPack>("fixture-pack-item-mods.json");
        foreach (var source in pack.Sources)
            temp.App.Store.UpsertSource(source);
        foreach (var revision in pack.Revisions)
            temp.App.Store.AddRevision(revision);
        var arcanist = new ContentReference(Guid.Parse("5f5dc000-0000-4000-8000-000000000001"), Guid.Parse("5f5de000-0000-4000-8000-000000000001"));
        var character = new Character
        {
            Id = Guid.NewGuid(), Name = "Test Tinker", RulesFamily = RulesFamilies.Srd521, Level = 3, Classes = [new(arcanist, 3)],
            BaseAbilities = new(14, 14, 12, 10, 10, 10),
        };
        return new(temp, temp.App.SaveCharacter(character).Character.Id);
    }

    private static ContentRevision Item(string name, int n, int version = 12, RevisionStatus status = RevisionStatus.Published, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"6a7e00f6-0000-4000-8000-{n:X12}"), RevisionId = Guid.Parse($"6a7e00f6-0000-4000-8000-{0x100 + n:X12}"),
        SchemaVersion = version, Kind = ContentKind.Item, Name = name, RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Guid.Parse("6a7e0000-0000-4000-8000-000000000001")), Status = status, Effects = effects,
    };

    private static ContentReference Install(Setup s, ContentRevision revision)
    {
        s.Temp.App.Store.AddRevision(revision);
        return revision.Reference;
    }

    /// <summary>A weapon with three charges that a long rest refills, for the paths that must not refuse a stored mod.</summary>
    private static ContentRevision ChargedBlade() => Item("Fixture Charged Blade", 0x01, effects:
    [
        new WeaponEffect { Id = "charged-weapon", Category = WeaponCategory.Martial, Attack = WeaponAttack.Melee, Damage = "1d8", DamageType = "slashing", WeaponKey = "chargedblade" },
        new ItemEffect { Id = "charged-use", Charges = new() { Max = 3, Recovery = new() { Period = RestPeriod.LongRest } } },
    ]);

    private static CharacterView Save(Setup s, params EquipmentEntry[] equipment) =>
        s.Temp.App.SaveCharacter(s.Temp.App.GetCharacter(s.Id).Character with { Equipment = equipment });

    private static CharacterView Get(Setup s) => s.Temp.App.GetCharacter(s.Id);

    private static string CodeOf(Action act) => Assert.Throws<AppValidationException>(act).Problems[0].Code;

    [Fact]
    public void Saving_an_entry_with_a_mod_gives_every_copy_of_that_item_an_id()
    {
        var s = Start();
        using var _ = s.Temp;

        var view = Save(s, new EquipmentEntry(Longblade, Equipped: true), new EquipmentEntry(Longblade, Equipped: true, Mods: [Keen]), new EquipmentEntry(Coat, Equipped: true));

        var equipment = view.Character.Equipment;
        Assert.NotNull(equipment[0].Id);
        Assert.NotNull(equipment[1].Id);
        Assert.NotEqual(equipment[0].Id, equipment[1].Id);
        Assert.Null(equipment[2].Id); // a plain item with no mods on any entry stays id-less
        Assert.Equal(2, view.Sheet.Attacks!.Count(a => a.Item == Longblade));
    }

    [Fact]
    public void Two_copies_of_a_plain_weapon_are_allowed_once_one_has_a_mod()
    {
        var s = Start();
        using var _ = s.Temp;

        var view = Save(s, new EquipmentEntry(Longblade), new EquipmentEntry(Longblade, Mods: [Keen]));

        Assert.Equal(2, view.Character.Equipment.Count);
        Assert.Equal(2, Get(s).Character.Equipment.Count);
    }

    [Fact]
    public void A_null_entry_beside_a_duplicated_item_is_refused_as_an_empty_entry()
    {
        var s = Start();
        using var _ = s.Temp;

        var problems = Assert.Throws<AppValidationException>(() => Save(s, new EquipmentEntry(Longblade), null!, new EquipmentEntry(Longblade))).Problems;

        Assert.Contains(problems, p => p.Code == "character.empty-entry");
    }

    [Fact]
    public void A_plain_second_copy_alone_is_still_refused()
    {
        var s = Start();
        using var _ = s.Temp;

        Assert.Equal("character.equipment-duplicate", CodeOf(() => Save(s, new EquipmentEntry(Longblade), new EquipmentEntry(Longblade))));
    }

    [Fact]
    public void Removing_the_last_mod_keeps_the_ids_and_both_copies()
    {
        var s = Start();
        using var _ = s.Temp;
        var first = Save(s, new EquipmentEntry(Longblade, Equipped: true), new EquipmentEntry(Longblade, Equipped: true, Mods: [Keen]));

        var again = Save(s, first.Character.Equipment[0], first.Character.Equipment[1] with { Mods = null });

        Assert.Equal(first.Character.Equipment.Select(e => e.Id), again.Character.Equipment.Select(e => e.Id));
        Assert.All(again.Character.Equipment, e => Assert.NotNull(e.Id));
        Assert.Null(again.Character.Equipment[1].Mods);
        Assert.Equal(2, again.Character.Equipment.Count);
    }

    [Fact]
    public void A_save_refuses_a_mod_that_is_not_a_published_mod_for_the_host()
    {
        var s = Start();
        using var _ = s.Temp;
        var draft = Install(s, Item("Fixture Draft Rune", 0x31, status: RevisionStatus.Draft, effects: new ItemModEffect { Id = "draft-rune", AppliesTo = ModTarget.Weapon, Bonus = 1 }));
        var old = Install(s, Item("Fixture Old Rune", 0x32, version: 11, effects: new ModifierEffect { Id = "old", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" }));

        Assert.Equal("character.equipment-mod-invalid", CodeOf(() => Save(s, new EquipmentEntry(Longblade, Mods: [Whisper])))); // a feature
        Assert.Equal("character.equipment-mod-invalid", CodeOf(() => Save(s, new EquipmentEntry(Longblade, Mods: [draft]))));
        Assert.Equal("character.equipment-mod-invalid", CodeOf(() => Save(s, new EquipmentEntry(Longblade, Mods: [old])))); // not an itemMod
        Assert.Equal("character.equipment-mod-invalid", CodeOf(() => Save(s, new EquipmentEntry(Longblade, Mods: [Plating])))); // an armor mod on a weapon host
        Assert.Equal("character.equipment-mod-invalid", CodeOf(() => Save(s, new EquipmentEntry(Coat, Mods: [Keen])))); // a weapon mod on an armor host
        Assert.Equal("character.equipment-mod-invalid", CodeOf(() => Save(s, new EquipmentEntry(Whisper, Mods: [Keen])))); // a host that is not an item
        Assert.Equal("character.equipment-mod-invalid", CodeOf(() => Save(s, new EquipmentEntry(old, Mods: [Keen])))); // a host with no weapon or armor
        var refused = Assert.Throws<AppValidationException>(() => Save(s, new EquipmentEntry(Longblade), new EquipmentEntry(Longblade, Mods: [Plating])));
        Assert.Contains("equipment entry 2", refused.Problems[0].Message, StringComparison.Ordinal);
        Assert.Empty(Get(s).Character.Equipment);

        // The fits: weapon on weapon, armor on armor, any on either.
        Save(s, new EquipmentEntry(Longblade, Mods: [Keen, Lining]), new EquipmentEntry(Coat, Mods: [Plating, Lining]));
        Assert.Equal(2, Get(s).Character.Equipment.Count);
    }

    [Fact]
    public void A_stored_pair_whose_mod_lost_its_itemMod_still_saves_spends_a_charge_rests_and_restores()
    {
        var s = Start();
        using var _ = s.Temp;
        var blade = Install(s, ChargedBlade());
        // An id-less v11 character, stored directly (an imported or restored copy has no ids).
        var stored = Get(s).Character with { Equipment = [new EquipmentEntry(blade, Equipped: true, Mods: [Keen])], UpdatedAt = DateTimeOffset.UtcNow };
        s.Temp.App.Store.InTransaction(() => s.Temp.App.Store.SaveCharacter(stored));
        // A new revision of the mod, without the itemMod.
        var republished = Item("Fixture Keen Rune", 0x01, effects: new ModifierEffect { Id = "plain", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" }) with
        {
            ContentId = Keen.ContentId, RevisionId = Guid.Parse("6a7e00f3-0000-4000-8000-000000000201"),
        };
        Install(s, republished);

        // A save keeps the pair, whatever the catalog says now; the entry takes an id; a new pair is still checked.
        var saved = s.Temp.App.SaveCharacter(Get(s).Character with { Name = "Test Tinker Renamed" });
        var entry = Assert.Single(saved.Character.Equipment);
        Assert.NotNull(entry.Id);
        Assert.Equal("character.equipment-mod-invalid", CodeOf(() => s.Temp.App.SaveCharacter(Get(s).Character with { Equipment = [.. Get(s).Character.Equipment, new EquipmentEntry(Longblade, Mods: [Whisper])] })));

        var spent = s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true, Amount: 1, EntryId: entry.Id));
        Assert.Equal(1, spent.Character.Equipment[0].ChargesSpent);

        var asking = s.Temp.App.PreviewRest(s.Id, RestPeriod.LongRest);
        var rested = s.Temp.App.Rest(new(s.Id, RestPeriod.LongRest, Confirm: true, Basis: asking.Basis));
        Assert.Null(rested.Character.Equipment[0].ChargesSpent);

        var snapshot = s.Temp.App.Snapshot(new(s.Id));
        s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true, Amount: 1, EntryId: entry.Id));
        var preview = s.Temp.App.PreviewRestore(new(s.Id, snapshot.Id));
        var restored = s.Temp.App.RestoreSnapshot(new(preview.Token, Confirm: true));
        Assert.Equal(Keen.ContentId, Assert.Single(Assert.Single(restored.View.Character.Equipment).Mods!).ContentId);
    }

    [Fact]
    public void A_backup_export_includes_the_mod_revision()
    {
        var s = Start();
        using var _ = s.Temp;
        Save(s, new EquipmentEntry(Longblade, Equipped: true, Mods: [Keen]));

        var package = s.Temp.App.ExportCharacters([s.Id]).Content;

        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var revisions = new List<Guid>();
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("content/", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open());
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            revisions.Add(document.RootElement.GetProperty("revisionId").GetGuid());
        }
        Assert.Contains(Keen.RevisionId, revisions);
        Assert.Contains(Longblade.RevisionId, revisions);
    }

    [Fact]
    public void Listed_mods_carry_modOf()
    {
        var s = Start();
        using var _ = s.Temp;

        var options = s.Temp.App.ListContent(RulesFamilies.Srd521).Where(o => o.Reference.ContentId.ToString().StartsWith("6a7e00f3", StringComparison.Ordinal)).ToDictionary(o => o.Reference.ContentId);

        Assert.Equal(ModTarget.Weapon, options[Keen.ContentId].ModOf);
        Assert.Equal(ModTarget.Armor, options[Plating.ContentId].ModOf);
        Assert.Equal(ModTarget.Any, options[Lining.ContentId].ModOf);
        Assert.Null(options[Longblade.ContentId].ModOf);
        Assert.Null(options[Whisper.ContentId].ModOf);
    }

    [Fact]
    public void Charge_roll_cap_counts_charged_rows_only()
    {
        var s = Start();
        using var _ = s.Temp;
        var blade = Install(s, ChargedBlade());
        // Three item rows (the host Longblade twice and the charged blade), one of them with charges.
        var view = Save(s, new EquipmentEntry(Longblade, Equipped: true), new EquipmentEntry(Longblade, Equipped: true, Mods: [Keen]), new EquipmentEntry(blade, Equipped: true));
        Assert.Equal(3, view.Sheet.ItemUses!.Count);
        var charged = view.Character.Equipment[2].Id!.Value;
        s.Temp.App.Play(new(s.Id, PlayActionKind.SpendCharge, Confirm: true, Amount: 1, EntryId: charged));
        var key = charged.ToString("D");

        var problem = Assert.Throws<AppValidationException>(() => s.Temp.App.PreviewRest(s.Id, RestPeriod.LongRest, null, [new(key, 1), new(key, 1)])).Problems[0];

        Assert.Equal("rest.charge-rolls-too-many", problem.Code);
        Assert.Contains("for 1 item ", problem.Message, StringComparison.Ordinal);
    }
}
