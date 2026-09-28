using System.IO.Compression;
using System.Security.Cryptography;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// M2.1 (SPEC P-02, Q-01; audit H1): "Back up everything" and "Restore full backup". A library backup is a v6 package of
/// the whole data folder, written to and read from a file (it can hold PDFs far beyond the 50 MB package limit, so
/// nothing is held in memory but the JSON). It is always a personal backup, never a share.
/// <para>
/// Included: every source (with its PDF link), every revision (published, superseded and draft) except the bundled SRD
/// packs (every install seeds them), characters, campaigns, gap notes, attachment records and managed PDF copies. Left
/// out: the files of linked PDFs (their records stay), and extracted PDF text, import jobs and candidates, which are
/// local-only (ADR-009) and can be re-read from the PDF.
/// </para>
/// </summary>
public sealed partial class PackageService
{
    public const int MaxLibraryEntries = 200_000;

    /// <summary>Unpacked JSON of a library backup (PDFs are streamed and not counted).</summary>
    public const long MaxLibraryJsonBytes = 256L * 1024 * 1024;

    public const int MaxLibraryPdfs = 10_000;

    /// <summary>The bundled packs' revision IDs: every install seeds them, so a library backup leaves them out.</summary>
    private IReadOnlySet<Guid> _bundledRevisions = new HashSet<Guid>();

    internal void SetBundledRevisions(IReadOnlySet<Guid> revisionIds) => _bundledRevisions = revisionIds;

    private static readonly ReadLimits LibraryLimits = new(MaxLibraryEntries, MaxLibraryJsonBytes);

    private sealed record LibraryPlan(
        List<SourceRecord> Sources, List<ContentRevision> Revisions, List<Character> Characters, List<Campaign> Campaigns,
        List<GapNote> GapNotes, List<Attachment> Attachments, int LinkedPdfs, List<string> Unreadable);

    /// <param name="verifyPdfs">Hash every managed PDF in full (writing); the preview only checks that each is there with its size.</param>
    private LibraryPlan PlanLibrary(bool verifyPdfs)
    {
        var sources = store.ListSources().ToList();
        var titles = sources.GroupBy(s => s.AttachmentId).Where(g => g.Key is not null).ToDictionary(g => g.Key!.Value, g => g.First().Title);
        var characters = store.ListCharacters().OrderBy(c => c.Id).ToList();
        var ids = characters.Select(c => c.Id).ToHashSet();
        var attachments = new List<Attachment>();
        var unreadable = new List<string>();
        var linked = 0;
        foreach (var attachment in store.ListAttachments())
        {
            if (attachment.Mode == AttachmentMode.Linked)
            {
                linked++;
                attachments.Add(attachment);
                continue;
            }
            var path = attachment.Sha256 is null ? null : AttachmentFiles.ManagedPath(store, attachment.Sha256);
            var intact = verifyPdfs
                ? AttachmentFiles.ManagedCopyIsIntact(store, attachment)
                : path is not null && File.Exists(path) && new FileInfo(path).Length == attachment.ByteLength;
            if (intact)
                attachments.Add(attachment);
            else
                unreadable.Add(titles.GetValueOrDefault(attachment.AttachmentId) ?? attachment.OriginalFileName);
        }
        return new(
            sources,
            [.. store.ListRevisionsInOrder().Where(r => !_bundledRevisions.Contains(r.RevisionId))],
            characters,
            [.. store.ListCampaigns()],
            [.. store.ListAllGapNotes().Where(n => ids.Contains(n.CharacterId))], // a note without its character cannot be restored
            attachments,
            linked,
            unreadable);
    }

    private LibraryBackupPreview Summary(LibraryPlan plan)
    {
        var managed = plan.Attachments.Where(a => a.Mode == AttachmentMode.Managed).DistinctBy(a => a.Sha256).ToList();
        var stamp = time.GetUtcNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return new(
            $"TomeStack-full-backup-{stamp}.tomestack.zip",
            plan.Characters.Count, plan.Campaigns.Count, plan.GapNotes.Count, plan.Sources.Count,
            plan.Revisions.Count(r => r.Status == RevisionStatus.Published), plan.Revisions.Count(r => r.Status != RevisionStatus.Published),
            managed.Count, managed.Sum(a => a.ByteLength), plan.LinkedPdfs, plan.Unreadable);
    }

