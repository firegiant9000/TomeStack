using System.Text.Json;
using TomeStack.AppService.CharacterImport;
using TomeStack.ImportWorker.Forms;
using TomeStack.ImportWorker.Tests;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Character-sheet import S2 (<c>features/ddb-pdf-import.md</c> "Architecture"): the layout maps and the parser from form
/// fields to a <see cref="DdbSheet"/>. The parser is total: what it cannot read is <see cref="ReadStatus.Missing"/> or
/// <see cref="ReadStatus.Unreadable"/>, never an exception, and it never echoes a value it could not read. Every value is
/// invented; the field names are the fixture's placeholders until S0's findings settle the maps.
/// </summary>
public class DdbParserTests
{
    /// <summary>The form fields the worker reads from a fixture sheet (<c>FormReaderTests</c> proves the PDF reads exactly so).</summary>
    internal static List<FormField> Fields(IEnumerable<FormPdfWriter.FormSpec> specs) =>
    [
        .. specs.Select(s => s.Checked is { } on
            ? new FormField(s.Name, "checkbox", s.Page, Checked: on, OnState: "Yes")
            : new FormField(s.Name, "text", s.Page, s.Text)),
    ];

    private static List<FormField> With(List<FormField> fields, string name, string? value) =>
        [.. fields.Select(f => f.Name == name ? f with { Value = value } : f)];

    private static DdbSheet Parse(List<FormField> fields) => DdbParser.Parse(DdbParser.Recognise(fields)!, fields);

