using System.Globalization;
using System.Text;

namespace TomeStack.RulesCore;

/// <summary>
/// Tester feedback item 10 (ruling R43): the one normaliser and validator for spell list keys, shared by the detector's
/// proposals and the service's "add to spell list" commands. A key is trimmed, lower-cased (invariant) and has each run of
/// whitespace turned into one hyphen; it is valid when it is 1 to 60 characters of <c>[a-z0-9-]</c>. The bundled keys
/// (bard, cleric, wizard, …) are already valid. The UI has a mirror of <see cref="Normalise"/> for its preview.
/// </summary>
public static class SpellListKey
{
    public const int MaxLength = 60;

    public static string Normalise(string? text)
    {
        var result = new StringBuilder();
        var pendingGap = false;
        foreach (var c in (text ?? "").Trim().ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsWhiteSpace(c))
            {
                pendingGap = true;
                continue;
            }
            if (pendingGap)
                result.Append('-');
            pendingGap = false;
            result.Append(c);
        }
        return result.ToString();
    }

    public static bool IsValid(string? key) =>
        key is { Length: > 0 and <= MaxLength } && key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>The normalised key, or false when the text cannot be a key.</summary>
    public static bool TryNormalise(string? text, out string key)
    {
        key = Normalise(text);
        return IsValid(key);
    }
}
