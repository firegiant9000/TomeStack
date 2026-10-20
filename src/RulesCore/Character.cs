using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

/// <summary>
/// Stored character choices and play state. Derived values are never stored here; they are
/// recalculated from <see cref="BaseAbilities"/>, <see cref="Pins"/> and <see cref="Overrides"/>.
/// </summary>
public sealed record Character
{
    /// <summary>
    /// v9 adds <see cref="Details"/>, <see cref="CustomItems"/>, <see cref="CustomAttacks"/>, <see cref="DamageDefences"/>, <see cref="Companions"/>,
    /// <see cref="Xp"/>, the id, attunement and charges of an <see cref="EquipmentEntry"/>, and <see cref="PlayState.Companions"/> (Wave 2;
    /// LIVING_SPECS D42). The version written is computed (<see cref="SchemaVersion"/>): v9 only when one of them is set, otherwise
    /// <see cref="PreviousSchemaVersion"/>, so a character that uses none of them stays readable by the previous release.
    /// v8 adds <see cref="Currency"/>, <see cref="Notes"/> and <see cref="PlayState.Concentration"/> (owner, 2026-10-06; LIVING_SPECS D19, D21, D22).
    /// v7 adds the active toggles to <see cref="Play"/> (M3 B2).
    /// v6 adds <see cref="Spells"/> and spent spell slots in <see cref="Play"/> (spellcasting, M2, D04).
    /// v5 adds spent hit dice, death saves and inspiration to <see cref="Play"/> (the short rest, M2).
    /// v4 adds <see cref="Play"/> (M2 item 2) and <see cref="Equipment"/> (M2 item 4). v3 adds <see cref="Classes"/> (M1 item 5) and <see cref="Choices"/> (M1 item 4).
    /// v2 adds <see cref="Level"/> and <see cref="CrossFamilyExceptions"/>. Older versions are upcast on read with the new
    /// data at its default (no lists; full hit points, nothing spent, no conditions), which is exactly their meaning.
    /// </summary>
    public const int CurrentSchemaVersion = 9;

    /// <summary>The version written while no v9 field is set (R73): the release before v9 reads it, and the bytes are the ones it wrote.</summary>
    public const int PreviousSchemaVersion = 8;

    public const int MinLevel = 1;
    public const int MaxLevel = 20;

    // Bounds on the v9 data (untrusted input, SPEC Q-02). The ruled budget is about 100,000 characters in all; the maxima add up to:
    //   details      13 texts x 2,000                                   = 26,000
    //                2 lists x 30 entries x 80                          =  4,800
    //   items        50 x (name 80 + note 200)                          = 14,000
    //   attacks      30 x (name 80 + dice 40 + type 80 + note 200 = 400) = 12,000
    //   companions   10 x (name 80 + kind 80 + speed 80 + note 200
    //                      + 5 attacks x 400)                          = 24,400
    //                                                           total   = 81,200
    // (equipment ids and charges, XP, defences and ids add a few thousand more). A test keeps the sum at or under 100,000.
    public const int MaxDetailText = 2_000;
    public const int MaxDetailListEntries = 30;
    public const int MaxDetailListEntryLength = 80;
    public const int MaxCustomItems = 50;
    public const int MaxName = 80;
    public const int MaxShortNote = 200;
    public const int MaxCustomAttacks = 30;
    public const int MaxCompanions = 10;
    public const int MaxCompanionAttacks = 5;
    public const int MinToHit = -20;
    public const int MaxToHit = 30;
    public const int MaxXp = 1_000_000;
    public const int MaxDamageTypeLength = 80;
    public const int MaxCompanionTextLength = 80;
    public const int MaxCompanionSpeedLength = 80;
    public const int MaxCompanionArmorClass = 50;

    /// <summary>A bound on recorded spells (untrusted input, SPEC Q-02); far above any SRD caster's list.</summary>
    public const int MaxSpells = 500;

    /// <summary>A bound on dated session notes (SPEC Q-02).</summary>
    public const int MaxNotes = 200;

    private int _schemaVersion = CurrentSchemaVersion;
    private CharacterDetails? _details;
    private IReadOnlyList<CustomItem>? _customItems;
    private IReadOnlyList<CustomAttack>? _customAttacks;
    private IReadOnlyList<DamageDefence>? _damageDefences;
    private IReadOnlyList<Companion>? _companions;

    public required Guid Id { get; init; }

    /// <summary>
    /// The schema version this character is written in, computed from its data (R73): <see cref="CurrentSchemaVersion"/> when a v9 field
    /// is set, otherwise <see cref="PreviousSchemaVersion"/>. Every version from 1 reads as one of the two, since older data
    /// upcasts with the new fields at their default. A version outside 1 to <see cref="CurrentSchemaVersion"/> (data from a newer build) is
    /// kept as read, so <see cref="Validate"/> refuses it.
    /// </summary>
    public int SchemaVersion
    {
        get => _schemaVersion is >= 1 and <= CurrentSchemaVersion ? (UsesV9Fields ? CurrentSchemaVersion : PreviousSchemaVersion) : _schemaVersion;
        init => _schemaVersion = value;
    }

