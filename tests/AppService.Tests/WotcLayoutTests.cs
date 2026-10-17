using System.Text.Json;
using TomeStack.AppService.CharacterImport;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Forms;
using TomeStack.ImportWorker.Tests;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Item 14 (Q14, D16e): the fillable 2014 sheet layout (<c>wotc-2014</c>), read by the same parser. Every value is invented;
/// the field names are the form's, spaces included.
/// </summary>
public class WotcLayoutTests
{
    private static List<FormField> Wotc() => DdbParserTests.Fields(FixtureSheets.FieldsWotc2014);

    private static List<FormField> With(List<FormField> fields, string name, string? value) =>
        [.. fields.Select(f => f.Name == name ? f with { Value = value } : f)];

    private static List<FormField> Ticked(List<FormField> fields, string name, bool on) =>
        [.. fields.Select(f => f.Name == name ? f with { Checked = on } : f)];

    private static DdbSheet Parse(List<FormField> fields) => DdbParser.Parse(DdbParser.Recognise(fields)!, fields);

    [Fact]
    public void The_wotc_map_is_valid_requires_the_three_names_the_ddb_export_lacks_and_lists_its_clip_fields()
    {
        var map = LayoutMaps.All.Single(m => m.Id == "wotc-2014");

        Assert.Empty(LayoutMaps.Problems(map));
        Assert.Equal(["ClassLevel", "Features and Traits", "PersonalityTraits "], map.Required);
        Assert.Equal(RulesFamilies.Srd51, map.SuggestedFamily);
        string[] expectedClip = [.. FixtureSheets.Wotc.Prose, "Equipment", "ProficienciesLang"];
        Assert.Equal(expectedClip.Order(StringComparer.Ordinal), map.ClipFields!.Order(StringComparer.Ordinal));
        Assert.All(map.ClipFields!, c => Assert.Contains(c, map.Fields.Keys));
        Assert.Equal("Imported from the character sheet", map.ImportedOverrideReason);
        Assert.Equal("the fillable 2014 sheet", map.DisplayName);
        Assert.Equal("D&D Beyond 2014", LayoutMaps.All.Single(m => m.Id == "ddb-2014").DisplayName);
        Assert.Equal("Imported from D&D Beyond", LayoutMaps.All.Single(m => m.Id == "ddb-2014").ImportedOverrideReason);
        // Item 14 fix 1: each layout words its import gap notes; the D&D Beyond ones stay exactly as they were.
        Assert.Equal("Not brought over from the character sheet: TomeStack had no match or no place for it.", map.ImportGapNoteText);
        Assert.All(LayoutMaps.All.Where(m => m.Id.StartsWith("ddb-", StringComparison.Ordinal)),
            m => Assert.Equal("Not brought over from the D&D Beyond sheet: TomeStack had no match or no place for it.", m.ImportGapNoteText));
        Assert.Equal("Not brought over from the D&D Beyond sheet: TomeStack had no match or no place for it.", TomeStackApp.ImportNoteText);
    }

    [Fact]
    public void Names_with_trailing_or_double_spaces_survive_the_loader_so_a_formatter_cannot_trim_them()
    {
        // The keys are the form's own spelling; an editor or formatter that trims a JSON string key would lose the sheet.
        using var stream = typeof(LayoutMaps).Assembly.GetManifestResourceStream("TomeStack.CharacterImport.Layouts.wotc-2014.v1.json")!;
        using var json = JsonDocument.Parse(stream);
        var fields = json.RootElement.GetProperty("fields");
        foreach (var name in new[] { "PersonalityTraits ", "Race ", "DEXmod ", "Deception ", "Stealth ", "Wpn2 AtkBonus ", "Wpn3 AtkBonus  ", "Wpn3 Damage " })
            Assert.True(fields.TryGetProperty(name, out _), $"The key '{name}' lost its spaces.");
        Assert.Contains("PersonalityTraits ", json.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()));

