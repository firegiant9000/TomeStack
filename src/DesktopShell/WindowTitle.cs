namespace TomeStack.DesktopShell;

/// <summary>Maps the web page's document title to the native window title.</summary>
public static class WindowTitle
{
    private const string AppName = "TomeStack";
    private const int MaxLength = 200;

    /// <summary>
    /// The trimmed document title, cut to 200 characters; "TomeStack" when the title is null or blank.
    /// </summary>
    public static string From(string? documentTitle)
    {
        var trimmed = documentTitle?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return AppName;
        return trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength];
    }
}
