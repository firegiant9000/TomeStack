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
public class DdbImportAcceptanceTests
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
        var levels = string.Join(" / ", sheet.Classes.Value?.Select(c => c.Level) ?? []);
        report.Append(CultureInfo.InvariantCulture, $"- name: {sheet.Name.Status}\n- classes: {sheet.Classes.Status}, {sheet.Classes.Value?.Count ?? 0} read, levels {(levels.Length > 0 ? levels : "none")}\n");
        if (sheet.Name.Status != ReadStatus.Ok || sheet.Classes.Status != ReadStatus.Ok)
            failures.Add($"A required field did not read: name {sheet.Name.Status}, classes {sheet.Classes.Status}.");

        // Answering one "Choose" row (a class) can make others (spells, a subclass) become "Choose", so iterate until no
        // new row needs an answer.
        var resolutions = new List<Resolution>();
        for (var pass = 0; pass < MaxChoosePasses; pass++)
        {
            var added = temp.App.PreviewDdbImport(new(read.Token, family, null, resolutions, null, false)).Matches
                .Where(m => m.Status == MatchStatus.Choose && resolutions.All(r => r.RowId != m.RowId))
                .Select(m => new Resolution(m.RowId, m.Candidates[0].Reference, false, m.Candidates[0].Placement.Caster)).ToList();
            if (added.Count == 0)
                break;
            resolutions.AddRange(added);
        }
        var resolved = temp.App.PreviewDdbImport(new(read.Token, family, null, resolutions, null, false));
        var differing = resolved.Comparison.Where(n => n.Differs).ToList();
        var kept = temp.App.PreviewDdbImport(new(read.Token, family, null, resolutions, [.. differing.Select(n => new NumberChoice(n.Field, NumberAction.KeepSheet))], false));
        int? remaining = null;
        try
        {
            var shown = temp.App.Preview(kept.Character).Sheet;
            remaining = kept.Comparison.Count(n => n.Sheet is { } value && shown.Fields.FirstOrDefault(f => f.Field == n.Field)?.Value != value);
        }
        catch (AppValidationException ex)
        {
            var codes = string.Join(", ", ex.Problems.Select(p => p.Code).Distinct());
            report.Append(CultureInfo.InvariantCulture, $"- validation: refused, {(codes.Length > 0 ? codes : ex.Code)}\n");
            failures.Add($"The proposal does not validate: {(codes.Length > 0 ? codes : ex.Code)}.");
        }

        int Count(MatchStatus status) => resolved.Matches.Count(m => m.Status == status);
        report.Append(CultureInfo.InvariantCulture, $"- rows: {resolved.Matches.Count}; matched {Count(MatchStatus.Matched)}, still to choose {Count(MatchStatus.Choose)}, chosen to measure {resolutions.Count}, not found {Count(MatchStatus.NotFound)}, no place {Count(MatchStatus.NoPlace)}, unreadable {Count(MatchStatus.Unreadable)}\n");
        report.Append(CultureInfo.InvariantCulture, $"- numbers compared: {resolved.Comparison.Count}; differences {differing.Count}; overrides proposed {kept.Character.Overrides.Count}; differences after the overrides {(remaining?.ToString(CultureInfo.InvariantCulture) ?? "n/a")}\n");
        var noteCodes = string.Join(", ", resolved.AbilityPlan.Notes.Select(n => n.Code).Distinct());
        report.Append(CultureInfo.InvariantCulture, $"- open choices: {resolved.OpenChoices.Count}; ability notes: {(noteCodes.Length > 0 ? noteCodes : "none")}\n");
        report.Append(CultureInfo.InvariantCulture, $"- can apply: {kept.CanApply}\n\n## Rows not matched\n\n");
        foreach (var row in resolved.Matches.Where(m => m.Status != MatchStatus.Matched))
            report.Append(CultureInfo.InvariantCulture, $"- {row.RowId}: {row.Status}{(row.Note is null ? "" : $" ({row.Note})")}\n");
        report.Append("\n## Differing numbers\n\n");
        foreach (var row in differing)
            report.Append(CultureInfo.InvariantCulture, $"- {row.Field}\n");

        if (remaining is > 0)
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
        return new Outcome(report.ToString(), failures, LabelsFor(sheet, resolved.Matches));
    }

    /// <summary>How many times a new "Choose" row is answered before the loop gives up (it normally settles in two).</summary>
    private const int MaxChoosePasses = 5;

    /// <summary>
    /// Every sheet value the report must not quote: the matched rows' labels, the name (when it was read) and each class
    /// name (which the report otherwise carries only as a level). Empty labels are dropped, because every string contains "".
    /// </summary>
    internal static IReadOnlyList<string> LabelsFor(DdbSheet sheet, IEnumerable<MatchRow> rows) =>
        [.. rows.Select(m => m.Label)
            .Append(sheet.Name.Value ?? "")
            .Concat(sheet.Classes.Value?.Select(c => c.Name) ?? [])
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.Ordinal)];

    // Exact per-line shapes: fixed keys, and values that are only digits, enum names, layout/family ids, row ids, field ids
    // and codes. A name or any free text matches none of them.
    private const string Code = @"[a-z]+(?:\.[a-z\-]+)+";
    private const string Enum = @"[A-Z][A-Za-z]*";
    private const string Id = @"[a-z0-9]+(?:[.\-][a-z0-9]+)*";
    private const string RowId = @"(?:species|background|class:\d+|class:\d+:subclass|feat:\d+|skill:[a-z\-]+|item:\d+|spell:\d+|feature:\d+)";
    private const string FieldId = @"[a-z][A-Za-z]*(?:\.[a-z0-9]+)?";

    /// <summary>The lines before the first section heading, by their fixed keys.</summary>
    private static readonly Regex[] HeaderLines =
    [
        Exact(@"# D&D Beyond sheet import: acceptance run"),
        Exact(@"Counts, row ids and codes only\."),
        Exact(@"- read: refused, " + Code),
        Exact($@"- layout: {Id}"),
        Exact($@"- family: {Id}"),
        Exact($@"- name: {Enum}"),
        Exact($@"- classes: {Enum}, \d+ read, levels (?:none|\d+(?: / \d+)*)"),
        Exact($@"- validation: refused, (?:{Code}|validation)(?:, (?:{Code}|validation))*"),
        Exact(@"- rows: \d+; matched \d+, still to choose \d+, chosen to measure \d+, not found \d+, no place \d+, unreadable \d+"),
        Exact(@"- numbers compared: \d+; differences \d+; overrides proposed \d+; differences after the overrides (?:\d+|n/a)"),
        Exact($@"- open choices: \d+; ability notes: (?:none|{Code}(?:, {Code})*)"),
        Exact($@"- can apply: (?:True|False)"),
    ];

    private static readonly Regex RowsNotMatchedLine = Exact($@"- {RowId}: {Enum}(?: \({Code}\))?");

    private static readonly Regex DifferingNumberLine = Exact($@"- {FieldId}");

    private static Regex Exact(string pattern) => new($"^{pattern}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Checks the exact report before it is written: every line has one of the fixed shapes above, and no label appears in
    /// it. The messages carry line numbers only, never a line or a label, because test output is a log.
    /// </summary>
    internal static IReadOnlyList<string> ReportProblems(string report, IReadOnlyList<string> labels)
    {
        var problems = new List<string>();
        var section = "";
        var lines = report.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            bool ok;
            if (line == "## Rows not matched" || line == "## Differing numbers")
            {
                section = line;
                ok = true;
            }
            else if (line.Length == 0)
                ok = true;
            else if (section == "## Rows not matched")
                ok = RowsNotMatchedLine.IsMatch(line);
            else if (section == "## Differing numbers")
                ok = DifferingNumberLine.IsMatch(line);
            else
                ok = HeaderLines.Any(r => r.IsMatch(line));
            if (!ok)
                problems.Add($"Report line {i + 1} is not a heading, count, id or code.");
        }
        if (labels.Any(l => l.Length > 0 && report.Contains(l, StringComparison.Ordinal)))
            problems.Add("The report quotes a sheet value.");
        return problems;
    }

    private const string FixtureReport = "# D&D Beyond sheet import: acceptance run\n\nCounts, row ids and codes only.\n\n"
        + "- layout: ddb-2014\n- family: srd-5.1\n- name: Ok\n- classes: Ok, 2 read, levels 3 / 2\n"
        + "- rows: 9; matched 6, still to choose 0, chosen to measure 1, not found 1, no place 1, unreadable 0\n"
        + "- numbers compared: 12; differences 3; overrides proposed 3; differences after the overrides 0\n"
        + "- open choices: 0; ability notes: ability.set-by-content, ability.cap-ambiguous\n"
        + "- can apply: True\n\n## Rows not matched\n\n- feat:3: NotFound\n- skill:arcana: NoPlace (skill.no-open-choice)\n\n## Differing numbers\n\n- armorClass\n- save.str\n";

    [Fact]
    public void A_well_formed_fixture_report_passes_the_report_checks()
    {
        Assert.Empty(ReportProblems(FixtureReport, ["Fixture Hero", "Fixture Fighter"]));
    }

    [Theory]
    [InlineData("- layout: Some Person Name")]
    [InlineData("# Testy Person")]
    [InlineData("- family: srd-5.1 Testy")]
    [InlineData("- feat:3: NotFound (Testy note)")]
    public void A_report_line_that_could_carry_free_text_is_rejected(string line)
    {
        var report = FixtureReport.Replace("- can apply: True", line, StringComparison.Ordinal);
        var problems = ReportProblems(report, ["Fixture Hero"]);
        Assert.NotEmpty(problems);
        Assert.DoesNotContain(problems, p => p.Contains("Testy", StringComparison.Ordinal));
    }

    [Fact]
    public void A_report_that_quotes_a_label_is_rejected_by_the_label_check()
    {
        // The line is shape-valid (an enum-like word), so only the label check can catch it.
        var report = FixtureReport.Replace("- name: Ok", "- name: Fixturehero", StringComparison.Ordinal);
        Assert.Empty(ReportProblems(report, ["Other"]));
        Assert.NotEmpty(ReportProblems(report, ["Fixturehero"]));
    }

    [Fact]
    public void Labels_drop_an_unread_name_and_include_every_class_name()
    {
        var pdf = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-ddb-sheet.pdf"));
        using var temp = new TempApp();
        var read = temp.App.ReadDdbSheetData("sheet.pdf", pdf);
        var sheet = temp.App.DdbSessions.Peek(read.Token)!;
        var unnamed = sheet with { Name = Read<string>.Missing };
        var preview = temp.App.PreviewDdbImport(new(read.Token, read.SuggestedFamily ?? RulesFamilies.Srd521, null, null, null, false));

        var labels = LabelsFor(unnamed, preview.Matches);

        Assert.DoesNotContain("", labels);
        Assert.All(sheet.Classes.Value!, c => Assert.Contains(c.Name, labels));
    }

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

        // The report is written only when this exact text passes the privacy checks.
        var problems = ReportProblems(outcome.Report, outcome.Labels);
        if (problems.Count == 0)
            File.WriteAllText(Path.Combine(LocalFolder, "report.md"), outcome.Report, Encoding.UTF8);
        Assert.True(problems.Count == 0, $"report.md was not written. {string.Join(" ", problems)}");
        Assert.True(outcome.Failures.Count == 0, string.Join(" ", outcome.Failures));
    }

    [LocalSheetFact]
    public void The_owners_report_names_no_value()
    {
        var outcome = Check(File.ReadAllBytes(Path.Combine(LocalFolder, "sheet.pdf")), null);
        var problems = ReportProblems(outcome.Report, outcome.Labels);
        Assert.True(problems.Count == 0, string.Join(" ", problems));
    }

    [Fact]
    public void The_pipeline_on_the_committed_fixture_sheet_passes_and_its_report_names_no_value()
    {
        var pdf = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-ddb-sheet.pdf"));

        // Three of the invented sheet's numbers differ from TomeStack's: Armor Class, hit points and the proficiency bonus
        // (the sheet says +2 for a level-5 character).
        var outcome = Check(pdf, new Expectations("ddb-2014", RulesFamilies.Srd51, [new(3), new(2)], new(null, null, null, 3)));

        Assert.True(outcome.Failures.Count == 0, string.Join(" ", outcome.Failures));
        Assert.Empty(ReportProblems(outcome.Report, outcome.Labels));
        Assert.NotEmpty(outcome.Labels);
        Assert.All(outcome.Labels, l => Assert.DoesNotContain(l, outcome.Report, StringComparison.Ordinal));
        Assert.Contains("- rows:", outcome.Report, StringComparison.Ordinal);
        Assert.Contains("still to choose 0", outcome.Report, StringComparison.Ordinal);
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
