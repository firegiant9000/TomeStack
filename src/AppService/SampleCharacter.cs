using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <param name="Confirm">Must be true: the UI asks before it removes the sample.</param>
public sealed record DeleteSampleRequest(Guid CharacterId, bool Confirm = false);

public sealed partial class TomeStackApp
{
    /// <summary>The origin mark (<c>characters.origin</c>) of the bundled sample character (Wave 3, D95).</summary>
    public const string SampleOrigin = "sample";

    private const string SampleName = "Wren Ashdown";

    // 2014 rules (srd-5.1) content ids; revisions are resolved through the store, never named here.
    private static readonly Guid SampleCleric = Guid.Parse("51c00000-0000-4000-8000-000000401000");
    private static readonly Guid SampleLifeDomain = Guid.Parse("51c00000-0000-4000-8000-000000401500");
    private static readonly Guid SampleDwarf = Guid.Parse("51c10000-0000-4000-8000-000000000101");
    private static readonly Guid SampleHillDwarf = Guid.Parse("51c10000-0000-4000-8000-000000000102");
    private static readonly Guid SampleAcolyte = Guid.Parse("51c00000-0000-4000-8000-000000000002");

    // Fallback gear (no bundled guide): mace, scale mail, shield, light crossbow.
    private static readonly Guid[] SampleFallbackGear =
    [
        Guid.Parse("51c00000-0000-4000-8000-000000200007"), Guid.Parse("51c00000-0000-4000-8000-000000201006"),
        Guid.Parse("51c00000-0000-4000-8000-000000201013"), Guid.Parse("51c00000-0000-4000-8000-000000200011"),
    ];

    private static readonly string[] SampleEquipped = ["Mace", "Scale mail", "Shield"];
    private static readonly string[] SampleSkills = ["Medicine", "Persuasion"];
    private static readonly string[] SampleCantrips = ["Guidance", "Sacred Flame", "Spare the Dying"];
    private static readonly string[] SamplePrepared = ["Healing Word", "Guiding Bolt", "Shield of Faith", "Sanctuary", "Aid", "Prayer of Healing"];

    /// <summary>
    /// <c>character.createSample</c> (Wave 3, D95): saves the bundled sample character (a level 3 Hill Dwarf Cleric of the Life
    /// Domain, 2014 rules) as an ordinary character, marked with the <c>sample</c> origin. Refused while any character, archived
    /// or not, already has that mark.
    /// </summary>
    public CharacterView CreateSample()
    {
        if (_store.ListCharacters().Any(c => _store.CharacterOrigin(c.Id) == SampleOrigin))
            throw new AppValidationException([new("character.sample-exists", "The sample character already exists.")]);
        var view = SaveCharacter(BuildSample());
        if (!_store.SetCharacterOrigin(view.Character.Id, SampleOrigin))
            throw new InvalidOperationException("The sample character could not be marked.");
        return view;
    }

    /// <summary>
    /// <c>character.deleteSample</c>: removes the sample character, archived or not, with its gap notes. Refused without
    /// <c>confirm</c>, for any character that is not the sample, and for a sample with snapshots (snapshots are insert-only, so
    /// such a sample can only be archived).
    /// </summary>
    public void DeleteSample(DeleteSampleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw new AppValidationException([new("character.confirm-required", "Confirm to delete the sample character.")]);
        _ = FindCharacterOrThrow(request.CharacterId);
        if (_store.CharacterOrigin(request.CharacterId) != SampleOrigin)
            throw new AppValidationException([new("character.not-sample", "Only the sample character can be deleted this way.")]);
        try
        {
            if (_store.SnapshotCount(request.CharacterId) > 0)
                throw new SampleHasSnapshotsException();
            if (!_store.DeleteSampleCharacter(request.CharacterId))
                throw new AppValidationException([new("character.not-found", $"Character {request.CharacterId} does not exist.")]);
        }
        catch (SampleHasSnapshotsException)
        {
            throw new AppValidationException([new("character.sample-has-snapshots", "This sample has snapshots, so it can only be archived")]);
        }
    }

