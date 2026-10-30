using System.Reflection;
using System.Text.RegularExpressions;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// CharacterCalculator is split into partial files (Calculation.*.cs). C# runs static initializers in textual order
/// within one file but leaves the order across partial files unspecified, and Specs = BuildSpecs() reads AbilityNames
/// and Skills. So every static initializer stays in Calculation.cs.
/// </summary>
public class CalculationSplitTests
{
    [Fact]
    public void Static_initialisers_live_in_one_file()
    {
        var names = typeof(CharacterCalculator)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(f => !f.IsLiteral)
            .Select(f => f.Name.StartsWith('<') ? f.Name[1..f.Name.IndexOf('>')] : f.Name)
            .ToList();
        Assert.Contains("Specs", names);

        var dir = Path.Combine(RepoRoot(), "src", "RulesCore");
        var shell = File.ReadAllText(Path.Combine(dir, "Calculation.cs"));
        var parts = Directory.GetFiles(dir, "Calculation.*.cs").ToDictionary(f => Path.GetFileName(f), File.ReadAllText);
        Assert.NotEmpty(parts);

        foreach (var name in names)
        {
            var declaration = new Regex($@"\bstatic\b[^;=]*\b{Regex.Escape(name)}\s*(=|\{{\s*get;)");
            Assert.True(declaration.IsMatch(shell), $"{name} is not declared in Calculation.cs");
            foreach (var (file, text) in parts)
            {
                Assert.False(declaration.IsMatch(text), $"{name} is declared in {file}; keep static initializers in Calculation.cs");
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TomeStack.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "TomeStack.slnx was not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