    /// <summary>True when any field that only schema v9 has is set. The lists and <see cref="Details"/> are normalised to absent when empty (R74), so null is the only "unset".</summary>
    [JsonIgnore]
    public bool UsesV9Fields =>
        Details is not null || CustomItems is not null || CustomAttacks is not null || DamageDefences is not null || Companions is not null || Xp is not null
        || Play?.Companions is not null
        || (Equipment?.Any(e => e is { Id: not null } or { Attuned: not null } or { ChargesSpent: not null }) ?? false);

    public required string Name { get; init; }
    public required string RulesFamily { get; init; }

    /// <summary>
    /// Total character level (1–20). With <see cref="Classes"/> recorded it must equal their sum (the service keeps it
    /// in step on save); use <see cref="TotalLevel"/> for rules.
    /// </summary>
    public int Level { get; init; } = MinLevel;

    /// <summary>
    /// Levels per class, in the order taken: the first entry is the starting class (maximum hit points at level 1).
    /// Each class reference is an active pin of its own; it need not be repeated in <see cref="Pins"/>.
    /// </summary>
    public IReadOnlyList<ClassLevel> Classes { get; init; } = [];

    /// <summary>The level rules use: the sum of <see cref="Classes"/>, or <see cref="Level"/> when no class is recorded.</summary>
    [JsonIgnore]
    public int TotalLevel => Classes.Count > 0 ? Classes.Sum(c => Math.Max(c.Level, 0)) : Level;

    /// <summary>
    /// SPEC C-01: the character's selections for <see cref="ChoiceEffect"/>s, keyed by the revision that offers the choice
    /// and its <c>choiceId</c>. Chosen content becomes active like a pin, with a "chosen from" trace.
    /// </summary>
    public IReadOnlyList<ChoiceSelection> Choices { get; init; } = [];

    /// <summary>Every content revision the character references: pins, classes, chosen options, equipment and spells. Packages and updates use this.</summary>
    public IEnumerable<ContentReference> AllReferences() =>
        Pins.Concat(Classes.Select(c => c.Class)).Concat(Choices.SelectMany(c => c.Selected)).Concat(Equipment.Select(e => e.Item)).Concat(Spells.Select(s => s.Spell)).Distinct();

    /// <summary>
    /// Character schema v6 (M2, D04): the spells the character knows or has prepared, each for one caster (the content id
    /// of the class or subclass with the <c>spellcasting</c> effect, so an update of that content keeps the list).
    /// </summary>
    public IReadOnlyList<KnownSpell> Spells { get; init; } = [];

    /// <summary>
    /// BACKLOG B06 / ARCHITECTURE step 2: deliberate use of content from another rules family, each with a recorded
    /// reason. Without a record, such content is never applied. With one, it applies under this character's own
    /// family policy, with a warning.
    /// </summary>
    public IReadOnlyList<CrossFamilyException> CrossFamilyExceptions { get; init; } = [];

    /// <summary>SPEC P-01, BACKLOG B12: the local campaign profile this character plays in (allowed sources, rules family).</summary>
    public Guid? CampaignId { get; init; }

    /// <summary>
    /// SPEC P-01 (character schema v4, M2 item 7): content the player deliberately uses although the campaign does not
    /// allow its source, each with a reason. Campaign rules never change calculation; they only warn.
    /// </summary>
    public IReadOnlyList<CampaignException> CampaignExceptions { get; init; } = [];
    public required AbilityScores BaseAbilities { get; init; }
    public IReadOnlyList<ContentReference> Pins { get; init; } = [];
    public IReadOnlyList<FieldOverride> Overrides { get; init; } = [];

    /// <summary>
    /// SPEC C-05: mutable play state (hit points, spent resources, conditions), stored separately from choices and never
    /// derived. Changed only by an explicit, confirmed command.
    /// </summary>
    public PlayState Play { get; init; } = new();

    /// <summary>
    /// SPEC C-05, M2 item 4 (character schema v4): items carried. Only equipped items apply, as active content like a
    /// pin; worn armor sets the Armor Class base. Unequipped items are still referenced, so packages carry them.
    /// </summary>
    public IReadOnlyList<EquipmentEntry> Equipment { get; init; } = [];

    /// <summary>Character schema v8 (D21): coins carried. Not derived; edited on Inventory and saved with the character.</summary>
    public Currency Currency { get; init; } = new();

    /// <summary>
    /// Character schema v8 (D22): the player's dated session notes. A journal, not rules: never calculated, never in a share
    /// package (like gap notes), kept across a snapshot restore. Printed only when ticked.
    /// </summary>
    public IReadOnlyList<SessionNote> Notes { get; init; } = [];

