using System.IO;

namespace TomeStack.DesktopShell.Tests;

/// <summary>
/// Release checklist §2 option (b): the worker compiles the C#/WinRT projection it needs into its own assembly
/// (<c>CsWinRTEmbedded</c>), so no Windows SDK DLL ships. The deps files are rewritten on every build, unlike the
/// folder, where a DLL from an older build can linger. <c>scripts/pack-installer.ps1</c> checks the publish folder.
/// </summary>
public class PublishedOutputTests
{
    private static readonly string[] SdkFiles = ["Microsoft.Windows.SDK.NET.dll", "WinRT.Runtime.dll"];

    [Theory]
    [InlineData("TomeStack.deps.json")]
    [InlineData("TomeStack.ImportWorker.Host.deps.json")]
    public void No_windows_sdk_projection_ships_with_the_app(string depsFile)
    {
        var path = Path.Combine(ShellOutput(), depsFile);
        Assert.True(File.Exists(path), path + " was not found; build the shell first.");

        var deps = File.ReadAllText(path);
        Assert.DoesNotContain("Microsoft.Windows.SDK.NET.Ref", deps, StringComparison.OrdinalIgnoreCase);
        foreach (var file in SdkFiles)
            Assert.DoesNotContain(file, deps, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The shell's pinned output folder, <c>src/DesktopShell/bin/&lt;config&gt;/net10.0-windows/</c>, for this test's configuration.</summary>
    private static string ShellOutput()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TomeStack.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "TomeStack.slnx was not found above " + AppContext.BaseDirectory);

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        return Path.Combine(dir!.FullName, "src", "DesktopShell", "bin", configuration, "net10.0-windows");
    }
}
