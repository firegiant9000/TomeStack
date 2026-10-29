using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>The character v11 rung (R54): the mods are dropped, then plain copies that differed only by their mods are merged.</summary>
public class ItemModDownConvertTests
{
    private static readonly ContentReference Longblade = new(Guid.Parse("6a7e00f3-0000-4000-8000-000000000011"), Guid.Parse("6a7e00f3-0000-4000-8000-000000000111"));

    private static readonly ContentReference ScaleCoat = new(Guid.Parse("6a7e00f3-0000-4000-8000-000000000012"), Guid.Parse("6a7e00f3-0000-4000-8000-000000000112"));

    private static readonly ContentReference KeenRune = new(Guid.Parse("6a7e00f3-0000-4000-8000-000000000001"), Guid.Parse("6a7e00f3-0000-4000-8000-000000000101"));

    private static readonly ContentReference WardingPlating = new(Guid.Parse("6a7e00f3-0000-4000-8000-000000000002"), Guid.Parse("6a7e00f3-0000-4000-8000-000000000102"));

    private static Character Base() => Fixtures.Load("srd521-courier-v9.json") with { Equipment = [] };

    private static Character With(params EquipmentEntry[] entries) => Base() with { Equipment = entries };

    [Fact]
    public void Lowering_below_v11_drops_mods_and_merges_plain_copies()
    {
        var character = With(
            new(Longblade, true, 1, Guid.NewGuid(), Mods: [KeenRune]),
            new(Longblade, false, 1, Guid.NewGuid()),
            new(ScaleCoat, true, 1, Guid.NewGuid(), Mods: [WardingPlating]));
        Assert.Equal(11, character.SchemaVersion);
        Assert.Empty(character.Validate());

        var to10 = DownConvert.Below(character, 10);
        Assert.False(to10.Blocked);
        Assert.Equal(2, to10.Changes.Count(c => c.Code == BelowCodes.ItemMods && c.Action == BelowAction.Dropped));
        var copies = Assert.Single(to10.Changes, c => c.Code == BelowCodes.ItemModCopies);
        Assert.Equal(BelowAction.Dropped, copies.Action);
        Assert.Contains("2 copies", copies.Message, StringComparison.Ordinal);
        Assert.Equal(2, to10.Character.Equipment.Count);
        var blade = to10.Character.Equipment[0];
        Assert.Equal(Longblade, blade.Item);
        Assert.Equal(2, blade.Quantity);
        Assert.True(blade.Equipped);
        Assert.Equal(character.Equipment[0].Id, blade.Id);
        Assert.All(to10.Character.Equipment, e => Assert.Null(e.Mods));
        Assert.True(to10.Character.SchemaVersion <= 10);
        Assert.Empty(to10.Character.Validate());

        var to8 = DownConvert.Below(character, 8);
        Assert.False(to8.Blocked);
        Assert.Equal(8, to8.Character.SchemaVersion);
        Assert.All(to8.Character.Equipment, e => Assert.Null(e.Id));
        Assert.Empty(to8.Character.Validate());
    }

    [Fact]
    public void Lowering_keeps_charged_copies_apart()
    {
        var character = With(
            new(Longblade, true, 1, Guid.NewGuid(), ChargesSpent: 2, Mods: [KeenRune]),
            new(Longblade, false, 1, Guid.NewGuid()));

        var to10 = DownConvert.Below(character, 10);

        Assert.False(to10.Blocked);
        Assert.Single(to10.Changes, c => c.Code == BelowCodes.ItemMods);
        Assert.DoesNotContain(to10.Changes, c => c.Code == BelowCodes.ItemModCopies);
        Assert.Equal(2, to10.Character.Equipment.Count);
        Assert.Equal(2, to10.Character.Equipment[0].ChargesSpent);
        Assert.Empty(to10.Character.Validate());
    }

    [Fact]
    public void Groups_that_never_had_a_mod_are_left_alone()
    {
        var character = With(
            new(Longblade, true, 1, Guid.NewGuid(), Mods: [KeenRune]),
            new(ScaleCoat, true, 1, Guid.NewGuid()),
            new(ScaleCoat, false, 1, Guid.NewGuid()));

        var to10 = DownConvert.Below(character, 10);

        Assert.Equal(3, to10.Character.Equipment.Count);
        Assert.DoesNotContain(to10.Changes, c => c.Code == BelowCodes.ItemModCopies);
    }

    [Fact]
    public void Quantity_clamps_on_merge()
    {
        var character = With(
            new(Longblade, true, EquipmentEntry.MaxQuantity - 1, Guid.NewGuid(), Mods: [KeenRune]),
            new(Longblade, false, 5, Guid.NewGuid()));

        var to10 = DownConvert.Below(character, 10);

        var only = Assert.Single(to10.Character.Equipment);
        Assert.Equal(EquipmentEntry.MaxQuantity, only.Quantity);
        Assert.Empty(to10.Character.Validate());
    }

    [Fact]
    public void A_character_at_v11_is_returned_as_it_is_when_the_target_is_11()
    {
        var character = With(new EquipmentEntry(Longblade, true, 1, Guid.NewGuid(), Mods: [KeenRune]));

        var same = DownConvert.Below(character, 11);

        Assert.Same(character, same.Character);
        Assert.Empty(same.Changes);
        Assert.Contains("\"mods\"", JsonSerializer.Serialize(same.Character, RulesJson.Compact), StringComparison.Ordinal);
    }
}