    internal static string Json(DdbSheet sheet) => JsonSerializer.Serialize(sheet, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void Every_layout_map_is_valid_names_only_known_semantics_and_its_patterns_compile()
    {
        Assert.Equal(["ddb-2014", "ddb-2024"], LayoutMaps.All.Select(m => m.Id));
        Assert.All(LayoutMaps.All, map => Assert.Empty(LayoutMaps.Problems(map)));
        Assert.All(LayoutMaps.All, map => Assert.NotEmpty(map.Required));
    }

    [Fact]
    public void The_map_check_finds_an_unknown_semantic_a_bad_pattern_and_a_row_index_on_one_side_only()
    {
        var map = LayoutMaps.All[0] with
        {
            Fields = new Dictionary<string, FieldRule>
            {
                ["fixture.a"] = new("fixture-unknown"),
                ["fixture.b"] = new("name", "^(unclosed$"),
                ["fixture.c.{n}"] = new("species"),
                ["fixture.d"] = new("spells[n].name"),
            },
        };

        var problems = LayoutMaps.Problems(map);

        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.Contains("fixture.a", StringComparison.Ordinal) && p.Contains("unknown semantic", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("fixture.b", StringComparison.Ordinal) && p.Contains("pattern", StringComparison.Ordinal));
    }

    [Fact]
    public void The_fixture_2014_sheet_is_recognised_as_the_2014_layout_and_parses_to_the_expected_DdbSheet()
    {
        var fields = Fields(FixtureSheets.Fields2014);

        var map = DdbParser.Recognise(fields);
        Assert.Equal("ddb-2014", map?.Id);
        var sheet = DdbParser.Parse(map!, fields);

        Assert.Equal(("ddb-2014", RulesFamilies.Srd51), (sheet.Layout, sheet.SuggestedFamily));
        Assert.Equal(Read<string>.Ok("Testy McFixture"), sheet.Name);
        Assert.Equal(ReadStatus.Ok, sheet.Classes.Status);
        Assert.Equal([new ClassText("Fixture Arcanist", 3, null), new ClassText("Fixture Chanter", 2, null)], sheet.Classes.Value!);
        Assert.Equal((Read<string>.Ok("Fixture Quickfoot"), Read<string>.Ok("Fixture Archivist")), (sheet.Species, sheet.Background));
        Assert.Equal([16, 14, 15, 10, 12, 8], Enum.GetValues<Ability>().Select(a => sheet.Abilities[a].Value));
        Assert.Equal(Read<bool>.Ok(true), sheet.SaveProficient[Ability.Str]);
        Assert.Equal(Read<bool>.Ok(false), sheet.SaveProficient[Ability.Dex]);
        Assert.Equal(Read<bool>.Missing, sheet.SaveProficient[Ability.Wis]);
        Assert.Equal(Read<bool>.Ok(true), sheet.SkillProficient["athletics"]);
        Assert.Equal(Read<bool>.Missing, sheet.SkillProficient["stealth"]);
        Assert.Equal(18, sheet.SkillProficient.Count);
        Assert.Equal([Read<string>.Ok("Fixture Keen Watcher")], sheet.Feats);
        Assert.Equal([Read<string>.Ok("Fixture Steady Breath"), Read<string>.Ok("Fixture Bold Surge")], sheet.Features);
        Assert.Equal(
        [
            Read<SpellText>.Ok(new("Fixture Frost Ring", true)),
            Read<SpellText>.Ok(new("Fixture Veil", false)),
            Read<SpellText>.Ok(new("Fixture Mending Word", true)),
        ], sheet.Spells);
        Assert.Equal(
        [
            Read<ItemText>.Ok(new("Fixture Longblade", 1, true)),
            Read<ItemText>.Ok(new("Fixture Padded Jerkin", 1, true)),
            Read<ItemText>.Ok(new("Fixture Rope Coil", 2, false)),
            Read<ItemText>.Ok(new("Fixture Lantern", 1, false)),
        ], sheet.Items);
        Assert.Equal(
            new Dictionary<string, Read<int>>
            {
                [FieldIds.ProficiencyBonus] = Read<int>.Ok(2),
                [FieldIds.ArmorClass] = Read<int>.Ok(16),
                [FieldIds.Initiative] = Read<int>.Ok(2),
                [FieldIds.HitPoints] = Read<int>.Ok(28),
            }, sheet.Numbers);
        Assert.Equal(Read<int>.Ok(21), sheet.Play.CurrentHitPoints);
        Assert.Equal(Read<int>.Missing, sheet.Play.TemporaryHitPoints);
        Assert.Equal((Read<int>.Ok(1), Read<int>.Missing), (sheet.Play.DeathSuccesses, sheet.Play.DeathFailures));
        Assert.Equal(Read<bool>.Ok(false), sheet.Play.Inspiration);
        Assert.Empty(sheet.Play.HitDiceSpent);
        Assert.Empty(sheet.Play.SpellSlotsSpent);
    }

    [Fact]
    public void The_fixture_2024_sheet_is_recognised_as_the_2024_layout_and_suggests_srd_5_2_1()
    {
        var sheet = Parse(Fields(FixtureSheets.Fields2024));

        Assert.Equal(("ddb-2024", RulesFamilies.Srd521), (sheet.Layout, sheet.SuggestedFamily));
        Assert.Equal([new ClassText("Fixture Fighter", 3, "Fixture Vanguard")], sheet.Classes.Value!);
        Assert.Equal(4, sheet.Items.Count);
    }

    [Fact]
    public async Task The_committed_fixture_sheet_read_through_the_worker_parses_like_its_fields()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-ddb-sheet.pdf");
        var read = await new WorkerFormReader(Path.Combine(AppContext.BaseDirectory, TomeStackApp.WorkerFileName)).ReadAsync(path, CancellationToken.None);

        Assert.Equal(Json(Parse(Fields(FixtureSheets.Fields2014))), Json(Parse([.. read])));
    }

    [Fact]
    public void A_field_set_missing_a_required_name_is_not_recognised()
    {
        var fields = Fields(FixtureSheets.Fields2014);
        Assert.Null(DdbParser.Recognise([.. fields.Where(f => f.Name != "fixture.2014.name")]));
        Assert.Null(DdbParser.Recognise([new FormField("fixture.other", "text", 1, "Fixture")]));
        Assert.Null(DdbParser.Recognise([]));
    }

