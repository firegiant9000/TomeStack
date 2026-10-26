using System.Text;
using TomeStack.ImportWorker.Detection;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;

namespace TomeStack.ImportWorker.Tests;

/// <summary>Lab Task 12 (design 7.2, L7): the Markdown extractor emits blocks shaped like PdfPig's, one test per grammar row.</summary>
public class MarkdownExtractorTests
{
    private static readonly Guid Source = Guid.Parse("5fad5000-0000-4000-8000-000000000012");

    private static IReadOnlyList<ExtractedPage> Parse(string markdown) => MarkdownExtractor.Parse(Encoding.UTF8.GetBytes(markdown));

    private static List<TextBlock> Blocks(string markdown) => [.. Parse(markdown).SelectMany(p => p.Blocks!)];

    private static string CodeOf(Action act) => Assert.IsType<ExtractionException>(Record.Exception(act)).Code;

    [Fact]
    public void Headings_become_bold_blocks_sized_by_level()
    {
        var blocks = Blocks("# One\n## Two\n### Three\n#### Four");

        Assert.Equal([24d, 18d, 14d, 12d], blocks.Select(b => b.FontSize));
        Assert.All(blocks, b => Assert.True(b.Bold));
        Assert.Equal(["One", "Two", "Three", "Four"], blocks.Select(b => b.Text));
        Assert.All(blocks, b => Assert.Single(b.Lines!));
    }

    [Fact]
    public void A_fifth_level_hash_or_a_hash_without_a_space_is_plain_text()
    {
        var blocks = Blocks("##### Five\n\n#tag");

        Assert.Equal(["##### Five", "#tag"], blocks.Select(b => b.Text));
        Assert.All(blocks, b => Assert.False(b.Bold));
    }

    [Fact]
    public void Paragraph_lines_become_text_lines()
    {
        var blocks = Blocks("first line\nsecond line\n\nnext paragraph");

        Assert.Equal(2, blocks.Count);
        var paragraph = blocks[0];
        Assert.Equal("first line\nsecond line", paragraph.Text);
        Assert.Equal(10, paragraph.FontSize);
        Assert.False(paragraph.Bold);
        Assert.Equal(["first line", "second line"], paragraph.Lines!.Select(l => l.Text));
        // 12 points per source line, reading downward: PDF coordinates, so a lower line has a smaller Y.
        Assert.Equal(12, paragraph.Lines![0].Y - paragraph.Lines[1].Y);
        Assert.All(paragraph.Lines, l => Assert.Equal(72, l.X));
        Assert.Equal("next paragraph", blocks[1].Text);
        Assert.True(blocks[1].Y < paragraph.Y);
    }

    [Fact]
    public async Task Run_in_head_matches_pdf_shape()
    {
        // R4: measured by Pdf_run_in_head_shape. One block, one line holding name and body, not bold, at the body size.
        var pdf = Assert.Single((await PdfRunInShapeTests.ExtractRunIn()).Blocks!);

        var markdown = Assert.Single(Blocks("**Fixture Pool.** You regain one fixture use when you finish a long rest."));

        Assert.Equal(pdf.Text, markdown.Text);
        Assert.Equal(pdf.Bold, markdown.Bold);
        Assert.Equal(pdf.FontSize, markdown.FontSize);
        Assert.Equal(pdf.Lines!.Count, markdown.Lines!.Count);
        Assert.Equal(pdf.Lines[0].Text, markdown.Lines[0].Text);
    }

    [Fact]
    public void List_items_become_one_block_each_with_their_text()
    {
        var blocks = Blocks("- alpha\n* beta\n+ gamma\n1. delta\n2) epsilon\n   continued here\n\nafter");

        Assert.Equal(["alpha", "beta", "gamma", "delta", "epsilon\ncontinued here", "after"], blocks.Select(b => b.Text));
        Assert.All(blocks, b => Assert.Equal(10, b.FontSize));
    }

