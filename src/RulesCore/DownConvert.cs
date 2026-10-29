using System.Text.Json;
using System.Text.Json.Nodes;

namespace TomeStack.RulesCore;

/// <summary>What lowering does to one thing: <see cref="Dropped"/> loses it and says so, <see cref="Blocks"/> stops the export, <see cref="Silent"/> loses it with nothing to tell.</summary>
public enum BelowAction { Dropped, Blocks, Silent }

/// <summary>
/// One thing lowering a record to an older schema loses or refuses. <paramref name="Path"/> is a JSON pointer-like label for the preview
/// ("play.encounter", "effects[3].upkeep", "effects[1].modifier.target"); <paramref name="Code"/> is a stable key (<c>below.*</c>) the service
/// maps to the package's target-drops and target-blocked lists.
/// </summary>
public sealed record BelowChange(string Path, BelowAction Action, string Code, string Message);

/// <summary>A character lowered to an older schema, with what it lost. When <see cref="Blocked"/>, <see cref="Character"/> is the input, unchanged.</summary>
public sealed record CharacterBelow(Character Character, IReadOnlyList<BelowChange> Changes)
{
    public bool Blocked => Changes.Any(c => c.Action == BelowAction.Blocks);
}

/// <summary>A content revision lowered to an older schema, with what it lost. <see cref="Revision"/> is always the lowered copy, blocked fields removed too; when <see cref="Blocked"/> the caller must not write it.</summary>
public sealed record ContentBelow(ContentRevision Revision, IReadOnlyList<BelowChange> Changes)
{
    public bool Blocked => Changes.Any(c => c.Action == BelowAction.Blocks);
}

/// <summary>The stable <c>below.*</c> keys a <see cref="BelowChange"/> carries. The service maps each to a message; <see cref="DownConvert.KnownCodes"/> lists them.</summary>
public static class BelowCodes
{
    public const string EncounterRunning = "below.encounter-running";
    public const string RoundsLeft = "below.rounds-left";
    public const string Details = "below.details";
    public const string CustomItems = "below.custom-items";
    public const string CustomAttacks = "below.custom-attacks";
    public const string DamageDefences = "below.damage-defences";
    public const string Vulnerability = "below.vulnerability";
    public const string Companions = "below.companions";
    public const string Xp = "below.xp";
    public const string EquipmentState = "below.equipment-state";
    public const string EffectUnsupported = "below.effect-unsupported";
    public const string RollModeDisadvantage = "below.roll-mode-disadvantage";
    public const string ValueFormula = "below.value-formula";
    public const string Upkeep = "below.upkeep";
    public const string ToggleGroup = "below.toggle-group";
    public const string ToggleCost = "below.toggle-cost";
    public const string ToggleLevel = "below.toggle-level";
    public const string ToggleDuration = "below.toggle-duration";
    public const string ToggleAutoWhen = "below.toggle-auto-when";
    public const string ResourceDetail = "below.resource-detail";
    public const string ResourceCountsUp = "below.resource-counts-up";
    public const string RollDetail = "below.roll-detail";
    public const string RollAlsoChange = "below.roll-also-change";
    public const string DicelessRoll = "below.diceless-roll";
    public const string ModifierToggleContent = "below.modifier-toggle-content";
    public const string UnknownTarget = "below.unknown-target";
    public const string RestrictionLifted = "below.restriction-lifted";
    public const string DefenceWhileToggle = "below.defence-while-toggle";
    public const string GrantDefence = "below.grant-defence";
    public const string ItemAttunement = "below.item-attunement";
    public const string ItemCharges = "below.item-charges";
    public const string Activation = "below.activation";
    public const string WeaponBonus = "below.weapon-bonus";
    public const string V12Key = "below.v12-key";
    public const string Payment = "below.payment";
    public const string CastsSpell = "below.casts-spell";
    public const string ItemMods = "below.item-mods";
    public const string ItemModCopies = "below.item-mod-copies";
    public const string ValidationError = "below.validation-error";
}

