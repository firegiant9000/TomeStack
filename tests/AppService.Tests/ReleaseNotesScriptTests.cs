using System.Diagnostics;
using System.Text;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Runs <c>scripts/release-notes.ps1</c> (under Windows PowerShell 5.1 and PowerShell 7, since the release workflow runs it with <c>shell: pwsh</c>) against a temporary changelog and checks that
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

    /// <summary>Writes a changelog whose 9.9.9 section is <paramref name="section"/>, runs the script with <paramref name="host"/> and returns the notes it wrote.</summary>
    private static string Run(string host, string section)
    {
        var temp = Path.Combine(Path.GetTempPath(), "ts-relnotes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var changelog = Path.Combine(temp, "CHANGELOG.md");
            var output = Path.Combine(temp, "notes.md");
            File.WriteAllText(changelog, "# Changelog\n\n## 9.9.9 (test)\n" + section + "\n\n## 9.9.8\nprevious-section-marker [x](old.md)\n", new UTF8Encoding(true));

            var start = new ProcessStartInfo(host)
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

    private static bool PwshAvailable()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(dir.Trim('"'), "pwsh.exe")) || File.Exists(Path.Combine(dir.Trim('"'), "pwsh"))) return true;
            }
            catch (ArgumentException) { }
        }
        return false;
    }

    /// <summary>xUnit 2 has no runtime skip, so the skip is decided when the test is discovered.</summary>
    public sealed class PwshTheoryAttribute : TheoryAttribute
    {
        public PwshTheoryAttribute()
        {
            if (!PwshAvailable())
                Skip = "pwsh (PowerShell 7) is not on PATH; the release workflow runs the script with it.";
        }
    }

    /// <summary>Input and expected output for one changelog bullet; the last case: an unpaired backtick must not swallow a later line.</summary>
    public static TheoryData<string, string> Cases { get; } = new()
    {
        { "[a](decisions/ADR-022-content-v12.md)", "[a](" + Blob + "docs/decisions/ADR-022-content-v12.md)" },
        { "[b](features/x.md#part)", "[b](" + Blob + "docs/features/x.md#part)" },
        { "[c](../README.md)", "[c](" + Blob + "README.md)" },
        { "[d](https://example.org/x)", "[d](https://example.org/x)" },
        { "[e](#heading)", "[e](#heading)" },
        { "[f](mailto:a@b.c)", "[f](mailto:a@b.c)" },
        { "![g](media/x.png)", "![g](" + Blob + "docs/media/x.png?raw=true)" },
        { "[h](a.md \"t\")", "[h](" + Blob + "docs/a.md \"t\")" },
        { "[i](./a.md)", "[i](" + Blob + "docs/a.md)" },
        { "[j](http://example.org/x)", "[j](http://example.org/x)" },
        { "[k](ftp://example.org/x)", "[k](ftp://example.org/x)" },
        { "[l](../../../x.md)", "[l](" + Blob + "x.md)" },
        { "`[m](a.md)` and [n](b.md)", "`[m](a.md)` and [n](" + Blob + "docs/b.md)" },
        { "[o](a.md) then [p](b.md#q) and ![r](c.png)", "[o](" + Blob + "docs/a.md) then [p](" + Blob + "docs/b.md#q) and ![r](" + Blob + "docs/c.png?raw=true)" },
        { "an unpaired ` tick\n- [q](a.md) and `x`", "an unpaired ` tick\n- [q](" + Blob + "docs/a.md) and `x`" },
    };

    private static void AssertRewritten(string host, string input, string expected)
    {
        var notes = Run(host, "- " + input);
        Assert.Contains("- " + expected + "\n", notes.Replace("\r\n", "\n"));
    }

    private static void AssertOnlyRequestedSection(string host)
    {
        var notes = Run(host, "- [a](a.md)");
        Assert.DoesNotContain("previous-section-marker", notes);
        Assert.Contains("[GitHub CLI](https://cli.github.com/)", notes);
    }

    private static void AssertNamesTheSboms(string host)
    {
        var notes = Run(host, "- [a](a.md)").Replace("\r\n", "\n");
        Assert.Contains("tomestack-9.9.9-nuget.cdx.json", notes);
        Assert.Contains("tomestack-9.9.9-npm.cdx.json", notes);
        Assert.Contains("CycloneDX", notes);
        Assert.Contains("software bills of materials", notes);
        Assert.Contains("`SHA256SUMS` and attested", notes);
    }

    [Fact]
    public void DownloadListNamesTheSboms() => AssertNamesTheSboms("powershell");

    [PwshTheory]
    [InlineData("pwsh")]
    public void DownloadListNamesTheSbomsUnderPwsh(string host) => AssertNamesTheSboms(host);

    [Theory]
    [MemberData(nameof(Cases))]
    public void RelativeLinksAreRewritten(string input, string expected) => AssertRewritten("powershell", input, expected);

    [PwshTheory]
    [MemberData(nameof(Cases))]
    public void RelativeLinksAreRewrittenUnderPwsh(string input, string expected) => AssertRewritten("pwsh", input, expected);

    [Fact]
    public void OnlyTheRequestedSectionIsEmittedAndTheFixedTextIsUntouched() => AssertOnlyRequestedSection("powershell");

    [PwshTheory]
    [InlineData("pwsh")]
    public void OnlyTheRequestedSectionIsEmittedUnderPwsh(string host) => AssertOnlyRequestedSection(host);
}
