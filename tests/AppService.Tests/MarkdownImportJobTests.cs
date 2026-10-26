using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Import Lab Task 13 (R3, D67): a Markdown import keeps import pages only. There is no attachment and no managed copy; the job
/// is stored under the SHA-256 of the bytes that were read, it is not resumable, and nothing it finds is active until accepted.
/// The original fixture class only.
/// </summary>
public class MarkdownImportJobTests
{
    private static byte[] ClassFixture => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "lab", "placeholder-class.md"));

    private static byte[] FixtureBook => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf"));

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static SourceRecord Homebrew(TempApp temp) => temp.App.CreateHomebrewSource(new("Test Markdown Book", [RulesFamilies.Srd521]));

    /// <summary>
    /// The app's temp copies (<c>tomestack-markdown-*</c>) that hold exactly <paramref name="bytes"/>. The temp folder is shared with
    /// every other test, so a test gives its import bytes of its own (<see cref="Unique"/>) and looks only for copies of those: a copy
    /// this test's app leaked would still be found, and a copy another test is writing or deleting is not read as this test's.
    /// </summary>
    private static string[] CopiesOf(byte[] bytes)
    {
        var found = new List<string>();
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), "tomestack-markdown-*"))
        {
            try
            {
                if (new FileInfo(file).Length == bytes.Length && File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes))
                    found.Add(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another test's copy, written or deleted while we looked.
            }
        }
        return [.. found];
    }

    /// <summary>The class fixture with a trailing comment no other test uses (the extractor drops comment content).</summary>
    private static byte[] Unique(byte[] bytes) => [.. bytes, .. Encoding.UTF8.GetBytes($"\n\n<!-- {Guid.NewGuid():N} -->\n")];

    private static ImportJobRecord Import(TempApp temp, Guid sourceId, byte[] bytes, ImportJobStatus expected = ImportJobStatus.Completed) =>
        temp.WaitForImport(temp.App.StartMarkdownImport(sourceId, bytes).Id, expected);

    [Fact]
    public void Markdown_job_stores_pages_by_sha_and_detects()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var bytes = ClassFixture;

        var job = Import(temp, source.Id, bytes);

        Assert.Equal("markdown", job.Format);
        Assert.Equal(Sha(bytes), job.Sha256);
        Assert.True(job.PagesDone >= 1);
        Assert.Null(job.NextPage);
        var stored = temp.App.Store.FindImportPage(job.Sha256, 1);
        Assert.NotNull(stored);
        Assert.Contains("Placeholder Class", stored.Text, StringComparison.Ordinal);
        Assert.True(job.Candidates > 0, "detection read the stored pages");
        var candidates = temp.App.ListCandidates(new(job.Id));
        Assert.Equal(job.Candidates, candidates.Count);
        Assert.All(candidates, c => Assert.Equal(job.Sha256, c.AttachmentSha256));
        Assert.Equal(["created", "started", "completed"], temp.App.ImportAudit(job.Id).Select(a => a.Event));
    }

    [Fact]
    public void Markdown_import_keeps_no_attachment_or_file()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var attachmentsBefore = temp.App.Store.ListAttachments().Count;
        var managedFolder = Path.Combine(temp.Directory, "attachments");
        var managedBefore = Directory.Exists(managedFolder) ? Directory.GetFiles(managedFolder, "*", SearchOption.AllDirectories) : [];
        var mine = Unique(ClassFixture);

        Import(temp, source.Id, mine);

        Assert.Null(temp.App.Store.FindSource(source.Id)!.AttachmentId);
        Assert.Equal(attachmentsBefore, temp.App.Store.ListAttachments().Count);
        Assert.Equal(managedBefore, Directory.Exists(managedFolder) ? Directory.GetFiles(managedFolder, "*", SearchOption.AllDirectories) : []);
        Assert.Empty(CopiesOf(mine)); // the worker's temp copy is deleted when the run ends
    }

    [Fact]
    public void Imported_candidates_are_not_active_until_accepted()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var revisions = temp.App.Store.ListRevisions().Count;

        var job = Import(temp, source.Id, ClassFixture);

        Assert.True(job.Candidates > 0);
        Assert.Equal(revisions, temp.App.Store.ListRevisions().Count);
        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == source.Id);
        Assert.All(temp.App.ListCandidates(new(job.Id)), c => Assert.Equal(CandidateStatus.Pending, c.Status));
    }

    [Fact]
    public void Markdown_job_is_not_resumable()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var failed = Import(temp, source.Id, [0xFF, 0xFE, 0x41, 0x00], ImportJobStatus.Failed); // not UTF-8

        Assert.Equal("markdown.encoding", failed.FailureCode);
        var refused = Assert.Throws<AppValidationException>(() => temp.App.ResumeImport(failed.Id));
        Assert.Equal("import.markdown-read-again", refused.Problems[0].Code);
        Assert.Equal("Import the Markdown file again.", refused.Problems[0].Message);
        Assert.Equal(ImportJobStatus.Failed, temp.App.ImportStatus(failed.Id).Status); // nothing was started

        var done = Import(temp, source.Id, ClassFixture);
        Assert.Equal("import.not-resumable", Assert.Throws<AppValidationException>(() => temp.App.ResumeImport(done.Id)).Problems[0].Code);
    }

    [Fact]
    public void Markdown_too_large_stores_nothing()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var big = new byte[ExtractionLimitsBytes + 1];
        Array.Fill(big, (byte)'a');

        var refused = Assert.Throws<AppValidationException>(() => temp.App.StartMarkdownImport(source.Id, big));

        Assert.Equal("markdown.too-large", refused.Problems[0].Code);
        Assert.Empty(temp.App.ListImports());
        Assert.Null(temp.App.Store.FindImportPage(Sha(big), 1));
        Assert.Empty(CopiesOf(big));
    }

    private const int ExtractionLimitsBytes = ImportWorker.ExtractionLimits.MarkdownMaxBytes;

    [Fact]
    public void A_picked_file_is_read_once_and_a_missing_or_oversized_one_is_refused_by_its_code()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var folder = Path.Combine(temp.Directory, "picked");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "class.md");
        File.WriteAllBytes(path, ClassFixture);

        var job = temp.WaitForImport(temp.App.StartMarkdownImportFile(source.Id, path).Id);
        Assert.Equal(Sha(ClassFixture), job.Sha256);
        Assert.True(File.Exists(path), "the picked file is left alone");

        Assert.Equal("markdown.missing", Assert.Throws<AppValidationException>(() => temp.App.StartMarkdownImportFile(source.Id, Path.Combine(folder, "gone.md"))).Problems[0].Code);
        var large = Path.Combine(folder, "large.md");
        File.WriteAllBytes(large, new byte[ExtractionLimitsBytes + 1]);
        Assert.Equal("markdown.too-large", Assert.Throws<AppValidationException>(() => temp.App.StartMarkdownImportFile(source.Id, large)).Problems[0].Code);
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal("markdown.unreadable", Assert.Throws<AppValidationException>(() => temp.App.StartMarkdownImportFile(source.Id, path)).Problems[0].Code);
    }

    [Fact]
    public void Search_and_page_use_the_markdown_sha_without_a_pdf()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        Assert.Empty(temp.App.SearchImportedText(new(source.Id, "Placeholder")));
        Assert.Equal("import.page-not-extracted", Assert.Throws<AppValidationException>(() => temp.App.ImportedPage(new(source.Id, 1))).Problems[0].Code);

        var job = Import(temp, source.Id, ClassFixture);

        var hits = temp.App.SearchImportedText(new(source.Id, "Placeholder"));
        Assert.NotEmpty(hits);
        Assert.Contains("Placeholder", temp.App.ImportedPage(new(source.Id, hits[0].Page)).Text, StringComparison.Ordinal);
        Assert.Contains("Placeholder", temp.App.ImportedPage(new(source.Id, 1, job.Sha256)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_newest_markdown_job_decides_when_there_are_two()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        Import(temp, source.Id, Encoding.UTF8.GetBytes("# Older Alpha\n\nAlphaword only here.\n"));
        Assert.NotEmpty(temp.App.SearchImportedText(new(source.Id, "Alphaword")));

        // The fixed test clock gives both jobs one CreatedAt; the later one is the newest by list order.
        Import(temp, source.Id, Encoding.UTF8.GetBytes("# Newer Beta\n\nBetaword only here.\n"));

        Assert.NotEmpty(temp.App.SearchImportedText(new(source.Id, "Betaword")));
        Assert.Empty(temp.App.SearchImportedText(new(source.Id, "Alphaword")));
    }

    [Fact]
    public void A_newer_failed_markdown_job_does_not_hide_the_earlier_good_one()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var good = Import(temp, source.Id, Encoding.UTF8.GetBytes("# Older Alpha\n\nAlphaword only here.\n"));
        var failed = Import(temp, source.Id, [0xFF, 0xFE, 0xFD, 0x80, 0x81], ImportJobStatus.Failed);
        Assert.Equal("markdown.encoding", failed.FailureCode);
        Assert.Equal(0, failed.PagesDone);

        Assert.NotEmpty(temp.App.SearchImportedText(new(source.Id, "Alphaword")));
        Assert.Contains("Alphaword", temp.App.ImportedPage(new(source.Id, 1)).Text, StringComparison.Ordinal);
        Assert.Equal(good.Sha256, Sha(Encoding.UTF8.GetBytes("# Older Alpha\n\nAlphaword only here.\n")));
    }

    [Fact]
    public void The_pdf_sha_wins_when_the_source_has_a_pdf()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", FixtureBook);
        temp.WaitForImport(temp.App.StartImport(new(source.Id, WholeDocument: true)).Id);
        var markdown = Import(temp, source.Id, ClassFixture);

        Assert.Empty(temp.App.SearchImportedText(new(source.Id, "Placeholder"))); // the PDF's pages are searched
        Assert.NotEmpty(temp.App.SearchImportedText(new(source.Id, "Fixture Ember Lance")));
        Assert.Contains("Fixture", temp.App.ImportedPage(new(source.Id, 2)).Text, StringComparison.Ordinal);
        Assert.Contains("Placeholder", temp.App.ImportedPage(new(source.Id, 1, markdown.Sha256)).Text, StringComparison.Ordinal); // by sha still works
    }

    [Fact]
    public void Pdf_job_json_has_no_format_field()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", FixtureBook);
        var pdf = temp.WaitForImport(temp.App.StartImport(new(source.Id, WholeDocument: true)).Id);
        var markdown = Import(temp, source.Id, ClassFixture);

        Assert.Null(pdf.Format);
        Assert.DoesNotContain("\"format\"", JsonSerializer.Serialize(temp.App.ImportStatus(pdf.Id), RulesJson.Compact), StringComparison.Ordinal);
        Assert.Contains("\"format\":\"markdown\"", JsonSerializer.Serialize(temp.App.ImportStatus(markdown.Id), RulesJson.Compact), StringComparison.Ordinal);
    }

    [Fact]
    public void Doc_with_only_warnings_completes_with_one_no_text_page()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);

        var job = Import(temp, source.Id, Encoding.UTF8.GetBytes("---\nnote: nothing else\n---\n"));

        Assert.Equal((1, 1, 1, 0), (job.PageCount, job.PagesDone, job.PagesWithoutText, job.Candidates));
        var page = temp.App.ImportedPage(new(source.Id, 1));
        Assert.Equal("", page.Text);
        Assert.Contains("page.no-text", page.Warnings);
        Assert.Contains("markdown.front-matter-ignored", page.Warnings);
    }

    [Fact]
    public void An_empty_file_completes_with_no_pages()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);

        var job = Import(temp, source.Id, []);

        Assert.Equal((0, 0, 0), (job.PageCount, job.PagesDone, job.Candidates));
    }

    [Fact]
    public void Markdown_pages_with_gaps_in_their_numbers_complete()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);

        var job = Import(temp, source.Id, Encoding.UTF8.GetBytes("<!-- page: 2 -->\nSecond text\n\n<!-- page: 6 -->\nSixth text\n"));

        Assert.Equal((6, 2), (job.PageCount, job.PagesDone));
        Assert.Contains("Sixth", temp.App.ImportedPage(new(source.Id, 6)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bundled_source_is_refused_as_for_a_pdf_import()
    {
        using var temp = new TempApp();
        var bundled = temp.App.ListSources().First(s => s.EditionVersion != "homebrew");

        var refused = Assert.Throws<AppValidationException>(() => temp.App.StartMarkdownImport(bundled.Id, ClassFixture));

        Assert.Equal("source.not-editable", refused.Problems[0].Code);
        Assert.Empty(temp.App.ListImports());
    }

    [Fact]
    public void A_written_for_family_outside_the_source_is_refused_and_no_job_is_written()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.StartMarkdownImport(source.Id, ClassFixture, [RulesFamilies.Srd51]));

        Assert.Equal("import.family-not-in-source", refused.Problems[0].Code);
        Assert.Empty(temp.App.ListImports());
    }

    [Fact]
    public void A_second_import_while_one_runs_is_busy_and_leaves_no_temp_copy()
    {
        using var gated = new GatedMarkdown();
        using var temp = new TempApp(markdownExtractor: gated);
        var source = Homebrew(temp);
        var mine = Unique(ClassFixture);
        var first = temp.App.StartMarkdownImport(source.Id, mine);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.StartMarkdownImport(source.Id, mine));

        Assert.Equal("import.busy", refused.Problems[0].Code);
        Assert.Single(CopiesOf(mine)); // the running job's copy only: the refused one is gone
        gated.Release();
        temp.WaitForImport(first.Id);
        Assert.Empty(CopiesOf(mine));
    }

    private sealed class GatedMarkdown : ImportWorker.IDocumentExtractor, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(0);

        public void Release() => _gate.Release();

        public async IAsyncEnumerable<ImportWorker.ExtractionEvent> ExtractAsync(string path, ImportWorker.PageScope scope, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            await foreach (var item in new ImportWorker.Extraction.MarkdownExtractor().ExtractAsync(path, scope, cancellationToken))
                yield return item;
        }

        public void Dispose() => _gate.Dispose();
    }

    // ---- the dispatcher ----

    private sealed class PickHost(string? chosen) : IHostServices
    {
        public string? AskedExtension { get; private set; }

        public bool CanOpenFiles => true;

        public string? ChooseSaveLocation(string suggestedFileName, string filterDescription, string extension) => null;

        public string? ChooseOpenFile(string filterDescription, string extension)
        {
            AskedExtension = extension;
            return chosen;
        }

        public bool OpenPdf(string path, int page, string title) => false;
    }

    private static JsonElement Dispatch(TempApp temp, IHostServices? host, string command, object payload) =>
        JsonDocument.Parse(new CommandDispatcher(temp.App, host: host).Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;

    [Fact]
    public void Both_commands_are_registered_and_start_markdown_data_round_trips()
    {
        Assert.Contains("import.startMarkdown", CommandDispatcher.Commands);
        Assert.Contains("import.startMarkdownData", CommandDispatcher.Commands);
        using var temp = new TempApp();
        var source = Homebrew(temp);

        var response = Dispatch(temp, null, "import.startMarkdownData", new { sourceId = source.Id, fileName = "class.md", base64 = Convert.ToBase64String(ClassFixture) });

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var result = response.GetProperty("result");
        Assert.Equal("markdown", result.GetProperty("format").GetString());
        Assert.Equal(Sha(ClassFixture), result.GetProperty("sha256").GetString());
        Assert.Equal(source.Id, result.GetProperty("sourceId").GetGuid());
        var job = temp.WaitForImport(result.GetProperty("id").GetGuid());
        Assert.True(job.Candidates > 0);
    }

    [Fact]
    public void Start_markdown_asks_the_native_dialog_for_md_and_reports_unsupported_without_one()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var path = Path.Combine(temp.Directory, "picked.md");
        Directory.CreateDirectory(temp.Directory);
        File.WriteAllBytes(path, ClassFixture);
        var host = new PickHost(path);

        var started = Dispatch(temp, host, "import.startMarkdown", new { sourceId = source.Id });

        Assert.True(started.GetProperty("ok").GetBoolean(), started.ToString());
        Assert.Equal(".md", host.AskedExtension);
        Assert.Equal("markdown", started.GetProperty("result").GetProperty("format").GetString());
        Assert.DoesNotContain(temp.Directory.Replace("\\", "\\\\", StringComparison.Ordinal), started.ToString(), StringComparison.OrdinalIgnoreCase);
        temp.WaitForImport(started.GetProperty("result").GetProperty("id").GetGuid());

        var cancelled = Dispatch(temp, new PickHost(null), "import.startMarkdown", new { sourceId = source.Id });
        Assert.True(cancelled.GetProperty("ok").GetBoolean());
        Assert.False(cancelled.TryGetProperty("result", out _), "a cancelled dialog starts nothing");
        Assert.Single(temp.App.ListImports(source.Id));

        Assert.Equal("unsupported", Dispatch(temp, null, "import.startMarkdown", new { sourceId = source.Id }).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void A_picked_path_that_is_not_a_local_md_file_is_refused_before_it_is_read()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);

        var response = Dispatch(temp, new PickHost(@"\\other-machine\share\class.md"), "import.startMarkdown", new { sourceId = source.Id });

        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal("pdf.path-not-local", response.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(temp.App.ListImports());
    }
}
