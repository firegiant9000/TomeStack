using System.Text.Json;
using System.Text.Json.Nodes;

namespace TomeStack.AppService.Extensions;

/// <summary>
/// ADR-011 "Outputs are untrusted input": the second line of privacy control, after the allowlisted sheet export model.
/// Before anything an extension produced is written, it is searched for the data-folder path, the user-profile path, the
/// Windows user name and every linked-PDF path (they can lie outside the data folder). The search ignores case, treats
/// both slash directions alike, and reads JSON output both as written and with its string escapes decoded.
/// </summary>
public static class OutputScan
{
    /// <summary>Names shorter than this are not searched for: they would refuse ordinary words.</summary>
    public const int MinimumLength = 3;

    /// <summary>True when <paramref name="output"/> contains one of <paramref name="sensitive"/>. It never says which.</summary>
    public static bool Leaks(string output, IEnumerable<string?> sensitive)
    {
        ArgumentNullException.ThrowIfNull(output);
        var haystacks = new List<string> { Normalize(output) };
        try
        {
            if (JsonNode.Parse(output) is { } node)
                haystacks.Add(Normalize(string.Join("\n", Strings(node))));
        }
        catch (JsonException)
        {
            // Not JSON: the text as written is all there is.
        }
        foreach (var needle in sensitive.OfType<string>().Select(Normalize).Where(n => n.Length >= MinimumLength).Distinct())
        {
            // A path is also found without its drive letter or with a trailing slash trimmed.
            var trimmed = needle.TrimEnd('/');
            var withoutDrive = trimmed.Length > 2 && trimmed[1] == ':' ? trimmed[2..] : trimmed;
            if (haystacks.Any(h => h.Contains(trimmed, StringComparison.Ordinal) || (withoutDrive.Length >= MinimumLength && h.Contains(withoutDrive, StringComparison.Ordinal))))
                return true;
        }
        return false;
    }

    private static string Normalize(string text) => text.Replace('\\', '/').ToLowerInvariant();

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