        var map = LayoutMaps.All.Single(m => m.Id == "wotc-2014");
        Assert.Contains("Race ", map.Fields.Keys);
        Assert.Contains("PersonalityTraits ", map.Required);
        Assert.Contains("Wpn3 AtkBonus  ", map.Fields.Keys);
    }

    [Fact]
    public void Each_layout_recognises_its_own_fixture_and_the_player_name_is_never_mapped()
    {
        Assert.Equal("ddb-2014", DdbParser.Recognise(DdbParserTests.Fields(FixtureSheets.Fields2014))?.Id);
        Assert.Equal("ddb-2024", DdbParser.Recognise(DdbParserTests.Fields(FixtureSheets.Fields2024))?.Id);
        Assert.Equal("wotc-2014", DdbParser.Recognise(Wotc())?.Id);
        var map = LayoutMaps.All.Single(m => m.Id == "wotc-2014");
        Assert.DoesNotContain(map.Fields.Keys, k => k.Contains("Player", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Testy Player", DdbParserTests.Json(Parse(Wotc())), StringComparison.Ordinal);
    }

    [Fact]
    public void A_doctored_sheet_with_both_name_sets_is_refused_rather_than_resolved_to_one_layout()
    {
        List<FormField> both = [.. Wotc(), .. DdbParserTests.Fields(FixtureSheets.Fields2014)];

        Assert.Null(DdbParser.Recognise(both));

        using var harness = new DdbHarness { Fields = both };
        var refused = Assert.Throws<AppValidationException>(() => harness.Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf"));
        Assert.Equal("ddb.layout-unknown", refused.Code);
    }

    [Fact]
    public void The_fixture_wotc_sheet_parses_to_the_expected_DdbSheet()
    {
        var sheet = Parse(Wotc());

        Assert.Equal("wotc-2014", sheet.Layout);
        Assert.Equal("Testy McFixture", sheet.Name.Value);
        Assert.Equal([new ClassText("Fixture Arcanist", 3, null)], sheet.Classes.Value!);
        Assert.Equal("Fixture Quickfoot", sheet.Species.Value);
        Assert.Equal("Fixture Archivist", sheet.Background.Value);
        Assert.Equal([16, 14, 15, 10, 12, 8], Enum.GetValues<Ability>().Select(a => sheet.Abilities[a].Value));
        Assert.Equal([DeathLine], sheet.Lines); // the fixture ticks some death-save boxes and nothing else needs a line
        Assert.Equal([true, false, false, false, false, false], Enum.GetValues<Ability>().Select(a => sheet.SaveProficient[a].Value));
        Assert.Equal(["athletics"], sheet.SkillProficient.Where(s => s.Value.Value).Select(s => s.Key));
        Assert.Equal(18, sheet.SkillProficient.Count(s => s.Value.Status == ReadStatus.Ok));
        Assert.Equal(2, sheet.Numbers[FieldIds.ProficiencyBonus].Value);
        Assert.Equal(16, sheet.Numbers[FieldIds.ArmorClass].Value);
        Assert.Equal(2, sheet.Numbers[FieldIds.Initiative].Value);
        Assert.Equal(30, sheet.Numbers[FieldIds.Speed].Value);
        Assert.Equal(28, sheet.Numbers[FieldIds.HitPoints].Value);
        Assert.Equal(11, sheet.Numbers[FieldIds.Passive("perception")].Value);
        Assert.Equal(5, sheet.Numbers[FieldIds.Skill("athletics")].Value);
        Assert.Equal(3, sheet.Numbers[FieldIds.Save(Ability.Str)].Value);
        Assert.Equal((21, 3, 1, 2), (sheet.Play.CurrentHitPoints.Value, sheet.Play.TemporaryHitPoints.Value, sheet.Play.DeathSuccesses.Value, sheet.Play.DeathFailures.Value));
        Assert.Equal([new DieSpent(8, 1)], sheet.Play.HitDiceSpent);
        Assert.Equal((5, 12), (sheet.Currency["cp"].Value, sheet.Currency["gp"].Value));
        Assert.Equal(ReadStatus.Missing, sheet.Currency["sp"].Status);

        var texts = sheet.Texts!;
        Assert.Equal("Fixture Neutral", texts.Details["alignment"].Value);
        Assert.Equal(["age", "eyes", "hair", "height", "skin", "weight"], texts.Details.Keys.Where(k => k != "alignment").Order());
        Assert.Equal("Testy personality.", texts.Prose["personalityTraits"].Value);
        Assert.Equal(8, texts.Prose.Count);
        Assert.Equal("Fixture Steady Breath\nFixture Bold Surge", texts.Prose["featuresAndTraits"].Value);
        Assert.Equal(3, texts.Weapons.Count);
        Assert.Equal(("Testy Longblade", "+5", "1d8+3 slashing"), (texts.Weapons[0].Name.Value, texts.Weapons[0].ToHit.Value, texts.Weapons[0].Damage.Value));
        Assert.Equal(ReadStatus.Missing, texts.Weapons[2].Name.Status);
        Assert.Equal(ReadStatus.Ok, texts.Equipment.Status);
        Assert.Equal(ReadStatus.Ok, texts.LanguagesAndTools.Status);
    }

    [Fact]
    public void The_six_save_boxes_and_the_eighteen_skill_boxes_map_in_sheet_order()
    {
        var plain = Wotc().Select(f => f.Type == "checkbox" ? f with { Checked = false } : f).ToList();
        for (var i = 0; i < 6; i++)
        {
            var sheet = Parse(Ticked(plain, FixtureSheets.Wotc.SaveBoxes[i], true));
            Assert.Equal([.. Enumerable.Range(0, 6).Select(j => j == i)], Enum.GetValues<Ability>().Select(a => sheet.SaveProficient[a].Value));
            Assert.DoesNotContain(true, sheet.SkillProficient.Values.Select(v => v.Value));
        }
        for (var i = 0; i < 18; i++)
        {
            var sheet = Parse(Ticked(plain, FixtureSheets.Wotc.Skills[i].Box, true));
            Assert.Equal([FixtureSheets.Wotc.Skills[i].Key], sheet.SkillProficient.Where(s => s.Value.Value).Select(s => s.Key));
            Assert.DoesNotContain(true, sheet.SaveProficient.Values.Select(v => v.Value));
        }
        // Box 11 is the Strength save and 23 the first skill: the numbering is not contiguous.
        Assert.Equal("Check Box 11", FixtureSheets.Wotc.SaveBoxes[0]);
        Assert.Equal("Check Box 23", FixtureSheets.Wotc.Skills[0].Box);
    }

    [Fact]
    public void The_six_death_save_boxes_count_successes_then_failures()
    {
        var plain = Wotc().Select(f => f.Type == "checkbox" ? f with { Checked = false } : f).ToList();
        var none = Parse(plain).Play;
        Assert.Equal((0, 0), (none.DeathSuccesses.Value, none.DeathFailures.Value));

        var some = plain;
        foreach (var box in FixtureSheets.Wotc.DeathSuccessBoxes.Take(3).Concat(FixtureSheets.Wotc.DeathFailureBoxes.Take(1)))
            some = Ticked(some, box, true);
        var play = Parse(some).Play;
        Assert.Equal((3, 1), (play.DeathSuccesses.Value, play.DeathFailures.Value));
    }

    private const string DeathLine = "Death saves were read as boxes 12 to 14 successes and 15 to 17 failures; check them.";

    [Fact]
    public void A_ticked_death_save_box_adds_one_check_them_line_and_no_tick_adds_none()
    {
        var plain = Wotc().Select(f => f.Type == "checkbox" ? f with { Checked = false } : f).ToList();
        Assert.Null(Parse(plain).Lines);

        foreach (var box in FixtureSheets.Wotc.DeathSuccessBoxes.Concat(FixtureSheets.Wotc.DeathFailureBoxes))
            Assert.Equal([DeathLine], Parse(Ticked(plain, box, true)).Lines);

        // One line however many are ticked, and only for this layout.
        var all = FixtureSheets.Wotc.DeathSuccessBoxes.Concat(FixtureSheets.Wotc.DeathFailureBoxes).Aggregate(plain, (f, box) => Ticked(f, box, true));
        Assert.Equal([DeathLine], Parse(all).Lines);
        Assert.DoesNotContain(DeathLine, Parse(DdbParserTests.Fields(FixtureSheets.Fields2014)).Lines ?? []);
    }

    // ---- the swap guard (Review Focus 2) ----

    private static DdbSheet Abilities(string score, string mod, out List<FormField> fields)
    {
        // R95: on the owner's sheet the form's "STRmod" box holds the score and "STR" the modifier.
        fields = With(With(Wotc(), "STRmod", score), "STR", mod);
        return Parse(fields);
    }

    [Theory]
    [InlineData("18", "+4", 18, false)] // a normal sheet
    [InlineData("18", "4", 18, false)]
    [InlineData("8", "-1", 8, false)]
    [InlineData("8", "\u22121", 8, false)] // U+2212
    [InlineData("+4", "18", 18, true)] // the boxes swapped
    [InlineData("4", "18", 18, true)]
    [InlineData("\u22121", "8", 8, true)]
    [InlineData("10", "0", 10, false)]
    [InlineData("18", "", 18, false)] // a score-only box
    [InlineData("", "18", 18, true)] // only the modifier box holds the score
    // R97: a lone unsigned 1 to 30 in the score slot is the score, with no line.
    [InlineData("4", "", 4, false)]
    [InlineData("1", "", 1, false)]
    [InlineData("30", "", 30, false)]
    [InlineData("10", "  ", 10, false)]
    // R97: a lone value in the modifier slot is the score only when unsigned and 11 to 30, with the line.
    [InlineData("", "11", 11, true)]
    [InlineData("", "30", 30, true)]
    public void The_swap_guard_reads_the_score_from_whichever_box_holds_it(string score, string mod, int expected, bool fromModifier)
    {
        var sheet = Abilities(score, mod, out _);

        Assert.Equal(Read<int>.Ok(expected), sheet.Abilities[Ability.Str]);
        Assert.Equal(fromModifier, (sheet.Lines ?? []).Contains("Strength was read from the modifier box."));
    }

    [Theory]
    [InlineData("+4", "12")] // never 4
    [InlineData("18", "+5")] // the two disagree
    [InlineData("18", "12")]
    [InlineData("+4", "")] // a modifier alone is not a score
    [InlineData("0", "")] // R97: outside 1 to 30
    [InlineData("31", "")]
    [InlineData("", "+4")]
    [InlineData("", "4")] // R97: a lone value in the modifier slot under 11 is a modifier or a guess, never a score
    [InlineData("", "10")]
    [InlineData("", "0")]
    [InlineData("", "31")]
    [InlineData("", "-3")]
    [InlineData("31", "+10")]
    [InlineData("abc", "+4")]
    public void The_swap_guard_refuses_boxes_that_disagree_and_never_reads_a_modifier_as_a_score(string score, string mod)
    {
        var sheet = Abilities(score, mod, out _);

        Assert.Equal(ReadStatus.Unreadable, sheet.Abilities[Ability.Str].Status);
        Assert.NotEqual(4, sheet.Abilities[Ability.Str].Value);
        Assert.DoesNotContain("Strength was read from the modifier box.", sheet.Lines ?? []);
    }

    [Fact]
    public void All_six_scores_in_the_modifier_boxes_give_one_report_line_not_six()
    {
        // A sheet filled the other way round from the owner's (R95): the guard still reads it, with one collapsed line.
        var fields = Wotc();
        string[] mods = ["+3", "+2", "+2", "+0", "+1", "-1"];
        string[] scores = ["16", "14", "15", "10", "12", "8"];
        for (var i = 0; i < 6; i++)
            fields = With(With(fields, FixtureSheets.Wotc.Scores[i], mods[i]), FixtureSheets.Wotc.Mods[i], scores[i]);

        var sheet = Parse(fields);

        Assert.Equal([16, 14, 15, 10, 12, 8], Enum.GetValues<Ability>().Select(a => sheet.Abilities[a].Value));
        Assert.Equal(["All six ability scores were read from the modifier boxes.", DeathLine], sheet.Lines);
    }

    [Fact]
    public void Blank_ability_boxes_are_missing()
    {
        Assert.Equal(ReadStatus.Missing, Abilities("", "  ", out _).Abilities[Ability.Str].Status);
    }

    // ---- the class pattern ----

    [Theory]
    [InlineData("Fixture Fighter LVL 5", "Fixture Fighter", 5)]
    [InlineData("Fixture Fighter Level 5", "Fixture Fighter", 5)]
    [InlineData("Fixture Fighter lvl. 5", "Fixture Fighter", 5)]
    [InlineData("Fixture Fighter 5", "Fixture Fighter", 5)]
    public void The_class_pattern_accepts_lvl_level_and_a_bare_number(string text, string name, int level)
    {
        var sheet = Parse(With(Wotc(), "ClassLevel", text));
        Assert.Equal([new ClassText(name, level, null)], sheet.Classes.Value!);
    }

    [Theory]
    [InlineData("Fixture Fighter 3 / Fixture Mage LVL 2")]
    [InlineData("Fixture Fighter 3, Fixture Mage Level 2")]
    public void Several_classes_are_split_on_a_slash_or_a_comma(string text)
    {
        var sheet = Parse(With(Wotc(), "ClassLevel", text));
        Assert.Equal([new ClassText("Fixture Fighter", 3, null), new ClassText("Fixture Mage", 2, null)], sheet.Classes.Value!);
    }

    // ---- hit dice ----

    [Theory]
    [InlineData("3d8", "2d8", 8, 1)]
    [InlineData("3d8", "2", 8, 1)]
    [InlineData("3d8", "3d8", 8, 0)]
    [InlineData("5d10", "0", 10, 5)]
    public void Spent_hit_dice_are_the_total_less_the_remaining(string total, string remaining, int die, int spent)
    {
        var play = Parse(With(With(Wotc(), "HDTotal", total), "HD", remaining)).Play;
        Assert.Equal([new DieSpent(die, spent)], play.HitDiceSpent.Where(d => d.Die == die).Select(d => d with { Spent = d.Spent }));
        Assert.Equal(0, play.UnreadableSpent);
    }

    [Theory]
    [InlineData("3d8", "4d8")]
    [InlineData("3d8", "2d10")]
    [InlineData("1d8 + 2d10", "2")]
    [InlineData("many", "2")]
    public void Hit_dice_that_do_not_add_up_are_counted_as_unreadable_never_guessed(string total, string remaining)
    {
        var play = Parse(With(With(Wotc(), "HDTotal", total), "HD", remaining)).Play;
        Assert.Empty(play.HitDiceSpent);
        Assert.Equal(1, play.UnreadableSpent);
    }

    // ---- clipping (R78) ----

    [Fact]
    public void A_clipped_field_gives_a_report_line_naming_the_field_never_its_text()
    {
        var fields = Wotc().Select(f => f.Name == "Backstory" ? f with { Value = new string('B', 20_000), Clipped = true } : f).ToList();

        var sheet = Parse(fields);

        Assert.Equal(["Backstory was cut to 20,000 characters.", DeathLine], sheet.Lines);
        Assert.Equal(20_000, sheet.Texts!.Prose["backstory"].Value!.Length);
        Assert.DoesNotContain("BBBB", string.Join(' ', sheet.Lines!), StringComparison.Ordinal);
    }

    [Fact]
    public void The_union_of_every_layouts_clip_names_is_what_the_worker_is_told()
    {
        var limits = LayoutMaps.FormLimits;

        Assert.Equal(LayoutMaps.All.SelectMany(m => m.ClipFields ?? []).Distinct().Order(StringComparer.Ordinal), limits.ClipFields.Order(StringComparer.Ordinal));
        Assert.Contains("PersonalityTraits ", limits.ClipFields);
        Assert.Equal(FormLimits.Default.MaxValueChars, limits.MaxValueChars);
    }

    [Fact]
    public async Task An_over_long_free_text_field_read_through_the_real_worker_is_cut_while_an_over_long_plain_field_is_refused()
    {
        var worker = Path.Combine(AppContext.BaseDirectory, TomeStackApp.WorkerFileName);
        var long25 = new string('B', 25_000);
        var specs = FixtureSheets.FieldsWotc2014.Select(s => s.Name == "Backstory" ? s with { Text = long25 } : s).ToList();
        using var file = FixturePdfs.Write(FormPdfWriter.Write(specs, pages: 3));

        var fields = await new WorkerFormReader(worker, form: LayoutMaps.FormLimits).ReadAsync(file.Path, CancellationToken.None);
        var sheet = Parse([.. fields]);

        Assert.Equal(20_000, sheet.Texts!.Prose["backstory"].Value!.Length);
        Assert.Equal(["Backstory was cut to 20,000 characters.", DeathLine], sheet.Lines);

        var plain = FixtureSheets.FieldsWotc2014.Select(s => s.Name == "CharacterName" ? s with { Text = long25 } : s).ToList();
        using var other = FixturePdfs.Write(FormPdfWriter.Write(plain, pages: 3));
        var refused = await Assert.ThrowsAsync<ExtractionException>(() => new WorkerFormReader(worker, form: LayoutMaps.FormLimits).ReadAsync(other.Path, CancellationToken.None));
        Assert.Equal("ddb.value-too-long", refused.Code);
    }
}
