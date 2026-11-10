namespace TomeStack.RulesCore.Tests;

/// <summary>Content schema v12 (G11): validation of a conditionImmunity grant, and its validate.requires-v12 rung.</summary>
public class ConditionImmunityV12ValidationTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private static readonly Guid ContentId = Guid.Parse("6a7e1100-0000-4000-8000-000000000001");

    private static GrantEffect Immunity(Func<GrantEffect, GrantEffect>? shape = null)
    {
        var grant = new GrantEffect { Id = "g", Grant = GrantKind.ConditionImmunity, Condition = "charmed" };
        return shape is null ? grant : shape(grant);
    }

    private static ContentRevision Built(int version, params Effect[] effects) => new()
    {
        ContentId = ContentId, RevisionId = Guid.Parse("6a7e1100-0000-4000-8000-000000000101"),
        SchemaVersion = version, Kind = ContentKind.Feature, Name = "Fixture Ward", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Fixtures.Source2024), Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ValidationReport Validate(GrantEffect grant, int version = 12, params Effect[] others) =>
        ContentValidator.Validate(Built(version, [grant, .. others]), Catalog);

    private static void AssertError(ValidationReport report, string code) => Assert.Contains(report.Errors, e => e.Code == code);

    private static void AssertClean(ValidationReport report)
    {
        Assert.Empty(report.Errors);
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("exhaustion")]
    [InlineData("disease")]
    [InlineData("Charmed")]
    public void Missing_or_unknown_condition_is_refused(string? condition)
    {
        var report = Validate(Immunity(g => g with { Condition = condition }));
        AssertError(report, "validate.grant-condition");
        Assert.Contains(report.Errors, e => e.Code == "validate.grant-condition" && e.Message.Contains("charmed", StringComparison.Ordinal) && e.Message.Contains("stunned", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_condition_key_is_accepted()
    {
        foreach (var key in ConditionKeys.All)
            AssertClean(Validate(Immunity(g => g with { Condition = key })));
    }

    [Fact]
    public void Condition_on_a_proficiency_grant_is_refused() =>
        AssertError(
            Validate(new GrantEffect { Id = "g", Grant = GrantKind.Proficiency, Target = "skill.stealth", Condition = "charmed" }),
            "validate.grant-condition-kind");

    [Fact]
    public void Target_content_or_damage_type_on_condition_immunity_is_refused()
    {
        AssertError(Validate(Immunity(g => g with { Target = "skill.stealth" })), "validate.grant-condition-fields");
        AssertError(Validate(Immunity(g => g with { Content = new ContentReference(Guid.NewGuid(), Guid.NewGuid()) })), "validate.grant-condition-fields");
        AssertError(Validate(Immunity(g => g with { DamageType = "fire" })), "validate.grant-condition-fields");
    }

    [Fact]
    public void While_toggle_naming_an_unknown_toggle_is_refused()
    {
        var report = Validate(Immunity(g => g with { WhileToggle = new ToggleRef(null, "missing") }));
        AssertError(report, "validate.while-toggle-unknown");
    }

    [Fact]
    public void While_toggle_naming_a_toggle_of_the_same_content_validates() =>
        AssertClean(Validate(
            Immunity(g => g with { WhileToggle = new ToggleRef(null, "fury") }),
            12,
            new ToggleEffect { Id = "t", ToggleId = "fury", Label = "Fury" }));

    [Theory]
    [InlineData(AutomationStatus.Assisted, EffectTiming.Always)]
    [InlineData(AutomationStatus.Reference, EffectTiming.Always)]
    [InlineData(AutomationStatus.Automatic, EffectTiming.WhileActive)]
    [InlineData(AutomationStatus.Automatic, EffectTiming.OnRoll)]
    public void Manual_or_timed_condition_immunity_validates(AutomationStatus automation, EffectTiming timing) =>
        AssertClean(Validate(Immunity(g => g with { Automation = automation, Timing = timing })));

    [Fact]
    public void Draft_with_condition_immunity_below_v12_fails_requires_v12_and_at_v12_requires_12()
    {
        var grant = Immunity();
        var below = Validate(grant, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("condition immunity", StringComparison.Ordinal));
        AssertClean(Validate(grant));
    }
}
