using System.Text.RegularExpressions;
using TomeStack.RulesCore;

namespace TomeStack.AppService.CharacterImport;

/// <summary>
/// Item 15 (D35): a class the sheet names that TomeStack does not have, offered as a minimal homebrew class for this
/// import. <paramref name="HitDie"/> is the die read from the sheet's hit dice total (null when it cannot be read);
/// <paramref name="Note"/> is null when the class can be created, else a code: <c>stub.hit-die-missing</c>,
/// <c>stub.hit-die-unreadable</c>, or <c>stub.one-per-import</c> (R100: another row already creates one). The name is
/// the sheet's text, shown to the user only.
/// </summary>
public sealed record StubOffer(string RowId, string Name, int? HitDie, string? Note)
{
    public override string ToString() => $"StubOffer {{ HitDie = {HitDie}, Note = {Note} }}";
}

/// <summary>
/// The stub class in full, as the user reviews it (D35): its name, rules family, hit die, the saving throws it grants
/// (ability keys, <c>str</c> to <c>cha</c>) and the source it is created in. Nothing else is in it.
/// </summary>
public sealed record StubClassReview(string RowId, string Name, string RulesFamily, int HitDie, IReadOnlyList<string> Saves, string SourceTitle)
{
    public override string ToString() => $"StubClassReview {{ HitDie = {HitDie}, Saves = {Saves.Count} }}";
}

/// <summary>What <c>ddb.apply</c> writes for a stub: the draft class and its (import-derived) source.</summary>
internal sealed record StubWrite(ContentRevision Draft, SourceRecord Source);

/// <summary>
/// Everything <c>ddb.apply</c> writes besides the character (D35, item 16): the stub class, the one import-derived source
/// both live in (stored only when one of them is written), and the reference drafts.
/// </summary>
internal sealed record ImportWrites(StubWrite? Stub, SourceRecord Source, IReadOnlyList<ContentRevision> References);

/// <summary>
/// The planner's stub request: which row the user asked to create (null for none), the rules family and the source the
/// class would live in, and the callback that lets the calculation see the in-memory draft (<see cref="ImportCatalog"/>).
/// </summary>
internal sealed record StubRequest(string? RowId, string Family, SourceRecord Source, Action<ContentRevision> Created, bool? AllowedInCampaign = null);

/// <summary>The stub class: its source, its content, and the hit die rule. Pure; nothing here stores anything.</summary>
internal static partial class StubClasses
{
    /// <summary>The one source every stub class lives in (import-derived: it is never shared).</summary>
    public const string SourceTitle = "Characters imported from sheets";

    public const string NoteMissing = "stub.hit-die-missing";
    public const string NoteUnreadable = "stub.hit-die-unreadable";
    public const string NoteOnePerImport = "stub.one-per-import";

    /// <summary>The report line when the stub is not the sheet's first class (R101).</summary>
    public const string SavesLine = "Saving throws come from the first class; the created class has none.";

    /// <summary>The report line when the hit dice total is there but cannot be read (names the box, never its text).</summary>
    public const string HitDiceLine = "The hit dice total could not be read, so no class can be created for a class TomeStack does not have.";

    private const string Summary = "Created from an imported character sheet: a hit die and saving throws only. A draft until it is published.";

    /// <summary>One group of dice: a count and a size ("3d10"), with the text between groups checked separately.</summary>
    [GeneratedRegex(@"(?<count>[0-9]{1,2})\s*[dD]\s*(?<die>[0-9]{1,2})(?![0-9])", RegexOptions.None, 100)]
    private static partial Regex DiceGroup();

