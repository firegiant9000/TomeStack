using System.Text.Json;
using System.Text.RegularExpressions;
using TomeStack.ImportWorker.Forms;
using TomeStack.RulesCore;

namespace TomeStack.AppService.CharacterImport;

/// <summary>
/// How a field of a layout is read. <paramref name="Pattern"/> is optional: for most semantics it is matched against the
/// whole value and its <c>value</c> group (or the whole match) is kept; for <c>classLevels</c> it reads each class part and
/// must have <c>name</c> and <c>level</c> groups (and may have <c>sub</c>). Patterns must be anchored (<c>^…$</c>), run
/// without backtracking and with a 100 ms timeout; a value that does not match is <see cref="ReadStatus.Unreadable"/>.
/// </summary>
public sealed record FieldRule(string Semantic, string? Pattern = null);

/// <summary>A field that holds a list: its value is split on <paramref name="Separator"/> (literal; <c>\n</c> is any line break).</summary>
/// <param name="ItemPattern">When set, only items it matches are kept, as its <c>value</c> group (S0: a bullet line names a feature; description lines are not items).</param>
/// <param name="SectionHeading">A line it matches starts a section named by its <c>section</c> group; the line itself is no item.</param>
/// <param name="FeatsSection">For <c>features</c>: items in a section whose name it matches are feats, not features (S0: one text holds both).</param>
public sealed record SplitRule(string Semantic, string Separator, string? ItemPattern = null, string? SectionHeading = null, string? FeatsSection = null);

/// <summary>
/// A layout map (<c>features/ddb-pdf-import.md</c> "Layout maps are data, not code"): full field names to semantic
/// fields. A field name may hold one <c>{n}</c>, a row number, when its semantic holds <c>[n]</c> (<c>spells[n].name</c>).
/// A layout is recognised when every <paramref name="Required"/> name is present. The map holds field names only,
/// never text from a sheet. <paramref name="Unverified"/> marks names not yet confirmed against a real export (S0).
/// <paramref name="MarkValues"/> are the texts that mark a text field used as a checkbox (S0: the 2014 export writes P for
/// proficient and E for expertise), compared trimmed and ignoring case; any other text is unreadable, never guessed.
/// <paramref name="ClipFields"/> (item 14, R78) are free-text field names, each also in <paramref name="Fields"/>, whose value is
/// cut to the worker's per-value limit instead of refusing the read; the worker is given the union over all layouts.
/// <paramref name="DisplayName"/> names the layout in the report ("D&amp;D Beyond 2014", "the fillable 2014 sheet");
/// <paramref name="ImportedOverrideReason"/> and <paramref name="ImportFieldNoteText"/> are the texts a kept or noted number gets; <paramref name="ImportGapNoteText"/> is the text of every import gap note (an item with no match or no place).
/// </summary>
public sealed record LayoutMap(
    string Id,
    int Version,
    string? SuggestedFamily,
    IReadOnlyList<string> Required,
    IReadOnlyDictionary<string, FieldRule> Fields,
    string CheckboxOnState,
    IReadOnlyList<SplitRule> Splits,
    bool Unverified = false,
    IReadOnlyList<string>? MarkValues = null,
    IReadOnlyList<string>? ClipFields = null,
    string DisplayName = "",
    string ImportedOverrideReason = "Imported from D&D Beyond",
    string ImportFieldNoteText = "The D&D Beyond sheet showed a different number here when this character was imported.",
    string ImportGapNoteText = "Not brought over from the D&D Beyond sheet: TomeStack had no match or no place for it.");

/// <summary>The semantic fields a map may name.</summary>
public static class DdbSemantics
{
    /// <summary>Semantics that hold a list, read from one field with a <see cref="SplitRule"/>, or from rows.</summary>
    public static IReadOnlySet<string> Lists { get; } = new HashSet<string>(StringComparer.Ordinal) { "classLevels", "feats", "features", "spells", "equipment" };

    /// <summary>The calculated fields a sheet number may be compared with (<c>features/ddb-pdf-import.md</c> step 4).</summary>
    public static IReadOnlyList<string> NumberIds { get; } =
    [
        FieldIds.ProficiencyBonus, FieldIds.ArmorClass, FieldIds.Initiative, FieldIds.HitPoints, FieldIds.SpellAttack, FieldIds.SpellSaveDc,
        .. Enum.GetValues<Ability>().Select(FieldIds.Save),
        .. CharacterCalculator.Skills.Select(s => FieldIds.Skill(s.Key)),
        .. Enumerable.Range(1, 9).Select(FieldIds.SpellSlots),
        // D24: calculated since 2026-10-06 (speed has a rules-policy base; a kept sheet number is a character override, never a content target).
        FieldIds.Speed, FieldIds.Passive("perception"),
    ];

    /// <summary>The coins of <c>currency.&lt;coin&gt;</c> (D21), in <see cref="Currency"/>'s order.</summary>
    public static IReadOnlyList<string> Coins { get; } = ["cp", "sp", "ep", "gp", "pp"];