    [Fact]
    public void A_missing_optional_field_is_Missing_and_an_unparsable_value_is_Unreadable_not_an_error()
    {
        var fields = Fields(FixtureSheets.Fields2014);
        fields = With(fields, "fixture.2014.classLevel", "Fixture Fighter 25");
        fields = With(fields, "fixture.2014.str", "abc");
        fields = With(fields, "fixture.2014.equipment.0.quantity", "0");
        fields = With(fields, "fixture.2014.armorClass", "lots");
        fields = [.. fields.Where(f => f.Name != "fixture.2014.species")];

        var sheet = Parse(fields);

        Assert.Equal(ReadStatus.Unreadable, sheet.Classes.Status);
        Assert.Null(sheet.Classes.Value);
        Assert.Equal(Read<int>.Unreadable, sheet.Abilities[Ability.Str]);
        Assert.Equal(Read<string>.Missing, sheet.Species);
        Assert.Equal(Read<int>.Unreadable, sheet.Numbers[FieldIds.ArmorClass]);
        // The row's name was readable, so it is kept for the list of what was left out.
        Assert.Equal(ReadStatus.Unreadable, sheet.Items[0].Status);
        Assert.Equal("Fixture Longblade", sheet.Items[0].Value!.Name);
        Assert.Equal(ReadStatus.Ok, sheet.Items[1].Status);
    }

    [Fact]
    public void A_blank_spell_or_equipment_row_is_skipped()
    {
        var fields = With(Fields(FixtureSheets.Fields2014), "fixture.2014.spells.1.name", "  ");
        fields = With(fields, "fixture.2014.equipment.3.name", null);

        var sheet = Parse(fields);

        Assert.Equal(["Fixture Frost Ring", "Fixture Mending Word"], sheet.Spells.Select(s => s.Value!.Name));
        Assert.Equal(3, sheet.Items.Count);
    }

    [Fact]
    public void Class_text_with_two_classes_keeps_the_sheets_order_and_reads_each_subclass()
    {
        var sheet = Parse(With(Fields(FixtureSheets.Fields2014), "fixture.2014.classLevel", "Fixture Fighter 5 (Fixture Vanguard) / Fixture Mage 3"));

        Assert.Equal([new ClassText("Fixture Fighter", 5, "Fixture Vanguard"), new ClassText("Fixture Mage", 3, null)], sheet.Classes.Value!);
    }

    [Theory]
    [InlineData("Fixture Fighter 15 / Fixture Mage 6")]
    [InlineData("Fixture Fighter 3 / ")]
    [InlineData("Fixture Fighter")]
    [InlineData("Fixture Fighter 0")]
    public void Class_levels_that_add_to_more_than_twenty_are_Unreadable_as_a_whole(string text)
    {
        Assert.Equal(Read<IReadOnlyList<ClassText>>.Unreadable, Parse(With(Fields(FixtureSheets.Fields2014), "fixture.2014.classLevel", text)).Classes);
    }

    [Fact]
    public void Class_levels_adding_to_exactly_twenty_read_and_more_than_twenty_parts_do_not()
    {
        var twenty = Parse(With(Fields(FixtureSheets.Fields2014), "fixture.2014.classLevel", "Fixture Fighter 12 / Fixture Mage 8")).Classes;
        Assert.Equal([12, 8], twenty.Value!.Select(c => c.Level));

        var parts = string.Join(" / ", Enumerable.Range(0, 21).Select(i => $"Fixture Class {(char)('A' + i)} 1"));
        Assert.Equal(Read<IReadOnlyList<ClassText>>.Unreadable, Parse(With(Fields(FixtureSheets.Fields2014), "fixture.2014.classLevel", parts)).Classes);
    }

    // ---- review fixes: map checks, recognition, patterns, rows, printing ----

    private static LayoutMap Custom(IReadOnlyList<string> required, IReadOnlyDictionary<string, FieldRule> fields, string id = "fixture-layout") =>
        new(id, 1, RulesFamilies.Srd51, required, fields, "Yes", []);

