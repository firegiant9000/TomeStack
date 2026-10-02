using TomeStack.AppService;
using TomeStack.RulesCore;

namespace TomeStack.Benchmarks;

/// <summary>Throwaway data folders and original benchmark content. Nothing here is SRD or third-party text.</summary>
internal static class BenchmarkLibrary
{
    public static string NewFolder() => Path.Combine(Path.GetTempPath(), "tomestack-bench", Guid.NewGuid().ToString("N"));

    public static TomeStackApp Open(string folder) => TomeStackApp.Open(folder, TimeProvider.System, syncRoots: []);

    public static void Delete(TomeStackApp app)
    {
        var folder = app.DataDirectory;
        app.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    /// <summary>
    /// Content <paramref name="index"/> of a benchmark source: a feat with a bonus, a resource, its recovery and a roll that
    /// spends it. The content id is fixed by the index, so every run builds the same library.
    /// </summary>
    public static ContentRevision Feat(Guid source, int index, int bonus) => new()
    {
        ContentId = Guid.Parse($"7be0d000-0000-4000-8000-{index:D12}"),
        RevisionId = Guid.Empty,
        Kind = ContentKind.Feat,
        Name = $"Fixture Benchmark Feat {index:D3}",
        RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
        Provenance = new(source),
        Status = RevisionStatus.Draft,
        Summary = $"Original benchmark content number {index}.",
        Effects =
        [
            new ModifierEffect { Id = "bonus", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = $"{bonus}" },
            new ResourceEffect { Id = "pool", ResourceId = "spark", Label = "Sparks", Maximum = "PB + 1" },
            new RecoveryEffect { Id = "rest", ResourceId = "spark", On = RestPeriod.LongRest, Amount = "all" },
            new RollEffect { Id = "flash", RollId = "flash", Label = "Flash", Dice = "1d6+1", ResourceId = "spark", Activation = Activation.BonusAction },
        ],
    };

    public static ContentReference Publish(TomeStackApp app, ContentRevision draft) => app.Publish(app.SaveDraft(draft)).Published;
}
