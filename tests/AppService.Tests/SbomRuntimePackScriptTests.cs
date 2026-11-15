using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Runs <c>scripts/sbom-add-runtime.mjs</c> with node on tiny inline fixtures: the two bundled .NET runtime packs are added from the
/// published deps.json, a missing pack or deps.json fails, and a second run does not duplicate them.
/// </summary>
public sealed class SbomRuntimePackScriptTests
{
    private const string Sbom = """
        {
          "bomFormat": "CycloneDX",
          "components": [ { "type": "library", "bom-ref": "pkg:nuget/Velopack@1.0.0", "name": "Velopack", "version": "1.0.0", "purl": "pkg:nuget/Velopack@1.0.0" } ]
        }
        """;

    private const string Deps = """
        {
          "libraries": {
            "TomeStack/1.0.0": { "type": "project" },
            "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.1": { "type": "runtimepack" },
            "runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.2": { "type": "runtimepack" }
          }
        }
        """;

    private const string DepsWithoutDesktop = """
        {
          "libraries": {
            "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.1": { "type": "runtimepack" }
          }
        }
        """;

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

    private static int Node(string sbom, string deps, string output, out string stderr)
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { Path.Combine(RepoRoot, "scripts", "sbom-add-runtime.mjs"), sbom, deps, "--out", output })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var err = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("sbom-add-runtime.mjs did not finish within 60 s.");
        }
        _ = stdout.Result;
        stderr = err.Result;
        return process.ExitCode;
    }

    /// <summary>Runs the script <paramref name="times"/> times, each run reading the previous output; returns the last exit code and output bytes.</summary>
    private static (int Exit, byte[]? Output, string Stderr) Run(string sbom, string? deps, int times = 1)
    {
        var temp = Path.Combine(Path.GetTempPath(), "ts-sbomrt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var sbomPath = Path.Combine(temp, "sbom.json");
            var depsPath = Path.Combine(temp, "TomeStack.deps.json");
            var outPath = Path.Combine(temp, "out.json");
            File.WriteAllText(sbomPath, sbom, new UTF8Encoding(false));
            if (deps is not null)
                File.WriteAllText(depsPath, deps, new UTF8Encoding(false));

            var exit = 0;
            var stderr = "";
            for (var i = 0; i < times; i++)
            {
                exit = Node(sbomPath, depsPath, outPath, out stderr);
                if (exit != 0)
                    break;
                File.Copy(outPath, sbomPath, overwrite: true);
            }
            return (exit, File.Exists(outPath) ? File.ReadAllBytes(outPath) : null, stderr);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void AddsBothRuntimePacksWithPurlAndVersion()
    {
        var (exit, output, stderr) = Run(Sbom, Deps);
        Assert.True(exit == 0, stderr);
        Assert.NotNull(output);
        Assert.False(output.Length >= 3 && output[0] == 0xEF && output[1] == 0xBB && output[2] == 0xBF, "no BOM expected");

        var components = JsonNode.Parse(output)!["components"]!.AsArray();
        Assert.Equal(3, components.Count);
        var core = components.Single(c => (string)c!["name"]! == "Microsoft.NETCore.App.Runtime.win-x64")!;
        Assert.Equal("10.0.1", (string)core["version"]!);
        Assert.Equal("pkg:nuget/Microsoft.NETCore.App.Runtime.win-x64@10.0.1", (string)core["purl"]!);
        Assert.Equal((string)core["purl"]!, (string)core["bom-ref"]!);
        Assert.Equal("library", (string)core["type"]!);
        var desktop = components.Single(c => (string)c!["name"]! == "Microsoft.WindowsDesktop.App.Runtime.win-x64")!;
        Assert.Equal("pkg:nuget/Microsoft.WindowsDesktop.App.Runtime.win-x64@10.0.2", (string)desktop["purl"]!);
    }

    [Fact]
    public void MissingRuntimePackFails()
    {
        var (exit, output, stderr) = Run(Sbom, DepsWithoutDesktop);
        Assert.NotEqual(0, exit);
        Assert.Null(output);
        Assert.Contains("Microsoft.WindowsDesktop.App.Runtime.win-x64", stderr);
    }

    [Fact]
    public void MissingDepsFileFails()
    {
        var (exit, output, stderr) = Run(Sbom, deps: null);
        Assert.NotEqual(0, exit);
        Assert.Null(output);
        Assert.Contains("deps.json", stderr);
    }

    [Fact]
    public void RunningTwiceDoesNotDuplicate()
    {
        var (exit, output, stderr) = Run(Sbom, Deps, times: 2);
        Assert.True(exit == 0, stderr);
        Assert.Equal(3, JsonNode.Parse(output!)!["components"]!.AsArray().Count);
    }
}
