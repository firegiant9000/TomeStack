using System.Xml.Linq;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Guards the supply-chain policy (D100): NuGet lock files, locked restore in CI, and NuGet audit that fails the build.
/// </summary>
public sealed class SupplyChainPolicyTests
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

    [Fact]
    public void Directory_build_props_turns_on_lock_files_locked_ci_restore_and_audit()
    {
        var props = XDocument.Load(Path.Combine(RepoRoot, "Directory.Build.props"));
        var elements = props.Descendants().Where(e => e.Parent?.Name.LocalName == "PropertyGroup").ToList();

        XElement Single(string name) =>
            Assert.Single(elements, e => e.Name.LocalName == name);

        Assert.Equal("true", Single("RestorePackagesWithLockFile").Value);
        Assert.Equal("true", Single("RestoreLockedMode").Value);
        Assert.Equal("true", Single("NuGetAudit").Value);
        Assert.Equal("all", Single("NuGetAuditMode").Value);
        Assert.Equal("low", Single("NuGetAuditLevel").Value);

        Assert.Contains("NU1900", Single("WarningsNotAsErrors").Value.Split(';'));

        var group = Single("RestoreLockedMode").Parent!;
        Assert.Equal("'$(CI)' == 'true'", (string?)group.Attribute("Condition") ?? (string?)Single("RestoreLockedMode").Attribute("Condition"));
    }

    /// <summary>
    /// The installer publishes with <c>-r win-x64</c>, which restores the whole project graph with that runtime. Without
    /// <c>&lt;RuntimeIdentifiers&gt;win-x64</c> in each project, the lock file has no runtime and locked restore fails with NU1004.
    /// </summary>
    [Theory]
    [InlineData("src/RulesCore/TomeStack.RulesCore.csproj")]
    [InlineData("src/ImportWorker/TomeStack.ImportWorker.csproj")]
    [InlineData("src/AppService/TomeStack.AppService.csproj")]
    [InlineData("src/ImportWorker.Host/TomeStack.ImportWorker.Host.csproj")]
    [InlineData("src/DesktopShell/TomeStack.DesktopShell.csproj")]
    public void Publish_graph_projects_declare_the_win_x64_runtime_identifier(string project)
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot, project));
        var ids = doc.Descendants("RuntimeIdentifiers").SelectMany(e => e.Value.Split(';'));
        Assert.Contains("win-x64", ids);
    }

    [Fact]
    public void Every_project_has_a_committed_lock_file()
    {
        var missing = new List<string>();
        foreach (var top in new[] { "src", "tests", "benchmarks" })
        {
            var folder = Path.Combine(RepoRoot, top);
            if (!Directory.Exists(folder)) continue;
            foreach (var project in Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepoRoot, project);
                var parts = relative.Split(Path.DirectorySeparatorChar);
                if (parts.Any(p => p is "bin" or "obj" or "node_modules")) continue;
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json"))) missing.Add(relative);
            }
        }
        Assert.True(missing.Count == 0, "Projects without packages.lock.json: " + string.Join(", ", missing));
    }
}
