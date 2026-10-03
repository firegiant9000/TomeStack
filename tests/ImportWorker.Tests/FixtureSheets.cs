using static TomeStack.ImportWorker.Tests.FormPdfWriter;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// Invented character sheets for the character-sheet importer (SPEC Q-03: every value is original and starts with
/// "Fixture" or "Testy"). Nothing here comes from a real export. The 2014 sheet uses the field names of the
/// <c>ddb-2014</c> layout map, confirmed by S0 against the owner's export (names only); the 2024 sheet keeps placeholder
/// names prefixed <c>fixture.2024.</c> until a 2024-style export is inventoried. <see cref="Sheet2014"/> is committed as
/// <c>tests/RulesFixtures/pdf/fixture-ddb-sheet.pdf</c>.
/// </summary>
internal static class FixtureSheets
{
    // Static initializers run in textual order: the layout tables come before the sheets that read them.
    private static readonly Dictionary<string, (string Score, string Prof, string Save)> Abilities = new()
    {
        ["str"] = ("STR", "Str", "Strength"), ["dex"] = ("DEX", "Dex", "Dexterity"), ["con"] = ("CON", "Con", "Constitution"),
        ["int"] = ("INT", "Int", "Intelligence"), ["wis"] = ("WIS", "Wis", "Wisdom"), ["cha"] = ("CHA", "Cha", "Charisma"),
    };

    /// <summary>The 2014 layout's skill fields: the proficiency mark's prefix and the bonus field, as the export names them.</summary>
    private static readonly Dictionary<string, (string Prof, string Bonus)> Skills = new()
    {
        ["acrobatics"] = ("Acrobatics", "Acrobatics"), ["animalHandling"] = ("AnimalHandling", "Animal"), ["arcana"] = ("Arcana", "Arcana"),
        ["athletics"] = ("Athletics", "Athletics"), ["deception"] = ("Deception", "Deception"), ["history"] = ("History", "History"),
        ["insight"] = ("Insight", "Insight"), ["intimidation"] = ("Intimidation", "Intimidation"), ["investigation"] = ("Investigation", "Investigation"),
        ["medicine"] = ("Medicine", "Medicine"), ["nature"] = ("Nature", "Nature"), ["perception"] = ("Perception", "Perception"),
        ["performance"] = ("Performance", "Performance"), ["persuasion"] = ("Persuasion", "Persuasion"), ["religion"] = ("Religion", "Religion"),
        ["sleightOfHand"] = ("SleightOfHand", "SleightofHand"), ["stealth"] = ("Stealth", "Stealth "), ["survival"] = ("Survival", "Survival"),
    };

    private static readonly Dictionary<string, string> Plain = new()
    {
        ["name"] = "CharacterName", ["classLevel"] = "CLASS  LEVEL", ["species"] = "RACE", ["background"] = "BACKGROUND",
        ["hitPoints"] = "MaxHP", ["proficiencyBonus"] = "ProfBonus", ["armorClass"] = "AC", ["initiative"] = "Init",
        ["spellAttack"] = "spellAtkBonus0", ["spellSaveDc"] = "spellSaveDC0",
        ["currentHitPoints"] = "CurrentHP", ["temporaryHitPoints"] = "TempHP", ["playerName"] = "PLAYER NAME",
    };

    /// <summary>The committed fixture's path in the test output.</summary>
    public static string CommittedPath => Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-ddb-sheet.pdf");

    public static IReadOnlyList<FormSpec> Fields2014 { get; } = Character(Ddb2014, "Fixture Arcanist 3 / Fixture Chanter 2");

    /// <summary>The 2024 sheet (parsed only, never imported) names a subclass.</summary>
    public static IReadOnlyList<FormSpec> Fields2024 { get; } = Character(Placeholder2024, "Fixture Fighter 3 (Fixture Vanguard)");

    public static byte[] Sheet2014() => Write(Fields2014, pages: 3);

    public static byte[] Sheet2024() => Write(Fields2024, pages: 3, nested: true);

    /// <summary>A field of the placeholder 2024 layout: its key under <c>fixture.2024.</c>, a checkbox where the key is a mark.</summary>
    public static (string Name, bool TextMark)? Placeholder2024(string key) => ("fixture.2024." + key, false);

