using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TomeStack.AppService.CharacterImport;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Character-sheet import S6 (<c>features/ddb-pdf-import.md</c> "Slices and acceptance"): the owner's own exported sheet,
/// imported through the real worker. The sheet is private and stays in the gitignored
/// <c>tests/RulesFixtures/local/ddb-import/</c>:
/// <list type="bullet">
/// <item><c>sheet.pdf</c>: the D&amp;D Beyond export.</item>
/// <item><c>expectations.json</c> (optional): see <see cref="Expectations"/>.</item>
/// </list>
/// The real test skips without the sheet. Its output and <c>report.md</c> (written to the same folder) carry counts, row
/// ids and codes only, never a name or value, because test output is a log. The synthetic test runs the same pipeline on
/// the committed fixture sheet, so the gate proves the checks themselves.
/// </summary>
public partial class DdbImportAcceptanceTests
{
    /// <summary>
    /// The owner's expectations: <c>{ "layout": "ddb-2014", "family": "srd-5.1", "classes": [{ "level": 5 }],
    /// "counts": { "matched": n, "notFound": n, "noPlace": n, "differences": n } }</c>. Every part is optional.
    /// </summary>
    public sealed record Expectations(string? Layout, string? Family, IReadOnlyList<ExpectedClass>? Classes, ExpectedCounts? Counts);

    public sealed record ExpectedClass(int Level);

    public sealed record ExpectedCounts(int? Matched, int? NotFound, int? NoPlace, int? Differences);

