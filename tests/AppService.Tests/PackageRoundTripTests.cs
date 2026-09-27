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
        Effects = [new ModifierEffect { Id = "quick-draw-init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "2" }],
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
    public void Class_levels_travel_with_the_character_including_the_class_revisions_and_their_granted_features()
    {
        var m1 = TempApp.LoadFixture<ContentPack>("fixture-pack-m1.json");
        var warden = m1.Revisions.Single(r => r.Name == "Fixture Warden");
        using var origin = new TempApp();
        foreach (var source in m1.Sources)
            origin.App.Store.UpsertSource(source);
        foreach (var revision in m1.Revisions)
            origin.App.Store.AddRevision(revision);
        var character = TempApp.LoadFixture<Character>("characters/srd521-ash-m1.json") with { Pins = [], Classes = [new(warden.Reference, 3)], Level = 1 };

        var saved = origin.App.SaveCharacter(character);
        var export = origin.App.ExportCharacters([saved.Character.Id]);

        Assert.Equal(3, saved.Character.Level); // the service keeps the level in step with the class levels
        Assert.Contains(export.Manifest.Entries, e => e.Path == $"content/{warden.RevisionId:D}.json");
        // Granted features are not referenced by the character, but the export includes them so the sheet is identical.
        foreach (var granted in warden.Effects.OfType<GrantEffect>().Where(g => g.Content is not null))
            Assert.Contains(export.Manifest.Entries, e => e.Path == $"content/{granted.Content!.RevisionId:D}.json");
        using var destination = new TempApp();
        destination.App.ApplyImport(export.Content);
        var imported = destination.App.GetCharacter(saved.Character.Id);
        Assert.Equal(TempApp.Json(saved.Sheet), TempApp.Json(imported.Sheet));
        Assert.Equal(10 + (2 * 6) + 3, imported.Sheet.Field(FieldIds.HitPoints).Value); // d10 Warden 3, Con +1
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
    public void Import_that_replaces_a_character_backs_up_the_local_copy_and_the_backup_restores_it()
    {
        using var origin = new TempApp();
        var exported = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var package = origin.App.ExportCharacters([exported.Character.Id]).Content;

        using var local = new TempApp();
        var localCopy = local.App.SaveCharacter(exported.Character with
        {
            Name = "Pell (edited locally)",
            Overrides = [new FieldOverride(RulesCore.CharacterCalculator.InitiativeField, 11, "Local ruling")],
        }).Character;

        var preview = local.App.PreviewImport(package);
        Assert.Contains(preview.Warnings, w => w.Code == "package.character-replace" && w.Message.Contains("backups", StringComparison.Ordinal));

        var result = local.App.ApplyImport(package);

        Assert.Equal(1, result.Replaced);
        Assert.NotNull(result.BackupFile);
        Assert.StartsWith("backups/pre-import-", result.BackupFile, StringComparison.Ordinal);
        Assert.Equal(exported.Character.Name, local.App.GetCharacter(localCopy.Id).Character.Name);

        // Restore: the backup is an ordinary package; importing it brings the local edit back.
        var backup = File.ReadAllBytes(Path.Combine(local.App.DataDirectory, result.BackupFile));
        Assert.True(local.App.PreviewImport(backup).CanApply);
        var restore = local.App.ApplyImport(backup);

        var restored = local.App.GetCharacter(localCopy.Id).Character;
        Assert.Equal(TempApp.Json(localCopy), TempApp.Json(restored));
        Assert.NotEqual(result.BackupFile, restore.BackupFile);
    }

    [Fact]
    public void Import_that_adds_only_new_characters_takes_no_backup()
    {
        using var origin = new TempApp();
        var saved = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        using var destination = new TempApp();

        var result = destination.App.ApplyImport(origin.App.ExportCharacters([saved.Character.Id]).Content);

        Assert.Null(result.BackupFile);
        Assert.False(Directory.Exists(Path.Combine(destination.App.DataDirectory, PackageService.BackupFolderName)));
    }

    [Fact]
    public void Import_is_refused_when_the_local_copy_cannot_be_backed_up()
    {
        using var origin = new TempApp();
        var saved = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var package = origin.App.ExportCharacters([saved.Character.Id]).Content;

        using var local = new TempApp();
        var broken = local.App.SaveCharacter(saved.Character with { Pins = [new ContentReference(Guid.NewGuid(), Guid.NewGuid())] }).Character;

        var ex = Assert.Throws<PackageException>(() => local.App.ApplyImport(package));

        Assert.Equal("package.backup-failed", ex.Errors[0].Code);
        Assert.Equal(TempApp.Json(broken), TempApp.Json(local.App.GetCharacter(broken.Id).Character));
    }

    private static SourceRecord LocalHomebrewSource() => new()
    {
        Id = HomebrewSource,
        Title = "My Homebrew Notes",
        Publisher = "Test author",
        RulesFamilies = [RulesFamilies.Srd51],
        EditionVersion = "1",
        License = "CC BY 4.0 (my local note)",
        Redistributable = true,
        PdfRef = @"C:\Users\someone\Books\notes.pdf",
    };

    [Fact]
    public void Differing_source_metadata_is_shown_as_a_diff_and_blocks_apply_until_a_choice_is_made()
    {
        using var origin = new TempApp();
        AddHomebrew(origin);
        var saved = origin.App.SaveCharacter(ExportableCharacter());
        var package = origin.App.ExportCharacters([saved.Character.Id]).Content;

        using var local = new TempApp();
        local.App.Store.UpsertSource(LocalHomebrewSource());

        var preview = local.App.PreviewImport(package);

        Assert.True(preview.CanApply);
        var item = Assert.Single(preview.Items, i => i.Id == HomebrewSource);
        Assert.Equal(PackageItemAction.Replace, item.Action);
        Assert.Equal(["license", "redistributable"], item.Changes!.Select(c => c.Field));
        Assert.Equal("\"CC BY 4.0 (my local note)\"", item.Changes![0].Local);
        Assert.Equal("\"Personal homebrew\"", item.Changes![0].Imported);
        Assert.Contains(preview.Warnings, w => w.Code == "package.source-differs");

        var ex = Assert.Throws<PackageException>(() => local.App.ApplyImport(package));
        Assert.Equal("package.source-choice-required", Assert.Single(ex.Errors).Code);
        Assert.Empty(local.App.ListCharacters());
        Assert.Equal(TempApp.Json(LocalHomebrewSource()), TempApp.Json(local.App.Store.FindSource(HomebrewSource)));
    }

    [Theory]
    [InlineData(SourceChoice.KeepLocal, "CC BY 4.0 (my local note)", true)]
    [InlineData(SourceChoice.UseImported, "Personal homebrew", false)]
    public void Source_choice_decides_which_license_metadata_is_kept_and_never_touches_the_local_pdf_reference(
        SourceChoice choice, string expectedLicense, bool expectedRedistributable)
    {
        using var origin = new TempApp();
        AddHomebrew(origin);
        var saved = origin.App.SaveCharacter(ExportableCharacter());
        var package = origin.App.ExportCharacters([saved.Character.Id]).Content;
        using var local = new TempApp();
        local.App.Store.UpsertSource(LocalHomebrewSource());

        local.App.ApplyImport(package, new Dictionary<Guid, SourceChoice> { [HomebrewSource] = choice });

        var source = local.App.Store.FindSource(HomebrewSource)!;
        Assert.Equal(expectedLicense, source.License);
        Assert.Equal(expectedRedistributable, source.Redistributable);
        Assert.Equal(LocalHomebrewSource().PdfRef, source.PdfRef);
        Assert.NotNull(local.App.ListCharacters().SingleOrDefault(c => c.Id == saved.Character.Id));
    }

    [Fact]
    public void Export_never_includes_the_machine_local_pdf_reference()
    {
        using var origin = new TempApp();
        origin.App.Store.UpsertSource(LocalHomebrewSource());
        origin.App.Store.AddRevision(HomebrewFeat);
        var saved = origin.App.SaveCharacter(ExportableCharacter());

        var package = origin.App.ExportCharacters([saved.Character.Id]).Content;

        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry($"sources/{HomebrewSource:D}.json")!.Open());
        var json = reader.ReadToEnd();
        Assert.DoesNotContain("pdfRef", json, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatcher_passes_source_choices_through_package_apply()
    {
        using var origin = new TempApp();
        AddHomebrew(origin);
        var saved = origin.App.SaveCharacter(ExportableCharacter());
        var base64 = Convert.ToBase64String(origin.App.ExportCharacters([saved.Character.Id]).Content);
        using var local = new TempApp();
        local.App.Store.UpsertSource(LocalHomebrewSource());
        var dispatcher = new CommandDispatcher(local.App);

        var refused = JsonDocument.Parse(dispatcher.Dispatch($$$"""{"id":"1","command":"package.apply","payload":{"base64":"{{{base64}}}"}}""")).RootElement;
        var choices = $$"""{"{{HomebrewSource}}":"keepLocal"}""";
        var applied = JsonDocument.Parse(dispatcher.Dispatch($$$"""{"id":"2","command":"package.apply","payload":{"base64":"{{{base64}}}","sourceChoices":{{{choices}}}}}""")).RootElement;

        Assert.Equal("package.source-choice-required", refused.GetProperty("error").GetProperty("diagnostics")[0].GetProperty("code").GetString());
        Assert.True(applied.GetProperty("ok").GetBoolean(), applied.ToString());
        Assert.Equal("CC BY 4.0 (my local note)", local.App.Store.FindSource(HomebrewSource)!.License);
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
