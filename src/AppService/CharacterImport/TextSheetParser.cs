using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TomeStack.RulesCore;

namespace TomeStack.AppService.CharacterImport;

/// <summary>Why a text was refused, as a code and a message that holds counts only (item 19a). The text is the user's own and may name real people.</summary>
public sealed record TextRefusal(string Code, string Message)
{
    public override string ToString() => $"TextRefusal {{ Code = {Code} }}";
}

/// <summary>Exactly one of the two is set: the parsed sheet, or the refusal.</summary>
public sealed record TextParse(DdbSheet? Sheet, TextRefusal? Refusal);

/// <summary>
/// Item 19a (D37, ADR-017): pasted text or a <c>.txt</c>/<c>.md</c> file to the same <see cref="DdbSheet"/> the PDF importer
/// produces (<c>features/ddb-pdf-import.md</c> "From text"). It reads <c>Label: value</c> lines, the three ability forms, section
/// headings, comma or line lists, coins and the two shapes of a feature list; light Markdown (a leading <c>#</c> run, <c>**</c>/<c>__</c>,
/// list markers) is stripped and nothing is ever rendered. Every line it does not understand (blank lines excepted) is a
/// <see cref="NotReadLine"/> with its line number, never silently dropped.
/// </summary>
/// <remarks>
/// The parser is total and bounded: 262,144 characters, 5,000 lines, 2,000 characters a line (a longer line is not parsed, only
/// listed, clipped) and a 2 second budget checked between lines; every pattern runs without backtracking and with a 100 ms limit.
/// A cap is a <see cref="TextRefusal"/> with counts. It never throws on content, and no message, <c>ToString</c> or code carries any of the text.
/// </remarks>
public static class TextSheetParser
{
    public const int MaxChars = 262_144;
    public const int MaxLines = 5_000;
    public const int MaxLineChars = 2_000;

    /// <summary>The longest name a list entry (a spell, an item) may have; a longer one is prose, and its line is not read.</summary>
    private const int MaxEntry = 80;

    /// <summary>The longest feat or feature name.</summary>
    private const int MaxFeatureName = 100;

    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    /// <param name="budget">The total time allowed; tests pass a shorter one.</param>
    public static TextParse Parse(string text, TimeSpan? budget = null)
    {
        var clock = Stopwatch.StartNew();
        return Parse(text, budget ?? Budget, () => clock.Elapsed);
    }

