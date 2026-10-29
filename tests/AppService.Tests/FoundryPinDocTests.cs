using System.Text.RegularExpressions;
using TomeStack.AppService.Exports;

namespace TomeStack.AppService.Tests;

/// <summary>
/// F2 (10x addendum): the weekly job reads the Foundry pin from <see cref="FoundryDnd5e"/>, so the adapter doc must quote the
/// same pair. Every dnd5e version, release tag and core version (and core generation, "Foundry 14") the doc names is the pinned one; a doc that discusses a newer
/// release moves the pin in the same change.
/// </summary>
public partial class FoundryPinDocTests
{
    private static string Doc()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, "docs", "features", "export-adapters.md"));
    }

    [Fact]
    public void The_adapter_doc_quotes_the_pinned_core_and_dnd5e_versions()
    {
        var doc = Doc();
        Assert.Contains($"Foundry core {FoundryDnd5e.CoreVersion} with dnd5e {FoundryDnd5e.SystemVersion}", doc, StringComparison.Ordinal);
        Assert.Contains($"`release-{FoundryDnd5e.SystemVersion}`", doc, StringComparison.Ordinal);

        var systems = SystemVersion().Matches(doc).Select(m => m.Groups["v"].Value).Distinct().ToList();
        var tags = ReleaseTag().Matches(doc).Select(m => m.Groups["v"].Value).Distinct().ToList();
        var cores = CoreVersion().Matches(doc).Select(m => m.Groups["v"].Value).Distinct().ToList();
        Assert.Equal([FoundryDnd5e.SystemVersion], systems);
        Assert.Equal([FoundryDnd5e.SystemVersion], tags);
        Assert.Equal([FoundryDnd5e.CoreVersion], cores);
        // "verified 14", "a real Foundry 14 world": the core generation is the pin's major.
        var majors = CoreMajor().Matches(doc).Select(m => m.Groups["v"].Value).Distinct().ToList();
        Assert.NotEmpty(majors);
        Assert.Equal([FoundryDnd5e.CoreVersion.Split('.')[0]], majors);
    }

    [GeneratedRegex(@"(?:\bverified|\bFoundry) (?<v>\d+)\b(?!\.)")]
    private static partial Regex CoreMajor();

    [GeneratedRegex(@"dnd5e (?<v>\d+\.\d+\.\d+)")]
    private static partial Regex SystemVersion();

    [GeneratedRegex(@"release-(?<v>\d+\.\d+\.\d+)")]
    private static partial Regex ReleaseTag();

    [GeneratedRegex(@"\bcore(?:Version`?)? (?<v>\d+\.\d+)\b")]
    private static partial Regex CoreVersion();
}
