using System.Runtime.CompilerServices;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Exceptions;

namespace TomeStack.ImportWorker.Extraction;

/// <summary>
/// ADR-009 (a): page text and layout with PdfPig, which has no scripting engine and no network code. The text of a page
/// is its blocks (Docstrum segmentation) in reading order, separated by blank lines, so a two-column page reads column
/// by column. A page with no letters is read by <see cref="IOcrEngine"/> when one is available. This class parses
/// untrusted bytes, so it runs only inside the worker process (<see cref="WorkerMain"/>), never in the app.
/// </summary>
public sealed class PdfPigExtractor(IOcrEngine? ocr = null, ExtractionLimits? limits = null) : IDocumentExtractor
{
    private readonly ExtractionLimits _limits = limits ?? ExtractionLimits.Default;

    public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(scope);
        CheckFile(path);
        using var document = Open(path);
        var pageCount = document.NumberOfPages;
        if (pageCount > _limits.MaxPages)
            throw new ExtractionException("pdf.too-many-pages", $"The PDF has {pageCount} pages; at most {_limits.MaxPages} can be imported.");
        yield return new DocumentOpened(pageCount);

        var (first, last) = scope.Resolve(pageCount);
        for (var number = first; number <= last; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await ReadPageAsync(document, path, number, cancellationToken).ConfigureAwait(false);
        }
    }

    private void CheckFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new ExtractionException("pdf.missing", "The PDF file is missing.");
        if (info.Length > _limits.MaxBytes)
            throw new ExtractionException("pdf.too-large", $"The PDF is larger than {_limits.MaxBytes / (1024 * 1024)} MB.");
        Span<byte> header = stackalloc byte[5];
        using var stream = File.OpenRead(path);
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length || !header.SequenceEqual("%PDF-"u8))
            throw new ExtractionException("pdf.not-a-pdf", "The file is not a PDF.");
    }

    private static PdfDocument Open(string path)
    {
        try
        {
            return PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new ExtractionException("pdf.encrypted", "The PDF is encrypted. Remove the password in another program, then import it again.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            // Parser messages can quote document bytes, so only the exception type is kept (and not even that is shown).
            throw new ExtractionException("pdf.unreadable", "The PDF could not be read. It may be damaged.");
        }
    }

    private async Task<ExtractedPage> ReadPageAsync(PdfDocument document, string path, int number, CancellationToken cancellationToken)
    {
        Page page;
        IReadOnlyList<TextBlock> blocks;
        try
        {
            page = document.GetPage(number);
            blocks = Blocks(page);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return new ExtractedPage(number, "", false, Error: "page.unreadable");
        }

        var warnings = new List<string>();
        var fromOcr = false;
        if (page.Letters.Count == 0)
        {
            if (ocr is { Available: true })
            {
                blocks = await ocr.RecognizeAsync(path, number, page.Width, page.Height, cancellationToken).ConfigureAwait(false) ?? [];
                fromOcr = true;
                if (blocks.Count == 0)
                    warnings.Add("page.no-text");
            }
            else
            {
                warnings.Add(ocr is null ? "page.no-text" : "ocr.unavailable");
            }
        }
        if (blocks.Count > _limits.MaxBlocksPerPage)
        {
            blocks = [.. blocks.Take(_limits.MaxBlocksPerPage)];
            warnings.Add("page.blocks-truncated");
        }
        var text = string.Join("\n\n", blocks.Select(b => b.Text));
        if (text.Length > _limits.MaxTextPerPage)
        {
            text = text[.._limits.MaxTextPerPage];
            warnings.Add("page.text-truncated");
        }
        return new ExtractedPage(number, text, fromOcr, blocks, page.Width, page.Height, warnings);
    }

    private static IReadOnlyList<TextBlock> Blocks(Page page)
    {
        if (page.Letters.Count == 0)
            return [];
        var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        return
        [
            .. UnsupervisedReadingOrderDetector.Instance.Get(blocks).Select(block =>
            {
                var letters = block.TextLines.SelectMany(l => l.Words).SelectMany(w => w.Letters).ToList();
                var size = letters.Count == 0 ? 0 : letters.GroupBy(l => Math.Round(l.PointSize, 1)).MaxBy(g => g.Count())!.Key;
                var bold = letters.Count > 0 && letters.Count(l => l.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true) * 2 > letters.Count;
                var box = block.BoundingBox;
                return new TextBlock(Normalize(block.Text), box.Left, box.Bottom, box.Width, box.Height, size, bold);
            }),
        ];
    }

    /// <summary>Compatibility forms (ligatures such as "ﬁ") become plain letters, and line breaks inside a block become "\n".</summary>
    private static string Normalize(string text) => text.Normalize(NormalizationForm.FormKC).Replace("\r\n", "\n", StringComparison.Ordinal);
}
