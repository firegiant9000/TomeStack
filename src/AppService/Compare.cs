using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// <c>content.compare</c>: two revisions of one content. <paramref name="From"/> is stored; the other is stored
/// (<paramref name="To"/>) or unsaved (<paramref name="ToRevision"/>, such as the studio editor's draft).
/// </summary>
/// <param name="CharacterIds">Saved characters to run both revisions on (unsaved copies), at most <see cref="TomeStackApp.MaxCompareCharacters"/>.</param>
/// <param name="Blank">Also run both on a blank character (a class or subclass only).</param>
public sealed record CompareRequest(
    ContentReference From, ContentReference? To = null, ContentRevision? ToRevision = null, IReadOnlyList<Guid>? CharacterIds = null, CompareBlank? Blank = null);

/// <param name="Level">The level in the class (or in the class a subclass joins); default as in the sandbox.</param>
public sealed record CompareBlank(string? RulesFamily = null, int? Level = null);

/// <summary>One character with each revision: what changes from the first to the second, or why it could not run.</summary>
/// <param name="CharacterId">The saved character the copies came from; null for a blank character.</param>
public sealed record CompareRun(
    string Name,
    Guid? CharacterId,
    IReadOnlyList<FieldDelta> Fields,
    IReadOnlyList<Diagnostic> NewDiagnostics,
    IReadOnlyList<Diagnostic> ResolvedDiagnostics,
    IReadOnlyList<ChoiceStatus> UnresolvedChoices,
    IReadOnlyList<Diagnostic> Problems);

/// <param name="Mechanics">Effects and properties, by effect id (<see cref="ContentDiff"/>).</param>
/// <param name="Text">The name, summary and rule texts, line by line (<see cref="ContentTextDiff"/>).</param>
public sealed record ContentComparison(ContentDiff Mechanics, IReadOnlyList<TextChange> Text, IReadOnlyList<CompareRun> Runs);

public sealed partial class TomeStackApp
{
    public const int MaxCompareCharacters = 20;

    /// <summary>The most rules (effects) either revision of a comparison may have; far above any bundled revision's.</summary>
    public const int MaxCompareEffects = 5_000;

    /// <summary>
    /// M5 slice 4 (B04 before/after tests, B07 diff viewer): compares two revisions of a content by mechanics and by
    /// text, and runs both on unsaved copies of chosen characters, and on a blank character, with the update review's
    /// computation (<see cref="SheetChanges"/>). A draft calculates only through the sandbox overlay, one draft per
    /// calculation (LIVING_SPECS D14). Writes nothing and applies nothing.
    /// </summary>
    public ContentComparison Compare(CompareRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.From);
        var from = _store.FindRevision(request.From)
            ?? throw new AppValidationException([new("content.not-found", $"Revision {request.From.RevisionId} is not installed.", request.From)]);
        if ((request.To is null) == (request.ToRevision is null))
            throw new AppValidationException([new("compare.to-required", "Name the second revision: a stored one, or an unsaved one.")]);
        var to = request.ToRevision ?? _store.FindRevision(request.To!)
            ?? throw new AppValidationException([new("content.not-found", $"Revision {request.To!.RevisionId} is not installed.", request.To)]);
        var unsaved = request.ToRevision is not null;
        if (unsaved)
        {
            if (ContentValidator.EmptyEntries(to) is { Count: > 0 } empty)
                throw new AppValidationException(empty);
            // An unsaved revision is a draft whatever it says, and gets an id of its own.
            to = to with { Status = RevisionStatus.Draft, RevisionId = to.RevisionId == Guid.Empty || _store.FindRevision(to.Reference) is not null ? Guid.NewGuid() : to.RevisionId };
        }
        var problems = new List<Diagnostic>();
        if (from.ContentId != to.ContentId)
            problems.Add(new("compare.different-content", $"'{to.Name}' is different content, not another revision of '{from.Name}'.", to.Reference));
        if (from.Reference == to.Reference)
            problems.Add(new("compare.same-revision", "Pick two different revisions.", to.Reference));
        var ids = request.CharacterIds ?? [];
        if (ids.Count > MaxCompareCharacters)
            problems.Add(new("compare.too-many", $"Compare on at most {MaxCompareCharacters} characters at a time."));
        if (request.Blank is not null && to.Kind is not (ContentKind.Class or ContentKind.Subclass))
            problems.Add(new("compare.blank-kind", "A blank character can only try a class or a subclass.", to.Reference));
        // Bounds (SPEC Q-02, review fix): each run calculates twice, and the response repeats every changed effect.
        if (from.Effects.Count > MaxCompareEffects || to.Effects.Count > MaxCompareEffects)
            problems.Add(new("compare.too-large", $"Compare works on revisions of up to {MaxCompareEffects} rules each.", to.Reference));
        string? blankFamily = null;
        if (request.Blank is { } requested)
        {
            // Checked before any character runs, so a bad request costs no calculation.
            blankFamily = requested.RulesFamily ?? to.RulesFamilies.FirstOrDefault(from.RulesFamilies.Contains) ?? to.RulesFamilies.FirstOrDefault() ?? "";
            if (!RulesFamilies.IsKnown(blankFamily))
                problems.Add(new("rules-family.unknown", $"Rules family '{blankFamily}' is not supported."));
            if (requested.Level is < Character.MinLevel or > Character.MaxLevel)
                problems.Add(new("sandbox.level", $"The level must be {Character.MinLevel} to {Character.MaxLevel}."));
        }
        if (problems.Count > 0)
            throw new AppValidationException(problems);

