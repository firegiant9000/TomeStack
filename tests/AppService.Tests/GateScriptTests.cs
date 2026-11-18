using System.Diagnostics;
using System.Text.Json;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Runs <c>scripts/gate.ps1</c> under Windows PowerShell with <c>-DryRun</c> or a <c>-StepsJson</c> table (never the real gate) and checks
/// step selection, the stop-on-failure summary, the e2e watchdog and the <c>-RequireQuiet</c> CPU check.
/// </summary>
public sealed class GateScriptTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("TomeStack.slnx not found above the test output folder.");
        }
    }

    /// <summary>Runs the script with a temporary <c>-LogDir</c> and returns its exit code, output, elapsed time and the log folder (deleted after <paramref name="inspect"/>).</summary>
    private static (int ExitCode, string Output, TimeSpan Elapsed) Run(Action<string>? inspect, params string[] scriptArguments)
    {
        var logDir = Path.Combine(Path.GetTempPath(), "ts-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var start = new ProcessStartInfo("powershell")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepoRoot, "scripts", "gate.ps1"), "-LogDir", logDir })
                start.ArgumentList.Add(arg);
            foreach (var arg in scriptArguments)
                start.ArgumentList.Add(arg);

            var clock = Stopwatch.StartNew();
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("gate.ps1 did not finish within 120 s.");
            }
            clock.Stop();
            inspect?.Invoke(logDir);
            return (process.ExitCode, stdout.Result + stderr.Result, clock.Elapsed);
        }
        finally
        {
            if (Directory.Exists(logDir))
                Directory.Delete(logDir, recursive: true);
        }
    }

    private static string Steps(params (string Id, string Command)[] steps) =>
        JsonSerializer.Serialize(steps.Select(s => new { id = s.Id, exe = "powershell", args = new[] { "-NoProfile", "-Command", s.Command } }));

    [Fact]
    public void Dry_run_lists_the_eight_steps_in_order()
    {
        var (exitCode, output, _) = Run(null, "-DryRun", "-SkipCpuCheck");

        Assert.Equal(0, exitCode);
        var position = -1;
        foreach (var id in new[] { "npm-ci", "lint", "ui-test", "ui-build", "dotnet-build", "dotnet-test", "e2e", "a11y" })
        {
            var next = output.IndexOf(id, position + 1, StringComparison.Ordinal);
            Assert.True(next > position, $"Step '{id}' is missing or out of order in:\n{output}");
            position = next;
        }
    }

    [Fact]
    public void From_skips_earlier_steps()
    {
        var (exitCode, output, _) = Run(null, "-DryRun", "-SkipCpuCheck", "-From", "dotnet-test");

        Assert.Equal(0, exitCode);
        Assert.Contains("dotnet-test", output);
        Assert.Contains("e2e", output);
        Assert.Contains("a11y", output);
        Assert.DoesNotContain("ui-test", output);
    }

    [Fact]
    public void Unknown_step_id_fails_fast()
    {
        var (exitCode, output, _) = Run(null, "-SkipCpuCheck", "-Only", "nope");

        Assert.Equal(2, exitCode);
        Assert.Contains("npm-ci", output);
    }

    [Fact]
    public void Failing_step_stops_the_run_and_writes_summary()
    {
        var marker = Path.Combine(Path.GetTempPath(), "ts-gate-marker-" + Guid.NewGuid().ToString("N"));
        string summary = "";
        var logExists = false;
        try
        {
            var (exitCode, _, _) = Run(
                logDir =>
                {
                    summary = File.ReadAllText(Path.Combine(logDir, "summary.txt"));
                    logExists = File.Exists(Path.Combine(logDir, "a.log"));
                },
                "-SkipCpuCheck", "-StepsJson", Steps(("a", "exit 0"), ("b", "exit 3"), ("c", $"Set-Content -LiteralPath '{marker}' -Value x")));

            Assert.Equal(3, exitCode);
            Assert.False(File.Exists(marker), "Step c ran after step b failed.");
            Assert.Contains("a", summary);
            Assert.Contains("b", summary);
            Assert.Contains("3", summary);
            Assert.True(logExists);
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Theory]
    [InlineData("e2e")]
    [InlineData("a11y")]
    public void Hung_devhost_step_is_killed_by_the_watchdog(string id)
    {
        var (exitCode, output, elapsed) = Run(null, "-SkipCpuCheck", "-DevHostStepTimeoutMinutes", "0.05", "-StepsJson", Steps((id, "Start-Sleep 60")));

        Assert.Equal(124, exitCode);
        Assert.Contains("timed out", output);
        Assert.True(elapsed < TimeSpan.FromSeconds(30), $"Took {elapsed.TotalSeconds:N0} s.");
    }

    [Fact]
    public void Require_quiet_fails_when_over_threshold()
    {
        var (exitCode, output, _) = Run(null, "-CpuWarnPercent", "0", "-RequireQuiet", "-DryRun");

        Assert.Equal(2, exitCode);
        Assert.Contains("% Processor Time", output);
    }
}
