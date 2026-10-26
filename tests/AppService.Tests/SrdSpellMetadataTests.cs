using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Detection;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;
using Xunit.Abstractions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// F3 (10x addendum): the bundled spells' game data read back from both SRD PDFs and compared field by field: level, school,
/// casting time, the ritual and concentration flags, and the component letters. The spell headers are parsed by the import
/// detector from the hashed PDF, and each is matched to the pack spell of the same name on the same page (one either side).
/// The PDFs are not in the repository: the test runs only when <c>TOMESTACK_SRD_PDF_DIR</c> names a folder holding them with
/// the SHA-256 hashes of <c>docs/licensing/srd-pack-review.md</c>, and skips otherwise. The output is counts only;
/// <c>TOMESTACK_SPELL_METADATA_OUT</c> (outside the repository) receives the names of the spells that differ. A difference is
/// not fixed here: a correction is a new revision derived from the hashed page, reviewed under SPEC Q-03.
/// </summary>
public partial class SrdSpellMetadataTests(ITestOutputHelper output)
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

    private static readonly string[] FieldNames = ["level", "school", "casting time", "ritual", "concentration", "components"];

    /// <summary>
    /// A field the detector did not read is counted, not compared, against a ceiling per field (0 when not listed).
    /// 2026-10-26: the Components line of 1 spell on 5.1 and 12 on 5.2.1 is not found by the detector (a detection gap; the
    /// packs hold the components). Lower these as detection improves.
    /// </summary>
    private static readonly Dictionary<(string Version, string Field), int> NotReadCeiling = new()
    {
        [("5.1", "components")] = 1,
        [("5.2.1", "components")] = 12,
    };

    [SrdPdfFact]
    public async Task The_bundled_spell_metadata_matches_the_SRD_spell_headers()
    {
        var report = new StringBuilder();
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
            var read = CandidateDetector.Detect(pages, new(Guid.NewGuid(), [family], _ => false))
                .Where(c => c.ProposedKind == ContentKind.Spell)
                .Select(c => (c.ProposedName, c.Page.Start, Spell: c.ProposedEffects.OfType<SpellEffect>().First())).ToList();

            var bundled = TomeStackApp.LoadBundledPack($"TomeStack.Content.srd-{version}-spells.json").Revisions
                .GroupBy(r => r.ContentId).Select(g => g.Last())
                .Where(r => r.Kind == ContentKind.Spell && r.Provenance.Page is not null)
                .Select(r => (r.Name, r.Provenance.Page!, Spell: r.Effects.OfType<SpellEffect>().Single())).ToList();

            var wrong = FieldNames.ToDictionary(f => f, _ => 0);
            var notRead = FieldNames.ToDictionary(f => f, _ => 0);
            int matched = 0, allRight = 0, unread = 0;
            foreach (var (name, page, truth) in bundled)
            {
                var hit = read.FindIndex(r => CandidateDetector.Key(r.ProposedName) == CandidateDetector.Key(name)
                    && r.Start >= page.Start - 1 && r.Start <= (page.End ?? page.Start) + 1);
                if (hit < 0)
                {
                    unread++;
                    report.AppendLine($"{version} unread: {name} p{page.Start}");
                    continue;
                }
                var pdf = read[hit].Spell;
                read.RemoveAt(hit);
                matched++;
                var differs = new List<string>();
                void Compare(string field, string? fromPdf, string? fromPack)
                {
                    if (fromPdf == fromPack)
                        return;
                    if (fromPdf is null)
                        notRead[field]++;
                    else
                        wrong[field]++;
                    differs.Add($"{field} pdf {Quote(fromPdf)} pack {Quote(fromPack)}");
                }
                Compare("level", pdf.Level.ToString(System.Globalization.CultureInfo.InvariantCulture), truth.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Compare("school", pdf.School?.ToLowerInvariant(), truth.School?.ToLowerInvariant());
                Compare("casting time", Normalise(pdf.CastingTime), Normalise(truth.CastingTime));
                // The detector reads concentration from the Duration line and, on 5.2.1, the ritual tag from the Casting Time line
                // (5.1 has it in the header): with that line not read, the flag is not read either, rather than a silent "no".
                var ritualRead = family == RulesFamilies.Srd51 || pdf.CastingTime is not null;
                Compare("ritual", ritualRead ? YesNo(pdf.Ritual) : null, YesNo(truth.Ritual));
                Compare("concentration", pdf.Duration is null ? null : YesNo(pdf.Concentration), YesNo(truth.Concentration));
                Compare("components", Letters(pdf.Components), Letters(truth.Components));
                if (differs.Count == 0)
                    allRight++;
                else
                    report.AppendLine($"{version} {name} p{page.Start}: {string.Join("; ", differs)}");
            }

            var line = $"{version}: bundled spells {bundled.Count}, read from the PDF and matched {matched}, unread {unread}, every field read and right {allRight}/{matched}; wrong: "
                + string.Join(", ", FieldNames.Select(f => $"{f} {wrong[f]}")) + "; not read from the PDF: "
                + string.Join(", ", FieldNames.Select(f => $"{f} {notRead[f]}"));
            output.WriteLine(line);
            Assert.NotEmpty(bundled);
            if (unread != 0)
                failures.Add($"{version} unread {unread}/{bundled.Count}");
            foreach (var field in FieldNames.Where(f => wrong[f] != 0))
                failures.Add($"{version} {field} {wrong[field]}/{matched}");
            foreach (var field in FieldNames)
            {
                var ceiling = NotReadCeiling.GetValueOrDefault((version, field));
                if (notRead[field] > ceiling)
                    failures.Add($"{version} {field} not read {notRead[field]} > {ceiling}");
                else if (notRead[field] < ceiling)
                    output.WriteLine($"{version}: {field} not read {notRead[field]} < ceiling {ceiling}; lower the ceiling.");
            }
        }
        if (Environment.GetEnvironmentVariable("TOMESTACK_SPELL_METADATA_OUT") is { Length: > 0 } outFile)
            await File.WriteAllTextAsync(outFile, report.ToString());
        Assert.True(failures.Count == 0, string.Join("; ", failures)); // counts only, never names
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Quote(string? value) => value is null ? "(not read)" : $"'{value}'";

    /// <summary>The PDFs break lines and hyphens differently from the packs' repaired text: compare on collapsed whitespace and plain hyphens.</summary>
    private static string? Normalise(string? text) =>
        text is null ? null : Whitespace().Replace(Hyphens().Replace(text, "-").Replace('’', '\''), " ").Trim();

    /// <summary>The component letters (V, S, M) in order, without the material's description.</summary>
    private static string? Letters(string? components) =>
        components is null ? null : string.Join(", ", ComponentLetter().Matches(Parenthetical().Replace(components, string.Empty)).Select(m => m.Value));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[­‐‑‒–-]+")]
    private static partial Regex Hyphens();

    [GeneratedRegex(@"\([^)]*\)?")]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"\b[VSM]\b")]
    private static partial Regex ComponentLetter();
}
