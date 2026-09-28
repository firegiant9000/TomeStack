using System.Security.Cryptography;
using System.Text;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Detection;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;
using Xunit.Abstractions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M4 D3: precision and recall of candidate detection, with the bundled SRD packs as ground truth and the two SRD PDFs
/// as input. The PDFs are not in the repository: the test runs only when <c>TOMESTACK_SRD_PDF_DIR</c> names a folder
/// holding them with the SHA-256 hashes recorded in <c>docs/licensing/srd-pack-review.md</c>, and skips otherwise. The
/// output is counts only; <c>TOMESTACK_MEASURE_OUT</c> (outside the repository) receives the unmatched names.
/// Match rule: the same kind, the same name (case, spacing and apostrophes aside), and a page within one of the pack's.
/// </summary>
public class SrdDetectionMeasurementTests(ITestOutputHelper output)
{
    private static readonly (string Family, string File, string Sha256, string Version)[] Pdfs =
    [
        (RulesFamilies.Srd51, "SRD_CC_v5.1.pdf", "2504d2a0abb0a4d491a939be4f17910a2dde0312570ab8d208080225ccf0a1f0", "5.1"),
        (RulesFamilies.Srd521, "SRD_CC_v5.2.1.pdf", "8974902d109d6e63672d7c490bde9ccf052410503d9cfa768237154fbc5e3d87", "5.2.1"),
    ];

    private static string? Folder => Environment.GetEnvironmentVariable("TOMESTACK_SRD_PDF_DIR");

    public sealed class SrdPdfFactAttribute : FactAttribute
    {
        public SrdPdfFactAttribute()
        {
            if (Folder is not { Length: > 0 } folder || !Pdfs.All(p => File.Exists(Path.Combine(folder, p.File))))
                Skip = "Set TOMESTACK_SRD_PDF_DIR to a folder with SRD_CC_v5.1.pdf and SRD_CC_v5.2.1.pdf (not in the repository).";
        }
    }

    /// <summary>
    /// Floors a little under the 2026-09-28 measurement (<c>m4-acceptance.md</c>), so a detection regression fails the run
    /// instead of only changing its output (review 2026-09-28). Precision is null where the packs are partial truth.
    /// </summary>
    private static readonly Dictionary<(string Version, ContentKind Kind), (double? Precision, double Recall)> Floors = new()
    {
        [("5.1", ContentKind.Spell)] = (0.99, 0.99),
        [("5.2.1", ContentKind.Spell)] = (0.99, 0.99),
        [("5.1", ContentKind.Item)] = (0.95, 0.99),
        [("5.2.1", ContentKind.Item)] = (0.95, 0.99),
        [("5.1", ContentKind.Class)] = (0.70, 0.99),
        [("5.2.1", ContentKind.Class)] = (0.70, 0.99),
        [("5.1", ContentKind.Feature)] = (null, 0.85),
        [("5.2.1", ContentKind.Feature)] = (null, 0.99),
    };

    private sealed record Truth(ContentKind Kind, string Name, int Start, int End, SpellEffect? Spell, WeaponEffect? Weapon);

    private static IEnumerable<Truth> GroundTruth(string version)
    {
        var packs = new[] { $"srd-{version}.json", $"srd-{version}-classes.json", $"srd-{version}-spells.json", $"srd-{version}-equipment.json" }
            .Select(f => TomeStackApp.LoadBundledPack($"TomeStack.Content.{f}"));
        // The newest revision of each content: later revisions (M2, M3 C3) repeat the same entries.
        var newest = packs.SelectMany(p => p.Revisions).GroupBy(r => r.ContentId).Select(g => g.Last());
        foreach (var revision in newest)
        {
            if (revision.Provenance.Page is not { } page)
                continue;
            var weapon = revision.Effects.OfType<WeaponEffect>().FirstOrDefault();
            if (revision.Kind == ContentKind.Item && weapon is null)
                continue; // only the weapon table is bundled
            if (revision.Name.Contains(':', StringComparison.Ordinal))
                continue; // option entries such as "Barbarian Skill: Athletics" are not headings in the book
            yield return new(revision.Kind, revision.Name, page.Start, page.End ?? page.Start, revision.Effects.OfType<SpellEffect>().FirstOrDefault(), weapon);
        }
    }

    [SrdPdfFact]
    public async Task Detection_is_measured_against_the_bundled_SRD_packs()
    {
        var report = new StringBuilder();
        var belowFloor = new List<string>();
        foreach (var (family, file, sha, version) in Pdfs)
        {
            var path = Path.Combine(Folder!, file);
            await using (var stream = File.OpenRead(path))
                Assert.Equal(sha, Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)));

