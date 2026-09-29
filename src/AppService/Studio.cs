using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <param name="Redistributable">Default false: a share export leaves personal homebrew out unless the author says it may be shared (ADR-007).</param>
/// <param name="ConfirmOwnWork">
/// M6 slice 1: required with <paramref name="Redistributable"/>, as for "Mark as shareable": the author confirms the source
/// is their own work (<see cref="TomeStackApp.OwnWorkStatement"/>).
/// </param>
public sealed record HomebrewSourceRequest(string Title, IReadOnlyList<string> RulesFamilies, string? Publisher = null, bool Redistributable = false, bool ConfirmOwnWork = false);

/// <summary>
/// <c>source.setShareable</c> (M6 slice 1, "Mark as shareable"). <paramref name="Shareable"/> true needs
/// <paramref name="ConfirmOwnWork"/>; false stops sharing and needs no confirmation.
/// </summary>
public sealed record ShareableRequest(Guid SourceId, bool Shareable, bool ConfirmOwnWork = false);

/// <summary><c>content.diagnose</c>: exactly one of a source, a stored revision or an unsaved revision.</summary>
public sealed record DiagnoseRequest(Guid? SourceId = null, ContentReference? Reference = null, ContentRevision? Revision = null);

/// <summary>One content entity from a source, as the studio lists it: every revision in the order it was added.</summary>
public sealed record StudioEntry(Guid ContentId, string Name, ContentKind Kind, IReadOnlyList<ContentRevision> Revisions)
{
    public ContentRevision Latest => Revisions[^1];

    public ContentRevision? LatestPublished => Revisions.LastOrDefault(r => r.Status == RevisionStatus.Published);
}

/// <summary>
/// M2 item 5, SPEC I-04: the homebrew studio's reads and its one write besides drafts, a homebrew source. Content is
/// authored as drafts (<c>content.saveDraft</c>), checked (<c>content.validate</c>) and published (<c>content.publish</c>).
/// </summary>
public sealed partial class TomeStackApp
{
    public const int MaxSourceTitleLength = 200;

    /// <summary>
    /// <c>source.list</c>: every installed source. The machine-local PDF path is never sent to the UI (ADR-005); the
    /// attachment commands (M2 item 6) describe a source's PDF instead.
    /// </summary>
    public IReadOnlyList<SourceRecord> ListSources() => [.. _store.ListSources().Select(s => s with { PdfRef = null })];

    /// <summary><c>source.createHomebrew</c>: a new local source for the user's own content (SPEC S-01).</summary>
    public SourceRecord CreateHomebrewSource(HomebrewSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = new List<Diagnostic>();
        var title = request.Title?.Trim() ?? "";
        if (title.Length is 0 or > MaxSourceTitleLength)
            problems.Add(new("source.title-required", $"A source needs a title of 1 to {MaxSourceTitleLength} characters."));
        var families = request.RulesFamilies ?? [];
        if (families.Count == 0 || families.Any(f => !RulesFamilies.IsKnown(f)) || families.Distinct().Count() != families.Count)
            problems.Add(new("source.rules-family-required", "Name the rules families this source is written for (srd-5.1, srd-5.2.1 or both), each once."));
        // M6 slice 1 (D14 item 6): the same guard as "Mark as shareable". A new source cannot be import-derived yet, so the
        // guard is the author's confirmation; attaching a PDF later turns sharing off for good.
        if (request.Redistributable && !request.ConfirmOwnWork)
            problems.Add(new("source.confirm-own-work", $"To share this source, confirm that it is your own work: {OwnWorkStatement}"));
        if (problems.Count > 0)
            throw new AppValidationException(problems);

        var now = _time.GetUtcNow();
        var source = new SourceRecord
        {
            Id = Guid.NewGuid(),
            Title = title,
            Publisher = string.IsNullOrWhiteSpace(request.Publisher) ? "Personal homebrew" : request.Publisher.Trim(),
            RulesFamilies = families,
            EditionVersion = "homebrew",
            License = "Personal homebrew",
            Redistributable = request.Redistributable,
            ImportedAt = now,
            Origin = SourceOrigin.Local,
            ShareConfirmedAt = request.Redistributable ? now : null,
        };
        _store.InTransaction(() => _store.UpsertSource(source));
        return source;
    }

