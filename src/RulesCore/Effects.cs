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

public enum GrantKind { Proficiency, Expertise, Content }

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

    /// <summary>Dice expression, e.g. <c>1d8+2</c>.</summary>
    public required string Dice { get; init; }

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

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<ToggleEffect>(unknown);
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

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<SpellcastingEffect>(unknown);
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
    public static readonly IReadOnlyDictionary<string, (int Version, Func<UnknownEffect, Effect> Type)> ByName =
        new Dictionary<string, (int, Func<UnknownEffect, Effect>)>(StringComparer.Ordinal)
        {
            [ArmorEffect.TypeName] = (ArmorEffect.SchemaVersion, ArmorEffect.FromUnknown),
            [SpellcastingEffect.TypeName] = (SpellcastingEffect.SchemaVersion, SpellcastingEffect.FromUnknown),
            [SpellEffect.TypeName] = (SpellcastingEffect.SchemaVersion, SpellEffect.FromUnknown),
            [WeaponEffect.TypeName] = (SpellcastingEffect.SchemaVersion, WeaponEffect.FromUnknown),
            [ToggleEffect.TypeName] = (ToggleEffect.SchemaVersion, ToggleEffect.FromUnknown),
        };

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
                ModifierEffect.TypeName => (Effect?)element.Deserialize<ModifierEffect>(options),
                GrantEffect.TypeName => element.Deserialize<GrantEffect>(options),
                ResourceEffect.TypeName => element.Deserialize<ResourceEffect>(options),
                ChoiceEffect.TypeName => element.Deserialize<ChoiceEffect>(options),
                RestrictionEffect.TypeName => element.Deserialize<RestrictionEffect>(options),
                RecoveryEffect.TypeName => element.Deserialize<RecoveryEffect>(options),
                RollEffect.TypeName => element.Deserialize<RollEffect>(options),
                HitDieEffect.TypeName => element.Deserialize<HitDieEffect>(options),
                // Typed only in revisions of their schema version (ContentRevision.OnDeserialized); older ones keep them as written.
                ArmorEffect.TypeName or SpellcastingEffect.TypeName or SpellEffect.TypeName or WeaponEffect.TypeName or ToggleEffect.TypeName => UnknownEffect.From(element),
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