    /// <summary>
    /// Character schema v9 (D42): the sheet's Details page (personality, appearance, backstory, languages). Text the player keeps,
    /// never calculated. Null while every field is empty (an all-empty value is normalised to null).
    /// </summary>
    public CharacterDetails? Details { get => _details; init => _details = value?.Normalized(); }

    /// <summary>Character schema v9 (D42): carried things that are not content (gear from a paper sheet). Never calculated. Null while empty.</summary>
    public IReadOnlyList<CustomItem>? CustomItems { get => _customItems; init => _customItems = NullIfEmpty(value); }

    /// <summary>Character schema v9 (D42): attacks recorded by hand, with a typed-in to-hit bonus and damage. Never calculated. Null while empty.</summary>
    public IReadOnlyList<CustomAttack>? CustomAttacks { get => _customAttacks; init => _customAttacks = NullIfEmpty(value); }

    /// <summary>Character schema v9 (D42): damage resistances, immunities and vulnerabilities. A reminder only. Types are stored lower-case. Null while empty.</summary>
    public IReadOnlyList<DamageDefence>? DamageDefences
    {
        get => _damageDefences;
        init => _damageDefences = NullIfEmpty(value?.Select(d => d is null ? d! : d with { Type = d.Type?.ToLowerInvariant()! }).ToArray());
    }

    /// <summary>Character schema v9 (D42): familiars, mounts and the like. Their current hit points are <see cref="PlayState.Companions"/>. Null while empty.</summary>
    public IReadOnlyList<Companion>? Companions
    {
        get => _companions;
        init => _companions = NullIfEmpty(value?.Select(c => c?.Normalized()!).ToArray());
    }

    /// <summary>Character schema v9 (D42): experience points as the player records them. Never derives the level. Null while unrecorded.</summary>
    public int? Xp { get; init; }

