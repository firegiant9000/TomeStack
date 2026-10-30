using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D85 part 1, lane C1-4: a library saved "for an older TomeStack" is pinned byte for byte, entry by entry
/// (<c>tests/RulesFixtures/downconvert/&lt;folder&gt;/expected/</c>). Set <c>TOMESTACK_REGENERATE_GOLDENS=1</c> to rewrite them
/// from the implementation, then review them by eye (see the README beside them).
/// </summary>
public class DownConversionGoldenTests
{
    private static readonly Regex AppVersion = new("(\"appVersion\"\\s*:\\s*\")[^\"]*(\")", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static TheoryData<string, string> Folders => new() { { "for-0.6.0", "0.6.0" }, { "for-0.5.0", "0.5.0" } };

    private static string SourceFolder(string folder)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("The repository root was not found."), "tests", "RulesFixtures", "downconvert", folder);
    }

    /// <summary>The build's version varies, so it is written as <c>{appVersion}</c> in the manifest (the README says so).</summary>
    private static string Normalise(string entry, byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n"); // the repository stores LF; the writer uses the platform's line ending
        return entry == "manifest.json" ? AppVersion.Replace(text, "$1{appVersion}$2") : text;
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void Golden_copy_matches_byte_for_byte(string folder, string target)
    {
        using var app = new TempApp();
        var library = $"downconvert/{folder}/library";
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", library, "pack.json")))
            app.AddPack($"{library}/pack.json");
        var id = app.App.SaveCharacter(TempApp.LoadFixture<Character>($"{library}/character.json")).Character.Id;
        var dispatcher = new CommandDispatcher(app.App);
        var payload = $$"""{"characterIds":["{{id}}"],"purpose":"share","target":"{{target}}"}""";

        var preview = Dispatch(dispatcher, "package.exportPreview", payload);
        var hash = preview.GetProperty("changesHash").GetString()!;
        var export = Dispatch(dispatcher, "package.export", payload.Replace("}", $",\"changesHash\":\"{hash}\"}}", StringComparison.Ordinal));
        var changes = JsonSerializer.Serialize(preview.GetProperty("changes"), Indented).ReplaceLineEndings("\n");

        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using (var zip = new ZipArchive(new MemoryStream(Convert.FromBase64String(export.GetProperty("base64").GetString()!))))
            foreach (var entry in zip.Entries.Where(e => e.Length > 0 || !e.FullName.EndsWith('/')))
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                entries[entry.FullName] = buffer.ToArray();
            }

        var expected = Path.Combine(SourceFolder(folder), "expected");
        if (Environment.GetEnvironmentVariable("TOMESTACK_REGENERATE_GOLDENS") == "1")
        {
            if (Directory.Exists(expected))
                Directory.Delete(expected, recursive: true);
            foreach (var (name, bytes) in entries)
            {
                var path = Path.Combine(expected, "entries", name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Normalise(name, bytes), new UTF8Encoding(false));
            }

            File.WriteAllText(Path.Combine(expected, "changes.json"), changes + "\n", new UTF8Encoding(false));
            Assert.Fail($"Regenerated the expected files under {folder}; review them by eye against spec section 2, unset TOMESTACK_REGENERATE_GOLDENS and run again.");
        }

        var expectedEntries = Path.Combine(expected, "entries");
        Assert.True(Directory.Exists(expectedEntries), $"No expected/ for {folder}; run once with TOMESTACK_REGENERATE_GOLDENS=1 and review it.");
        Assert.Equal(
            Directory.EnumerateFiles(expectedEntries, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(expectedEntries, f).Replace('\\', '/')).Order(StringComparer.Ordinal),
            entries.Keys);
        foreach (var (name, bytes) in entries)
            Assert.Equal(File.ReadAllText(Path.Combine(expectedEntries, name.Replace('/', Path.DirectorySeparatorChar))), Normalise(name, bytes));
        Assert.Equal(File.ReadAllText(Path.Combine(expected, "changes.json")).TrimEnd('\n'), changes);

        using var manifest = JsonDocument.Parse(entries["manifest.json"]);
        Assert.Equal("", SchemaTests.Validate("package-manifest", manifest.RootElement));
        Assert.Equal(target, manifest.RootElement.GetProperty("madeFor").GetString());
    }

    private static JsonElement Dispatch(CommandDispatcher dispatcher, string command, string payload)
    {
        var response = JsonDocument.Parse(dispatcher.Dispatch($$"""{"id":"1","command":"{{command}}","payload":{{payload}}}""")).RootElement.Clone();
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        return response.GetProperty("result");
    }
}
