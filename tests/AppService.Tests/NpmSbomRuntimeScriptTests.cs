using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Runs <c>scripts/npm-sbom-runtime.mjs</c> with node on tiny inline CycloneDX files: development-only components are dropped and pruned
/// from the dependency graph, and an empty or runtime-less input fails instead of writing an empty SBOM.
/// </summary>
public sealed class NpmSbomRuntimeScriptTests
{
    private const string Fixture = """
        {
          "bomFormat": "CycloneDX",
          "metadata": { "component": { "bom-ref": "root", "name": "app" } },
          "components": [
            { "bom-ref": "react", "name": "react", "properties": [ { "name": "cdx:npm:package:development", "value": "false" } ] },
            { "bom-ref": "vitest", "name": "vitest", "properties": [ { "name": "cdx:npm:package:development", "value": "true" } ] }
          ],
          "dependencies": [
            { "ref": "root", "dependsOn": [ "react", "vitest" ] },
            { "ref": "react", "dependsOn": [] },
            { "ref": "vitest", "dependsOn": [ "react" ] }
          ]
        }
        """;

    private const string DevOnly = """
        {
          "metadata": { "component": { "bom-ref": "root", "name": "app" } },
          "components": [ { "bom-ref": "vitest", "name": "vitest", "properties": [ { "name": "cdx:npm:package:development", "value": "true" } ] } ],
          "dependencies": [ { "ref": "root", "dependsOn": [ "vitest" ] } ]
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

    /// <summary>Writes <paramref name="input"/> to a temp file, runs the script and returns the exit code and the output file's bytes (null if none was written).</summary>
    private static (int Exit, byte[]? Output, string Stderr) Run(string input)
    {
        var temp = Path.Combine(Path.GetTempPath(), "ts-sbom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var inPath = Path.Combine(temp, "in.json");
            var outPath = Path.Combine(temp, "out.json");
            File.WriteAllText(inPath, input, new UTF8Encoding(false));

            var start = new ProcessStartInfo("node")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { Path.Combine(RepoRoot, "scripts", "npm-sbom-runtime.mjs"), inPath, "--out", outPath })
                start.ArgumentList.Add(arg);

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("npm-sbom-runtime.mjs did not finish within 60 s.");
            }
            _ = stdout.Result;
            return (process.ExitCode, File.Exists(outPath) ? File.ReadAllBytes(outPath) : null, stderr.Result);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void DropsDevelopmentComponentsAndPrunesTheGraph()
    {
        var (exit, output, stderr) = Run(Fixture);
        Assert.True(exit == 0, stderr);
        Assert.NotNull(output);
        Assert.False(output.Length >= 3 && output[0] == 0xEF && output[1] == 0xBB && output[2] == 0xBF, "no BOM expected");

        var bom = JsonNode.Parse(output)!;
        var names = bom["components"]!.AsArray().Select(c => (string)c!["name"]!).ToArray();
        Assert.Equal(["react"], names);

        var dependencies = bom["dependencies"]!.AsArray();
        Assert.DoesNotContain(dependencies, d => (string)d!["ref"]! == "vitest");
        var root = dependencies.Single(d => (string)d!["ref"]! == "root");
        Assert.Equal(["react"], root!["dependsOn"]!.AsArray().Select(r => (string)r!).ToArray());
    }

    [Fact]
    public void EmptyInputFails()
    {
        var (exit, output, _) = Run("");
        Assert.NotEqual(0, exit);
        Assert.Null(output);
    }

    [Fact]
    public void ZeroRuntimeComponentsFails()
    {
        var (exit, output, stderr) = Run(DevOnly);
        Assert.NotEqual(0, exit);
        Assert.Null(output);
        Assert.Contains("No runtime npm components", stderr);
    }
}
