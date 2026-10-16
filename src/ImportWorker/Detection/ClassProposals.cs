using System.Globalization;
using System.Text.RegularExpressions;
using TomeStack.RulesCore;

namespace TomeStack.ImportWorker.Detection;

/// <summary>
/// Item 11, D34, ADR-016: the effects a class page supports, proposed on the class candidate. Each has a stable id so the
/// reviewer confirms it by name at accept (<c>confirmedEffectIds</c>); unconfirmed effects stay reference. The class's own
/// <c>spellcasting</c> effect is proposed here, not on a feature, because calculation reads it from any active revision,
/// including the class itself (ruling R52). Multiclass caster shares are never proposed (R57).
/// </summary>
public static partial class ClassProposals
{
    public const string HitDieId = "hit-die";
    public const string SpellcastingId = "spellcasting";
    public const string SubclassId = "subclass";

    public sealed record Proposal(IReadOnlyList<Effect> Effects, IReadOnlyList<string> Uncertainties, IReadOnlyList<string> LowConfidenceFields, IReadOnlyDictionary<string, string> Fields);

    /// <param name="className">The class's display name ("Fixture Lamplighter").</param>
    /// <param name="table">The reconstructed level table, or null when it could not be read whole.</param>
    /// <param name="pageText">The page's lines of text, for the hit die, saves and proficiencies.</param>
    /// <param name="featuresByLevel">The Features column by level (from the table, or the legacy reader).</param>
    public static Proposal Propose(string className, ClassTable? table, IReadOnlyList<string> pageText, IReadOnlyDictionary<int, IReadOnlyList<string>> featuresByLevel)
    {
        var effects = new List<Effect>();
        var uncertainties = new List<string>();
        var lowConfidence = new List<string>();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var key = SpellListKeyOf(className);
        var idStem = key.Length > 0 ? key : "class";

        foreach (var line in pageText)
        {
            if (effects.Any(e => e is HitDieEffect))
                break;
            if (HitDie().Match(line) is { Success: true } hit)
                effects.Add(new HitDieEffect { Id = HitDieId, Die = int.Parse(hit.Groups["die"].Value, CultureInfo.InvariantCulture), Text = "Hit die read from the class page." });
        }
        if (!effects.Any(e => e is HitDieEffect))
            uncertainties.Add("The hit die was not found on the class page: add it by hand.");

        foreach (var line in pageText)
        {
            if (SavesLine().Match(line) is not { Success: true } saves)
                continue;
            foreach (var ability in AbilityNames().Matches(saves.Groups["list"].Value).Select(m => AbilityOf(m.Value)).Distinct())
            {
                effects.Add(new GrantEffect
                {
                    Id = $"save-{FieldIds.Key(ability)}", Grant = GrantKind.Proficiency, Target = FieldIds.Save(ability), OnlyAs = ClassEntry.StartingClass,
                });
            }
            break;
        }

        foreach (var line in pageText)
        {
            if (ArmorLine().Match(line) is not { Success: true } armor)
                continue;
            var list = armor.Groups["list"].Value.ToLowerInvariant();
            foreach (var kind in new[] { "light", "medium", "heavy" })
            {
                if (list.Contains(kind, StringComparison.Ordinal))
                    effects.Add(new GrantEffect { Id = $"armor-{kind}", Grant = GrantKind.Proficiency, Target = $"armor.{kind}" });
            }
            if (list.Contains("shield", StringComparison.Ordinal))
                effects.Add(new GrantEffect { Id = "armor-shield", Grant = GrantKind.Proficiency, Target = "armor.shield" });
            break;
        }

        foreach (var line in pageText)
        {
            if (WeaponsLine().Match(line) is not { Success: true } weapons)
                continue;
            var list = weapons.Groups["list"].Value.ToLowerInvariant();
            foreach (var kind in new[] { "simple", "martial" })
            {
                if (list.Contains(kind, StringComparison.Ordinal))
                    effects.Add(new GrantEffect { Id = $"weapon-{kind}", Grant = GrantKind.Proficiency, Target = $"weapon.{kind}" });
            }
            break;
        }

        // Skills are a choice from a named list; a choice with no options cannot be resolved, so the line is kept for the reviewer.
        foreach (var line in pageText)
        {
            if (SkillsLine().Match(line) is { Success: true } skills)
            {
                fields["skills"] = skills.Groups["list"].Value.Trim();
                uncertainties.Add("The skill choice is shown but not proposed: add it by hand.");
                break;
            }
        }

        if (SubclassLevel(featuresByLevel) is { } subclassLevel)
        {
            effects.Add(new ChoiceEffect { Id = $"{idStem}-{SubclassId}", ChoiceId = $"{idStem}-{SubclassId}", Count = 1, Level = subclassLevel });
            uncertainties.Add("The subclass choice is proposed with no options: subclasses extend it.");
        }

        if (table is not null && table.HasSlotColumns)
            ProposeSpellcasting(key, table, pageText, effects, uncertainties, lowConfidence);

        return new(effects, uncertainties, lowConfidence, fields);
    }