    /// <summary>What the author confirms to mark a source as shareable (M6 slice 1). Shown in the UI and in refusals.</summary>
    public const string OwnWorkStatement =
        "the source is my own work, and it holds no text, tables or rules copied from a book, PDF or other material I did not write.";

    /// <summary>
    /// <c>source.setShareable</c> (M6 slice 1, "Mark as shareable"; LIVING_SPECS D14 item 6). Marking needs the author's
    /// confirmation and is refused for a bundled source, an import-derived source and a source received from someone
    /// else. It sets <c>redistributable</c> and records when it was confirmed. Stopping sharing is always allowed; it does
    /// not recall files already sent.
    /// </summary>
    public SourceRecord SetShareable(ShareableRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = FindSourceOrThrow(request.SourceId);
        if (_bundledSources.Contains(source.Id))
            throw new AppValidationException([new("source.bundled", $"'{source.Title}' is bundled with TomeStack; its license is fixed.")]);
        if (!request.Shareable)
        {
            var stopped = source with { Redistributable = false, ShareConfirmedAt = null };
            _store.InTransaction(() => _store.UpsertSource(stopped));
            return _store.FindSource(source.Id)!;
        }
        if (source.ImportDerived == true)
            throw new AppValidationException([new("source.import-derived", $"'{source.Title}' holds material imported from a PDF, so it can never be shared. Removing the PDF does not change that. Put your own homebrew in a new source.")]);
        if (source.Origin == SourceOrigin.Received)
            throw new AppValidationException([new("source.received", $"'{source.Title}' came from someone else's package, so you cannot mark it as your own work.")]);
        if (!request.ConfirmOwnWork)
            throw new AppValidationException([new("source.confirm-own-work", $"To share '{source.Title}', confirm that {OwnWorkStatement}")]);
        var marked = source with { Redistributable = true, ShareConfirmedAt = _time.GetUtcNow() };
        _store.InTransaction(() => _store.UpsertSource(marked));
        return _store.FindSource(source.Id)!;
    }

    /// <summary>
    /// M6 slice 1: records that material from outside the author entered the source (a PDF attached, a candidate accepted,
    /// pages imported). The flag never goes down, and sharing is turned off with it. Bundled sources are never marked.
    /// Call inside the transaction that brings the material in.
    /// </summary>
    private void MarkImportDerived(Guid sourceId)
    {
        if (_bundledSources.Contains(sourceId) || _store.FindSource(sourceId) is not { } source || source.ImportDerived == true)
            return;
        _store.UpsertSource(source with { ImportDerived = true, Redistributable = false, ShareConfirmedAt = null });
    }

