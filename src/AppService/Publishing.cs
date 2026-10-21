using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>How a character references a content revision.</summary>
public enum ReferenceRole { Pin, Class, Choice, Grant, Equipment, Spell }

/// <param name="Via">For <see cref="ReferenceRole.Grant"/>: the name of the referenced revision that grants it.</param>
public sealed record AffectedCharacter(Guid CharacterId, string Name, ContentReference Pinned, ReferenceRole Role, string? Via = null);

public sealed record PublishResult(ContentReference Draft, ContentReference Published, ValidationReport Report, IReadOnlyList<AffectedCharacter> Affected);

/// <summary>What publishing would move in one content: grants and choice options that pin a revision of something published in the same batch.</summary>
public sealed record RePointing(ContentReference Dependent, string DependentName, int GrantsMoved, int OptionsMoved);

/// <summary>
/// One content in a bulk publish. For an entry of the request, <paramref name="Draft"/> is its pending draft; for a dependent,
/// its newest revision (published, or a draft the request did not name, which is an error).
/// </summary>
/// <param name="ReplacesPublished">The content already has a published revision, so publishing replaces what a character gets.</param>
/// <param name="Report">Validation of that revision, plus the bulk publish's own errors (<c>publish.*</c>).</param>
public sealed record PublishManyEntry(
    ContentReference Draft, string Name, ContentKind Kind, ValidationReport Report,
    IReadOnlyList<DebugFinding> Findings, bool ReplacesPublished, IReadOnlyList<RePointing> RePointings);

/// <param name="Entries">The requested drafts, in the order they would publish (what they grant first).</param>
/// <param name="Dependents">Content of the same source whose grants or options pin something published here; each is re-pointed and re-published.</param>
public sealed record PublishManyPreview(IReadOnlyList<PublishManyEntry> Entries, IReadOnlyList<PublishManyEntry> Dependents, bool CanPublish);

/// <param name="ContentIds">The content to publish.</param>
/// <param name="Expected">The pending draft revision ids the user saw in the preview; must equal the pending set exactly.</param>
/// <param name="Confirm">Must be true.</param>
/// <param name="ExpectedDependents">
/// The dependents the preview showed (content id to the newest revision id). When present, the dependents planned inside the
/// transaction must equal them exactly (none added, none removed, none with a newer revision); absent (an older caller), they are not compared.
/// </param>
public sealed record PublishManyRequest(IReadOnlyList<Guid> ContentIds, IReadOnlyList<Guid> Expected, bool Confirm, IReadOnlyDictionary<Guid, Guid>? ExpectedDependents = null);

public sealed record PublishManyResult(IReadOnlyList<PublishResult> Published);

/// <summary>
/// M3 C7 (SPEC I-06): a newer published revision of content the character references directly, from a bundled pack or
/// the user's own source. An offer only: <c>character.reviewUpdate</c> shows it and <c>character.applyUpdate</c> adopts it.
/// </summary>
/// <param name="Bundled">The source ships with TomeStack (an SRD pack), rather than being made in TomeStack.</param>
public sealed record UpdateOffer(ContentReference From, ContentReference To, string Name, ReferenceRole Role, string SourceTitle, bool Bundled);

/// <summary>A displayed value that an update would change.</summary>
public sealed record FieldDelta(string Field, string Label, int Before, int After);

/// <summary>
/// SPEC I-06: what opting a character into a newer revision would change, computed without changing anything: the
/// mechanics diff, every field whose value changes, new and resolved diagnostics, choices left unresolved, and
/// overrides whose calculated value moves underneath them.
/// </summary>
public sealed record UpdateReview(
    Guid CharacterId,
    ContentReference From,
    ContentReference To,
    ContentDiff Mechanics,
    IReadOnlyList<FieldDelta> Fields,
    IReadOnlyList<Diagnostic> NewDiagnostics,
    IReadOnlyList<Diagnostic> ResolvedDiagnostics,
    IReadOnlyList<ChoiceStatus> UnresolvedChoices,
    IReadOnlyList<FieldOverride> AffectedOverrides);

public sealed partial class TomeStackApp
{
    /// <summary>Test seam (fix round 1, item 7): runs just before a draft is written, to force a failure inside a transaction.</summary>
    internal Action<ContentRevision>? BeforeSaveDraft { get; set; }

