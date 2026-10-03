using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using TomeStack.ImportWorker.Forms;
using TomeStack.RulesCore;

namespace TomeStack.AppService.CharacterImport;

/// <summary>
/// Character-sheet import S2 (<c>features/ddb-pdf-import.md</c> "Parsing text fields"): form fields to a
/// <see cref="DdbSheet"/> through a <see cref="LayoutMap"/>. Only fields the map names are read; the rest are dropped here.
/// The parser is total: every value it cannot read is <see cref="ReadStatus.Missing"/> or <see cref="ReadStatus.Unreadable"/>,
/// it never throws on field content, and it produces no message, so nothing it says can quote a value. Patterns are
/// anchored and time-limited (100 ms); a timeout is an unreadable value.
/// </summary>
public static partial class DdbParser
{
    /// <summary>
    /// The most items one list keeps (features, feats, a split spell or equipment list); the rest are one unreadable item.
    /// Above the 500 gap notes a character can hold, so the import notes' limit is still what users meet first.
    /// </summary>
    public const int MaxListItems = 1_000;

    /// <summary>The layout whose required field names are all present (the one with the most, if several), or null.</summary>
    public static LayoutMap? Recognise(IReadOnlyList<FormField> fields)
    {
        if (fields is null)
            return null;
        var names = new HashSet<string>(fields.Where(f => f?.Name is not null).Select(f => f.Name), StringComparer.Ordinal);
        return LayoutMaps.All.Where(m => m.Required.All(names.Contains)).OrderByDescending(m => m.Required.Count).FirstOrDefault();
    }

