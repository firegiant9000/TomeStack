namespace TomeStack.RulesCore;

/// <summary>A bundle of sources and content revisions, e.g. the bundled M0 fixture pack.</summary>
public sealed record ContentPack
{
    public required string PackId { get; init; }
    public required int FormatVersion { get; init; }
    public string? Notice { get; init; }
    public IReadOnlyList<SourceRecord> Sources { get; init; } = [];
    public IReadOnlyList<ContentRevision> Revisions { get; init; } = [];
}

public sealed class InMemoryContentCatalog(IEnumerable<SourceRecord> sources, IEnumerable<ContentRevision> revisions) : IContentCatalog
{
    private readonly Dictionary<Guid, SourceRecord> _sources = sources.ToDictionary(s => s.Id);
    private readonly Dictionary<ContentReference, ContentRevision> _revisions = revisions.ToDictionary(r => r.Reference);

    public InMemoryContentCatalog(ContentPack pack) : this(pack.Sources, pack.Revisions) { }

    public ContentRevision? FindRevision(ContentReference reference) => _revisions.GetValueOrDefault(reference);

    public SourceRecord? FindSource(Guid sourceId) => _sources.GetValueOrDefault(sourceId);
}
