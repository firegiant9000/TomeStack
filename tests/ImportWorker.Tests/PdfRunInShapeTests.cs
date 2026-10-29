using TomeStack.ImportWorker.Extraction;

namespace TomeStack.ImportWorker.Tests;

/// <summary>Lab Task 12 (ruling R4): how PdfPig presents a run-in head, so the Markdown extractor can copy the shape.</summary>
public class PdfRunInShapeTests
{
    internal static async Task<ExtractedPage> ExtractRunIn()
    {
        using var file = FixturePdfs.Write(FixturePdfs.RunInHead());
        await foreach (var item in new PdfPigExtractor().ExtractAsync(file.Path, PageScope.WholeDocument, CancellationToken.None))
        {
            if (item is ExtractedPage page)
                return page;
        }
        throw new InvalidOperationException("No page.");
    }

    /// <summary>
    /// Measured 2026-10-25: a bold name and a regular body on one line come out as ONE block with ONE line holding both
    /// ("Fixture Pool. You regain ..."), not bold (fewer than half of the letters are bold), at the body size. So a run-in
    /// head is never a heading (<c>CandidateDetector.IsHeading</c> needs bold). The Markdown extractor must emit this shape.
    /// </summary>
    [Fact]
    public async Task Pdf_run_in_head_shape()
    {
        var page = await ExtractRunIn();

        var block = Assert.Single(page.Blocks!);
        Assert.Equal("Fixture Pool. You regain one fixture use when you finish a long rest.", block.Text);
        Assert.False(block.Bold);
        Assert.Equal(10, block.FontSize);
        var line = Assert.Single(block.Lines!);
        Assert.Equal(block.Text, line.Text);
    }
}
