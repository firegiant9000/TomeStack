using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// The application interface used by every transport (WebView2 bridge, loopback dev host, tests).
/// Owns validation and transactions; delegates calculation to the rules core.
/// </summary>
public sealed partial class TomeStackApp : IDisposable
{
    public const string DatabaseFileName = "tomestack.db";

    private readonly SqliteStore _store;
    private readonly PackageService _packages;
    private readonly TimeProvider _time;

    /// <summary>Startup warnings (<see cref="AppInfo.Warnings"/>): a sync-root data folder, and bundled revisions that could not be seeded.</summary>
    private readonly List<Diagnostic> _warnings;

    private TomeStackApp(string dataDirectory, TimeProvider time, List<Diagnostic> warnings)
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

    /// <summary>
    /// The bundled SRD packs (M1 item 1; spells, weapons and the caster classes since M2): always seeded. They are insert-only, so re-seeding is a no-op.
    /// Each family's packs share one source record (docs/licensing/srd-pack-review.md).
    /// </summary>
    public static IReadOnlyList<string> BundledPacks { get; } =
    [
        "TomeStack.Content.srd-5.1.json", "TomeStack.Content.srd-5.2.1.json",
        "TomeStack.Content.srd-5.1-spells.json", "TomeStack.Content.srd-5.2.1-spells.json",
        "TomeStack.Content.srd-5.1-equipment.json", "TomeStack.Content.srd-5.2.1-equipment.json",
        "TomeStack.Content.srd-5.1-classes.json", "TomeStack.Content.srd-5.2.1-classes.json",
        // M2.2: armor before the Fighter, which is proficient with it; the Fighter after the classes it may multiclass with.
        "TomeStack.Content.srd-5.1-armor.json", "TomeStack.Content.srd-5.2.1-armor.json",
        "TomeStack.Content.srd-5.1-fighter.json", "TomeStack.Content.srd-5.2.1-fighter.json",
    ];

    /// <param name="syncRoots">Cloud sync roots to warn about (ADR-005); discovered from this machine when null.</param>
    /// <param name="devFixtures">
    /// Also seed the original test fixture pack. Development only (DevHost, tests, <c>TOMESTACK_DEV_FIXTURES=1</c>): the
    /// shipped app has real SRD content, so fixtures stay out of user data (owner decision, 2026-09-26). Data folders
    /// that already contain fixture content keep it (published revisions are never deleted).
    /// </param>
    /// <param name="extractor">PDF extraction for imports (ADR-009); by default the isolated worker next to the app.</param>
    /// <exception cref="DataFolderInUseException">Another process has the folder open (M2.1); nothing was read or changed.</exception>
    public static TomeStackApp Open(string dataDirectory, TimeProvider? time = null, IEnumerable<string>? syncRoots = null, bool devFixtures = false, ImportWorker.IDocumentExtractor? extractor = null)
    {
        // Before the database opens: migration, the leftover-import check and the file cleanup below all assume this
        // process is the only one using the folder.
        var folderLock = DataFolderLock.Acquire(dataDirectory);
        TomeStackApp? app = null;
        try
        {
            var warning = DataFolder.SyncRootWarning(dataDirectory, syncRoots ?? DataFolder.DiscoverSyncRoots());
            app = new TomeStackApp(dataDirectory, time ?? TimeProvider.System, warning is null ? [] : [warning]) { _extractor = extractor, _folderLock = folderLock };
            app.InterruptLeftoverImports();
            var bundled = new HashSet<Guid>();
            foreach (var pack in BundledPacks)
                bundled.UnionWith(app.Seed(pack).Revisions.Select(r => r.RevisionId));
            // A stored revision that took a bundled id is the user's own data, so a full backup must keep it.
            bundled.ExceptWith(app._seedConflicts.Select(c => c.RevisionId));
            app._packages.SetBundledRevisions(bundled); // every install seeds these, so a full backup leaves them out
            if (devFixtures)
            {
                app.Seed("TomeStack.FixturePack.json");
                app.Seed("TomeStack.FixturePackM2.json"); // original test equipment (M2 item 4)
                app.Seed("TomeStack.FixturePackM2Spells.json"); // original test casters and spells (M2 spellcasting)
                app.Seed("TomeStack.FixturePackM2Combat.json"); // original test weapons and a multiclass class (M2)
                app.Seed("TomeStack.FixturePackM3Effects.json"); // original test toggles and shared, variable-cost resources (M3 B2)
            }
            AttachmentFiles.DeleteUnusedManagedFiles(app._store); // copies a failed delete or a rolled-back migration left (ADR-005)
            return app;
        }
        catch
        {
            if (app is not null)
                app.Dispose(); // releases the lock too
            else
                folderLock.Dispose();
            throw;
        }
    }

