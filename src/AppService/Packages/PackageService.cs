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
    public const int MaxEntries = 2_000;
    private const string ManifestPath = "manifest.json";

    [GeneratedRegex("^(sources|content|characters)/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryPathPattern();

    public ExportResult Export(IReadOnlyList<Guid> characterIds)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
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
        foreach (var pin in characters.SelectMany(c => c.Pins))
        {
            if (store.FindRevision(pin) is { } revision)
                revisions[revision.RevisionId] = revision;
            else
                errors.Add(new("export.revision-missing", $"Pinned revision {pin.RevisionId} is missing; repair the character before exporting.", pin));
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

        var files = new SortedDictionary<string, (string Kind, byte[] Bytes)>(StringComparer.Ordinal);
        foreach (var source in sources.Values)
            files[$"sources/{source.Id:D}.json"] = ("source", Json(source with { PdfRef = null })); // machine-local path; may name the user
        foreach (var revision in revisions.Values)
            files[$"content/{revision.RevisionId:D}.json"] = ("contentRevision", Json(revision));
        foreach (var character in characters)
            files[$"characters/{character.Id:D}.json"] = ("character", Json(character));

        var createdAt = time.GetUtcNow();
        var manifest = new PackageManifest
        {
            CreatedAt = createdAt,
            AppVersion = typeof(PackageService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            Characters = [.. characters.Select(c => c.Id)],
            Entries = [.. files.Select(f => new PackageEntry(f.Key, f.Value.Kind, Hash(f.Value.Bytes), f.Value.Bytes.LongLength))],
            Notices = [.. sources.Values.OrderBy(s => s.Id).Select(s => new LicenseNotice(s.Id, s.Title, s.Publisher, s.License, s.Redistributable, s.Attribution))],
        };

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, ManifestPath, Json(manifest), createdAt);
            foreach (var (path, file) in files)
                WriteEntry(zip, path, file.Bytes, createdAt);
        }

        var name = characters.Count == 1 ? SafeFileName(characters[0].Name) : "characters";
        return new ExportResult($"{name}.tomestack.zip", buffer.ToArray(), manifest);
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
        var keepLocal = differing.Where(i => sourceChoices![i.Id] == SourceChoice.KeepLocal).Select(i => i.Id).ToHashSet();

        // SPEC C-07/Q-01: never overwrite a local character without a restorable copy.
        var toReplace = parsed.Characters.Where(c => store.FindCharacter(c.Id) is not null).Select(c => c.Id).ToList();
        var backupFile = toReplace.Count > 0 ? WriteBackup(toReplace) : null;

        int added = 0, replaced = 0, unchanged = 0;
        store.InTransaction(() =>
        {
            foreach (var source in parsed.Sources.Where(s => !keepLocal.Contains(s.Id)))
            {
                // A PDF reference is machine-local; an import never adds, changes or removes one.
                store.UpsertSource(source with { PdfRef = store.FindSource(source.Id)?.PdfRef });
            }
            foreach (var revision in parsed.Revisions)
            {
                if (store.AddRevision(revision)) added++;
                else unchanged++;
            }
            foreach (var character in parsed.Characters)
            {
                if (store.FindCharacter(character.Id) is null) added++;
                else replaced++;
                store.SaveCharacter(character);
            }
        });
        return new ImportResult(added, replaced, unchanged, [.. parsed.Characters.Select(c => c.Id)], backupFile);
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

    private sealed record ParsedPackage(
        PackageManifest Manifest,
        IReadOnlyList<SourceRecord> Sources,
        IReadOnlyList<ContentRevision> Revisions,
        IReadOnlyList<Character> Characters);

    private (PackagePreview Preview, ParsedPackage? Parsed) Read(byte[] package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var errors = new List<Diagnostic>();
        var parsed = Parse(package, errors);
        if (parsed is null)
            return (new PackagePreview(false, null, [], errors, []), null);

        var warnings = new List<Diagnostic>();
        var items = new List<PackageItem>();
        var packageRevisions = parsed.Revisions.ToDictionary(r => r.Reference);
        var packageSources = parsed.Sources.ToDictionary(s => s.Id);

        foreach (var source in parsed.Sources)
        {
            var local = store.FindSource(source.Id);
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
            if (revision.Status != RevisionStatus.Published)
                warnings.Add(new("package.revision-draft", $"'{revision.Name}' is a draft and stays inactive after import.", revision.Reference));
            var families = string.Join(", ", revision.RulesFamilies);
            items.Add(new("contentRevision", revision.RevisionId, revision.Name, action, $"{revision.Kind} · {families} · {revision.Status}"));
        }

        foreach (var character in parsed.Characters)
        {
            foreach (var problem in character.Validate())
                errors.Add(problem with { Message = $"'{character.Name}': {problem.Message}" });
            foreach (var pin in character.Pins)
            {
                if (!packageRevisions.ContainsKey(pin) && store.FindRevision(pin) is null)
                    errors.Add(new("package.pin-missing", $"'{character.Name}' pins revision {pin.RevisionId}, which is neither in the package nor installed.", pin));
            }
            var exists = store.FindCharacter(character.Id) is not null;
            if (exists)
                warnings.Add(new("package.character-replace", $"'{character.Name}' already exists and will be replaced by the imported copy. The current copy is saved to the {BackupFolderName} folder in your data folder first, and you can restore it by importing that file."));
            items.Add(new("character", character.Id, character.Name, exists ? PackageItemAction.Replace : PackageItemAction.Add, character.RulesFamily));
        }

        return (new PackagePreview(errors.Count == 0, parsed.Manifest, items, errors, warnings), parsed);
    }

    private static ParsedPackage? Parse(byte[] package, List<Diagnostic> errors)
    {
        if (package.LongLength > MaxPackageBytes)
        {
            errors.Add(new("package.too-large", $"Package is {package.LongLength} bytes; the limit is {MaxPackageBytes}."));
            return null;
        }

        Dictionary<string, byte[]> files;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries)
            {
                errors.Add(new("package.too-many-entries", $"Package has {zip.Entries.Count} entries; the limit is {MaxEntries}."));
                return null;
            }
            files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName != ManifestPath && !EntryPathPattern().IsMatch(entry.FullName))
                {
                    errors.Add(new("package.entry-not-allowed", $"Entry '{entry.FullName}' is not an allowed package path."));
                    continue;
                }
                if (!files.TryAdd(entry.FullName, ReadBounded(entry)))
                    errors.Add(new("package.entry-duplicate", $"Entry '{entry.FullName}' appears more than once."));
            }
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
        if (manifest.Format != PackageManifest.FormatName || manifest.FormatVersion is < 1 or > PackageManifest.CurrentFormatVersion)
        {
            errors.Add(new("package.unsupported-format", $"Package format '{manifest.Format}' v{manifest.FormatVersion} is not supported by this version (v{PackageManifest.CurrentFormatVersion})."));
            return null;
        }

        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries.Where(e => !listed.Add(e.Path)))
            errors.Add(new("package.entry-duplicate", $"Manifest lists '{entry.Path}' more than once."));
        foreach (var path in files.Keys.Where(p => p != ManifestPath && !listed.Contains(p)))
            errors.Add(new("package.entry-unlisted", $"Entry '{path}' is not listed in the manifest."));
        foreach (var entry in manifest.Entries)
        {
            if (!files.TryGetValue(entry.Path, out var bytes))
                errors.Add(new("package.entry-missing", $"Manifest lists '{entry.Path}', which is not in the package."));
            else if (Hash(bytes) != entry.Sha256)
                errors.Add(new("package.hash-mismatch", $"Entry '{entry.Path}' does not match its manifest hash; the package may be damaged or altered."));
        }
        if (errors.Count > 0)
            return null;

        var sources = new List<SourceRecord>();
        var revisions = new List<ContentRevision>();
        var characters = new List<Character>();
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
            }
        }
        return errors.Count > 0 ? null : new ParsedPackage(manifest, sources, revisions, characters);
    }

    /// <summary>Field-by-field differences in serialized form, ignoring the machine-local <c>pdfRef</c>.</summary>
    private static List<FieldChange> SourceChanges(SourceRecord local, SourceRecord imported)
    {
        var localNode = JsonSerializer.SerializeToNode(local, RulesJson.Compact)!.AsObject();
        var importedNode = JsonSerializer.SerializeToNode(imported, RulesJson.Compact)!.AsObject();
        return
        [
            .. localNode.Select(p => p.Key).Union(importedNode.Select(p => p.Key)).Order(StringComparer.Ordinal)
                .Where(field => field != "pdfRef")
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
    }

    /// <summary>Reads at most <see cref="MaxEntryBytes"/>; does not trust the declared entry length.</summary>
    private static byte[] ReadBounded(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxEntryBytes)
            throw new EntryTooLargeException(entry.FullName);
        using var stream = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > MaxEntryBytes)
                throw new EntryTooLargeException(entry.FullName);
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

    private sealed class EntryTooLargeException(string path)
        : Exception($"Entry '{path}' exceeds the {MaxEntryBytes}-byte limit.");
}
