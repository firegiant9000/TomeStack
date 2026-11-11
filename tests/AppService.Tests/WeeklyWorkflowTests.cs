using System.Text.RegularExpressions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Guards the Weekly workflow's Stryker job: it runs in shards, and the shards together must select every
/// <c>src/RulesCore/*.cs</c> file exactly once (a file no shard selects is silently never mutated; one two shards select is mutated twice).
/// The matrix is read as plain text from <c>.github/workflows/weekly.yml</c>.
/// </summary>
public sealed partial class WeeklyWorkflowTests
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

    /// <summary>The lines of the <c>stryker:</c> job, up to the next job (a two-space-indented key).</summary>
    private static string[] StrykerJob()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot, ".github", "workflows", "weekly.yml"));
        var start = Array.FindIndex(lines, l => l == "  stryker:");
        Assert.True(start >= 0, "weekly.yml has no stryker job.");
        var end = start + 1;
        while (end < lines.Length && !JobKey().IsMatch(lines[end])) end++;
        return lines[start..end];
    }

    [GeneratedRegex(@"^  [A-Za-z0-9_-]+:\s*$")]
    private static partial Regex JobKey();

    // Reads the matrix entries, each written as
    //   - shard: effects
    //     mutate: "'**/Effects.cs'"
    private static Dictionary<string, List<string>> Shards()
    {
        var shards = new Dictionary<string, List<string>>();
        string? current = null;
        foreach (var line in StrykerJob())
        {
            var name = ShardName().Match(line);
            if (name.Success)
            {
                current = name.Groups[1].Value;
                shards[current] = [];
                continue;
            }
            var globs = ShardGlobs().Match(line);
            if (globs.Success && current is not null)
                foreach (Match glob in QuotedToken().Matches(globs.Groups[1].Value))
                    shards[current].Add(glob.Groups[1].Value);
        }
        return shards;
    }

    [GeneratedRegex(@"^\s+- shard:\s*([A-Za-z0-9_-]+)\s*$")]
    private static partial Regex ShardName();

    [GeneratedRegex(@"^\s+mutate:\s*(.+?)\s*$")]
    private static partial Regex ShardGlobs();

    [GeneratedRegex(@"'([^']+)'")]
    private static partial Regex QuotedToken();

    /// <summary>A glob is "**/" plus a file-name pattern (<c>*</c> any run of characters), so it matches on the whole file name.</summary>
    private static bool GlobMatches(string glob, string fileName)
    {
        const string prefix = "**/";
        Assert.StartsWith(prefix, glob);
        var pattern = "^" + Regex.Escape(glob[prefix.Length..]).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(fileName, pattern);
    }

    private static bool Selects(IEnumerable<string> globs, string fileName)
    {
        var selected = false;
        foreach (var glob in globs)
        {
            if (glob.StartsWith('!')) { if (GlobMatches(glob[1..], fileName)) selected = false; }
            else if (GlobMatches(glob, fileName)) selected = true;
        }
        return selected;
    }

    [Fact]
    public void Calculation_partials_land_in_the_calculation_shard()
    {
        var shards = Shards();
        Assert.True(Selects(shards["calculation"], "Calculation.Attacks.cs"));
        Assert.False(Selects(shards["rest"], "Calculation.Attacks.cs"));
    }

    [Fact]
    public void Stryker_shards_cover_every_RulesCore_file()
    {
        var shards = Shards();
        Assert.Equal(["effects", "validation", "calculation", "rest"], shards.Keys);

        var files = Directory.GetFiles(Path.Combine(RepoRoot, "src", "RulesCore"), "*.cs").Select(Path.GetFileName).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var owners = shards.Where(s => Selects(s.Value, file!)).Select(s => s.Key).ToList();
            Assert.True(owners.Count == 1, $"{file} is selected by {owners.Count} shards ({string.Join(", ", owners)}); expected exactly one.");
        }
    }

    [Fact]
    public void Stryker_job_allows_120_minutes()
    {
        var timeout = StrykerJob().Select(l => Regex.Match(l, @"^    timeout-minutes:\s*(\d+)\s*$")).FirstOrDefault(m => m.Success);
        Assert.NotNull(timeout);
        Assert.Equal("120", timeout.Groups[1].Value);
    }
}
