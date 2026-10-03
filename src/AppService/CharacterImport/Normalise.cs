using System.Text;

namespace TomeStack.AppService.CharacterImport;

/// <summary>
/// Name normalisation for matching (<c>features/ddb-pdf-import.md</c> "Matching rules"): case folded, trimmed, inner
/// whitespace collapsed to one space, and typographic quotes and dashes folded to ASCII. Nothing else: there is no fuzzy
/// matching, because a near-miss is a wrong match the user may not notice.
/// </summary>
public static class Normalise
{
    public static string Name(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var text = new StringBuilder(s.Length);
        var space = false;
        foreach (var c in s.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }
            if (space && text.Length > 0)
                text.Append(' ');
            space = false;
            text.Append(c switch
            {
                '‘' or '’' or '‚' or '‛' or '′' => '\'',
                '“' or '”' or '„' or '‟' or '″' => '"',
                '‐' or '‑' or '‒' or '–' or '—' or '―' or '−' => '-',
                _ => char.ToLowerInvariant(c),
            });
        }
        return text.ToString();
    }
}
