using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TomeStack.ImportWorker;
using TomeStack.AppService.Persistence;
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
    /// <summary>
    /// Item 11 (R39, ADR-016): the feature candidates of this job that a class candidate grants, each at a class level. Omitted
    /// when there are none, so every row stored before it existed reads as no links and untouched rows keep their bytes; no
    /// schema version change. 0.5.0 keeps it unchanged as an unknown field when it saves the row.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CandidateGrant>? GrantsCandidates { get; init; }
    public CandidateStatus Status { get; init; } = CandidateStatus.Pending;
    /// <summary>The draft revision an accepted candidate became.</summary>
    public ContentReference? Draft { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// R120: the compare-and-swap token of the stored row: SHA-256 (hex) of the stored JSON string, set by the store when it
    /// reads the row and sent back as <see cref="CandidateEditRequest.Expected"/>. Never written to the row (the store drops
    /// it on save), so it is absent from the stored JSON.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Etag { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>What review shows and acceptance uses: the edited version when there is one.</summary>
    [JsonIgnore]
    public DraftCandidate Current => Edited ?? Candidate;
}

/// <summary>A class candidate grants this feature candidate of the same job at this class level (<see cref="StoredCandidate.GrantsCandidates"/>).</summary>
public sealed record CandidateGrant(Guid CandidateId, int Level);

/// <summary>
/// M4 D4 <c>import.candidate.edit</c>: the reviewer's version. Absent fields keep the current value. Saving it clears the
/// low-confidence flags (the reviewer has seen the fields). <paramref name="DismissReferences"/> names the unresolved
/// references the reviewer says are not needed (for example a spell that is only mentioned).
/// <paramref name="Expected"/> (R120) is the candidate's <see cref="StoredCandidate.Etag"/> as the editor last saw it; the edit
/// is refused (<c>import.candidate-changed</c>) when the stored row has changed since, or when none is sent.
/// </summary>
public sealed record CandidateEditRequest(
    Guid CandidateId,
    string? Name = null,
    ContentKind? Kind = null,
    IReadOnlyList<string>? RulesFamilies = null,
    string? Summary = null,
    IReadOnlyList<Effect>? Effects = null,
    IReadOnlyList<string>? DismissReferences = null,
    string? Expected = null);

public sealed record CandidateRequest(Guid CandidateId);

/// <param name="AsReference">Accept as a reference-only entry: the text and page without the proposed effects.</param>
/// <param name="Confirm">Must be true: it creates a draft revision.</param>
/// <param name="ConfirmedEffectIds">
/// Item 11 (D34, ADR-016): the ids of the class candidate's proposed effects the reviewer checked against the page (the hit die;
/// the spell slot table when the class has one; any other). Those keep the automation proposed; every other effect is reference.
/// A class is not accepted as a draft without its hit die confirmed (<c>import.class-unconfirmed</c>). Only for a class, and not
/// together with <paramref name="AsReference"/>.
/// </param>
/// <param name="Expected">
/// R134: the candidate's <see cref="StoredCandidate.Etag"/> as the review panel last read it. The accept is refused
/// (<c>import.candidate-changed</c>) when the stored row has changed since, or when none is sent.
/// </param>
public sealed record CandidateAcceptRequest(Guid CandidateId, bool AsReference = false, bool Confirm = false, IReadOnlyList<string>? ConfirmedEffectIds = null, string? Expected = null);

/// <summary><c>import.candidate.ignore</c>; <paramref name="Expected"/> is the candidate's etag as the panel last read it (R134).</summary>
public sealed record CandidateIgnoreRequest(Guid CandidateId, string? Expected = null);

/// <param name="CandidateIds">At most <see cref="TomeStackApp.MaxAcceptMany"/>.</param>
/// <param name="Confirm">Must be true: it creates draft revisions.</param>
/// <param name="Expected">
/// R134: one expected etag per candidate id, as the panel last read them. A stored candidate with a stale or missing token
/// refuses the whole batch (<c>import.candidate-changed</c>) and nothing is written. An id that no longer exists is reported
/// as blocked, as before.
/// </param>
public sealed record CandidateAcceptManyRequest(IReadOnlyList<Guid> CandidateIds, bool AsReference = false, bool Confirm = false, IReadOnlyDictionary<Guid, string>? Expected = null);

/// <summary>A candidate that <c>import.candidate.acceptMany</c> skipped, and why (diagnostic codes only).</summary>
public sealed record BlockedCandidate(Guid CandidateId, IReadOnlyList<string> Codes);

public sealed record AcceptManyResult(IReadOnlyList<StoredCandidate> Accepted, IReadOnlyList<BlockedCandidate> Blocked);

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
    bool CanAcceptAsReference)
{
    /// <summary>Informational lines that do not block (ruling R66: linked features that are ignored and will not be granted).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Item 11 (fix round 1): every feature a class grants, by name, at its lowest level, with its review status.</summary>
    public IReadOnlyList<CandidateLinkSummary> Linked { get; init; } = [];
}

