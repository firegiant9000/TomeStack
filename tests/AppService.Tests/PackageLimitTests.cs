using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>SPEC Q-02: every package limit and structural check rejects the package with its own diagnostic.</summary>
public class PackageLimitTests
{
    /// <summary>Seeded by every data folder (tests/RulesFixtures/fixture-pack.json).</summary>
    private const string SeededSource = "5f0d5000-0000-4000-8000-000000000001";

    private static string Path(string folder, Guid id) => $"{folder}/{id:D}.json";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A zip with a manifest that lists every entry with its real hash, unless <paramref name="listed"/> says otherwise.</summary>
    private static byte[] Package(IEnumerable<(string Path, byte[] Bytes)> entries, Action<JsonObject>? manifest = null, Func<string, bool>? listed = null)
    {
        var files = entries.ToList();
        var node = new JsonObject
        {
            ["format"] = PackageManifest.FormatName,
            ["formatVersion"] = PackageManifest.CurrentFormatVersion,
            ["createdAt"] = "2026-09-24T12:00:00+00:00",
            ["appVersion"] = "0.1.0",
            ["entries"] = new JsonArray([.. files.Where(f => listed?.Invoke(f.Path) ?? true).Select(f => (JsonNode)new JsonObject
            {
                ["path"] = f.Path,
                ["kind"] = "character",
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(f.Bytes)),
                ["size"] = f.Bytes.LongLength,
            })]),
        };
        manifest?.Invoke(node);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "manifest.json", Utf8(node.ToJsonString()));
            foreach (var (path, bytes) in files)
                Add(zip, path, bytes);
        }
        return buffer.ToArray();
    }

    private static void Add(ZipArchive zip, string path, byte[] bytes)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
    }

    private static byte[] CharacterJson(Action<JsonObject>? change = null)
    {
        var node = JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "characters", "srd51-quickfoot.json")))!.AsObject();
        change?.Invoke(node);
        return Utf8(node.ToJsonString());
    }

    private static Guid CharacterId => TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json").Id;

    private static void AssertRejected(byte[] package, string code)
    {
        using var app = new TempApp();
        var preview = app.App.PreviewImport(package);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.Code == code);
        Assert.Throws<PackageException>(() => app.App.ApplyImport(package));
        Assert.Empty(app.App.ListCharacters());
    }

    [Fact]
    public void Package_over_the_size_limit_is_rejected_before_it_is_opened() =>
        AssertRejected(new byte[PackageService.MaxPackageBytes + 1], "package.too-large");

    [Fact]
    public void Package_with_too_many_entries_is_rejected() =>
        AssertRejected(Package(Enumerable.Range(0, PackageService.MaxEntries).Select(_ => (Path("sources", Guid.NewGuid()), Utf8("{}")))), "package.too-many-entries");

    [Fact]
    public void Entry_over_the_entry_limit_is_rejected() =>
        AssertRejected(Package([(Path("sources", Guid.NewGuid()), new byte[PackageService.MaxEntryBytes + 1])]), "package.entry-too-large");

    [Fact]
    public void Entries_that_together_unpack_beyond_the_total_limit_are_rejected()
    {
        // Each entry is within the per-entry limit and compresses to a few KB: a zip bomb in miniature.
        var count = (int)(PackageService.MaxTotalBytes / PackageService.MaxEntryBytes) + 1;
        var zeros = new byte[PackageService.MaxEntryBytes];
        var package = Package(Enumerable.Range(0, count).Select(_ => (Path("sources", Guid.NewGuid()), zeros)));

        Assert.True(package.Length < 1024 * 1024, $"test package is {package.Length} bytes");
        AssertRejected(package, "package.content-too-large");
    }

    [Fact]
    public void Missing_manifest_is_rejected()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            Add(zip, Path("characters", CharacterId), CharacterJson());

        AssertRejected(buffer.ToArray(), "package.manifest-missing");
    }

    [Theory]
    [InlineData("other.package", 2)]
    [InlineData(PackageManifest.FormatName, 0)]
    [InlineData(PackageManifest.FormatName, PackageManifest.CurrentFormatVersion + 1)]
    public void Unsupported_format_is_rejected(string format, int version) =>
        AssertRejected(
            Package([(Path("characters", CharacterId), CharacterJson())], m =>
            {
                m["format"] = format;
                m["formatVersion"] = version;
            }),
            "package.unsupported-format");

    [Fact]
    public void Entry_not_listed_in_the_manifest_is_rejected()
    {
        var extra = Path("sources", Guid.NewGuid());
        AssertRejected(Package([(Path("characters", CharacterId), CharacterJson()), (extra, Utf8("{}"))], listed: p => p != extra), "package.entry-unlisted");
    }

    [Fact]
    public void Manifest_entry_missing_from_the_archive_is_rejected() =>
        AssertRejected(
            Package([(Path("characters", CharacterId), CharacterJson())], m => m["entries"]!.AsArray().Add(new JsonObject
            {
                ["path"] = Path("sources", Guid.NewGuid()), ["kind"] = "source", ["sha256"] = new string('0', 64), ["size"] = 2,
            })),
            "package.entry-missing");

    [Fact]
    public void Entry_listed_twice_in_the_manifest_is_rejected() =>
        AssertRejected(
            Package([(Path("characters", CharacterId), CharacterJson())], m => m["entries"]!.AsArray().Add(m["entries"]![0]!.DeepClone())),
            "package.entry-duplicate");

    [Fact]
    public void Entry_present_twice_in_the_archive_is_rejected()
    {
        var path = Path("characters", CharacterId);
        AssertRejected(Package([(path, CharacterJson()), (path, CharacterJson())], m => m["entries"]!.AsArray().RemoveAt(1)), "package.entry-duplicate");
    }

    [Fact]
    public void Entry_whose_id_does_not_match_its_file_name_is_rejected() =>
        AssertRejected(Package([(Path("characters", Guid.NewGuid()), CharacterJson())]), "package.id-mismatch");

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("""{"id":"not-a-guid"}""")]
    public void Entry_that_is_not_a_valid_document_is_rejected(string json) =>
        AssertRejected(Package([(Path("characters", CharacterId), Utf8(json))]), "package.invalid-json");

    [Fact]
    public void Revision_whose_source_is_neither_packaged_nor_installed_is_rejected()
    {
        var id = Guid.NewGuid();
        var revision = $$"""{"contentId":"{{id}}","revisionId":"{{id}}","schemaVersion":2,"kind":"feat","name":"Orphan","rulesFamilies":["srd-5.1"],"provenance":{"sourceId":"{{Guid.NewGuid()}}"},"status":"published"}""";
        AssertRejected(Package([(Path("content", id), Utf8(revision))]), "package.source-missing");
    }

    [Fact]
    public void Character_pinning_a_revision_that_is_neither_packaged_nor_installed_is_rejected() =>
        AssertRejected(
            Package([(Path("characters", CharacterId), CharacterJson(c => c["pins"] = new JsonArray(new JsonObject { ["contentId"] = Guid.NewGuid(), ["revisionId"] = Guid.NewGuid() })))]),
            "package.pin-missing");

    [Fact]
    public void Malformed_schema_v1_effect_is_imported_as_reference_only_instead_of_failing()
    {
        var id = Guid.NewGuid();
        var revision = $$"""{"contentId":"{{id}}","revisionId":"{{id}}","schemaVersion":1,"kind":"feat","name":"Old","rulesFamilies":["srd-5.1"],"provenance":{"sourceId":"{{SeededSource}}"},"status":"published","effects":[{"type":"initiativeBonus","amount":2}]}""";
        var package = Package([(Path("content", id), Utf8(revision))], m => m["formatVersion"] = 1);
        using var app = new TempApp();
        var dispatcher = new CommandDispatcher(app.App);

        var response = JsonDocument.Parse(dispatcher.Dispatch($$$"""{"id":"1","command":"package.apply","payload":{"base64":"{{{Convert.ToBase64String(package)}}}"}}""")).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var stored = app.App.Store.FindRevision(new ContentReference(id, id))!;
        Assert.Equal(AutomationStatus.Reference, Assert.IsType<UnknownEffect>(Assert.Single(stored.Effects)).Automation);
    }
}
