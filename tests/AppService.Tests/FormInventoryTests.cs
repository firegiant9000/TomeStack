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

        Assert.Equal(33, report.FieldCount);
        Assert.Equal(report.FieldCount, report.Fields.Count);
        Assert.Equal(new FormInventory.Entry("fixture.2014.name", "text", 1, "Testy McFixture".Length, null, null), report.Fields[0]);
        Assert.Contains(new FormInventory.Entry("fixture.2014.spells.0.prepared", "checkbox", 2, 0, true, "Yes"), report.Fields);
        Assert.Contains(new FormInventory.Entry("fixture.2014.equipment.3.name", "text", 3, "Fixture Lantern".Length, null, null), report.Fields);

        var json = FormInventory.ToJson(report);
        using var document = JsonDocument.Parse(json);
        Assert.All(document.RootElement.GetProperty("fields").EnumerateArray(), f => Assert.False(f.TryGetProperty("value", out _)));
        foreach (var value in new[] { "Testy McFixture", "Fixture Fighter 3", "Fixture Glimmerkin", "Fixture Archivist", "Fixture Ember Lance", "Fixture Hookblade", "Fixture Rope Coil" })
            Assert.DoesNotContain(value, json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_inventory_summary_gives_counts_by_type_and_no_name()
    {
        var summary = FormInventory.Format(FormInventory.Read(Worker, FixtureSheet));

        Assert.Contains("33 fields", summary, StringComparison.Ordinal);
        Assert.Contains("text 22", summary, StringComparison.Ordinal);
        Assert.Contains("checkbox 11", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture.", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Testy", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_radio_group_keeps_each_widgets_state_but_not_its_on_state_name()
    {
        // A radio widget's on-state name is the group's value once you know which one is on, so it is dropped.
        var folder = Directory.CreateTempSubdirectory("tomestack-inventory-");
        try
        {
            var file = Path.Combine(folder.FullName, "fixture.pdf");
            File.WriteAllBytes(file, TomeStack.ImportWorker.Tests.FormPdfWriter.Write([new("fixture.name", "Testy McFixture")],
                groups: [new("fixture.pick", ["FixtureX", "FixtureY"], On: 1, Radio: true)]));

            var report = FormInventory.Read(Worker, file);
            var radios = report.Fields.Where(f => f.Type == "radio").ToList();

            Assert.Equal([false, true], radios.Select(r => r.Checked));
            Assert.All(radios, r => Assert.Null(r.OnState));
            Assert.DoesNotContain("FixtureY", FormInventory.ToJson(report), StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_output_path_is_refused_when_it_is_the_pdf_and_its_folder_is_created_before_the_read()
    {
        var folder = Directory.CreateTempSubdirectory("tomestack-inventory-");
        try
        {
            var pdf = Path.Combine(folder.FullName, "fixture.pdf");
            File.WriteAllText(pdf, "Fixture");

            var same = Assert.Throws<ArgumentException>(() => FormInventory.PrepareOutput(pdf, Path.Combine(folder.FullName, ".", "FIXTURE.pdf"), out _));
            Assert.DoesNotContain(folder.FullName, same.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Fixture", File.ReadAllText(pdf));

            var output = FormInventory.PrepareOutput(pdf, Path.Combine(folder.FullName, "new", "fields.json"), out var outsideLocal);
            Assert.True(Directory.Exists(Path.GetDirectoryName(output)));
            Assert.True(outsideLocal);

            FormInventory.PrepareOutput(pdf, Path.Combine(folder.FullName, "tests", "RulesFixtures", "local", "ddb-import", "fields.json"), out var outside);
            Assert.False(outside);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
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
