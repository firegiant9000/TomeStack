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

/// <summary>
/// M4 D4 <c>import.candidate.edit</c>: the reviewer's version. Absent fields keep the current value. Saving it clears the
/// low-confidence flags (the reviewer has seen the fields). <paramref name="DismissReferences"/> names the unresolved
/// references the reviewer says are not needed (for example a spell that is only mentioned).
/// </summary>
public sealed record CandidateEditRequest(
    Guid CandidateId,
    string? Name = null,
    ContentKind? Kind = null,
    IReadOnlyList<string>? RulesFamilies = null,
    string? Summary = null,
    IReadOnlyList<Effect>? Effects = null,
    IReadOnlyList<string>? DismissReferences = null);

public sealed record CandidateRequest(Guid CandidateId);

/// <param name="AsReference">Accept as a reference-only entry: the text and page without the proposed effects.</param>
/// <param name="Confirm">Must be true: it creates a draft revision.</param>
public sealed record CandidateAcceptRequest(Guid CandidateId, bool AsReference = false, bool Confirm = false);

/// <summary>Something an accepted candidate depends on: its source, installed content it names, or a name found nowhere.</summary>
/// <param name="Kind"><c>source</c>, <c>content</c>, <c>missing-content</c> or <c>unresolved-name</c>.</param>
public sealed record CandidateDependency(string Kind, string Name, ContentReference? Reference = null);

