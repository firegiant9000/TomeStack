using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M1 item 6: the rules-family policy fields, exercised with real SRD content. The SRD slice needs no new policy field
/// (docs/features/rules-family-policy.md). Using one family's content under the other needs a recorded exception
/// (BACKLOG B06), and then the character's own policy governs it.
/// </summary>
public class SrdRulesFamilyTests
{
    private static readonly ContentPack Srd51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1.json");
    private static readonly ContentPack Srd521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1.json");
    private static readonly InMemoryContentCatalog Catalog = new([.. Srd51.Sources, .. Srd521.Sources], [.. Srd51.Revisions, .. Srd521.Revisions]);

    private static ContentReference Named(ContentPack pack, string name) => pack.Revisions.Single(r => r.Name == name).Reference;

    private static Character Base(string family) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Policy check",
        RulesFamily = family,
        BaseAbilities = new AbilityScores(15, 12, 14, 10, 10, 8),
    };

    [Fact]
    public void Species_ability_increases_apply_under_2014_rules_and_not_under_2024_rules()
    {
        var halfOrc = Named(Srd51, "Half-Orc");
        var as2014 = CharacterCalculator.Calculate(Base(RulesFamilies.Srd51) with { Pins = [halfOrc] }, Catalog);
        var as2024 = CharacterCalculator.Calculate(
            Base(RulesFamilies.Srd521) with { Pins = [halfOrc], CrossFamilyExceptions = [new(halfOrc, "Table allows the 2014 species")] }, Catalog);

        Assert.Equal((17, 15), (as2014.Field(FieldIds.Score(Ability.Str)).Value, as2024.Field(FieldIds.Score(Ability.Str)).Value));
        Assert.Contains(as2024.Field(FieldIds.Score(Ability.Str)).Warnings, w => w.Code == "policy.ability-increase-source" && w.EffectId == "half-orc-str");
        // Non-ability traits still apply under the exception: Menacing's Intimidation proficiency.
        Assert.Equal(-1 + 2, as2024.Field(FieldIds.Skill("intimidation")).Value);
    }

    [Fact]
    public void A_2024_background_raises_scores_and_grants_its_feat_but_not_under_2014_rules()
    {
        var soldier = Named(Srd521, "Soldier");
        var option = Named(Srd521, "Soldier Ability Scores: Strength +2, Constitution +1");
        var choice = new ChoiceSelection(soldier, "soldier-ability-scores", [option]);
        var as2024 = CharacterCalculator.Calculate(Base(RulesFamilies.Srd521) with { Pins = [soldier], Choices = [choice] }, Catalog);
        var as2014 = CharacterCalculator.Calculate(
            Base(RulesFamilies.Srd51) with
            {
                Pins = [soldier],
                Choices = [choice],
                CrossFamilyExceptions = [new(soldier, "Testing the 2024 background"), new(option, "Testing the 2024 background")],
            },
            Catalog);

        // AbilityIncreaseSource: the chosen option is background content (a feature chosen from the background).
        Assert.Equal((17, 15), (as2024.Field(FieldIds.Score(Ability.Str)).Value, as2014.Field(FieldIds.Score(Ability.Str)).Value));
        Assert.Contains(as2014.Field(FieldIds.Score(Ability.Str)).Warnings, w => w.Code == "policy.ability-increase-source");
        // BackgroundGrantsFeat: Savage Attacker is granted only under 2024 rules.
        Assert.DoesNotContain(as2024.Diagnostics, d => d.Code == "policy.background-feat");
        Assert.Contains(as2014.Diagnostics, d => d.Code == "policy.background-feat" && d.EffectId == "soldier-feat");
        // Skill proficiencies are the same under both: Athletics = Str modifier + PB 2 (Str 17 vs 15 is the only difference).
        Assert.Equal((3 + 2, 2 + 2), (as2024.Field(FieldIds.Skill("athletics")).Value, as2014.Field(FieldIds.Skill("athletics")).Value));
        Assert.All(new[] { as2024, as2014 }, s => Assert.Contains(s.Field(FieldIds.Skill("athletics")).Trace, t => t.Origin.Content == soldier && t.Operation == "add"));
    }

    [Fact]
    public void A_2014_background_grants_its_feature_because_only_feats_are_restricted()
    {
        var sheet = CharacterCalculator.Calculate(Base(RulesFamilies.Srd51) with { Pins = [Named(Srd51, "Acolyte")] }, Catalog);

        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "policy.background-feat");
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Content == Named(Srd51, "Shelter of the Faithful"));
        Assert.Equal(0 + 2, sheet.Field(FieldIds.Skill("religion")).Value);
    }

    [Fact]
    public void Same_named_SRD_classes_are_never_merged_across_families()
    {
        var barbarian2014 = Named(Srd51, "Barbarian");
        var character = Base(RulesFamilies.Srd521) with { Classes = [new(barbarian2014, 1)] };

        var sheet = CharacterCalculator.Calculate(character, Catalog);

        Assert.Equal(barbarian2014, Assert.Single(sheet.Diagnostics, d => d.Code == "content.rules-family-mismatch").Content);
        Assert.Equal(AutomationStatus.Assisted, sheet.Field(FieldIds.HitPoints).Automation); // no usable class
    }
}
