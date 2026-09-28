using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>How a character references a content revision.</summary>
public enum ReferenceRole { Pin, Class, Choice, Grant, Equipment, Spell }

/// <param name="Via">For <see cref="ReferenceRole.Grant"/>: the name of the referenced revision that grants it.</param>
public sealed record AffectedCharacter(Guid CharacterId, string Name, ContentReference Pinned, ReferenceRole Role, string? Via = null);

public sealed record PublishResult(ContentReference Draft, ContentReference Published, ValidationReport Report, IReadOnlyList<AffectedCharacter> Affected);

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
    /// <summary>
    /// Stores an inactive draft (ADR-004). Drafts are insert-only like published revisions, so changing a draft means
    /// saving it under a new revision id. Only a draft can be saved this way; publishing is a separate command.
    /// </summary>
    public ContentReference SaveDraft(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.Status != RevisionStatus.Draft)
            throw new AppValidationException([new("content.draft-required", "Only a draft can be saved. Publish it with content.publish, which validates it first.", revision.Reference)]);
        // A draft may be incomplete, but not malformed: validation and publishing must be able to read it.
        if (ContentValidator.EmptyEntries(revision) is { Count: > 0 } empty)
            throw new AppValidationException(empty);
        var draft = revision.RevisionId == Guid.Empty ? revision with { RevisionId = Guid.NewGuid() } : revision;
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
        ArgumentNullException.ThrowIfNull(draftReference);
        var draft = _store.FindRevision(draftReference)
            ?? throw new AppValidationException([new("content.not-found", $"Revision {draftReference.RevisionId} is not installed.", draftReference)]);
        if (draft.Status != RevisionStatus.Draft)
            throw new AppValidationException([new("content.not-a-draft", $"'{draft.Name}' is already published; published revisions are immutable. Save a new draft to change it.", draftReference)]);
        var report = ContentValidator.Validate(draft, _store);
        if (!report.CanPublish)
            throw new AppValidationException([new("content.validation-failed", $"'{draft.Name}' has {report.Errors.Count} problem(s) and was not published.", draftReference), .. report.Errors]);

        var published = draft with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };
        _store.InTransaction(() => _store.AddRevision(published));
        return new PublishResult(draftReference, published.Reference, report, AffectedCharacters(draft.ContentId));
    }

    public IReadOnlyList<ContentRevision> ListRevisions(Guid contentId) => _store.ListRevisions(contentId);

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
        var oldSheet = CharacterCalculator.Calculate(character, _store);
        var newSheet = CharacterCalculator.Calculate(character.ReplaceReference(from, to), _store);

        bool Same(Diagnostic a, Diagnostic b) =>
            a.Code == b.Code && a.EffectId == b.EffectId && (a.Content == b.Content || (a.Content == from && b.Content == to));
        var fields = newSheet.Fields
            .Select(f => (New: f, Old: oldSheet.Field(f.Field)))
            .Where(p => p.New.Value != p.Old.Value)
            .Select(p => new FieldDelta(p.New.Field, p.New.Label, p.Old.Value, p.New.Value))
            .ToList();
        var overrides = character.Overrides
            .Where(o => CharacterCalculator.IsField(o.Field) && oldSheet.Field(o.Field).ComputedValue != newSheet.Field(o.Field).ComputedValue)
            .ToList();
        return new UpdateReview(
            characterId, from, to,
            ContentDiff.Compare(before, after),
            fields,
            [.. newSheet.Diagnostics.Where(n => !oldSheet.Diagnostics.Any(o => Same(o, n)))],
            [.. oldSheet.Diagnostics.Where(o => !newSheet.Diagnostics.Any(n => Same(o, n)))],
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
