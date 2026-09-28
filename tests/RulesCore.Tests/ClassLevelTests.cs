namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M1 item 5: class levels per class, total level derived, CLASS_LEVEL, level-gated grants, hit points and armor class
/// with full traces. Uses the original fixtures "Fixture Warden" (d10) and "Fixture Scholar" (d6).
/// Base scores: Dex 14 (+2), Con 12 (+1), Wis 13 (+1).
/// </summary>
public class ClassLevelTests
{
    private static Character With(params ClassLevel[] classes) =>
        Fixtures.Load("srd521-ash-m1.json") with { Pins = [], Classes = classes, Level = classes.Length == 0 ? 1 : classes.Sum(c => c.Level) };

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.M1Catalog());

    [Fact]
    public void Hit_points_are_the_hit_die_maximum_then_the_fixed_value_plus_constitution_per_level_with_a_sourced_trace()
    {
        var hp = Sheet(With(new ClassLevel(Fixtures.Warden, 3))).Field(FieldIds.HitPoints);

        Assert.Equal(10 + (2 * 6) + (1 * 3), hp.Value);
        Assert.Equal(AutomationStatus.Automatic, hp.Automation);
        var own = hp.Trace.Where(t => t.Field == FieldIds.HitPoints).ToList();
        Assert.Equal(["Fixture Warden level 1: the hit die maximum (d10)", "Fixture Warden levels 2–3: 2 × 6 (fixed value for d10)", "Constitution modifier (+1) × 3 character level(s)"], own.Select(t => t.Description));
        Assert.All(own.Take(2), t => Assert.Equal((Fixtures.Warden, "TomeStack Fixtures: M1 Shared", new PageRef(20)), (t.Origin.Content!, t.Origin.SourceTitle!, t.Origin.Page!)));
        Assert.Equal(TraceOriginKind.RulesPolicy, own[2].Origin.Kind);
        Assert.Contains(own[2].Inputs!, i => i.Name == FieldIds.Modifier(Ability.Con) && i.Value == 1);
    }

    [Fact]
    public void Multiclass_hit_points_use_the_starting_class_for_level_1_and_the_total_level_drives_the_proficiency_bonus()
    {
        var wardenFirst = Sheet(With(new ClassLevel(Fixtures.Warden, 2), new ClassLevel(Fixtures.Scholar, 3)));
        var scholarFirst = Sheet(With(new ClassLevel(Fixtures.Scholar, 3), new ClassLevel(Fixtures.Warden, 2)));

        Assert.Equal(10 + 6 + (3 * 4) + 5, wardenFirst.Field(FieldIds.HitPoints).Value);
        Assert.Equal(6 + (2 * 4) + (2 * 6) + 5, scholarFirst.Field(FieldIds.HitPoints).Value);
        Assert.Equal(3, wardenFirst.Field(FieldIds.ProficiencyBonus).Value); // total level 5
        Assert.Equal(0 + 3, wardenFirst.Field(FieldIds.Save(Ability.Int)).Value); // Scholar save proficiency
        Assert.Equal(0 + 3, wardenFirst.Field(FieldIds.Save(Ability.Str)).Value); // Warden save proficiency
    }

    [Theory]
    [InlineData(1, 2, false)]
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(4, 4, true)]
    public void Grants_apply_from_their_class_level_and_CLASS_LEVEL_reads_the_owning_class(int wardenLevel, int initiative, bool perceptive)
    {
        var sheet = Sheet(With(new ClassLevel(Fixtures.Warden, wardenLevel)));

        Assert.Equal(initiative, sheet.Field(FieldIds.Initiative).Value);
        var stride = sheet.Field(FieldIds.Initiative).Trace.Where(t => t.Origin.Content == Fixtures.WardenStride).ToList();
        if (wardenLevel < 2)
            Assert.Empty(stride);
        else
            Assert.Equal([new TraceInput(FormulaIdentifiers.ClassLevel, wardenLevel)], Assert.Single(stride).Inputs);
        var pb = sheet.Field(FieldIds.ProficiencyBonus).Value;
        Assert.Equal(1 + (perceptive ? pb : 0), sheet.Field(FieldIds.Skill("perception")).Value);
    }

    [Fact]
    public void CLASS_LEVEL_is_unavailable_to_content_that_does_not_belong_to_a_class()
    {
        // Pinned directly, the feature belongs to no class, so its formula cannot be evaluated; only that effect is disabled.
        var initiative = Sheet(With() with { Pins = [Fixtures.WardenStride] }).Field(FieldIds.Initiative);

        Assert.Equal(2, initiative.Value);
        Assert.Contains(initiative.Warnings, w => w.EffectId == "stride-init" && w.Message.Contains("formula.value-unavailable", StringComparison.Ordinal));
        Assert.Equal(AutomationStatus.Assisted, initiative.Automation);
    }

    [Fact]
    public void Armor_class_starts_unarmored_and_the_highest_replacement_wins_with_the_others_traced()
    {
        var plain = Sheet(With());
        var guarded = Sheet(With(new ClassLevel(Fixtures.Warden, 1)) with { BaseAbilities = new AbilityScores(10, 14, 12, 10, 16, 8), Pins = [Fixtures.Bracers] });

        var baseAc = plain.Field(FieldIds.ArmorClass);
        Assert.Equal(10 + 2, baseAc.Value);
        Assert.Equal("Armor Class without armor = 10 + Dexterity modifier (armor is not modeled yet)", baseAc.Trace[^1].Description);

        var ac = guarded.Field(FieldIds.ArmorClass);
        Assert.Equal(10 + 2 + 3, ac.Value); // Warden Guard 15 beats Bracers 13
        var replace = Assert.Single(ac.Trace, t => t.Operation == "replace");
        Assert.Equal(Fixtures.WardenGuard, replace.Origin.Content);
        Assert.Contains("granted by class 'Fixture Warden'", replace.Description, StringComparison.Ordinal);
        Assert.Equal(Fixtures.Bracers, Assert.Single(ac.Trace, t => t.Operation == "ignored").Origin.Content);
    }

    [Fact]
    public void A_hit_point_bonus_formula_can_read_the_character_level()
    {
        var hp = Sheet(With(new ClassLevel(Fixtures.Warden, 3)) with { Pins = [Fixtures.Hardy] }).Field(FieldIds.HitPoints);

        Assert.Equal(25 + 3, hp.Value);
        Assert.Equal([new TraceInput(FormulaIdentifiers.Level, 3)], hp.Trace[^1].Inputs);
    }

    [Fact]
    public void Without_a_class_hit_points_are_assisted_and_explain_why()
    {
        var hp = Sheet(With()).Field(FieldIds.HitPoints);

        Assert.Equal((0, AutomationStatus.Assisted), (hp.Value, hp.Automation));
        Assert.Equal("hit-points.no-class", Assert.Single(hp.Warnings).Code);
    }

    [Fact]
    public void A_pinned_class_without_levels_and_a_class_without_a_hit_die_are_reported()
    {
        var catalog = Fixtures.M1Catalog();
        var noDie = catalog.FindRevision(Fixtures.Warden)! with
        {
            RevisionId = Guid.Parse("5f1de000-0000-4000-8000-0000000000fa"),
            Effects = [.. catalog.FindRevision(Fixtures.Warden)!.Effects.Where(e => e is not HitDieEffect)],
        };
        var withNoDie = new InMemoryContentCatalog([.. Fixtures.Pack().Sources, .. Fixtures.M1Pack().Sources], [.. Fixtures.Pack().Revisions, .. Fixtures.M1Pack().Revisions, noDie]);

        var pinnedOnly = Sheet(With() with { Pins = [Fixtures.Warden] });
        var dieless = CharacterCalculator.Calculate(With(new ClassLevel(noDie.Reference, 2)), withNoDie);

        Assert.Contains(pinnedOnly.Diagnostics, d => d.Code == "character.class-without-levels" && d.Content == Fixtures.Warden);
        Assert.Contains(dieless.Field(FieldIds.HitPoints).Warnings, w => w.Code == "class.hit-die-missing");
        Assert.Equal(AutomationStatus.Assisted, dieless.Field(FieldIds.HitPoints).Automation);
    }

    [Fact]
    public void Content_recorded_as_a_class_must_be_a_class()
    {
        var sheet = Sheet(With(new ClassLevel(Fixtures.Hardy, 2)));

        Assert.Contains(sheet.Diagnostics, d => d.Code == "character.class-not-a-class" && d.Content == Fixtures.Hardy);
        Assert.Equal(AutomationStatus.Assisted, sheet.Field(FieldIds.HitPoints).Automation);
    }

    [Fact]
    public void Class_levels_are_validated()
    {
        var character = With(new ClassLevel(Fixtures.Warden, 3));

        Assert.Empty(character.Validate());
        Assert.Contains((character with { Level = 4 }).Validate(), d => d.Code == "character.level-mismatch");
        Assert.Contains(With(new ClassLevel(Fixtures.Warden, 12), new ClassLevel(Fixtures.Scholar, 9)).Validate(), d => d.Code == "character.level-out-of-range");
        Assert.Contains(With(new ClassLevel(Fixtures.Warden, 1), new ClassLevel(Fixtures.Warden with { RevisionId = Guid.NewGuid() }, 1)).Validate(), d => d.Code == "character.class-duplicate");
        Assert.Contains(With(new ClassLevel(Fixtures.Warden, 0)).Validate(), d => d.Code == "character.class-level-out-of-range");
        Assert.Equal(5, With(new ClassLevel(Fixtures.Warden, 2), new ClassLevel(Fixtures.Scholar, 3)).TotalLevel);
    }
}
