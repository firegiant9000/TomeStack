namespace TomeStack.AppService;

/// <summary>
/// Native capabilities provided by the hosting shell (ARCHITECTURE: the shell owns native file dialogs).
/// Hosts without them (DevHost, tests) pass none, and the corresponding commands report <c>unsupported</c>.
/// </summary>
public interface IHostServices
{
    /// <summary>Shows a native Save dialog. Returns the full path the user chose, or null if they cancelled.</summary>
    string? ChooseSaveLocation(string suggestedFileName, string filterDescription, string extension);
}

/// <summary>Result of a native save: only the file name is returned to the UI, never the full path.</summary>
public sealed record SaveOutcome(bool Saved, string? FileName);
