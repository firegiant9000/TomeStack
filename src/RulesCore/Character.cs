using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

/// <summary>
/// Stored character choices and play state. Derived values are never stored here; they are
/// recalculated from <see cref="BaseAbilities"/>, <see cref="Pins"/> and <see cref="Overrides"/>.
/// </summary>
public sealed record Character : IJsonOnDeserialized
{
    /// <summary>
    /// v8 adds <see cref="Currency"/>, <see cref="Notes"/> and <see cref="PlayState.Concentration"/> (owner, 2026-10-06; LIVING_SPECS D19, D21, D22).
    /// v7 adds the active toggles to <see cref="Play"/> (M3 B2).
    /// v6 adds <see cref="Spells"/> and spent spell slots in <see cref="Play"/> (spellcasting, M2, D04).
    /// v5 adds spent hit dice, death saves and inspiration to <see cref="Play"/> (the short rest, M2).
    /// v4 adds <see cref="Play"/> (M2 item 2) and <see cref="Equipment"/> (M2 item 4). v3 adds <see cref="Classes"/> (M1 item 5) and <see cref="Choices"/> (M1 item 4).
    /// v2 adds <see cref="Level"/> and <see cref="CrossFamilyExceptions"/>. Older versions are upcast on read with the new
    /// data at its default (no lists; full hit points, nothing spent, no conditions), which is exactly their meaning.
    /// </summary>
    public const int CurrentSchemaVersion = 8;

    public const int MinLevel = 1;
    public const int MaxLevel = 20;

    /// <summary>A bound on recorded spells (untrusted input, SPEC Q-02); far above any SRD caster's list.</summary>
    public const int MaxSpells = 500;

    /// <summary>A bound on dated session notes (SPEC Q-02).</summary>
    public const int MaxNotes = 200;

    private int _schemaVersion = CurrentSchemaVersion;

    public required Guid Id { get; init; }

    public int SchemaVersion { get => _schemaVersion; init => _schemaVersion = value; }

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
        return problems;
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
        Check("play state", Play is null || Play.Resources is null || Play.Resources.Any(r => r?.ResourceId is null) || Play.Conditions is null || Play.Conditions.Any(c => c is null)
            || Play.HitDiceSpent is null || Play.HitDiceSpent.Any(h => h is null) || Play.DeathSaves is null
            || Play.SpellSlotsSpent is null || Play.SpellSlotsSpent.Any(s => s is null)
            || Play.Toggles is null || Play.Toggles.Any(t => t?.ToggleId is null)
            || (Play.Concentration is { } con && (con.Spell is null || con.Name is null)));
        return problems;
    }

    /// <summary>
    /// v1 has no level, v2 no classes, v3 no play state, v4 no hit dice, death saves or inspiration, v5 no spells or spell
    /// slots, v6 no toggles, and v7 no currency, notes or concentration; the defaults (level 1, none, full, nothing spent, all off) are exactly their meaning.
    /// </summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        if (_schemaVersion is >= 1 and < CurrentSchemaVersion)
            _schemaVersion = CurrentSchemaVersion;
    }
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

/// <summary>One item carried: an exact pin to the item revision, whether it is equipped (worn or held), and how many.</summary>
public sealed record EquipmentEntry(ContentReference Item, bool Equipped = false, int Quantity = 1)
{
    public const int MaxQuantity = 9_999;
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

/// <summary>SPEC C-06. A labeled user override applied as the final display layer.</summary>
public sealed record FieldOverride(string Field, int Value, string? Reason = null);
