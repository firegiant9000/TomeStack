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
    /// The report line for a layout whose death-save boxes were read by number. It is about the play state, so the preview and the
    /// apply drop it unless "Bring over current hit points…" is ticked (the boxes are not brought over otherwise).
    /// </summary>
    internal const string DeathSavesLine = "Death saves were read as boxes 12 to 14 successes and 15 to 17 failures; check them.";

    /// <summary>
    /// The most items one list keeps (features, feats, a split spell or equipment list); the rest are one unreadable item.
    /// Above the 500 gap notes a character can hold, so the import notes' limit is still what users meet first.
    /// </summary>
    public const int MaxListItems = 1_000;

    /// <summary>
    /// The layout whose required field names are all present, or null (R72). When several match, the one with the most
    /// required names wins only if its set holds every other match's whole set; otherwise the sheet is ambiguous (a doctored
    /// file with two layouts' names) and none is picked, rather than one chosen by the order the maps happen to load in.
    /// </summary>
    public static LayoutMap? Recognise(IReadOnlyList<FormField> fields) => Recognise(fields, LayoutMaps.All);

    internal static LayoutMap? Recognise(IReadOnlyList<FormField> fields, IReadOnlyList<LayoutMap> maps)
    {
        if (fields is null)
            return null;
        var names = new HashSet<string>(fields.Where(f => f?.Name is not null).Select(f => f.Name), StringComparer.Ordinal);
        var present = maps.Where(m => m.Required.All(names.Contains)).ToList();
        if (present.Count == 0)
            return null;
        var most = present.Max(m => m.Required.Count);
        var best = present.Where(m => m.Required.Count == most).ToList();
        if (best.Count != 1)
            return null;
        var winner = best[0];
        var held = winner.Required.ToHashSet(StringComparer.Ordinal);
        return present.All(m => ReferenceEquals(m, winner) || (m.Required.Count < most && m.Required.All(held.Contains))) ? winner : null;
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
        var ownFeats = sheet.List("feats"); // the feats field's own items come first in the final feats list
        var hitDice = DdbSemantics.HitDice.Select(d => (Die: d, Read: sheet.Int($"play.hitDiceSpent.d{d}", 0, 20))).ToList();
        var slots = Enumerable.Range(1, 9).Select(l => (Level: l, Read: sheet.Int($"play.spellSlotsSpent.{l}", 0, 99))).ToList();
        var lines = new List<string>();
        var abilities = Enum.GetValues<Ability>().ToDictionary(a => a, a => sheet.Score(a, lines));
        // The fillable sheet's boxes may be named the other way round on every row: one line says so, not six.
        if (lines.Count == Enum.GetValues<Ability>().Length && lines.All(l => l.EndsWith(" was read from the modifier box.", StringComparison.Ordinal)))
        {
            lines.Clear();
            lines.Add("All six ability scores were read from the modifier boxes.");
        }
        var spentFromTotal = sheet.HitDiceFromTotal();
        var spent = hitDice.Where(x => x.Read.Status == ReadStatus.Ok).Select(x => new DieSpent(x.Die, x.Read.Value)).ToList();
        if (spentFromTotal.Spent is { } fromTotal)
            spent.Add(fromTotal);
        var texts = sheet.Texts(lines);
        // The order of the form's six death-save boxes is read from their numbers, not confirmed against a filled sheet.
        if (sheet.AnyDeathBoxTicked())
            lines.Add(DeathSavesLine);
        return new DdbSheet(
            map.Id,
            map.SuggestedFamily,
            sheet.Text("name"),
            sheet.Classes(),
            sheet.Text("species"),
            sheet.Text("background"),
            abilities,
            Enum.GetValues<Ability>().ToDictionary(a => a, a => sheet.Bool($"saves.{FieldIds.Key(a)}.proficient")),
            CharacterCalculator.Skills.ToDictionary(s => s.Key, s => sheet.Bool($"skills.{s.Key}.proficient"), StringComparer.Ordinal),
            SheetReader.Capped([.. ownFeats, .. features.Feats]), // one list, one cap, wherever the feats came from
            [.. sheet.SpellRows(), .. sheet.List("spells").Select(r => r.Status == ReadStatus.Ok ? Read<SpellText>.Ok(new(r.Value!, null)) : Read<SpellText>.Unreadable)],
            [.. sheet.ItemRows(), .. sheet.List("equipment").Select(r => r.Status == ReadStatus.Ok ? Read<ItemText>.Ok(new(r.Value!, 1, null)) : Read<ItemText>.Unreadable)],
            features.Items,
            DdbSemantics.NumberIds.Where(id => values.ContainsKey($"numbers.{id}")).ToDictionary(id => id, id => sheet.Int($"numbers.{id}", -999, 9_999), StringComparer.Ordinal),
            new DdbPlay(
                sheet.Int("play.currentHitPoints", 0, 9_999),
                sheet.Int("play.temporaryHitPoints", 0, 9_999),
                spent,
                sheet.Deaths("play.deathSuccesses"),
                sheet.Deaths("play.deathFailures"),
                sheet.Bool("play.inspiration"),
                [.. slots.Where(x => x.Read.Status == ReadStatus.Ok).Select(x => new SlotsSpent(x.Level, x.Read.Value))],
                hitDice.Count(x => x.Read.Status == ReadStatus.Unreadable) + slots.Count(x => x.Read.Status == ReadStatus.Unreadable) + spentFromTotal.Unreadable),
            DdbSemantics.Coins.Where(c => values.ContainsKey($"currency.{c}")).ToDictionary(c => c, c => sheet.Int($"currency.{c}", 0, Currency.MaxCoins, Coin()), StringComparer.Ordinal),
            lines.Count == 0 ? null : lines,
            texts,
            sheet.Text("play.hitDice.total", applyPattern: false),
            // A feat text's index was counted in the features split's own feats; the final list puts the feats field's items first.
            features.Texts.Count == 0 ? null : [.. features.Texts.Select(t => t.Kind == FeatureKind.Feat ? t with { Index = t.Index + ownFeats.Count } : t)]);
    }

    private static readonly ConditionalWeakTable<LayoutMap, CompiledMap> CompiledMaps = [];

    /// <summary>
    /// One class part: a name, a level from 1 to 20 (written bare, or after "LVL" or "Level", as the fillable sheet's owners
    /// write it), and an optional subclass in parentheses (unverified shape, S0).
    /// </summary>
    [GeneratedRegex(@"^(?<name>[^/\d]{1,60}?)\s+(?:(?:[Ll][Vv][Ll]|[Ll][Ee][Vv][Ee][Ll])\.?\s*)?(?<level>[1-9]|1[0-9]|20)(?:\s*\((?<sub>[^()]{1,60})\))?$", RegexOptions.None, 100)]
    private static partial Regex ClassPart();

    /// <summary>A hit dice total: a count and one die size ("3d8").</summary>
    [GeneratedRegex(@"^(?<count>[0-9]{1,2})\s*[dD]\s*(?<die>6|8|10|12)$", RegexOptions.None, 100)]
    private static partial Regex DiceTotal();

    /// <summary>Hit dice left: a count, with the die size optional ("2d8", "2").</summary>
    [GeneratedRegex(@"^(?<count>[0-9]{1,2})(?:\s*[dD]\s*(?<die>[0-9]{1,2}))?$", RegexOptions.None, 100)]
    private static partial Regex DiceLeft();

    /// <summary>A signed whole number: a plus, a hyphen or a minus sign, then at most five digits.</summary>
    [GeneratedRegex(@"^(?<sign>[+\-−]?)\s*(?<digits>[0-9]{1,5})$", RegexOptions.None, 100)]
    private static partial Regex Number();

    /// <summary>A coin count: digits only, at most seven (the character bound is 1,000,000).</summary>
    [GeneratedRegex(@"^(?<sign>[+\-−]?)\s*(?<digits>[0-9]{1,7})$", RegexOptions.None, 100)]
    private static partial Regex Coin();

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
                // NumberStyles.None: ASCII digits only, no sign or spaces. No leading zero ("01" and "1" are not one row).
                if (row.Length <= 3 && (row.Length == 1 || row[0] != '0') && int.TryParse(row, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
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

        public Read<int> Int(string semantic, int min, int max, Regex? shape = null)
        {
            var text = Text(semantic);
            if (text.Status != ReadStatus.Ok)
                return new(default, text.Status);
            try
            {
                var match = (shape ?? Number()).Match(text.Value!);
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
        /// A checkbox's state. A text field is marked when it holds the layout's on-state or one of its
        /// <see cref="LayoutMap.MarkValues"/> (S0 found proficiency marks drawn as text), unmarked when empty, and
        /// unreadable otherwise.
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
            var marked = string.Equals(value, compiled.Map.CheckboxOnState, StringComparison.OrdinalIgnoreCase)
                || (compiled.Map.MarkValues ?? []).Any(m => string.Equals(value, m, StringComparison.OrdinalIgnoreCase));
            return marked ? Read<bool>.Ok(true) : Read<bool>.Unreadable;
        }

        /// <summary>A list field's items (see <see cref="ListItems"/>).</summary>
        public List<Read<string>> List(string semantic) => ListItems(semantic).Items;

        /// <summary>
        /// A list's items: its own field and then its numbered fields (<c>features[1]</c>, <c>features[2]</c>, …) in order,
        /// each split on the map's separator for it (one item when it has none). With the split's patterns, a heading line
        /// starts a section, which carries on into the next numbered field (the fields are one text cut into boxes), only
        /// lines the item pattern matches are items, and items in the feats section are returned apart (<paramref name="semantic"/>
        /// <c>features</c> only). Each list keeps at most <see cref="MaxListItems"/> items. Item 16 (R104): the lines the item
        /// pattern skips are the description of the item before them, up to the next item or heading (and carrying on into the
        /// next numbered field); <paramref name="Texts"/> holds each description that is not blank.
        /// </summary>
        public (List<Read<string>> Items, List<Read<string>> Feats, List<FeatureText> Texts) ListItems(string semantic)
        {
            var items = new List<Read<string>>();
            var feats = new List<Read<string>>();
            var described = new List<FeatureText>();
            var split = compiled.Map.Splits.FirstOrDefault(s => s.Semantic == semantic);
            var texts = new List<Read<string>> { Text(semantic) };
            texts.AddRange(Rows(semantic).Select(n => Text($"{semantic}[{n}]")));
            string? section = null;
            (string Name, FeatureKind Kind, int Index)? current = null;
            var under = new List<string>();
            void Flush()
            {
                if (current is { } open && string.Join('\n', under).Trim() is { Length: > 0 } body)
                    described.Add(new(open.Name, open.Kind, body, open.Index));
                current = null;
                under.Clear();
            }
            foreach (var text in texts)
            {
                if (text.Status == ReadStatus.Missing)
                    continue;
                if (text.Status == ReadStatus.Unreadable)
                {
                    Flush();
                    items.Add(Read<string>.Unreadable);
                    continue;
                }
                try
                {
                    foreach (var line in Split(text.Value!, semantic, null))
                    {
                        if (split?.SectionHeading is { } heading && compiled.Patterns[heading].Match(line) is { Success: true } start)
                        {
                            Flush();
                            section = start.Groups["section"].Value.Trim();
                            continue;
                        }
                        var item = line;
                        if (split?.ItemPattern is { } itemPattern)
                        {
                            var match = compiled.Patterns[itemPattern].Match(line);
                            if (!match.Success)
                            {
                                if (current is not null)
                                    under.Add(line);
                                continue;
                            }
                            item = match.Groups["value"].Value.Trim();
                        }
                        Flush();
                        if (item.Length == 0)
                            continue;
                        var inFeats = split?.FeatsSection is { } featsSection && section is not null && compiled.Patterns[featsSection].IsMatch(section);
                        var list = inFeats ? feats : items;
                        if (split?.ItemPattern is not null)
                            current = (item, inFeats ? FeatureKind.Feat : FeatureKind.Feature, list.Count);
                        list.Add(Read<string>.Ok(item));
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    Flush();
                    items.Add(Read<string>.Unreadable);
                }
            }
            Flush();
            return (Capped(items), Capped(feats), described.Count <= MaxListItems ? described : described.GetRange(0, MaxListItems));
        }

        /// <summary>At most <see cref="MaxListItems"/> items, then one unreadable item for the rest.</summary>
        internal static List<Read<string>> Capped(List<Read<string>> list) =>
            list.Count <= MaxListItems ? list : [.. list.Take(MaxListItems), Read<string>.Unreadable];

        /// <summary>The class-and-level text: each part read, at most 20 parts, levels adding up to 1 to 20, or unreadable as a whole.</summary>
        public Read<IReadOnlyList<ClassText>> Classes()
        {
            var text = Text("classLevels", applyPattern: false);
            if (text.Status != ReadStatus.Ok)
                return new(default, text.Status);
            var part = values["classLevels"].Rule.Pattern is { } pattern ? compiled.Patterns[pattern] : ClassPart();
            var strict = ReadClasses(Split(text.Value!, "classLevels", "/"), part);
            if (strict is not null)
                return strict.Value;
            // Players of the fillable sheet separate classes with a comma as often as a slash: try that when the slash split fails.
            return ReadClasses(CommaSplit(text.Value!), part) ?? Read<IReadOnlyList<ClassText>>.Unreadable;
        }

        /// <summary>Null when a part does not read (so the caller may try another split); unreadable as a whole when the levels do not add up.</summary>
        private static Read<IReadOnlyList<ClassText>>? ReadClasses(string[] parts, Regex part)
        {
            if (parts.Length > 20)
                return Read<IReadOnlyList<ClassText>>.Unreadable;
            var classes = new List<ClassText>(parts.Length);
            try
            {
                foreach (var item in parts)
                {
                    var match = part.Match(item);
                    if (!match.Success || !int.TryParse(match.Groups["level"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var level) || level is < 1 or > 20)
                        return null;
                    var name = match.Groups["name"].Value.Trim();
                    if (name.Length == 0)
                        return null;
                    var sub = match.Groups["sub"] is { Success: true } group && group.Value.Trim() is { Length: > 0 } s ? s : null;
                    classes.Add(new ClassText(name, level, sub));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
            var total = classes.Sum(c => c.Level);
            return total is >= 1 and <= 20 ? Read<IReadOnlyList<ClassText>>.Ok(classes) : Read<IReadOnlyList<ClassText>>.Unreadable;
        }

        /// <summary>The text split on slashes and on commas outside parentheses.</summary>
        private static string[] CommaSplit(string text)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '(':
                        depth++;
                        break;
                    case ')':
                        depth = Math.Max(0, depth - 1);
                        break;
                    case '/' or ',' when depth == 0:
                        parts.Add(text[start..i].Trim());
                        start = i + 1;
                        break;
                }
            }
            parts.Add(text[start..].Trim());
            return [.. parts];
        }

        private static readonly Dictionary<Ability, string> AbilityNames = new()
        {
            [Ability.Str] = "Strength", [Ability.Dex] = "Dexterity", [Ability.Con] = "Constitution",
            [Ability.Int] = "Intelligence", [Ability.Wis] = "Wisdom", [Ability.Cha] = "Charisma",
        };

        /// <summary>
        /// An ability score. A layout with a modifier box beside the score (the fillable sheet, whose map follows the owner's
        /// sheet, where the form names the two boxes the other way round from their labels, R95) gets the swap guard: the score
        /// is read from the box that holds it when the other holds its modifier, and a pair that disagrees is unreadable. A
        /// number with a sign is a modifier, never a score. A lone unsigned 1 to 30 in the score slot is the score (R97); a lone
        /// value in the modifier slot is the score only when it is unsigned and 11 to 30. Reading the score from the modifier
        /// box adds a report line.
        /// </summary>
        public Read<int> Score(Ability ability, List<string> lines)
        {
            var key = FieldIds.Key(ability);
            if (!values.ContainsKey($"abilityMods.{key}"))
                return Int($"abilities.{key}", 1, 30);
            var score = Raw($"abilities.{key}");
            var mod = Raw($"abilityMods.{key}");
            if (score is null && mod is null)
                return Read<int>.Missing;
            var s = score is null ? null : Signed(score);
            var m = mod is null ? null : Signed(mod);
            if ((score is not null && s is null) || (mod is not null && m is null))
                return Read<int>.Unreadable;
            if (s is { } sv && m is { } mv)
            {
                if (!sv.HasSign && sv.Number is >= 1 and <= 30 && Modifier(sv.Number) == mv.Number)
                    return Read<int>.Ok(sv.Number);
                if (!mv.HasSign && mv.Number is >= 1 and <= 30 && Modifier(mv.Number) == sv.Number)
                {
                    lines.Add($"{AbilityNames[ability]} was read from the modifier box.");
                    return Read<int>.Ok(mv.Number);
                }
                return Read<int>.Unreadable;
            }
            // R97: a lone box. In the score slot an unsigned 1 to 30 is the score; in the modifier slot only an unsigned 11 to 30
            // is (a modifier tops out at 10), and the report says so. Anything else is unreadable, never a guess.
            if (s is { } only && !only.HasSign && only.Number is >= 1 and <= 30)
                return Read<int>.Ok(only.Number);
            if (m is { } lone && !lone.HasSign && lone.Number is >= 11 and <= 30)
            {
                lines.Add($"{AbilityNames[ability]} was read from the modifier box.");
                return Read<int>.Ok(lone.Number);
            }
            return Read<int>.Unreadable;
        }

        private static int Modifier(int score) => (score - 10) >> 1; // floor division

        /// <summary>The trimmed text of a field, or null when it is blank or the map has no such field.</summary>
        private string? Raw(string semantic) =>
            values.TryGetValue(semantic, out var hit) && !string.IsNullOrWhiteSpace(hit.Field.Value) ? hit.Field.Value.Trim() : null;

        /// <summary>A whole number and whether it carried a sign; null when it is not one.</summary>
        private static (int Number, bool HasSign)? Signed(string text)
        {
            try
            {
                var match = Number().Match(text);
                if (!match.Success)
                    return null;
                var number = int.Parse(match.Groups["digits"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                var sign = match.Groups["sign"].Value;
                return (sign is "-" or "−" ? -number : number, sign.Length > 0);
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        /// <summary>
        /// Death saves: a layout with one box per save (<c>play.deathSuccesses.1</c> to <c>.3</c>) counts the ticked ones; an
        /// unreadable box makes the count unreadable; otherwise the layout's own count field is read.
        /// </summary>
        public Read<int> Deaths(string semantic)
        {
            var boxes = Enumerable.Range(1, DdbSemantics.DeathBoxes).Select(i => $"{semantic}.{i}").Where(values.ContainsKey).ToList();
            if (boxes.Count == 0)
                return Int(semantic, 0, 3);
            var reads = boxes.Select(Bool).ToList();
            if (reads.Any(r => r.Status == ReadStatus.Unreadable))
                return Read<int>.Unreadable;
            return Read<int>.Ok(reads.Count(r => r is { Status: ReadStatus.Ok, Value: true }));
        }

        /// <summary>True when the layout has death-save boxes and any is ticked.</summary>
        public bool AnyDeathBoxTicked() =>
            new[] { "play.deathSuccesses", "play.deathFailures" }
                .SelectMany(s => Enumerable.Range(1, DdbSemantics.DeathBoxes).Select(i => $"{s}.{i}"))
                .Where(values.ContainsKey)
                .Any(box => Bool(box) is { Status: ReadStatus.Ok, Value: true });

        /// <summary>
        /// Spent hit dice from the sheet's "total" and "left" boxes (R: spent is the total less what is left). Nothing is
        /// guessed: a total that is not one die size, a left count above the total, or another die is one unreadable field.
        /// A blank left box is nothing read.
        /// </summary>
        public (DieSpent? Spent, int Unreadable) HitDiceFromTotal()
        {
            var total = Raw("play.hitDice.total");
            var left = Raw("play.hitDice.remaining");
            if (total is null && left is null)
                return (null, 0);
            if (left is null)
                return (null, 0);
            try
            {
                var t = total is null ? null : DiceTotal().Match(total);
                var l = DiceLeft().Match(left);
                if (t is not { Success: true } || !l.Success)
                    return (null, 1);
                var die = int.Parse(t.Groups["die"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                if (l.Groups["die"] is { Success: true } leftDie && int.Parse(leftDie.Value, NumberStyles.None, CultureInfo.InvariantCulture) != die)
                    return (null, 1);
                var count = int.Parse(t.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                var remaining = int.Parse(l.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                return remaining > count || count > 20 ? (null, 1) : (new DieSpent(die, count - remaining), 0);
            }
            catch (RegexMatchTimeoutException)
            {
                return (null, 1);
            }
        }

        /// <summary>The fillable sheet's free text (null for a layout that maps none), with a report line for each field cut short (R78).</summary>
        public DdbTexts? Texts(List<string> lines)
        {
            if (!values.Keys.Any(k => k.StartsWith("details.", StringComparison.Ordinal) || k.StartsWith("prose.", StringComparison.Ordinal)
                    || k.StartsWith("weapons.", StringComparison.Ordinal) || k is "equipmentText" or "languagesAndTools"))
                return null;
            Read<string> Block(string semantic)
            {
                var read = Text(semantic, applyPattern: false);
                return read.Status == ReadStatus.Ok ? Read<string>.Ok(read.Value!.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')) : read;
            }
            var details = DdbSemantics.Details.Where(values.ContainsKey).ToDictionary(
                s => s["details.".Length..].Replace("appearance.", "", StringComparison.Ordinal), s => Block(s), StringComparer.Ordinal);
            var prose = DdbSemantics.Prose.Keys.Where(k => values.ContainsKey($"prose.{k}")).ToDictionary(k => k, k => Block($"prose.{k}"), StringComparer.Ordinal);
            var weapons = Enumerable.Range(1, DdbSemantics.WeaponRows).Select(r => new WeaponRow(Text($"weapons.{r}.name"), Text($"weapons.{r}.toHit"), Text($"weapons.{r}.damage"))).ToList();
            var cut = $"{FormLimits.Default.MaxValueChars.ToString("N0", CultureInfo.InvariantCulture)}";
            foreach (var (key, title) in DdbSemantics.Prose.Select(p => ($"prose.{p.Key}", p.Value)).Concat(
                [("languagesAndTools", DdbSemantics.TextTitles["languagesAndTools"]), ("equipmentText", DdbSemantics.TextTitles["equipmentText"])]))
            {
                if (values.TryGetValue(key, out var hit) && hit.Field.Clipped)
                    lines.Add($"{title} was cut to {cut} characters.");
            }
            return new DdbTexts(details, prose, values.Keys.Any(k => k.StartsWith("weapons.", StringComparison.Ordinal)) ? weapons : [], Block("equipmentText"), Block("languagesAndTools"));
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
