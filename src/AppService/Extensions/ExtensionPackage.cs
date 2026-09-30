using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Extensions;

/// <summary>ADR-011: what an extension may do. Declared in its manifest, granted by the user, revocable.</summary>
public static class ExtensionPermissions
{
    public const string ReadSheet = "read.sheet";
    public const string ReadContent = "read.content";
    public const string ImportFile = "import.file";
    public const string WriteDrafts = "write.drafts";
    public const string ExportFile = "export.file";

    public static IReadOnlyList<string> All { get; } = [ReadSheet, ReadContent, ImportFile, WriteDrafts, ExportFile];

    /// <summary>What each grants, in words for the install preview (ADR-011 "Permissions").</summary>
    public static string Describe(string permission) => permission switch
    {
        ReadSheet => "Read the sheet of a character you pick when you run it (no gap notes, no file paths).",
        ReadContent => "Read the published content of sources you pick when you run it, filtered for sharing.",
        ImportFile => "Read one JSON or CSV file you pick when you run it (at most 5 MB). It never sees where the file is.",
        WriteDrafts => "Create draft content in a new source of its own. Drafts are never active until you publish them.",
        ExportFile => "Write one file where you choose when you run it. It never sees where the file goes.",
        _ => "Unknown permission.",
    };
}

/// <summary>An import hook reads one file (<c>accepts</c>); an export hook writes one (<c>produces</c>).</summary>
public enum HookKind { Import, Export }

/// <param name="Accepts">Import hooks: <c>json</c> or <c>csv</c>.</param>
/// <param name="Produces">Export hooks: <c>text</c> or <c>json</c>.</param>
/// <param name="FileExtension">Export hooks: the output file's extension (<c>.txt</c>, <c>.md</c>, <c>.json</c> or <c>.csv</c>).</param>
/// <param name="Transform">The transform document in the extension file: <c>transforms/&lt;id&gt;.json</c>.</param>
public sealed record ExtensionHook(HookKind Kind, string Id, string Label, string Transform, string? Accepts = null, string? Produces = null, string? FileExtension = null);

/// <summary><c>extension.json</c> (docs/schemas/extension-manifest.v1.schema.json, ADR-011).</summary>
public sealed record ExtensionManifest
{
    public const string FormatName = "tomestack.extension";
    public const int CurrentFormatVersion = 1;

    public required string Format { get; init; }
    public required int FormatVersion { get; init; }
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Author { get; init; }
    public required string License { get; init; }
    public string? Homepage { get; init; }
    public required int ExtensionApi { get; init; }
    public required string Runtime { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
    public required IReadOnlyList<ExtensionHook> Hooks { get; init; }
    public string? Description { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; init; }
}

/// <summary>An extension file read and checked: its manifest, its transforms and the SHA-256 of the whole file.</summary>
public sealed record ExtensionPackage(ExtensionManifest Manifest, string Sha256, IReadOnlyDictionary<string, DeclarativeTransform> Transforms, long Bytes);

/// <summary>
/// Reads a <c>*.tomestack-ext.zip</c> as untrusted input, under the package limits (ADR-011 "An extension is a separate
/// artifact"): the size and entry count are checked, every path is on an allowlist and is checked before anything is
/// unpacked, nothing is extracted to disk, and every declared transform must parse. Nothing in it runs here.
/// </summary>
public static partial class ExtensionReader
{
    public const long MaxFileBytes = 5L * 1024 * 1024;
    public const int MaxEntries = 64;
    public const long MaxUnpackedBytes = 16L * 1024 * 1024;

    /// <summary>ADR-011 API versions this build runs.</summary>
    public static IReadOnlyList<int> SupportedApis { get; } = [1];