    /// <summary>The hit die sizes of <c>play.hitDiceSpent.d&lt;size&gt;</c>.</summary>
    public static IReadOnlyList<int> HitDice { get; } = [6, 8, 10, 12];

    /// <summary>The weapon rows of the fillable sheet.</summary>
    public const int WeaponRows = 3;

    /// <summary>The boxes per death-save row.</summary>
    public const int DeathBoxes = 3;

    /// <summary>The short details a map may fill (<c>details.alignment</c>, <c>details.appearance.age</c>, …).</summary>
    public static IReadOnlyList<string> Details { get; } =
    [
        "details.alignment", "details.appearance.age", "details.appearance.height", "details.appearance.weight",
        "details.appearance.eyes", "details.appearance.skin", "details.appearance.hair",
    ];

    /// <summary>The prose fields (key to the title a note and a report line use), in the order their notes are written (R79).</summary>
    public static IReadOnlyDictionary<string, string> Prose { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["personalityTraits"] = "Personality traits",
        ["ideals"] = "Ideals",
        ["bonds"] = "Bonds",
        ["flaws"] = "Flaws",
        ["backstory"] = "Backstory",
        ["featuresAndTraits"] = "Features and traits",
        ["featsAndTraits"] = "Feats and traits",
        ["treasure"] = "Treasure",
    };

    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(
    [
        "name", "classLevels", "species", "background", "feats", "features", "spells", "equipment",
        .. Enum.GetValues<Ability>().Select(a => $"abilities.{FieldIds.Key(a)}"),
        .. Enum.GetValues<Ability>().Select(a => $"saves.{FieldIds.Key(a)}.proficient"),
        .. CharacterCalculator.Skills.Select(s => $"skills.{s.Key}.proficient"),
        // S0: a list may also be spread over numbered text fields, read in order.
        "features[n]", "feats[n]",
        "spells[n].name", "spells[n].prepared", "equipment[n].name", "equipment[n].quantity", "equipment[n].equipped",
        .. NumberIds.Select(id => $"numbers.{id}"),
        .. Coins.Select(c => $"currency.{c}"),
        "play.currentHitPoints", "play.temporaryHitPoints", "play.deathSuccesses", "play.deathFailures", "play.inspiration",
        .. HitDice.Select(d => $"play.hitDiceSpent.d{d}"),
        .. Enumerable.Range(1, 9).Select(l => $"play.spellSlotsSpent.{l}"),
        // Item 14: the fillable 2014 sheet. The modifier box beside each score (the swap guard reads both), the short details,
        // the prose that goes to session notes, the free-text lists, three weapon rows, death-save boxes and hit dice.
        .. Enum.GetValues<Ability>().Select(a => $"abilityMods.{FieldIds.Key(a)}"),
        .. Details,
        .. Prose.Keys.Select(k => $"prose.{k}"),
        "languagesAndTools", "equipmentText",
        .. Enumerable.Range(1, WeaponRows).SelectMany(r => new[] { $"weapons.{r}.name", $"weapons.{r}.toHit", $"weapons.{r}.damage" }),
        .. Enumerable.Range(1, DeathBoxes).SelectMany(b => new[] { $"play.deathSuccesses.{b}", $"play.deathFailures.{b}" }),
        "play.hitDice.total", "play.hitDice.remaining",
    ], StringComparer.Ordinal);

    /// <summary>The names a report line uses for the other free-text fields.</summary>
    public static IReadOnlyDictionary<string, string> TextTitles { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["languagesAndTools"] = "Proficiencies and languages",
        ["equipmentText"] = "Equipment",
        ["details.alignment"] = "Alignment",
        ["details.appearance.age"] = "Age",
        ["details.appearance.height"] = "Height",
        ["details.appearance.weight"] = "Weight",
        ["details.appearance.eyes"] = "Eyes",
        ["details.appearance.skin"] = "Skin",
        ["details.appearance.hair"] = "Hair",
    };
}

/// <summary>The layout maps shipped with the app (embedded <c>CharacterImport/Layouts/ddb-*.v1.json</c>), checked once at load.</summary>
public static class LayoutMaps
{
    private const string Prefix = "TomeStack.CharacterImport.Layouts.";

    private static readonly Lazy<IReadOnlyList<LayoutMap>> Loaded = new(Load);

    public static IReadOnlyList<LayoutMap> All => Loaded.Value;

    /// <summary>
    /// What the worker is told to read under (R78): the default sheet limits plus the union of every layout's clip names, since the
    /// worker reads the fields before any layout is recognised.
    /// </summary>
    public static FormLimits FormLimits => Limits.Value;

    private static readonly Lazy<FormLimits> Limits = new(() => new FormLimits { ClipFields = [.. All.SelectMany(m => m.ClipFields ?? []).Distinct(StringComparer.Ordinal)] });

    /// <summary>The layout's name in a report: its display name, or its id when it has none.</summary>
    public static string NameOf(string layoutId) =>
        Find(layoutId) is { } map && map.DisplayName.Length > 0 ? map.DisplayName : layoutId;

    /// <summary>The id of the text importer's layout (item 19a).</summary>
    public const string TextId = "text";

