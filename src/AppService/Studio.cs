using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <param name="Redistributable">Default false: a share export leaves personal homebrew out unless the author says it may be shared (ADR-007).</param>
public sealed record HomebrewSourceRequest(string Title, IReadOnlyList<string> RulesFamilies, string? Publisher = null, bool Redistributable = false);

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
        if (problems.Count > 0)
            throw new AppValidationException(problems);

        var source = new SourceRecord
        {
            Id = Guid.NewGuid(),
            Title = title,
            Publisher = string.IsNullOrWhiteSpace(request.Publisher) ? "Personal homebrew" : request.Publisher.Trim(),
            RulesFamilies = families,
            EditionVersion = "homebrew",
            License = "Personal homebrew",
            Redistributable = request.Redistributable,
            ImportedAt = _time.GetUtcNow(),
        };
        _store.InTransaction(() => _store.UpsertSource(source));
        return source;
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
