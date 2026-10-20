namespace TomeStack.ImportWorker.Detection;

/// <summary>Shared limits for the entry-finding patterns. A pattern that runs past its timeout stops the import with <c>detect.timeout</c>.</summary>
internal static class DetectionLimits
{
    /// <summary>
    /// Wall-clock match timeout (ms) for every detection regex. It is a guard against runaway patterns, not a speed target:
    /// 100 ms was short enough that a paused thread on a busy computer could fail an import on any line (R151).
    /// </summary>
    internal const int RegexTimeoutMs = 1000;
}
