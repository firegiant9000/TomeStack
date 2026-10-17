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

    /// <summary>
    /// The field names of the fillable 2014 sheet layout (<c>wotc-2014</c>), exactly as the form spells them, trailing and
    /// double spaces included (names only: item 14 read them from the owner's sheet, never a value). The check boxes are
    /// numbered by the form: 11 and 18 to 22 are the six saves in sheet order, 23 to 40 the eighteen skills in sheet order
    /// (alphabetical), 12 to 17 the death saves (three successes, then three failures).
    /// </summary>
    public static class Wotc
    {
        public static readonly string[] AbilityKeys = ["str", "dex", "con", "int", "wis", "cha"];
        // R95: on the owner's sheet the form names its boxes the other way round: "STR" holds the modifier and "STRmod" the score.
        public static readonly string[] Scores = ["STRmod", "DEXmod ", "CONmod", "INTmod", "WISmod", "CHamod"];
        public static readonly string[] Mods = ["STR", "DEX", "CON", "INT", "WIS", "CHA"];
        public static readonly string[] SaveBoxes = ["Check Box 11", "Check Box 18", "Check Box 19", "Check Box 20", "Check Box 21", "Check Box 22"];
        public static readonly string[] SaveBonuses = ["ST Strength", "ST Dexterity", "ST Constitution", "ST Intelligence", "ST Wisdom", "ST Charisma"];
        public static readonly string[] DeathSuccessBoxes = ["Check Box 12", "Check Box 13", "Check Box 14"];
        public static readonly string[] DeathFailureBoxes = ["Check Box 15", "Check Box 16", "Check Box 17"];

        /// <summary>The skills in sheet order: the key, the bonus field, and the proficiency box (23 to 40).</summary>
        public static readonly (string Key, string Bonus, string Box)[] Skills =
        [
            ("acrobatics", "Acrobatics", "Check Box 23"), ("animalHandling", "Animal", "Check Box 24"), ("arcana", "Arcana", "Check Box 25"),
            ("athletics", "Athletics", "Check Box 26"), ("deception", "Deception ", "Check Box 27"), ("history", "History ", "Check Box 28"),
            ("insight", "Insight", "Check Box 29"), ("intimidation", "Intimidation", "Check Box 30"), ("investigation", "Investigation ", "Check Box 31"),
            ("medicine", "Medicine", "Check Box 32"), ("nature", "Nature", "Check Box 33"), ("perception", "Perception ", "Check Box 34"),
            ("performance", "Performance", "Check Box 35"), ("persuasion", "Persuasion", "Check Box 36"), ("religion", "Religion", "Check Box 37"),
            ("sleightOfHand", "SleightofHand", "Check Box 38"), ("stealth", "Stealth ", "Check Box 39"), ("survival", "Survival", "Check Box 40"),
        ];

        /// <summary>The three weapon rows: name, attack bonus and damage (the form's irregular spacing is kept).</summary>
        public static readonly (string Name, string Bonus, string Damage)[] Weapons =
        [
            ("Wpn Name", "Wpn1 AtkBonus", "Wpn1 Damage"),
            ("Wpn Name 2", "Wpn2 AtkBonus ", "Wpn2 Damage "),
            ("Wpn Name 3", "Wpn3 AtkBonus  ", "Wpn3 Damage "),
        ];

        public static readonly string[] Appearance = ["Age", "Height", "Weight", "Eyes", "Skin", "Hair"];

        /// <summary>The free-text fields that go to session notes, with the layout's clip list.</summary>
        public static readonly string[] Prose = ["PersonalityTraits ", "Ideals", "Bonds", "Flaws", "Backstory", "Features and Traits", "Feat+Traits", "Treasure"];
    }

    /// <summary>The committed fillable-2014-shaped fixture's path in the test output.</summary>
    public static string WotcCommittedPath => Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-wotc-sheet.pdf");

    /// <summary>
    /// Testy McFixture on the fillable 2014 sheet's field names: invented values only. The class text uses the form's
    /// "LVL" spelling, the abilities have both boxes, and the weapon rows, equipment and proficiencies are filled in the
    /// ways a player writes them. <c>PlayerName</c> is filled so tests can prove it is never read.
    /// </summary>
    public static IReadOnlyList<FormSpec> FieldsWotc2014 { get; } = WotcCharacter();

    public static byte[] SheetWotc2014() => Write(FieldsWotc2014, pages: 3);

    /// <summary>The committed stub-class fixture's path in the test output (item 15, D35).</summary>
    public static string TinkererCommittedPath => Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-tinkerer-sheet.pdf");

    /// <summary>
    /// "Testy Tinker" on the fillable 2014 sheet with one class TomeStack does not have, the invented "Fixture Tinkerer" 3
    /// (hit dice total 3d8, the Strength save ticked). The e2e flow creates a homebrew class from it.
    /// </summary>
    public static IReadOnlyList<FormSpec> FieldsTinkerer { get; } =
    [
        .. FieldsWotc2014.Select(f => f.Name switch
        {
            "ClassLevel" => f with { Text = "Fixture Tinkerer LVL 3" },
            "CharacterName" => f with { Text = "Testy Tinker" },
            _ => f,
        }),
    ];

    public static byte[] SheetTinkerer() => Write(FieldsTinkerer, pages: 3);

    private static List<FormSpec> WotcCharacter()
    {
        var fields = new List<FormSpec>();
        void Text(string name, string value, int page = 1) => fields.Add(new(name, value, Page: page));
        void Box(string name, bool on, int page = 1) => fields.Add(new(name, Checked: on, Page: page));

        Text("ClassLevel", "Fixture Arcanist LVL 3");
        Text("Background", "Fixture Archivist");
        Text("PlayerName", "Testy Player");
        Text("CharacterName", "Testy McFixture");
        Text("Race ", "Fixture Quickfoot");
        Text("Alignment", "Fixture Neutral");
        Text("XP", "900");
        string[] scores = ["16", "14", "15", "10", "12", "8"];
        string[] mods = ["+3", "+2", "+2", "+0", "+1", "-1"];
        for (var i = 0; i < 6; i++)
        {
            Text(Wotc.Scores[i], scores[i]);
            Text(Wotc.Mods[i], mods[i]);
            Text(Wotc.SaveBonuses[i], mods[i]);
            Box(Wotc.SaveBoxes[i], i == 0);
        }
        Text("ProfBonus", "+2");
        Text("AC", "16");
        Text("Initiative", "+2");
        Text("Speed", "30 ft");
        Text("HPMax", "28");
        Text("HPCurrent", "21");
        Text("HPTemp", "3");
        Text("HDTotal", "3d8");
        Text("HD", "2d8");
        Text("Passive", "11");
        Text("CP", "5");
        Text("SP", "");
        Text("EP", "");
        Text("GP", "12");
        Text("PP", "");
        for (var i = 0; i < 3; i++)
        {
            Box(Wotc.DeathSuccessBoxes[i], i == 0);
            Box(Wotc.DeathFailureBoxes[i], i < 2);
        }
        foreach (var (key, bonus, box) in Wotc.Skills)
        {
            Text(bonus, key == "athletics" ? "+5" : "+0");
            Box(box, key == "athletics");
        }
        (string Name, string Bonus, string Damage)[] weapons = [("Testy Longblade", "+5", "1d8+3 slashing"), ("Testy Dagger", "+4", "1d4+2 piercing"), ("", "", "")];
        for (var i = 0; i < 3; i++)
        {
            Text(Wotc.Weapons[i].Name, weapons[i].Name);
            Text(Wotc.Weapons[i].Bonus, weapons[i].Bonus);
            Text(Wotc.Weapons[i].Damage, weapons[i].Damage);
        }
        Text("AttacksSpellcasting", "Fixture attack notes.");
        Text("Equipment", "Fixture Rope Coil (3)\n2 x Fixture Lantern\nFixture Waterskin");
        Text("ProficienciesLang", "Languages: Common, Fixture Tongue\nTools: Fixture Kit");
        Text("PersonalityTraits ", "Testy personality.");
        Text("Ideals", "Testy ideals.");
        Text("Bonds", "Testy bonds.");
        Text("Flaws", "Testy flaws.");
        Text("Features and Traits", "Fixture Steady Breath\nFixture Bold Surge");
        string[] looks = ["25", "5 ft 8 in", "150 lb", "Fixture Green", "Fixture Pale", "Fixture Dark"];
        for (var i = 0; i < looks.Length; i++)
            Text(Wotc.Appearance[i], looks[i], 2);
        Text("CharacterName 2", "Testy McFixture", 2);
        Text("Allies", "Testy allies.", 2);
        Text("Backstory", "Testy backstory.", 2);
        Text("Feat+Traits", "Fixture Keen Watcher", 2);
        Text("Treasure", "Testy treasure.", 2);
        Text("Spells 1014", "Fixture Frost Ring", 3);
        return fields;
    }

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