/// <summary>
/// Lowers records to an older schema for a down-conversion export (pure: no I/O, no clock). Each step names what the older reader cannot hold;
/// anything that would change what the record means blocks instead of being dropped.
/// </summary>
public static class DownConvert
{
    /// <summary>Every code <see cref="BelowChange.Code"/> can carry (the <see cref="BelowCodes"/> constants).</summary>
    public static IReadOnlySet<string> KnownCodes { get; } = typeof(BelowCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Lowers <paramref name="character"/> to <paramref name="targetCharacterSchema"/> (8 to <see cref="Character.CurrentSchemaVersion"/>).
    /// Returns the same instance and no changes when nothing is above the target. When any change blocks, returns the input instance
    /// with the full change list; otherwise the lowered copy, whose <see cref="Character.SchemaVersion"/> is at most the target (it is computed from the data, so a
    /// copy left with no v9 field reads as v8).
    /// </summary>
    public static CharacterBelow Below(Character character, int targetCharacterSchema)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetCharacterSchema, Character.PreviousSchemaVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetCharacterSchema, Character.CurrentSchemaVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(character.SchemaVersion, Character.CurrentSchemaVersion);

        if (character.SchemaVersion <= targetCharacterSchema)
            return new CharacterBelow(character, []);

        var changes = new List<BelowChange>();
        var lowered = character;
        var name = character.Name;

        if (targetCharacterSchema < Character.ItemModsSchemaVersion && character.UsesV11Fields)
            lowered = BelowV11(lowered, name, changes);
        if (targetCharacterSchema < Character.EncounterSchemaVersion && character.UsesV10Fields)
            lowered = BelowV10(lowered, name, changes);
        if (targetCharacterSchema < Character.DetailsSchemaVersion && character.UsesV9Fields)
            lowered = BelowV9(lowered, name, changes);
        // Stripping the entry ids can leave two entries of one item that only the ids kept apart; an older build refuses that.
        if (!ReferenceEquals(lowered, character) && changes.All(c => c.Action != BelowAction.Blocks)
            && lowered.Validate().FirstOrDefault(d => d.Code == "character.equipment-duplicate") is { } duplicate)
            changes.Add(new("equipment", BelowAction.Blocks, BelowCodes.ValidationError, $"'{name}': {duplicate.Message} Remove one copy, then export."));

        return changes.Any(c => c.Action == BelowAction.Blocks)
            ? new CharacterBelow(character, changes)
            : new CharacterBelow(lowered, changes);
    }

    /// <summary>
    /// Lowers <paramref name="revision"/> to <paramref name="targetContentSchema"/> (9 to <see cref="ContentRevision.CurrentSchemaVersion"/>).
    /// Never touches the ids, provenance, status or name, and never rewrites <c>grant.content</c> or <c>choice.options</c> (the service does).
    /// Blocked fields are removed too: the copy is always the lowered one, and <see cref="ContentBelow.Blocked"/> tells the caller not to write it.
    /// Returns the same instance and no changes when nothing is above the target.
    /// The lowered copy is then validated at the target version (every error blocks, code <c>below.validation-error</c>): with a
    /// <paramref name="catalog"/>, against it; with none, against a stub that knows the copy's own source and no revisions, so the
    /// null-catalog mode skips the availability checks (<c>validate.source-missing</c>, <c>validate.reference-missing</c>). The
    /// service passes its own catalog (lane C should pass it).
    /// </summary>
    public static ContentBelow Below(ContentRevision revision, int targetContentSchema, IContentCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetContentSchema, ContentRevision.PlayDetailsSchemaVersion - 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetContentSchema, ContentRevision.CurrentSchemaVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(revision.SchemaVersion, ContentRevision.CurrentSchemaVersion);

        var own = revision.SchemaVersion;
        // Parsed case-sensitively (the serializer's own node would be case-insensitive and throw on two spellings of one key); lookups ignore case themselves.
        var node = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(revision, RulesJson.Compact))!.AsObject();
        var changes = new List<BelowChange>();
        var values = revision.Effects.OfType<ValueEffect>().GroupBy(v => v.ValueId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Formula, StringComparer.Ordinal);
        // Where each effect of the working array was in the input, so paths keep the original index after earlier steps removed effects.
        var origin = Enumerable.Range(0, revision.Effects.Count).ToList();
        var scope = new Scope(revision, targetContentSchema, values, origin);

        // One step per schema version above the target, newest first.
        for (var step = ContentRevision.CurrentSchemaVersion; step > targetContentSchema; step--)
        {
            switch (step)
            {
                case ContentRevision.WeaponBonusSchemaVersion:
                    ContentBelowV12(scope, node, changes);
                    break;
                case ContentRevision.PlayMechanicsSchemaVersion:
                    ContentBelowV11(scope, node, changes);
                    break;
                case ContentRevision.PlayDetailsSchemaVersion:
                    ContentBelowV10(scope, node, changes);
                    break;
                default:
                    throw new System.Diagnostics.UnreachableException($"No lowering step for content v{step}.");
            }
        }

        if (own <= targetContentSchema && changes.Count == 0)
            return new ContentBelow(revision, changes);

