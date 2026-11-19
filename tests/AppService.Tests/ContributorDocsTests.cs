using System.Text.RegularExpressions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Pins the contributor start-here page (Wave 4 item 4.11): its three paths, its relative links, the links to it from
/// CONTRIBUTING.md and the README, and that the tester path asks for no build.
/// </summary>
public sealed partial class ContributorDocsTests
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

    private static string StartHerePath => Path.Combine(RepoRoot, "docs", "contributing", "start-here.md");

    [GeneratedRegex(@"\]\(([^)#\s]+)(#[^)]*)?\)", RegexOptions.None, 1000)]
    private static partial Regex MarkdownLink();

    [Fact]
    public void StartHere_exists_with_three_paths()
    {
        Assert.True(File.Exists(StartHerePath), "docs/contributing/start-here.md is missing.");
        var lines = File.ReadAllLines(StartHerePath);
        Assert.Contains("## Play and report", lines);
        Assert.Contains("## Write homebrew", lines);
        Assert.Contains("## Fix the UI", lines);
    }

    [Fact]
    public void StartHere_relative_links_resolve()
    {
        var folder = Path.GetDirectoryName(StartHerePath)!;
        var targets = MarkdownLink().Matches(File.ReadAllText(StartHerePath))
            .Select(m => m.Groups[1].Value)
            .Where(t => !t.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(targets);
        var broken = targets.Where(t =>
        {
            var full = Path.GetFullPath(Path.Combine(folder, t.Replace('/', Path.DirectorySeparatorChar)));
            return !File.Exists(full) && !Directory.Exists(full);
        }).ToList();
        Assert.Empty(broken);
    }

    [Theory]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("README.md")]
    public void Contributing_and_readme_link_to_start_here(string file)
    {
        Assert.Contains("](docs/contributing/start-here.md)", File.ReadAllText(Path.Combine(RepoRoot, file)), StringComparison.Ordinal);
    }

    [Fact]
    public void Play_and_report_needs_no_build()
    {
        var text = File.ReadAllText(StartHerePath);
        var start = text.IndexOf("## Play and report", StringComparison.Ordinal);
        Assert.True(start >= 0, "The Play and report section is missing.");
        var next = text.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = next < 0 ? text[start..] : text[start..next];
        foreach (var forbidden in new[] { "npm ", "dotnet ", "scripts/" })
            Assert.DoesNotContain(forbidden, section, StringComparison.Ordinal);
    }
}
