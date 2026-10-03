using System.Text.Json;
using System.Text.RegularExpressions;
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
    IReadOnlyList<string>? MarkValues = null);

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
    ];

    /// <summary>The hit die sizes of <c>play.hitDiceSpent.d&lt;size&gt;</c>.</summary>
    public static IReadOnlyList<int> HitDice { get; } = [6, 8, 10, 12];

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
        "play.currentHitPoints", "play.temporaryHitPoints", "play.deathSuccesses", "play.deathFailures", "play.inspiration",
        .. HitDice.Select(d => $"play.hitDiceSpent.d{d}"),
        .. Enumerable.Range(1, 9).Select(l => $"play.spellSlotsSpent.{l}"),
    ], StringComparer.Ordinal);
}

/// <summary>The layout maps shipped with the app (embedded <c>CharacterImport/Layouts/ddb-*.v1.json</c>), checked once at load.</summary>
public static class LayoutMaps
{
    private const string Prefix = "TomeStack.CharacterImport.Layouts.";

    private static readonly Lazy<IReadOnlyList<LayoutMap>> Loaded = new(Load);

    public static IReadOnlyList<LayoutMap> All => Loaded.Value;

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