    private static void ProposeSpellcasting(string key, ClassTable table, IReadOnlyList<string> pageText, List<Effect> effects, List<string> uncertainties, List<string> lowConfidence)
    {
        var ability = pageText.Select(l => CastingAbility().Match(l)).FirstOrDefault(m => m.Success);
        var slots = table.Slots();
        if (ability is null)
        {
            uncertainties.Add("A spell slot table was read, but the spellcasting ability was not found: add spellcasting by hand.");
            return;
        }
        if (slots is null)
        {
            uncertainties.Add("A spell slot table was found but not read whole: add spellcasting by hand.");
            return;
        }
        if (key.Length == 0)
        {
            uncertainties.Add("The class name cannot be a spell list key: add spellcasting by hand.");
            return;
        }
        var label = table.SpellsLabel ?? "";
        var known = label.Contains("known", StringComparison.OrdinalIgnoreCase);
        var spellsTable = table.Numbers(ClassColumnRole.SpellsTable);
        effects.Add(new SpellcastingEffect
        {
            Id = ClassProposals.SpellcastingId,
            Ability = AbilityOf(ability.Groups["ability"].Value),
            Preparation = known ? SpellPreparation.Known : SpellPreparation.Prepared,
            SpellList = key,
            SlotKind = table.IsPact ? SpellSlotKind.PactMagic : SpellSlotKind.SpellSlots,
            Slots = slots,
            Cantrips = table.Numbers(ClassColumnRole.Cantrips),
            SpellsTable = spellsTable,
            Text = "Slots, cantrips and spells read from the class table.",
        });
        lowConfidence.Add("spellList");
        uncertainties.Add($"The spell list is assumed to be this class's own ({key}): check it before accepting.");
        if (table.SpellsLabel is not null && spellsTable is null)
            uncertainties.Add("The spells column was found but not read whole.");
        if (table.SpellsLabel is null && !known)
            uncertainties.Add("No spells column: how many spells are prepared is not proposed.");
    }

    private static string SpellListKeyOf(string className) => SpellListKey.TryNormalise(className, out var key) ? key : "";

    /// <summary>The first level a "Subclass" feature is gained, or the first "X feature" stem when the table names no subclass.</summary>
    private static int? SubclassLevel(IReadOnlyDictionary<int, IReadOnlyList<string>> featuresByLevel)
    {
        foreach (var (level, names) in featuresByLevel.OrderBy(f => f.Key))
        {
            if (names.Any(n => n.Contains("subclass", StringComparison.OrdinalIgnoreCase)))
                return level;
        }
        foreach (var (level, names) in featuresByLevel.OrderBy(f => f.Key))
        {
            if (names.Any(n => n.EndsWith(" feature", StringComparison.OrdinalIgnoreCase)))
                return level;
        }
        return null;
    }

    private static Ability AbilityOf(string name) => name.ToLowerInvariant() switch
    {
        "strength" => Ability.Str,
        "dexterity" => Ability.Dex,
        "constitution" => Ability.Con,
        "intelligence" => Ability.Int,
        "wisdom" => Ability.Wis,
        _ => Ability.Cha,
    };

    [GeneratedRegex(@"\b1?[dD](?<die>6|8|10|12) per .{1,40}? level\b", RegexOptions.None, 100)]
    private static partial Regex HitDie();

    [GeneratedRegex(@"^Saving Throws?:\s*(?<list>.{3,80})$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex SavesLine();

    [GeneratedRegex(@"^Armor(?: Training)?:\s*(?<list>.{3,120})$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex ArmorLine();

    [GeneratedRegex(@"^Weapons?(?: Proficiencies)?:\s*(?<list>.{3,160})$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex WeaponsLine();

    [GeneratedRegex(@"^Skills?:\s*(?<list>.{3,200})$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex SkillsLine();

    [GeneratedRegex(@"\b(?:Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\b", RegexOptions.None, 100)]
    private static partial Regex AbilityNames();

    [GeneratedRegex(@"\b(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma) is your spellcasting ability|[Ss]pellcasting [Aa]bility:?\s*(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\b", RegexOptions.None, 100)]
    private static partial Regex CastingAbility();
}
