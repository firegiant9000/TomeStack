using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2 item 6, ADR-005, SPEC S-04: PDF attachments (managed copies by default), the numbered pdfRef → attachment
/// migration with its pre-upgrade backup, removal that keeps content, and page navigation through the host.
/// </summary>
public class AttachmentTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% TomeStack test PDF\n%%EOF\n");
    private static readonly byte[] OtherPdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% another test PDF\n%%EOF\n");

    private sealed class FakeHost(string? chosen = null) : IHostServices
    {
        public List<(string Path, int Page)> Opened { get; } = [];

        public bool CanOpenFiles => true;

        public string? ChooseSaveLocation(string suggestedFileName, string filterDescription, string extension) => null;

        public string? ChooseOpenFile(string filterDescription, string extension) => chosen;

        public bool OpenPdf(string path, int page, string title)
        {
            Opened.Add((path, page));
            return true;
        }
    }

    private static SourceRecord Homebrew(TempApp temp) => temp.App.CreateHomebrewSource(new("Test Book", [RulesFamilies.Srd51]));

    private static JsonElement Dispatch(TempApp temp, IHostServices? host, string command, object payload) =>
        JsonDocument.Parse(new CommandDispatcher(temp.App, host: host).Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;

    private static string Code(JsonElement response) =>
        response.GetProperty("error").GetProperty("diagnostics").EnumerateArray().First().GetProperty("code").GetString()!;

    [Fact]
    public void Migration_v3_turns_pdf_references_into_attachments_after_a_backup_and_never_fails_on_a_missing_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var books = Path.Combine(directory, "books");
        Directory.CreateDirectory(books);
        var existing = Path.Combine(books, "handbook.pdf");
        File.WriteAllBytes(existing, Pdf);
        var notPdf = Path.Combine(books, "notes.pdf");
        File.WriteAllText(notPdf, "just text");
        var missing = Path.Combine(books, "gone.pdf");
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);

        // A schema-2 data folder (M1), with four sources: two share the same file, one is missing, one is not a PDF.
        using (new SqliteStore(database, SqliteStore.Migrations[..2])) { }
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var refs = new[] { existing, existing, missing, notPdf };
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            for (var i = 0; i < ids.Length; i++)
            {
                var source = new SourceRecord
                {
                    Id = ids[i], Title = $"Book {i}", Publisher = "Test", RulesFamilies = [RulesFamilies.Srd51], EditionVersion = "1",
                    License = "Test", Redistributable = false, PdfRef = refs[i],
                };
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO sources (id, json) VALUES ($id, $json);";
                command.Parameters.AddWithValue("$id", ids[i].ToString("D"));
                command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(source, RulesJson.Compact));
                command.ExecuteNonQuery();
            }
        }

        using (var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: []))
        {
            Assert.Equal(3, app.GetInfo().SchemaVersion);
            Assert.True(File.Exists(SqliteStore.BackupPath(database, 2)));

            var first = app.GetAttachment(ids[0])!;
            var second = app.GetAttachment(ids[1])!;
            Assert.Equal((AttachmentMode.Managed, "available", "handbook.pdf"), (first.Mode, first.Status, first.OriginalFileName));
            Assert.Equal(first.AttachmentId, second.AttachmentId); // de-duplicated: one attachment, one file
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "attachments"), "*.pdf"));
            Assert.Equal((AttachmentMode.Linked, "missing"), (app.GetAttachment(ids[2])!.Mode, app.GetAttachment(ids[2])!.Status));
            Assert.Equal("missing", app.GetAttachment(ids[3])!.Status); // not a PDF: linked, flagged
            Assert.All(app.Store.ListSources(), s => Assert.Null(s.PdfRef));
        }
        Assert.Equal(missing, Text(database, $"SELECT legacy_pdf_ref FROM sources WHERE id = '{ids[2]:D}';"));
        File.SetAttributes(Directory.GetFiles(Path.Combine(directory, "attachments"))[0], FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    private static string? Text(string databasePath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    [Fact]
    public void Attaching_copies_a_read_only_pdf_by_content_hash_and_refuses_anything_else()
    {
        using var temp = new TempApp();
        var a = Homebrew(temp);
        var b = Homebrew(temp);

        var first = temp.App.AttachPdf(a.Id, "book.pdf", Pdf);
        var second = temp.App.AttachPdf(b.Id, "same-book.pdf", Pdf);

        Assert.Equal((AttachmentMode.Managed, "available", (long)Pdf.Length), (first.Mode, first.Status, first.ByteLength));
        Assert.Equal(first.AttachmentId, second.AttachmentId);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(temp.Directory, "attachments")));
        Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal("attachment.not-a-pdf", Assert.Throws<AppValidationException>(() => temp.App.AttachPdf(a.Id, "x.pdf", Encoding.ASCII.GetBytes("<html>"))).Problems[0].Code);
        Assert.Equal("attachment.not-a-pdf", Assert.Throws<AppValidationException>(() => temp.App.AttachPdf(a.Id, "x.pdf", [])).Problems[0].Code);
        Assert.Equal("attachment.file-name-invalid", Assert.Throws<AppValidationException>(() => temp.App.AttachPdf(a.Id, "", Pdf)).Problems[0].Code);
        Assert.Equal(first.AttachmentId, temp.App.GetAttachment(a.Id)!.AttachmentId); // a refused attach changes nothing
    }

    [Fact]
    public void Removing_an_attachment_says_what_breaks_needs_confirmation_and_keeps_the_content()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        temp.App.AttachPdf(source.Id, "book.pdf", Pdf);
        var feature = temp.App.SaveDraft(new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Feature, Name = "Test Cited Feature",
            RulesFamilies = [RulesFamilies.Srd51], Provenance = new(source.Id, new(42)), Status = RevisionStatus.Draft,
        });

        var preview = temp.App.PreviewDetach(source.Id);
        Assert.Equal((1, "book.pdf"), (preview.PageLinks, preview.OriginalFileName));
        Assert.Equal("attachment.confirmation-required", Code(Dispatch(temp, null, "source.detach", new { sourceId = source.Id })));
        Assert.NotNull(temp.App.GetAttachment(source.Id));

        temp.App.Detach(source.Id, confirm: true);

        Assert.Null(temp.App.GetAttachment(source.Id));
        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Directory, "attachments")));
        Assert.NotNull(temp.App.Store.FindRevision(feature)); // content stays
    }

    [Fact]
    public void A_shared_pdf_stays_until_its_last_source_lets_it_go()
    {
        using var temp = new TempApp();
        var (a, b) = (Homebrew(temp), Homebrew(temp));
        temp.App.AttachPdf(a.Id, "book.pdf", Pdf);
        temp.App.AttachPdf(b.Id, "book.pdf", Pdf);

        temp.App.Detach(a.Id, confirm: true);
        Assert.Single(Directory.GetFiles(Path.Combine(temp.Directory, "attachments")));
        Assert.Equal("available", temp.App.GetAttachment(b.Id)!.Status);

        temp.App.AttachPdf(b.Id, "new-edition.pdf", OtherPdf); // replacing the last user frees the old file
        Assert.Single(Directory.GetFiles(Path.Combine(temp.Directory, "attachments")));
        Assert.Equal("new-edition.pdf", temp.App.GetAttachment(b.Id)!.OriginalFileName);
    }

    [Fact]
    public void Opening_a_cited_page_goes_through_the_host_and_never_sends_a_path_to_the_ui()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        temp.App.AttachPdf(source.Id, "book.pdf", Pdf);
        var host = new FakeHost();

        var response = Dispatch(temp, host, "source.openPage", new { sourceId = source.Id, page = 42 });

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var (path, page) = Assert.Single(host.Opened);
        Assert.Equal(42, page);
        Assert.StartsWith(Path.Combine(temp.Directory, "attachments"), path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(temp.Directory.Replace("\\", "\\\\", StringComparison.Ordinal), response.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("unsupported", Dispatch(temp, null, "source.openPage", new { sourceId = source.Id, page = 1 }).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("attachment.page-invalid", Code(Dispatch(temp, host, "source.openPage", new { sourceId = source.Id, page = 0 })));
        Assert.Equal("attachment.none", Code(Dispatch(temp, host, "source.openPage", new { sourceId = Homebrew(temp).Id, page = 1 })));
    }

    [Fact]
    public void A_linked_pdf_that_changed_opens_with_a_warning_and_a_deleted_one_asks_to_reattach()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var outside = Path.Combine(temp.Directory, "outside.pdf");
        File.WriteAllBytes(outside, Pdf);
        var host = new FakeHost(chosen: outside);
        var attached = Dispatch(temp, host, "source.attachPdf", new { sourceId = source.Id, mode = "linked" });
        Assert.True(attached.GetProperty("ok").GetBoolean(), attached.ToString());
        Assert.Equal("linked", attached.GetProperty("result").GetProperty("attachment").GetProperty("mode").GetString());
        Assert.False(Directory.Exists(Path.Combine(temp.Directory, "attachments")) && Directory.GetFiles(Path.Combine(temp.Directory, "attachments")).Length > 0);

        File.WriteAllBytes(outside, OtherPdf);
        var changed = temp.App.OpenPage(host, source.Id, 3);
        Assert.Equal("attachment.changed", Assert.Single(changed.Warnings).Code);

        File.Delete(outside);
        Assert.Equal("attachment.missing", Assert.Throws<AppValidationException>(() => temp.App.OpenPage(host, source.Id, 3)).Problems[0].Code);
    }

    [Fact]
    public void Attachments_are_machine_local_exports_leave_them_out_and_imports_keep_the_local_one()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var info = temp.App.AttachPdf(source.Id, "book.pdf", Pdf);
        var character = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var feature = temp.App.Publish(temp.App.SaveDraft(new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Feat, Name = "Test Book Feat",
            RulesFamilies = [RulesFamilies.Srd51], Provenance = new(source.Id, new(3)), Status = RevisionStatus.Draft,
        })).Published;
        temp.App.SaveCharacter(character.Character with { Pins = [.. character.Character.Pins, feature] });

        var package = temp.App.ExportCharacters([character.Character.Id]);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(package.Content));
        var entry = new StreamReader(zip.GetEntry($"sources/{source.Id:D}.json")!.Open()).ReadToEnd();
        Assert.DoesNotContain("attachmentId", entry, StringComparison.Ordinal);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

        temp.App.ApplyImport(package.Content); // re-import on the same machine: the local attachment stays
        Assert.Equal(info.AttachmentId, temp.App.GetAttachment(source.Id)!.AttachmentId);

        using var clean = new TempApp();
        clean.App.ApplyImport(package.Content); // a clean machine: the PDF is absent, and the UI can say so
        Assert.Null(clean.App.GetAttachment(source.Id));
    }
}
