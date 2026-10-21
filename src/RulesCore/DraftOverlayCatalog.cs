namespace TomeStack.RulesCore;

/// <summary>
/// M5 slice 3 (B03, the studio sandbox; owner decision LIVING_SPECS D14): an in-memory view of a catalog in which one
/// draft counts as published, for one calculation on an unsaved copy of a character. The draft replaces the other
/// revisions of its content: it is the only one offered where it extends a choice. Nothing is written: the overlay holds
/// no store, and the draft's status changes only in this object. This is the one place a draft calculates, so the rule
/// reads "only published revisions affect <em>saved</em> characters".
/// </summary>
public sealed class DraftOverlayCatalog : IContentCatalog
{
    private readonly IContentCatalog _inner;
    private readonly SourceRecord? _source;

    /// <param name="source">
    /// Item 15 (D35): the draft's source when it is not stored yet (the import's stub class, whose source is written only at
    /// apply). Found by its id, in memory only; every other source comes from <paramref name="inner"/>. Null (the sandbox and
    /// compare) changes nothing.
    /// </param>
    public DraftOverlayCatalog(IContentCatalog inner, ContentRevision draft, SourceRecord? source = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(draft);
        _inner = inner;
        _source = source;
        Draft = draft with { Status = RevisionStatus.Published };
    }

    /// <summary>The draft as the calculation sees it (published, in memory only).</summary>
    public ContentRevision Draft { get; }

    public ContentRevision? FindRevision(ContentReference reference) => reference == Draft.Reference ? Draft : _inner.FindRevision(reference);

    public SourceRecord? FindSource(Guid sourceId) => _inner.FindSource(sourceId) ?? (_source?.Id == sourceId ? _source : null);

    public IEnumerable<ContentRevision> ChoiceExtensions(Guid contentId, string choiceId) =>
        _inner.ChoiceExtensions(contentId, choiceId)
            .Where(r => r.ContentId != Draft.ContentId)
            .Concat(Draft.ExtendsChoice == new ChoiceExtension(contentId, choiceId) ? [Draft] : []);

    public IEnumerable<ContentRevision> RevisionsOf(Guid contentId) =>
        contentId == Draft.ContentId
            ? _inner.RevisionsOf(contentId).Where(r => r.Reference != Draft.Reference).Append(Draft)
            : _inner.RevisionsOf(contentId);
}
