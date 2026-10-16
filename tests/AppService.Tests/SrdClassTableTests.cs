using System.Security.Cryptography;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Detection;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;
using Xunit.Abstractions;
using TextLine = TomeStack.ImportWorker.TextLine;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Item 11, D34 (ADR-016): the class-table reconstruction read back from both SRD PDFs and compared cell by cell with the
/// bundled packs (the Spellcasting and Pact Magic effects, the proficiency bonus and the features granted at each level).
/// The PDFs are not in the repository: the test runs only when <c>TOMESTACK_SRD_PDF_DIR</c> names a folder holding them
/// with the SHA-256 hashes of <c>docs/licensing/srd-pack-review.md</c>, and skips otherwise. The output is counts only.
/// </summary>
public class SrdClassTableTests(ITestOutputHelper output)
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

    private static List<TextLine> LinesOf(DetectionPage page) =>
        [.. page.Blocks.SelectMany(b => b.Lines is { Count: > 0 } lines ? lines : [new TextLine(b.Text, b.X, b.Y, b.Width, b.Height)])];

    [SrdPdfFact]
    public async Task The_reconstructed_class_tables_match_the_bundled_SRD_packs_cell_for_cell()
    {
        var failures = new List<string>();
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
            var classPages = CandidateDetector.Detect(pages, new(Guid.NewGuid(), [family], _ => false))
                .Where(c => c.ProposedKind == ContentKind.Class).Select(c => (c.ProposedName, c.Page.Start)).ToList();

            var packs = new[] { $"srd-{version}-classes.json", $"srd-{version}-fighter.json" }.Select(f => TomeStackApp.LoadBundledPack($"TomeStack.Content.{f}")).ToList();
            var newest = packs.SelectMany(p => p.Revisions).GroupBy(r => r.ContentId).Select(g => g.Last()).ToList();
            var byRevision = packs.SelectMany(p => p.Revisions).GroupBy(r => r.RevisionId).ToDictionary(g => g.Key, g => g.First());

            var unreadable = new List<string>();
            int tables = 0, complete = 0, casters = 0, cellsCompared = 0, cellsRight = 0, bonusRight = 0, featureLevels = 0, featureLevelsRight = 0;
            foreach (var (name, pageNumber) in classPages)
            {
                var lines = LinesOf(pages.First(p => p.Page == pageNumber));
                var title = lines.Select(l => l.Text.Trim()).FirstOrDefault(t => t == $"{name} Features" || t == $"The {name}");
                var table = ClassTableReconstruction.Reconstruct(lines, title);
                if (table is null)
                {
                    unreadable.Add(name);
                    continue;
                }
                tables++;
                if (!table.Complete)
                {
                    failures.Add($"{version} table {tables} incomplete ({table.Rows.Count} rows)");
                    continue;
                }
                complete++;
                var bonus = table.Numbers(ClassColumnRole.ProficiencyBonus);
                if (bonus is not null && Enumerable.Range(1, 20).All(l => bonus[l - 1] == 2 + (l - 1) / 4))
                    bonusRight++;
                else
                    failures.Add($"{version} proficiency bonus off in table {tables}");

                var klass = newest.FirstOrDefault(r => r.Kind == ContentKind.Class && r.Name == name);
                if (klass is not null)
                {
                    foreach (var grant in klass.Effects.OfType<GrantEffect>().Where(g => g is { Grant: GrantKind.Content, Level: > 0 }))
                    {
                        if (grant.Content is not { } target || !byRevision.TryGetValue(target.RevisionId, out var granted) || granted.Name.Contains(':', StringComparison.Ordinal))
                            continue;
                        featureLevels++;
                        if (table.FeaturesByLevel.TryGetValue(grant.Level!.Value, out var names) && names.Any(n => SameFeature(n, granted.Name)))
                            featureLevelsRight++;
                    }
                }

                var caster = newest.FirstOrDefault(r => r.Effects.OfType<SpellcastingEffect>().Any() && r.Effects.OfType<SpellcastingEffect>().First().SpellList == name.ToLowerInvariant());
                if (caster is null)
                {
                    if (table.HasSlotColumns)
                        failures.Add($"{version} slot columns on a class the pack does not cast with (table {tables})");
                    continue;
                }
                casters++;
                var effect = caster.Effects.OfType<SpellcastingEffect>().First();
                var slots = table.Slots();
                for (var level = 0; level < 20; level++)
                {
                    cellsCompared++;
                    if (slots is not null && slots[level].SequenceEqual(Trim(effect.Slots[level])))
                        cellsRight++;
                }
                foreach (var (read, truth) in new[] { (table.Numbers(ClassColumnRole.Cantrips), effect.Cantrips), (table.Numbers(ClassColumnRole.SpellsTable), effect.SpellsTable) })
                {
                    if (truth is null)
                    {
                        if (read is not null)
                            failures.Add($"{version} a column the pack lacks was read in table {tables}");
                        continue;
                    }
                    for (var level = 0; level < 20; level++)
                    {
                        cellsCompared++;
                        if (read is not null && read[level] == truth[level])
                            cellsRight++;
                    }
                }
                if (table.IsPact != (effect.SlotKind == SpellSlotKind.PactMagic))
                    failures.Add($"{version} pact kind wrong in table {tables}");
            }
            output.WriteLine($"{version}: class pages {classPages.Count}, tables read {tables}, complete {complete}, unreadable {unreadable.Count}, proficiency bonus right {bonusRight}/{complete}, "
                + $"casters {casters}, spellcasting cells right {cellsRight}/{cellsCompared}, granted features found at their level {featureLevelsRight}/{featureLevels}");
            // The one table that is not read: the SRD 5.1 Sorcerer's extraction interleaves its columns ("Cantrips Spells",
            // "Known Known 1st 2nd ..." are single lines), so it stays a features-only candidate rather than a wrong table.
            if (!unreadable.SequenceEqual(version == "5.1" ? new[] { "Sorcerer" } : Array.Empty<string>()))
                failures.Add($"{version} unreadable tables {unreadable.Count}");
            if (cellsRight != cellsCompared)
                failures.Add($"{version} spellcasting cells {cellsRight}/{cellsCompared}");
            if (bonusRight != complete || complete != tables)
                failures.Add($"{version} tables {tables}, complete {complete}, bonus {bonusRight}");
            if (featureLevelsRight != featureLevels)
                failures.Add($"{version} features {featureLevelsRight}/{featureLevels}");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures)); // counts only, never names
    }

    /// <summary>The table names a feature with a qualifier ("Rage (2 dice)") or in the plural where the pack does not.</summary>
    private static bool SameFeature(string tableName, string packName)
    {
        static string Stem(string name) => CandidateDetector.Key(System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^)]*\)", string.Empty));
        var (a, b) = (Stem(tableName), Stem(packName));
        return a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal);
    }

    private static IReadOnlyList<int> Trim(IReadOnlyList<int> row)
    {
        var length = row.Count;
        while (length > 0 && row[length - 1] == 0)
            length--;
        return [.. row.Take(length)];
    }
}
