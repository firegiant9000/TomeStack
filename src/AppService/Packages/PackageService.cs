using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Packages;

/// <summary>
/// Exports characters with their pinned content revisions and sources, and imports packages
/// through a preview step. Packages are untrusted input (SPEC Q-02): sizes, entry names and hashes
/// are checked before anything is parsed or written, and apply re-validates from the bytes.
/// </summary>
public sealed partial class PackageService(SqliteStore store, TimeProvider time, string backupDirectory)
{
    public const string BackupFolderName = "backups";

    public const long MaxPackageBytes = 50L * 1024 * 1024;
    public const long MaxEntryBytes = 5L * 1024 * 1024;

    /// <summary>Total decompressed size of all entries; bounds memory against archives that expand far beyond their size.</summary>
    public const long MaxTotalBytes = 64L * 1024 * 1024;
    public const int MaxEntries = 2_000;

    /// <summary>The first content schema whose published revisions were always validated before publishing (M1 item 3).</summary>
    private const int ValidatedOnPublishSchemaVersion = 3;
    private const string ManifestPath = "manifest.json";

    [GeneratedRegex("^(sources|content|characters|campaigns|gaps)/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryPathPattern();

    /// <summary>M2.1, library backups only: attachment records, and managed PDFs named by their SHA-256.</summary>
    [GeneratedRegex("^(attachments/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\.json|files/[0-9a-f]{64}\\.pdf)$", RegexOptions.CultureInvariant)]
    private static partial Regex LibraryEntryPathPattern();

    private const string AttachmentFolder = "attachments/";
    private const string PdfFolder = "files/";

    private sealed record ExportPlan(
        List<Character> Characters, List<ContentRevision> Revisions, List<SourceRecord> Sources, List<OmittedSource> Omitted, string FileName);

    /// <summary>What <see cref="Export"/> would write for <paramref name="purpose"/>, without writing it (ADR-007).</summary>
    public ExportPreview PreviewExport(IReadOnlyList<Guid> characterIds, ExportPurpose purpose)
    {
        var plan = Plan(characterIds, purpose);
        var gapNotes = purpose == ExportPurpose.Backup ? plan.Characters.Sum(c => store.ListGapNotes(c.Id).Count) : 0;
        return new ExportPreview(purpose, plan.FileName, [.. plan.Characters.Select(c => c.Id)], [.. plan.Sources.Select(Notice)], plan.Omitted, gapNotes);
    }

    public ExportResult Export(IReadOnlyList<Guid> characterIds, ExportPurpose purpose = ExportPurpose.Backup)
    {
        var plan = Plan(characterIds, purpose);
        var files = new SortedDictionary<string, (string Kind, byte[] Bytes)>(StringComparer.Ordinal);
        foreach (var source in plan.Sources)
            files[$"sources/{source.Id:D}.json"] = ("source", Json(ForCharacterPackage(source))); // machine-local; a path may name the user (ADR-005)
        foreach (var revision in plan.Revisions)
            files[$"content/{revision.RevisionId:D}.json"] = ("contentRevision", Json(revision));
        // SPEC C-08: the archive mark is local library organisation; a character package (backup or share) never carries it.
        foreach (var character in plan.Characters)
            files[$"characters/{character.Id:D}.json"] = ("character", Json(character with { ArchivedAt = null }));
        // SPEC P-01, MVP DoD 5: the campaign profile travels with its characters (it holds no rules text).
        foreach (var campaign in plan.Characters.Select(c => c.CampaignId).OfType<Guid>().Distinct().Select(store.FindCampaign).OfType<Campaign>())
            files[$"campaigns/{campaign.Id:D}.json"] = ("campaign", Json(campaign));
        // M3 B3: gap notes are the player's own session feedback. They go in a backup, and never in a share.
        if (purpose == ExportPurpose.Backup)
        {
            foreach (var note in plan.Characters.SelectMany(c => store.ListGapNotes(c.Id)))
                files[$"gaps/{note.Id:D}.json"] = ("gapNote", Json(note));
        }

        var createdAt = time.GetUtcNow();
        var manifest = new PackageManifest
        {
            FormatVersion = PackageManifest.CharacterFormatVersion,
            CreatedAt = createdAt,
            AppVersion = typeof(PackageService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            Purpose = purpose,
            Characters = [.. plan.Characters.Select(c => c.Id)],
            Entries = [.. files.Select(f => new PackageEntry(f.Key, f.Value.Kind, Hash(f.Value.Bytes), f.Value.Bytes.LongLength))],
            Notices = [.. plan.Sources.Select(Notice)],
            Omitted = plan.Omitted,
        };

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, ManifestPath, Json(manifest), createdAt);
            foreach (var (path, file) in files)
                WriteEntry(zip, path, file.Bytes, createdAt);
        }
        return new ExportResult(plan.FileName, buffer.ToArray(), manifest);
    }

    private static LicenseNotice Notice(SourceRecord s) => new(s.Id, s.Title, s.Publisher, s.License, s.Redistributable, s.Attribution, s.ModificationNotice);

    /// <summary>
    /// Resolves characters, their pinned revisions and those revisions' sources. For <see cref="ExportPurpose.Share"/>,
    /// revisions from sources with <c>redistributable: false</c> are left out and listed in <see cref="ExportPlan.Omitted"/>
    /// with the characters that pin them; the characters keep their pins (ADR-007).
    /// </summary>
    private ExportPlan Plan(IReadOnlyList<Guid> characterIds, ExportPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        if (!Enum.IsDefined(purpose))
            throw new PackageException([new("export.purpose-unknown", $"Export purpose '{purpose}' is not supported.")]);
        var errors = new List<Diagnostic>();
        var characters = new List<Character>();
        foreach (var id in characterIds.Distinct())
        {
            if (store.FindCharacter(id) is { } character)
                characters.Add(character);
            else
                errors.Add(new("export.character-missing", $"Character {id} does not exist."));
        }

        var revisions = new Dictionary<Guid, ContentRevision>();
        foreach (var pin in characters.SelectMany(c => c.AllReferences()))
        {
            if (store.FindRevision(pin) is { } revision)
                revisions[revision.RevisionId] = revision;
            else
                errors.Add(new("export.revision-missing", $"Pinned revision {pin.RevisionId} is missing; repair the character before exporting.", pin));
        }
        // Content granted by included content (class features, a background's feat) is needed to calculate the same
        // sheet on the receiving machine. Follow grants transitively; a granted revision missing here is already shown
        // as missing on this machine's sheet, so it is skipped rather than failing the export.
        var pending = new Queue<ContentRevision>(revisions.Values);
        while (pending.Count > 0)
        {
            foreach (var grant in pending.Dequeue().Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content && g.Content is not null))
            {
                if (!revisions.ContainsKey(grant.Content!.RevisionId) && store.FindRevision(grant.Content) is { } granted)
                {
                    revisions[granted.RevisionId] = granted;
                    pending.Enqueue(granted);
                }
            }
        }

        var sources = new Dictionary<Guid, SourceRecord>();
        foreach (var revision in revisions.Values)
        {
            if (store.FindSource(revision.Provenance.SourceId) is { } source)
                sources[source.Id] = source;
            else
                errors.Add(new("export.source-missing", $"Source {revision.Provenance.SourceId} for '{revision.Name}' is missing.", revision.Reference));
        }

        if (errors.Count > 0)
            throw new PackageException(errors);

        var omitted = new List<OmittedSource>();
        if (purpose == ExportPurpose.Share)
        {
            // M6 slice 1: an import-derived source is never shared, whatever its redistributable flag says.
            foreach (var source in sources.Values.Where(s => !s.MayBeShared).OrderBy(s => s.Id))
            {
                var left = revisions.Values.Where(r => r.Provenance.SourceId == source.Id).OrderBy(r => r.RevisionId).ToList();
                omitted.Add(new(source.Id, source.Title, source.Publisher, source.License,
                [
                    .. left.Select(r => new OmittedRevision(
                        r.Reference, r.Name, [.. characters.Where(c => c.AllReferences().Contains(r.Reference)).Select(c => c.Id)])),
                ]));
                foreach (var revision in left)
                    revisions.Remove(revision.RevisionId);
                sources.Remove(source.Id);
            }
        }

        var name = characters.Count == 1 ? SafeFileName(characters[0].Name) : "characters";
        // ADR-007: the file name says what a backup is, so it is not handed on by mistake.
        var fileName = purpose == ExportPurpose.Backup ? $"{name}-personal-backup.tomestack.zip" : $"{name}.tomestack.zip";
        return new ExportPlan(characters, [.. revisions.Values], [.. sources.Values.OrderBy(s => s.Id)], omitted, fileName);
    }