    /// <summary>
    /// The stub's hit die from the sheet's hit dice total. The groups are read ("3d10 + 2d8", or separated by commas or
    /// spaces; groups of one die are added), then each matched class takes its levels of its own die out. The die is what is
    /// left only when exactly one group is left and its count equals <paramref name="unmatchedLevels"/> (the levels of the
    /// classes the sheet names that TomeStack has not: the stub's own when it is the only one). Anything else is
    /// unreadable: unparseable text, a die that is not d6 to d12, a matched class with no known die, counts that do not add
    /// up, two groups left. Never a guess, and never an exception.
    /// </summary>
    /// <param name="matched">Each matched class's levels and its die (0 when its revision has none).</param>
    public static (int? Die, string? Note) ReadHitDie(string? text, IReadOnlyList<(int Level, int Die)> matched, int unmatchedLevels)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, NoteMissing);
        try
        {
            var groups = new Dictionary<int, int>();
            var rest = DiceGroup().Replace(text, "");
            if (rest.Any(c => !(char.IsWhiteSpace(c) || c is ',' or '+' or ';')))
                return (null, NoteUnreadable);
            foreach (Match match in DiceGroup().Matches(text))
            {
                var count = int.Parse(match.Groups["count"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var die = int.Parse(match.Groups["die"].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (!HitDieEffect.AllowedDice.Contains(die) || count is < 1 or > Character.MaxLevel)
                    return (null, NoteUnreadable);
                groups[die] = groups.GetValueOrDefault(die) + count;
            }
            if (groups.Count == 0)
                return (null, NoteUnreadable);
            foreach (var (level, die) in matched)
            {
                if (!groups.TryGetValue(die, out var have) || have < level)
                    return (null, NoteUnreadable);
                groups[die] = have - level;
            }
            var left = groups.Where(g => g.Value > 0).ToList();
            return left is [var only] && only.Value == unmatchedLevels ? (only.Key, null) : (null, NoteUnreadable);
        }
        catch (RegexMatchTimeoutException)
        {
            return (null, NoteUnreadable);
        }
    }

    /// <summary>The stub as a draft: the hit die and one proficiency grant per save, nothing else (no features, spells or proficiencies).</summary>
    public static ContentRevision Build(string name, string family, Guid sourceId, int hitDie, IReadOnlyList<Ability> saves) => new()
    {
        ContentId = Guid.NewGuid(),
        RevisionId = Guid.NewGuid(),
        Kind = ContentKind.Class,
        Name = name,
        RulesFamilies = [family],
        Provenance = new(sourceId),
        Status = RevisionStatus.Draft,
        Summary = Summary,
        Effects =
        [
            new HitDieEffect { Id = "hit-die", Die = hitDie },
            .. saves.Select(a => new GrantEffect { Id = $"save-{FieldIds.Key(a)}", Grant = GrantKind.Proficiency, Target = FieldIds.Save(a) }),
        ],
    };

    /// <summary>The source a stub lives in, as a new in-memory record (the apply stores it).</summary>
    public static SourceRecord NewSource(string family, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Title = SourceTitle,
        Publisher = "Personal homebrew",
        RulesFamilies = [family],
        EditionVersion = "homebrew",
        License = "Personal homebrew",
        Redistributable = false,
        ImportedAt = now,
        Origin = SourceOrigin.Local,
        ImportDerived = true,
    };
}

/// <summary>
/// The catalog an import calculates through: the store, until the planner creates the stub class, and from then on the
/// store with that one draft counting as published, in memory (a <see cref="DraftOverlayCatalog"/> that also knows the
/// draft's not-yet-stored source). It feeds the planner, every calculation and every choice check, so the preview sees the
/// class exactly as a published one. Nothing is written.
/// </summary>
internal sealed class ImportCatalog : IContentCatalog
{
    private readonly IContentCatalog _inner;
    private IContentCatalog _current;

    private readonly bool _publishStub;

    /// <param name="publishStub">
    /// R129: the stub counts as published only when apply will publish it. Otherwise the calculation reads the store alone,
    /// exactly as the saved character will (a draft gives it nothing), so the preview describes what apply saves.
    /// </param>
    public ImportCatalog(IContentCatalog inner, bool publishStub)
    {
        _inner = inner;
        _current = inner;
        _publishStub = publishStub;
    }

    public void Use(ContentRevision draft, SourceRecord source)
    {
        if (_publishStub)
            _current = new DraftOverlayCatalog(_inner, draft, source);
    }

    public ContentRevision? FindRevision(ContentReference reference) => _current.FindRevision(reference);

    public SourceRecord? FindSource(Guid sourceId) => _current.FindSource(sourceId);

    public IEnumerable<ContentRevision> ChoiceExtensions(Guid contentId, string choiceId) => _current.ChoiceExtensions(contentId, choiceId);

    public IEnumerable<ContentRevision> RevisionsOf(Guid contentId) => _current.RevisionsOf(contentId);
}