    internal static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T>? list) => list is { Count: 0 } ? null : list;

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// SPEC C-08: when the character was archived (moved out of the character list), or null. Library organisation only:
    /// it never affects calculation. Optional and written only when set, so the character schema is unchanged; an older
    /// build keeps it as extension data and lists the character as active.
    /// </summary>
    public DateTimeOffset? ArchivedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>
    /// SPEC I-06: the same character pinned to <paramref name="to"/> wherever it referenced <paramref name="from"/>: pins,
    /// classes, choice sources and selections, and recorded exceptions. Levels, overrides and other choices are kept.
    /// </summary>
    public Character ReplaceReference(ContentReference from, ContentReference to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ContentReference Swap(ContentReference r) => r == from ? to : r;
        return this with
        {
            Pins = [.. Pins.Select(Swap)],
            Classes = [.. Classes.Select(c => c with { Class = Swap(c.Class) })],
            Choices = [.. Choices.Select(c => c with { Source = Swap(c.Source), Selected = [.. c.Selected.Select(Swap)] })],
            CrossFamilyExceptions = [.. CrossFamilyExceptions.Select(e => e with { Content = Swap(e.Content) })],
            Equipment = [.. Equipment.Select(e => e with { Item = Swap(e.Item) })],
            CampaignExceptions = [.. CampaignExceptions.Select(e => e with { Content = Swap(e.Content) })],
            Spells = [.. Spells.Select(s => s with { Spell = Swap(s.Spell) })],
            Play = Play.Concentration is { } con && con.Spell == from ? Play with { Concentration = con with { Spell = to } } : Play,
        };
    }

    public IReadOnlyList<Diagnostic> Validate()
    {
        // Untrusted JSON (SPEC Q-02) can carry null list items, which nullable annotations do not reject. Nothing else
        // can be checked safely until they are gone, so they are reported alone.
        if (EmptyEntries() is { Count: > 0 } empty)
            return empty;
        var problems = new List<Diagnostic>();
        if (SchemaVersion is < 1 or > CurrentSchemaVersion)
            problems.Add(new("character.schema-unsupported", $"Character data uses schema v{SchemaVersion}; this version of TomeStack supports v1 to v{CurrentSchemaVersion}. Update TomeStack to open it."));
        if (string.IsNullOrWhiteSpace(Name))
            problems.Add(new("character.name-required", "Character name is required."));
        if (!RulesFamilies.IsKnown(RulesFamily))
            problems.Add(new("character.rules-family-unknown", $"Rules family '{RulesFamily}' is not supported."));
        if (Level is < MinLevel or > MaxLevel)
            problems.Add(new("character.level-out-of-range", $"Level {Level} must be between {MinLevel} and {MaxLevel}."));
        foreach (var entry in Classes.Where(c => c.Level is < MinLevel or > MaxLevel))
            problems.Add(new("character.class-level-out-of-range", $"Class level {entry.Level} must be between {MinLevel} and {MaxLevel}.", entry.Class));
        foreach (var duplicate in Classes.GroupBy(c => c.Class.ContentId).Where(g => g.Count() > 1))
            problems.Add(new("character.class-duplicate", "A class is recorded more than once; record one entry with the total level in that class.", duplicate.First().Class));
        if (Classes.Count > 0 && Classes.Sum(c => c.Level) is var sum && (sum > MaxLevel || sum != Level))
        {
            problems.Add(sum > MaxLevel
                ? new("character.level-out-of-range", $"Class levels add up to {sum}; the total must be at most {MaxLevel}.")
                : new("character.level-mismatch", $"Level {Level} does not match the class levels, which add up to {sum}."));
        }
        foreach (var choice in Choices.Where(c => string.IsNullOrWhiteSpace(c.ChoiceId)))
            problems.Add(new("character.choice-id-required", "A recorded choice needs the id of the choice it answers.", choice.Source));
        foreach (var duplicate in Choices.GroupBy(c => (c.Source, c.ChoiceId)).Where(g => g.Count() > 1))
            problems.Add(new("character.choice-duplicate", $"Choice '{duplicate.Key.ChoiceId}' is recorded more than once; record all selections in one entry.", duplicate.Key.Source));
        foreach (var exception in CrossFamilyExceptions.Where(e => string.IsNullOrWhiteSpace(e.Reason)))
            problems.Add(new("character.exception-reason-required", "A cross-family exception needs a reason.", exception.Content));
        foreach (var exception in CampaignExceptions.Where(e => string.IsNullOrWhiteSpace(e.Reason)))
            problems.Add(new("character.exception-reason-required", "A campaign exception needs a reason.", exception.Content));
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var score = BaseAbilities.Get(ability);
            if (score is < 1 or > 30)
                problems.Add(new("character.ability-out-of-range", $"{ability} score {score} must be between 1 and 30."));
        }
        problems.AddRange(Play.Validate());
        foreach (var duplicate in Equipment.GroupBy(e => e.Item).Where(g => g.Count() > 1))
            problems.Add(new("character.equipment-duplicate", "An item is recorded more than once; record one entry with its quantity.", duplicate.Key));
        foreach (var entry in Equipment.Where(e => e.Quantity is < 1 or > EquipmentEntry.MaxQuantity))
            problems.Add(new("character.equipment-quantity", $"Item quantity {entry.Quantity} must be between 1 and {EquipmentEntry.MaxQuantity}.", entry.Item));
        foreach (var duplicate in Spells.GroupBy(s => (s.Caster, s.Spell.ContentId)).Where(g => g.Count() > 1))
            problems.Add(new("character.spell-duplicate", "A spell is recorded more than once for the same caster.", duplicate.First().Spell));
        if (Spells.Count > MaxSpells)
            problems.Add(new("character.spells-too-many", $"At most {MaxSpells} spells can be recorded."));
        if (!Currency.IsValid)
            problems.Add(new("character.currency-out-of-range", $"Each coin count must be between 0 and {Currency.MaxCoins}."));
        if (Notes.Count > MaxNotes)
            problems.Add(new("character.notes-too-many", $"At most {MaxNotes} session notes can be kept."));
        foreach (var note in Notes.Where(n => string.IsNullOrWhiteSpace(n.Text) || n.Text.Length > SessionNote.MaxText))
            problems.Add(new("character.note-text-invalid", $"A session note needs 1 to {SessionNote.MaxText} characters."));
        foreach (var note in Notes.Where(n => n.Date.Year is < 1900 or > 2200))
            problems.Add(new("character.note-date-invalid", "A session note's date must be between 1900 and 2200."));
        ValidateV9(problems);
        return problems;
    }

    /// <summary>
    /// The v9 bounds (D42, SPEC Q-02). Every message names the field and the limit, never the player's text, because the dispatcher logs
    /// messages and a character carries free text.
    /// </summary>
    private void ValidateV9(List<Diagnostic> problems)
    {
        if (Xp is < 0 or > MaxXp)
            problems.Add(new("character.xp-out-of-range", $"Experience points must be between 0 and {MaxXp}."));
        foreach (var (entry, number) in Equipment.Select((e, i) => (e, i + 1)).Where(p => p.e.ChargesSpent is < 0 or > EquipmentEntry.MaxChargesSpent))
            problems.Add(new("character.equipment-charges-out-of-range", $"Charges spent on equipment entry {number} must be between 0 and {EquipmentEntry.MaxChargesSpent}.", entry.Item));
        if (Equipment.Any(e => e.Id == Guid.Empty))
            problems.Add(new("character.equipment-id-missing", "An equipment entry has an empty id; leave the id out or give it a real one."));
        if (Equipment.Where(e => e.Id is not null).GroupBy(e => e.Id).Any(g => g.Count() > 1))
            problems.Add(new("character.equipment-id-duplicate", "Two equipment entries share an id."));

        if (Details is { } details)
        {
            foreach (var (field, text) in details.Texts().Where(t => t.Text is { Length: > MaxDetailText }))
                problems.Add(new("character.details-text-too-long", $"The {field} text must be at most {MaxDetailText} characters."));
            foreach (var (field, list) in new[] { ("languages", details.Languages), ("tools and other", details.ToolsAndOther) })
            {
                if (list is null)
                    continue;
                if (list.Count > MaxDetailListEntries)
                    problems.Add(new("character.details-list-too-many", $"At most {MaxDetailListEntries} entries can be kept under {field}."));
                else if (list.Any(e => string.IsNullOrWhiteSpace(e) || e.Length > MaxDetailListEntryLength))
                    problems.Add(new("character.details-list-entry-invalid", $"Each entry under {field} needs 1 to {MaxDetailListEntryLength} characters."));
            }
        }

        if (CustomItems is { } items)
        {
            if (items.Count > MaxCustomItems)
                problems.Add(new("character.custom-items-too-many", $"At most {MaxCustomItems} custom items can be kept."));
            else
            {
                foreach (var (item, number) in items.Select((c, i) => (c, i + 1)))
                {
                    if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > MaxName)
                        problems.Add(new("character.custom-item-name-invalid", $"Custom item {number} needs a name of 1 to {MaxName} characters."));
                    if (item.Note is { Length: > MaxShortNote })
                        problems.Add(new("character.custom-item-note-too-long", $"The note on custom item {number} must be at most {MaxShortNote} characters."));
                    if (item.Quantity is < 1 or > EquipmentEntry.MaxQuantity)
                        problems.Add(new("character.custom-item-quantity", $"The quantity of custom item {number} must be between 1 and {EquipmentEntry.MaxQuantity}."));
                }
                if (items.Any(c => c.Id == Guid.Empty))
                    problems.Add(new("character.custom-item-id-missing", "A custom item has no id."));
                if (items.GroupBy(c => c.Id).Any(g => g.Count() > 1))
                    problems.Add(new("character.custom-item-duplicate", "Two custom items share an id."));
            }
        }

        if (CustomAttacks is { } attacks)
        {
            if (attacks.Count > MaxCustomAttacks)
                problems.Add(new("character.custom-attacks-too-many", $"At most {MaxCustomAttacks} custom attacks can be kept."));
            else
                ValidateAttacks(attacks, "Custom attack", problems);
        }

        if (DamageDefences is { } defences)
        {
            if (defences.Any(d => !DamageTypes.All.Contains(d.Type, StringComparer.OrdinalIgnoreCase)))
                problems.Add(new("character.damage-defence-type-unknown", "A damage resistance, immunity or vulnerability names a damage type TomeStack does not know."));
            if (defences.GroupBy(d => (d.Type, d.Kind)).Any(g => g.Count() > 1))
                problems.Add(new("character.damage-defence-duplicate", "A damage type is recorded more than once for the same kind of defence."));
        }

        if (Companions is { } companions)
        {
            if (companions.Count > MaxCompanions)
                problems.Add(new("character.companions-too-many", $"At most {MaxCompanions} companions can be kept."));
            else
            {
                foreach (var (companion, number) in companions.Select((c, i) => (c, i + 1)))
                {
                    if (string.IsNullOrWhiteSpace(companion.Name) || companion.Name.Length > MaxName)
                        problems.Add(new("character.companion-name-invalid", $"Companion {number} needs a name of 1 to {MaxName} characters."));
                    if (companion.Kind is { Length: > MaxCompanionTextLength } || companion.Speed is { Length: > MaxCompanionSpeedLength } || companion.Note is { Length: > MaxShortNote })
                        problems.Add(new("character.companion-text-too-long", $"Companion {number} allows a kind of at most {MaxCompanionTextLength} characters, a speed of at most {MaxCompanionSpeedLength} and a note of at most {MaxShortNote}."));
                    if (companion.ArmorClass is < 0 or > MaxCompanionArmorClass)
                        problems.Add(new("character.companion-armor-class-out-of-range", $"The Armor Class of companion {number} must be between 0 and {MaxCompanionArmorClass}."));
                    if (companion.HitPointMaximum is < 0 or > PlayState.MaxHitPoints)
                        problems.Add(new("character.companion-hit-points-out-of-range", $"The hit point maximum of companion {number} must be between 0 and {PlayState.MaxHitPoints}."));
                    if (companion.Attacks is { } companionAttacks)
                    {
                        if (companionAttacks.Count > MaxCompanionAttacks)
                            problems.Add(new("character.companion-attacks-too-many", $"Companion {number} can have at most {MaxCompanionAttacks} attacks."));
                        else
                            ValidateAttacks(companionAttacks, $"Companion {number} attack", problems);
                    }
                }
                if (companions.Any(c => c.Id == Guid.Empty))
                    problems.Add(new("character.companion-id-missing", "A companion has no id."));
                if (companions.GroupBy(c => c.Id).Any(g => g.Count() > 1))
                    problems.Add(new("character.companion-duplicate", "Two companions share an id."));
            }
        }

        if (Play.Companions is { } companionPlay && companionPlay.Any(p => Companions is null || Companions.All(c => c.Id != p.Id)))
            problems.Add(new("character.companion-play-unknown", "Hit points are recorded for a companion the character does not have."));
    }

    private static void ValidateAttacks(IReadOnlyList<CustomAttack> attacks, string label, List<Diagnostic> problems)
    {
        foreach (var (attack, number) in attacks.Select((a, i) => (a, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(attack.Name) || attack.Name.Length > MaxName)
                problems.Add(new("character.custom-attack-name-invalid", $"{label} {number} needs a name of 1 to {MaxName} characters."));
            if (attack.Note is { Length: > MaxShortNote })
                problems.Add(new("character.custom-attack-note-too-long", $"The note on {label.ToLowerInvariant()} {number} must be at most {MaxShortNote} characters."));
            if (attack.DamageType is { Length: > MaxDamageTypeLength })
                problems.Add(new("character.custom-attack-type-too-long", $"The damage type of {label.ToLowerInvariant()} {number} must be at most {MaxDamageTypeLength} characters."));
            if (attack.ToHit is < MinToHit or > MaxToHit)
                problems.Add(new("character.custom-attack-to-hit-out-of-range", $"The to-hit bonus of {label.ToLowerInvariant()} {number} must be between {MinToHit} and +{MaxToHit}."));
            if (attack.Damage is not null && !DiceExpression.TryParse(attack.Damage, out _, out _))
                problems.Add(new("character.custom-attack-damage-invalid", $"The damage of {label.ToLowerInvariant()} {number} must be dice such as 1d8+3 (at most {DiceLimits.MaxLength} characters, {DiceLimits.MaxDice} dice, {DiceLimits.MaxSides} sides)."));
        }
        if (attacks.Any(a => a.Id == Guid.Empty))
            problems.Add(new("character.custom-attack-id-missing", $"A {label.ToLowerInvariant()} has no id."));
        if (attacks.GroupBy(a => a.Id).Any(g => g.Count() > 1))
            problems.Add(new("character.custom-attack-duplicate", $"Two {label.ToLowerInvariant()}s share an id."));
    }

    private List<Diagnostic> EmptyEntries()
    {
        var problems = new List<Diagnostic>();
        void Check(string list, bool empty)
        {
            if (empty)
                problems.Add(new("character.empty-entry", $"The character's {list} list contains an empty entry."));
        }
        Check("pins", Pins is null || Pins.Any(p => p is null));
        Check("classes", Classes is null || Classes.Any(c => c?.Class is null));
        Check("choices", Choices is null || Choices.Any(c => c?.Source is null || c.Selected is null || c.Selected.Any(s => s is null)));
        Check("cross-family exceptions", CrossFamilyExceptions is null || CrossFamilyExceptions.Any(e => e?.Content is null));
        Check("overrides", Overrides is null || Overrides.Any(o => o?.Field is null));
        Check("equipment", Equipment is null || Equipment.Any(e => e?.Item is null));
        Check("campaign exceptions", CampaignExceptions is null || CampaignExceptions.Any(e => e?.Content is null));
        Check("spells", Spells is null || Spells.Any(s => s?.Spell is null));
        Check("notes", Notes is null || Notes.Any(n => n is null || n.Text is null));
        Check("currency", Currency is null);
        Check("details", Details is { } d && ((d.Languages?.Any(l => l is null) ?? false) || (d.ToolsAndOther?.Any(l => l is null) ?? false)));
        Check("custom items", CustomItems?.Any(i => i is null) ?? false);
        Check("custom attacks", CustomAttacks?.Any(a => a is null) ?? false);
        Check("damage defences", DamageDefences?.Any(x => x is null || x.Type is null) ?? false);
        Check("companions", Companions?.Any(c => c is null || (c.Attacks?.Any(a => a is null) ?? false)) ?? false);
        Check("companion play state", Play?.Companions?.Any(c => c is null) ?? false);
        Check("play state", Play is null || Play.Resources is null || Play.Resources.Any(r => r?.ResourceId is null) || Play.Conditions is null || Play.Conditions.Any(c => c is null)
            || Play.HitDiceSpent is null || Play.HitDiceSpent.Any(h => h is null) || Play.DeathSaves is null
            || Play.SpellSlotsSpent is null || Play.SpellSlotsSpent.Any(s => s is null)
            || Play.Toggles is null || Play.Toggles.Any(t => t?.ToggleId is null)
            || (Play.Concentration is { } con && (con.Spell is null || con.Name is null)));
        return problems;
    }

    // Reading needs no upcast step: v1 has no level, v2 no classes, v3 no play state, v4 no hit dice, death saves or inspiration, v5 no
    // spells or spell slots, v6 no toggles, v7 no currency, notes or concentration, and v8 none of the v9 fields; the defaults (level 1,
    // none, full, nothing spent, all off, absent) are exactly their meaning, and SchemaVersion is computed from what is set.
}

