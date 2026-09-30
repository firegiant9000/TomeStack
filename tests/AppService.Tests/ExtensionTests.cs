using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Exports;
using TomeStack.AppService.Extensions;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M6 slice 3 (ADR-011, option A): extensions. The external sample in examples/extensions is zipped here as a user
/// would, installed with a review of its permissions, and run: its import hook makes drafts only, in a new
/// import-derived source; its export hook reads the purpose-filtered sheet export model, and its output is scanned for
/// paths and the user name before anything is written. All content is original test data.
/// </summary>
public class ExtensionTests
{
    private static readonly string SampleFolder = Path.Combine(AppContext.BaseDirectory, "examples", "extensions", "spell-list-and-sheet-summary");
    private static readonly Guid SampleId = Guid.Parse("6f3e0000-0000-4000-8000-00000000e001");

    /// <summary>The sample as an extension file: extension.json and transforms/, edited by <paramref name="edit"/>.</summary>
    internal static byte[] Sample(Action<JsonObject>? edit = null, IReadOnlyDictionary<string, string>? extra = null, Func<string, string>? transform = null)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(SampleFolder, "extension.json")))!.AsObject();
        edit?.Invoke(manifest);
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["extension.json"] = manifest.ToJsonString() };
        foreach (var file in Directory.GetFiles(Path.Combine(SampleFolder, "transforms")))
            entries[$"transforms/{Path.GetFileName(file)}"] = transform?.Invoke(File.ReadAllText(file)) ?? File.ReadAllText(file);
        foreach (var (path, text) in extra ?? new Dictionary<string, string>())
            entries[path] = text;
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in entries)
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        return output.ToArray();
    }

    private static byte[] SampleCsv => File.ReadAllBytes(Path.Combine(SampleFolder, "sample-spells.csv"));

    private static IReadOnlyList<string> Codes(Action action) => [.. Assert.Throws<AppValidationException>(action).Problems.Select(p => p.Code)];

    /// <summary>Installs the sample with every permission it asks for.</summary>
    internal static InstalledExtension Install(TomeStackApp app, byte[]? file = null)
    {
        var preview = app.PreviewExtensionInstall(file ?? Sample());
        Assert.True(preview.CanInstall, string.Join("; ", preview.Errors.Select(e => e.Code)));
        return app.InstallExtension(preview.Token!.Value, [.. preview.Manifest!.Permissions], confirm: true);
    }

    [Fact]
    public void The_sample_installs_after_a_review_of_its_permissions_and_is_kept_read_only()
    {
        using var temp = new TempApp();
        var preview = temp.App.PreviewExtensionInstall(Sample());
        Assert.True(preview.CanInstall);
        Assert.Equal(["import.file", "write.drafts", "read.sheet", "export.file"], preview.Permissions.Select(p => p.Permission));
        Assert.All(preview.Permissions, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
        Assert.Empty(temp.App.ListExtensions()); // the preview installs nothing

        Assert.Contains("extension.confirm-required", Codes(() => temp.App.InstallExtension(preview.Token!.Value, ["read.sheet"], confirm: false)));
        Assert.Contains("extension.grant-unknown", Codes(() => temp.App.InstallExtension(temp.App.PreviewExtensionInstall(Sample()).Token!.Value, ["read.content"], confirm: true)));
        var installed = temp.App.InstallExtension(temp.App.PreviewExtensionInstall(Sample()).Token!.Value, ["read.sheet", "export.file"], confirm: true);
        Assert.Equal((SampleId, true, 2), (installed.Id, installed.Enabled, installed.Grants.Count));
        var file = Path.Combine(temp.Directory, "extensions", $"{installed.Sha256}.zip");
        Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(SampleFolder, "extension.json")));
        Assert.Equal("", SchemaTests.Validate("extension-manifest", manifest.RootElement));
        foreach (var transform in Directory.GetFiles(Path.Combine(SampleFolder, "transforms")))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(transform));
            Assert.Equal("", SchemaTests.Validate("extension-transform", document.RootElement));
        }
        Assert.Contains(1, temp.App.GetInfo().ExtensionApi!);
    }

    [Theory]
    [InlineData("permission", "extension.permission-unknown")]
    [InlineData("api", "extension.api-unsupported")]
    [InlineData("format", "extension.format-unsupported")]
    [InlineData("runtime", "extension.runtime-unsupported")]
    [InlineData("license", "extension.license-required")]
    [InlineData("hook-permission", "extension.hook-permission")]
    [InlineData("field", "extension.field-unknown")]
    public void A_manifest_this_build_cannot_honour_is_refused_and_nothing_is_installed(string change, string code)
    {
        using var temp = new TempApp();
        var file = Sample(m =>
        {
            switch (change)
            {
                case "permission": m["permissions"]!.AsArray().Add("network.fetch"); break;
                case "api": m["extensionApi"] = 2; break;
                case "format": m["formatVersion"] = 2; break;
                case "runtime": m["runtime"] = "native"; break;
                case "license": m.Remove("license"); m["license"] = " "; break;
                case "hook-permission": m["permissions"] = new JsonArray("read.sheet", "export.file"); break;
                case "field": m["autorun"] = true; break;
            }
        });
        var preview = temp.App.PreviewExtensionInstall(file);
        Assert.False(preview.CanInstall);
        Assert.Contains(code, preview.Errors.Select(e => e.Code));
        Assert.Empty(temp.App.ListExtensions());
    }

    [Fact]
    public void The_file_is_read_under_package_limits_and_a_path_allowlist()
    {
        using var temp = new TempApp();
        string First(byte[] file) => temp.App.PreviewExtensionInstall(file).Errors[0].Code;
        Assert.Equal("extension.entry-not-allowed", First(Sample(extra: new Dictionary<string, string> { ["../escape.json"] = "{}" })));
        Assert.Equal("extension.entry-not-allowed", First(Sample(extra: new Dictionary<string, string> { ["README.md"] = "x" })));
        Assert.Equal("extension.entry-not-allowed", First(Sample(extra: new Dictionary<string, string> { ["bin/tool.dll"] = "MZ" })));
        Assert.Equal("extension.entry-unused", First(Sample(extra: new Dictionary<string, string> { ["transforms/spare.json"] = """{"output":1}""" })));
        Assert.Equal("extension.too-large", First(new byte[ExtensionReader.MaxFileBytes + 1]));
        Assert.Equal("extension.invalid-archive", First(Encoding.UTF8.GetBytes("not a zip")));
        Assert.Equal("transform.invalid", First(Sample(transform: _ => """{"output":{"eval":"x"}}""")));
    }

    [Fact]
    public void Content_and_campaign_packages_never_carry_an_extension()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Harbor Notes", [RulesFamilies.Srd521], Redistributable: true, ConfirmOwnWork: true));
        var draft = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = "Test Harbor Feat",
            RulesFamilies = [RulesFamilies.Srd521], Provenance = new(source.Id), Status = RevisionStatus.Draft, Effects = [],
        };
        temp.App.SaveDraft(draft);
        temp.App.Publish(draft.Reference);
        var pack = temp.App.ExportSourcePack([source.Id]).Content;
        foreach (var path in new[] { "extension.json", "transforms/x.json", $"extensions/{new string('a', 64)}.zip" })
        {
            var planted = SourcePackTests.AddEntry(pack, path, "extension", new { });
            Assert.Contains("package.extension-not-allowed", temp.App.PreviewImport(planted).Errors.Select(e => e.Code));
        }
        // A newer format is no way in either (M6 stack re-review): the extension's own entries are refused as an
        // extension, not with "update TomeStack". A later library backup's extensions/ folder is left to the version check.
        var newer = PackageEditor.Edit(pack, _ => false, _ => { }, m => m["formatVersion"] = PackageManifest.CurrentFormatVersion + 1);
        foreach (var path in new[] { "extension.json", "transforms/x.json", "tool.tomestack-ext.zip" })
            Assert.Equal("package.extension-not-allowed", Assert.Single(temp.App.PreviewImport(SourcePackTests.AddEntry(newer, path, "extension", new { })).Errors).Code);
        var laterBackup = SourcePackTests.AddEntry(newer, $"extensions/{new string('a', 64)}/extension.json", "extension", new { });
        Assert.Equal("package.unsupported-format", Assert.Single(temp.App.PreviewImport(laterBackup).Errors).Code);
    }

    [Fact]
    public void An_import_run_previews_drafts_writes_nothing_until_confirmed_and_puts_them_in_a_new_import_derived_source()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var installed = Install(app);
        var sourcesBefore = app.ListSources().Count;

        var preview = app.PreviewExtensionRun(new(installed.Id, "spells-from-csv", RulesFamily: RulesFamilies.Srd521, SourceTitle: "Test Imported Spells", Input: SampleCsv));
        Assert.Equal(["Example Lantern Mote", "Example Quiet Step", "Example Ink Ward"], preview.Drafts.Select(d => d.Name));
        Assert.All(preview.Drafts, d => Assert.Equal(ContentKind.Spell, d.Kind));
        Assert.Contains(preview.Warnings, w => w.Code == "extension.import-derived");
        Assert.Equal(sourcesBefore, app.ListSources().Count); // nothing written by the preview

        Assert.Contains("extension.confirm-required", Codes(() => app.ApplyExtensionImport(preview.Token, confirm: false)));
        var result = app.ApplyExtensionImport(preview.Token, confirm: true);
        Assert.Equal(3, result.Drafts);
        Assert.Contains("extension.run-expired", Codes(() => app.ApplyExtensionImport(preview.Token, confirm: true))); // one use

        var source = app.Store.FindSource(result.SourceId)!;
        Assert.Equal((true, false, "Personal homebrew", SourceOrigin.Local), (source.ImportDerived, source.Redistributable, source.License, source.Origin));
        var drafts = app.Store.ListRevisionsInOrder().Where(r => r.Provenance.SourceId == source.Id).ToList();
        Assert.All(drafts, d => Assert.Equal(RevisionStatus.Draft, d.Status)); // nothing is active until published
        var mote = Assert.IsType<SpellEffect>(Assert.Single(drafts.Single(d => d.Name == "Example Lantern Mote").Effects));
        Assert.Equal((0, "evocation", false), (mote.Level, mote.School, mote.Concentration));
        Assert.Equal(["wizard", "sorcerer"], mote.Lists);
        // It can never be shared: the slice 1 guard holds.
        Assert.Contains("source.import-derived", Codes(() => app.SetShareable(new(source.Id, Shareable: true, ConfirmOwnWork: true))));
        Assert.Contains("pack.source-import-derived", Assert.Throws<PackageException>(() => app.ExportSourcePack([source.Id])).Errors.Select(e => e.Code));
    }

    [Fact]
    public void Grants_are_bound_to_the_file_and_every_change_asks_again()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var installed = Install(app);

        // A different file under the same id and author: an update that shows what changes and grants nothing by itself.
        var update = app.PreviewExtensionInstall(Sample(m => m["version"] = "1.1.0"));
        Assert.True(update.CanInstall);
        Assert.Equal(("1.0.0", "1.1.0", false), (update.Update!.InstalledVersion, update.Update.NewVersion, update.Update.SameFile));
        // A different author is refused.
        var hijack = app.PreviewExtensionInstall(Sample(m => m["author"] = "Someone Else"));
        Assert.Contains("extension.author-changed", hijack.Errors.Select(e => e.Code));

        // A file changed on disk after it was granted does not run.
        var path = Path.Combine(temp.Directory, "extensions", $"{installed.Sha256}.zip");
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllBytes(path, Sample(m => m["description"] = "changed on disk"));
        Assert.Contains("extension.file-changed", Codes(() => app.PreviewExtensionRun(new(installed.Id, "spells-from-csv", Input: SampleCsv))));
    }

    [Fact]
    public void A_run_needs_the_extension_turned_on_and_each_permission_granted_and_removing_it_keeps_its_drafts()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var preview = app.PreviewExtensionInstall(Sample());
        var installed = app.InstallExtension(preview.Token!.Value, ["read.sheet", "export.file"], confirm: true);
        Assert.Contains("extension.permission-not-granted", Codes(() => app.PreviewExtensionRun(new(installed.Id, "spells-from-csv", Input: SampleCsv))));

        var full = Install(app);
        var run = app.PreviewExtensionRun(new(full.Id, "spells-from-csv", Input: SampleCsv));
        app.SetExtensionEnabled(full.Id, enabled: false);
        Assert.Contains("extension.run-expired", Codes(() => app.ApplyExtensionImport(run.Token, confirm: true))); // turned off after the preview
        Assert.Contains("extension.disabled", Codes(() => app.PreviewExtensionRun(new(full.Id, "spells-from-csv", Input: SampleCsv))));

        app.SetExtensionEnabled(full.Id, enabled: true);
        var result = app.ApplyExtensionImport(app.PreviewExtensionRun(new(full.Id, "spells-from-csv", Input: SampleCsv)).Token, confirm: true);
        app.RemoveExtension(full.Id, confirm: true);
        Assert.Empty(app.ListExtensions());
        Assert.False(File.Exists(Path.Combine(temp.Directory, "extensions", $"{full.Sha256}.zip")));
        Assert.Equal(3, app.Store.ListRevisionsInOrder().Count(r => r.Provenance.SourceId == result.SourceId)); // drafts stay
    }

    [Fact]
    public void An_export_run_reads_the_filtered_sheet_carries_every_notice_and_never_a_path_the_user_name_or_a_gap_note()
    {
        const string sentinel = "SentinelUser7f3q";
        using var temp = new TempApp(folder: sentinel);
        var app = temp.App;
        app.ScanIdentity = () => ($"C:\\Users\\{sentinel}", sentinel);
        // A linked PDF under the sentinel's folder, and a gap note: neither may reach the output.
        var pdfFolder = Path.Combine(temp.Directory, "..", $"{sentinel}-books");
        Directory.CreateDirectory(pdfFolder);
        var pdf = Path.Combine(pdfFolder, "book.pdf");
        File.WriteAllBytes(pdf, Encoding.ASCII.GetBytes("%PDF-1.4\n% test\n%%EOF\n"));
        var book = app.CreateHomebrewSource(new("Test Book Notes", [RulesFamilies.Srd521]));
        app.AttachPdfFile(book.Id, Path.GetFullPath(pdf), AttachmentMode.Linked);
        var character = app.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json"));
        app.AddGapNote(new(character.Character.Id, new(GapTargetKind.Field, FieldId: FieldIds.Initiative), $"Gap text {sentinel}"));

        var installed = Install(app);
        var preview = app.PreviewExtensionRun(new(installed.Id, "sheet-markdown", CharacterId: character.Character.Id));
        Assert.EndsWith("-sheet-markdown.md", preview.FileName, StringComparison.Ordinal);
        var (_, bytes) = app.ExtensionExportOutput(preview.Token);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("# ", text, StringComparison.Ordinal);
        Assert.Contains("## Sources and licenses", text, StringComparison.Ordinal);
        foreach (var notice in preview.Notices)
            Assert.Contains(notice.Title, text, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gap text", text, StringComparison.Ordinal);

        // A transform that writes a path in any spelling is refused before anything is written. Review fix: the paths are
        // also tested where they do not contain the user name, so the path matching itself is what refuses them.
        var plainFolder = Path.GetFullPath(Path.Combine(temp.Directory, "..", "plain-books"));
        Directory.CreateDirectory(plainFolder);
        var plainPdf = Path.Combine(plainFolder, "other.pdf");
        File.WriteAllBytes(plainPdf, Encoding.ASCII.GetBytes("%PDF-1.4\n% test\n%%EOF\n"));
        var other = app.CreateHomebrewSource(new("Test Other Notes", [RulesFamilies.Srd521]));
        app.AttachPdfFile(other.Id, plainPdf, AttachmentMode.Linked);
        Assert.DoesNotContain(sentinel, plainPdf, StringComparison.OrdinalIgnoreCase);
        foreach (var leak in new[]
        {
            plainPdf,
            plainPdf.Replace('\\', '/').ToUpperInvariant(),
            plainPdf.Replace("\\", "\\\\", StringComparison.Ordinal),
            plainPdf[2..].Replace('\\', '\u2216'),
            temp.Directory,
            $"C:/Users/{sentinel.ToLowerInvariant()}/notes",
        })
        {
            var evil = Sample(m => m["version"] = $"1.0.{Math.Abs(leak.GetHashCode()) % 1000}", transform: t => t.Contains("sheet/character", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { output = new { template = "leak: " + leak.Replace("{", "{{", StringComparison.Ordinal) } })
                : t);
            var reinstalled = Install(app, evil);
            Assert.Contains("extension.output-refused", Codes(() => app.PreviewExtensionRun(new(reinstalled.Id, "sheet-markdown", CharacterId: character.Character.Id))));
        }
    }

    [Fact]
    public void A_previewed_run_is_refused_once_a_permission_it_used_is_withdrawn_and_a_cancelled_save_keeps_it()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var installed = Install(app);
        var character = app.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json"));
        var export = app.PreviewExtensionRun(new(installed.Id, "sheet-markdown", CharacterId: character.Character.Id));
        // Peeking (the Save dialog cancelled) keeps the token.
        app.PeekExtensionOutput(export.Token);
        Assert.NotEmpty(app.PeekExtensionOutput(export.Token).Bytes);

        // The same file installed again with only the import permissions: the export previewed before cannot be written.
        var again = app.PreviewExtensionInstall(Sample());
        app.InstallExtension(again.Token!.Value, ["import.file", "write.drafts"], confirm: true);
        Assert.Contains("extension.run-expired", Codes(() => app.PeekExtensionOutput(export.Token)));
        Assert.Contains("extension.permission-not-granted", Codes(() => app.PreviewExtensionRun(new(installed.Id, "sheet-markdown", CharacterId: character.Character.Id))));
    }

    [Fact]
    public void Installing_the_same_file_again_repairs_a_damaged_copy()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var installed = Install(app);
        var path = Path.Combine(temp.Directory, "extensions", $"{installed.Sha256}.zip");
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Contains("extension.file-changed", Codes(() => app.PreviewExtensionRun(new(installed.Id, "spells-from-csv", Input: SampleCsv))));
        Install(app);
        Assert.Equal(3, app.PreviewExtensionRun(new(installed.Id, "spells-from-csv", Input: SampleCsv)).Drafts.Count);
    }

    [Fact]
    public void A_hook_without_a_kind_or_with_a_field_this_build_does_not_know_is_refused()
    {
        using var temp = new TempApp();
        var noKind = temp.App.PreviewExtensionInstall(Sample(m => m["hooks"]![0]!.AsObject().Remove("kind")));
        Assert.Contains("extension.hook-invalid", noKind.Errors.Select(e => e.Code));
        var extra = temp.App.PreviewExtensionInstall(Sample(m => m["hooks"]![0]!["autorun"] = true));
        Assert.Contains("extension.field-unknown", extra.Errors.Select(e => e.Code));
    }

    [Fact]
    public void A_share_export_keeps_totals_but_drops_content_that_may_not_leave_and_personal_lets_out_only_your_own()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var mine = app.CreateHomebrewSource(new("Test Own Notes", [RulesFamilies.Srd521]));
        var feat = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = "Test Own Knack", RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(mine.Id), Status = RevisionStatus.Draft, Summary = "Original test text.",
            Effects = [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "3" }],
        };
        app.SaveDraft(feat);
        var knack = app.Publish(feat.Reference).Published;
        var character = app.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { Pins = [.. TempApp.LoadFixture<Character>("characters/srd521-courier.json").Pins, knack] });
        var initiative = character.Sheet.Field(FieldIds.Initiative).Value;

        var share = app.SheetExportFor(character.Character.Id, SheetPurpose.Share);
        Assert.DoesNotContain(share.Features, f => f.Name == "Test Own Knack");
        Assert.Contains(share.Dropped, d => d.Source == "Test Own Notes" && d.Items >= 1);
        Assert.Equal(initiative, share.Fields.Single(f => f.Id == FieldIds.Initiative).Value); // the total keeps its effect
        Assert.True(share.Notices.Single(n => n.Title == "Test Own Notes").TotalsOnly);

        var personal = app.SheetExportFor(character.Character.Id, SheetPurpose.Personal);
        Assert.Contains(personal.Features, f => f.Name == "Test Own Knack");
        Assert.DoesNotContain(personal.Dropped, d => d.Source == "Test Own Notes");

        // Import-derived homebrew stays out even of a personal export.
        app.AttachPdf(mine.Id, "book.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\n% test\n%%EOF\n"));
        Assert.DoesNotContain(app.SheetExportFor(character.Character.Id, SheetPurpose.Personal).Features, f => f.Name == "Test Own Knack");

        foreach (var model in new[] { share, personal })
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(model, RulesJson.Compact));
            Assert.Equal("", SchemaTests.Validate("sheet-export", document.RootElement));
        }
    }

    [Fact]
    public void A_library_backup_keeps_extensions_without_their_grants_and_a_restore_brings_them_back_turned_off()
    {
        using var origin = new TempApp();
        var installed = Install(origin.App);
        var backup = Path.Combine(origin.Directory, "..", "backup.tomestack.zip");
        using (var file = File.Create(backup))
            Assert.Equal(1, origin.App.WriteLibraryBackup(file).Contents.Extensions);
        using (var zip = ZipFile.OpenRead(backup))
        {
            Assert.NotNull(zip.GetEntry($"extensions/{installed.Sha256}.zip"));
            using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
            Assert.Equal(PackageManifest.LibraryExtensionsFormatVersion, manifest.RootElement.GetProperty("formatVersion").GetInt32());
            Assert.Equal("", SchemaTests.Validate("package-manifest", manifest.RootElement));
        }

        using var clean = new TempApp();
        var preview = clean.App.PreviewLibraryRestore(backup);
        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Code)));
        Assert.Contains(preview.Items, i => i.Kind == "extension" && i.Action == PackageItemAction.Add);
        Assert.Contains(preview.Warnings, w => w.Code == "restore.extension-review");
        clean.App.ApplyLibraryRestore(backup);
        var restored = Assert.Single(clean.App.ListExtensions());
        Assert.Equal((installed.Id, installed.Sha256, false, 0), (restored.Id, restored.Sha256, restored.Enabled, restored.Grants.Count));
        Assert.Contains("extension.disabled", Codes(() => clean.App.PreviewExtensionRun(new(restored.Id, "sheet-markdown", CharacterId: Guid.NewGuid()))));
        // Review fix: it can be reviewed and granted from its stored file, without the original.
        var review = clean.App.PreviewInstalledExtension(restored.Id);
        Assert.True(review.CanInstall);
        Assert.True(clean.App.InstallExtension(review.Token!.Value, [.. review.Manifest!.Permissions], confirm: true).Enabled);

        // Without an extension, a backup stays v7 so v7 builds can restore it.
        using var plain = new TempApp();
        using var stream = new MemoryStream();
        plain.App.WriteLibraryBackup(stream);
        using var plainZip = new ZipArchive(new MemoryStream(stream.ToArray()));
        using var plainManifest = JsonDocument.Parse(plainZip.GetEntry("manifest.json")!.Open());
        Assert.Equal(PackageManifest.LibraryFormatVersion, plainManifest.RootElement.GetProperty("formatVersion").GetInt32());
    }

    [Fact]
    public void The_dispatcher_offers_the_extension_commands_and_the_desktop_only_ones_say_so_elsewhere()
    {
        using var temp = new TempApp();
        var dispatcher = new CommandDispatcher(temp.App);
        string Send(string command, object? payload = null) => dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact));

        using var preview = JsonDocument.Parse(Send("extension.installPreview", new { base64 = Convert.ToBase64String(Sample()) }));
        var token = preview.RootElement.GetProperty("result").GetProperty("token").GetGuid();
        Assert.Contains("\"ok\":true", Send("extension.install", new { token, grants = new[] { "import.file", "write.drafts" }, confirm = true }), StringComparison.Ordinal);
        using var run = JsonDocument.Parse(Send("extension.runPreview", new { extensionId = SampleId, hookId = "spells-from-csv", inputBase64 = Convert.ToBase64String(SampleCsv) }));
        var runToken = run.RootElement.GetProperty("result").GetProperty("token").GetGuid();
        Assert.Contains("\"drafts\":3", Send("extension.runImport", new { token = runToken, confirm = true }), StringComparison.Ordinal);
        foreach (var command in new[] { "extension.installChoose", "extension.chooseInput" })
            Assert.Contains("\"host.unsupported\"", Send(command, new { }), StringComparison.Ordinal);
        Assert.Contains("\"ok\":true", Send("extension.remove", new { extensionId = SampleId, confirm = true }), StringComparison.Ordinal);
        Assert.Contains("[]", Send("extension.list"), StringComparison.Ordinal);
    }
}
