using TomeStack.ImportWorker.Extraction;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// M4 D1 (ADR-009 (a), SPEC I-01, Q-02): PdfPig extraction of page text and layout from the original fixture book, and
/// the malformed-input suite. These tests call the extractor in the test process; <see cref="WorkerProcessTests"/> runs
/// it in the isolated worker, as the app does.
/// </summary>
public class ExtractionTests
{
    private static async Task<List<ExtractionEvent>> Extract(string path, PageScope? scope = null, ExtractionLimits? limits = null, IOcrEngine? ocr = null)
    {
        var events = new List<ExtractionEvent>();
        await foreach (var item in new PdfPigExtractor(ocr, limits).ExtractAsync(path, scope ?? PageScope.WholeDocument, CancellationToken.None))
            events.Add(item);
        return events;
    }

    private static async Task<ExtractionException> Refused(byte[] pdf, ExtractionLimits? limits = null)
    {
        using var file = FixturePdfs.Write(pdf);
        return await Assert.ThrowsAsync<ExtractionException>(() => Extract(file.Path, limits: limits));
    }

    [Fact]
    public async Task The_committed_fixture_book_has_exactly_the_generators_original_text()
    {
        // Regenerate with TOMESTACK_WRITE_FIXTURES=1 (writes tests/RulesFixtures/pdf/fixture-import.pdf). The bytes differ
        // per build (PdfPig writes a random document /ID), so the check compares the text of every page.
        var generated = FixturePdfs.Import();
        if (Environment.GetEnvironmentVariable("TOMESTACK_WRITE_FIXTURES") == "1")
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(repo.FullName, "TomeStack.slnx")))
                repo = repo.Parent!;
            File.WriteAllBytes(Path.Combine(repo.FullName, "tests", "RulesFixtures", "pdf", "fixture-import.pdf"), generated);
        }
        using var fresh = FixturePdfs.Write(generated);
        static IEnumerable<string> Texts(IEnumerable<ExtractionEvent> events) => events.OfType<ExtractedPage>().Select(p => p.Text);
        Assert.Equal(Texts(await Extract(fresh.Path)), Texts(await Extract(FixturePdfs.ImportPath)));
    }

    [Fact]
    public async Task The_fixture_book_gives_page_text_and_blocks_in_reading_order_with_page_coordinates()
    {
        var events = await Extract(FixturePdfs.ImportPath);

        Assert.Equal(FixturePdfs.ImportPageCount, Assert.IsType<DocumentOpened>(events[0]).PageCount);
        var pages = events.OfType<ExtractedPage>().ToList();
        Assert.Equal(Enumerable.Range(1, FixturePdfs.ImportPageCount), pages.Select(p => p.PageNumber));
        Assert.All(pages, p => Assert.Null(p.Error));

        // Page 2 has two columns: the left spell reads completely before the right one.
        var spells = pages[1];
        Assert.False(spells.FromOcr);
        var ember = spells.Text.IndexOf("Fixture Ember Lance", StringComparison.Ordinal);
        var damage = spells.Text.IndexOf("3d6 Fire", StringComparison.Ordinal);
        var frost = spells.Text.IndexOf("Fixture Frost Veil", StringComparison.Ordinal);
        Assert.True(ember >= 0 && damage > ember && frost > damage, "the left column comes first");

        // Blocks carry page coordinates (PDF points, bottom-left origin), the font size and a bold hint for headings.
        var heading = spells.Blocks!.First(b => b.Text.StartsWith("Fixture Ember Lance", StringComparison.Ordinal));
        Assert.True(heading.Bold);
        Assert.Equal(14, heading.FontSize);
        Assert.InRange(heading.X, 49, 51);
        Assert.InRange(heading.Y, 700, 740);
        Assert.Equal((612, 792), (spells.Width, spells.Height));
        Assert.All(spells.Blocks!, b => Assert.True(b.X >= 0 && b.Y >= 0 && b.X + b.Width <= spells.Width && b.Y + b.Height <= spells.Height));
        Assert.Contains(pages[3].Blocks!, b => !b.Bold && b.Text.Contains("Fixture Hookblade", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_page_without_a_text_layer_is_reported_and_never_invented()
    {
        var page = (await Extract(FixturePdfs.ImportPath, new(6, 6))).OfType<ExtractedPage>().Single();

        Assert.Equal((6, "", false), (page.PageNumber, page.Text, page.FromOcr));
        Assert.Contains("page.no-text", page.Warnings!);
    }

    [Fact]
    public async Task An_unavailable_ocr_engine_is_reported_per_page()
    {
        var page = (await Extract(FixturePdfs.ImportPath, new(6, 6), ocr: new NoLanguageOcr())).OfType<ExtractedPage>().Single();
        Assert.Contains("ocr.unavailable", page.Warnings!);
    }

    [Fact]
    public async Task A_page_scope_extracts_only_its_pages_and_clips_to_the_document()
    {
        var pages = (await Extract(FixturePdfs.ImportPath, new(4, 99))).OfType<ExtractedPage>().Select(p => p.PageNumber);
        Assert.Equal([4, 5, 6], pages);
    }

    [Fact]
    public async Task Page_text_and_blocks_are_capped()
    {
        var page = (await Extract(FixturePdfs.ImportPath, new(2, 2), new ExtractionLimits { MaxTextPerPage = 40, MaxBlocksPerPage = 2 })).OfType<ExtractedPage>().Single();
        Assert.Equal(40, page.Text.Length);
        Assert.Equal(2, page.Blocks!.Count);
        Assert.Contains("page.text-truncated", page.Warnings!);
        Assert.Contains("page.blocks-truncated", page.Warnings!);
    }

    // ---- malformed input (SPEC Q-02, Q-04) ----

    [Fact]
    public async Task A_file_that_is_not_a_PDF_is_refused_before_parsing()
    {
        Assert.Equal("pdf.not-a-pdf", (await Refused("PK\u0003\u0004 not a pdf"u8.ToArray())).Code);
        Assert.Equal("pdf.not-a-pdf", (await Refused([])).Code);
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_refused_before_parsing()
    {
        Assert.Equal("pdf.too-large", (await Refused(FixturePdfs.Import(), new ExtractionLimits { MaxBytes = 100 })).Code);
    }

    [Fact]
    public async Task A_truncated_PDF_fails_cleanly_or_yields_what_it_can_without_inventing_text()
    {
        var whole = FixturePdfs.Import();
        foreach (var fraction in new[] { 0.1, 0.5, 0.9 })
        {
            using var file = FixturePdfs.Write(whole[..(int)(whole.Length * fraction)]);
            try
            {
                var pages = (await Extract(file.Path)).OfType<ExtractedPage>().ToList();
                // Lenient parsing may recover some pages; whatever it returns is fixture text or a page error.
                Assert.All(pages, p => Assert.True(p.Error is not null || p.Text.Length == 0 || p.Text.Contains("Fixture", StringComparison.Ordinal) || p.Text.Contains("fixture", StringComparison.Ordinal)));
            }
            catch (ExtractionException ex)
            {
                Assert.Equal("pdf.unreadable", ex.Code);
                Assert.DoesNotContain("Fixture", ex.Message, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task An_encrypted_PDF_is_refused_with_a_clear_code()
    {
        var refused = await Refused(FixturePdfs.Encrypted());
        Assert.Equal("pdf.encrypted", refused.Code);
    }

    [Fact]
    public async Task A_PDF_with_more_pages_than_the_limit_is_refused_before_any_page_is_read()
    {
        Assert.Equal("pdf.too-many-pages", (await Refused(FixturePdfs.Pages(12), new ExtractionLimits { MaxPages = 10 })).Code);
        Assert.Equal("pdf.too-many-pages", (await Refused(FixturePdfs.Pages(5_001))).Code); // the default limit
    }

    [Fact]
    public async Task A_page_tree_that_lies_about_its_count_does_not_hang_or_crash()
    {
        using var file = FixturePdfs.Write(FixturePdfs.OnePage("BT ET"u8.ToArray(), pagesExtra: "/Count 900000000"));
        try
        {
            var pages = (await Extract(file.Path)).OfType<ExtractedPage>().ToList();
            Assert.True(pages.Count <= 1);
        }
        catch (ExtractionException ex)
        {
            Assert.Contains(ex.Code, new[] { "pdf.too-many-pages", "pdf.unreadable" });
        }
    }

    [Fact]
    public async Task A_page_with_an_unreadable_content_stream_is_a_page_error_not_a_failed_book()
    {
        // A FlateDecode stream that is not zlib data.
        using var file = FixturePdfs.Write(FixturePdfs.OnePage("this is not deflated"u8.ToArray(), "/Filter /FlateDecode "));
        var events = await Extract(file.Path);
        var page = events.OfType<ExtractedPage>().Single();
        Assert.True(page.Error == "page.unreadable" || page.Text.Length == 0);
    }

    private sealed class NoLanguageOcr : IOcrEngine
    {
        public bool Available => false;

        public Task<IReadOnlyList<TextBlock>?> RecognizeAsync(string path, int pageNumber, double pageWidth, double pageHeight, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not available.");
    }
}
