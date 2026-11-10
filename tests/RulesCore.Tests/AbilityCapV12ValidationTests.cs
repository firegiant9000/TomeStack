using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>Content schema v12 (G5): validation of a modifier's ability-score cap, and its validate.requires-v12 rung.</summary>
public class AbilityCapV12ValidationTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private static readonly Guid ContentId = Guid.Parse("6a7e5500-0000-4000-8000-000000000002");

    private static ModifierEffect Capped(Func<ModifierEffect, ModifierEffect>? shape = null)
    {
        var modifier = new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = "ability.str.score", Value = "4", Cap = 25 };
        return shape is null ? modifier : shape(modifier);
    }

    private static ContentRevision Built(ContentKind kind, int version, params Effect[] effects) => new()
    {
        ContentId = ContentId, RevisionId = Guid.Parse("6a7e5500-0000-4000-8000-000000000102"),
        SchemaVersion = version, Kind = kind, Name = "Fixture Gauntlet", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Fixtures.Source2024), Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ValidationReport Validate(Effect effect, int version = 12, ContentKind kind = ContentKind.Feature) =>
        ContentValidator.Validate(Built(kind, version, effect), Catalog);

    private static void AssertError(ValidationReport report, string code) => Assert.Contains(report.Errors, e => e.Code == code);

    private static void AssertClean(ValidationReport report)
    {
        Assert.Empty(report.Errors);
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData("initiative")]
    [InlineData("ability.str.check")]
    public void Cap_on_a_non_score_target_is_refused(string target)
    {
        var report = Validate(Capped(m => m with { Target = target }));
        AssertError(report, "validate.modifier-cap");
        Assert.Contains(report.Errors, e => e.Code == "validate.modifier-cap" && e.Message.Contains("'m'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ModifierOperation.Set)]
    [InlineData(ModifierOperation.Replace)]
    public void Cap_on_set_or_replace_is_refused(ModifierOperation operation) =>
        AssertError(Validate(Capped(m => m with { Operation = operation })), "validate.modifier-cap");

    [Theory]
    [InlineData("-2")]
    [InlineData(" -1 ")]
    public void Cap_on_a_literal_negative_value_is_refused(string value) =>
        AssertError(Validate(Capped(m => m with { Value = value })), "validate.modifier-cap");

    [Theory]
    [InlineData(20)]
    [InlineData(31)]
    [InlineData(0)]
    public void Cap_outside_21_to_30_is_refused(int cap)
    {
        var report = Validate(Capped(m => m with { Cap = cap }));
        Assert.Contains(report.Errors, e => e.Code == "validate.modifier-cap-range"
            && e.Message == $"Effect 'm' has cap {cap}; it must be a whole number from 21 to 30 (20 is the usual maximum).");
    }

    [Theory]
    [InlineData(21)]
    [InlineData(30)]
    public void Cap_21_and_30_validate(int cap) => AssertClean(Validate(Capped(m => m with { Cap = cap })));

    [Fact]
    public void Cap_with_toggle_while_armored_or_timed_validates()
    {
        var toggle = new ToggleEffect { Id = "t", ToggleId = "fury", Label = "Fury" };
        var revision = Built(ContentKind.Feature, 12, toggle,
            Capped(m => m with { Toggle = "fury", Timing = EffectTiming.WhileActive, WhileArmored = true }));
        AssertClean(ContentValidator.Validate(revision, Catalog));
    }

    [Fact]
    public void Cap_on_a_top_level_modifier_of_an_item_mod_revision_is_refused()
    {
        var mod = new ItemModEffect { Id = "im", AppliesTo = ModTarget.Weapon };
        var report = ContentValidator.Validate(Built(ContentKind.Item, 12, mod, Capped()), Catalog);
        AssertError(report, "validate.modifier-cap");
    }

    [Fact]
    public void Cap_inside_an_item_mod_is_refused()
    {
        var mod = new ItemModEffect { Id = "im", AppliesTo = ModTarget.Weapon, Modifiers = [Capped()] };
        AssertError(Validate(mod, 12, ContentKind.Item), "validate.item-mod-modifier");
    }

    [Fact]
    public void Unreadable_cap_is_malformed_and_needs_12()
    {
        var json = "{\"contentId\":\"6a7e5500-0000-4000-8000-000000000002\",\"revisionId\":\"6a7e5500-0000-4000-8000-000000000102\",\"schemaVersion\":12,\"kind\":\"feature\",\"name\":\"Fixture Gauntlet\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"5f0d5210-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[{\"type\":\"modifier\",\"id\":\"m\",\"operation\":\"bonus\",\"target\":\"ability.str.score\",\"value\":\"4\",\"cap\":\"abc\"}]}";
        var revision = RevisionHashProperties.Read(json);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());
        var report = ContentValidator.Validate(revision, Catalog);
        AssertError(report, "validate.effect-malformed");
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Draft_with_cap_below_v12_fails_requires_v12_and_at_v12_requires_12()
    {
        var cap = Capped();
        var below = Validate(cap, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("ability-score cap", StringComparison.Ordinal));
        AssertClean(Validate(cap));
    }
}
