using System.IO.Compression;
using System.Text;
using TomeStack.AppService.CharacterImport;
using TomeStack.AppService.Packages;
using TomeStack.ImportWorker.Forms;
using TomeStack.ImportWorker.Tests;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Item 14 (Q14, D16e amended): what the fillable 2014 sheet brings over. Short structured details fill the Details page,
/// custom items and custom attacks; prose goes to dated session notes (R79), never to Details; a weapon row maps only the
/// parts that parse (R83). Every value is invented.
/// </summary>
public class WotcImportTests
{
    private static readonly DateOnly Day = new(2026, 10, 17);
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static List<FormField> Wotc() => DdbParserTests.Fields(FixtureSheets.FieldsWotc2014);

    private static List<FormField> With(List<FormField> fields, string name, string? value) =>
        [.. fields.Select(f => f.Name == name ? f with { Value = value } : f)];

    private static DdbSheet Parse(List<FormField> fields) => DdbParser.Parse(DdbParser.Recognise(fields)!, fields);

    private static ImportExtras Extras(List<FormField> fields, int noteRoom = Character.MaxNotes) => ImportExtras.Build(Parse(fields), Day, Now, noteRoom);

    /// <summary>The one "Import note" that holds what did not fit or parse (the fixture's prose notes come with it).</summary>
    private static SessionNote ImportNote(ImportExtras extras) =>
        Assert.Single(extras.Notes, n => n.Text.StartsWith("Import note\n\n", StringComparison.Ordinal));

    private static List<FormField> Row(List<FormField> fields, int row, string name, string bonus, string damage)
    {
        var (n, b, d) = FixtureSheets.Wotc.Weapons[row - 1];
        return With(With(With(fields, n, name), b, bonus), d, damage);
    }

    // ---- weapon rows (R83) ----

    [Fact]
    public void Two_good_weapon_rows_become_custom_attacks_and_the_blank_third_row_is_skipped()
    {
        var extras = Extras(Wotc());

        Assert.Equal(
            [("Testy Longblade", 5, "1d8+3", "slashing"), ("Testy Dagger", 4, "1d4+2", "piercing")],
            extras.Attacks.Select(a => (a.Name, a.ToHit, a.Damage!, a.DamageType!)));
        Assert.All(extras.Attacks, a => Assert.NotEqual(Guid.Empty, a.Id));
        Assert.Equal(2, extras.Attacks.Select(a => a.Id).Distinct().Count());
        Assert.All(extras.Attacks, a => Assert.Null(a.Note));
        Assert.Empty(extras.Lines);
    }

    [Theory]
    [InlineData("+5", "1d8+3 slashing", 5, "1d8+3", "slashing")]
    [InlineData("5", "2d6 FIRE", 5, "2d6", "fire")]
    [InlineData("\u22122", "1d4 - 1 Cold", -2, "1d4-1", "cold")]
    [InlineData("-20", "1d4", -20, "1d4", null)]
    [InlineData("+30", "1d4+1 Radiant damage", 30, "1d4+1", "radiant")]
    [InlineData("+3", "slashing", 3, null, "slashing")]
    public void A_weapon_row_splits_the_bonus_the_dice_and_the_damage_type(string bonus, string damage, int toHit, string? dice, string? type)
    {
        var extras = Extras(Row(Wotc(), 1, "Testy Blade", bonus, damage));

        var attack = extras.Attacks[0];
        Assert.Equal((toHit, dice, type), (attack.ToHit, attack.Damage, attack.DamageType));
        Assert.Empty(extras.Lines);
    }

