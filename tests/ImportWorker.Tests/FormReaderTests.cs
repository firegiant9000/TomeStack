using TomeStack.ImportWorker.Forms;
using static TomeStack.ImportWorker.Tests.FormPdfWriter;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// The character-sheet importer's reader (<c>features/ddb-pdf-import.md</c> S1): AcroForm fields read with PdfPig, the
/// limits, and the hostile and malformed inputs of the spec's "Limits and malformed input" table. These tests call the
/// reader in the test process; <see cref="WorkerProcessTests"/> runs it in the isolated worker, as the app does. Every
/// value is invented.
/// </summary>
public class FormReaderTests
{
    /// <summary>The sheet limits of the spec: 20 MB and 50 pages.</summary>
    private static readonly ExtractionLimits Sheet = new() { MaxBytes = 20L << 20, MaxPages = 50 };

    private static IReadOnlyList<FormField> Read(byte[] pdf, ExtractionLimits? limits = null, FormLimits? form = null)
    {
        using var file = FixturePdfs.Write(pdf);
        return AcroFormReader.Read(file.Path, limits ?? Sheet, form ?? FormLimits.Default);
    }

    private static ExtractionException Refused(byte[] pdf, ExtractionLimits? limits = null, FormLimits? form = null) =>
        Assert.Throws<ExtractionException>(() => Read(pdf, limits, form));

    [Fact]
    public void A_hand_written_form_reads_every_text_field_and_checkbox_with_its_full_name_type_page_and_value()
    {
        var fields = Read(Write(
        [
            new("fixture.name", "Testy McFixture"),
            new("fixture.class", "Fixture Fighter 3", Page: 1),
            new("fixture.empty"),
            new("fixture.inspired", Checked: true, Page: 2),
            new("fixture.rested", Checked: false, Page: 2),
            new("fixture.ünïcode", "Fixture Ünïcode – value"),
        ]));

        Assert.Equal(
        [
            new FormField("fixture.name", "text", 1, "Testy McFixture"),
            new FormField("fixture.class", "text", 1, "Fixture Fighter 3"),
            new FormField("fixture.empty", "text", 1),
            new FormField("fixture.inspired", "checkbox", 2, Checked: true, OnState: "Yes"),
            new FormField("fixture.rested", "checkbox", 2, Checked: false, OnState: "Yes"),
            new FormField("fixture.ünïcode", "text", 1, "Fixture Ünïcode – value"),
        ], fields);
    }

    [Fact]
    public void Nested_fields_get_the_period_joined_full_name()
    {
        var fields = Read(Write(
        [
            new("fixture.spells.0.name", "Fixture Ember Lance"),
            new("fixture.spells.1.name", "Fixture Frost Veil"),
            new("fixture.spells.1.prepared", Checked: true),
            new("fixture.top", "Fixture Top"),
        ], nested: true));

        Assert.Equal(["fixture.spells.0.name", "fixture.spells.1.name", "fixture.spells.1.prepared", "fixture.top"], fields.Select(f => f.Name));
        Assert.Equal("Fixture Frost Veil", fields[1].Value);
        Assert.True(fields[2].Checked);
    }

    [Fact]
    public void A_button_group_whose_widgets_have_no_name_is_listed_once_per_widget_under_the_fields_name()
    {
        var fields = Read(Write([new("fixture.name", "Testy McFixture")], groups:
        [
            new("fixture.saves", ["FixtureA", "FixtureB"], On: 1),
            new("fixture.pick", ["FixtureX", "FixtureY", "FixtureZ"], On: 2, Radio: true),
        ]));

        Assert.Equal(
        [
            new FormField("fixture.name", "text", 1, "Testy McFixture"),
            new FormField("fixture.saves", "checkbox", 1, Checked: false, OnState: "FixtureA"),
            new FormField("fixture.saves", "checkbox", 1, Checked: true, OnState: "FixtureB"),
            new FormField("fixture.pick", "radio", 1, Checked: false, OnState: "FixtureX"),
            new FormField("fixture.pick", "radio", 1, Checked: false, OnState: "FixtureY"),
            new FormField("fixture.pick", "radio", 1, Checked: true, OnState: "FixtureZ"),
        ], fields);
    }

    [Fact]
    public void A_file_another_program_holds_open_is_pdf_unreadable_not_a_crash()
    {
        using var file = FixturePdfs.Write(Write([new("fixture.name", "Testy McFixture")]));
        using var held = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.None);

        var refused = Assert.Throws<ExtractionException>(() => AcroFormReader.Read(file.Path, Sheet, FormLimits.Default));

