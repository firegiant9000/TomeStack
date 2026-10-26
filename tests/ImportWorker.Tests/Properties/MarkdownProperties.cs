using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TomeStack.ImportWorker.Extraction;

namespace TomeStack.ImportWorker.Tests.Properties;

/// <summary>Lab Task 12 (L7): the Markdown extractor reads untrusted bytes, so it is deterministic and never throws anything but a coded failure.</summary>
public class MarkdownProperties
{
    private static readonly string[] Fragments =
    [
        "# ", "## ", "#### ", "##### ", "| ", " | ", "|---|", "|:-:|--:|", "\\|", "**", "__", "*", "_", "`", "```", "~~~", "~~", "- ", "* ", "+ ", "1. ", "2) ",
        "<!-- page: 3 -->", "<!-- page: 1 -->", "<!--", "-->", "<script>", "</script", "<style>", "</style>", "<b>", "</b>", "<", ">", "<img src=x", "![a](b)", "[x](y)", "](", "[", "]", "(", ")",
        "---", "...", "\n", "\n", "\n\n", "\r\n", "\r", " ", "\t", "\0", "text", "Fixture Pool.",
        "\u00E9", "\u00A0", "\u00AD", "\u2010", "\uFB01", "\uFEFF", "\uD83D\uDE00",
        // Bare words and look-alikes that stripping can leave next to a '<'.
        "script", "style", "!--", "<<", "<scr<", "\uFF1C", "\uFF1E", "&lt;", "&gt;",
    ];

    private static Gen<string> Document { get; } =
        Gen.ListOf(Gen.Elements(Fragments)).Select(parts => string.Concat(parts));

    private static Gen<byte[]> RawBytes { get; } =
        Gen.ListOf(Gen.Choose(0, 255).Select(i => (byte)i)).Select(bytes => bytes.ToArray());

    private static string Serialise(IReadOnlyList<ExtractedPage> pages) => JsonSerializer.Serialize(pages);

    /// <summary>The pages, or the code of the failure; any other exception escapes and fails the property.</summary>
    private static (string? Code, string Pages) Outcome(byte[] bytes)
    {
        try
        {
            return (null, Serialise(MarkdownExtractor.Parse(bytes)));
        }
        catch (ExtractionException ex) when (ex.Code.StartsWith("markdown.", StringComparison.Ordinal))
        {
            return (ex.Code, "");
        }
    }

    /// <summary>True when the text holds a '&lt;' followed by an ASCII letter, '!', '?' or '/': something a reader of the text could take for a tag.</summary>
    internal static bool HoldsTagOpener(string text)
    {
        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] == '<' && (char.IsAsciiLetter(text[i + 1]) || text[i + 1] is '!' or '?' or '/'))
                return true;
        }
        return false;
    }

    internal static IEnumerable<string> AllText(IEnumerable<ExtractedPage> pages) =>
        pages.SelectMany(p => new[] { p.Text }.Concat((p.Blocks ?? []).SelectMany(b => new[] { b.Text }.Concat((b.Lines ?? []).Select(l => l.Text)))));

    [Property(MaxTest = 300)]
    public Property Same_bytes_same_pages() =>
        Prop.ForAll(Document.ToArbitrary(), document =>
        {
            var bytes = Encoding.UTF8.GetBytes(document);
            var first = Outcome(bytes);
            var second = Outcome(bytes);
            return (first == second).Label("two reads of the same bytes differ");
        });

    [Property(MaxTest = 300)]
    public Property Never_throws_on_arbitrary_input_within_limits() =>
        Prop.ForAll(Document.ToArbitrary(), document =>
        {
            var outcome = Outcome(Encoding.UTF8.GetBytes(document));
            return (outcome.Code is null || outcome.Code is "markdown.too-large").Label($"unexpected failure {outcome.Code}");
        });

    [Property(MaxTest = 300)]
    public Property Arbitrary_bytes_give_pages_or_a_coded_failure() =>
        Prop.ForAll(RawBytes.ToArbitrary(), bytes =>
        {
            var outcome = Outcome(bytes);
            return (outcome.Code is null || outcome.Code is "markdown.encoding" or "markdown.too-large").Label($"unexpected failure {outcome.Code}");
        });

    [Property(MaxTest = 1000)]
    public Property No_text_or_line_holds_a_tag_opener() =>
        Prop.ForAll(Document.ToArbitrary(), document =>
        {
            var pages = MarkdownExtractor.Parse(Encoding.UTF8.GetBytes(document));
            var bad = AllText(pages).FirstOrDefault(HoldsTagOpener);
            return (bad is null).Label($"markup survived: {bad}");
        });

    [Property(MaxTest = 300)]
    public Property Page_numbers_increase_and_stay_within_the_limit() =>
        Prop.ForAll(Document.ToArbitrary(), document =>
        {
            var numbers = MarkdownExtractor.Parse(Encoding.UTF8.GetBytes(document)).Select(p => p.PageNumber).ToList();
            var ok = numbers.SequenceEqual(numbers.Order().Distinct()) && numbers.All(n => n is >= 1 and <= ExtractionLimits.MarkdownMaxPages);
            return ok.Label(string.Join(",", numbers));
        });
}