    public PackagePreview Preview(byte[] package) => Read(package).Preview;

    /// <param name="sourceChoices">
    /// Required for every package source whose metadata differs from the local record (preview items with
    /// <see cref="PackageItem.Changes"/>). Local license metadata is never overwritten without an explicit choice.
    /// </param>
    public ImportResult Apply(byte[] package, IReadOnlyDictionary<Guid, SourceChoice>? sourceChoices = null)
    {
        var (preview, parsed) = Read(package);
        if (!preview.CanApply || parsed is null)
            throw new PackageException(preview.Errors);
        var keepLocal = SourcesKeptLocal(preview, sourceChoices);

        // SPEC C-07/Q-01: never overwrite a local character without a restorable copy.
        var toReplace = parsed.Characters.Where(c => store.FindCharacter(c.Id) is not null).Select(c => c.Id).ToList();
        var backupFile = toReplace.Count > 0 ? WriteBackup(toReplace) : null;
        // M6 slice 1: a source pack can replace source metadata and make its revisions the newest, so the whole database is
        // copied first whenever it changes anything (package-format.md rule 10).
        if (parsed.Manifest.Scope == PackageScope.Source && preview.Items.Any(i => (i.Action is PackageItemAction.Add or PackageItemAction.Replace) && !(i.Kind == "source" && keepLocal.Contains(i.Id))))
            backupFile = WriteSafetyCopy("pre-import");

        var (added, replaced, unchanged) = Commit(parsed, keepLocal, []);
        return new ImportResult(added, replaced, unchanged, [.. parsed.Characters.Select(c => c.Id)], backupFile);
    }

    /// <summary>The sources to leave as they are; every source that differs from the local record needs a choice.</summary>
    private static HashSet<Guid> SourcesKeptLocal(PackagePreview preview, IReadOnlyDictionary<Guid, SourceChoice>? sourceChoices)
    {
        var differing = preview.Items.Where(i => i.Kind == "source" && i.Action == PackageItemAction.Replace).ToList();
        var missingChoices = differing.Where(i => sourceChoices is null || !sourceChoices.ContainsKey(i.Id)).ToList();
        if (missingChoices.Count > 0)
        {
            throw new PackageException(
            [
                .. missingChoices.Select(i => new Diagnostic(
                    "package.source-choice-required",
                    $"Source '{i.Name}' in the package differs from your local record ({string.Join(", ", i.Changes!.Select(c => c.Field))}). Choose whether to keep your local version or use the imported one.")),
            ]);
        }
        return differing.Where(i => sourceChoices![i.Id] == SourceChoice.KeepLocal).Select(i => i.Id).ToHashSet();
    }

