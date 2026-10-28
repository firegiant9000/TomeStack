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

/// <summary>
/// Lowers records to an older schema for a down-conversion export (pure: no I/O, no clock). Each step names what the older reader cannot hold;
/// anything that would change what the record means blocks instead of being dropped.
/// </summary>
public static class DownConvert
{
    /// <summary>
    /// Lowers <paramref name="character"/> to <paramref name="targetCharacterSchema"/> (8 to <see cref="Character.CurrentSchemaVersion"/>).
    /// Returns the same instance and no changes when nothing is above the target. When any change blocks, returns the input instance
    /// with the full change list; otherwise the lowered copy, whose <see cref="Character.SchemaVersion"/> is the target.
    /// </summary>
    public static CharacterBelow Below(Character character, int targetCharacterSchema)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetCharacterSchema, Character.PreviousSchemaVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetCharacterSchema, Character.CurrentSchemaVersion);

        if (character.SchemaVersion <= targetCharacterSchema)
            return new CharacterBelow(character, []);

        var changes = new List<BelowChange>();
        var lowered = character;
        var name = character.Name;

        if (targetCharacterSchema < Character.EncounterSchemaVersion && character.UsesV10Fields)
            lowered = BelowV10(lowered, name, changes);
        if (targetCharacterSchema < Character.DetailsSchemaVersion && character.UsesV9Fields)
            lowered = BelowV9(lowered, name, changes);

        return changes.Any(c => c.Action == BelowAction.Blocks)
            ? new CharacterBelow(character, changes)
            : new CharacterBelow(lowered, changes);
    }

    /// <summary>
    /// Lowers <paramref name="revision"/> to <paramref name="targetContentSchema"/> (9 to <see cref="ContentRevision.CurrentSchemaVersion"/>).
    /// Never touches the ids, provenance, status or name, and never rewrites <c>grant.content</c> or <c>choice.options</c> (the service does).
    /// Blocked fields are removed too: the copy is always the lowered one, and <see cref="ContentBelow.Blocked"/> tells the caller not to write it.
    /// Returns the same instance and no changes when nothing is above the target. <paramref name="catalog"/> is for the validation of the
    /// copy at the target (not done yet).
    /// </summary>
    public static ContentBelow Below(ContentRevision revision, int targetContentSchema, IContentCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetContentSchema, ContentRevision.PlayDetailsSchemaVersion - 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetContentSchema, ContentRevision.CurrentSchemaVersion);
        _ = catalog;

        var own = revision.SchemaVersion;
        var node = JsonSerializer.SerializeToNode(revision, RulesJson.Compact)!.AsObject();
        var changes = new List<BelowChange>();

        // One step per schema version above the target, newest first. A later task adds the 10 to 9 step.
        for (var step = ContentRevision.CurrentSchemaVersion; step > targetContentSchema; step--)
        {
            switch (step)
            {
                case ContentRevision.PlayMechanicsSchemaVersion:
                    ContentBelowV11(revision, node, changes);
                    break;
                case ContentRevision.PlayDetailsSchemaVersion:
                    ContentBelowV10(revision, node, changes);
                    break;
            }
        }

        if (own <= targetContentSchema && changes.Count == 0)
            return new ContentBelow(revision, changes);

        node["schemaVersion"] = Math.Min(own, targetContentSchema);
        return new ContentBelow(node.Deserialize<ContentRevision>(RulesJson.Options)!, changes);
    }

    /// <summary>The 10 to 9 step (content v10: speed and passive targets, damage defences, <c>item</c>). Filled in by a later task.</summary>
    private static void ContentBelowV10(ContentRevision revision, JsonObject node, List<BelowChange> changes)
    {
        _ = (revision, node, changes);
    }

    private const string Unsupported = "below.effect-unsupported";

    /// <summary>
    /// The 11 to 10 step: whole effects and the toggle, resource, roll, modifier and grant fields that v11 added. A revision that declares an
    /// older version only loses the inert v11 keys that a typed effect carries as extension data (silently); its v11-type effects stay
    /// as they are, since every reader already treats them as reference-only.
    /// </summary>
    private static void ContentBelowV11(ContentRevision revision, JsonObject node, List<BelowChange> changes)
    {
        if (node["effects"] is not JsonArray effects)
            return;
        var declaresV11 = revision.SchemaVersion >= ContentRevision.PlayMechanicsSchemaVersion;
        var name = revision.Name;
        var kept = new List<JsonNode?>(effects.Count);

        for (var i = 0; i < effects.Count; i++)
        {
            var element = effects[i];
            if (element is not JsonObject effect)
            {
                kept.Add(element?.DeepClone());
                continue;
            }

            var type = effect.FirstOrDefault(p => string.Equals(p.Key, "type", StringComparison.OrdinalIgnoreCase)).Value is JsonValue v && v.TryGetValue<string>(out var t) ? t : null;
            if (declaresV11 && WholeEffectBelowV11(type, effect, i, name, changes))
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
                foreach (var (key, value) in effect.ToList())
                {
                    var canonical = keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                    if (canonical is null)
                        continue;
                    effect.Remove(key);
                    changes.Add(declaresV11 ? KeyBelowV11(type!, canonical, value, i, name) : KeyBelowV11(type!, canonical, value, i, name) with { Action = BelowAction.Silent });
                }
            }
            kept.Add(effect);
        }

        effects.Clear();
        foreach (var item in kept)
            effects.Add(item);
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
                    : new(path, BelowAction.Blocks, "below.roll-mode-disadvantage", $"'{name}': its disadvantage on rolls; the older version would leave it out."));
                return true;
            case UpkeepEffect.TypeName:
                changes.Add(string.Equals(StringOf(Property(effect, "change")), "regain", StringComparison.OrdinalIgnoreCase)
                    ? new(path, BelowAction.Dropped, "below.upkeep", $"'{name}': its upkeep that regains a resource.")
                    : new(path, BelowAction.Blocks, "below.upkeep", $"'{name}': its upkeep that spends a resource; the older version would never charge it."));
                return true;
            case ToggleGroupEffect.TypeName:
                changes.Add(new(path, BelowAction.Blocks, "below.toggle-group", $"'{name}': the form group; two forms could be on at once."));
                return true;
            case RollEffect.TypeName when Property(effect, "dice") is null:
                changes.Add(new(path, BelowAction.Dropped, "below.diceless-roll", $"'{name}': a roll with no dice; the feature stays as text."));
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
                return new(path, BelowAction.Blocks, "below.toggle-cost", $"'{name}': what a toggle costs; it would turn on for free.");
            case (ToggleEffect.TypeName, "level"):
                return new(path, BelowAction.Blocks, "below.toggle-level", $"'{name}': the level a toggle is used at; it would work at one fixed level.");
            case (ToggleEffect.TypeName, "duration"):
                return new(path, BelowAction.Dropped, "below.toggle-duration", $"'{name}': how long a toggle lasts; it stays on until turned off.");
            case (ToggleEffect.TypeName, _):
                return new(path, BelowAction.Blocks, "below.toggle-group", $"'{name}': the form group ({key}); forms could stack that should not.");
            case (ResourceEffect.TypeName, "counts"):
                return string.Equals(StringOf(value), "up", StringComparison.OrdinalIgnoreCase)
                    ? new(path, BelowAction.Blocks, "below.resource-counts-up", $"'{name}': a resource that counts up; it would count down instead.")
                    : new(path, BelowAction.Dropped, "below.resource-detail", $"'{name}': how a resource counts.");
            case (ResourceEffect.TypeName, _):
                return new(path, BelowAction.Dropped, "below.resource-detail", $"'{name}': the {key} of a resource.");
            case (RollEffect.TypeName, "alsoChange") when value is null:
                return new(path, BelowAction.Silent, "below.roll-also-change", $"'{name}': an empty also-change on a roll.");
            case (RollEffect.TypeName, "alsoChange"):
                return AlsoChangeSpends(value)
                    ? new(path, BelowAction.Blocks, "below.roll-also-change", $"'{name}': a roll that also spends a resource; the older version would skip that cost.")
                    : new(path, BelowAction.Dropped, "below.roll-also-change", $"'{name}': a roll that also regains a resource.");
            case (RollEffect.TypeName, "drawsDown"):
                return value is null || (value is JsonValue { } flag && flag.TryGetValue<bool>(out var drawsDown) && !drawsDown)
                    ? new(path, BelowAction.Silent, "below.roll-detail", $"'{name}': that a roll does not draw a resource down.")
                    : new(path, BelowAction.Dropped, "below.roll-detail", $"'{name}': that a roll draws a resource down.");
            case (RollEffect.TypeName, _):
                return new(path, BelowAction.Dropped, "below.roll-detail", $"'{name}': the {key} of a roll.");
            case (ModifierEffect.TypeName, _):
                return new(path, BelowAction.Blocks, "below.modifier-toggle-content", $"'{name}': the toggle a modifier waits for; it would apply all the time.");
            case (GrantEffect.TypeName, _):
                return new(path, BelowAction.Blocks, "below.defence-while-toggle", $"'{name}': the toggle a damage defence waits for; it would apply all the time.");
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

    private static Character BelowV10(Character character, string name, List<BelowChange> changes)
    {
        var play = character.Play;
        if (play.Encounter is not null)
            changes.Add(new("play.encounter", BelowAction.Blocks, "below.encounter-running", $"'{name}': an encounter is running. End it, then export."));

        var toggles = play.Toggles;
        for (var i = 0; i < toggles.Count; i++)
        {
            if (toggles[i]?.RoundsLeft is not null)
                changes.Add(new($"play.toggles[{i}].roundsLeft", BelowAction.Dropped, "below.rounds-left", $"'{name}': the rounds left on '{toggles[i].ToggleId}'."));
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
            changes.Add(new("details", BelowAction.Dropped, "below.details", $"'{name}': backstory and details."));
        if (character.CustomItems is not null)
            changes.Add(new("customItems", BelowAction.Dropped, "below.custom-items", $"'{name}': the custom items."));
        if (character.CustomAttacks is not null)
            changes.Add(new("customAttacks", BelowAction.Dropped, "below.custom-attacks", $"'{name}': the custom attacks."));

        if (character.DamageDefences is { } defences)
        {
            var any = false;
            for (var i = 0; i < defences.Count; i++)
            {
                if (defences[i]?.Kind == DamageDefenceKind.Vulnerability)
                {
                    any = true;
                    changes.Add(new($"damageDefences[{i}]", BelowAction.Blocks, "below.vulnerability",
                        $"'{name}': a vulnerability to {defences[i].Type} damage, which the older version cannot hold. Remove it, then export."));
                }
            }
            if (!any)
                changes.Add(new("damageDefences", BelowAction.Dropped, "below.damage-defences", $"'{name}': the damage resistances and immunities."));
        }

        if (character.Companions is not null)
            changes.Add(new("companions", BelowAction.Dropped, "below.companions", $"'{name}': the companions."));
        if (character.Play.Companions is not null)
            changes.Add(new("play.companions", BelowAction.Dropped, "below.companions", $"'{name}': the companions' current hit points."));
        if (character.Xp is not null)
            changes.Add(new("xp", BelowAction.Dropped, "below.xp", $"'{name}': the experience points."));

        var equipment = character.Equipment;
        for (var i = 0; i < equipment.Count; i++)
        {
            var entry = equipment[i];
            if (entry is null)
                continue;
            if (entry.Attuned is not null)
                changes.Add(new($"equipment[{i}].attuned", BelowAction.Dropped, "below.equipment-state", $"'{name}': that an item is attuned."));
            if (entry.ChargesSpent is not null)
                changes.Add(new($"equipment[{i}].chargesSpent", BelowAction.Dropped, "below.equipment-state", $"'{name}': the charges spent on an item."));
            if (entry is { Id: not null, Attuned: null, ChargesSpent: null })
                changes.Add(new($"equipment[{i}].id", BelowAction.Silent, "below.equipment-state", $"'{name}': the id of an item entry."));
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
