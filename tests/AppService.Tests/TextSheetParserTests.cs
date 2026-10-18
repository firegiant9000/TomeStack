using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TomeStack.AppService.CharacterImport;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;
using Xunit.Abstractions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Item 19a (D37, ADR-017): the text importer reads pasted text and .txt/.md files into the same <see cref="DdbSheet"/> the PDF
/// importer produces. Every note below is invented ("Testy", "Fixture"); no real person's notes were used or seen.
/// </summary>
public class TextSheetParserTests(ITestOutputHelper output)
{
    private const string Hero = "Testy McFixture";
    private const string Arcanist = "Fixture Arcanist";
    private const string Chanter = "Fixture Chanter";

    private static DdbSheet Parse(string text) => TextSheetParser.Parse(text).Sheet ?? throw new InvalidOperationException("The text was refused.");

    private static string Lines(params string[] lines) => string.Join('\n', lines);

    private static IEnumerable<string> Texts(IEnumerable<Read<string>> reads) => reads.Where(r => r.Status == ReadStatus.Ok).Select(r => r.Value!);

    /// <summary>One invented note that uses most shapes at once.</summary>
    private static readonly string Full = Lines(
        $"Name: {Hero}",
        $"Class: {Arcanist} 3 / {Chanter} 2",
        "Race: Fixture Dwarfling",
        "Background: Fixture Courier",
        "Alignment: Neutral Fixture",
        "HP: 33/44",
        "AC: 15",
        "Speed: 30 ft.",
        "Initiative: +2",
        "Hit Dice: 3d8 + 2d6",
        "",
        "str: 10 (+0)",
        "DEX 14",
        "Constitution 12 (+1)",
        "Intelligence: 16",
        "Wis 8",
        "Charisma 11 (+0)",
        "",
        "Saves: Int, Wis",
        "Skills: Arcana, Stealth, Animal Handling",
        "Languages: Common, Fixture Tongue",
        "Tools: Fixture Kit",
        "",
        "Cantrips: Fixture Spark, Fixture Glow",
        "1st: Fixture Veil; Fixture Mend",
        "2nd",
        "- Fixture Gust",
        "",
        "Equipment",
        "- Fixture Rope (50)",
        "- 2 x Fixture Torch",
        "Fixture Pouch, Fixture Flint",
        "15gp 3sp",
        "",
        "Feats",
        "Fixture Wayfarer Feat: A wayfarer description line.",
        "",
        "Features",
        "Fixture Knack",
        "  First invented line.",
        "  Second invented line.",
        "",
        "Fixture Quiet Knack",
        "A quiet line.");

    // ---- labels ----

    [Fact]
    public void Labels_are_read_into_the_sheet_and_the_layout_is_text()
    {
        var sheet = Parse(Full);

        Assert.Equal("text", sheet.Layout);
        Assert.Equal(Hero, sheet.Name.Value);
        Assert.Equal([(Arcanist, 3), (Chanter, 2)], sheet.Classes.Value!.Select(c => (c.Name, c.Level)));
        Assert.Equal("Fixture Dwarfling", sheet.Species.Value);
        Assert.Equal("Fixture Courier", sheet.Background.Value);
        Assert.Equal(RulesFamilies.Srd51, sheet.SuggestedFamily); // "Race" is the 2014 word
        Assert.Equal("Neutral Fixture", sheet.Texts!.Details["alignment"].Value);
        Assert.Equal(44, sheet.Numbers[FieldIds.HitPoints].Value);
        Assert.Equal(33, sheet.Play.CurrentHitPoints.Value);
        Assert.Equal(15, sheet.Numbers[FieldIds.ArmorClass].Value);
        Assert.Equal(30, sheet.Numbers[FieldIds.Speed].Value);
        Assert.Equal(2, sheet.Numbers[FieldIds.Initiative].Value);
        Assert.Equal("3d8 + 2d6", sheet.HitDiceTotal!.Value.Value);
    }

    [Theory]
    [InlineData("Species", RulesFamilies.Srd521)]
    [InlineData("species", RulesFamilies.Srd521)]
    [InlineData("RACE", RulesFamilies.Srd51)]
    public void Race_and_Species_map_to_the_family_as_the_PDF_layouts_do(string label, string family)
    {
        var sheet = Parse($"{label}: Fixture Dwarfling");

        Assert.Equal("Fixture Dwarfling", sheet.Species.Value);
        Assert.Equal(family, sheet.SuggestedFamily);
    }

    [Fact]
    public void Both_words_or_neither_suggest_no_family()
    {
        Assert.Null(Parse("Race: Fixture A\nSpecies: Fixture B").SuggestedFamily);
        Assert.Null(Parse($"Name: {Hero}").SuggestedFamily);
    }

