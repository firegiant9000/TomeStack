using BenchmarkDotNet.Attributes;
using TomeStack.RulesCore;

namespace TomeStack.Benchmarks;

/// <summary>
/// T4 case 3: every formula in the bundled SRD packs (modifier values, resource maximums, recovery amounts, roll costs and
/// bonuses, prepared-spell formulas), parsed and evaluated against fixed identifier values.
/// </summary>
public class FormulaBenchmarks
{
    private (string Source, bool Scales)[] _sources = [];
    private Formula[] _parsed = [];

    private static readonly Dictionary<string, int?> Values =
        FormulaIdentifiers.All.ToDictionary(i => i, int? (_) => 3, StringComparer.Ordinal);

    private static int? Resolve(string identifier) =>
        Values.TryGetValue(identifier, out var value) ? value : FormulaIdentifiers.IsScale(identifier) ? 2 : null;

    private static IEnumerable<string?> FormulasOf(Effect effect) => effect switch
    {
        ModifierEffect m => [m.Value],
        ResourceEffect r => [r.Maximum],
        RecoveryEffect r when r.Amount != "all" => [r.Amount],
        RollEffect r => [r.Cost, r.Bonus],
        SpellcastingEffect s => [s.SpellsFormula],
        _ => [],
    };

    [GlobalSetup]
    public void Setup()
    {
        _sources = [.. Srd.Revisions.SelectMany(r => r.Effects.SelectMany(FormulasOf).OfType<string>().Select(f => (f, r.SchemaVersion >= ScaleEffect.SchemaVersion)))];
        _parsed = [.. _sources.Select(s => Formula.TryParse(s.Source, s.Scales, out var f, out _) ? f! : throw new InvalidOperationException($"SRD formula '{s.Source}' does not parse."))];
        Console.WriteLine($"// {_sources.Length} formulas ({_sources.Select(s => s.Source).Distinct().Count()} distinct) in {Srd.Revisions.Count()} SRD revisions");
    }

    [Benchmark]
    public int ParseAll()
    {
        var parsed = 0;
        foreach (var (source, scales) in _sources)
        {
            if (Formula.TryParse(source, scales, out _, out _))
                parsed++;
        }
        return parsed;
    }

    [Benchmark]
    public int EvaluateAll()
    {
        var total = 0;
        foreach (var formula in _parsed)
        {
            if (formula.TryEvaluate(Resolve, out var value, out _))
                total += value;
        }
        return total;
    }
}
