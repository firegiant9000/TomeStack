using System.Runtime.CompilerServices;
using System.Text.Json;
using TomeStack.AppService.CharacterImport;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Forms;
using TomeStack.ImportWorker.Tests;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Item 19b (D37, ADR-013): a character PDF with no form fields is read as page text and parsed as the text importer parses
/// pasted notes. Every sheet below is invented ("Testy", "Fixture"); no message, code or ToString carries any of the text.
/// </summary>
public class TextPdfImportTests
{
    private const string Marker = "Testy Secret Marker";

    /// <summary>A form reader for a PDF that has no form: the app then reads its page text.</summary>
    private static FakeFormReader NoForm() => new(() => throw new ExtractionException("ddb.no-form-fields", "Fixture message."));

    /// <summary>An extractor that reports <paramref name="pageCount"/> pages, each with the given text in turn, and records the scope it was asked for.</summary>
    private sealed class PagesExtractor(int pageCount, params string[] texts) : IDocumentExtractor
    {
        public List<PageScope> Scopes { get; } = [];

        public string[]? Warnings { get; init; }

        public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Scopes.Add(scope);
            await Task.Yield();
            yield return new DocumentOpened(pageCount);
            var (first, last) = scope.Resolve(pageCount);
            for (var number = first; number <= last; number++)
                yield return new ExtractedPage(number, texts[(number - 1) % texts.Length], false, Warnings: Warnings);
        }
    }

    private sealed class FailingExtractor(string code) : IDocumentExtractor
    {
        public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new DocumentOpened(1);
            throw new ExtractionException(code, "Fixture message.");
        }
    }

    private static string SheetText => string.Join("\n", FixturePdfs.TextSheetPages[0]);

    private static AppValidationException Refused(Func<object> read) => Assert.Throws<AppValidationException>(read);

    [Fact]
    public void A_pdf_without_form_fields_is_read_as_text_and_previews_the_parsed_character()
    {
        using var temp = new TempApp(formReader: NoForm());

        var read = temp.App.ReadDdbSheet(FixturePdfs.TextSheetPath);

        Assert.Equal(LayoutMaps.TextId, read.Layout);
        Assert.Equal("text from the PDF", read.LayoutName);
        Assert.Equal("Testy Printout", read.Summary.Name);
        Assert.Equal("Fixture Arcanist 3", read.Summary.ClassText);
        Assert.Equal(1, read.Summary.Spells);
        Assert.Equal(2, read.Summary.Features);
        Assert.Equal(0, read.Summary.NotRead);
        var preview = temp.App.PreviewDdbImport(new DdbPreviewRequest(read.Token, RulesFamilies.Srd51, null, null, null, false));
        Assert.Equal("Testy Printout", preview.Character.Name);
        Assert.Equal(3, preview.Character.Classes.Single().Level);
        Assert.Equal("text from the PDF", preview.Report.LayoutName);
        Assert.Empty(preview.Report.Lines ?? []);
    }

    [Fact]
    public void The_apps_own_wiring_reads_the_text_pdf_through_the_worker_process_under_the_sheet_limits()
    {
        // No fakes: the form reader and the text extractor are the worker child, as the shipped app runs them.
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"), "data");
        using var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: [], devFixtures: true);

        var read = app.ReadDdbSheet(FixturePdfs.TextSheetPath);

        Assert.Equal(LayoutMaps.TextId, read.Layout);
        Assert.Equal("Testy Printout", read.Summary.Name);
        Assert.Equal(0, read.Summary.NotRead);
    }

    [Fact]
    public void The_pdf_read_through_ddb_readData_falls_back_the_same_way_and_deletes_its_copy()
    {
        using var temp = new TempApp(formReader: NoForm());

        var read = temp.App.ReadDdbSheetData("sheet.pdf", FixturePdfs.TextSheet());

        Assert.Equal(LayoutMaps.TextId, read.Layout);
        Assert.Equal("Testy Printout", read.Summary.Name);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(temp.Directory, "tmp")));
    }

    [Fact]
    public void A_pdf_with_a_form_never_asks_for_its_text()
    {
        var extractor = new PagesExtractor(1, SheetText);
        using var temp = new TempApp(extractor: extractor, formReader: new FakeFormReader(DdbParserTests.Fields(FixtureSheets.Fields2014)));

        var read = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf");

        Assert.Equal("ddb-2014", read.Layout);
        Assert.Empty(extractor.Scopes);
    }

    [Theory]
    [InlineData("pdf.encrypted")]
    [InlineData("pdf.too-large")]
    [InlineData("ddb.too-many-fields")]
    public void Only_a_missing_form_falls_back_to_text(string code)
    {
        var extractor = new PagesExtractor(1, SheetText);
        using var temp = new TempApp(extractor: extractor, formReader: new FakeFormReader(() => throw new ExtractionException(code, "Fixture message.")));

        var refused = Refused(() => temp.App.ReadDdbSheet("C:/fixture/sheet.pdf"));

        Assert.Equal(code, refused.Code);
        Assert.Empty(extractor.Scopes);
    }

    [Fact]
    public void At_most_twenty_pages_are_read_and_the_rest_are_counted_in_the_report()
    {
        var extractor = new PagesExtractor(25, SheetText);
        using var temp = new TempApp(extractor: extractor, formReader: NoForm());

        var read = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf");

        Assert.Equal(new PageScope(1, 20), Assert.Single(extractor.Scopes));
        var preview = temp.App.PreviewDdbImport(new DdbPreviewRequest(read.Token, RulesFamilies.Srd51, null, null, null, false));
        Assert.Contains(preview.Report.Lines!, l => l.StartsWith("5 pages", StringComparison.Ordinal) && l.Contains("not read", StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Report.Lines!, l => l.Contains("Testy", StringComparison.Ordinal));
    }

    [Fact]
    public void A_document_of_twenty_pages_or_fewer_adds_no_line()
    {
        using var temp = new TempApp(extractor: new PagesExtractor(20, SheetText), formReader: NoForm());

        var read = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf");

        var preview = temp.App.PreviewDdbImport(new DdbPreviewRequest(read.Token, RulesFamilies.Srd51, null, null, null, false));
        Assert.DoesNotContain(preview.Report.Lines ?? [], l => l.Contains("not read", StringComparison.Ordinal) && l.Contains("page", StringComparison.Ordinal));
    }

    [Fact]
    public void The_text_caps_apply_to_the_extracted_text_and_the_refusal_holds_counts_only()
    {
        var big = $"{Marker} " + new string('x', 14_000);
        using var temp = new TempApp(extractor: new PagesExtractor(20, big), formReader: NoForm());

        var refused = Refused(() => temp.App.ReadDdbSheet("C:/fixture/sheet.pdf"));

        Assert.Equal("ddb.text-too-large", refused.Code);
        Assert.DoesNotContain("Testy", refused.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Testy", refused.Problems.Single().Message, StringComparison.Ordinal);
        Assert.Equal(0, temp.App.DdbSessions.Count);
    }

    [Fact]
    public void A_text_over_the_line_cap_is_refused_with_its_code()
    {
        var lines = string.Join("\n", Enumerable.Repeat("a", 300));
        using var temp = new TempApp(extractor: new PagesExtractor(20, lines), formReader: NoForm());

        var refused = Refused(() => temp.App.ReadDdbSheet("C:/fixture/sheet.pdf"));

        Assert.Equal("ddb.text-too-many-lines", refused.Code);
    }

    [Theory]
    [InlineData("page.text-truncated")]
    [InlineData("page.blocks-truncated")]
    public void A_page_the_worker_cut_short_is_refused_as_too_large_not_read_in_part(string warning)
    {
        using var temp = new TempApp(extractor: new PagesExtractor(1, SheetText) { Warnings = [warning] }, formReader: NoForm());

        var refused = Refused(() => temp.App.ReadDdbSheet("C:/fixture/sheet.pdf"));

        Assert.Equal("ddb.text-too-large", refused.Code);
        Assert.DoesNotContain("Testy", refused.Problems.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pdf_with_no_letters_is_refused_with_counts_and_a_code_of_its_own()
    {
        using var file = FixturePdfs.Write(FixturePdfs.TextPages([[""]]));
        using var temp = new TempApp(formReader: NoForm());

        var refused = Refused(() => temp.App.ReadDdbSheet(file.Path));

        Assert.Equal("ddb.pdf-no-text", refused.Code);
        Assert.Contains("1 page", refused.Problems.Single().Message, StringComparison.Ordinal);
        Assert.Equal(0, temp.App.DdbSessions.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n \n")]
    [InlineData("Name: Bo")]
    public void A_near_empty_extraction_is_refused_the_same_way_and_never_quotes_the_text(string text)
    {
        using var temp = new TempApp(extractor: new PagesExtractor(1, text), formReader: NoForm());

        var refused = Refused(() => temp.App.ReadDdbSheet("C:/fixture/sheet.pdf"));

        Assert.Equal("ddb.pdf-no-text", refused.Code);
        Assert.DoesNotContain("Name", refused.Problems.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("worker.page-timeout", "ddb.read-failed")]
    [InlineData("worker.crashed", "ddb.read-failed")]
    [InlineData("pdf.unreadable", "pdf.unreadable")]
    public void A_failure_reading_the_text_keeps_its_pdf_code_and_a_worker_failure_is_ddb_read_failed(string code, string expected)
    {
        using var temp = new TempApp(extractor: new FailingExtractor(code), formReader: NoForm());

        var refused = Refused(() => temp.App.ReadDdbSheet("C:/fixture/sheet.pdf"));

        Assert.Equal(expected, refused.Code);
    }

    [Fact]
    public void No_page_text_reaches_an_error_a_code_or_a_description_of_the_sheet()
    {
        // The dispatcher logs Exception.ToString(); a read that fails must not carry a line of the document.
        var text = $"Name: {Marker}\nClass: Fixture Arcanist 3\nStrength 16";
        using var temp = new TempApp(extractor: new PagesExtractor(1, text), formReader: NoForm());
        var read = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf");

        var held = temp.App.DdbSessions.Peek(read.Token)!;

        Assert.DoesNotContain("Secret", held.ToString(), StringComparison.Ordinal);
        foreach (var texts in new[] { new PagesExtractor(1, $"{Marker} " + new string('x', 300_000)), new PagesExtractor(1, ""), new PagesExtractor(1, string.Join("\n", Enumerable.Repeat(Marker, 6_000))) })
        {
            using var other = new TempApp(extractor: texts, formReader: NoForm());
            var refused = Refused(() => other.App.ReadDdbSheet("C:/fixture/sheet.pdf"));
            Assert.DoesNotContain("Secret", refused.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Secret", JsonSerializer.Serialize(refused.Problems), StringComparison.Ordinal);
        }
    }
}
