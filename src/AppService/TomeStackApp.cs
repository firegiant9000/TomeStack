using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// The application interface used by every transport (WebView2 bridge, loopback dev host, tests).
/// Owns validation and transactions; delegates calculation to the rules core.
/// </summary>
public sealed class TomeStackApp : IDisposable
{
    public const string DatabaseFileName = "tomestack.db";

    private readonly SqliteStore _store;
    private readonly PackageService _packages;
    private readonly TimeProvider _time;

    private readonly IReadOnlyList<Diagnostic> _warnings;

    private TomeStackApp(string dataDirectory, TimeProvider time, IReadOnlyList<Diagnostic> warnings)
    {
        DataDirectory = dataDirectory;
        _time = time;
        _warnings = warnings;
        _store = new SqliteStore(Path.Combine(dataDirectory, DatabaseFileName));
        _packages = new PackageService(_store, time, Path.Combine(dataDirectory, PackageService.BackupFolderName));
        ErrorLog = new FileErrorLog(Path.Combine(dataDirectory, "logs"), time);
    }

    public string DataDirectory { get; }

    /// <summary>Local-only log for unexpected failures (never sent to the UI).</summary>
    public IErrorLog ErrorLog { get; }

    internal SqliteStore Store => _store;

    /// <param name="syncRoots">Cloud sync roots to warn about (ADR-005); discovered from this machine when null.</param>
    public static TomeStackApp Open(string dataDirectory, TimeProvider? time = null, IEnumerable<string>? syncRoots = null)
    {
        Directory.CreateDirectory(dataDirectory);
        var warning = DataFolder.SyncRootWarning(dataDirectory, syncRoots ?? DataFolder.DiscoverSyncRoots());
        var app = new TomeStackApp(dataDirectory, time ?? TimeProvider.System, warning is null ? [] : [warning]);
        app.SeedFixturePack();
        return app;
    }

    /// <summary>
    /// Default data directory: <c>TOMESTACK_DATA_DIR</c> if set, else <c>%LOCALAPPDATA%\TomeStack</c> (D02, ADR-005).
    /// A folder inside a cloud sync root is allowed but warned about in <see cref="AppInfo.Warnings"/>.
    /// </summary>
    public static string DefaultDataDirectory(string folderName = "TomeStack") =>
        Environment.GetEnvironmentVariable("TOMESTACK_DATA_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folderName);

    public AppInfo GetInfo() => new(
        typeof(TomeStackApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        _store.SchemaVersion,
        RulesFamilies.All,
        _warnings);

    public IReadOnlyList<ContentOption> ListContent(string rulesFamily)
    {
        if (!RulesFamilies.IsKnown(rulesFamily))
            throw new AppValidationException([new("rules-family.unknown", $"Rules family '{rulesFamily}' is not supported.")]);
        var sources = _store.ListSources().ToDictionary(s => s.Id);
        return
        [
            .. _store.ListRevisions()
                .Where(r => r.Status == RevisionStatus.Published)
                .Select(r =>
                {
                    var source = sources.GetValueOrDefault(r.Provenance.SourceId);
                    return new ContentOption(
                        r.Reference, r.Kind, r.Name, r.RulesFamilies, r.RulesFamilies.Contains(rulesFamily),
                        r.Provenance.SourceId, source?.Title ?? "(unknown source)", r.Provenance.Page?.ToString(), r.Summary);
                })
                .OrderBy(o => o.Kind).ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(o => o.SourceTitle, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    public IReadOnlyList<CharacterSummary> ListCharacters() =>
        [.. _store.ListCharacters().Select(c => new CharacterSummary(c.Id, c.Name, c.RulesFamily, c.UpdatedAt))];

    public CharacterView GetCharacter(Guid id) =>
        _store.FindCharacter(id) is { } character
            ? View(character)
            : throw new AppValidationException([new("character.not-found", $"Character {id} does not exist.")]);

    public CharacterView CreateCharacter(CreateCharacterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SaveCharacter(new Character
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            RulesFamily = request.RulesFamily,
            BaseAbilities = request.BaseAbilities,
            Pins = request.Pins ?? [],
        });
    }

    public CharacterView SaveCharacter(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var problems = character.Validate().ToList();
        if (problems.Count == 0 && _store.FindCharacter(character.Id) is { } existing && existing.RulesFamily != character.RulesFamily)
            problems.Add(new("character.rules-family-changed", "Changing a saved character's rules family needs a reviewed migration and is not supported yet."));
        if (problems.Count > 0)
            throw new AppValidationException(problems);

        var saved = character with { UpdatedAt = _time.GetUtcNow() };
        _store.InTransaction(() => _store.SaveCharacter(saved));
        return View(saved);
    }

    public ExportResult ExportCharacters(IReadOnlyList<Guid> characterIds, ExportPurpose purpose = ExportPurpose.Backup) =>
        _packages.Export(characterIds, purpose);

    public ExportPreview PreviewExport(IReadOnlyList<Guid> characterIds, ExportPurpose purpose) => _packages.PreviewExport(characterIds, purpose);

    public PackagePreview PreviewImport(byte[] package) => _packages.Preview(package);

    public ImportResult ApplyImport(byte[] package, IReadOnlyDictionary<Guid, SourceChoice>? sourceChoices = null) =>
        _packages.Apply(package, sourceChoices);

    public void Dispose() => _store.Dispose();

    private CharacterView View(Character character) => new(character, CharacterCalculator.Calculate(character, _store));

    private void SeedFixturePack()
    {
        using var stream = typeof(TomeStackApp).Assembly.GetManifestResourceStream("TomeStack.FixturePack.json")
            ?? throw new InvalidOperationException("Embedded fixture pack is missing.");
        var pack = JsonSerializer.Deserialize<ContentPack>(stream, RulesJson.Options)
            ?? throw new InvalidOperationException("Embedded fixture pack is empty.");
        _store.InTransaction(() =>
        {
            foreach (var source in pack.Sources.Where(s => _store.FindSource(s.Id) is null))
                _store.UpsertSource(source);
            foreach (var revision in pack.Revisions)
                _store.AddRevision(revision);
        });
    }
}

/// <param name="Warnings">Startup warnings for the user, such as a data folder inside a sync root (<c>data-dir.sync-root</c>).</param>
public sealed record AppInfo(string Version, int SchemaVersion, IReadOnlyList<RulesFamilyPolicy> RulesFamilies, IReadOnlyList<Diagnostic> Warnings);

public sealed record ContentOption(
    ContentReference Reference,
    ContentKind Kind,
    string Name,
    IReadOnlyList<string> RulesFamilies,
    bool Compatible,
    Guid SourceId,
    string SourceTitle,
    string? Page,
    string? Summary);

public sealed record CharacterSummary(Guid Id, string Name, string RulesFamily, DateTimeOffset UpdatedAt);

public sealed record CharacterView(Character Character, CharacterSheet Sheet);

public sealed record CreateCharacterRequest(string Name, string RulesFamily, AbilityScores BaseAbilities, IReadOnlyList<ContentReference>? Pins);

/// <param name="code">Error code at the transport boundary: <c>validation</c>, or <c>unsupported</c> for a missing host capability.</param>
public sealed class AppValidationException(IReadOnlyList<Diagnostic> problems, string code = "validation")
    : Exception(string.Join(" ", problems.Select(p => p.Message)))
{
    public IReadOnlyList<Diagnostic> Problems { get; } = problems;

    public string Code { get; } = code;
}
