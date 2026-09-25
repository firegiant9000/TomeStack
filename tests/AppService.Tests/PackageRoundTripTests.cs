using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

public class PackageRoundTripTests
{
    private static readonly Guid HomebrewSource = Guid.Parse("7a000000-0000-4000-8000-000000000001");

    /// <summary>A published homebrew revision that exists only on the exporting machine.</summary>
    private static readonly ContentRevision HomebrewFeat = new()
    {
        ContentId = Guid.Parse("7a00c000-0000-4000-8000-000000000001"),
        RevisionId = Guid.Parse("7a00e000-0000-4000-8000-000000000001"),
        Kind = ContentKind.Feat,
        Name = "Homebrew Quick Draw",
        RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(HomebrewSource, new PageRef(12)),
        Status = RevisionStatus.Published,
        Effects = [new Effect { Id = "quick-draw-init", Type = Effect.InitiativeBonus, Amount = 2 }],
    };

    private static Character ExportableCharacter()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "characters", "srd51-quickfoot.json"));
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["x-futureField"] = new System.Text.Json.Nodes.JsonObject { ["kept"] = true };
        var character = node.Deserialize<Character>(RulesJson.Options)!;
        return character with
        {
            Pins = [.. character.Pins, HomebrewFeat.Reference],
            Overrides = [new FieldOverride(RulesCore.CharacterCalculator.InitiativeField, 9, "Table ruling")],
        };
    }

    private static void AddHomebrew(TempApp source)
    {
        source.App.Store.UpsertSource(new SourceRecord
        {
            Id = HomebrewSource,
            Title = "My Homebrew Notes",
            Publisher = "Test author",
            RulesFamilies = [RulesFamilies.Srd51],
            EditionVersion = "1",
            License = "Personal homebrew",
            Redistributable = false,
        });
        source.App.Store.AddRevision(HomebrewFeat);
    }

    [Fact]
    public void Export_then_import_on_a_clean_data_directory_preserves_character_pins_overrides_and_trace()
    {
        using var origin = new TempApp();
        AddHomebrew(origin);
        var saved = origin.App.SaveCharacter(ExportableCharacter());
        var export = origin.App.ExportCharacters([saved.Character.Id]);

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(export.Content);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        Assert.Contains(preview.Items, i => i.Kind == "character" && i.Action == PackageItemAction.Add);
        Assert.Contains(preview.Items, i => i.Id == HomebrewFeat.RevisionId && i.Action == PackageItemAction.Add);
        Assert.Contains(preview.Items, i => i.Kind == "contentRevision" && i.Action == PackageItemAction.Unchanged);
        Assert.Null(destination.App.ListCharacters().FirstOrDefault(c => c.Id == saved.Character.Id));

        var result = destination.App.ApplyImport(export.Content);
        var imported = destination.App.GetCharacter(saved.Character.Id);

        Assert.Equal([saved.Character.Id], result.Characters);
        Assert.Equal(TempApp.Json(saved.Character), TempApp.Json(imported.Character));
        Assert.Equal(TempApp.Json(saved.Sheet), TempApp.Json(imported.Sheet));

        var initiative = imported.Sheet.Field(RulesCore.CharacterCalculator.InitiativeField);
        Assert.Equal(9, initiative.Value);
        Assert.Equal(6, initiative.ComputedValue);
        Assert.Contains(initiative.Trace, t => t.Origin.Content == HomebrewFeat.Reference && t.Origin.SourceTitle == "My Homebrew Notes" && t.Origin.Page == new PageRef(12));
        Assert.True(imported.Character.Extensions!.ContainsKey("x-futureField"));
    }

    [Fact]
    public void Manifest_carries_license_notices_hashes_and_the_attachment_policy()
    {
        using var origin = new TempApp();
        AddHomebrew(origin);
        var saved = origin.App.SaveCharacter(ExportableCharacter());

        var export = origin.App.ExportCharacters([saved.Character.Id]);

        var notice = Assert.Single(export.Manifest.Notices, n => n.SourceId == HomebrewSource);
        Assert.False(notice.Redistributable);
        Assert.Equal("Personal homebrew", notice.License);
        Assert.All(export.Manifest.Entries, e => Assert.Matches("^[0-9a-f]{64}$", e.Sha256));
        Assert.Contains("never included", export.Manifest.AttachmentPolicy, StringComparison.Ordinal);
        Assert.EndsWith(".tomestack.zip", export.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_is_deterministic_for_the_same_data_and_time()
    {
        using var origin = new TempApp();
        var saved = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));

        var first = origin.App.ExportCharacters([saved.Character.Id]).Content;
        var second = origin.App.ExportCharacters([saved.Character.Id]).Content;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Tampered_entry_is_rejected_and_nothing_is_written()
    {
        using var origin = new TempApp();
        var saved = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var package = Rewrite(origin.App.ExportCharacters([saved.Character.Id]).Content, zip =>
        {
            var path = $"characters/{saved.Character.Id:D}.json";
            zip.GetEntry(path)!.Delete();
            Write(zip, path, "{\"tampered\":true}");
        });

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(package);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.Code == "package.hash-mismatch");
        Assert.Throws<PackageException>(() => destination.App.ApplyImport(package));
        Assert.Empty(destination.App.ListCharacters());
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("characters/../../evil.json")]
    [InlineData("C:/Windows/evil.json")]
    [InlineData("assets/script.js")]
    public void Entries_outside_the_allowed_layout_are_rejected(string entryPath)
    {
        using var origin = new TempApp();
        var saved = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var package = Rewrite(origin.App.ExportCharacters([saved.Character.Id]).Content, zip => Write(zip, entryPath, "{}"));

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(package);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.Code == "package.entry-not-allowed");
    }

    [Fact]
    public void Not_a_zip_file_is_reported_not_thrown()
    {
        using var app = new TempApp();

        var preview = app.App.PreviewImport(Encoding.UTF8.GetBytes("definitely not a zip"));

        Assert.False(preview.CanApply);
        Assert.Equal("package.invalid-archive", Assert.Single(preview.Errors).Code);
    }

    [Fact]
    public void Revision_with_same_id_but_different_data_is_a_blocking_conflict()
    {
        using var origin = new TempApp();
        AddHomebrew(origin);
        var saved = origin.App.SaveCharacter(ExportableCharacter());
        var export = origin.App.ExportCharacters([saved.Character.Id]).Content;

        using var destination = new TempApp();
        destination.App.Store.AddRevision(HomebrewFeat with { Name = "Locally Edited Quick Draw" });

        var preview = destination.App.PreviewImport(export);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.Code == "package.revision-conflict");
        Assert.Throws<PackageException>(() => destination.App.ApplyImport(export));
    }

    [Fact]
    public void Draft_revisions_stay_inactive_after_import()
    {
        using var origin = new TempApp();
        var character = TempApp.LoadFixture<Character>("characters/srd521-courier.json");
        var saved = origin.App.SaveCharacter(character);
        var export = origin.App.ExportCharacters([saved.Character.Id]).Content;

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(export);
        destination.App.ApplyImport(export);

        Assert.Contains(preview.Warnings, w => w.Code == "package.revision-draft");
        var sheet = destination.App.GetCharacter(saved.Character.Id).Sheet;
        Assert.Contains(sheet.Diagnostics, d => d.Code == "content.unpublished");
        Assert.Equal(3, sheet.Field(RulesCore.CharacterCalculator.InitiativeField).Value);
    }

    [Fact]
    public void Missing_pinned_revision_blocks_export()
    {
        using var origin = new TempApp();
        var character = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json") with
        {
            Pins = [new ContentReference(Guid.NewGuid(), Guid.NewGuid())],
        };
        var saved = origin.App.SaveCharacter(character);

        var ex = Assert.Throws<PackageException>(() => origin.App.ExportCharacters([saved.Character.Id]));

        Assert.Equal("export.revision-missing", Assert.Single(ex.Errors).Code);
    }

    private static byte[] Rewrite(byte[] package, Action<ZipArchive> change)
    {
        using var buffer = new MemoryStream();
        buffer.Write(package);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
            change(zip);
        return buffer.ToArray();
    }

    private static void Write(ZipArchive zip, string path, string content)
    {
        using var stream = zip.CreateEntry(path).Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
