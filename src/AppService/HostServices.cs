namespace TomeStack.AppService;

/// <summary>
/// Native capabilities provided by the hosting shell (ARCHITECTURE: the shell owns native file dialogs).
/// Hosts without them (DevHost, tests) pass none, and the corresponding commands report <c>unsupported</c>.
/// </summary>
public interface IHostServices
{
    /// <summary>Shows a native Save dialog. Returns the full path the user chose, or null if they cancelled.</summary>
    string? ChooseSaveLocation(string suggestedFileName, string filterDescription, string extension);

    /// <summary>Whether <see cref="ChooseOpenFile"/> and <see cref="OpenPdf"/> are available (M2 item 6).</summary>
    bool CanOpenFiles => false;

    /// <summary>
    /// Shows a native Open dialog. Returns the full path the user chose, or null if they cancelled. <paramref name="extension"/>
    /// may list several, separated by semicolons (".json;.csv").
    /// </summary>
    string? ChooseOpenFile(string filterDescription, string extension) => null;

    /// <summary>
    /// ADR-005, SPEC S-04: opens a PDF at a page in the shell's offline viewer. Returns false when the host cannot.
    /// The path never reaches the UI.
    /// </summary>
    bool OpenPdf(string path, int page, string title) => false;
}

/// <summary>Result of a native save: only the file name is returned to the UI, never the full path.</summary>
public sealed record SaveOutcome(bool Saved, string? FileName);