            var pages = new List<DetectionPage>();
            await foreach (var item in new PdfPigExtractor().ExtractAsync(path, PageScope.WholeDocument, CancellationToken.None))
            {
                if (item is ExtractedPage page)
                    pages.Add(new(page.PageNumber, page.Text, page.Blocks ?? [], page.FromOcr));
            }
            var candidates = CandidateDetector.Detect(pages, new(Guid.NewGuid(), [family], _ => false));
            var truth = GroundTruth(version).ToList();

            foreach (var kind in new[] { ContentKind.Spell, ContentKind.Item, ContentKind.Feature, ContentKind.Feat, ContentKind.Class })
            {
                var expected = truth.Where(t => t.Kind == kind).ToList();
                // Items are measured as weapons: the weapon table is the only item table the packs bundle.
                var proposed = candidates.Where(c => c.ProposedKind == kind && (kind != ContentKind.Item || c.ProposedEffects.OfType<WeaponEffect>().Any())).ToList();
                var unmatched = new List<Truth>(expected);
                var matched = new List<(DraftCandidate Candidate, Truth Truth)>();
                var extra = new List<DraftCandidate>();
                foreach (var candidate in proposed)
                {
                    var hit = unmatched.FirstOrDefault(t => CandidateDetector.Key(t.Name) == CandidateDetector.Key(candidate.ProposedName)
                        && candidate.Page.Start >= t.Start - 1 && candidate.Page.Start <= t.End + 1);
                    if (hit is null)
                        extra.Add(candidate);
                    else
                    {
                        unmatched.Remove(hit);
                        matched.Add((candidate, hit));
                    }
                }
                // Armor is not bundled and the packs hold only some feats, so their "precision" counts entries the packs lack.
                var complete = kind is ContentKind.Spell or ContentKind.Item or ContentKind.Class;
                var precision = proposed.Count == 0 ? 0 : (double)matched.Count / proposed.Count;
                var recall = expected.Count == 0 ? 0 : (double)matched.Count / expected.Count;
                var line = $"{version} {kind}: truth {expected.Count}, candidates {proposed.Count}, matched {matched.Count}, precision {(complete ? $"{precision:P1}" : "n/a (partial truth)")}, recall {recall:P1}";
                var levelsRight = matched.Count(m => m.Candidate.ProposedEffects.OfType<SpellEffect>().FirstOrDefault()?.Level == m.Truth.Spell?.Level);
                var damageRight = matched.Count(m => m.Candidate.ProposedEffects.OfType<WeaponEffect>().FirstOrDefault()?.Damage == m.Truth.Weapon?.Damage);
                if (kind == ContentKind.Spell && matched.Count > 0)
                    line += $", level correct {levelsRight:D}/{matched.Count}";
                if (kind == ContentKind.Item && matched.Count > 0)
                    line += $", damage correct {damageRight:D}/{matched.Count}";
                if (Floors.TryGetValue((version, kind), out var floor))
                {
                    if (floor.Precision is { } p && precision < p)
                        belowFloor.Add($"{version} {kind} precision {precision:P1} < {p:P0}");
                    if (recall < floor.Recall)
                        belowFloor.Add($"{version} {kind} recall {recall:P1} < {floor.Recall:P0}");
                    if (kind == ContentKind.Spell && levelsRight < matched.Count)
                        belowFloor.Add($"{version} spell levels {levelsRight}/{matched.Count}");
                    if (kind == ContentKind.Item && damageRight < matched.Count)
                        belowFloor.Add($"{version} weapon damage {damageRight}/{matched.Count}");
                }
                output.WriteLine(line);
                report.AppendLine(line);
                report.AppendLine($"  missed: {string.Join("; ", unmatched.Select(t => $"{t.Name} p{t.Start}"))}");
                report.AppendLine($"  extra: {string.Join("; ", extra.Select(c => $"{c.ProposedName} p{c.Page.Start}"))}");
            }
            var armor = candidates.Count(c => c.ProposedKind == ContentKind.Item && c.ProposedEffects.OfType<ArmorEffect>().Any());
            output.WriteLine($"{version}: {pages.Count} pages, {candidates.Count} candidates ({armor} armor rows; armor is not bundled, so it is not measured)");
            Assert.NotEmpty(candidates);
        }
        if (Environment.GetEnvironmentVariable("TOMESTACK_MEASURE_OUT") is { Length: > 0 } outFile)
            await File.WriteAllTextAsync(outFile, report.ToString());
        Assert.True(belowFloor.Count == 0, string.Join("; ", belowFloor)); // counts only, never names
    }
}
