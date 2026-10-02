using BenchmarkDotNet.Attributes;
using TomeStack.AppService;
using TomeStack.RulesCore;

namespace TomeStack.Benchmarks;

/// <summary>
/// T4 case 4: after one homebrew revision changes, the dependency recalculation across N characters that pin it. That is
/// the path the sheet takes: <c>character.updates</c> finds the newer revision (<see cref="FindUpdates"/>), and
/// <c>character.reviewUpdate</c> calculates each sheet before and after and diffs them (<see cref="ReviewAll"/>). The
/// characters are the level-20 three-class character of case 1, alternating rules families: the worst case.
/// </summary>
public class ReviewUpdateBenchmarks
{
    private TomeStackApp _app = null!;
    private ContentReference _from = null!;
    private ContentReference _to = null!;
    private Guid[] _characters = [];

    [Params(1, 10, 100)]
    public int Characters { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _app = BenchmarkLibrary.Open(BenchmarkLibrary.NewFolder());
        var source = _app.CreateHomebrewSource(new("Fixture Benchmark Updates", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        _from = BenchmarkLibrary.Publish(_app, BenchmarkLibrary.Feat(source.Id, 0, 1));
        var templates = new Dictionary<string, Character>();
        foreach (var family in new[] { RulesFamilies.Srd51, RulesFamilies.Srd521 })
        {
            var calculation = new CalculationBenchmarks { Family = family };
            calculation.Setup();
            templates[family] = calculation.Character;
        }
        _characters = [.. Enumerable.Range(0, Characters).Select(i =>
        {
            var template = templates[i % 2 == 0 ? RulesFamilies.Srd51 : RulesFamilies.Srd521];
            return _app.SaveCharacter(template with { Id = Guid.NewGuid(), Name = $"Benchmark Character {i:D3}", Pins = [_from] }).Character.Id;
        })];
        _to = BenchmarkLibrary.Publish(_app, BenchmarkLibrary.Feat(source.Id, 0, 2));
    }

    [GlobalCleanup]
    public void Cleanup() => BenchmarkLibrary.Delete(_app);

    [Benchmark]
    public int FindUpdates()
    {
        var offers = 0;
        foreach (var id in _characters)
            offers += _app.AvailableUpdates(id).Count;
        return offers;
    }

    [Benchmark]
    public int ReviewAll()
    {
        var changed = 0;
        foreach (var id in _characters)
            changed += _app.ReviewUpdate(id, _from, _to).Fields.Count;
        return changed;
    }
}
