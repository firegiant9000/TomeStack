using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TomeStack.AppService.Exports;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

public sealed class HtmlSheetTests
{
    private const string Evil = "\"><img src=x onerror=alert(1)><script>x</script>&'";

    private static readonly ContentReference Ref = new(Guid.Empty, Guid.Empty);
    private static readonly HtmlSheetExtras NoExtras = new([], new DeathSaves());

    private static SheetExport Sample(Func<string, string>? t = null)
    {
        t ??= s => s;
        return new SheetExport(
            SheetExport.FormatName, SheetExport.CurrentFormatVersion, SheetPurpose.Personal, new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero),
            new SheetCharacter(t("Mira Vale"), "srd-5.2.1", 3, [new SheetClass(t("Wayfarer"), 3, 8, t("Lantern Path"), Ref, "Homebrew", Ref)]),
            [new SheetAbility("str", 10, 0, 0, SheetProficiency.None), new SheetAbility("dex", 16, 3, 5, SheetProficiency.Proficient)],
            [new SheetSkill("stealth", t("Stealth"), "dex", 5, SheetProficiency.Expertise)],
            [new SheetField(FieldIds.ArmorClass, t("Armor Class"), 15, AutomationStatus.Automatic, false), new SheetField(FieldIds.Initiative, t("Initiative"), 3, AutomationStatus.Automatic, false)],
            new SheetHitPoints(24, 20, 2),
            [new SheetHitDice(8, 3, 1)],
            [new SheetSlots(1, 4, 1)],
            null,
            [new SheetCaster(t("Wayfarer"), "int", 5, 13, 3, "prepared", "spellSlots",
                [new SheetSpell(t("Lantern Spark"), 1, true, t("evocation"), "1 action", "60 feet", "V, S", "Instant", false, false, t("A small flare."), t("The flare lights the way."), Ref, t("Homebrew"))],
                Ref, t("Homebrew"))],
            [new SheetResource(t("Trail Sense"), t("Trailcraft"), 3, 1, [t("long rest: all")], Ref, t("Homebrew"))],
            [new SheetAttack(t("Walking Staff"), 5, "1d6+3", "1d8+3", t("bludgeoning"), [t("versatile")], t("5 ft"), true, Ref, t("Homebrew"))],
            [new SheetFeature(t("Map Reader"), t("class"), t("Reads maps."), AutomationStatus.Reference, [t("You always know north.")], Ref, t("Homebrew"))],
            [new SheetToggle(t("Lantern lit"), t("Lantern Path"), true, Ref, t("Homebrew"))],
            [new SheetScale(t("Trail dice"), t("Wayfarer"), 2, Ref, t("Homebrew"))],
            new SheetConditions([t("Prone")], 1),
            [new SheetDropped(t("Outside Book"), t("Outside Press"), 2)],
            [new SheetNotice(t("Homebrew"), t("Me"), t("CC-BY-4.0"), true, t("Credit line."), t("Changed for tests."), false)]);
    }

    private static HtmlSheetExtras Extras(params SessionNote[] notes) => new(notes, new DeathSaves(2, 1));

    private static SessionNote Note(string date, string text, int minutes = 0) =>
        new(Guid.NewGuid(), DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), text, new DateTimeOffset(2026, 11, 1, 0, minutes, 0, TimeSpan.Zero));

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static Match[] Find(string input, string pattern) =>
        [.. Regex.Matches(input, pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Cast<Match>()];

    private static (int Total, int Checked) BoxesWith(string html, string labelPrefix)
    {
        var matches = Find(html, "<input type=\"checkbox\" aria-label=\"" + Regex.Escape(labelPrefix) + "[^\"]*\"( checked)?>");
        return (matches.Length, matches.Count(m => m.Groups[1].Success));
    }

    private static string Wrap(string body) =>
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>t</title></head><body><main>" + body + "</main></body></html>";

    [Theory]
    [InlineData("<script>x</script>")]
    [InlineData("<p onclick=\"x\">a</p>")]
    [InlineData("<img src=\"a\">")]
    [InlineData("<a href=\"http://x\">a</a>")]
    [InlineData("<a href=\"data:text/html,x\">a</a>")]
    [InlineData("<p style=\"x\">a</p>")]
    [InlineData("<iframe></iframe>")]
    [InlineData("<object></object>")]
    [InlineData("<embed>")]
    [InlineData("<form></form>")]
    [InlineData("<base href=\"#\">")]
    [InlineData("<link rel=\"x\">")]
    [InlineData("<svg></svg>")]
    [InlineData("<math></math>")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0\">")]
    [InlineData("<style>@import \"x\";</style>")]
    [InlineData("<style>a{background:url(x)}</style>")]
    [InlineData("<SCRIPT>x</SCRIPT>")]
    [InlineData("<p/onclick=x>a</p>")]
    [InlineData("<script")]
    [InlineData("<p title=\">\" onclick=x>a</p>")]
    [InlineData("<p a=\"1\"onclick=x>a</p>")]
    [InlineData("<p title=\"unclosed onclick=x>a</p>")]
    public void Validate_rejects_each_forbidden_construct(string fragment)
    {
        Assert.NotEmpty(HtmlSheet.Validate(Wrap(fragment)));
    }

    [Fact]
    public void Validate_accepts_dangerous_words_in_text()
    {
        Assert.Empty(HtmlSheet.Validate(Wrap("<p>javascript:alert(1) onerror=1 &lt;script&gt;</p>")));
        Assert.Empty(HtmlSheet.Validate(Wrap("<a href=\"#s1\">a</a>")));
    }

    [Fact]
    public void Every_string_field_is_encoded()
    {
        var html = HtmlSheet.Render(Sample(s => Evil + s), Extras(Note("2026-11-01", Evil)));

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Evil, html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Empty(HtmlSheet.Validate(html));
    }

    [Fact]
    public void Csp_hash_matches_the_one_style()
    {
        var html = HtmlSheet.Render(Sample(), Extras());

        Assert.Equal(1, Count(html, "<style>"));
        var style = Find(html, "<style>(.*?)</style>").Single().Groups[1].Value;
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(style)));
        var csp = Find(html, "<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]*)\">").Single().Groups[1].Value;
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("style-src 'sha256-" + hash + "'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public void Spent_slots_and_pools_are_pre_ticked()
    {
        var html = HtmlSheet.Render(Sample() with { Resources = [], HitDice = [] }, Extras());

        Assert.Equal((4, 1), BoxesWith(html, "Level 1 slot "));
        Assert.Equal((3, 2), BoxesWith(html, "Death save success "));
        Assert.Equal((3, 1), BoxesWith(html, "Death save failure "));
    }

    [Fact]
    public void Pool_boxes_are_bounded()
    {
        string Resources(int? maximum, int spent)
        {
            var html = HtmlSheet.Render(Sample() with { Resources = [new SheetResource("Pool", "Pool", maximum, spent, [], Ref, "S")], HitDice = [] }, Extras());
            var start = html.IndexOf("<h2 id=\"s", StringComparison.Ordinal);
            var at = html.IndexOf(">Resources</h2>", start, StringComparison.Ordinal);
            return html[at..html.IndexOf("</section>", at, StringComparison.Ordinal)];
        }

        Assert.Equal((3, 3), BoxesWith(Resources(3, 5), "Pool "));
        var none = Resources(0, 0);
        Assert.Equal(0, Count(none, "<input"));
        var big = Resources(21, 5);
        Assert.Equal(0, Count(big, "type=\"checkbox\""));
        Assert.Contains("Spent 5 of 21", big, StringComparison.Ordinal);
        Assert.Equal(1, Count(big, "<input type=\"number\""));
        var unbounded = Resources(null, 4);
        Assert.Contains("no fixed maximum", unbounded, StringComparison.Ordinal);
        Assert.Equal(0, Count(unbounded, "<input"));
    }

    [Fact]
    public void Long_texts_sit_in_details()
    {
        var html = HtmlSheet.Render(Sample(), Extras());

        Assert.Matches(new Regex("<details><summary>Lantern Spark[^<]*</summary>\n.*The flare lights the way\\..*?</details>", RegexOptions.Singleline, TimeSpan.FromSeconds(1)), html);
        Assert.Matches(new Regex("<details><summary>Map Reader</summary>\n.*You always know north\\..*?</details>", RegexOptions.Singleline, TimeSpan.FromSeconds(1)), html);
    }

    [Fact]
    public void Notes_render_only_when_given()
    {
        var withNotes = HtmlSheet.Render(Sample(), Extras(Note("2026-10-01", "older note"), Note("2026-11-01", "newer note")));
        Assert.Contains("Session notes", withNotes, StringComparison.Ordinal);
        Assert.True(withNotes.IndexOf("newer note", StringComparison.Ordinal) < withNotes.IndexOf("older note", StringComparison.Ordinal));

        Assert.DoesNotContain("Session notes", HtmlSheet.Render(Sample(), Extras()), StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_sections_are_left_out()
    {
        var empty = Sample() with
        {
            Spellcasting = [], Slots = [], PactSlots = null, Resources = [], HitDice = [], Toggles = [], Scales = [], Attacks = [], Features = [],
        };
        var html = HtmlSheet.Render(empty, Extras());

        Assert.DoesNotContain("Spellcasting</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Resources</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Attacks</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Features</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Session notes", html, StringComparison.Ordinal);
        Assert.Empty(HtmlSheet.Validate(html));
        Assert.Equal(1, Count(html, "<h1>"));
    }

    [Fact]
    public void Non_ascii_text_survives()
    {
        var html = HtmlSheet.Render(Sample(s => s == "Mira Vale" ? "Zoë Ærin" : s), Extras());

        Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
        Assert.True(html.Contains("Zoë", StringComparison.Ordinal) || html.Contains("Zo&#235;", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_checkbox_and_input_has_a_name_for_screen_readers()
    {
        var html = HtmlSheet.Render(Sample() with { Resources = [new SheetResource("Big", "Big", 30, 2, [], Ref, "S")] }, Extras(Note("2026-11-01", "n")));

        var inputs = Find(html, "<input[^>]*>");
        Assert.NotEmpty(inputs);
        foreach (var input in inputs)
            Assert.Contains("aria-label=\"", input.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_is_deterministic()
    {
        Assert.Equal(HtmlSheet.Render(Sample(), Extras(Note("2026-11-01", "n"))), HtmlSheet.Render(Sample(), Extras(Note("2026-11-01", "n"))));
    }
}
