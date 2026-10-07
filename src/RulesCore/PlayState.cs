using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

/// <summary>
/// SPEC C-05, character schema v4 (v5 adds hit dice, death saves and inspiration): what changes during play. Never derived and never changed by calculation; only an
/// explicit, confirmed command writes it (ARCHITECTURE "commands vs calculation"). The defaults mean "fresh": at the hit
/// point maximum, no temporary hit points, nothing spent, no conditions.
/// </summary>
public sealed record PlayState
{
    public const int MaxHitPoints = 10_000;
    public const int MaxExhaustion = 6;

    /// <summary>The highest pending concentration save DC validation accepts (the damage command clamps to it).</summary>
    public const int MaxConcentrationSaveDc = 100;

    /// <summary>The longest spell name a concentration records (a name the player's own content gave it; the same bound as a snapshot label).</summary>
    public const int MaxConcentrationNameLength = 200;

    /// <summary>A bound on spent slots of one level (untrusted input); overrides can raise a maximum, never past this.</summary>
    public const int MaxSlots = 100;

    /// <summary>Current hit points; <c>null</c> means at the maximum, so a character keeps full hit points when it levels up.</summary>
    public int? CurrentHitPoints { get; init; }

    public int TemporaryHitPoints { get; init; }

    /// <summary>Spent uses per resource, keyed by content id (not revision), so an update to a new revision keeps them.</summary>
    public IReadOnlyList<ResourceUse> Resources { get; init; } = [];

    /// <summary>Condition keys from <see cref="ConditionKeys.All"/>, without exhaustion (see <see cref="Exhaustion"/>).</summary>
    public IReadOnlyList<string> Conditions { get; init; } = [];

    /// <summary>Exhaustion level, 0–6 in both SRDs.</summary>
    public int Exhaustion { get; init; }

    /// <summary>
    /// Character schema v5 (M2 item 3 follow-up, D01): spent hit dice per die size. Hit dice of the same size form one pool,
    /// whichever class gave them, so the key is the die, not the class.
    /// </summary>
    public IReadOnlyList<HitDiceUse> HitDiceSpent { get; init; } = [];

    /// <summary>Character schema v5 (SPEC C-05): death saving throw successes and failures, 0–3 each.</summary>
    public DeathSaves DeathSaves { get; init; } = new();

    /// <summary>Character schema v5 (SPEC C-05): Inspiration (2014) or Heroic Inspiration (2024). Either you have it or not.</summary>
    public bool Inspiration { get; init; }

    /// <summary>Character schema v6 (D04): spent spell slots per spell level (1–9). Recovered by a long rest.</summary>
    public IReadOnlyList<SpellSlotUse> SpellSlotsSpent { get; init; } = [];

    /// <summary>Character schema v6: spent Pact Magic slots. Recovered by a short or long rest.</summary>
    public int PactSlotsSpent { get; init; }

    /// <summary>Character schema v7 (M3 B2): the <c>toggle</c> effects switched on, keyed by content id (so an update keeps them).</summary>
    public IReadOnlyList<ActiveToggle> Toggles { get; init; } = [];

    /// <summary>
    /// Character schema v8 (D19): the concentration spell, or null. Set by <c>startConcentration</c>, cleared by
    /// <c>endConcentration</c>, by damage that drops hit points to 0, and by a long rest. <see cref="Concentration.PendingSaveDc"/>
    /// is set by <c>damage</c> (max(10, half the damage dealt), at most the family's cap: 30 under SRD 5.2.1) and cleared by <c>clearConcentrationCheck</c> or <c>endConcentration</c>.
    /// </summary>
    public Concentration? Concentration { get; init; }

    public bool IsOn(Guid contentId, string toggleId) => Toggles.Any(t => t.ContentId == contentId && t.ToggleId == toggleId);

    public int SlotsSpentOf(int level) => SpellSlotsSpent.LastOrDefault(s => s.Level == level)?.Spent ?? 0;

    /// <summary>The same state with <paramref name="spent"/> slots of spell level <paramref name="level"/> spent (0 removes the entry).</summary>
    public PlayState WithSlotsSpent(int level, int spent) => this with
    {
        SpellSlotsSpent =
        [
            .. SpellSlotsSpent.Where(s => s.Level != level),
            .. spent > 0 ? [new SpellSlotUse(level, spent)] : Array.Empty<SpellSlotUse>(),
        ],
    };

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    public int SpentOf(Guid contentId, string resourceId) =>
        Resources.LastOrDefault(r => r.ContentId == contentId && r.ResourceId == resourceId)?.Spent ?? 0;