    internal static string LocalFolder
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
                directory = directory.Parent;
            return directory is null ? "" : Path.Combine(directory.FullName, "tests", "RulesFixtures", "local", "ddb-import");
        }
    }

    /// <summary>xUnit 2 has no runtime skip, so the skip is decided when the test is discovered.</summary>
    public sealed class LocalSheetFactAttribute : FactAttribute
    {
        public LocalSheetFactAttribute()
        {
            if (!File.Exists(Path.Combine(LocalFolder, "sheet.pdf")))
                Skip = "The owner's D&D Beyond sheet is not in tests/RulesFixtures/local/ddb-import/ (it is private and gitignored).";
        }
    }

    /// <summary>What one run found: the report (counts, row ids and codes) and the checks, asserted without names.</summary>
    internal sealed record Outcome(string Report, IReadOnlyList<string> Failures, IReadOnlyList<string> Labels);

    /// <summary>
    /// Reads the sheet through the real worker, previews it, answers every "Choose" row with its first candidate (to
    /// measure, not to decide), proposes an override for every differing number, and previews again.
    /// </summary>
    internal static Outcome Check(byte[] pdf, Expectations? expectations)
    {
        using var temp = new TempApp();
        var failures = new List<string>();
        var report = new StringBuilder("# D&D Beyond sheet import: acceptance run\n\nCounts, row ids and codes only.\n\n");
        DdbReadResult read;
        try
        {
            read = temp.App.ReadDdbSheetData("sheet.pdf", pdf);
        }
        catch (AppValidationException ex)
        {
            report.Append(CultureInfo.InvariantCulture, $"- read: refused, {ex.Code}\n");
            return new Outcome(report.ToString(), [$"The sheet was not read: {ex.Code}."], []);
        }
        var sheet = temp.App.DdbSessions.Peek(read.Token)!;
        var family = expectations?.Family ?? read.SuggestedFamily ?? RulesFamilies.Srd521;
        report.Append(CultureInfo.InvariantCulture, $"- layout: {read.Layout}\n- family: {family}\n");
        report.Append(CultureInfo.InvariantCulture, $"- name: {sheet.Name.Status}\n- classes: {sheet.Classes.Status}, {sheet.Classes.Value?.Count ?? 0} read, levels {string.Join(" / ", sheet.Classes.Value?.Select(c => c.Level) ?? [])}\n");
        if (sheet.Name.Status != ReadStatus.Ok || sheet.Classes.Status != ReadStatus.Ok)
            failures.Add($"A required field did not read: name {sheet.Name.Status}, classes {sheet.Classes.Status}.");

        var first = temp.App.PreviewDdbImport(new(read.Token, family, null, null, null, false));
        var resolutions = first.Matches.Where(m => m.Status == MatchStatus.Choose)
            .Select(m => new Resolution(m.RowId, m.Candidates[0].Reference, false, m.Candidates[0].Placement.Caster)).ToList();
        var resolved = temp.App.PreviewDdbImport(new(read.Token, family, null, resolutions, null, false));
        var differing = resolved.Comparison.Where(n => n.Differs).ToList();
        var kept = temp.App.PreviewDdbImport(new(read.Token, family, null, resolutions, [.. differing.Select(n => new NumberChoice(n.Field, NumberAction.KeepSheet))], false));
        var shown = temp.App.Preview(kept.Character).Sheet;
        var remaining = kept.Comparison.Count(n => n.Sheet is { } value && shown.Fields.FirstOrDefault(f => f.Field == n.Field)?.Value != value);

        int Count(MatchStatus status) => resolved.Matches.Count(m => m.Status == status);
        report.Append(CultureInfo.InvariantCulture, $"- rows: {resolved.Matches.Count}; matched {Count(MatchStatus.Matched)}, chosen to measure {resolutions.Count}, not found {Count(MatchStatus.NotFound)}, no place {Count(MatchStatus.NoPlace)}, unreadable {Count(MatchStatus.Unreadable)}\n");
        report.Append(CultureInfo.InvariantCulture, $"- numbers compared: {resolved.Comparison.Count}; differences {differing.Count}; overrides proposed {kept.Character.Overrides.Count}; differences after the overrides {remaining}\n");
        var noteCodes = string.Join(", ", resolved.AbilityPlan.Notes.Select(n => n.Code).Distinct());
        report.Append(CultureInfo.InvariantCulture, $"- open choices: {resolved.OpenChoices.Count}; ability notes: {(noteCodes.Length > 0 ? noteCodes : "none")}\n");
        report.Append(CultureInfo.InvariantCulture, $"- can apply: {kept.CanApply}\n\n## Rows not matched\n\n");
        foreach (var row in resolved.Matches.Where(m => m.Status != MatchStatus.Matched))
            report.Append(CultureInfo.InvariantCulture, $"- {row.RowId}: {row.Status}{(row.Note is null ? "" : $" ({row.Note})")}\n");
        report.Append("\n## Differing numbers\n\n");
        foreach (var row in differing)
            report.Append(CultureInfo.InvariantCulture, $"- {row.Field}\n");

        if (remaining != 0)
            failures.Add($"{remaining} number(s) still differ after the proposed overrides (see report.md).");
        if (!kept.CanApply)
            failures.Add("The proposal cannot be applied (see report.md).");
        if (expectations is not null)
        {
            if (expectations.Layout is { } layout && layout != read.Layout)
                failures.Add("The layout is not the expected one (see report.md).");
            if (expectations.Classes is { } classes && !classes.Select(c => c.Level).SequenceEqual(sheet.Classes.Value?.Select(c => c.Level) ?? []))
                failures.Add("The class levels are not the expected ones (see report.md).");
            if (expectations.Counts is { } counts)
            {
                void Expect(string what, int? expected, int actual)
                {
                    if (expected is { } e && e != actual)
                        failures.Add($"{what}: expected {e}, found {actual}.");
                }
                Expect("matched", counts.Matched, Count(MatchStatus.Matched));
                Expect("not found", counts.NotFound, Count(MatchStatus.NotFound));
                Expect("no place", counts.NoPlace, Count(MatchStatus.NoPlace));
                Expect("differences", counts.Differences, differing.Count);
            }
        }
        return new Outcome(report.ToString(), failures, [.. resolved.Matches.Select(m => m.Label).Where(l => l.Length > 0), sheet.Name.Value ?? ""]);
    }

    /// <summary>Every report line is a heading, a count, a row id, a field id or a code.</summary>
    [GeneratedRegex(@"^(#.*|Counts, row ids and codes only\.|- [a-z ]+: [\w.\-/ ,;():]*|- [a-zA-Z]+(:[a-zA-Z0-9]+)?(:subclass)?: \w+( \([a-z.\-]+\))?|- [a-zA-Z.]+\d*|)$")]
    private static partial Regex ReportLine();

    [LocalSheetFact]
    public void The_owners_sheet_is_recognised_and_parses_with_no_unreadable_required_field()
    {
        var outcome = Check(File.ReadAllBytes(Path.Combine(LocalFolder, "sheet.pdf")), null);
        var unread = outcome.Failures.Where(f => f.StartsWith("The sheet was not read", StringComparison.Ordinal) || f.StartsWith("A required field", StringComparison.Ordinal)).ToList();
        Assert.True(unread.Count == 0, string.Join(" ", unread));
    }

    [LocalSheetFact]
    public void The_owners_sheet_imports_with_zero_differences_after_the_proposed_overrides_and_the_counts_match_expectations()
    {
        var expectationsPath = Path.Combine(LocalFolder, "expectations.json");
        var expectations = File.Exists(expectationsPath) ? JsonSerializer.Deserialize<Expectations>(File.ReadAllText(expectationsPath), RulesJson.Options) : null;

        var outcome = Check(File.ReadAllBytes(Path.Combine(LocalFolder, "sheet.pdf")), expectations);

        File.WriteAllText(Path.Combine(LocalFolder, "report.md"), outcome.Report, Encoding.UTF8);
        Assert.True(outcome.Failures.Count == 0, string.Join(" ", outcome.Failures));
    }

    [LocalSheetFact]
    public void The_owners_report_names_no_value()
    {
        var outcome = Check(File.ReadAllBytes(Path.Combine(LocalFolder, "sheet.pdf")), null);
        var bad = outcome.Report.Split('\n').Select((line, i) => (line, i)).Where(x => !ReportLine().IsMatch(x.line)).Select(x => x.i + 1).ToList();
        Assert.True(bad.Count == 0, $"Report lines {string.Join(", ", bad)} are not counts, ids or codes.");
        Assert.True(outcome.Labels.All(l => !outcome.Report.Contains(l, StringComparison.Ordinal)), "The report quotes a sheet value.");
    }

    [Fact]
    public void The_pipeline_on_the_committed_fixture_sheet_passes_and_its_report_names_no_value()
    {
        var pdf = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-ddb-sheet.pdf"));

        // Three of the invented sheet's numbers differ from TomeStack's: Armor Class, hit points and the proficiency bonus
        // (the sheet says +2 for a level-5 character).
        var outcome = Check(pdf, new Expectations("ddb-2014", RulesFamilies.Srd51, [new(3), new(2)], new(null, null, null, 3)));

        Assert.True(outcome.Failures.Count == 0, string.Join(" ", outcome.Failures));
        var bad = outcome.Report.Split('\n').Where(line => !ReportLine().IsMatch(line)).ToList();
        // The fixture sheet is invented, so its lines may appear here; the owner's test reports line numbers only.
        Assert.True(bad.Count == 0, $"Report lines that are not counts, ids or codes: {string.Join(" | ", bad)}");
        Assert.NotEmpty(outcome.Labels);
        Assert.All(outcome.Labels, l => Assert.DoesNotContain(l, outcome.Report, StringComparison.Ordinal));
        Assert.Contains("- rows:", outcome.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pipeline_reports_a_refused_sheet_by_its_code_and_a_wrong_expectation_by_its_count()
    {
        var refused = Check("Fixture: not a PDF"u8.ToArray(), null);
        Assert.Contains(refused.Failures, f => f.Contains("pdf.not-a-pdf", StringComparison.Ordinal));

        var pdf = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-ddb-sheet.pdf"));
        var wrong = Check(pdf, new Expectations("ddb-2024", null, [new(20)], new(999, null, null, null)));
        Assert.Equal(3, wrong.Failures.Count);
        Assert.Contains(wrong.Failures, f => f.StartsWith("matched: expected 999", StringComparison.Ordinal));
    }
}
