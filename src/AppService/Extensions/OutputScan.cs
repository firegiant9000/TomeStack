using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TomeStack.AppService.Extensions;

/// <summary>
/// ADR-011 "Outputs are untrusted input": the second line of privacy control, after the allowlisted sheet export model.
/// Before anything an extension or an adapter produced is written, it is searched for the data-folder path, the
/// user-profile path, every linked-PDF path, and the Windows user name as a path segment. The search ignores case and
/// Unicode compatibility forms, drops invisible format characters, treats both slash directions (and their lookalikes)
/// alike, collapses doubled slashes, and reads JSON output both as written and with its string escapes decoded.
/// </summary>
public static class OutputScan
{
    /// <summary>Needles shorter than this are not searched for: they would refuse ordinary words.</summary>
    public const int MinimumLength = 3;

    /// <summary>
    /// True when <paramref name="output"/> contains one of <paramref name="paths"/>, or one of <paramref name="userNames"/>
    /// as a whole path segment (review fix: a bare substring refused "maximum" for a user named Max, and SRD text for
    /// "Owner"; the profile path already covers the name inside it). It never says which.
    /// </summary>
    public static bool Leaks(string output, IEnumerable<string?> paths, IEnumerable<string?> userNames)
    {
        ArgumentNullException.ThrowIfNull(output);
        var haystacks = new List<string> { Normalize(output) };
        try
        {
            // As deep as any output TomeStack writes may be, so deep output is decoded too.
            if (JsonNode.Parse(output, documentOptions: new JsonDocumentOptions { MaxDepth = 256 }) is { } node)
                haystacks.Add(Normalize(string.Join("\n", Strings(node))));
        }
        catch (JsonException)
        {
            // Not JSON: the text as written is all there is.
        }
        foreach (var needle in paths.OfType<string>().Select(Normalize).Select(n => n.TrimEnd('/')).Where(n => n.Length >= MinimumLength).Distinct())
        {
            // A path is also found without its drive letter.
            var withoutDrive = needle.Length > 2 && needle[1] == ':' ? needle[2..] : needle;
            if (haystacks.Any(h => h.Contains(needle, StringComparison.Ordinal) || (withoutDrive.Length >= MinimumLength && h.Contains(withoutDrive, StringComparison.Ordinal))))
                return true;
        }
        foreach (var name in userNames.OfType<string>().Select(Normalize).Where(n => n.Length >= MinimumLength && !n.Contains('/', StringComparison.Ordinal)).Distinct())
        {
            if (haystacks.Any(h => IsSegment(h, name)))
                return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="name"/> occurs right after a slash and ends at a slash, a quote, whitespace or the end.</summary>
    private static bool IsSegment(string haystack, string name)
    {
        var from = 0;
        while ((from = haystack.IndexOf("/" + name, from, StringComparison.Ordinal)) >= 0)
        {
            var end = from + 1 + name.Length;
            if (end == haystack.Length || haystack[end] is '/' or '"' or '\'' || char.IsWhiteSpace(haystack[end]))
                return true;
            from = end;
        }
        return false;
    }

    /// <summary>Compatibility-normalized, lower case, no format characters, every slash lookalike a "/", no doubled slashes.</summary>
    internal static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.GetUnicodeCategory(c) == UnicodeCategory.Format)
                continue;
            var mapped = c is '\\' or '∕' or '∖' or '⧵' or '／' or '＼' or '⁄' ? '/' : char.ToLowerInvariant(c);
            if (mapped == '/' && builder.Length > 0 && builder[^1] == '/')
                continue;
            builder.Append(mapped);
        }
        return builder.ToString();
    }

    /// <summary>Every property name and string value, decoded.</summary>
    private static IEnumerable<string> Strings(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    yield return key;
                    if (value is not null)
                        foreach (var inner in Strings(value)) yield return inner;
                }
                break;
            case JsonArray array:
                foreach (var value in array.OfType<JsonNode>())
                    foreach (var inner in Strings(value)) yield return inner;
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                yield return (string)value!;
                break;
        }
    }
}
