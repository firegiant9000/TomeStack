namespace TomeStack.DesktopShell;

/// <summary>Maps the web page's document title to the native window title.</summary>
public static class WindowTitle
{
    private const string AppName = "TomeStack";
    private const int MaxLength = 200;

    /// <summary>
    /// The trimmed document title, cut to at most 200 characters, never between a surrogate pair; "TomeStack" when the title is null or blank.
    /// </summary>
    public static string From(string? documentTitle)
    {
        var trimmed = documentTitle?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return AppName;
        if (trimmed.Length <= MaxLength)
            return trimmed;
        // Never split a surrogate pair: if the cut would fall between a high and a low surrogate, cut one earlier.
        var cut = char.IsHighSurrogate(trimmed[MaxLength - 1]) && char.IsLowSurrogate(trimmed[MaxLength])
            ? MaxLength - 1
            : MaxLength;
        return trimmed[..cut];
    }
}
