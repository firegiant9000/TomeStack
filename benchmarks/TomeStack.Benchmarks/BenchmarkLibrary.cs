using TomeStack.AppService;
using TomeStack.ImportWorker;
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

    /// <summary>
    /// Import Lab (L3): <paramref name="n"/> text-and-roll features of one source, named by index ("Fixture Feature 0017"), as drafts.
    /// The ids are fixed by the index, so every run builds the same library.
    /// </summary>
    public static IEnumerable<ContentRevision> Features(Guid source, int n) =>
        Enumerable.Range(0, n).Select(i => new ContentRevision
        {
            ContentId = Guid.Parse($"7be0f000-0000-4000-8000-{i:D12}"),
            RevisionId = Guid.Empty,
            Kind = ContentKind.Feature,
            Name = $"Fixture Feature {i:D4}",
            RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
            Provenance = new(source),
            Status = RevisionStatus.Draft,
            Summary = $"Original benchmark feature number {i}.",
            Effects =
            [
                new ModifierEffect { Id = "bonus", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = $"{i % 3 + 1}" },
                new RollEffect { Id = "flash", RollId = "flash", Label = "Flash", Dice = "1d6+1", Activation = Activation.BonusAction },
            ],
        });

    /// <summary>Import Lab (L3): <paramref name="n"/> pending candidates of one job, spread over pages, named by index. Not content.</summary>
    public static IEnumerable<StoredCandidate> Candidates(Guid job, Guid source, int n) =>
        Enumerable.Range(0, n).Select(i =>
        {
            var id = Guid.Parse($"7be0c000-0000-4000-8000-{i:D12}");
            return new StoredCandidate
            {
                Id = id,
                JobId = job,
                Candidate = new DraftCandidate(
                    id, source, new PageRef(1 + i / 4), $"Fixture candidate excerpt number {i}.", ContentKind.Feature, $"Fixture Candidate {i:D4}",
                    [RulesFamilies.Srd51, RulesFamilies.Srd521], [], 0.5 + (i % 5) / 10.0, ["Fixture uncertainty."]),
                UpdatedAt = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
            };
        });

    public static ContentReference Publish(TomeStackApp app, ContentRevision draft) => app.Publish(app.SaveDraft(draft)).Published;
}
