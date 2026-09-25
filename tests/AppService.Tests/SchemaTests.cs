using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>docs/schemas describe the stored and exchanged JSON; fixtures and real exports must conform.</summary>
public class SchemaTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "RulesFixtures");

    /// <summary>JsonSchema.Net registers each <c>$id</c> globally and refuses to load it twice.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, JsonSchema> Loaded = new(StringComparer.Ordinal);

    /// <summary>Picks <c>{kind}.v{schemaVersion}.schema.json</c>; documents without a version are v1.</summary>
    internal static string Validate(string kind, JsonElement document)
    {
        var version = document.TryGetProperty(kind == "package-manifest" ? "formatVersion" : "schemaVersion", out var v) ? v.GetInt32() : 1;
        var schema = Loaded.GetOrAdd($"{kind}.v{version}.schema.json", name => JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schemas", name))));
        var result = schema.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        if (result.IsValid)
            return "";
        return string.Join("; ", (result.Details ?? []).Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key} {e.Value}")));
    }

    private static void AssertValid(string kind, JsonElement document, string label)
    {
        var errors = Validate(kind, document);
        Assert.True(errors.Length == 0, $"{label} does not match {kind} schema: {errors}");
    }

    public static TheoryData<string> CharacterFixtures() =>
        [.. Directory.GetFiles(Path.Combine(FixtureRoot, "characters"), "*.json").Select(f => Path.GetFileName(f))];

    [Theory]
    [MemberData(nameof(CharacterFixtures))]
    public void Character_fixtures_match_the_character_schema(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "characters", file)));
        AssertValid("character", document.RootElement, file);
    }

    public static TheoryData<string> PackFixtures() =>
        [.. Directory.GetFiles(FixtureRoot, "fixture-pack*.json").Select(f => Path.GetFileName(f))];

    [Theory]
    [MemberData(nameof(PackFixtures))]
    public void Fixture_pack_sources_and_revisions_match_their_schemas(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureRoot, file)));
        foreach (var source in document.RootElement.GetProperty("sources").EnumerateArray())
            AssertValid("source", source, $"{file} source {source.GetProperty("id")}");
        foreach (var revision in document.RootElement.GetProperty("revisions").EnumerateArray())
            AssertValid("content-revision", revision, $"{file} revision {revision.GetProperty("name")}");
    }

    [Fact]
    public void Every_entry_and_the_manifest_of_an_exported_package_match_their_schemas()
    {
        using var temp = new TempApp();
        var ids = new[] { "srd51-quickfoot.json", "srd521-courier.json" }
            .Select(f => temp.App.SaveCharacter(TempApp.LoadFixture<Character>($"characters/{f}")).Character.Id)
            .ToList();
        var package = temp.App.ExportCharacters(ids).Content;

        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var kinds = new Dictionary<string, string> { ["sources"] = "source", ["content"] = "content-revision", ["characters"] = "character" };
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var kind = entry.FullName == "manifest.json" ? "package-manifest" : kinds[entry.FullName[..entry.FullName.IndexOf('/', StringComparison.Ordinal)]];
            AssertValid(kind, document.RootElement, entry.FullName);
        }
        Assert.Contains(zip.Entries, e => e.FullName.StartsWith("content/", StringComparison.Ordinal));
    }

    [Fact]
    public void Schemas_are_not_vacuous()
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "characters", "srd51-quickfoot.json")))!;
        node["baseAbilities"]!["dex"] = 40;
        node["rulesFamily"] = "5e";

        using var document = JsonDocument.Parse(node.ToJsonString());
        var errors = Validate("character", document.RootElement);

        Assert.Contains("/baseAbilities/dex", errors, StringComparison.Ordinal);
        Assert.Contains("/rulesFamily", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("characters", "character")]
    [InlineData("content", "content")]
    public void Package_entry_with_a_newer_schema_version_is_refused_with_a_clear_diagnostic(string folder, string kind)
    {
        using var origin = new TempApp();
        var saved = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var package = origin.App.ExportCharacters([saved.Character.Id]).Content;
        var future = PackageEditor.Edit(package, path => path.StartsWith(folder + "/", StringComparison.Ordinal), node => node["schemaVersion"] = 99);

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(future);

        Assert.False(preview.CanApply);
        var errors = preview.Errors.Where(e => e.Code == "package.schema-unsupported").ToList();
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Contains($"{kind} schema v99", e.Message, StringComparison.Ordinal));
        Assert.All(errors, e => Assert.Contains("Update TomeStack", e.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void Newer_content_schema_is_isolated_by_the_calculator_and_newer_character_schema_fails_validation()
    {
        using var temp = new TempApp();
        var character = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        var future = temp.App.Store.ListRevisions().First(r => r.Name == "Fixture Keen Reflexes") with
        {
            RevisionId = Guid.NewGuid(),
            SchemaVersion = 99,
        };
        var catalog = new InMemoryContentCatalog(temp.App.Store.ListSources(), [.. temp.App.Store.ListRevisions(), future]);

        var sheet = CharacterCalculator.Calculate(character with { Pins = [future.Reference] }, catalog);

        Assert.Equal("content.schema-unsupported", Assert.Single(sheet.Diagnostics).Code);
        Assert.Contains((character with { SchemaVersion = 99 }).Validate(), d => d.Code == "character.schema-unsupported");
    }
}

/// <summary>Rewrites package entries and re-signs the manifest so that only the edited content differs.</summary>
internal static class PackageEditor
{
    public static byte[] Edit(byte[] package, Func<string, bool> select, Action<JsonNode> change)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using (var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                files[entry.FullName] = buffer.ToArray();
            }
        }

        var manifest = JsonNode.Parse(files["manifest.json"])!;
        foreach (var path in files.Keys.Where(p => p != "manifest.json" && select(p)).ToList())
        {
            var node = JsonNode.Parse(files[path])!;
            change(node);
            files[path] = Encoding.UTF8.GetBytes(node.ToJsonString(RulesJson.Options) + "\n");
            var listed = manifest["entries"]!.AsArray().Single(e => (string?)e!["path"] == path)!;
            listed["sha256"] = Convert.ToHexStringLower(SHA256.HashData(files[path]));
            listed["size"] = files[path].LongLength;
        }
        files["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString(RulesJson.Options) + "\n");

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in files)
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(bytes);
            }
        }
        return output.ToArray();
    }
}
