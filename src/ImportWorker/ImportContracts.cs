using TomeStack.RulesCore;

namespace TomeStack.ImportWorker;

/// <summary>
/// Import worker boundary (ARCHITECTURE "Import lifecycle", ADR-009). Extraction (M4 D1) turns a local PDF into page
/// text and layout; detection (M4 D3) turns page text into draft candidates. Nothing here can publish content.
/// </summary>
/// <param name="FirstPage">1-based; null is the first page.</param>
/// <param name="LastPage">1-based and inclusive; null is the last page.</param>
public sealed record PageScope(int? FirstPage = null, int? LastPage = null)
{
    public static PageScope WholeDocument { get; } = new();

    /// <summary>The pages of a document of <paramref name="pageCount"/> pages that this scope covers, clipped to the document.</summary>
    public (int First, int Last) Resolve(int pageCount) => (Math.Max(1, FirstPage ?? 1), Math.Min(pageCount, LastPage ?? pageCount));
}

/// <summary>A block of text in reading order, in PDF points from the page's bottom-left corner (ADR-009: page coordinates where available).</summary>
/// <param name="FontSize">The block's most common letter size, in points (0 for OCR text, which has none).</param>
/// <param name="Bold">Most letters come from a bold font (a heading hint for detection).</param>
public sealed record TextBlock(string Text, double X, double Y, double Width, double Height, double FontSize, bool Bold);

/// <summary>Something the extractor reports while it reads a document.</summary>
public abstract record ExtractionEvent;

/// <summary>The document opened; <paramref name="PageCount"/> pages exist (the scope may cover fewer).</summary>
public sealed record DocumentOpened(int PageCount) : ExtractionEvent;

/// <summary>
/// One page. <paramref name="Error"/> is a code (never extracted text) when the page could not be read; the text is then
/// empty, and the other pages still extract. <paramref name="FromOcr"/> means the page had no text layer and was read by OCR.
/// </summary>
/// <param name="Warnings">Codes such as <c>page.text-truncated</c>, <c>ocr.unavailable</c> or <c>page.no-text</c>.</param>
public sealed record ExtractedPage(
    int PageNumber,
    string Text,
    bool FromOcr,
    IReadOnlyList<TextBlock>? Blocks = null,
    double Width = 0,
    double Height = 0,
    IReadOnlyList<string>? Warnings = null,
    string? Error = null) : ExtractionEvent;

/// <summary>ADR-009 (c): the bounds on one extraction run. Every value is a hard limit, enforced before or while reading.</summary>
public sealed record ExtractionLimits
{
    public long MaxBytes { get; init; } = 1L << 30;
    public int MaxPages { get; init; } = 5_000;
    public int MaxTextPerPage { get; init; } = 200_000;
    public int MaxBlocksPerPage { get; init; } = 5_000;
    public TimeSpan PageTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan RunTimeout { get; init; } = TimeSpan.FromMinutes(60);
    /// <summary>The child's managed heap cap (<c>DOTNET_GCHeapHardLimit</c>).</summary>
    public long HeapHardLimit { get; init; } = 1L << 30;
    /// <summary>The parent kills the child above this working set.</summary>
    public long MaxWorkingSet { get; init; } = 3L << 29;

    public static ExtractionLimits Default { get; } = new();
}

/// <summary>
/// A failure of the whole run. <see cref="Code"/> is stable (<c>pdf.not-a-pdf</c>, <c>pdf.too-large</c>, <c>pdf.encrypted</c>,
/// <c>pdf.unreadable</c>, <c>pdf.too-many-pages</c>, <c>worker.timeout</c>, <c>worker.memory</c>, <c>worker.crashed</c>, …),
/// and the message never quotes the document.
/// </summary>
public sealed class ExtractionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Extracts text and layout from a local PDF (SPEC I-01). Implementations must not execute embedded scripts or fetch
/// remote resources (ADR-009). The first event is <see cref="DocumentOpened"/>; then one <see cref="ExtractedPage"/> per page
/// in the scope, in order. A document-level failure throws <see cref="ExtractionException"/>.
/// </summary>
public interface IDocumentExtractor
{
    IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, CancellationToken cancellationToken);
}

/// <summary>ADR-009 (b): reads a page that has no text layer. Implementations run only inside the worker process.</summary>
public interface IOcrEngine
{
    /// <summary>False when no OCR language is installed; pages without text then get <c>ocr.unavailable</c>.</summary>
    bool Available { get; }

    /// <summary>The page's text as blocks (lines) in PDF points, or null when nothing was recognized.</summary>
    Task<IReadOnlyList<TextBlock>?> RecognizeAsync(string path, int pageNumber, double pageWidth, double pageHeight, CancellationToken cancellationToken);
}

/// <summary>A proposed entity awaiting user review (SPEC I-02). Confidence is a UI hint, never permission to publish.</summary>
public sealed record DraftCandidate(
    Guid Id,
    Guid SourceId,
    PageRef Page,
    string Excerpt,
    ContentKind ProposedKind,
    string ProposedName,
    IReadOnlyList<string> RulesFamilies,
    IReadOnlyList<Effect> ProposedEffects,
    double Confidence,
    IReadOnlyList<string> Uncertainties);

public sealed record ImportJob(Guid Id, Guid SourceId, PageScope Scope, IReadOnlyList<DraftCandidate> Candidates, IReadOnlyList<string> Warnings);

public static class CandidateQuarantine
{
    /// <summary>
    /// Converts a candidate into a <see cref="RevisionStatus.Draft"/> revision. There is deliberately no
    /// overload that produces a published revision: publishing is a separate, user-confirmed command.
    /// </summary>
    public static ContentRevision ToDraftRevision(DraftCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new ContentRevision
        {
            ContentId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid(),
            Kind = candidate.ProposedKind,
            Name = candidate.ProposedName,
            RulesFamilies = candidate.RulesFamilies,
            Provenance = new Provenance(candidate.SourceId, candidate.Page),
            Status = RevisionStatus.Draft,
            Summary = candidate.Excerpt,
            Effects = [.. candidate.ProposedEffects.Select(e => e with { Automation = AutomationStatus.Reference })],
        };
    }
}
