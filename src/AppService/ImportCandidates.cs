using System.Text.Json;
using System.Text.Json.Serialization;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Detection;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

public enum CandidateStatus { Pending, Accepted, AcceptedAsReference, Ignored }

/// <summary>
/// M4 D3: a detected candidate of one import job, awaiting review (SPEC I-02). <see cref="Candidate"/> is the proposal as
/// detected, and <see cref="Edited"/> the reviewer's version, if any; neither is content. Accepting turns it into a
/// draft revision through <see cref="CandidateQuarantine"/> only (ADR-004). Local only; never exported.
/// </summary>
public sealed record StoredCandidate
{
    public const int CurrentSchemaVersion = 1;

    public required Guid Id { get; init; }
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required Guid JobId { get; init; }
    public required DraftCandidate Candidate { get; init; }
    public DraftCandidate? Edited { get; init; }
    public CandidateStatus Status { get; init; } = CandidateStatus.Pending;
    /// <summary>The draft revision an accepted candidate became.</summary>
    public ContentReference? Draft { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>What review shows and acceptance uses: the edited version when there is one.</summary>
    [JsonIgnore]
    public DraftCandidate Current => Edited ?? Candidate;
}

/// <param name="Kind">Only this kind; null for every kind.</param>
/// <param name="MaxConfidence">Only candidates at or below this confidence (the unsure ones first, for example 0.7).</param>
public sealed record ImportCandidatesRequest(Guid JobId, int? Page = null, ContentKind? Kind = null, double? MinConfidence = null, double? MaxConfidence = null, CandidateStatus? Status = null);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// M4 D3: after extraction, detects candidates over the job's pages (text, not PDF bytes) and stores them for review.
    /// Candidates already reviewed stay; pending ones are replaced. Nothing becomes content here (ADR-004).
    /// </summary>
    private ImportJobRecord DetectCandidates(ImportJobRecord job)
    {
        var source = _store.FindSource(job.SourceId);
        if (source is null)
            return job;
        var end = job.ScopeEnd ?? int.MaxValue;
        var pages = _store.ListImportPages(job.Sha256)
            .Where(p => p.Page >= job.FirstPage && p.Page <= end && p.Error is null)
            .Select(p => new DetectionPage(p.Page, p.Text, p.Blocks, p.FromOcr))
            .ToList();
        var installed = _store.ListRevisions()
            .Where(r => r.Status == RevisionStatus.Published && r.RulesFamilies.Any(source.RulesFamilies.Contains))
            .Select(r => CandidateDetector.Key(r.Name))
            .ToHashSet(StringComparer.Ordinal);
        var detected = CandidateDetector.Detect(pages, new(source.Id, source.RulesFamilies, name => installed.Contains(CandidateDetector.Key(name))));

        var reviewed = _store.ListImportCandidates(job.Id).Where(c => c.Status != CandidateStatus.Pending)
            .Select(c => (c.Candidate.ProposedKind, CandidateDetector.Key(c.Candidate.ProposedName), c.Candidate.Page.Start)).ToHashSet();
        var now = _time.GetUtcNow();
        var fresh = detected.Where(c => !reviewed.Contains((c.ProposedKind, CandidateDetector.Key(c.ProposedName), c.Page.Start))).ToList();
        _store.InTransaction(() =>
        {
            _store.DeletePendingImportCandidates(job.Id);
            foreach (var candidate in fresh)
                _store.SaveImportCandidate(new StoredCandidate { Id = candidate.Id, JobId = job.Id, Candidate = candidate, UpdatedAt = now });
        });
        return job with { Candidates = reviewed.Count + fresh.Count };
    }

    /// <summary><c>import.candidates</c> (SPEC I-02): a job's candidates, filtered by page, kind, confidence and status, in page order. Writes nothing.</summary>
    public IReadOnlyList<StoredCandidate> ListCandidates(ImportCandidatesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ImportStatus(request.JobId);
        return
        [
            .. _store.ListImportCandidates(request.JobId)
                .Where(c => request.Page is not { } page || (c.Current.Page.Start <= page && (c.Current.Page.End ?? c.Current.Page.Start) >= page))
                .Where(c => request.Kind is not { } kind || c.Current.ProposedKind == kind)
                .Where(c => request.MinConfidence is not { } min || c.Current.Confidence >= min)
                .Where(c => request.MaxConfidence is not { } max || c.Current.Confidence <= max)
                .Where(c => request.Status is not { } status || c.Status == status)
                .OrderBy(c => c.Current.Page.Start).ThenBy(c => c.Current.ProposedName, StringComparer.CurrentCultureIgnoreCase),
        ];
    }
}