    [Fact]
    public void Two_layouts_whose_required_names_are_all_present_in_equal_number_are_ambiguous_and_neither_is_picked()
    {
        var a = Custom(["fixture name", "fixture a"], new Dictionary<string, FieldRule> { ["fixture name"] = new("name") }, "fixture-a");
        var b = Custom(["fixture name", "fixture b"], new Dictionary<string, FieldRule> { ["fixture name"] = new("name") }, "fixture-b");
        var c = Custom(["fixture name", "fixture a", "fixture c"], new Dictionary<string, FieldRule> { ["fixture name"] = new("name") }, "fixture-c");
        FormField[] both = [new("fixture name", "text", 1, "Testy"), new("fixture a", "text", 1, ""), new("fixture b", "text", 1, "")];

        Assert.Null(DdbParser.Recognise(both, [a, b]));
        Assert.Equal("fixture-c", DdbParser.Recognise([.. both, new("fixture c", "text", 1, "")], [a, b, c])?.Id);
        Assert.Equal("fixture-a", DdbParser.Recognise(both, [a])?.Id);
    }

    [Fact]
    public void The_map_check_finds_a_semantic_named_twice_missing_fields_or_splits_and_an_unanchored_pattern()
    {
        var twice = Custom(["fixture name"], new Dictionary<string, FieldRule>
        {
            ["fixture name"] = new("name"), ["fixture other name"] = new("name"),
            ["fixture a {n}"] = new("spells[n].name"), ["fixture b {n}"] = new("spells[n].name"),
        });
        Assert.Equal(2, LayoutMaps.Problems(twice).Count(p => p.Contains("more than one field", StringComparison.Ordinal)));

        Assert.NotEmpty(LayoutMaps.Problems(twice with { Fields = null! }));
        Assert.NotEmpty(LayoutMaps.Problems(twice with { Splits = null! }));
        Assert.NotEmpty(LayoutMaps.Problems(twice with { Splits = [null!] }));

        var unanchored = Custom(["fixture name"], new Dictionary<string, FieldRule> { ["fixture name"] = new("name", "(?<value>Fixture)") });
        Assert.Contains(LayoutMaps.Problems(unanchored), p => p.Contains("anchored", StringComparison.Ordinal));
    }

    [Fact]
    public void Map_patterns_keep_their_value_group_or_whole_match_and_a_classLevels_pattern_reads_each_part()
    {
        var map = Custom(["fixture name"], new Dictionary<string, FieldRule>
        {
            ["fixture name"] = new("name", @"^Name: (?<value>.+)$"),
            ["fixture species"] = new("species", @"^Fixture [A-Za-z]+$"),
            ["fixture class"] = new("classLevels", @"^(?<name>Fixture [A-Za-z]+) L(?<level>\d{1,2})$"),
            ["fixture background"] = new("background", @"^Background: (?<value>.+)$"),
        });
        FormField[] fields =
        [
            new("fixture name", "text", 1, "Name: Testy McFixture"),
            new("fixture species", "text", 1, "Fixture Quickfoot"),
            new("fixture class", "text", 1, "Fixture Fighter L3 / Fixture Mage L2"),
            new("fixture background", "text", 1, "not the shape"),
        ];

        var sheet = DdbParser.Parse(map, fields);

        Assert.Empty(LayoutMaps.Problems(map));
        Assert.Equal(Read<string>.Ok("Testy McFixture"), sheet.Name);
        Assert.Equal(Read<string>.Ok("Fixture Quickfoot"), sheet.Species);
        Assert.Equal([new ClassText("Fixture Fighter", 3, null), new ClassText("Fixture Mage", 2, null)], sheet.Classes.Value!);
        Assert.Equal(Read<string>.Unreadable, sheet.Background);
    }

    [Fact]
    public void A_row_number_with_a_leading_zero_is_not_a_row()
    {
        var map = Custom(["fixture name"], new Dictionary<string, FieldRule> { ["fixture name"] = new("name"), ["fixture spell {n}"] = new("spells[n].name") });
        FormField[] fields = [new("fixture name", "text", 1, "Testy"), new("fixture spell 01", "text", 1, "Fixture Veil"), new("fixture spell 0", "text", 1, "Fixture Spark")];

        Assert.Equal(["Fixture Spark"], DdbParser.Parse(map, fields).Spells.Select(s => s.Value!.Name));
    }