public sealed record AbilityScores(int Str, int Dex, int Con, int Int, int Wis, int Cha)
{
    public int Get(Ability ability) => ability switch
    {
        Ability.Str => Str,
        Ability.Dex => Dex,
        Ability.Con => Con,
        Ability.Int => Int,
        Ability.Wis => Wis,
        Ability.Cha => Cha,
        _ => throw new ArgumentOutOfRangeException(nameof(ability)),
    };
}

/// <summary>Levels in one class: an exact pin to the class revision, and the number of levels taken in it.</summary>
public sealed record ClassLevel(ContentReference Class, int Level);

/// <summary>The options a character picked for one choice: <paramref name="Source"/> is the revision offering it.</summary>
public sealed record ChoiceSelection(ContentReference Source, string ChoiceId, IReadOnlyList<ContentReference> Selected);

/// <summary>A recorded decision to use one revision whose source the character's campaign does not allow (SPEC P-01).</summary>
public sealed record CampaignException(ContentReference Content, string Reason, DateTimeOffset? RecordedAt = null);

/// <summary>A recorded, per-character decision to use one pinned revision outside its rules families (B06).</summary>
public sealed record CrossFamilyException(ContentReference Content, string Reason, DateTimeOffset? RecordedAt = null);

/// <summary>
/// One item carried: an exact pin to the item revision, whether it is equipped (worn or held), and how many. Schema v9 (D42) adds
/// <paramref name="Id"/> (names this entry, for a later change to find it), <paramref name="Attuned"/> and <paramref name="ChargesSpent"/>
/// (recorded as the player says; never calculated). All three are null until set. "Not attuned" (false) and "no charges spent" (0)
/// are the same as unset, so they are stored as null and do not make the character v9; readers treat null as false and 0.
/// </summary>
public sealed record EquipmentEntry(ContentReference Item, bool Equipped = false, int Quantity = 1, Guid? Id = null, bool? Attuned = null, int? ChargesSpent = null)
{
    private readonly bool? _attuned = Attuned == false ? null : Attuned;
    private readonly int? _chargesSpent = ChargesSpent == 0 ? null : ChargesSpent;