    private DataFolderLock? _folderLock;

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
        _warnings,
        CharacterCalculator.FieldInfos);

    /// <param name="campaignId">SPEC P-01: when set, each option says whether the campaign allows its source (<see cref="ContentOption.AllowedInCampaign"/>).</param>
    public IReadOnlyList<ContentOption> ListContent(string rulesFamily, Guid? campaignId = null)
    {
        if (!RulesFamilies.IsKnown(rulesFamily))
            throw new AppValidationException([new("rules-family.unknown", $"Rules family '{rulesFamily}' is not supported.")]);
        HashSet<Guid>? allowed = null;
        if (campaignId is { } id)
            allowed = (_store.FindCampaign(id) ?? throw new AppValidationException([new("campaign.not-found", $"Campaign {id} does not exist.")])).AllowedSources.ToHashSet();
        var sources = _store.ListSources().ToDictionary(s => s.Id);
        var published = _store.ListRevisionsInOrder().Where(r => r.Status == RevisionStatus.Published).ToList();
        // SPEC I-06: the newest published revision of each content is what new picks get; older ones stay listed (saved
        // characters still pin them and show their names) but are marked, so pickers offer only the newest.
        var newest = published.GroupBy(r => r.ContentId).ToDictionary(g => g.Key, g => g.Last().RevisionId);
        // Content that another revision grants or offers in a choice (class features, skill options) arrives through it;
        // pickers for directly pinned content leave it out.
        var reachable = published
            .SelectMany(r => r.Effects.OfType<GrantEffect>().Where(g => g.Content is not null).Select(g => g.Content!.ContentId)
                .Concat(r.Effects.OfType<ChoiceEffect>().SelectMany(c => c.Options.Select(o => o.ContentId))))
            .ToHashSet();
        return
        [
            .. published
                .Select(r =>
                {
                    var source = sources.GetValueOrDefault(r.Provenance.SourceId);
                    return new ContentOption(
                        r.Reference, r.Kind, r.Name, r.RulesFamilies, r.RulesFamilies.Contains(rulesFamily),
                        r.Provenance.SourceId, source?.Title ?? "(unknown source)", r.Provenance.Page?.ToString(), Preview(r.Summary),
                        allowed?.Contains(r.Provenance.SourceId),
                        r.Effects.OfType<SpellEffect>().FirstOrDefault() is { } spell
                            ? new SpellSummary(spell.Level, spell.Lists, spell.School, spell.Concentration, spell.Ritual)
                            : null,
                        Superseded: newest[r.ContentId] != r.RevisionId,
                        Standalone: !reachable.Contains(r.ContentId));
                })
                .OrderBy(o => o.Kind).ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(o => o.SourceTitle, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    /// <summary>A listing needs only the start of a summary; full feature texts would make every list call megabytes.</summary>
    private const int SummaryPreviewLength = 200;

    private static string? Preview(string? summary) =>
        summary is { Length: > SummaryPreviewLength } ? summary[..SummaryPreviewLength].TrimEnd() + "…" : summary;

    /// <summary>
    /// M1 item 3: schema, reference, formula and cycle problems for one revision, before it is published. Validates a
    /// stored revision (by reference) or an unsaved one (inline). Nothing is written.
    /// </summary>
    public ValidationReport ValidateContent(ContentReference? reference, ContentRevision? revision)
    {
        var target = revision
            ?? (reference is not null ? _store.FindRevision(reference) : null)
            ?? throw new AppValidationException([new("content.not-found", reference is null ? "Name a revision to validate." : $"Revision {reference.RevisionId} is not installed.", reference)]);
        return ContentValidator.Validate(target, _store);
    }

    public IReadOnlyList<CharacterSummary> ListCharacters() =>
        [.. _store.ListCharacters().Select(Summary)];

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
            Classes = request.Classes ?? [],
            Choices = request.Choices ?? [],
            CampaignId = request.CampaignId,
            CampaignExceptions = request.CampaignExceptions ?? [],
            Spells = request.Spells ?? [],
        });
    }

    /// <summary>
    /// <c>character.save</c>. SPEC C-05: the play state of a stored character is kept as stored, whatever the payload
    /// says, so a save for another reason (or from a stale copy) never changes hit points, spent uses or conditions.
    /// Only <see cref="Play"/> and <see cref="Rest"/>, which need a confirmation, write it. A new character keeps the
    /// play state it is saved with. The archive mark (SPEC C-08) is kept as stored too: only <c>character.archive</c> and
    /// <c>character.unarchive</c> change it.
    /// </summary>
    public CharacterView SaveCharacter(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return SaveWithPlay(_store.FindCharacter(character.Id) is { } stored
            ? character with { Play = stored.Play, ArchivedAt = stored.ArchivedAt }
            : character with { ArchivedAt = null }); // a save never creates an archived character
    }

    /// <summary>Saves the character with the play state it carries: for the confirmed play and rest commands only.</summary>
    private CharacterView SaveWithPlay(Character character)
    {
        var saved = Checked(character) with { UpdatedAt = _time.GetUtcNow() };
        // Calculate before writing: if the sheet cannot be calculated, nothing is stored (the store never holds a
        // character that cannot be opened).
        var view = View(saved);
        _store.InTransaction(() => _store.SaveCharacter(saved));
        return view;
    }

    /// <summary>
    /// SPEC C-01: records the options picked for one choice (an empty list clears it). The choice must be offered to the
    /// character now (its revision active, its level reached), every option must be one of its options and usable under
    /// the character's rules family, and the count must not be exceeded. Nothing else about the character changes.
    /// </summary>
    public CharacterView Choose(ChooseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var character = _store.FindCharacter(request.CharacterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {request.CharacterId} does not exist.")]);
        return SaveCharacter(WithChoice(character, request.Source, request.ChoiceId, request.Selected));
    }

    /// <summary>
    /// The character with one choice answered (or cleared), after the checks <see cref="Choose"/> documents. Shared by
    /// <see cref="Choose"/> (saved) and <see cref="PreviewChoice"/> (a builder draft, never saved).
    /// </summary>
    private Character WithChoice(Character character, ContentReference? source, string choiceId, IReadOnlyList<ContentReference>? selectedOrNull)
    {
        var selected = selectedOrNull ?? [];
        if (source is null || selected.Any(o => o is null))
            throw new AppValidationException([new("choice.empty-entry", "The choice source and every selected option must be set.")]);
        var request = (Source: source, ChoiceId: choiceId);
        var without = character with { Choices = [.. character.Choices.Where(c => !(c.Source == request.Source && c.ChoiceId == request.ChoiceId))] };
        var offered = CharacterCalculator.Calculate(without, _store).Choices?.FirstOrDefault(c => c.Source == request.Source && c.ChoiceId == request.ChoiceId)
            ?? throw new AppValidationException([new("choice.not-offered", $"Choice '{request.ChoiceId}' is not offered to this character: its content is not active or its level is not reached.", request.Source)]);

        var problems = new List<Diagnostic>();
        if (selected.Distinct().Count() != selected.Count)
            problems.Add(new("choice.duplicate-option", "The same option is selected more than once.", request.Source));
        if (selected.Count > offered.Count)
            problems.Add(new("choice.too-many", $"'{offered.SourceName}' choice '{offered.ChoiceId}' allows {offered.Count} selection(s), not {selected.Count}.", request.Source));
        foreach (var option in selected.Where(o => !offered.Options.Contains(o)))
            problems.Add(new("choice.invalid-option", $"Revision {option.RevisionId} is not an option of '{offered.SourceName}' choice '{offered.ChoiceId}'.", option));
        // One option answers one choice: SRD wording such as "another skill" means a second choice over the same list
        // must pick something new.
        var chosenElsewhere = without.Choices.SelectMany(c => c.Selected).ToHashSet();
        foreach (var option in selected.Where(chosenElsewhere.Contains))
            problems.Add(new("choice.option-already-chosen", $"Revision {option.RevisionId} is already selected for another choice.", option));
        foreach (var option in selected.Where(offered.Options.Contains))
        {
            var revision = _store.FindRevision(option);
            if (revision is null || revision.Status != RevisionStatus.Published || !revision.RulesFamilies.Contains(character.RulesFamily))
                problems.Add(new("choice.option-unavailable", $"Option {revision?.Name ?? option.RevisionId.ToString()} is not a published revision for {character.RulesFamily}.", option));
        }
        if (problems.Count > 0)
            throw new AppValidationException(problems);

        return selected.Count == 0
            ? without
            : without with { Choices = [.. without.Choices, new ChoiceSelection(request.Source, request.ChoiceId, selected)] };
    }

    public ExportResult ExportCharacters(IReadOnlyList<Guid> characterIds, ExportPurpose purpose = ExportPurpose.Backup) =>
        _packages.Export(characterIds, purpose);

    public ExportPreview PreviewExport(IReadOnlyList<Guid> characterIds, ExportPurpose purpose) => _packages.PreviewExport(characterIds, purpose);

    public PackagePreview PreviewImport(byte[] package) => _packages.Preview(package);

    public ImportResult ApplyImport(byte[] package, IReadOnlyDictionary<Guid, SourceChoice>? sourceChoices = null) =>
        _packages.Apply(package, sourceChoices);

    /// <summary>M2.1 "Back up everything": what it would contain (<c>library.backupPreview</c>).</summary>
    public LibraryBackupPreview PreviewLibraryBackup() => _packages.PreviewLibraryBackup();

    /// <summary>M2.1: writes the whole library, managed PDFs included, to <paramref name="output"/>.</summary>
    public LibraryBackupResult WriteLibraryBackup(Stream output) => _packages.WriteLibraryBackup(output);

    /// <summary>M2.1 "Restore full backup": checks the file completely; writes nothing.</summary>
    public PackagePreview PreviewLibraryRestore(string backupPath) => _packages.PreviewLibraryRestore(backupPath);

    public LibraryRestoreResult ApplyLibraryRestore(string backupPath, IReadOnlyDictionary<Guid, SourceChoice>? sourceChoices = null) =>
        _packages.ApplyLibraryRestore(backupPath, sourceChoices);

    public void Dispose()
    {
        StopImportsForDispose(); // a running import becomes "interrupted" and resumes after the next start
        _store.Dispose();
        _folderLock?.Dispose(); // last: the folder is free only once the database is closed
    }

    private CharacterView View(Character character)
    {
        var sheet = CharacterCalculator.Calculate(character, _store);
        return new(character, sheet, CampaignOf(character, sheet));
    }

    /// <summary>Loads an embedded content pack (bundled with this build, so trusted like code).</summary>
    public static ContentPack LoadBundledPack(string resourceName)
    {
        using var stream = typeof(TomeStackApp).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded content pack {resourceName} is missing.");
        return JsonSerializer.Deserialize<ContentPack>(stream, RulesJson.Options)
            ?? throw new InvalidOperationException($"Embedded content pack {resourceName} is empty.");
    }

    /// <summary>Bundled revisions whose id was already stored with other data (see <see cref="Seed"/>).</summary>
    private readonly List<ContentReference> _seedConflicts = [];

    /// <summary>
    /// Seeds a bundled pack (insert-only). A stored revision with the same id but different data (for example one a package
    /// brought in under an id that a later build bundles) is kept, and the bundled revision is skipped with a startup
    /// warning, instead of the data folder failing to open on every launch. Published revisions are never overwritten.
    /// </summary>
    private ContentPack Seed(string resourceName)
    {
        var pack = LoadBundledPack(resourceName);
        _store.InTransaction(() =>
        {
            foreach (var source in pack.Sources.Where(s => _store.FindSource(s.Id) is null))
                _store.UpsertSource(source);
            foreach (var revision in pack.Revisions)
            {
                try
                {
                    _store.AddRevision(revision);
                }
                catch (ImmutableRevisionException conflict) // thrown before anything is written
                {
                    _seedConflicts.Add(conflict.Reference);
                    // Ids and the bundled name only: never text from the stored revision, which may be the user's (gotcha: error logs).
                    _warnings.Add(new(
                        "content.bundled-conflict",
                        $"TomeStack could not add the bundled revision '{revision.Name}' ({revision.RevisionId}): your library already has different content under that id, which is kept. Characters that use this revision may calculate differently from the bundled rules.",
                        conflict.Reference));
                }
            }
        });
        return pack;
    }
}

