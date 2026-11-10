namespace TomeStack.RulesCore.Tests;

/// <summary>Content schema v12 (G10): validation of a checkAbility's target and ability, and its validate.requires-v12 rung.</summary>
public class CheckAbilityV12ValidationTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private static readonly Guid ContentId = Guid.Parse("6a7e1000-0000-4000-8000-000000000001");

    private static CheckAbilityEffect Swap(Func<CheckAbilityEffect, CheckAbilityEffect>? shape = null)
    {
        var swap = new CheckAbilityEffect { Id = "c", Target = "skill.athletics", Ability = "dex" };
        return shape is null ? swap : shape(swap);
    }

    private static ContentRevision Built(int version, params Effect[] effects) => new()
    {
        ContentId = ContentId, RevisionId = Guid.Parse("6a7e1000-0000-4000-8000-000000000101"),
        SchemaVersion = version, Kind = ContentKind.Feature, Name = "Fixture Swap", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Fixtures.Source2024), Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ValidationReport Validate(CheckAbilityEffect swap, int version = 12) => ContentValidator.Validate(Built(version, swap), Catalog);

    private static void AssertError(ValidationReport report, string code) => Assert.Contains(report.Errors, e => e.Code == code);

    private static void AssertClean(ValidationReport report)
    {
        Assert.Empty(report.Errors);
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData("skill.athletics", "dex")]
    [InlineData("skill.sleightOfHand", "str")]
    [InlineData("ability.str.check", "dex")]
    [InlineData("ability.int.check", "wis")]
    public void Valid_targets_are_accepted(string target, string ability) =>
        AssertClean(Validate(Swap(c => c with { Target = target, Ability = ability })));

    [Theory]
    [InlineData("save.dex")]
    [InlineData("initiative")]
    [InlineData("check")]
    [InlineData("ability.str.mod")]
    [InlineData("ability.str.score")]
    [InlineData("ability.xyz.check")]
    [InlineData("ability.con.check")]
    public void Target_save_initiative_and_bare_check_are_refused(string target)
    {
        var report = Validate(Swap(c => c with { Target = target }));
        AssertError(report, "validate.check-ability-target");
        Assert.Contains(report.Errors, e => e.Code == "validate.check-ability-target" && e.Message.Contains("skill.<skill>", StringComparison.Ordinal) && e.Message.Contains("ability.<ability>.check", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_skill_is_refused() =>
        AssertError(Validate(Swap(c => c with { Target = "skill.juggling" })), "validate.check-ability-target");

    [Theory]
    [InlineData("skill.athletics", "str")]
    [InlineData("ability.str.check", "str")]
    [InlineData("skill.stealth", "dex")]
    public void Ability_same_as_printed_is_refused(string target, string ability) =>
        AssertError(Validate(Swap(c => c with { Target = target, Ability = ability })), "validate.check-ability-ability");

    [Theory]
    [InlineData("Dex")]
    [InlineData("dexterity")]
    [InlineData("")]
    public void Unknown_ability_key_is_refused(string ability) =>
        AssertError(Validate(Swap(c => c with { Ability = ability })), "validate.check-ability-ability");

    [Theory]
    [InlineData(AutomationStatus.Assisted, EffectTiming.Always)]
    [InlineData(AutomationStatus.Reference, EffectTiming.Always)]
    [InlineData(AutomationStatus.Automatic, EffectTiming.WhileActive)]
    [InlineData(AutomationStatus.Automatic, EffectTiming.OnRoll)]
    public void Manual_or_timed_checkAbility_is_refused(AutomationStatus automation, EffectTiming timing) =>
        AssertError(Validate(Swap(c => c with { Automation = automation, Timing = timing })), "validate.check-ability-automation");

    [Fact]
    public void Draft_with_checkAbility_below_v12_fails_requires_v12_and_at_v12_requires_12()
    {
        var swap = Swap();
        var below = Validate(swap, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("checkAbility", StringComparison.Ordinal));
        AssertClean(Validate(swap));
    }

    [Fact]
    public void Untyped_checkAbility_at_v12_is_reported_malformed()
    {
        var json = "{\"contentId\":\"6a7e1000-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e1000-0000-4000-8000-000000000101\",\"schemaVersion\":12,\"kind\":\"feature\",\"name\":\"Fixture Swap\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"5f0d5210-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[{\"type\":\"checkAbility\",\"id\":\"c\",\"target\":\"skill.athletics\"}]}";
        var revision = Properties.RevisionHashProperties.Read(json);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());
        AssertError(ContentValidator.Validate(revision, Catalog), "validate.effect-malformed");
    }
}