    [Theory]
    [InlineData("Class: Fixture Arcanist\nLevel: 4", "Fixture Arcanist", 4)]
    [InlineData("Class: Fixture Arcanist 4", "Fixture Arcanist", 4)]
    [InlineData("class: Fixture Arcanist Level 4", "Fixture Arcanist", 4)]
    [InlineData("CLASS & LEVEL: Fixture Arcanist lvl 4", "Fixture Arcanist", 4)]
    public void A_class_takes_its_level_from_the_line_or_the_Level_label(string text, string name, int level)
    {
        var sheet = Parse(text);

        Assert.Equal([(name, level)], sheet.Classes.Value!.Select(c => (c.Name, c.Level)));
        Assert.Empty(sheet.NotRead ?? []);
    }

    [Fact]
    public void A_subclass_in_parentheses_or_on_its_own_line_is_kept()
    {
        Assert.Equal("Fixture Path", Parse("Class: Fixture Arcanist 5 (Fixture Path)").Classes.Value![0].Subclass);
        Assert.Equal("Fixture Path", Parse("Class: Fixture Arcanist 5\nSubclass: Fixture Path").Classes.Value![0].Subclass);
    }

    [Theory]
    [InlineData("Class: Fixture Arcanist 15 / Fixture Chanter 9")] // adds up to more than 20
    [InlineData("Class: Fixture Arcanist / Fixture Chanter\nLevel: 4")] // a level for several classes is a guess
    public void Classes_that_cannot_be_read_are_unreadable_and_their_lines_are_not_read(string text)
    {
        var sheet = Parse(text);

        Assert.Equal(ReadStatus.Unreadable, sheet.Classes.Status);
        Assert.NotEmpty(sheet.NotRead!);
    }

    [Fact]
    public void A_level_with_no_class_is_not_read()
    {
        var sheet = Parse("Level: 4");

        Assert.Equal(ReadStatus.Missing, sheet.Classes.Status);
        Assert.Equal([1], sheet.NotRead!.Select(n => n.Line));
    }

    // ---- abilities ----

    [Fact]
    public void The_three_ability_forms_and_the_six_on_one_line_are_read()
    {
        var sheet = Parse(Full);

        Assert.Equal(
            [(Ability.Str, 10), (Ability.Dex, 14), (Ability.Con, 12), (Ability.Int, 16), (Ability.Wis, 8), (Ability.Cha, 11)],
            Enum.GetValues<Ability>().Select(a => (a, sheet.Abilities[a].Value)));

        var oneLine = Parse("STR 10 DEX 12 CON 14 INT 8 WIS 13 CHA 15");
        Assert.Equal([10, 12, 14, 8, 13, 15], Enum.GetValues<Ability>().Select(a => oneLine.Abilities[a].Value));
        Assert.Empty(oneLine.NotRead ?? []);
    }

    [Theory]
    [InlineData("STR 0")]
    [InlineData("STR 31")]
    public void A_score_out_of_range_is_unreadable_and_the_line_is_not_read(string line)
    {
        var sheet = Parse(line);

        Assert.Equal(ReadStatus.Unreadable, sheet.Abilities[Ability.Str].Status);
        Assert.Equal([1], sheet.NotRead!.Select(n => n.Line));
    }

    [Fact]
    public void An_ability_never_seen_is_missing()
    {
        Assert.Equal(ReadStatus.Missing, Parse("STR 10").Abilities[Ability.Dex].Status);
    }

    // ---- saves and skills ----

    [Fact]
    public void Saves_and_skills_are_read_and_the_rest_are_false_once_the_list_is_given()
    {
        var sheet = Parse(Full);

        Assert.Equal([Ability.Int, Ability.Wis], Enum.GetValues<Ability>().Where(a => sheet.SaveProficient[a].Value));
        Assert.Equal(["animalHandling", "arcana", "stealth"], sheet.SkillProficient.Where(s => s.Value.Value).Select(s => s.Key).Order(StringComparer.Ordinal));
        Assert.Equal(ReadStatus.Ok, sheet.SkillProficient["history"].Status);
        Assert.Equal(ReadStatus.Missing, Parse("STR 10").SaveProficient[Ability.Str].Status);
    }

    [Fact]
    public void A_skill_entry_with_a_bonus_or_a_note_still_reads_and_an_unknown_one_leaves_its_line_not_read()
    {
        var sheet = Parse("Skills: Stealth +5 (expertise), Fixture Juggling");

        Assert.True(sheet.SkillProficient["stealth"].Value);
        Assert.Equal([1], sheet.NotRead!.Select(n => n.Line));
    }