    public bool? Attuned { get => _attuned; init => _attuned = value == false ? null : value; }

    public int? ChargesSpent { get => _chargesSpent; init => _chargesSpent = value == 0 ? null : value; }

    public const int MaxQuantity = 9_999;
    public const int MaxChargesSpent = 1_000;
}

/// <summary>
/// A spell the character knows or has prepared for one caster. <paramref name="Caster"/> is the content id of the class
/// (or subclass) with the <c>spellcasting</c> effect. <paramref name="Prepared"/> matters for prepared casters (a wizard's
/// spellbook holds unprepared spells); a known caster's spells are always ready.
/// </summary>
public sealed record KnownSpell(Guid Caster, ContentReference Spell, bool Prepared = true);

/// <summary>Character schema v8 (D21): coins. Plain counts; TomeStack never converts between them.</summary>
public sealed record Currency(int Cp = 0, int Sp = 0, int Ep = 0, int Gp = 0, int Pp = 0)
{
    public const int MaxCoins = 1_000_000;

    [JsonIgnore]
    public bool IsValid => new[] { Cp, Sp, Ep, Gp, Pp }.All(c => c is >= 0 and <= MaxCoins);
}

/// <summary>Character schema v8 (D22): one dated session note. <see cref="Date"/> is the session's day; <see cref="CreatedAt"/> when it was written.</summary>
public sealed record SessionNote(Guid Id, DateOnly Date, string Text, DateTimeOffset CreatedAt)
{
    public const int MaxText = 4_000;
}

