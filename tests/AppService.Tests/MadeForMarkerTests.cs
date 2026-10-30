using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>D85: the optional <c>madeFor</c> manifest marker. No format bump; a malformed value is ignored with a warning.</summary>
public class MadeForMarkerTests
{
    private static byte[] ExportWithMarker(TempApp app, string? madeFor)
    {
        var character = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        var id = app.App.SaveCharacter(character).Character.Id;
        var package = app.App.ExportCharacters([id], ExportPurpose.Backup).Content;
        if (madeFor is null)
            return package;
        using var buffer = new MemoryStream();
        buffer.Write(package);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("manifest.json")!;
            JsonObject manifest;
            using (var reader = new StreamReader(entry.Open()))
                manifest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
            manifest["madeFor"] = madeFor;
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write(manifest.ToJsonString());
        }
        return buffer.ToArray();
    }

    [Fact]
    public void Manifest_round_trips_madeFor()
    {
        using var origin = new TempApp();
        var package = ExportWithMarker(origin, "0.6.0");

        using var clean = new TempApp();
        var preview = clean.App.PreviewImport(package);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        Assert.Equal("0.6.0", preview.Manifest!.MadeFor);
        Assert.DoesNotContain(preview.Warnings, w => w.Code == "package.marker-ignored");
        using var written = JsonDocument.Parse(JsonSerializer.Serialize(preview.Manifest, RulesJson.Options));
        Assert.Equal("0.6.0", written.RootElement.GetProperty("madeFor").GetString());
    }

    [Fact]
    public void An_ordinary_export_does_not_write_madeFor()
    {
        using var origin = new TempApp();
        var package = ExportWithMarker(origin, null);

        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open());
        Assert.DoesNotContain("madeFor", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("9999.9999.9999.1")]
    [InlineData("abc")]
    [InlineData("12345.0.0")]
    [InlineData("")]
    [InlineData("0.6.0\n")]
    [InlineData(" 0.6.0")]
    public void Manifest_ignores_malformed_madeFor(string value) => AssertIgnored(value);

    [Fact]
    public void Manifest_ignores_a_200_character_madeFor_without_echoing_it()
    {
        var value = new string('9', 200);
        var warning = AssertIgnored(value);
        Assert.DoesNotContain(value, warning.Message, StringComparison.Ordinal);
    }

    private static Diagnostic AssertIgnored(string value)
    {
        using var origin = new TempApp();
        var package = ExportWithMarker(origin, value);

        using var clean = new TempApp();
        var preview = clean.App.PreviewImport(package);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        Assert.Null(preview.Manifest!.MadeFor);
        return Assert.Single(preview.Warnings, w => w.Code == "package.marker-ignored");
    }
}
