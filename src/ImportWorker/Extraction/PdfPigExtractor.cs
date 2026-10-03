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
public sealed partial class PdfPigExtractor(IOcrEngine? ocr = null, ExtractionLimits? limits = null) : IDocumentExtractor
{
    private readonly ExtractionLimits _limits = limits ?? ExtractionLimits.Default;

    public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(scope);
        CheckFile(path, _limits);
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

    /// <summary>Refuses a missing file, one over <see cref="ExtractionLimits.MaxBytes"/>, and one without a PDF header, before PdfPig reads it.</summary>
    internal static void CheckFile(string path, ExtractionLimits limits)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new ExtractionException("pdf.missing", "The PDF file is missing.");
        if (info.Length > limits.MaxBytes)
            throw new ExtractionException("pdf.too-large", $"The PDF is larger than {limits.MaxBytes / (1024 * 1024)} MB.");
        Span<byte> header = stackalloc byte[5];
        int read;
        try
        {
            using var stream = File.OpenRead(path);
            read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked by another program or not readable: a code, never the path the exception names.
            throw new ExtractionException("pdf.unreadable", "The PDF could not be opened. Close it in other programs and check you can read it, then try again.");
        }
        if (read < header.Length || !header.SequenceEqual("%PDF-"u8))
            throw new ExtractionException("pdf.not-a-pdf", "The file is not a PDF.");
    }

    /// <summary>Opens the document, mapping every PdfPig failure to a code (<c>pdf.encrypted</c>, <c>pdf.unreadable</c>).</summary>
    internal static PdfDocument Open(string path)
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
                try
                {
                    blocks = await ocr.RecognizeAsync(path, number, page.Width, page.Height, cancellationToken).ConfigureAwait(false) ?? [];
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                {
                    // A page the renderer or OCR cannot read fails alone; the other pages still extract (review 2026-09-28).
                    return new ExtractedPage(number, "", false, Width: page.Width, Height: page.Height, Error: "page.ocr-failed");
                }
                fromOcr = true;
                if (blocks.Count == 0)
                    warnings.Add("page.no-text");
            }
            else
            {
                warnings.Add(ocr is null ? "page.no-text" : "ocr.unavailable");
            }
        }
        blocks = Bounded(blocks, warnings);
        var text = string.Join("\n\n", blocks.Select(b => b.Text));
        if (text.Length > _limits.MaxTextPerPage)
        {
            text = text[.._limits.MaxTextPerPage];
            if (!warnings.Contains("page.text-truncated"))
                warnings.Add("page.text-truncated");
        }
        return new ExtractedPage(number, text, fromOcr, blocks, page.Width, page.Height, warnings);
    }

    /// <summary>
    /// ADR-009 (c): at most <see cref="ExtractionLimits.MaxBlocksPerPage"/> blocks and <see cref="ExtractionLimits.MaxLinesPerPage"/>
    /// lines, and at most <see cref="ExtractionLimits.MaxTextPerPage"/> characters of block text and as many of line text,
    /// so what a page sends to the app is bounded, not only its joined text.
    /// </summary>
    private IReadOnlyList<TextBlock> Bounded(IReadOnlyList<TextBlock> blocks, List<string> warnings)
    {
        var budget = _limits.MaxTextPerPage;
        var kept = new List<TextBlock>(Math.Min(blocks.Count, _limits.MaxBlocksPerPage));
        int blockChars = 0, lineChars = 0, lineCount = 0;
        bool tooManyBlocks = false, textCut = false;
        foreach (var block in blocks)
        {
            if (kept.Count == _limits.MaxBlocksPerPage)
            {
                tooManyBlocks = true;
                break;
            }
            if (blockChars >= budget)
            {
                textCut = true;
                break;
            }
            var text = Cut(block.Text, budget - blockChars, ref textCut);
            blockChars += text.Length;
            List<TextLine>? lines = null;
            if (block.Lines is { } source)
            {
                lines = new(Math.Min(source.Count, _limits.MaxLinesPerPage - lineCount));
                foreach (var line in source)
                {
                    if (lineCount == _limits.MaxLinesPerPage || lineChars >= budget)
                    {
                        textCut = true;
                        break;
                    }
                    var lineText = Cut(line.Text, budget - lineChars, ref textCut);
                    lineChars += lineText.Length;
                    lineCount++;
                    lines.Add(ReferenceEquals(lineText, line.Text) ? line : line with { Text = lineText });
                }
            }
            kept.Add(ReferenceEquals(text, block.Text) && lines?.Count == block.Lines?.Count ? block : block with { Text = text, Lines = lines });
        }
        if (tooManyBlocks)
            warnings.Add("page.blocks-truncated");
        if (textCut)
            warnings.Add("page.text-truncated");
        return kept;
    }

    private static string Cut(string text, int room, ref bool cut)
    {
        if (text.Length <= room)
            return text;
        cut = true;
        return text[..room];
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
                var lines = block.TextLines.Select(l => new TextLine(Normalize(l.Text), l.BoundingBox.Left, l.BoundingBox.Bottom, l.BoundingBox.Width, l.BoundingBox.Height)).ToList();
                return new TextBlock(Normalize(block.Text), box.Left, box.Bottom, box.Width, box.Height, size, bold, lines);
            }),
        ];
    }

    /// <summary>
    /// Compatibility forms (ligatures such as "ﬁ") become plain letters, and line breaks inside a block become "\n".
    /// Soft hyphens are dropped, and the hyphen variants some PDFs print for one hyphen ("1st-­‐‐level")
    /// become one "-". Em and en dashes stay.
    /// </summary>
    internal static string Normalize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("­", "", StringComparison.Ordinal);
        return HyphenRun().Replace(normalized, "-");
    }

    [System.Text.RegularExpressions.GeneratedRegex("[-‐‑]{2,}|[‐‑]")]
    private static partial System.Text.RegularExpressions.Regex HyphenRun();
}