/// <summary>
/// A feature a class candidate grants: a linked candidate of the same job (<paramref name="CandidateId"/> and its
/// <paramref name="Status"/>), or the one installed published feature of that name (<paramref name="Installed"/>, ruling R67).
/// </summary>
public sealed record CandidateLinkSummary(Guid? CandidateId, string Name, int Level, CandidateStatus? Status, bool Installed);

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

    /// <summary>Test seam (fix round 1, item 7): runs after an accept (or accept-many) is planned and checked, just before its writes.</summary>
    internal Action? BeforeAcceptWrites { get; set; }

    /// <summary>Test seam (Task 8a fix round 1, M6): runs after an edit's first check, just before its transaction.</summary>
    internal Action? BeforeEditWrites { get; set; }

    /// <summary>Test seam (Task 8a fix round 1, M2): runs after an ignore reads the candidate, just before its transaction.</summary>
    internal Action? BeforeIgnoreWrites { get; set; }

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
        var features = InstalledFeaturesByName(families, source.Id);
        var result = CandidateDetector.DetectWithLimit(pages,
            new(source.Id, families, name => installed.Contains(CandidateDetector.Key(name)), name => features.TryGetValue(CandidateDetector.Key(name), out var matches) && matches.Count == 1),
            DetectionCandidateCap, cancellationToken);
        var detected = result.Candidates;

        static (ContentKind, string, int) Identity(DraftCandidate c) => CandidateIdentity(c); // one function for detection and accept (I1)
        var reviewed = _store.ListImportCandidates(job.Id).Where(c => c.Status != CandidateStatus.Pending).Select(c => Identity(c.Candidate)).ToHashSet();
        var reviewedBefore = _store.ListImportJobs()
            .Where(j => j.Id != job.Id && j.SourceId == job.SourceId && j.Sha256 == job.Sha256)
            .SelectMany(j => _store.ListImportCandidates(j.Id))
            .Where(c => c.Status != CandidateStatus.Pending)
            .Select(c => Identity(c.Candidate))
            .ToHashSet();
        var now = _time.GetUtcNow();
        var fresh = detected.Where(c => !reviewed.Contains(Identity(c)) && !reviewedBefore.Contains(Identity(c))).ToList();
        // Item 11 (R39): a link to a feature this job already reviewed points at the stored one (its draft is pinned at accept).
        var kept = _store.ListImportCandidates(job.Id).Where(c => c.Status != CandidateStatus.Pending).GroupBy(c => Identity(c.Candidate)).ToDictionary(g => g.Key, g => g.First().Id);
        var freshIds = fresh.Select(c => c.Id).ToHashSet();
        var detectedById = detected.ToDictionary(c => c.Id);
        Guid? Target(Guid detectedId) => freshIds.Contains(detectedId) ? detectedId : kept.TryGetValue(Identity(detectedById[detectedId]), out var existing) ? existing : null;
        var links = result.Links
            .Where(l => freshIds.Contains(l.ClassId))
            .Select(l => (l.ClassId, l.FeatureId, Target: Target(l.FeatureId), l.Level))
            .ToList();
        var grants = links
            .Where(l => l.Target is not null)
            .GroupBy(l => l.ClassId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CandidateGrant>)[.. g.OrderBy(l => l.Level).Select(l => new CandidateGrant(l.Target!.Value, l.Level))]);
        // Ruling R68: a link whose feature was reviewed in an earlier import of the document is not dropped silently (and not
        // linked across jobs): the class says how many, by count (an uncertainty never quotes the document).
        var earlier = links.Where(l => l.Target is null).GroupBy(l => l.ClassId).ToDictionary(g => g.Key, g => g.Select(l => l.FeatureId).Distinct().Count());
        static DraftCandidate WithEarlier(DraftCandidate candidate, int count) => count == 0 ? candidate : candidate with
        {
            Uncertainties = [.. candidate.Uncertainties, count == 1
                ? "1 table feature was reviewed in an earlier import and is not linked: add it in the studio."
                : $"{count} table features were reviewed in an earlier import and are not linked: add them in the studio."],
        };
        cancellationToken.ThrowIfCancellationRequested();
        _store.InTransaction(() =>
        {
            _store.DeletePendingImportCandidates(job.Id);
            foreach (var candidate in fresh)
                _store.SaveImportCandidate(new StoredCandidate { Id = candidate.Id, JobId = job.Id, Candidate = WithEarlier(candidate, earlier.GetValueOrDefault(candidate.Id)), GrantsCandidates = grants.GetValueOrDefault(candidate.Id), UpdatedAt = now });
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
        RequireUnchanged(stored, request.Expected);
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
        StoredCandidate? saved = null;
        BeforeEditWrites?.Invoke();
        _store.InTransaction(() =>
        {
            // R120: the check and the write share one transaction (and the store lock), so no other writer can land between them.
            RequireUnchanged(FindCandidateOrThrow(request.CandidateId), request.Expected);
            _store.SaveImportCandidate(updated);
            _store.AddImportAudit(stored.JobId, _time.GetUtcNow(), "candidate-edited", $"{edited.ProposedKind.ToString().ToLowerInvariant()}, {dismissed.Count} references dismissed");
            saved = _store.FindImportCandidate(updated.Id);
        });
        return saved ?? updated;
    }

    /// <summary>R120: refuses an edit made from a state other than the stored one (or from none). Says nothing about the contents.</summary>
    private static void RequireUnchanged(StoredCandidate stored, string? expected, bool forEdit = true)
    {
        if (expected is null || stored.Etag is null || !string.Equals(expected, stored.Etag, StringComparison.Ordinal))
            throw new AppValidationException([new("import.candidate-changed", forEdit
                ? "This candidate changed since it was opened. Reload it and edit again; nothing was changed."
                : "This candidate changed since you opened it. Review it again; nothing was changed.")]);
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
        RequireUnchanged(stored, request.Expected, forEdit: false); // R134: the caller's token, as for an edit (R120)
        // Even as reference, an entry needs a family, and a double acceptance is refused: "reference only" lifts neither.
        var provenance = ProvenanceBlockers(stored); // once: it reads every candidate of every same-document job
        if (provenance is [var first, ..])
            throw new AppValidationException([first with { Message = $"{first.Message} Nothing was changed." }]);
        var confirmed = ConfirmedOf(stored, request.AsReference, request.ConfirmedEffectIds);
        var plan = PlanOf(stored, request.AsReference, confirmed);
        var check = Check(stored, request.AsReference, provenance, plan);
        if (!request.AsReference && check.Blockers.Count > 0)
            throw new AppValidationException([new("candidate.needs-review", $"This candidate has {check.Blockers.Count} unsure field(s), unresolved reference(s) or linked feature(s) to settle. Edit them, or accept it as reference only."), .. check.Blockers]);
        if (!check.Report.CanPublish)
            throw new AppValidationException([new("candidate.validation-failed", $"This candidate has {check.Report.Errors.Count} problem(s)."), .. check.Report.Errors]);
        if (ClassUnconfirmed(stored, request.AsReference, confirmed) is { } unconfirmed)
            throw new AppValidationException([unconfirmed]);

        BeforeAcceptWrites?.Invoke();
        StoredCandidate? accepted = null;
        // One transaction: a draft never exists without its candidate marked accepted, so a crash cannot lead to a second draft.
        // The linked feature drafts are written first, so the class draft's grants pin drafts that exist (ADR-016).
        _store.InTransaction(() =>
        {
            // The candidate is read again here, so the accept acts on what is stored at commit time: reviewed since the plan
            // was made, or edited (the same compare-and-swap as an edit, R120), it is refused and nothing is written.
            var current = FindCandidateOrThrow(request.CandidateId);
            RequirePending(current);
            RequireUnchanged(current, request.Expected, forEdit: false);
            stored = current;
            // Fix round 1, item 7: the plan read each linked feature's status before the transaction; one reviewed since then
            // (accepted, ignored) would leave a second draft or a pin at nothing, so the whole accept is refused instead.
            if (plan.Linked.Any(l => l.Candidate is { } planned && _store.FindImportCandidate(planned.Id)?.Status != planned.Status))
                throw new AppValidationException([new("candidate.linked-changed", "A feature this class grants was reviewed while it was being accepted. Check the class again; nothing was changed.")]);
            // Task 8a M1: one edited since the plan (status unchanged) would be saved Accepted from the stale row and the edit lost.
            if (plan.Linked.Any(l => l.Candidate is { } planned && !string.Equals(_store.FindImportCandidate(planned.Id)?.Etag, planned.Etag, StringComparison.Ordinal)))
                throw new AppValidationException([new("import.candidate-changed", "A feature this class grants changed while it was being accepted. Check the class again; nothing was changed.")]);
            foreach (var feature in plan.Linked.Where(l => l.NewDraft is not null))
                WriteAccepted(feature.Candidate!, feature.NewDraft!, asReference: false);
            accepted = WriteAccepted(stored, plan.Draft, request.AsReference);
            accepted = _store.FindImportCandidate(accepted.Id) ?? accepted; // the row as stored, with its fresh Etag
        });
        return accepted!;
    }

    /// <summary>
    /// The confirmed effect ids of an accept, validated: they belong to a class that is accepted as a draft, and each names one
    /// of its proposed effects. Unknown ids are refused (nothing is silently dropped).
    /// </summary>
    private static IReadOnlyCollection<string> ConfirmedOf(StoredCandidate stored, bool asReference, IReadOnlyList<string>? ids)
    {
        if (ids is null or { Count: 0 })
            return [];
        if (asReference || stored.Current.ProposedKind != ContentKind.Class)
            throw new AppValidationException([new("candidate.confirm-unsupported", "Only a class accepted as a draft takes confirmed effects. Nothing was changed.")]);
        var known = CandidateQuarantine.WithTypedEffects(stored.Current).ProposedEffects.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Any(id => !known.Contains(id)))
            throw new AppValidationException([new("candidate.confirm-unknown", "A confirmed effect is not one of this candidate's effects. Nothing was changed.")]);
        return [.. ids.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// ADR-016: a class accepted as a draft needs its hit die confirmed, and its spell slot table too when it has one. Without
    /// them the draft would be a class whose every effect is reference, which is not a usable class.
    /// </summary>
    private static Diagnostic? ClassUnconfirmed(StoredCandidate stored, bool asReference, IReadOnlyCollection<string> confirmed)
    {
        if (asReference || stored.Current.ProposedKind != ContentKind.Class)
            return null;
        var effects = CandidateQuarantine.WithTypedEffects(stored.Current).ProposedEffects;
        var hitDieConfirmed = effects.OfType<HitDieEffect>().Any(e => confirmed.Contains(e.Id));
        var slotsConfirmed = effects.OfType<SpellcastingEffect>().All(e => confirmed.Contains(e.Id));
        if (hitDieConfirmed && slotsConfirmed)
            return null;
        return new("import.class-unconfirmed", "Confirm the hit die (and the spell slot table, when the class has one) against the page before accepting this class, or accept it as reference only. Nothing was changed.");
    }

    /// <summary>
    /// A feature a class grants, with the draft it will be (or already is) and the reference the class pins: a linked candidate
    /// of the same job, or (<paramref name="Candidate"/> null) the one installed published feature of that name (ruling R67).
    /// </summary>
    private sealed record LinkedFeature(StoredCandidate? Candidate, string Name, int Level, ContentRevision? NewDraft, ContentReference? Pin);

    /// <summary>What accepting a candidate would write: the class (or other) draft, and the linked feature drafts it grants.</summary>
    /// <param name="Unlinked">Features-column names neither linked to a candidate nor pinned to one installed match: not granted, counted in a note.</param>
    private sealed record AcceptPlan(ContentRevision Draft, IReadOnlyList<LinkedFeature> Linked, int Unlinked = 0);

    /// <summary>
    /// The draft of a candidate and, for a class accepted as a draft, the features it grants. A linked candidate already
    /// accepted is pinned at its content's newest published revision, or its newest draft when none is published (fix round 1,
    /// item 1: it may have been published or re-drafted since); a pending one gets a draft built now (written before the
    /// class); an ignored one is not granted (ruling R66). A Features-column name with no linked candidate is granted from the
    /// one installed published feature of that name in the class's families, outside this source (R67). Each feature is
    /// granted once, at the lowest level the table lists it.
    /// </summary>
    private AcceptPlan PlanOf(StoredCandidate stored, bool asReference, IReadOnlyCollection<string> confirmed)
    {
        var current = stored.Current;
        if (asReference)
            return new(DraftOf(stored, true), []);
        var classDraft = CandidateQuarantine.ToDraftRevision(current, confirmed);
        if (current.ProposedKind != ContentKind.Class)
            return new(classDraft, []);
        var linked = new List<LinkedFeature>();
        foreach (var group in (stored.GrantsCandidates ?? []).GroupBy(g => g.CandidateId))
        {
            if (_store.FindImportCandidate(group.Key) is not { } feature || feature.JobId != stored.JobId)
                continue;
            var level = group.Min(g => g.Level);
            var name = feature.Current.ProposedName;
            // A feature that names no rules family (a stub read from a two-family book) is written for the class's: the reviewer answers once.
            if (feature.Status == CandidateStatus.Pending && feature.Current.RulesFamilies.Count == 0 && current.RulesFamilies.Count > 0)
                feature = feature with { Edited = feature.Current with { RulesFamilies = current.RulesFamilies }, FamiliesReviewed = true };
            if (feature.Status is CandidateStatus.Accepted or CandidateStatus.AcceptedAsReference && feature.Draft is { } accepted)
            {
                linked.Add(new(feature, name, level, null, NewestOf(accepted.ContentId) ?? accepted));
            }
            else if (feature.Status == CandidateStatus.Pending)
            {
                var draft = DraftOf(feature, asReference: false);
                linked.Add(new(feature, name, level, draft, draft.Reference));
            }
            else
            {
                linked.Add(new(feature, name, level, null, null)); // ignored: not granted (R66)
            }
        }
        // The link was made on the detected name: a reviewer's rename of the feature must not cut it off from its table entry.
        var linkedNames = linked.Select(l => CandidateDetector.Key(l.Candidate?.Candidate.ProposedName ?? l.Name)).ToHashSet(StringComparer.Ordinal);
        var installed = InstalledFeaturesByName(current.RulesFamilies, current.SourceId);
        var earlier = ReviewedInEarlierJobs(stored.JobId);
        var unlinked = 0;
        foreach (var (name, level) in TableFeatures(current).Where(f => !linkedNames.Contains(CandidateDetector.Key(f.Name))))
        {
            if (installed.TryGetValue(CandidateDetector.Key(name), out var matches) && matches is [var only])
                linked.Add(new(null, only.Name, level, null, only.Reference));
            else if (!earlier.Contains(CandidateDetector.Key(name)))
                unlinked++; // no match, or several, at accept time: not granted, and the check says so (R68 names are counted by the class's uncertainty)
        }
        var used = new HashSet<string>(classDraft.Effects.Select(e => e.Id), StringComparer.Ordinal);
        var effects = new List<Effect>(classDraft.Effects);
        foreach (var feature in linked.Where(l => l.Pin is not null).OrderBy(l => l.Level).ThenBy(l => l.Name, StringComparer.Ordinal))
        {
            var slug = GrantSlug(feature.Name);
            var id = $"feature-{slug}";
            for (var n = 2; !used.Add(id); n++)
                id = $"feature-{slug}-{n}";
            effects.Add(new GrantEffect { Id = id, Grant = GrantKind.Content, Content = feature.Pin, Level = feature.Level, Text = null });
        }
        return new(classDraft with { Effects = effects }, linked, unlinked);
    }

    /// <summary>
    /// R68: name keys of the features reviewed in an earlier import of this job's document into its source (as detection
    /// counts them), which the class's uncertainty already reports.
    /// </summary>
    private HashSet<string> ReviewedInEarlierJobs(Guid jobId)
    {
        var jobs = _store.ListImportJobs();
        if (jobs.FirstOrDefault(j => j.Id == jobId) is not { } job)
            return [];
        return jobs
            .Where(j => j.Id != job.Id && j.SourceId == job.SourceId && j.Sha256 == job.Sha256)
            .SelectMany(j => _store.ListImportCandidates(j.Id))
            .Where(c => c.Status != CandidateStatus.Pending && c.Candidate.ProposedKind == ContentKind.Feature)
            .Select(c => CandidateDetector.Key(c.Candidate.ProposedName))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The newest published revision of a content, or its newest draft when none is published (insertion order).</summary>
    private ContentReference? NewestOf(Guid contentId)
    {
        var revisions = _store.RevisionsOf(contentId).ToList();
        return (revisions.LastOrDefault(r => r.Status == RevisionStatus.Published) ?? revisions.LastOrDefault())?.Reference;
    }

    /// <summary>
    /// R67: the installed published features in these families, outside <paramref name="exceptSource"/> (no cross-job linking
    /// within a source, R68), by name key: the newest published revision of each content.
    /// </summary>
    private Dictionary<string, List<ContentRevision>> InstalledFeaturesByName(IReadOnlyList<string> families, Guid exceptSource) =>
        _store.ListRevisionsInOrder()
            .Where(r => r.Kind == ContentKind.Feature && r.Status == RevisionStatus.Published && r.Provenance.SourceId != exceptSource && r.RulesFamilies.Any(families.Contains))
            .GroupBy(r => r.ContentId).Select(g => g.Last())
            .GroupBy(r => CandidateDetector.Key(r.Name))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    /// <summary>The linkable names of a class candidate's Features column (its "level N" fields), each at its lowest level.</summary>
    private static IEnumerable<(string Name, int Level)> TableFeatures(DraftCandidate candidate) =>
        candidate.Fields
            .Select(f => (f.Value, Level: f.Key.StartsWith("level ", StringComparison.Ordinal) && int.TryParse(f.Key["level ".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var level) ? level : 0))
            .Where(f => f.Level > 0)
            .SelectMany(f => f.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(name => (Name: name, f.Level)))
            .Where(f => CandidateDetector.IsLinkable(f.Name))
            .GroupBy(f => CandidateDetector.Key(f.Name))
            .Select(g => g.MinBy(f => f.Level));

    private static string GrantSlug(string name)
    {
        var slug = new string([.. name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Length == 0 ? "feature" : slug.Length > 60 ? slug[..60] : slug;
    }

    /// <summary>The writes of one acceptance; call inside the transaction.</summary>
    private StoredCandidate WriteAccepted(StoredCandidate stored, ContentRevision draft, bool asReference)
    {
        MarkImportDerived(draft.Provenance.SourceId); // M6 slice 1: PDF material now lives in this source
        var accepted = stored with
        {
            Status = asReference ? CandidateStatus.AcceptedAsReference : CandidateStatus.Accepted,
            Draft = SaveDraft(draft),
            UpdatedAt = _time.GetUtcNow(),
        };
        _store.SaveImportCandidate(accepted);
        _store.AddImportAudit(stored.JobId, _time.GetUtcNow(), asReference ? "candidate-accepted-as-reference" : "candidate-accepted", stored.Current.ProposedKind.ToString().ToLowerInvariant());
        return accepted;
    }

    /// <summary>The most candidates one <c>import.candidate.acceptMany</c> takes.</summary>
    public const int MaxAcceptMany = 200;

    /// <summary>
    /// <c>import.candidate.acceptMany</c> (tester feedback item 9, D33): <see cref="AcceptCandidate"/> for each id, in one
    /// transaction. Each goes through the same checks (provenance, blockers, validation); a candidate that fails any is
    /// returned with its codes and skipped, never forced, and the rest are accepted. The provenance check runs once per
    /// candidate and inside the transaction, so two twins in one request do not both pass. Messages never quote the document.
    /// </summary>
    public AcceptManyResult AcceptMany(CandidateAcceptManyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("candidate.confirmation-required", "Accepting creates draft entries. Confirm to accept; nothing was changed.")]);
        var ids = (request.CandidateIds ?? []).Distinct().ToList();
        if (ids.Count is < 1 or > MaxAcceptMany)
            throw new AppValidationException([new("import.limit-out-of-range", $"Accept 1 to {MaxAcceptMany} candidates at a time.")]);
        var accepted = new List<StoredCandidate>();
        var blocked = new List<BlockedCandidate>();
        var acceptedCache = new Dictionary<(Guid Source, string Sha), List<(Guid JobId, (ContentKind, string, int) Identity)>>();
        // R134: one expected etag per id; any stale or missing one refuses the whole batch before anything is written.
        // Checked here and again inside the transaction (the same compare-and-swap as an edit, R120).
        void RequireAllUnchanged()
        {
            foreach (var id in ids)
                if (_store.FindImportCandidate(id) is { } row)
                    RequireUnchanged(row, request.Expected is { } expected && expected.TryGetValue(id, out var token) ? token : null, forEdit: false);
        }
        RequireAllUnchanged();
        BeforeAcceptWrites?.Invoke();
        _store.InTransaction(() =>
        {
            RequireAllUnchanged();
            foreach (var id in ids)
            {
                if (_store.FindImportCandidate(id) is not { } stored)
                {
                    blocked.Add(new(id, ["candidate.not-found"]));
                    continue;
                }
                if (stored.Status != CandidateStatus.Pending)
                {
                    blocked.Add(new(id, ["candidate.already-reviewed"]));
                    continue;
                }
                var provenance = ProvenanceBlockers(stored, acceptedCache); // once per candidate; the document's accepted identities are read once
                var check = Check(stored, request.AsReference, provenance);
                var codes = new List<string>(provenance.Select(p => p.Code));
                if (provenance.Count == 0)
                {
                    if (!request.AsReference)
                        codes.AddRange(check.Blockers.Select(b => b.Code));
                    if (!check.Report.CanPublish)
                        codes.Add("candidate.validation-failed");
                    // ADR-016: a bulk accept confirms nothing, so a class is never accepted here with every effect reference
                    // (and never with the features it links left behind); it goes through import.candidate.accept.
                    if (ClassUnconfirmed(stored, request.AsReference, []) is { } unconfirmed)
                        codes.Add(unconfirmed.Code);
                }
                if (codes.Count > 0)
                {
                    blocked.Add(new(id, [.. codes.Distinct()]));
                    continue;
                }
                var written = WriteAccepted(stored, DraftOf(stored, request.AsReference), request.AsReference);
                accepted.Add(_store.FindImportCandidate(written.Id) ?? written); // the row as stored, with its fresh Etag
                AcceptedOf(acceptedCache, ImportStatus(stored.JobId)).Add((stored.JobId, CandidateIdentity(written.Candidate)));
            }
        });
        return new(accepted, blocked);
    }

    /// <summary><c>import.candidate.ignore</c>: sets a pending candidate aside. Nothing is created.</summary>
    public StoredCandidate IgnoreCandidate(CandidateIgnoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var candidateId = request.CandidateId;
        var stored = FindCandidateOrThrow(candidateId);
        RequirePending(stored);
        RequireUnchanged(stored, request.Expected, forEdit: false); // R134
        BeforeIgnoreWrites?.Invoke();
        StoredCandidate? ignored = null;
        _store.InTransaction(() =>
        {
            // Task 8a M2: read again inside the transaction, so a concurrent edit or accept is refused instead of overwritten.
            var current = FindCandidateOrThrow(candidateId);
            RequirePending(current);
            RequireUnchanged(current, request.Expected, forEdit: false);
            _store.SaveImportCandidate(current with { Status = CandidateStatus.Ignored, UpdatedAt = _time.GetUtcNow() });
            _store.AddImportAudit(current.JobId, _time.GetUtcNow(), "candidate-ignored", current.Current.ProposedKind.ToString().ToLowerInvariant());
            ignored = _store.FindImportCandidate(candidateId);
        });
        return ignored!;
    }

    private CandidateCheck Check(StoredCandidate stored, bool asReference, IReadOnlyList<Diagnostic>? provenance = null, AcceptPlan? plan = null)
    {
        var current = stored.Current;
        plan ??= PlanOf(stored, asReference, []);
        var draft = plan.Draft;
        var batch = plan.Linked.Where(l => l.NewDraft is not null).Select(l => l.NewDraft!).ToList();
        var report = ContentValidator.Validate(draft, _store, batch);
        provenance ??= ProvenanceBlockers(stored);
        var blockers = new List<Diagnostic>(provenance);
        if (!asReference)
            blockers.AddRange(LinkedBlockers(plan));
        foreach (var field in current.LowConfidenceFields.Where(f => f != "rulesFamilies" || current.RulesFamilies.Count > 0))
            blockers.Add(new("candidate.low-confidence", $"The detector is unsure of '{field}'. Check it against the page and save your edit."));
        // Messages never quote the PDF's text; the review shows the names from the candidate itself.
        for (var i = 0; i < current.UnresolvedReferences.Count; i++)
            blockers.Add(new("candidate.unresolved-reference", $"Reference {i + 1} of {current.UnresolvedReferences.Count} is neither installed nor another candidate. Install or import it, or dismiss the reference."));
        // R66: an ignored linked feature is not granted; that is said, not blocked.
        var ignored = plan.Linked.Count(l => l.Candidate?.Status == CandidateStatus.Ignored);
        var notes = new List<string>();
        if (ignored > 0)
            notes.Add(ignored == 1 ? "1 linked feature is ignored and will not be granted." : $"{ignored} linked features are ignored and will not be granted.");
        // Fix round 2: a table feature with no link and no single installed match is counted, never named.
        if (plan.Unlinked > 0)
            notes.Add(plan.Unlinked == 1 ? "1 table feature is not linked and will not be granted; add it in the studio." : $"{plan.Unlinked} table features are not linked and will not be granted; add them in the studio.");
        // A class accepted as reference grants nothing, so its check lists what accepting it as a draft would grant.
        var linkedPlan = asReference && current.ProposedKind == ContentKind.Class ? PlanOf(stored, asReference: false, []) : plan;
        return new(stored.Id, report, Dependencies(draft, current, batch), blockers,
            CanAccept: stored.Status == CandidateStatus.Pending && blockers.Count == 0 && report.CanPublish,
            CanAcceptAsReference: stored.Status == CandidateStatus.Pending && provenance.Count == 0 && ContentValidator.Validate(DraftOf(stored, asReference: true), _store).CanPublish)
        {
            Notes = asReference ? [] : notes,
            Linked = [.. linkedPlan.Linked.OrderBy(l => l.Level).ThenBy(l => l.Name, StringComparer.Ordinal)
                .Select(l => new CandidateLinkSummary(l.Candidate?.Id, l.Name, l.Level, l.Candidate?.Status, l.Candidate is null))],
        };
    }

    /// <summary>
    /// R55: what stops a class from being accepted because of the features it links: a pending one that would itself be refused
    /// blocks with its own codes (the same code once, with a count). An ignored one does not (R66). Messages never quote the document.
    /// </summary>
    private List<Diagnostic> LinkedBlockers(AcceptPlan plan)
    {
        var blockers = new List<Diagnostic>();
        if (plan.Linked.Count == 0)
            return blockers;
        var cache = new Dictionary<(Guid Source, string Sha), List<(Guid JobId, (ContentKind, string, int) Identity)>>();
        var codes = new Dictionary<string, (int Count, string Message)>(StringComparer.Ordinal);
        foreach (var feature in plan.Linked.Where(l => l.NewDraft is not null))
        {
            var check = Check(feature.Candidate!, asReference: false, ProvenanceBlockers(feature.Candidate!, cache));
            var found = check.Blockers.Select(b => (b.Code, b.Message)).ToList();
            if (!check.Report.CanPublish)
                found.Add(("candidate.validation-failed", "has problems the validator reports."));
            foreach (var (code, message) in found.DistinctBy(f => f.Code))
                codes[code] = codes.TryGetValue(code, out var seen) ? (seen.Count + 1, seen.Message) : (1, message);
        }
        foreach (var (code, (count, message)) in codes)
            blockers.Add(new(code, $"{count} linked feature(s): {message}"));
        return blockers;
    }

    /// <summary>
    /// What "reference only" does not lift. Computed from what is stored, never stored itself (no migration):
    /// no rules family (2026-10-14 item 3; the empty set has its own blocker, so the unsure "rulesFamilies" field is not listed twice);
    /// an armour row or class table kept by a 0.5.0 job on a two-family source, which silently took srd-5.2.1 (ruling R28), until the
    /// reviewer saves a family; and an entry another reading of the same document already turned into a draft (ruling R27).
    /// </summary>
    private List<Diagnostic> ProvenanceBlockers(StoredCandidate stored, Dictionary<(Guid Source, string Sha), List<(Guid JobId, (ContentKind, string, int) Identity)>>? acceptedCache = null)
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

        var identity = CandidateIdentity(current); // the effective values: renaming a twin is how a deliberate second copy is made (R27)
        var already = acceptedCache is null
            ? _store.ListImportJobs()
                .Where(j => j.Id != job.Id && j.SourceId == job.SourceId && j.Sha256 == job.Sha256)
                .SelectMany(j => _store.ListImportCandidates(j.Id))
                .Any(c => c.Status is CandidateStatus.Accepted or CandidateStatus.AcceptedAsReference && CandidateIdentity(c.Candidate) == identity)
            : AcceptedOf(acceptedCache, job).Any(a => a.JobId != job.Id && a.Identity == identity);
        if (already)
            blockers.Add(new("import.already-accepted", "This entry was already accepted from another reading of this document."));
        return blockers;
    }

    /// <summary>
    /// What makes two candidates the same entry: kind, name and first page, and (Task 8a I1) the class a feature stub belongs to,
    /// so two classes on one page that list one feature name keep a stub each. The class joins the name part, after a control
    /// character no name carries; a candidate with no <c>class</c> field has the plain name, as before.
    /// Used both when detection filters reviewed entries and when an accept checks for an earlier acceptance.
    /// </summary>
    internal static (ContentKind, string, int) CandidateIdentity(DraftCandidate c) =>
        (c.ProposedKind, CandidateDetector.Key(c.ProposedName) + (c.Fields.TryGetValue("class", out var owner) ? "\u0001" + CandidateDetector.Key(owner) : ""), c.Page.Start);

    /// <summary>
    /// The identities already accepted from one document in one source, across all its jobs, read once per (source, document)
    /// for a bulk accept; the caller adds each identity it accepts, so later twins in the same request see it (R27).
    /// </summary>
    private List<(Guid JobId, (ContentKind, string, int) Identity)> AcceptedOf(
        Dictionary<(Guid Source, string Sha), List<(Guid JobId, (ContentKind, string, int) Identity)>> cache, ImportJobRecord job)
    {
        if (!cache.TryGetValue((job.SourceId, job.Sha256), out var list))
        {
            list = [.. _store.ListImportJobs()
                .Where(j => j.SourceId == job.SourceId && j.Sha256 == job.Sha256)
                .SelectMany(j => _store.ListImportCandidates(j.Id).Where(c => c.Status is CandidateStatus.Accepted or CandidateStatus.AcceptedAsReference).Select(c => (j.Id, CandidateIdentity(c.Candidate))))];
            cache[(job.SourceId, job.Sha256)] = list;
        }
        return list;
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

    private List<CandidateDependency> Dependencies(ContentRevision draft, DraftCandidate candidate, IReadOnlyList<ContentRevision> batch)
    {
        var dependencies = new List<CandidateDependency>();
        var source = _store.FindSource(draft.Provenance.SourceId);
        dependencies.Add(new("source", source?.Title ?? "(missing source)"));
        var references = draft.Effects.OfType<GrantEffect>().Where(g => g.Content is not null).Select(g => g.Content!)
            .Concat(draft.Effects.OfType<ChoiceEffect>().SelectMany(c => c.Options))
            .Distinct();
        foreach (var reference in references)
        {
            var revision = batch.FirstOrDefault(r => r.Reference == reference) ?? _store.FindRevision(reference); // a linked feature's draft is written with the class
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
        return _store.QueryImportCandidates(request.JobId, FilterOf(request), 0, null).Items;
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
        // R82: the filter and the order run on the indexed columns; only the rows of this page are read from the JSON.
        var (items, total) = _store.QueryImportCandidates(request.JobId, FilterOf(request), request.Offset ?? 0, request.Limit ?? DefaultCandidatePageSize);
        return new(items, total);
    }

    private static ImportCandidateFilter FilterOf(ImportCandidatesRequest request) =>
        new(request.FirstPage ?? request.Page, request.LastPage ?? request.Page, request.Kind, request.MinConfidence, request.MaxConfidence, request.Status);
}
