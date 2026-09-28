using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>ADR-007 (D03): a backup includes everything; a share leaves out non-redistributable sources and says so.</summary>
public class ExportPurposeTests
{
    private static readonly Guid HomebrewSource = Guid.Parse("7b000000-0000-4000-8000-000000000001");

    private static readonly ContentRevision HomebrewFeat = new()
    {
        ContentId = Guid.Parse("7b00c000-0000-4000-8000-000000000001"),
        RevisionId = Guid.Parse("7b00e000-0000-4000-8000-000000000001"),
        Kind = ContentKind.Feat,
        Name = "Homebrew Quick Draw",
        RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(HomebrewSource, new PageRef(12)),
        Status = RevisionStatus.Published,
        Effects = [new ModifierEffect { Id = "quick-draw-init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "2" }],
    };

    /// <summary>A character pinning seeded (redistributable) fixture content plus one non-redistributable homebrew feat.</summary>
    private static Guid SaveMixedCharacter(TempApp app)
    {
        app.App.Store.UpsertSource(new SourceRecord
        {
            Id = HomebrewSource,
            Title = "My Homebrew Notes",
            Publisher = "Test author",
            RulesFamilies = [RulesFamilies.Srd51],
            EditionVersion = "1",
            License = "Personal homebrew",
            Redistributable = false,
        });
        app.App.Store.AddRevision(HomebrewFeat);
        var character = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        return app.App.SaveCharacter(character with { Pins = [.. character.Pins, HomebrewFeat.Reference] }).Character.Id;
    }

    [Fact]
    public void Backup_includes_non_redistributable_content_and_says_it_is_a_personal_backup()
    {
        using var origin = new TempApp();
        var id = SaveMixedCharacter(origin);

        var export = origin.App.ExportCharacters([id], ExportPurpose.Backup);

        Assert.Equal(ExportPurpose.Backup, export.Manifest.Purpose);
        Assert.Equal(PackageManifest.CurrentFormatVersion, export.Manifest.FormatVersion); // v4 since M2 item 7 (campaigns)
        Assert.Contains(export.Manifest.Entries, e => e.Path == $"content/{HomebrewFeat.RevisionId:D}.json");
        Assert.Contains(export.Manifest.Notices, n => n.SourceId == HomebrewSource && !n.Redistributable);
        Assert.Empty(export.Manifest.Omitted);
        Assert.EndsWith("-personal-backup.tomestack.zip", export.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public void Share_leaves_out_non_redistributable_sources_and_lists_each_omitted_item_with_its_characters()
    {
        using var origin = new TempApp();
        var id = SaveMixedCharacter(origin);

        var export = origin.App.ExportCharacters([id], ExportPurpose.Share);

        Assert.Equal(ExportPurpose.Share, export.Manifest.Purpose);
        Assert.DoesNotContain(export.Manifest.Entries, e => e.Path.Contains(HomebrewFeat.RevisionId.ToString("D"), StringComparison.Ordinal));
        Assert.DoesNotContain(export.Manifest.Entries, e => e.Path == $"sources/{HomebrewSource:D}.json");
        Assert.DoesNotContain(export.Manifest.Notices, n => n.SourceId == HomebrewSource);
        Assert.All(export.Manifest.Notices, n => Assert.True(n.Redistributable));
        var omitted = Assert.Single(export.Manifest.Omitted);
        Assert.Equal(("My Homebrew Notes", "Test author", "Personal homebrew"), (omitted.Title, omitted.Publisher, omitted.License));
        var revision = Assert.Single(omitted.Revisions);
        Assert.Equal((HomebrewFeat.Reference, "Homebrew Quick Draw"), (revision.Reference, revision.Name));
        Assert.Equal([id], revision.Characters);
        // The character keeps its pin: the receiver sees what is missing rather than a silently different character.
        Assert.Contains(export.Manifest.Entries, e => e.Path == $"characters/{id:D}.json");
        Assert.Equal("Pell--2014-fixture.tomestack.zip", export.FileName);
    }

    [Fact]
    public void Export_preview_lists_what_a_share_would_omit_without_writing_anything()
    {
        using var origin = new TempApp();
        var id = SaveMixedCharacter(origin);
        var dispatcher = new CommandDispatcher(origin.App);

        var response = JsonDocument.Parse(dispatcher.Dispatch($$$"""{"id":"1","command":"package.exportPreview","payload":{"characterIds":["{{{id}}}"],"purpose":"share"}}""")).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var result = response.GetProperty("result");
        Assert.Equal("share", result.GetProperty("purpose").GetString());
        var omitted = Assert.Single(result.GetProperty("omitted").EnumerateArray());
        Assert.Equal("My Homebrew Notes", omitted.GetProperty("title").GetString());
        Assert.Equal(id, Assert.Single(Assert.Single(omitted.GetProperty("revisions").EnumerateArray()).GetProperty("characters").EnumerateArray()).GetGuid());
        Assert.DoesNotContain(result.GetProperty("included").EnumerateArray(), n => n.GetProperty("sourceId").GetGuid() == HomebrewSource);
        Assert.False(System.IO.Directory.Exists(Path.Combine(origin.App.DataDirectory, PackageService.BackupFolderName)));

        var backup = origin.App.PreviewExport([id], ExportPurpose.Backup);
        Assert.Empty(backup.Omitted);
        Assert.Contains(backup.Included, n => n.SourceId == HomebrewSource);
    }

    [Fact]
    public void Share_package_imports_on_a_clean_machine_with_the_omitted_content_reported_as_missing()
    {
        using var origin = new TempApp();
        var id = SaveMixedCharacter(origin);
        var package = origin.App.ExportCharacters([id], ExportPurpose.Share).Content;

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(package);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        var warning = Assert.Single(preview.Warnings, w => w.Code == "package.content-omitted");
        Assert.Contains("My Homebrew Notes", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Test author", warning.Message, StringComparison.Ordinal);
        Assert.Equal(HomebrewFeat.Reference, warning.Content);

        destination.App.ApplyImport(package);
        var sheet = destination.App.GetCharacter(id).Sheet;
        Assert.Contains(sheet.Diagnostics, d => d.Code == "content.missing" && d.Content == HomebrewFeat.Reference);
        Assert.Null(destination.App.Store.FindSource(HomebrewSource));
    }

    [Fact]
    public void A_missing_pin_that_the_manifest_does_not_list_as_omitted_is_still_refused()
    {
        using var origin = new TempApp();
        var id = SaveMixedCharacter(origin);
        var package = origin.App.ExportCharacters([id], ExportPurpose.Share).Content;
        var unlisted = PackageEditor.Edit(package, _ => false, _ => { }, manifest => manifest["omitted"] = new JsonArray());

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(unlisted);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.Code == "package.pin-missing");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Older_formats_cannot_claim_to_be_share_packages(int formatVersion)
    {
        using var origin = new TempApp();
        var id = SaveMixedCharacter(origin);
        var package = origin.App.ExportCharacters([id], ExportPurpose.Share).Content;
        var old = PackageEditor.Edit(package, _ => false, _ => { }, manifest => manifest["formatVersion"] = formatVersion);

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(old);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.Code == "package.pin-missing");
    }

    [Theory]
    [InlineData("""[null]""")]
    [InlineData("""[{"sourceId":"7b000000-0000-4000-8000-000000000001","title":"x","publisher":"y","license":"z","revisions":null}]""")]
    [InlineData("""[{"sourceId":"7b000000-0000-4000-8000-000000000001","title":"x","publisher":"y","license":"z","revisions":[null]}]""")]
    [InlineData("null")]
    public void Malformed_omitted_list_is_rejected_as_invalid(string omitted)
    {
        using var origin = new TempApp();
        var id = SaveMixedCharacter(origin);
        var package = origin.App.ExportCharacters([id], ExportPurpose.Share).Content;
        var broken = PackageEditor.Edit(package, _ => false, _ => { }, manifest => manifest["omitted"] = JsonNode.Parse(omitted));

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(broken);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.Code == "package.invalid-json");
    }

    [Fact]
    public void Share_manifest_matches_its_schema()
    {
        using var origin = new TempApp();
        var id = SaveMixedCharacter(origin);
        var manifest = origin.App.ExportCharacters([id], ExportPurpose.Share).Manifest;

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(manifest, RulesJson.Options));
        var errors = SchemaTests.Validate("package-manifest", document.RootElement);

        Assert.True(errors.Length == 0, errors);
    }
}