    /// <summary>
    /// <c>content.diagnose</c> (M5 slice 2, B02): the homebrew debugger. Studies one source (the latest revision of each of
    /// its contents, drafts included), one stored revision, or one unsaved revision, against everything installed. It
    /// reports validation problems and what the content graph shows (<see cref="ContentDebugger"/>). Writes nothing.
    /// </summary>
    public DebugReport Diagnose(DiagnoseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.SourceId is null ? 0 : 1) + (request.Reference is null ? 0 : 1) + (request.Revision is null ? 0 : 1) != 1)
            throw new AppValidationException([new("diagnose.scope", "Name exactly one of a source, a stored revision or an unsaved revision to check.")]);
        IReadOnlyList<ContentRevision> scope;
        if (request.SourceId is { } sourceId)
        {
            if (_store.FindSource(sourceId) is null)
                throw new AppValidationException([new("source.not-found", $"Source {sourceId} is not installed.")]);
            scope = [.. ContentBySource(sourceId).Select(e => e.Latest)];
        }
        else if (request.Reference is { } reference)
        {
            scope = [_store.FindRevision(reference) ?? throw new AppValidationException([new("content.not-found", $"Revision {reference.RevisionId} is not installed.", reference)])];
        }
        else
        {
            // As for a draft: incomplete is fine, malformed is not (the graph must be able to read it).
            if (ContentValidator.EmptyEntries(request.Revision!) is { Count: > 0 } empty)
                throw new AppValidationException(empty);
            scope = [request.Revision!];
        }
        // One revision is studied among the latest revisions of its own source (drafts too), as "Find problems in <source>"
        // sees them, so both give the same answer about it (review fix). Those revisions shape the graph; only the scope is
        // reported.
        var context = request.SourceId is null ? ContentBySource(scope[0].Provenance.SourceId).Select(e => e.Latest).ToList() : [];
        return ContentDebugger.Diagnose(scope, _store.ListRevisionsInOrder(), _store, context);
    }

    /// <summary>
    /// <c>content.tree</c> (M5 slice 5, B19): the relationships of one content as a tree (<see cref="ContentTree"/>): a
    /// stored revision by reference, or the unsaved revision on screen, among the latest revisions of its source, as the
    /// debugger studies it. Writes nothing.
    /// </summary>
    public ContentTreeView Tree(DiagnoseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceId is not null || (request.Reference is null) == (request.Revision is null))
            throw new AppValidationException([new("tree.scope", "Name one stored revision or one unsaved revision.")]);
        var revision = request.Revision
            ?? _store.FindRevision(request.Reference!)
            ?? throw new AppValidationException([new("content.not-found", $"Revision {request.Reference!.RevisionId} is not installed.", request.Reference)]);
        if (ContentValidator.EmptyEntries(revision) is { Count: > 0 } empty)
            throw new AppValidationException(empty);
        if (revision.Effects.Count > MaxCompareEffects)
            throw new AppValidationException([new("tree.too-large", $"Relationships are shown for revisions of up to {MaxCompareEffects} rules.", revision.Reference)]);
        // One read of the store (review fix): the source's latest revisions are the context, as for the debugger.
        var all = _store.ListRevisionsInOrder();
        var context = all.Where(r => r.Provenance.SourceId == revision.Provenance.SourceId && r.ContentId != revision.ContentId)
            .GroupBy(r => r.ContentId).Select(g => g.Last());
        return ContentTree.Build(ContentGraph.Build(all, [.. context, revision], reach: false), revision.ContentId);
    }

    /// <summary>
    /// <c>content.feedback</c> (M5 slice 7, LIVING_SPECS D14): design hints for one revision (stored, or the unsaved one on
    /// screen), against the newest published revision of each bundled (SRD) content. The studio asks only while its "design
    /// feedback" setting is on (off by default). Hints never block, never change a calculation, and are never stored.
    /// </summary>
    public IReadOnlyList<DesignHint> Feedback(DiagnoseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceId is not null || (request.Reference is null) == (request.Revision is null))
            throw new AppValidationException([new("feedback.scope", "Name one stored revision or one unsaved revision.")]);
        var revision = request.Revision
            ?? _store.FindRevision(request.Reference!)
            ?? throw new AppValidationException([new("content.not-found", $"Revision {request.Reference!.RevisionId} is not installed.", request.Reference)]);
        if (ContentValidator.EmptyEntries(revision) is { Count: > 0 } empty)
            throw new AppValidationException(empty);
        if (revision.Effects.Count > MaxCompareEffects)
            throw new AppValidationException([new("feedback.too-large", $"Design feedback covers revisions of up to {MaxCompareEffects} rules.", revision.Reference)]);
        // Only the SRD packs this build ships, never an imported source whatever its edition says (review fix). Every kind:
        // the SRD packs keep a class's spellcasting on its granted "Spellcasting" feature.
        var baseline = _store.ListRevisionsInOrder()
            .Where(r => r.Status == RevisionStatus.Published && _bundledSources.Contains(r.Provenance.SourceId))
            .GroupBy(r => r.ContentId).Select(g => g.Last());
        return DesignFeedback.Analyze(revision, baseline);
    }

    /// <summary><c>content.bySource</c>: every content entity of one source with all its revisions (drafts too).</summary>
    public IReadOnlyList<StudioEntry> ContentBySource(Guid sourceId) =>
    [
        .. _store.ListRevisionsInOrder()
            .Where(r => r.Provenance.SourceId == sourceId)
            .GroupBy(r => r.ContentId)
            .Select(g => new StudioEntry(g.Key, g.Last().Name, g.Last().Kind, [.. g]))
            .OrderBy(e => e.Kind).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
    ];
}
