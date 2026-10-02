using BenchmarkDotNet.Attributes;
using TomeStack.AppService;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.Benchmarks;

/// <summary>
/// T4 case 2: a large original source pack (250 feats with 2 published revisions each: 500 revisions) imported into a
/// fresh data folder. Opening the folder (which seeds the bundled SRD packs) is in the iteration setup and not measured;
/// <see cref="Apply"/> is the import alone, <see cref="Preview"/> the preview the UI shows first.
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 20, invocationCount: 1)]
public class ImportBenchmarks
{
    public const int Feats = 250;

    private byte[] _pack = [];
    private TomeStackApp _previewTarget = null!;
    private TomeStackApp _target = null!;

    [GlobalSetup]
    public void Setup()
    {
        var author = BenchmarkLibrary.Open(BenchmarkLibrary.NewFolder());
        try
        {
            var source = author.CreateHomebrewSource(new("Fixture Benchmark Library", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
            for (var i = 0; i < Feats; i++)
            {
                BenchmarkLibrary.Publish(author, BenchmarkLibrary.Feat(source.Id, i, 1));
                BenchmarkLibrary.Publish(author, BenchmarkLibrary.Feat(source.Id, i, 2));
            }
            author.SetShareable(new(source.Id, Shareable: true, ConfirmOwnWork: true));
            var export = author.ExportSourcePack([source.Id]);
            _pack = export.Content;
            Console.WriteLine($"// source pack: {_pack.Length:N0} bytes, {export.Manifest.Entries.Count} entries");
        }
        finally
        {
            BenchmarkLibrary.Delete(author);
        }
        _previewTarget = BenchmarkLibrary.Open(BenchmarkLibrary.NewFolder());
    }

    [GlobalCleanup]
    public void Cleanup() => BenchmarkLibrary.Delete(_previewTarget);

    [IterationSetup(Target = nameof(Apply))]
    public void OpenFreshFolder() => _target = BenchmarkLibrary.Open(BenchmarkLibrary.NewFolder());

    [IterationCleanup(Target = nameof(Apply))]
    public void DeleteFolder() => BenchmarkLibrary.Delete(_target);

    [Benchmark]
    public PackagePreview Preview() => _previewTarget.PreviewImport(_pack);

    [Benchmark]
    public ImportResult Apply() => _target.ApplyImport(_pack);
}
