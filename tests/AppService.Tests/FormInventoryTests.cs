using System.Text.Json;
using TomeStack.AppService.Diagnostics;
using TomeStack.ImportWorker;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Character-sheet import S0 (<c>features/ddb-pdf-import.md</c>): the field inventory the dev-only DevHost writes with
/// <c>--ddb-fields</c>. It reads through the real worker (ADR-009) and keeps names, types, pages, value lengths and states,
/// never a value, so an owner's real export can be inventoried without its contents leaving the local folder.
/// </summary>
public class FormInventoryTests
{
    private static string Worker => Path.Combine(AppContext.BaseDirectory, TomeStackApp.WorkerFileName);

    private static string FixtureSheet => Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-ddb-sheet.pdf");

    [Fact]
    public void The_inventory_of_the_fixture_sheet_lists_every_field_with_its_type_page_and_value_length_and_no_value()
    {
        var report = FormInventory.Read(Worker, FixtureSheet);

        Assert.Equal(34, report.FieldCount);
        Assert.Equal(report.FieldCount, report.Fields.Count);
        Assert.Equal(new FormInventory.Entry("CharacterName", "text", 1, "Testy McFixture".Length, null, null), report.Fields[0]);
        // The 2014 layout's prepared mark is a one-character text field (S0); inspiration stays a checkbox.
        Assert.Contains(new FormInventory.Entry("Prepared0", "text", 2, 1, null, null), report.Fields);
        Assert.Contains(new FormInventory.Entry("Inspiration", "checkbox", 1, 0, false, "Yes"), report.Fields);
        Assert.Contains(new FormInventory.Entry("Eq Name3", "text", 3, "Fixture Lantern".Length, null, null), report.Fields);

        var json = FormInventory.ToJson(report);
        using var document = JsonDocument.Parse(json);
        Assert.All(document.RootElement.GetProperty("fields").EnumerateArray(), f => Assert.False(f.TryGetProperty("value", out _)));
        foreach (var value in new[] { "Testy McFixture", "Fixture Arcanist 3", "Fixture Quickfoot", "Fixture Archivist", "Fixture Frost Ring", "Fixture Longblade", "Fixture Rope Coil" })
            Assert.DoesNotContain(value, json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_inventory_summary_gives_counts_by_type_and_no_name()
    {
        var summary = FormInventory.Format(FormInventory.Read(Worker, FixtureSheet));

        var lines = summary.Split(Environment.NewLine);
        Assert.Contains("34 fields", lines);
        Assert.Contains("  text 33", lines);
        Assert.Contains("  checkbox 1", lines);
        foreach (var name in new[] { "CharacterName", "CLASS  LEVEL", "Prepared0", "Eq Name0", "Inspiration" })
            Assert.DoesNotContain(name, summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Testy", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_inventory_refuses_a_file_that_is_not_a_pdf_with_a_code()
    {
        var folder = Directory.CreateTempSubdirectory("tomestack-inventory-");
        try
        {
            var file = Path.Combine(folder.FullName, "fixture.pdf");
            File.WriteAllText(file, "Fixture: not a PDF");
            Assert.Equal("pdf.not-a-pdf", Assert.Throws<ExtractionException>(() => FormInventory.Read(Worker, file)).Code);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
