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

/// <summary>A content revision lowered to an older schema, with what it lost. When <see cref="Blocked"/>, <see cref="Revision"/> is the input, unchanged.</summary>
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