    // ---- lists and headings ----

    [Fact]
    public void Spells_come_from_comma_semicolon_and_line_lists_under_their_headings()
    {
        var sheet = Parse(Full);

        Assert.Equal(["Fixture Spark", "Fixture Glow", "Fixture Veil", "Fixture Mend", "Fixture Gust"], sheet.Spells.Select(s => s.Value!.Name));
    }

    [Fact]
    public void Equipment_lines_become_the_equipment_text_and_languages_and_tools_the_proficiency_text()
    {
        var sheet = Parse(Full);

        Assert.Equal(Lines("Fixture Rope (50)", "2 x Fixture Torch", "Fixture Pouch", "Fixture Flint"), sheet.Texts!.Equipment.Value);
        Assert.Equal(Lines("Languages: Common, Fixture Tongue", "Tools: Fixture Kit"), sheet.Texts.LanguagesAndTools.Value);
        Assert.Empty(sheet.Items); // custom items come from the equipment text only
    }

    [Fact]
    public void Languages_and_tools_headings_with_line_lists_are_read()
    {
        var sheet = Parse(Lines("Languages", "- Common", "- Fixture Tongue", "Tools:", "Fixture Kit, Fixture Pipes"));

        Assert.Equal(Lines("Languages: Common, Fixture Tongue", "Tools: Fixture Kit, Fixture Pipes"), sheet.Texts!.LanguagesAndTools.Value);
        Assert.Empty(sheet.NotRead ?? []);
    }

    [Fact]
    public void Proficiency_lines_with_other_labels_are_not_read_but_labelled_languages_are()
    {
        var sheet = Parse(Lines("Proficiencies", "Languages: Common", "Armor: Fixture Light", "Fixture loose line"));

        Assert.Equal("Languages: Common", sheet.Texts!.LanguagesAndTools.Value);
        Assert.Equal([3, 4], sheet.NotRead!.Select(n => n.Line));
    }

    [Fact]
    public void Headings_and_labels_match_case_insensitively()
    {
        var sheet = Parse(Lines("NAME: A", "cAnTrIpS", "Fixture Spark", "EQUIPMENT:", "Fixture Rope"));

        Assert.Equal("A", sheet.Name.Value);
        Assert.Equal(["Fixture Spark"], sheet.Spells.Select(s => s.Value!.Name));
        Assert.Equal("Fixture Rope", sheet.Texts!.Equipment.Value);
    }

    // ---- Markdown ----

    [Fact]
    public void Markdown_markup_is_stripped_from_headings_labels_and_list_items_and_never_rendered()
    {
        var sheet = Parse(Lines(
            $"# {Hero}",
            "",
            $"**Class:** {Arcanist} 3",
            "__Species__: Fixture Dwarfling",
            "",
            "## Spells",
            "- Fixture Spark",
            "* Fixture Glow",
            "• Fixture Gust",
            "1. Fixture Veil",
            "2) Fixture Mend",
            "",
            "### Equipment",
            "- Fixture Rope"));

        Assert.Equal(Hero, sheet.Name.Value); // a first-line title is the name when there is no Name label
        Assert.Equal([(Arcanist, 3)], sheet.Classes.Value!.Select(c => (c.Name, c.Level)));
        Assert.Equal("Fixture Dwarfling", sheet.Species.Value);
        Assert.Equal(RulesFamilies.Srd521, sheet.SuggestedFamily);
        Assert.Equal(["Fixture Spark", "Fixture Glow", "Fixture Gust", "Fixture Veil", "Fixture Mend"], sheet.Spells.Select(s => s.Value!.Name));
        Assert.Equal("Fixture Rope", sheet.Texts!.Equipment.Value);
        Assert.Empty(sheet.NotRead ?? []);
    }

    [Fact]
    public void A_Name_label_wins_over_a_title_and_html_is_just_text()
    {
        var sheet = Parse(Lines("# A Title", "Name: Testy", "<b>Fixture</b> line"));

        Assert.Equal("Testy", sheet.Name.Value);
        Assert.Equal("<b>Fixture</b> line", Assert.Single(sheet.NotRead!).Text);
    }

    [Fact]
    public void A_Markdown_subheading_inside_features_is_a_feature_and_a_known_heading_ends_the_list()
    {
        var sheet = Parse(Lines("## Features", "### Fixture Knack", "An invented line.", "### Fixture Other Knack", "Another line.", "## Equipment", "Fixture Rope"));

        Assert.Equal(["Fixture Knack", "Fixture Other Knack"], Texts(sheet.Features));
        Assert.Equal([("Fixture Knack", "An invented line."), ("Fixture Other Knack", "Another line.")], sheet.FeatureTexts!.Select(t => (t.Name, t.Text)));
        Assert.Equal("Fixture Rope", sheet.Texts!.Equipment.Value);
    }

