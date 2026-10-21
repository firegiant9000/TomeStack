namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Tester feedback item 12 (R48): the shapes the studio's species and background editors write are the shapes the calculator
/// already reads. A homebrew species carries its increases as modifiers on the ability scores (the older rules' source); a
/// homebrew background carries them as a choice among option features (the newer rules' source). The policy decides which
/// one counts, never a family name. Original fixtures only.
/// </summary>
public class OriginEditorShapeTests
{
    private static ModifierEffect Bonus(string id, Ability ability, int value) =>
        new() { Id = id, Operation = ModifierOperation.Bonus, Target = FieldIds.Score(ability), Value = $"{value}" };

    private static readonly ContentRevision Species = ChoiceTests.Homebrew(21, ContentKind.Species, Bonus("str", Ability.Str, 2), Bonus("dex", Ability.Dex, 1));

    private static readonly ContentRevision StrDexOption = ChoiceTests.Homebrew(22, ContentKind.Feature, Bonus("str", Ability.Str, 2), Bonus("dex", Ability.Dex, 1));

    private static readonly ContentRevision Background = ChoiceTests.Homebrew(23, ContentKind.Background,
        new ChoiceEffect { Id = "fixture-ability-scores", ChoiceId = "fixture-ability-scores", Count = 1, Options = [StrDexOption.Reference] });

    private static Character Character(string family, params ContentRevision[] pinned) =>
        Fixtures.Load("srd521-ash-m1.json") with
        {
            RulesFamily = family,
            Pins = [.. pinned.Select(p => p.Reference)],
            Choices = pinned.Contains(Background) ? [new(Background.Reference, "fixture-ability-scores", [StrDexOption.Reference])] : [],
        };

    [Theory]
    [InlineData(RulesFamilies.Srd51, 12, 15)]
    [InlineData(RulesFamilies.Srd521, 10, 14)]
    public void Species_modifiers_raise_the_scores_only_where_the_policy_says_the_species_is_the_source(string family, int str, int dex)
    {
        var sheet = ChoiceTests.SheetWith(Character(family, Species), Species);

        Assert.Equal(str, sheet.Field(FieldIds.Score(Ability.Str)).Value);
        Assert.Equal(dex, sheet.Field(FieldIds.Score(Ability.Dex)).Value);
        Assert.Equal(family == RulesFamilies.Srd521, sheet.Field(FieldIds.Score(Ability.Str)).Warnings.Any(w => w.Code == "policy.ability-increase-source"));
    }

    [Theory]
    [InlineData(RulesFamilies.Srd521, 12, 15)]
    [InlineData(RulesFamilies.Srd51, 10, 14)]
    public void A_background_choice_of_option_features_raises_the_scores_only_where_the_policy_says_the_background_is_the_source(string family, int str, int dex)
    {
        var sheet = ChoiceTests.SheetWith(Character(family, Background), StrDexOption, Background);

        Assert.Equal(str, sheet.Field(FieldIds.Score(Ability.Str)).Value);
        Assert.Equal(dex, sheet.Field(FieldIds.Score(Ability.Dex)).Value);
        Assert.Equal(family == RulesFamilies.Srd51, sheet.Field(FieldIds.Score(Ability.Str)).Warnings.Any(w => w.Code == "policy.ability-increase-source"));
    }
}