    [GeneratedRegex("^(extension\\.json|transforms/[a-z0-9][a-z0-9-]{0,39}\\.json)$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryPath();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex HookId();

    [GeneratedRegex("^(0|[1-9][0-9]{0,5})\\.(0|[1-9][0-9]{0,5})\\.(0|[1-9][0-9]{0,5})$", RegexOptions.CultureInvariant)]
    private static partial Regex SemVer();

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <exception cref="ExtensionException">Every problem found, with its code; nothing was installed.</exception>
    public static ExtensionPackage Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.LongLength > MaxFileBytes)
            throw new ExtensionException([new("extension.too-large", $"An extension file is at most {MaxFileBytes / (1024 * 1024)} MB.")]);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries)
                throw new ExtensionException([new("extension.too-many-entries", $"An extension file has at most {MaxEntries} entries.")]);
            var stray = zip.Entries.Where(e => !EntryPath().IsMatch(e.FullName)).Select(e => new Diagnostic("extension.entry-not-allowed", $"Entry '{Clip(e.FullName)}' is not allowed in an extension (only extension.json and transforms/<hook>.json).")).ToList();
            if (stray.Count > 0)
                throw new ExtensionException(stray);
            long remaining = MaxUnpackedBytes;
            foreach (var entry in zip.Entries)
            {
                if (entry.Length > DeclarativeTransform.MaxDocumentBytes || entry.Length > remaining)
                    throw new ExtensionException([new("extension.entry-too-large", $"Entry '{entry.FullName}' is too large.")]);
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = stream.Read(chunk)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > DeclarativeTransform.MaxDocumentBytes || buffer.Length > remaining)
                        throw new ExtensionException([new("extension.entry-too-large", $"Entry '{entry.FullName}' is too large.")]);
                }
                remaining -= buffer.Length;
                if (!files.TryAdd(entry.FullName, buffer.ToArray()))
                    throw new ExtensionException([new("extension.entry-duplicate", $"Entry '{entry.FullName}' appears more than once.")]);
            }
        }
        catch (InvalidDataException)
        {
            throw new ExtensionException([new("extension.invalid-archive", "The file is not a readable extension (a ZIP file).")]);
        }

        if (!files.TryGetValue("extension.json", out var manifestBytes))
            throw new ExtensionException([new("extension.manifest-missing", "The extension has no extension.json.")]);
        var manifest = ReadManifest(manifestBytes);
        var errors = Check(manifest).ToList();
        var transforms = new Dictionary<string, DeclarativeTransform>(StringComparer.Ordinal);
        foreach (var hook in errors.Count == 0 ? manifest.Hooks : [])
        {
            if (!files.TryGetValue(hook.Transform, out var document))
            {
                errors.Add(new("extension.transform-missing", $"Hook '{hook.Id}' names {hook.Transform}, which is not in the file."));
                continue;
            }
            try
            {
                transforms[hook.Id] = DeclarativeTransform.Parse(document);
            }
            catch (TransformException ex)
            {
                errors.Add(new(ex.Code, $"Hook '{hook.Id}': {ex.Message}"));
            }
        }
        foreach (var unused in files.Keys.Where(p => p != "extension.json" && errors.Count == 0 && !manifest.Hooks.Any(h => h.Transform == p)))
            errors.Add(new("extension.entry-unused", $"Entry '{unused}' is not a transform of any hook."));
        if (errors.Count > 0)
            throw new ExtensionException(errors);
        return new ExtensionPackage(manifest, Sha256(bytes), transforms, bytes.LongLength);
    }

    private static ExtensionManifest ReadManifest(byte[] bytes)
    {
        try
        {
            // The version first, so a newer format is refused as newer rather than as damaged.
            using (var document = JsonDocument.Parse(bytes))
            {
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("formatVersion", out var version) && version.TryGetInt32(out var number) && number > ExtensionManifest.CurrentFormatVersion)
                    throw new ExtensionException([new("extension.format-unsupported", $"The extension uses format v{number}; this TomeStack reads v{ExtensionManifest.CurrentFormatVersion}. Update TomeStack to install it.")]);
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("extensionApi", out var api) && api.TryGetInt32(out var apiNumber) && !SupportedApis.Contains(apiNumber))
                    throw new ExtensionException([new("extension.api-unsupported", $"The extension was written for extension API {apiNumber}; this TomeStack supports {string.Join(", ", SupportedApis)}. Nothing was installed.")]);
            }
            return JsonSerializer.Deserialize<ExtensionManifest>(bytes, RulesJson.Options)
                ?? throw new JsonException("The manifest is null.");
        }
        catch (JsonException)
        {
            throw new ExtensionException([new("extension.invalid-manifest", "extension.json is not a valid extension manifest (see docs/schemas/extension-manifest.v1.schema.json).")]);
        }
    }

    /// <summary>The manifest's own rules (ADR-011 "Manifest"). Each problem is reported; the messages never quote the manifest's free text.</summary>
    public static IEnumerable<Diagnostic> Check(ExtensionManifest manifest)
    {
        if (manifest.Format != ExtensionManifest.FormatName || manifest.FormatVersion != ExtensionManifest.CurrentFormatVersion)
            yield return new("extension.format-unsupported", $"extension.json must say format '{ExtensionManifest.FormatName}', version {ExtensionManifest.CurrentFormatVersion}.");
        if (manifest.Id == Guid.Empty)
            yield return new("extension.id-required", "An extension needs a UUID id.");
        if (!Text(manifest.Name, 100)) yield return new("extension.name-required", "An extension needs a name of 1 to 100 characters.");
        if (!Text(manifest.Author, 100)) yield return new("extension.author-required", "An extension needs an author of 1 to 100 characters.");
        if (!Text(manifest.License, 100)) yield return new("extension.license-required", "An extension must state its license (1 to 100 characters); it is not installed without one.");
        if (manifest.Version is null || !SemVer().IsMatch(manifest.Version)) yield return new("extension.version-invalid", "The version is major.minor.patch, for example 1.0.0.");
        if (manifest.Homepage is { Length: > 200 }) yield return new("extension.homepage-invalid", "The homepage is at most 200 characters. TomeStack never opens it.");
        if (manifest.Description is { Length: > 2000 }) yield return new("extension.description-too-long", "The description is at most 2,000 characters.");
        if (!SupportedApis.Contains(manifest.ExtensionApi))
            yield return new("extension.api-unsupported", $"The extension was written for extension API {manifest.ExtensionApi}; this TomeStack supports {string.Join(", ", SupportedApis)}.");
        if (manifest.Runtime != "declarative")
            yield return new("extension.runtime-unsupported", "This TomeStack runs only declarative extensions (ADR-011 option A): no extension code ever runs.");
        if (manifest.Unknown is { Count: > 0 })
            yield return new("extension.field-unknown", $"extension.json has fields this TomeStack does not know ({manifest.Unknown.Count}); a newer extension may need a newer TomeStack.");
        var permissions = manifest.Permissions ?? [];
        foreach (var unknown in permissions.Where(p => !ExtensionPermissions.All.Contains(p)))
            yield return new("extension.permission-unknown", $"Permission '{Clip(unknown ?? "")}' is not one this TomeStack knows, so nothing was installed.");
        if (permissions.Distinct().Count() != permissions.Count)
            yield return new("extension.permission-repeated", "Each permission is listed once.");
        var hooks = manifest.Hooks ?? [];
        if (hooks.Count is 0 or > 20)
            yield return new("extension.hooks-count", "An extension has 1 to 20 hooks.");
        if (hooks.Any(h => h is null))
        {
            yield return new("extension.hook-invalid", "A hook is empty.");
            yield break;
        }
        if (hooks.Select(h => h.Id).Distinct().Count() != hooks.Count)
            yield return new("extension.hook-repeated", "Each hook id is used once.");
        foreach (var hook in hooks)
        {
            var id = hook.Id is not null && HookId().IsMatch(hook.Id) ? hook.Id : null;
            if (id is null)
            {
                yield return new("extension.hook-invalid", "A hook id is 1 to 40 lowercase letters, digits and hyphens.");
                continue;
            }
            if (!Text(hook.Label, 100))
                yield return new("extension.hook-invalid", $"Hook '{id}' needs a label of 1 to 100 characters.");
            if (hook.Transform != $"transforms/{id}.json")
                yield return new("extension.hook-invalid", $"Hook '{id}' reads its transform from transforms/{id}.json.");
            if (hook.Kind == HookKind.Import)
            {
                if (hook.Accepts is not ("json" or "csv"))
                    yield return new("extension.hook-invalid", $"Import hook '{id}' accepts json or csv.");
                if (!permissions.Contains(ExtensionPermissions.ImportFile) || !permissions.Contains(ExtensionPermissions.WriteDrafts))
                    yield return new("extension.hook-permission", $"Import hook '{id}' needs the import.file and write.drafts permissions.");
            }
            else
            {
                if (hook.Produces is not ("text" or "json"))
                    yield return new("extension.hook-invalid", $"Export hook '{id}' produces text or json.");
                if (hook.FileExtension is not (null or ".txt" or ".md" or ".json" or ".csv"))
                    yield return new("extension.hook-invalid", $"Export hook '{id}' writes a .txt, .md, .json or .csv file.");
                if (!permissions.Contains(ExtensionPermissions.ExportFile) || !(permissions.Contains(ExtensionPermissions.ReadSheet) || permissions.Contains(ExtensionPermissions.ReadContent)))
                    yield return new("extension.hook-permission", $"Export hook '{id}' needs export.file and read.sheet or read.content.");
            }
        }
    }

    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;

    private static string Clip(string text) => text.Length <= 60 ? text : text[..60] + "…";
}

public sealed class ExtensionException(IReadOnlyList<Diagnostic> errors) : Exception(string.Join(" ", errors.Select(e => e.Message)))
{
    public IReadOnlyList<Diagnostic> Errors { get; } = errors;
}
