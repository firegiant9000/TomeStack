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

    private static readonly Guid SlingId = Guid.Parse("c0c00000-0000-4000-8000-000000000a01");
    private static readonly Guid PlainId = Guid.Parse("c0c00000-0000-4000-8000-000000000a02");
    private const string Marker = "ZQXMARKERZQX";

    private static (TempApp Temp, Guid Id) WithCustomAttacks(string name = "Testy Sling")
    {
        var (temp, id) = Berserker3();
        var character = temp.App.Store.FindCharacter(id)! with
        {
            CustomAttacks = [new(SlingId, name, 5, "1d6+2", "bludgeoning", null), new(PlainId, "Testy Poke", -1, null, null, null)],
        };
        temp.App.SaveCharacter(character);
        return (temp, id);
    }

    [Fact]
    public void A_custom_attack_rolls_d20_plus_its_typed_to_hit_under_advantage_and_disadvantage()
    {
        var (temp, id) = WithCustomAttacks();
        using var _ = temp;

        var advantage = temp.App.Roll(new RollCommand(id, CustomAttack: SlingId, Mode: RollMode.Advantage));
        var disadvantage = temp.App.Roll(new RollCommand(id, CustomAttack: SlingId, Mode: RollMode.Disadvantage));

        Assert.Equal("1d20", advantage.Formula);
        Assert.Equal(2, advantage.Dice.Count);
        var kept = Assert.Single(advantage.Dice, d => d.Kept);
        Assert.Equal(advantage.Dice.Max(d => d.Value), kept.Value);
        var modifier = Assert.Single(advantage.Modifiers);
        Assert.Equal(("Testy Sling to hit", 5), (modifier.Label, modifier.Amount));
        Assert.Null(modifier.Origin);
        Assert.Equal(kept.Value + 5, advantage.Total);
        Assert.Equal(("custom.attack", "Testy Sling attack"), (advantage.Provenance!.RollId, advantage.Provenance.Label));
        Assert.Null(advantage.Provenance.Content);
        Assert.Null(advantage.Provenance.SourceId);
        var lowest = Assert.Single(disadvantage.Dice, d => d.Kept);
        Assert.Equal(disadvantage.Dice.Min(d => d.Value), lowest.Value);
        var lowModifier = Assert.Single(disadvantage.Modifiers);
        Assert.Equal(("Testy Sling to hit", 5), (lowModifier.Label, lowModifier.Amount));
        Assert.Equal(lowest.Value + 5, disadvantage.Total);
    }

    [Fact]
    public void A_custom_attack_damage_rolls_its_dice_and_a_critical_doubles_them()
    {
        var (temp, id) = WithCustomAttacks();
        using var _ = temp;

        var normal = temp.App.Roll(new RollCommand(id, CustomAttack: SlingId, Damage: true));
        var critical = temp.App.Roll(new RollCommand(id, CustomAttack: SlingId, Damage: true, Critical: true));

        Assert.Equal("1d6+2", normal.Formula);
        Assert.Single(normal.Dice);
        Assert.Equal(2, normal.ExpressionConstant);
        Assert.Equal(2, critical.Dice.Count);
        Assert.Equal(1, critical.Dice.Count(d => d.FromCritical));
        Assert.Equal(2, critical.ExpressionConstant); // the flat part is not doubled
        Assert.Equal(critical.DiceTotal + 2, critical.Total);
        Assert.Equal(("custom.damage", "Testy Sling damage (bludgeoning)"), (critical.Provenance!.RollId, critical.Provenance.Label));
    }

    [Fact]
    public void A_custom_attack_without_a_damage_type_labels_its_damage_without_parentheses()
    {
        var (temp, id) = WithCustomAttacks();
        using var _ = temp;
        var character = temp.App.Store.FindCharacter(id)! with { CustomAttacks = [new(SlingId, "Testy Sling", 0, "1d4", null, null)] };
        temp.App.SaveCharacter(character);

        Assert.Equal("Testy Sling damage", temp.App.Roll(new RollCommand(id, CustomAttack: SlingId, Damage: true)).Provenance!.Label);
    }

    [Fact]
    public void Custom_attack_refusals_never_quote_the_name()
    {
        var (temp, id) = WithCustomAttacks(Marker);
        using var _ = temp;
        AppValidationException Refused(RollCommand command) => Assert.Throws<AppValidationException>(() => temp.App.Roll(command));

        var unknown = Refused(new(id, CustomAttack: Guid.NewGuid()));
        var noDamage = Refused(new(id, CustomAttack: PlainId, Damage: true));
        var ambiguous = Refused(new(id, Field: FieldIds.Initiative, CustomAttack: SlingId));

        Assert.Equal("roll.custom-attack-unknown", Assert.Single(unknown.Problems).Code);
        Assert.Equal("roll.custom-attack-no-damage", Assert.Single(noDamage.Problems).Code);
        Assert.Equal("roll.ambiguous", Assert.Single(ambiguous.Problems).Code);
        Assert.Contains("your own attack", ambiguous.Problems[0].Message);
        foreach (var error in new[] { unknown, noDamage, ambiguous })
            Assert.DoesNotContain(Marker, JsonSerializer.Serialize(error.Problems, RulesJson.Compact) + error);
    }

    [Fact]
    public void A_custom_attack_with_a_weapon_is_ambiguous()
    {
        var (temp, id) = WithCustomAttacks();
        using var _ = temp;

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Roll(new RollCommand(id, Weapon: Srd(99), CustomAttack: SlingId)));

        Assert.Equal("roll.ambiguous", Assert.Single(refused.Problems).Code);
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

    // D43 S5 (Task 5.3): per-unit amounts and the spend a roll implies (fixture-pack-action-details.json).
    private static readonly Guid AdeptClass = Guid.Parse("6f0dc000-0000-4000-8000-000000000050");
    private static readonly ContentReference TideWave = new(Guid.Parse("6f0dc000-0000-4000-8000-000000000053"), Guid.Parse("6f0de000-0000-4000-8000-000000000053"));

    private static (TempApp Temp, Guid Id) WaveAdept(int spentFocus = 0, Func<ContentRevision, ContentRevision>? adjust = null)
    {
        var temp = new TempApp();
        temp.App.Random = new SeededRandomSource(9);
        temp.AddPack("fixture-pack-action-details.json", adjust);
        var id = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/pm-wave-adept-l5.json")).Character.Id;
        if (spentFocus > 0)
            temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, ContentId: AdeptClass, ResourceId: "focus", Amount: spentFocus));
        return (temp, id);
    }

    private static string Code(TempApp temp, RollCommand command) => Assert.Throws<AppValidationException>(() => temp.App.Roll(command)).Code;

    [Fact]
    public void PerUnit_dice_match_the_amount_spent()
    {
        var (temp, id) = WaveAdept();
        using var _ = temp;
        var before = TempApp.Json(temp.App.Store.FindCharacter(id));

        var record = temp.App.Roll(new RollCommand(id, TideWave, "wave", Amount: 4));

        Assert.Equal("4d10+2d10", record.Formula);
        Assert.Equal(6, record.Dice.Count);
        Assert.Equal(new RollSpend(AdeptClass, "focus", 4), record.Spend);
        Assert.Equal(before, TempApp.Json(temp.App.Store.FindCharacter(id))); // rolling spends nothing
    }

    [Fact]
    public void A_per_unit_roll_without_amount_rolls_the_base()
    {
        var (temp, id) = WaveAdept();
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, TideWave, "wave"));

        Assert.Equal("4d10", record.Formula);
        Assert.Equal(2, record.Spend!.Amount);
        Assert.Equal("focus", record.Provenance!.LinkedResourceId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void PerUnit_amount_out_of_range_is_refused(int amount)
    {
        var (temp, id) = WaveAdept();
        using var _ = temp;

        Assert.Equal("roll.amount-out-of-range", Code(temp, new RollCommand(id, TideWave, "wave", Amount: amount)));
    }

    [Fact]
    public void A_per_unit_roll_the_pool_cannot_pay_is_refused()
    {
        var (temp, id) = WaveAdept(spentFocus: 6); // focus 9, 3 left
        using var _ = temp;

        Assert.Equal("roll.amount-unaffordable", Code(temp, new RollCommand(id, TideWave, "wave", Amount: 4)));
        Assert.Equal("4d10", temp.App.Roll(new RollCommand(id, TideWave, "wave")).Formula); // no amount: the base rolls, unchecked
    }

    [Fact]
    public void Rolling_a_dice_less_action_is_refused()
    {
        var (temp, id) = WaveAdept();
        using var _ = temp;

        Assert.Equal("roll.no-dice", Code(temp, new RollCommand(id, TideWave, "brace")));
        Assert.Equal("roll.no-dice", Code(temp, new RollCommand(id, TideWave, "brace", Amount: 2)));
    }

    [Fact]
    public void An_amount_on_a_roll_without_per_unit_is_not_allowed()
    {
        var (temp, id) = WaveAdept(adjust: r => r with { Effects = [.. r.Effects.Select(e => e is RollEffect { Id: "wave" } w ? w with { PerUnit = null } : e)] });
        using var _ = temp;

        Assert.Equal("roll.amount-not-allowed", Code(temp, new RollCommand(id, TideWave, "wave", Amount: 3)));
    }

    [Fact]
    public void A_failing_cost_refuses_an_amount_and_rolls_without_one()
    {
        var (temp, id) = WaveAdept(adjust: r => r with { Effects = [.. r.Effects.Select(e => e is RollEffect { Id: "wave" } w ? w with { Cost = "NOPE.X" } : e)] });
        using var _ = temp;

        Assert.Equal("effect.invalid-formula", Code(temp, new RollCommand(id, TideWave, "wave", Amount: 3)));
        var record = temp.App.Roll(new RollCommand(id, TideWave, "wave"));
        Assert.Equal("4d10", record.Formula);
        Assert.Null(record.Spend);
    }

    private static Func<ContentRevision, ContentRevision> ChangeWave(Func<RollEffect, RollEffect> change) =>
        r => r with { Effects = [.. r.Effects.Select(e => e is RollEffect { Id: "wave" } w ? change(w) : e)] };

    [Fact]
    public void The_acceptance_technique_rolls_and_spends_the_same_amount()
    {
        var (temp, id) = WaveAdept();
        using var _ = temp;
        var wave = temp.App.GetCharacter(id).Sheet.Features!.Single(f => f.Content == TideWave).Effects.Single(e => e.Id == "wave");
        Assert.Equal((13, Ability.Dex), (wave.SaveDc, wave.SaveAbility)); // 8 + PB 3 + Wisdom 2, from VALUE.focusMod
        Assert.Equal(new RollArea(AreaShape.Line, 100, 5), wave.Area);
        var before = TempApp.Json(temp.App.Store.FindCharacter(id));

        var record = temp.App.Roll(new RollCommand(id, TideWave, "wave", Amount: 4));

        Assert.Equal("4d10+2d10", record.Formula);
        var spend = record.Spend!;
        Assert.Equal(new RollSpend(AdeptClass, "focus", 4), spend);
        Assert.Equal(before, TempApp.Json(temp.App.Store.FindCharacter(id))); // the roll itself changes nothing

        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, ContentId: spend.ContentId, ResourceId: spend.ResourceId, Amount: spend.Amount));

        var focus = temp.App.GetCharacter(id).Sheet.Resources!.Single(r => r.ResourceId == "focus");
        Assert.Equal((9, 5), (focus.Maximum, focus.Current)); // 9 - 4
    }

    [Fact]
    public void An_untracked_pool_refuses_an_amount()
    {
        var (temp, id) = WaveAdept(adjust: r => r.Kind == ContentKind.Class
            ? r with { Effects = [.. r.Effects.Select(e => e is ResourceEffect { ResourceId: "focus" } p ? p with { Maximum = "NOPE.X" } : e)] }
            : r);
        using var _ = temp;

        Assert.Equal("roll.amount-unaffordable", Code(temp, new RollCommand(id, TideWave, "wave", Amount: 3)));
    }

    [Fact]
    public void Without_max_units_the_pool_maximum_caps_the_amount()
    {
        var (temp, id) = WaveAdept(adjust: ChangeWave(w => w with { PerUnit = new RollPerUnit("1d10", null) }));
        using var _ = temp;

        Assert.Equal("roll.amount-out-of-range", Code(temp, new RollCommand(id, TideWave, "wave", Amount: 10))); // focus maximum is 9
        var record = temp.App.Roll(new RollCommand(id, TideWave, "wave", Amount: 9));
        Assert.Equal("4d10+7d10", record.Formula);
        Assert.Equal(9, record.Spend!.Amount);
    }

    [Fact]
    public void An_explicit_amount_equal_to_the_base_rolls_the_base_dice()
    {
        var (temp, id) = WaveAdept();
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, TideWave, "wave", Amount: 2));

        Assert.Equal("4d10", record.Formula);
        Assert.Equal(2, record.Spend!.Amount);
    }

    [Theory]
    [InlineData("1d10+1")]
    [InlineData("1d10+1d6")]
    [InlineData("-1d10")]
    [InlineData("nonsense")]
    public void A_per_unit_die_that_is_not_one_plain_die_term_is_refused_not_ignored(string perUnitDice)
    {
        // Unreachable for validated content: the roll must not show the base dice while spending extra units.
        var (temp, id) = WaveAdept(adjust: ChangeWave(w => w with { PerUnit = new RollPerUnit(perUnitDice, 4) }));
        using var _ = temp;

        Assert.Equal("roll.amount-out-of-range", Code(temp, new RollCommand(id, TideWave, "wave", Amount: 4)));
    }

    [Fact]
    public void A_failing_save_dc_does_not_stop_a_roll_whose_bonus_works()
    {
        var (temp, id) = WaveAdept(adjust: ChangeWave(w => w with { Bonus = "PB", Save = new RollSave(Ability.Dex, "8 + VALUE.nonesuch") }));
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, TideWave, "wave"));

        Assert.Equal("4d10", record.Formula);
        Assert.Contains(record.Modifiers, m => m.Amount == 3);
    }

    [Fact]
    public void A_refused_bonus_carries_the_bonus_own_diagnostic_when_the_dc_fails_too()
    {
        var (temp, id) = WaveAdept(adjust: ChangeWave(w => w with { Bonus = "BONUSBAD.X", Save = new RollSave(Ability.Dex, "8 + DCBAD.X") }));
        using var _ = temp;

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Roll(new RollCommand(id, TideWave, "wave")));

        Assert.Equal("roll.bonus-invalid", refused.Problems[0].Code);
        Assert.Contains("BONUSBAD", refused.Problems[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("DCBAD", refused.Problems[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_costed_roll_without_per_unit_reports_its_spend()
    {
        using var temp = new TempApp();
        temp.App.Random = new SeededRandomSource(3);
        temp.AddPack("fixture-pack-m3-effects.json");
        var stance = new ContentReference(Guid.Parse("5f8dc000-0000-4000-8000-000000000001"), Guid.Parse("5f8de000-0000-4000-8000-000000000001"));
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        var id = temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins ?? [], stance] }).Character.Id;

        var surge = temp.App.Roll(new RollCommand(id, stance, "surge"));
        var flare = temp.App.Roll(new RollCommand(id, stance, "flare"));

        Assert.Equal(new RollSpend(stance.ContentId, "radiance", 2), surge.Spend); // variable cost: the most it may spend (PB)
        Assert.Equal(new RollSpend(stance.ContentId, "radiance", 1), flare.Spend);
    }

    // D43 S6 (Task 6.4): field rolls combine the player's choice with the sheet's automatic roll mode (fixture-pack-roll-modes.json).
    private static ContentReference Mode(int n) => new(Guid.Parse($"6f0dc000-0000-4000-8000-{n:X12}"), Guid.Parse($"6f0de000-0000-4000-8000-{n:X12}"));

    private static readonly ContentReference SwiftInstinct = Mode(0x60);
    private static readonly ContentReference HeavyStance = Mode(0x61);
    private static readonly ContentReference LightStep = Mode(0x63);
    private static readonly ContentReference WeightedGear = Mode(0x64);

    /// <summary>Testy Instinct (level 4) with the roll-modes pack; <paramref name="edit"/> changes the stored character before it is saved.</summary>
    private static (TempApp Temp, Guid Id) Instinct(Func<Character, Character>? edit = null)
    {
        var temp = new TempApp();
        temp.App.Random = new SeededRandomSource(5);
        temp.AddPack("fixture-pack-roll-modes.json");
        var character = TempApp.LoadFixture<Character>("characters/pm-instinct-l4.json");
        return (temp, temp.App.SaveCharacter(edit is null ? character : edit(character)).Character.Id);
    }

    private static Character Pinned(Character character, params ContentReference[] pins) => character with { Pins = [.. pins] };

    private static Character WithoutSaveDexMode(Character character) =>
        Pinned(character, HeavyStance, Mode(0x62));

    private static Character HeavyOn(Character character) =>
        character with { Play = character.Play with { Toggles = [new ActiveToggle(HeavyStance.ContentId, "heavy")] } };

    private static Character AutomaticState(Character character, RollMode automatic) => automatic switch
    {
        RollMode.Normal => WithoutSaveDexMode(character),
        RollMode.Advantage => character,
        _ => Pinned(character, HeavyStance, Mode(0x62), WeightedGear),
    };

    [Theory]
    [InlineData(RollMode.Normal, RollMode.Normal, RollMode.Normal)]
    [InlineData(RollMode.Normal, RollMode.Advantage, RollMode.Advantage)]
    [InlineData(RollMode.Normal, RollMode.Disadvantage, RollMode.Disadvantage)]
    [InlineData(RollMode.Advantage, RollMode.Normal, RollMode.Advantage)]
    [InlineData(RollMode.Advantage, RollMode.Advantage, RollMode.Advantage)]
    [InlineData(RollMode.Advantage, RollMode.Disadvantage, RollMode.Normal)]
    [InlineData(RollMode.Disadvantage, RollMode.Normal, RollMode.Disadvantage)]
    [InlineData(RollMode.Disadvantage, RollMode.Advantage, RollMode.Normal)]
    [InlineData(RollMode.Disadvantage, RollMode.Disadvantage, RollMode.Disadvantage)]
    public void The_players_choice_combines_with_the_automatic_mode(RollMode request, RollMode automatic, RollMode expected)
    {
        var (temp, id) = Instinct(c => AutomaticState(c, automatic));
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: "save.dex", Mode: request));

        Assert.Equal(expected, record.Mode);
        Assert.Equal(expected == RollMode.Normal ? 1 : 2, record.Dice.Count);
        if (automatic == RollMode.Normal)
        {
            // No automatic reason on save.dex at all: the record says nothing about an automatic mode.
            Assert.Null(record.AutomaticMode);
            Assert.Null(record.ModeSources);
        }
        else
        {
            Assert.Equal(automatic, record.AutomaticMode);
            string[] sources = automatic == RollMode.Advantage ? ["Fixture Swift Instinct", "Fixture Light Step"] : ["Fixture Weighted Gear"];
            Assert.Equal(sources, record.ModeSources);
        }
    }

    [Fact]
    public void Automatic_cancellation_beats_the_players_choice()
    {
        var (temp, id) = Instinct(c => Pinned(c, SwiftInstinct, HeavyStance, Mode(0x62), LightStep, WeightedGear));
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: "save.dex", Mode: RollMode.Advantage));

        Assert.Equal(RollMode.Normal, record.Mode);
        Assert.Single(record.Dice);
        Assert.Equal(RollMode.Normal, record.AutomaticMode);
        Assert.Equal(["Fixture Swift Instinct", "Fixture Light Step", "Fixture Weighted Gear"], record.ModeSources);
    }

    [Fact]
    public void A_critical_field_roll_with_an_automatic_advantage_is_refused()
    {
        var (temp, id) = Instinct();
        using var _ = temp;

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Roll(new RollCommand(id, Field: "save.dex", Critical: true)));

        Assert.Equal("dice.critical-with-advantage", Assert.Single(refused.Problems).Code);
    }

    [Fact]
    public void An_assisted_mode_never_changes_the_roll()
    {
        var (temp, id) = Instinct();
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: "save.con"));

        Assert.Equal(RollMode.Normal, record.Mode);
        Assert.Single(record.Dice);
        Assert.Null(record.AutomaticMode);
        Assert.Null(record.ModeSources);
    }

    [Fact]
    public void A_skill_takes_its_ability_check_mode()
    {
        var (temp, id) = Instinct(HeavyOn);
        using var _ = temp;

        var athletics = temp.App.Roll(new RollCommand(id, Field: "skill.athletics"));
        var stealth = temp.App.Roll(new RollCommand(id, Field: "skill.stealth"));

        Assert.Equal((RollMode.Advantage, 2), (athletics.Mode, athletics.Dice.Count));
        Assert.Equal(["Fixture Heavy Stance"], athletics.ModeSources);
        Assert.Equal((RollMode.Disadvantage, 2), (stealth.Mode, stealth.Dice.Count));
        Assert.Equal(RollMode.Disadvantage, stealth.AutomaticMode);
    }

    [Fact]
    public void Rolling_never_changes_the_character_with_a_roll_mode()
    {
        var (temp, id) = Instinct(HeavyOn);
        using var _ = temp;
        var before = TempApp.Json(temp.App.Store.FindCharacter(id));

        temp.App.Roll(new RollCommand(id, Field: "save.dex", Mode: RollMode.Disadvantage));
        temp.App.Roll(new RollCommand(id, Field: "skill.stealth"));

        Assert.Equal(before, TempApp.Json(temp.App.Store.FindCharacter(id)));
    }

    [Fact]
    public void A_roll_without_automatic_modes_serialises_as_before()
    {
        var (temp, id) = Instinct();
        using var _ = temp;

        var json = TempApp.Json(temp.App.Roll(new RollCommand(id, Field: "save.str")));

        Assert.DoesNotContain("automaticMode", json);
        Assert.DoesNotContain("modeSources", json);
    }

    // D72 (Task 1): a roll carries the sheet's trace for the number rolled.
    [Fact]
    public void A_field_roll_explains_its_modifier_with_the_fields_trace()
    {
        var (temp, id) = Instinct();
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: FieldIds.Save(Ability.Dex)));

        var field = temp.App.GetCharacter(id).Sheet.Field(FieldIds.Save(Ability.Dex));
        Assert.Equal(TempApp.Json(field.Trace), TempApp.Json(record.Explain));
        Assert.Equal(record.Modifiers.Sum(m => m.Amount), record.Explain![^1].Result);
    }

    [Fact]
    public void An_overridden_field_roll_ends_its_explanation_with_the_override()
    {
        var (temp, id) = Instinct(c => c with { Overrides = [.. c.Overrides, new FieldOverride(FieldIds.Save(Ability.Dex), 9, "test")] });
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: FieldIds.Save(Ability.Dex)));

        Assert.Equal("override", record.Explain![^1].Operation);
        Assert.Equal(9, record.Explain[^1].Result);
        Assert.Equal(9, record.Modifiers.Sum(m => m.Amount));
    }

    [Fact]
    public void Rolls_without_a_sheet_number_have_no_explanation()
    {
        var (temp, id) = Instinct();
        using var _ = temp;

        Assert.Null(temp.App.Roll(new RollCommand(id, DeathSave: true)).Explain);

        // The roll-modes pack declares no hit dice, so the hit die is rolled on the action-details fixture.
        var (wave, waveId) = WaveAdept();
        using var __ = wave;
        var die = wave.App.GetCharacter(waveId).Sheet.HitDice![0].Die;
        Assert.Null(wave.App.Roll(new RollCommand(waveId, HitDie: die)).Explain);
    }

    private static bool IsAutomaticD20(DerivedValue f) =>
        f.Automation == AutomationStatus.Automatic
        && (f.Field == FieldIds.Initiative
            || f.Field.StartsWith("save.", StringComparison.Ordinal)
            || f.Field.StartsWith("skill.", StringComparison.Ordinal)
            || (f.Field.StartsWith("ability.", StringComparison.Ordinal) && f.Field.EndsWith(".mod", StringComparison.Ordinal)));

    private static void AssertEveryD20RollEndsAtItsModifierTotal(TempApp temp, Guid id)
    {
        var sheet = temp.App.GetCharacter(id).Sheet;
        var records = new List<(string What, RollRecord Record)>();
        foreach (var f in sheet.Fields.Where(IsAutomaticD20))
            records.Add((f.Field, temp.App.Roll(new RollCommand(id, Field: f.Field))));
        foreach (var a in sheet.Attacks ?? [])
            records.Add(($"attack {a.Name}", a.Source == AttackSource.Weapon
                ? temp.App.Roll(new RollCommand(id, Weapon: a.Item))
                : temp.App.Roll(new RollCommand(id, Content: a.Item, EffectId: a.EffectId))));
        Assert.NotEmpty(records);
        foreach (var (what, record) in records)
        {
            Assert.True(record.Explain is { Count: > 0 }, $"{what} has no explanation");
            Assert.True(record.Explain![^1].Result == record.Modifiers.Sum(m => m.Amount), $"{what}: explanation ends at {record.Explain[^1].Result}, modifiers total {record.Modifiers.Sum(m => m.Amount)}");
        }
    }

    [Fact]
    public void Every_automatic_d20_roll_in_the_play_fixtures_ends_its_explanation_at_the_modifier_total()
    {
        using (var temp = new TempApp())
        {
            temp.App.Random = new SeededRandomSource(5);
            temp.AddPack("fixture-pack-feature-attacks.json");
            var id = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/pm-adept-l5.json")).Character.Id;
            var kindled = new ContentReference(Guid.Parse("6f0dc000-0000-4000-8000-000000000042"), Guid.Parse("6f0de000-0000-4000-8000-000000000042"));
            temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: kindled.ContentId, ToggleId: "kindled"));
            AssertEveryD20RollEndsAtItsModifierTotal(temp, id);
        }

        var (wave, waveId) = WaveAdept();
        using (wave)
            AssertEveryD20RollEndsAtItsModifierTotal(wave, waveId);

        var (instinct, instinctId) = Instinct();
        using (instinct)
            AssertEveryD20RollEndsAtItsModifierTotal(instinct, instinctId);
    }
}