    // ---- coins ----

    [Theory]
    [InlineData("15gp 3sp", 15, 3)]
    [InlineData("Coins: 15 gp, 3 sp", 15, 3)]
    [InlineData("- 15gp", 15, 0)]
    [InlineData("Gold: 15", 15, 0)]
    public void Coins_are_read_wherever_they_are(string line, int gp, int sp)
    {
        var sheet = Parse(Lines("Equipment", "Fixture Rope", line));

        Assert.Equal(gp, sheet.Currency["gp"].Value);
        if (sp > 0)
            Assert.Equal(sp, sheet.Currency["sp"].Value);
        Assert.Equal("Fixture Rope", sheet.Texts!.Equipment.Value);
    }

    [Fact]
    public void A_coin_amount_that_is_too_large_is_unreadable_and_a_repeated_coin_line_is_not_read()
    {
        var big = Parse("9999999gp");
        Assert.Equal(ReadStatus.Unreadable, big.Currency["gp"].Status);
        Assert.Equal([1], big.NotRead!.Select(n => n.Line));

        var twice = Parse("5gp\n7gp");
        Assert.Equal(5, twice.Currency["gp"].Value);
        Assert.Equal([2], twice.NotRead!.Select(n => n.Line));
    }

    // ---- hit dice ----

    [Theory]
    [InlineData("Hit Dice: 5d8", "5d8")]
    [InlineData("HD: 3d10 + 2d8", "3d10 + 2d8")]
    [InlineData("hit dice 4d6", "4d6")]
    public void A_hit_dice_label_feeds_the_total_the_stub_class_reads(string line, string total)
    {
        Assert.Equal(total, Parse(line).HitDiceTotal!.Value.Value);
    }

    [Fact]
    public void No_hit_dice_label_leaves_the_total_absent()
    {
        Assert.Null(Parse($"Name: {Hero}").HitDiceTotal);
    }

    // ---- feats and features ----

    [Fact]
    public void Feats_and_features_come_in_both_shapes_with_their_texts_by_position()
    {
        var sheet = Parse(Full);

        Assert.Equal(["Fixture Wayfarer Feat"], Texts(sheet.Feats));
        Assert.Equal(["Fixture Knack", "Fixture Quiet Knack"], Texts(sheet.Features));
        Assert.Equal(
            [
                ("Fixture Wayfarer Feat", FeatureKind.Feat, "A wayfarer description line.", 0),
                ("Fixture Knack", FeatureKind.Feature, "First invented line.\nSecond invented line.", 0),
                ("Fixture Quiet Knack", FeatureKind.Feature, "A quiet line.", 1),
            ],
            sheet.FeatureTexts!.Select(t => (t.Name, t.Kind, t.Text, t.Index)));
    }

    [Fact]
    public void A_feature_with_no_text_has_none_and_a_name_list_gives_names_only()
    {
        var sheet = Parse(Lines("Features: Fixture One, Fixture Two", "Feats", "Fixture Bare Feat"));

        Assert.Equal(["Fixture One", "Fixture Two"], Texts(sheet.Features));
        Assert.Equal(["Fixture Bare Feat"], Texts(sheet.Feats));
        Assert.Empty(sheet.FeatureTexts ?? []);
    }

    [Fact]
    public void Bulleted_features_are_each_an_item_and_indented_bullets_belong_to_the_item_above()
    {
        var sheet = Parse(Lines("Features", "- Fixture Knack: First line.", "- Fixture Other", "    - an indented point", "- Fixture Third"));

        Assert.Equal(["Fixture Knack", "Fixture Other", "Fixture Third"], Texts(sheet.Features));
        Assert.Equal([("Fixture Knack", "First line."), ("Fixture Other", "- an indented point")], sheet.FeatureTexts!.Select(t => (t.Name, t.Text)));
    }

    // ---- not read ----

    [Fact]
    public void Every_line_not_understood_is_listed_with_its_line_number_and_blank_lines_are_not()
    {
        var sheet = Parse(Lines(
            $"Name: {Hero}", // 1
            "", // 2
            "Remember to buy rope", // 3
            "Pet: Fixture Hound", // 4
            "Backstory:", // 5
            "He liked rope.", // 6
            "   ", // 7
            "Speed: fast")); // 8

        Assert.Equal([3, 4, 5, 6, 8], sheet.NotRead!.Select(n => n.Line));
        Assert.Equal("Remember to buy rope", sheet.NotRead![0].Text);
        Assert.Equal("Speed: fast", sheet.NotRead![4].Text);
    }

