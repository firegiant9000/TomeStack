using System.Text.RegularExpressions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Guards the CI workflow's shape: parallel jobs behind one required check named <c>build-test</c>
/// (the repository ruleset requires that name). Read as plain text from <c>.github/workflows/ci.yml</c>
/// and parsed by line and indentation (no YAML library): comments and blank lines are dropped, job-level
/// keys are read at exactly four spaces and step-level keys at exactly eight, so a commented-out line or a
/// key at the wrong level cannot satisfy a check. The checks take the lines as a parameter so the mutation
/// tests below can prove each one fails on a broken copy.
/// </summary>
public sealed partial class CiWorkflowTests
{
    private static readonly string[] Others = ["ui-build", "ui-test", "dotnet", "gui"];

    private const string HelperLine = "check() { echo \"$1: $2\"; if [ \"$2\" != \"success\" ]; then fail=1; fi; }";

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

    /// <summary>The workflow lines without full-line comments and blank lines; indentation is kept.</summary>
    private static string[] CodeLines(string[] lines) =>
        lines.Where(l => !string.IsNullOrWhiteSpace(l) && !CommentLine().IsMatch(l)).ToArray();

    private static List<string> JobKeys(string[] lines)
    {
        var code = CodeLines(lines);
        var start = Array.FindIndex(code, l => l == "jobs:");
        Assert.True(start >= 0, "ci.yml has no jobs section.");
        return code.Skip(start + 1).Select(l => JobKey().Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>The code lines of one job below its header, up to the next job (a two-space-indented key).</summary>
    private static string[] Job(string[] lines, string name)
    {
        var code = CodeLines(lines);
        var start = Array.FindIndex(code, l => l == $"  {name}:");
        Assert.True(start >= 0, $"ci.yml has no {name} job.");
        var end = start + 1;
        while (end < code.Length && !JobKey().IsMatch(code[end])) end++;
        return code[(start + 1)..end];
    }

    /// <summary>A job-level key: only lines at exactly four spaces count, so a step-level key cannot match.</summary>
    private static string JobValue(string[] job, string key)
    {
        var found = job.Select(l => Regex.Match(l, $@"^ {{4}}{Regex.Escape(key)}:\s*(.*?)\s*$")).Where(m => m.Success).ToList();
        Assert.True(found.Count == 1, $"Expected exactly one job-level '{key}' key, found {found.Count}.");
        return found[0].Groups[1].Value;
    }

    private static List<string> NeedsOf(string[] job)
    {
        var value = JobValue(job, "needs");
        return value.Trim('[', ']').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>
    /// The steps of a job, each as its lines with the dash line rewritten so every step-level key
    /// sits at eight spaces.
    /// </summary>
    private static List<string[]> Steps(string[] job)
    {
        var start = Array.FindIndex(job, l => l == "    steps:");
        Assert.True(start >= 0, "Job has no steps list.");
        var steps = new List<List<string>>();
        foreach (var line in job.Skip(start + 1))
        {
            if (line.StartsWith("      - ", StringComparison.Ordinal))
                steps.Add(["        " + line[8..]]);
            else if (steps.Count > 0)
                steps[^1].Add(line);
        }
        return steps.Select(s => s.ToArray()).ToList();
    }

    private static string? StepValue(string[] step, string key)
    {
        var found = step.Select(l => Regex.Match(l, $@"^ {{8}}{Regex.Escape(key)}:\s*(.*?)\s*$")).Where(m => m.Success).ToList();
        return found.Count == 1 ? found[0].Groups[1].Value : null;
    }

    /// <summary>The trimmed lines of the step's <c>run: |</c> block (indent of ten or more).</summary>
    private static List<string> Run(string[] step)
    {
        var start = Array.FindIndex(step, l => Regex.IsMatch(l, @"^ {8}run:"));
        Assert.True(start >= 0, "Step has no run block.");
        return step.Skip(start + 1).TakeWhile(l => l.StartsWith("          ", StringComparison.Ordinal)).Select(l => l.Trim()).ToList();
    }

    /// <summary>The step's <c>with:</c> entries (keys at ten spaces).</summary>
    private static Dictionary<string, string> With(string[] step)
    {
        var start = Array.FindIndex(step, l => l == "        with:");
        var result = new Dictionary<string, string>();
        if (start < 0) return result;
        foreach (var line in step.Skip(start + 1).TakeWhile(l => l.StartsWith("          ", StringComparison.Ordinal)))
        {
            var m = Regex.Match(line, @"^ {10}([A-Za-z0-9_-]+):\s*(.*?)\s*$");
            if (m.Success) result[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return result;
    }

    [GeneratedRegex(@"^  ([A-Za-z0-9_-]+):\s*$")]
    private static partial Regex JobKey();

    [GeneratedRegex(@"^\s*#")]
    private static partial Regex CommentLine();

    [GeneratedRegex(@"^check (\S+) ""\$\{\{ needs\.\1\.result \}\}""$")]
    private static partial Regex CheckLine();

    [GeneratedRegex(@"^[\w./-]+@[0-9a-f]{40} # v")]
    private static partial Regex PinnedUses();

    // The checks, each taking the workflow lines.

    private static void CheckBuildTest(string[] lines)
    {
        var job = Job(lines, "build-test");
        Assert.Equal("always()", JobValue(job, "if"));
        Assert.Equal(Others.Order().ToList(), NeedsOf(job).Order().ToList());

        var step = Assert.Single(Steps(job), s => StepValue(s, "name") == "Require every CI job to succeed");
        var run = Run(step);
        // The comparison itself: the helper flags anything that is not "success" and the script exits with that flag.
        Assert.Single(run, l => l == HelperLine);
        Assert.Single(run, l => l == "exit $fail");
        Assert.Equal("exit $fail", run[^1]);
        var checks = run.Where(l => CheckLine().IsMatch(l)).Select(l => CheckLine().Match(l).Groups[1].Value).ToList();
        Assert.Equal(Others.Order().ToList(), checks.Order().ToList());
        // Every "check" call is a well-formed pair; a mismatched job and result name fails the pattern.
        Assert.Equal(Others.Length, run.Count(l => l.StartsWith("check ", StringComparison.Ordinal)));
    }

    private static void CheckTakesBundle(string[] lines, string name)
    {
        var job = Job(lines, name);
        Assert.Equal(["ui-build"], NeedsOf(job));
        var download = Assert.Single(Steps(job), s => (StepValue(s, "uses") ?? "").StartsWith("actions/download-artifact@", StringComparison.Ordinal));
        var with = With(download);
        Assert.Equal("ui-dist", with.GetValueOrDefault("name"));
        Assert.Equal("src/Ui/dist", with.GetValueOrDefault("path"));

        var upload = Assert.Single(Steps(Job(lines, "ui-build")), s => (StepValue(s, "uses") ?? "").StartsWith("actions/upload-artifact@", StringComparison.Ordinal));
        Assert.Equal("ui-dist", With(upload).GetValueOrDefault("name"));
    }

    private static void CheckPinned(string[] lines)
    {
        var code = CodeLines(lines);
        var stepLevel = new List<string>();
        foreach (var name in JobKeys(lines))
            foreach (var step in Steps(Job(lines, name)))
                if (StepValue(step, "uses") is { } uses) stepLevel.Add(uses);
        Assert.NotEmpty(stepLevel);
        foreach (var uses in stepLevel)
            Assert.Matches(PinnedUses(), uses);
        // No "uses:" hides anywhere else (a run block, a with value, a job-level key).
        Assert.Equal(code.Count(l => l.Contains("uses:", StringComparison.Ordinal)), stepLevel.Count);
    }

    [Fact]
    public void Ci_has_the_four_jobs_and_the_build_test_aggregate()
    {
        Assert.Equal(["build-test", "dotnet", "gui", "ui-build", "ui-test"], JobKeys(WorkflowLines()).Order().ToList());
    }

    [Fact]
    public void Build_test_fails_unless_every_job_succeeded() => CheckBuildTest(WorkflowLines());

    [Theory]
    [InlineData("dotnet")]
    [InlineData("gui")]
    public void Dotnet_and_gui_take_the_bundle_from_ui_build(string name) => CheckTakesBundle(WorkflowLines(), name);

    [Fact]
    public void Every_action_is_pinned_to_a_commit_sha() => CheckPinned(WorkflowLines());

    // Mutation tests: a broken copy of ci.yml must fail the check that guards the broken thing.

    private static List<string> Copy() => WorkflowLines().ToList();

    private static int IndexOf(List<string> lines, string exact, int from = 0)
    {
        var i = lines.FindIndex(from, l => l == exact);
        Assert.True(i >= 0, $"Test setup: '{exact}' not found.");
        return i;
    }

    [Fact]
    public void Mutated_copies_of_build_test_fail()
    {
        // Sanity: the unmutated copy passes.
        CheckBuildTest(Copy().ToArray());

        var commented = Copy();
        commented[IndexOf(commented, "    if: always()")] = "    # if: always()";
        Assert.ThrowsAny<Exception>(() => CheckBuildTest(commented.ToArray()));

        var moved = Copy();
        moved.RemoveAt(IndexOf(moved, "    if: always()"));
        moved.Insert(IndexOf(moved, "      - name: Require every CI job to succeed") + 1, "        if: always()");
        Assert.ThrowsAny<Exception>(() => CheckBuildTest(moved.ToArray()));

        var wrongResult = Copy();
        var gui = IndexOf(wrongResult, "          check gui \"${{ needs.gui.result }}\"");
        wrongResult[gui] = "          check gui \"${{ needs.dotnet.result }}\"";
        Assert.ThrowsAny<Exception>(() => CheckBuildTest(wrongResult.ToArray()));

        var commentedCheck = Copy();
        var ui = IndexOf(commentedCheck, "          check ui-test \"${{ needs.ui-test.result }}\"");
        commentedCheck[ui] = "          # check ui-test \"${{ needs.ui-test.result }}\"";
        Assert.ThrowsAny<Exception>(() => CheckBuildTest(commentedCheck.ToArray()));

        var needsDropped = Copy();
        needsDropped[IndexOf(needsDropped, "    needs: [ui-build, ui-test, dotnet, gui]")] = "    needs: [ui-build, ui-test, dotnet]";
        Assert.ThrowsAny<Exception>(() => CheckBuildTest(needsDropped.ToArray()));

        var exitNotLast = Copy();
        var exit = IndexOf(exitNotLast, "          exit $fail");
        exitNotLast.Insert(exit + 1, "          echo done");
        Assert.ThrowsAny<Exception>(() => CheckBuildTest(exitNotLast.ToArray()));

        var weakHelper = Copy();
        weakHelper[IndexOf(weakHelper, "          " + HelperLine)] = "          check() { echo \"$1: $2\"; }";
        Assert.ThrowsAny<Exception>(() => CheckBuildTest(weakHelper.ToArray()));
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("gui")]
    public void Mutated_copies_of_the_bundle_download_fail(string name)
    {
        CheckTakesBundle(Copy().ToArray(), name);

        var start = IndexOf(Copy(), $"  {name}:");

        var nameMoved = Copy();
        var download = IndexOf(nameMoved, "      - name: Download UI bundle", start);
        var nameLine = IndexOf(nameMoved, "          name: ui-dist", download);
        nameMoved.RemoveAt(nameLine);
        // Park the name on the next step instead, where it is not the download step's input.
        nameMoved.Insert(IndexOf(nameMoved, "      - name: .NET build", download) + 1, "        name: ui-dist");
        Assert.ThrowsAny<Exception>(() => CheckTakesBundle(nameMoved.ToArray(), name));

        var needsChanged = Copy();
        needsChanged[IndexOf(needsChanged, "    needs: [ui-build]", start)] = "    needs: [ui-test]";
        Assert.ThrowsAny<Exception>(() => CheckTakesBundle(needsChanged.ToArray(), name));

        var needsCommented = Copy();
        needsCommented[IndexOf(needsCommented, "    needs: [ui-build]", start)] = "    # needs: [ui-build]";
        Assert.ThrowsAny<Exception>(() => CheckTakesBundle(needsCommented.ToArray(), name));

        var downloadCommented = Copy();
        var uses = IndexOf(downloadCommented, "        uses: actions/download-artifact@9000827ccba6bdab643e8b6fd33ac0654aef8333 # v8.0.2", start);
        downloadCommented[uses] = "        # " + downloadCommented[uses].TrimStart();
        Assert.ThrowsAny<Exception>(() => CheckTakesBundle(downloadCommented.ToArray(), name));
    }

    [Fact]
    public void Mutated_copies_with_unpinned_actions_fail()
    {
        CheckPinned(Copy().ToArray());

        var tagged = Copy();
        var checkout = IndexOf(tagged, "      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1");
        tagged[checkout] = "      - uses: actions/checkout@v7";
        Assert.ThrowsAny<Exception>(() => CheckPinned(tagged.ToArray()));

        var hidden = Copy();
        hidden.Insert(IndexOf(hidden, "        run: npm run build") + 1, "          uses: evil/action@v1");
        Assert.ThrowsAny<Exception>(() => CheckPinned(hidden.ToArray()));
    }
}