    [Theory]
    [InlineData("+5", "1d8+3 sonic", "1d8+3", null, "Weapon row 1: the damage type could not be read and was left empty.")]
    [InlineData("+5", "banana", null, null, "Weapon row 1: the damage could not be read and was left empty.")]
    [InlineData("+5", "1d8+3 +", "1d8+3", null, "Weapon row 1: the damage type could not be read and was left empty.")]
    [InlineData("+5", "200d8", null, null, "Weapon row 1: the damage could not be read and was left empty.")]
    public void An_unparseable_part_is_left_null_with_a_line_naming_the_row_and_the_raw_text_goes_to_the_note(string bonus, string damage, string? dice, string? type, string line)
    {
        var extras = Extras(Row(Wotc(), 1, "Testy Blade", bonus, damage));

        var attack = extras.Attacks[0];
        Assert.Equal((5, dice, type), (attack.ToHit, attack.Damage, attack.DamageType));
        Assert.Null(attack.Note);
        Assert.Equal([line], extras.Lines);
        var note = ImportNote(extras);
        Assert.Contains("Weapon row 1", note.Text, StringComparison.Ordinal);
        Assert.Contains(damage, note.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("oops")]
    [InlineData("+31")]
    [InlineData("-21")]
    [InlineData("")]
    public void A_row_whose_to_hit_does_not_read_goes_to_the_note_only(string bonus)
    {
        var extras = Extras(Row(Wotc(), 3, "Testy Sling", bonus, "1d4 bludgeoning"));

        Assert.Equal(2, extras.Attacks.Count); // rows 1 and 2 only
        Assert.Equal(["Weapon row 3: the attack bonus could not be read, so the row is in the import note only."], extras.Lines);
        var note = ImportNote(extras);
        Assert.Contains("Testy Sling", note.Text, StringComparison.Ordinal);
        Assert.Contains("1d4 bludgeoning", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_with_a_name_over_eighty_characters_goes_to_the_note_only()
    {
        var extras = Extras(Row(Wotc(), 3, new string('N', 81), "+1", "1d4"));

        Assert.Equal(2, extras.Attacks.Count);
        Assert.Equal(["Weapon row 3: the name is longer than 80 characters, so the row is in the import note only."], extras.Lines);
        ImportNote(extras);
    }

    // ---- equipment ----

    [Fact]
    public void Equipment_lines_become_unequipped_custom_items_with_a_clear_count()
    {
        var extras = Extras(Wotc());

        Assert.Equal([("Fixture Rope Coil", 3), ("Fixture Lantern", 2), ("Fixture Waterskin", 1)], extras.Items.Select(i => (i.Name, i.Quantity)));
        Assert.All(extras.Items, i => Assert.False(i.Equipped));
        Assert.All(extras.Items, i => Assert.Null(i.Note));
        Assert.Equal(3, extras.Items.Select(i => i.Id).Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, extras.Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData("Fixture Rope (3)", "Fixture Rope", 3)]
    [InlineData("2 x Fixture Rope", "Fixture Rope", 2)]
    [InlineData("2x Fixture Rope", "Fixture Rope", 2)]
    [InlineData("Fixture Rope x4", "Fixture Rope", 4)]
    [InlineData("- Fixture Rope", "Fixture Rope", 1)]
    [InlineData("\u2022 Fixture Rope", "Fixture Rope", 1)]
    [InlineData("Fixture Rope (0)", "Fixture Rope (0)", 1)]
    [InlineData("50 ft of Fixture Rope", "50 ft of Fixture Rope", 1)]
    [InlineData("Fixture Rope", "Fixture Rope", 1)]
    public void A_leading_or_trailing_count_is_the_quantity_and_anything_else_is_the_name(string line, string name, int quantity)
    {
        var extras = Extras(With(Wotc(), "Equipment", line));

        Assert.Equal((name, quantity), (extras.Items.Single().Name, extras.Items.Single().Quantity));
    }

    [Fact]
    public void Blank_equipment_lines_are_skipped_and_a_name_over_eighty_goes_to_the_note()
    {
        var text = $"\n  \nFixture Rope\n{new string('L', 81)}\n";
        var extras = Extras(With(Wotc(), "Equipment", text));

        Assert.Equal(["Fixture Rope"], extras.Items.Select(i => i.Name));
        Assert.Equal(["1 equipment line was not added as an item and is in the import note."], extras.Lines);
        Assert.Contains(new string('L', 81), ImportNote(extras).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Equipment_past_the_custom_item_limit_is_counted_in_the_report_and_kept_in_the_note()
    {
        var text = string.Join('\n', Enumerable.Range(0, Character.MaxCustomItems + 3).Select(i => $"Fixture Thing {i:D2}"));
        var extras = Extras(With(Wotc(), "Equipment", text));

        Assert.Equal(Character.MaxCustomItems, extras.Items.Count);
        Assert.Equal(["3 equipment lines were not added as items and are in the import note."], extras.Lines);
        var note = ImportNote(extras);
        Assert.Contains("Fixture Thing 52", note.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixture Thing 49", note.Text, StringComparison.Ordinal);
    }

    // ---- languages, tools, details ----

    [Fact]
    public void Proficiencies_split_into_languages_and_tools_and_appearance_and_alignment_fill_the_details()
    {
        var details = Extras(Wotc()).Details!;

        Assert.Equal(["Common", "Fixture Tongue"], details.Languages);
        Assert.Equal(["Fixture Kit"], details.ToolsAndOther);
        Assert.Equal("Fixture Neutral", details.Alignment);
        Assert.Equal(new Appearance("25", "5 ft 8 in", "150 lb", "Fixture Green", "Fixture Pale", "Fixture Dark"), details.Appearance);
        // Prose never goes to Details (R79).
        Assert.Null(details.PersonalityTraits);
        Assert.Null(details.Ideals);
        Assert.Null(details.Bonds);
        Assert.Null(details.Flaws);
        Assert.Null(details.Backstory);
        Assert.Null(details.AlliesAndOrganisations);
    }

    [Fact]
    public void Unlabelled_proficiencies_are_split_on_lines_and_commas_into_tools_and_other()
    {
        var details = Extras(With(Wotc(), "ProficienciesLang", "Fixture Kit, Fixture Set; Fixture Tool\n- Fixture Pick\n")).Details!;

        Assert.Null(details.Languages);
        Assert.Equal(["Fixture Kit", "Fixture Set", "Fixture Tool", "Fixture Pick"], details.ToolsAndOther);
    }

    [Fact]
    public void Entries_over_eighty_characters_or_past_thirty_are_reported_and_go_to_the_note()
    {
        var many = string.Join(", ", Enumerable.Range(0, 32).Select(i => $"Fixture Lang {i:D2}"));
        var extras = Extras(With(Wotc(), "ProficienciesLang", $"Languages: {many}, {new string('X', 81)}"));

        Assert.Equal(Character.MaxDetailListEntries, extras.Details!.Languages!.Count);
        Assert.Equal(["3 language or tool entries did not fit the lists and are in the import note."], extras.Lines);
        var note = ImportNote(extras);
        Assert.Contains("Fixture Lang 31", note.Text, StringComparison.Ordinal);
        Assert.Contains(new string('X', 81), note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_detail_over_the_text_limit_goes_to_the_note_instead_of_failing_the_character()
    {
        var extras = Extras(With(Wotc(), "Hair", new string('H', Character.MaxDetailText + 1)));

        Assert.Null(extras.Details!.Appearance!.Hair);
        Assert.Equal(["Hair is longer than 2,000 characters and is in the import note instead."], extras.Lines);
        ImportNote(extras);
    }

    // ---- prose to session notes (R79) ----

    [Fact]
    public void Each_prose_field_becomes_one_dated_session_note_in_sheet_order()
    {
        var extras = Extras(Wotc());

        Assert.Equal(
            ["Personality traits", "Ideals", "Bonds", "Flaws", "Backstory", "Features and traits", "Feats and traits", "Treasure"],
            extras.Notes.Select(n => n.Text.Split('\n')[0]));
        Assert.All(extras.Notes, n => Assert.Equal((Day, Now), (n.Date, n.CreatedAt)));
        Assert.Equal(extras.Notes.Count, extras.Notes.Select(n => n.Id).Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, extras.Notes.Select(n => n.Id));
        Assert.EndsWith("Testy personality.", extras.Notes[0].Text, StringComparison.Ordinal);
        Assert.Contains("backstory", extras.Brought);
        Assert.Contains("notes", extras.Brought);
        Assert.Contains("languages", extras.Brought);
        Assert.Contains("appearance", extras.Brought);
    }

    [Fact]
    public void A_long_field_is_split_into_consecutive_notes_titled_with_the_part_and_the_count()
    {
        var words = string.Join(' ', Enumerable.Range(0, 2_400).Select(i => $"w{i:D4}"));
        var extras = Extras(With(With(Wotc(), "PersonalityTraits ", ""), "Backstory", words));

        var parts = extras.Notes.Where(n => n.Text.StartsWith("Backstory", StringComparison.Ordinal)).ToList();
        Assert.True(parts.Count >= 3);
        Assert.All(parts, n => Assert.True(n.Text.Length <= SessionNote.MaxText));
        Assert.Equal(Enumerable.Range(1, parts.Count).Select(i => $"Backstory (part {i} of {parts.Count})"), parts.Select(n => n.Text.Split('\n')[0]));
        var joined = string.Join(' ', parts.Select(n => n.Text[(n.Text.IndexOf("\n\n", StringComparison.Ordinal) + 2)..].Trim()));
        Assert.Equal(words, joined);
    }

    [Fact]
    public void Notes_past_the_room_left_are_counted_in_the_report_never_dropped_silently()
    {
        var extras = Extras(Wotc(), noteRoom: 3);

        Assert.Equal(3, extras.Notes.Count);
        Assert.Contains("5 session notes could not be added: a character holds at most 200.", extras.Lines);
    }

    [Fact]
    public void Nothing_is_added_when_the_sheet_has_none_of_it()
    {
        var bare = Wotc().Where(f => f.Name is "ClassLevel" or "Features and Traits" or "PersonalityTraits " or "CharacterName").ToList();

        var extras = Extras(bare);

        Assert.Null(extras.Details);
        Assert.Empty(extras.Items);
        Assert.Empty(extras.Attacks);
        Assert.Equal(2, extras.Notes.Count); // the two prose fields that exist
    }

    // ---- the whole import ----

    private static DdbApplyRequest Apply(Guid token, IReadOnlyList<NumberChoice>? numbers = null, bool play = false) =>
        new(token, RulesFamilies.Srd51, null, [], numbers ?? [], play, Confirm: true);

    [Fact]
    public void Applying_the_wotc_sheet_creates_a_character_with_the_v9_fields_filled_and_the_report_names_the_layout()
    {
        using var h = new DdbHarness { Fields = Wotc() };
        var read = h.Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf");
        Assert.Equal(("wotc-2014", "the fillable 2014 sheet"), (read.Layout, read.LayoutName));

        var applied = h.Temp.App.ApplyDdbImport(Apply(read.Token));
        var character = h.Temp.App.GetCharacter(applied.CharacterId).Character;

        Assert.Equal("the fillable 2014 sheet", applied.Report.LayoutName);
        Assert.Equal(2, character.CustomAttacks!.Count);
        Assert.Equal(3, character.CustomItems!.Count);
        Assert.Equal(new Currency(Cp: 5, Gp: 12), character.Currency);
        Assert.Equal(["Common", "Fixture Tongue"], character.Details!.Languages);
        Assert.Equal(8, character.Notes.Count);
        Assert.Null(character.Details.Backstory);
        Assert.DoesNotContain("languages", applied.Report.NotBroughtOver);
        Assert.DoesNotContain("toolProficiencies", applied.Report.NotBroughtOver);
        Assert.DoesNotContain("appearance", applied.Report.NotBroughtOver);
        Assert.DoesNotContain("backstory", applied.Report.NotBroughtOver);
        Assert.Contains("playerName", applied.Report.NotBroughtOver);
    }

    [Fact]
    public void A_kept_number_and_a_noted_difference_use_the_layouts_own_texts_and_the_ddb_layout_keeps_its_own()
    {
        using var h = new DdbHarness { Fields = Wotc() };

        var token = h.Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;
        var applied = h.Temp.App.ApplyDdbImport(Apply(token, [new(FieldIds.ArmorClass, NumberAction.KeepSheet), new(FieldIds.HitPoints, NumberAction.Note)]));

        var view = h.Temp.App.GetCharacter(applied.CharacterId);
        Assert.Equal("Imported from the character sheet", Assert.Single(view.Character.Overrides).Reason);
        Assert.Contains(h.Temp.App.ListGapNotes(applied.CharacterId), n => n.Text == "The character sheet showed a different number here when this character was imported.");

        Assert.Equal("Imported from D&D Beyond", TomeStackApp.ImportedOverrideReason);
    }

    [Fact]
    public void Play_state_brings_the_death_saves_and_spent_hit_dice()
    {
        using var h = new DdbHarness { Fields = Wotc() };
        var token = h.Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;

        var play = h.Temp.App.GetCharacter(h.Temp.App.ApplyDdbImport(Apply(token, play: true)).CharacterId).Character.Play;

        Assert.Equal((21, 3, 1, 2), (play.CurrentHitPoints, play.TemporaryHitPoints, play.DeathSaves.Successes, play.DeathSaves.Failures));
        Assert.Equal(1, play.HitDiceSpent.Single(d => d.Die == 8).Spent);
    }

    [Fact]
    public void The_report_carries_the_swap_clip_and_row_lines_with_labels_and_counts_only()
    {
        var fields = With(With(Wotc(), "STR", "+3"), "STRmod", "16");
        fields = [.. Row(fields, 3, "Testy Sling", "oops", "1d4").Select(f => f.Name == "Backstory" ? f with { Clipped = true } : f)];
        using var h = new DdbHarness { Fields = fields };
        var token = h.Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;

        var applied = h.Temp.App.ApplyDdbImport(Apply(token));

        Assert.Equal(
            ["Strength was read from the modifier box.", "Backstory was cut to 20,000 characters.", "Weapon row 3: the attack bonus could not be read, so the row is in the import note only."],
            applied.Report.Lines);
        Assert.DoesNotContain("Testy Sling", string.Join(' ', applied.Report.Lines!), StringComparison.Ordinal);
    }

    // ---- shares (Review Focus 6) ----

    private static string Everything(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var all = new StringBuilder();
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            all.Append(reader.ReadToEnd());
        }
        return all.ToString();
    }

    [Fact]
    public void A_marker_in_every_prose_field_is_absent_from_a_share_package_and_present_in_the_personal_backup()
    {
        const string marker = "FIXTURE-MARKER-4471";
        var fields = Wotc();
        foreach (var name in FixtureSheets.Wotc.Prose)
            fields = With(fields, name, $"Testy {marker} {name.Trim()}");
        using var h = new DdbHarness { Fields = fields };
        var token = h.Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;
        var id = h.Temp.App.ApplyDdbImport(Apply(token)).CharacterId;

        var share = Everything(h.Temp.App.ExportCharacters([id], ExportPurpose.Share).Content);
        var backup = Everything(h.Temp.App.ExportCharacters([id], ExportPurpose.Backup).Content);

        Assert.DoesNotContain(marker, share, StringComparison.Ordinal);
        Assert.Contains(marker, backup, StringComparison.Ordinal);
        Assert.DoesNotContain("Testy Player", backup, StringComparison.Ordinal);
    }
}
