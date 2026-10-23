using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

/// <summary>Derived-field identifiers that effects can target and formulas can read (ADR-003).</summary>
public static class FieldIds
{
    public const string Initiative = "initiative";
    public const string ProficiencyBonus = "proficiencyBonus";
    public const string ArmorClass = "armorClass";
    public const string HitPoints = "hitPoints";

    /// <summary>
    /// Content schema v8 (M2.2): attacks per Attack action. Base 1; Extra Attack features <c>set</c> it, and the highest
    /// applies (SRD 5.1 p. 57: "the features don't add together"; SRD 5.2.1 p. 25: "the features don't stack").
    /// </summary>
    public const string Attacks = "attacks";

    /// <summary>
    /// Content schema v8 (M2.2): the lowest d20 roll that is a critical hit with weapon attacks. Base 20; Improved and
    /// Superior Critical each lower it by 1.
    /// </summary>
    public const string CriticalRange = "criticalRange";

    /// <summary>The primary caster's spell attack bonus (content schema v5; D04).</summary>
    public const string SpellAttack = "spellAttack";

    /// <summary>The primary caster's spell save DC.</summary>
    public const string SpellSaveDc = "spellSaveDc";

    /// <summary>Pact Magic slots (all of one level).</summary>
    public const string PactSlots = "pactSlots";

    /// <summary>Spell slots of spell level <paramref name="level"/> (1–9).</summary>
    public static string SpellSlots(int level) => $"spellSlots.{level}";

    public static string Score(Ability ability) => $"ability.{Key(ability)}.score";

    public static string Modifier(Ability ability) => $"ability.{Key(ability)}.mod";

    public static string Save(Ability ability) => $"save.{Key(ability)}";

    public static string Skill(string skill) => $"skill.{skill}";

    /// <summary>D24: passive score of a skill (10 + the skill).</summary>
    public static string Passive(string skill) => $"passive.{skill}";

    /// <summary>D24: walking speed in feet. Base 30 by rules policy unless a species sets it (a v10 replace modifier); override it on Stats.</summary>
    public const string Speed = "speed";

    /// <summary>
    /// Content schema v11 (D43): flying speed in feet, base 0 (no flying speed). Content may target it only from v11: below v11 the
    /// validator refuses it and the calculator ignores such a modifier (<see cref="CharacterCalculator.IsPlayMechanicsOnlyField"/>).
    /// </summary>
    public const string FlySpeed = "speed.fly";

    public static string Key(Ability ability) => ability.ToString().ToLowerInvariant();
}

/// <summary>How a <see cref="ModifierEffect"/> combines with the field (ADR-003 "Stacking and order").</summary>
public enum ModifierOperation
{
    /// <summary>Adds the value. Subject to <see cref="StackingRule"/>.</summary>
    Bonus,

    /// <summary>The field becomes the value, after bonuses. Among several sets, the highest wins.</summary>
    Set,

    /// <summary>Replaces the field's base value before bonuses. Among several replacements, the highest wins.</summary>
    Replace,
}

/// <summary>Bonuses stack unless they share a group, in which case only the highest in the group applies.</summary>
public enum StackingRule { Stack, HighestInGroup }

/// <summary>When an effect applies. Derived fields use only <see cref="Always"/>; the rest belong to commands and rolls.</summary>
public enum EffectTiming { Always, WhileActive, OnRoll, OnShortRest, OnLongRest }

/// <summary>
/// What a <see cref="GrantEffect"/> grants. Content schema v10 (D42) adds the three damage defences, which name a damage
/// type (<see cref="GrantEffect.DamageType"/>) and no target. They are typed only in a v10 revision: a v9 build cannot read
/// these values, keeps such an effect as an <see cref="UnknownEffect"/>, and so does this build below v10 (<see cref="GrantEffect.FromUnknown"/>).
/// </summary>
public enum GrantKind { Proficiency, Expertise, Content, DamageResistance, DamageImmunity, DamageVulnerability }

/// <summary>Content schema v10 (Wave 3, D42): when a toggle switches itself, computed from play state and never stored. One value for now.</summary>
public enum ToggleAutoWhen { Bloodied }

/// <summary>
/// Content schema v5 (D04 multiclass proficiency subsets): a class's grant or choice that applies only when the class is
/// the character's starting class (for example saving throws and the full skill choice), or only when it was taken as a
/// later class (the SRD "as a multiclass character" subset).
/// </summary>
public enum ClassEntry { StartingClass, Multiclass }

/// <summary>SPEC C-04: when a roll's action is used, so the sheet can group actions.</summary>
public enum Activation { Action, BonusAction, Reaction, Other }

public enum RestPeriod { ShortRest, LongRest }

/// <summary>
/// A declarative, typed effect (ADR-003). Effects never contain code. Values are bounded formulas held as source
/// text and parsed by the rules core. Unknown fields round-trip through <see cref="Extensions"/>, and unknown effect
/// types round-trip unchanged as <see cref="UnknownEffect"/>.
/// </summary>
[JsonConverter(typeof(EffectJsonConverter))]
public abstract record Effect
{
    /// <summary>Discriminator; written first, followed by <see cref="Id"/>, by <see cref="EffectJsonConverter"/>.</summary>
    public abstract string Type { get; }

    public required string Id { get; init; }

    public AutomationStatus Automation { get; init; } = AutomationStatus.Automatic;

    public EffectTiming Timing { get; init; } = EffectTiming.Always;

