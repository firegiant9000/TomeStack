namespace TomeStack.RulesCore.Tests;

/// <summary>Item 11: dependency-ordered calculation over a small field graph with cycle detection (ARCHITECTURE step 5).</summary>
public class FieldGraphTests
{
    private static int _next;

    private static ContentRevision Content(string name, ContentKind kind, params Effect[] effects)
    {
        var n = Interlocked.Increment(ref _next);
        return new ContentRevision
        {
            ContentId = Guid.Parse($"a0000000-0000-4000-8000-{n:D12}"),
            RevisionId = Guid.Parse($"b0000000-0000-4000-8000-{n:D12}"),
            Kind = kind,
            Name = name,
            RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
            Provenance = new(Fixtures.SourceShared, new PageRef(20 + n)),
            Status = RevisionStatus.Published,
            Effects = effects,
        };
    }

    private static ModifierEffect Bonus(string id, string target, string value, StackingRule stacking = StackingRule.Stack, string? group = null) =>
        new() { Id = id, Operation = ModifierOperation.Bonus, Target = target, Value = value, Stacking = stacking, StackGroup = group };

    private static CharacterSheet Sheet(Character character, params ContentRevision[] extra)
    {
        var pack = Fixtures.Pack();
        var catalog = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, .. extra]);
        return CharacterCalculator.Calculate(character with { Pins = [.. character.Pins, .. extra.Select(r => r.Reference)] }, catalog);
    }

    [Fact]
    public void Sheet_has_every_modeled_field_with_units()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());

        // scores, modifiers, PB, saves, skills, initiative, AC, HP; spell attack, save DC, 9 slot levels, pact slots (v5)
        Assert.Equal(6 + 6 + 1 + 6 + 18 + 1 + 1 + 1 + 2 + 9 + 1, sheet.Fields.Count);
        Assert.Equal(
            ["score", "modifier", "bonus", "modifier", "modifier", "modifier", "modifier", "score", "score"],
            new[] { "ability.str.score", "ability.str.mod", FieldIds.ProficiencyBonus, FieldIds.Save(Ability.Wis), FieldIds.Skill("stealth"), FieldIds.Skill("animalHandling"), FieldIds.Initiative, FieldIds.ArmorClass, FieldIds.HitPoints }
                .Select(f => sheet.Field(f).Units));
        Assert.Equal(
            [(10, 0), (17, 3), (12, 1), (10, 0), (13, 1), (8, -1)],
            Enum.GetValues<Ability>().Select(a => (sheet.Field(FieldIds.Score(a)).Value, sheet.Field(FieldIds.Modifier(a)).Value)));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 4)]
    [InlineData(13, 5)]
    [InlineData(17, 6)]
    [InlineData(20, 6)]
    public void Proficiency_bonus_follows_level(int level, int expected)
    {
        var pb = CharacterCalculator.Calculate(Fixtures.Srd51Character() with { Level = level }, Fixtures.Catalog()).Field(FieldIds.ProficiencyBonus);

        Assert.Equal(expected, pb.Value);
        Assert.Equal([new TraceInput("LEVEL", level)], pb.Trace.Single().Inputs);
    }

    [Fact]
    public void Proficiency_and_expertise_grants_add_the_proficiency_bonus_with_a_cited_trace()
    {
        var training = Content(
            "Fixture Scout Training", ContentKind.Feature,
            new GrantEffect { Id = "dex-save", Grant = GrantKind.Proficiency, Target = FieldIds.Save(Ability.Dex) },
            new GrantEffect { Id = "stealth", Grant = GrantKind.Expertise, Target = FieldIds.Skill("stealth") });

        var sheet = Sheet(Fixtures.Srd51Character() with { Level = 5 }, training);

        var save = sheet.Field(FieldIds.Save(Ability.Dex));
        Assert.Equal(3 + 3, save.Value);
        Assert.Equal(3 + 6, sheet.Field(FieldIds.Skill("stealth")).Value);
        Assert.Equal(0, sheet.Field(FieldIds.Save(Ability.Str)).Value);
        var step = save.Trace[^1];
        Assert.Equal(("add", 3, 6, FieldIds.Save(Ability.Dex)), (step.Operation, step.Amount!.Value, step.Result, step.Field!));
        Assert.Equal(("Fixture Scout Training", "dex-save"), (step.Origin.ContentName!, step.Origin.EffectId!));
        Assert.Contains(save.Trace, t => t.Field == FieldIds.ProficiencyBonus && t.Operation == "derive");
        Assert.Contains(save.Trace, t => t.Field == FieldIds.Score(Ability.Dex) && t.Origin.Content == Fixtures.Quickfoot);
    }

    [Fact]
    public void Effect_formulas_read_other_fields_in_dependency_order_and_trace_their_inputs()
    {
        var alert = Content("Fixture Alertness", ContentKind.Feat, Bonus("alert", FieldIds.Initiative, "PB + WIS.MOD"));

        var initiative = Sheet(Fixtures.Srd51Character() with { Level = 9 }, alert).Field(FieldIds.Initiative);

        Assert.Equal(4 + 4 + 1, initiative.Value);
        var step = initiative.Trace[^1];
        Assert.Equal([new TraceInput("PB", 4), new TraceInput("WIS.MOD", 1)], step.Inputs);
        var order = initiative.Trace.Select(t => t.Field!).Distinct().ToList();
        Assert.True(order.IndexOf(FieldIds.ProficiencyBonus) < order.IndexOf(FieldIds.Initiative));
        Assert.True(order.IndexOf(FieldIds.Modifier(Ability.Wis)) < order.IndexOf(FieldIds.Initiative));
        Assert.Equal(Enumerable.Range(1, initiative.Trace.Count), initiative.Trace.Select(t => t.Order));
    }

    [Fact]
    public void An_effect_that_reads_its_own_target_is_a_cycle_and_is_disabled_alone()
    {
        var loop = Content(
            "Fixture Feedback Loop", ContentKind.Feat,
            Bonus("self", FieldIds.Modifier(Ability.Dex), "DEX.MOD"),
            Bonus("fine", FieldIds.Initiative, "1"));

        var sheet = Sheet(Fixtures.Srd51Character(), loop);

        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.Code == "effect.dependency-cycle");
        Assert.Equal(("self", loop.Reference), (diagnostic.EffectId, diagnostic.Content));
        Assert.Contains("ability.dex.mod → ability.dex.mod", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(3, sheet.Field(FieldIds.Modifier(Ability.Dex)).Value);
        Assert.Equal(4 + 1, sheet.Field(FieldIds.Initiative).Value);
    }

    [Fact]
    public void Score_to_modifier_to_score_cycle_is_detected_and_both_fields_still_calculate()
    {
        var loop = Content("Fixture Bootstrap", ContentKind.Feat, Bonus("boot", FieldIds.Score(Ability.Dex), "DEX.MOD"));

        var sheet = Sheet(Fixtures.Srd51Character(), loop);

        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.Code == "effect.dependency-cycle");
        Assert.Contains("ability.dex.score → ability.dex.mod → ability.dex.score", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(sheet.Field(FieldIds.Score(Ability.Dex)).Warnings, w => w.Code == "effect.dependency-cycle");
        Assert.Equal(17, sheet.Field(FieldIds.Score(Ability.Dex)).Value);
        Assert.Equal(4, sheet.Field(FieldIds.Initiative).Value);
    }

    [Fact]
    public void Cycle_through_two_effects_disables_both_and_nothing_else()
    {
        var a = Content("Fixture Twin A", ContentKind.Feat, Bonus("a", FieldIds.Score(Ability.Str), "DEX.MOD"));
        var b = Content("Fixture Twin B", ContentKind.Feat, Bonus("b", FieldIds.Score(Ability.Dex), "STR.MOD"));
        var ok = Content("Fixture Unrelated", ContentKind.Feat, Bonus("c", FieldIds.Score(Ability.Con), "WIS.MOD"));

        var sheet = Sheet(Fixtures.Srd51Character(), a, b, ok);

        Assert.Equal(["a", "b"], sheet.Diagnostics.Where(d => d.Code == "effect.dependency-cycle").Select(d => d.EffectId).Order());
        Assert.Equal((10, 17, 13), (sheet.Field(FieldIds.Score(Ability.Str)).Value, sheet.Field(FieldIds.Score(Ability.Dex)).Value, sheet.Field(FieldIds.Score(Ability.Con)).Value));
    }

    [Fact]
    public void An_effect_that_only_repeats_a_base_dependency_is_not_disabled_by_a_cycle_elsewhere()
    {
        // "parallel" reads DEX.SCORE into the Dex modifier, which already depends on the score, so it cannot close a
        // cycle. Only "loop" (score reads its own modifier) is cyclic.
        var content = Content(
            "Fixture Parallel", ContentKind.Feat,
            Bonus("parallel", FieldIds.Modifier(Ability.Dex), "floor(DEX.SCORE / 10)"),
            Bonus("loop", FieldIds.Score(Ability.Dex), "DEX.MOD"));

        var sheet = Sheet(Fixtures.Srd51Character(), content);

        Assert.Equal(["loop"], sheet.Diagnostics.Where(d => d.Code == "effect.dependency-cycle").Select(d => d.EffectId));
        Assert.Equal((17, 3 + 1), (sheet.Field(FieldIds.Score(Ability.Dex)).Value, sheet.Field(FieldIds.Modifier(Ability.Dex)).Value));
        Assert.Equal(3 + 1 + 1, sheet.Field(FieldIds.Initiative).Value);
    }

    [Fact]
    public void Many_bounded_bonuses_cannot_overflow_the_running_value()
    {
        var flood = Content("Fixture Flood", ContentKind.Feat, [.. Enumerable.Range(0, 2_200).Select(i => (Effect)Bonus($"f{i}", FieldIds.Initiative, "10000 * 50"))]);

        var initiative = Sheet(Fixtures.Srd51Character(), flood).Field(FieldIds.Initiative);

        Assert.Equal(4 + 500_000, initiative.Value);
        Assert.Equal(2_199, initiative.Warnings.Count(w => w.Code == "effect.out-of-range"));
        Assert.Equal(AutomationStatus.Assisted, initiative.Automation);
    }

    [Fact]
    public void Fields_whose_effects_are_not_all_applied_are_assisted_and_so_are_their_dependents()
    {
        var manual = Content(
            "Fixture Manual", ContentKind.Feat,
            new ModifierEffect { Id = "manual", Operation = ModifierOperation.Bonus, Target = FieldIds.Score(Ability.Dex), Value = "2", Automation = AutomationStatus.Assisted });

        var plain = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        var sheet = Sheet(Fixtures.Srd51Character(), manual);

        // Hit points need a class, and this M0 character has none, so only they are assisted without the manual effect.
        Assert.All(plain.Fields.Where(f => f.Field != FieldIds.HitPoints), f => Assert.Equal(AutomationStatus.Automatic, f.Automation));
        Assert.Equal(AutomationStatus.Assisted, plain.Field(FieldIds.HitPoints).Automation);
        Assert.Equal(
            [FieldIds.Score(Ability.Dex), FieldIds.Modifier(Ability.Dex), FieldIds.Save(Ability.Dex), FieldIds.Skill("acrobatics"), FieldIds.Skill("sleightOfHand"), FieldIds.Skill("stealth"), FieldIds.Initiative, FieldIds.ArmorClass],
            sheet.Fields.Where(f => f.Automation == AutomationStatus.Assisted && f.Field != FieldIds.HitPoints).Select(f => f.Field));
        Assert.Equal(17, sheet.Field(FieldIds.Score(Ability.Dex)).Value);
    }

    [Fact]
    public void Bonuses_in_the_same_group_do_not_stack_and_the_trace_says_so()
    {
        var ring = Content("Fixture Ring", ContentKind.Item, Bonus("ring", FieldIds.Initiative, "2", StackingRule.HighestInGroup, "luck"));
        var charm = Content("Fixture Charm", ContentKind.Item, Bonus("charm", FieldIds.Initiative, "1", StackingRule.HighestInGroup, "luck"));
        var boots = Content("Fixture Boots", ContentKind.Item, Bonus("boots", FieldIds.Initiative, "1"));

        var initiative = Sheet(Fixtures.Srd51Character(), charm, ring, boots).Field(FieldIds.Initiative);

        Assert.Equal(4 + 2 + 1, initiative.Value);
        var ignored = Assert.Single(initiative.Trace, t => t.Operation == "ignored");
        Assert.Equal("charm", ignored.Origin.EffectId);
        Assert.Contains("does not stack", ignored.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_then_bonuses_then_set_apply_in_order_and_the_highest_of_each_wins()
    {
        var headband = Content("Fixture Headband", ContentKind.Item, new ModifierEffect { Id = "set", Operation = ModifierOperation.Set, Target = FieldIds.Score(Ability.Int), Value = "19" });
        var tome = Content("Fixture Tome", ContentKind.Item, Bonus("tome", FieldIds.Score(Ability.Int), "2"));
        var weak = Content("Fixture Weak Headband", ContentKind.Item, new ModifierEffect { Id = "set-low", Operation = ModifierOperation.Set, Target = FieldIds.Score(Ability.Int), Value = "11" });
        var instinct = Content("Fixture Instinct", ContentKind.Feature, new ModifierEffect { Id = "replace", Operation = ModifierOperation.Replace, Target = FieldIds.Initiative, Value = "WIS.MOD + 2" });

        var sheet = Sheet(Fixtures.Srd51Character(), weak, tome, headband, instinct);

        var intelligence = sheet.Field(FieldIds.Score(Ability.Int));
        Assert.Equal(19, intelligence.Value);
        Assert.Equal(["base", "add", "set", "ignored"], intelligence.Trace.Select(t => t.Operation));
        var initiative = sheet.Field(FieldIds.Initiative);
        Assert.Equal(1 + 2 + 1, initiative.Value); // replaced base (WIS.MOD + 2), then Keen Reflexes +1
        Assert.Equal(["base", "replace", "add"], initiative.Trace.Where(t => t.Field == FieldIds.Initiative).Select(t => t.Operation));
    }

    [Fact]
    public void An_override_on_an_input_flows_to_every_dependent_and_is_visible_in_their_traces()
    {
        var character = Fixtures.Srd51Character() with { Overrides = [new FieldOverride(FieldIds.Score(Ability.Dex), 20, "Gloves not modeled")] };

        var sheet = CharacterCalculator.Calculate(character, Fixtures.Catalog());

        var score = sheet.Field(FieldIds.Score(Ability.Dex));
        Assert.Equal((20, 17), (score.Value, score.ComputedValue));
        Assert.Equal(5, sheet.Field(FieldIds.Modifier(Ability.Dex)).Value);
        var initiative = sheet.Field(FieldIds.Initiative);
        Assert.Equal(5 + 1, initiative.Value);
        Assert.Contains(initiative.Trace, t => t.Operation == "override" && t.Field == FieldIds.Score(Ability.Dex));
    }

    [Fact]
    public void Unavailable_identifiers_and_unknown_targets_are_isolated_with_diagnostics()
    {
        var classy = Content(
            "Fixture Class Scaling", ContentKind.Feature,
            Bonus("scaling", FieldIds.Initiative, "floor(CLASS_LEVEL / 2)"),
            Bonus("nowhere", "speed", "1"),
            new GrantEffect { Id = "bad-grant", Grant = GrantKind.Proficiency, Target = "skill.juggling" });

        var sheet = Sheet(Fixtures.Srd51Character(), classy);

        var initiative = sheet.Field(FieldIds.Initiative);
        Assert.Equal(4, initiative.Value);
        Assert.Contains(initiative.Warnings, w => w.EffectId == "scaling" && w.Message.Contains("formula.value-unavailable", StringComparison.Ordinal));
        Assert.Equal(["bad-grant", "nowhere"], sheet.Diagnostics.Where(d => d.Code == "effect.unknown-target").Select(d => d.EffectId).Order());
    }

    [Fact]
    public void Feats_may_raise_ability_scores_under_both_rules_families()
    {
        var feat = Content("Fixture Athlete", ContentKind.Feat, Bonus("str", FieldIds.Score(Ability.Str), "1"));

        foreach (var family in new[] { RulesFamilies.Srd51, RulesFamilies.Srd521 })
        {
            var character = Fixtures.Srd51Character() with { RulesFamily = family, Pins = [] };
            var strength = Sheet(character, feat).Field(FieldIds.Score(Ability.Str));

            Assert.Equal(11, strength.Value);
            Assert.Empty(strength.Warnings);
        }
    }

    [Fact]
    public void Every_trace_is_numbered_from_one_and_ends_with_the_fields_own_steps()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Srd521Character() with { Level = 6 }, Fixtures.Catalog());

        Assert.All(sheet.Fields, field =>
        {
            Assert.Equal(Enumerable.Range(1, field.Trace.Count), field.Trace.Select(t => t.Order));
            Assert.Equal(field.Field, field.Trace[^1].Field);
            Assert.Equal(field.Value, field.Trace[^1].Result);
            Assert.All(field.Trace, t => Assert.Equal(RulesFamilies.Srd521, t.Origin.RulesFamily));
        });
    }
}
