using TomeStack.AppService.CharacterImport;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Forms;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// Character import from a D&amp;D Beyond PDF sheet (<c>features/ddb-pdf-import.md</c>), reading side: the form fields are
/// read once in the import worker (ADR-009), parsed with the layout map, and held in memory under a token. Nothing is
/// written to the database; <c>ddb.readData</c>'s temporary copy is deleted before the command returns. Error messages
/// carry codes and counts, never a field value.
/// </summary>
public sealed partial class TomeStackApp
{
    /// <summary>The sheet size limit of the reader (20 MB).</summary>
    public static long MaxDdbSheetBytes => WorkerFormReader.DefaultLimits.MaxBytes;

    private const string DdbTempPrefix = "ddb-";

    private IFormReader? _formReader;

    private IFormReader FormReader => _formReader ??= new WorkerFormReader(Path.Combine(AppContext.BaseDirectory, WorkerFileName));

    private ImportSessions? _ddbSessions;

    internal ImportSessions DdbSessions => _ddbSessions ??= new ImportSessions(_time);

    private string DdbTempFolder => Path.Combine(DataDirectory, "tmp");

    /// <summary><c>ddb.read</c>: reads the sheet at <paramref name="path"/> (a dialog's choice or the temporary copy, never a UI path).</summary>
    public DdbReadResult ReadDdbSheet(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        IReadOnlyList<FormField> fields;
        try
        {
            fields = FormReader.ReadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (ExtractionException ex) when (ex.Code.StartsWith("worker.", StringComparison.Ordinal))
        {
            throw Refused("ddb.read-failed", $"The sheet could not be read ({ex.Code}). Nothing was kept.");
        }
        catch (ExtractionException ex)
        {
            throw Refused(ex.Code, ex.Message); // the reader's messages never quote the document
        }

        var map = DdbParser.Recognise(fields)
            ?? throw Refused("ddb.layout-unknown", $"This PDF's {fields.Count} form field{(fields.Count == 1 ? "" : "s")} do not match a known D&D Beyond sheet layout.");
        var sheet = DdbParser.Parse(map, fields);
        var token = DdbSessions.Add(sheet);
        var classes = sheet.Classes.Status == ReadStatus.Ok
            ? string.Join(" / ", sheet.Classes.Value!.Select(c => $"{c.Name} {c.Level}{(c.Subclass is null ? "" : $" ({c.Subclass})")}"))
            : "";
        return new DdbReadResult(token, map.Id, map.SuggestedFamily,
            new DdbSummary(sheet.Name.Value ?? "", classes, sheet.Features.Count, sheet.Spells.Count, sheet.Items.Count));
    }

    /// <summary>
    /// <c>ddb.readData</c> (DevHost, browser, e2e): the bytes go to <c>tmp/ddb-&lt;guid&gt;.pdf</c> under the data folder, are
    /// read, and the file is deleted whatever happens. The size is checked before anything is written. A copy another
    /// program still held is retried briefly, then left for the sweep that runs before the next read and at start.
    /// </summary>
    public DdbReadResult ReadDdbSheetData(string fileName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.LongLength > MaxDdbSheetBytes)
            throw Refused("pdf.too-large", $"The PDF is larger than {MaxDdbSheetBytes / (1024 * 1024)} MB.");
        DeleteLeftoverDdbFiles(); // an earlier copy that could not be deleted then
        Directory.CreateDirectory(DdbTempFolder);
        var path = Path.Combine(DdbTempFolder, $"{DdbTempPrefix}{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, data);
            return ReadDdbSheet(path);
        }
        finally
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    File.Delete(path);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Held for a moment by another program (a virus scanner).
                    if (attempt < 2)
                        Thread.Sleep(100 * (attempt + 1));
                }
            }
        }
    }

    /// <summary>The override reason of a number the user keeps from the sheet (step 4).</summary>
    public const string ImportedOverrideReason = "Imported from D&D Beyond";

    /// <summary>What the import leaves out by design (D16d, D16e), listed in every report.</summary>
    private static readonly string[] NotBroughtOver =
        ["currency", "notes", "speed", "passivePerception", "attunement", "languages", "toolProficiencies", "senses", "appearance", "backstory", "playerName"];

    /// <summary>
    /// <c>ddb.preview</c>: the proposed character for the read sheet under the chosen family and campaign, with every match,
    /// the open choices, the number comparison and the ability plan. The token stays; nothing is written.
    /// </summary>
    public DdbPreview PreviewDdbImport(DdbPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sheet = DdbSessions.Peek(request.Token)
            ?? throw Refused("ddb.token-invalid", "This sheet is no longer open: it was used, discarded or expired. Read it again.");
        return ProposeDdbCharacter(sheet, request, Guid.NewGuid());
    }

    /// <summary>The text of a note the user asked for on a differing number (it names no value).</summary>
    public const string ImportFieldNoteText = "The D&D Beyond sheet showed a different number here when this character was imported.";

    /// <summary>
    /// <c>ddb.apply</c>: refused without <c>confirm</c> (<c>ddb.confirmation-required</c>) and with an unknown, used or
    /// expired token (<c>ddb.token-invalid</c>). Rebuilds the previewed character under a new id and saves it through the
    /// same path as <c>character.save</c>, with its import notes and noted differences, in one transaction; then spends the
    /// token. A refusal anywhere writes nothing and keeps the token.
    /// </summary>
    public DdbApplyResult ApplyDdbImport(DdbApplyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirm)
            throw Refused("ddb.confirmation-required", "Creating the character needs confirmation.");
        var sheet = DdbSessions.Peek(request.Token)
            ?? throw Refused("ddb.token-invalid", "This sheet is no longer open: it was used, discarded or expired. Read it again.");
        var proposal = ProposeDdbCharacter(sheet,
            new DdbPreviewRequest(request.Token, request.RulesFamily, request.CampaignId, request.Resolutions, request.NumberChoices, request.IncludePlayState, request.Answers, request.EquipMatched),
            Guid.NewGuid());

        var problems = new List<Diagnostic>();
        if (sheet.Classes.Status != ReadStatus.Ok)
            problems.Add(new("character.level-out-of-range", $"The sheet's class levels could not be read, or add up to more than {Character.MaxLevel}. Fix the classes before creating the character."));
        if (proposal.Matches.Any(m => m.Status == MatchStatus.Choose))
            problems.Add(new("ddb.choice-required", $"{proposal.Matches.Count(m => m.Status == MatchStatus.Choose)} item(s) still need a choice or \"Leave out\"."));
        if (!proposal.CanApply && problems.Count == 0)
            problems.AddRange(proposal.Diagnostics.Where(d => d.Code.StartsWith("character.", StringComparison.Ordinal)));
        if (!proposal.CanApply && problems.Count == 0)
            problems.Add(new("ddb.cannot-apply", "The character cannot be created yet: it needs at least one class."));
        if (problems.Count > 0)
            throw new AppValidationException(problems, problems[0].Code);

        var character = proposal.Character;
        var labels = proposal.Matches
            .Where(m => m.Status is MatchStatus.NotFound or MatchStatus.NoPlace or MatchStatus.Unreadable && !string.IsNullOrWhiteSpace(m.Label))
            .Select(m => m.Label)
            .ToList();
        // A note only on a number this proposal compares and finds different: a choice left from an earlier preview (its
        // field gone, or now equal) writes nothing rather than refusing the apply or noting a difference that is not there.
        var noteFields = (request.NumberChoices ?? []).Where(n => n?.Action == NumberAction.Note && n.Field is not null).Select(n => n.Field).ToHashSet(StringComparer.Ordinal);
        var noted = proposal.Comparison.Where(n => n.Differs && noteFields.Contains(n.Field)).Select(n => n.Field).Distinct(StringComparer.Ordinal).ToList();
        var importNotes = 0;
        _store.InTransaction(() =>
        {
            SaveCharacter(character);
            // The few noted differences first, so the import notes (counted past the limit, not stored) never crowd them out.
            foreach (var field in noted)
                AddGapNote(new(character.Id, new(GapTargetKind.Field, FieldId: field), ImportFieldNoteText)); // refused: everything rolls back
            importNotes = AddImportGapNotes(character.Id, labels);
        });
        DdbSessions.Take(request.Token);
        return new DdbApplyResult(character.Id, character.Overrides.Count, importNotes + noted.Count, labels.Count - importNotes, proposal.Report);
    }

    /// <summary>Builds the proposal for <paramref name="sheet"/> (shared with <c>ddb.apply</c>, which saves it under <paramref name="id"/>).</summary>
    internal DdbPreview ProposeDdbCharacter(DdbSheet sheet, DdbPreviewRequest request, Guid id)
    {
        var options = ListContent(request.RulesFamily, request.CampaignId).Where(o => o.Compatible && !o.Superseded).ToList();
        static Character Levelled(Character c) => c.Classes.Count > 0 ? c with { Level = c.Classes.Sum(l => l.Level) } : c;
        CharacterSheet Calculate(Character c) => CharacterCalculator.Calculate(Levelled(c), _store);

        var start = new Character
        {
            Id = id,
            Name = sheet.Name.Value is { } name && !string.IsNullOrWhiteSpace(name) ? name.Trim() : "Imported character",
            RulesFamily = request.RulesFamily,
            BaseAbilities = new(AbilitySolver.DefaultBase, AbilitySolver.DefaultBase, AbilitySolver.DefaultBase, AbilitySolver.DefaultBase, AbilitySolver.DefaultBase, AbilitySolver.DefaultBase),
            CampaignId = request.CampaignId,
        };
        var planner = new ImportPlanner(start, options, request.Resolutions ?? [], Calculate,
            (c, source, choiceId, selected) => WithChoice(Levelled(c), source, choiceId, selected), _store.FindRevision, request.EquipMatched);
        planner.Plan(sheet);
        var diagnostics = new List<Diagnostic>();
        var answered = planner.Character;
        var accepted = new HashSet<(ContentReference, string)>();
        foreach (var answer in (request.Answers ?? []).Where(a => a?.Source is not null))
        {
            try
            {
                answered = WithChoice(Levelled(answered), answer.Source, answer.ChoiceId, answer.Selected);
                accepted.Add((answer.Source, answer.ChoiceId));
            }
            catch (AppValidationException ex)
            {
                diagnostics.AddRange(ex.Problems); // a refused answer stays open, as in the builder
            }
        }

        var scores = sheet.Abilities.Where(a => a.Value.Status == ReadStatus.Ok).ToDictionary(a => a.Key, a => a.Value.Value);
        var plan = AbilitySolver.Solve(bases => Calculate(answered with { BaseAbilities = bases }), scores);
        var character = Levelled(answered with { BaseAbilities = plan.ProposedBase });
        if (request.IncludePlayState)
        {
            character = character with { Play = PlayFrom(sheet.Play) };
            if (sheet.Play.UnreadableSpent > 0)
                diagnostics.Add(new("ddb.play-unreadable", $"{sheet.Play.UnreadableSpent} spent hit dice or spell slot field(s) could not be read and are left as not spent."));
        }

        var calculated = Calculate(character);
        var comparison = new List<NumberRow>();
        void Compare(string field, int value)
        {
            if (calculated.Fields.FirstOrDefault(f => f.Field == field) is { } derived)
                comparison.Add(new(field, derived.Label, value, derived.ComputedValue, derived.ComputedValue != value));
        }
        foreach (var (ability, score) in scores)
            Compare(FieldIds.Score(ability), score);
        foreach (var (field, read) in sheet.Numbers.Where(n => n.Value.Status == ReadStatus.Ok))
            Compare(field, read.Value);
        comparison = [.. comparison.OrderByDescending(n => n.Differs)];

        var kept = (request.NumberChoices ?? []).Where(n => n?.Action == NumberAction.KeepSheet).Select(n => n.Field).ToHashSet(StringComparer.Ordinal);
        character = character with
        {
            Overrides = [.. comparison.Where(n => n.Differs && kept.Contains(n.Field)).Select(n => new FieldOverride(n.Field, n.Sheet!.Value, ImportedOverrideReason))],
        };

        var valid = true;
        try
        {
            character = Checked(character);
        }
        catch (AppValidationException ex)
        {
            diagnostics.AddRange(ex.Problems);
            valid = false;
        }
        var final = Calculate(character);
        diagnostics.AddRange(final.Diagnostics);

        var rows = planner.Rows;
        int Count(MatchStatus status) => rows.Count(r => r.Status == status);
        var report = new DdbReport(
            Count(MatchStatus.Matched) - planner.ChosenByUser,
            planner.ChosenByUser,
            Count(MatchStatus.NotFound),
            Count(MatchStatus.NoPlace),
            Count(MatchStatus.Unreadable),
            Count(MatchStatus.LeftOut),
            [.. NotBroughtOver, .. request.IncludePlayState ? Array.Empty<string>() : ["playState"]],
            ListCharacters().Any(c => string.Equals(c.Name, character.Name, StringComparison.OrdinalIgnoreCase)),
            sheet.SuggestedFamily is { } suggested && suggested != request.RulesFamily);
        var canApply = valid && sheet.Classes.Status == ReadStatus.Ok && character.Classes.Count > 0 && Count(MatchStatus.Choose) == 0;
        // A choice the user answered stays listed once it is full, so step 3 can still change the answer; a refused answer
        // does not keep a choice the sheet filled listed (its diagnostic says why).
        var openChoices = (final.Choices ?? []).Where(c => !c.Resolved || accepted.Contains((c.Source, c.ChoiceId))).ToList();
        return new DdbPreview(character, rows, openChoices, comparison, plan, report, diagnostics, canApply);
    }

    /// <summary>D16d: only what <see cref="PlayState"/> has and the layout read.</summary>
    private static PlayState PlayFrom(DdbPlay play) => new()
    {
        CurrentHitPoints = play.CurrentHitPoints.Status == ReadStatus.Ok ? play.CurrentHitPoints.Value : null,
        TemporaryHitPoints = play.TemporaryHitPoints.Status == ReadStatus.Ok ? play.TemporaryHitPoints.Value : 0,
        HitDiceSpent = [.. play.HitDiceSpent.Select(h => new HitDiceUse(h.Die, h.Spent))],
        DeathSaves = new(play.DeathSuccesses.Status == ReadStatus.Ok ? play.DeathSuccesses.Value : 0, play.DeathFailures.Status == ReadStatus.Ok ? play.DeathFailures.Value : 0),
        Inspiration = play.Inspiration is { Status: ReadStatus.Ok, Value: true },
        SpellSlotsSpent = [.. play.SpellSlotsSpent.Select(s => new SpellSlotUse(s.Level, s.Spent))],
    };

    /// <summary><c>ddb.discard</c>: drops the token. False when it was unknown, used or expired.</summary>
    public bool DiscardDdbSheet(Guid token) => DdbSessions.Discard(token);

    /// <summary>At startup: a crash during <c>ddb.readData</c> can leave its temporary copy.</summary>
    private void DeleteLeftoverDdbFiles()
    {
        try
        {
            if (!Directory.Exists(DdbTempFolder))
                return;
            foreach (var file in Directory.EnumerateFiles(DdbTempFolder, $"{DdbTempPrefix}*.pdf"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Held by another program for a moment: the next start tries again.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable tmp folder must not stop the app from opening; the next start tries again.
        }
    }

    private static AppValidationException Refused(string code, string message) => new([new Diagnostic(code, message)], code);
}

public enum NumberAction { UseTomeStack, KeepSheet, Note }

/// <summary>The user's answer for one differing number (step 4): TomeStack's number, the sheet's as an override, or a gap note.</summary>
public sealed record NumberChoice(string Field, NumberAction Action);

/// <param name="Sheet">The sheet's number; <paramref name="Calculated"/> is TomeStack's, before any override.</param>
public sealed record NumberRow(string Field, string Label, int? Sheet, int Calculated, bool Differs);

/// <param name="NotBroughtOver">Codes of what the import leaves out by design (<c>currency</c>, <c>speed</c>, <c>playState</c> unless asked, …).</param>
public sealed record DdbReport(int Matched, int Chosen, int NotFound, int NoPlace, int Unreadable, int LeftOut, IReadOnlyList<string> NotBroughtOver, bool SameNameExists, bool FamilyMismatch);

/// <param name="Character">The proposed character (a new id), as <c>ddb.apply</c> would save it. Nothing is stored.</param>
/// <param name="OpenChoices">Choices the draft offers that the sheet did not settle, and those the user answered (so the answer can change); the builder can answer them later.</param>
/// <param name="CanApply">No row waits for a choice, the classes were read, and the character passes the save checks.</param>
public sealed record DdbPreview(
    Character Character,
    IReadOnlyList<MatchRow> Matches,
    IReadOnlyList<ChoiceStatus> OpenChoices,
    IReadOnlyList<NumberRow> Comparison,
    AbilityPlan AbilityPlan,
    DdbReport Report,
    IReadOnlyList<Diagnostic> Diagnostics,
    bool CanApply);

/// <param name="Answers">Open choices the user answered in step 3 (a 2024 background's ability scores), applied after the matches through the builder's check.</param>
/// <param name="EquipMatched">D16g: matched items whose revision is a weapon or armour are created equipped.</param>
public sealed record DdbPreviewRequest(Guid Token, string RulesFamily, Guid? CampaignId, IReadOnlyList<Resolution>? Resolutions, IReadOnlyList<NumberChoice>? NumberChoices, bool IncludePlayState, IReadOnlyList<ChoiceSelection>? Answers = null, bool EquipMatched = false);

/// <param name="Confirm">Must be true: creating the character is the one write of the import.</param>
public sealed record DdbApplyRequest(Guid Token, string RulesFamily, Guid? CampaignId, IReadOnlyList<Resolution>? Resolutions, IReadOnlyList<NumberChoice>? NumberChoices, bool IncludePlayState, IReadOnlyList<ChoiceSelection>? Answers = null, bool Confirm = false, bool EquipMatched = false);

/// <param name="GapNotes">Notes stored: one per unmatched, unplaced or unreadable item, and one per noted difference.</param>
/// <param name="GapNotesNotStored">Unmatched items past the per-character note limit, counted but not stored.</param>
public sealed record DdbApplyResult(Guid CharacterId, int Overrides, int GapNotes, int GapNotesNotStored, DdbReport Report);

/// <param name="Name">The character's name as read (shown to the user only, never logged).</param>
/// <param name="ClassText">The classes as read, "Name level (subclass)" joined with " / ", or empty when unreadable.</param>
public sealed record DdbSummary(string Name, string ClassText, int Features, int Spells, int Items);

public sealed record DdbReadResult(Guid Token, string Layout, string? SuggestedFamily, DdbSummary Summary);