        node["schemaVersion"] = Math.Min(own, targetContentSchema);
        var copy = node.Deserialize<ContentRevision>(RulesJson.Options)!;
        ValidateAtTarget(copy, catalog, origin, changes);
        return new ContentBelow(copy, changes);
    }

    /// <summary>What the steps share: the input, the version being written, its named values and the original effect indexes.</summary>
    private sealed record Scope(ContentRevision Revision, int Target, IReadOnlyDictionary<string, string> Values, List<int> Origin)
    {
        /// <summary>The sign of each named value, once worked out (true when it can never be below zero).</summary>
        public Dictionary<string, bool> Signs { get; } = new(StringComparer.Ordinal);

        public int Own => Revision.SchemaVersion;

        public string Name => Revision.Name;
    }

    /// <summary>A catalog that knows one source and no revisions: validation of a lowered copy with no catalog to hand.</summary>
    private sealed class SourceOnlyCatalog(SourceRecord source) : IContentCatalog
    {
        public ContentRevision? FindRevision(ContentReference reference) => null;

        public SourceRecord? FindSource(Guid sourceId) => sourceId == source.Id ? source : null;
    }

    private static void ValidateAtTarget(ContentRevision copy, IContentCatalog? catalog, List<int> origin, List<BelowChange> changes)
    {
        var skipAvailability = catalog is null;
        catalog ??= new SourceOnlyCatalog(new SourceRecord
        {
            Id = copy.Provenance.SourceId, Title = "", Publisher = "", RulesFamilies = copy.RulesFamilies,
            EditionVersion = "", License = "", Redistributable = false,
        });
        foreach (var error in ContentValidator.Validate(copy, catalog).Errors)
        {
            if (skipAvailability && error.Code is "validate.source-missing" or "validate.reference-missing")
                continue;
            var index = error.EffectId is null ? -1 : copy.Effects.ToList().FindIndex(e => e.Id == error.EffectId);
            // The copy's index back to the input's, since earlier steps removed effects.
            var path = index < 0 ? "revision" : $"effects[{(index < origin.Count ? origin[index] : index)}]";
            changes.Add(new(path, BelowAction.Blocks, BelowCodes.ValidationError, $"'{copy.Name}': {error.Message}"));
        }
    }

    // ---- shared helpers for the by-value classification ----------------------------------------------------------------

    private const FormulaFeatures AllFeatures = FormulaFeatures.Scales | FormulaFeatures.Values;

    /// <summary>
    /// Whether the formula can never be below zero for any character: it parses and is made only of non-negative numbers, identifiers
    /// (a <c>VALUE.id</c> only when this revision declares it with a formula that is itself so) and sums and products of those.
    /// Identifiers such as <c>STR.MOD</c> count (spec §2).
    /// </summary>
    private static bool StaticallyNonNegative(string? source, Scope scope)
    {
        if (!Formula.TryParse(source, AllFeatures, out var formula, out _))
            return false;
        return NonNegative(formula!.Root);

        bool NonNegative(FormulaNode node) => node switch
        {
            NumberNode n => n.Value >= 0,
            IdentifierNode i when FormulaIdentifiers.IsValue(i.Name) => ValueNonNegative(FormulaIdentifiers.ValueId(i.Name)),
            IdentifierNode => true,
            BinaryNode { Operator: '+' or '*' } b => NonNegative(b.Left) && NonNegative(b.Right),
            _ => false,
        };

        // Each id's sign is worked out once per call; an id being worked out reads as false, which is the cycle guard.
        bool ValueNonNegative(string id)
        {
            if (scope.Signs.TryGetValue(id, out var known))
                return known;
            if (!scope.Values.TryGetValue(id, out var inner))
                return false;
            scope.Signs[id] = false;
            var sign = StaticallyNonNegative(inner, scope);
            scope.Signs[id] = sign;
            return sign;
        }
    }

    private static bool ReadsValue(string? source) =>
        Formula.TryParse(source, AllFeatures, out var formula, out _) && formula!.Identifiers.Any(FormulaIdentifiers.IsValue);

    private static string? TypeOf(JsonObject effect) => StringOf(Property(effect, "type"));

    private static bool IsRemovedTarget(string? target, Scope scope) =>
        target is not null
        && ((scope.Own >= ContentRevision.PlayMechanicsSchemaVersion && scope.Target < ContentRevision.PlayMechanicsSchemaVersion
                && (target.StartsWith(ContentValidator.ResourceTargetPrefix, StringComparison.Ordinal) || target == FieldIds.FlySpeed))
            || (scope.Own >= ContentRevision.PlayDetailsSchemaVersion && scope.Target < ContentRevision.PlayDetailsSchemaVersion
                && CharacterCalculator.IsSpeedOrPassive(target)));

    /// <summary>A modifier or restriction on a target the target version lacks. Returns whether the effect was removed.</summary>
    private static bool RemovedTargetBelow(JsonObject effect, string? type, int index, Scope scope, List<BelowChange> changes)
    {
        var path = $"effects[{scope.Origin[index]}]";
        var name = scope.Name;
        switch (type)
        {
            case ModifierEffect.TypeName when StringOf(Property(effect, "target")) is { } target && IsRemovedTarget(target, scope):
                changes.Add(string.Equals(StringOf(Property(effect, "operation")), nameof(ModifierOperation.Bonus), StringComparison.OrdinalIgnoreCase)
                    && StaticallyNonNegative(StringOf(Property(effect, "value")), scope)
                    ? new(path, BelowAction.Dropped, BelowCodes.UnknownTarget, $"'{name}': its bonus to '{target}', which the older version has no field for.")
                    : new(path, BelowAction.Blocks, BelowCodes.UnknownTarget, $"'{name}': its change to '{target}', which the older version has no field for; it could set or reduce the value, so leaving it out would change the result."));
                return true;
            case RestrictionEffect.TypeName when StringOf(Property(effect, "field")) is { } field && IsRemovedTarget(field, scope):
                changes.Add(new(path, BelowAction.Blocks, BelowCodes.RestrictionLifted, $"'{name}': its limit on '{field}', which the older version has no field for; the limit would be lifted."));
                return true;
            default:
                return false;
        }
    }

    private static readonly Dictionary<string, string[]> FormulaFields = new(StringComparer.Ordinal)
    {
        [ModifierEffect.TypeName] = ["value"],
        [ResourceEffect.TypeName] = ["maximum"],
        [RecoveryEffect.TypeName] = ["amount"],
        [RollEffect.TypeName] = ["cost", "bonus"],
        [SpellcastingEffect.TypeName] = ["spellsFormula"],
    };

    /// <summary>A remaining effect with a formula that reads <c>VALUE.</c>, which v10 cannot evaluate. Returns whether the effect was removed.</summary>
    private static bool ValueFormulaBelow(JsonObject effect, string? type, int index, Scope scope, List<BelowChange> changes)
    {
        if (type is null || !FormulaFields.TryGetValue(type, out var fields))
            return false;
        var field = fields.FirstOrDefault(f => ReadsValue(StringOf(Property(effect, f))));
        if (field is null)
            return false;
        var path = $"effects[{scope.Origin[index]}]";
        var name = scope.Name;
        if (type == ModifierEffect.TypeName)
        {
            var bonus = string.Equals(StringOf(Property(effect, "operation")), "bonus", StringComparison.OrdinalIgnoreCase);
            changes.Add(bonus && StaticallyNonNegative(StringOf(Property(effect, "value")), scope)
                ? new(path, BelowAction.Dropped, BelowCodes.ValueFormula, $"'{name}': a bonus that reads a named value, which the older version cannot calculate.")
                : new(path, BelowAction.Blocks, BelowCodes.ValueFormula, $"'{name}': a modifier that reads a named value, which the older version cannot calculate; leaving it out would change the result."));
        }
        else
        {
            changes.Add(new(path, BelowAction.Blocks, BelowCodes.ValueFormula, $"'{name}': its {field} reads a named value, which the older version cannot calculate."));
        }
        return true;
    }

    /// <summary>
    /// The 10 to 9 step (content v10): speed and passive targets, damage defences, <c>autoWhen</c>, <c>activation</c> and <c>item</c>.
    /// A revision that declares v9 only loses the inert v10 keys that a typed toggle, resource or grant carries as extension data (silently).
    /// </summary>
    private static void ContentBelowV10(Scope scope, JsonObject node, List<BelowChange> changes)
    {
        if (node["effects"] is not JsonArray effects)
            return;
        var declaresV10 = scope.Own >= ContentRevision.PlayDetailsSchemaVersion;
        var revision = scope.Revision;
        var name = scope.Name;
        var kept = new List<JsonNode?>(effects.Count);
        var keptOrigin = new List<int>(effects.Count);

        for (var i = 0; i < effects.Count; i++)
        {
            var element = effects[i];
            if (element is not JsonObject effect)
            {
                kept.Add(element?.DeepClone());
                keptOrigin.Add(scope.Origin[i]);
                continue;
            }

            var type = TypeOf(effect);
            var path = $"effects[{scope.Origin[i]}]";
            if (RemovedTargetBelow(effect, type, i, scope, changes) || (declaresV10 && WholeEffectBelowV10(effect, type, path, name, changes)))
                continue;

            // A v9 revision holds the v10 keys of a typed effect as inert extension data; an untyped one is written back as it is.
            var originalIndex = scope.Origin[i];
            var typed = originalIndex < revision.Effects.Count && revision.Effects[originalIndex] is not UnknownEffect;
            if (declaresV10 || typed)
            {
                switch (type)
                {
                    case ToggleEffect.TypeName:
                        RemoveKey(effect, ToggleEffect.AutoWhenProperty, path, declaresV10 ? BelowAction.Blocks : BelowAction.Silent, BelowCodes.ToggleAutoWhen, $"'{name}': when a toggle switches itself on; it would stay off until turned on by hand.", changes);
                        RemoveKey(effect, ToggleEffect.ActivationProperty, path, BelowAction.Silent, BelowCodes.Activation, $"'{name}': the kind of action a toggle needs.", changes);
                        break;
                    case ResourceEffect.TypeName:
                        RemoveKey(effect, ToggleEffect.ActivationProperty, path, BelowAction.Silent, BelowCodes.Activation, $"'{name}': the kind of action a resource needs.", changes);
                        break;
                    case GrantEffect.TypeName:
                        RemoveKey(effect, GrantEffect.DamageTypeProperty, path, BelowAction.Silent, BelowCodes.GrantDefence, $"'{name}': a damage type on a grant that does not use one.", changes);
                        break;
                }
            }
            kept.Add(effect);
            keptOrigin.Add(scope.Origin[i]);
        }

        effects.Clear();
        foreach (var item in kept)
            effects.Add(item);
        scope.Origin.Clear();
        scope.Origin.AddRange(keptOrigin);
    }

    private static void RemoveKey(JsonObject effect, string key, string path, BelowAction action, string code, string message, List<BelowChange> changes)
    {
        var spellings = effect.Select(p => p.Key).Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (spellings.Count == 0)
            return;
        foreach (var spelling in spellings)
            effect.Remove(spelling);
        changes.Add(new($"{path}.{key}", action, code, message));
    }

    /// <summary>Handles an effect that v9 cannot hold at all (damage defence grants, <c>item</c>). Returns whether the effect was removed.</summary>
    private static bool WholeEffectBelowV10(JsonObject effect, string? type, string path, string name, List<BelowChange> changes)
    {
        switch (type)
        {
            case GrantEffect.TypeName:
                var grant = StringOf(Property(effect, "grant"));
                if (string.Equals(grant, nameof(GrantKind.DamageVulnerability), StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(new(path, BelowAction.Blocks, BelowCodes.Vulnerability, $"'{name}': its vulnerability to damage; the older version cannot hold it."));
                    return true;
                }
                if (string.Equals(grant, nameof(GrantKind.DamageResistance), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(grant, nameof(GrantKind.DamageImmunity), StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(new(path, BelowAction.Dropped, BelowCodes.GrantDefence, $"'{name}': its resistance or immunity to damage."));
                    return true;
                }
                return false;
            case ItemEffect.TypeName:
                var attunes = Property(effect, "requiresAttunement") is JsonValue flag && flag.TryGetValue<bool>(out var requires) && requires;
                var charges = Property(effect, "charges") is not null;
                if (attunes)
                    changes.Add(new(path, BelowAction.Blocks, BelowCodes.ItemAttunement, $"'{name}': that the item needs attunement; the older version would let anyone use it."));
                if (charges)
                    changes.Add(new(path, BelowAction.Dropped, BelowCodes.ItemCharges, $"'{name}': the charges of the item."));
                if (!attunes && !charges)
                    changes.Add(new(path, BelowAction.Silent, Unsupported, $"'{name}': an item effect that sets nothing."));
                return true;
            default:
                return false;
        }
    }

    private const string Unsupported = BelowCodes.EffectUnsupported;

    /// <summary>
    /// The 11 to 10 step: whole effects and the toggle, resource, roll, modifier and grant fields that v11 added. A revision that declares an
    /// older version only loses the inert v11 keys that a typed effect carries as extension data (silently); its v11-type effects stay
    /// as they are, since every reader already treats them as reference-only.
    /// </summary>
    private static void ContentBelowV11(Scope scope, JsonObject node, List<BelowChange> changes)
    {
        if (node["effects"] is not JsonArray effects)
            return;
        var revision = scope.Revision;
        var declaresV11 = revision.SchemaVersion >= ContentRevision.PlayMechanicsSchemaVersion;
        var name = revision.Name;
        var kept = new List<JsonNode?>(effects.Count);
        var keptOrigin = new List<int>(effects.Count);

        for (var i = 0; i < effects.Count; i++)
        {
            var element = effects[i];
            if (element is not JsonObject effect)
            {
                kept.Add(element?.DeepClone());
                keptOrigin.Add(scope.Origin[i]);
                continue;
            }

            var type = TypeOf(effect);
            if (declaresV11 && WholeEffectBelowV11(type, effect, scope.Origin[i], name, changes))
                continue;
            if (RemovedTargetBelow(effect, type, i, scope, changes))
                continue;
            if (declaresV11 && ValueFormulaBelow(effect, type, i, scope, changes))
                continue;

            string[] keys = type switch
            {
                ToggleEffect.TypeName => ToggleEffect.V11Keys,
                ResourceEffect.TypeName => ResourceEffect.V11Keys,
                RollEffect.TypeName => RollEffect.V11Keys,
                ModifierEffect.TypeName => ModifierEffect.V11Keys,
                GrantEffect.TypeName => GrantEffect.V11Keys,
                _ => [],
            };
            // Below v11 only a typed effect holds its v11 keys as inert extension data; an untyped one is written back as it is.
            var typed = i < revision.Effects.Count && revision.Effects[i] is not UnknownEffect;
            if (keys.Length > 0 && (declaresV11 || typed))
            {
                var done = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (key, value) in effect.ToList())
                {
                    var canonical = keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                    // Every spelling of the key goes, with one change (for the first spelling's value).
                    if (canonical is null || !done.Add(canonical))
                    {
                        if (canonical is not null)
                            effect.Remove(key);
                        continue;
                    }
                    effect.Remove(key);
                    changes.Add(declaresV11 ? KeyBelowV11(type!, canonical, value, scope.Origin[i], name) : KeyBelowV11(type!, canonical, value, scope.Origin[i], name) with { Action = BelowAction.Silent });
                }
            }
            kept.Add(effect);
            keptOrigin.Add(scope.Origin[i]);
        }

        effects.Clear();
        foreach (var item in kept)
            effects.Add(item);
        scope.Origin.Clear();
        scope.Origin.AddRange(keptOrigin);
    }

    /// <summary>
    /// The 12 to 11 step: the item mod effect, and the keys v12 added (weapon bonus, spell tags, replaceable choices, payment, castsSpell).
    /// A revision that declares an older version carries them as inert extension data, so it is left as it is.
    /// </summary>
    private static void ContentBelowV12(Scope scope, JsonObject node, List<BelowChange> changes)
    {
        if (node["effects"] is not JsonArray effects || scope.Revision.SchemaVersion < ContentRevision.WeaponBonusSchemaVersion)
            return;
        var name = scope.Revision.Name;
        var kept = new List<JsonNode?>(effects.Count);
        var keptOrigin = new List<int>(effects.Count);

        for (var i = 0; i < effects.Count; i++)
        {
            var path = $"effects[{scope.Origin[i]}]";
            if (effects[i] is JsonObject effect)
            {
                switch (TypeOf(effect))
                {
                    case ItemModEffect.TypeName:
                        changes.Add(new(path, BelowAction.Dropped, BelowCodes.EffectUnsupported, $"'{name}': an item modification, which the older version cannot hold."));
                        continue;
                    case WeaponEffect.TypeName:
                        RemoveKey(effect, "bonus", path, BelowAction.Dropped, BelowCodes.WeaponBonus, $"'{name}': the weapon's bonus to hit and damage.", changes);
                        break;
                    case SpellEffect.TypeName:
                        RemoveKey(effect, "tags", path, BelowAction.Dropped, BelowCodes.V12Key, $"'{name}': the spell's tags, which only filter the Spells tab.", changes);
                        break;
                    case ChoiceEffect.TypeName:
                        RemoveKey(effect, "replaceable", path, BelowAction.Dropped, BelowCodes.V12Key, $"'{name}': when a choice may be replaced.", changes);
                        break;
                    case RollEffect.TypeName:
                        PaymentBelow(effect, path, name, "a roll", changes);
                        RemoveKey(effect, "castsSpell", path, BelowAction.Blocks, BelowCodes.CastsSpell, $"'{name}': the spell a roll casts; the older version would leave the spell out.", changes);
                        break;
                    case AttackEffect.TypeName:
                    case ToggleEffect.TypeName:
                        PaymentBelow(effect, path, name, "it", changes);
                        break;
                }
            }
            kept.Add(effects[i]?.DeepClone());
            keptOrigin.Add(scope.Origin[i]);
        }

        effects.Clear();
        foreach (var item in kept)
            effects.Add(item);
        scope.Origin.Clear();
        scope.Origin.AddRange(keptOrigin);
    }

    /// <summary>
    /// A <c>payment</c> of kind resource (or with no kind) is today's pool payment, so leaving it out loses nothing and is dropped; hit points and
    /// spell slots would be charged wrongly (or not at all) by an older version, so they block.
    /// </summary>
    private static void PaymentBelow(JsonObject effect, string path, string name, string who, List<BelowChange> changes)
    {
        var lossless = Property(effect, "payment") is JsonObject payment
            && (Property(payment, "kind") is not { } kind || string.Equals(StringOf(kind), "resource", StringComparison.OrdinalIgnoreCase));
        if (lossless)
            RemoveKey(effect, "payment", path, BelowAction.Dropped, BelowCodes.V12Key, $"'{name}': that it pays from a resource pool, which is how the older version always pays.", changes);
        else
            RemoveKey(effect, "payment", path, BelowAction.Blocks, BelowCodes.Payment, $"'{name}': how {who} pays its cost with hit points or a spell slot; the older version would charge the wrong thing.", changes);
    }

    /// <summary>Handles an effect that v10 cannot hold at all. Returns whether the effect was removed.</summary>
    private static bool WholeEffectBelowV11(string? type, JsonObject effect, int index, string name, List<BelowChange> changes)
    {
        var path = $"effects[{index}]";
        switch (type)
        {
            case AttackEffect.TypeName:
                changes.Add(new(path, BelowAction.Dropped, Unsupported, $"'{name}': its attack; the feature stays as text."));
                return true;
            case AttackRiderEffect.TypeName:
                changes.Add(new(path, BelowAction.Dropped, Unsupported, $"'{name}': its attack rider; the feature stays as text."));
                return true;
            case ResponseEffect.TypeName:
                changes.Add(new(path, BelowAction.Dropped, Unsupported, $"'{name}': its response; the feature stays as text."));
                return true;
            case ValueEffect.TypeName:
                changes.Add(new(path, BelowAction.Dropped, Unsupported, $"'{name}': its named value; the feature stays as text."));
                return true;
            case RollModeEffect.TypeName:
                var mode = StringOf(Property(effect, "mode"));
                changes.Add(string.Equals(mode, "advantage", StringComparison.OrdinalIgnoreCase)
                    ? new(path, BelowAction.Dropped, Unsupported, $"'{name}': its advantage on rolls; the feature stays as text.")
                    : new(path, BelowAction.Blocks, BelowCodes.RollModeDisadvantage, $"'{name}': its disadvantage on rolls; the older version would leave it out."));
                return true;
            case UpkeepEffect.TypeName:
                changes.Add(string.Equals(StringOf(Property(effect, "change")), "regain", StringComparison.OrdinalIgnoreCase)
                    ? new(path, BelowAction.Dropped, BelowCodes.Upkeep, $"'{name}': its upkeep that regains a resource.")
                    : new(path, BelowAction.Blocks, BelowCodes.Upkeep, $"'{name}': its upkeep that spends a resource; the older version would never charge it."));
                return true;
            case ToggleGroupEffect.TypeName:
                changes.Add(new(path, BelowAction.Blocks, BelowCodes.ToggleGroup, $"'{name}': the form group; two forms could be on at once."));
                return true;
            case RollEffect.TypeName when Property(effect, "dice") is null:
                changes.Add(new(path, BelowAction.Dropped, BelowCodes.DicelessRoll, $"'{name}': a roll with no dice; the feature stays as text."));
                return true;
            default:
                return false;
        }
    }

    private static BelowChange KeyBelowV11(string type, string key, JsonNode? value, int index, string name)
    {
        var path = $"effects[{index}].{key}";
        switch (type, key)
        {
            case (ToggleEffect.TypeName, "cost" or "resourceContent"):
                return new(path, BelowAction.Blocks, BelowCodes.ToggleCost, $"'{name}': what a toggle costs; it would turn on for free.");
            case (ToggleEffect.TypeName, "level"):
                return new(path, BelowAction.Blocks, BelowCodes.ToggleLevel, $"'{name}': the level a toggle is used at; it would work at one fixed level.");
            case (ToggleEffect.TypeName, "duration"):
                return new(path, BelowAction.Dropped, BelowCodes.ToggleDuration, $"'{name}': how long a toggle lasts; it stays on until turned off.");
            case (ToggleEffect.TypeName, _):
                return new(path, BelowAction.Blocks, BelowCodes.ToggleGroup, $"'{name}': the form group ({key}); forms could stack that should not.");
            case (ResourceEffect.TypeName, "counts"):
                return string.Equals(StringOf(value), "up", StringComparison.OrdinalIgnoreCase)
                    ? new(path, BelowAction.Blocks, BelowCodes.ResourceCountsUp, $"'{name}': a resource that counts up; it would count down instead.")
                    : new(path, BelowAction.Dropped, BelowCodes.ResourceDetail, $"'{name}': how a resource counts.");
            case (ResourceEffect.TypeName, _):
                return new(path, BelowAction.Dropped, BelowCodes.ResourceDetail, $"'{name}': the {key} of a resource.");
            case (RollEffect.TypeName, "alsoChange") when value is null or JsonArray { Count: 0 }:
                return new(path, BelowAction.Silent, BelowCodes.RollAlsoChange, $"'{name}': an empty also-change on a roll.");
            case (RollEffect.TypeName, "alsoChange"):
                return AlsoChangeSpends(value)
                    ? new(path, BelowAction.Blocks, BelowCodes.RollAlsoChange, $"'{name}': a roll that also spends a resource; the older version would skip that cost.")
                    : new(path, BelowAction.Dropped, BelowCodes.RollAlsoChange, $"'{name}': a roll that also regains a resource.");
            case (RollEffect.TypeName, "drawsDown"):
                return value is null || (value is JsonValue { } flag && flag.TryGetValue<bool>(out var drawsDown) && !drawsDown)
                    ? new(path, BelowAction.Silent, BelowCodes.RollDetail, $"'{name}': that a roll does not draw a resource down.")
                    : new(path, BelowAction.Dropped, BelowCodes.RollDetail, $"'{name}': that a roll draws a resource down.");
            case (RollEffect.TypeName, _):
                return new(path, BelowAction.Dropped, BelowCodes.RollDetail, $"'{name}': the {key} of a roll.");
            case (ModifierEffect.TypeName, _):
                return new(path, BelowAction.Blocks, BelowCodes.ModifierToggleContent, $"'{name}': the toggle a modifier waits for; it would apply all the time.");
            case (GrantEffect.TypeName, _):
                return new(path, BelowAction.Blocks, BelowCodes.DefenceWhileToggle, $"'{name}': the toggle a damage defence waits for; it would apply all the time.");
            default:
                throw new System.Diagnostics.UnreachableException($"No lowering rule for {type}.{key}.");
        }
    }

    private static bool AlsoChangeSpends(JsonNode? value)
    {
        if (value is not JsonArray entries)
            return true;
        return entries.Any(e => e is not JsonObject o || !string.Equals(StringOf(Property(o, "change")), "regain", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The value of the property with this name in any case; null when absent or JSON null.</summary>
    private static JsonNode? Property(JsonObject o, string name) =>
        o.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? StringOf(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>
    /// The 11 to 10 step (R54): every entry's mods are dropped; then plain copies of one item that differed only by their mods are merged into
    /// the first, since an older build refuses a second plain entry. Groups where a copy has attunement or charges state are left alone (an older
    /// build accepts them), and so are groups that never had a mod.
    /// A group left alone because of attunement or charges state may still block at target 8: the v9 step strips the entry ids, and the validation
    /// step then reports the repeated item as <see cref="BelowCodes.ValidationError"/>.
    /// </summary>
    private static Character BelowV11(Character character, string name, List<BelowChange> changes)
    {
        var equipment = character.Equipment;
        for (var i = 0; i < equipment.Count; i++)
        {
            if (equipment[i]?.Mods is { Count: > 0 })
                changes.Add(new($"equipment[{i}].mods", BelowAction.Dropped, BelowCodes.ItemMods, $"'{name}': the mods on equipment entry {i + 1}."));
        }

        var merged = new List<EquipmentEntry>(equipment.Count);
        var slotOf = new Dictionary<ContentReference, int>();
        var groups = equipment.Select((e, i) => (e, i)).Where(p => p.e is not null).GroupBy(p => p.e.Item).ToDictionary(g => g.Key, g => g.Select(p => p.e).ToList());
        foreach (var entry in equipment)
        {
            if (entry is null)
            {
                merged.Add(entry!);
                continue;
            }
            var group = groups[entry.Item];
            var mergeable = group.Count > 1 && group.Any(e => e.Mods is { Count: > 0 }) && group.All(e => e.Attuned is null && e.ChargesSpent is null);
            var plain = entry with { Mods = null };
            if (!mergeable)
            {
                merged.Add(plain);
                continue;
            }
            if (slotOf.TryGetValue(entry.Item, out var first))
            {
                var head = merged[first];
                merged[first] = head with { Quantity = (int)Math.Min((long)head.Quantity + entry.Quantity, EquipmentEntry.MaxQuantity), Equipped = head.Equipped || entry.Equipped };
                continue;
            }
            slotOf[entry.Item] = merged.Count;
            merged.Add(plain);
        }

        foreach (var (item, group) in groups.Where(g => g.Value.Count > 1 && g.Value.Any(e => e.Mods is { Count: > 0 }) && g.Value.All(e => e.Attuned is null && e.ChargesSpent is null)))
            changes.Add(new($"equipment[{equipment.ToList().FindIndex(e => e?.Item == item)}]", BelowAction.Dropped, BelowCodes.ItemModCopies,
                $"'{name}': {group.Count} copies of one item merged into one; they differed only by their mods."));

        return character with { Equipment = merged };
    }

    private static Character BelowV10(Character character, string name, List<BelowChange> changes)
    {
        var play = character.Play;
        if (play.Encounter is not null)
            changes.Add(new("play.encounter", BelowAction.Blocks, BelowCodes.EncounterRunning, $"'{name}': an encounter is running. End it, then export."));

        var toggles = play.Toggles;
        for (var i = 0; i < toggles.Count; i++)
        {
            if (toggles[i]?.RoundsLeft is not null)
                changes.Add(new($"play.toggles[{i}].roundsLeft", BelowAction.Dropped, BelowCodes.RoundsLeft, $"'{name}': the rounds left on '{toggles[i].ToggleId}'."));
        }

        return character with
        {
            Play = play with
            {
                Encounter = null,
                Toggles = [.. toggles.Select(t => t?.RoundsLeft is null ? t! : t with { RoundsLeft = null })],
            },
        };
    }

    private static Character BelowV9(Character character, string name, List<BelowChange> changes)
    {
        if (character.Details is not null)
            changes.Add(new("details", BelowAction.Dropped, BelowCodes.Details, $"'{name}': backstory and details."));
        if (character.CustomItems is not null)
            changes.Add(new("customItems", BelowAction.Dropped, BelowCodes.CustomItems, $"'{name}': the custom items."));
        if (character.CustomAttacks is not null)
            changes.Add(new("customAttacks", BelowAction.Dropped, BelowCodes.CustomAttacks, $"'{name}': the custom attacks."));

        if (character.DamageDefences is { } defences)
        {
            var any = false;
            for (var i = 0; i < defences.Count; i++)
            {
                if (defences[i]?.Kind == DamageDefenceKind.Vulnerability)
                {
                    any = true;
                    changes.Add(new($"damageDefences[{i}]", BelowAction.Blocks, BelowCodes.Vulnerability,
                        $"'{name}': a vulnerability to {defences[i].Type} damage, which the older version cannot hold. Remove it, then export."));
                }
            }
            if (!any)
                changes.Add(new("damageDefences", BelowAction.Dropped, BelowCodes.DamageDefences, $"'{name}': the damage resistances and immunities."));
        }

        if (character.Companions is not null)
            changes.Add(new("companions", BelowAction.Dropped, BelowCodes.Companions, $"'{name}': the companions."));
        if (character.Play.Companions is not null)
            changes.Add(new("play.companions", BelowAction.Dropped, BelowCodes.Companions, $"'{name}': the companions' current hit points."));
        if (character.Xp is not null)
            changes.Add(new("xp", BelowAction.Dropped, BelowCodes.Xp, $"'{name}': the experience points."));

        var equipment = character.Equipment;
        for (var i = 0; i < equipment.Count; i++)
        {
            var entry = equipment[i];
            if (entry is null)
                continue;
            if (entry.Attuned is not null)
                changes.Add(new($"equipment[{i}].attuned", BelowAction.Dropped, BelowCodes.EquipmentState, $"'{name}': that an item is attuned."));
            if (entry.ChargesSpent is not null)
                changes.Add(new($"equipment[{i}].chargesSpent", BelowAction.Dropped, BelowCodes.EquipmentState, $"'{name}': the charges spent on an item."));
            if (entry is { Id: not null, Attuned: null, ChargesSpent: null })
                changes.Add(new($"equipment[{i}].id", BelowAction.Silent, BelowCodes.EquipmentState, $"'{name}': the id of an item entry."));
        }

        return character with
        {
            Details = null,
            CustomItems = null,
            CustomAttacks = null,
            DamageDefences = null,
            Companions = null,
            Xp = null,
            Play = character.Play with { Companions = null },
            Equipment = [.. equipment.Select(e => e is null ? e! : e with { Id = null, Attuned = null, ChargesSpent = null })],
        };
    }
}