/// <param name="Warnings">Startup warnings for the user, such as a data folder inside a sync root (<c>data-dir.sync-root</c>).</param>
/// <param name="Fields">Every calculated field and its label, for the homebrew studio (M2 item 5).</param>
public sealed record AppInfo(string Version, int SchemaVersion, IReadOnlyList<RulesFamilyPolicy> RulesFamilies, IReadOnlyList<Diagnostic> Warnings, IReadOnlyList<FieldInfo> Fields);

public sealed record ContentOption(
    ContentReference Reference,
    ContentKind Kind,
    string Name,
    IReadOnlyList<string> RulesFamilies,
    bool Compatible,
    Guid SourceId,
    string SourceTitle,
    string? Page,
    string? Summary,
    bool? AllowedInCampaign = null,
    SpellSummary? Spell = null,
    bool Superseded = false,
    bool Standalone = true);

/// <summary>What the builder's spell picker needs to filter and sort a spell option (content schema v5).</summary>
public sealed record SpellSummary(int Level, IReadOnlyList<string> Lists, string? School, bool Concentration, bool Ritual);

/// <param name="ArchivedAt">SPEC C-08: set while the character is archived; the UI lists it apart, collapsed.</param>
public sealed record CharacterSummary(Guid Id, string Name, string RulesFamily, DateTimeOffset UpdatedAt, DateTimeOffset? ArchivedAt = null, IReadOnlyList<Guid>? ExceptionContentIds = null);

