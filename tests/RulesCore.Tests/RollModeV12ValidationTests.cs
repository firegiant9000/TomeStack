using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G7): validation of a rollMode's attack, initiative and attacksAgainst targets and its tags, ability,
/// unlessCondition and prevent members, and the validate.requires-v12 rung each one registers.
/// </summary>
public class RollModeV12ValidationTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private static readonly Guid ContentId = Guid.Parse("6a7e0700-0000-4000-8000-000000000001");

    private static RollModeEffect Mode(string target, Func<RollModeEffect, RollModeEffect>? shape = null)
    {
        var mode = new RollModeEffect { Id = "m", Target = target, Mode = RollMode.Advantage };
        return shape is null ? mode : shape(mode);
    }

    private static RollModeEffect AttackMode(Func<RollModeEffect, RollModeEffect>? shape = null)
    {
        var mode = Mode(RollModeEffect.AttackTarget, m => m with { Tags = ["melee"] });
        return shape is null ? mode : shape(mode);
    }

    private static ContentRevision Built(int version, params Effect[] effects) => new()
    {
        ContentId = ContentId, RevisionId = Guid.Parse("6a7e0700-0000-4000-8000-000000000101"),
        SchemaVersion = version, Kind = ContentKind.Feature, Name = "Fixture Mode", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Fixtures.Source2024), Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ValidationReport Validate(RollModeEffect mode, int version = 12) => ContentValidator.Validate(Built(version, mode), Catalog);

    private static ValidationReport ValidateJson(int version, string effect) =>
        ContentValidator.Validate(RevisionHashProperties.Read(
            "{\"contentId\":\"" + ContentId + "\",\"revisionId\":\"6a7e0700-0000-4000-8000-000000000101\",\"schemaVersion\":" + version
            + ",\"kind\":\"feature\",\"name\":\"Fixture Mode\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"" + Fixtures.Source2024 + "\"},\"status\":\"draft\",\"effects\":[" + effect + "]}"),
            Catalog);

    private static void AssertError(ValidationReport report, string code) => Assert.Contains(report.Errors, e => e.Code == code);

    private static void AssertNoError(ValidationReport report, string code) => Assert.DoesNotContain(report.Errors, e => e.Code == code);

    private static void AssertClean(ValidationReport report)
    {
        Assert.Empty(report.Errors);
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Fact]
    public void V11_rollMode_without_v12_members_still_requires_11()
    {
        var report = Validate(Mode("save.dex"), 11);
        Assert.Empty(report.Errors);
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Draft_with_an_attack_target_below_v12_fails_requires_v12_and_at_v12_requires_12()
    {
        var mode = AttackMode();
        var below = Validate(mode, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a rollMode on attack rolls", StringComparison.Ordinal));
        AssertNoError(below, "validate.roll-mode-target");
        AssertClean(Validate(mode));
    }

    [Fact]
    public void Draft_with_an_initiative_target_below_v12_fails_requires_v12_and_at_v12_requires_12()
    {
        var mode = Mode(RollModeEffect.InitiativeTarget);
        var below = Validate(mode, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a rollMode on initiative", StringComparison.Ordinal));
        AssertClean(Validate(mode));
    }

    [Fact]
    public void Draft_with_an_attacksAgainst_target_below_v12_fails_requires_v12_and_at_v12_requires_12()
    {
        var mode = Mode(RollModeEffect.AttacksAgainstTarget);
        var below = Validate(mode, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a rollMode on attacks against you", StringComparison.Ordinal));
        AssertClean(Validate(mode));
    }

    [Theory]
    [InlineData("attack", ",\"tags\":[\"melee\"]", "a rollMode on attack rolls")]
    [InlineData("initiative", "", "a rollMode on initiative")]
    [InlineData("attacksAgainst", "", "a rollMode on attacks against you")]
    public void Json_v11_rollMode_with_a_v12_target_requires_v12_not_malformed(string target, string extra, string phrase)
    {
        var report = ValidateJson(11, "{\"type\":\"rollMode\",\"id\":\"m\",\"target\":\"" + target + "\",\"mode\":\"advantage\"" + extra + "}");
        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains(phrase, StringComparison.Ordinal));
        AssertNoError(report, "validate.effect-malformed");
    }

    [Theory]
    [InlineData("attack", ",\"tags\":[\"melee\"]")]
    [InlineData("initiative", "")]
    [InlineData("attacksAgainst", "")]
    public void Json_v12_rollMode_with_a_v12_target_is_accepted(string target, string extra)
    {
        AssertClean(ValidateJson(12, "{\"type\":\"rollMode\",\"id\":\"m\",\"target\":\"" + target + "\",\"mode\":\"advantage\"" + extra + "}"));
    }

    [Fact]
    public void Json_v12_rollMode_that_does_not_read_and_carries_a_v12_key_is_malformed_and_needs_v12()
    {
        var report = ValidateJson(12, "{\"type\":\"rollMode\",\"id\":\"m\",\"target\":\"attack\",\"mode\":\"advantage\",\"tags\":[\"melee\"],\"ability\":\"Str\"}");
        AssertError(report, "validate.effect-malformed");
    }

    [Fact]
    public void Json_v11_rollMode_with_a_v11_target_and_no_mode_is_still_malformed()
    {
        AssertError(ValidateJson(11, "{\"type\":\"rollMode\",\"id\":\"m\",\"target\":\"save.dex\"}"), "validate.effect-malformed");
    }

    [Fact]
    public void Target_unknown_names_are_still_refused()
    {
        AssertError(Validate(Mode("attacks")), "validate.roll-mode-target");
    }

    [Fact]
    public void Tags_are_valid_with_attack()
    {
        AssertClean(Validate(AttackMode(m => m with { Tags = ["all"] })));
    }

    [Fact]
    public void Tags_below_v12_require_v12()
    {
        var report = Validate(Mode("save.dex", m => m with { Tags = ["melee"] }), 11);
        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("rollMode tags", StringComparison.Ordinal));
    }

    [Fact]
    public void Tags_missing_on_attack_is_an_error()
    {
        AssertError(Validate(Mode(RollModeEffect.AttackTarget)), "validate.roll-mode-tags");
    }

    [Fact]
    public void Tags_with_another_target_is_an_error()
    {
        AssertError(Validate(Mode("save.dex", m => m with { Tags = ["melee"] })), "validate.roll-mode-tags");
    }

    [Fact]
    public void Tags_empty_list_is_an_error()
    {
        AssertError(Validate(AttackMode(m => m with { Tags = [] })), "validate.roll-mode-tags");
    }

    [Fact]
    public void Tags_duplicate_is_an_error()
    {
        AssertError(Validate(AttackMode(m => m with { Tags = ["melee", "melee"] })), "validate.roll-mode-tags");
    }

    [Fact]
    public void Tags_empty_string_is_an_error()
    {
        AssertError(Validate(AttackMode(m => m with { Tags = ["melee", ""] })), "validate.roll-mode-tags");
    }

    [Fact]
    public void Ability_is_valid_with_attack()
    {
        AssertClean(Validate(AttackMode(m => m with { Ability = Ability.Str })));
    }

    [Fact]
    public void Ability_below_v12_requires_v12()
    {
        var report = Validate(AttackMode(m => m with { Ability = Ability.Str }), 11);
        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a rollMode ability", StringComparison.Ordinal));
    }

    [Fact]
    public void Ability_with_another_target_is_an_error()
    {
        AssertError(Validate(Mode(RollModeEffect.InitiativeTarget, m => m with { Ability = Ability.Dex })), "validate.roll-mode-ability");
    }

    [Fact]
    public void UnlessCondition_is_valid_on_any_target()
    {
        AssertClean(Validate(Mode(RollModeEffect.InitiativeTarget, m => m with { UnlessCondition = "incapacitated" })));
        AssertClean(Validate(AttackMode(m => m with { UnlessCondition = "incapacitated" })));
    }

    [Fact]
    public void UnlessCondition_below_v12_requires_v12()
    {
        var report = Validate(Mode("save.dex", m => m with { UnlessCondition = "incapacitated" }), 11);
        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a rollMode unlessCondition", StringComparison.Ordinal));
    }

    [Fact]
    public void UnlessCondition_not_a_condition_key_is_an_error()
    {
        AssertError(Validate(AttackMode(m => m with { UnlessCondition = "grumpy" })), "validate.roll-mode-condition");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prevent_is_valid_with_attacksAgainst(bool prevent)
    {
        AssertClean(Validate(Mode(RollModeEffect.AttacksAgainstTarget, m => m with { Prevent = prevent })));
    }

    [Fact]
    public void Prevent_below_v12_requires_v12()
    {
        var report = Validate(Mode("save.dex", m => m with { Prevent = false }), 11);
        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a rollMode prevent", StringComparison.Ordinal));
    }

    [Fact]
    public void Prevent_with_another_target_is_an_error()
    {
        AssertError(Validate(AttackMode(m => m with { Prevent = true })), "validate.roll-mode-prevent");
    }

    [Theory]
    [InlineData(RollModeEffect.InitiativeTarget)]
    [InlineData(RollModeEffect.AttacksAgainstTarget)]
    public void Against_is_refused_with_initiative_and_attacksAgainst(string target)
    {
        AssertError(Validate(Mode(target, m => m with { Against = ["frightened"] })), "validate.roll-mode-against");
    }

    [Fact]
    public void Against_is_still_allowed_with_attack()
    {
        AssertClean(Validate(AttackMode(m => m with { Against = ["frightened"] })));
    }
}
