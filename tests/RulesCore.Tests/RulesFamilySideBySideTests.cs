namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Item 12 / ROADMAP M1 risk response: the same inputs give different, explained outputs per rules family.
/// Uses the original M1 fixtures (tests/RulesFixtures/fixture-pack-m1.json). Differences come only from
/// <see cref="RulesFamilyPolicy"/> fields and explicit per-family content, never from names.
/// </summary>
public class RulesFamilySideBySideTests
{
    private static CharacterSheet Sheet(string file) => CharacterCalculator.Calculate(Fixtures.Load(file), Fixtures.M1Catalog());

    [Fact]
    public void Same_background_is_explained_differently_by_each_family()
    {
        var ash14 = Sheet("srd51-ash-m1.json");
        var ash24 = Sheet("srd521-ash-m1.json");

        // Wisdom: the background's +1 applies only where backgrounds grant ability increases (2024).
        Assert.Equal((13, 14), (ash14.Field(FieldIds.Score(Ability.Wis)).Value, ash24.Field(FieldIds.Score(Ability.Wis)).Value));
        var blocked = Assert.Single(ash14.Field(FieldIds.Score(Ability.Wis)).Warnings);
        Assert.Equal(("policy.ability-increase-source", "wayfarer-wis"), (blocked.Code, blocked.EffectId));
        Assert.Contains(ash24.Field(FieldIds.Score(Ability.Wis)).Trace, t => t.Origin.EffectId == "wayfarer-wis" && t.Operation == "add");

        // Initiative: the background's feat is granted only where backgrounds grant feats (2024).
        Assert.Equal((2, 2 + 3), (ash14.Field(FieldIds.Initiative).Value, ash24.Field(FieldIds.Initiative).Value));
        var noFeat = Assert.Single(ash14.Diagnostics, d => d.Code == "policy.background-feat");
        Assert.Equal((Fixtures.Wayfarer, "wayfarer-feat"), (noFeat.Content, noFeat.EffectId));
        var featStep = ash24.Field(FieldIds.Initiative).Trace[^1];
        Assert.Equal(("add", 3, Fixtures.Watchful), (featStep.Operation, featStep.Amount!.Value, featStep.Origin.Content));
        Assert.Contains("granted by background 'Fixture Wayfarer'", featStep.Description, StringComparison.Ordinal);
        Assert.Equal([new TraceInput("PB", 3)], featStep.Inputs);
    }

    [Fact]
    public void Same_named_content_is_never_merged_across_families()
    {
        var ash14 = Sheet("srd51-ash-m1.json");
        var ash24 = Sheet("srd521-ash-m1.json");

        // 2014 "Fixture Keen Senses": species +1 Dex and +1 Stealth; 2024 "Fixture Keen Senses": Stealth expertise.
        var stealth14 = ash14.Field(FieldIds.Skill("stealth"));
        var stealth24 = ash24.Field(FieldIds.Skill("stealth"));
        Assert.Equal((2 + 3 + 1, 2 + 6), (stealth14.Value, stealth24.Value));
        Assert.Contains(stealth14.Trace, t => t.Origin.Content == Fixtures.KeenSenses2014);
        Assert.DoesNotContain(stealth14.Trace, t => t.Origin.Content == Fixtures.KeenSenses2024);
        Assert.Contains(stealth24.Trace, t => t.Origin.Content == Fixtures.KeenSenses2024 && t.Description.StartsWith("Expertise", StringComparison.Ordinal));
        Assert.Equal((15, 14), (ash14.Field(FieldIds.Score(Ability.Dex)).Value, ash24.Field(FieldIds.Score(Ability.Dex)).Value));

        // Pinning the other family's entry by name alone does nothing and says why.
        var wrong = Fixtures.Load("srd521-ash-m1.json") with { Pins = [Fixtures.Wayfarer, Fixtures.KeenSenses2014] };
        var mismatch = Assert.Single(CharacterCalculator.Calculate(wrong, Fixtures.M1Catalog()).Diagnostics, d => d.Code == "content.rules-family-mismatch");
        Assert.Equal(Fixtures.KeenSenses2014, mismatch.Content);
    }

