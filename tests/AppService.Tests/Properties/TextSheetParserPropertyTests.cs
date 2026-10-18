using System.Diagnostics;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TomeStack.AppService.CharacterImport;

namespace TomeStack.AppService.Tests.Properties;

/// <summary>
/// Item 19a: the text parser is total and bounded in time over any input. Text is generated from the shapes it reads (labels,
/// headings, lists, markup, coins, ability lines) mixed with arbitrary characters, control characters and lone surrogates
/// included; none of it is anyone's real notes.
/// </summary>
public class TextSheetParserPropertyTests
{
    private static readonly string[] Shaped =
    [
        "", " ", "\t", "Name: Testy", "Class: Fixture Arcanist 3 / Fixture Chanter 2", "Class: ///", "Level: 99", "Level: 0", "HP: 33/44", "HP: /", "AC: -5",
        "Speed: 30 ft.", "Speed: 999999", "Hit Dice: 3d8 + 2d6", "STR 10 DEX 12 CON 14 INT 8 WIS 13 CHA 15", "str: 10 (+0)", "Strength 99", "Saves: Str, Int",
        "Skills: Stealth +5 (expertise), Fixture Juggling", "Spells", "Cantrips:", "1st: Fixture A; Fixture B", "9th (1 slot)", "Equipment", "Features", "Feats:",
        "# Title", "## Spells", "### Fixture Knack", "**Class:** Fixture A 1", "__Species__: Fixture B", "- item", "* item", "1. item", "• item", "  indented",
        "15gp 3sp", "Gold: 5", "99999999 gp", "Fixture Knack: a description", "Fixture Knack:", "(", ")", ",,,;;;", "(((", "[[[", ":", "::", "---", "***",
        "Languages: a, b", "Tools:", "Proficiencies", "Backstory:", "a, b, c, d, e, f", "x".PadRight(2_100, 'x'), "\U0001F600", "\ud800", "\udc00",
        "ſtr 10", "ſpeed 30", "5 ſp", "ſp: 5", "Kelvin 5", "SKills: Stealth", "ſtr: 10 Kon 12", "Hſ: 5",
    ];

    private static Gen<string> Chars { get; } =
        from length in Gen.Frequency((8, Gen.Choose(0, 40)), (1, Gen.Choose(0, 3_000)))
        from chars in Gen.Frequency((6, Gen.Choose(0x20, 0x7E)), (2, Gen.Choose(0, 0x1F)), (1, Gen.Choose(0x80, 0xFFFF))).Select(c => (char)c).ArrayOf(length)
        select new string(chars);

    private static Gen<string> Line { get; } = Gen.Frequency((6, Gen.Elements(Shaped)), (2, Chars), (1, Gen.Elements(Shaped).SelectMany(a => Gen.Elements(Shaped).Select(b => a + b))));

    private static Gen<string> Texts { get; } =
        from count in Gen.Frequency((6, Gen.Choose(0, 30)), (1, Gen.Choose(0, 400)))
        from lines in Line.ArrayOf(count)
        from newline in Gen.Elements("\n", "\r\n", "\r")
        select string.Join(newline, lines);

    [Property(MaxTest = 300)]
    public Property The_parser_never_throws_and_returns_a_sheet_or_a_coded_refusal() =>
        Prop.ForAll(Texts.ToArbitrary(), text =>
        {
            var result = TextSheetParser.Parse(text);
            Assert.True((result.Sheet is null) != (result.Refusal is null));
            if (result.Refusal is { } refusal)
                Assert.StartsWith("ddb.text-", refusal.Code, StringComparison.Ordinal);
            _ = result.ToString();
        });

    [Property(MaxTest = 300)]
    public Property Not_read_lines_are_in_order_inside_the_text_and_never_longer_than_2000() =>
        Prop.ForAll(Texts.ToArbitrary(), text =>
        {
            var sheet = TextSheetParser.Parse(text).Sheet;
            if (sheet is null)
                return;
            var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).Length;
            var numbers = (sheet.NotRead ?? []).Select(n => n.Line).ToList();
            Assert.Equal(numbers.Order().ToList(), numbers);
            Assert.Equal(numbers.Distinct().Count(), numbers.Count);
            Assert.All(numbers, n => Assert.InRange(n, 1, lines));
            Assert.All(sheet.NotRead ?? [], n => Assert.True(n.Text.Length <= TextSheetParser.MaxLineChars));
        });

    [Property(MaxTest = 200)]
    public Property Parsing_is_deterministic_and_the_sheet_prints_no_text() =>
        Prop.ForAll(Texts.ToArbitrary(), text =>
        {
            var one = TextSheetParser.Parse(text).Sheet;
            var two = TextSheetParser.Parse(text).Sheet;
            Assert.Equal(one?.ToString(), two?.ToString());
            Assert.Equal(one?.NotRead?.Select(n => (n.Line, n.Text)), two?.NotRead?.Select(n => (n.Line, n.Text)));
            if (one is not null)
                Assert.DoesNotContain("Fixture", one.ToString(), StringComparison.Ordinal);
        });

    [Property(MaxTest = 40)]
    public Property Any_text_within_the_caps_is_read_within_the_budget() =>
        Prop.ForAll(Texts.ToArbitrary(), text =>
        {
            var clock = Stopwatch.StartNew();
            _ = TextSheetParser.Parse(text);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), "a text within the caps took two seconds or more");
        });

    [Property(MaxTest = 100)]
    public Property Text_is_kept_only_in_the_sheets_values_never_in_the_refusal() =>
        Prop.ForAll(Gen.Choose(262_145, 300_000).ToArbitrary(), size =>
        {
            var result = TextSheetParser.Parse(new string('Q', size));
            Assert.Equal("ddb.text-too-large", result.Refusal!.Code);
            Assert.DoesNotContain("Q", result.Refusal.Message, StringComparison.Ordinal);
        });
}
