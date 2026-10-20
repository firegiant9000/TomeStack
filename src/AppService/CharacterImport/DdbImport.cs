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

    // The worker reads before any layout is recognised, so it is told the union of every layout's free-text field names (R78).
    private IFormReader FormReader => _formReader ??= new WorkerFormReader(Path.Combine(AppContext.BaseDirectory, WorkerFileName), form: LayoutMaps.FormLimits);

    private ImportSessions? _ddbSessions;

    internal ImportSessions DdbSessions => _ddbSessions ??= new ImportSessions(_time);

    private string DdbTempFolder => Path.Combine(DataDirectory, "tmp");

    /// <summary><c>ddb.read</c>: reads the sheet at <paramref name="path"/> (a dialog's choice or the temporary copy, never a UI path).</summary>
    public DdbReadResult ReadDdbSheet(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        LocalDrivePath.Require(path, ".pdf"); // R116: the one place a dialog path reaches the worker; no caller can skip it
        return ReadDdbSheetAt(path);
    }

    /// <summary>The read itself, for a path the rule has passed or the app made (the <c>ddb.readData</c> copy under its own tmp folder).</summary>
    private DdbReadResult ReadDdbSheetAt(string path)
    {
        IReadOnlyList<FormField> fields;
        try
        {
            // No cancellation reaches this synchronous command (the dispatcher passes no token), and the worker's own limits bound the
            // wait (30 s), so CancellationToken.None and a blocking wait are safe here: the read does not capture a context.
            fields = FormReader.ReadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (ExtractionException ex) when (ex.Code == NoFormCode)
        {
            return ReadDdbPdfText(path); // item 19b (R115): a PDF without form fields is read as text
        }
        catch (ExtractionException ex)
        {
            throw Failed(ex); // the reader's messages never quote the document
        }

        var map = DdbParser.Recognise(fields)
            ?? throw Refused("ddb.layout-unknown", $"This PDF's {fields.Count} form field{(fields.Count == 1 ? "" : "s")} do not match a known character sheet layout.");
        return Hold(DdbParser.Parse(map, fields));
    }

    /// <summary>
    /// <c>ddb.readText</c> (item 19a, D37, ADR-017): reads pasted or file text into the same sheet the PDF importer makes and holds
    /// it under a token; everything after (preview, apply, discard) is unchanged. A text over a cap is refused with a code and counts.
    /// Error messages carry counts, never the text.
    /// </summary>
    public DdbReadResult ReadDdbText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parsed = TextSheetParser.Parse(text);
        if (parsed.Refusal is { } refusal)
            throw Refused(refusal.Code, refusal.Message);
        return Hold(parsed.Sheet!);
    }

    /// <summary>The form reader's code for a PDF with no form (the worker's <c>AcroFormReader</c>).</summary>
    private const string NoFormCode = "ddb.no-form-fields";

    /// <summary>A PDF's text is read from its first twenty pages; the rest are counted, not read.</summary>
    public const int MaxTextPdfPages = 20;

    /// <summary>Fewer letters, digits and marks than this on all the pages is not a sheet (a scan with no OCR, a blank page).</summary>
    public const int MinTextPdfChars = 10;

    /// <summary>What the summary and the report call a sheet whose text came from a PDF.</summary>
    public const string PdfTextLayoutName = "text from the PDF";

    private IDocumentExtractor? _sheetExtractor;

    // R114: the extract request, called here with the sheet limits (20 MB, 50 pages, 30 s) and kept apart from the import jobs'
    // book limits. Nothing is stored; the text is parsed in memory and held under a token like any other read.
    private IDocumentExtractor SheetExtractor => _sheetExtractor ??= new ImportWorker.Extraction.WorkerProcessExtractor(Path.Combine(AppContext.BaseDirectory, WorkerFileName), WorkerFormReader.DefaultLimits);

    /// <summary>
    /// Item 19b (D37, ADR-013): the page text of a PDF that has no form fields goes through <see cref="TextSheetParser"/>, so the user
    /// picks a PDF the same way whether or not it has a form. The same caps and refusals apply as to pasted text; a page the worker
    /// cut short is refused, not read in part. No page text reaches a message, a code or a log.
    /// </summary>
    private DdbReadResult ReadDdbPdfText(string path)
    {
        var pages = new List<string>();
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var pageCount = 0;
        var unreadable = 0;
        var blank = 0;
        try
        {
            ReadPages().GetAwaiter().GetResult();
        }
        catch (ExtractionException ex)
        {
            throw Failed(ex);
        }

        async Task ReadPages()
        {
            await foreach (var item in SheetExtractor.ExtractAsync(path, new PageScope(1, MaxTextPdfPages), CancellationToken.None).ConfigureAwait(false))
            {
                switch (item)
                {
                    case DocumentOpened opened:
                        pageCount = opened.PageCount;
                        break;
                    case ExtractedPage { Error: not null }:
                        unreadable++;
                        break;
                    case ExtractedPage page:
                        pages.Add(page.Text);
                        warnings.UnionWith(page.Warnings ?? []);
                        if (page.Text.All(char.IsWhiteSpace) || page.Warnings?.Any(w => w is "page.no-text" or "ocr.unavailable") == true)
                            blank++;
                        break;
                }
            }
        }

        var read = pages.Count + unreadable;
        if (read > 0 && unreadable == read)
            throw Refused("pdf.unreadable", $"None of the {Count(read)} page{(read == 1 ? "" : "s")} read could be read. The PDF may be damaged. Nothing was read.");
        if (warnings.Contains("page.text-truncated") || warnings.Contains("page.blocks-truncated"))
            throw Refused("ddb.text-too-large", $"A page of the PDF holds more text than can be read (the limit is {Count(WorkerFormReader.DefaultLimits.MaxTextPerPage)} characters a page). Nothing was read.");
        var text = string.Join("\n\n", pages);
        var readable = text.Count(c => !char.IsWhiteSpace(c));
        if (readable < MinTextPdfChars)
        {
            throw Refused("ddb.pdf-no-text", $"The PDF has no text that can be read ({Count(readable)} character{(readable == 1 ? "" : "s")} on {Count(read)} page{(read == 1 ? "" : "s")}). "
                + "If it is a scan, its text could not be recognised: save the sheet as a PDF that has text, or paste its text instead. Nothing was read.");
        }

        var parsed = TextSheetParser.Parse(text);
        if (parsed.Refusal is { } refusal)
            throw Refused(refusal.Code, refusal.Message);
        var sheet = parsed.Sheet!;
        var notes = new List<string>(sheet.Lines ?? []);
        var skipped = Math.Max(0, pageCount - MaxTextPdfPages);
        if (skipped > 0)
            notes.Add($"{Count(skipped)} page{(skipped == 1 ? "" : "s")} after the first {MaxTextPdfPages} {(skipped == 1 ? "was" : "were")} not read.");
        // Pages that read without error but held no text (blank, or a scan with no OCR): counted, never named.
        if (blank > 0)
            notes.Add($"{Count(blank)} page{(blank == 1 ? "" : "s")} had no readable text and {(blank == 1 ? "was" : "were")} not read.");
        if (unreadable > 0)
            notes.Add($"{Count(unreadable)} page{(unreadable == 1 ? "" : "s")} could not be read.");
        return Hold(sheet with { LayoutName = PdfTextLayoutName, Lines = notes.Count > 0 ? notes : sheet.Lines });
    }

    private static string Count(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The codes the app itself raises when the worker process fails (timeout, crash, a message over its limit): safe to show.</summary>
    private static readonly HashSet<string> AppRaisedWorkerCodes = new(StringComparer.Ordinal)
    {
        "worker.missing", "worker.crashed", "worker.protocol", "worker.failed", "worker.limits", "worker.timeout", "worker.page-timeout", "worker.message-too-large", "worker.memory",
    };

    /// <summary>
    /// The child is untrusted, so neither its code nor its message is forwarded as sent. A code the reader can raise keeps that code
    /// with the app's own message (the allowlist the form reader uses); any other is <c>ddb.read-failed</c>, which names the code only
    /// when the app itself raises it. No child text is interpolated anywhere.
    /// </summary>
    private static AppValidationException Failed(ExtractionException ex)
    {
        if (WorkerFormReader.KnownMessage(ex.Code, WorkerFormReader.DefaultLimits, LayoutMaps.FormLimits) is { } known)
            return Refused(ex.Code, known);
        return AppRaisedWorkerCodes.Contains(ex.Code)
            ? Refused("ddb.read-failed", $"The sheet could not be read ({ex.Code}). Nothing was kept.")
            : Refused("ddb.read-failed", "The sheet could not be read. Nothing was kept.");
    }

    /// <summary>The layout in words: "text from the PDF" for a PDF's text, else the layout's own name.</summary>
    private static string LayoutNameOf(DdbSheet sheet) => sheet.LayoutName ?? LayoutMaps.NameOf(sheet.Layout);

    private DdbReadResult Hold(DdbSheet sheet)
    {
        var token = DdbSessions.Add(sheet);
        var classes = sheet.Classes.Status == ReadStatus.Ok
            ? string.Join(" / ", sheet.Classes.Value!.Select(c => $"{c.Name} {c.Level}{(c.Subclass is null ? "" : $" ({c.Subclass})")}"))
            : "";
        // A text sheet's equipment is one text (its lines become custom items), so the card counts the lines.
        var items = sheet.Items.Count + (sheet.Layout == LayoutMaps.TextId && sheet.Texts?.Equipment is { Status: ReadStatus.Ok } equipment ? equipment.Value!.Split('\n').Count(l => l.Length > 0) : 0);
        return new DdbReadResult(token, sheet.Layout, sheet.SuggestedFamily,
            new DdbSummary(sheet.Name.Value ?? "", classes, sheet.Features.Count, sheet.Spells.Count, items, sheet.NotRead?.Count ?? 0),
            LayoutNameOf(sheet));
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
            return ReadDdbSheetAt(path);
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
    private static readonly string[] AlwaysNotBroughtOver =
        ["notes", "attunement", "languages", "toolProficiencies", "senses", "appearance", "backstory", "playerName"];

    /// <summary>
    /// D16e amended (D21, D24): currency is brought over only when a coin was read and none was unreadable (a partly read
    /// purse is never zeroed silently: no coins come over and it is listed); speed and passive Perception are numbers now,
    /// compared when the layout maps them and the value reads, and listed otherwise (unmapped, blank or unreadable).
    /// </summary>
    private static IEnumerable<string> NotBroughtOver(DdbSheet sheet, ImportExtras extras)
    {
        if (!CurrencyBroughtOver(sheet))
            yield return "currency";
        if (!NumberRead(sheet, FieldIds.Speed))
            yield return "speed";
        if (!NumberRead(sheet, FieldIds.Passive("perception")))
            yield return "passivePerception";
        // Item 14: what the fillable sheet's free text brought over is not listed as left out.
        foreach (var code in AlwaysNotBroughtOver.Where(c => !extras.Brought.Contains(c)))
            yield return code;
    }

    private static LayoutMap? MapOf(DdbSheet sheet) => LayoutMaps.Find(sheet.Layout);

    private static bool NumberRead(DdbSheet sheet, string field) => sheet.Numbers.TryGetValue(field, out var read) && read.Status == ReadStatus.Ok;

    private static bool CurrencyBroughtOver(DdbSheet sheet) =>
        sheet.Currency.Values.Any(r => r.Status == ReadStatus.Ok) && sheet.Currency.Values.All(r => r.Status != ReadStatus.Unreadable);

    private static Currency CurrencyFrom(DdbSheet sheet)
    {
        if (!CurrencyBroughtOver(sheet))
            return new();
        int Coin(string key) => sheet.Currency.TryGetValue(key, out var read) && read.Status == ReadStatus.Ok ? read.Value : 0;
        return new(Coin("cp"), Coin("sp"), Coin("ep"), Coin("gp"), Coin("pp"));
    }

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

    /// <summary>The apply's rebuilt proposal, plus (item 15) the stub class and its source when the user chose to create one.</summary>
    internal DdbPreview ProposeDdbCharacter(DdbSheet sheet, DdbPreviewRequest request, Guid id) => ProposeDdb(sheet, request, id).Preview;

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
        // R129: the preview calculated for one publish choice; an apply for the other would save numbers the user never saw.
        if (request.PreviewedPublishClass is { } previewedPublish && previewedPublish != request.PublishClass)
            throw Refused("ddb.publish-changed", "The publish choice is not the one the summary was calculated for. Nothing was created; look at the summary again.");
        var (proposal, writes) = ProposeDdb(sheet,
            new DdbPreviewRequest(request.Token, request.RulesFamily, request.CampaignId, request.Resolutions, request.NumberChoices, request.IncludePlayState, request.Answers, request.EquipMatched, request.CreateClassRow, request.KeepNotRead, request.PublishClass),
            Guid.NewGuid());
        // Item 15: a class asked for that is not offered (its die cannot be read, another row already creates one, or it is
        // installed after all) is refused with a code, not guessed at. The message never names the class.
        var stub = writes.Stub;
        if (request.CreateClassRow is not null && stub is null)
            throw Refused("ddb.class-not-offered", "A class cannot be created for that line of the sheet. Choose it again in the matches step, or leave it out.");

        // Item 16: the publish tick belongs to the drafts the user was shown. Unless the drafts derived now are exactly those
        // (by row id, in any order), nothing is written and the token is kept. The refusal names no entry.
        if (request.PublishReferences)
        {
            var derived = (proposal.ReferenceDrafts ?? []).Select(d => d.RowId).ToHashSet(StringComparer.Ordinal);
            if (!derived.SetEquals(request.ExpectedReferences ?? []))
                throw Refused("ddb.references-changed", "The reference drafts are not the ones you were shown. Nothing was created; look at the summary again.");
        }

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
            // D35, item 16: the source, the stub draft, the reference drafts, the optional publishes, then the character, in this
            // one transaction: any refusal below rolls the source, the drafts, the publishes and the character back together.
            if (stub is not null || writes.References.Count > 0)
                _store.UpsertSource(writes.Source);
            if (stub is not null)
            {
                SaveDraft(stub.Draft);
                if (request.PublishClass)
                {
                    var published = PublishMany(new PublishManyRequest([stub.Draft.ContentId], [stub.Draft.RevisionId], Confirm: true)).Published.Single().Published;
                    character = character.ReplaceReference(stub.Draft.Reference, published);
                }
            }
            foreach (var reference in writes.References)
                SaveDraft(reference);
            if (request.PublishReferences && writes.References.Count > 0)
            {
                var published = PublishMany(new PublishManyRequest([.. writes.References.Select(r => r.ContentId)], [.. writes.References.Select(r => r.RevisionId)], Confirm: true)).Published;
                // R105: a published feat is linked as a pin (where a matched standalone feat goes); a feature has no place on the
                // character, so it stays reference content in the studio and its gap note stays the character's link to it.
                var pins = published.Where(p => writes.References.Any(r => r.Reference == p.Draft && r.Kind == ContentKind.Feat)).Select(p => p.Published);
                character = character with { Pins = [.. character.Pins, .. pins] };
            }
            SaveCharacter(character);
            // The few noted differences first, so the import notes (counted past the limit, not stored) never crowd them out.
            foreach (var field in noted)
                AddGapNote(new(character.Id, new(GapTargetKind.Field, FieldId: field), MapOf(sheet)?.ImportFieldNoteText ?? ImportFieldNoteText)); // refused: everything rolls back
            importNotes = AddImportGapNotes(character.Id, labels, MapOf(sheet)?.ImportGapNoteText);
        });
        DdbSessions.Take(request.Token);
        return new DdbApplyResult(character.Id, character.Overrides.Count, importNotes + noted.Count, labels.Count - importNotes, proposal.Report, stub?.Draft.ContentId,
            writes.References.Count > 0 ? [.. writes.References.Select(r => r.ContentId)] : null);
    }

    /// <summary>The source stub classes live in: the stored one (one for the data folder), else a new in-memory record the apply stores.</summary>
    private SourceRecord StubSource(string family)
    {
        var existing = _store.ListSources().FirstOrDefault(s => s.Title == StubClasses.SourceTitle && s.ImportDerived == true && s.Origin == SourceOrigin.Local);
        if (existing is null)
            return StubClasses.NewSource(family, _time.GetUtcNow());
        return existing.RulesFamilies.Contains(family) ? existing : existing with { RulesFamilies = [.. existing.RulesFamilies, family] };
    }

    /// <summary>
    /// Builds the proposal for <paramref name="sheet"/> (shared with <c>ddb.apply</c>, which saves it under <paramref name="id"/>).
    /// Item 15 (D35): the calculation, the planner's options and every choice check go through an overlay that holds the
    /// stub class as if it were published, with its not-yet-stored source; nothing is written. <c>Stub</c> is set when the
    /// user chose to create the class and the sheet settled its die.
    /// </summary>
    internal (DdbPreview Preview, ImportWrites Writes) ProposeDdb(DdbSheet sheet, DdbPreviewRequest request, Guid id)
    {
        var options = ListContent(request.RulesFamily, request.CampaignId).Where(o => o.Compatible && !o.Superseded).ToList();
        var catalog = new ImportCatalog(_store, request.PublishClass);
        static Character Levelled(Character c) => c.Classes.Count > 0 ? c with { Level = c.Classes.Sum(l => l.Level) } : c;
        CharacterSheet Calculate(Character c) => CharacterCalculator.Calculate(Levelled(c), catalog);
        var stubSource = StubSource(request.RulesFamily);

        var now = _time.GetUtcNow();
        var extras = ImportExtras.Build(sheet, DateOnly.FromDateTime(now.LocalDateTime), now, keepNotRead: request.KeepNotRead);
        var start = new Character
        {
            Id = id,
            Name = sheet.Name.Value is { } name && !string.IsNullOrWhiteSpace(name) ? name.Trim() : "Imported character",
            RulesFamily = request.RulesFamily,
            BaseAbilities = new(AbilitySolver.DefaultBase, AbilitySolver.DefaultBase, AbilitySolver.DefaultBase, AbilitySolver.DefaultBase, AbilitySolver.DefaultBase, AbilitySolver.DefaultBase),
            CampaignId = request.CampaignId,
            Currency = CurrencyFrom(sheet),
            Details = extras.Details,
            CustomItems = extras.Items,
            CustomAttacks = extras.Attacks,
            Notes = extras.Notes,
        };
        var planner = new ImportPlanner(start, options, request.Resolutions ?? [], Calculate,
            (c, source, choiceId, selected) => WithChoice(Levelled(c), source, choiceId, selected, catalog), catalog.FindRevision, request.EquipMatched,
            new StubRequest(request.CreateClassRow, request.RulesFamily, stubSource, draft => catalog.Use(draft, stubSource),
                request.CampaignId is { } campaignId ? _store.FindCampaign(campaignId)?.AllowedSources.Contains(stubSource.Id) : null));
        planner.Plan(sheet);
        var diagnostics = new List<Diagnostic>();
        var answered = planner.Character;
        var accepted = new HashSet<(ContentReference, string)>();
        foreach (var answer in (request.Answers ?? []).Where(a => a?.Source is not null))
        {
            try
            {
                answered = WithChoice(Levelled(answered), answer.Source, answer.ChoiceId, answer.Selected, catalog);
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
            Overrides = [.. comparison.Where(n => n.Differs && kept.Contains(n.Field)).Select(n => new FieldOverride(n.Field, n.Sheet!.Value, MapOf(sheet)?.ImportedOverrideReason ?? ImportedOverrideReason))],
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
        // Item 16: the unmatched feats and features that carry text become reference drafts in the same source as the stub.
        // R106: an entry the import-derived source already holds (any revision, draft or published) is not drafted again.
        // The source is read only for a sheet that has texts, so any other preview does no extra store work.
        var references = sheet.FeatureTexts is { Count: > 0 }
            ? ReferenceDrafts.Build(sheet, rows, request.RulesFamily, stubSource.Id, ContentBySource(stubSource.Id).Select(e => (e.Kind, Normalise.Name(e.Name))).ToHashSet())
            : ReferenceDraftSet.None;
        // A campaign that does not allow that source still gets the pin; the summary says so first (campaign.source-not-allowed).
        var referenceNote = references.Drafts.Any(d => d.Kind == ContentKind.Feat)
            && request.CampaignId is { } referenceCampaign && _store.FindCampaign(referenceCampaign)?.AllowedSources.Contains(stubSource.Id) == false
                ? "campaign.source-not-allowed"
                : null;
        int Count(MatchStatus status) => rows.Count(r => r.Status == status);
        var report = new DdbReport(
            Count(MatchStatus.Matched) - planner.ChosenByUser,
            planner.ChosenByUser,
            Count(MatchStatus.NotFound),
            Count(MatchStatus.NoPlace),
            Count(MatchStatus.Unreadable),
            Count(MatchStatus.LeftOut),
            [.. NotBroughtOver(sheet, extras), .. request.IncludePlayState ? Array.Empty<string>() : ["playState"]],
            ListCharacters().Any(c => string.Equals(c.Name, character.Name, StringComparison.OrdinalIgnoreCase)),
            sheet.SuggestedFamily is { } suggested && suggested != request.RulesFamily,
            LayoutNameOf(sheet),
            [.. (sheet.Lines ?? []).Where(l => request.IncludePlayState || l != DdbParser.DeathSavesLine), .. extras.Lines, .. planner.Lines, .. references.Lines]);
        var canApply = valid && sheet.Classes.Status == ReadStatus.Ok && character.Classes.Count > 0 && Count(MatchStatus.Choose) == 0;
        // A choice the user answered stays listed once it is full, so step 3 can still change the answer; a refused answer
        // does not keep a choice the sheet filled listed (its diagnostic says why).
        var openChoices = (final.Choices ?? []).Where(c => !c.Resolved || accepted.Contains((c.Source, c.ChoiceId))).ToList();
        var write = planner.StubRevision is { } draft ? new StubWrite(draft, stubSource) : null;
        return (new DdbPreview(character, rows, openChoices, comparison, plan, report, diagnostics, canApply, planner.StubOffers, planner.StubReview,
            references.Info.Count > 0 ? references.Info : null, referenceNote, sheet.NotRead is { Count: > 0 } notRead ? notRead : null), new ImportWrites(write, stubSource, references.Drafts));
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
/// <param name="LayoutName">The layout in words ("D&amp;D Beyond 2014", "the fillable 2014 sheet"), for the report and the step titles.</param>
/// <param name="Lines">Report lines from reading and importing the sheet's free text (a field cut short, an ability read from the other box, a weapon row's damage left empty): labels and counts only, never the sheet's text.</param>
public sealed record DdbReport(int Matched, int Chosen, int NotFound, int NoPlace, int Unreadable, int LeftOut, IReadOnlyList<string> NotBroughtOver, bool SameNameExists, bool FamilyMismatch, string LayoutName = "", IReadOnlyList<string>? Lines = null);

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
    bool CanApply,
    IReadOnlyList<StubOffer>? StubOffers = null,
    StubClassReview? Stub = null,
    IReadOnlyList<ReferenceDraftInfo>? ReferenceDrafts = null,
    string? ReferenceNote = null,
    IReadOnlyList<NotReadLine>? NotRead = null);

/// <param name="Answers">Open choices the user answered in step 3 (a 2024 background's ability scores), applied after the matches through the builder's check.</param>
/// <param name="EquipMatched">D16g: matched items whose revision is a weapon or armour are created equipped. Defaults to true, as the UI's checkbox does; a caller that omits it gets the guess.</param>
/// <param name="PublishClass">
/// R129: the created class will be published with the character. The calculation counts the class only then, so the comparison,
/// overrides and gap notes describe the character apply saves. Reference drafts carry no effects, so ticking them changes no number and is not part of the request.
/// </param>
public sealed record DdbPreviewRequest(Guid Token, string RulesFamily, Guid? CampaignId, IReadOnlyList<Resolution>? Resolutions, IReadOnlyList<NumberChoice>? NumberChoices, bool IncludePlayState, IReadOnlyList<ChoiceSelection>? Answers = null, bool EquipMatched = true, string? CreateClassRow = null, bool KeepNotRead = true, bool PublishClass = false);

/// <param name="Confirm">Must be true: creating the character is the one write of the import.</param>
/// <param name="ExpectedReferences">
/// Item 16, the consent token (D33's <c>expected</c> pattern): the row ids of the reference drafts the user was shown. Required
/// with <c>PublishReferences</c>; the apply refuses (<c>ddb.references-changed</c>) unless the drafts it derives are exactly these.
/// </param>
/// <param name="PreviewedPublishClass">R129: the <c>PublishClass</c> of the preview the user was shown. When given and different, the apply refuses (<c>ddb.publish-changed</c>).</param>
public sealed record DdbApplyRequest(Guid Token, string RulesFamily, Guid? CampaignId, IReadOnlyList<Resolution>? Resolutions, IReadOnlyList<NumberChoice>? NumberChoices, bool IncludePlayState, IReadOnlyList<ChoiceSelection>? Answers = null, bool Confirm = false, bool EquipMatched = true, string? CreateClassRow = null, bool PublishClass = false, bool PublishReferences = false, IReadOnlyList<string>? ExpectedReferences = null, bool KeepNotRead = true, bool? PreviewedPublishClass = null);

/// <param name="GapNotes">Notes stored: one per unmatched, unplaced or unreadable item, and one per noted difference.</param>
/// <param name="GapNotesNotStored">Unmatched items past the per-character note limit, counted but not stored.</param>
/// <param name="ReferenceDrafts">Item 16: the content ids of the reference drafts written, in the order the summary listed them; null when none.</param>
public sealed record DdbApplyResult(Guid CharacterId, int Overrides, int GapNotes, int GapNotesNotStored, DdbReport Report, Guid? CreatedClass = null, IReadOnlyList<Guid>? ReferenceDrafts = null);

/// <param name="Name">The character's name as read (shown to the user only, never logged).</param>
/// <param name="ClassText">The classes as read, "Name level (subclass)" joined with " / ", or empty when unreadable.</param>
/// <param name="NotRead">Item 19a: the lines of a text import that were not understood (0 for a PDF).</param>
public sealed record DdbSummary(string Name, string ClassText, int Features, int Spells, int Items, int NotRead = 0);

/// <param name="LayoutName">The layout in words, as <see cref="DdbReport.LayoutName"/>.</param>
public sealed record DdbReadResult(Guid Token, string Layout, string? SuggestedFamily, DdbSummary Summary, string LayoutName = "");