    /// <summary>
    /// Writes a checked package in one transaction. A package import never changes a source's PDF. A library restore
    /// (<see cref="ParsedPackage.Attachments"/> non-empty or library scope) also adds attachment records, and gives a
    /// source the backup's PDF when it has none here; a different local PDF is kept (<paramref name="warnings"/>).
    /// </summary>
    private (int Added, int Replaced, int Unchanged) Commit(ParsedPackage parsed, HashSet<Guid> keepLocal, List<Diagnostic> warnings)
    {
        var library = parsed.Manifest.Scope == PackageScope.Library;
        var linked = library ? AttachmentsToRestore(parsed, keepLocal) : [];
        int added = 0, replaced = 0, unchanged = 0;
        store.InTransaction(() =>
        {
            // Only records a restored source will point to: any other would be an orphan nothing ever removes.
            foreach (var attachment in parsed.Attachments.Where(a => linked.Contains(a.AttachmentId)))
            {
                if (store.FindAttachment(attachment.AttachmentId) is null) { store.AddAttachment(attachment); added++; }
                else unchanged++;
            }
            foreach (var source in parsed.Sources)
            {
                var local = store.FindSource(source.Id);
                if (local is not null && _bundledSources.Contains(source.Id))
                {
                    // M6 slice 1: bundled source records are this build's own. A full restore still gives an SRD source the
                    // backup's PDF when it has none here (its page links), as for any source (review fix).
                    if (library && local.AttachmentId is null && source.AttachmentId is { } srdPdf && linked.Contains(srdPdf) && store.FindAttachment(srdPdf) is not null)
                        store.UpsertSource(local with { AttachmentId = srdPdf });
                    continue;
                }
                if (keepLocal.Contains(source.Id))
                {
                    // The local metadata stays, but the import-derived flag still only goes up (M6 slice 1): the file's flag,
                    // or, in a backup, a PDF the source had there.
                    if (local is not null && local.ImportDerived != true && (source.ImportDerived == true || (library && source.AttachmentId is not null)))
                        store.UpsertSource(local with { ImportDerived = true });
                    continue;
                }
                // A PDF reference is machine-local; a package import never adds, changes or removes one.
                var attachmentId = local?.AttachmentId;
                if (library && source.AttachmentId is { } fromBackup && fromBackup != attachmentId)
                {
                    if (attachmentId is null && linked.Contains(fromBackup) && store.FindAttachment(fromBackup) is not null)
                        attachmentId = fromBackup;
                    else if (attachmentId is not null)
                        warnings.Add(new("restore.pdf-kept", $"'{source.Title}' already has a different PDF here; it is kept."));
                }
                store.UpsertSource(Merged(source, local, parsed.Manifest.Scope, attachmentId) with { PdfRef = local?.PdfRef });
            }
            var unconfirm = new HashSet<Guid>();
            foreach (var revision in parsed.Revisions)
            {
                if (store.AddRevision(revision))
                {
                    added++;
                    if (!library)
                        unconfirm.Add(revision.Provenance.SourceId);
                }
                else unchanged++;
            }
            // M6 slice 1: a source of unknown origin that received content from a package must be confirmed again before
            // it is shared as the author's own work (package.source-unconfirmed).
            foreach (var sourceId in unconfirm)
            {
                if (store.FindSource(sourceId) is { Origin: null, ShareConfirmedAt: not null } unknown && !_bundledSources.Contains(sourceId))
                    store.UpsertSource(unknown with { ShareConfirmedAt = null });
            }
            foreach (var campaign in parsed.Campaigns)
            {
                var local = store.FindCampaign(campaign.Id);
                if (local is null) added++;
                else if (Json(local).AsSpan().SequenceEqual(Json(campaign))) { unchanged++; continue; }
                else replaced++;
                store.SaveCampaign(campaign);
            }
            foreach (var character in parsed.Characters)
            {
                var local = store.FindCharacter(character.Id);
                if (local is null) added++;
                else replaced++;
                // SPEC C-08: only a full library restore brings the archive mark back. A character package keeps the local
                // mark (or none for a new character), so an import never archives or unarchives anything by itself.
                store.SaveCharacter(library ? character : character with { ArchivedAt = local?.ArchivedAt });
            }
            foreach (var note in parsed.GapNotes)
            {
                var local = store.FindGapNote(note.Id);
                if (local is null) added++;
                else if (Json(local).AsSpan().SequenceEqual(Json(note))) { unchanged++; continue; }
                else replaced++;
                store.SaveGapNote(note);
            }
        });
        return (added, replaced, unchanged);
    }

    /// <summary>
    /// Exports the local copies of <paramref name="characterIds"/> to <c>backups/</c> as an ordinary package, so
    /// restoring is a normal import. Returns the path relative to the data directory.
    /// </summary>
    private string WriteBackup(IReadOnlyList<Guid> characterIds)
    {
        ExportResult backup;
        try
        {
            backup = Export(characterIds);
        }
        catch (PackageException ex)
        {
            throw new PackageException(
            [
                new("package.backup-failed", "The local copy of a character this package would replace cannot be backed up, so nothing was imported. Repair or export that character first."),
                .. ex.Errors,
            ]);
        }

        Directory.CreateDirectory(backupDirectory);
        var stamp = time.GetUtcNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        for (var attempt = 0; ; attempt++)
        {
            var name = attempt == 0 ? $"pre-import-{stamp}.tomestack.zip" : $"pre-import-{stamp}-{attempt}.tomestack.zip";
            try
            {
                using var file = new FileStream(Path.Combine(backupDirectory, name), FileMode.CreateNew, FileAccess.Write);
                file.Write(backup.Content);
                file.Flush(flushToDisk: true);
                return $"{BackupFolderName}/{name}";
            }
            catch (IOException) when (attempt < 100 && File.Exists(Path.Combine(backupDirectory, name)))
            {
                // Same second as an earlier backup; try the next suffix.
            }
        }
    }