/// <summary>
/// M4 D4 <c>import.candidate.check</c>: what accepting would give, without writing anything. <see cref="Report"/> is
/// <see cref="ContentValidator"/>'s on the draft the candidate would become. <see cref="Blockers"/> (low-confidence
/// fields, unresolved references) must be edited first, or the candidate accepted as reference only.
/// </summary>
public sealed record CandidateCheck(
    Guid CandidateId,
    ValidationReport Report,
    IReadOnlyList<CandidateDependency> Dependencies,
    IReadOnlyList<Diagnostic> Blockers,
    bool CanAccept,
    bool CanAcceptAsReference);

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

    /// <summary><c>import.candidate.check</c> (SPEC I-02, M4 D4): validation, dependencies and blockers of accepting; writes nothing.</summary>
    public CandidateCheck CheckCandidate(Guid candidateId)
    {
        var stored = FindCandidateOrThrow(candidateId);
        return Check(stored, asReference: false);
    }

    /// <summary><c>import.candidate.edit</c>: stores the reviewer's version of a pending candidate. It is still a proposal.</summary>
    public StoredCandidate EditCandidate(CandidateEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stored = FindCandidateOrThrow(request.CandidateId);
        RequirePending(stored);
        var current = stored.Current;
        var name = request.Name?.Trim() ?? current.ProposedName;
        if (name.Length is < 1 or > 200)
            throw new AppValidationException([new("candidate.name-invalid", "A name has 1 to 200 characters.")]);
        var families = request.RulesFamilies ?? current.RulesFamilies;
        if (families.Count == 0 || families.Any(f => !RulesFamilies.IsKnown(f)))
            throw new AppValidationException([new("candidate.family-invalid", "Choose at least one supported rules family.")]);
        var summary = request.Summary ?? current.Summary;
        if (summary is { Length: > 20_000 })
            throw new AppValidationException([new("candidate.summary-too-long", "The text has at most 20,000 characters.")]);
        var dismissed = request.DismissReferences ?? [];
        var edited = current with
        {
            ProposedName = name,
            ProposedKind = request.Kind ?? current.ProposedKind,
            RulesFamilies = families,
            ProposedEffects = request.Effects ?? current.ProposedEffects,
            Summary = summary,
            LowConfidenceFields = [],
            UnresolvedReferences = [.. current.UnresolvedReferences.Where(r => !dismissed.Contains(r, StringComparer.OrdinalIgnoreCase) && !IsInstalledName(r, families))],
        };
        var updated = stored with { Edited = edited, UpdatedAt = _time.GetUtcNow() };
        _store.InTransaction(() =>
        {
            _store.SaveImportCandidate(updated);
            _store.AddImportAudit(stored.JobId, _time.GetUtcNow(), "candidate-edited", $"{edited.ProposedKind.ToString().ToLowerInvariant()}, {dismissed.Count} references dismissed");
        });
        return updated;
    }

    /// <summary>
    /// <c>import.candidate.accept</c> (ADR-004): the candidate becomes a **draft** revision through
    /// <see cref="CandidateQuarantine.ToDraftRevision"/>, the only conversion, which forces every effect to reference.
    /// Refused without <c>confirm</c>, while blockers remain (unless as reference), or when validation reports an error.
    /// Publishing stays the separate, re-validating <c>content.publish</c>.
    /// </summary>
    public StoredCandidate AcceptCandidate(CandidateAcceptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("candidate.confirmation-required", "Accepting creates a draft entry. Confirm to accept; nothing was changed.")]);
        var stored = FindCandidateOrThrow(request.CandidateId);
        RequirePending(stored);
        var check = Check(stored, request.AsReference);
        if (!request.AsReference && check.Blockers.Count > 0)
            throw new AppValidationException([new("candidate.needs-review", $"This candidate has {check.Blockers.Count} unsure field(s) or unresolved reference(s). Edit them, or accept it as reference only."), .. check.Blockers]);
        if (!check.Report.CanPublish)
            throw new AppValidationException([new("candidate.validation-failed", $"This candidate has {check.Report.Errors.Count} problem(s)."), .. check.Report.Errors]);

        var draft = DraftOf(stored, request.AsReference);
        var reference = SaveDraft(draft);
        var accepted = stored with
        {
            Status = request.AsReference ? CandidateStatus.AcceptedAsReference : CandidateStatus.Accepted,
            Draft = reference,
            UpdatedAt = _time.GetUtcNow(),
        };
        _store.InTransaction(() =>
        {
            _store.SaveImportCandidate(accepted);
            _store.AddImportAudit(stored.JobId, _time.GetUtcNow(), request.AsReference ? "candidate-accepted-as-reference" : "candidate-accepted", stored.Current.ProposedKind.ToString().ToLowerInvariant());
        });
        return accepted;
    }

    /// <summary><c>import.candidate.ignore</c>: sets a pending candidate aside. Nothing is created.</summary>
    public StoredCandidate IgnoreCandidate(Guid candidateId)
    {
        var stored = FindCandidateOrThrow(candidateId);
        RequirePending(stored);
        var ignored = stored with { Status = CandidateStatus.Ignored, UpdatedAt = _time.GetUtcNow() };
        _store.InTransaction(() =>
        {
            _store.SaveImportCandidate(ignored);
            _store.AddImportAudit(stored.JobId, _time.GetUtcNow(), "candidate-ignored", stored.Current.ProposedKind.ToString().ToLowerInvariant());
        });
        return ignored;
    }

    private CandidateCheck Check(StoredCandidate stored, bool asReference)
    {
        var current = stored.Current;
        var draft = DraftOf(stored, asReference);
        var report = ContentValidator.Validate(draft, _store);
        var blockers = new List<Diagnostic>();
        foreach (var field in current.LowConfidenceFields)
            blockers.Add(new("candidate.low-confidence", $"The detector is unsure of '{field}'. Check it against the page and save your edit."));
        // Messages never quote the PDF's text; the review shows the names from the candidate itself.
        for (var i = 0; i < current.UnresolvedReferences.Count; i++)
            blockers.Add(new("candidate.unresolved-reference", $"Reference {i + 1} of {current.UnresolvedReferences.Count} is neither installed nor another candidate. Install or import it, or dismiss the reference."));
        return new(stored.Id, report, Dependencies(draft, current), blockers,
            CanAccept: stored.Status == CandidateStatus.Pending && blockers.Count == 0 && report.CanPublish,
            CanAcceptAsReference: stored.Status == CandidateStatus.Pending && ContentValidator.Validate(DraftOf(stored, asReference: true), _store).CanPublish);
    }

    /// <summary>The draft a candidate becomes: always through the quarantine, so a draft with reference-only effects (ADR-004).</summary>
    private static ContentRevision DraftOf(StoredCandidate stored, bool asReference)
    {
        var current = stored.Current;
        return CandidateQuarantine.ToDraftRevision(asReference ? current with { ProposedEffects = [] } : current);
    }

    private List<CandidateDependency> Dependencies(ContentRevision draft, DraftCandidate candidate)
    {
        var dependencies = new List<CandidateDependency>();
        var source = _store.FindSource(draft.Provenance.SourceId);
        dependencies.Add(new("source", source?.Title ?? "(missing source)"));
        var references = draft.Effects.OfType<GrantEffect>().Where(g => g.Content is not null).Select(g => g.Content!)
            .Concat(draft.Effects.OfType<ChoiceEffect>().SelectMany(c => c.Options))
            .Distinct();
        foreach (var reference in references)
        {
            var revision = _store.FindRevision(reference);
            dependencies.Add(revision is null ? new("missing-content", reference.RevisionId.ToString(), reference) : new("content", revision.Name, reference));
        }
        foreach (var contentId in draft.Effects.OfType<RollEffect>().Select(r => r.ResourceContent).OfType<Guid>().Distinct())
        {
            var revision = _store.ListRevisions(contentId).LastOrDefault();
            dependencies.Add(revision is null ? new("missing-content", contentId.ToString()) : new("content", revision.Name, revision.Reference));
        }
        dependencies.AddRange(candidate.UnresolvedReferences.Select(n => new CandidateDependency("unresolved-name", n)));
        return dependencies;
    }

    private bool IsInstalledName(string name, IReadOnlyList<string> families) =>
        _store.ListRevisions().Any(r => r.Status == RevisionStatus.Published && r.RulesFamilies.Any(families.Contains) && CandidateDetector.Key(r.Name) == CandidateDetector.Key(name));

    private StoredCandidate FindCandidateOrThrow(Guid candidateId) =>
        _store.FindImportCandidate(candidateId) ?? throw new AppValidationException([new("candidate.not-found", $"Candidate {candidateId} does not exist.")]);

    private static void RequirePending(StoredCandidate stored)
    {
        if (stored.Status != CandidateStatus.Pending)
            throw new AppValidationException([new("candidate.already-reviewed", $"This candidate is already {stored.Status switch { CandidateStatus.Ignored => "ignored", _ => "accepted" }}.")]);
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
