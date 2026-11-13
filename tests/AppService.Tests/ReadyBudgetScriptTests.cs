using System.Diagnostics;
using System.Text;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Runs <c>scripts/ready-budget.ps1</c> under Windows PowerShell against a temporary smoke report and checks the first-paint budget
/// (process start to UI ready, D102): 15 s locally, 30 s when <c>CI</c> is <c>true</c>, a negative budget skips the check and a report
/// without the field is not checked.
/// </summary>
public sealed class ReadyBudgetScriptTests
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

    /// <summary>Writes <paramref name="reportJson"/> to a temporary report, runs the script with <paramref name="scriptArguments"/> and returns its exit code and output.</summary>
    /// <param name="ciValue">The <c>CI</c> value for the child process, or null to remove it from the child's environment.</param>
    private static (int ExitCode, string Output) Run(string reportJson, string? ciValue, params string[] scriptArguments)
    {
        var temp = Path.Combine(Path.GetTempPath(), "ts-readybudget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var report = Path.Combine(temp, "smoke.json");
            File.WriteAllText(report, reportJson, new UTF8Encoding(false));

            var start = new ProcessStartInfo("powershell")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.Environment["CI"] = ciValue;
            foreach (var arg in new[]
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepoRoot, "scripts", "ready-budget.ps1"),
                "-Report", report,
            })
                start.ArgumentList.Add(arg);
            foreach (var arg in scriptArguments)
                start.ArgumentList.Add(arg);

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("ready-budget.ps1 did not finish within 120 s.");
            }
            return (process.ExitCode, stdout.Result + stderr.Result);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Under_budget_passes()
    {
        var (exitCode, output) = Run("""{"success": true, "detail": "ok", "processStartToReadyMs": 5000}""", ciValue: null, "-MaxReadyMs", "15000");

        Assert.Equal(0, exitCode);
        Assert.Contains("First paint 5000 ms (budget 15000 ms).", output);
    }

    [Fact]
    public void Over_budget_fails()
    {
        var (exitCode, output) = Run("""{"success": true, "detail": "ok", "processStartToReadyMs": 16000}""", ciValue: null);

        Assert.Equal(1, exitCode);
        Assert.Contains("over the 15000 ms budget", output);
    }

    [Fact]
    public void Ci_environment_uses_the_30_second_budget()
    {
        var (exitCode, output) = Run("""{"success": true, "detail": "ok", "processStartToReadyMs": 16000}""", ciValue: "true");

        Assert.Equal(0, exitCode);
        Assert.Contains("budget 30000 ms", output);
    }

    [Fact]
    public void Missing_field_is_reported_and_passes()
    {
        var (exitCode, output) = Run("""{"success": true, "detail": "ok"}""", ciValue: null);

        Assert.Equal(0, exitCode);
        Assert.Contains("not reported", output);
    }

    [Fact]
    public void Missing_field_with_require_fails()
    {
        var (exitCode, output) = Run("""{"success": true, "detail": "ok"}""", ciValue: null, "-Require");

        Assert.Equal(1, exitCode);
        Assert.Contains("Smoke ready time not reported; budget required.", output);
    }

    [Fact]
    public void Null_field_with_require_fails()
    {
        var (exitCode, _) = Run("""{"success": true, "detail": "ok", "processStartToReadyMs": null}""", ciValue: null, "-Require");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Present_field_with_require_passes()
    {
        var (exitCode, _) = Run("""{"success": true, "detail": "ok", "processStartToReadyMs": 5000}""", ciValue: null, "-Require");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Negative_budget_skips_the_check()
    {
        var (exitCode, output) = Run("""{"success": true, "detail": "ok", "processStartToReadyMs": 99999}""", ciValue: null, "-MaxReadyMs", "-1");

        Assert.Equal(0, exitCode);
        Assert.Contains("not checked", output);
    }
}
