using System.Text.Json;
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
/// <param name="Lines">The block's lines with their own boxes (M4 D3): tables print as columns, so rows are rebuilt from line positions.</param>
public sealed record TextBlock(string Text, double X, double Y, double Width, double Height, double FontSize, bool Bold, IReadOnlyList<TextLine>? Lines = null);

/// <summary>One line of a block, in PDF points.</summary>
public sealed record TextLine(string Text, double X, double Y, double Width, double Height);

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

    /// <summary>
    /// Characters per page, for each copy of the text a page carries: its text, its blocks' text, and their lines' text
    /// (review 2026-09-28: a cap on the page text alone left the blocks unbounded).
    /// </summary>
    public int MaxTextPerPage { get; init; } = 200_000;
    public int MaxBlocksPerPage { get; init; } = 5_000;
    public int MaxLinesPerPage { get; init; } = 20_000;
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
    IReadOnlyList<string> Uncertainties)
{
    /// <summary>Names the candidate refers to that are neither installed nor another candidate of the job (SPEC I-02), for example a spell a feature casts.</summary>
    public IReadOnlyList<string> UnresolvedReferences { get; init; } = [];

    /// <summary>What the detector read, by field (for example <c>level</c> → <c>2</c>), shown for review.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Fields the detector is unsure of (M4 D4): with <see cref="UnresolvedReferences"/>, they block acceptance until the
    /// candidate is edited or accepted as reference only.
    /// </summary>
    public IReadOnlyList<string> LowConfidenceFields { get; init; } = [];

    /// <summary>The entry's text for the draft (the description), when it differs from the excerpt.</summary>
    public string? Summary { get; init; }
}

public static class CandidateQuarantine
{
    /// <summary>
    /// Converts a candidate into a <see cref="RevisionStatus.Draft"/> revision. There is deliberately no
    /// overload that produces a published revision: publishing is a separate, user-confirmed command.
    /// </summary>
    public static ContentRevision ToDraftRevision(DraftCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var draft = new ContentRevision
        {
            ContentId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid(),
            Kind = candidate.ProposedKind,
            Name = candidate.ProposedName,
            RulesFamilies = candidate.RulesFamilies,
            Provenance = new Provenance(candidate.SourceId, candidate.Page),
            Status = RevisionStatus.Draft,
            Summary = candidate.Summary ?? candidate.Excerpt,
            Effects = candidate.ProposedEffects,
        };
        // Effect types added after v3 (spell, weapon, armor, …) are typed only inside a revision of their schema version
        // (ADR-003). A candidate stored or sent outside one carries them as unknown effects, whose "automation" lives in
        // their raw JSON, where "with" cannot reach it. Read the draft back so they are typed, then force every effect to
        // reference (M4 D4 review: without this, an imported weapon became automatic once its draft was re-read).
        var typed = JsonSerializer.Deserialize<ContentRevision>(JsonSerializer.Serialize(draft, RulesJson.Compact), RulesJson.Compact)!;
        return typed with { Effects = [.. typed.Effects.Select(e => e with { Automation = AutomationStatus.Reference })] };
    }

    /// <summary>The candidate with its effects typed as a current revision would type them (for review; still a proposal).</summary>
    public static DraftCandidate WithTypedEffects(DraftCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.ProposedEffects.OfType<UnknownEffect>().Any())
            return candidate;
        var holder = new ContentRevision
        {
            ContentId = Guid.Empty, RevisionId = Guid.Empty, Kind = candidate.ProposedKind, Name = candidate.ProposedName,
            RulesFamilies = candidate.RulesFamilies, Provenance = new Provenance(candidate.SourceId), Status = RevisionStatus.Draft,
            Effects = candidate.ProposedEffects,
        };
        var typed = JsonSerializer.Deserialize<ContentRevision>(JsonSerializer.Serialize(holder, RulesJson.Compact), RulesJson.Compact)!;
        return candidate with { ProposedEffects = typed.Effects };
    }
}