    [Fact]
    public void Table_cells_get_column_positions()
    {
        var blocks = Blocks("| Level | Bonus | Features |\n|---|:-:|--:|\n| 1 | +2 | Fixture Pool |\n| 2 | +2 | |");

        Assert.Equal(3, blocks.Count);
        var header = blocks[0];
        Assert.True(header.Bold);
        Assert.Equal([72d, 162d, 252d], header.Lines!.Select(l => l.X));
        Assert.Equal(["Level", "Bonus", "Features"], header.Lines!.Select(l => l.Text));
        Assert.Single(header.Lines!.Select(l => l.Y).Distinct());

        var row = blocks[1];
        Assert.False(row.Bold);
        Assert.Equal([72d, 162d, 252d], row.Lines!.Select(l => l.X));
        Assert.Equal(["1", "+2", "Fixture Pool"], row.Lines!.Select(l => l.Text));
        Assert.Single(row.Lines!.Select(l => l.Y).Distinct());
        Assert.True(row.Lines![0].Y < header.Lines![0].Y);

        // An empty cell prints nothing, but the others keep their columns.
        Assert.Equal([72d, 162d], blocks[2].Lines!.Select(l => l.X));
    }

    [Fact]
    public void Ragged_table_becomes_text()
    {
        var blocks = Blocks("| Level | Bonus | Features |\n|---|---|---|\n| 1 | +2 | Fixture Pool |\n| 2 | +2 |");

        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b =>
        {
            Assert.False(b.Bold);
            Assert.Single(b.Lines!);
            Assert.Equal(72, b.Lines![0].X);
        });
        Assert.DoesNotContain(blocks, b => b.Text.Contains("---", StringComparison.Ordinal));
    }

    [Fact]
    public void A_table_without_a_header_separator_is_text()
    {
        var blocks = Blocks("| Level | Bonus |\n| 1 | +2 |\n| 2 | +2 |");

        var block = Assert.Single(blocks);
        Assert.False(block.Bold);
        Assert.Equal(3, block.Lines!.Count);
        Assert.All(block.Lines!, l => Assert.Equal(72, l.X));
    }

    [Fact]
    public void A_separator_with_another_cell_count_than_the_header_is_text()
    {
        var blocks = Blocks("| a | b | c |\n|---|---|\n| 1 | 2 | 3 |");

        Assert.All(blocks, b => Assert.All(b.Lines!, l => Assert.Equal(72, l.X)));
    }

    [Fact]
    public void Page_markers_set_pages()
    {
        var pages = Parse("<!-- page: 1 -->\nfirst\n\n<!-- page: 4 -->\nsecond\n\n<!--page:7-->\nthird");

        Assert.Equal([1, 4, 7], pages.Select(p => p.PageNumber));
        Assert.Equal(["first", "second", "third"], pages.Select(p => p.Text));
        Assert.All(pages, p =>
        {
            Assert.False(p.FromOcr);
            Assert.Equal(612, p.Width);
            Assert.Equal(792, p.Height);
            Assert.Null(p.Error);
        });
    }

    [Fact]
    public void Out_of_order_marker_warns_and_is_ignored()
    {
        var pages = Parse("<!-- page: 3 -->\nthree\n\n<!-- page: 2 -->\nstill three\n\n<!-- page: 3 -->\nalso three");

        var page = Assert.Single(pages);
        Assert.Equal(3, page.PageNumber);
        Assert.Equal("three\n\nstill three\n\nalso three", page.Text);
        Assert.Contains("markdown.page-order", page.Warnings!);
    }

    [Fact]
    public void Sixty_line_pages_without_markers()
    {
        // 75 paragraphs of one line, a blank line between: source lines 0, 2, ... 148.
        var markdown = string.Join("\n\n", Enumerable.Range(0, 75).Select(i => $"paragraph {i}"));

        var pages = Parse(markdown);

        Assert.Equal([1, 2, 3], pages.Select(p => p.PageNumber));
        Assert.Equal([30, 30, 15], pages.Select(p => p.Blocks!.Count));
        Assert.Equal("paragraph 30", pages[1].Blocks![0].Text);
    }

    [Fact]
    public void A_long_paragraph_is_not_cut_and_the_next_block_starts_the_page()
    {
        var markdown = string.Join("\n", Enumerable.Range(0, 70).Select(i => $"line {i}")) + "\n\nafter";

        var pages = Parse(markdown);

        Assert.Equal([1, 2], pages.Select(p => p.PageNumber));
        Assert.Equal(70, pages[0].Blocks![0].Lines!.Count);
        Assert.Equal("after", pages[1].Text);
    }

    [Fact]
    public void Html_is_stripped_and_never_kept_as_markup()
    {
        var pages = Parse("""
            Before <b>bold</b> and <a href="https://example.test/x">link</a> after.

            <script>
            var secret = "payload";
            </script>

            <style>body { color: red }</style>

            <div class="x">inside</div>
            <script type="text/javascript">alert(1)</script>after script
            <!-- a comment
            spanning lines -->
            Unclosed <img src="x" onerror="alert(2)"
            ![image alt](https://example.test/i.png) kept words
            """);

        var text = string.Join("\n", pages.Select(p => p.Text));
        Assert.DoesNotContain('<', text);
        Assert.DoesNotContain("payload", text, StringComparison.Ordinal);
        Assert.DoesNotContain("color: red", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", text, StringComparison.Ordinal);
        Assert.DoesNotContain("comment", text, StringComparison.Ordinal);
        Assert.Contains("Before bold and link after.", text, StringComparison.Ordinal);
        Assert.Contains("inside", text, StringComparison.Ordinal);
        Assert.Contains("after script", text, StringComparison.Ordinal);
        Assert.Contains("kept words", text, StringComparison.Ordinal);
        Assert.DoesNotContain("image alt", text, StringComparison.Ordinal);
        Assert.All(pages.SelectMany(p => p.Blocks!).SelectMany(b => b.Lines!), l => Assert.DoesNotContain('<', l.Text));
    }

    [Fact]
    public void A_less_than_sign_that_starts_no_tag_stays_text()
    {
        var text = Assert.Single(Blocks("if a < b and 2<3 then")).Text;

        Assert.Equal("if a < b and 2<3 then", text);
    }

    [Fact]
    public void Links_keep_their_text_and_images_are_dropped()
    {
        var text = Assert.Single(Blocks("See [the fixture rules](https://example.test/rules) and ![diagram](d.png) now, `code` and ~~gone~~ too.")).Text;

        Assert.Equal("See the fixture rules and  now, code and gone too.", text);
    }

    [Fact]
    public void Emphasis_marks_go_but_intra_word_marks_stay()
    {
        var text = Assert.Single(Blocks("*one* and _two_ and **three** and snake_case_name and 2 * 3 and 5*4")).Text;

        Assert.Equal("one and two and three and snake_case_name and 2 * 3 and 5*4", text);
    }

    [Fact]
    public void Code_fences_become_plain_text_lines()
    {
        var blocks = Blocks("intro\n\n```\n# not a heading\n| a | b |\n```\n\nafter");

        Assert.Equal(["intro", "# not a heading\n| a | b |", "after"], blocks.Select(b => b.Text));
        Assert.All(blocks, b => Assert.False(b.Bold));
    }

    [Fact]
    public void Front_matter_is_ignored_with_a_warning()
    {
        var pages = Parse("---\ntitle: Fixture\ntags: [a]\n---\n# Heading\n\ntext");

        var page = Assert.Single(pages);
        Assert.Equal("Heading\n\ntext", page.Text);
        Assert.Contains("markdown.front-matter-ignored", page.Warnings!);
    }

    [Fact]
    public void A_thematic_break_at_the_start_is_not_front_matter()
    {
        var pages = Parse("---\ntext");

        var page = Assert.Single(pages);
        Assert.Equal("text", page.Text);
        Assert.DoesNotContain("markdown.front-matter-ignored", page.Warnings!);
    }

    [Fact]
    public void Text_is_normalised_like_pdf_output()
    {
        // NBSP and a ligature fold to plain characters (FormKC), soft hyphens go, hyphen variants become '-', and CRLF is one break.
        var text = Assert.Single(Blocks("a\u00A0b \uFB01re soft\u00ADly 1st\u2010level\r\nnext")).Text;

        Assert.Equal("a b fire softly 1st-level\nnext", text);
    }

    [Fact]
    public void Control_characters_become_spaces_and_a_bom_is_skipped()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "a\0b\u0007c\tleft"u8];

        var text = Assert.Single(Assert.Single(MarkdownExtractor.Parse(bytes)).Blocks!).Text;

        Assert.Equal("a b c left", text);
    }

    [Fact]
    public void Over_limits_fail_with_too_large_and_yield_nothing()
    {
        Assert.Equal("markdown.too-large", CodeOf(() => MarkdownExtractor.Parse(new byte[ExtractionLimits.MarkdownMaxBytes + 1])));
        Assert.Equal("markdown.too-large", CodeOf(() => Parse(new string('\n', ExtractionLimits.MarkdownMaxLines + 1))));
        Assert.Equal("markdown.too-large", CodeOf(() => Parse(new string('a', ExtractionLimits.MarkdownMaxLineLength + 1))));
        Assert.Equal("markdown.too-large", CodeOf(() => Parse("<!-- page: 2001 -->\ntext")));
        Assert.Equal("markdown.too-large", CodeOf(() => Parse(string.Concat(Enumerable.Range(1, ExtractionLimits.MarkdownMaxPages + 1).Select(i => $"<!-- page: {i} -->\ntext\n")))));
        Assert.Equal("markdown.too-large", CodeOf(() => Parse(Table(ExtractionLimits.MarkdownMaxTableColumns + 1))));

        // At the limits, the input is accepted.
        Assert.NotEmpty(Parse(Table(ExtractionLimits.MarkdownMaxTableColumns)));
        Assert.NotEmpty(Parse(new string('a', ExtractionLimits.MarkdownMaxLineLength)));
        Assert.NotEmpty(Parse(string.Concat(Enumerable.Range(1, ExtractionLimits.MarkdownMaxPages).Select(i => $"<!-- page: {i} -->\ntext\n"))));
    }

    [Theory]
    [InlineData("![")]
    [InlineData("](")]
    [InlineData("[](")]
    [InlineData("<a ")]
    [InlineData("<")]
    [InlineData("* _ ")]
    [InlineData("\\")]
    [InlineData("|-")]
    [InlineData("`~")]
    public void Adversarial_lines_at_the_limits_are_read_in_linear_time(string unit)
    {
        // 400 lines of 10,000 characters (4 MB, the file limit): a quadratic scan would take minutes.
        var line = string.Concat(Enumerable.Repeat(unit, ExtractionLimits.MarkdownMaxLineLength / unit.Length));
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', Enumerable.Repeat(line, 400)));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var outcome = Record.Exception(() => MarkdownExtractor.Parse(bytes));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"{watch.Elapsed.TotalSeconds:0.0} s");
        Assert.True(outcome is null || outcome is ExtractionException { Code: "markdown.too-large" }, outcome?.ToString());
    }

    // ---- fix round 1 ----

    [Theory]
    [InlineData("<<b>script>alert(1)")]
    [InlineData("<scr<script>ipt>alert(1)</script>")]
    [InlineData("<<<b>!-- x -->")]
    [InlineData("\uFF1Cscript\uFF1Ealert(1)\uFF1C/script\uFF1E")]
    [InlineData("\uFF1C!-- x --\uFF1E")]
    [InlineData("a \uFF1Cb\uFF1E bold")]
    [InlineData("<script")]
    [InlineData("<!-- never closed")]
    [InlineData("<style")]
    [InlineData("## <<b>h1>head")]
    [InlineData("| a | <<b>i>b |\n|---|---|\n| 1 | \uFF1Cscript\uFF1E |")]
    [InlineData("```\n<<b>script>x\n```")]
    public void Stripping_cannot_leave_or_rebuild_a_tag_opener(string markdown)
    {
        var pages = Parse(markdown);

        Assert.All(Properties.MarkdownProperties.AllText(pages), text => Assert.False(Properties.MarkdownProperties.HoldsTagOpener(text), text));
    }

    [Fact]
    public void Entities_are_not_decoded_so_they_stay_literal_text()
    {
        // Decision: no entity is ever decoded, so "&lt;script&gt;" is text that can never become a tag opener.
        var text = Assert.Single(Blocks("&lt;script&gt;alert(1)&lt;/script&gt; and &#60;b&#62;")).Text;

        Assert.Equal("&lt;script&gt;alert(1)&lt;/script&gt; and &#60;b&#62;", text);
    }

    [Fact]
    public void A_fullwidth_bracket_is_folded_and_then_removed_as_a_tag_opener()
    {
        var text = Assert.Single(Blocks("x \uFF1Cscript\uFF1Ealert(1) y 1 \uFF1C 2")).Text;

        Assert.Equal("x script>alert(1) y 1 < 2", text);
    }

    [Theory]
    [InlineData("<!-- note")]
    [InlineData("<script")]
    [InlineData("<script type=\"x\">")]
    [InlineData("<style>")]
    public void An_opener_with_no_closer_drops_only_its_line(string opener)
    {
        var tail = string.Join("\n\n", Enumerable.Range(0, 50).Select(i => $"kept line {i}"));
        var pages = Parse($"intro\n{opener}\n\n{tail}");

        var text = string.Join("\n", pages.Select(p => p.Text));
        Assert.Contains("intro", text, StringComparison.Ordinal);
        Assert.Contains("kept line 0", text, StringComparison.Ordinal);
        Assert.Contains("kept line 49", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_closer_before_the_opener_does_not_count()
    {
        var pages = Parse("--> early\n\n<!-- late opener\n\nkept after");

        Assert.Contains("kept after", string.Join("\n", pages.Select(p => p.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_closed_span_still_drops_its_content()
    {
        var text = Assert.Single(Parse("a\n\n<!-- gone\nstill gone -->\n\n<script>\nx = 1\n</script>\n\nb")).Text;

        Assert.Equal("a\n\nb", text);
    }

    [Fact]
    public void An_oversized_block_is_cut_to_a_prefix_with_a_warning_not_dropped()
    {
        // 300 lines of 1,000 characters in one paragraph: 300,000 characters against a 200,000 cap.
        var markdown = string.Join("\n", Enumerable.Range(0, 300).Select(_ => new string('a', 1000)));

        var page = Assert.Single(Parse(markdown));

        var block = Assert.Single(page.Blocks!);
        Assert.Equal(ExtractionLimits.Default.MaxTextPerPage, block.Text.Length);
        Assert.True(block.Lines!.Sum(l => l.Text.Length) <= ExtractionLimits.Default.MaxTextPerPage);
        Assert.Contains("page.text-truncated", page.Warnings!);
    }

    [Fact]
    public void A_block_after_a_full_page_of_text_is_dropped_with_the_warning()
    {
        var big = string.Join("\n", Enumerable.Range(0, 250).Select(_ => new string('a', 1000)));

        var page = Assert.Single(Parse($"<!-- page: 1 -->\n{big}\n\nlater paragraph"));

        Assert.Single(page.Blocks!);
        Assert.Contains("page.text-truncated", page.Warnings!);
    }

    [Fact]
    public void Lines_per_page_are_capped_like_the_pdf_reader()
    {
        var page = Assert.Single(Parse(string.Concat(Enumerable.Repeat("a\n", ExtractionLimits.Default.MaxLinesPerPage + 5))));

        Assert.Equal(ExtractionLimits.Default.MaxLinesPerPage, page.Blocks!.Sum(b => b.Lines!.Count));
        Assert.Contains("page.text-truncated", page.Warnings!);
    }

    [Theory]
    [InlineData("---\ntitle: only front matter\n---\n", "markdown.front-matter-ignored")]
    [InlineData("<!-- page: 3 -->\n<!-- page: 2 -->\n", "markdown.page-order")]
    public void Document_warnings_reach_the_output_even_with_no_content(string markdown, string code)
    {
        var page = Assert.Single(Parse(markdown));

        Assert.Equal("", page.Text);
        Assert.Empty(page.Blocks!);
        Assert.Contains(code, page.Warnings!);
        Assert.Contains("page.no-text", page.Warnings!);
    }

    [Fact]
    public void An_empty_file_has_no_pages_and_no_warning()
    {
        Assert.Empty(Parse(""));
    }

    [Fact]
    public void Exactly_200000_lines_are_accepted_and_one_more_is_not()
    {
        var atLimit = string.Concat(Enumerable.Repeat("a\n", ExtractionLimits.MarkdownMaxLines));

        Assert.NotEmpty(Parse(atLimit));
        Assert.Equal("markdown.too-large", CodeOf(() => Parse(atLimit + "a")));
    }

    [Fact]
    public void Exactly_4_MiB_is_accepted_and_one_byte_more_is_not()
    {
        var bytes = new byte[ExtractionLimits.MarkdownMaxBytes];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = i % 1001 == 1000 ? (byte)'\n' : (byte)'a';

        Assert.NotEmpty(MarkdownExtractor.Parse(bytes));
        Assert.Equal("markdown.too-large", CodeOf(() => MarkdownExtractor.Parse(new byte[ExtractionLimits.MarkdownMaxBytes + 1])));
    }

    [Fact]
    public void No_invisible_character_is_spelled_literally_in_the_sources()
    {
        // The files this task owns spell invisible characters as escapes, so no tool can rewrite them unseen.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "TomeStack.slnx")))
            root = root.Parent!;
        string[] files =
        [
            Path.Combine(root.FullName, "src", "ImportWorker", "Extraction", "MarkdownExtractor.cs"),
            Path.Combine(root.FullName, "tests", "ImportWorker.Tests", "MarkdownExtractorTests.cs"),
            Path.Combine(root.FullName, "tests", "ImportWorker.Tests", "Properties", "MarkdownProperties.cs"),
        ];
        foreach (var file in files)
            Assert.DoesNotContain(File.ReadAllText(file), c => c is (char)0xFEFF or (char)0x00AD or (char)0x2010 or (char)0x2011 or (char)0x200B);
    }

    private static string Table(int columns) =>
        $"|{string.Concat(Enumerable.Repeat(" h |", columns))}\n|{string.Concat(Enumerable.Repeat("---|", columns))}\n|{string.Concat(Enumerable.Repeat(" c |", columns))}";

    [Fact]
    public async Task An_over_limit_file_fails_through_the_extractor_before_any_event()
    {
        using var file = FixturePdfs.Write(new byte[ExtractionLimits.MarkdownMaxBytes + 1], "big.md");
        var events = new List<ExtractionEvent>();

        var error = await Assert.ThrowsAsync<ExtractionException>(async () =>
        {
            await foreach (var item in new MarkdownExtractor().ExtractAsync(file.Path, PageScope.WholeDocument, CancellationToken.None))
                events.Add(item);
        });

        Assert.Equal("markdown.too-large", error.Code);
        Assert.Empty(events);
    }

    [Fact]
    public void Invalid_utf8_fails_with_encoding()
    {
        Assert.Equal("markdown.encoding", CodeOf(() => MarkdownExtractor.Parse([0x68, 0x69, 0xC3, 0x28])));
        Assert.Equal("markdown.encoding", CodeOf(() => MarkdownExtractor.Parse([0xFF, 0xFE, 0x41, 0x00])));
        Assert.Equal("markdown.encoding", CodeOf(() => MarkdownExtractor.Parse([0xED, 0xA0, 0x80]))); // a lone surrogate
    }

    [Fact]
    public async Task The_extractor_yields_the_opened_document_then_the_pages_in_scope()
    {
        using var file = FixturePdfs.Write(Encoding.UTF8.GetBytes("<!-- page: 1 -->\none\n\n<!-- page: 2 -->\ntwo\n\n<!-- page: 3 -->\nthree"), "doc.md");
        var events = new List<ExtractionEvent>();

        await foreach (var item in new MarkdownExtractor().ExtractAsync(file.Path, new PageScope(2, 3), CancellationToken.None))
            events.Add(item);

        Assert.Equal(3, Assert.IsType<DocumentOpened>(events[0]).PageCount);
        Assert.Equal([2, 3], events.OfType<ExtractedPage>().Select(p => p.PageNumber));
    }

    [Fact]
    public async Task A_missing_file_fails_with_a_code()
    {
        var path = Path.Combine(Path.GetTempPath(), "tomestack-import-tests", Guid.NewGuid().ToString("N"), "missing.md");

        var error = await Assert.ThrowsAsync<ExtractionException>(async () =>
        {
            await foreach (var unused in new MarkdownExtractor().ExtractAsync(path, PageScope.WholeDocument, CancellationToken.None))
            {
            }
        });

        Assert.Equal("markdown.missing", error.Code);
    }

    // ---- detection ----

    private static IReadOnlyList<DraftCandidate> Detect(IReadOnlyList<ExtractedPage> pages) =>
        CandidateDetector.Detect([.. pages.Select(p => new DetectionPage(p.PageNumber, p.Text, p.Blocks ?? [], p.FromOcr))], new(Source, [RulesFamilies.Srd521], _ => false));

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "lab", "placeholder-class.md");

    [Fact]
    public void Placeholder_class_markdown_detects_like_its_pdf()
    {
        var pages = MarkdownExtractor.Parse(File.ReadAllBytes(FixturePath));
        var found = Detect(pages);

        // The class, read from its level table of twenty rows: the levels that name a feature, and the table read whole.
        var cls = Assert.Single(found, c => c.ProposedKind == ContentKind.Class);
        Assert.Equal("Placeholder", cls.ProposedName);
        Assert.Equal(
            [1, 2, 3, 5, 6, 9, 11, 12, 15, 18, 20],
            cls.Fields.Keys.Where(k => k.StartsWith("level ", StringComparison.Ordinal)).Select(k => int.Parse(k[6..], System.Globalization.CultureInfo.InvariantCulture)).Order());
        Assert.DoesNotContain(cls.Uncertainties, u => u.Contains("could not be read whole", StringComparison.Ordinal));
        Assert.Equal("Fixture Pool", cls.Fields["level 1"]);
        Assert.Equal("Fixture Finale", cls.Fields["level 20"]);

        // The features, named by their headings with the level and owner from the line under it.
        var features = found.Where(c => c.ProposedKind == ContentKind.Feature && c.Fields.ContainsKey("level")).ToList();
        Assert.Equal(
            ["Fixture Pool 1", "Fixture Rider 2", "Fixture Edge 3", "Fixture Surge 5", "Fixture Ward 6", "Fixture Anchor 9", "Fixture Reserve 11", "Fixture Rider 12", "Fixture Lull 15", "Fixture Beacon 18", "Fixture Finale 20"],
            features.OrderBy(f => int.Parse(f.Fields["level"], System.Globalization.CultureInfo.InvariantCulture)).Select(f => $"{f.ProposedName} {f.Fields["level"]}"));
    }

    [Fact]
    public void Same_named_features_at_two_levels_stay_two_candidates()
    {
        // Review Focus 3: no page markers, so the second Fixture Rider sits on another synthetic page.
        var pages = MarkdownExtractor.Parse(File.ReadAllBytes(FixturePath));
        var found = Detect(pages);

        var riders = found.Where(c => c.ProposedKind == ContentKind.Feature && c.ProposedName == "Fixture Rider").ToList();

        Assert.Equal(2, riders.Count);
        Assert.Equal(2, riders.Select(r => r.Page.Start).Distinct().Count());
        Assert.True(pages.Count >= 2);
    }

    [Fact]
    public void Same_named_features_on_one_page_share_a_page()
    {
        // The counterpart: close together they share a page, which is what the dedup identity (kind, name, page) keys on.
        var markdown = "### Fixture Rider\n\nLevel 2 Placeholder Feature\n\nFirst text.\n\n### Fixture Rider\n\nLevel 12 Placeholder Feature\n\nSecond text.";

        var riders = Detect(Parse(markdown)).Where(c => c.ProposedName == "Fixture Rider").ToList();

        Assert.Equal(2, riders.Count);
        Assert.All(riders, r => Assert.Equal(1, r.Page.Start));
    }
}
