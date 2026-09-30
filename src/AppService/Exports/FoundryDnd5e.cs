using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Exports;

/// <summary>
/// ADR-012 (accepted 2026-09-29): the Foundry VTT <c>dnd5e</c> adapter. It maps the sheet export model v1 (never internal
/// records) to one Actor document that the user imports by hand with "Import Data". First-party code; no third-party code
/// runs, and nothing is sent anywhere. Pinned to a core and system release pair, verified against that release's data
/// models (module/data/actor/character.mjs, templates/common.mjs, creature.mjs, attributes.mjs, and the class, subclass,
/// feat, spell and weapon item models, plus shared/damage-field.mjs and uses-field.mjs at tag release-6.0.5). Documented in
/// docs/features/export-adapters.md, with what is lost.
/// </summary>
public static partial class FoundryDnd5e
{
    public const string Target = "foundry-dnd5e";
    public const string AdapterVersion = "1.0.0";

    /// <summary>The pinned pair: dnd5e 6.0.5 declares compatibility minimum 14.367, verified 14.</summary>
    public const string SystemVersion = "6.0.5";
    public const string CoreVersion = "14.367";

    public const string FileSuffix = ".foundry-dnd5e.json";

    /// <summary>TomeStack skill keys to the dnd5e abbreviations (CONFIG.DND5E.skills).</summary>
    private static readonly IReadOnlyDictionary<string, string> Skills = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["acrobatics"] = "acr", ["animalHandling"] = "ani", ["arcana"] = "arc", ["athletics"] = "ath", ["deception"] = "dec",
        ["history"] = "his", ["insight"] = "ins", ["intimidation"] = "itm", ["investigation"] = "inv", ["medicine"] = "med",
        ["nature"] = "nat", ["perception"] = "prc", ["performance"] = "prf", ["persuasion"] = "per", ["religion"] = "rel",
        ["sleightOfHand"] = "slt", ["stealth"] = "ste", ["survival"] = "sur",
    };

    /// <summary>Spell school names to the dnd5e keys (CONFIG.DND5E.spellSchools).</summary>
    private static readonly IReadOnlyDictionary<string, string> Schools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["abjuration"] = "abj", ["conjuration"] = "con", ["divination"] = "div", ["enchantment"] = "enc",
        ["evocation"] = "evo", ["illusion"] = "ill", ["necromancy"] = "nec", ["transmutation"] = "trs",
    };

    [GeneratedRegex("^(?<n>[0-9]{1,2})d(?<d>4|6|8|10|12|20)\\b", RegexOptions.CultureInvariant)]
    private static partial Regex Dice();

    /// <summary>The Actor document for <paramref name="sheet"/>. Keys are sorted, so the same sheet gives the same bytes.</summary>
    public static JsonObject Map(SheetExport sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var abilities = new JsonObject();
        foreach (var a in sheet.Abilities)
            abilities[a.Ability] = new JsonObject { ["value"] = a.Score, ["proficient"] = a.SaveProficiency == SheetProficiency.None ? 0 : 1 };
        var skills = new JsonObject();
        foreach (var s in sheet.Skills)
        {
            if (Skills.TryGetValue(s.Skill, out var key))
                skills[key] = new JsonObject { ["ability"] = s.Ability, ["value"] = Level(s.Proficiency) };
        }
        var field = sheet.Fields.ToDictionary(f => f.Id);
        var attributes = new JsonObject
        {
            // TomeStack's Armor Class as a fixed value: Foundry's own armor formula is not used (6.x: "flat" is an override).
            ["ac"] = new JsonObject { ["calcs"] = new JsonArray(), ["flat"] = Value(field, FieldIds.ArmorClass), ["override"] = Value(field, FieldIds.ArmorClass) },
            // hp.max on a character is the override: TomeStack's calculated maximum, override included.
            ["hp"] = new JsonObject { ["max"] = sheet.HitPoints.Maximum, ["temp"] = sheet.HitPoints.Temporary, ["value"] = sheet.HitPoints.Current },
            ["exhaustion"] = sheet.Conditions.Exhaustion,
        };
        if (sheet.Spellcasting.FirstOrDefault() is { } primary)
            attributes["spellcasting"] = primary.Ability;
        var spells = new JsonObject();
        foreach (var slot in sheet.Slots)
            spells[$"spell{slot.Level}"] = new JsonObject { ["override"] = slot.Maximum, ["value"] = Math.Max(0, slot.Maximum - slot.Spent) };
        if (sheet.PactSlots is { } pact)
            spells["pact"] = new JsonObject { ["override"] = pact.Maximum, ["value"] = Math.Max(0, pact.Maximum - pact.Spent) };

        var items = new JsonArray();
        var spentByDie = sheet.HitDice.ToDictionary(h => h.Die, h => h.Spent);
        foreach (var klass in sheet.Character.Classes)
        {
            var identifier = Identifier(klass.Name);
            var die = klass.HitDie ?? 8;
            var spent = Math.Min(klass.Level, spentByDie.GetValueOrDefault(die));
            spentByDie[die] = spentByDie.GetValueOrDefault(die) - spent;
            items.Add(Item(klass.Name, "class", new JsonObject
            {
                ["hd"] = new JsonObject { ["denomination"] = $"d{die}", ["spent"] = spent },
                ["identifier"] = identifier,
                ["levels"] = klass.Level,
            }, klass.Ref));
            if (klass.Subclass is { } subclass)
                items.Add(Item(subclass, "subclass", new JsonObject { ["classIdentifier"] = identifier, ["identifier"] = Identifier(subclass) }, klass.SubclassRef));
        }
        var resources = sheet.Resources.GroupBy(r => r.Ref.ContentId).ToDictionary(g => g.Key, g => g.First());
        foreach (var feature in sheet.Features.Where(f => f.Kind is "feature" or "feat" or "species" or "background"))
        {
            var system = new JsonObject { ["description"] = Description(feature.Summary, feature.Texts), ["identifier"] = Identifier(feature.Name) };
            if (resources.TryGetValue(feature.Ref.ContentId, out var resource) && resource.Maximum is { } max)
            {
                system["uses"] = new JsonObject
                {
                    ["max"] = max.ToString(CultureInfo.InvariantCulture),
                    ["recovery"] = new JsonArray([.. resource.Recovery.Select(Recovery).OfType<string>().Distinct().Select(p => (JsonNode)new JsonObject { ["period"] = p, ["type"] = "recoverAll" })]),
                    ["spent"] = Math.Min(resource.Spent, max),
                };
            }
            items.Add(Item(feature.Name, "feat", system, feature.Ref));
        }
        foreach (var caster in sheet.Spellcasting)
        {
            foreach (var spell in caster.Spells)
            {
                var system = new JsonObject
                {
                    ["description"] = Description(spell.Summary, spell.Text is null ? [] : [spell.Text]),
                    ["identifier"] = Identifier(spell.Name),
                    ["level"] = spell.Level,
                    ["prepared"] = spell.Prepared || spell.Level == 0 ? 1 : 0,
                };
                if (spell.School is { } school && Schools.TryGetValue(school, out var schoolKey))
                    system["school"] = schoolKey;
                items.Add(Item(spell.Name, "spell", system, spell.Ref));
            }
        }
        foreach (var attack in sheet.Attacks)
        {
            var system = new JsonObject
            {
                ["description"] = Description($"{(attack.ToHit >= 0 ? "+" : "")}{attack.ToHit} to hit, {attack.Damage} {attack.DamageType} damage (TomeStack).", []),
                ["equipped"] = true,
                ["identifier"] = Identifier(attack.Name),
                ["proficient"] = attack.Proficient ? 1 : 0,
            };
            if (Dice().Match(attack.Damage) is { Success: true } dice)
            {
                system["damage"] = new JsonObject
                {
                    ["base"] = new JsonObject
                    {
                        ["denomination"] = int.Parse(dice.Groups["d"].Value, CultureInfo.InvariantCulture),
                        ["number"] = int.Parse(dice.Groups["n"].Value, CultureInfo.InvariantCulture),
                        ["types"] = new JsonArray(attack.DamageType.ToLowerInvariant()),
                    },
                };
            }
            items.Add(Item(attack.Name, "weapon", system, attack.Ref));
        }

        var actor = new JsonObject
        {
            ["_stats"] = new JsonObject { ["coreVersion"] = CoreVersion, ["systemId"] = "dnd5e", ["systemVersion"] = SystemVersion },
            ["flags"] = new JsonObject
            {
                ["tomestack"] = new JsonObject
                {
                    ["adapterVersion"] = AdapterVersion,
                    ["exportedAt"] = sheet.ExportedAt.ToString("O", CultureInfo.InvariantCulture),
                    ["purpose"] = sheet.Purpose == SheetPurpose.Personal ? "personal" : "share",
                    ["sources"] = new JsonArray([.. sheet.Notices.Select(n => (JsonNode)n.Title)]),
                },
            },
            ["items"] = items,
            ["name"] = sheet.Character.Name,
            ["system"] = new JsonObject
            {
                ["abilities"] = abilities,
                ["attributes"] = attributes,
                ["details"] = new JsonObject { ["biography"] = new JsonObject { ["value"] = Biography(sheet) } },
                ["skills"] = skills,
                ["spells"] = spells,
            },
            ["type"] = "character",
        };
        return (JsonObject)Sorted(actor)!;
    }

    /// <summary>ADR-012 "Validation" 1: the subset this adapter emits, checked before anything is written. Empty when valid.</summary>
    public static IReadOnlyList<string> Validate(JsonObject actor)
    {
        try
        {
            return Check(actor);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return ["a value has the wrong type"];
        }
    }

    private static List<string> Check(JsonObject actor)
    {
        var problems = new List<string>();
        void Need(bool ok, string what)
        {
            if (!ok) problems.Add(what);
        }
        Need(actor["type"]?.GetValue<string>() == "character", "type is character");
        Need(actor["name"] is JsonValue name && name.GetValue<string>().Length is > 0 and <= 200, "name is 1 to 200 characters");
        Need(actor["_stats"]?["systemId"]?.GetValue<string>() == "dnd5e" && actor["_stats"]?["systemVersion"]?.GetValue<string>() == SystemVersion, "_stats pins the system");
        var system = actor["system"] as JsonObject;
        Need(system is not null, "system is an object");
        var abilities = system?["abilities"] as JsonObject;
        foreach (var key in new[] { "str", "dex", "con", "int", "wis", "cha" })
        {
            var ability = abilities?[key];
            Need(ability?["value"] is JsonValue v && v.GetValue<int>() >= 0, $"abilities.{key}.value is an integer ≥ 0");
            Need(ability?["proficient"] is JsonValue p && p.GetValue<int>() is 0 or 1, $"abilities.{key}.proficient is 0 or 1");
        }
        foreach (var (key, skill) in system?["skills"] as JsonObject ?? [])
            Need(Skills.Values.Contains(key) && skill?["value"] is JsonValue level && level.GetValue<double>() is 0 or 0.5 or 1 or 2, $"skills.{key}.value is 0, 0.5, 1 or 2");
        var hp = system?["attributes"]?["hp"];
        Need(hp?["max"] is JsonValue && hp["value"] is JsonValue && hp["value"]!.GetValue<int>() >= 0 && hp["max"]!.GetValue<int>() >= 0, "attributes.hp has value and max ≥ 0");
        Need(system?["attributes"]?["ac"]?["flat"] is JsonValue ac && ac.GetValue<int>() >= 0, "attributes.ac.flat is an integer ≥ 0");
        foreach (var (key, slot) in system?["spells"] as JsonObject ?? [])
            Need((key == "pact" || (key.StartsWith("spell", StringComparison.Ordinal) && int.TryParse(key[5..], out var l) && l is >= 1 and <= 9)) && slot?["value"] is JsonValue, $"spells.{key} is a spell level with a value");
        foreach (var item in actor["items"] as JsonArray ?? [])
        {
            var type = item?["type"]?.GetValue<string>();
            Need(type is "class" or "subclass" or "feat" or "spell" or "weapon", "every item is a class, subclass, feat, spell or weapon");
            Need(item?["name"] is JsonValue itemName && itemName.GetValue<string>().Length > 0, "every item has a name");
            if (type == "class")
                Need(item!["system"]?["levels"] is JsonValue levels && levels.GetValue<int>() is >= 1 and <= 20, "a class has 1 to 20 levels");
            if (type == "spell")
                Need(item!["system"]?["level"] is JsonValue level && level.GetValue<int>() is >= 0 and <= 9, "a spell has a level 0 to 9");
        }
        return problems;
    }

    private static double Level(SheetProficiency proficiency) => proficiency switch
    {
        SheetProficiency.Expertise => 2,
        SheetProficiency.Proficient => 1,
        _ => 0,
    };

    private static int Value(IReadOnlyDictionary<string, SheetField> fields, string id) => fields.TryGetValue(id, out var f) ? Math.Max(0, f.Value) : 0;

    /// <summary>"LongRest: all" → lr, "ShortRest: …" → sr; anything else is kept only in the text.</summary>
    private static string? Recovery(string recovery) =>
        recovery.StartsWith("LongRest", StringComparison.Ordinal) ? "lr" : recovery.StartsWith("ShortRest", StringComparison.Ordinal) ? "sr" : null;

    private static JsonObject Item(string name, string type, JsonObject system, ContentReference? reference)
    {
        var item = new JsonObject { ["name"] = name, ["system"] = system, ["type"] = type };
        if (reference is not null)
        {
            // ADR-012 provenance: content and revision ids only.
            item["flags"] = new JsonObject { ["tomestack"] = new JsonObject { ["contentId"] = reference.ContentId.ToString("D"), ["revisionId"] = reference.RevisionId.ToString("D") } };
        }
        return item;
    }

    private static JsonObject Description(string? summary, IEnumerable<string> texts)
    {
        var html = new StringBuilder();
        foreach (var paragraph in new[] { summary }.Concat(texts).OfType<string>().Where(t => t.Length > 0).Distinct())
            html.Append("<p>").Append(WebUtility.HtmlEncode(paragraph)).Append("</p>");
        return new JsonObject { ["value"] = html.ToString() };
    }

    /// <summary>
    /// ADR-012 "Attribution for every included source": every notice in the actor's biography, and the nominative
    /// non-affiliation statement (the README carries it too).
    /// </summary>
    private static string Biography(SheetExport sheet)
    {
        var html = new StringBuilder("<h2>Sources and licenses</h2>");
        foreach (var notice in sheet.Notices)
        {
            html.Append("<p><strong>").Append(WebUtility.HtmlEncode(notice.Title)).Append("</strong> (")
                .Append(WebUtility.HtmlEncode(notice.Publisher)).Append("), ").Append(WebUtility.HtmlEncode(notice.License)).Append('.');
            if (notice.Attribution is { Length: > 0 } attribution) html.Append(' ').Append(WebUtility.HtmlEncode(attribution));
            if (notice.ModificationNotice is { Length: > 0 } modified) html.Append(' ').Append(WebUtility.HtmlEncode(modified));
            if (notice.TotalsOnly) html.Append(" Only totals from this source are in this file.");
            html.Append("</p>");
        }
        if (sheet.Purpose == SheetPurpose.Personal)
            html.Append("<p><strong>Personal copy: includes your own homebrew. Do not share it.</strong></p>");
        html.Append("<p>Exported by TomeStack for Foundry VTT (dnd5e system). TomeStack is not affiliated with Foundry Gaming LLC, Roll20 or Wizards of the Coast.</p>");
        return html.ToString();
    }

    /// <summary>A dnd5e identifier: lowercase letters, digits and hyphens.</summary>
    internal static string Identifier(string name)
    {
        var slug = new string([.. name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        return slug.Length == 0 ? "item" : slug[..Math.Min(slug.Length, 60)];
    }

    /// <summary>Object keys in ordinal order, recursively, so output is deterministic (ADR-012 "Golden files").</summary>
    internal static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Sorted)]),
        _ => node?.DeepClone(),
    };
}
