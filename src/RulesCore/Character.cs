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
    /// v3 adds <see cref="Classes"/> (M1 item 5) and <see cref="Choices"/> (M1 item 4). v2 adds <see cref="Level"/> and <see cref="CrossFamilyExceptions"/>.
    /// Older versions are upcast on read with the new lists empty, which is exactly their meaning.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    public const int MinLevel = 1;
    public const int MaxLevel = 20;

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

    /// <summary>Every content revision the character references: pins, classes and chosen options. Packages and updates use this.</summary>
    public IEnumerable<ContentReference> AllReferences() =>
        Pins.Concat(Classes.Select(c => c.Class)).Concat(Choices.SelectMany(c => c.Selected)).Distinct();

    /// <summary>
    /// BACKLOG B06 / ARCHITECTURE step 2: deliberate use of content from another rules family, each with a recorded
    /// reason. Without a record, such content is never applied. With one, it applies under this character's own
    /// family policy, with a warning.
    /// </summary>
    public IReadOnlyList<CrossFamilyException> CrossFamilyExceptions { get; init; } = [];
    public Guid? CampaignId { get; init; }
    public required AbilityScores BaseAbilities { get; init; }
    public IReadOnlyList<ContentReference> Pins { get; init; } = [];
    public IReadOnlyList<FieldOverride> Overrides { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }

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
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var score = BaseAbilities.Get(ability);
            if (score is < 1 or > 30)
                problems.Add(new("character.ability-out-of-range", $"{ability} score {score} must be between 1 and 30."));
        }
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
        return problems;
    }

    /// <summary>v1 has no level and v2 no classes; the defaults (level 1, none) are exactly their meaning.</summary>
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

/// <summary>A recorded, per-character decision to use one pinned revision outside its rules families (B06).</summary>
public sealed record CrossFamilyException(ContentReference Content, string Reason, DateTimeOffset? RecordedAt = null);

/// <summary>SPEC C-06. A labeled user override applied as the final display layer.</summary>
public sealed record FieldOverride(string Field, int Value, string? Reason = null);
