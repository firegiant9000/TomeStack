using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M1 item 7 (SPEC C-04): the transport-neutral <c>roll</c> command rolls a content roll effect or a sheet field as a
/// d20 test and returns a roll record with provenance. It never changes the character or spends a resource.
/// </summary>
public class RollCommandTests
{
    private static ContentReference Srd(int n) => new(Guid.Parse($"52c00000-0000-4000-8000-{n:D12}"), Guid.Parse($"52e00000-0000-4000-8000-{n:D12}"));

    private static readonly ContentReference Barbarian = Srd(11);
    private static readonly ContentReference Berserker = Srd(23);
    private static readonly ContentReference Frenzy = Srd(24);

    /// <summary>An SRD 5.2.1 Barbarian 3 (Path of the Berserker), so Frenzy's roll is available.</summary>
    private static (TempApp Temp, Guid Id) Berserker3(ulong seed = 42)
    {
        var temp = new TempApp();
        temp.App.Random = new SeededRandomSource(seed);
        var character = TempApp.LoadFixture<Character>("characters/srd521-ash-m1.json") with
        {
            Pins = [],
            Classes = [new(Barbarian, 3)],
            Choices = [new(Barbarian, "barbarian-subclass", [Berserker])],
        };
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    [Fact]
    public void A_content_roll_effect_is_rolled_with_its_provenance()
    {
        var (temp, id) = Berserker3();
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Frenzy, "frenzy-damage"));

