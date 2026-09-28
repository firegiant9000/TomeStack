namespace TomeStack.RulesCore.Tests;

/// <summary>ADR-003 bounded formula grammar (SPEC Q-02, C-03). Content is untrusted: nothing here may throw.</summary>
public class FormulaTests
{
    private static readonly Dictionary<string, int> Values = new(StringComparer.Ordinal)
    {
        ["PB"] = 3, ["CON.MOD"] = 2, ["DEX.MOD"] = -1, ["DEX.SCORE"] = 8, ["CLASS_LEVEL"] = 5, ["LEVEL"] = 7,
    };

    private static int? Resolve(string identifier) => Values.TryGetValue(identifier, out var v) ? v : null;

    private static int Eval(string source)
    {
        Assert.True(Formula.TryParse(source, out var formula, out var parseError), parseError?.Message);
        Assert.True(formula!.TryEvaluate(Resolve, out var value, out var evalError), evalError?.Message);
        return value;
    }

    private static FormulaError Fails(string source)
    {
        if (!Formula.TryParse(source, out var formula, out var error))
            return error!;
        Assert.False(formula!.TryEvaluate(Resolve, out _, out error), $"'{source}' unexpectedly evaluated");
        return error!;
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData("-3", -3)]
    [InlineData("PB + CON.MOD", 5)]
    [InlineData("floor(CLASS_LEVEL / 2)", 2)]
    [InlineData("ceil(CLASS_LEVEL / 2)", 3)]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("10 - 4 - 3", 3)]
    [InlineData("7 / 2", 3)]
    [InlineData("-7 / 2", -4)]
    [InlineData("7 / 3 * 3", 7)]
    [InlineData("max(DEX.MOD, 1)", 1)]
    [InlineData("min(3, 1, 2)", 1)]
    [InlineData("abs(DEX.MOD)", 1)]
    [InlineData("--2", 2)]
    [InlineData("10000", 10000)]
    [InlineData("  PB*2  ", 6)]
    public void Valid_formulas_evaluate_with_5e_round_down(string source, int expected) =>
        Assert.Equal(expected, Eval(source));

    [Fact]
    public void Identifiers_are_reported_for_the_dependency_graph()
    {
        Assert.True(Formula.TryParse("PB + max(CON.MOD, DEX.MOD) + PB", out var formula, out _));

        Assert.Equal(["CON.MOD", "DEX.MOD", "PB"], formula!.Identifiers.Order(StringComparer.Ordinal));
        Assert.Equal(FieldIds.Modifier(Ability.Con), FormulaIdentifiers.FieldFor("CON.MOD"));
        Assert.Equal(FieldIds.ProficiencyBonus, FormulaIdentifiers.FieldFor("PB"));
        Assert.Null(FormulaIdentifiers.FieldFor("CLASS_LEVEL"));
    }

    [Theory]
    [InlineData("", "formula.empty")]
    [InlineData("   ", "formula.empty")]
    [InlineData("2 +", "formula.syntax")]
    [InlineData("+2", "formula.syntax")]
    [InlineData("2 ** 3", "formula.syntax")]
    [InlineData("(2", "formula.syntax")]
    [InlineData("2)", "formula.syntax")]
    [InlineData("()", "formula.syntax")]
    [InlineData("2 3", "formula.syntax")]
    [InlineData("1.5", "formula.syntax")]
    [InlineData("1e5", "formula.syntax")]
    [InlineData("floor()", "formula.syntax")]
    [InlineData("floor(1, 2)", "formula.wrong-argument-count")]
    [InlineData("min(1)", "formula.wrong-argument-count")]
    [InlineData("max(1, 2, 3, 4, 5)", "formula.wrong-argument-count")]
    [InlineData("eval(1)", "formula.unknown-function")]
    [InlineData("Floor(1)", "formula.unknown-function")]
    [InlineData("constructor(1)", "formula.unknown-function")]
    [InlineData("DEX", "formula.unknown-identifier")]
    [InlineData("dex.mod", "formula.unknown-identifier")]
    [InlineData("PB.MOD", "formula.unknown-identifier")]
    [InlineData("__proto__", "formula.unknown-identifier")]
    [InlineData("SPELL_LEVEL", "formula.unknown-identifier")]
    [InlineData("10001", "formula.number-too-large")]
    [InlineData("99999999999999999999999999999", "formula.number-too-large")]
    [InlineData("2 $ 3", "formula.invalid-character")]
    [InlineData("PB; DROP TABLE characters", "formula.invalid-character")]
    [InlineData("PB\n+1", "formula.invalid-character")]
    [InlineData("1\u00002", "formula.invalid-character")]
    [InlineData("１", "formula.invalid-character")]
    [InlineData("\"PB\"", "formula.invalid-character")]
    [InlineData("1 / 0", "formula.division-by-zero")]
    [InlineData("PB / (PB - PB)", "formula.division-by-zero")]
    [InlineData("10000 * 10000", "formula.out-of-range")]
    [InlineData("max(10000 * 100, 1) * 2", "formula.out-of-range")]
    [InlineData("SPELL_LEVEL + 1", "formula.unknown-identifier")]
    public void Malformed_or_hostile_formulas_fail_with_a_stable_code(string source, string code) =>
        Assert.Equal(code, Fails(source).Code);

    [Fact]
    public void Unavailable_values_fail_at_evaluation_not_parse()
    {
        Assert.True(Formula.TryParse("CLASS_LEVEL", out var formula, out _));

        Assert.False(formula!.TryEvaluate(_ => null, out _, out var error));
        Assert.Equal("formula.value-unavailable", error!.Code);
    }

    [Fact]
    public void Nesting_is_bounded()
    {
        Assert.Equal(1, Eval(new string('(', 8) + "1" + new string(')', 8)));
        Assert.Equal("formula.too-deep", Fails(new string('(', 9) + "1" + new string(')', 9)).Code);
        Assert.Equal("formula.too-deep", Fails(string.Concat(Enumerable.Repeat("-", 9)) + "1").Code);
        Assert.Equal("formula.too-deep", Fails(string.Concat(Enumerable.Repeat("floor(", 9)) + "1" + new string(')', 9)).Code);
        // Nesting well past the depth limit but within the length and token limits still fails as too deep.
        Assert.Equal("formula.too-deep", Fails(new string('(', 30) + "1" + new string(')', 30)).Code);
        // Beyond that, the cheaper length and token checks reject it before any recursion happens.
        Assert.Equal("formula.too-long", Fails(new string('(', 100) + "1" + new string(')', 100)).Code);
    }

    [Fact]
    public void Length_and_token_count_are_bounded()
    {
        Assert.Equal("formula.too-long", Fails(new string('1', FormulaLimits.MaxLength + 1)).Code);
        Assert.Equal("formula.too-long", Fails(new string(' ', 1_000_000) + "1").Code);
        var tokens65 = string.Join("+", Enumerable.Repeat("1", 33)); // 33 numbers + 32 operators
        Assert.Equal("formula.too-many-tokens", Fails(tokens65).Code);
        Assert.Equal(32, Eval(string.Join("+", Enumerable.Repeat("1", 32))));
    }

    [Fact]
    public void Fuzzed_input_never_throws_and_never_escapes_the_bounds()
    {
        const string alphabet = "0123456789+-*/(),. PBDEXCONSTRWISCHAMOLVfloraceimnxbs_$;\"'\\{}[]\u0000é１";
        var random = new Random(20260925);
        for (var i = 0; i < 20_000; i++)
        {
            var length = random.Next(0, 60);
            var chars = new char[length];
            for (var c = 0; c < length; c++)
                chars[c] = alphabet[random.Next(alphabet.Length)];
            var source = new string(chars);

            if (Formula.TryParse(source, out var formula, out var parseError))
            {
                if (formula!.TryEvaluate(Resolve, out var value, out _))
                    Assert.InRange(value, -FormulaLimits.MaxMagnitude, FormulaLimits.MaxMagnitude);
            }
            else
            {
                Assert.StartsWith("formula.", parseError!.Code, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Generated_well_formed_formulas_parse_and_evaluate_within_bounds()
    {
        var random = new Random(7);
        string[] leaves = ["PB", "CON.MOD", "DEX.MOD", "LEVEL", "CLASS_LEVEL", "1", "2", "10", "9999"];
        string Generate(int depth) => depth == 0 || random.Next(3) == 0
            ? leaves[random.Next(leaves.Length)]
            : random.Next(4) switch
            {
                0 => $"({Generate(depth - 1)} + {Generate(depth - 1)})",
                1 => $"{Generate(depth - 1)} * {Generate(depth - 1)}",
                2 => $"floor({Generate(depth - 1)} / {Generate(depth - 1)})",
                _ => $"max({Generate(depth - 1)}, {Generate(depth - 1)})",
            };

        for (var i = 0; i < 2_000; i++)
        {
            var source = Generate(4);
            if (!Formula.TryParse(source, out var formula, out var error))
            {
                Assert.Contains(error!.Code, new[] { "formula.too-long", "formula.too-many-tokens", "formula.too-deep" });
                continue;
            }
            if (formula!.TryEvaluate(Resolve, out var value, out error))
                Assert.InRange(value, -FormulaLimits.MaxMagnitude, FormulaLimits.MaxMagnitude);
            else
                Assert.Contains(error!.Code, new[] { "formula.out-of-range", "formula.division-by-zero" });
        }
    }

    [Fact]
    public void A_malformed_formula_disables_only_its_own_effect_with_a_diagnostic_naming_the_feature()
    {
        var pack = Fixtures.Pack();
        var broken = new ContentRevision
        {
            ContentId = Guid.Parse("f0000000-0000-4000-8000-000000000001"),
            RevisionId = Guid.Parse("f0000000-0000-4000-8000-000000000002"),
            Kind = ContentKind.Feat,
            Name = "Fixture Broken Formula",
            RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(Fixtures.SourceShared, new PageRef(9)),
            Status = RevisionStatus.Published,
            Effects =
            [
                new ModifierEffect { Id = "bad", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "PB; DROP TABLE" },
                new ModifierEffect { Id = "deep", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = new string('(', 20) + "1" + new string(')', 20) },
                new ModifierEffect { Id = "good", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "floor(5 / 2)" },
            ],
        };
        var catalog = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, broken]);
        var character = Fixtures.Srd51Character() with { Pins = [.. Fixtures.Srd51Character().Pins, broken.Reference] };

        var initiative = CharacterCalculator.Calculate(character, catalog).Field(CharacterCalculator.InitiativeField);

        Assert.Equal(4 + 2, initiative.Value); // the fixture's 4, plus the one good effect
        var problems = initiative.Warnings.Where(w => w.Code == "effect.invalid-formula").ToList();
        Assert.Equal(["bad", "deep"], problems.Select(p => p.EffectId));
        Assert.All(problems, p => Assert.Equal(broken.Reference, p.Content));
        Assert.Contains("Fixture Broken Formula", problems[0].Message, StringComparison.Ordinal);
        Assert.Contains("formula.invalid-character", problems[0].Message, StringComparison.Ordinal);
        Assert.Contains("formula.too-deep", problems[1].Message, StringComparison.Ordinal);
    }
}
