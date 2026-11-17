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

    /// <summary>
    /// MECH-X item 3 (R51a): an entry also needs an id when any entry of its item carries mods (the mods are what make copies differ), and
    /// keeps it when the mods are later removed (R158: ids are never taken away).
    /// </summary>
    private bool NeedsEntryId(EquipmentEntry entry, IReadOnlyList<EquipmentEntry> all) =>
        NeedsEntryId(entry.Item) || all.Any(e => e?.Item == entry.Item && e.Mods is { Count: > 0 });

    private Character WithEntryIds(Character character)
    {
        if (character.Equipment is null || character.Equipment.Any(e => e?.Item is null) || !character.Equipment.Any(e => e.Id is null && NeedsEntryId(e, character.Equipment)))
            return character;
        return character with { Equipment = [.. character.Equipment.Select(e => e.Id is null && NeedsEntryId(e, character.Equipment) ? e with { Id = Guid.NewGuid() } : e)] };
    }

    /// <summary>
    /// MECH-X item 3 (R51): whether <paramref name="mod"/> is a published item revision carrying one <c>itemMod</c> whose target fits
    /// <paramref name="host"/>, a published item with a weapon or armor effect (<c>any</c> fits either, never neither).
    /// </summary>
    private bool IsModFor(ContentReference host, ContentReference mod)
    {
        if (_store.FindRevision(mod) is not { Kind: ContentKind.Item, Status: RevisionStatus.Published, SchemaVersion: >= ContentRevision.ItemModSchemaVersion } modRevision
            || modRevision.Effects.OfType<ItemModEffect>().ToList() is not [{ } effect]
            || _store.FindRevision(host) is not { Kind: ContentKind.Item, Status: RevisionStatus.Published } hostRevision)
            return false;
        var weapon = hostRevision.Effects.OfType<WeaponEffect>().Any();
        var armor = hostRevision.Effects.OfType<ArmorEffect>().Any();
        return effect.AppliesTo switch
        {
            ModTarget.Weapon => weapon,
            ModTarget.Armor => armor,
            _ => weapon || armor,
        };
    }

    /// <summary>
    /// The character as it would be stored: the level kept in step with the class levels, then validated. Throws with
    /// every problem found. Used by saving and by the draft previews, so a preview never accepts what a save refuses.
    /// </summary>
    /// <param name="checkMods">
    /// MECH-X item 3 (R51): also refuse a mod that is not a published mod fitting its host. Only <c>character.save</c> and
    /// <c>character.create</c> pass <see langword="true"/>; play, rests, charge changes and snapshot restores never do, so a stored
    /// character whose mod was later republished without its <c>itemMod</c> can still be played, rested and restored.
    /// </param>
    private Character Checked(Character character, bool checkMods = false)
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
            if (!group.Any(e => NeedsEntryId(e, character.Equipment)) && !problems.Any(p => p.Code == "character.equipment-duplicate" && p.Content == group.Key))
                problems.Add(new("character.equipment-duplicate", "An item is recorded more than once; record one entry with its quantity.", group.Key));
        }
        if (checkMods)
        {
            // The exemption: a (host content, mod content) pair the stored character already holds on any entry is never refused, whatever the
            // catalog says now (ids and revision ids ignored), so an update that dropped the itemMod, a share that omits the mod's source, or an
            // id-less imported character can still be saved. Messages carry the entry's position and the mod's id only.
            var storedPairs = _store.FindCharacter(character.Id)?.Equipment?
                .Where(e => e?.Item is not null && e.Mods is not null)
                .SelectMany(e => e.Mods!.Where(m => m is not null).Select(m => (e.Item.ContentId, m.ContentId)))
                .ToHashSet();
            for (var i = 0; i < character.Equipment.Count; i++)
            {
                var entry = character.Equipment[i];
                if (entry?.Item is null || entry.Mods is null)
                    continue;
                foreach (var mod in entry.Mods.Where(m => m is not null))
                {
                    if (storedPairs?.Contains((entry.Item.ContentId, mod.ContentId)) == true || IsModFor(entry.Item, mod))
                        continue;
                    problems.Add(new("character.equipment-mod-invalid", $"A mod on equipment entry {i + 1} is not a published mod for that item.", mod));
                    break;
                }
            }
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

    /// <summary>
    /// <c>character.levelUpOptions</c> (D99): the classes a saved character can take its next level in. Its own classes come first, in
    /// the order it has them; the others follow by name. Another rules family's classes and those a campaign does not allow
    /// (<see cref="ContentOption.AllowedInCampaign"/>, as <see cref="ListContent"/> marks them) are left out; the character's own classes
    /// stay. A character at level 20 has no next level, so the list is empty (the sheet offers no level-up there either).
    /// A class the character already has uses its pinned revision, a new one the newest published revision.
    /// </summary>
    public IReadOnlyList<LevelUpOption> LevelUpOptions(Guid characterId)
    {
        var character = _store.FindCharacter(characterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {characterId} does not exist.")]);
        if (character.Classes.Sum(c => c.Level) >= Character.MaxLevel)
            return [];

        var sheet = CharacterCalculator.Calculate(character, _store);
        var owned = character.Classes
            .Select(c => (Entry: c, Revision: _store.FindRevision(c.Class)))
            .Where(c => c.Revision is { Kind: ContentKind.Class })
            .ToList();
        // The multiclass rule applies into and out of a class: what the classes the character has need counts against a new one.
        var outgoing = owned.SelectMany(c => ShortfallPhrases(sheet, c.Revision!)).ToList();

        var options = owned
            .Select(c => new LevelUpOption(
                c.Revision!.ContentId, c.Revision.Name, true, c.Entry.Level + 1,
                GainsAt(c.Revision, c.Entry.Level + 1, c.Entry.Class == character.Classes[0].Class, sheet), []))
            .ToList();
        var have = owned.Select(c => c.Revision!.ContentId).ToHashSet();
        // A campaign deleted since the character was saved no longer restricts anything.
        var campaignId = character.CampaignId is { } id && _store.FindCampaign(id) is not null ? id : (Guid?)null;
        var others = ListContent(character.RulesFamily, campaignId)
            .Where(o => o.Kind == ContentKind.Class && o.Compatible && !o.Superseded && o.AllowedInCampaign != false && !have.Contains(o.Reference.ContentId))
            .Select(o => _store.FindRevision(o.Reference))
            .OfType<ContentRevision>()
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new LevelUpOption(
                r.ContentId, r.Name, false, 1, GainsAt(r, 1, startingClass: owned.Count == 0, sheet),
                [.. ShortfallPhrases(sheet, r).Concat(outgoing).Distinct(StringComparer.Ordinal)]));
        return [.. options, .. others];
    }

    /// <summary>
    /// What class level <paramref name="level"/> of <paramref name="revision"/> gives, by name: the content the class grants, then
    /// what the subclass(es) the character has picked for it grant (the engine gates them by the class's level), then the choices the
    /// class or a subclass presents at that level, labelled as the builder labels them. Only grants that apply on their own
    /// (automatic, always) count, as the engine's. A grant or choice with no level is a level 1 one.
    /// </summary>
    private IReadOnlyList<string> GainsAt(ContentRevision revision, int level, bool startingClass, CharacterSheet sheet)
    {
        var owners = new List<ContentRevision> { revision };
        owners.AddRange((sheet.Choices ?? []).Where(c => c.Source == revision.Reference)
            .SelectMany(c => c.Selected)
            .Select(_store.FindRevision)
            .OfType<ContentRevision>()
            .Where(r => r.Kind == ContentKind.Subclass));
        bool Applies(ClassEntry? onlyAs) => onlyAs is null || (onlyAs == ClassEntry.StartingClass) == startingClass;

        var grants = owners.SelectMany(o => o.Effects.OfType<GrantEffect>())
            .Where(g => g.Grant == GrantKind.Content && g.Content is not null && g.Automation == AutomationStatus.Automatic && g.Timing == EffectTiming.Always
                && (g.Level ?? 1) == level && Applies(g.OnlyAs))
            .Select(g => _store.FindRevision(g.Content!)?.Name)
            .OfType<string>();
        var choices = owners.SelectMany(o => o.Effects.OfType<ChoiceEffect>().Select(c => (Owner: o, Choice: c)))
            .Where(x => (x.Choice.Level ?? 1) == level && Applies(x.Choice.OnlyAs))
            .Select(x => ChoiceLabel(x.Owner.Name, x.Choice));
        return [.. grants.Concat(choices).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The label the builder shows for a choice: its own short text, else "{source}: choose {count}".</summary>
    private static string ChoiceLabel(string sourceName, ChoiceEffect choice)
    {
        var text = choice.Text?.Trim();
        return text is { Length: > 0 and <= 40 } && !".!?".Contains(text[^1]) && !text.Contains('\n')
            ? text
            : $"{sourceName}: choose {choice.Count}";
    }

    /// <summary>
    /// The unmet multiclass prerequisites of <paramref name="revision"/>, one phrase each: "Intelligence 13 (you have 8)", or for an
    /// either/or group "Strength 13 or Dexterity 13 (you have 10 and 12)".
    /// </summary>
    private static IEnumerable<string> ShortfallPhrases(CharacterSheet sheet, ContentRevision revision) =>
        CharacterCalculator.MulticlassShortfalls(sheet, revision).Select(group =>
            $"{string.Join(" or ", group.Select(s => $"{AbilityLabel(s.Label)} {s.Minimum}"))} (you have {string.Join(" and ", group.Select(s => s.Actual))})");

    /// <summary>The sheet labels an ability's score field "Strength score"; the player is told "Strength".</summary>
    private static string AbilityLabel(string fieldLabel) =>
        fieldLabel.EndsWith(" score", StringComparison.Ordinal) ? fieldLabel[..^" score".Length] : fieldLabel;
}

/// <summary>
/// One class a character can take its next level in (D99). <see cref="Gains"/> names what that level gives; <see cref="Unmet"/> is
/// player-ready wording for the multiclass prerequisites not met (empty for a class the character already has).
/// </summary>
public sealed record LevelUpOption(Guid ContentId, string Name, bool Current, int NextLevel, IReadOnlyList<string> Gains, IReadOnlyList<string> Unmet);
