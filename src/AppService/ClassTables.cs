using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary><c>content.classTable</c> (Import Lab, D62 L2): the revision to project, a class revision, published or draft.</summary>
public sealed record ClassTableRequest(Guid RevisionId);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// <c>content.classTable</c> (D62 L2): the 20-level progression of one class revision. Read-only. Features and choices are read
    /// from the published revisions (the latest of each content, as the coverage view does), so a draft class shows its own moves
    /// against what characters really get. Nothing is written.
    /// </summary>
    public ClassTableView ClassTable(ClassTableRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var all = _store.ListRevisionsInOrder();
        var revision = all.FirstOrDefault(r => r.RevisionId == request.RevisionId)
            ?? throw new AppValidationException([new("content.not-found", $"Revision {request.RevisionId} is not installed.")], "content.not-found");
        if (revision.Kind != ContentKind.Class)
            throw new AppValidationException([new("content.not-a-class", $"Revision {request.RevisionId} is not a class.")], "content.not-a-class");
        var (published, _) = global::TomeStack.AppService.Coverage.Choose(all, includeDrafts: false);
        return ClassProjection.Of(revision, new InMemoryContentCatalog([], published.Values));
    }
}