    [Fact]
    public void An_unknown_heading_stops_a_list_so_its_prose_is_not_taken_as_items()
    {
        var sheet = Parse(Lines("Spells", "Fixture Spark", "Backstory:", "He liked rope.", "Fixture Veil"));

        Assert.Equal(["Fixture Spark"], sheet.Spells.Select(s => s.Value!.Name));
        Assert.Equal([3, 4, 5], sheet.NotRead!.Select(n => n.Line));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("\n")]
    public void Every_line_break_style_counts_one_line(string newline)
    {
        var sheet = Parse(string.Join(newline, "Name: A", "Fixture loose", "Fixture other"));

        Assert.Equal([2, 3], sheet.NotRead!.Select(n => n.Line));
    }

    [Fact]
    public void Control_characters_and_lone_surrogates_are_replaced_in_what_is_kept()
    {
        var sheet = Parse("Name: A\u0000B\ud800C\nFixture\u0007loose");

        Assert.Equal("A B�C", sheet.Name.Value);
        Assert.Equal("Fixture loose", sheet.NotRead!.Single().Text);
    }

    // ---- caps ----

    [Fact]
    public void Five_thousand_and_one_one_character_lines_are_refused_with_the_counts_in_well_under_two_seconds()
    {
        var text = string.Join('\n', Enumerable.Repeat("Z", 5_001));
        var clock = Stopwatch.StartNew();

        var result = TextSheetParser.Parse(text);
        clock.Stop();
        output.WriteLine($"RF5 case 1: 5,001 one-character lines refused in {clock.Elapsed.TotalMilliseconds:N1} ms");

        Assert.Null(result.Sheet);
        Assert.Equal("ddb.text-too-many-lines", result.Refusal!.Code);
        Assert.Contains("5,001", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("5,000", result.Refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Z", result.Refusal.Message, StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Exactly_five_thousand_lines_are_read()
    {
        Assert.NotNull(TextSheetParser.Parse(string.Join('\n', Enumerable.Repeat("Z", 5_000))).Sheet);
    }

    [Fact]
    public void One_300_KB_line_is_refused_as_too_large_with_the_counts_in_well_under_two_seconds()
    {
        var text = new string('Z', 300 * 1024);
        var clock = Stopwatch.StartNew();

        var result = TextSheetParser.Parse(text);
        clock.Stop();
        output.WriteLine($"RF5 case 2: one 300 KB line refused in {clock.Elapsed.TotalMilliseconds:N1} ms");

        Assert.Null(result.Sheet);
        Assert.Equal("ddb.text-too-large", result.Refusal!.Code);
        Assert.Contains("307,200", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("262,144", result.Refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Z", result.Refusal.Message, StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void The_size_cap_is_262144_characters_inclusive()
    {
        Assert.NotNull(TextSheetParser.Parse(new string('Z', 262_144)).Sheet); // one long line: read (and not read), not refused
        Assert.Equal("ddb.text-too-large", TextSheetParser.Parse(new string('Z', 262_145)).Refusal!.Code);
    }

    [Fact]
    public void A_line_over_2000_characters_is_not_parsed_it_is_clipped_listed_and_counted()
    {
        var long1 = "Name: " + new string('Q', 2_100);
        var sheet = Parse(Lines("Class: Fixture Arcanist 3", long1, "Race: Fixture Dwarfling"));

        Assert.Equal(ReadStatus.Missing, sheet.Name.Status); // a long "Name:" line is not parsed
        var entry = Assert.Single(sheet.NotRead!);
        Assert.Equal(2, entry.Line);
        Assert.Equal(2_000, entry.Text.Length);
        Assert.Contains(sheet.Lines!, l => l.Contains("1 line", StringComparison.Ordinal) && l.Contains("2,000", StringComparison.Ordinal));
        Assert.Equal("Fixture Dwarfling", sheet.Species.Value);
    }

    [Fact]
    public void A_clip_never_splits_a_surrogate_pair()
    {
        var sheet = Parse(new string('a', 1_999) + "\U0001F600" + new string('b', 100));

        var kept = Assert.Single(sheet.NotRead!).Text;
        Assert.Equal(1_999, kept.Length);
        Assert.False(char.IsHighSurrogate(kept[^1]));
    }

    [Fact]
    public void An_expired_budget_stops_with_the_code_and_the_counts_and_keeps_nothing()
    {
        var result = TextSheetParser.Parse(Lines("Name: Secret Fixture", "Class: Fixture Arcanist 3", "Fixture loose"), TimeSpan.Zero);

        Assert.Null(result.Sheet);
        Assert.Equal("ddb.text-timeout", result.Refusal!.Code);
        Assert.Contains("0 of 3 lines", result.Refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_with_nothing_in_it_is_refused()
    {
        Assert.Equal("ddb.text-empty", TextSheetParser.Parse("").Refusal!.Code);
        Assert.Equal("ddb.text-empty", TextSheetParser.Parse(" \n\t\r\n ").Refusal!.Code);
    }

    [Fact]
    public void A_worst_case_text_under_the_caps_is_read_in_well_under_two_seconds()
    {
        // 131 lines of 2,000 characters: every line a long comma list, then the same under a spells heading.
        var item = "Fixture Spell, ";
        var line = string.Concat(Enumerable.Repeat(item, 2_000 / item.Length));
        var text = "Spells\n" + string.Join('\n', Enumerable.Repeat(line, 120));
        var clock = Stopwatch.StartNew();

        var result = TextSheetParser.Parse(text);
        clock.Stop();
        output.WriteLine($"RF5 worst case: {text.Length:N0} characters read in {clock.Elapsed.TotalMilliseconds:N1} ms");

        Assert.NotNull(result.Sheet);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
    }

    // ---- nothing of the text in ToString ----

    [Fact]
    public void ToString_of_the_sheet_the_result_and_the_not_read_lines_prints_counts_only()
    {
        var result = TextSheetParser.Parse(Lines("Name: Secret Fixture", "Class: Fixture Arcanist 3", "Hit Dice: 3d8", "Secret loose line"));

        var printed = result.ToString() + result.Sheet + string.Concat(result.Sheet!.NotRead!.Select(n => n.ToString())) + result.Sheet.HitDiceTotal;
        var refused = TextSheetParser.Parse("Secret " + new string('x', 262_144)).ToString();

        Assert.DoesNotContain("Secret", printed + refused, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixture", printed + refused, StringComparison.Ordinal);
        Assert.Contains("NotRead = 1", result.Sheet.ToString(), StringComparison.Ordinal);
    }

    // ---- through the app: preview, apply, notes ----

    private static DdbPreviewRequest Preview(Guid token, string family = RulesFamilies.Srd51, string? create = null, bool keep = true) =>
        new(token, family, null, [], [], false, CreateClassRow: create, KeepNotRead: keep);

    private static DdbApplyRequest Apply(Guid token, string family = RulesFamilies.Srd51, string? create = null, bool keep = true) =>
        new(token, family, null, [], [], false, Confirm: true, CreateClassRow: create, KeepNotRead: keep);

    private static ContentReference Srd(int n) => new(Guid.Parse($"52c00000-0000-4000-8000-{n:D12}"), Guid.Parse($"52e00000-0000-4000-8000-{n:D12}"));

    [Fact]
    public void The_command_text_is_read_into_a_token_and_the_preview_lists_the_lines_not_read()
    {
        using var h = new DdbHarness();

        var read = h.Temp.App.ReadDdbText(Lines($"Name: {Hero}", "Fixture loose line"));
        var preview = h.Temp.App.PreviewDdbImport(Preview(read.Token));

        Assert.Equal("text", read.Layout);
        Assert.Equal("pasted or file text", read.LayoutName);
        Assert.Equal((Hero, 1), (read.Summary.Name, read.Summary.NotRead));
        Assert.Equal([(2, "Fixture loose line")], preview.NotRead!.Select(n => (n.Line, n.Text)));
        Assert.Equal("pasted or file text", preview.Report.LayoutName);
        Assert.Equal(Hero, preview.Character.Name);
    }

    [Fact]
    public void A_refused_text_throws_a_coded_refusal_naming_counts_only_and_keeps_no_token()
    {
        using var h = new DdbHarness();
        var text = string.Join('\n', Enumerable.Repeat("Z", 5_001));
        var clock = Stopwatch.StartNew();

        var refused = Assert.Throws<AppValidationException>(() => h.Temp.App.ReadDdbText(text));
        clock.Stop();
        var tooLarge = Assert.Throws<AppValidationException>(() => h.Temp.App.ReadDdbText(new string('Z', 300 * 1024)));
        output.WriteLine($"RF5 through the app: 5,001 lines refused, then one 300 KB line refused, in {clock.Elapsed.TotalMilliseconds:N1} ms in all");

        Assert.Equal("ddb.text-too-many-lines", refused.Code);
        Assert.Equal("ddb.text-too-large", tooLarge.Code);
        Assert.DoesNotContain("Z", refused.Message + tooLarge.Message + string.Concat(refused.Problems.Select(p => p.Message)), StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void The_command_dispatcher_knows_ddb_readText_and_a_refusal_quotes_nothing()
    {
        using var h = new DdbHarness();
        var dispatcher = new CommandDispatcher(h.Temp.App);
        string Send(object payload) => dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command = "ddb.readText", payload }, RulesJson.Compact));

        var ok = JsonDocument.Parse(Send(new { text = $"Name: {Hero}" })).RootElement;
        var refused = Send(new { text = new string('Z', 300 * 1024) });
        var missing = JsonDocument.Parse(Send(new { })).RootElement;

        Assert.Contains("ddb.readText", CommandDispatcher.Commands);
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.Equal(Hero, ok.GetProperty("result").GetProperty("summary").GetProperty("name").GetString());
        Assert.Contains("ddb.text-too-large", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("ZZZ", refused, StringComparison.Ordinal);
        Assert.False(missing.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void A_text_through_the_whole_import_makes_custom_items_coins_details_and_session_notes()
    {
        using var h = new DdbHarness();
        var barbarian = h.Name(Srd(11));
        var text = Lines(
            $"Name: {Hero}", $"Class: {barbarian} 1", "Alignment: Neutral Fixture", "Equipment", "- Fixture Rope (50)", "- 2 x Fixture Torch", "15gp 3sp",
            "Languages: Common, Fixture Tongue", "Tools: Fixture Kit", "Remember the Fixture rope");
        var token = h.Temp.App.ReadDdbText(text).Token;

        var result = h.Temp.App.ApplyDdbImport(Apply(token, RulesFamilies.Srd521));

        var character = h.Temp.App.GetCharacter(result.CharacterId).Character;
        Assert.Equal(Hero, character.Name);
        Assert.Equal([("Fixture Rope", 50), ("Fixture Torch", 2)], character.CustomItems!.Select(i => (i.Name, i.Quantity))); // the Task 4 equipment rules
        Assert.Equal((15, 3), (character.Currency.Gp, character.Currency.Sp));
        Assert.Equal("Neutral Fixture", character.Details!.Alignment);
        Assert.Equal(["Common", "Fixture Tongue"], character.Details.Languages);
        Assert.Equal(["Fixture Kit"], character.Details.ToolsAndOther);
        var note = Assert.Single(character.Notes);
        Assert.StartsWith("Not read from the text", note.Text, StringComparison.Ordinal);
        Assert.Contains("Remember the Fixture rope", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unticking_the_keep_drops_the_lines_and_says_so_and_ticking_it_is_the_default()
    {
        using var h = new DdbHarness();
        var barbarian = h.Name(Srd(11));
        var text = Lines($"Class: {barbarian} 1", "Fixture loose one", "Fixture loose two");

        var kept = h.Temp.App.ApplyDdbImport(Apply(h.Temp.App.ReadDdbText(text).Token, RulesFamilies.Srd521));
        var dropped = h.Temp.App.ApplyDdbImport(Apply(h.Temp.App.ReadDdbText(text).Token, RulesFamilies.Srd521, keep: false));

        Assert.Single(h.Temp.App.GetCharacter(kept.CharacterId).Character.Notes);
        Assert.Empty(h.Temp.App.GetCharacter(dropped.CharacterId).Character.Notes);
        Assert.Contains(dropped.Report.Lines!, l => l.Contains("2 lines", StringComparison.Ordinal) && l.Contains("not read", StringComparison.Ordinal));
        Assert.True(new DdbApplyRequest(Guid.NewGuid(), RulesFamilies.Srd51, null, null, null, false).KeepNotRead);
        Assert.True(new DdbPreviewRequest(Guid.NewGuid(), RulesFamilies.Srd51, null, null, null, false).KeepNotRead);
    }

    [Fact]
    public void A_stub_class_is_offered_for_text_with_a_hit_dice_label_and_not_without()
    {
        using var h = new DdbHarness();

        var withDice = h.Temp.App.PreviewDdbImport(Preview(h.Temp.App.ReadDdbText("Class: Fixture Tinkerer 3\nHit Dice: 3d8").Token));
        var without = h.Temp.App.PreviewDdbImport(Preview(h.Temp.App.ReadDdbText("Class: Fixture Tinkerer 3").Token));
        var chosen = h.Temp.App.PreviewDdbImport(Preview(h.Temp.App.ReadDdbText("Class: Fixture Tinkerer 3\nHit Dice: 3d8").Token, create: "class:0"));

        var offer = Assert.Single(withDice.StubOffers!);
        Assert.Equal(("class:0", "Fixture Tinkerer", 8, null), (offer.RowId, offer.Name, offer.HitDie, offer.Note));
        Assert.Equal("stub.hit-die-missing", Assert.Single(without.StubOffers!).Note);
        Assert.Equal(8, chosen.Stub!.HitDie);
        Assert.True(chosen.CanApply);
    }

    [Fact]
    public void Reference_drafts_work_through_text_and_a_second_import_does_not_draft_them_again()
    {
        using var h = new DdbHarness();
        var barbarian = h.Name(Srd(11));
        var text = Lines($"Class: {barbarian} 1", "Features", "Fixture Knack", "  First invented line.", "Fixture Bare Knack", "", "Feats", "Fixture Wayfarer Feat: A wayfarer line.");
        var token = h.Temp.App.ReadDdbText(text).Token;

        var preview = h.Temp.App.PreviewDdbImport(Preview(token, RulesFamilies.Srd521));

        Assert.Equal(
            [("feat:0", "Fixture Wayfarer Feat"), ("feature:0", "Fixture Knack")],
            preview.ReferenceDrafts!.Select(d => (d.RowId, d.Name)));
        var apply = Apply(token, RulesFamilies.Srd521) with { PublishReferences = true, ExpectedReferences = [.. preview.ReferenceDrafts!.Select(d => d.RowId)] };
        Assert.Equal(2, h.Temp.App.ApplyDdbImport(apply).ReferenceDrafts!.Count);

        var again = h.Temp.App.PreviewDdbImport(Preview(h.Temp.App.ReadDdbText(text).Token, RulesFamilies.Srd521));
        Assert.Empty(again.ReferenceDrafts ?? []); // R106
    }

    [Fact]
    public void A_text_number_that_differs_is_kept_with_the_neutral_override_reason()
    {
        using var h = new DdbHarness();
        var barbarian = h.Name(Srd(11));
        var token = h.Temp.App.ReadDdbText(Lines($"Class: {barbarian} 1", "AC: 31")).Token;
        var request = Apply(token, RulesFamilies.Srd521) with { NumberChoices = [new(FieldIds.ArmorClass, NumberAction.KeepSheet)] };

        var result = h.Temp.App.ApplyDdbImport(request);

        var character = h.Temp.App.GetCharacter(result.CharacterId).Character;
        Assert.Equal("Imported from text", Assert.Single(character.Overrides).Reason);
    }

    [Fact]
    public void The_not_read_notes_are_split_and_the_note_cap_is_counted_not_dropped_silently()
    {
        var long1 = string.Join('\n', Enumerable.Range(0, 60).Select(i => $"Fixture loose line number {i:D3} " + new string('w', 60)));
        var sheet = Parse(long1);

        var all = ImportExtras.Build(sheet, new DateOnly(2026, 10, 17), DateTimeOffset.Parse("2026-10-17T10:00:00Z"));
        var capped = ImportExtras.Build(sheet, new DateOnly(2026, 10, 17), DateTimeOffset.Parse("2026-10-17T10:00:00Z"), noteRoom: 1);

        Assert.True(all.Notes.Count >= 2);
        Assert.All(all.Notes, n => Assert.True(n.Text.Length <= SessionNote.MaxText));
        Assert.StartsWith($"Not read from the text (part 1 of {all.Notes.Count})", all.Notes[0].Text, StringComparison.Ordinal);
        Assert.Single(capped.Notes);
        Assert.Contains(capped.Lines, l => l.Contains($"{all.Notes.Count - 1} session note", StringComparison.Ordinal));
    }

    // ---- shares ----

    private static string AllText(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var text = new StringBuilder();
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            text.Append(reader.ReadToEnd());
        }
        return text.ToString();
    }

    [Fact]
    public void A_marker_in_a_kept_not_read_line_is_absent_from_a_share_and_present_in_the_personal_backup()
    {
        const string marker = "FixtureMarkerQzx7";
        using var h = new DdbHarness();
        var barbarian = h.Name(Srd(11));
        var token = h.Temp.App.ReadDdbText(Lines($"Class: {barbarian} 1", $"Remember {marker} for later")).Token;
        var id = h.Temp.App.ApplyDdbImport(Apply(token, RulesFamilies.Srd521)).CharacterId;

        var share = AllText(h.Temp.App.ExportCharacters([id], ExportPurpose.Share).Content);
        var backup = AllText(h.Temp.App.ExportCharacters([id], ExportPurpose.Backup).Content);

        Assert.DoesNotContain(marker, share, StringComparison.Ordinal);
        Assert.Contains(marker, backup, StringComparison.Ordinal);
    }
}
