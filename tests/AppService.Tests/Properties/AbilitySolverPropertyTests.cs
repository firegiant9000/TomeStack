using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TomeStack.AppService.CharacterImport;
using TomeStack.RulesCore;
using static TomeStack.AppService.Tests.DdbTestContent;

namespace TomeStack.AppService.Tests.Properties;

/// <summary>Feats that each give Strength one bonus from −5 to +5 (three of each), published once for the property.</summary>
public sealed class SolverPalette : IDisposable
{
    public SolverPalette()
    {
        var source = Source(Temp, "Fixture Solver Palette", RulesFamilies.Srd521);
        foreach (var value in Enumerable.Range(-5, 11).Where(v => v != 0))
        {
            Feats[value] =
            [
                .. Enumerable.Range(0, 3).Select(i => Publish(Temp, source, ContentKind.Feat, $"Fixture Palette {value:+0;-0} {i}", [RulesFamilies.Srd521],
                    [Modifier("m", FieldIds.Score(Ability.Str), ModifierOperation.Bonus, value)])),
            ];
        }
    }

    internal TempApp Temp { get; } = new();

    internal Dictionary<int, ContentReference[]> Feats { get; } = [];

    public void Dispose() => Temp.Dispose();
}

/// <summary>
/// Character-sheet import S3: for any base scores and any Strength bonuses, the solver's base calculates back to the
/// sheet's scores, or a note says why not (the cap or the 1–30 range).
/// </summary>
public class AbilitySolverPropertyTests(SolverPalette palette) : IClassFixture<SolverPalette>
{
    public sealed record Case(int[] Bases, int[] Bonuses)
    {
        public override string ToString() => $"bases [{string.Join(", ", Bases)}], Strength bonuses [{string.Join(", ", Bonuses)}]";
    }

    private static Gen<Case> Cases { get; } =
        from bases in Gen.Choose(1, 30).ArrayOf(6)
        from count in Gen.Choose(0, 3)
        from bonuses in Gen.Elements(-5, -4, -3, -2, -1, 1, 2, 3, 4, 5).ArrayOf(count)
        select new Case(bases, bonuses);

    [Property(MaxTest = 150)]
    public Property For_any_base_and_bonus_set_the_solver_recovers_a_base_whose_preview_equals_the_sheet_or_notes_why_not() =>
        Prop.ForAll(Cases.ToArbitrary(), c =>
        {
            // Each bonus value has three feats, so up to three equal bonuses pin three different revisions.
            var pins = c.Bonuses.GroupBy(b => b).SelectMany(g => palette.Feats[g.Key].Take(g.Count())).ToArray();
            var character = Character(RulesFamilies.Srd521, pins) with { BaseAbilities = new(c.Bases[0], c.Bases[1], c.Bases[2], c.Bases[3], c.Bases[4], c.Bases[5]) };
            CharacterSheet Preview(AbilityScores bases) => palette.Temp.App.Preview(character with { BaseAbilities = bases }).Sheet;
            var calculated = Preview(character.BaseAbilities);
            var sheet = Enum.GetValues<Ability>().ToDictionary(a => a, a => calculated.Field(FieldIds.Score(a)).Value);

            var plan = AbilitySolver.Solve(Preview, sheet);
            var again = Preview(plan.ProposedBase);

            return Enum.GetValues<Ability>().All(a => again.Field(FieldIds.Score(a)).Value == sheet[a] || plan.Notes.Any(n => n.Ability == a));
        });
}
