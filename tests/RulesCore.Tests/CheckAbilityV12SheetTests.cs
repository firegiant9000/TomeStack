namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G10), Task 5: a <c>checkAbility</c> effect on the calculated sheet. A skill uses, among its printed ability and the
/// abilities content lets it use, the one with the best automatic roll mode, then the higher modifier (a tie keeps the printed one); roll modes and floors follow the ability it ends up using. Original fixtures.
/// </summary>
public class CheckAbilityV12SheetTests
{
    private static readonly Guid SourceId = Guid.Parse("6a7e0000-0000-4000-8000-000000000001");

    private static InMemoryContentCatalog Catalog(params ContentRevision[] extra)
    {
        var pack = Fixtures.CompanionsPack();
        return new(pack.Sources, [.. pack.Revisions, .. extra]);
    }

    private static Character Handler(AbilityScores scores) => Fixtures.Load("pm-handler-l3.json") with { BaseAbilities = scores };

    private static ContentRevision Feature(string name, int n, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"6a7e00a5-0000-4000-8000-{0x200 + n:X12}"), RevisionId = Guid.Parse($"6a7e00a5-0000-4000-8000-{0x300 + n:X12}"),
        SchemaVersion = 12, Kind = ContentKind.Feature, Name = name, RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(SourceId), Status = RevisionStatus.Published, Effects = effects,
    };

    private static CheckAbilityEffect Use(string id, string target, string ability) => new() { Id = id, Target = target, Ability = ability };

    private static CharacterSheet Sheet(Character character, params ContentRevision[] extra) =>
        CharacterCalculator.Calculate(character with { Pins = [.. character.Pins, .. extra.Select(e => e.Reference)] }, Catalog(extra));

    private static readonly string Athletics = FieldIds.Skill("athletics");

    // Modifiers: STR 10 = 0, DEX 16 = +3, CON 14 = +2, INT 8 = -1, WIS 12 = +1, CHA 10 = 0.
    private static readonly AbilityScores LowStrHighDex = new(10, 16, 10, 10, 10, 10);

    private static List<(string Field, string Operation, string Description, int? Amount, int Result, string Inputs)> Trace(DerivedValue field) =>
        [.. field.Trace.Select(t => (t.Field ?? "", t.Operation, t.Description, t.Amount, t.Result, string.Join(",", (t.Inputs ?? []).Select(i => $"{i.Name}={i.Value}"))))];

    [Fact]
    public void Dex_athletics_uses_the_higher_dex_with_a_trace_step()
    {
        var baseline = Sheet(Handler(LowStrHighDex));
        var sheet = Sheet(Handler(LowStrHighDex), Feature("Fixture Quickstep", 1, Use("u", Athletics, "dex")));

        var bonus = baseline.Field(Athletics).Value - baseline.Field("ability.str.mod").Value;
        var athletics = sheet.Field(Athletics);
        Assert.Equal(3 + bonus, athletics.Value);
        var step = athletics.Trace.Single(t => t.Field == Athletics && t.Operation == "base");
        Assert.Equal("Athletics starts at the Dexterity modifier (Fixture Quickstep lets you use Dexterity instead of Strength)", step.Description);
        Assert.Equal(TraceOriginKind.Content, step.Origin.Kind);
        Assert.Equal("u", step.Origin.EffectId);
        Assert.Equal(new TraceInput("ability.dex.mod", 3), Assert.Single(step.Inputs!));
        Assert.DoesNotContain(athletics.Trace, t => t.Field == "ability.str.mod");
    }

    [Fact]
    public void Lower_substitute_changes_nothing()
    {
        var scores = new AbilityScores(16, 10, 10, 10, 10, 10);
        var baseline = Sheet(Handler(scores));
        var sheet = Sheet(Handler(scores), Feature("Fixture Quickstep", 2, Use("u", Athletics, "dex")));

        // Value, step text and inputs of every field are exactly the baseline's (Review Focus 1 and 2).
        foreach (var field in baseline.Fields)
        {
            Assert.Equal(field.Value, sheet.Field(field.Field).Value);
            Assert.Equal(Trace(field), Trace(sheet.Field(field.Field)));
        }
        var step = sheet.Field(Athletics).Trace.Single(t => t.Field == Athletics && t.Operation == "base");
        Assert.Equal("Athletics starts at the Strength modifier", step.Description);
        Assert.Equal(TraceOriginKind.RulesPolicy, step.Origin.Kind);
        Assert.Equal(new TraceInput("ability.str.mod", 3), Assert.Single(step.Inputs!));
    }

    [Fact]
    public void Tie_keeps_the_printed_ability()
    {
        var scores = new AbilityScores(14, 14, 10, 10, 10, 10);
        var sheet = Sheet(Handler(scores), Feature("Fixture Quickstep", 3, Use("u", Athletics, "dex")));

        var step = sheet.Field(Athletics).Trace.Single(t => t.Field == Athletics && t.Operation == "base");
        Assert.Equal("Athletics starts at the Strength modifier", step.Description);
        Assert.Equal(TraceOriginKind.RulesPolicy, step.Origin.Kind);
    }

    [Theory]
    [InlineData("dex", "con")]
    [InlineData("con", "dex")]
    public void Two_candidates_the_highest_wins(string first, string second)
    {
        var scores = new AbilityScores(8, 14, 16, 10, 10, 10);
        var sheet = Sheet(Handler(scores), Feature("Fixture Quickstep", 4, Use("a", Athletics, first), Use("b", Athletics, second)));

        var step = sheet.Field(Athletics).Trace.Single(t => t.Field == Athletics && t.Operation == "base");
        Assert.Equal(new TraceInput("ability.con.mod", 3), Assert.Single(step.Inputs!));
        Assert.Contains("Constitution", step.Description);
        Assert.Equal(first == "con" ? "a" : "b", step.Origin.EffectId);
    }

    [Fact]
    public void Ability_check_target_reaches_every_skill_of_that_printed_ability_only()
    {
        var scores = new AbilityScores(10, 10, 10, 16, 8, 10);
        var baseline = Sheet(Handler(scores));
        var sheet = Sheet(Handler(scores), Feature("Fixture Lore", 5, Use("u", "ability.wis.check", "int")));

        foreach (var skill in CharacterCalculator.Skills)
        {
            var field = FieldIds.Skill(skill.Key);
            var expected = skill.Ability == Ability.Wis ? baseline.Field(field).Value + 4 : baseline.Field(field).Value;
            Assert.Equal(expected, sheet.Field(field).Value);
        }
        // The bare ability check, initiative and saves are untouched.
        Assert.Equal(baseline.Field("ability.wis.mod").Value, sheet.Field("ability.wis.mod").Value);
        Assert.Equal(baseline.Field("save.wis").Value, sheet.Field("save.wis").Value);
    }

    [Fact]
    public void Passive_perception_follows_a_substituted_perception()
    {
        var scores = new AbilityScores(10, 10, 10, 16, 8, 10);
        var baseline = Sheet(Handler(scores));
        var sheet = Sheet(Handler(scores), Feature("Fixture Lore", 6, Use("u", FieldIds.Skill("perception"), "int")));

        Assert.Equal(baseline.Field(FieldIds.Passive("perception")).Value + 4, sheet.Field(FieldIds.Passive("perception")).Value);
        Assert.Equal(sheet.Field(FieldIds.Skill("perception")).Value + 10, sheet.Field(FieldIds.Passive("perception")).Value);
    }

    [Fact]
    public void Str_check_disadvantage_skips_dex_athletics_and_dex_check_advantage_reaches_it()
    {
        var disadvantage = new RollModeEffect { Id = "r", Target = "ability.str.check", Mode = RollMode.Disadvantage };
        var advantage = new RollModeEffect { Id = "r2", Target = "ability.dex.check", Mode = RollMode.Advantage };
        var quickstep = Feature("Fixture Quickstep", 7, Use("u", Athletics, "dex"));
        var modes = Feature("Fixture Modes", 8, disadvantage, advantage);

        var sheet = Sheet(Handler(LowStrHighDex), quickstep, modes);

        Assert.Equal(RollMode.Advantage, sheet.Field(Athletics).Mode!.Automatic);
        Assert.Equal(RollMode.Disadvantage, sheet.Field("ability.str.mod").Mode!.Automatic);

        // Without the substitution the printed ability keeps the disadvantage.
        var plain = Sheet(Handler(LowStrHighDex), modes);
        Assert.Equal(RollMode.Disadvantage, plain.Field(Athletics).Mode!.Automatic);
    }

    // STR 14 = +2, DEX 16 = +3.
    private static readonly AbilityScores StrBehindDex = new(14, 16, 10, 10, 10, 10);

    private static TraceInput AthleticsBase(CharacterSheet sheet) =>
        Assert.Single(sheet.Field(Athletics).Trace.Single(t => t.Field == Athletics && t.Operation == "base").Inputs!);

    [Fact]
    public void Mode_beats_modifier_a_str_check_advantage_keeps_str_athletics()
    {
        var rule = Feature("Fixture Quickstep", 20, Use("u", Athletics, "dex"));
        var strAdvantage = Feature("Fixture Surge Mode", 21, new RollModeEffect { Id = "r", Target = "ability.str.check", Mode = RollMode.Advantage });

        var sheet = Sheet(Handler(StrBehindDex), rule, strAdvantage);

        Assert.Equal(RollMode.Advantage, sheet.Field(Athletics).Mode!.Automatic);
        Assert.Equal(new TraceInput("ability.str.mod", 2), AthleticsBase(sheet));

        // Not raging (the mode is absent): the higher Dexterity modifier wins.
        Assert.Equal(new TraceInput("ability.dex.mod", 3), AthleticsBase(Sheet(Handler(StrBehindDex), rule)));
    }

    [Fact]
    public void Dex_check_disadvantage_makes_the_lower_str_win()
    {
        var rule = Feature("Fixture Quickstep", 22, Use("u", Athletics, "dex"));
        var dexDisadvantage = Feature("Fixture Drag Mode", 23, new RollModeEffect { Id = "r", Target = "ability.dex.check", Mode = RollMode.Disadvantage });

        var sheet = Sheet(Handler(StrBehindDex), rule, dexDisadvantage);

        Assert.Equal(new TraceInput("ability.str.mod", 2), AthleticsBase(sheet));
    }

    [Fact]
    public void Equal_mode_and_equal_modifier_keep_the_printed_ability()
    {
        var rule = Feature("Fixture Quickstep", 24, Use("u", Athletics, "dex"));
        var both = Feature("Fixture Both Modes", 25,
            new RollModeEffect { Id = "r1", Target = "ability.str.check", Mode = RollMode.Advantage },
            new RollModeEffect { Id = "r2", Target = "ability.dex.check", Mode = RollMode.Advantage });

        var sheet = Sheet(Handler(new AbilityScores(14, 14, 10, 10, 10, 10)), rule, both);

        Assert.Equal(TraceOriginKind.RulesPolicy, sheet.Field(Athletics).Trace.Single(t => t.Field == Athletics && t.Operation == "base").Origin.Kind);
        Assert.Equal(new TraceInput("ability.str.mod", 2), AthleticsBase(sheet));
    }

    [Fact]
    public void A_user_override_on_the_dex_modifier_changes_the_choice()
    {
        var rule = Feature("Fixture Quickstep", 26, Use("u", Athletics, "dex"));
        var character = Handler(StrBehindDex) with { Overrides = [new FieldOverride("ability.dex.mod", 1, "test")] };

        Assert.Equal(new TraceInput("ability.str.mod", 2), AthleticsBase(Sheet(character, rule)));
    }

    [Fact]
    public void Dex_check_roll_floor_reaches_dex_athletics()
    {
        var floor = new RollFloorEffect { Id = "f", Target = "ability.dex.check", Face = 8 };
        var strFloor = new RollFloorEffect { Id = "f2", Target = "ability.str.check", Face = 5 };
        var quickstep = Feature("Fixture Quickstep", 9, Use("u", Athletics, "dex"));

        var sheet = Sheet(Handler(LowStrHighDex), quickstep, Feature("Fixture Floors", 10, floor, strFloor));

        Assert.Equal(8, sheet.Field(Athletics).Floor!.Face);
        Assert.Equal(5, sheet.Field("ability.str.mod").Floor!.Face);
    }

    [Fact]
    public void Self_cycle_on_the_candidates_score_is_removed_and_the_substitution_still_holds()
    {
        // The formula grammar reads only ability modifiers and scores, so no modifier can depend on a skill and a true
        // skill cycle cannot be authored; the closest case is a modifier on the candidate's own score reading its modifier.
        var selfCycle = new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = "ability.dex.score", Value = "DEX.MOD" };
        var sheet = Sheet(Handler(LowStrHighDex), Feature("Fixture Quickstep", 11, Use("u", Athletics, "dex"), selfCycle));

        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.dependency-cycle");
        var athletics = sheet.Field(Athletics);
        Assert.Equal(3, sheet.Field("ability.dex.mod").Value);
        Assert.Equal(new TraceInput("ability.dex.mod", 3), Assert.Single(athletics.Trace.Single(t => t.Field == Athletics && t.Operation == "base").Inputs!));
        Assert.Contains("Dexterity", athletics.Trace.Single(t => t.Field == Athletics && t.Operation == "base").Description);
    }

    [Fact]
    public void Chained_modifier_on_the_candidates_score_orders_before_the_skill()
    {
        // A modifier on dex.score: Athletics must be computed after dex.mod has the bonus.
        var chained = new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = "ability.dex.score", Value = "4" };
        var sheet = Sheet(Handler(LowStrHighDex), Feature("Fixture Quickstep", 12, Use("u", Athletics, "dex"), chained));

        var dex = sheet.Field("ability.dex.mod").Value;
        Assert.Equal(5, dex);
        var step = sheet.Field(Athletics).Trace.Single(t => t.Field == Athletics && t.Operation == "base");
        Assert.Equal(new TraceInput("ability.dex.mod", 5), Assert.Single(step.Inputs!));
    }

    [Theory]
    [InlineData(AutomationStatus.Reference, EffectTiming.Always)]
    [InlineData(AutomationStatus.Assisted, EffectTiming.Always)]
    [InlineData(AutomationStatus.Automatic, EffectTiming.WhileActive)]
    public void Stored_non_automatic_or_non_always_entry_is_not_applied(AutomationStatus automation, EffectTiming timing)
    {
        var effect = Use("u", Athletics, "dex") with { Automation = automation, Timing = timing };
        var baseline = Sheet(Handler(LowStrHighDex));
        var sheet = Sheet(Handler(LowStrHighDex), Feature("Fixture Quickstep", 13, effect));

        Assert.Equal(baseline.Field(Athletics).Value, sheet.Field(Athletics).Value);
    }

    [Fact]
    public void Unparsable_ability_or_target_is_skipped()
    {
        var baseline = Sheet(Handler(LowStrHighDex));
        var sheet = Sheet(Handler(LowStrHighDex), Feature("Fixture Quickstep", 14, Use("a", Athletics, "DEX"), Use("b", "skill.nothing", "dex"), Use("c", "save.str", "dex")));

        foreach (var field in baseline.Fields)
            Assert.Equal(field.Value, sheet.Field(field.Field).Value);
    }

    [Fact]
    public void Sheet_without_checkAbility_has_identical_order_and_trace()
    {
        var scores = new AbilityScores(16, 10, 10, 10, 10, 10);
        var baseline = Sheet(Handler(scores));
        var sheet = Sheet(Handler(scores), Feature("Fixture Quickstep", 15, Use("u", Athletics, "dex")));

        Assert.Equal(baseline.Fields.Select(f => f.Field), sheet.Fields.Select(f => f.Field));
        foreach (var field in baseline.Fields)
            Assert.Equal(Trace(field), Trace(sheet.Field(field.Field)));
    }
}
