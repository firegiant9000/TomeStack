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

    public static IReadOnlyList<FormSpec> Fields2014 { get; } = Character("fixture.2014.");

    public static IReadOnlyList<FormSpec> Fields2024 { get; } = Character("fixture.2024.");

    public static byte[] Sheet2014() => Write(Fields2014, pages: 3);

    public static byte[] Sheet2024() => Write(Fields2024, pages: 3, nested: true);

    /// <summary>Testy McFixture, a Fixture Fighter 3: page 1 the character, page 2 the spells, page 3 the equipment.</summary>
    private static List<FormSpec> Character(string prefix)
    {
        var fields = new List<FormSpec>
        {
            new(prefix + "name", "Testy McFixture"),
            new(prefix + "classLevel", "Fixture Fighter 3"),
            new(prefix + "species", "Fixture Glimmerkin"),
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
        };
        string[] spells = ["Fixture Ember Lance", "Fixture Frost Veil", "Fixture Thunder Word"];
        for (var i = 0; i < spells.Length; i++)
        {
            fields.Add(new($"{prefix}spells.{i}.name", spells[i], Page: 2));
            fields.Add(new($"{prefix}spells.{i}.prepared", Checked: i != 1, Page: 2));
        }
        (string Name, string Quantity, bool Equipped)[] items =
        [
            ("Fixture Hookblade", "1", true),
            ("Fixture Quilted Coat", "1", true),
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
