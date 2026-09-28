using TomeStack.ImportWorker.Extraction;
using TomeStack.ImportWorker.Host;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Windows.Storage.Streams;
using PdfRenderDocument = Windows.Data.Pdf.PdfDocument;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// M4 D1, ADR-009 (b): a page without a text layer is read by Windows OCR. The image-only page is made at test time:
/// the fixture book's page 3 (original text) rendered by Windows.Data.Pdf and placed on a page as a PNG.
/// </summary>
public class OcrTests
{
    /// <summary>xUnit 2 has no runtime skip; decide at discovery whether Windows has an OCR language.</summary>
    public sealed class OcrFactAttribute : FactAttribute
    {
        public OcrFactAttribute()
        {
            using var engine = WindowsOcrEngine.TryCreate();
            if (engine is null)
                Skip = "Windows has no OCR language installed on this machine (Settings, Time & language, Language).";
        }
    }

    private static async Task<byte[]> ImageOnlyPage(string sourcePdf, int page)
    {
        using var file = File.OpenRead(sourcePdf);
        using var stream = file.AsRandomAccessStream();
        var document = await PdfRenderDocument.LoadFromStreamAsync(stream);
        using var source = document.GetPage((uint)(page - 1));
        using var image = new InMemoryRandomAccessStream();
        await source.RenderToStreamAsync(image, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 1700 });
        var png = new byte[image.Size];
        using (var reader = new DataReader(image.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)image.Size);
            reader.ReadBytes(png);
        }
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.Letter).AddPng(png, new PdfRectangle(0, 0, 612, 792));
        return builder.Build();
    }

    [OcrFact]
    public async Task A_page_with_no_text_layer_is_read_by_windows_ocr_with_its_lines_as_blocks()
    {
        using var file = FixturePdfs.Write(await ImageOnlyPage(FixturePdfs.ImportPath, 3));
        using var ocr = WindowsOcrEngine.TryCreate()!;

        var events = new List<ExtractionEvent>();
        await foreach (var item in new PdfPigExtractor(ocr).ExtractAsync(file.Path, PageScope.WholeDocument, CancellationToken.None))
            events.Add(item);

        var page = events.OfType<ExtractedPage>().Single();
        Assert.True(page.FromOcr);
        Assert.Contains("Keen Watcher", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("initiative", page.Text, StringComparison.OrdinalIgnoreCase);
        // Lines as blocks in PDF points; OCR has no font size or confidence.
        var heading = page.Blocks!.First(b => b.Text.Contains("Keen Watcher", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, heading.FontSize);
        Assert.InRange(heading.X, 50, 110);
        Assert.InRange(heading.Y, 680, 760);
    }

    [OcrFact]
    public async Task The_worker_process_reads_an_image_only_page_with_ocr_too()
    {
        using var file = FixturePdfs.Write(await ImageOnlyPage(FixturePdfs.ImportPath, 2));
        var events = new List<ExtractionEvent>();
        await foreach (var item in new WorkerProcessExtractor(Path.Combine(AppContext.BaseDirectory, "TomeStack.ImportWorker.Host.exe")).ExtractAsync(file.Path, PageScope.WholeDocument, CancellationToken.None))
            events.Add(item);

        var page = events.OfType<ExtractedPage>().Single();
        Assert.True(page.FromOcr);
        Assert.Contains("Ember Lance", page.Text, StringComparison.OrdinalIgnoreCase);
    }
}
