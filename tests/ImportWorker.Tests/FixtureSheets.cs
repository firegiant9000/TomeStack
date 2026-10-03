using static TomeStack.ImportWorker.Tests.FormPdfWriter;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// Invented character sheets for the character-sheet importer (SPEC Q-03: every value is original and starts with
/// "Fixture" or "Testy"). Nothing here comes from a real export. The field names are placeholders prefixed
/// <c>fixture.</c> until the layout maps exist (plan S1, S2); they then take the maps' names. <see cref="Sheet2014"/> is
/// committed as <c>tests/RulesFixtures/pdf/fixture-ddb-sheet.pdf</c>.
/// </summary>
internal static class FixtureSheets
{
    /// <summary>The committed fixture's path in the test output.</summary>
    public static string CommittedPath => Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-ddb-sheet.pdf");

    public static IReadOnlyList<FormSpec> Fields2014 { get; } = Character("fixture.2014.", "Fixture Arcanist 3 / Fixture Chanter 2");

    /// <summary>The 2024 sheet (parsed only, never imported) names a subclass.</summary>
    public static IReadOnlyList<FormSpec> Fields2024 { get; } = Character("fixture.2024.", "Fixture Fighter 3 (Fixture Vanguard)");

    public static byte[] Sheet2014() => Write(Fields2014, pages: 3);

    public static byte[] Sheet2024() => Write(Fields2024, pages: 3, nested: true);

    /// <summary>
    /// Testy McFixture: page 1 the character, its numbers, feat, features (one field, a line each) and play state; page 2
    /// the spells; page 3 the equipment. The 2014 sheet names the original fixture content the DevHost seeds (the casters
    /// "Fixture Arcanist" and "Fixture Chanter", the species "Fixture Quickfoot", two fixture items), so the e2e import
    /// matches it; its background, feat, features and two items are deliberately not installed, and "Fixture Veil" is on
    /// both casters' lists, so the import has rows to resolve and leave out.
    /// </summary>
    private static List<FormSpec> Character(string prefix, string classLevel)
    {
        var fields = new List<FormSpec>
        {
            new(prefix + "name", "Testy McFixture"),
            new(prefix + "classLevel", classLevel),
            new(prefix + "species", "Fixture Quickfoot"),
            new(prefix + "background", "Fixture Archivist"),
            new(prefix + "str", "16"),
            new(prefix + "dex", "14"),
            new(prefix + "con", "15"),
            new(prefix + "int", "10"),
            new(prefix + "wis", "12"),
            new(prefix + "cha", "8"),
            new(prefix + "saves.str.proficient", Checked: true),
            new(prefix + "saves.dex.proficient", Checked: false),
            new(prefix + "skills.athletics.proficient", Checked: true),
            new(prefix + "hitPoints", "28"),
            new(prefix + "inspiration", Checked: false),
            new(prefix + "proficiencyBonus", "+2"),
            new(prefix + "armorClass", "16"),
            new(prefix + "initiative", "+2"),
            new(prefix + "feats", "Fixture Keen Watcher"),
            new(prefix + "features", "Fixture Steady Breath\nFixture Bold Surge"),
            new(prefix + "currentHitPoints", "21"),
            new(prefix + "deathSuccesses", "1"),
        };
        string[] spells = ["Fixture Frost Ring", "Fixture Veil", "Fixture Mending Word"];
        for (var i = 0; i < spells.Length; i++)
        {
            fields.Add(new($"{prefix}spells.{i}.name", spells[i], Page: 2));
            fields.Add(new($"{prefix}spells.{i}.prepared", Checked: i != 1, Page: 2));
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
            fields.Add(new($"{prefix}equipment.{i}.name", items[i].Name, Page: 3));
            fields.Add(new($"{prefix}equipment.{i}.quantity", items[i].Quantity, Page: 3));
            fields.Add(new($"{prefix}equipment.{i}.equipped", Checked: items[i].Equipped, Page: 3));
        }
        return fields;
    }
}