    /// <summary>
    /// The 2014 layout's field for a test key (<c>classLevel</c>, <c>saves.str.proficient</c>, <c>skills.stealth.bonus</c>,
    /// <c>spells.0.name</c>, <c>equipment.3.quantity</c>, …), and whether it is a text proficiency mark rather than a
    /// checkbox. Null for what the layout does not have (an equipped mark, death saves, spent hit dice or slots).
    /// <c>features</c> and <c>feats</c> share one text (<see cref="FeaturesText"/>).
    /// </summary>
    public static (string Name, bool TextMark)? Ddb2014(string key)
    {
        if (Plain.TryGetValue(key, out var plain))
            return (plain, false);
        if (key == "inspiration")
            return ("Inspiration", false);
        if (Abilities.TryGetValue(key, out var score))
            return (score.Score, false);
        var parts = key.Split('.');
        return parts switch
        {
            ["saves", var a, "proficient"] when Abilities.ContainsKey(a) => (Abilities[a].Prof + "Prof", true),
            ["saves", var a, "bonus"] when Abilities.ContainsKey(a) => ("ST " + Abilities[a].Save, false),
            ["skills", var s, "proficient"] when Skills.ContainsKey(s) => (Skills[s].Prof + "Prof", true),
            ["skills", var s, "bonus"] when Skills.ContainsKey(s) => (Skills[s].Bonus, false),
            ["spells", var n, "name"] => ("SpellName" + n, false),
            ["spells", var n, "prepared"] => ("Prepared" + n, true),
            ["equipment", var n, "name"] => ("Eq Name" + n, false),
            ["equipment", var n, "quantity"] => ("Eq Qty" + n, false),
            _ => null,
        };
    }

    /// <summary>The first features text of the 2014 layout: bullet lines under section headings, feats under <c>FEATS</c>, with an invented description line.</summary>
    public static string FeaturesText(IEnumerable<string> features, IEnumerable<string> feats)
    {
        var text = new List<string> { "=== FIXTURE FEATURES ===" };
        foreach (var feature in features)
        {
            text.Add($"* {feature} • FX 1");
            text.Add("An invented line of fixture description.");
        }
        var featList = feats.ToList();
        if (featList.Count > 0)
        {
            text.Add("");
            text.Add("=== FEATS ===");
            text.AddRange(featList.Select(f => $"* {f} •"));
        }
        return string.Join('\n', text);
    }

    /// <summary>
    /// Testy McFixture: the character, its numbers and play state, a feat and features; the spells; the equipment. The 2014
    /// sheet names the original fixture content the DevHost seeds (the casters "Fixture Arcanist" and "Fixture Chanter", the
    /// species "Fixture Quickfoot", two fixture items), so the e2e import matches it; its background, feat, features and two
    /// items are deliberately not installed, and "Fixture Veil" is on both casters' lists, so the import has rows to resolve
    /// and leave out.
    /// </summary>
    private static List<FormSpec> Character(Func<string, (string Name, bool TextMark)?> layout, string classLevel)
    {
        var fields = new List<FormSpec>();
        void Text(string key, string value, int page = 1)
        {
            if (layout(key) is { } field)
                fields.Add(new(field.Name, value, Page: page));
        }
        void Mark(string key, bool on, int page = 1)
        {
            if (layout(key) is { } field)
                fields.Add(field.TextMark ? new(field.Name, on ? "P" : "", Page: page) : new(field.Name, Checked: on, Page: page));
        }

        Text("name", "Testy McFixture");
        Text("classLevel", classLevel);
        Text("species", "Fixture Quickfoot");
        Text("background", "Fixture Archivist");
        foreach (var (ability, value) in new[] { ("str", "16"), ("dex", "14"), ("con", "15"), ("int", "10"), ("wis", "12"), ("cha", "8") })
            Text(ability, value);
        Mark("saves.str.proficient", true);
        Mark("saves.dex.proficient", false);
        Mark("skills.athletics.proficient", true);
        Text("hitPoints", "28");
        Mark("inspiration", false);
        Text("proficiencyBonus", "+2");
        Text("armorClass", "16");
        Text("initiative", "+2");
        string[] features = ["Fixture Steady Breath", "Fixture Bold Surge"];
        string[] feats = ["Fixture Keen Watcher"];
        if (layout("feats") is { } featsField)
        {
            fields.Add(new(featsField.Name, string.Join('\n', feats)));
            fields.Add(new(layout("features")!.Value.Name, string.Join('\n', features)));
        }
        else
            fields.Add(new("FeaturesTraits1", FeaturesText(features, feats), Page: 2));
        Text("currentHitPoints", "21");
        Text("deathSuccesses", "1");

        string[] spells = ["Fixture Frost Ring", "Fixture Veil", "Fixture Mending Word"];
        for (var i = 0; i < spells.Length; i++)
        {
            Text($"spells.{i}.name", spells[i], page: 2);
            Mark($"spells.{i}.prepared", i != 1, page: 2);
        }
        (string Name, string Quantity, bool Equipped)[] items =
        [
            ("Fixture Longblade", "1", true),
            ("Fixture Padded Jerkin", "1", true),
            ("Fixture Rope Coil", "2", false),
            ("Fixture Lantern", "1", false),
        ];
        for (var i = 0; i < items.Length; i++)
        {
            Text($"equipment.{i}.name", items[i].Name, page: 3);
            Text($"equipment.{i}.quantity", items[i].Quantity, page: 3);
            Mark($"equipment.{i}.equipped", items[i].Equipped, page: 3);
        }
        return fields;
    }
}