    [Fact]
    public void Recorded_cross_family_exception_applies_the_content_under_the_characters_own_policy_with_a_warning()
    {
        var rook = Sheet("srd521-rook-exception.json");

        var warning = Assert.Single(rook.Diagnostics, d => d.Code == "content.cross-family-exception");
        Assert.Equal(Fixtures.KeenSenses2014, warning.Content);
        Assert.Contains("Table ruling", warning.Message, StringComparison.Ordinal);
        Assert.Contains("SRD 5.2.1 (2024 rules)", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rook.Diagnostics, d => d.Code == "content.rules-family-mismatch");

        // The 2014 entry's +1 Stealth applies; its species Dex increase does not, because 2024 policy governs.
        Assert.Equal(2 + 3 + 1, rook.Field(FieldIds.Skill("stealth")).Value);
        Assert.Equal(14, rook.Field(FieldIds.Score(Ability.Dex)).Value);
        Assert.Contains(rook.Field(FieldIds.Score(Ability.Dex)).Warnings, w => w.Code == "policy.ability-increase-source" && w.EffectId == "keen14-dex");
        Assert.Equal(2 + 3, rook.Field(FieldIds.Initiative).Value);
    }

    [Fact]
    public void Exception_without_a_reason_fails_validation_and_an_unused_one_is_reported()
    {
        var rook = Fixtures.Load("srd521-rook-exception.json");

        Assert.Contains((rook with { CrossFamilyExceptions = [new(Fixtures.KeenSenses2014, " ")] }).Validate(), d => d.Code == "character.exception-reason-required");
        var unused = rook with { Pins = [Fixtures.Wayfarer] };
        Assert.Contains(CharacterCalculator.Calculate(unused, Fixtures.M1Catalog()).Diagnostics, d => d.Code == "character.exception-unused");
    }

    [Fact]
    public void Granted_content_is_followed_one_level_deep_only()
    {
        var catalog = Fixtures.M1Catalog();
        var watchful = catalog.FindRevision(Fixtures.Watchful)!;
        var chaining = watchful with
        {
            RevisionId = Guid.Parse("5f1de000-0000-4000-8000-0000000000ff"),
            Effects = [.. watchful.Effects, new GrantEffect { Id = "chain", Grant = GrantKind.Content, Content = Fixtures.Wayfarer }],
        };
        var granter = catalog.FindRevision(Fixtures.Wayfarer)! with
        {
            RevisionId = Guid.Parse("5f1de000-0000-4000-8000-0000000000fe"),
            Effects = [new GrantEffect { Id = "grant", Grant = GrantKind.Content, Content = chaining.Reference }],
        };
        var pack = Fixtures.M1Pack();
        var withChain = new InMemoryContentCatalog([.. Fixtures.Pack().Sources, .. pack.Sources], [.. Fixtures.Pack().Revisions, .. pack.Revisions, chaining, granter]);
        var character = Fixtures.Load("srd521-ash-m1.json") with { Pins = [granter.Reference] };

        var sheet = CharacterCalculator.Calculate(character, withChain);

        Assert.Contains(sheet.Diagnostics, d => d.Code == "grant.nested-ignored" && d.EffectId == "chain");
        Assert.Equal(2 + 3, sheet.Field(FieldIds.Initiative).Value); // the granted feat applies; its grant is not followed
    }

    [Fact]
    public void Policy_differences_are_named_fields_not_inferred()
    {
        var p2014 = RulesFamilies.Get(RulesFamilies.Srd51);
        var p2024 = RulesFamilies.Get(RulesFamilies.Srd521);

        Assert.Equal((ContentKind.Species, false), (p2014.AbilityIncreaseSource, p2014.BackgroundGrantsFeat));
        Assert.Equal((ContentKind.Background, true), (p2024.AbilityIncreaseSource, p2024.BackgroundGrantsFeat));
    }
}
