using System.Text.Json;
using System.Text.Json.Serialization;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

public enum GapTargetKind { Feature, Field }

public enum GapNoteStatus { Open, Resolved }

/// <summary>
/// What a gap note is about: a feature (content id, and optionally one of its effects) or a calculated field.
/// <paramref name="Label"/> is the feature or field name when the note was written, so the note stays readable after the
/// feature is removed or updated. The service fills it in.
/// </summary>
public sealed record GapTarget(GapTargetKind Kind, Guid? ContentId = null, string? EffectId = null, string? FieldId = null, string? Label = null);

/// <summary>
/// M3 B3: a session feedback note. At the table the player writes down where TomeStack fell short on a feature or field
/// (a missing mechanic, a wrong number, a manual step), to fix or accept later. Notes are stored only in the local
/// database and are never transmitted. They leave the machine only inside a personal backup, never in a share package
/// (docs/features/gap-notes.md).
/// </summary>
public sealed record GapNote
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxTextLength = 2_000;
    public const int MaxLabelLength = 200;
    public const int MaxNotesPerCharacter = 500;

    public required Guid Id { get; init; }
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required Guid CharacterId { get; init; }
    public required GapTarget Target { get; init; }
    public required string Text { get; init; }
    public GapNoteStatus Status { get; init; } = GapNoteStatus.Open;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>Shape checks only; never quotes the text, which may describe private homebrew.</summary>
    public IReadOnlyList<Diagnostic> Validate()
    {
        var problems = new List<Diagnostic>();
        if (SchemaVersion is < 1 or > CurrentSchemaVersion)
            problems.Add(new("gap.schema-unsupported", $"Gap note data uses schema v{SchemaVersion}; this version supports v1 to v{CurrentSchemaVersion}. Update TomeStack to open it."));
        if (string.IsNullOrWhiteSpace(Text) || Text.Trim().Length > MaxTextLength)
            problems.Add(new("gap.text-required", $"A gap note needs text of 1 to {MaxTextLength} characters."));
        if (Target is null || !Enum.IsDefined(Target.Kind))
            problems.Add(new("gap.target-required", "A gap note is about a feature or a field."));
        else if (Target.Kind == GapTargetKind.Feature ? Target.ContentId is null || Target.FieldId is not null : string.IsNullOrWhiteSpace(Target.FieldId) || Target.ContentId is not null || Target.EffectId is not null)
            problems.Add(new("gap.target-invalid", "A feature note names a content id (and optionally an effect); a field note names only a field."));
        else if (Target.Label is { Length: > MaxLabelLength } || Target.EffectId is { Length: > MaxLabelLength } || Target.FieldId is { Length: > MaxLabelLength })
            problems.Add(new("gap.target-invalid", $"Target names are at most {MaxLabelLength} characters."));
        if (!Enum.IsDefined(Status))
            problems.Add(new("gap.status-unknown", "A gap note is open or resolved."));
        return problems;
    }
}

/// <summary><c>gap.add</c>: <paramref name="Target"/>'s label is ignored; the service takes it from the sheet.</summary>
public sealed record AddGapNoteRequest(Guid CharacterId, GapTarget Target, string Text);

public sealed record GapNoteStatusRequest(Guid Id, GapNoteStatus Status);

/// <param name="Confirm">Must be true: deleting a note cannot be undone.</param>
public sealed record DeleteGapNoteRequest(Guid Id, bool Confirm = false);

public sealed partial class TomeStackApp
{
    /// <summary><c>gap.list</c>: the character's notes, open first, then newest first. Writes nothing.</summary>
    public IReadOnlyList<GapNote> ListGapNotes(Guid characterId)
    {
        if (_store.FindCharacter(characterId) is null)
            throw new AppValidationException([new("character.not-found", $"Character {characterId} does not exist.")]);
        return [.. _store.ListGapNotes(characterId).OrderBy(n => n.Status).ThenByDescending(n => n.CreatedAt).ThenBy(n => n.Id)];
    }

    /// <summary>
    /// <c>gap.add</c>: a note about a feature or field that is on the character's sheet now (<c>gap.target-not-found</c>
    /// otherwise). Sent only from the note's "Save note" button.
    /// </summary>
    public GapNote AddGapNote(AddGapNoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var character = _store.FindCharacter(request.CharacterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {request.CharacterId} does not exist.")]);
        var now = _time.GetUtcNow();
        var note = new GapNote
        {
            Id = Guid.NewGuid(),
            CharacterId = character.Id,
            Target = request.Target!, // untrusted JSON may omit it; Validate reports gap.target-required
            Text = request.Text?.Trim() ?? "",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var problems = note.Validate().ToList();
        if (problems.Count == 0)
        {
            var sheet = CharacterCalculator.Calculate(character, _store);
            var label = LabelOf(sheet, note.Target);
            if (label is null)
                problems.Add(new("gap.target-not-found", "That feature or field is not on this character's sheet."));
            else
                note = note with { Target = note.Target with { Label = label[..Math.Min(label.Length, GapNote.MaxLabelLength)] } };
        }
        if (problems.Count == 0 && _store.ListGapNotes(character.Id).Count >= GapNote.MaxNotesPerCharacter)
            problems.Add(new("gap.too-many", $"A character has at most {GapNote.MaxNotesPerCharacter} gap notes. Delete resolved ones first."));
        if (problems.Count > 0)
            throw new AppValidationException(problems);
        _store.InTransaction(() => _store.SaveGapNote(note));
        return note;
    }

    /// <summary><c>gap.setStatus</c>: marks a note resolved (fixed or accepted) or open again.</summary>
    public GapNote SetGapNoteStatus(GapNoteStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var note = _store.FindGapNote(request.Id)
            ?? throw new AppValidationException([new("gap.not-found", $"Gap note {request.Id} does not exist.")]);
        if (!Enum.IsDefined(request.Status))
            throw new AppValidationException([new("gap.status-unknown", "A gap note is open or resolved.")]);
        var updated = note with { Status = request.Status, UpdatedAt = _time.GetUtcNow() };
        _store.InTransaction(() => _store.SaveGapNote(updated));
        return updated;
    }

    /// <summary><c>gap.delete</c>: refused without <c>confirm: true</c> (<c>gap.confirmation-required</c>).</summary>
    public void DeleteGapNote(DeleteGapNoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("gap.confirmation-required", "Deleting a gap note needs confirmation; it cannot be undone.")]);
        if (_store.FindGapNote(request.Id) is null)
            throw new AppValidationException([new("gap.not-found", $"Gap note {request.Id} does not exist.")]);
        _store.InTransaction(() => _store.DeleteGapNote(request.Id));
    }

    private static string? LabelOf(CharacterSheet sheet, GapTarget target)
    {
        if (target.Kind == GapTargetKind.Field)
            return sheet.Fields.FirstOrDefault(f => f.Field == target.FieldId)?.Label;
        var feature = (sheet.Features ?? []).FirstOrDefault(f => f.Content.ContentId == target.ContentId);
        if (feature is null)
            return null;
        if (target.EffectId is null)
            return feature.Name;
        return feature.Effects.FirstOrDefault(e => e.Id == target.EffectId) is { } effect
            ? $"{feature.Name}: {effect.Label ?? effect.Id}"
            : null;
    }
}