    private Character BuildSample()
    {
        ContentReference Newest(Guid contentId) =>
            _store.NewestPublished(contentId)?.Reference ?? throw new InvalidOperationException($"Bundled content {contentId} is not installed.");

        var cleric = Newest(SampleCleric);
        var dwarf = Newest(SampleDwarf);
        var draft = new Character
        {
            Id = Guid.NewGuid(),
            Name = SampleName,
            RulesFamily = RulesFamilies.Srd51,
            Level = 3,
            Classes = [new(cleric, 3)],
            BaseAbilities = new(14, 10, 13, 8, 15, 12),
            Pins = [dwarf, Newest(SampleAcolyte)],
            Choices =
            [
                new(dwarf, "dwarf-subrace", [Newest(SampleHillDwarf)]),
                new(cleric, "cleric-subclass", [Newest(SampleLifeDomain)]),
            ],
        };

        // The class skills are picked by name from what the sheet offers.
        var offered = CharacterCalculator.Calculate(draft, _store).Choices?.FirstOrDefault(c => c.Source == cleric && c.ChoiceId == "cleric-skills")
            ?? throw new InvalidOperationException("The Cleric offers no skill choice.");
        var skills = offered.Options.Where(o => _store.FindRevision(o) is { } r && SampleSkills.Any(s => r.Name.EndsWith(": " + s, StringComparison.OrdinalIgnoreCase))).ToList();
        if (skills.Count != SampleSkills.Length)
            throw new InvalidOperationException("The sample's skills are not offered.");

        return draft with
        {
            Choices = [.. draft.Choices, new(cleric, "cleric-skills", skills)],
            Spells = SampleSpells(),
            Equipment = SampleGear(cleric, out var coins),
            Currency = coins,
        };
    }

    private List<KnownSpell> SampleSpells()
    {
        var spells = _store.ListRevisionsInOrder()
            .Where(r => r.Kind == ContentKind.Spell && r.Status == RevisionStatus.Published && r.RulesFamilies.Contains(RulesFamilies.Srd51))
            .GroupBy(r => r.ContentId).Select(g => g.Last())
            .ToDictionary(r => r.Name, r => r.Reference, StringComparer.OrdinalIgnoreCase);
        ContentReference Find(string name) => spells.TryGetValue(name, out var found) ? found : throw new InvalidOperationException($"Sample spell '{name}' is not installed.");
        return
        [
            .. SampleCantrips.Select(n => new KnownSpell(SampleCleric, Find(n), true)),
            .. SamplePrepared.Select(n => new KnownSpell(SampleCleric, Find(n), true)),
        ];
    }

    /// <summary>The Cleric guide's option "a" for every pick, expanded to equipment; the mace, scale mail and shield are equipped.</summary>
    private List<EquipmentEntry> SampleGear(ContentReference cleric, out Currency coins)
    {
        List<EquipmentEntry> entries;
        coins = new();
        if (_store.GuidesFor(cleric.ContentId) is [var guide, ..] && guide.Effects.OfType<StartingEquipmentEffect>().FirstOrDefault() is { } kit)
        {
            var result = KitExpansion.Expand(kit, [.. kit.Picks.Select(p => new KitChoice(p.Id, "a"))], _store);
            if (result.Problems.Count > 0)
                throw new InvalidOperationException("The Cleric guide's first options do not expand.");
            entries = [.. result.Equipment];
            coins = result.Coins;
        }
        else
        {
            entries = [.. SampleFallbackGear.Select(id => new EquipmentEntry(Newest(id)))];
        }

        ContentReference Newest(Guid id) => _store.NewestPublished(id)?.Reference ?? throw new InvalidOperationException($"Bundled content {id} is not installed.");
        return [.. entries.Select(e => _store.FindRevision(e.Item) is { } item && SampleEquipped.Contains(item.Name, StringComparer.OrdinalIgnoreCase) ? e with { Equipped = true } : e)];
    }
}