    /// <summary><c>library.backupPreview</c>: what "Back up everything" would write. Nothing is written.</summary>
    public LibraryBackupPreview PreviewLibraryBackup() => Summary(PlanLibrary(verifyPdfs: false));

    /// <summary>
    /// Writes the library backup to <paramref name="output"/> (a seekable stream; the shell writes a <c>.partial</c> file
    /// and renames it). A managed PDF that is missing or no longer matches its hash is left out with a warning, so one
    /// damaged file never blocks the backup of everything else.
    /// </summary>
    public LibraryBackupResult WriteLibraryBackup(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var plan = PlanLibrary(verifyPdfs: true);
        var files = new SortedDictionary<string, (string Kind, byte[] Bytes)>(StringComparer.Ordinal);
        var withPdf = plan.Attachments.Select(a => a.AttachmentId).ToHashSet();
        foreach (var source in plan.Sources)
        {
            // Unlike a package, the backup keeps the source's PDF link (a restore re-links it), unless the PDF is left out.
            var attachment = source.AttachmentId is { } id && withPdf.Contains(id) ? source.AttachmentId : null;
            files[$"sources/{source.Id:D}.json"] = ("source", Json(source with { PdfRef = null, AttachmentId = attachment }));
        }
        foreach (var revision in plan.Revisions)
            files[$"content/{revision.RevisionId:D}.json"] = ("contentRevision", Json(revision));
        foreach (var character in plan.Characters)
            files[$"characters/{character.Id:D}.json"] = ("character", Json(character));
        foreach (var campaign in plan.Campaigns)
            files[$"campaigns/{campaign.Id:D}.json"] = ("campaign", Json(campaign));
        foreach (var note in plan.GapNotes)
            files[$"gaps/{note.Id:D}.json"] = ("gapNote", Json(note));
        foreach (var attachment in plan.Attachments)
            files[$"{AttachmentFolder}{attachment.AttachmentId:D}.json"] = ("attachment", Json(attachment));
        var pdfs = plan.Attachments.Where(a => a.Mode == AttachmentMode.Managed).DistinctBy(a => a.Sha256).OrderBy(a => a.Sha256, StringComparer.Ordinal).ToList();

        var createdAt = time.GetUtcNow();
        var manifest = new PackageManifest
        {
            FormatVersion = 6,
            Scope = PackageScope.Library,
            Purpose = ExportPurpose.Backup,
            CreatedAt = createdAt,
            AppVersion = typeof(PackageService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            Characters = [.. plan.Characters.Select(c => c.Id)],
            Entries =
            [
                .. files.Select(f => new PackageEntry(f.Key, f.Value.Kind, Hash(f.Value.Bytes), f.Value.Bytes.LongLength)),
                .. pdfs.Select(a => new PackageEntry($"{PdfFolder}{a.Sha256}.pdf", "pdf", a.Sha256!, a.ByteLength)),
            ],
            Notices = [.. plan.Sources.Select(Notice)],
            RevisionOrder = [.. plan.Revisions.Select(r => r.RevisionId)],
            AttachmentPolicy = PackageManifest.LibraryAttachmentPolicy,
        };

        long written;
        var start = output.Position;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, file) in files)
                WriteEntry(zip, path, file.Bytes, createdAt);
            foreach (var pdf in pdfs)
            {
                var entry = zip.CreateEntry($"{PdfFolder}{pdf.Sha256}.pdf", CompressionLevel.NoCompression); // PDFs are compressed already
                entry.LastWriteTime = createdAt;
                using var source = File.OpenRead(AttachmentFiles.ManagedPath(store, pdf.Sha256!));
                using var target = entry.Open();
                source.CopyTo(target);
            }
            // Last: a reader finds it by name, and every hash above is known by now.
            WriteEntry(zip, ManifestPath, Json(manifest), createdAt);
        }
        written = output.Position - start;

        var warnings = plan.Unreadable
            .Select(title => new Diagnostic("backup.pdf-unreadable", $"The PDF copy for '{title}' is missing or damaged, so it is not in the backup. Attach the PDF again; everything else is backed up."))
            .ToList();
        return new(Summary(plan).FileName, written, Summary(plan), warnings);
    }

    /// <summary><c>library.restorePreview</c>: checks a library backup file completely (every PDF's hash too). Nothing is written.</summary>
    public PackagePreview PreviewLibraryRestore(string path) => WithLibrary(path, (preview, _, _) => preview);

    /// <summary>
    /// Restores a library backup into this data folder after the same checks as the preview. Nothing is deleted: items
    /// not in the backup stay. Before anything is replaced (a character, campaign, gap note, or a source you chose to take
    /// from the backup), the whole database is copied to <c>backups/pre-restore-*.db</c>. PDFs are copied before the
    /// transaction; if it fails, the next start removes them.
    /// </summary>
    public LibraryRestoreResult ApplyLibraryRestore(string path, IReadOnlyDictionary<Guid, SourceChoice>? sourceChoices = null) =>
        WithLibrary(path, (preview, parsed, _) =>
        {
            if (!preview.CanApply || parsed is null)
                throw new PackageException(preview.Errors);
            var keepLocal = SourcesKeptLocal(preview, sourceChoices);

            var replaces = preview.Items.Any(i => i.Action == PackageItemAction.Replace && (i.Kind != "source" || !keepLocal.Contains(i.Id)));
            var safetyCopy = replaces ? WriteSafetyCopy() : null;

            var needed = parsed.Attachments
                .Where(a => a.Mode == AttachmentMode.Managed && !File.Exists(AttachmentFiles.ManagedPath(store, a.Sha256!)))
                .DistinctBy(a => a.Sha256)
                .ToList();
            EnsureFreeSpace(needed.Sum(a => a.ByteLength));
            var copied = 0;
            foreach (var attachment in needed)
            {
                try
                {
                    using var content = parsed.Pdfs[attachment.Sha256!].Open();
                    if (AttachmentFiles.RestoreManaged(store, content, attachment.Sha256!))
                        copied++;
                }
                catch (Exception ex) when (ex is AttachmentException or InvalidDataException)
                {
                    // The file changed since the preview. Copies made so far have no record and go at the next start.
                    throw new PackageException([new("restore.pdf-failed", $"A PDF in the backup could not be restored ({ex.Message}) Nothing else was changed.")]);
                }
            }

            var warnings = new List<Diagnostic>();
            var (added, replaced, unchanged) = Commit(parsed, keepLocal, warnings);
            return new LibraryRestoreResult(added, replaced, unchanged, copied, safetyCopy, warnings);
        });

    private T WithLibrary<T>(string path, Func<PackagePreview, ParsedPackage?, List<Diagnostic>, T> use)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var errors = new List<Diagnostic>();
        FileStream file;
        try
        {
            file = File.OpenRead(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new("restore.unreadable", "The backup file cannot be read. Check that it still exists and is not open in another program."));
            return use(new PackagePreview(false, null, [], errors, []), null, errors);
        }
        using (file)
        {
            ZipArchive zip;
            try
            {
                zip = new ZipArchive(file, ZipArchiveMode.Read);
            }
            catch (InvalidDataException ex)
            {
                errors.Add(new("package.invalid-archive", $"The file is not a readable backup: {ex.Message}"));
                return use(new PackagePreview(false, null, [], errors, []), null, errors);
            }
            using (zip)
            {
                var parsed = ParseArchive(zip, LibraryLimits, errors);
                if (parsed is not null && parsed.Manifest.Scope != PackageScope.Library)
                {
                    errors.Add(new("restore.not-a-library-backup", "This file is a character package, not a full backup. Use \"Import package\" instead."));
                    parsed = null;
                }
                var warnings = new List<Diagnostic>();
                if (parsed is not null)
                    VerifyPdfs(parsed, errors);
                var (preview, checkedPackage) = BuildPreview(parsed, errors, warnings);
                return use(preview, checkedPackage, errors);
            }
        }
    }

    /// <summary>Streams every PDF entry: the declared size, the PDF signature, the size limit and the SHA-256 in its name.</summary>
    private static void VerifyPdfs(ParsedPackage parsed, List<Diagnostic> errors)
    {
        if (parsed.Pdfs.Count > MaxLibraryPdfs)
        {
            errors.Add(new("package.too-many-entries", $"The backup has {parsed.Pdfs.Count} PDFs; the limit is {MaxLibraryPdfs}."));
            return;
        }
        var declared = parsed.Manifest.Entries.Where(e => e.Path.StartsWith(PdfFolder, StringComparison.Ordinal)).ToDictionary(e => e.Sha256, e => e.Size, StringComparer.Ordinal);
        foreach (var (sha, entry) in parsed.Pdfs)
        {
            if (entry.Length > AttachmentFiles.MaxPdfBytes || entry.Length != declared.GetValueOrDefault(sha, -1))
            {
                errors.Add(new("package.entry-too-large", $"PDF entry '{entry.FullName}' is larger than listed or than {AttachmentFiles.MaxPdfBytes / (1024 * 1024)} MB."));
                continue;
            }
            try
            {
                using var stream = entry.Open();
                using var bounded = new BoundedStream(stream, AttachmentFiles.MaxPdfBytes);
                if (!PdfHashMatches(bounded, sha))
                    errors.Add(new("package.hash-mismatch", $"PDF entry '{entry.FullName}' does not match its hash; the backup may be damaged or altered."));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                errors.Add(new("package.invalid-archive", $"PDF entry '{entry.FullName}' cannot be read: {ex.Message}"));
            }
        }
    }

    private static bool PdfHashMatches(Stream stream, string sha)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var first = true;
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (first && (read < 5 || !buffer.AsSpan(0, 5).SequenceEqual("%PDF-"u8)))
                return false;
            first = false;
            hash.AppendData(buffer, 0, read);
        }
        return !first && Convert.ToHexStringLower(hash.GetHashAndReset()) == sha;
    }

    /// <summary>A managed record names a PDF in the backup; a linked one names a path. Messages never contain the path.</summary>
    private static void ExpectAttachment(string path, Attachment attachment, IReadOnlyDictionary<string, ZipArchiveEntry> pdfs, List<Diagnostic> errors)
    {
        var name = attachment.OriginalFileName;
        if (string.IsNullOrWhiteSpace(name) || name.Length > AttachmentFiles.MaxFileNameLength || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || attachment.ByteLength < 0)
            errors.Add(new("package.invalid-json", $"Entry '{path}' has an invalid file name or size."));
        else if (!Enum.IsDefined(attachment.Mode))
            errors.Add(new("package.invalid-json", $"Entry '{path}' has an unknown mode."));
        else if (attachment.Mode == AttachmentMode.Managed && (attachment.Sha256 is null || !pdfs.ContainsKey(attachment.Sha256)))
            errors.Add(new("package.entry-missing", $"Entry '{path}' is a PDF copy whose file is not in the backup."));
        else if (attachment.Mode == AttachmentMode.Linked && (string.IsNullOrWhiteSpace(attachment.LinkedPath) || attachment.LinkedPath.Length > 32_767))
            errors.Add(new("package.invalid-json", $"Entry '{path}' is a linked PDF without a valid location."));
    }

    /// <summary>The revisions in the order the manifest lists; the list must name each revision entry exactly once.</summary>
    private static List<ContentRevision> OrderAsStored(List<ContentRevision> revisions, IReadOnlyList<Guid>? order, List<Diagnostic> errors)
    {
        var byId = revisions.ToDictionary(r => r.RevisionId);
        if (order is null || order.Count != byId.Count || order.Distinct().Count() != order.Count || order.Any(id => !byId.ContainsKey(id)))
        {
            errors.Add(new("package.invalid-json", "The backup's revision order does not match its content entries."));
            return revisions;
        }
        return [.. order.Select(id => byId[id])];
    }

    private string WriteSafetyCopy()
    {
        Directory.CreateDirectory(backupDirectory);
        var stamp = time.GetUtcNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        for (var attempt = 0; ; attempt++)
        {
            var name = attempt == 0 ? $"pre-restore-{stamp}.db" : $"pre-restore-{stamp}-{attempt}.db";
            var target = Path.Combine(backupDirectory, name);
            if (File.Exists(target) && attempt < 100)
                continue;
            store.BackupTo(target);
            return $"{BackupFolderName}/{name}";
        }
    }

    /// <summary>Refuses before copying anything when the PDFs would not fit (with 256 MB to spare).</summary>
    private void EnsureFreeSpace(long bytes)
    {
        if (bytes == 0)
            return;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(store.AttachmentsDirectory));
            if (root is null || new DriveInfo(root).AvailableFreeSpace >= bytes + (256L * 1024 * 1024))
                return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return; // the copy itself fails cleanly if the disk is full
        }
        throw new PackageException([new("restore.disk-full", $"The PDFs in this backup need {bytes / (1024 * 1024)} MB, and the drive with your data folder does not have that much free space. Nothing was changed.")]);
    }

    /// <summary>Reads at most <paramref name="limit"/> bytes, whatever the archive declares.</summary>
    private sealed class BoundedStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            _read += n;
            if (_read > limit)
                throw new InvalidDataException($"A PDF entry is larger than {limit / (1024 * 1024)} MB.");
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