        Assert.Equal("pdf.unreadable", refused.Code);
        Assert.DoesNotContain(file.Path, refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_checkbox_with_another_on_state_is_read_as_checked()
    {
        var fields = Read(Write([new("fixture.on", Checked: true), new("fixture.off", Checked: false)], onState: "FixtureOn"));

        Assert.Equal((true, "FixtureOn"), (fields[0].Checked, fields[0].OnState));
        Assert.Equal(false, fields[1].Checked);
    }

    [Fact]
    public void A_combo_box_reads_its_selected_option()
    {
        var field = Assert.Single(Read(Write([new("fixture.choice", Selected: ["Fixture Option"])])));

        Assert.Equal("combo", field.Type);
        Assert.Equal(["Fixture Option"], field.Selected!);
        Assert.Null(field.Value);
    }

    // ---- S0 finding: a real export had 874 named widget fields but no /AcroForm in its catalog ----

    private static readonly FormSpec[] WidgetSheet =
    [
        new("fixture.name", "Testy McFixture"),
        new("fixture.class", "Fixture Arcanist 3", Page: 1),
        new("fixture.empty"),
        new("fixture.inspired", Checked: true, Page: 2),
        new("fixture.rested", Checked: false, Page: 2),
        new("fixture.ünïcode", "Fixture Ünïcode – value"),
        new("fixture.choice", Selected: ["Fixture Option"]),
    ];

    [Fact]
    public void A_form_whose_catalog_has_no_AcroForm_is_read_from_its_page_widgets_exactly_as_with_one()
    {
        var withForm = Read(Write(WidgetSheet));
        var widgetsOnly = Read(Write(WidgetSheet, withAcroForm: false));

        // Widgets come in page order, the form in its own; the parser keys by name, so the order does not matter.
        static IEnumerable<FormField> ByName(IEnumerable<FormField> fields) => fields.Select(f => f with { Selected = null }).OrderBy(f => f.Name, StringComparer.Ordinal);
        Assert.Equal(ByName(withForm), ByName(widgetsOnly));
        Assert.Equal(["Fixture Option"], widgetsOnly.Single(f => f.Name == "fixture.choice").Selected!);
    }

    [Fact]
    public void Nested_widget_fields_without_AcroForm_get_the_period_joined_full_name_from_their_parents()
    {
        var fields = Read(Write(
        [
            new("fixture.spells.0.name", "Fixture Frost Ring"),
            new("fixture.spells.0.prepared", Checked: true),
            new("fixture.top", "Fixture Top"),
        ], nested: true, withAcroForm: false));

        Assert.Equal(["fixture.spells.0.name", "fixture.spells.0.prepared", "fixture.top"], fields.Select(f => f.Name));
        Assert.True(fields[1].Checked);
    }

    [Fact]
    public void The_limits_hold_for_widget_fields_too()
    {
        var tooLong = new string('F', 50);
        var pdf = Write([new("fixture.a", tooLong), new("fixture.b", tooLong), new("fixture.c", tooLong)], withAcroForm: false);

        Assert.Equal("ddb.too-many-fields", Refused(pdf, form: new FormLimits { MaxFields = 2, MaxValueChars = 10 }).Code);
        Assert.Equal("ddb.value-too-long", Refused(pdf, form: new FormLimits { MaxValueChars = 10 }).Code);
        Assert.Equal("ddb.value-too-long", Refused(pdf, form: new FormLimits { MaxTotalValueChars = 100 }).Code);
    }

    [Fact]
    public void Without_AcroForm_a_group_takes_its_type_and_value_from_the_parent_and_its_on_state_from_the_widget_that_is_on()
    {
        var fields = Read(Write([new("fixture.name", "Testy McFixture")], withAcroForm: false, groups:
        [
            new("fixture.saves", ["FixtureA", "FixtureB"], On: 1),
            new("fixture.pick", ["FixtureX", "FixtureY", "FixtureZ"], On: 2, Radio: true),
            new("fixture.none", ["FixtureP", "FixtureQ"], On: null, Radio: true),
        ]));

        // Widgets that share a name are one field here (a checkbox is on if any of its widgets is).
        Assert.Equal(
        [
            new FormField("fixture.name", "text", 1, "Testy McFixture"),
            new FormField("fixture.saves", "checkbox", 1, Checked: true, OnState: "FixtureB"),
            new FormField("fixture.pick", "radio", 1, Checked: true, OnState: "FixtureZ"),
            new FormField("fixture.none", "radio", 1, Checked: false, OnState: "FixtureP"),
        ], fields);
    }

    /// <summary>A catalog without <c>/AcroForm</c> and one page whose <c>/Annots</c> is <paramref name="annots"/>, then the extra objects (from 4).</summary>
    private static byte[] OnePage(string annots, params string[] extra) => FixturePdfs.Raw(
    [
        .. new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [{annots}] >>" }
            .Concat(extra).Select(o => System.Text.Encoding.ASCII.GetBytes(o)),
    ]);

    [Fact]
    public void One_widget_listed_many_times_is_read_once_and_past_the_annotation_cap_is_refused()
    {
        const string widget = "<< /Type /Annot /Subtype /Widget /T (fixture.a) /FT /Tx /V (Fixture) /P 3 0 R /Rect [0 0 1 1] >>";

        var fields = Read(OnePage(string.Join(' ', Enumerable.Repeat("4 0 R", 5_000)), widget));
        Assert.Equal([new FormField("fixture.a", "text", 1, "Fixture")], fields);

        // Every entry counts toward the cap (four times the field limit), repeated or not.
        Assert.Equal("ddb.too-many-fields", Refused(OnePage(string.Join(' ', Enumerable.Repeat("4 0 R", 4 * FormLimits.Default.MaxFields + 1)), widget)).Code);
    }

    [Fact]
    public void A_flood_of_annotations_is_ddb_too_many_fields_even_when_none_is_a_named_widget()
    {
        var pdf = OnePage(string.Join(' ', Enumerable.Repeat("<< /Subtype /Link /Rect [0 0 1 1] >>", 9_000)));

        Assert.Equal("ddb.too-many-fields", Refused(pdf).Code);
    }

    [Fact]
    public void A_parent_loop_stops_the_name_walk()
    {
        var fields = Read(OnePage("4 0 R",
            "<< /Type /Annot /Subtype /Widget /T (fixture.a) /FT /Tx /V (Fixture) /Parent 5 0 R /P 3 0 R /Rect [0 0 1 1] >>",
            "<< /T (fixture.p) /Kids [4 0 R] /Parent 4 0 R >>"));

        Assert.Equal(["fixture.p.fixture.a"], fields.Select(f => f.Name));
    }

    [Fact]
    public void A_pdf_without_a_form_is_refused_with_ddb_no_form_fields()
    {
        var refused = Refused(FixturePdfs.Pages(2));
        Assert.Equal("ddb.no-form-fields", refused.Code);
        Assert.Contains("Export the sheet again", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_form_with_only_an_xfa_stream_and_no_terminal_fields_is_refused_with_ddb_no_form_fields()
    {
        var xfa = "<xdp:xdp xmlns:xdp=\"http://ns.adobe.com/xdp/\"><template/></xdp:xdp>"u8.ToArray();
        var pdf = FixturePdfs.Raw(
        [
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [] /XFA 4 0 R >> >>"u8.ToArray(),
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"u8.ToArray(),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>"u8.ToArray(),
            [.. System.Text.Encoding.ASCII.GetBytes($"<< /Length {xfa.Length} >>\nstream\n"), .. xfa, .. "\nendstream"u8.ToArray()],
        ]);

        Assert.Equal("ddb.no-form-fields", Refused(pdf).Code);
    }

    [Fact]
    public void More_than_the_field_limit_is_refused_with_ddb_too_many_fields_before_values_are_held()
    {
        // Every value is also over the value limit: the count is checked first, before any value is read.
        var tooLong = new string('F', 50);
        var pdf = Write([new("fixture.a", tooLong), new("fixture.b", tooLong), new("fixture.c", tooLong)]);

        var refused = Refused(pdf, form: new FormLimits { MaxFields = 2, MaxValueChars = 10 });

        Assert.Equal("ddb.too-many-fields", refused.Code);
        Assert.Equal(3, Read(pdf, form: new FormLimits { MaxFields = 3 }).Count);
    }

    [Fact]
    public void A_value_over_the_length_limit_is_refused_with_ddb_value_too_long()
    {
        const string value = "Fixture value that is too long";
        var refused = Refused(Write([new("fixture.long", value)]), form: new FormLimits { MaxValueChars = value.Length - 1 });

        Assert.Equal("ddb.value-too-long", refused.Code);
        Assert.DoesNotContain("Fixture value", refused.Message, StringComparison.Ordinal);
        Assert.Single(Read(Write([new("fixture.long", value)]), form: new FormLimits { MaxValueChars = value.Length }));
    }

    [Fact]
    public void A_name_over_the_length_limit_is_refused_with_ddb_value_too_long()
    {
        var name = "fixture." + new string('n', 40);
        Assert.Equal("ddb.value-too-long", Refused(Write([new(name, "Fixture")]), form: new FormLimits { MaxValueChars = 40 }).Code);
    }

    [Fact]
    public void Values_over_the_total_limit_are_refused_with_ddb_value_too_long()
    {
        // Names count toward the total too: 2 × (9 + 20) = 58 characters.
        var pdf = Write([new("fixture.a", new string('F', 20)), new("fixture.b", new string('G', 20))]);

        Assert.Equal("ddb.value-too-long", Refused(pdf, form: new FormLimits { MaxTotalValueChars = 57 }).Code);
        Assert.Equal(2, Read(pdf, form: new FormLimits { MaxTotalValueChars = 58 }).Count);
    }

    [Fact]
    public void A_pdf_with_javascript_reads_its_fields_and_runs_nothing()
    {
        // The script would write a marker file if anything ran it. PdfPig has no script engine (ADR-009).
        var marker = Path.Combine(Path.GetTempPath(), "tomestack-import-tests", $"fixture-marker-{Guid.NewGuid():N}.txt");
        var script = $"var f = this.getField('fixture.name'); util.writeFile('{marker.Replace("\\", "/", StringComparison.Ordinal)}', 'Fixture');";

        var fields = Read(Write([new("fixture.name", "Testy McFixture")], withJavaScript: true, javaScript: script));

        Assert.Equal("Testy McFixture", Assert.Single(fields).Value);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void An_encrypted_form_is_refused_with_pdf_encrypted()
    {
        Assert.Equal("pdf.encrypted", Refused(FixturePdfs.Encrypted()).Code);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(2)]
    [InlineData(4.0 / 3)]
    [InlineData(1.05)]
    public void A_truncated_form_is_pdf_unreadable_never_invented_fields(double divisor)
    {
        var whole = Write([new("fixture.name", "Testy McFixture"), new("fixture.class", "Fixture Fighter 3")]);
        var refused = Refused(whole[..(int)(whole.Length / divisor)]);
        Assert.Equal("pdf.unreadable", refused.Code);
    }

    [Fact]
    public void More_than_fifty_pages_is_pdf_too_many_pages_before_any_field_is_read()
    {
        // The fields are also over the field limit: the page count is checked first.
        var pdf = Write([new("fixture.a", Page: 1), new("fixture.b", Page: 51)], pages: 51);

        var refused = Refused(pdf, form: new FormLimits { MaxFields = 1 });

        Assert.Equal("pdf.too-many-pages", refused.Code);
        Assert.Equal(2, Read(pdf, Sheet with { MaxPages = 51 }).Count);
    }

    [Fact]
    public void A_file_over_the_size_limit_is_pdf_too_large()
    {
        var pdf = Write([new("fixture.name", "Testy McFixture")]);
        Assert.Equal("pdf.too-large", Refused(pdf, Sheet with { MaxBytes = pdf.Length - 1 }).Code);
    }

    [Fact]
    public void The_fixture_sheets_read_exactly_as_generated()
    {
        foreach (var (pdf, specs) in new[] { (FixtureSheets.Sheet2014(), FixtureSheets.Fields2014), (FixtureSheets.Sheet2024(), FixtureSheets.Fields2024) })
        {
            var expected = specs.Select(s => s.Checked is { } on
                ? new FormField(s.Name, "checkbox", s.Page, Checked: on, OnState: "Yes")
                : new FormField(s.Name, "text", s.Page, s.Text));
            Assert.Equal(expected, Read(pdf));
        }
    }

    [Fact]
    public void The_committed_fixture_sheet_has_exactly_the_generators_fields()
    {
        // Regenerate with TOMESTACK_WRITE_FIXTURES=1 (writes tests/RulesFixtures/pdf/fixture-ddb-sheet.pdf). The field names
        // are the ddb-2014 layout map's (S0); regenerate when the map or the generator changes.
        var generated = FixtureSheets.Sheet2014();
        if (Environment.GetEnvironmentVariable("TOMESTACK_WRITE_FIXTURES") == "1")
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(repo.FullName, "TomeStack.slnx")))
                repo = repo.Parent!;
            File.WriteAllBytes(Path.Combine(repo.FullName, "tests", "RulesFixtures", "pdf", "fixture-ddb-sheet.pdf"), generated);
        }
        Assert.Equal(Read(generated), AcroFormReader.Read(FixtureSheets.CommittedPath, Sheet, FormLimits.Default));
    }

    [Fact]
    public void A_file_that_is_not_a_pdf_is_pdf_not_a_pdf()
    {
        Assert.Equal("pdf.not-a-pdf", Refused("Fixture: not a PDF"u8.ToArray()).Code);
    }
}
