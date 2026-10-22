using System.Xml.Linq;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// RulesCore stays free of persistence, UI, Windows and network references. A deny-list is used on
/// purpose: .NET's own assembly split changes between SDKs, and an allow-list would break on a patch.
/// </summary>
public class RulesCoreBoundaryTests
{
    private static readonly string[] DeniedAssemblyPrefixes =
    [
        "TomeStack.",
        "Microsoft.Data.Sqlite",
        "SQLitePCL",
        "System.Data",
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Windows",
        "Microsoft.Web.WebView2",
        "System.Net.Http",
        "System.Net.Sockets",
    ];

    [Fact]
    public void RulesCore_references_no_TomeStack_or_platform_assembly()
    {
        var referenced = typeof(CharacterCalculator).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(name => DeniedAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(referenced.Count == 0,
            "RulesCore references a forbidden assembly: " + string.Join(", ", referenced));
    }

    [Fact]
    public void RulesCore_project_declares_no_references()
    {
        var declared = LoadProject().Descendants()
            .Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference" or "FrameworkReference")
            .Select(e => e.Name.LocalName + " " + (e.Attribute("Include") ?? e.Attribute("Update"))?.Value)
            .ToList();

        Assert.True(declared.Count == 0,
            "TomeStack.RulesCore.csproj declares references: " + string.Join(", ", declared));
    }

    [Fact]
    public void RulesCore_targets_plain_net()
    {
        var project = LoadProject();
        var frameworks = project.Descendants()
            .Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .Select(e => e.Value.Trim())
            .ToList();

        Assert.NotEmpty(frameworks);
        Assert.DoesNotContain(frameworks, f => f.Contains("-windows", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(project.Descendants(), e => e.Name.LocalName is "UseWPF" or "UseWindowsForms");
    }

    private static XDocument LoadProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TomeStack.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "TomeStack.slnx was not found above " + AppContext.BaseDirectory);
        return XDocument.Load(Path.Combine(dir!.FullName, "src", "RulesCore", "TomeStack.RulesCore.csproj"));
    }
}
