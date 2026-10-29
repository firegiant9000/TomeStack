namespace TomeStack.AppService.CharacterImport;

/// <summary>Shared limits for the character-import patterns (sheet fields, text lines, the stub class's hit dice).</summary>
internal static class ImportRegex
{
    /// <summary>
    /// Wall-clock match timeout (ms) for every character-import regex. It is a guard against runaway patterns, not a speed
    /// target: at 100 ms a paused thread on a busy computer read a valid hit-dice line as unreadable (2026-10-25, as R151 found
    /// for PDF detection). The text importer's 2 s budget for a whole text is separate.
    /// </summary>
    internal const int TimeoutMs = 1000;
}