    [Fact]
    public void An_unreadable_spent_hit_die_or_slot_is_counted_not_dropped_as_if_missing()
    {
        var map = Custom(["fixture name"], new Dictionary<string, FieldRule>
        {
            ["fixture name"] = new("name"), ["fixture d8"] = new("play.hitDiceSpent.d8"), ["fixture d10"] = new("play.hitDiceSpent.d10"), ["fixture slots 1"] = new("play.spellSlotsSpent.1"),
        });
        FormField[] fields = [new("fixture name", "text", 1, "Testy"), new("fixture d8", "text", 1, "abc"), new("fixture d10", "text", 1, "2"), new("fixture slots 1", "text", 1, "Fixture")];

        var play = DdbParser.Parse(map, fields).Play;

        Assert.Equal([new DieSpent(10, 2)], play.HitDiceSpent);
        Assert.Empty(play.SpellSlotsSpent);
        Assert.Equal(2, play.UnreadableSpent);
    }

    [Fact]
    public void A_parsed_sheet_prints_statuses_and_counts_never_a_value()
    {
        var sheet = Parse(Fields(FixtureSheets.Fields2014));

        string[] printed = [sheet.ToString(), sheet.Name.ToString(), sheet.Classes.Value![0].ToString(), sheet.Spells[0].Value!.ToString(), sheet.Items[0].Value!.ToString(), sheet.Play.ToString()];

        Assert.All(printed, p => Assert.DoesNotContain("Fixture", p, StringComparison.Ordinal));
        Assert.All(printed, p => Assert.DoesNotContain("Testy", p, StringComparison.Ordinal));
        Assert.Contains("Ok", sheet.Name.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Signed_numbers_read_with_a_plus_a_hyphen_or_a_minus_sign()
    {
        var fields = With(Fields(FixtureSheets.Fields2014), "fixture.2014.initiative", "−1");
        fields = With(fields, "fixture.2014.proficiencyBonus", " +3 ");
        var sheet = Parse(With(fields, "fixture.2014.armorClass", "-2"));

        Assert.Equal((-1, 3, -2), (sheet.Numbers[FieldIds.Initiative].Value, sheet.Numbers[FieldIds.ProficiencyBonus].Value, sheet.Numbers[FieldIds.ArmorClass].Value));
    }

    [Fact]
    public void Unmapped_fields_are_not_in_the_DdbSheet()
    {
        const string player = "Testy Player Sentinel";
        var fields = Fields(FixtureSheets.Fields2014);
        fields.Add(new FormField("fixture.2014.playerName", "text", 1, player));
        fields.Add(new FormField("fixture.2014.unmapped.box", "checkbox", 1, Checked: true, OnState: "FixtureSentinelState"));

        var json = Json(Parse(fields));

        Assert.DoesNotContain(player, json, StringComparison.Ordinal);
        Assert.DoesNotContain("FixtureSentinelState", json, StringComparison.Ordinal);
        Assert.DoesNotContain("playerName", json, StringComparison.Ordinal);
    }

    [Fact]
    public void No_message_or_diagnostic_quotes_a_field_value()
    {
        // Every number, mark and class field gets a value it cannot read, each a unique sentinel. The sheet holds no
        // message of its own, and an unreadable read keeps nothing of what it could not read.
        var fields = Fields(FixtureSheets.Fields2014);
        var sentinels = new List<string>();
        var text = new HashSet<string>(StringComparer.Ordinal) { "name", "species", "background", "feats", "features" };
        fields = [.. fields.Select((f, i) =>
        {
            var leaf = f.Name["fixture.2014.".Length..];
            if (text.Contains(leaf) || leaf.EndsWith(".name", StringComparison.Ordinal))
                return f;
            var sentinel = $"FixtureSentinel{i:D3}";
            sentinels.Add(sentinel);
            return f with { Type = "text", Value = sentinel, Checked = null, OnState = null };
        })];

        var sheet = Parse(fields);
        var json = Json(sheet);

        Assert.Equal(28, sentinels.Count);
        Assert.All(sentinels, s => Assert.DoesNotContain(s, json, StringComparison.Ordinal));
        Assert.Equal(ReadStatus.Unreadable, sheet.Classes.Status);
        Assert.All(sheet.Items, i => Assert.Equal(ReadStatus.Unreadable, i.Status));
    }
}