/// <summary>
/// Character schema v9 (D42): the sheet's Details page. All free text the player keeps, never calculated. Each text is at most
/// <see cref="Character.MaxDetailText"/> characters; <paramref name="Languages"/> and <paramref name="ToolsAndOther"/> are short entries.
/// </summary>
public sealed record CharacterDetails(
    string? Alignment = null, string? PersonalityTraits = null, string? Ideals = null, string? Bonds = null, string? Flaws = null,
    Appearance? Appearance = null, string? Backstory = null, string? AlliesAndOrganisations = null,
    IReadOnlyList<string>? Languages = null, IReadOnlyList<string>? ToolsAndOther = null)
{
    /// <summary>The same details with empty text and empty lists made absent, or null when nothing is left (R74), so clearing the page makes the character v8 again.</summary>
    internal CharacterDetails? Normalized()
    {
        var result = this with
        {
            Alignment = Text(Alignment),
            PersonalityTraits = Text(PersonalityTraits),
            Ideals = Text(Ideals),
            Bonds = Text(Bonds),
            Flaws = Text(Flaws),
            Appearance = Appearance?.Normalized(),
            Backstory = Text(Backstory),
            AlliesAndOrganisations = Text(AlliesAndOrganisations),
            Languages = Character.NullIfEmpty(Languages),
            ToolsAndOther = Character.NullIfEmpty(ToolsAndOther),
        };
        return result == new CharacterDetails() ? null : result;
    }

    internal static string? Text(string? value) => string.IsNullOrEmpty(value) ? null : value;

    internal IEnumerable<(string Field, string? Text)> Texts()
    {
        yield return ("alignment", Alignment);
        yield return ("personality traits", PersonalityTraits);
        yield return ("ideals", Ideals);
        yield return ("bonds", Bonds);
        yield return ("flaws", Flaws);
        yield return ("backstory", Backstory);
        yield return ("allies and organisations", AlliesAndOrganisations);
        if (Appearance is not { } look)
            yield break;
        yield return ("age", look.Age);
        yield return ("height", look.Height);
        yield return ("weight", look.Weight);
        yield return ("eyes", look.Eyes);
        yield return ("skin", look.Skin);
        yield return ("hair", look.Hair);
    }
}

