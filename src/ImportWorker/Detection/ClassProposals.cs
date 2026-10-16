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
    public static Proposal Propose(string className, ClassTable? table, IReadOnlyList<string> pageText, IReadOnlyDictionary<int, IReadOnlyList<string>> featuresByLevel, IReadOnlyList<string>? followingText = null)
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

        var savesFound = false;
        foreach (var line in pageText)
        {
            if (SavesLine().Match(line) is not { Success: true } saves)
                continue;
            var abilities = AbilityNames().Matches(saves.Groups["list"].Value).Select(m => AbilityOf(m.Value)).Distinct().ToList();
            if (abilities.Count == 0)
                continue; // prose that starts like the label
            foreach (var ability in abilities)
            {
                effects.Add(new GrantEffect
                {
                    Id = $"save-{FieldIds.Key(ability)}", Grant = GrantKind.Proficiency, Target = FieldIds.Save(ability), OnlyAs = ClassEntry.StartingClass,
                });
            }
            savesFound = true;
            break;
        }
        if (!savesFound)
            uncertainties.Add("The saving throw proficiencies were not found on the class page: add them by hand.");

        var armorFound = false;
        foreach (var line in pageText)
        {
            if (ArmorLine().Match(line) is not { Success: true } armor)
                continue;
            var list = armor.Groups["list"].Value.ToLowerInvariant();
            var before = effects.Count;
            foreach (var kind in new[] { "light", "medium", "heavy" })
            {
                if (list.Contains(kind, StringComparison.Ordinal))
                    effects.Add(new GrantEffect { Id = $"armor-{kind}", Grant = GrantKind.Proficiency, Target = $"armor.{kind}" });
            }
            if (list.Contains("all armor", StringComparison.Ordinal))
            {
                foreach (var kind in new[] { "light", "medium", "heavy" }.Where(k => !effects.Any(e => e.Id == $"armor-{k}")))
                    effects.Add(new GrantEffect { Id = $"armor-{kind}", Grant = GrantKind.Proficiency, Target = $"armor.{kind}" });
            }
            if (list.Contains("shield", StringComparison.Ordinal))
                effects.Add(new GrantEffect { Id = "armor-shield", Grant = GrantKind.Proficiency, Target = "armor.shield" });
            if (effects.Count == before && !list.Contains("none", StringComparison.Ordinal))
                continue; // prose that starts like the label
            armorFound = true; // "Armor: None" is a reading too, with nothing to propose
            break;
        }
        if (!armorFound)
            uncertainties.Add("The armor proficiencies were not found on the class page: add them by hand if the class has any.");

        var weaponsFound = false;
        foreach (var line in pageText)
        {
            if (WeaponsLine().Match(line) is not { Success: true } weapons)
                continue;
            var list = weapons.Groups["list"].Value.ToLowerInvariant();
            var kinds = new[] { "simple", "martial" }.Where(k => list.Contains(k, StringComparison.Ordinal)).ToList();
            if (kinds.Count == 0)
                continue;
            weaponsFound = true;
            if (QualifiedWeapons().IsMatch(list))
            {
                uncertainties.Add("A weapon proficiency with a condition was found (for example a required property): it is not proposed; add it by hand.");
                break;
            }
            foreach (var kind in kinds)
                effects.Add(new GrantEffect { Id = $"weapon-{kind}", Grant = GrantKind.Proficiency, Target = $"weapon.{kind}" });
            break;
        }
        if (!weaponsFound)
            uncertainties.Add("The weapon proficiencies were not found on the class page: add them by hand if the class has any.");

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
            ProposeSpellcasting(key, table, followingText is { Count: > 0 } ? [.. pageText, .. followingText] : pageText, effects, uncertainties, lowConfidence);

        return new(effects, uncertainties, lowConfidence, fields);
    }

    private static void ProposeSpellcasting(string key, ClassTable table, IReadOnlyList<string> pageText, List<Effect> effects, List<string> uncertainties, List<string> lowConfidence)
    {
        // Lines wrap, so the sentence can span two, and a word can break across them ("spellcast- ing").
        var ability = CastingAbility().Match(LineBreakHyphen().Replace(string.Join(' ', pageText), "$1$2")) is { Success: true } found ? found : null;
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

    private static readonly string[] TraitLabels = ["Primary Ability", "Hit Point Die", "Saving Throw", "Saving Throw Proficiencies", "Skill Proficiencies", "Weapon Proficiencies", "Tool Proficiencies", "Armor Training", "Starting Equipment"];

    /// <summary>
    /// The page's lines of text, preceded, for each label of a "Core Traits" table (as in the 2024 SRD: a label cell with its
    /// value in the cell to its right, or fused with it), by one joined line "label value", so the label forms read like the
    /// 2014 "Label: value" lines and a wrapped value is read whole.
    /// </summary>
    public static IReadOnlyList<string> LabelledLines(IReadOnlyList<TextLine> lines, IReadOnlyList<TextLine>? previousPage = null, string className = "")
    {
        // The 2024 SRD prints "Core <Class> Traits" at the foot of the page before the class's feature table for some classes.
        IReadOnlyList<TextLine> core = previousPage is { Count: > 0 } && className.Length > 0 && previousPage.Any(l => l.Text.Trim().Equals($"Core {className} Traits", StringComparison.OrdinalIgnoreCase))
            ? previousPage : [];
        return [.. JoinedLabels(lines), .. JoinedLabels(core), .. lines.Select(l => l.Text), .. core.Select(l => l.Text)];
    }

    private static List<string> JoinedLabels(IReadOnlyList<TextLine> lines)
    {
        var joined = new List<string>();
        var labels = lines.Select(l => (Line: l, Name: TraitLabelOf(l.Text))).Where(x => x.Name is not null).OrderByDescending(x => x.Line.Y).ToList();
        foreach (var (label, name) in labels)
        {
            var tolerance = Math.Max(2.5, label.Height * 0.6);
            // The next label in the same column ends this value.
            var next = labels.Where(x => x.Line.Y < label.Y - tolerance && Math.Abs(x.Line.X - label.X) <= 4).Select(x => x.Line.Y).DefaultIfEmpty(label.Y - label.Height * 6).Max();
            string head;
            Func<TextLine, bool> inColumn;
            if (label.Text.Trim().Length == name!.Length)
            {
                // The label alone in its cell: the value is the cell to its right, and wraps in that cell's column.
                var right = lines.Where(l => l != label && l.X > label.X + Math.Max(10, label.Width * 0.5) && Math.Abs(l.Y - label.Y) <= tolerance).OrderBy(l => l.X).FirstOrDefault();
                if (right is null)
                    continue;
                head = right.Text.Trim();
                inColumn = l => l != right && Math.Abs(l.X - right.X) <= 4;
            }
            else
            {
                // The extractor fused label and value into one line: the value wraps under it, right of the label.
                head = label.Text.Trim()[name.Length..].Trim();
                inColumn = l => l.X > label.X + 10 && l.X < label.X + label.Width;
            }
            var rest = lines.Where(l => l != label && inColumn(l) && l.Y < label.Y - tolerance && l.Y > next + tolerance).OrderByDescending(l => l.Y).Select(l => l.Text.Trim());
            joined.Add($"{name} {string.Join(' ', rest.Prepend(head))}");
        }
        return joined; // read before the raw lines, so a fragment of a value ("Simple weapons and") never wins over the whole
    }

    /// <summary>The trait label a line is or starts with (the longest one: "Saving Throw Proficiencies" before "Saving Throw").</summary>
    private static string? TraitLabelOf(string text)
    {
        var trimmed = text.Trim();
        return TraitLabels.Where(l => trimmed.Equals(l, StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith(l + " ", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.Length).FirstOrDefault();
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

    [GeneratedRegex(@"^Saving Throws?(?: Proficiencies)?(?::\s*|\s+)(?<list>.{3,80})$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex SavesLine();

    [GeneratedRegex(@"^Armor(?::\s*|(?: Training| Proficiencies):?\s*)(?<list>.{3,120})$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex ArmorLine();

    [GeneratedRegex(@"^Weapons?(?::\s*|(?: Proficiencies):?\s*)(?<list>.{3,160})$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex WeaponsLine();

    [GeneratedRegex(@"\b(?:that|which|with|property|properties|finesse)\b", RegexOptions.None, 100)]
    private static partial Regex QualifiedWeapons();

    [GeneratedRegex(@"^Skills?(?::\s*|(?: Proficiencies):?\s*)(?<list>.{3,200})$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex SkillsLine();

    [GeneratedRegex(@"\b(?:Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\b", RegexOptions.None, 100)]
    private static partial Regex AbilityNames();

    [GeneratedRegex(@"(\p{L})- (\p{Ll})", RegexOptions.None, 100)]
    private static partial Regex LineBreakHyphen();

    [GeneratedRegex(@"\b(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma) is your spellcasting ability|[Ss]pellcasting [Aa]bility:?\s*(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\b", RegexOptions.None, 100)]
    private static partial Regex CastingAbility();
}
