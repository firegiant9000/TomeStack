using TomeStack.AppService.CharacterImport;
using TomeStack.RulesCore;
using static TomeStack.AppService.Tests.DdbTestContent;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Character-sheet import S3 (<c>features/ddb-pdf-import.md</c> "Ability scores"): the sheet shows final scores and
/// TomeStack stores base scores, so the base is solved from the calculator's own trace: a preview at base 1, the
/// <c>add</c> steps split into increases and penalties, the 20 cap, and <c>replace</c> and <c>set</c> left unsolved with a
/// note. Every content name is invented.
/// </summary>
public class AbilitySolverTests
{
    private static readonly string Str = FieldIds.Score(Ability.Str);

    /// <summary>A character pinning one feat per Strength modifier, and the solver's preview over it.</summary>
    private static (Func<AbilityScores, CharacterSheet> Preview, TempApp Temp) Setup(string family, params (ContentKind Kind, ModifierOperation Operation, int Value)[] modifiers)
    {
        var temp = new TempApp();
        var source = Source(temp, "Fixture Solver Notes", family);
        var pins = modifiers.Select((m, i) => Publish(temp, source, m.Kind, $"Fixture Solver Content {i}", [family], [Modifier($"m{i}", Str, m.Operation, m.Value)])).ToArray();
        var character = Character(family, pins);
        return (bases => temp.App.Preview(character with { BaseAbilities = bases }).Sheet, temp);
    }

    private static Dictionary<Ability, int> Sheet(int strength) =>
        Enum.GetValues<Ability>().ToDictionary(a => a, a => a == Ability.Str ? strength : 10);

    private static int Score(CharacterSheet sheet, Ability ability) => sheet.Field(FieldIds.Score(ability)).Value;

    [Fact]
    public void A_plain_bonus_gives_sheet_minus_bonus()
    {
        var (preview, temp) = Setup(RulesFamilies.Srd521, (ContentKind.Feat, ModifierOperation.Bonus, 2));
        using (temp)
        {
            var plan = AbilitySolver.Solve(preview, Sheet(16));

            Assert.Equal(14, plan.ProposedBase.Str);
            Assert.Equal(10, plan.ProposedBase.Dex);
            Assert.Empty(plan.Notes);
        }
    }

    [Fact]
    public void A_sheet_score_of_twenty_under_a_plus_two_gives_base_eighteen_and_notes_the_ambiguity()
    {
        var (preview, temp) = Setup(RulesFamilies.Srd521, (ContentKind.Feat, ModifierOperation.Bonus, 2));
        using (temp)
        {
            var plan = AbilitySolver.Solve(preview, Sheet(20));

            Assert.Equal(18, plan.ProposedBase.Str);
            var note = Assert.Single(plan.Notes);
            Assert.Equal((Ability.Str, "ability.cap-ambiguous"), (note.Ability, note.Code));
        }
    }

    [Fact]
    public void A_sheet_score_above_twenty_gives_base_equal_to_the_score_minus_penalties()
    {
        var (preview, temp) = Setup(RulesFamilies.Srd521, (ContentKind.Feat, ModifierOperation.Bonus, 2));
        using (temp)
        {
            var plan = AbilitySolver.Solve(preview, Sheet(22));

            Assert.Equal(22, plan.ProposedBase.Str);
            Assert.Equal(22, Score(preview(plan.ProposedBase), Ability.Str));
        }
    }

    [Theory]
    [InlineData(15, 14)] // 14 + 3 = 17, then -2
    [InlineData(19, 21)] // only a base of 21 or more keeps 21 under the cap before the -2
    public void A_penalty_and_a_bonus_together_solve_in_the_calculators_order(int sheet, int expectedBase)
    {
        var (preview, temp) = Setup(RulesFamilies.Srd521, (ContentKind.Feat, ModifierOperation.Bonus, -2), (ContentKind.Feat, ModifierOperation.Bonus, 3));
        using (temp)
        {
            var plan = AbilitySolver.Solve(preview, Sheet(sheet));

            Assert.Equal(expectedBase, plan.ProposedBase.Str);
            Assert.Equal(sheet, Score(preview(plan.ProposedBase), Ability.Str));
        }
    }

    [Theory]
    [InlineData(ModifierOperation.Set)]
    [InlineData(ModifierOperation.Replace)]
    public void A_score_set_or_replaced_by_content_is_not_solved_and_is_noted(ModifierOperation operation)
    {
        var (preview, temp) = Setup(RulesFamilies.Srd521, (ContentKind.Feat, operation, 19));
        using (temp)
        {
            var plan = AbilitySolver.Solve(preview, Sheet(19));

            Assert.Equal(19, plan.ProposedBase.Str);
            var note = Assert.Single(plan.Notes);
            Assert.Equal((Ability.Str, "ability.set-by-content"), (note.Ability, note.Code));
            Assert.Contains("Fixture Solver Content 0", note.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_bonus_the_family_policy_refuses_does_not_count()
    {
        // Under srd-5.1 only species raise ability scores; a background's +2 is skipped by the calculator.
        var (preview, temp) = Setup(RulesFamilies.Srd51, (ContentKind.Background, ModifierOperation.Bonus, 2));
        using (temp)
        {
            var plan = AbilitySolver.Solve(preview, Sheet(15));

            Assert.Equal(15, plan.ProposedBase.Str);
            Assert.Equal(15, Score(preview(plan.ProposedBase), Ability.Str));
        }
    }

    [Fact]
    public void A_base_outside_one_to_thirty_is_clamped_and_noted()
    {
        var (preview, temp) = Setup(RulesFamilies.Srd521, (ContentKind.Feat, ModifierOperation.Bonus, 5));
        using (temp)
        {
            var plan = AbilitySolver.Solve(preview, Sheet(3));

            Assert.Equal(1, plan.ProposedBase.Str);
            Assert.Equal((Ability.Str, "ability.out-of-range"), (Assert.Single(plan.Notes).Ability, Assert.Single(plan.Notes).Code));
        }
    }

    [Fact]
    public void A_score_the_sheet_lacks_keeps_ten_and_is_noted()
    {
        var (preview, temp) = Setup(RulesFamilies.Srd521);
        using (temp)
        {
            var plan = AbilitySolver.Solve(preview, new Dictionary<Ability, int> { [Ability.Str] = 12 });

            Assert.Equal(new AbilityScores(12, 10, 10, 10, 10, 10), plan.ProposedBase);
            Assert.Equal(5, plan.Notes.Count(n => n.Code == "ability.not-read"));
        }
    }

    [Fact]
    public void The_second_preview_equals_the_sheet_for_every_solved_score()
    {
        var (preview, temp) = Setup(RulesFamilies.Srd521, (ContentKind.Feat, ModifierOperation.Bonus, 1), (ContentKind.Feat, ModifierOperation.Bonus, 2));
        using (temp)
        {
            var sheet = new Dictionary<Ability, int> { [Ability.Str] = 17, [Ability.Dex] = 14, [Ability.Con] = 13, [Ability.Int] = 8, [Ability.Wis] = 12, [Ability.Cha] = 30 };

            var plan = AbilitySolver.Solve(preview, sheet);
            var calculated = preview(plan.ProposedBase);

            Assert.All(sheet, s => Assert.Equal(s.Value, Score(calculated, s.Key)));
        }
    }
}