    /// <param name="Attachments">Library backups only: attachment records (<c>attachments/</c>).</param>
    /// <param name="Pdfs">Library backups only: the <c>files/</c> entries by content hash. Valid while the archive is open; never read into memory.</param>
    private sealed record ParsedPackage(
        PackageManifest Manifest,
        IReadOnlyList<SourceRecord> Sources,
        IReadOnlyList<ContentRevision> Revisions,
        IReadOnlyList<Character> Characters,
        IReadOnlyList<Campaign> Campaigns,
        IReadOnlyList<GapNote> GapNotes,
        IReadOnlyList<Attachment> Attachments,
        IReadOnlyDictionary<string, ZipArchiveEntry> Pdfs);

    private (PackagePreview Preview, ParsedPackage? Parsed) Read(byte[] package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var errors = new List<Diagnostic>();
        var parsed = Parse(package, errors);
        if (parsed?.Manifest.Scope == PackageScope.Library)
        {
            errors.Add(new("package.library-backup", "This file is a full backup of a TomeStack library. Use \"Restore full backup\" to restore it; it cannot be imported as a package."));
            parsed = null;
        }
        return BuildPreview(parsed, errors, []);
    }

    /// <param name="warnings">Warnings found before the preview (a library restore's PDF checks).</param>
    private (PackagePreview Preview, ParsedPackage? Parsed) BuildPreview(ParsedPackage? parsed, List<Diagnostic> errors, List<Diagnostic> warnings)
    {
        if (parsed is null || errors.Count > 0)
            return (new PackagePreview(false, parsed?.Manifest, [], errors, warnings), null);
        var items = new List<PackageItem>();
        var packageRevisions = parsed.Revisions.ToDictionary(r => r.Reference);
        var packageSources = parsed.Sources.ToDictionary(s => s.Id);
        var catalog = new PackageCatalog(packageRevisions, packageSources, store);

        foreach (var source in parsed.Sources)
        {
            var local = store.FindSource(source.Id);
            // M6 slice 1: a bundled SRD source record is this build's own; no package replaces it (its CC-BY notice
            // travels in every later share).
            if (local is not null && _bundledSources.Contains(source.Id))
            {
                if (SourceChanges(local, source).Count > 0)
                    warnings.Add(new("package.bundled-source-kept", $"The package carries a different record for the bundled source '{local.Title}'. TomeStack keeps its own."));
                items.Add(new("source", source.Id, local.Title, PackageItemAction.Unchanged, "bundled with TomeStack"));
                continue;
            }
            if (source.ImportDerived == true && local?.ImportDerived != true)
                warnings.Add(new("package.source-import-derived", $"'{source.Title}' holds material imported from a PDF on the sender's machine. It stays marked that way here and is never shared."));
            var changes = local is null ? [] : SourceChanges(local, source);
            var action = local is null ? PackageItemAction.Add
                : changes.Count == 0 ? PackageItemAction.Unchanged
                : PackageItemAction.Replace;
            if (action == PackageItemAction.Replace)
                warnings.Add(new("package.source-differs", $"Source '{source.Title}' differs from your local record ({string.Join(", ", changes.Select(c => c.Field))}). Choose which version to keep before importing."));
            items.Add(new(
                "source", source.Id, source.Title, action,
                $"{source.License}; redistributable: {(source.Redistributable ? "yes" : "no")}",
                action == PackageItemAction.Replace ? changes : null));
        }

        foreach (var revision in parsed.Revisions)
        {
            if (!packageSources.ContainsKey(revision.Provenance.SourceId) && store.FindSource(revision.Provenance.SourceId) is null)
                errors.Add(new("package.source-missing", $"'{revision.Name}' references source {revision.Provenance.SourceId}, which is neither in the package nor installed.", revision.Reference));

            var localHash = store.RevisionHash(revision.RevisionId);
            var action = localHash is null ? PackageItemAction.Add
                : localHash == SqliteStore.Sha256(SqliteStore.Serialize(revision)) ? PackageItemAction.Unchanged
                : PackageItemAction.Conflict;
            if (action == PackageItemAction.Conflict)
                errors.Add(new("package.revision-conflict", $"Revision {revision.RevisionId} of '{revision.Name}' differs from the installed revision with the same ID. Published revisions are immutable.", revision.Reference));
            // M6 slice 1 (review fixes), every scope but a full restore (the user's own file):
            if (action == PackageItemAction.Add && parsed.Manifest.Scope != PackageScope.Library)
            {
                // New content under a bundled SRD source would travel in every share with the SRD's CC-BY notice.
                if (_bundledSources.Contains(revision.Provenance.SourceId))
                    errors.Add(new("package.bundled-source-content", $"'{revision.Name}' claims to belong to a bundled SRD source, but is not part of it. It was not written by this TomeStack's SRD packs.", revision.Reference));
                // Only you add content to a source you made here. A source of unknown origin (stored before v8) may be yours
                // from another machine, so it is not refused, but it must be marked as shareable again (Commit).
                else if (store.FindSource(revision.Provenance.SourceId) is { Origin: SourceOrigin.Local } own)
                    errors.Add(new("package.own-source", $"'{revision.Name}' would be added to '{own.Title}', a source you made on this machine. Only you add content to your own sources.", revision.Reference));
                else if (store.FindSource(revision.Provenance.SourceId) is { Origin: null, ShareConfirmedAt: not null } unknown)
                    warnings.Add(new("package.source-unconfirmed", $"'{revision.Name}' is added to '{unknown.Title}'. Its share confirmation is withdrawn: mark it as shareable again once you have checked its content.", revision.Reference));
                // A content's revisions belong to one source (M6 review): a new revision of an SRD or homebrew content here,
                // under another source, would become its newest and leave that content in two sources for good.
                if (store.RevisionsOf(revision.ContentId).FirstOrDefault(r => r.Provenance.SourceId != revision.Provenance.SourceId) is { } other)
                    errors.Add(new("pack.content-conflict", $"'{revision.Name}' would add a revision to '{other.Name}', which belongs to another source here.", revision.Reference));
            }
            if (revision.Status != RevisionStatus.Published)
                warnings.Add(new("package.revision-draft", $"'{revision.Name}' is a draft and stays inactive after import.", revision.Reference));
            var families = string.Join(", ", revision.RulesFamilies);
            items.Add(new("contentRevision", revision.RevisionId, revision.Name, action, $"{revision.Kind} · {families} · {revision.Status}"));

            Diagnostic Named(Diagnostic d) => d with { Message = $"'{revision.Name}': {d.Message}" };
            if (ContentValidator.EmptyEntries(revision) is { Count: > 0 } empty)
            {
                errors.AddRange(empty.Select(Named));
                continue;
            }
            // ADR-004: a published revision in a package becomes active on import, so it gets the same validation as
            // content.publish, shown in the preview. Installed copies were checked when they arrived; drafts stay inactive.
            if (action != PackageItemAction.Add || revision.Status != RevisionStatus.Published)
                continue;
            // Builds that write content schema v3 validate before publishing, so a v3 revision that fails was not
            // published by TomeStack and is refused. Older revisions were published before validation existed (v0.1):
            // their problems are shown as warnings, and the calculator isolates what it cannot apply (SPEC C-03).
            var blocking = revision.SchemaVersion >= ValidatedOnPublishSchemaVersion;
            foreach (var problem in ContentValidator.Validate(revision, catalog).Errors)
            {
                if (problem.Code == "validate.source-missing")
                    continue; // reported above as package.source-missing
                // A share package may leave out referenced content (ADR-007), and a backup skips a granted revision that
                // is already missing locally; the sheet shows either as missing content, so it does not block import.
                // Two v9 checks warn here and block only content.publish (review fix, M5 slice 1a): requires-v9 on an
                // older revision whose inert "scale" or table key an earlier build published (it stays reference-only),
                // and a scale id clash that the order of publishing allowed (the calculation reports scale.duplicate
                // and the class's column wins). Refusing them would refuse a backup that publishing produced.
                if (blocking && problem.Code is not ("validate.reference-missing" or "validate.requires-v9" or "validate.scale-duplicate"))
                    errors.Add(Named(problem));
                else
                    warnings.Add(Named(problem));
            }
        }

        foreach (var campaign in parsed.Campaigns)
        {
            foreach (var problem in campaign.Validate())
                errors.Add(problem with { Message = $"Campaign '{campaign.Name}': {problem.Message}" });
            var local = store.FindCampaign(campaign.Id);
            var action = local is null ? PackageItemAction.Add
                : Json(local).AsSpan().SequenceEqual(Json(campaign)) ? PackageItemAction.Unchanged
                : PackageItemAction.Replace;
            if (action == PackageItemAction.Replace)
                warnings.Add(new("package.campaign-replace", $"Campaign '{campaign.Name}' differs from your local copy (allowed sources, rules family or house rules); the imported copy replaces it."));
            items.Add(new("campaign", campaign.Id, campaign.Name, action, $"{campaign.RulesFamily} · {campaign.AllowedSources.Count} allowed source(s)"));
        }

        // ADR-007: a share package may leave out non-redistributable content, but only content its manifest names.
        var omitted = new Dictionary<ContentReference, OmittedSource>();
        if (parsed.Manifest.FormatVersion >= 3 && parsed.Manifest.Purpose == ExportPurpose.Share) // v1/v2 had no purpose
        {
            foreach (var source in parsed.Manifest.Omitted)
            {
                foreach (var revision in source.Revisions)
                    omitted.TryAdd(revision.Reference, source); // a repeated entry in the untrusted manifest is harmless
            }
        }
        foreach (var character in parsed.Characters)
        {
            var problems = character.Validate();
            foreach (var problem in problems)
                errors.Add(problem with { Message = $"'{character.Name}': {problem.Message}" });
            var malformed = problems.Any(p => p.Code == "character.empty-entry"); // its references cannot be read
            foreach (var pin in malformed ? Enumerable.Empty<ContentReference>() : character.AllReferences())
            {
                if (packageRevisions.ContainsKey(pin) || store.FindRevision(pin) is not null)
                    continue;
                if (omitted.TryGetValue(pin, out var source))
                    warnings.Add(new("package.content-omitted", $"'{character.Name}' uses content from '{source.Title}' ({source.Publisher}), which the sender left out because it may not be shared. It shows as missing until you install that source yourself.", pin));
                else
                    errors.Add(new("package.pin-missing", $"'{character.Name}' pins revision {pin.RevisionId}, which is neither in the package nor installed.", pin));
            }
            var exists = store.FindCharacter(character.Id) is not null;
            if (exists && parsed.Manifest.Scope == PackageScope.Library)
                warnings.Add(new("restore.character-replace", $"'{character.Name}' already exists and will be replaced by the backup's copy. Your whole database is copied to the {BackupFolderName} folder first (pre-restore-….db); to go back, close TomeStack and put that file in place of {TomeStackApp.DatabaseFileName}."));
            else if (exists)
                warnings.Add(new("package.character-replace", $"'{character.Name}' already exists and will be replaced by the imported copy. The current copy is saved to the {BackupFolderName} folder in your data folder first, and you can restore it by importing that file."));
            items.Add(new("character", character.Id, character.Name, exists ? PackageItemAction.Replace : PackageItemAction.Add, character.RulesFamily));
        }

        // M3 B3: gap notes travel only in a v5+ backup, and only with their character.
        if (parsed.GapNotes.Count > 0 && (parsed.Manifest.FormatVersion < 5 || parsed.Manifest.Purpose != ExportPurpose.Backup))
            errors.Add(new("package.gap-notes-not-allowed", "This package carries gap notes, which only a personal backup may contain."));
        var packageCharacters = parsed.Characters.ToDictionary(c => c.Id);
        foreach (var note in parsed.GapNotes)
        {
            // The messages never quote the note's text: it may describe private homebrew.
            foreach (var problem in note.Validate())
                errors.Add(problem with { Message = $"Gap note {note.Id}: {problem.Message}" });
            if (!packageCharacters.TryGetValue(note.CharacterId, out var owner))
            {
                errors.Add(new("package.gap-note-orphan", $"Gap note {note.Id} belongs to a character that is not in the package."));
                continue;
            }
            var local = store.FindGapNote(note.Id);
            var action = local is null ? PackageItemAction.Add
                : Json(local).AsSpan().SequenceEqual(Json(note)) ? PackageItemAction.Unchanged
                : PackageItemAction.Replace;
            items.Add(new("gapNote", note.Id, note.Target?.Label ?? "(gap note)", action, $"{owner.Name} · {note.Status}"));
        }

        // Inside the package too, a content's revisions belong to one source (every scope but a full restore; M6 review).
        if (parsed.Manifest.Scope != PackageScope.Library)
        {
            foreach (var split in parsed.Revisions.GroupBy(r => r.ContentId).Where(g => g.Select(r => r.Provenance.SourceId).Distinct().Count() > 1))
                errors.Add(new("pack.content-conflict", $"'{split.Last().Name}' has revisions in more than one of the package's sources.", split.Last().Reference));
        }
        if (parsed.Manifest.Scope == PackageScope.Source)
            CheckSourcePack(parsed, errors, warnings);
        if (parsed.Manifest.Scope is PackageScope.Library or PackageScope.Source)
            warnings.AddRange(LibraryWarnings(parsed));
        foreach (var attachment in parsed.Attachments)
        {
            var action = store.FindAttachment(attachment.AttachmentId) is null ? PackageItemAction.Add : PackageItemAction.Unchanged;
            var detail = attachment.Mode == AttachmentMode.Managed
                ? $"PDF copy · {attachment.ByteLength / (1024.0 * 1024.0):0.#} MB"
                : "linked PDF (the file itself is not in the backup)";
            items.Add(new("attachment", attachment.AttachmentId, attachment.OriginalFileName, action, detail));
        }

        return (new PackagePreview(errors.Count == 0, parsed.Manifest, items, errors, warnings), errors.Count == 0 ? parsed : null);
    }

