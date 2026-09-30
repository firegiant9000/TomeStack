using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2.1 (audit H1; SPEC P-02, Q-01; docs/features/package-format.md "Full library backup"): "Back up everything" writes
/// the whole library, drafts, unused homebrew and managed PDFs included, and "Restore full backup" brings it back into
/// a clean data folder exactly. All content is original test content.
/// </summary>
public class LibraryBackupTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% TomeStack library backup test PDF\n%%EOF\n");

    private static ContentRevision Feat(SourceRecord source, Guid contentId, string name, int initiative) => new()
    {
        ContentId = contentId,
        RevisionId = Guid.Empty,
        Kind = ContentKind.Feat,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
        Provenance = new(source.Id, new(3)),
        Status = RevisionStatus.Draft,
        Effects = [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = $"{initiative}" }],
    };

    private static ContentReference Publish(TomeStackApp app, ContentRevision draft) => app.Publish(app.SaveDraft(draft)).Published;

    private sealed record Library(SourceRecord Source, ContentReference Pinned, ContentReference Superseded, ContentReference Unpinned, ContentReference Draft, Guid Character);

    /// <summary>What H1 found unprotected: a draft, a published revision no character uses, a superseded revision, a campaign no character is in, and a PDF.</summary>
    private static Library Fill(TomeStackApp app)
    {
        var source = app.CreateHomebrewSource(new("Test Library Homebrew", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        var featId = Guid.NewGuid();
        var superseded = Publish(app, Feat(source, featId, "Test Quick Hands", 1));
        var pinned = Publish(app, Feat(source, featId, "Test Quick Hands", 2));
        var unpinned = Publish(app, Feat(source, Guid.NewGuid(), "Test Unused Trick", 3));
        var draft = app.SaveDraft(Feat(source, Guid.NewGuid(), "Test Half-Written Idea", 4));
        app.AttachPdf(source.Id, "homebrew-notes.pdf", Pdf);
        var fixture = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        var character = app.SaveCharacter(fixture with { Pins = [.. fixture.Pins, pinned] }).Character.Id;
        app.AddGapNote(new(character, new(GapTargetKind.Field, FieldId: FieldIds.ArmorClass), "Test note: the table ruled a cover bonus."));
        app.SaveCampaign(new() { Id = Guid.Empty, Name = "Test Unused Campaign", RulesFamily = RulesFamilies.Srd51, AllowedSources = [source.Id], HouseRules = "Test rules." });
        return new(source, pinned, superseded, unpinned, draft, character);
    }

    /// <summary>Everything a user could lose, serialized, so two data folders compare as a whole.</summary>
    private static string Snapshot(TomeStackApp app)
    {
        var store = app.Store;
        return JsonSerializer.Serialize(new
        {
            revisions = store.ListRevisionsInOrder().Select(r => (r.RevisionId, store.RevisionHash(r.RevisionId))).Select(x => $"{x.RevisionId}:{x.Item2}"),
            sources = store.ListSources(),
            characters = store.ListCharacters(),
            sheets = store.ListCharacters().Select(c => app.GetCharacter(c.Id).Sheet),
            campaigns = store.ListCampaigns(),
            gapNotes = store.ListAllGapNotes(),
            attachments = store.ListAttachments(),
            content51 = app.ListContent(RulesFamilies.Srd51),
            content521 = app.ListContent(RulesFamilies.Srd521),
        }, RulesJson.Options);
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "tomestack-tests", $"{Guid.NewGuid():N}.tomestack.zip");

    private static LibraryBackupResult Backup(TomeStackApp app, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        return app.WriteLibraryBackup(file);
    }

    [Fact]
    public void A_full_backup_restores_everything_into_a_clean_data_folder()
    {
        using var origin = new TempApp();
        var library = Fill(origin.App);
        var path = TempFile();
        try
        {
            var written = Backup(origin.App, path);
            Assert.Empty(written.Warnings);
            Assert.Equal((1, 1, 1, 1, Pdf.Length), (written.Contents.Characters, written.Contents.Campaigns, written.Contents.GapNotes, written.Contents.ManagedPdfs, written.Contents.ManagedPdfBytes));
            // Every draft (each publish leaves its draft behind; revisions are insert-only) and every published revision.
            var bundled = TomeStackApp.BundledPacks.SelectMany(p => TomeStackApp.LoadBundledPack(p).Revisions).Select(r => r.RevisionId).ToHashSet();
            var own = origin.App.Store.ListRevisions().Where(r => !bundled.Contains(r.RevisionId)).ToList();
            Assert.Equal(own.Count(r => r.Status != RevisionStatus.Published), written.Contents.DraftRevisions);
            Assert.Equal(own.Count(r => r.Status == RevisionStatus.Published), written.Contents.PublishedRevisions);
            Assert.Equal(3, own.Count(r => r.Status == RevisionStatus.Published && r.Provenance.SourceId == library.Source.Id));

            using var clean = new TempApp();
            Assert.Empty(clean.App.ListCharacters());
            var preview = clean.App.PreviewLibraryRestore(path);
            Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
            Assert.Contains(preview.Items, i => i.Kind == "attachment" && i.Name == "homebrew-notes.pdf" && i.Action == PackageItemAction.Add);
            Assert.Contains(preview.Items, i => i.Kind == "contentRevision" && i.Id == library.Draft.RevisionId && i.Action == PackageItemAction.Add);
            Assert.Empty(clean.App.ListCharacters()); // the preview wrote nothing

            var restored = clean.App.ApplyLibraryRestore(path);

            Assert.Null(restored.SafetyCopy); // nothing was replaced
            Assert.Equal(1, restored.PdfsCopied);
            Assert.Equal(Snapshot(origin.App), Snapshot(clean.App));
            var attachment = clean.App.GetAttachment(library.Source.Id)!;
            Assert.Equal("homebrew-notes.pdf", attachment.OriginalFileName);
            Assert.Equal(Pdf, File.ReadAllBytes(AttachmentFiles.ManagedPath(clean.App.Store, clean.App.Store.FindAttachment(attachment.AttachmentId)!.Sha256!)));
            // The newest revision is still the newest (restore keeps the stored order), and the draft is still a draft.
            Assert.True(clean.App.ListContent(RulesFamilies.Srd51).Single(o => o.Reference == library.Superseded).Superseded);
            Assert.Equal(RevisionStatus.Draft, clean.App.Store.FindRevision(library.Draft)!.Status);

            // A restart's clean-up keeps the restored PDF: its record is there.
            clean.Reopen();
            Assert.Equal(Snapshot(origin.App), Snapshot(clean.App));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_bundled_SRD_packs_and_extracted_text_are_not_in_a_full_backup()
    {
        using var temp = new TempApp();
        Fill(temp.App);
        var path = TempFile();
        try
        {
            Backup(temp.App, path);
            using var zip = ZipFile.OpenRead(path);
            var bundled = TomeStackApp.BundledPacks.SelectMany(p => TomeStackApp.LoadBundledPack(p).Revisions).Select(r => $"content/{r.RevisionId:D}.json").ToHashSet();
            Assert.DoesNotContain(zip.Entries, e => bundled.Contains(e.FullName));
            Assert.All(zip.Entries, e => Assert.Matches("^(manifest\\.json|(sources|content|characters|campaigns|gaps|attachments)/[0-9a-f-]{36}\\.json|files/[0-9a-f]{64}\\.pdf)$", e.FullName));

            using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
            Assert.Equal(("library", "backup", PackageManifest.LibraryFormatVersion), (manifest.RootElement.GetProperty("scope").GetString(), manifest.RootElement.GetProperty("purpose").GetString(), manifest.RootElement.GetProperty("formatVersion").GetInt32()));
            Assert.Equal("", SchemaTests.Validate("package-manifest", manifest.RootElement));
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("attachments/", StringComparison.Ordinal)))
            {
                using var document = JsonDocument.Parse(entry.Open());
                Assert.Equal("", SchemaTests.Validate("attachment", document.RootElement));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Character_packages_stay_format_v5_and_an_ordinary_import_refuses_a_full_backup()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App);
        Assert.Equal(PackageManifest.CharacterFormatVersion, temp.App.ExportCharacters([library.Character]).Manifest.FormatVersion);
        var path = TempFile();
        try
        {
            Backup(temp.App, path);
            var preview = temp.App.PreviewImport(File.ReadAllBytes(path));
            Assert.False(preview.CanApply);
            Assert.Equal("package.library-backup", Assert.Single(preview.Errors).Code);
            Assert.Throws<PackageException>(() => temp.App.ApplyImport(File.ReadAllBytes(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_restore_refuses_a_character_package()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App);
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllBytes(path, temp.App.ExportCharacters([library.Character]).Content);
            var preview = temp.App.PreviewLibraryRestore(path);
            Assert.False(preview.CanApply);
            Assert.Contains(preview.Errors, e => e.Code == "restore.not-a-library-backup");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_backup_with_an_altered_pdf_is_refused_before_anything_changes()
    {
        using var origin = new TempApp();
        Fill(origin.App);
        var path = TempFile();
        try
        {
            Backup(origin.App, path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var pdf = zip.Entries.Single(e => e.FullName.StartsWith("files/", StringComparison.Ordinal));
                var name = pdf.FullName;
                pdf.Delete();
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n% altered, same name %%EOF\n"));
            }

            using var clean = new TempApp();
            var before = Snapshot(clean.App);
            var preview = clean.App.PreviewLibraryRestore(path);
            Assert.False(preview.CanApply);
            Assert.Contains(preview.Errors, e => e.Code is "package.hash-mismatch" or "package.entry-too-large" or "package.content-too-large" or "package.invalid-json");
            Assert.Throws<PackageException>(() => clean.App.ApplyLibraryRestore(path));
            Assert.Equal(before, Snapshot(clean.App));
            Assert.False(Directory.Exists(clean.App.Store.AttachmentsDirectory) && Directory.EnumerateFiles(clean.App.Store.AttachmentsDirectory).Any());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Restoring_over_existing_data_takes_a_database_copy_first_and_deletes_nothing()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App);
        var path = TempFile();
        try
        {
            Backup(temp.App, path);
            // After the backup: the character is renamed and a new one is created.
            var character = temp.App.Store.FindCharacter(library.Character)!;
            temp.App.SaveCharacter(character with { Name = "Test Renamed" });
            var later = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { Id = Guid.NewGuid() }).Character.Id;

            var preview = temp.App.PreviewLibraryRestore(path);
            Assert.Contains(preview.Items, i => i.Kind == "character" && i.Action == PackageItemAction.Replace);
            var restored = temp.App.ApplyLibraryRestore(path);

            Assert.NotNull(restored.SafetyCopy);
            Assert.Equal(1, restored.Replaced);
            Assert.Equal(0, restored.PdfsCopied); // the PDF is already here
            Assert.Equal(character.Name, temp.App.Store.FindCharacter(library.Character)!.Name);
            Assert.NotNull(temp.App.Store.FindCharacter(later)); // not in the backup, and kept
            // The copy has the state from just before the restore.
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(temp.Directory, restored.SafetyCopy!)};Pooling=False;Mode=ReadOnly");
            connection.Open();
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT name FROM characters WHERE id = $id;";
            query.Parameters.AddWithValue("$id", library.Character.ToString("D"));
            Assert.Equal("Test Renamed", query.ExecuteScalar());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_damaged_pdf_copy_is_left_out_with_a_warning_and_the_rest_restores()
    {
        using var origin = new TempApp();
        var library = Fill(origin.App);
        var managed = AttachmentFiles.ManagedPath(origin.App.Store, origin.App.Store.ListAttachments().Single().Sha256!);
        File.SetAttributes(managed, FileAttributes.Normal);
        File.WriteAllBytes(managed, Encoding.ASCII.GetBytes("%PDF-1.4\n% bit rot %%EOF\n"));
        var path = TempFile();
        try
        {
            var written = Backup(origin.App, path);
            Assert.Equal("backup.pdf-unreadable", Assert.Single(written.Warnings).Code);
            Assert.Contains("Test Library Homebrew", written.Warnings[0].Message, StringComparison.Ordinal);

            using var clean = new TempApp();
            var restored = clean.App.ApplyLibraryRestore(path);
            Assert.Equal(0, restored.PdfsCopied);
            Assert.Null(clean.App.GetAttachment(library.Source.Id)); // re-attach it
            Assert.NotNull(clean.App.Store.FindRevision(library.Draft));
            Assert.NotNull(clean.App.Store.FindCharacter(library.Character));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Edits the JSON entries of a backup file in place and re-signs its manifest.</summary>
    private static void EditBackup(string path, string folder, Action<System.Text.Json.Nodes.JsonNode> change) =>
        File.WriteAllBytes(path, PackageEditor.Edit(File.ReadAllBytes(path), p => p.StartsWith(folder, StringComparison.Ordinal), change));

    [Fact]
    public void A_source_kept_local_brings_no_pdf_and_no_orphan_attachment_record()
    {
        // Review L1: the backup's PDF must not be copied (and kept forever) when no restored source will use it.
        using var origin = new TempApp();
        var library = Fill(origin.App);
        var path = TempFile();
        try
        {
            Backup(origin.App, path);
            using var target = new TempApp();
            // The same source exists here with different metadata, so the restore asks, and the user keeps theirs.
            target.App.Store.UpsertSource(library.Source with { Title = "Test Library Homebrew (mine)" });
            var preview = target.App.PreviewLibraryRestore(path);
            Assert.Contains(preview.Items, i => i.Kind == "source" && i.Id == library.Source.Id && i.Action == PackageItemAction.Replace);

            var restored = target.App.ApplyLibraryRestore(path, new Dictionary<Guid, SourceChoice> { [library.Source.Id] = SourceChoice.KeepLocal });

            Assert.Equal(0, restored.PdfsCopied);
            Assert.Empty(target.App.Store.ListAttachments());
            Assert.False(Directory.Exists(target.App.Store.AttachmentsDirectory) && Directory.EnumerateFiles(target.App.Store.AttachmentsDirectory).Any());
            Assert.Equal("Test Library Homebrew (mine)", target.App.Store.FindSource(library.Source.Id)!.Title);
            Assert.NotNull(target.App.Store.FindRevision(library.Draft)); // the rest is restored
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(@"\\attacker-host\share\book.pdf")]
    [InlineData(@"\\?\C:\books\book.pdf")]
    [InlineData(@"books\book.pdf")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData(@"C:\books\..\Windows\book.pdf")]
    public void A_linked_pdf_outside_a_local_drive_is_not_restored(string linkedPath)
    {
        // Review R1/L2: listing sources checks that a linked file exists. A network path would send the user's Windows
        // credentials to another machine without a click, so a restore never records one.
        using var origin = new TempApp();
        var source = origin.App.CreateHomebrewSource(new("Test Linked Book", [RulesFamilies.Srd521]));
        var real = Path.Combine(Path.GetTempPath(), "tomestack-tests", $"{Guid.NewGuid():N}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(real)!);
        File.WriteAllBytes(real, Pdf);
        var path = TempFile();
        try
        {
            origin.App.AttachPdfFile(source.Id, real, AttachmentMode.Linked);
            Backup(origin.App, path);
            EditBackup(path, "attachments/", node => node["linkedPath"] = linkedPath);

            using var clean = new TempApp();
            var preview = clean.App.PreviewLibraryRestore(path);
            Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
            var warning = Assert.Single(preview.Warnings, w => w.Code == "restore.linked-pdf-skipped");
            Assert.DoesNotContain(linkedPath, warning.Message, StringComparison.Ordinal);
            clean.App.ApplyLibraryRestore(path);

            Assert.Empty(clean.App.Store.ListAttachments());
            Assert.Null(clean.App.Store.FindSource(source.Id)!.AttachmentId);
        }
        finally
        {
            File.Delete(path);
            File.Delete(real);
        }
    }

    [Fact]
    public void A_linked_pdf_on_a_local_drive_is_restored_with_its_link()
    {
        using var origin = new TempApp();
        var source = origin.App.CreateHomebrewSource(new("Test Linked Book", [RulesFamilies.Srd521]));
        var real = Path.Combine(Path.GetTempPath(), "tomestack-tests", $"{Guid.NewGuid():N}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(real)!);
        File.WriteAllBytes(real, Pdf);
        var path = TempFile();
        try
        {
            origin.App.AttachPdfFile(source.Id, real, AttachmentMode.Linked);
            var written = Backup(origin.App, path);
            Assert.Equal((0, 1), (written.Contents.ManagedPdfs, written.Contents.LinkedPdfs));

            using var clean = new TempApp();
            var restored = clean.App.ApplyLibraryRestore(path);
            Assert.Equal(0, restored.PdfsCopied); // a linked file stays where it is
            Assert.Equal("linked", clean.App.GetAttachment(source.Id)!.Mode.ToString().ToLowerInvariant());
        }
        finally
        {
            File.Delete(path);
            File.Delete(real);
        }
    }

    [Fact]
    public void An_attachment_record_whose_size_differs_from_its_pdf_is_refused()
    {
        // Review R2: the recorded size feeds the free-space check and the next backup's manifest, so it must be true.
        using var origin = new TempApp();
        Fill(origin.App);
        var path = TempFile();
        try
        {
            Backup(origin.App, path);
            EditBackup(path, "attachments/", node => node["byteLength"] = 0);

            using var clean = new TempApp();
            var preview = clean.App.PreviewLibraryRestore(path);
            Assert.False(preview.CanApply);
            Assert.Contains(preview.Errors, e => e.Code == "package.invalid-json" && e.Message.Contains("different size", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Pdf_entries_that_unpack_to_much_more_than_they_occupy_are_refused_unread()
    {
        // Review R3: the writer stores PDFs uncompressed; a highly compressed "PDF" is a decompression bomb, not a backup.
        using var origin = new TempApp();
        Fill(origin.App);
        var path = TempFile();
        try
        {
            Backup(origin.App, path);
            var bomb = Encoding.ASCII.GetBytes("%PDF-1.4\n" + new string(' ', 200_000) + "%%EOF\n");
            var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bomb));
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                using (var stream = zip.CreateEntry($"files/{sha}.pdf", CompressionLevel.SmallestSize).Open())
                    stream.Write(bomb);
                var manifestEntry = zip.GetEntry("manifest.json")!;
                System.Text.Json.Nodes.JsonNode manifest;
                using (var read = manifestEntry.Open())
                    manifest = System.Text.Json.Nodes.JsonNode.Parse(read)!;
                manifest["entries"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["path"] = $"files/{sha}.pdf", ["kind"] = "pdf", ["sha256"] = sha, ["size"] = bomb.Length });
                manifestEntry.Delete();
                using var write = zip.CreateEntry("manifest.json").Open();
                write.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
            }

            using var clean = new TempApp();
            var preview = clean.App.PreviewLibraryRestore(path);
            Assert.False(preview.CanApply);
            Assert.Contains(preview.Errors, e => e.Code == "package.content-too-large");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_restore_preview_names_the_database_copy_and_a_revision_that_would_become_newest()
    {
        // Review R5, R6.
        using var temp = new TempApp();
        var library = Fill(temp.App);
        var path = TempFile();
        try
        {
            Backup(temp.App, path);
            temp.App.SaveCharacter(temp.App.Store.FindCharacter(library.Character)! with { Name = "Test Renamed" });

            var preview = temp.App.PreviewLibraryRestore(path);
            var replace = Assert.Single(preview.Warnings, w => w.Code == "restore.character-replace");
            Assert.Contains("pre-restore-", replace.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(preview.Warnings, w => w.Code == "package.character-replace");
            Assert.DoesNotContain(preview.Warnings, w => w.Code == "restore.newest-changes"); // nothing diverged

            // Another library published a newer revision of the same content that the backup does not have.
            using var other = new TempApp();
            other.App.Store.InTransaction(() =>
            {
                other.App.Store.UpsertSource(library.Source);
                foreach (var revision in temp.App.Store.ListRevisions(library.Pinned.ContentId).Where(r => r.RevisionId == library.Superseded.RevisionId))
                    other.App.Store.AddRevision(revision);
                other.App.Store.AddRevision(temp.App.Store.FindRevision(library.Superseded)! with { RevisionId = Guid.NewGuid(), Name = "Test Quick Hands (newer here)" });
            });
            var diverged = other.App.PreviewLibraryRestore(path);
            Assert.Contains(diverged.Warnings, w => w.Code == "restore.newest-changes" && w.Content == library.Pinned);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_backup_file_that_changes_after_its_preview_is_not_applied()
    {
        // Review R4: a sync client or a later save can replace the file between "Choose" and "Restore".
        using var origin = new TempApp();
        Fill(origin.App);
        var path = TempFile();
        try
        {
            Backup(origin.App, path);
            using var clean = new TempApp();
            var dispatcher = new CommandDispatcher(clean.App, host: new DialogHost(null, path));
            var token = Ok(dispatcher, "library.restoreChoose").GetProperty("token").GetString();
            File.AppendAllText(path, " ");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

            Assert.Equal("restore.file-changed", ErrorCode(dispatcher, "library.restoreApply", new { token, confirm = true }));
            Assert.Empty(clean.App.ListCharacters());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class DialogHost(string? save, string? open) : IHostServices
    {
        public bool CanOpenFiles => true;
        public string? ChooseSaveLocation(string suggestedFileName, string filterDescription, string extension) => save;
        public string? ChooseOpenFile(string filterDescription, string extension) => open;
    }

    private static JsonElement Ok(CommandDispatcher dispatcher, string command, object? payload = null)
    {
        using var response = JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload })));
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean(), response.RootElement.ToString());
        return response.RootElement.GetProperty("result").Clone();
    }

    private static string ErrorCode(CommandDispatcher dispatcher, string command, object? payload = null)
    {
        using var response = JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload })));
        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        var error = response.RootElement.GetProperty("error");
        return error.TryGetProperty("diagnostics", out var d) && d.ValueKind == JsonValueKind.Array && d.GetArrayLength() > 0
            ? d[0].GetProperty("code").GetString()!
            : error.GetProperty("code").GetString()!;
    }

    [Fact]
    public void The_commands_use_the_native_dialogs_never_return_a_path_and_need_a_confirmation()
    {
        using var origin = new TempApp();
        Fill(origin.App);
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            Assert.Equal("host.unsupported", ErrorCode(new CommandDispatcher(origin.App), "library.backupSaveAs"));
            var preview = Ok(new CommandDispatcher(origin.App), "library.backupPreview");
            Assert.Equal(1, preview.GetProperty("managedPdfs").GetInt32());

            var saved = Ok(new CommandDispatcher(origin.App, host: new DialogHost(path, null)), "library.backupSaveAs");
            Assert.True(saved.GetProperty("saved").GetBoolean());
            Assert.Equal(Path.GetFileName(path), saved.GetProperty("fileName").GetString());
            Assert.DoesNotContain(Path.GetTempPath(), saved.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".partial"));

            using var clean = new TempApp();
            var dispatcher = new CommandDispatcher(clean.App, host: new DialogHost(null, path));
            var chosen = Ok(dispatcher, "library.restoreChoose");
            Assert.True(chosen.GetProperty("preview").GetProperty("canApply").GetBoolean());
            Assert.DoesNotContain(Path.GetTempPath(), chosen.ToString(), StringComparison.OrdinalIgnoreCase);
            var token = chosen.GetProperty("token").GetString();

            Assert.Equal("restore.confirm-required", ErrorCode(dispatcher, "library.restoreApply", new { token }));
            Assert.Equal("restore.not-chosen", ErrorCode(dispatcher, "library.restoreApply", new { token = Guid.NewGuid(), confirm = true }));
            Assert.Empty(clean.App.ListCharacters());
            var result = Ok(dispatcher, "library.restoreApply", new { token, confirm = true });
            Assert.Equal(1, result.GetProperty("pdfsCopied").GetInt32());
            Assert.Single(clean.App.ListCharacters());
            Assert.Equal("restore.not-chosen", ErrorCode(dispatcher, "library.restoreApply", new { token, confirm = true })); // used once
        }
        finally
        {
            File.Delete(path);
        }
    }
}
