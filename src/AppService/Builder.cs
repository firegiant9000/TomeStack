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
        var problems = character.Validate().ToList();
        if (problems.Count == 0 && _store.FindCharacter(character.Id) is { } existing && existing.RulesFamily != character.RulesFamily)
            problems.Add(new("character.rules-family-changed", "Changing a saved character's rules family needs a reviewed migration and is not supported yet."));
        // A character is one package entry in every backup, and import refuses an entry over the limit: refuse it here, so a
        // character the app accepted can always be backed up and restored (session notes in a script the JSON encoder escapes
        // and long free-text fields are what can get there).
        if (problems.Count == 0 && Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(character, RulesJson.Options)) + 1 > PackageService.MaxEntryBytes)
            problems.Add(new("character.too-large", $"This character would be larger than a backup can hold ({PackageService.MaxEntryBytes / (1024 * 1024)} MB). Shorten its session notes or other long text."));
        if (problems.Count > 0)
            throw new AppValidationException(problems);
        return character;
    }
}