    /// <summary>The same state with <paramref name="spent"/> recorded for one resource (0 removes the entry).</summary>
    public PlayState WithSpent(Guid contentId, string resourceId, int spent) => this with
    {
        Resources =
        [
            .. Resources.Where(r => !(r.ContentId == contentId && r.ResourceId == resourceId)),
            .. spent > 0 ? [new ResourceUse(contentId, resourceId, spent)] : Array.Empty<ResourceUse>(),
        ],
    };

    public int HitDiceSpentOf(int die) => HitDiceSpent.LastOrDefault(h => h.Die == die)?.Spent ?? 0;

    /// <summary>The same state with <paramref name="spent"/> hit dice of size <paramref name="die"/> spent (0 removes the entry).</summary>
    public PlayState WithHitDiceSpent(int die, int spent) => this with
    {
        HitDiceSpent =
        [
            .. HitDiceSpent.Where(h => h.Die != die),
            .. spent > 0 ? [new HitDiceUse(die, spent)] : Array.Empty<HitDiceUse>(),
        ],
    };

    public IEnumerable<Diagnostic> Validate()
    {
        if (CurrentHitPoints is < 0 or > MaxHitPoints)
            yield return new("play.hit-points-out-of-range", $"Current hit points {CurrentHitPoints} must be between 0 and {MaxHitPoints}.");
        if (TemporaryHitPoints is < 0 or > MaxHitPoints)
            yield return new("play.hit-points-out-of-range", $"Temporary hit points {TemporaryHitPoints} must be between 0 and {MaxHitPoints}.");
        if (Exhaustion is < 0 or > MaxExhaustion)
            yield return new("play.exhaustion-out-of-range", $"Exhaustion level {Exhaustion} must be between 0 and {MaxExhaustion}.");
        foreach (var use in Resources.Where(r => r.Spent is < 0 or > MaxHitPoints || string.IsNullOrWhiteSpace(r.ResourceId)))
            yield return new("play.resource-invalid", $"Spent uses of resource '{use.ResourceId}' must be between 0 and {MaxHitPoints}, with a resource id.");
        foreach (var duplicate in Resources.GroupBy(r => (r.ContentId, r.ResourceId)).Where(g => g.Count() > 1))
            yield return new("play.resource-duplicate", $"Resource '{duplicate.Key.ResourceId}' is recorded more than once.");
        foreach (var condition in Conditions.Where(c => !ConditionKeys.All.Contains(c)))
            yield return new("play.condition-unknown", $"'{condition}' is not a condition TomeStack knows.");
        if (Conditions.Distinct().Count() != Conditions.Count)
            yield return new("play.condition-duplicate", "A condition is recorded more than once.");
        foreach (var use in HitDiceSpent.Where(h => !HitDieEffect.AllowedDice.Contains(h.Die) || h.Spent is < 0 or > Character.MaxLevel))
            yield return new("play.hit-dice-invalid", $"Spent d{use.Die} hit dice ({use.Spent}) must be a d6, d8, d10 or d12, and between 0 and {Character.MaxLevel}.");
        if (HitDiceSpent.GroupBy(h => h.Die).Any(g => g.Count() > 1))
            yield return new("play.hit-dice-duplicate", "A hit die size is recorded more than once.");
        foreach (var use in SpellSlotsSpent.Where(s => s.Level is < 1 or > SpellcastingEffect.MaxSpellLevel || s.Spent is < 0 or > MaxSlots))
            yield return new("play.spell-slots-invalid", $"Spent level {use.Level} spell slots ({use.Spent}) must be for spell levels 1–{SpellcastingEffect.MaxSpellLevel}, between 0 and {MaxSlots}.");
        if (SpellSlotsSpent.GroupBy(s => s.Level).Any(g => g.Count() > 1))
            yield return new("play.spell-slots-duplicate", "A spell slot level is recorded more than once.");
        if (Toggles.Any(t => string.IsNullOrWhiteSpace(t.ToggleId)) || Toggles.GroupBy(t => (t.ContentId, t.ToggleId)).Any(g => g.Count() > 1))
            yield return new("play.toggle-invalid", "An active toggle needs a toggle id, and each is recorded once.");
        if (PactSlotsSpent is < 0 or > MaxSlots)
            yield return new("play.spell-slots-invalid", $"Spent Pact Magic slots must be between 0 and {MaxSlots}.");
        if (DeathSaves.Successes is < 0 or > DeathSaves.Maximum || DeathSaves.Failures is < 0 or > DeathSaves.Maximum)
            yield return new("play.death-saves-out-of-range", $"Death saving throw successes and failures must each be between 0 and {DeathSaves.Maximum}.");
        if (Concentration is { } con && (string.IsNullOrWhiteSpace(con.Name) || con.Name.Length > MaxConcentrationNameLength || con.PendingSaveDc is < 10 or > MaxConcentrationSaveDc))
            yield return new("play.concentration-invalid", $"Concentration needs the spell's name (at most {MaxConcentrationNameLength} characters), and a pending save DC between 10 and 100.");
    }
}