    /// <summary>
    /// The text importer's layout (item 19a): not a map of form fields (it is not in <see cref="All"/>, so no PDF is ever recognised
    /// as it), only the neutral texts a kept number or an import note gets. Nothing in it says where the text came from.
    /// </summary>
    public static LayoutMap Text { get; } = new(
        TextId, 1, null, [], new Dictionary<string, FieldRule>(), "", [],
        DisplayName: "pasted or file text",
        ImportedOverrideReason: "Imported from text",
        ImportFieldNoteText: "Imported from the character text: it showed a different number here when this character was imported.",
        ImportGapNoteText: "Not brought over from the text: TomeStack had no match or no place for it.");

    /// <summary>A shipped map by id, or the text layout.</summary>
    public static LayoutMap? Find(string layoutId) => All.FirstOrDefault(m => m.Id == layoutId) ?? (layoutId == TextId ? Text : null);

    /// <summary>Compiles a map's pattern as the parser runs it.</summary>
    internal static Regex Compile(string pattern) =>
        new(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Everything wrong with a map, as messages naming the map's own field names (never a sheet's values).</summary>
    public static IReadOnlyList<string> Problems(LayoutMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(map.Id) || map.Version < 1)
            problems.Add("the map needs an id and a version of 1 or more");
        if (map.SuggestedFamily is { } family && family is not (RulesFamilies.Srd51 or RulesFamilies.Srd521))
            problems.Add($"suggested family {family} is not a rules family");
        if (map.Required is not { Count: > 0 } || map.Required.Any(r => string.IsNullOrEmpty(r) || r.Contains("{n}", StringComparison.Ordinal)))
            problems.Add("required names must be plain field names, at least one");
        if (string.IsNullOrEmpty(map.CheckboxOnState))
            problems.Add("the checkbox on-state is empty");
        // The parser reads both, so a map without them would throw on a sheet instead of failing here, at load.
        if (map.Fields is null)
            problems.Add("the map has no fields");
        if (map.Splits is null || map.Splits.Any(s => s is null))
            problems.Add("the map's splits are missing or hold an empty entry");
        foreach (var clip in map.ClipFields ?? [])
        {
            if (map.Fields is null || !map.Fields.ContainsKey(clip))
                problems.Add($"{clip}: a clip field must be a field of the map");
        }
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, rule) in map.Fields ?? new Dictionary<string, FieldRule>())
        {
            if (rule?.Semantic is not { } semantic || !DdbSemantics.Known.Contains(semantic))
            {
                problems.Add($"{name}: unknown semantic {rule?.Semantic}");
                continue;
            }
            // The parser keeps whichever field the worker returns first, so a semantic named twice reads by chance.
            if (!named.Add(semantic))
                problems.Add($"{name}: the semantic {semantic} is named by more than one field");
            if (rule.Pattern is { } anchoring && !(anchoring.StartsWith('^') && anchoring.EndsWith('$')))
                problems.Add($"{name}: the pattern must be anchored (^…$), so a partial match is not read as the value");
            var rows = name.Split("{n}").Length - 1;
            if (rows > 1 || (rows == 1) != semantic.Contains("[n]", StringComparison.Ordinal))
                problems.Add($"{name}: a row index {{n}} must be in both the field name and the semantic, once");
            if (rule.Pattern is { } pattern)
            {
                try
                {
                    var regex = Compile(pattern);
                    if (semantic == "classLevels" && !(regex.GetGroupNames().Contains("name") && regex.GetGroupNames().Contains("level")))
                        problems.Add($"{name}: a classLevels pattern needs the groups name and level");
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                {
                    problems.Add($"{name}: the pattern does not compile without backtracking");
                }
            }
        }
        foreach (var split in (map.Splits ?? []).Where(s => s is not null))
        {
            if (!DdbSemantics.Lists.Contains(split.Semantic) || string.IsNullOrEmpty(split.Separator))
                problems.Add($"split {split.Semantic}: only a list semantic can be split, on a non-empty separator");
            foreach (var (pattern, group) in new[] { (split.ItemPattern, "value"), (split.SectionHeading, "section"), (split.FeatsSection, null) })
            {
                if (pattern is null)
                    continue;
                try
                {
                    if (group is not null && !Compile(pattern).GetGroupNames().Contains(group))
                        problems.Add($"split {split.Semantic}: the pattern needs the group {group}");
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                {
                    problems.Add($"split {split.Semantic}: a pattern does not compile without backtracking");
                }
            }
        }
        return problems;
    }

    private static List<LayoutMap> Load()
    {
        var assembly = typeof(LayoutMaps).Assembly;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var maps = new List<LayoutMap>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var map = JsonSerializer.Deserialize<LayoutMap>(stream, options) ?? throw new InvalidDataException($"The layout map {resource} is empty.");
            if (Problems(map) is { Count: > 0 } problems)
                throw new InvalidDataException($"The layout map {map.Id} is not valid: {string.Join("; ", problems)}");
            maps.Add(map);
        }
        return maps;
    }
}
