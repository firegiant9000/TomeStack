using System.Text.RegularExpressions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Guards the CI workflow's shape: parallel jobs behind one required check named <c>build-test</c>
/// (the repository ruleset requires that name). Read as plain text from <c>.github/workflows/ci.yml</c>.
/// </summary>
public sealed partial class CiWorkflowTests
{
    private static readonly string[] Others = ["ui-build", "ui-test", "dotnet", "gui"];

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

    private static string[] WorkflowLines() => File.ReadAllLines(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml"));

    private static List<string> JobKeys()
    {
        var lines = WorkflowLines();
        var start = Array.FindIndex(lines, l => l == "jobs:");
        Assert.True(start >= 0, "ci.yml has no jobs section.");
        return lines.Skip(start + 1).Select(l => JobKey().Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>The lines of one job, up to the next job (a two-space-indented key).</summary>
    private static string Job(string name)
    {
        var lines = WorkflowLines();
        var start = Array.FindIndex(lines, l => l == $"  {name}:");
        Assert.True(start >= 0, $"ci.yml has no {name} job.");
        var end = start + 1;
        while (end < lines.Length && !JobKey().IsMatch(lines[end])) end++;
        return string.Join('\n', lines[start..end]);
    }

    [GeneratedRegex(@"^  ([A-Za-z0-9_-]+):\s*$")]
    private static partial Regex JobKey();

    [Fact]
    public void Ci_has_the_four_jobs_and_the_build_test_aggregate()
    {
        Assert.Equal(["build-test", "dotnet", "gui", "ui-build", "ui-test"], JobKeys().Order().ToList());
    }

    [Fact]
    public void Build_test_fails_unless_every_job_succeeded()
    {
        var job = Job("build-test");
        Assert.Contains("if: always()", job);
        var needs = Regex.Match(job, @"needs:\s*\[([^\]]*)\]");
        Assert.True(needs.Success, "build-test has no inline needs list.");
        var listed = needs.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(Others.Order().ToList(), listed.Order().ToList());
        foreach (var other in Others)
            Assert.Matches($@"needs\.{Regex.Escape(other)}\.result\b.*success|success.*needs\.{Regex.Escape(other)}\.result\b", job);
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("gui")]
    public void Dotnet_and_gui_take_the_bundle_from_ui_build(string name)
    {
        var job = Job(name);
        Assert.Matches(@"needs:\s*(\[\s*ui-build\s*\]|ui-build\b)", job);
        Assert.Contains("download-artifact@", job);
        Assert.Contains("name: ui-dist", job);
    }

    [Fact]
    public void Every_action_is_pinned_to_a_commit_sha()
    {
        var uses = WorkflowLines().Where(l => l.Contains("uses:")).ToList();
        Assert.NotEmpty(uses);
        foreach (var line in uses)
            Assert.Matches(@"uses: [\w./-]+@[0-9a-f]{40} # v", line);
    }
}
