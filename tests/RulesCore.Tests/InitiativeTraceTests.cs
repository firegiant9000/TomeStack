using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

public class InitiativeTraceTests
{
    [Fact]
    public void Srd51_species_increases_apply_and_every_content_step_cites_its_source()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        var initiative = sheet.Field(CharacterCalculator.InitiativeField);

        Assert.Equal(4, initiative.Value);
        Assert.Equal(4, initiative.ComputedValue);
        Assert.Empty(initiative.Warnings);
        Assert.Empty(sheet.Diagnostics);
        Assert.Equal(
            [("base", 14, 14), ("add", 2, 16), ("add", 1, 17), ("derive", 17, 3), ("add", 1, 4)],
            initiative.Trace.Select(t => (t.Operation, t.Amount ?? 0, t.Result)));
        Assert.Equal([1, 2, 3, 4, 5], initiative.Trace.Select(t => t.Order));

        var quickfoot = initiative.Trace[1].Origin;
        Assert.Equal(TraceOriginKind.Content, quickfoot.Kind);
        Assert.Equal(Fixtures.Quickfoot, quickfoot.Content);
        Assert.Equal(Fixtures.Source2014, quickfoot.SourceId);
        Assert.Equal("TomeStack Fixtures: 2014 Family", quickfoot.SourceTitle);
        Assert.Equal(new PageRef(1), quickfoot.Page);
        Assert.Equal("quickfoot-dex", quickfoot.EffectId);

        Assert.Equal(new PageRef(3, 4), initiative.Trace[2].Origin.Page);
        Assert.Equal(TraceOriginKind.RulesPolicy, initiative.Trace[3].Origin.Kind);
        Assert.All(initiative.Trace, t => Assert.Equal(RulesFamilies.Srd51, t.Origin.RulesFamily));
    }

    [Fact]
    public void Srd521_takes_increases_from_background_and_isolates_species_increase()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Srd521Character(), Fixtures.Catalog());
        var initiative = sheet.Field(CharacterCalculator.InitiativeField);

        Assert.Equal(3, initiative.Value);
        Assert.Equal(
            [("base", 14, 14), ("add", 2, 16), ("derive", 16, 3)],
            initiative.Trace.Select(t => (t.Operation, t.Amount ?? 0, t.Result)));
        Assert.Equal(Fixtures.Courier, initiative.Trace[1].Origin.Content);
        Assert.Equal(Fixtures.Source2024, initiative.Trace[1].Origin.SourceId);

        var warning = Assert.Single(initiative.Warnings);
        Assert.Equal("policy.ability-increase-source", warning.Code);
        Assert.Equal(Fixtures.Wanderer, warning.Content);
        Assert.Equal("wanderer-dex", warning.EffectId);
    }

    [Fact]
    public void Same_species_is_treated_differently_by_each_rules_family()
    {
        var catalog = Fixtures.Catalog();
        var base51 = Fixtures.Srd51Character() with { Pins = [Fixtures.Wanderer] };
        var base521 = base51 with { RulesFamily = RulesFamilies.Srd521 };

        var dex51 = CharacterCalculator.Calculate(base51, catalog).Field(CharacterCalculator.InitiativeField);
        var dex521 = CharacterCalculator.Calculate(base521, catalog).Field(CharacterCalculator.InitiativeField);

        Assert.Contains(dex51.Trace, t => t.Origin.Content == Fixtures.Wanderer);
        Assert.DoesNotContain(dex521.Trace, t => t.Origin.Content == Fixtures.Wanderer);
        Assert.Single(dex521.Warnings);
    }

    [Fact]
    public void Draft_revision_never_affects_calculation()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Srd521Character(), Fixtures.Catalog());
        var initiative = sheet.Field(CharacterCalculator.InitiativeField);

        Assert.DoesNotContain(initiative.Trace, t => t.Origin.Content == Fixtures.UnreviewedTrick);
        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.Code == "content.unpublished");
        Assert.Equal(Fixtures.UnreviewedTrick, diagnostic.Content);
    }

    [Fact]
    public void Unsupported_effect_is_reported_as_reference_only_without_breaking_the_sheet()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Srd521Character(), Fixtures.Catalog());

        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.Code == "effect.unsupported");
        Assert.Equal(Fixtures.StarSense, diagnostic.Content);
        Assert.Equal(3, sheet.Field(CharacterCalculator.InitiativeField).Value);
    }

    [Fact]
    public void Content_for_another_rules_family_is_not_applied()
    {
        var character = Fixtures.Srd521Character() with { Pins = [Fixtures.Quickfoot] };

        var sheet = CharacterCalculator.Calculate(character, Fixtures.Catalog());

        Assert.Equal("content.rules-family-mismatch", Assert.Single(sheet.Diagnostics).Code);
        Assert.Equal(2, sheet.Field(CharacterCalculator.InitiativeField).Value);
    }

    [Fact]
    public void Missing_pinned_revision_is_isolated()
    {
        var missing = new ContentReference(Guid.NewGuid(), Guid.NewGuid());
        var character = Fixtures.Srd51Character() with { Pins = [missing] };

        var sheet = CharacterCalculator.Calculate(character, Fixtures.Catalog());

        Assert.Equal("content.missing", Assert.Single(sheet.Diagnostics).Code);
        Assert.Equal(2, sheet.Field(CharacterCalculator.InitiativeField).Value);
    }

    [Fact]
    public void Override_is_the_final_labeled_layer_and_preserves_the_computed_trace()
    {
        var character = Fixtures.Srd51Character() with
        {
            Overrides = [new FieldOverride(CharacterCalculator.InitiativeField, 7, "Magic item not modeled yet")],
        };

        var initiative = CharacterCalculator.Calculate(character, Fixtures.Catalog()).Field(CharacterCalculator.InitiativeField);

        Assert.Equal(7, initiative.Value);
        Assert.Equal(4, initiative.ComputedValue);
        Assert.NotNull(initiative.Override);
        var last = initiative.Trace[^1];
        Assert.Equal("override", last.Operation);
        Assert.Equal(TraceOriginKind.Override, last.Origin.Kind);
        Assert.Contains("Magic item not modeled yet", last.Description, StringComparison.Ordinal);
        Assert.Equal(4, initiative.Trace[^2].Result);
    }

    [Fact]
    public void Unknown_rules_family_fails_validation_and_calculation()
    {
        var character = Fixtures.Srd51Character() with { RulesFamily = "5e" };

        Assert.Contains(character.Validate(), d => d.Code == "character.rules-family-unknown");
        Assert.Throws<ArgumentException>(() => CharacterCalculator.Calculate(character, Fixtures.Catalog()));
    }

    [Fact]
    public void Rules_family_policies_encode_the_ability_increase_difference_explicitly()
    {
        Assert.Equal(ContentKind.Species, RulesFamilies.Get(RulesFamilies.Srd51).AbilityIncreaseSource);
        Assert.Equal(ContentKind.Background, RulesFamilies.Get(RulesFamilies.Srd521).AbilityIncreaseSource);
    }
}
