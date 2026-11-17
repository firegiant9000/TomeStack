namespace TomeStack.RulesCore.Tests;

/// <summary>
/// The prerequisite check for a candidate class (D99): the same check the sheet uses for <c>restriction.multiclass-unmet</c>,
/// asked before the class is taken. Original fixture: the "Chronicler" class needs Intelligence 13 (the SRD Wizard's rule, same shape).
/// </summary>
public class MulticlassPrerequisiteTests
{
    private static readonly string[] BothFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521];

    private static (CharacterSheet Sheet, ContentRevision Candidate) Setup(string family, int intelligence)
    {
        var catalog = Fixtures.ChroniclerCatalog();
        var sheet = CharacterCalculator.Calculate(
            CustomClassTests.Chronicler(family, intelligence, new ClassLevel(Fixtures.Scholar, 2)), catalog);
        return (sheet, catalog.FindRevision(Fixtures.Chronicler)!);
    }

    [Fact]
    public void Intelligence_8_gives_one_diagnostic_naming_the_minimum_and_the_score()
    {
        foreach (var family in BothFamilies)
        {
            var (sheet, candidate) = Setup(family, 8);

            var diagnostic = Assert.Single(CharacterCalculator.MulticlassPrerequisites(sheet, candidate));

            Assert.Equal("restriction.multiclass-unmet", diagnostic.Code);
            Assert.Equal(Fixtures.Chronicler, diagnostic.Content);
            Assert.Contains("requires Intelligence score 13 or higher; this character has 8.", diagnostic.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Shortfalls_give_the_structured_requirement_and_agree_with_the_diagnostics()
    {
        foreach (var family in BothFamilies)
        {
            var (low, candidate) = Setup(family, 8);
            var group = Assert.Single(CharacterCalculator.MulticlassShortfalls(low, candidate));
            var shortfall = Assert.Single(group);
            Assert.Equal(new PrerequisiteShortfall("Intelligence score", 13, 8), shortfall);

            var (met, _) = Setup(family, 13);
            Assert.Empty(CharacterCalculator.MulticlassShortfalls(met, candidate));
        }
    }

    [Fact]
    public void Intelligence_13_gives_none()
    {
        foreach (var family in BothFamilies)
        {
            var (sheet, candidate) = Setup(family, 13);

            Assert.Empty(CharacterCalculator.MulticlassPrerequisites(sheet, candidate));
        }
    }
}
