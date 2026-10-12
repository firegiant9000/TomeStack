using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>Why a snapshot was taken: by hand, or automatically just before a restore (so the restore can be undone).</summary>
public enum SnapshotReason { Manual, BeforeRestore }

/// <summary>
/// M5 slice 8 (B08): a fixed copy of a character: its choices, pins, levels, equipment, spells, overrides and play state.
/// Insert-only (database migration v7). It never carries the archive mark (SPEC C-08), and no package, share or library
/// backup includes it (owner decision LIVING_SPECS D14).
/// </summary>
public sealed record CharacterSnapshot(Guid Id, Guid CharacterId, DateTimeOffset CreatedAt, SnapshotReason Reason, string? Label, Character Character);

/// <summary>What the snapshot list shows: never the whole character.</summary>
public sealed record SnapshotSummary(Guid Id, Guid CharacterId, DateTimeOffset CreatedAt, SnapshotReason Reason, string? Label, string Name, int Level);

/// <param name="Items">One page, newest first.</param>
/// <param name="HasMore">Whether older snapshots exist: ask again with <c>before</c> = the last item's id.</param>
public sealed record SnapshotPage(IReadOnlyList<SnapshotSummary> Items, bool HasMore);

public sealed record SnapshotRequest(Guid CharacterId, string? Label = null);

public sealed record RestorePreviewRequest(Guid CharacterId, Guid SnapshotId);

/// <param name="Confirm">Must be true: only the preview's "Restore" button sends it.</param>
public sealed record RestoreSnapshotRequest(Guid Token, bool Confirm = false);

/// <summary>
/// <c>character.restorePreview</c>: what restoring the snapshot would change, shown like an update review. Nothing
/// changes. <paramref name="Token"/> is good for one restore of exactly this character state.
/// </summary>
/// <param name="Fields">Calculated values that change (now → after the restore).</param>
/// <param name="NewDiagnostics">Problems the restored character would have, such as content it pins that is not installed (<c>content.missing</c>).</param>
/// <param name="Added">References (pins, classes, choices, equipment, spells) the snapshot has and the character now does not.</param>
/// <param name="Removed">References the character has now and the snapshot does not.</param>
/// <param name="PlayChanges">Whether the play state (hit points, spent uses, conditions, toggles) differs.</param>
/// <param name="NameAfter">The name the restore brings back, when it differs from the current one.</param>
/// <param name="ExceptionsChange">Whether the recorded cross-family exceptions differ.</param>
/// <param name="CampaignWarnings">Campaign warnings the restored character would have that it does not have now (its campaign stays as it is).</param>
/// <param name="CurrencyNow">The coins now, set only when the restore changes them (coins roll back with the snapshot; D21).</param>
/// <param name="CurrencyAfter">The coins the restore brings back, set only when they differ from the current ones.</param>
public sealed record RestorePreview(
    Guid Token,
    SnapshotSummary Snapshot,
    IReadOnlyList<FieldDelta> Fields,
    IReadOnlyList<Diagnostic> NewDiagnostics,
    IReadOnlyList<ContentReference> Added,
    IReadOnlyList<ContentReference> Removed,
    bool PlayChanges,
    string? NameAfter = null,
    bool ExceptionsChange = false,
    IReadOnlyList<Diagnostic>? CampaignWarnings = null,
    Currency? CurrencyNow = null,
    Currency? CurrencyAfter = null);

/// <param name="Undo">The snapshot of the character as it was just before, taken in the same transaction.</param>
public sealed record RestoreResult(CharacterView View, SnapshotSummary Undo);

public sealed partial class TomeStackApp
{
    public const int MaxSnapshotLabelLength = 200;

    /// <summary>A restore preview's token → what it previewed. One use each (<see cref="RestoreSnapshot"/> removes it).</summary>
    private readonly ConcurrentDictionary<Guid, PendingRestore> _pendingRestores = new();

    private sealed record PendingRestore(Guid CharacterId, Guid SnapshotId, string CharacterHash);

    /// <summary><c>character.snapshot</c> (M5 slice 8): takes a snapshot of the saved character, by hand (owner decision: never automatically, except the undo snapshot of a restore).</summary>
    public SnapshotSummary Snapshot(SnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var character = FindCharacterOrThrow(request.CharacterId);
        var label = request.Label?.Trim();
        if (label is { Length: > MaxSnapshotLabelLength })
            throw new AppValidationException([new("snapshot.label-too-long", $"A snapshot label has at most {MaxSnapshotLabelLength} characters.")]);
        var snapshot = NewSnapshot(character, SnapshotReason.Manual, string.IsNullOrEmpty(label) ? null : label);
        _store.InTransaction(() => _store.AddSnapshot(snapshot));
        return Summarize(snapshot);
    }

    /// <summary>
    /// <c>character.snapshots</c>: a page of the character's snapshots, newest first (at most
    /// <see cref="Persistence.SqliteStore.MaxListedSnapshots"/>). <paramref name="before"/> is the id of the oldest snapshot
    /// already shown: the page then holds the ones stored before it. Snapshots are never removed, so this pages back to the first.
    /// </summary>
    public SnapshotPage Snapshots(Guid characterId, Guid? before = null)
    {
        FindCharacterOrThrow(characterId);
        if (before is { } cursor && _store.FindSnapshot(cursor)?.CharacterId != characterId)
            throw new AppValidationException([new("snapshot.not-found", $"Snapshot {before} is not a snapshot of this character.")]);
        var rows = _store.ListSnapshots(characterId, before, Persistence.SqliteStore.MaxListedSnapshots + 1);
        return new SnapshotPage([.. rows.Take(Persistence.SqliteStore.MaxListedSnapshots).Select(Summarize)], rows.Count > Persistence.SqliteStore.MaxListedSnapshots);
    }

