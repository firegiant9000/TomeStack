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

    [GeneratedRegex(@"\]\(([^)#\s]+)(#[^)]*)?\)", RegexOptions.CultureInvariant, 1000)]
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
        var (checkedCount, broken) = BrokenLinks(StartHerePath);
        Assert.True(checkedCount > 0, "No relative links were found to check.");
        Assert.Empty(broken);
    }

    [Fact]
    public void BrokenLinks_catches_wrong_case_missing_anchor_and_missing_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ts-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Real.md"), "# Real\n\n## Some Heading (x/y)\n");
            var page = Path.Combine(folder, "page.md");
            File.WriteAllText(page, "[a](real.md) [b](Real.md#missing) [c](Real.md#some-heading-xy) [d](httpd.md) [e](https://example.com/x) [f](#local)\n");
            var (checkedCount, broken) = BrokenLinks(page);
            Assert.Equal(4, checkedCount);
            Assert.Equal(["real.md", "Real.md#missing", "httpd.md"], broken);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The relative links in <paramref name="markdownPath"/> that would 404 on GitHub: a missing file or folder, a name whose case differs from
    /// the file on disk (GitHub paths are case-sensitive, Windows is not), or an <c>#anchor</c> that matches no heading of a Markdown target.
    /// Skips <c>http://</c>, <c>https://</c>, <c>mailto:</c> and same-page <c>#anchor</c> links.
    /// </summary>
    internal static (int Checked, List<string> Broken) BrokenLinks(string markdownPath)
    {
        var folder = Path.GetDirectoryName(markdownPath)!;
        var count = 0;
        var broken = new List<string>();
        foreach (Match match in MarkdownLink().Matches(File.ReadAllText(markdownPath)))
        {
            var target = match.Groups[1].Value;
            if (target.StartsWith("http://", StringComparison.Ordinal) || target.StartsWith("https://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal))
                continue;
            count++;
            var anchor = match.Groups[2].Success ? match.Groups[2].Value[1..] : null;
            var full = Path.GetFullPath(Path.Combine(folder, target.Replace('/', Path.DirectorySeparatorChar)));
            var ok = ExistsWithExactCase(full)
                && (anchor is null || !File.Exists(full) || !full.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || HeadingSlugs(full).Contains(anchor));
            if (!ok) broken.Add(match.Groups[1].Value + match.Groups[2].Value);
        }
        return (count, broken);
    }

    private static bool ExistsWithExactCase(string fullPath)
    {
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed);
        if (parent is null) return Directory.Exists(trimmed);
        if (!Directory.Exists(parent)) return false;
        var name = Path.GetFileName(trimmed);
        return Directory.EnumerateFileSystemEntries(parent).Any(e => string.Equals(Path.GetFileName(e), name, StringComparison.Ordinal))
            && ExistsWithExactCase(parent);
    }

    /// <summary>GitHub's heading anchors: lower case, punctuation other than hyphens and underscores dropped, spaces to hyphens.</summary>
    private static HashSet<string> HeadingSlugs(string markdownFile) =>
        File.ReadLines(markdownFile)
            .Where(l => l.StartsWith('#'))
            .Select(l => l.TrimStart('#').Trim().ToLowerInvariant())
            .Select(h => new string(h.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Replace(' ', '-'))
            .ToHashSet(StringComparer.Ordinal);

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