    /// <summary>Original or summarized text, kept so unhandled mechanics stay readable (SPEC I-05).</summary>
    public string? Text { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>bonus / set / replace on one derived field.</summary>
public sealed record ModifierEffect : Effect
{
    public const string TypeName = "modifier";

    public override string Type => TypeName;

    public required ModifierOperation Operation { get; init; }

    /// <summary>A <see cref="FieldIds"/> value, e.g. <c>initiative</c> or <c>ability.dex.score</c>.</summary>
    public required string Target { get; init; }

    /// <summary>Bounded formula source, e.g. <c>2</c> or <c>PB + CON.MOD</c>.</summary>
    public required string Value { get; init; }

    public StackingRule Stacking { get; init; } = StackingRule.Stack;

    /// <summary>Required with <see cref="StackingRule.HighestInGroup"/>.</summary>
    public string? StackGroup { get; init; }

    /// <summary>
    /// Content schema v6 (M3 B2): with <see cref="EffectTiming.WhileActive"/>, the <c>toggle</c> of this revision that
    /// switches the modifier on. It applies only while that toggle is on. Null: a whileActive modifier stays assisted.
    /// </summary>
    public string? Toggle { get; init; }

    /// <summary>
    /// Content schema v8 (M2.2): applies only while the character wears equipped body armor (light, medium or heavy; a
    /// shield alone does not count), for example the Defense fighting style. Null or false: always.
    /// </summary>
    public bool? WhileArmored { get; init; }

    /// <summary>
    /// Content schema v11 (D43): the content that declares <see cref="Toggle"/>, when it is not this revision's (a feature whose
    /// bonus follows another feature's mode). Null: this revision. Below v11 the key stays extension data and does nothing.
    /// </summary>
    public Guid? ToggleContent { get; init; }

    /// <summary>The keys content schema v11 adds to this effect; below v11 they stay extension data.</summary>
    internal static readonly string[] V11Keys = ["toggleContent"];

    /// <summary>Typed by the revision when the converter hands it over (see <see cref="EffectJsonConverter"/>): v11 keys only from v11.</summary>
    internal static Effect FromUnknown(UnknownEffect unknown, int revisionSchemaVersion) =>
        revisionSchemaVersion >= ContentRevision.PlayMechanicsSchemaVersion
            ? VersionedEffects.Typed<ModifierEffect>(unknown)
            : VersionedEffects.TypedWithout<ModifierEffect>(unknown, V11Keys);
}

/// <summary>Grants a proficiency or expertise in a field (<c>save.dex</c>, <c>skill.stealth</c>), or another content revision.</summary>
public sealed record GrantEffect : Effect
{
    public const string TypeName = "grant";

    public override string Type => TypeName;

    public required GrantKind Grant { get; init; }

    public string? Target { get; init; }

    public ContentReference? Content { get; init; }

    /// <summary>
    /// Content schema v3: the grant applies from this level on. It is the class level for class content and for content
    /// that belongs to a class (granted by it or chosen from it), and the character level for anything else.
    /// </summary>
    public int? Level { get; init; }

    /// <summary>Content schema v5: only as the starting class, or only as a later class (<see cref="ClassEntry"/>). Null: always.</summary>
    public ClassEntry? OnlyAs { get; init; }

    /// <summary>
    /// Content schema v10 (D42): with <see cref="GrantKind.DamageResistance"/>, <see cref="GrantKind.DamageImmunity"/> or
    /// <see cref="GrantKind.DamageVulnerability"/>, the damage type, one of <see cref="DamageTypes.All"/> in any case and
    /// stored as written (use <see cref="DamageTypes.TryNormalize"/> to compare). Refused on every other grant kind.
    /// Below v10 the key stays extension data and does nothing.
    /// </summary>
    public string? DamageType { get; init; }

    /// <summary>
    /// Content schema v11 (D43): with a damage defence, the toggle that must be on for the defence to apply (a form-only
    /// resistance). Null: always. The v11 schema refuses it on any other grant kind. Below v11 the key stays extension data
    /// and does nothing.
    /// </summary>
    public ToggleRef? WhileToggle { get; init; }

    /// <summary>The content schema version that adds the damage-defence kinds and <see cref="DamageType"/>.</summary>
    public const int DamageSchemaVersion = ContentRevision.PlayDetailsSchemaVersion;

    internal const string DamageTypeProperty = "damageType";

    internal static bool IsDamageDefence(GrantKind kind) =>
        kind is GrantKind.DamageResistance or GrantKind.DamageImmunity or GrantKind.DamageVulnerability;

    /// <summary>The JSON spelling of a damage kind, matched the way the serializer matches enum names: ignoring case.</summary>
    internal static bool IsDamageDefenceName(string? name) =>
        name is not null && Enum.GetValues<GrantKind>().Where(IsDamageDefence).Any(k => string.Equals(k.ToString(), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Typed from the revision's own version. A deferred grant (<see cref="EffectJsonConverter"/> hands over any grant that
    /// names a damage kind or a <c>damageType</c>) is typed in full from v10. Below v10 it reads as a v9 build reads it: a
    /// damage kind is a value the v9 enum cannot hold, so the whole effect stays unknown and byte for byte; a
    /// <c>damageType</c> on any other kind, in any spelling and with any value, stays extension data in document order.
    /// </summary>
    internal static Effect FromUnknown(UnknownEffect unknown, int revisionSchemaVersion)
    {
        // From v10 the kind must be written as a name (any case): the serializer would also bind padded names, lists of names
        // (read as combined bits) and, in an enum with more members, a different value than a v9 build read; those stay unknown.
        // Content v11 (D43) adds whileToggle: a v10 revision keeps it as extension data, byte for byte, and it does nothing.
        if (revisionSchemaVersion >= DamageSchemaVersion)
        {
            if (!VersionedEffects.NamesMember<GrantKind>(unknown.Raw, "grant"))
                return unknown;
            return revisionSchemaVersion >= ContentRevision.PlayMechanicsSchemaVersion
                ? VersionedEffects.Typed<GrantEffect>(unknown)
                : VersionedEffects.TypedWithout<GrantEffect>(unknown, V11Keys);
        }
        // Below v10 any value outside the v9 names (Proficiency, Expertise, Content) is one a v9 build either refused (it stays
        // unknown) or could only bind by naming a member this build has since added (a damage kind, a padded or listed
        // spelling): this build must not bind it, whatever number it would give.
        var typed = VersionedEffects.TypedWithout<GrantEffect>(unknown, [DamageTypeProperty, .. V11Keys]);
        return typed is GrantEffect { Grant: var kind } && (int)kind > (int)GrantKind.Content ? unknown : typed;
    }

    /// <summary>
    /// The keys content schema v11 adds to this effect. A slice that adds one also strips it below v11 in
    /// <see cref="FromUnknown"/>, as <see cref="ToggleEffect"/> does.
    /// </summary>
    internal static readonly string[] V11Keys = ["whileToggle"];

    /// <summary>The names a v9 build could read (the converter hands over any grant whose kind is not exactly one of them).</summary>
    internal static bool IsPreV10Name(string? name) =>
        name is not null && Enum.GetValues<GrantKind>().Where(k => !IsDamageDefence(k)).Any(k => string.Equals(k.ToString(), name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Content schema v3: a class's hit point die (d6–d12), used for hit points at each level of that class.</summary>
public sealed record HitDieEffect : Effect
{
    public const string TypeName = "hitDie";

    public static readonly IReadOnlyList<int> AllowedDice = [6, 8, 10, 12];

    public override string Type => TypeName;

    public required int Die { get; init; }
}

/// <summary>A limited-use resource with a formula maximum (for example, uses per rest).</summary>
public sealed record ResourceEffect : Effect
{
    public const string TypeName = "resource";

    public override string Type => TypeName;

    public required string ResourceId { get; init; }

    public required string Label { get; init; }

    public required string Maximum { get; init; }

    /// <summary>
    /// Content schema v10 (D42): the kind of action that spends this resource, so the sheet can group it. Display only
    /// (Wave 3 item 24); null: ungrouped. Below v10 the key stays extension data and does nothing.
    /// </summary>
    public Activation? Activation { get; init; }

    /// <summary>Content schema v11 (D43): <see cref="ResourceCounting.Up"/> makes the pool count up from zero to its maximum ("built up"). Null: counts down (uses left).</summary>
    public ResourceCounting? Counts { get; init; }

    /// <summary>Content schema v11 (D43): what the sheet says when a counting-up pool is full. Only with <see cref="Counts"/>.</summary>
    public ResourceFullPrompt? ResourceFull { get; init; }

    /// <summary>Content schema v11 (D43): a per-turn allowance (for example one reaction) that an upkeep regain refreshes. Null: false.</summary>
    public bool? Allowance { get; init; }

    internal const string CountsProperty = "counts";

    /// <summary>
    /// The keys content schema v11 adds to this effect (matched in every spelling, like the serializer). Below v11 they stay
    /// extension data and do nothing, and the revision keeps its exact bytes and hash.
    /// </summary>
    internal static readonly string[] V11Keys = [CountsProperty, "resourceFull", "allowance"];

    /// <summary>
    /// Typed by the revision. From v11 all keys apply (an unknown <c>counts</c> value leaves the effect untyped); v10 types
    /// <c>activation</c> but not <see cref="V11Keys"/>; below v10 neither.
    /// </summary>
    internal static Effect FromUnknown(UnknownEffect unknown, int revisionSchemaVersion)
    {
        if (revisionSchemaVersion < ContentRevision.PlayDetailsSchemaVersion)
            return VersionedEffects.TypedWithout<ResourceEffect>(unknown, [ToggleEffect.ActivationProperty, .. V11Keys]);
        if (!VersionedEffects.NamesMember<Activation>(unknown.Raw, ToggleEffect.ActivationProperty))
            return unknown;
        if (revisionSchemaVersion < ContentRevision.PlayMechanicsSchemaVersion)
            return VersionedEffects.TypedWithout<ResourceEffect>(unknown, V11Keys);
        return VersionedEffects.NamesMember<ResourceCounting>(unknown.Raw, CountsProperty) ? VersionedEffects.Typed<ResourceEffect>(unknown) : unknown;
    }
}

/// <summary>Content schema v11 (D43): how a pool counts. One value for now: up from zero to the maximum.</summary>
public enum ResourceCounting { Up }

/// <summary>Content schema v11 (D43): the text a full counting-up pool shows.</summary>
public sealed record ResourceFullPrompt(string Text);

/// <summary>Content schema v11 (D43): when in a turn an upkeep happens.</summary>
public enum TurnMoment { TurnStart, TurnEnd }

/// <summary>Content schema v11 (D43): whether an upkeep spends from a pool or regains to it.</summary>
public enum UpkeepChange { Spend, Regain }

/// <summary>Content schema v11 (D43): what an upkeep spend that the pool cannot cover does. Null in content means <see cref="Ask"/>.</summary>
public enum UpkeepShortfall { Ask, End }

/// <summary>
/// Content schema v11 (D43): a pool change at the start or end of the turn, optionally tied to a toggle (it applies while
/// the toggle is on). Previewed and confirmed by a turn command, never applied by calculation. Typed only in a v11 revision:
/// in an older revision an <c>upkeep</c> stays an <see cref="UnknownEffect"/>, byte for byte.
/// </summary>
public sealed record UpkeepEffect : Effect
{
    public const string TypeName = "upkeep";

    public const int SchemaVersion = ContentRevision.PlayMechanicsSchemaVersion;

    public override string Type => TypeName;

    /// <summary>The toggle this upkeep belongs to (applies while it is on). Null: always.</summary>
    public string? Toggle { get; init; }

    /// <summary>The content that declares <see cref="Toggle"/>. Null: this revision.</summary>
    public Guid? ToggleContent { get; init; }

    public required TurnMoment At { get; init; }

    public required string ResourceId { get; init; }

    /// <summary>The content that declares <see cref="ResourceId"/>. Null: this revision.</summary>
    public Guid? ResourceContent { get; init; }

    public required UpkeepChange Change { get; init; }

    /// <summary>A formula, or <c>all</c> (regain only).</summary>
    public required string Amount { get; init; }

    /// <summary>What a spend the pool cannot cover does. Null: <see cref="UpkeepShortfall.Ask"/>.</summary>
    public UpkeepShortfall? IfShort { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) =>
        VersionedEffects.NamesMember<TurnMoment>(unknown.Raw, "at")
        && VersionedEffects.NamesMember<UpkeepChange>(unknown.Raw, "change")
        && VersionedEffects.NamesMember<UpkeepShortfall>(unknown.Raw, "ifShort")
            ? VersionedEffects.Typed<UpkeepEffect>(unknown)
            : unknown;
}

/// <summary>Content schema v11 (D43): the dice of a feature attack, both formulas (<c>1</c>, <c>SCALE.handDie</c>), so a die can follow a class column.</summary>
public sealed record AttackDamage(string Count, string Die);

/// <summary>
/// Content schema v11 (D43): an attack a feature gives, such as an unarmed strike or a bolt: to hit = the best of the listed
/// ability modifiers plus the proficiency bonus (unless <see cref="Proficient"/> is false); damage = <see cref="Damage"/> plus
/// <see cref="DamageBonus"/> (default: that ability modifier). Typed only in a v11 revision: in an older revision an
/// <c>attack</c> stays an <see cref="UnknownEffect"/>, byte for byte.
/// </summary>
public sealed record AttackEffect : Effect
{
    public const string TypeName = "attack";

    public const int SchemaVersion = ContentRevision.PlayMechanicsSchemaVersion;

    public override string Type => TypeName;

    /// <summary>A key like a scale id (<see cref="ScaleEffect.IsValidScaleId"/>); its shape is validated in the content validator. Ids should be unique within the revision (uniqueness is not enforced).</summary>
    public required string AttackId { get; init; }

    public required string Label { get; init; }

    public required WeaponAttack Kind { get; init; }

    /// <summary>The abilities the attack may use; the best one counts. At least one.</summary>
    public required IReadOnlyList<Ability> Abilities { get; init; }

    /// <summary>Whether the proficiency bonus is added to the attack roll. Null: true.</summary>
    public bool? Proficient { get; init; }

    public required AttackDamage Damage { get; init; }

    /// <summary>A formula added to the damage. Null: the chosen ability modifier.</summary>
    public string? DamageBonus { get; init; }

    /// <summary>One of <see cref="DamageTypes.All"/>.</summary>
    public required string DamageType { get; init; }

    public string? Range { get; init; }

    /// <summary>Keys a rider (<see cref="AttackRiderEffect.Tags"/>) matches. <c>all</c> is reserved for riders.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>The attack can stand in for one attack of the Attack action. Null: false.</summary>
    public bool? ReplacesAttack { get; init; }

    public Activation? Activation { get; init; }

    /// <summary>The pool the attack spends from; only an explicit action spends it.</summary>
    public string? ResourceId { get; init; }

    /// <summary>The content that declares <see cref="ResourceId"/>. Null: this revision.</summary>
    public Guid? ResourceContent { get; init; }

    /// <summary>Uses the attack spends (a formula; default 1 with a resource).</summary>
    public string? Cost { get; init; }

    /// <summary>The attack is available only while this toggle is on (it may be another content's). Null: always.</summary>
    public ToggleRef? WhileToggle { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) =>
        VersionedEffects.NamesMember<WeaponAttack>(unknown.Raw, "kind")
        && VersionedEffects.NamesMember<Activation>(unknown.Raw, "activation")
        && VersionedEffects.NamesEveryMember<Ability>(unknown.Raw, "abilities")
            ? VersionedEffects.Typed<AttackEffect>(unknown)
            : unknown;
}

/// <summary>
/// Content schema v11 (D43): extra to-hit, damage dice or damage that applies to the attacks carrying one of its tags, while
/// its toggle is on (always, without one). Typed only in a v11 revision: in an older revision an <c>attackRider</c> stays an
/// <see cref="UnknownEffect"/>, byte for byte.
/// </summary>
public sealed record AttackRiderEffect : Effect
{
    public const string TypeName = "attackRider";

    public const int SchemaVersion = ContentRevision.PlayMechanicsSchemaVersion;

    /// <summary>The tag that matches every attack.</summary>
    public const string AllTag = "all";

    /// <summary>The die sizes a rider's dice may use.</summary>
    internal static readonly int[] AllowedDice = [4, 6, 8, 10, 12];

    public override string Type => TypeName;

    /// <summary>The attack tags the rider applies to; <see cref="AllTag"/> matches every attack. At least one.</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>A formula added to the attack roll.</summary>
    public string? ToHit { get; init; }

    /// <summary>Extra damage dice, <c>NdM</c> with M one of 4, 6, 8, 10 or 12.</summary>
    public string? Dice { get; init; }

    /// <summary>A formula added to the damage.</summary>
    public string? DamageBonus { get; init; }

    /// <summary>The type of the extra dice, one of <see cref="DamageTypes.All"/>. Null: the attack's own.</summary>
    public string? DamageType { get; init; }

    /// <summary>The toggle (of this revision, or of <see cref="ToggleContent"/>) that switches the rider on. Null: always on.</summary>
    public string? Toggle { get; init; }

    /// <summary>The content that declares <see cref="Toggle"/>. Null: this revision.</summary>
    public Guid? ToggleContent { get; init; }

    /// <summary>Riders of one stack group never add: the highest applies.</summary>
    public string? StackGroup { get; init; }

    /// <summary>Reads <c>NdM</c> exactly: a whole count from 1 to <see cref="DiceLimits.MaxDice"/>, a lowercase d, and an allowed die; no sign, space or modifier.</summary>
    internal static bool TryParseDice(string? source, out int count, out int sides)
    {
        count = sides = 0;
        var parts = source?.Split('d');
        return parts is [{ Length: > 0 } c, { Length: > 0 } s]
            && c.All(char.IsAsciiDigit) && s.All(char.IsAsciiDigit)
            && int.TryParse(c, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out count)
            && int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out sides)
            && count is >= 1 and <= DiceLimits.MaxDice && AllowedDice.Contains(sides);
    }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<AttackRiderEffect>(unknown);
}

/// <summary>A choice the player makes (for example, pick one of these content revisions).</summary>
public sealed record ChoiceEffect : Effect
{
    public const string TypeName = "choice";

    public override string Type => TypeName;

    public required string ChoiceId { get; init; }

    public int Count { get; init; } = 1;

    public IReadOnlyList<ContentReference> Options { get; init; } = [];

    /// <summary>Content schema v3: the choice is made from this level on (class level inside a class; see <see cref="GrantEffect.Level"/>).</summary>
    public int? Level { get; init; }

    /// <summary>Content schema v5: only as the starting class, or only as a later class. Null: always.</summary>
    public ClassEntry? OnlyAs { get; init; }
}

/// <summary>A prerequisite or limitation, for example a minimum ability score.</summary>
public sealed record RestrictionEffect : Effect
{
    public const string TypeName = "restriction";

    public override string Type => TypeName;

    public required string Field { get; init; }

    public required int Minimum { get; init; }

    /// <summary>
    /// Content schema v5 (D04): a multiclass prerequisite. Checked only when the character has levels in two or more
    /// classes, against the calculated sheet; an unmet one is a warning on the class, which stays applied (its levels are
    /// already taken). Null: an ordinary prerequisite.
    /// </summary>
    public bool? Multiclass { get; init; }

    /// <summary>Content schema v5: restrictions of one revision with the same group are alternatives; meeting any one is enough.</summary>
    public string? Group { get; init; }
}

/// <summary>Restores a resource on a rest. Previewed and confirmed by a command, never applied by calculation.</summary>
public sealed record RecoveryEffect : Effect
{
    public const string TypeName = "recovery";

    public override string Type => TypeName;

    public required string ResourceId { get; init; }

    public required RestPeriod On { get; init; }

    /// <summary>Formula, or <c>all</c>.</summary>
    public required string Amount { get; init; }
}

/// <summary>A roll the sheet offers. Rolling never consumes a resource by itself (ARCHITECTURE "commands vs calculation").</summary>
public sealed record RollEffect : Effect
{
    public const string TypeName = "roll";

    public override string Type => TypeName;

    public required string RollId { get; init; }

    public required string Label { get; init; }

    /// <summary>
    /// Dice expression, e.g. <c>1d8+2</c>. Content schema v11 (D43): optional, for an action that rolls nothing (it needs a
    /// <see cref="Cost"/> then). Below v11 a roll with no <c>dice</c> key is never typed (see <see cref="FromUnknown"/>).
    /// </summary>
    public string? Dice { get; init; }

    /// <summary>Optional resource the associated action spends; only an explicit action command spends it.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Content schema v5 (SPEC C-04): action, bonus action, reaction or other; null is listed under "Other".</summary>
    public Activation? Activation { get; init; }

    /// <summary>
    /// Content schema v6 (M3 B2, shared resources): the content id that defines <see cref="ResourceId"/>, when the resource
    /// belongs to another feature (for example one pool several features spend). Null: this revision defines it.
    /// </summary>
    public Guid? ResourceContent { get; init; }

    /// <summary>Content schema v6: uses the action spends (a formula; default 1). With <see cref="VariableCost"/>, the most it may spend.</summary>
    public string? Cost { get; init; }

    /// <summary>Content schema v6 (variable spend): the player chooses how many uses to spend, from 1 to <see cref="Cost"/> (or what is left).</summary>
    public bool? VariableCost { get; init; }

    /// <summary>
    /// Content schema v8 (M2.2): a formula added to the dice, evaluated for the character (for example <c>CLASS_LEVEL</c>
    /// for Second Wind's "1d10 plus your Fighter level"). It is a roll modifier: never doubled on a critical.
    /// </summary>
    public string? Bonus { get; init; }

    /// <summary>
    /// Content schema v11 (D43): the roll is offered only while this toggle is on (it may be another content's). Null: always.
    /// Below v11 the key stays extension data and does nothing.
    /// </summary>
    public ToggleRef? WhileToggle { get; init; }

    /// <summary>Content schema v11 (D43): the saving throw the action asks of its targets. Below v11 the key stays extension data and does nothing.</summary>
    public RollSave? Save { get; init; }

    /// <summary>Content schema v11 (D43): the area the action covers. Below v11 the key stays extension data and does nothing.</summary>
    public RollArea? Area { get; init; }

    /// <summary>Content schema v11 (D43): the range, as text (for example <c>Self</c> or <c>60 ft</c>), at most <see cref="MaxTextLength"/> characters.</summary>
    public string? Range { get; init; }

    /// <summary>Content schema v11 (D43): who the action affects, as text, at most <see cref="MaxTextLength"/> characters.</summary>
    public string? Targets { get; init; }

    /// <summary>Content schema v11 (D43): extra dice per additional unit of the resource spent. Needs a <see cref="Cost"/> and a <see cref="ResourceId"/>.</summary>
    public RollPerUnit? PerUnit { get; init; }

    /// <summary>The longest <see cref="Range"/> or <see cref="Targets"/>.</summary>
    public const int MaxTextLength = 200;

    /// <summary>The keys content schema v11 adds to this effect; below v11 they stay extension data.</summary>
    internal static readonly string[] V11Keys = ["whileToggle", "save", "area", "range", "targets", "perUnit"];

    /// <summary>
    /// Typed by the revision when the converter hands it over (see <see cref="EffectJsonConverter"/>): v11 keys, and a roll with
    /// no <c>dice</c>, only from v11. Below v11 a dice-less roll stays untyped, as it did when dice were required.
    /// </summary>
    internal static Effect FromUnknown(UnknownEffect unknown, int revisionSchemaVersion)
    {
        if (revisionSchemaVersion < ContentRevision.PlayMechanicsSchemaVersion)
            return LacksDice(unknown.Raw) ? unknown : VersionedEffects.TypedWithout<RollEffect>(unknown, V11Keys);
        return ReadsEnums(unknown.Raw) ? VersionedEffects.Typed<RollEffect>(unknown) : unknown;
    }

    /// <summary>Whether the JSON has no <c>dice</c> (no key in any spelling, or a null): what an older build, where it was required, could not type.</summary>
    internal static bool LacksDice(JsonElement raw) =>
        raw.ValueKind == JsonValueKind.Object
        && !raw.EnumerateObject().Any(p => string.Equals(p.Name, "dice", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind != JsonValueKind.Null);

    /// <summary>The save ability and area shape must be exactly a member name (the serializer also binds padded or combined names, which no schema allows).</summary>
    private static bool ReadsEnums(JsonElement raw)
    {
        foreach (var p in raw.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (string.Equals(p.Name, "save", StringComparison.OrdinalIgnoreCase) && !VersionedEffects.NamesMember<Ability>(p.Value, "ability"))
                return false;
            if (string.Equals(p.Name, "area", StringComparison.OrdinalIgnoreCase) && !VersionedEffects.NamesMember<AreaShape>(p.Value, "shape"))
                return false;
        }
        return true;
    }
}

/// <summary>Content schema v11 (D43): a saving throw an action asks of its targets: the ability, and a DC formula (for example <c>8 + PB + WIS.MOD</c>).</summary>
public sealed record RollSave([property: JsonRequired] Ability Ability, [property: JsonRequired] string Dc);

/// <summary>Content schema v11 (D43): the shape of an area.</summary>
public enum AreaShape { Line, Cone, Sphere, Cube, Cylinder, Emanation }

/// <summary>Content schema v11 (D43): an area in feet. <see cref="Width"/> applies to a line only.</summary>
public sealed record RollArea([property: JsonRequired] AreaShape Shape, [property: JsonRequired] int Size, int? Width);

/// <summary>Content schema v11 (D43): extra dice (<c>NdM</c>) for each unit spent beyond the base cost, up to <see cref="MaxUnits"/> units.</summary>
public sealed record RollPerUnit([property: JsonRequired] string Dice, int? MaxUnits);

/// <summary>
/// Content schema v11 (D43): a named formula a feature declares, read as <c>VALUE.&lt;valueId&gt;</c> by other formulas of the
/// content of the same class. Typed only in a v11 revision: in an older revision a <c>value</c> stays an
/// <see cref="UnknownEffect"/>, byte for byte.
/// </summary>
public sealed record ValueEffect : Effect
{
    public const string TypeName = "value";

    public const int SchemaVersion = ContentRevision.PlayMechanicsSchemaVersion;

    public override string Type => TypeName;

    /// <summary>A key like a scale id (<see cref="ScaleEffect.IsValidScaleId"/>); validated in the content validator.</summary>
    public required string ValueId { get; init; }

    public required string Formula { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<ValueEffect>(unknown);
}

/// <summary>
/// Content schema v11 (D43): advantage or disadvantage on a saving throw, skill check or ability check. Typed only in a v11
/// revision: in an older revision a <c>rollMode</c> stays an <see cref="UnknownEffect"/>, byte for byte. A mode that names no
/// <see cref="RollMode"/> member stays untyped here too, and the validator reports it.
/// </summary>
public sealed record RollModeEffect : Effect
{
    public const string TypeName = "rollMode";

    public const int SchemaVersion = ContentRevision.PlayMechanicsSchemaVersion;

    public override string Type => TypeName;

    /// <summary><c>save.&lt;ability&gt;</c>, <c>skill.&lt;skill&gt;</c> or <c>ability.&lt;ability&gt;.check</c> (the last is a roll target only, not a field); validated in the content validator.</summary>
    public required string Target { get; init; }

    /// <summary>Advantage or disadvantage. <see cref="RollMode.Normal"/> reads but is refused by the validator.</summary>
    public required RollMode Mode { get; init; }

    /// <summary>Condition keys (<see cref="ConditionKeys.All"/>): the mode then only applies against a creature with one of them, which the player confirms; it is assisted, never automatic. Null: always.</summary>
    public IReadOnlyList<string>? Against { get; init; }

    /// <summary>The <c>toggle</c> that must be on for the mode to apply (of this revision, or of <see cref="ToggleContent"/>). Null: always.</summary>
    public string? Toggle { get; init; }

    /// <summary>The content that declares <see cref="Toggle"/>, when it is not this revision's. Null: this revision.</summary>
    public Guid? ToggleContent { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) =>
        VersionedEffects.NamesMember<RollMode>(unknown.Raw, "mode") ? VersionedEffects.Typed<RollModeEffect>(unknown) : unknown;
}

/// <summary>
/// Content schema v6 (M3 B2, SPEC I-05 "assisted actions"): something the player switches on and off at the table, such
/// as a stance or an aura. While it is on (play state, character schema v7), the revision's modifiers that name it apply.
/// Turning it on can spend one use of a resource. Turning it on or off is a confirmed play action, and the long rest
/// proposes turning it off.
/// </summary>
public sealed record ToggleEffect : Effect
{
    public const string TypeName = "toggle";

    public const int SchemaVersion = 6;

    public override string Type => TypeName;

    public required string ToggleId { get; init; }

    public required string Label { get; init; }

    /// <summary>A resource of this revision that turning the toggle on spends one use of.</summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Content schema v10 (D42): the toggle switches itself from play state while the condition holds (Bloodied). Computed,
    /// never stored. Field only here; below v10 the key stays extension data and does nothing.
    /// </summary>
    public ToggleAutoWhen? AutoWhen { get; init; }

    /// <summary>Content schema v10 (D42): the kind of action that switches the toggle, so the sheet can group it. Display only.</summary>
    public Activation? Activation { get; init; }

    /// <summary>
    /// Content schema v11 (D43): the form group this toggle belongs to (a <see cref="ToggleGroupEffect.GroupId"/>). Toggles of one
    /// group exclude each other as primaries. Null: not in a group. Below v11 the key stays extension data and does nothing.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>Content schema v11 (D43): the content that declares <see cref="Group"/>; it may be another source's. Null: this revision.</summary>
    public Guid? GroupContent { get; init; }

    /// <summary>Content schema v11 (D43): <see cref="ToggleLayer.Primary"/> (the default when absent) or an overlay on a primary.</summary>
    public ToggleLayer? Layer { get; init; }

    /// <summary>Content schema v11 (D43): the primaries an overlay may be on with. Null or empty: none.</summary>
    public IReadOnlyList<ToggleRef>? AllowedWith { get; init; }

    /// <summary>Content schema v11 (D43): an overlay may be on while no primary of its group is. Null: false.</summary>
    public bool? AllowedInBase { get; init; }

    /// <summary>Content schema v11 (D43): a formula, the amount of the pool turning the toggle on spends. Validated and evaluated later.</summary>
    public string? Cost { get; init; }

    /// <summary>Content schema v11 (D43): the content that declares <see cref="ResourceId"/>, when it is not this revision.</summary>
    public Guid? ResourceContent { get; init; }

    /// <summary>Content schema v11 (D43): the toggle is available from this level on (class level for a class's features, otherwise character level; see <see cref="GrantEffect.Level"/>).</summary>
    public int? Level { get; init; }

    /// <summary>Content schema v11 (D43): rounds the toggle lasts once on (a formula, evaluated for the character). Null: until switched off.</summary>
    public string? Duration { get; init; }

    internal const string AutoWhenProperty = "autoWhen";

    internal const string ActivationProperty = "activation";

    internal const string LayerProperty = "layer";

    /// <summary>The keys content schema v11 adds (matched in every spelling, like the serializer).</summary>
    internal static readonly string[] V11Keys = ["group", "groupContent", LayerProperty, "allowedWith", "allowedInBase", "cost", "resourceContent", "level", "duration"];

    /// <summary>
    /// Typed from v6, in tiers. Below v10, <c>autoWhen</c> and <c>activation</c> stay extension data; below v11, so do the
    /// v11 keys (<see cref="V11Keys"/>). Every spelling of a key, with any value, stays in document order, exactly as the
    /// older build reads it: never typed, so it does nothing, and written back in the same place (the hash is unchanged).
    /// From a version, an enum value that names no member leaves the whole effect untyped.
    /// </summary>
    internal static Effect FromUnknown(UnknownEffect unknown, int revisionSchemaVersion)
    {
        if (revisionSchemaVersion < ContentRevision.PlayDetailsSchemaVersion)
            return VersionedEffects.TypedWithout<ToggleEffect>(unknown, [AutoWhenProperty, ActivationProperty, .. V11Keys]);
        if (!VersionedEffects.NamesMember<ToggleAutoWhen>(unknown.Raw, AutoWhenProperty) || !VersionedEffects.NamesMember<Activation>(unknown.Raw, ActivationProperty))
            return unknown;
        if (revisionSchemaVersion < ContentRevision.PlayMechanicsSchemaVersion)
            return VersionedEffects.TypedWithout<ToggleEffect>(unknown, V11Keys);
        return VersionedEffects.NamesMember<ToggleLayer>(unknown.Raw, LayerProperty) ? VersionedEffects.Typed<ToggleEffect>(unknown) : unknown;
    }
}

/// <summary>Content schema v11 (D43): whether a toggle is a form (primary) or sits on top of one (overlay).</summary>
public enum ToggleLayer { Primary, Overlay }

/// <summary>Content schema v11 (D43): a toggle named by the content that declares it (null: the same revision).</summary>
public sealed record ToggleRef(Guid? ContentId, string ToggleId);

/// <summary>
/// Content schema v11 (D43): declares a form group a feature's toggles can join, with a label for the sheet. The group has
/// no behaviour of its own; <see cref="ToggleEffect.Group"/> names it. Typed only in a v11 revision: in an older revision a
/// <c>toggleGroup</c> stays an <see cref="UnknownEffect"/>, byte for byte.
/// </summary>
public sealed record ToggleGroupEffect : Effect
{
    public const string TypeName = "toggleGroup";

    public const int SchemaVersion = ContentRevision.PlayMechanicsSchemaVersion;

    public override string Type => TypeName;

    /// <summary>A key like a scale id (<see cref="ScaleEffect.IsValidScaleId"/>); validated in the content validator.</summary>
    public required string GroupId { get; init; }

    public required string Label { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<ToggleGroupEffect>(unknown);
}

/// <summary>
/// Content schema v9 (M5, ADR-010): a named per-level column of a class or subclass, such as "Ink: 2, 2, 3, 3, 4 …".
/// Formulas read it as <c>SCALE.&lt;scaleId&gt;</c> at the level of the class the content belongs to, and the sheet shows
/// it as a column of the class table. The values are literals, so a scale reads no field and cannot form a cycle.
/// Typed only in a v9 (or newer) revision, like <see cref="ArmorEffect"/>.
/// </summary>
public sealed record ScaleEffect : Effect
{
    public const string TypeName = "scale";

    public const int SchemaVersion = 9;

    /// <summary>The longest scale id: <c>SCALE.</c> plus it must still fit comfortably in a 200-character formula.</summary>
    public const int MaxScaleIdLength = 32;

    public override string Type => TypeName;

    /// <summary>A key, never a display name, set once like a resource id: <c>[a-z][A-Za-z0-9]{0,31}</c>.</summary>
    public required string ScaleId { get; init; }

    public required string Label { get; init; }

    /// <summary>20 values (class levels 1–20), each 0 to <see cref="FormulaLimits.MaxLiteral"/>.</summary>
    public required IReadOnlyList<int> Values { get; init; }

    public static bool IsValidScaleId(string? id) =>
        id is { Length: > 0 and <= MaxScaleIdLength } && char.IsAsciiLetterLower(id[0]) && id.All(char.IsAsciiLetterOrDigit);

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<ScaleEffect>(unknown);
}

/// <summary>Content schema v10: when an item's charges come back, and how many (<see cref="Regain"/>; null: all of them).</summary>
public sealed record ItemChargeRecovery
{
    public required RestPeriod Period { get; init; }

    /// <summary>A dice expression (<see cref="DiceExpression"/>), for example <c>1d4+1</c>. Null: every charge comes back on that rest.</summary>
    public string? Regain { get; init; }
}

/// <summary>Content schema v10: an item's charges, 1 to <see cref="MaxCharges"/>. A null <see cref="Recovery"/> means they come back only through the play action.</summary>
public sealed record ItemCharges
{
    public const int MaxCharges = 99;

    public required int Max { get; init; }

    public ItemChargeRecovery? Recovery { get; init; }
}

/// <summary>
/// Content schema v10 (Wave 3, R155): what an item needs to be used: attunement and charges. The calculator does not use it
/// yet (Task 5 gives it behaviour). Typed only in a v10 revision, like <see cref="ArmorEffect"/>: in an older revision an
/// <c>item</c> effect stays an <see cref="UnknownEffect"/>, byte for byte. On an <see cref="ContentKind.Item"/> revision, at most one.
/// </summary>
public sealed record ItemEffect : Effect
{
    public const string TypeName = "item";

    public const int SchemaVersion = ContentRevision.PlayDetailsSchemaVersion;

    public override string Type => TypeName;

    public bool? RequiresAttunement { get; init; }

    public ItemCharges? Charges { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<ItemEffect>(unknown);
}

public enum WeaponCategory { Simple, Martial }

public enum WeaponAttack { Melee, Ranged }

/// <summary>
/// Content schema v5 (SPEC C-02, C-04): a weapon on an item. An equipped weapon gives an attack: to hit = the ability
/// modifier (Strength for melee, Dexterity for ranged, the better of the two with <c>finesse</c>) plus the proficiency bonus
/// when proficient; damage = <see cref="Damage"/> plus the same modifier. The same in both SRDs.
/// </summary>
public sealed record WeaponEffect : Effect
{
    public const string TypeName = "weapon";

    public override string Type => TypeName;

    public required WeaponCategory Category { get; init; }

    public required WeaponAttack Attack { get; init; }

    /// <summary>Damage dice, for example <c>1d8</c>.</summary>
    public required string Damage { get; init; }

    public required string DamageType { get; init; }

    /// <summary>Property keys, for example <c>finesse</c>, <c>light</c>, <c>thrown</c>, <c>versatile</c>, <c>two-handed</c>.</summary>
    public IReadOnlyList<string> Properties { get; init; } = [];

    /// <summary>Damage dice when used with two hands (the versatile property).</summary>
    public string? Versatile { get; init; }

    /// <summary>Normal and long range, for example <c>80/320</c>.</summary>
    public string? Range { get; init; }

    /// <summary>The key a specific weapon proficiency names (<c>weapon.&lt;key&gt;</c>), for example <c>rapier</c>. A key, never a display name.</summary>
    public required string WeaponKey { get; init; }

    /// <summary>The 2024 Weapon Mastery property, as text (it is not automated).</summary>
    public string? Mastery { get; init; }

    public bool Has(string property) => Properties.Contains(property, StringComparer.Ordinal);

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<WeaponEffect>(unknown);
}

public enum ArmorCategory { Light, Medium, Heavy, Shield }

/// <summary>
/// M2 item 4: armor on an item. Body armor (light, medium, heavy) sets the Armor Class base while the item is equipped:
/// <see cref="ArmorClass"/> plus the Dexterity modifier (light), capped at <see cref="DexterityCap"/> (medium, default 2),
/// or none (heavy). A shield adds <see cref="ArmorClass"/>. The same in both SRDs. Content schema v4 only: in an older
/// revision an <c>armor</c> effect stays an <see cref="UnknownEffect"/>, byte for byte, because a 0.2.0 build stored it that
/// way and typing it would change both its hash and its meaning (ADR-003).
/// </summary>
public sealed record ArmorEffect : Effect
{
    public const string TypeName = "armor";

    /// <summary>The content schema version that introduced this effect type.</summary>
    public const int SchemaVersion = 4;

    /// <summary>An <c>armor</c> effect read from a v4 revision; a malformed body stays reference-only.</summary>
    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<ArmorEffect>(unknown);

    public const int DefaultMediumDexterityCap = 2;

    public override string Type => TypeName;

    public required ArmorCategory Category { get; init; }

    /// <summary>The armor's base Armor Class, or a shield's bonus.</summary>
    public required int ArmorClass { get; init; }

    /// <summary>Medium armor only: the most the Dexterity modifier adds (default 2).</summary>
    public int? DexterityCap { get; init; }

    /// <summary>
    /// Content schema v8 (M2.2): the Strength score this armor needs; below it the wearer's speed is 10 feet lower (both
    /// SRDs). TomeStack has no speed field, so the sheet warns.
    /// </summary>
    public int? Strength { get; init; }

    /// <summary>Content schema v8 (M2.2): the wearer has disadvantage on Dexterity (Stealth) checks. The Stealth field warns.</summary>
    public bool? StealthDisadvantage { get; init; }
}

/// <summary>How a caster readies spells: <see cref="Prepared"/> from a list (or spellbook) that can change, or a fixed <see cref="Known"/> set.</summary>
public enum SpellPreparation { Prepared, Known }

/// <summary>Which pool a caster's slots belong to: ordinary spell slots (long rest) or Pact Magic (short or long rest).</summary>
public enum SpellSlotKind { SpellSlots, PactMagic }

/// <summary>
/// Content schema v7 (M3 C3): how a caster's class levels count toward the SRD Multiclass Spellcaster table: all of them,
/// half, or a third. The rounding of the fractions is rules-family policy (<see cref="RulesFamilyPolicy.HalfCasterLevels"/>).
/// </summary>
public enum MulticlassCaster { Full, Half, Third }

/// <summary>
/// Content schema v5 (M2, D04): a class's (or subclass's) Spellcasting feature. Spell attack bonus = PB + the ability
/// modifier and save DC = 8 + PB + the ability modifier, in both SRDs. Tables are indexed by the level in the class the
/// content belongs to (row 0 = level 1), so each SRD revision states its own progression and the 2014/2024 differences
/// (for example half casters with slots at level 1 in 2024) are content, not code. Typed only in a v5 (or newer) revision,
/// like <see cref="ArmorEffect"/>.
/// </summary>
public sealed record SpellcastingEffect : Effect
{
    public const string TypeName = "spellcasting";

    public const int SchemaVersion = 5;

    public const int MaxSpellLevel = 9;

    public override string Type => TypeName;

    /// <summary>The spellcasting ability (Intelligence, Wisdom or Charisma in the SRDs).</summary>
    public required Ability Ability { get; init; }

    public SpellPreparation Preparation { get; init; } = SpellPreparation.Prepared;

    /// <summary>The key of the spell list this caster uses; spells name the lists they are on (<see cref="SpellEffect.Lists"/>). A key, never a display name.</summary>
    public required string SpellList { get; init; }

    public SpellSlotKind SlotKind { get; init; } = SpellSlotKind.SpellSlots;

    /// <summary>20 rows (class levels 1–20), each the number of slots of spell levels 1, 2, … (at most 9 entries).</summary>
    public required IReadOnlyList<IReadOnlyList<int>> Slots { get; init; }

    /// <summary>Optional, 20 entries: cantrips known at each class level.</summary>
    public IReadOnlyList<int>? Cantrips { get; init; }

    /// <summary>Optional, 20 entries: spells known (known casters) or prepared (a 2024 table) at each class level.</summary>
    public IReadOnlyList<int>? SpellsTable { get; init; }

    /// <summary>Optional formula for the number of prepared spells, for example <c>max(1, WIS.MOD + CLASS_LEVEL)</c> (2014 rules).</summary>
    public string? SpellsFormula { get; init; }

    /// <summary>
    /// Content schema v7 (M3 C3): how this caster's levels combine with other casters' through the Multiclass Spellcaster
    /// table. Absent (the default, so older revisions serialize unchanged): its slots are not combined, and a character
    /// with a second slot caster gets the slot total as a manual step. Ordinary spell slots only; Pact Magic stays separate.
    /// </summary>
    public MulticlassCaster? MulticlassCaster { get; init; }

    /// <summary>The content schema version that adds <see cref="MulticlassCaster"/>.</summary>
    public const int MulticlassSchemaVersion = 7;

    /// <summary>
    /// Content schema v9 (M5, ADR-010): the caster levels this class adds to the Multiclass Spellcaster table at each of
    /// its levels (20 entries), for a share that <see cref="MulticlassCaster"/> cannot state, such as two thirds. Exact
    /// numbers, with no rules-family rounding. Never together with <see cref="MulticlassCaster"/>; never on Pact Magic.
    /// </summary>
    public IReadOnlyList<int>? MulticlassCasterTable { get; init; }

    /// <summary>The content schema version that adds <see cref="MulticlassCasterTable"/>.</summary>
    public const int MulticlassTableSchemaVersion = 9;

    internal const string MulticlassCasterTableProperty = "multiclassCasterTable";

    /// <summary>
    /// Typed from a v5 (or newer) revision. Below v9, a <c>multiclassCasterTable</c> key stays extension data, exactly as a
    /// v8 build reads it: never typed (so it is not combined) and written back in the same place (so the hash is unchanged).
    /// </summary>
    internal static Effect FromUnknown(UnknownEffect unknown, int revisionSchemaVersion)
    {
        // The serializer matches names case-insensitively, so every spelling of the key, with any value (null, or not a
        // list), is taken out before typing, exactly as a v8 build never binds it (review fix).
        return revisionSchemaVersion >= MulticlassTableSchemaVersion
            ? VersionedEffects.Typed<SpellcastingEffect>(unknown)
            : VersionedEffects.TypedWithout<SpellcastingEffect>(unknown, MulticlassCasterTableProperty);
    }
}

/// <summary>Whether a spell needs an attack roll.</summary>
public enum SpellAttackKind { None, Melee, Ranged }

/// <summary>
/// Content schema v5: the game data of a spell, on a <see cref="ContentKind.Spell"/> revision. The revision's summary and
/// this effect's text carry the description. A spell is never active content: it applies only through a caster's list,
/// and nothing on it changes calculated fields.
/// </summary>
public sealed record SpellEffect : Effect
{
    public const string TypeName = "spell";

    public override string Type => TypeName;

    /// <summary>0 for a cantrip, else 1–9.</summary>
    public required int Level { get; init; }

    public string? School { get; init; }
    public string? CastingTime { get; init; }
    public string? Range { get; init; }
    public string? Components { get; init; }
    public string? Duration { get; init; }
    public bool Concentration { get; init; }
    public bool Ritual { get; init; }

    /// <summary>The spell lists (keys) this spell is on, for example <c>wizard</c>.</summary>
    public IReadOnlyList<string> Lists { get; init; } = [];

    public SpellAttackKind Attack { get; init; } = SpellAttackKind.None;

    /// <summary>The saving throw a target makes, if any.</summary>
    public Ability? Save { get; init; }

    /// <summary>Optional dice the sheet can roll (damage or healing at the spell's base level), for example <c>8d6</c>.</summary>
    public string? Dice { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<SpellEffect>(unknown);
}

/// <summary>
/// Effect types added after v3 are typed only in a revision of their content schema version or newer. An older revision
/// may carry the same type name as an unknown effect stored byte for byte; typing it would change its hash and meaning
/// (ADR-003, the <c>armor</c> lesson).
/// </summary>
internal static class VersionedEffects
{
    /// <summary>Each type's first version, and how to type it given the revision's own schema version.</summary>
    public static readonly IReadOnlyDictionary<string, (int Version, Func<UnknownEffect, int, Effect> Type)> ByName =
        new Dictionary<string, (int, Func<UnknownEffect, int, Effect>)>(StringComparer.Ordinal)
        {
            [ArmorEffect.TypeName] = (ArmorEffect.SchemaVersion, (u, _) => ArmorEffect.FromUnknown(u)),
            [SpellcastingEffect.TypeName] = (SpellcastingEffect.SchemaVersion, SpellcastingEffect.FromUnknown),
            [SpellEffect.TypeName] = (SpellcastingEffect.SchemaVersion, (u, _) => SpellEffect.FromUnknown(u)),
            [WeaponEffect.TypeName] = (SpellcastingEffect.SchemaVersion, (u, _) => WeaponEffect.FromUnknown(u)),
            [ToggleEffect.TypeName] = (ToggleEffect.SchemaVersion, ToggleEffect.FromUnknown),
            [ScaleEffect.TypeName] = (ScaleEffect.SchemaVersion, (u, _) => ScaleEffect.FromUnknown(u)),
            [ItemEffect.TypeName] = (ItemEffect.SchemaVersion, (u, _) => ItemEffect.FromUnknown(u)),
            [ToggleGroupEffect.TypeName] = (ToggleGroupEffect.SchemaVersion, (u, _) => ToggleGroupEffect.FromUnknown(u)),
            [UpkeepEffect.TypeName] = (UpkeepEffect.SchemaVersion, (u, _) => UpkeepEffect.FromUnknown(u)),
            [AttackEffect.TypeName] = (AttackEffect.SchemaVersion, (u, _) => AttackEffect.FromUnknown(u)),
            [AttackRiderEffect.TypeName] = (AttackRiderEffect.SchemaVersion, (u, _) => AttackRiderEffect.FromUnknown(u)),
            // Always typed, except that the converter hands these over when they carry a key or value added after v9 (see
            // EffectJsonConverter.CarriesDetailAbove), so the revision's version decides how they read. A later slice that
            // gives one of them a v11 key adds it to that type's V11Keys, and nothing else.
            [ModifierEffect.TypeName] = (1, ModifierEffect.FromUnknown),
            [GrantEffect.TypeName] = (1, GrantEffect.FromUnknown),
            [ResourceEffect.TypeName] = (1, ResourceEffect.FromUnknown),
            [RollEffect.TypeName] = (1, RollEffect.FromUnknown),
            [ValueEffect.TypeName] = (ValueEffect.SchemaVersion, (u, _) => ValueEffect.FromUnknown(u)),
            [RollModeEffect.TypeName] = (RollModeEffect.SchemaVersion, (u, _) => RollModeEffect.FromUnknown(u)),
        };

    /// <summary>
    /// Types <paramref name="unknown"/> as <typeparamref name="T"/> with the named keys kept out of the typed properties, the
    /// way a build that has no such property reads them (content schema gates, ADR-003). Every spelling of a key (the
    /// serializer matches names case-insensitively), with any value, stays in <see cref="Effect.Extensions"/> in document order,
    /// so the effect writes back byte for byte as that build writes it, and the key does nothing. An effect that does not
    /// type without the keys stays <paramref name="unknown"/>, as it would in that build.
    /// </summary>
    public static Effect TypedWithout<T>(UnknownEffect unknown, params string[] keys) where T : Effect
    {
        bool IsGated(string name) => keys.Any(k => string.Equals(name, k, StringComparison.OrdinalIgnoreCase));
        if (!unknown.Raw.EnumerateObject().Any(p => IsGated(p.Name)))
            return Typed<T>(unknown);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in unknown.Raw.EnumerateObject().Where(p => !IsGated(p.Name)))
                property.WriteTo(writer);
            writer.WriteEndObject();
        }
        using var stripped = JsonDocument.Parse(buffer.WrittenMemory);
        if (Typed<T>(UnknownEffect.From(stripped.RootElement)) is not T typed)
            return unknown; // the rest does not type either: reference-only and unchanged, as in that build
        var extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in unknown.Raw.EnumerateObject())
        {
            if (IsGated(property.Name) || typed.Extensions?.ContainsKey(property.Name) == true)
                extensions[property.Name] = property.Value.Clone(); // document order, as that build keeps it
        }
        return ((Effect)typed) with { Extensions = extensions };
    }

    /// <summary>
    /// Whether every property of <paramref name="raw"/> named <paramref name="property"/> (any case) is null or a string that
    /// is exactly the name of a <typeparamref name="TEnum"/> member, ignoring case. The serializer also binds padded names and
    /// lists of names (as combined bits), which no schema allows, so v10 refuses them instead of reading a guess.
    /// </summary>
    public static bool NamesMember<TEnum>(JsonElement raw, string property) where TEnum : struct, Enum
    {
        foreach (var p in raw.EnumerateObject())
        {
            if (!string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase) || p.Value.ValueKind == JsonValueKind.Null)
                continue;
            if (p.Value.ValueKind != JsonValueKind.String
                || !Enum.GetNames<TEnum>().Any(n => string.Equals(n, p.Value.GetString(), StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Whether every property of <paramref name="raw"/> named <paramref name="property"/> (any case) is null or a list whose
    /// items are all strings that are exactly the name of a <typeparamref name="TEnum"/> member, ignoring case (see
    /// <see cref="NamesMember{TEnum}"/> for why a padded or combined name is refused).
    /// </summary>
    public static bool NamesEveryMember<TEnum>(JsonElement raw, string property) where TEnum : struct, Enum
    {
        foreach (var p in raw.EnumerateObject())
        {
            if (!string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase) || p.Value.ValueKind == JsonValueKind.Null)
                continue;
            if (p.Value.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var item in p.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || !Enum.GetNames<TEnum>().Any(n => string.Equals(n, item.GetString(), StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
        }
        return true;
    }

    /// <summary>The typed effect, or the unknown one unchanged when its body does not fit (it stays reference-only).</summary>
    public static Effect Typed<T>(UnknownEffect unknown) where T : Effect
    {
        try
        {
            return unknown.Raw.Deserialize<T>(RulesJson.Compact) ?? (Effect)unknown;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return unknown;
        }
    }
}

/// <summary>
/// An effect type this build does not know. The original JSON is kept and written back with the same properties,
/// order and values (whitespace and string escaping are normalized), and it is never automated.
/// </summary>
public sealed record UnknownEffect : Effect
{
    public override string Type => DeclaredType;

    /// <summary>The <c>type</c> string from the JSON (empty if absent).</summary>
    public string DeclaredType { get; private init; } = "";

    public required JsonElement Raw { get; init; }

    internal static UnknownEffect From(JsonElement raw)
    {
        var id = raw.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString()! : "";
        var type = raw.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
        var text = raw.TryGetProperty("text", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
        return new UnknownEffect { Id = id, DeclaredType = type, Raw = raw.Clone(), Automation = AutomationStatus.Reference, Text = text };
    }
}

/// <summary>
/// Reads the <c>type</c> discriminator. Maps the schemaVersion 1 types (<c>abilityScoreIncrease</c>, <c>initiativeBonus</c>)
/// to <see cref="ModifierEffect"/> (ADR-003 migration). Anything unknown or malformed becomes <see cref="UnknownEffect"/>
/// instead of failing the whole revision.
/// </summary>
public sealed class EffectJsonConverter : JsonConverter<Effect>
{
    public const string LegacyAbilityScoreIncrease = "abilityScoreIncrease";
    public const string LegacyInitiativeBonus = "initiativeBonus";

    public override Effect? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("An effect must be a JSON object.");
        var type = element.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        try
        {
            return type switch
            {
                // An always-typed effect that carries a detail added after v9 (v10 grant and resource keys, any v11 key) is
                // typed by the revision, which knows its version (ContentRevision.OnDeserialized); the others are typed
                // here, as in every earlier build.
                ModifierEffect.TypeName or GrantEffect.TypeName or ResourceEffect.TypeName or RollEffect.TypeName
                    when CarriesDetailAbove(element, type, UngatedVersion) => UnknownEffect.From(element),
                ModifierEffect.TypeName => (Effect?)element.Deserialize<ModifierEffect>(options),
                GrantEffect.TypeName => element.Deserialize<GrantEffect>(options),
                ResourceEffect.TypeName => element.Deserialize<ResourceEffect>(options),
                ChoiceEffect.TypeName => element.Deserialize<ChoiceEffect>(options),
                RestrictionEffect.TypeName => element.Deserialize<RestrictionEffect>(options),
                RecoveryEffect.TypeName => element.Deserialize<RecoveryEffect>(options),
                RollEffect.TypeName => element.Deserialize<RollEffect>(options),
                HitDieEffect.TypeName => element.Deserialize<HitDieEffect>(options),
                // Typed only in revisions of their schema version (ContentRevision.OnDeserialized); older ones keep them as written.
                ArmorEffect.TypeName or SpellcastingEffect.TypeName or SpellEffect.TypeName or WeaponEffect.TypeName or ToggleEffect.TypeName or ScaleEffect.TypeName or ItemEffect.TypeName or ToggleGroupEffect.TypeName or UpkeepEffect.TypeName or AttackEffect.TypeName or AttackRiderEffect.TypeName or ValueEffect.TypeName or RollModeEffect.TypeName => UnknownEffect.From(element),
                LegacyAbilityScoreIncrease or LegacyInitiativeBonus => FromSchemaVersion1(element, type, options),
                _ => UnknownEffect.From(element),
            } ?? UnknownEffect.From(element);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // A known type with a malformed body stays reference-only and visible rather than breaking the revision.
            // JsonElement accessors throw InvalidOperationException on a wrong value kind, not JsonException.
            return UnknownEffect.From(element);
        }
    }

    /// <summary>The newest content schema version before the first always-typed effect gained a gated key (v9: the last that read none).</summary>
    private const int UngatedVersion = ContentRevision.PlayDetailsSchemaVersion - 1;

    /// <summary>
    /// Whether an always-typed effect (<c>modifier</c>, <c>grant</c>, <c>resource</c>, <c>roll</c>) has something a build of
    /// content schema <paramref name="version"/> did not read, so the revision, which knows its own version, must type it
    /// (<see cref="VersionedEffects.ByName"/>). Detail from v10: a grant that names a damage kind (a value the v9 enum cannot
    /// hold, matched case-insensitively like the serializer) or has a <c>damageType</c> key (any spelling), or a resource with
    /// an <c>activation</c> key. Detail from v11: any key in the type's <c>V11Keys</c> (empty until a later slice adds one).
    /// The names are the ones the serializer binds to those properties.
    /// </summary>
    private static bool CarriesDetailAbove(JsonElement element, string type, int version)
    {
        var newest = type switch
        {
            ModifierEffect.TypeName => ModifierEffect.V11Keys,
            GrantEffect.TypeName => GrantEffect.V11Keys,
            ResourceEffect.TypeName => ResourceEffect.V11Keys,
            RollEffect.TypeName => RollEffect.V11Keys,
            _ => [],
        };
        // Content v11: a roll may omit its dice; below v11 it must stay untyped, and only the revision knows its version.
        if (version < ContentRevision.PlayMechanicsSchemaVersion && type == RollEffect.TypeName && RollEffect.LacksDice(element))
            return true;
        foreach (var property in element.EnumerateObject())
        {
            if (version < ContentRevision.PlayMechanicsSchemaVersion && newest.Any(k => string.Equals(property.Name, k, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (version >= ContentRevision.PlayDetailsSchemaVersion)
                continue;
            if (type == ResourceEffect.TypeName)
            {
                if (string.Equals(property.Name, ToggleEffect.ActivationProperty, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (type == GrantEffect.TypeName
                && (string.Equals(property.Name, GrantEffect.DamageTypeProperty, StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(property.Name, "grant", StringComparison.OrdinalIgnoreCase)
                        && !(property.Value.ValueKind == JsonValueKind.String && GrantEffect.IsPreV10Name(property.Value.GetString())))))
            {
                return true;
            }
        }
        return false;
    }

    public override void Write(Utf8JsonWriter writer, Effect value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        if (value is UnknownEffect unknown)
        {
            unknown.Raw.WriteTo(writer);
            return;
        }
        // Deterministic layout (revision hashes depend on it): type, id, then the rest in serializer order.
        var body = JsonSerializer.SerializeToElement(value, value.GetType(), options);
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        writer.WriteString("id", value.Id);
        foreach (var property in body.EnumerateObject())
        {
            if (property.Name is not ("type" or "id"))
                property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// schemaVersion 1 <c>{ id, type, ability?, amount?, automation?, text? }</c> to a typed bonus. Anything the v1
    /// build could not have written (no string id, a non-integer amount, an unknown ability) throws, so the effect is
    /// kept unchanged as <see cref="UnknownEffect"/> instead of being mapped with data dropped.
    /// </summary>
    private static ModifierEffect FromSchemaVersion1(JsonElement element, string type, JsonSerializerOptions options)
    {
        var id = element.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String
            ? i.GetString()!
            : throw new JsonException("Effect id is required.");
        var automation = element.TryGetProperty("automation", out var a)
            ? a.Deserialize<AutomationStatus>(options)
            : AutomationStatus.Automatic;
        var text = element.TryGetProperty("text", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
        var amount = element.TryGetProperty("amount", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var value)
            ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : throw new JsonException("Effect amount must be an integer.");
        var target = type == LegacyInitiativeBonus
            ? FieldIds.Initiative
            : element.TryGetProperty("ability", out var ab) && ab.ValueKind == JsonValueKind.String
                && ab.GetString() is { Length: > 0 } name && name.All(char.IsAsciiLetter)
                && Enum.TryParse<Ability>(name, ignoreCase: true, out var ability)
                ? FieldIds.Score(ability)
                : throw new JsonException("Effect ability is missing or unknown.");

        var extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name is not ("id" or "type" or "ability" or "amount" or "automation" or "text"))
                extensions[property.Name] = property.Value.Clone();
        }

        return new ModifierEffect
        {
            Id = id,
            Operation = ModifierOperation.Bonus,
            Target = target,
            Value = amount,
            Automation = automation,
            Text = text,
            Extensions = extensions.Count > 0 ? extensions : null,
        };
    }
}
