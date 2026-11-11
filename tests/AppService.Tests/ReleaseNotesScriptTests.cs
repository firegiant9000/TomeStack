using System.Diagnostics;
using System.Text;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Runs <c>scripts/release-notes.ps1</c> (Windows PowerShell 5.1, as the release workflow does) against a temporary changelog and checks that
/// relative links in the changelog section point at the tagged docs on GitHub, because the release page is not inside the repo.
/// </summary>
public sealed class ReleaseNotesScriptTests
{
    private const string Blob = "https://github.com/firegiant9000/TomeStack/blob/v9.9.9/";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

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

    /// <summary>Writes a changelog whose 9.9.9 section is <paramref name="section"/>, runs the script and returns the notes it wrote.</summary>
    private static string Run(string section)
    {
        var temp = Path.Combine(Path.GetTempPath(), "ts-relnotes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var changelog = Path.Combine(temp, "CHANGELOG.md");
            var output = Path.Combine(temp, "notes.md");
            File.WriteAllText(changelog, "# Changelog\n\n## 9.9.9 (test)\n" + section + "\n\n## 9.9.8\nprevious-section-marker [x](old.md)\n", new UTF8Encoding(true));

            var start = new ProcessStartInfo("powershell")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[]
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepoRoot, "scripts", "release-notes.ps1"),
                "-Version", "9.9.9", "-Commit", Commit, "-Output", output, "-ChangelogPath", changelog,
            })
                start.ArgumentList.Add(arg);

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("release-notes.ps1 did not finish within 120 s.");
            }
            Assert.True(process.ExitCode == 0, $"release-notes.ps1 failed ({process.ExitCode}): {stderr.Result}{stdout.Result}");
            return File.ReadAllText(output, Encoding.UTF8);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("[a](decisions/ADR-022-content-v12.md)", "[a](" + Blob + "docs/decisions/ADR-022-content-v12.md)")]
    [InlineData("[b](features/x.md#part)", "[b](" + Blob + "docs/features/x.md#part)")]
    [InlineData("[c](../README.md)", "[c](" + Blob + "README.md)")]
    [InlineData("[d](https://example.org/x)", "[d](https://example.org/x)")]
    [InlineData("[e](#heading)", "[e](#heading)")]
    [InlineData("[f](mailto:a@b.c)", "[f](mailto:a@b.c)")]
    [InlineData("![g](media/x.png)", "![g](" + Blob + "docs/media/x.png?raw=true)")]
    [InlineData("[h](a.md \"t\")", "[h](" + Blob + "docs/a.md \"t\")")]
    [InlineData("[i](./a.md)", "[i](" + Blob + "docs/a.md)")]
    [InlineData("[j](http://example.org/x)", "[j](http://example.org/x)")]
    [InlineData("[k](ftp://example.org/x)", "[k](ftp://example.org/x)")]
    [InlineData("[l](../../../x.md)", "[l](" + Blob + "x.md)")]
    [InlineData("`[m](a.md)` and [n](b.md)", "`[m](a.md)` and [n](" + Blob + "docs/b.md)")]
    [InlineData("[o](a.md) then [p](b.md#q) and ![r](c.png)", "[o](" + Blob + "docs/a.md) then [p](" + Blob + "docs/b.md#q) and ![r](" + Blob + "docs/c.png?raw=true)")]
    public void RelativeLinksAreRewritten(string input, string expected)
    {
        var notes = Run("- " + input);
        Assert.Contains("- " + expected + "\n", notes.Replace("\r\n", "\n"));
    }

    [Fact]
    public void OnlyTheRequestedSectionIsEmittedAndTheFixedTextIsUntouched()
    {
        var notes = Run("- [a](a.md)");
        Assert.DoesNotContain("previous-section-marker", notes);
        Assert.Contains("[GitHub CLI](https://cli.github.com/)", notes);
    }
}