        var expected = new SeededRandomSource(42);
        Assert.Equal("2d6", record.Formula);
        Assert.Equal([expected.Next(6), expected.Next(6)], record.Dice.Select(d => d.Value));
        Assert.Equal(record.Dice.Sum(d => d.Value), record.Total);
        var provenance = record.Provenance!;
        Assert.Equal((Frenzy, "Frenzy", "frenzy-damage"), (provenance.Content!, provenance.ContentName!, provenance.EffectId!));
        Assert.Equal(("System Reference Document 5.2.1", new PageRef(30)), (provenance.SourceTitle!, provenance.Page!));
    }

    [Fact]
    public void A_critical_doubles_the_dice_and_advantage_is_refused_for_damage_dice()
    {
        var (temp, id) = Berserker3();
        using var _ = temp;

        var critical = temp.App.Roll(new RollCommand(id, Frenzy, "frenzy-damage", Critical: true));
        var advantage = Assert.Throws<AppValidationException>(() => temp.App.Roll(new RollCommand(id, Frenzy, "frenzy-damage", Mode: RollMode.Advantage)));

        Assert.Equal(4, critical.Dice.Count);
        Assert.Equal(2, critical.Dice.Count(d => d.FromCritical));
        Assert.Equal("dice.advantage-requires-d20", Assert.Single(advantage.Problems).Code);
    }

    [Fact]
    public void A_sheet_field_is_rolled_as_a_d20_test_with_its_value_and_origin()
    {
        var (temp, id) = Berserker3(seed: 7);
        using var _ = temp;
        var save = temp.App.GetCharacter(id).Sheet.Field(FieldIds.Save(Ability.Str));

        var record = temp.App.Roll(new RollCommand(id, Field: FieldIds.Save(Ability.Str), Mode: RollMode.Advantage));

        Assert.Equal("1d20", record.Formula);
        Assert.Equal(2, record.Dice.Count);
        var kept = Assert.Single(record.Dice, d => d.Kept);
        Assert.Equal(record.Dice.Max(d => d.Value), kept.Value);
        var modifier = Assert.Single(record.Modifiers);
        Assert.Equal(("Strength saving throw", save.Value), (modifier.Label, modifier.Amount));
        Assert.Equal(save.Trace[^1].Origin, modifier.Origin); // the proficiency step from the Barbarian class
        Assert.Equal(kept.Value + save.Value, record.Total);
        Assert.Equal((FieldIds.Save(Ability.Str), "Strength saving throw (d20 test)"), (record.Provenance!.RollId, record.Provenance.Label));
    }

    [Fact]
    public void An_ability_modifier_is_rolled_as_a_check_named_like_the_summary_button()
    {
        // Investigation 2026-10-06 item 7: the summary button says "Roll Strength check (+N)"; the record must say the same.
        var (temp, id) = Berserker3(seed: 7);
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: FieldIds.Modifier(Ability.Str)));

        Assert.Equal((FieldIds.Modifier(Ability.Str), "Strength check"), (record.Provenance!.RollId, record.Provenance.Label));
        Assert.Equal("Strength modifier", Assert.Single(record.Modifiers).Label); // the Dice line keeps the modifier's own name
    }

    [Fact]
    public void Rolling_never_changes_the_character_even_for_a_roll_linked_to_a_resource()
    {
        using var temp = new TempApp();
        temp.App.Random = new SeededRandomSource(1);
        var source = temp.App.Store.ListSources()[0];
        var linked = new ContentRevision
        {
            ContentId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid(),
            Kind = ContentKind.Feature,
            Name = "Homebrew Surge",
            RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(source.Id),
            Status = RevisionStatus.Published,
            Effects =
            [
                new ResourceEffect { Id = "uses", ResourceId = "surge", Label = "Surges", Maximum = "1" },
                new RollEffect { Id = "surge-roll", RollId = "surge", Label = "Surge", Dice = "1d8+2", ResourceId = "surge" },
            ],
        };
        temp.App.Store.AddRevision(linked);
        var id = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-ash-m1.json") with { Pins = [linked.Reference] }).Character.Id;
        var before = TempApp.Json(temp.App.Store.FindCharacter(id));

        var record = temp.App.Roll(new RollCommand(id, linked.Reference, "surge-roll"));

        Assert.Equal("surge", record.Provenance!.LinkedResourceId);
        Assert.Equal(2, record.ExpressionConstant);
        Assert.Equal(before, TempApp.Json(temp.App.Store.FindCharacter(id)));
    }

    [Fact]
    public void Unavailable_or_invalid_roll_targets_are_refused()
    {
        var (temp, id) = Berserker3();
        using var _ = temp;
        string Code(RollCommand command) => Assert.Single(Assert.Throws<AppValidationException>(() => temp.App.Roll(command)).Problems).Code;

        Assert.Equal("roll.content-inactive", Code(new(id, Srd(1), "stonecunning"))); // Dwarf is not pinned
        Assert.Equal("roll.effect-not-found", Code(new(id, Frenzy, "nope")));
        Assert.Equal("roll.field-not-rollable", Code(new(id, Field: FieldIds.HitPoints)));
        Assert.Equal("roll.field-not-rollable", Code(new(id, Field: FieldIds.Passive("perception"))));
        Assert.Equal("roll.field-not-rollable", Code(new(id, Field: FieldIds.Speed)));
        Assert.Equal("roll.target-required", Code(new(id)));
        Assert.Equal("roll.ambiguous", Code(new(id, Frenzy, "frenzy-damage", FieldIds.Initiative)));
    }

    [Fact]
    public void The_roll_command_is_transport_neutral_json()
    {
        var (temp, id) = Berserker3();
        using var _ = temp;

        var response = JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(JsonSerializer.Serialize(new
        {
            id = "r1",
            command = "roll",
            payload = new { characterId = id, field = FieldIds.Initiative, mode = "disadvantage" },
        }, RulesJson.Compact))).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var result = response.GetProperty("result");
        Assert.Equal("disadvantage", result.GetProperty("mode").GetString());
        Assert.Equal(2, result.GetProperty("dice").GetArrayLength());
        Assert.Equal("initiative", result.GetProperty("provenance").GetProperty("rollId").GetString());
    }

    private static readonly Guid HomebrewSource = Guid.Parse("7c000000-0000-4000-8000-000000000b01");

    /// <summary>The Berserker 3 with Strength 8 and a pinned homebrew feat whose roll has the bonus formula <paramref name="bonus"/> (content v8).</summary>
    private static (TempApp Temp, Guid Id, ContentReference Feat) WithBonusRoll(string bonus)
    {
        var (temp, id) = Berserker3();
        temp.App.Store.UpsertSource(new SourceRecord
        {
            Id = HomebrewSource, Title = "Test Rolls", Publisher = "Me", RulesFamilies = [RulesFamilies.Srd521],
            EditionVersion = "homebrew", License = "Personal homebrew", Redistributable = false,
        });
        var feat = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = "Test Heavy Swing",
            RulesFamilies = [RulesFamilies.Srd521], Provenance = new(HomebrewSource), Status = RevisionStatus.Published,
            Effects = [new RollEffect { Id = "swing", RollId = "swing", Label = "Heavy swing", Dice = "1d6", Bonus = bonus }],
        };
        temp.App.Store.AddRevision(feat);
        var character = temp.App.GetCharacter(id).Character;
        temp.App.SaveCharacter(character with { Pins = [feat.Reference], BaseAbilities = new(8, 14, 14, 10, 10, 10) });
        return (temp, id, feat.Reference);
    }

    [Theory]
    [InlineData("-1", -1)]
    [InlineData("STR.MOD", -1)] // Strength 8
    public void A_negative_roll_bonus_is_subtracted_and_labelled_by_the_roll(string bonus, int amount)
    {
        var (temp, id, feat) = WithBonusRoll(bonus);
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, feat, "swing"));

        var modifier = Assert.Single(record.Modifiers);
        Assert.Equal(("Heavy swing bonus", amount), (modifier.Label, modifier.Amount)); // the label names the roll, not the formula
        Assert.Equal(record.Dice.Sum(d => d.Value) + amount, record.Total);
    }

    [Fact]
    public void A_roll_whose_bonus_cannot_be_calculated_is_refused()
    {
        // CLASS_LEVEL has no value in a feat, which belongs to no class.
        var (temp, id, feat) = WithBonusRoll("CLASS_LEVEL");
        using var _ = temp;

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Roll(new RollCommand(id, feat, "swing")));

        var problem = Assert.Single(refused.Problems);
        Assert.Equal(("roll.bonus-invalid", feat, "swing"), (problem.Code, problem.Content, problem.EffectId));
        Assert.Contains(temp.App.GetCharacter(id).Sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.Content == feat && d.EffectId == "swing");
    }
}
