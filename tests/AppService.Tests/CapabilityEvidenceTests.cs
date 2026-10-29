using System.Text.RegularExpressions;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// ADR-020, D66: every capability's <c>Evidence</c> names a test class that exists. This project does not reference
/// RulesCore.Tests (and must not: the build graph stays as it is), so classes of that project are found in its source files,
/// located from the repository root the way the other tests here find fixtures.
/// </summary>
public class CapabilityEvidenceTests
{
    private static readonly Regex ClassDeclaration = new(@"^\s*(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+(?<name>\w+)", RegexOptions.Multiline);

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
            directory = directory.Parent;
        Assert.True(directory is not null, "TomeStack.slnx was not found above " + AppContext.BaseDirectory);
        return directory!.FullName;
    }

    private static HashSet<string> TestClasses()
    {
        var names = typeof(CapabilityEvidenceTests).Assembly.GetTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var rulesCoreTests = Path.Combine(Repository(), "tests", "RulesCore.Tests");
        foreach (var file in Directory.EnumerateFiles(rulesCoreTests, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            foreach (Match match in ClassDeclaration.Matches(File.ReadAllText(file)))
                names.Add(match.Groups["name"].Value);
        }
        return names;
    }

    [Fact]
    public void Every_evidence_names_an_existing_test_class()
    {
        var classes = TestClasses();

        foreach (var entry in CapabilityRegistry.Entries)
            Assert.True(classes.Contains(entry.Evidence), $"{entry.EffectType}{(entry.Field is null ? "" : "." + entry.Field)}: no test class named '{entry.Evidence}'");
    }
}