    public static DdbSheet Parse(LayoutMap map, IReadOnlyList<FormField> fields)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(fields);
        var compiled = CompiledMaps.GetValue(map, m => new CompiledMap(m));
        var values = new Dictionary<string, (FormField Field, FieldRule Rule)>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (field?.Name is { } name && compiled.Resolve(name) is { } hit)
                values.TryAdd(hit.Semantic, (field, hit.Rule));
        }
        var sheet = new SheetReader(compiled, values);
        var features = sheet.ListItems("features"); // S0: feats can sit in a section of the features text
        return new DdbSheet(
            map.Id,
            map.SuggestedFamily,
            sheet.Text("name"),
            sheet.Classes(),
            sheet.Text("species"),
            sheet.Text("background"),
            Enum.GetValues<Ability>().ToDictionary(a => a, a => sheet.Int($"abilities.{FieldIds.Key(a)}", 1, 30)),
            Enum.GetValues<Ability>().ToDictionary(a => a, a => sheet.Bool($"saves.{FieldIds.Key(a)}.proficient")),
            CharacterCalculator.Skills.ToDictionary(s => s.Key, s => sheet.Bool($"skills.{s.Key}.proficient"), StringComparer.Ordinal),
            [.. sheet.List("feats"), .. features.Feats],
            [.. sheet.SpellRows(), .. sheet.List("spells").Select(r => r.Status == ReadStatus.Ok ? Read<SpellText>.Ok(new(r.Value!, null)) : Read<SpellText>.Unreadable)],
            [.. sheet.ItemRows(), .. sheet.List("equipment").Select(r => r.Status == ReadStatus.Ok ? Read<ItemText>.Ok(new(r.Value!, 1, null)) : Read<ItemText>.Unreadable)],
            features.Items,
            DdbSemantics.NumberIds.Where(id => values.ContainsKey($"numbers.{id}")).ToDictionary(id => id, id => sheet.Int($"numbers.{id}", -999, 9_999), StringComparer.Ordinal),
            new DdbPlay(
                sheet.Int("play.currentHitPoints", 0, 9_999),
                sheet.Int("play.temporaryHitPoints", 0, 9_999),
                [.. DdbSemantics.HitDice.Select(d => (Die: d, Read: sheet.Int($"play.hitDiceSpent.d{d}", 0, 20))).Where(x => x.Read.Status == ReadStatus.Ok).Select(x => new DieSpent(x.Die, x.Read.Value))],
                sheet.Int("play.deathSuccesses", 0, 3),
                sheet.Int("play.deathFailures", 0, 3),
                sheet.Bool("play.inspiration"),
                [.. Enumerable.Range(1, 9).Select(l => (Level: l, Read: sheet.Int($"play.spellSlotsSpent.{l}", 0, 99))).Where(x => x.Read.Status == ReadStatus.Ok).Select(x => new SlotsSpent(x.Level, x.Read.Value))]));
    }

    private static readonly ConditionalWeakTable<LayoutMap, CompiledMap> CompiledMaps = [];

    /// <summary>One class part: a name, a level from 1 to 20, and an optional subclass in parentheses (unverified shape, S0).</summary>
    [GeneratedRegex(@"^(?<name>[^/\d]{1,60}?)\s+(?<level>[1-9]|1[0-9]|20)(?:\s*\((?<sub>[^()]{1,60})\))?$", RegexOptions.None, 100)]
    private static partial Regex ClassPart();

    /// <summary>A signed whole number: a plus, a hyphen or a minus sign, then at most five digits.</summary>
    [GeneratedRegex(@"^(?<sign>[+\-−]?)\s*(?<digits>[0-9]{1,5})$", RegexOptions.None, 100)]
    private static partial Regex Number();

    /// <summary>A map with its patterns compiled and its row-numbered names split around <c>{n}</c>.</summary>
    private sealed class CompiledMap
    {
        private readonly Dictionary<string, FieldRule> _exact = new(StringComparer.Ordinal);
        private readonly List<(string Prefix, string Suffix, FieldRule Rule)> _rows = [];

        public CompiledMap(LayoutMap map)
        {
            Map = map;
            foreach (var (name, rule) in map.Fields)
            {
                var at = name.IndexOf("{n}", StringComparison.Ordinal);
                if (at < 0)
                    _exact[name] = rule;
                else
                    _rows.Add((name[..at], name[(at + 3)..], rule));
                if (rule.Pattern is { } pattern)
                    Patterns[pattern] = LayoutMaps.Compile(pattern);
            }
            foreach (var split in map.Splits)
            {
                foreach (var pattern in new[] { split.ItemPattern, split.SectionHeading, split.FeatsSection })
                {
                    if (pattern is not null)
                        Patterns[pattern] = LayoutMaps.Compile(pattern);
                }
            }
        }

        public LayoutMap Map { get; }

        public Dictionary<string, Regex> Patterns { get; } = new(StringComparer.Ordinal);

        /// <summary>The semantic a field name maps to (<c>spells[3].name</c> for row 3), or null when the map does not name it.</summary>
        public (string Semantic, FieldRule Rule)? Resolve(string name)
        {
            if (_exact.TryGetValue(name, out var exact))
                return (exact.Semantic, exact);
            foreach (var (prefix, suffix, rule) in _rows)
            {
                if (name.Length <= prefix.Length + suffix.Length || !name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
                    continue;
                var row = name.AsSpan(prefix.Length, name.Length - prefix.Length - suffix.Length);
                // NumberStyles.None: ASCII digits only, no sign or spaces.
                if (row.Length <= 3 && int.TryParse(row, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    return (rule.Semantic.Replace("[n]", $"[{n}]", StringComparison.Ordinal), rule);
            }
            return null;
        }
    }

    private sealed class SheetReader(CompiledMap compiled, Dictionary<string, (FormField Field, FieldRule Rule)> values)
    {
        /// <summary>A text value, trimmed; through the field's pattern when it has one.</summary>
        public Read<string> Text(string semantic, bool applyPattern = true)
        {
            if (!values.TryGetValue(semantic, out var hit) || string.IsNullOrWhiteSpace(hit.Field.Value))
                return Read<string>.Missing;
            var value = hit.Field.Value.Trim();
            if (!applyPattern || hit.Rule.Pattern is not { } pattern)
                return Read<string>.Ok(value);
            try
            {
                var match = compiled.Patterns[pattern].Match(value);
                var kept = !match.Success ? "" : match.Groups["value"] is { Success: true } group ? group.Value.Trim() : match.Value.Trim();
                return kept.Length > 0 ? Read<string>.Ok(kept) : Read<string>.Unreadable;
            }
            catch (RegexMatchTimeoutException)
            {
                return Read<string>.Unreadable;
            }
        }

        public Read<int> Int(string semantic, int min, int max)
        {
            var text = Text(semantic);
            if (text.Status != ReadStatus.Ok)
                return new(default, text.Status);
            try
            {
                var match = Number().Match(text.Value!);
                if (!match.Success)
                    return Read<int>.Unreadable;
                var number = int.Parse(match.Groups["digits"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                if (match.Groups["sign"].Value is "-" or "−")
                    number = -number;
                return number >= min && number <= max ? Read<int>.Ok(number) : Read<int>.Unreadable;
            }
            catch (RegexMatchTimeoutException)
            {
                return Read<int>.Unreadable;
            }
        }

        /// <summary>
        /// A checkbox's state. A text field is marked when it holds the layout's on-state (or, with
        /// <see cref="LayoutMap.MarkedWhenAnyText"/>, any text: S0 found proficiency marks drawn as text) and unmarked when empty.
        /// </summary>
        public Read<bool> Bool(string semantic)
        {
            if (!values.TryGetValue(semantic, out var hit))
                return Read<bool>.Missing;
            if (hit.Field.Type is "checkbox" or "radio")
                return hit.Field.Checked is { } on ? Read<bool>.Ok(on) : Read<bool>.Unreadable;
            var value = hit.Field.Value?.Trim();
            if (string.IsNullOrEmpty(value))
                return Read<bool>.Ok(false);
            if (compiled.Map.MarkedWhenAnyText)
                return Read<bool>.Ok(true);
            return string.Equals(value, compiled.Map.CheckboxOnState, StringComparison.OrdinalIgnoreCase) ? Read<bool>.Ok(true) : Read<bool>.Unreadable;
        }

        /// <summary>A list field's items (see <see cref="ListItems"/>).</summary>
        public List<Read<string>> List(string semantic) => ListItems(semantic).Items;

        /// <summary>
        /// A list's items: its own field and then its numbered fields (<c>features[1]</c>, <c>features[2]</c>, …) in order,
        /// each split on the map's separator for it (one item when it has none). With the split's patterns, a heading line
        /// starts a section, which carries on into the next numbered field (the fields are one text cut into boxes), only
        /// lines the item pattern matches are items, and items in the feats section are returned apart (<paramref name="semantic"/>
        /// <c>features</c> only). Each list keeps at most <see cref="MaxListItems"/> items.
        /// </summary>
        public (List<Read<string>> Items, List<Read<string>> Feats) ListItems(string semantic)
        {
            var items = new List<Read<string>>();
            var feats = new List<Read<string>>();
            var split = compiled.Map.Splits.FirstOrDefault(s => s.Semantic == semantic);
            var texts = new List<Read<string>> { Text(semantic) };
            texts.AddRange(Rows(semantic).Select(n => Text($"{semantic}[{n}]")));
            string? section = null;
            foreach (var text in texts)
            {
                if (text.Status == ReadStatus.Missing)
                    continue;
                if (text.Status == ReadStatus.Unreadable)
                {
                    items.Add(Read<string>.Unreadable);
                    continue;
                }
                try
                {
                    foreach (var line in Split(text.Value!, semantic, null))
                    {
                        if (split?.SectionHeading is { } heading && compiled.Patterns[heading].Match(line) is { Success: true } start)
                        {
                            section = start.Groups["section"].Value.Trim();
                            continue;
                        }
                        var item = line;
                        if (split?.ItemPattern is { } itemPattern)
                        {
                            var match = compiled.Patterns[itemPattern].Match(line);
                            if (!match.Success)
                                continue;
                            item = match.Groups["value"].Value.Trim();
                        }
                        if (item.Length == 0)
                            continue;
                        var inFeats = split?.FeatsSection is { } featsSection && section is not null && compiled.Patterns[featsSection].IsMatch(section);
                        (inFeats ? feats : items).Add(Read<string>.Ok(item));
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    items.Add(Read<string>.Unreadable);
                }
            }
            return (Capped(items), Capped(feats));
        }

        /// <summary>At most <see cref="MaxListItems"/> items, then one unreadable item for the rest.</summary>
        private static List<Read<string>> Capped(List<Read<string>> list) =>
            list.Count <= MaxListItems ? list : [.. list.Take(MaxListItems), Read<string>.Unreadable];

        /// <summary>The class-and-level text: each part read, at most 20 parts, levels adding up to 1 to 20, or unreadable as a whole.</summary>
        public Read<IReadOnlyList<ClassText>> Classes()
        {
            var text = Text("classLevels", applyPattern: false);
            if (text.Status != ReadStatus.Ok)
                return new(default, text.Status);
            var part = values["classLevels"].Rule.Pattern is { } pattern ? compiled.Patterns[pattern] : ClassPart();
            var parts = Split(text.Value!, "classLevels", "/");
            if (parts.Length > 20)
                return Read<IReadOnlyList<ClassText>>.Unreadable;
            var classes = new List<ClassText>(parts.Length);
            try
            {
                foreach (var item in parts)
                {
                    var match = part.Match(item);
                    if (!match.Success || !int.TryParse(match.Groups["level"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var level) || level is < 1 or > 20)
                        return Read<IReadOnlyList<ClassText>>.Unreadable;
                    var name = match.Groups["name"].Value.Trim();
                    if (name.Length == 0)
                        return Read<IReadOnlyList<ClassText>>.Unreadable;
                    var sub = match.Groups["sub"] is { Success: true } group && group.Value.Trim() is { Length: > 0 } s ? s : null;
                    classes.Add(new ClassText(name, level, sub));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return Read<IReadOnlyList<ClassText>>.Unreadable;
            }
            var total = classes.Sum(c => c.Level);
            return total is >= 1 and <= 20 ? Read<IReadOnlyList<ClassText>>.Ok(classes) : Read<IReadOnlyList<ClassText>>.Unreadable;
        }

        public IEnumerable<Read<SpellText>> SpellRows()
        {
            foreach (var row in Rows("spells"))
            {
                var name = Text($"spells[{row}].name");
                if (name.Status == ReadStatus.Missing)
                    continue;
                if (name.Status == ReadStatus.Unreadable)
                {
                    yield return Read<SpellText>.Unreadable;
                    continue;
                }
                var prepared = Bool($"spells[{row}].prepared");
                yield return prepared.Status == ReadStatus.Unreadable
                    ? new(new SpellText(name.Value!, null), ReadStatus.Unreadable)
                    : Read<SpellText>.Ok(new(name.Value!, prepared.Status == ReadStatus.Ok ? prepared.Value : null));
            }
        }

        public IEnumerable<Read<ItemText>> ItemRows()
        {
            foreach (var row in Rows("equipment"))
            {
                var name = Text($"equipment[{row}].name");
                if (name.Status == ReadStatus.Missing)
                    continue;
                if (name.Status == ReadStatus.Unreadable)
                {
                    yield return Read<ItemText>.Unreadable;
                    continue;
                }
                var quantity = Int($"equipment[{row}].quantity", 1, 9_999);
                var equipped = Bool($"equipment[{row}].equipped");
                yield return quantity.Status == ReadStatus.Unreadable || equipped.Status == ReadStatus.Unreadable
                    ? new(new ItemText(name.Value!, 0, null), ReadStatus.Unreadable)
                    : Read<ItemText>.Ok(new(name.Value!, quantity.Status == ReadStatus.Ok ? quantity.Value : 1, equipped.Status == ReadStatus.Ok ? equipped.Value : null));
            }
        }

        /// <summary>The row numbers present for a row semantic, in order.</summary>
        private SortedSet<int> Rows(string list)
        {
            var rows = new SortedSet<int>();
            var prefix = list + "[";
            foreach (var key in values.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                var end = key.IndexOf(']', prefix.Length);
                if (end > prefix.Length && int.TryParse(key.AsSpan(prefix.Length, end - prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var row))
                    rows.Add(row);
            }
            return rows;
        }

        private string[] Split(string text, string semantic, string? fallback)
        {
            var separator = compiled.Map.Splits.FirstOrDefault(s => s.Semantic == semantic)?.Separator ?? fallback;
            if (separator is null)
                return [text.Trim()];
            if (separator == "\n")
                text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            return [.. text.Split(separator).Select(p => p.Trim())];
        }
    }
}