    /// <summary>Entry count and total unpacked JSON for one kind of archive.</summary>
    private sealed record ReadLimits(int MaxEntries, long MaxJsonBytes);

    private static readonly ReadLimits PackageLimits = new(MaxEntries, MaxTotalBytes);

    private static ParsedPackage? Parse(byte[] package, List<Diagnostic> errors)
    {
        if (package.LongLength > MaxPackageBytes)
        {
            errors.Add(new("package.too-large", $"Package is {package.LongLength} bytes; the limit is {MaxPackageBytes}."));
            return null;
        }
        try
        {
            using var zip = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
            return ParseArchive(zip, PackageLimits, errors);
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new("package.invalid-archive", $"The file is not a readable package: {ex.Message}"));
            return null;
        }
    }

    /// <summary>
    /// Checks and reads every JSON entry of an open archive. PDF entries (<c>files/</c>, library backups only) are listed
    /// but not read: <see cref="VerifyPdfs"/> streams them. The returned entries are valid while <paramref name="zip"/> is open.
    /// </summary>
    private static ParsedPackage? ParseArchive(ZipArchive zip, ReadLimits limits, List<Diagnostic> errors)
    {
        Dictionary<string, byte[]> files;
        var pdfs = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        try
        {
            if (zip.Entries.Count > limits.MaxEntries)
            {
                errors.Add(new("package.too-many-entries", $"Package has {zip.Entries.Count} entries; the limit is {limits.MaxEntries}."));
                return null;
            }
            files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            // The format and version first (M6 slice 1), read bounded from the manifest alone: a newer package may add an
            // entry path as well as a scope or field, and must be refused as "update TomeStack", not as a bad path.
            if (zip.GetEntry(ManifestPath) is { } manifestEntry && !ManifestVersionReadable(ReadBounded(manifestEntry, limits.MaxJsonBytes), errors))
                return null;
            // Names are checked for every other entry before it is decompressed.
            foreach (var entry in zip.Entries.Where(e => e.FullName != ManifestPath && !EntryPathPattern().IsMatch(e.FullName) && !LibraryEntryPathPattern().IsMatch(e.FullName)))
                errors.Add(new("package.entry-not-allowed", $"Entry '{entry.FullName}' is not an allowed package path."));
            if (errors.Count > 0)
                return null;
            long remaining = limits.MaxJsonBytes;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.StartsWith(PdfFolder, StringComparison.Ordinal))
                {
                    if (!pdfs.TryAdd(entry.FullName[PdfFolder.Length..^4], entry))
                        errors.Add(new("package.entry-duplicate", $"Entry '{entry.FullName}' appears more than once."));
                    continue;
                }
                var bytes = ReadBounded(entry, remaining);
                remaining -= bytes.LongLength;
                if (!files.TryAdd(entry.FullName, bytes))
                    errors.Add(new("package.entry-duplicate", $"Entry '{entry.FullName}' appears more than once."));
            }
        }
        catch (TotalTooLargeException ex)
        {
            errors.Add(new("package.content-too-large", ex.Message));
            return null;
        }
        catch (InvalidDataException ex)
        {
            errors.Add(new("package.invalid-archive", $"The file is not a readable package: {ex.Message}"));
            return null;
        }
        catch (EntryTooLargeException ex)
        {
            errors.Add(new("package.entry-too-large", ex.Message));
            return null;
        }
        if (errors.Count > 0)
            return null;

        if (!files.TryGetValue(ManifestPath, out var manifestBytes))
        {
            errors.Add(new("package.manifest-missing", "The package has no manifest.json."));
            return null;
        }
        var manifest = Deserialize<PackageManifest>(ManifestPath, manifestBytes, errors);
        if (manifest is null)
            return null;
        // Untrusted JSON can carry null where the model has none; reject it here rather than fail later.
        if (manifest.Entries is null || manifest.Entries.Any(e => e?.Path is null || e.Sha256 is null)
            || manifest.Notices is null || manifest.Notices.Any(n => n is null)
            || manifest.Omitted is null || manifest.Omitted.Any(s => s?.Revisions is null || s.Revisions.Any(r => r?.Reference is null)))
        {
            errors.Add(new("package.invalid-json", "The package manifest has missing or empty entries."));
            return null;
        }
        if (manifest.Format != PackageManifest.FormatName || manifest.FormatVersion is < 1 or > PackageManifest.CurrentFormatVersion)
        {
            errors.Add(new("package.unsupported-format", $"Package format '{manifest.Format}' v{manifest.FormatVersion} is not supported by this version (v{PackageManifest.CurrentFormatVersion})."));
            return null;
        }

        // M2.1: attachment records and PDFs belong only in a v6 library backup.
        var library = manifest.FormatVersion >= 6 && manifest.Scope == PackageScope.Library;
        if (!library && (pdfs.Count > 0 || files.Keys.Any(p => p.StartsWith(AttachmentFolder, StringComparison.Ordinal))))
        {
            errors.Add(new("package.entry-not-allowed", "Only a full library backup may contain PDFs and attachment records."));
            return null;
        }
        if (manifest.Scope == PackageScope.Library && (manifest.FormatVersion < 6 || manifest.Purpose != ExportPurpose.Backup))
        {
            errors.Add(new("package.invalid-json", "A full library backup must be a format v6 backup."));
            return null;
        }
        // M6 slice 1: a source pack is always a share and carries only sources and published content.
        var sourcePack = manifest.Scope == PackageScope.Source;
        if (sourcePack && (manifest.FormatVersion < PackageManifest.SourceFormatVersion || manifest.Purpose != ExportPurpose.Share))
        {
            errors.Add(new("package.invalid-json", $"A source pack must be a format v{PackageManifest.SourceFormatVersion} share."));
            return null;
        }
        if (sourcePack && files.Keys.FirstOrDefault(p => p != ManifestPath && !p.StartsWith("sources/", StringComparison.Ordinal) && !p.StartsWith("content/", StringComparison.Ordinal)) is { } stray)
        {
            errors.Add(new("package.entry-not-allowed", $"Entry '{stray}' is not allowed in a source pack, which carries only sources and their published content."));
            return null;
        }

        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries.Where(e => !listed.Add(e.Path)))
            errors.Add(new("package.entry-duplicate", $"Manifest lists '{entry.Path}' more than once."));
        foreach (var path in files.Keys.Where(p => p != ManifestPath).Concat(pdfs.Values.Select(e => e.FullName)).Where(p => !listed.Contains(p)))
            errors.Add(new("package.entry-unlisted", $"Entry '{path}' is not listed in the manifest."));
        foreach (var entry in manifest.Entries)
        {
            if (entry.Path.StartsWith(PdfFolder, StringComparison.Ordinal))
            {
                // A PDF entry is named by its hash; VerifyPdfs streams it and checks the hash and size.
                if (!pdfs.ContainsKey(entry.Path[PdfFolder.Length..^4]))
                    errors.Add(new("package.entry-missing", $"Manifest lists '{entry.Path}', which is not in the package."));
                else if (entry.Sha256 != entry.Path[PdfFolder.Length..^4])
                    errors.Add(new("package.hash-mismatch", $"Entry '{entry.Path}' is listed with a different hash than its name."));
            }
            else if (!files.TryGetValue(entry.Path, out var bytes))
                errors.Add(new("package.entry-missing", $"Manifest lists '{entry.Path}', which is not in the package."));
            else if (Hash(bytes) != entry.Sha256)
                errors.Add(new("package.hash-mismatch", $"Entry '{entry.Path}' does not match its manifest hash; the package may be damaged or altered."));
        }
        if (errors.Count > 0)
            return null;

        var sources = new List<SourceRecord>();
        var revisions = new List<ContentRevision>();
        var characters = new List<Character>();
        var campaigns = new List<Campaign>();
        var gapNotes = new List<GapNote>();
        var attachments = new List<Attachment>();
        foreach (var (path, bytes) in files.Where(f => f.Key != ManifestPath).OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var id = Guid.Parse(Path.GetFileNameWithoutExtension(path));
            switch (path[..path.IndexOf('/', StringComparison.Ordinal)])
            {
                case "sources" when Deserialize<SourceRecord>(path, bytes, errors) is { } source:
                    ExpectId(path, id, source.Id, errors);
                    sources.Add(source);
                    break;
                case "content" when Deserialize<ContentRevision>(path, bytes, errors) is { } revision:
                    ExpectId(path, id, revision.RevisionId, errors);
                    ExpectSchema(path, "content", revision.SchemaVersion, ContentRevision.CurrentSchemaVersion, errors);
                    revisions.Add(revision);
                    break;
                case "characters" when Deserialize<Character>(path, bytes, errors) is { } character:
                    ExpectId(path, id, character.Id, errors);
                    ExpectSchema(path, "character", character.SchemaVersion, Character.CurrentSchemaVersion, errors);
                    characters.Add(character);
                    break;
                case "campaigns" when Deserialize<Campaign>(path, bytes, errors) is { } campaign:
                    ExpectId(path, id, campaign.Id, errors);
                    ExpectSchema(path, "campaign", campaign.SchemaVersion, Campaign.CurrentSchemaVersion, errors);
                    if (campaign.AllowedSources is null)
                        errors.Add(new("package.invalid-json", $"Entry '{path}' has no allowed-sources list."));
                    else
                        campaigns.Add(campaign);
                    break;
                case "gaps" when Deserialize<GapNote>(path, bytes, errors) is { } note:
                    ExpectId(path, id, note.Id, errors);
                    ExpectSchema(path, "gap note", note.SchemaVersion, GapNote.CurrentSchemaVersion, errors);
                    gapNotes.Add(note);
                    break;
                case "attachments" when Deserialize<Attachment>(path, bytes, errors) is { } attachment:
                    ExpectId(path, id, attachment.AttachmentId, errors);
                    ExpectAttachment(path, attachment, pdfs, errors);
                    attachments.Add(attachment);
                    break;
            }
        }
        if (errors.Count > 0)
            return null;
        // Newest means last stored (SPEC I-06), so a library backup and a source pack add revisions in the order the
        // sender stored them, not in entry (id) order.
        if (library || sourcePack)
            revisions = OrderAsStored(revisions, manifest.RevisionOrder, errors);
        return errors.Count > 0 ? null : new ParsedPackage(manifest, sources, revisions, characters, campaigns, gapNotes, attachments, pdfs);
    }

    /// <summary>Field-by-field differences in serialized form, ignoring the machine-local <c>pdfRef</c> and <c>attachmentId</c>.</summary>
    private static List<FieldChange> SourceChanges(SourceRecord local, SourceRecord imported)
    {
        var localNode = JsonSerializer.SerializeToNode(local, RulesJson.Compact)!.AsObject();
        var importedNode = JsonSerializer.SerializeToNode(imported, RulesJson.Compact)!.AsObject();
        return
        [
            .. localNode.Select(p => p.Key).Union(importedNode.Select(p => p.Key)).Order(StringComparer.Ordinal)
                // Set only by the receiving machine (M6 slice 1), so never a choice: the flag only goes up, and origin and
                // the share confirmation are this machine's own record.
                .Where(field => field is not ("pdfRef" or "attachmentId" or "importDerived" or "origin" or "shareConfirmedAt"))
                .Select(field => new FieldChange(field, localNode[field]?.ToJsonString(), importedNode[field]?.ToJsonString()))
                .Where(change => change.Local != change.Imported),
        ];
    }

    /// <summary>Data from a newer TomeStack is refused rather than silently misread.</summary>
    private static void ExpectSchema(string path, string kind, int version, int supported, List<Diagnostic> errors)
    {
        if (version < 1 || version > supported)
            errors.Add(new("package.schema-unsupported", $"Entry '{path}' uses {kind} schema v{version}; this version of TomeStack supports v1 to v{supported}. Update TomeStack to import this package."));
    }

    private static void ExpectId(string path, Guid expected, Guid actual, List<Diagnostic> errors)
    {
        if (expected != actual)
            errors.Add(new("package.id-mismatch", $"Entry '{path}' contains ID {actual}, which does not match its file name."));
    }

    private static T? Deserialize<T>(string path, byte[] bytes, List<Diagnostic> errors) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, RulesJson.Options)
                ?? throw new JsonException("Document is null.");
        }
        catch (JsonException ex)
        {
            errors.Add(new("package.invalid-json", $"Entry '{path}' is not valid: {ex.Message}"));
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or KeyNotFoundException or FormatException or ArgumentException)
        {
            // Package bytes are untrusted: a reader bug must surface as a rejected entry, not an internal error.
            errors.Add(new("package.invalid-json", $"Entry '{path}' is not valid."));
            return null;
        }
    }

    /// <summary>
    /// Reads at most <see cref="MaxEntryBytes"/>, and at most <paramref name="remaining"/> of the package-wide
    /// <see cref="MaxTotalBytes"/>. Does not trust the declared entry length.
    /// </summary>
    private static byte[] ReadBounded(ZipArchiveEntry entry, long remaining)
    {
        if (entry.Length > MaxEntryBytes)
            throw new EntryTooLargeException(entry.FullName);
        if (entry.Length > remaining)
            throw new TotalTooLargeException();
        using var stream = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > MaxEntryBytes)
                throw new EntryTooLargeException(entry.FullName);
            if (output.Length > remaining)
                throw new TotalTooLargeException();
        }
        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string path, byte[] bytes, DateTimeOffset timestamp)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = timestamp;
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static byte[] Json<T>(T value) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, RulesJson.Options) + "\n");

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string SafeFileName(string name)
    {
        var cleaned = new string([.. name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')]).Trim('-');
        return cleaned.Length == 0 ? "character" : cleaned[..Math.Min(cleaned.Length, 60)];
    }

    /// <summary>The package's revisions and sources first, then this machine's: what content in the package can refer to.</summary>
    private sealed class PackageCatalog(
        IReadOnlyDictionary<ContentReference, ContentRevision> revisions, IReadOnlyDictionary<Guid, SourceRecord> sources, IContentCatalog local)
        : IContentCatalog
    {
        public ContentRevision? FindRevision(ContentReference reference) => revisions.GetValueOrDefault(reference) ?? local.FindRevision(reference);

        public SourceRecord? FindSource(Guid sourceId) => sources.GetValueOrDefault(sourceId) ?? local.FindSource(sourceId);

        public IEnumerable<ContentRevision> ChoiceExtensions(Guid contentId, string choiceId) =>
            revisions.Values
                .Where(r => r.Status == RevisionStatus.Published && r.ExtendsChoice == new ChoiceExtension(contentId, choiceId))
                .Concat(local.ChoiceExtensions(contentId, choiceId))
                .DistinctBy(r => r.Reference);

        /// <summary>
        /// This machine's revisions first, in stored order, then the package's: the order they have once the import adds
        /// them, so "the newest" (the last) means the same during the check as afterwards (review fix).
        /// </summary>
        public IEnumerable<ContentRevision> RevisionsOf(Guid contentId) =>
            local.RevisionsOf(contentId).Concat(revisions.Values.Where(r => r.ContentId == contentId)).DistinctBy(r => r.Reference);
    }

    private sealed class EntryTooLargeException(string path)
        : Exception($"Entry '{path}' exceeds the {MaxEntryBytes}-byte limit.");

    private sealed class TotalTooLargeException()
        : Exception($"The package's contents exceed {MaxTotalBytes} bytes when unpacked.");
}
