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
    /// <summary>
    /// Ruling R29: the reviewer saved a rules family for this candidate (an edit that sent one). Absent in every row stored
    /// before it existed; omitted from the JSON while false so untouched rows keep their bytes. 0.5.0 does not read it
    /// and keeps it unchanged when it saves the row.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FamiliesReviewed { get; init; }
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
/// <param name="Page">An alias for <paramref name="FirstPage"/> and <paramref name="LastPage"/> both set to this page.</param>
/// <param name="FirstPage">Only candidates that reach this page or later.</param>
/// <param name="LastPage">Only candidates that start on or before this page.</param>
/// <param name="Offset">Candidates to skip (paged lists only); 0 or more.</param>
/// <param name="Limit">Candidates to return (paged lists only); 1 to <see cref="TomeStackApp.MaxCandidatePageSize"/>, 50 when absent.</param>
public sealed record ImportCandidatesRequest(
    Guid JobId, int? Page = null, ContentKind? Kind = null, double? MinConfidence = null, double? MaxConfidence = null, CandidateStatus? Status = null,
    int? FirstPage = null, int? LastPage = null, int? Offset = null, int? Limit = null);

/// <summary>One page of a job's filtered candidates, and how many the filter matches in all.</summary>
public sealed record CandidatePage(IReadOnlyList<StoredCandidate> Items, int Total);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// What detection may hold in the app's memory, in stored characters (about 4,000 typical pages). A job past it detects
    /// its first pages only and says so in its audit; importing the rest as a page range detects them (review 2026-09-28:
    /// extraction is bounded per page, and a 5,000-page PDF at the per-page limit would otherwise exhaust the app).
    /// </summary>
    public const long MaxDetectionChars = 64_000_000;

    /// <summary>The limits detection runs under; tests lower them to reach the limits with a small book.</summary>
    internal long DetectionCharBudget { get; set; } = MaxDetectionChars;

    internal int DetectionCandidateCap { get; set; } = CandidateDetector.MaxCandidates;

    /// <summary>
    /// M4 D3: after extraction, detects candidates over the job's pages (text, not PDF bytes) and stores them for review.
    /// Candidates already reviewed stay; pending ones are replaced. A candidate reviewed in an earlier import of the same
    /// PDF into the same source is not proposed again. Nothing becomes content here (ADR-004).
    /// </summary>
    private ImportJobRecord DetectCandidates(ImportJobRecord job, CancellationToken cancellationToken)
    {
        var source = _store.FindSource(job.SourceId);
        if (source is null)
            return job;
        var stored = _store.ListImportPagesForDetection(job.Sha256, job.FirstPage, job.ScopeEnd ?? int.MaxValue, DetectionCharBudget, out var stoppedBefore);
        if (stoppedBefore is { } page)
            _store.AddImportAudit(job.Id, _time.GetUtcNow(), "detection-limited", $"pages {job.FirstPage}-{page - 1} detected; import pages {page} on as a range to detect them");
        var pages = stored.Where(p => p.Error is null).Select(p => new DetectionPage(p.Page, "", p.Blocks, p.FromOcr)).ToList();
        var families = job.WrittenFor ?? source.RulesFamilies; // 2026-10-14 item 3: what the reviewer said the book is written for
        var installed = _store.ListRevisions()
            .Where(r => r.Status == RevisionStatus.Published && r.RulesFamilies.Any(families.Contains))
            .Select(r => CandidateDetector.Key(r.Name))
            .ToHashSet(StringComparer.Ordinal);
        var result = CandidateDetector.DetectWithLimit(pages, new(source.Id, families, name => installed.Contains(CandidateDetector.Key(name))), DetectionCandidateCap, cancellationToken);
        var detected = result.Candidates;

        static (ContentKind, string, int) Identity(DraftCandidate c) => (c.ProposedKind, CandidateDetector.Key(c.ProposedName), c.Page.Start);
        var reviewed = _store.ListImportCandidates(job.Id).Where(c => c.Status != CandidateStatus.Pending).Select(c => Identity(c.Candidate)).ToHashSet();
        var reviewedBefore = _store.ListImportJobs()
            .Where(j => j.Id != job.Id && j.SourceId == job.SourceId && j.Sha256 == job.Sha256)
            .SelectMany(j => _store.ListImportCandidates(j.Id))
            .Where(c => c.Status != CandidateStatus.Pending)
            .Select(c => Identity(c.Candidate))
            .ToHashSet();
        var now = _time.GetUtcNow();
        var fresh = detected.Where(c => !reviewed.Contains(Identity(c)) && !reviewedBefore.Contains(Identity(c))).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        _store.InTransaction(() =>
        {
            _store.DeletePendingImportCandidates(job.Id);
            foreach (var candidate in fresh)
                _store.SaveImportCandidate(new StoredCandidate { Id = candidate.Id, JobId = job.Id, Candidate = candidate, UpdatedAt = now });
        });
        // Item 13: the review list says when it is not the whole book. The flags are set each run (a resumed job re-detects).
        return job with
        {
            Candidates = reviewed.Count + fresh.Count,
            TruncatedCandidates = result.Truncated ? true : null,
            DetectionLimitedFromPage = stoppedBefore,
        };
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
        IReadOnlyList<string> families = [.. (request.RulesFamilies ?? current.RulesFamilies).Distinct(StringComparer.Ordinal)];
        // 2026-10-14 item 3: none is allowed here (other fields can be saved first); check and accept wait for a family.
        if (families.Any(f => !RulesFamilies.IsKnown(f)))
            throw new AppValidationException([new("candidate.family-invalid", "Choose supported rules families.")]);
        var sourceFamilies = _store.FindSource(ImportStatus(stored.JobId).SourceId)?.RulesFamilies ?? [];
        if (families.Any(f => !sourceFamilies.Contains(f)))
            throw new AppValidationException([new("import.family-not-in-source", $"A candidate can only be written for the families its source declares ({string.Join(", ", sourceFamilies)}).")]);
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
        // R29: sending a families set (even the current one) is the reviewer's answer; an edit that sends none leaves it as it was.
        var updated = stored with { Edited = edited, FamiliesReviewed = stored.FamiliesReviewed || request.RulesFamilies is not null, UpdatedAt = _time.GetUtcNow() };
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
        // Even as reference, an entry needs a family, and a double acceptance is refused: "reference only" lifts neither.
        var provenance = ProvenanceBlockers(stored); // once: it reads every candidate of every same-document job
        if (provenance is [var first, ..])
            throw new AppValidationException([first with { Message = $"{first.Message} Nothing was changed." }]);
        var check = Check(stored, request.AsReference, provenance);
        if (!request.AsReference && check.Blockers.Count > 0)
            throw new AppValidationException([new("candidate.needs-review", $"This candidate has {check.Blockers.Count} unsure field(s) or unresolved reference(s). Edit them, or accept it as reference only."), .. check.Blockers]);
        if (!check.Report.CanPublish)
            throw new AppValidationException([new("candidate.validation-failed", $"This candidate has {check.Report.Errors.Count} problem(s)."), .. check.Report.Errors]);

        var draft = DraftOf(stored, request.AsReference);
        StoredCandidate? accepted = null;
        // One transaction: a draft never exists without its candidate marked accepted, so a crash cannot lead to a second draft.
        _store.InTransaction(() =>
        {
            MarkImportDerived(draft.Provenance.SourceId); // M6 slice 1: PDF material now lives in this source
            accepted = stored with
            {
                Status = request.AsReference ? CandidateStatus.AcceptedAsReference : CandidateStatus.Accepted,
                Draft = SaveDraft(draft),
                UpdatedAt = _time.GetUtcNow(),
            };
            _store.SaveImportCandidate(accepted);
            _store.AddImportAudit(stored.JobId, _time.GetUtcNow(), request.AsReference ? "candidate-accepted-as-reference" : "candidate-accepted", stored.Current.ProposedKind.ToString().ToLowerInvariant());
        });
        return accepted!;
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

    private CandidateCheck Check(StoredCandidate stored, bool asReference, IReadOnlyList<Diagnostic>? provenance = null)
    {
        var current = stored.Current;
        var draft = DraftOf(stored, asReference);
        var report = ContentValidator.Validate(draft, _store);
        provenance ??= ProvenanceBlockers(stored);
        var blockers = new List<Diagnostic>(provenance);
        foreach (var field in current.LowConfidenceFields.Where(f => f != "rulesFamilies" || current.RulesFamilies.Count > 0))
            blockers.Add(new("candidate.low-confidence", $"The detector is unsure of '{field}'. Check it against the page and save your edit."));
        // Messages never quote the PDF's text; the review shows the names from the candidate itself.
        for (var i = 0; i < current.UnresolvedReferences.Count; i++)
            blockers.Add(new("candidate.unresolved-reference", $"Reference {i + 1} of {current.UnresolvedReferences.Count} is neither installed nor another candidate. Install or import it, or dismiss the reference."));
        return new(stored.Id, report, Dependencies(draft, current), blockers,
            CanAccept: stored.Status == CandidateStatus.Pending && blockers.Count == 0 && report.CanPublish,
            CanAcceptAsReference: stored.Status == CandidateStatus.Pending && provenance.Count == 0 && ContentValidator.Validate(DraftOf(stored, asReference: true), _store).CanPublish);
    }

    /// <summary>
    /// What "reference only" does not lift. Computed from what is stored, never stored itself (no migration):
    /// no rules family (2026-10-14 item 3; the empty set has its own blocker, so the unsure "rulesFamilies" field is not listed twice);
    /// an armour row or class table kept by a 0.5.0 job on a two-family source, which silently took srd-5.2.1 (ruling R28), until the
    /// reviewer saves a family; and an entry another reading of the same document already turned into a draft (ruling R27).
    /// </summary>
    private List<Diagnostic> ProvenanceBlockers(StoredCandidate stored)
    {
        var blockers = new List<Diagnostic>();
        if (stored.Status != CandidateStatus.Pending)
            return blockers;
        var current = stored.Current;
        var job = ImportStatus(stored.JobId);
        if (current.RulesFamilies.Count == 0)
            blockers.Add(new("import.family-needed", "Say which rules family this entry is written for."));
        else if (job.WrittenFor is null && !stored.FamiliesReviewed && IsLayoutNeutral(stored.Candidate)
            && (_store.FindSource(job.SourceId)?.RulesFamilies.Count ?? 0) > 1)
            blockers.Add(new("import.family-needed", "Say which rules family this entry is written for. It was read before TomeStack asked."));

        static (ContentKind, string, int) Identity(DraftCandidate c) => (c.ProposedKind, CandidateDetector.Key(c.ProposedName), c.Page.Start);
        var identity = Identity(current); // the effective values: renaming a twin is how a deliberate second copy is made (R27)
        var already = _store.ListImportJobs()
            .Where(j => j.Id != job.Id && j.SourceId == job.SourceId && j.Sha256 == job.Sha256)
            .SelectMany(j => _store.ListImportCandidates(j.Id))
            .Any(c => c.Status is CandidateStatus.Accepted or CandidateStatus.AcceptedAsReference && Identity(c.Candidate) == identity);
        if (already)
            blockers.Add(new("import.already-accepted", "This entry was already accepted from another reading of this document."));
        return blockers;
    }

    /// <summary>Armour rows and class tables carry no layout, so the detector cannot tell which rules family they use.</summary>
    private static bool IsLayoutNeutral(DraftCandidate candidate) =>
        candidate.ProposedKind == ContentKind.Class || candidate.ProposedEffects.Any(e => e is ArmorEffect);

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
        return [.. Filtered(request)];
    }

    /// <summary>The most candidates one page of <c>import.candidates</c> returns.</summary>
    public const int MaxCandidatePageSize = 200;

    /// <summary>The page size when the request names none.</summary>
    public const int DefaultCandidatePageSize = 50;

    /// <summary>
    /// <c>import.candidates</c> (tester feedback item 13): <see cref="ListCandidates"/> in pages. <c>Total</c> counts the
    /// whole filtered set, so the list can say "51-100 of 340". Writes nothing.
    /// </summary>
    public CandidatePage ListCandidatesPage(ImportCandidatesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Offset is < 0)
            throw new AppValidationException([new("import.offset-out-of-range", "The offset is 0 or more.")]);
        if (request.Limit is < 1 or > MaxCandidatePageSize)
            throw new AppValidationException([new("import.limit-out-of-range", $"A page holds 1 to {MaxCandidatePageSize} candidates.")]);
        ImportStatus(request.JobId);
        var all = Filtered(request).ToList();
        return new([.. all.Skip(request.Offset ?? 0).Take(request.Limit ?? DefaultCandidatePageSize)], all.Count);
    }

    private IEnumerable<StoredCandidate> Filtered(ImportCandidatesRequest request)
    {
        var first = request.FirstPage ?? request.Page;
        var last = request.LastPage ?? request.Page;
        return _store.ListImportCandidates(request.JobId)
            .Where(c => first is not { } from || (c.Current.Page.End ?? c.Current.Page.Start) >= from)
            .Where(c => last is not { } to || c.Current.Page.Start <= to)
            .Where(c => request.Kind is not { } kind || c.Current.ProposedKind == kind)
            .Where(c => request.MinConfidence is not { } min || c.Current.Confidence >= min)
            .Where(c => request.MaxConfidence is not { } max || c.Current.Confidence <= max)
            .Where(c => request.Status is not { } status || c.Status == status)
            .OrderBy(c => c.Current.Page.Start).ThenBy(c => c.Current.ProposedName, StringComparer.CurrentCultureIgnoreCase);
    }
}