/// <summary>How many uses of one resource are spent. <paramref name="ContentId"/> is the content that defines it.</summary>
public sealed record ResourceUse(Guid ContentId, string ResourceId, int Spent);

/// <summary>A <c>toggle</c> effect that is switched on: the content that defines it and its toggle id.</summary>
public sealed record ActiveToggle(Guid ContentId, string ToggleId);

/// <summary>Character schema v8 (D19): the spell being concentrated on (an exact pin and its name) and a pending Constitution save DC.</summary>
public sealed record Concentration(ContentReference Spell, string Name, int? PendingSaveDc = null)
{
    /// <summary>
    /// Concentration on <paramref name="spell"/>, named for display. Content names have no length bound, so a longer name is
    /// clipped to <see cref="PlayState.MaxConcentrationNameLength"/> rather than refusing the change (the pin, not the name,
    /// identifies the spell).
    /// </summary>
    public static Concentration On(ContentReference spell, string name) =>
        new(spell, name.Length > PlayState.MaxConcentrationNameLength ? name[..PlayState.MaxConcentrationNameLength] : name);
}

/// <summary>How many spell slots of one spell level are spent.</summary>
public sealed record SpellSlotUse(int Level, int Spent);

/// <summary>How many hit dice of one size (d6, d8, d10 or d12) are spent.</summary>
public sealed record HitDiceUse(int Die, int Spent);

/// <summary>
/// Death saving throws (SRD 5.1 p. 98, SRD 5.2.1 p. 17; the same in both). Three successes: the character is Stable, and
/// TomeStack keeps the 3 as the marker until hit points are regained or the saves are cleared. Three failures: the
/// character dies. Both reset to 0 when the character regains any hit points.
/// </summary>
public sealed record DeathSaves(int Successes = 0, int Failures = 0)
{
    public const int Maximum = 3;

    /// <summary>
    /// The SRD outcome of one death saving throw with the d20 showing <paramref name="d20"/> (1–20): a 20 regains 1 hit point
    /// (and so resets the saves), a 1 counts as two failures, 10 or higher is a success, and anything else is a failure.
    /// </summary>
    public static DeathSaveOutcome Outcome(int d20) => d20 switch
    {
        < 1 or > 20 => throw new ArgumentOutOfRangeException(nameof(d20), d20, "A d20 shows 1 to 20."),
        20 => new(0, 0, RegainsOneHitPoint: true, "a natural 20: the character regains 1 hit point"),
        1 => new(0, 2, RegainsOneHitPoint: false, "a natural 1: two failures"),
        >= 10 => new(1, 0, RegainsOneHitPoint: false, "10 or higher: a success"),
        _ => new(0, 1, RegainsOneHitPoint: false, "below 10: a failure"),
    };

    /// <summary>These saves after <paramref name="outcome"/>, each count stopping at 3.</summary>
    public DeathSaves After(DeathSaveOutcome outcome) =>
        new(Math.Min(Successes + outcome.Successes, Maximum), Math.Min(Failures + outcome.Failures, Maximum));
}

/// <summary>What one death saving throw adds (<see cref="DeathSaves.Outcome"/>), and a description of why.</summary>
public sealed record DeathSaveOutcome(int Successes, int Failures, bool RegainsOneHitPoint, string Description);

/// <summary>
/// The conditions both SRDs define (exhaustion is a level, <see cref="PlayState.Exhaustion"/>). Keys only: the rules text
/// of each condition is not bundled, and conditions do not change calculated fields yet (they are reminders).
/// </summary>
public static class ConditionKeys
{
    public static IReadOnlyList<string> All { get; } =
    [
        "blinded", "charmed", "deafened", "frightened", "grappled", "incapacitated", "invisible", "paralyzed",
        "petrified", "poisoned", "prone", "restrained", "stunned", "unconscious",
    ];
}
