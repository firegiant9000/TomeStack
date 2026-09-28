using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D04 (M2 spellcasting): spells are recorded on the character (character schema v6), slots are spent and regained only by
/// confirmed <c>character.play</c> actions and recovered by rests, and spell rolls change nothing. Original fixture casters
/// seeded by development hosts (<c>tests/RulesFixtures/fixture-pack-m2-spells.json</c>).
/// </summary>
public class SpellcastingCommandTests
{
    private static ContentReference Ref(int n) => new(Guid.Parse($"5f5dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"5f5de000-0000-4000-8000-{n:D12}"));

    private static readonly ContentReference Arcanist = Ref(1);
    private static readonly ContentReference Spark = Ref(11);
    private static readonly ContentReference FrostRing = Ref(12);

    private static (TempApp Temp, Guid Id) Wizardly()
    {
        var temp = new TempApp();
        var character = new Character
        {
            Id = Guid.NewGuid(),
            Name = "Test Arcanist",
            RulesFamily = RulesFamilies.Srd521,
            Level = 3,
            Classes = [new(Arcanist, 3)],
            BaseAbilities = new(8, 14, 12, 16, 10, 10),
            Spells = [new(Arcanist.ContentId, Spark), new(Arcanist.ContentId, FrostRing)],
        };
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    private static string Code(JsonElement response) =>
        response.GetProperty("error").GetProperty("diagnostics").EnumerateArray().First().GetProperty("code").GetString()!;

    [Fact]
    public void Slots_are_spent_only_by_confirmed_actions_and_a_long_rest_restores_them()
    {
        var (temp, id) = Wizardly();
        using var _ = temp;
        var view = temp.App.GetCharacter(id);
        Assert.Equal(4, view.Sheet.SpellSlots!.Single(s => s.Level == 1).Maximum); // invented table at level 3: 4 / 1
        Assert.Equal(5, view.Sheet.Field(FieldIds.SpellAttack).Value); // PB 2 + Int 3

        var dispatcher = new CommandDispatcher(temp.App);
        var refused = JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command = "character.play", payload = new { characterId = id, action = "spendSlot", amount = 1 } }, RulesJson.Compact))).RootElement;
        Assert.Equal("play.confirmation-required", Code(refused));

        temp.App.Play(new(id, PlayActionKind.SpendSlot, Confirm: true, Amount: 1));
        view = temp.App.Play(new(id, PlayActionKind.SpendSlot, Confirm: true, Amount: 2));
        Assert.Equal((3, 0), (view.Sheet.SpellSlots!.Single(s => s.Level == 1).Remaining, view.Sheet.SpellSlots!.Single(s => s.Level == 2).Remaining));
        Assert.Equal("slots.none-left", Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.SpendSlot, Confirm: true, Amount: 2))).Problems[0].Code);
        Assert.Equal("slots.none-left", Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.SpendSlot, Confirm: true, Amount: 5))).Problems[0].Code);

        var preview = temp.App.PreviewRest(id, RestPeriod.LongRest);
        Assert.Equal(2, preview.Changes.Count(c => c.Kind == RestChangeKind.SpellSlots));
        view = temp.App.Rest(new(id, Confirm: true, Basis: preview.Basis, Skip: ["spellSlots:2"]));
        Assert.Equal((4, 0), (view.Sheet.SpellSlots!.Single(s => s.Level == 1).Remaining, view.Sheet.SpellSlots!.Single(s => s.Level == 2).Remaining));
    }

    [Fact]
    public void Spell_rolls_use_the_caster_attack_bonus_and_change_nothing()
    {
        var (temp, id) = Wizardly();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var attack = temp.App.Roll(new(id, Spell: Spark, SpellAttack: true));
        var damage = temp.App.Roll(new(id, Spell: Spark, Critical: true));

        Assert.Equal(("1d20", 5), (attack.Formula, attack.Modifiers.Single().Amount));
        Assert.Equal(("1d10", true), (damage.Formula, damage.Critical));
        Assert.Equal("TomeStack Fixtures: Spellcasting", damage.Provenance!.SourceTitle);
        Assert.Equal("roll.spell-no-attack", Assert.Throws<AppValidationException>(() => temp.App.Roll(new(id, Spell: FrostRing, SpellAttack: true))).Problems[0].Code);
        Assert.Equal("roll.spell-unknown", Assert.Throws<AppValidationException>(() => temp.App.Roll(new(id, Spell: Ref(14)))).Problems[0].Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void Spells_and_spent_slots_survive_a_package_round_trip_to_a_clean_data_folder()
    {
        var (temp, id) = Wizardly();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.SpendSlot, Confirm: true, Amount: 1));
        var stored = temp.App.GetCharacter(id);

        using var clean = new TempApp();
        clean.App.ApplyImport(temp.App.ExportCharacters([id]).Content);
        var imported = clean.App.GetCharacter(id);

        Assert.Equal(TempApp.Json(stored.Character.Spells), TempApp.Json(imported.Character.Spells));
        Assert.Equal(TempApp.Json(stored.Sheet.Spellcasting), TempApp.Json(imported.Sheet.Spellcasting));
        Assert.Equal(3, imported.Sheet.SpellSlots!.Single(s => s.Level == 1).Remaining);
    }

    [Fact]
    public void Content_list_offers_spells_with_their_level_and_lists()
    {
        using var temp = new TempApp();

        var spells = temp.App.ListContent(RulesFamilies.Srd521).Where(o => o.Kind == ContentKind.Spell).ToList();

        Assert.Contains(spells, o => o.Reference == FrostRing && o.Spell is { Level: 1 } s && s.Lists.Contains("fixture-arcane"));
    }
}