        var fromCatalog = CatalogFor(from);
        var toCatalog = CatalogFor(to);
        var saved = ids.Distinct().Select(id => _store.FindCharacter(id)
            ?? throw new AppValidationException([new("character.not-found", $"Character {id} does not exist.")])).ToList();
        var runs = new List<CompareRun>();
        foreach (var character in saved)
            runs.Add(Run(character.Name, character.Id, character with { Id = Guid.NewGuid() }, level: null));
        if (request.Blank is { } blank)
            runs.Add(Run("Blank character", null, BlankCharacter(blankFamily!), blank.Level));
        return new ContentComparison(ContentDiff.Compare(from, to), ContentTextDiff.Compare(from, to), runs);

        CompareRun Run(string name, Guid? characterId, Character copy, int? level)
        {
            try
            {
                var before = Checked(PlaceForCompare(copy, from, level));
                var after = Checked(PlaceForCompare(copy, to, level));
                var delta = SheetChanges(before, fromCatalog, after, toCatalog, from.Reference, to.Reference);
                return new(name, characterId, delta.Fields, delta.NewDiagnostics, delta.ResolvedDiagnostics, delta.UnresolvedChoices, []);
            }
            catch (AppValidationException ex)
            {
                // One character that cannot take the content (a family, a level, a choice) does not stop the others.
                return new(name, characterId, [], [], [], [], ex.Problems);
            }
        }
    }

    /// <summary>The store for a published revision; the sandbox overlay for a draft (which must be the one draft of its calculation).</summary>
    private IContentCatalog CatalogFor(ContentRevision revision) =>
        revision.Status == RevisionStatus.Published && _store.FindRevision(revision.Reference) is not null ? _store : new DraftOverlayCatalog(_store, revision);

    /// <summary>
    /// A copy on one revision. A copy that already uses the content (any kind, a declared-option subclass too) has its
    /// references replaced, as <c>character.applyUpdate</c> does, under the update review's family rule: the revision
    /// supports the character's family, or the character records an exception for that content. A class or subclass the
    /// copy does not have is placed as in the sandbox. Other content cannot be compared on a character that lacks it; a
    /// character that only gets it through a grant is told so (review fixes).
    /// </summary>
    private Character PlaceForCompare(Character copy, ContentRevision revision, int? level)
    {
        var used = copy.AllReferences().Where(r => r.ContentId == revision.ContentId).ToList();
        var exception = copy.CrossFamilyExceptions.Any(e => e.Content.ContentId == revision.ContentId);
        if (!revision.RulesFamilies.Contains(copy.RulesFamily) && !(exception && used.Count > 0))
            throw new AppValidationException([new("sandbox.rules-family", $"This revision of '{revision.Name}' supports {string.Join(", ", revision.RulesFamilies)}, not {copy.RulesFamily}.", revision.Reference)]);
        if (used.Count > 0 && (level is null || revision.Kind is not (ContentKind.Class or ContentKind.Subclass)))
        {
            foreach (var other in used.Where(r => r != revision.Reference))
                copy = copy.ReplaceReference(other, revision.Reference);
            return copy;
        }
        if (revision.Kind is ContentKind.Class or ContentKind.Subclass)
            return Place(copy, revision, level);
        var granter = copy.AllReferences().Select(_store.FindRevision).OfType<ContentRevision>()
            .FirstOrDefault(r => r.Effects.OfType<GrantEffect>().Any(g => g.Grant == GrantKind.Content && g.Content?.ContentId == revision.ContentId));
        throw new AppValidationException([granter is not null
            ? new("compare.granted", $"This character gets '{revision.Name}' through '{granter.Name}', which names one exact revision of it; compare '{granter.Name}' instead, or publish a revision of it that grants this one.", revision.Reference)
            : new("compare.unused", $"This character does not use '{revision.Name}'.", revision.Reference)]);
    }
}
