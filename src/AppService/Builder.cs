using System.Text;
using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// SPEC C-07: creation and level-up are drafts. The builder keeps the draft character in memory, and these commands
/// calculate it with the same checks as saving, without writing anything. Committing is one <c>character.create</c>
/// or <c>character.save</c>; cancelling discards the draft, so nothing is ever partly applied.
/// </summary>
public sealed partial class TomeStackApp
{
    /// <summary><c>character.preview</c>: the draft's sheet, as if it were saved. Nothing is stored.</summary>
    public CharacterView Preview(Character draft) => View(Checked(draft));

    /// <summary>
    /// <c>character.previewChoice</c>: the draft with one choice answered, refused exactly like
    /// <see cref="Choose"/>. Nothing is stored; the returned character is the next draft.
    /// </summary>
    public CharacterView PreviewChoice(PreviewChoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var draft = Checked(request.Draft ?? throw new AppValidationException([new("character.draft-required", "Send the draft character to preview a choice on.")]));
        return View(Checked(WithChoice(draft, request.Source, request.ChoiceId, request.Selected)));
    }

    /// <summary>
    /// Wave 3 item 25 (R158): an item whose v10 <c>item</c> effect needs attunement or has charges keeps its own state per entry, so
    /// its entry is named by an id, assigned here on the server the first time the entry is seen. Other entries stay id-less.
    /// </summary>
    private bool NeedsEntryId(ContentReference item) =>
        _store.FindRevision(item) is { Kind: ContentKind.Item, Status: RevisionStatus.Published } revision
        && revision.Effects.OfType<ItemEffect>().FirstOrDefault() is { } effect
        && (effect.RequiresAttunement == true || effect.Charges is not null);

    private Character WithEntryIds(Character character)
    {
        if (character.Equipment is null || character.Equipment.Any(e => e?.Item is null) || !character.Equipment.Any(e => e.Id is null && NeedsEntryId(e.Item)))
            return character;
        return character with { Equipment = [.. character.Equipment.Select(e => e.Id is null && NeedsEntryId(e.Item) ? e with { Id = Guid.NewGuid() } : e)] };
    }

    /// <summary>
    /// The character as it would be stored: the level kept in step with the class levels, then validated. Throws with
    /// every problem found. Used by saving and by the draft previews, so a preview never accepts what a save refuses.
    /// </summary>
    private Character Checked(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        // With classes recorded, the level is their sum: keep the stored value in step rather than reject a stale one.
        // (An empty entry is left for Validate to report.)
        if (character.Classes is { Count: > 0 } classes && classes.All(c => c is not null))
            character = character with { Level = classes.Sum(c => c.Level) };
        character = WithEntryIds(character);
        var problems = character.Validate().ToList();
        // R159: a second copy of an item is allowed only for items that need an entry of their own (charges or attunement), which the
        // pure Validate cannot tell without the catalog. An id given by the caller does not make a plain item repeatable.
        // R184: the rule is enforced only for copies the payload adds. A group whose every entry carries an id the stored character
        // already holds is accepted as it is, whatever the revision lookup says now (a share can omit the item's source, and an
        // update can drop the item effect), so a stored character is never left unable to save or take a play action.
        var storedIds = _store.FindCharacter(character.Id)?.Equipment?.Where(e => e?.Id is not null).Select(e => e.Id!.Value).ToHashSet();
        foreach (var group in character.Equipment.Where(e => e?.Item is not null).GroupBy(e => e.Item).Where(g => g.Count() > 1))
        {
            if (storedIds is not null && group.All(e => e.Id is { } id && storedIds.Contains(id)))
                continue;
            if (!NeedsEntryId(group.Key) && !problems.Any(p => p.Code == "character.equipment-duplicate" && p.Content == group.Key))
                problems.Add(new("character.equipment-duplicate", "An item is recorded more than once; record one entry with its quantity.", group.Key));
        }
        if (problems.Count == 0 && _store.FindCharacter(character.Id) is { } existing && existing.RulesFamily != character.RulesFamily)
            problems.Add(new("character.rules-family-changed", "Changing a saved character's rules family needs a reviewed migration and is not supported yet."));
        // A character is one package entry in every backup, and import refuses an entry over the limit: refuse it here, so a
        // character the app accepted can always be backed up and restored (session notes in a script the JSON encoder escapes
        // and long free-text fields are what can get there).
        if (problems.Count == 0 && Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(character, RulesJson.Options)) + 1 > PackageService.MaxEntryBytes)
            problems.Add(new("character.too-large", $"This character would be larger than a backup can hold ({PackageService.MaxEntryBytes / (1024 * 1024)} MB). Shorten the long text it holds (its journal, backstory and other details)."));
        if (problems.Count > 0)
            throw new AppValidationException(problems);
        return character;
    }
}
