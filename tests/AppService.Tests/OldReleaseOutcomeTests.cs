using System.Security.Cryptography;
using System.Text.Json;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Guards the committed old-release outcome files (spec section 8, "Real older build"). Each
/// <c>tests/RulesFixtures/downconvert/outcomes/&lt;target&gt;.json</c> is written by <c>scripts/old-release-check.ps1</c> and claims that the
/// real older release opened the golden packages. This check ties the committed outcome to the committed bytes (the weekly job is the proof
/// itself): the outcome exists, names the right tag, hashes every golden and <c>expected.json</c> to the committed bytes, lists them all, and records a pass for each package.
/// </summary>
public sealed class OldReleaseOutcomeTests : IDisposable
{
    private const string Downconvert = "tests/RulesFixtures/downconvert";

    private static string? RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
                directory = directory.Parent;
            return directory?.FullName;
        }
    }

    private static bool HasGoldens()
    {
        var root = RepoRoot;
        if (root is null) return false;
        var folder = Path.Combine(root, "tests", "RulesFixtures", "downconvert");
        return Directory.Exists(folder)
            && Directory.GetDirectories(folder, "for-*").Any(d => Directory.GetFiles(d, "*.tomestack.zip").Length > 0);
    }

    /// <summary>xUnit 2 has no runtime skip, so the skip is decided when the test is discovered.</summary>
    public sealed class GoldensFactAttribute : FactAttribute
    {
        public GoldensFactAttribute()
        {
            if (!HasGoldens())
                Skip = "No down-conversion goldens yet (no *.tomestack.zip directly in tests/RulesFixtures/downconvert/for-*/; lane C2 adds them).";
        }
    }

    /// <summary>The full commit each release tag points at; an outcome must record the commit it was run against.</summary>
    private static readonly Dictionary<string, string> TagCommits = new(StringComparer.Ordinal)
    {
        ["v0.5.0"] = "29ac9832e2b6ca6fedce9fa4034bfdea075cd328",
        ["v0.6.0"] = "6403df5c7e8cdd59bc5284843881904bdc314351",
        ["v0.7.0"] = "0ffe703149e2bce8747e26bdf915de163151c20b",
        ["v0.8.0"] = "0752782fbb5290497b147c9fdf369cccaddc8851",
    };

    private const string Advice = "Run scripts/old-release-check.ps1 locally and commit the outcome.";

    /// <summary>Every problem with the committed outcomes of <paramref name="repoRoot"/>; empty when each one matches its goldens.</summary>
    internal static List<string> FindProblems(string repoRoot)
    {
        var problems = new List<string>();
        var folder = Path.Combine(repoRoot, "tests", "RulesFixtures", "downconvert");
        if (!Directory.Exists(folder)) return problems;
        foreach (var forDir in Directory.GetDirectories(folder, "for-*").Order(StringComparer.Ordinal))
        {
            var target = Path.GetFileName(forDir)["for-".Length..];
            // The goldens sit directly in for-<target>/; for-<target>/expected/ holds their unpacked entries, not packages (R-C20).
            var packages = Directory.GetFiles(forDir, "*.tomestack.zip").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList();
            if (packages.Count == 0) continue;

            var outcomePath = Path.Combine(folder, "outcomes", target + ".json");
            var label = $"outcomes/{target}.json";
            if (!File.Exists(outcomePath))
            {
                problems.Add($"{label} is missing.");
                continue;
            }

            JsonElement doc;
            try
            {
                using var parsed = JsonDocument.Parse(File.ReadAllText(outcomePath));
                doc = parsed.RootElement.Clone();
            }
            catch (JsonException)
            {
                problems.Add($"{label} is not valid JSON.");
                continue;
            }

            if (doc.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{label} is not a JSON object.");
                continue;
            }

            if (!doc.TryGetProperty("harnessVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber) || versionNumber != 1)
                problems.Add($"{label}: harnessVersion is not 1.");
            if (!doc.TryGetProperty("tag", out var tag) || tag.ValueKind != JsonValueKind.String || tag.GetString() != "v" + target)
                problems.Add($"{label}: tag is not v{target}.");
            var commit = doc.TryGetProperty("tagCommit", out var tc) && tc.ValueKind == JsonValueKind.String ? tc.GetString() : null;
            if (!TagCommits.TryGetValue("v" + target, out var expectedCommit))
                problems.Add($"{label}: no expected commit is pinned for v{target}; add it to TagCommits in OldReleaseOutcomeTests (git rev-parse v{target}^{{commit}}).");
            else if (!string.Equals(commit, expectedCommit, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{label}: tagCommit is {commit ?? "missing"}, expected {expectedCommit} for v{target}.");

            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.TryGetProperty("inputs", out var inputs) && inputs.ValueKind == JsonValueKind.Array)
            {
                foreach (var input in inputs.EnumerateArray())
                {
                    if (input.ValueKind != JsonValueKind.Object)
                    {
                        problems.Add($"{label}: an inputs entry is not an object.");
                        continue;
                    }
                    var path = input.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                    var sha = input.TryGetProperty("sha256", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                    if (string.IsNullOrEmpty(path) || sha is null)
                    {
                        problems.Add($"{label}: an inputs entry has no path or sha256.");
                        continue;
                    }
                    listed.Add(path);
                    var full = Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                    {
                        problems.Add($"{label}: input {path} does not exist.");
                        continue;
                    }
                    var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full)));
                    if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
                        problems.Add($"{label}: input {path} no longer matches its recorded SHA-256.");
                }
            }
            else
            {
                problems.Add($"{label}: inputs is missing.");
            }

            var results = new Dictionary<string, string>(StringComparer.Ordinal);
            if (doc.TryGetProperty("results", out var resultList) && resultList.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in resultList.EnumerateArray())
                {
                    if (result.ValueKind != JsonValueKind.Object)
                    {
                        problems.Add($"{label}: a results entry is not an object.");
                        continue;
                    }
                    var package = result.TryGetProperty("package", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                    var status = result.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                    if (package is null) continue;
                    results[package] = status ?? "";
                    if (status != "pass")
                        problems.Add($"{label}: {package} is {(string.IsNullOrEmpty(status) ? "without a status" : status)}, not pass.");
                }
            }

            var expectedRelative = $"{Downconvert}/for-{target}/expected.json";
            if (!File.Exists(Path.Combine(forDir, "expected.json")))
                problems.Add($"{label}: {expectedRelative} does not exist; the proof needs an expectation.");
            else if (!listed.Contains(expectedRelative))
                problems.Add($"{label}: {expectedRelative} is not listed in inputs.");

            foreach (var package in packages)
            {
                if (!listed.Contains($"{Downconvert}/for-{target}/{package}"))
                    problems.Add($"{label}: golden {package} is not listed in inputs.");
                if (!results.ContainsKey(package))
                    problems.Add($"{label}: golden {package} has no result.");
            }
        }
        return problems;
    }

    [GoldensFact]
    public void Committed_outcome_matches_the_golden_inputs()
    {
        var problems = FindProblems(RepoRoot!);
        Assert.True(problems.Count == 0, string.Join(" ", problems) + " " + Advice);
    }

    [Fact]
    public void Every_offered_ladder_row_has_goldens_and_an_outcome()
    {
        var root = RepoRoot!;
        var folder = Path.Combine(root, "tests", "RulesFixtures", "downconvert");
        var problems = new List<string>();
        foreach (var row in TomeStack.AppService.Packages.ReleaseLadder.Rows.Where(r => r.OfferedAsTarget))
        {
            var release = row.Release.ToString(3);
            var forDir = Path.Combine(folder, "for-" + release);
            if (!Directory.Exists(forDir) || Directory.GetFiles(forDir, "*.tomestack.zip").Length == 0)
                problems.Add($"{release} is offered as a target but has no *.tomestack.zip under {Downconvert}/for-{release}/.");
            if (!File.Exists(Path.Combine(folder, "outcomes", release + ".json")))
                problems.Add($"{release} is offered as a target but {Downconvert}/outcomes/{release}.json is missing.");
        }
        Assert.True(problems.Count == 0, string.Join(" ", problems) + " An offered row needs goldens and a real-build outcome (scripts/old-release-check.ps1).");
    }

    [Fact]
    public void An_outcome_with_the_wrong_tag_commit_is_a_problem()
    {
        Build();
        Rewrite(t => t.Replace(TagCommits["v0.6.0"], "0123456789abcdef0123456789abcdef01234567", StringComparison.Ordinal));
        Assert.Contains(FindProblems(_root), p => p.Contains("tagCommit"));
    }

    // --- the checker itself, on a temporary tree ---

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tomestack-outcome-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>A target 0.6.0 tree with one golden, an expected.json and a matching passing outcome.</summary>
    private string Build(string status = "pass", bool writeOutcome = true, bool listGolden = true)
    {
        var forDir = Path.Combine(_root, "tests", "RulesFixtures", "downconvert", "for-0.6.0");
        Directory.CreateDirectory(forDir);
        var golden = Path.Combine(forDir, "compat-hero.tomestack.zip");
        File.WriteAllBytes(golden, [1, 2, 3, 4]);
        var expectedJson = Path.Combine(forDir, "expected.json");
        File.WriteAllText(expectedJson, "{\"characters\":{}}");
        if (writeOutcome)
        {
            var entries = new List<object>();
            if (listGolden) entries.Add(new { path = $"{Downconvert}/for-0.6.0/compat-hero.tomestack.zip", sha256 = Sha(golden) });
            entries.Add(new { path = $"{Downconvert}/for-0.6.0/expected.json", sha256 = Sha(expectedJson) });
            var outcome = new
            {
                harnessVersion = 1,
                tag = "v0.6.0",
                tagCommit = TagCommits["v0.6.0"],
                inputs = entries,
                results = new[] { new { package = "compat-hero.tomestack.zip", status, detail = "" } },
            };
            var outcomes = Path.Combine(_root, "tests", "RulesFixtures", "downconvert", "outcomes");
            Directory.CreateDirectory(outcomes);
            File.WriteAllText(Path.Combine(outcomes, "0.6.0.json"), JsonSerializer.Serialize(outcome));
        }
        return _root;
    }

    [Fact]
    public void A_matching_passing_outcome_has_no_problems()
    {
        Assert.Empty(FindProblems(Build()));
    }

    [Fact]
    public void A_changed_byte_in_a_golden_is_a_hash_problem()
    {
        Build();
        File.WriteAllBytes(Path.Combine(_root, "tests", "RulesFixtures", "downconvert", "for-0.6.0", "compat-hero.tomestack.zip"), [1, 2, 3, 5]);
        var problems = FindProblems(_root);
        Assert.Contains(problems, p => p.Contains("compat-hero.tomestack.zip") && p.Contains("SHA-256"));
    }

    [Fact]
    public void A_golden_missing_from_inputs_is_a_problem()
    {
        var problems = FindProblems(Build(listGolden: false));
        Assert.Contains(problems, p => p.Contains("compat-hero.tomestack.zip") && p.Contains("not listed"));
    }

    [Fact]
    public void A_failing_result_is_a_problem()
    {
        var problems = FindProblems(Build(status: "fail"));
        Assert.Contains(problems, p => p.Contains("compat-hero.tomestack.zip") && p.Contains("fail"));
    }

    [Fact]
    public void A_missing_outcome_is_a_problem()
    {
        var problems = FindProblems(Build(writeOutcome: false));
        Assert.Contains(problems, p => p.Contains("outcomes/0.6.0.json is missing"));
    }

    private string OutcomePath => Path.Combine(_root, "tests", "RulesFixtures", "downconvert", "outcomes", "0.6.0.json");

    private void Rewrite(Func<string, string> change) => File.WriteAllText(OutcomePath, change(File.ReadAllText(OutcomePath)));

    [Fact]
    public void A_changed_byte_in_expected_json_is_a_hash_problem()
    {
        Build();
        File.WriteAllText(Path.Combine(_root, "tests", "RulesFixtures", "downconvert", "for-0.6.0", "expected.json"), "{\"characters\":{\"x\":{}}}");
        Assert.Contains(FindProblems(_root), p => p.Contains("expected.json") && p.Contains("SHA-256"));
    }

    [Fact]
    public void An_expected_json_missing_from_inputs_is_a_problem()
    {
        Build();
        Rewrite(t => t.Replace("for-0.6.0/expected.json", "for-0.6.0/other.json", StringComparison.Ordinal));
        var problems = FindProblems(_root);
        Assert.Contains(problems, p => p.Contains("expected.json") && p.Contains("not listed"));
    }

    [Fact]
    public void A_missing_expected_json_is_a_problem()
    {
        Build();
        File.Delete(Path.Combine(_root, "tests", "RulesFixtures", "downconvert", "for-0.6.0", "expected.json"));
        Assert.Contains(FindProblems(_root), p => p.Contains("expected.json") && p.Contains("does not exist"));
    }

    [Fact]
    public void A_wrong_harness_version_is_a_problem()
    {
        Build();
        Rewrite(t => t.Replace("\"harnessVersion\":1", "\"harnessVersion\":2", StringComparison.Ordinal));
        Assert.Contains(FindProblems(_root), p => p.Contains("harnessVersion"));
    }

    [Fact]
    public void A_wrong_tag_is_a_problem()
    {
        Build();
        Rewrite(t => t.Replace("\"tag\":\"v0.6.0\"", "\"tag\":\"v0.5.0\"", StringComparison.Ordinal));
        Assert.Contains(FindProblems(_root), p => p.Contains("tag is not v0.6.0"));
    }

    [Fact]
    public void A_golden_without_a_result_is_a_problem()
    {
        Build();
        Rewrite(t => t.Replace("compat-hero.tomestack.zip\",\"status", "other.tomestack.zip\",\"status", StringComparison.Ordinal));
        Assert.Contains(FindProblems(_root), p => p.Contains("compat-hero.tomestack.zip") && p.Contains("no result"));
    }

    [Fact]
    public void An_unparseable_outcome_is_a_problem_not_an_exception()
    {
        Build();
        File.WriteAllText(OutcomePath, "{ not json");
        Assert.Contains(FindProblems(_root), p => p.Contains("not valid JSON"));
    }

    [Fact]
    public void Non_object_entries_and_a_fractional_version_are_problems_not_exceptions()
    {
        Build();
        File.WriteAllText(OutcomePath, "{\"harnessVersion\":1.5,\"tag\":\"v0.6.0\",\"inputs\":[1],\"results\":[\"x\"]}");
        var problems = FindProblems(_root);
        Assert.Contains(problems, p => p.Contains("harnessVersion"));
        Assert.Contains(problems, p => p.Contains("inputs entry is not an object"));
        Assert.Contains(problems, p => p.Contains("results entry is not an object"));
    }

    [Fact]
    public void A_target_without_goldens_needs_no_outcome()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tests", "RulesFixtures", "downconvert", "for-0.5.0"));
        Assert.Empty(FindProblems(_root));
    }

    [Fact]
    public void A_package_under_expected_is_not_a_golden()
    {
        // for-<target>/expected/ holds unpacked entries; a zip there is not one of the packages the old release opens (R-C20).
        var expectedDir = Path.Combine(_root, "tests", "RulesFixtures", "downconvert", "for-0.5.0", "expected");
        Directory.CreateDirectory(expectedDir);
        File.WriteAllBytes(Path.Combine(expectedDir, "stray.tomestack.zip"), [1]);
        Assert.Empty(FindProblems(_root));
    }
}
