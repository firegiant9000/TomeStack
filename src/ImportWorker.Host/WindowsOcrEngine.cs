using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using TomeStack.ImportWorker;
using PdfRenderDocument = Windows.Data.Pdf.PdfDocument;

namespace TomeStack.ImportWorker.Host;

/// <summary>
/// ADR-009 (b): OCR for a page without a text layer, offline and built into Windows. <c>Windows.Data.Pdf</c> renders
/// the page (it has no scripting support), and <c>Windows.Media.Ocr</c> reads the image in the user's profile
/// languages. Each recognized line becomes a block in PDF points. Windows OCR reports no confidence, so candidates from
/// OCR text carry an uncertainty instead (ADR-009). Runs only in the worker process.
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine, IDisposable
{
    /// <summary>The rendered image's longer side, in pixels (about 200 dpi for a letter page), within the OCR engine's limit.</summary>
    private const uint TargetLongSide = 2200;

    private readonly OcrEngine _engine;
    private string? _openPath;
    private PdfRenderDocument? _document;
    private IRandomAccessStream? _stream;

    private WindowsOcrEngine(OcrEngine engine) => _engine = engine;

    public bool Available => true;

    /// <summary>The engine for the user's languages, or null when Windows has no OCR language installed.</summary>
    public static WindowsOcrEngine? TryCreate()
    {
        try
        {
            return OcrEngine.TryCreateFromUserProfileLanguages() is { } engine ? new WindowsOcrEngine(engine) : null;
        }
        catch (Exception ex) when (ex is TypeLoadException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<TextBlock>?> RecognizeAsync(string path, int pageNumber, double pageWidth, double pageHeight, CancellationToken cancellationToken)
    {
        var document = await OpenAsync(path).ConfigureAwait(false);
        if (pageNumber < 1 || pageNumber > document.PageCount)
            return null;
        using var page = document.GetPage((uint)(pageNumber - 1));
        var longSide = Math.Min(TargetLongSide, OcrEngine.MaxImageDimension);
        var landscape = page.Size.Width > page.Size.Height;
        var options = new Windows.Data.Pdf.PdfPageRenderOptions();
        if (landscape)
            options.DestinationWidth = longSide;
        else
            options.DestinationHeight = longSide;

        using var image = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(image, options).AsTask(cancellationToken).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(image).AsTask(cancellationToken).ConfigureAwait(false);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken).ConfigureAwait(false);
        var result = await _engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);

        // Image pixels (top-left origin) to PDF points (bottom-left origin).
        var scaleX = pageWidth / bitmap.PixelWidth;
        var scaleY = pageHeight / bitmap.PixelHeight;
        var blocks = new List<TextBlock>();
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0)
                continue;
            var left = line.Words.Min(w => w.BoundingRect.X);
            var top = line.Words.Min(w => w.BoundingRect.Y);
            var right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
            var bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
            blocks.Add(new TextBlock(line.Text, left * scaleX, pageHeight - (bottom * scaleY), (right - left) * scaleX, (bottom - top) * scaleY, 0, false));
        }
        return blocks.Count == 0 ? null : blocks;
    }

    private async Task<PdfRenderDocument> OpenAsync(string path)
    {
        if (_document is not null && _openPath == path)
            return _document;
        _stream?.Dispose();
        _stream = File.OpenRead(path).AsRandomAccessStream();
        _document = await PdfRenderDocument.LoadFromStreamAsync(_stream);
        _openPath = path;
        return _document;
    }

    public void Dispose() => _stream?.Dispose();
}