/// <summary>Character schema v9 (D42): how the character looks, as free text (each at most <see cref="Character.MaxDetailText"/> characters).</summary>
public sealed record Appearance(string? Age = null, string? Height = null, string? Weight = null, string? Eyes = null, string? Skin = null, string? Hair = null)
{
    internal Appearance? Normalized()
    {
        var result = new Appearance(CharacterDetails.Text(Age), CharacterDetails.Text(Height), CharacterDetails.Text(Weight), CharacterDetails.Text(Eyes), CharacterDetails.Text(Skin), CharacterDetails.Text(Hair));
        return result == new Appearance() ? null : result;
    }
}

/// <summary>Character schema v9 (D42): a carried thing that is not content (gear copied from a paper sheet). Never calculated; <paramref name="Equipped"/> is a reminder.</summary>
public sealed record CustomItem(Guid Id, string Name, int Quantity, string? Note, bool Equipped);

/// <summary>
/// Character schema v9 (D42): an attack the player recorded by hand. <paramref name="ToHit"/> is typed in, never calculated;
/// <paramref name="Damage"/> is a dice expression (<see cref="DiceExpression"/>) or null.
/// </summary>
public sealed record CustomAttack(Guid Id, string Name, int ToHit, string? Damage, string? DamageType, string? Note);

/// <summary>Character schema v9 (D42): the damage types of both SRDs (a closed list; the same in 5.1 and 5.2.1).</summary>
public static class DamageTypes
{
    public static readonly IReadOnlyList<string> All =
        ["acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison", "psychic", "radiant", "slashing", "thunder"];

    /// <summary>
    /// Content schema v10: maps a damage type written in any case to its lowercase name in <see cref="All"/>. Exact apart
    /// from case (no trimming): anything else, including null, is refused with <paramref name="canonical"/> empty.
    /// </summary>
    public static bool TryNormalize(string? name, out string canonical)
    {
        canonical = All.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)) ?? "";
        return canonical.Length > 0;
    }
}

/// <summary>Character schema v9 (D42): how a character treats one damage type. A reminder only: damage commands do not use it.</summary>
public enum DamageDefenceKind { Resistance, Immunity, Vulnerability }

/// <summary>Character schema v9 (D42): one damage resistance, immunity or vulnerability. <paramref name="Type"/> is one of <see cref="DamageTypes.All"/>, stored lower-case.</summary>
public sealed record DamageDefence(string Type, DamageDefenceKind Kind);

/// <summary>
/// Character schema v9 (D42): a familiar, mount or similar. Its current hit points are in <see cref="PlayState.Companions"/>.
/// Nothing here is calculated.
/// </summary>
public sealed record Companion(Guid Id, string Name, string? Kind, int? ArmorClass, int HitPointMaximum, string? Speed, IReadOnlyList<CustomAttack>? Attacks, string? Note)
{
    internal Companion Normalized() => this with { Attacks = Character.NullIfEmpty(Attacks) };
}

/// <summary>Character schema v9 (D42): a companion's current hit points (null: at the maximum) and temporary hit points.</summary>
public sealed record CompanionPlay(Guid Id, int? Current, int Temporary);

/// <summary>SPEC C-06. A labeled user override applied as the final display layer.</summary>
public sealed record FieldOverride(string Field, int Value, string? Reason = null);