    /// <summary>
    /// Stores an inactive draft (ADR-004). Drafts are insert-only like published revisions, so changing a draft means
    /// saving it under a new revision id. Only a draft can be saved this way; publishing is a separate command.
    /// </summary>
    public ContentReference SaveDraft(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.Status != RevisionStatus.Draft)
            throw new AppValidationException([new("content.draft-required", "Only a draft can be saved. Publish it with content.publish, which validates it first.", revision.Reference)]);
        // M6 slice 1 (review fix): a bundled SRD source holds only this build's SRD content. A draft saved under it would be
        // published with the SRD's CC-BY record, shared everywhere and exempt from the import-derived flag.
        if (_bundledSources.Contains(revision.Provenance.SourceId))
            throw new AppValidationException([new("content.source-not-editable", "Bundled SRD sources cannot take your own content. Save it in a homebrew source of your own.", revision.Reference)]);
        // A content's revisions belong to one source (M6 stack review, 2026-09-30): a draft under another source would split
        // it, and a character package that pins both revisions could then not be imported again (pack.content-conflict).
        if (_store.RevisionsOf(revision.ContentId).FirstOrDefault(r => r.Provenance.SourceId != revision.Provenance.SourceId) is { } other)
            throw new AppValidationException([new("content.source-mismatch", $"'{other.Name}' belongs to another source. Save a new revision in that source, or make new content here.", revision.Reference)]);
        // A draft may be incomplete, but not malformed: validation and publishing must be able to read it.
        if (ContentValidator.EmptyEntries(revision) is { Count: > 0 } empty)
            throw new AppValidationException(empty);
        var draft = revision.RevisionId == Guid.Empty ? revision with { RevisionId = Guid.NewGuid() } : revision;
        BeforeSaveDraft?.Invoke(draft);
        try
        {
            _store.InTransaction(() => _store.AddRevision(draft));
        }
        catch (ImmutableRevisionException ex)
        {
            throw new AppValidationException([new("content.revision-conflict", ex.Message, draft.Reference)]);
        }
        return draft.Reference;
    }

    /// <summary>
    /// SPEC I-06, ADR-002, ADR-004: turns a validated draft into a new immutable published revision, with the same
    /// content id and a new revision id. It validates again here and refuses on any error. The draft stays as it was,
    /// and no character changes: each one opts in through a reviewed update.
    /// </summary>
    public PublishResult Publish(ContentReference draftReference)
    {
        var (result, draft) = PublishCore(draftReference);
        return result with { Affected = AffectedCharacters(draft.ContentId) };
    }

    /// <summary>The publish itself, without the affected-characters scan (which reads every character); a bulk publish skips that scan.</summary>
    private (PublishResult Result, ContentRevision Draft) PublishCore(ContentReference draftReference)
    {
        ArgumentNullException.ThrowIfNull(draftReference);
        var draft = _store.FindRevision(draftReference)
            ?? throw new AppValidationException([new("content.not-found", $"Revision {draftReference.RevisionId} is not installed.", draftReference)]);
        if (draft.Status != RevisionStatus.Draft)
            throw new AppValidationException([new("content.not-a-draft", $"'{draft.Name}' is already published; published revisions are immutable. Save a new draft to change it.", draftReference)]);
        var report = ContentValidator.Validate(draft, _store);
        if (!report.CanPublish)
            throw new AppValidationException([new("content.validation-failed", $"'{draft.Name}' has {report.Errors.Count} problem(s) and was not published.", draftReference), .. report.Errors]);

        // M2.2: write the lowest schema version the content needs (never above the draft's), so homebrew that uses no
        // newer field stays readable by older builds. The published revision is a new insert with its own hash; the
        // draft keeps its version, and no existing revision changes.
        var published = draft with
        {
            RevisionId = Guid.NewGuid(),
            Status = RevisionStatus.Published,
            SchemaVersion = Math.Min(draft.SchemaVersion, report.RequiredSchemaVersion),
        };
        _store.InTransaction(() => _store.AddRevision(published));
        // The report states the version actually written (it can be the draft's own, lower than the requirement when only a
        // spell field target raises it; those were never refused).
        return (new PublishResult(draftReference, published.Reference, report with { RequiredSchemaVersion = published.SchemaVersion }, []), draft);
    }

    public IReadOnlyList<ContentRevision> ListRevisions(Guid contentId) => _store.ListRevisions(contentId);

    /// <summary>The most content one bulk publish takes (a larger set is published in parts).</summary>
    public const int MaxPublishMany = 500;

    private sealed class PlanItem(ContentRevision revision, bool isEntry)
    {
        public ContentRevision Revision { get; } = revision;
        public bool IsEntry { get; } = isEntry;
        /// <summary>Errors of the batch itself (<c>publish.*</c>), found before anything runs.</summary>
        public List<Diagnostic> Extra { get; } = [];
        /// <summary>Validation of the revision as stored; its warnings are shown when the run does not replace them.</summary>
        public ValidationReport Report { get; set; } = null!;
    }

    private sealed record PublishPlan(List<PlanItem> Ordered, List<PlanItem> Blocked, HashSet<Guid> HasPublished, Dictionary<ContentReference, List<DebugFinding>> Findings)
    {
        public IEnumerable<PlanItem> All => Ordered.Concat(Blocked);

        public bool HasCycle => Ordered.Any(i => i.Extra.Any(e => e.Code == "publish.grants-cycle"));
    }

    /// <summary>What running one plan item did (a real publish, or the dry run behind the preview).</summary>
    private sealed class BatchItem(PlanItem plan)
    {
        public PlanItem Plan { get; } = plan;
        public PublishResult? Result { get; set; }
        public List<Diagnostic> Errors { get; } = [];
        public int Grants { get; set; }
        public int Options { get; set; }
    }

    /// <summary>Thrown by the preview's dry run to roll its transaction back, and caught outside it.</summary>
    private sealed class DryRunRollback : Exception;

    private static AppValidationException PublishSetChanged() =>
        new([new("content.publish-set-changed", "The drafts waiting to be published are not the ones you reviewed. Nothing was published; look at the list again.")]);

    /// <summary>Every grant's content and every choice option the revision names, one per mention.</summary>
    private static IEnumerable<ContentReference> NamedContent(ContentRevision revision) =>
        revision.Effects.OfType<GrantEffect>().Where(g => g.Content is not null).Select(g => g.Content!)
            .Concat(revision.Effects.OfType<ChoiceEffect>().SelectMany(c => c.Options));

    /// <summary>
    /// Points every grant and choice option that names a content of <paramref name="published"/> at the revision given there.
    /// <c>extendsChoice</c> names a content id and stays as it is. Returns the same instance when nothing moves.
    /// </summary>
    private static (ContentRevision Revision, int Grants, int Options) RePoint(ContentRevision revision, IReadOnlyDictionary<Guid, ContentReference> published)
    {
        ContentReference Map(ContentReference r) => r.ContentId != revision.ContentId && published.TryGetValue(r.ContentId, out var moved) ? moved : r;
        int grants = 0, options = 0;
        var effects = new List<Effect>();
        foreach (var effect in revision.Effects)
        {
            if (effect is GrantEffect { Content: { } content } grant && Map(content) != content)
            {
                effects.Add(grant with { Content = Map(content) });
                grants++;
            }
            else if (effect is ChoiceEffect choice && choice.Options.Any(o => Map(o) != o))
            {
                options += choice.Options.Count(o => Map(o) != o);
                effects.Add(choice with { Options = [.. choice.Options.Select(Map)] });
            }
            else
            {
                effects.Add(effect);
            }
        }
        return (grants + options == 0 ? revision : revision with { Effects = effects }, grants, options);
    }

    /// <summary>
    /// The read-only plan of a bulk publish (tester feedback item 9, D33), shared by the preview and the publish so they
    /// cannot disagree. Pending is per content id: its newest revision in stored order is a draft. A content id with nothing
    /// pending, or a pending set unlike <paramref name="expected"/>, is <c>content.publish-set-changed</c>. Dependents are
    /// content of the same source whose newest revision grants or offers a revision of anything published here, followed
    /// transitively; a dependent with a draft the request did not name is an error and is never swept in (ruling R41). When
    /// <paramref name="expectedDependents"/> is given, the dependents must equal it (content id to newest revision id), or it is
    /// <c>content.publish-set-changed</c> too.
    /// </summary>
    private PublishPlan PlanPublishMany(IReadOnlyList<Guid>? contentIds, IReadOnlyList<Guid>? expected, bool withFindings, IReadOnlyDictionary<Guid, Guid>? expectedDependents = null)
    {
        var requested = (contentIds ?? []).Distinct().ToList();
        if (requested.Count == 0)
            throw PublishSetChanged();
        if (requested.Count > MaxPublishMany)
            throw new AppValidationException([new("content.publish-too-many", $"Publish at most {MaxPublishMany} entries at a time.")]);
        var all = _store.ListRevisionsInOrder();
        var newest = new Dictionary<Guid, ContentRevision>();
        var hasPublished = new HashSet<Guid>();
        foreach (var revision in all)
        {
            newest[revision.ContentId] = revision; // stored order: the last one is the newest
            if (revision.Status == RevisionStatus.Published)
                hasPublished.Add(revision.ContentId);
        }
        var drafts = new List<ContentRevision>();
        foreach (var id in requested)
        {
            if (!newest.TryGetValue(id, out var pending) || pending.Status != RevisionStatus.Draft)
                throw PublishSetChanged();
            drafts.Add(pending);
        }
        if (expected is not null && !expected.ToHashSet().SetEquals(drafts.Select(d => d.RevisionId)))
            throw PublishSetChanged();

        var sources = drafts.Select(d => d.Provenance.SourceId).ToHashSet();
        var inSet = requested.ToHashSet();
        var items = drafts.Select(d => new PlanItem(d, isEntry: true)).ToList();
        var blocked = new List<PlanItem>();
        var seen = new HashSet<Guid>();
        bool grew;
        do
        {
            grew = false;
            foreach (var revision in newest.Values)
            {
                if (inSet.Contains(revision.ContentId) || seen.Contains(revision.ContentId) || !sources.Contains(revision.Provenance.SourceId)
                    || !NamedContent(revision).Any(r => inSet.Contains(r.ContentId)))
                    continue;
                seen.Add(revision.ContentId);
                var item = new PlanItem(revision, isEntry: false);
                if (revision.Status == RevisionStatus.Draft)
                {
                    item.Extra.Add(new("publish.dependent-pending", $"'{revision.Name}' has an unpublished draft that grants an entry in this batch; include it or publish it first.", revision.Reference));
                    blocked.Add(item);
                    continue;
                }
                inSet.Add(revision.ContentId);
                items.Add(item);
                grew = true;
            }
        }
        while (grew);

        // The dependents the preview showed were confirmed with the drafts (ADR-004): added, removed or newer ones are a changed set.
        if (expectedDependents is not null)
        {
            var planned = items.Where(i => !i.IsEntry).Concat(blocked).ToDictionary(i => i.Revision.ContentId, i => i.Revision.RevisionId);
            if (planned.Count != expectedDependents.Count || planned.Any(p => !expectedDependents.TryGetValue(p.Key, out var revisionId) || revisionId != p.Value))
                throw PublishSetChanged();
        }

        foreach (var item in items.Where(i => i.IsEntry))
        {
            foreach (var target in NamedContent(item.Revision).Where(t => !inSet.Contains(t.ContentId)).Distinct())
            {
                if (_store.FindRevision(target) is not { Status: RevisionStatus.Draft } outside)
                    continue;
                // Nothing pending to include: the content's newest revision is published, so this names an old draft of it.
                var message = newest.TryGetValue(target.ContentId, out var current) && current.Status == RevisionStatus.Published
                    ? $"'{item.Revision.Name}' points at an old draft of '{outside.Name}'; choose its published revision."
                    : $"'{item.Revision.Name}' grants or offers '{outside.Name}', a draft that is not in this batch. Include it, or publish it first.";
                item.Extra.Add(new("publish.grants-unpublished", message, item.Revision.Reference));
            }
        }

        // Leaves first: what an item grants (or the class whose choice it extends) publishes before it, so its grants can be
        // re-pointed at the published revisions and its validation sees the parent as published.
        var byId = items.ToDictionary(i => i.Revision.ContentId);
        // The extends edge is added only when the parent does not already name the child (its choice options or grants): then
        // the options or grants edge wins, as before, and the pair is not reported as a cycle (R58).
        IEnumerable<Guid> ExtendsEdge(PlanItem item) =>
            item.Revision.ExtendsChoice is { } extends
            && !(byId.TryGetValue(extends.ContentId, out var parent) && NamedContent(parent.Revision).Any(r => r.ContentId == item.Revision.ContentId))
                ? [extends.ContentId]
                : [];
        var needs = items.ToDictionary(
            i => i,
            i => NamedContent(i.Revision).Select(r => r.ContentId).Concat(ExtendsEdge(i))
                .Where(id => id != i.Revision.ContentId && byId.ContainsKey(id)).ToHashSet());
        var ordered = new List<PlanItem>();
        var done = new HashSet<Guid>();
        var remaining = new List<PlanItem>(items);
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(i => needs[i].All(done.Contains)).ToList();
            if (ready.Count == 0)
            {
                foreach (var stuck in remaining)
                    stuck.Extra.Add(new("publish.grants-cycle", $"'{stuck.Revision.Name}' grants, directly or through others, something that grants it back; there is no order to publish them in.", stuck.Revision.Reference));
                ordered.AddRange(remaining);
                break;
            }
            foreach (var item in ready)
            {
                ordered.Add(item);
                done.Add(item.Revision.ContentId);
            }
            remaining.RemoveAll(ready.Contains);
        }

        foreach (var item in ordered.Concat(blocked))
            item.Report = ContentValidator.Validate(item.Revision, _store);
        // The debugger's findings are for display. Its error findings are exactly ContentValidator's errors on the stored
        // draft (the graph findings are warnings and notes), and the validation that counts is the one `Publish` runs on the
        // revision it writes, which the dry run and the real run both execute; so errors are not shown from here, or a
        // problem that re-pointing fixes would be reported and then not happen.
        var findings = new Dictionary<ContentReference, List<DebugFinding>>();
        if (withFindings)
        {
            var context = newest.Values.Where(r => sources.Contains(r.Provenance.SourceId)).ToList();
            foreach (var finding in ContentDebugger.Diagnose([.. ordered.Select(i => i.Revision)], all, _store, context).Findings.Where(f => f.Severity != FindingSeverity.Error))
            {
                if (!findings.TryGetValue(finding.Content, out var list))
                    findings[finding.Content] = list = [];
                list.Add(finding);
            }
        }
        return new(ordered, blocked, hasPublished, findings);
    }

    /// <summary>
    /// Runs the plan: each item in order is saved as a new draft with its grants and options re-pointed at what was just
    /// published (an entry nothing re-points publishes as it is), then published through <see cref="PublishCore"/>, which
    /// validates the exact revision it writes. The preview and the real publish run this same body; only the dry run
    /// catches a refusal (it rolls back anyway), turning it into an error of that item.
    /// </summary>
    private List<BatchItem> RunBatch(PublishPlan plan, bool dryRun)
    {
        var published = new Dictionary<Guid, ContentReference>();
        var run = new List<BatchItem>();
        foreach (var item in plan.Ordered)
        {
            var (repointed, grants, options) = RePoint(item.Revision, published);
            var done = new BatchItem(item) { Grants = grants, Options = options };
            run.Add(done);
            try
            {
                // Insert-only: anything re-pointed, and every dependent, goes through a new draft.
                var draft = item.IsEntry && ReferenceEquals(repointed, item.Revision)
                    ? item.Revision.Reference
                    : SaveDraft(repointed with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft });
                var (result, _) = PublishCore(draft);
                published[item.Revision.ContentId] = result.Published;
                done.Result = result;
            }
            catch (AppValidationException ex) when (dryRun)
            {
                done.Errors.AddRange(ex.Problems.Where(p => p.Code != "content.validation-failed").DefaultIfEmpty(ex.Problems[0]));
            }
        }
        return run;
    }

    /// <summary>
    /// <c>content.publishManyPreview</c> (item 9, D33): what <see cref="PublishMany"/> would publish for these content ids,
    /// without writing anything: each pending draft with its validation report, the debugger's findings, whether it replaces
    /// a published version, and the re-pointing it needs; then the dependents that would be re-pointed and republished.
    /// </summary>
    public PublishManyPreview PreviewPublishMany(IReadOnlyList<Guid> contentIds)
    {
        // Ruling R50: the preview runs the real publish (same ordering, re-pointing, SaveDraft and validating Publish) inside
        // a transaction and rolls it back, so it cannot say "can publish" for something the publish would refuse.
        PublishPlan? plan = null;
        List<BatchItem> run = [];
        try
        {
            _store.InTransaction(() =>
            {
                plan = PlanPublishMany(contentIds, expected: null, withFindings: true);
                if (!plan.HasCycle) // a cycle has no order to run in
                    run = RunBatch(plan, dryRun: true);
                throw new DryRunRollback();
            });
        }
        catch (DryRunRollback)
        {
            // The rollback itself: nothing was written, and the choice-extension cache was reset by InTransaction.
        }
        var shown = run.ToDictionary(r => r.Plan);
        PublishManyEntry Entry(PlanItem item)
        {
            shown.TryGetValue(item, out var done);
            var errors = new List<Diagnostic>(item.Extra);
            if (done is null)
                errors.AddRange(item.Report.Errors); // not run (a cycle, or a dependent that is held): what stored validation says
            else
                errors.AddRange(done.Errors);
            var warnings = done?.Result?.Report.Warnings ?? item.Report.Warnings;
            var report = new ValidationReport(item.Revision.Reference, errors, warnings) { RequiredSchemaVersion = done?.Result?.Report.RequiredSchemaVersion ?? item.Report.RequiredSchemaVersion };
            return new(
                item.Revision.Reference, item.Revision.Name, item.Revision.Kind, report,
                plan!.Findings.TryGetValue(item.Revision.Reference, out var found) ? [.. found] : [],
                plan.HasPublished.Contains(item.Revision.ContentId),
                done is null || done.Grants + done.Options == 0 ? [] : [new(item.Revision.Reference, item.Revision.Name, done.Grants, done.Options)]);
        }
        var entries = plan!.Ordered.Where(i => i.IsEntry).Select(Entry).ToList();
        var dependents = plan.Ordered.Where(i => !i.IsEntry).Concat(plan.Blocked).Select(Entry).ToList();
        return new(entries, dependents, entries.Concat(dependents).All(e => e.Report.CanPublish));
    }

    /// <summary>
    /// <c>content.publishMany</c> (item 9, D33; ADR-004: a confirmed preview of the exact drafts is review). In one
    /// transaction: re-reads the pending set and refuses with <c>content.publish-set-changed</c> unless it equals
    /// <see cref="PublishManyRequest.Expected"/>, refuses on any error, then publishes leaves first. Each item that grants
    /// something published here (and each dependent) is saved as a new draft with those grants and options re-pointed at the
    /// published revisions, and that draft is published. Any failure rolls everything back. Characters are not re-pinned.
    /// A repeat finds nothing pending and writes nothing.
    /// </summary>
    public PublishManyResult PublishMany(PublishManyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("content.publish-confirmation-required", "Publishing creates permanent revisions. Review the preview and confirm; nothing was published.")]);
        PublishManyResult? result = null;
        _store.InTransaction(() =>
        {
            var plan = PlanPublishMany(request.ContentIds, request.Expected ?? throw PublishSetChanged(), withFindings: false, request.ExpectedDependents);
            var errors = plan.All.SelectMany(i => i.Extra).ToList();
            if (errors.Count > 0)
                throw new AppValidationException([new("content.validation-failed", $"This batch has {errors.Count} problem(s). Nothing was published."), .. errors]);
            // The same body as the preview's dry run. A refusal inside it (Publish validates what it writes) propagates, so
            // everything done so far rolls back with the transaction.
            // Affected characters are not scanned here (they read every character per item); the studio does not use them.
            result = new([.. RunBatch(plan, dryRun: false).Select(r => r.Result!)]);
        });
        return result!;
    }

    /// <summary>
    /// SPEC I-06: characters that use any revision of <paramref name="contentId"/>, whether they pin it, level in it,
    /// chose it, or it is granted by something they reference (one level, like the calculator).
    /// </summary>
    public IReadOnlyList<AffectedCharacter> AffectedCharacters(Guid contentId)
    {
        var affected = new List<AffectedCharacter>();
        foreach (var character in _store.ListCharacters())
        {
            void Add(ContentReference reference, ReferenceRole role, string? via = null)
            {
                if (reference.ContentId == contentId && !affected.Any(a => a.CharacterId == character.Id && a.Pinned == reference))
                    affected.Add(new(character.Id, character.Name, reference, role, via));
            }
            foreach (var pin in character.Pins)
                Add(pin, ReferenceRole.Pin);
            foreach (var entry in character.Classes)
                Add(entry.Class, ReferenceRole.Class);
            foreach (var selected in character.Choices.SelectMany(c => c.Selected))
                Add(selected, ReferenceRole.Choice);
            foreach (var entry in character.Equipment)
                Add(entry.Item, ReferenceRole.Equipment);
            foreach (var spell in character.Spells)
                Add(spell.Spell, ReferenceRole.Spell);
            foreach (var reference in character.AllReferences())
            {
                if (_store.FindRevision(reference) is not { } revision)
                    continue;
                foreach (var grant in revision.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content && g.Content is not null))
                    Add(grant.Content!, ReferenceRole.Grant, revision.Name);
            }
        }
        return affected;
    }

    /// <summary>
    /// <c>character.updates</c> (M3 C7, SPEC I-06): for each revision the character references directly (pins, classes,
    /// choice selections, equipment and spells), the newest published revision of the same content that supports the
    /// character's family (or is covered by its recorded exception), when that is a different revision. Granted content moves with its
    /// granter, so it is not offered on its own. Writes nothing; nothing is ever updated automatically.
    /// </summary>
    public IReadOnlyList<UpdateOffer> AvailableUpdates(Guid characterId)
    {
        var character = _store.FindCharacter(characterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {characterId} does not exist.")]);
        var sources = _store.ListSources().ToDictionary(s => s.Id);
        var offers = new List<UpdateOffer>();
        void Consider(ContentReference reference, ReferenceRole role)
        {
            if (offers.Any(o => o.From == reference) || _store.FindRevision(reference) is null)
                return;
            var exception = character.CrossFamilyExceptions.Any(e => e.Content == reference);
            var newest = _store.ListRevisions(reference.ContentId)
                .LastOrDefault(r => r.Status == RevisionStatus.Published && (exception || r.RulesFamilies.Contains(character.RulesFamily)));
            if (newest is null || newest.RevisionId == reference.RevisionId)
                return;
            var source = sources.GetValueOrDefault(newest.Provenance.SourceId);
            offers.Add(new(reference, newest.Reference, newest.Name, role, source?.Title ?? "(unknown source)", source is not null && source.EditionVersion != "homebrew"));
        }
        foreach (var pin in character.Pins)
            Consider(pin, ReferenceRole.Pin);
        foreach (var entry in character.Classes)
            Consider(entry.Class, ReferenceRole.Class);
        foreach (var selected in character.Choices.SelectMany(c => c.Selected))
            Consider(selected, ReferenceRole.Choice);
        foreach (var entry in character.Equipment)
            Consider(entry.Item, ReferenceRole.Equipment);
        foreach (var spell in character.Spells)
            Consider(spell.Spell, ReferenceRole.Spell);
        return offers;
    }

    /// <summary>SPEC I-06: the diff and recalculated fields for moving a character from one revision to another. Changes nothing.</summary>
    public UpdateReview ReviewUpdate(Guid characterId, ContentReference from, ContentReference to)
    {
        var (character, before, after) = PrepareUpdate(characterId, from, to);
        var changes = SheetChanges(character, _store, character.ReplaceReference(from, to), _store, from, to);
        return new UpdateReview(
            characterId, from, to,
            ContentDiff.Compare(before, after),
            changes.Fields, changes.NewDiagnostics, changes.ResolvedDiagnostics, changes.UnresolvedChoices, changes.AffectedOverrides);
    }

    /// <summary>What moving from one revision to another changes on a character's sheet (the update review's computation; M5 slice 4 reuses it).</summary>
    internal sealed record SheetDelta(
        IReadOnlyList<FieldDelta> Fields,
        IReadOnlyList<Diagnostic> NewDiagnostics,
        IReadOnlyList<Diagnostic> ResolvedDiagnostics,
        IReadOnlyList<ChoiceStatus> UnresolvedChoices,
        IReadOnlyList<FieldOverride> AffectedOverrides);

    /// <summary>
    /// Calculates <paramref name="before"/> and <paramref name="after"/>, each with its own catalog (the store, or a
    /// sandbox overlay for a draft), and compares them. A diagnostic of <paramref name="from"/> and the same one of
    /// <paramref name="to"/> count as unchanged. Pure: nothing is stored.
    /// </summary>
    internal static SheetDelta SheetChanges(
        Character before, IContentCatalog beforeCatalog, Character after, IContentCatalog afterCatalog, ContentReference from, ContentReference to)
    {
        var oldSheet = CharacterCalculator.Calculate(before, beforeCatalog);
        var newSheet = CharacterCalculator.Calculate(after, afterCatalog);
        // An old diagnostic a and a new one b are the same when their code and effect match and they name the same content,
        // or a names `from` where b names `to`. Keyed sets keep this linear in the number of diagnostics (review fix: a
        // nested scan was quadratic, and content.compare runs it for up to 21 characters).
        static (string, string?, ContentReference?) Key(Diagnostic d, ContentReference? content) => (d.Code, d.EffectId, content);
        var oldKeys = oldSheet.Diagnostics.Select(d => Key(d, d.Content)).ToHashSet();
        var newKeys = newSheet.Diagnostics.Select(d => Key(d, d.Content)).ToHashSet();
        bool InOld(Diagnostic b) => oldKeys.Contains(Key(b, b.Content)) || (b.Content == to && oldKeys.Contains(Key(b, from)));
        bool InNew(Diagnostic a) => newKeys.Contains(Key(a, a.Content)) || (a.Content == from && newKeys.Contains(Key(a, to)));
        var fields = newSheet.Fields
            .Select(f => (New: f, Old: oldSheet.Field(f.Field)))
            .Where(p => p.New.Value != p.Old.Value)
            .Select(p => new FieldDelta(p.New.Field, p.New.Label, p.Old.Value, p.New.Value))
            .ToList();
        var overrides = after.Overrides
            .Where(o => CharacterCalculator.IsField(o.Field) && oldSheet.Field(o.Field).ComputedValue != newSheet.Field(o.Field).ComputedValue)
            .ToList();
        return new(
            fields,
            [.. newSheet.Diagnostics.Where(n => !InOld(n))],
            [.. oldSheet.Diagnostics.Where(o => !InNew(o))],
            [.. (newSheet.Choices ?? []).Where(c => !c.Resolved)],
            overrides);
    }

    /// <summary>
    /// SPEC I-06: moves the character to the new revision, and only with <paramref name="confirm"/> set, after the user
    /// has seen <see cref="ReviewUpdate"/>. Overrides, levels and other choices are kept (SPEC C-06).
    /// </summary>
    public CharacterView ApplyUpdate(Guid characterId, ContentReference from, ContentReference to, bool confirm)
    {
        if (!confirm)
            throw new AppValidationException([new("update.confirmation-required", "Review the update first, then confirm it explicitly; nothing was changed.", from)]);
        var (character, _, _) = PrepareUpdate(characterId, from, to);
        return SaveCharacter(character.ReplaceReference(from, to));
    }

    private (Character Character, ContentRevision From, ContentRevision To) PrepareUpdate(Guid characterId, ContentReference from, ContentReference to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var character = _store.FindCharacter(characterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {characterId} does not exist.")]);
        var problems = new List<Diagnostic>();
        if (!character.AllReferences().Contains(from))
            problems.Add(new("update.not-referenced", $"The character does not use revision {from.RevisionId}.", from));
        var before = _store.FindRevision(from);
        var after = _store.FindRevision(to);
        if (before is null || after is null)
            problems.Add(new("content.not-found", "Both revisions must be installed.", before is null ? from : to));
        else
        {
            if (from.ContentId != to.ContentId)
                problems.Add(new("update.different-content", $"'{after.Name}' is different content, not a newer revision of '{before.Name}'. Updates stay within one content id.", to));
            if (after.Status != RevisionStatus.Published)
                problems.Add(new("update.not-published", $"'{after.Name}' revision {to.RevisionId} is a draft; only published revisions can be adopted.", to));
            if (!after.RulesFamilies.Contains(character.RulesFamily) && !character.CrossFamilyExceptions.Any(e => e.Content == from))
                problems.Add(new("update.rules-family", $"The new revision supports {string.Join(", ", after.RulesFamilies)}, not {character.RulesFamily}.", to));
        }
        if (from == to)
            problems.Add(new("update.same-revision", "The character already uses this revision.", to));
        if (problems.Count > 0)
            throw new AppValidationException(problems);
        return (character, before!, after!);
    }
}
