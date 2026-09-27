namespace TomeStack.RulesCore.Tests;

/// <summary>
/// SPEC Q-02 / C-03: malformed data that nullable annotations and schemas do not stop (empty list entries, a choice
/// count below 1, a hit die outside d6–d12) is reported and isolated, never applied and never a crash.
/// </summary>
public class MalformedContentTests
{
    private static Character Ash() => Fixtures.Load("srd521-ash-m1.json") with { Pins = [] };

    [Fact]
    public void Empty_entries_in_a_character_are_reported_on_their_own()
    {
        var character = Ash() with
        {
            Pins = [null!],
            Classes = [null!],
            Choices = [new(Fixtures.Warden, "warden-skills", [null!])],
            CrossFamilyExceptions = [null!],
            Overrides = [null!],
        };

        var problems = character.Validate();

        Assert.All(problems, p => Assert.Equal("character.empty-entry", p.Code));
        Assert.Equal(5, problems.Count);
        Assert.Contains(problems, p => p.Message.Contains("pins", StringComparison.Ordinal));
        Assert.Empty(Ash().Validate());
    }

    [Fact]
    public void Empty_entries_in_a_revision_are_reported_before_anything_else_is_checked()
    {
        var revision = ChoiceTests.Homebrew(20, ContentKind.Feat, null!, new ChoiceEffect { Id = "pick", ChoiceId = "pick", Options = [null!] }) with
        {
            RulesFamilies = [null!],
        };

        var report = ContentValidator.Validate(revision, Fixtures.M1Catalog());

        Assert.False(report.CanPublish);
        Assert.Equal(["validate.empty-entry", "validate.empty-entry", "validate.empty-entry"], report.Errors.Select(e => e.Code));
        Assert.Empty(ContentValidator.EmptyEntries(ChoiceTests.Homebrew(21, ContentKind.Feat)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_choice_with_a_count_below_one_is_not_offered_and_applies_nothing(int count)
    {
        var offering = ChoiceTests.Homebrew(22, ContentKind.Feat,
            new ChoiceEffect { Id = "pick", ChoiceId = "pick", Count = count, Options = [Fixtures.WardenAthletics, Fixtures.WardenSurvival] });
        var character = Ash() with
        {
            Pins = [offering.Reference],
            Choices = [new(offering.Reference, "pick", [Fixtures.WardenAthletics, Fixtures.WardenSurvival])],
        };

        var sheet = ChoiceTests.SheetWith(character, offering);

        Assert.Equal(offering.Reference, Assert.Single(sheet.Diagnostics, d => d.Code == "choice.invalid-count").Content);
        Assert.Equal([offering.Reference], sheet.Active!);
        Assert.Empty(sheet.Choices!);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "choice.orphaned");
    }

    [Fact]
    public void A_hit_die_outside_d6_to_d12_is_ignored_with_a_diagnostic()
    {
        var giant = ChoiceTests.Homebrew(23, ContentKind.Class, new HitDieEffect { Id = "hd", Die = 1_000_000 });
        var character = Ash() with { Classes = [new(giant.Reference, 3)], Level = 3 };

        var sheet = ChoiceTests.SheetWith(character, giant);

        Assert.Equal("hd", Assert.Single(sheet.Diagnostics, d => d.Code == "class.hit-die-invalid").EffectId);
        var hp = sheet.Field(FieldIds.HitPoints);
        Assert.Equal(1 * 3, hp.Value); // only the Constitution modifier (+1 for Con 12) per level
        Assert.Equal(AutomationStatus.Assisted, hp.Automation);
    }
}