/// <param name="Campaign">SPEC P-01: the character's campaign and its warnings (allowed sources, rules family), when it has one.</param>
public sealed record CharacterView(Character Character, CharacterSheet Sheet, CampaignStatus? Campaign = null);

public sealed record ChooseRequest(Guid CharacterId, ContentReference Source, string ChoiceId, IReadOnlyList<ContentReference>? Selected);

/// <param name="Classes">The starting class (and any further levels) from the builder draft (SPEC C-07).</param>
/// <param name="Choices">Choices answered in the builder draft; the sheet flags any that are no longer valid.</param>
public sealed record CreateCharacterRequest(
    string Name,
    string RulesFamily,
    AbilityScores BaseAbilities,
    IReadOnlyList<ContentReference>? Pins,
    IReadOnlyList<ClassLevel>? Classes = null,
    IReadOnlyList<ChoiceSelection>? Choices = null,
    Guid? CampaignId = null,
    IReadOnlyList<CampaignException>? CampaignExceptions = null,
    IReadOnlyList<KnownSpell>? Spells = null);

/// <summary>A choice answered on an unsaved builder draft (<c>character.previewChoice</c>).</summary>
public sealed record PreviewChoiceRequest(Character Draft, ContentReference Source, string ChoiceId, IReadOnlyList<ContentReference>? Selected);

/// <param name="code">Error code at the transport boundary: <c>validation</c>, or <c>unsupported</c> for a missing host capability.</param>
public sealed class AppValidationException(IReadOnlyList<Diagnostic> problems, string code = "validation")
    : Exception(string.Join(" ", problems.Select(p => p.Message)))
{
    public IReadOnlyList<Diagnostic> Problems { get; } = problems;

    public string Code { get; } = code;
}
