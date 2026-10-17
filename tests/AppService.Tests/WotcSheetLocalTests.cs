using System.Globalization;
using TomeStack.AppService.CharacterImport;
using TomeStack.ImportWorker.Forms;
using TomeStack.RulesCore;
using Xunit.Abstractions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Item 14: the owner's own filled fillable-2014 sheet, read through the real worker. The sheet is private and stays in the
/// gitignored <c>tests/RulesFixtures/local/wotc-2014/</c>. The test skips without a PDF there. It asserts that the layout is
/// recognised and that every mapped field reads, and it reports counts and pass or fail only: its output is a log, so it
/// holds no name, no value and no length, and the failure messages are counts too.
/// </summary>
public class WotcSheetLocalTests(ITestOutputHelper output)
{
    private static string LocalFolder
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
                directory = directory.Parent;
            return directory is null ? "" : Path.Combine(directory.FullName, "tests", "RulesFixtures", "local", "wotc-2014");
        }
    }

    /// <summary>xUnit 2 has no runtime skip, so the skip is decided when the test is discovered.</summary>
    public sealed class LocalSheetFactAttribute : FactAttribute
    {
        public LocalSheetFactAttribute()
        {
            if (!Directory.Exists(LocalFolder) || Directory.GetFiles(LocalFolder, "*.pdf").Length == 0)
                Skip = "No fillable 2014 sheet in tests/RulesFixtures/local/wotc-2014/ (it is private and gitignored).";
        }
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    [LocalSheetFact]
    public async Task The_owners_fillable_sheets_are_recognised_and_every_mapped_field_reads()
    {
        var map = LayoutMaps.All.Single(m => m.Id == "wotc-2014");
        var failures = new List<string>();
        var sheets = 0;
        foreach (var path in Directory.GetFiles(LocalFolder, "*.pdf").Order(StringComparer.Ordinal))
        {
            sheets++;
            var label = $"sheet {N(sheets)}";
            var fields = await new WorkerFormReader(Path.Combine(AppContext.BaseDirectory, TomeStackApp.WorkerFileName), form: LayoutMaps.FormLimits)
                .ReadAsync(path, CancellationToken.None);
            var recognised = DdbParser.Recognise(fields);
            if (recognised?.Id != "wotc-2014")
            {
                failures.Add($"{label}: the layout was not recognised.");
                output.WriteLine($"{label}: {N(fields.Count)} fields, layout not recognised");
                continue;
            }

            // Names only: how many of the map's plain names the form has.
            var names = fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
            var plain = map.Fields.Keys.Where(k => !k.Contains("{n}", StringComparison.Ordinal)).ToList();
            var missingNames = plain.Count(k => !names.Contains(k));

            var sheet = DdbParser.Parse(map, fields);
            var extras = ImportExtras.Build(sheet, new DateOnly(2026, 10, 17), DateTimeOffset.UnixEpoch);
            var statuses = new List<ReadStatus>();
            statuses.Add(sheet.Name.Status);
            statuses.Add(sheet.Classes.Status);
            statuses.Add(sheet.Species.Status);
            statuses.Add(sheet.Background.Status);
            statuses.AddRange(sheet.Abilities.Values.Select(r => r.Status));
            statuses.AddRange(sheet.SaveProficient.Values.Select(r => r.Status));
            statuses.AddRange(sheet.SkillProficient.Values.Select(r => r.Status));
            statuses.AddRange(sheet.Numbers.Values.Select(r => r.Status));
            statuses.AddRange(sheet.Currency.Values.Select(r => r.Status));
            statuses.AddRange([sheet.Play.CurrentHitPoints.Status, sheet.Play.TemporaryHitPoints.Status, sheet.Play.DeathSuccesses.Status, sheet.Play.DeathFailures.Status]);
            statuses.AddRange(sheet.Texts!.Details.Values.Select(r => r.Status));
            statuses.AddRange(sheet.Texts.Prose.Values.Select(r => r.Status));
            var unreadable = statuses.Count(s => s == ReadStatus.Unreadable) + sheet.Play.UnreadableSpent;

            output.WriteLine(
                $"{label}: fields {N(fields.Count)}; mapped names {N(plain.Count)}, absent from the form {N(missingNames)}; values read {N(statuses.Count(s => s == ReadStatus.Ok))}, blank {N(statuses.Count(s => s == ReadStatus.Missing))}, unreadable {N(unreadable)}; "
                + $"clipped {N(sheet.Lines?.Count(l => l.Contains("was cut to", StringComparison.Ordinal)) ?? 0)}; report lines {N(extras.Lines.Count + (sheet.Lines?.Count ?? 0))}; "
                + $"attacks {N(extras.Attacks.Count)}, items {N(extras.Items.Count)}, notes {N(extras.Notes.Count)}, languages {N(extras.Details?.Languages?.Count ?? 0)}, tools {N(extras.Details?.ToolsAndOther?.Count ?? 0)}");

            // The report lines are labels and counts by construction (the tests pin it), so they are safe to show.
            foreach (var line in (sheet.Lines ?? []).Concat(extras.Lines))
                output.WriteLine($"{label}: report line: {line}");

            if (sheet.Name.Status != ReadStatus.Ok || sheet.Classes.Status != ReadStatus.Ok)
                failures.Add($"{label}: a required field did not read (name {sheet.Name.Status}, classes {sheet.Classes.Status}).");
            if (missingNames > 0)
                failures.Add($"{label}: {N(missingNames)} mapped name(s) are not on the form.");
            if (unreadable > 0)
                failures.Add($"{label}: {N(unreadable)} mapped field(s) did not read.");
        }
        output.WriteLine(failures.Count == 0 ? $"PASS: {N(sheets)} sheet(s)" : $"FAIL: {N(failures.Count)} problem(s) in {N(sheets)} sheet(s)");
        Assert.True(failures.Count == 0, string.Join(" ", failures));
    }
}
