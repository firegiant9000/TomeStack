using TomeStack.RulesCore;

namespace TomeStack.ImportWorker;

/// <summary>
/// Import worker boundary (ARCHITECTURE "Import lifecycle"). M0 defines contracts only; extraction,
/// OCR and entity detection arrive in M2/M4. Nothing here can publish content.
/// </summary>
public sealed record PageScope(int? FirstPage = null, int? LastPage = null);

public sealed record ExtractedPage(int PageNumber, string Text, bool FromOcr);

/// <summary>Extracts text from a local document. Implementations must not execute embedded scripts or fetch remote resources.</summary>
public interface IDocumentExtractor
{
    IAsyncEnumerable<ExtractedPage> ExtractAsync(Stream document, PageScope scope, CancellationToken cancellationToken);
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
