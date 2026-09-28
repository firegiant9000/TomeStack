using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// <c>character.archivePreview</c>: what archiving the character does. Nothing is removed: its gap notes, its campaign
/// and every revision it uses stay, and a full library backup still includes it.
/// </summary>
/// <param name="Campaign">The character's campaign, which keeps it as a member.</param>
public sealed record ArchivePreview(Guid CharacterId, string Name, bool AlreadyArchived, int GapNotes, string? Campaign);

/// <param name="Confirm">Must be true: the UI shows <c>character.archivePreview</c> first.</param>
public sealed record ArchiveRequest(Guid CharacterId, bool Confirm = false);

public sealed partial class TomeStackApp
{
    /// <summary><c>character.archivePreview</c> (SPEC C-08): what archiving would do. Nothing changes.</summary>
    public ArchivePreview PreviewArchive(Guid characterId)
    {
        var character = FindCharacterOrThrow(characterId);
        var campaign = character.CampaignId is { } id ? _store.FindCampaign(id)?.Name : null;
        return new(character.Id, character.Name, character.ArchivedAt is not null, _store.ListGapNotes(character.Id).Count, campaign);
    }

    /// <summary>
    /// <c>character.archive</c> (SPEC C-08): moves the character out of the character list, after the confirmation that
    /// said what happens. It is not a delete: the character, its play state, gap notes and campaign membership are kept
    /// unchanged, nothing it references is removed, and full library backups include it. Refused without <c>confirm</c>.
    /// </summary>
    public CharacterSummary Archive(ArchiveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("character.archive-confirmation-required", "Archiving hides the character from the list. Confirm to archive it; nothing is deleted.")]);
        var character = FindCharacterOrThrow(request.CharacterId);
        if (character.ArchivedAt is not null)
            throw new AppValidationException([new("character.already-archived", $"'{character.Name}' is already archived.")]);
        return SetArchived(character, _time.GetUtcNow());
    }

    /// <summary><c>character.unarchive</c> (SPEC C-08): brings an archived character back to the list, as it was.</summary>
    public CharacterSummary Unarchive(Guid characterId)
    {
        var character = FindCharacterOrThrow(characterId);
        if (character.ArchivedAt is null)
            throw new AppValidationException([new("character.not-archived", $"'{character.Name}' is not archived.")]);
        return SetArchived(character, null);
    }

    /// <summary>
    /// Writes only the archive mark: choices, play state and <see cref="Character.UpdatedAt"/> stay as stored, so archiving
    /// and unarchiving leave the character exactly as it was.
    /// </summary>
    private CharacterSummary SetArchived(Character character, DateTimeOffset? archivedAt)
    {
        var saved = character with { ArchivedAt = archivedAt };
        _store.InTransaction(() => _store.SaveCharacter(saved));
        return Summary(saved);
    }

    private static CharacterSummary Summary(Character character) =>
        new(character.Id, character.Name, character.RulesFamily, character.UpdatedAt, character.ArchivedAt);

    private Character FindCharacterOrThrow(Guid characterId) =>
        _store.FindCharacter(characterId) ?? throw new AppValidationException([new("character.not-found", $"Character {characterId} does not exist.")]);
}