    /// <param name="elapsed">The time spent so far; a seam so a timeout part-way through a parse can be tested.</param>
    internal static TextParse Parse(string text, TimeSpan limit, Func<TimeSpan> elapsed)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxChars)
            return Refuse("ddb.text-too-large", $"The text is {Count(text.Length)} characters; the limit is {Count(MaxChars)}. Nothing was read.");
        var total = CountLines(text);
        if (total > MaxLines)
            return Refuse("ddb.text-too-many-lines", $"The text has {Count(total)} lines; the limit is {Count(MaxLines)}. Nothing was read.");
        var lines = SplitLines(text, total);
        if (lines.All(l => l.AsSpan().Trim().IsEmpty))
            return Refuse("ddb.text-empty", "The text is empty. Nothing was read.");

        var reader = new Reader();
        for (var i = 0; i < lines.Count; i++)
        {
            if (elapsed() >= limit)
                return Refuse("ddb.text-timeout", $"Reading took longer than {limit.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture)} seconds, after {Count(i)} of {Count(total)} lines. Nothing was kept.");
            reader.Line(i + 1, lines[i]);
        }
        return new TextParse(reader.Finish(), null);
    }

    private static TextParse Refuse(string code, string message) => new(null, new TextRefusal(code, message));

    private static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The lines in a text: <c>\n</c>, <c>\r\n</c> and <c>\r</c> each end one, and a final empty piece after the last break is no line.</summary>
    private static int CountLines(string text)
    {
        var count = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\n' or '\r'))
                continue;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            count++;
            start = i + 1;
        }
        return start < text.Length ? count + 1 : count;
    }

    private static List<string> SplitLines(string text, int count)
    {
        var lines = new List<string>(count);
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\n' or '\r'))
                continue;
            lines.Add(text[start..i]);
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            start = i + 1;
        }
        if (start < text.Length)
            lines.Add(text[start..]);
        return lines;
    }

    // ---- the patterns: no backtracking, ImportRegex.TimeoutMs each ----

    private static Regex Rx(string pattern, bool ignoreCase = false) =>
        new(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromMilliseconds(ImportRegex.TimeoutMs));

    private static readonly Regex Rule = Rx(@"^[-_*=]{3,}$");
    private static readonly Regex Marker = Rx(@"^(?:[-*•·]\s+|[0-9]{1,3}[.)]\s+)");
    private static readonly Regex CoinLine = Rx(@"^(?:(?:coins?|money|currency|purse|wealth|cash)\s*[:=]?\s*)?[0-9]{1,8}\s*(?:pp|gp|ep|sp|cp)\b(?:[\s,;/|+&]*(?:and\s+)?[0-9]{1,8}\s*(?:pp|gp|ep|sp|cp)\b)*\s*$", ignoreCase: true);
    private static readonly Regex CoinToken = Rx(@"(?<n>[0-9]{1,8})\s*(?<c>pp|gp|ep|sp|cp)\b", ignoreCase: true);
    private static readonly Regex CoinWord = Rx(@"^(?<w>platinum|gold|electrum|silver|copper|pp|gp|ep|sp|cp)\s*[:=]\s*(?<n>[0-9]{1,8})$", ignoreCase: true);

    private const string AbilityToken = @"(?:strength|dexterity|constitution|intelligence|wisdom|charisma|str|dex|con|int|wis|cha)\s*[:=\-]?\s*[0-9]{1,3}(?:\s*\(\s*[+\-−]?[0-9]{1,2}\s*\))?";
    private static readonly Regex AbilityLine = Rx($@"^{AbilityToken}(?:[\s,;|/]+{AbilityToken})*[\s,;|]*$", ignoreCase: true);
    private static readonly Regex AbilityOne = Rx(@"(?<a>strength|dexterity|constitution|intelligence|wisdom|charisma|str|dex|con|int|wis|cha)\s*[:=\-]?\s*(?<s>[0-9]{1,3})", ignoreCase: true);

    private static readonly Regex SpellLevel = Rx(@"^[1-9](?:st|nd|rd|th)(?:\s*-?\s*level)?(?:\s*\([^()]{0,30}\))?$", ignoreCase: true);
    private static readonly Regex ClassPart = Rx(@"^(?<name>[^0-9()]{1,60}?)(?:\s+(?:(?:lvl|level)\.?\s*)?(?<level>[0-9]{1,2}))?(?:\s*\((?<sub>[^()]{1,60})\))?$", ignoreCase: true);
    private static readonly Regex LevelValue = Rx(@"^(?:(?:level|lvl)\.?\s*)?(?<level>[0-9]{1,2})$", ignoreCase: true);
    private static readonly Regex WholeNumber = Rx(@"^(?<sign>[+\-−]?)\s*(?<d>[0-9]{1,5})(?:\s*(?:ft\.?|feet|foot))?(?:\s*\([^()]*\))?$", ignoreCase: true);
    private static readonly Regex Hp = Rx(@"^(?<cur>[0-9]{1,4})\s*(?:/|of)\s*(?<max>[0-9]{1,4})(?:\s*\([^()]*\))?$", ignoreCase: true);
    private static readonly Regex NumLabel = Rx(@"^(?<l>armou?r class|hit points|prof(?:iciency)? bonus|hit dice|initiative|speed|level|lvl|ac|hp|hd|pb)\s+(?<v>[+\-−]?[0-9].*)$", ignoreCase: true);

    // ---- the vocabulary ----

    private enum Label { Name, Class, Level, Subclass, Race, Species, Background, Alignment, Hp, Ac, Speed, Initiative, ProfBonus, HitDice, Passive, Age, Height, Weight, Eyes, Skin, Hair }

    private enum Section { None, Spells, Equipment, Feats, Features, Saves, Skills, Languages, Tools, Proficiencies }

    private static readonly Dictionary<string, Label> Labels = new(StringComparer.Ordinal)
    {
        ["name"] = Label.Name, ["character name"] = Label.Name, ["character"] = Label.Name, ["pc"] = Label.Name,
        ["class"] = Label.Class, ["classes"] = Label.Class, ["class and level"] = Label.Class, ["class/level"] = Label.Class, ["class and levels"] = Label.Class, ["class level"] = Label.Class,
        ["level"] = Label.Level, ["lvl"] = Label.Level, ["character level"] = Label.Level, ["total level"] = Label.Level,
        ["subclass"] = Label.Subclass, ["archetype"] = Label.Subclass,
        ["race"] = Label.Race, ["species"] = Label.Species,
        ["background"] = Label.Background, ["alignment"] = Label.Alignment,
        ["hp"] = Label.Hp, ["hit points"] = Label.Hp, ["health"] = Label.Hp,
        ["ac"] = Label.Ac, ["armor class"] = Label.Ac, ["armour class"] = Label.Ac,
        ["speed"] = Label.Speed, ["initiative"] = Label.Initiative, ["init"] = Label.Initiative,
        ["proficiency bonus"] = Label.ProfBonus, ["prof bonus"] = Label.ProfBonus, ["prof"] = Label.ProfBonus, ["pb"] = Label.ProfBonus,
        ["hit dice"] = Label.HitDice, ["hit die"] = Label.HitDice, ["hd"] = Label.HitDice,
        ["passive perception"] = Label.Passive,
        ["age"] = Label.Age, ["height"] = Label.Height, ["weight"] = Label.Weight, ["eyes"] = Label.Eyes, ["skin"] = Label.Skin, ["hair"] = Label.Hair,
    };

    private static readonly Dictionary<string, Section> Headings = new(StringComparer.Ordinal)
    {
        ["spells"] = Section.Spells, ["spell list"] = Section.Spells, ["spellbook"] = Section.Spells, ["spells known"] = Section.Spells, ["known spells"] = Section.Spells,
        ["prepared spells"] = Section.Spells, ["cantrips"] = Section.Spells, ["cantrip"] = Section.Spells,
        ["equipment"] = Section.Equipment, ["inventory"] = Section.Equipment, ["gear"] = Section.Equipment, ["items"] = Section.Equipment, ["possessions"] = Section.Equipment,
        ["feats"] = Section.Feats, ["feat"] = Section.Feats,
        ["features"] = Section.Features, ["traits"] = Section.Features, ["features and traits"] = Section.Features, ["class features"] = Section.Features,
        ["species traits"] = Section.Features, ["racial traits"] = Section.Features, ["feature"] = Section.Features,
        ["saves"] = Section.Saves, ["saving throws"] = Section.Saves, ["saving throw proficiencies"] = Section.Saves, ["save proficiencies"] = Section.Saves,
        ["skills"] = Section.Skills, ["skill proficiencies"] = Section.Skills, ["proficient skills"] = Section.Skills,
        ["languages"] = Section.Languages, ["language"] = Section.Languages,
        ["tools"] = Section.Tools, ["tool proficiencies"] = Section.Tools, ["tools and kits"] = Section.Tools,
        ["proficiencies"] = Section.Proficiencies,
    };

    /// <summary>Headings that are known to be something else, so they end a list of features instead of becoming one of its items.</summary>
    private static readonly HashSet<string> Enders = new(StringComparer.Ordinal)
    {
        "ability scores", "abilities", "stats", "statistics", "basics", "basic info", "combat", "summary", "info", "details", "stat block",
        "notes", "backstory", "personality", "personality traits", "ideals", "bonds", "flaws", "appearance", "description", "allies", "enemies",
        "journal", "session notes", "attacks", "weapons", "actions", "reactions", "treasure", "history", "coins", "money", "currency",
    };

    private static readonly Dictionary<string, Ability> AbilityWords = new(StringComparer.Ordinal)
    {
        ["str"] = Ability.Str, ["strength"] = Ability.Str, ["dex"] = Ability.Dex, ["dexterity"] = Ability.Dex, ["con"] = Ability.Con, ["constitution"] = Ability.Con,
        ["int"] = Ability.Int, ["intelligence"] = Ability.Int, ["wis"] = Ability.Wis, ["wisdom"] = Ability.Wis, ["cha"] = Ability.Cha, ["charisma"] = Ability.Cha,
    };

    private static readonly Dictionary<string, string> SkillKeys =
        CharacterCalculator.Skills.ToDictionary(s => LettersOnly(s.Label), s => s.Key, StringComparer.Ordinal);

    private static string LettersOnly(string s) => string.Concat(s.Where(char.IsLetter)).ToLowerInvariant();

    private static string Canon(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant().Replace("&", "and", StringComparison.Ordinal);

    // ---- text hygiene ----

    /// <summary>Control characters (but the tab) become spaces and a lone surrogate becomes U+FFFD, so what is kept is well-formed text.</summary>
    private static string Clean(string s)
    {
        StringBuilder? fixedUp = null;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var replacement = c switch
            {
                < ' ' when c != '\t' => ' ',
                '\u007f' => ' ',
                _ when char.IsHighSurrogate(c) && !(i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) => '�',
                _ when char.IsLowSurrogate(c) && !(i > 0 && char.IsHighSurrogate(s[i - 1])) => '�',
                _ => c,
            };
            if (replacement != c && fixedUp is null)
                fixedUp = new StringBuilder(s, 0, i, s.Length);
            fixedUp?.Append(replacement);
        }
        return fixedUp?.ToString() ?? s;
    }

    /// <summary>At most <see cref="MaxLineChars"/> characters, never between the halves of a surrogate pair.</summary>
    private static string Clip(string s)
    {
        if (s.Length <= MaxLineChars)
            return s;
        return s[..(char.IsHighSurrogate(s[MaxLineChars - 1]) ? MaxLineChars - 1 : MaxLineChars)];
    }

    private static int Indent(string line)
    {
        var width = 0;
        foreach (var c in line)
        {
            if (c == ' ')
                width++;
            else if (c == '\t')
                width += 4;
            else
                break;
        }
        return width;
    }

    /// <summary>The text split at commas and semicolons outside brackets; empty entries dropped.</summary>
    private static List<string> Entries(string text, ReadOnlySpan<char> separators)
    {
        var entries = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length)
            {
                var c = text[i];
                if (c is '(' or '[')
                    depth++;
                else if (c is ')' or ']')
                    depth = Math.Max(0, depth - 1);
                if (depth > 0 || !separators.Contains(c))
                    continue;
            }
            var entry = text[start..i].Trim();
            if (entry.Length > 0)
                entries.Add(entry);
            start = i + 1;
        }
        return entries;
    }

    private static bool NameLike(string name) =>
        name.Length is > 0 and <= MaxFeatureName && name.Count(c => c == ' ') < 8 && name.IndexOfAny(['.', '!', '?']) < 0;

    // ---- the reader ----

    private sealed class Reader
    {
        private readonly List<NotReadLine> _unread = [];
        private readonly HashSet<Label> _seen = [];
        private readonly Dictionary<Ability, Read<int>> _abilities = [];
        private readonly Dictionary<Ability, bool> _saves = [];
        private readonly Dictionary<string, bool> _skills = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Read<int>> _numbers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Read<string>> _details = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Read<int>> _currency = new(StringComparer.Ordinal);
        private readonly List<Read<SpellText>> _spells = [];
        private readonly List<Read<string>> _feats = [];
        private readonly List<Read<string>> _features = [];
        private readonly List<FeatureText> _texts = [];
        private readonly List<string> _equipment = [];
        private readonly List<string> _languages = [];
        private readonly List<string> _tools = [];

        private Read<string> _name = Read<string>.Missing;
        private Read<string> _species = Read<string>.Missing;
        private Read<string> _background = Read<string>.Missing;
        private Read<string>? _hitDice;
        private Read<int> _currentHp = Read<int>.Missing;
        private (int No, string Clean, string Value)? _class;
        private (int No, string Clean, string Value)? _level;
        private (int No, string Clean, string Value)? _subclass;
        private string? _titleName;
        private bool _race;
        private bool _speciesWord;
        private bool _any;
        private bool _blank = true;
        private int _longLines;
        private Section _section;
        private int _sectionHashes;
        private OpenItem? _open;

        private sealed class OpenItem(FeatureKind kind, string name, int index)
        {
            public FeatureKind Kind { get; } = kind;

            public string Name { get; } = name;

            public int Index { get; } = index;

            public List<string> Lines { get; } = [];
        }

        private void Unread(int no, string clean) => _unread.Add(new NotReadLine(no, Clip(clean.Trim())));

        public void Line(int no, string raw)
        {
            var clean = Clean(raw);
            if (clean.AsSpan().Trim().IsEmpty)
            {
                _blank = true;
                return;
            }
            var afterBlank = _blank;
            _blank = false;
            if (raw.Length > MaxLineChars)
            {
                // Not parsed: only listed, clipped, and counted.
                _longLines++;
                Unread(no, clean);
                return;
            }
            try
            {
                Handle(no, clean, afterBlank);
            }
            catch (Exception ex) when (ex is RegexMatchTimeoutException or KeyNotFoundException or ArgumentException or FormatException or OverflowException or InvalidOperationException or IndexOutOfRangeException)
            {
                // Total by construction: whatever a line does to a pattern or a lookup (a timeout, or case folding that matches a
                // word the tables do not hold), it is listed as not read. The exception text can carry the line, so it is dropped.
                Close();
                Unread(no, clean);
            }
        }

        private void Handle(int no, string clean, bool afterBlank)
        {
            var first = !_any;
            _any = true;
            var indent = Indent(clean);
            var plain = clean.Trim();
            if (Rule.IsMatch(plain))
                return; // a horizontal rule says nothing

            var hashes = 0;
            if (plain[0] == '#')
            {
                while (hashes < plain.Length && plain[hashes] == '#')
                    hashes++;
                if (hashes <= 6 && (hashes == plain.Length || plain[hashes] == ' '))
                    plain = plain[hashes..].Trim();
                else
                    hashes = 0;
            }
            plain = plain.Replace("**", "", StringComparison.Ordinal).Replace("__", "", StringComparison.Ordinal).Trim();
            if (plain.Length == 0)
                return;
            var bare = plain;
            var bulleted = false;
            if (hashes == 0 && Marker.Match(plain) is { Success: true } marker)
            {
                bulleted = true;
                bare = plain[marker.Length..].Trim();
                if (bare.Length == 0)
                    return;
            }

            // A description line indented under an open feat or feature belongs to it, whatever it looks like.
            if ((_open is not null || _dropped) && hashes == 0 && indent >= 2 && _section is Section.Feats or Section.Features)
            {
                if (_open is null)
                    _beyondCap++;
                else
                    _open.Lines.Add(plain);
                return;
            }

            if (CoinLine.IsMatch(bare))
            {
                Close();
                Coins(no, clean, bare);
                return;
            }
            if (CoinWord.Match(bare) is { Success: true } word)
            {
                Close();
                var coin = word.Groups["w"].Value.ToLowerInvariant() switch { "platinum" => "pp", "gold" => "gp", "electrum" => "ep", "silver" => "sp", "copper" => "cp", var c => c };
                SetCoin(no, clean, coin, word.Groups["n"].Value);
                return;
            }
            if (hashes == 0 && AbilityLine.IsMatch(bare))
            {
                Close();
                Abilities(no, clean, bare);
                return;
            }

            // Markdown: a heading deeper than the one that opened a features list is one of its entries.
            if (hashes > 0 && _sectionHashes > 0 && hashes > _sectionHashes && _section is Section.Feats or Section.Features)
            {
                NewItem(no, clean, bare);
                return;
            }

            var colon = bare.IndexOf(':');
            var head = colon > 0 && colon <= 40 ? bare[..colon].Trim() : null;
            var rest = head is null ? "" : bare[(colon + 1)..].Trim();
            var key = Canon(head ?? bare);

            if (head is not null && rest.Length > 0 && Labels.TryGetValue(key, out var label))
            {
                Close();
                Apply(no, clean, label, rest);
                return;
            }
            if (NumLabel.Match(bare) is { Success: true } numbered)
            {
                Close();
                if (Labels.TryGetValue(Canon(numbered.Groups["l"].Value), out var numberedLabel))
                    Apply(no, clean, numberedLabel, numbered.Groups["v"].Value.Trim());
                else
                    Unread(no, clean);
                return;
            }

            // Spell-level headings ("1st", "2nd level (3 slots):"), then the known headings.
            var headingText = head ?? bare;
            if (SpellLevel.IsMatch(headingText))
            {
                Close();
                if (rest.Length == 0)
                    Enter(Section.Spells, hashes);
                else
                    List(no, clean, Section.Spells, rest);
                return;
            }
            if (Headings.TryGetValue(key, out var section) && (head is not null || hashes > 0 || !bulleted))
            {
                Close();
                if (rest.Length == 0)
                    Enter(section, hashes);
                else
                {
                    _section = Section.None; // an inline list does not carry on into the lines below it
                    List(no, clean, section, rest);
                }
                return;
            }

            if (hashes > 0 || (head is not null && rest.Length == 0 && (Enders.Contains(key) || _section is not (Section.Feats or Section.Features))))
            {
                // A heading nothing here reads. A title on the first line is the character's name; any other ends the list above it.
                Close();
                if (first && hashes == 1 && head is null && _titleName is null && bare.Length <= Character.MaxName)
                {
                    _titleName = bare;
                    return;
                }
                _section = Section.None;
                _sectionHashes = 0;
                Unread(no, clean);
                return;
            }

            Content(no, clean, plain, bare, bulleted, indent, afterBlank);
        }

        private void Enter(Section section, int hashes)
        {
            Close();
            _section = section;
            _sectionHashes = hashes;
            _listFilled = false;
        }

        // ---- labels ----

        private void Apply(int no, string clean, Label label, string value)
        {
            if (!_seen.Add(label))
            {
                Unread(no, clean); // a second line for the same label is not guessed at
                return;
            }
            switch (label)
            {
                case Label.Name:
                    if (value.Length <= Character.MaxName)
                        _name = Read<string>.Ok(value);
                    else
                        Unread(no, clean);
                    break;
                case Label.Class:
                    _class = (no, clean, value);
                    break;
                case Label.Level:
                    _level = (no, clean, value);
                    break;
                case Label.Subclass:
                    _subclass = (no, clean, value);
                    break;
                case Label.Race or Label.Species:
                    // Both words on one note: the first value is kept, the second line is not read, and no family is suggested.
                    _race |= label == Label.Race;
                    _speciesWord |= label == Label.Species;
                    if (value.Length <= 120 && _species.Status == ReadStatus.Missing)
                        _species = Read<string>.Ok(value);
                    else
                        Unread(no, clean);
                    break;
                case Label.Background:
                    if (value.Length <= 120)
                        _background = Read<string>.Ok(value);
                    else
                        Unread(no, clean);
                    break;
                case Label.Alignment: Detail(no, clean, "alignment", value); break;
                case Label.Age: Detail(no, clean, "age", value); break;
                case Label.Height: Detail(no, clean, "height", value); break;
                case Label.Weight: Detail(no, clean, "weight", value); break;
                case Label.Eyes: Detail(no, clean, "eyes", value); break;
                case Label.Skin: Detail(no, clean, "skin", value); break;
                case Label.Hair: Detail(no, clean, "hair", value); break;
                case Label.HitDice:
                    if (value.Length <= 100)
                        _hitDice = Read<string>.Ok(value);
                    else
                        Unread(no, clean);
                    break;
                case Label.Hp:
                    HitPoints(no, clean, value);
                    break;
                case Label.Ac: Number(no, clean, FieldIds.ArmorClass, value); break;
                case Label.Speed: Number(no, clean, FieldIds.Speed, value); break;
                case Label.Initiative: Number(no, clean, FieldIds.Initiative, value); break;
                case Label.ProfBonus: Number(no, clean, FieldIds.ProficiencyBonus, value); break;
                case Label.Passive: Number(no, clean, FieldIds.Passive("perception"), value); break;
            }
        }

        private void Detail(int no, string clean, string key, string value)
        {
            if (value.Length <= 300)
                _details[key] = Read<string>.Ok(value);
            else
                Unread(no, clean);
        }

        private static bool ReadWhole(string value, out int number)
        {
            number = 0;
            if (WholeNumber.Match(value) is not { Success: true } m)
                return false;
            number = int.Parse(m.Groups["d"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
            if (m.Groups["sign"].Value is "-" or "−")
                number = -number;
            return number is >= -999 and <= 9_999;
        }

        private void Number(int no, string clean, string field, string value)
        {
            if (ReadWhole(value, out var number))
                _numbers[field] = Read<int>.Ok(number);
            else
                Unread(no, clean);
        }

        private void HitPoints(int no, string clean, string value)
        {
            if (Hp.Match(value) is { Success: true } both)
            {
                var current = int.Parse(both.Groups["cur"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                var max = int.Parse(both.Groups["max"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                _currentHp = Read<int>.Ok(current);
                if (max >= 1)
                    _numbers[FieldIds.HitPoints] = Read<int>.Ok(max);
                return;
            }
            if (ReadWhole(value, out var only) && only >= 1)
                _numbers[FieldIds.HitPoints] = Read<int>.Ok(only);
            else
                Unread(no, clean);
        }

        // ---- abilities and coins ----

        private void Abilities(int no, string clean, string bare)
        {
            var bad = false;
            foreach (Match m in AbilityOne.Matches(bare))
            {
                if (!AbilityWords.TryGetValue(m.Groups["a"].Value.ToLowerInvariant(), out var ability))
                {
                    bad = true;
                    continue;
                }
                var score = int.Parse(m.Groups["s"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                if (score is < 1 or > 30 || _abilities.ContainsKey(ability))
                {
                    bad = true;
                    continue;
                }
                _abilities[ability] = Read<int>.Ok(score);
            }
            if (bad)
            {
                foreach (Match m in AbilityOne.Matches(bare))
                {
                    if (AbilityWords.TryGetValue(m.Groups["a"].Value.ToLowerInvariant(), out var ability) && !_abilities.ContainsKey(ability))
                        _abilities[ability] = Read<int>.Unreadable;
                }
                Unread(no, clean);
            }
        }

        private void Coins(int no, string clean, string bare)
        {
            var bad = false;
            foreach (Match m in CoinToken.Matches(bare))
                bad |= !SetCoin(no, clean, m.Groups["c"].Value.ToLowerInvariant(), m.Groups["n"].Value, listed: false);
            if (bad)
                Unread(no, clean);
        }

        private bool SetCoin(int no, string clean, string coin, string digits, bool listed = true)
        {
            if (!DdbSemantics.Coins.Contains(coin))
            {
                // Case folding can match a word that is no coin ("5 ſp"): nothing is stored under it.
                if (listed)
                    Unread(no, clean);
                return false;
            }
            if (!_currency.ContainsKey(coin) && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount <= Currency.MaxCoins)
            {
                _currency[coin] = Read<int>.Ok(amount);
                return true;
            }
            if (!_currency.ContainsKey(coin))
                _currency[coin] = Read<int>.Unreadable;
            if (listed)
                Unread(no, clean);
            return false;
        }

        // ---- lists ----

        private void Content(int no, string clean, string plain, string bare, bool bulleted, int indent, bool afterBlank)
        {
            switch (_section)
            {
                case Section.Feats or Section.Features:
                    Feature(no, clean, plain, bare, bulleted, indent, afterBlank);
                    break;
                case Section.None or Section.Proficiencies:
                    Unread(no, clean);
                    break;
                default:
                    // A list that already has entries ends at a blank line followed by a line with no list marker: that is prose.
                    if (afterBlank && !bulleted && _listFilled)
                    {
                        _section = Section.None;
                        _listFilled = false;
                        Unread(no, clean);
                        break;
                    }
                    List(no, clean, _section, bare);
                    _listFilled = true;
                    break;
            }
        }

        private bool _listFilled;

        /// <summary>The entries of one list line under (or after) a heading; a line with an entry that cannot be one is not read, whole.</summary>
        private void List(int no, string clean, Section section, string content)
        {
            var entries = Entries(content, [',', ';']);
            entries = [.. entries.Select(e => Marker.Match(e) is { Success: true } m ? e[m.Length..].Trim() : e).Where(e => e.Length > 0)];
            if (entries.Count == 0)
            {
                Unread(no, clean);
                return;
            }
            var bad = false;
            switch (section)
            {
                case Section.Spells:
                    // A bare count of slots after a level heading ("1st: 4 slots") is not a spell.
                    if (entries.Any(e => e.Length > MaxEntry) || entries.All(e => e.All(c => char.IsDigit(c) || c == ' ') || e.EndsWith("slots", StringComparison.OrdinalIgnoreCase) || e.EndsWith("slot", StringComparison.OrdinalIgnoreCase)))
                        bad = true;
                    else
                    {
                        foreach (var entry in entries)
                            AddSpell(entry);
                    }
                    break;
                case Section.Equipment:
                    if (entries.Any(e => e.Length > MaxEntry))
                        bad = true;
                    else
                        _equipment.AddRange(entries);
                    break;
                case Section.Saves:
                    foreach (var entry in entries)
                    {
                        if (AbilityWords.TryGetValue(Word(entry), out var ability))
                            _saves[ability] = true;
                        else
                            bad = true;
                    }
                    _saveListSeen = true;
                    break;
                case Section.Skills:
                    foreach (var entry in entries)
                    {
                        if (SkillKeys.TryGetValue(LettersOnly(Word(entry)), out var skill))
                            _skills[skill] = true;
                        else
                            bad = true;
                    }
                    _skillListSeen = true;
                    break;
                case Section.Languages:
                    _languages.AddRange(entries);
                    break;
                case Section.Tools:
                    _tools.AddRange(entries);
                    break;
                case Section.Feats or Section.Features:
                    var saved = _section;
                    _section = section;
                    foreach (var entry in entries)
                    {
                        if (entry.Length <= MaxFeatureName)
                            AddItem(entry, null);
                        else
                            bad = true;
                    }
                    Close();
                    _section = saved;
                    break;
                default:
                    bad = true;
                    break;
            }
            if (bad)
                Unread(no, clean);
        }

        private bool _saveListSeen;
        private bool _skillListSeen;

        /// <summary>The leading letters of an entry ("Stealth +5 (expertise)" gives "Stealth"), for the saves and skills lists.</summary>
        private static string Word(string entry)
        {
            var end = 0;
            while (end < entry.Length && (char.IsLetter(entry[end]) || entry[end] is ' ' or '\''))
                end++;
            return entry[..end].Trim().ToLowerInvariant().Replace("  ", " ", StringComparison.Ordinal);
        }

        /// <summary>
        /// Adds a spell up to the list cap. The first one past it becomes one unreadable placeholder (so it still gets a gap note);
        /// every one past the cap is counted, for the report (a number, never a name).
        /// </summary>
        private void AddSpell(string entry)
        {
            if (_spells.Count < DdbParser.MaxListItems)
            {
                _spells.Add(Read<SpellText>.Ok(new(entry, null)));
                return;
            }
            if (_spells.Count == DdbParser.MaxListItems)
                _spells.Add(Read<SpellText>.Unreadable);
            _spellsBeyondCap++;
        }

        private int _spellsBeyondCap;

        // ---- feats and features: "Name: description", or a name with the lines under it ----

        private void Feature(int no, string clean, string plain, string bare, bool bulleted, int indent, bool afterBlank)
        {
            if (bulleted && indent < 2)
            {
                NewItem(no, clean, bare);
                return;
            }
            var colon = bare.IndexOf(':');
            if (colon > 0 && bare[..colon].Trim() is { } named && NameLike(named))
            {
                var description = bare[(colon + 1)..].Trim();
                AddItem(named, description.Length > 0 ? description : null);
                return;
            }
            if ((_open is not null || _dropped) && !afterBlank)
            {
                if (_open is null)
                    _beyondCap++;
                else
                    _open.Lines.Add(plain);
                return;
            }
            var names = Entries(bare, [',']);
            if (names.Count > 1 && !bare.EndsWith('.'))
            {
                Close();
                if (names.All(NameLike))
                {
                    foreach (var name in names)
                        AddItem(name, null);
                    Close();
                    return;
                }
            }
            NewItem(no, clean, bare, wholeLine: true);
        }

        /// <summary>A new entry from a line: "Name: description", or a name alone.</summary>
        private void NewItem(int no, string clean, string bare, bool wholeLine = false)
        {
            var colon = wholeLine ? -1 : bare.IndexOf(':');
            if (colon > 0 && bare[..colon].Trim() is { Length: > 0 } named && NameLike(named))
            {
                var description = bare[(colon + 1)..].Trim();
                AddItem(named, description.Length > 0 ? description : null);
                return;
            }
            var name = bare.TrimEnd(':').Trim();
            if (name.Length is > 0 and <= MaxFeatureName)
                AddItem(name, null);
            else
            {
                Close();
                Unread(no, clean);
            }
        }

        private void AddItem(string name, string? description)
        {
            Close();
            var kind = _section == Section.Feats ? FeatureKind.Feat : FeatureKind.Feature;
            var list = kind == FeatureKind.Feat ? _feats : _features;
            if (list.Count >= DdbParser.MaxListItems)
            {
                if (list.Count == DdbParser.MaxListItems)
                    list.Add(Read<string>.Unreadable);
                _beyondCap++;
                _dropped = true; // its continuation lines are skipped too, not taken as new names
                return;
            }
            var open = new OpenItem(kind, name, list.Count);
            list.Add(Read<string>.Ok(name));
            if (!string.IsNullOrWhiteSpace(description))
                open.Lines.Add(description.Trim());
            _open = open;
        }

        /// <summary>Ends the entry being read: its lines become its text, when it has any.</summary>
        private void Close()
        {
            if (_open is { } open)
            {
                var body = string.Join('\n', open.Lines).Trim();
                if (body.Length > 0 && _texts.Count < DdbParser.MaxListItems)
                    _texts.Add(new FeatureText(open.Name, open.Kind, body, open.Index));
            }
            _open = null;
            _dropped = false;
        }

        /// <summary>True while the entry being read was dropped for the list cap; its lines are counted and skipped.</summary>
        private bool _dropped;

        private int _beyondCap;

        // ---- the sheet ----

        public DdbSheet Finish()
        {
            Close();
            var classes = ResolveClasses();
            var lines = new List<string>();
            if (_longLines > 0)
                lines.Add($"{_longLines.ToString("N0", CultureInfo.InvariantCulture)} line{(_longLines == 1 ? " was" : "s were")} longer than {MaxLineChars.ToString("N0", CultureInfo.InvariantCulture)} characters and {(_longLines == 1 ? "was" : "were")} not read.");
            if (_beyondCap > 0)
                lines.Add($"{_beyondCap.ToString("N0", CultureInfo.InvariantCulture)} line{(_beyondCap == 1 ? " was" : "s were")} beyond the {DdbParser.MaxListItems.ToString("N0", CultureInfo.InvariantCulture)}-entry limit of the feats and features lists and {(_beyondCap == 1 ? "was" : "were")} not read.");
            if (_spellsBeyondCap > 0)
                lines.Add($"{_spellsBeyondCap.ToString("N0", CultureInfo.InvariantCulture)} spell{(_spellsBeyondCap == 1 ? " was" : "s were")} beyond the {DdbParser.MaxListItems.ToString("N0", CultureInfo.InvariantCulture)}-entry limit of the spell list and {(_spellsBeyondCap == 1 ? "was" : "were")} not read.");
            lines.AddRange(_notes);

            var abilities = Enum.GetValues<Ability>().ToDictionary(a => a, a => _abilities.TryGetValue(a, out var read) ? read : Read<int>.Missing);
            var saves = Enum.GetValues<Ability>().ToDictionary(a => a, a => _saveListSeen ? Read<bool>.Ok(_saves.ContainsKey(a)) : Read<bool>.Missing);
            var skills = CharacterCalculator.Skills.ToDictionary(s => s.Key, s => _skillListSeen ? Read<bool>.Ok(_skills.ContainsKey(s.Key)) : Read<bool>.Missing, StringComparer.Ordinal);

            var proficiencies = new List<string>();
            if (_languages.Count > 0)
                proficiencies.Add("Languages: " + string.Join(", ", _languages));
            if (_tools.Count > 0)
                proficiencies.Add("Tools: " + string.Join(", ", _tools));
            var texts = new DdbTexts(
                new Dictionary<string, Read<string>>(_details, StringComparer.Ordinal),
                new Dictionary<string, Read<string>>(StringComparer.Ordinal),
                [],
                _equipment.Count > 0 ? Read<string>.Ok(string.Join('\n', _equipment)) : Read<string>.Missing,
                proficiencies.Count > 0 ? Read<string>.Ok(string.Join('\n', proficiencies)) : Read<string>.Missing);

            var name = _name.Status == ReadStatus.Ok ? _name : _titleName is { } title ? Read<string>.Ok(title) : Read<string>.Missing;
            var family = _race == _speciesWord ? null : _race ? RulesFamilies.Srd51 : RulesFamilies.Srd521;
            var play = new DdbPlay(_currentHp, Read<int>.Missing, [], Read<int>.Missing, Read<int>.Missing, Read<bool>.Missing, []);
            var currency = DdbSemantics.Coins.Where(_currency.ContainsKey).ToDictionary(c => c, c => _currency[c], StringComparer.Ordinal);
            var unread = _unread.OrderBy(n => n.Line).ToList();

            return new DdbSheet(
                LayoutMaps.TextId, family, name, classes, _species, _background, abilities, saves, skills,
                _feats, _spells, [], _features, new Dictionary<string, Read<int>>(_numbers, StringComparer.Ordinal), play, currency,
                lines.Count == 0 ? null : lines, texts, _hitDice, _texts.Count == 0 ? null : _texts, unread);
        }

        private readonly List<string> _notes = [];

        /// <summary>The class line and the level line together; a line that cannot be used is a line not read.</summary>
        private Read<IReadOnlyList<ClassText>> ResolveClasses()
        {
            if (_class is not { } line)
            {
                if (_level is { } stray)
                    Unread(stray.No, stray.Clean);
                if (_subclass is { } straySub)
                    Unread(straySub.No, straySub.Clean);
                return Read<IReadOnlyList<ClassText>>.Missing;
            }
            int? lone = null;
            if (_level is { } levelLine)
            {
                try
                {
                    if (LevelValue.Match(levelLine.Value) is { Success: true } m)
                        lone = int.Parse(m.Groups["level"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                }
                catch (RegexMatchTimeoutException)
                {
                    lone = null;
                }
            }
            var parts = Entries(line.Value, ['/', ',']);
            var classes = new List<ClassText>();
            var usedLone = false;
            var ok = parts.Count is >= 1 and <= 20;
            try
            {
                foreach (var part in ok ? parts : [])
                {
                    var m = ClassPart.Match(part);
                    var name = m.Groups["name"].Value.Trim();
                    if (!m.Success || name.Length == 0)
                    {
                        ok = false;
                        break;
                    }
                    int level;
                    if (m.Groups["level"].Success)
                        level = int.Parse(m.Groups["level"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                    else if (parts.Count == 1 && lone is { } given)
                    {
                        level = given;
                        usedLone = true;
                    }
                    else
                    {
                        ok = false;
                        break;
                    }
                    classes.Add(new ClassText(name, level, m.Groups["sub"].Success && m.Groups["sub"].Value.Trim() is { Length: > 0 } sub ? sub : null));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                ok = false;
            }
            var total = classes.Sum(c => c.Level);
            ok = ok && classes.All(c => c.Level is >= 1 and <= 20) && total is >= 1 and <= 20;
            if (!ok)
            {
                Unread(line.No, line.Clean);
                if (_level is { } unusedLevel)
                    Unread(unusedLevel.No, unusedLevel.Clean);
                if (_subclass is { } unusedSub)
                    Unread(unusedSub.No, unusedSub.Clean);
                return Read<IReadOnlyList<ClassText>>.Unreadable;
            }
            if (_level is not null && !usedLone && lone != total)
                _notes.Add("The level line does not match the class levels; the class levels were used.");
            if (_subclass is { } subclass)
            {
                if (classes.Count == 1 && classes[0].Subclass is null && subclass.Value.Length <= 60)
                    classes[0] = classes[0] with { Subclass = subclass.Value };
                else
                    Unread(subclass.No, subclass.Clean);
            }
            return Read<IReadOnlyList<ClassText>>.Ok(classes);
        }
    }
}