    /// <summary>
    /// <c>character.restorePreview</c>: the calculated values, references and play state a restore would change, and the
    /// problems the restored character would have. Writes nothing. The token is bound to the character as it is now, so
    /// a change made after the preview makes the restore refuse.
    /// </summary>
    public RestorePreview PreviewRestore(RestorePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = FindCharacterOrThrow(request.CharacterId);
        var snapshot = _store.FindSnapshot(request.SnapshotId);
        if (snapshot is null || snapshot.CharacterId != current.Id)
            throw new AppValidationException([new("snapshot.not-found", $"Snapshot {request.SnapshotId} is not a snapshot of this character.")]);
        var restored = Restored(current, snapshot);
        var none = new ContentReference(Guid.Empty, Guid.Empty);
        var delta = SheetChanges(current, _store, restored, _store, none, none);
        var now = current.AllReferences().ToHashSet();
        var then = restored.AllReferences().ToHashSet();
        var warningsNow = CampaignOf(current, CharacterCalculator.Calculate(current, _store))?.Warnings ?? [];
        var warningsThen = CampaignOf(restored, CharacterCalculator.Calculate(restored, _store))?.Warnings ?? [];
        // One pending restore per character (review fix): a newer preview replaces an older, unused one.
        foreach (var stale in _pendingRestores.Where(p => p.Value.CharacterId == current.Id).Select(p => p.Key).ToList())
            _pendingRestores.TryRemove(stale, out _);
        var token = Guid.NewGuid();
        _pendingRestores[token] = new(current.Id, snapshot.Id, Hash(current));
        return new RestorePreview(
            token, Summarize(snapshot), delta.Fields, delta.NewDiagnostics,
            [.. then.Where(r => !now.Contains(r))], [.. now.Where(r => !then.Contains(r))],
            TempJson(current.Play) != TempJson(restored.Play),
            restored.Name != current.Name ? restored.Name : null,
            TempJson(current.CrossFamilyExceptions) != TempJson(restored.CrossFamilyExceptions),
            [.. warningsThen.Where(w => !warningsNow.Any(n => n.Code == w.Code && n.Content == w.Content))],
            restored.Currency != current.Currency ? current.Currency : null,
            restored.Currency != current.Currency ? restored.Currency : null);
    }

    /// <summary>
    /// <c>character.restoreSnapshot</c>: restores the previewed snapshot. In one transaction it first takes an undo snapshot
    /// of the character as it is, then writes the snapshot's state, so a restore can itself be restored. The token is used
    /// once: a repeated request finds none and changes nothing. The archive mark stays as it is now (SPEC C-08), and content
    /// the snapshot pins that is not installed shows as <c>content.missing</c> on the sheet.
    /// </summary>
    public RestoreResult RestoreSnapshot(RestoreSnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("snapshot.confirm-required", "Show the restore preview and confirm it first; nothing was changed.")]);
        if (!_pendingRestores.TryRemove(request.Token, out var pending))
            throw new AppValidationException([new("snapshot.token-unknown", "This restore was already done, or never previewed. Nothing was changed.")]);
        var current = FindCharacterOrThrow(pending.CharacterId);
        if (Hash(current) != pending.CharacterHash)
            throw new AppValidationException([new("snapshot.character-changed", $"'{current.Name}' changed after the preview. Preview the restore again to see what it would do.")]);
        var snapshot = _store.FindSnapshot(pending.SnapshotId)
            ?? throw new AppValidationException([new("snapshot.not-found", $"Snapshot {pending.SnapshotId} is not installed.")]);
        var undo = NewSnapshot(current, SnapshotReason.BeforeRestore, $"Before restoring {Describe(snapshot)}");
        CharacterView? view = null;
        _store.InTransaction(() =>
        {
            _store.AddSnapshot(undo);
            view = SaveWithPlay(Restored(current, snapshot));
        });
        return new RestoreResult(view!, Summarize(undo));
    }

    /// <summary>
    /// The snapshot's state for this character, with what is not the character's own state kept as it is now: its id, the
    /// archive mark (a restore never archives or unarchives), and its campaign membership with the campaign's recorded
    /// exceptions (review fix: a restore never moves a character between campaigns, or back into a deleted one).
    /// </summary>
    private static Character Restored(Character current, CharacterSnapshot snapshot) =>
        snapshot.Character with
        {
            Id = current.Id,
            ArchivedAt = current.ArchivedAt,
            CampaignId = current.CampaignId,
            CampaignExceptions = current.CampaignExceptions,
            Notes = current.Notes, // D22: a journal, kept like the archive mark; coins and play state roll back with the rest
        };

    private CharacterSnapshot NewSnapshot(Character character, SnapshotReason reason, string? label) =>
        new(Guid.NewGuid(), character.Id, _time.GetUtcNow(), reason, label, character with { ArchivedAt = null, Notes = [] }); // D22: the session journal is never copied into a snapshot (a restore keeps the current notes)

    private static SnapshotSummary Summarize(CharacterSnapshot s) =>
        new(s.Id, s.CharacterId, s.CreatedAt, s.Reason, s.Label, s.Character.Name, s.Character.TotalLevel);

    private static string Describe(CharacterSnapshot s) =>
        s.Label is { } label ? $"'{(label.Length > 60 ? label[..60] + "…" : label)}'" : $"the snapshot of {s.CreatedAt:yyyy-MM-dd HH:mm} UTC";

    private static string TempJson<T>(T value) => JsonSerializer.Serialize(value, RulesJson.Compact);

    private static string Hash(Character character) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TempJson(character))));
}
