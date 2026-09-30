using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// <c>content.sandbox</c>: a draft class or subclass (inline, or a stored draft by reference) tried at a level on an
/// unsaved copy of a character, or on a blank character of <paramref name="RulesFamily"/>.
/// </summary>
/// <param name="Level">The level in the draft class, or in the class a draft subclass joins. Default: 1 for a class, the subclass choice's level for a subclass.</param>
/// <param name="Abilities">A blank character's ability scores (default 10 each); a copy keeps its own.</param>
public sealed record SandboxRequest(
    ContentRevision? Revision = null, ContentReference? Reference = null, Guid? CharacterId = null, string? RulesFamily = null, int? Level = null,
    AbilityScores? Abilities = null);

/// <param name="View">The unsaved copy and its sheet, calculated with the draft as if published. Never stored.</param>
/// <param name="Draft">The draft's reference as used on the copy.</param>
/// <param name="Changes">For a copy of a saved character: every calculated sheet field the draft changes (not resource maximums or class columns). Empty for a blank one.</param>
/// <param name="Validation">The draft's validation report: a draft may be incomplete, and the sandbox still calculates it.</param>
public sealed record SandboxView(CharacterView View, ContentReference Draft, IReadOnlyList<FieldDelta> Changes, ValidationReport Validation);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// M5 slice 3 (B03; owner decision LIVING_SPECS D14): "Try it" in the studio. One draft counts as published for one
    /// calculation (<see cref="DraftOverlayCatalog"/>) on an in-memory copy with a new id. It writes nothing: no revision,
    /// character, play state or gap note changes, and the copy is never saved. Only published revisions affect saved
    /// characters.
    /// </summary>
    public SandboxView Sandbox(SandboxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var draft = SandboxDraft(request);
        var problems = new List<Diagnostic>();
        if (draft.Kind is not (ContentKind.Class or ContentKind.Subclass))
            problems.Add(new("sandbox.kind", $"Try it works on a class or a subclass; '{draft.Name}' is {draft.Kind.ToString().ToLowerInvariant()} content.", draft.Reference));
        if (request.Level is < Character.MinLevel or > Character.MaxLevel)
            problems.Add(new("sandbox.level", $"The level must be {Character.MinLevel} to {Character.MaxLevel}.", draft.Reference));

        Character? saved = null;
        Character copy;
        if (request.CharacterId is { } characterId)
        {
            if (request.RulesFamily is not null)
                problems.Add(new("sandbox.scope", "Name a character or a rules family for a blank character, not both.", draft.Reference));
            saved = _store.FindCharacter(characterId)
                ?? throw new AppValidationException([new("character.not-found", $"Character {characterId} does not exist.")]);
            // A new id: the copy can never be mistaken for, or saved over, the character it came from.
            copy = saved with { Id = Guid.NewGuid() };
        }
        else
        {
            var family = request.RulesFamily ?? draft.RulesFamilies.FirstOrDefault() ?? "";
            if (!RulesFamilies.IsKnown(family))
                problems.Add(new("rules-family.unknown", $"Rules family '{family}' is not supported.", draft.Reference));
            copy = new Character { Id = Guid.NewGuid(), Name = "Sandbox character", RulesFamily = family, BaseAbilities = request.Abilities ?? new(10, 10, 10, 10, 10, 10) };
        }
        if (!draft.RulesFamilies.Contains(copy.RulesFamily))
            problems.Add(new("sandbox.rules-family", $"'{draft.Name}' supports {string.Join(", ", draft.RulesFamilies)}, not {copy.RulesFamily}.", draft.Reference));
        if (problems.Count > 0)
            throw new AppValidationException(problems);

        copy = Checked(Place(copy, draft, request.Level));
        var overlay = new DraftOverlayCatalog(_store, draft);
        var sheet = CharacterCalculator.Calculate(copy, overlay);
        var changes = new List<FieldDelta>();
        if (saved is not null)
        {
            var before = CharacterCalculator.Calculate(saved, _store);
            changes.AddRange(sheet.Fields
                .Select(f => (New: f, Old: before.Field(f.Field)))
                .Where(p => p.New.Value != p.Old.Value)
                .Select(p => new FieldDelta(p.New.Field, p.New.Label, p.Old.Value, p.New.Value)));
        }
        return new SandboxView(new CharacterView(copy, sheet, CampaignOf(copy, sheet, overlay)), draft.Reference, changes, ContentValidator.Validate(draft, _store));
    }

    /// <summary>The draft to try: an unsaved one (given a revision id if it has none), or a stored draft. Published content needs no sandbox.</summary>
    private ContentRevision SandboxDraft(SandboxRequest request)
    {
        if ((request.Revision is null) == (request.Reference is null))
            throw new AppValidationException([new("sandbox.draft-required", "Send the draft to try, either unsaved or by the reference of a stored draft.")]);
        var draft = request.Revision
            ?? _store.FindRevision(request.Reference!)
            ?? throw new AppValidationException([new("content.not-found", $"Revision {request.Reference!.RevisionId} is not installed.", request.Reference)]);
        if (draft.Status != RevisionStatus.Draft)
            throw new AppValidationException([new("sandbox.draft-required", $"'{draft.Name}' is published; use it on a character directly.", draft.Reference)]);
        if (ContentValidator.EmptyEntries(draft) is { Count: > 0 } empty)
            throw new AppValidationException(empty);
        return draft.RevisionId == Guid.Empty ? draft with { RevisionId = Guid.NewGuid() } : draft;
    }

    /// <summary>
    /// Puts the draft on the copy. Every reference to another revision of the same content becomes the draft. A class gets
    /// <paramref name="level"/> levels (added as a further class when the copy does not have it). A subclass is selected in
    /// the choice it extends, on the class that offers it, which is added at <paramref name="level"/> when missing.
    /// </summary>
    private Character Place(Character copy, ContentRevision draft, int? level)
    {
        foreach (var other in copy.AllReferences().Where(r => r.ContentId == draft.ContentId && r != draft.Reference).ToList())
            copy = copy.ReplaceReference(other, draft.Reference);

        if (draft.Kind == ContentKind.Class)
        {
            // A copy that has the class keeps its level unless one is chosen. Otherwise the class is added, and a character
            // with no class levels recorded (a pinned class) keeps its level and no longer pins it (review fix).
            if (copy.Classes.Any(c => c.Class == draft.Reference))
                return copy with { Classes = [.. copy.Classes.Select(c => c.Class == draft.Reference ? c with { Level = level ?? c.Level } : c)] };
            var levels = level ?? (copy.Classes.Count == 0 ? copy.Level : Character.MinLevel);
            return copy with { Pins = [.. copy.Pins.Where(p => p != draft.Reference)], Classes = [.. copy.Classes, new(draft.Reference, levels)] };
        }

        // A subclass is offered to characters only through the choice it extends: a choice that lists another revision of
        // it as a declared option pins that revision, and so never accepts the draft (review fix).
        if (draft.ExtendsChoice is not { } extends)
        {
            throw new AppValidationException([copy.Choices.Any(c => c.Selected.Contains(draft.Reference))
                ? new("sandbox.subclass-declared-option", $"The character has '{draft.Name}' as a declared option of a choice, which names one exact revision; a new revision is offered there only once the class names it. Choose \"Offered in the choice\" to try it.", draft.Reference)
                : new("sandbox.subclass-unplaced", $"'{draft.Name}' is not offered in any class's choice yet. Choose \"Offered in the choice\" first.", draft.Reference)]);
        }
        bool OnExtendedChoice(ChoiceSelection c) => c.Source.ContentId == extends.ContentId && c.ChoiceId == extends.ChoiceId;
        // A selection of an older revision in some other choice (the draft now extends another one) no longer applies.
        copy = copy with
        {
            Choices = [.. copy.Choices.SelectMany(c =>
                OnExtendedChoice(c) || !c.Selected.Contains(draft.Reference) ? [c]
                : c.Selected.All(s => s == draft.Reference) ? []
                : new[] { c with { Selected = [.. c.Selected.Where(s => s != draft.Reference)] } })],
        };

        var classEntry = copy.Classes.FirstOrDefault(c => c.Class.ContentId == extends.ContentId);
        // The selection the calculator reads is the one on the class revision the copy has (review fix).
        // With a class entry only that exact revision counts: a selection keyed to another revision of the class is never
        // read, so rewriting it would leave the draft unapplied. Without one (a feature's choice) any revision of the source will do.
        var existing = classEntry is not null
            ? copy.Choices.FirstOrDefault(c => c.Source == classEntry.Class && c.ChoiceId == extends.ChoiceId)
            : copy.Choices.FirstOrDefault(OnExtendedChoice);
        if (existing is not null)
        {
            copy = copy with { Choices = [.. copy.Choices.Select(c => c == existing ? c with { Selected = [draft.Reference] } : c)] };
            if (level is not { } chosen)
                return copy;
            if (classEntry is null)
                throw new AppValidationException([new("sandbox.level-class", $"'{draft.Name}' joins a choice that is not a class's (for example a feature's), so it has no class level to set. Leave the level empty to try it at the character's levels.", draft.Reference)]);
            return WithClassLevel(copy, extends.ContentId, chosen);
        }
        var classRevision = classEntry is not null
            ? _store.FindRevision(classEntry.Class)
            : _store.ListRevisions(extends.ContentId).LastOrDefault(r => r.Status == RevisionStatus.Published && r.RulesFamilies.Contains(copy.RulesFamily));
        if (classRevision is not { Kind: ContentKind.Class })
            throw new AppValidationException([new("sandbox.subclass-class", $"'{draft.Name}' joins a choice of content that is not a published class for {copy.RulesFamily} (for example a feature's choice). Try it on a saved character who has that choice.", draft.Reference)]);
        var needed = level ?? classRevision.Effects.OfType<ChoiceEffect>().FirstOrDefault(c => c.ChoiceId == extends.ChoiceId)?.Level ?? Character.MinLevel;
        copy = classEntry is not null
            ? WithClassLevel(copy, extends.ContentId, Math.Max(needed, level is null ? classEntry.Level : needed))
            : copy with
            {
                // A character with no class levels recorded keeps its level, and no longer pins the class separately.
                Pins = [.. copy.Pins.Where(p => p.ContentId != extends.ContentId)],
                Classes = [.. copy.Classes, new(classRevision.Reference, copy.Classes.Count == 0 && level is null ? Math.Max(needed, copy.Level) : needed)],
            };
        return copy with { Choices = [.. copy.Choices, new(classRevision.Reference, extends.ChoiceId, [draft.Reference])] };
    }

    private static Character WithClassLevel(Character copy, Guid classContentId, int level) =>
        copy with { Classes = [.. copy.Classes.Select(c => c.Class.ContentId == classContentId ? c with { Level = level } : c)] };
}
