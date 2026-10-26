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
        "---", "...", "\n", "\n", "\n\n", "\r\n", "\r", " ", "\t", "\0", "text", "Fixture Pool.", "é", " ", "­", "‐", "ﬁ", "﻿", "😀",
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

    [Property(MaxTest = 300)]
    public Property No_page_text_holds_a_tag_opener_that_the_input_started_as_html() =>
        Prop.ForAll(Document.ToArbitrary(), document =>
        {
            var pages = MarkdownExtractor.Parse(Encoding.UTF8.GetBytes(document));
            // A '<' survives only as plain text: never one that opens a script, a style or a comment.
            var text = string.Join("\n", pages.Select(p => p.Text));
            var bad = text.Contains("<script", StringComparison.OrdinalIgnoreCase) || text.Contains("<style", StringComparison.OrdinalIgnoreCase) || text.Contains("<!--", StringComparison.Ordinal);
            return (!bad).Label("markup survived");
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
