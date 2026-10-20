using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TomeStack.RulesCore;

namespace TomeStack.ImportWorker.Detection;

/// <summary>One extracted page, as detection reads it.</summary>
public sealed record DetectionPage(int Page, string Text, IReadOnlyList<TextBlock> Blocks, bool FromOcr);

/// <param name="SourceFamilies">The families the source declares; a candidate's family comes from its layout, within these.</param>
/// <param name="IsInstalled">Whether content of that name is installed (for unresolved references).</param>
/// <param name="IsOneInstalledFeature">
/// Ruling R67: whether exactly one installed published feature has that name (a class's Features-column name is then granted
/// from it at accept, not given a stub). Null: <paramref name="IsInstalled"/> decides, as before.
/// </param>
public sealed record DetectionContext(Guid SourceId, IReadOnlyList<string> SourceFamilies, Func<string, bool> IsInstalled, Func<string, bool>? IsOneInstalledFeature = null);

/// <param name="Candidates">At most <see cref="CandidateDetector.MaxCandidates"/>, in page order.</param>
/// <param name="Truncated">More were found than the cap allows.</param>
/// <param name="Links">Item 11 (R39, R54): which feature candidate each class candidate grants, at which class level; both ends are in <paramref name="Candidates"/>.</param>
public sealed record DetectionResult(IReadOnlyList<DraftCandidate> Candidates, bool Truncated, IReadOnlyList<CandidateLink> Links);

/// <summary>A class candidate grants a feature candidate at a class level (a transient detection result; stored as <c>grantsCandidates</c> on the class).</summary>
public sealed record CandidateLink(Guid ClassId, Guid FeatureId, int Level);

/// <summary>
/// M4 D3 (SPEC I-01, I-02; ADR-004): deterministic, rule-based detection of SRD-shaped blocks in extracted page text. No
/// AI (an optional local model is M7). It reads the page blocks and their positioned lines and proposes
/// <see cref="DraftCandidate"/>s only: spells, feats, class features, weapon and armor table rows, and class feature
/// tables. Each carries its excerpt, page, kind, name, rules family, proposed effects, a confidence (a UI hint, never
/// permission), uncertainties, low-confidence fields and unresolved references. The input is text, not PDF bytes, so it
/// runs in the app; every pattern is anchored and has a match timeout.
/// </summary>
public static partial class CandidateDetector
{
    public const int MaxExcerpt = 4_000;
    public const int MaxCandidates = 5_000;
    private const int MaxDescriptionLines = 80;

    private static readonly string[] Schools = ["abjuration", "conjuration", "divination", "enchantment", "evocation", "illusion", "necromancy", "transmutation"];

    internal sealed record Unit(int Page, int Index, TextBlock Block, string[] Lines, bool FromOcr);

    private sealed record Row(int Page, double Y, List<TextLine> Cells);

    /// <param name="cancellationToken">Checked between passes and pages: cancelling an import also stops its detection.</param>
    public static IReadOnlyList<DraftCandidate> Detect(IReadOnlyList<DetectionPage> pages, DetectionContext context, CancellationToken cancellationToken = default) =>
        DetectWithLimit(pages, context, cancellationToken: cancellationToken).Candidates;

    /// <summary>
    /// <see cref="Detect"/> and whether more than <see cref="MaxCandidates"/> were found (the cap applies before the
    /// reviewer's earlier decisions are taken out, so it is reported as it is).
    /// </summary>
    /// <param name="maxCandidates">The cap; <see cref="MaxCandidates"/> unless a test lowers it.</param>
    public static DetectionResult DetectWithLimit(IReadOnlyList<DetectionPage> pages, DetectionContext context, int maxCandidates = MaxCandidates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(context);
        var boilerplate = Boilerplate(pages);
        var units = Units(pages, boilerplate);
        var body = BodySize(units);
        var found = new List<DraftCandidate>();
        var classFeatures = new Dictionary<Guid, IReadOnlyDictionary<int, IReadOnlyList<string>>>();
        cancellationToken.ThrowIfCancellationRequested();
        found.AddRange(ApplySpellLists(Spells(units, context, body).ToList(), SpellListPages(units, body)));
        cancellationToken.ThrowIfCancellationRequested();
        found.AddRange(Feats(units, context, body));
        cancellationToken.ThrowIfCancellationRequested();
        found.AddRange(Features(units, context, body));
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = PageLines(page, boilerplate);
            found.AddRange(Weapons(page, lines, context));
            found.AddRange(Armor(page, lines, context));
            found.AddRange(ClassTables(page, lines, units.Where(u => u.Page == page.Page).ToList(), context, classFeatures, () => FollowingText(pages, page, boilerplate), pages.Where(p => p.Page == page.Page - 1).Select(p => PageLines(p, boilerplate)).FirstOrDefault() ?? [], cancellationToken));
        }

        // Item 11 (R54): what each class's Features column names becomes a link to a detected feature, or to a reference stub.
        var links = LinkClasses(found, classFeatures, context, cancellationToken);

        // A candidate's references resolve against installed content and the job's other candidates.
        var named = found.Select(c => Key(c.ProposedName)).ToHashSet(StringComparer.Ordinal);
        var ordered = found
            .Select(c => c with { UnresolvedReferences = [.. c.UnresolvedReferences.Where(r => !named.Contains(Key(r)) && !context.IsInstalled(r)).Distinct(StringComparer.OrdinalIgnoreCase)] })
            .OrderBy(c => c.Page.Start).ThenBy(c => c.ProposedKind).ThenBy(c => c.ProposedName, StringComparer.Ordinal)
            .ToList();
        var taken = ordered.Take(maxCandidates).ToList();
        var takenIds = taken.Select(c => c.Id).ToHashSet();
        return new([.. taken], ordered.Count > maxCandidates, [.. links.Where(l => takenIds.Contains(l.ClassId) && takenIds.Contains(l.FeatureId))]);
    }

    /// <summary>
    /// Item 11 (R54, review fix): each class links the features its table names, owner-aware. Target order for a class C at page P with
    /// the next class table at page N: a detected feature whose class field names C; else the nearest detected feature with no class
    /// field in [P, N); else (R67) no link when exactly one installed feature has the name; else a stub owned by C. A detected feature
    /// that names another class is never C's target, and a stub is never shared between classes. Two classes on one page have no window of
    /// their own on that page (a feature there is placed under neither), and the pages after it belong to the last of them. New stubs are appended to <paramref name="found"/>.
    /// </summary>
    internal static List<CandidateLink> LinkClasses(List<DraftCandidate> found, IReadOnlyDictionary<Guid, IReadOnlyDictionary<int, IReadOnlyList<string>>> classFeatures, DetectionContext context, CancellationToken cancellationToken = default)
    {
        var owners = found.Where(c => c.ProposedKind == ContentKind.Class).ToDictionary(c => c.Id);
        var classPages = owners.Values.Select(c => c.Page.Start).Distinct().Order().ToList();
        var onPage = owners.Values.GroupBy(c => c.Page.Start).ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList()); // in detection order: top to bottom
        var detected = found.Where(c => c.ProposedKind == ContentKind.Feature).OrderBy(c => c.Page.Start).ToLookup(c => Key(c.ProposedName));
        var links = new List<CandidateLink>();
        foreach (var (classId, features) in classFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = owners[classId];
            var classKey = Key(owner.ProposedName);
            var start = owner.Page.Start;
            var next = classPages.Where(p => p > start).Select(p => (int?)p).FirstOrDefault();
            // Classes sharing a page: a feature on that page cannot be placed under one of them, and the pages after it belong to the last.
            var sharesPage = onPage[start].Count > 1;
            var lastOnPage = onPage[start][^1] == classId;
            bool InWindow(DraftCandidate c) => c.Page.Start == start ? !sharesPage : c.Page.Start > start && (next is null || c.Page.Start < next) && lastOnPage;
            foreach (var name in features.SelectMany(f => f.Value.Select(n => (Name: n, Level: f.Key))).Where(f => IsLinkable(f.Name)).GroupBy(f => Key(f.Name)).Select(g => (g.First().Name, Levels: g.Select(x => x.Level).Distinct().Order().ToList())))
            {
                var candidates = detected[Key(name.Name)].ToList();
                var target = candidates.FirstOrDefault(c => c.Fields.TryGetValue("class", out var named) && Key(named) == classKey)
                    ?? candidates.FirstOrDefault(c => !c.Fields.ContainsKey("class") && InWindow(c));
                if (target is null)
                {
                    // R67: one installed feature of that name is granted from at accept; two, or none, and the name gets a stub (R54).
                    if (context.IsOneInstalledFeature?.Invoke(name.Name) ?? context.IsInstalled(name.Name))
                        continue;
                    target = FeatureStub(owner, name.Name, name.Levels[0], context);
                    found.Add(target);
                }
                links.AddRange(name.Levels.Select(level => new CandidateLink(owner.Id, target.Id, level)));
            }
        }
        return links;
    }

    /// <summary>A feature name worth a link: not "Ability Score Improvement" and not a subclass placeholder ("Fixture Order feature").</summary>
    public static bool IsLinkable(string name) =>
        !name.Equals("Ability Score Improvement", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("feature", StringComparison.OrdinalIgnoreCase);

    /// <summary>A feature the class table names that no block of the job describes: the name and the class page only, reference.</summary>
    private static DraftCandidate FeatureStub(DraftCandidate owner, string name, int level, DetectionContext context)
    {
        var (families, uncertainties, _) = NeutralFamily(context);
        uncertainties.Add("Only the name was found, in the class table: add its text by hand, or accept it as reference.");
        return new DraftCandidate(
            Guid.NewGuid(), context.SourceId, owner.Page, $"{owner.ProposedName}, level {level}: {name}", ContentKind.Feature, name, families, [], 0.3, uncertainties)
        {
            Fields = new Dictionary<string, string> { ["class"] = owner.ProposedName, ["level"] = level.ToString(CultureInfo.InvariantCulture) },
        };
    }

    /// <summary>Case, spacing and apostrophes do not matter when names are compared.</summary>
    public static string Key(string name) =>
        string.Join(' ', name.Replace('’', '\'').ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ---- layout ----

    /// <summary>Running headers and footers: text (digits ignored) on at least a third of the pages, and on 3 or more.</summary>
    private static HashSet<string> Boilerplate(IReadOnlyList<DetectionPage> pages)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            foreach (var shape in page.Blocks.Select(b => Shape(b.Text)).Distinct())
                counts[shape] = counts.GetValueOrDefault(shape) + 1;
        }
        var threshold = Math.Max(3, pages.Count / 3);
        return [.. counts.Where(c => c.Value >= threshold && c.Key.Length <= 80).Select(c => c.Key)];
    }

    private static string Shape(string text) => Digits().Replace(text.Trim(), "#");

    private static List<Unit> Units(IReadOnlyList<DetectionPage> pages, HashSet<string> boilerplate)
    {
        var units = new List<Unit>();
        foreach (var page in pages.OrderBy(p => p.Page))
        {
            foreach (var block in page.Blocks)
            {
                if (boilerplate.Contains(Shape(block.Text)) || string.IsNullOrWhiteSpace(block.Text))
                    continue;
                string[] lines = [.. block.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];
                // A one-line heading block followed by a block that starts with a header's second line ("Level 2
                // Evocation (…)", "Origin Feat", "Level 3 … Feature") is one header, however the PDF split it.
                if (units.Count > 0 && units[^1] is { Lines.Length: 1 } previous && previous.Page == page.Page && LooksLikeName(previous.Lines[0]) && IsSecondHeaderLine(lines[0]))
                {
                    units[^1] = previous with { Lines = [.. previous.Lines, .. lines] };
                    continue;
                }
                units.Add(new(page.Page, units.Count, block, lines, page.FromOcr));
            }
        }
        return units;
    }

    /// <summary>The start of a header's second line: a spell's level and school (its class list may wrap), a feat's category, or "Level N … Feature".</summary>
    private static bool IsSecondHeaderLine(string line) =>
        Spell51().IsMatch(line) || SpellLineStart().IsMatch(line) || OwnedFeature().IsMatch(line) || FeatLineStart().IsMatch(line);

    /// <summary>The body text size: the most common font size, weighted by text length.</summary>
    private static double BodySize(List<Unit> units)
    {
        var sizes = units.Where(u => u.Block.FontSize > 0).GroupBy(u => Math.Round(u.Block.FontSize)).Select(g => (Size: g.Key, Weight: g.Sum(u => u.Block.Text.Length))).ToList();
        return sizes.Count == 0 ? 10 : sizes.MaxBy(g => g.Weight).Size;
    }

    private static bool IsHeading(Unit unit, double body) => unit.Block.Bold && unit.Block.FontSize >= body * 1.2 && unit.Lines.Length <= 2;

    /// <summary>Every positioned line of a page (OCR blocks count as one line each), without running headers and footers.</summary>
    private static List<(int Page, TextLine Line)> PageLines(DetectionPage page, HashSet<string> boilerplate) =>
    [
        .. page.Blocks.Where(b => !boilerplate.Contains(Shape(b.Text)))
            .SelectMany(b => b.Lines is { Count: > 0 } lines ? lines : [new TextLine(b.Text, b.X, b.Y, b.Width, b.Height)])
            .Where(l => l.Text.Trim().Length > 0)
            .Select(l => (page.Page, l with { Text = l.Text.Trim() })),
    ];

    /// <summary>The lines at the same height as <paramref name="anchor"/>, left to right.</summary>
    private static List<TextLine> SameRow(List<(int Page, TextLine Line)> lines, TextLine anchor) => SameRow(lines.Select(l => l.Line), anchor);

    /// <summary>The same rule over bare lines, shared with the class-table reconstruction.</summary>
    internal static List<TextLine> SameRow(IEnumerable<TextLine> lines, TextLine anchor) =>
        [.. lines.Where(l => Math.Abs(l.Y - anchor.Y) <= Math.Max(2.5, anchor.Height * 0.4)).OrderBy(l => l.X)];

    // ---- text helpers ----

    /// <summary>Joins lines into paragraphs, repairing a word split by a line-end hyphen ("En-" + "tertainer").</summary>
    private static string Join(IEnumerable<string> lines)
    {
        var text = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (text.Length > 0)
            {
                if (text[^1] == '-' && char.IsLower(line[0]))
                    text.Length--; // "En-" + "tertainer": a word split at the line end
                else if (text[^1] != '-')
                    text.Append(' '); // "Two-" + "Handed" keeps its hyphen and joins
            }
            text.Append(line);
        }
        return text.ToString();
    }

    private static string Excerpt(IEnumerable<string> lines)
    {
        var text = string.Join('\n', lines);
        return text.Length <= MaxExcerpt ? text : text[..MaxExcerpt];
    }

    private static bool LooksLikeName(string line) =>
        line.Length is >= 2 and <= 60 && char.IsUpper(line[0]) && !line.EndsWith('.') && !line.Contains(':', StringComparison.Ordinal) && line.Split(' ').Length <= 7;

    private static readonly HashSet<string> SmallWords = new(StringComparer.Ordinal) { "of", "the", "and", "a", "an", "to", "in", "on", "with", "for", "or" };

    /// <summary>
    /// A name printed in capitals ("FIXTURE BLADE OF DUSK") becomes title case ("Fixture Blade of Dusk"), with small words in
    /// lower case after the first. A name with any lower-case letter, or with fewer than three letters, is returned as it is.
    /// </summary>
    private static string TitleCase(string name)
    {
        var letters = 0;
        foreach (var c in name)
        {
            if (char.IsLower(c))
                return name;
            if (char.IsLetter(c))
                letters++;
        }
        if (letters < 3)
            return name;
        var text = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var startsWord = i == 0 || !(char.IsLetter(name[i - 1]) || name[i - 1] is '\'' or '’');
            text.Append(startsWord ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        var words = text.ToString().Split(' ');
        for (var i = 1; i < words.Length; i++)
        {
            if (SmallWords.Contains(words[i].ToLowerInvariant()))
                words[i] = words[i].ToLowerInvariant();
        }
        return string.Join(' ', words);
    }

    /// <summary>A price inside an item name or a sentence fragment ("…, which counts as") is a table cell misread as a name.</summary>
    private static bool IsJunkItemName(string name) => JunkItemName().IsMatch(name);

    /// <summary>A heading-like line: a name whose words (apart from small ones) all start with a capital letter.</summary>
    private static bool IsTitleCase(string line) =>
        LooksLikeName(line) && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => SmallWords.Contains(w) || char.IsUpper(w[0]) || !char.IsLetter(w[0]));

    /// <summary>A table cell name, not prose: with three or more words, no more than half of them start in lower case.</summary>
    private static bool LooksLikeCellName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return LooksLikeName(name) && (words.Length < 3 || words.Count(w => char.IsLower(w[0])) * 2 <= words.Length);
    }

    private static (IReadOnlyList<string> Families, List<string> Uncertainties) Family(string layout, DetectionContext context)
    {
        if (context.SourceFamilies.Contains(layout))
            return ([layout], []);
        return (context.SourceFamilies, [$"The layout looks like {layout}, which is outside the families this reading is for; those families are used."]);
    }

    /// <summary>
    /// 2026-10-14 item 3: a layout-neutral kind (armour rows, class tables) takes the context's family when there is one.
    /// With two, the reviewer says which: no family is proposed, and the field is marked unsure.
    /// </summary>
    private static (IReadOnlyList<string> Families, List<string> Notes, bool Unsure) NeutralFamily(DetectionContext context) =>
        context.SourceFamilies.Count == 1
            ? ([context.SourceFamilies[0]], [], false)
            : ([], ["Say which rules family this is written for before accepting."], true);

    /// <summary>The description after a header: the following blocks until the next header or heading, at most 80 lines.</summary>
    private static List<string> Description(List<Unit> units, int start, double body, Func<Unit, bool> isHeader, out int endPage)
    {
        var lines = new List<string>();
        endPage = start < units.Count ? units[start].Page : 0;
        for (var i = start; i < units.Count && lines.Count < MaxDescriptionLines; i++)
        {
            var unit = units[i];
            if (isHeader(unit) || IsHeading(unit, body) || unit.Block.FontSize >= body * 1.5)
                break;
            if (lines.Count > 0)
                lines.Add("");
            lines.AddRange(unit.Lines);
            endPage = unit.Page;
        }
        return lines;
    }

    private static string Paragraphs(List<string> lines) =>
        string.Join("\n\n", string.Join('\n', lines).Split("\n\n").Select(p => Join(p.Split('\n'))).Where(p => p.Length > 0));

    private static double Clamp(double confidence) => Math.Round(Math.Clamp(confidence, 0.05, 0.99), 2);

    private static IEnumerable<string> ReferencedSpells(string text) =>
        CastsSpell().Matches(text).Select(m => m.Groups["name"].Value.Trim())
            .Concat(NamedSpell().Matches(text).Select(m => m.Groups["name"].Value.Trim()))
            .Where(n => n.Length > 2 && !n.StartsWith("The ", StringComparison.Ordinal) && !IgnoredReferences.Contains(n));

    private static readonly HashSet<string> IgnoredReferences = new(StringComparer.OrdinalIgnoreCase) { "It", "A", "An", "This", "That", "Spells", "Spell" };

    // ---- spells ----

    private sealed record SpellHeader(string Name, int Level, string School, bool Ritual, IReadOnlyList<string> Lists, string Family, int HeaderLines)
    {
        /// <summary>How many class names in the header could not become a list key.</summary>
        public int DroppedLists { get; init; }
    }

    private static SpellHeader? ReadSpellHeader(Unit unit)
    {
        if (unit.Lines.Length < 2 || !LooksLikeName(unit.Lines[0]))
            return null;
        // SRD 5.2.1: "Level 2 Evocation (Sorcerer, Wizard)" or "Evocation Cantrip (Sorcerer)"; the class list may wrap.
        for (var take = 1; take <= Math.Min(3, unit.Lines.Length - 1); take++)
        {
            var header = string.Join(' ', unit.Lines.Skip(1).Take(take));
            if (Spell521().Match(header) is { Success: true } modern)
            {
                var level = modern.Groups["level"].Success ? int.Parse(modern.Groups["level"].Value, CultureInfo.InvariantCulture) : 0;
                // Each class is a spell list key (the one normaliser, review fix 4); a name that cannot be one is dropped and counted.
                var lists = new List<string>();
                var dropped = 0;
                foreach (var name in modern.Groups["lists"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!SpellListKey.TryNormalise(name, out var key))
                        dropped++;
                    else if (!lists.Contains(key, StringComparer.Ordinal))
                        lists.Add(key);
                }
                return new(TitleCase(unit.Lines[0]), level, modern.Groups["school"].Value.ToLowerInvariant(), false, lists, RulesFamilies.Srd521, 1 + take) { DroppedLists = dropped };
            }
        }
        if (Spell51().Match(unit.Lines[1]) is { Success: true } old)
        {
            var level = old.Groups["level"].Success ? int.Parse(old.Groups["level"].Value, CultureInfo.InvariantCulture) : 0;
            return new(TitleCase(unit.Lines[0]), level, old.Groups["school"].Value.ToLowerInvariant(), old.Groups["ritual"].Success, [], RulesFamilies.Srd51, 2);
        }
        return null;
    }

    private static IEnumerable<DraftCandidate> Spells(List<Unit> units, DetectionContext context, double body)
    {
        for (var i = 0; i < units.Count; i++)
        {
            if (ReadSpellHeader(units[i]) is not { } header)
                continue;
            var unit = units[i];
            // The stat lines follow the header, in the same block (SRD 5.1) or the next blocks.
            var stream = unit.Lines.Skip(header.HeaderLines).ToList();
            var next = i + 1;
            while (!stream.Any(l => l.StartsWith("Duration:", StringComparison.Ordinal)) && next < units.Count && next <= i + 3 && ReadSpellHeader(units[next]) is null)
                stream.AddRange(units[next++].Lines);

            var stats = new Dictionary<string, string>(StringComparer.Ordinal);
            var description = new List<string>();
            string? current = null;
            foreach (var line in stream)
            {
                if (StatLine().Match(line) is { Success: true } stat && description.Count == 0)
                {
                    current = stat.Groups["label"].Value;
                    stats[current] = stat.Groups["value"].Value.Trim();
                }
                else if (current is not null && current != "Duration" && description.Count == 0)
                    stats[current] = Join([stats[current], line]);
                else
                    description.Add(line);
            }
            var more = Description(units, next, body, u => ReadSpellHeader(u) is not null, out var endPage);
            if (more.Count > 0 && description.Count > 0)
                description.Add("");
            description.AddRange(more);
            var text = Paragraphs(description);

            var (families, uncertainties) = Family(header.Family, context);
            var low = new List<string>();
            var confidence = 0.95;
            foreach (var label in new[] { "Casting Time", "Range", "Components", "Duration" })
            {
                if (!stats.ContainsKey(label))
                {
                    confidence -= 0.1;
                    uncertainties.Add($"No '{label}' line was found.");
                    low.Add(label.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant());
                }
            }
            if (text.Length == 0)
            {
                confidence -= 0.2;
                uncertainties.Add("No description was found after the spell's details.");
            }
            if (header.DroppedLists > 0)
                uncertainties.Add($"{header.DroppedLists.ToString(CultureInfo.InvariantCulture)} class list(s) in the spell's header on page {unit.Page.ToString(CultureInfo.InvariantCulture)} could not be read as a list key; add the list by hand.");
            if (header.Lists.Count == 0)
                uncertainties.Add(NoListsNote);
            if (unit.FromOcr)
            {
                confidence -= 0.15;
                uncertainties.Add("OCR text: check the name and numbers against the page.");
            }
            var dice = DiceExpression().Matches(text).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();
            if (dice.Count > 1)
                uncertainties.Add($"Several dice appear in the text; the first ({dice[0]}) is proposed.");
            var duration = stats.GetValueOrDefault("Duration");
            var castingTime = stats.GetValueOrDefault("Casting Time");
            var save = SavingThrow().Match(text);
            var effect = new SpellEffect
            {
                Id = "spell",
                Level = header.Level,
                School = header.School,
                CastingTime = castingTime,
                Range = stats.GetValueOrDefault("Range"),
                Components = stats.GetValueOrDefault("Components"),
                Duration = duration,
                Concentration = duration?.StartsWith("Concentration", StringComparison.OrdinalIgnoreCase) == true,
                Ritual = header.Ritual || castingTime?.Contains("Ritual", StringComparison.OrdinalIgnoreCase) == true,
                Lists = header.Lists,
                Attack = text.Contains("ranged spell attack", StringComparison.OrdinalIgnoreCase) ? SpellAttackKind.Ranged
                    : text.Contains("melee spell attack", StringComparison.OrdinalIgnoreCase) ? SpellAttackKind.Melee : SpellAttackKind.None,
                Save = save.Success ? Enum.Parse<Ability>(save.Groups["ability"].Value[..3], ignoreCase: true) : null,
                Dice = dice.FirstOrDefault(),
                Text = text.Length == 0 ? null : text,
            };
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["level"] = header.Level.ToString(CultureInfo.InvariantCulture),
                ["school"] = header.School,
            };
            foreach (var (label, value) in stats)
                fields[label.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant()] = value;
            if (header.Lists.Count > 0)
                fields["lists"] = string.Join(", ", header.Lists);

            yield return new DraftCandidate(
                Guid.NewGuid(), context.SourceId, new PageRef(unit.Page, endPage > unit.Page ? endPage : null), Excerpt(unit.Lines.Concat(stream).Concat(description)),
                ContentKind.Spell, header.Name, families, [effect], Clamp(confidence), uncertainties)
            {
                Fields = fields,
                LowConfidenceFields = low,
                Summary = text.Length == 0 ? null : text,
            };
        }
    }

    // ---- spell list pages (item 10) ----

    /// <summary>The uncertainty a spell gets when no list is known; adding a list takes it away.</summary>
    public const string NoListsNote = "The spell block names no class lists (SRD 5.1 prints them separately); add them to use the spell with a caster.";

    /// <summary>One list page: its key, the page of its heading and the <see cref="Key"/>s of the spell names it lists.</summary>
    private sealed record SpellList(string ListKey, int Page, HashSet<string> Names);

    /// <summary>
    /// A heading "&lt;Name&gt; Spells" (any case) whose next line is a level group ("Cantrips", "1st Level"), then name lines
    /// under the level groups, until a heading, a line that cannot be a name, or the page ends. A name that cannot become a
    /// list key proposes nothing. Lines are read, never kept: only keys and names (compared, not stored) leave this pass.
    /// </summary>
    private static List<SpellList> SpellListPages(List<Unit> units, double body)
    {
        var lists = new List<SpellList>();
        // A list can run over to the next page (review fix 3): `current` is carried to the following page and ends at a heading,
        // a line that is not a name, or a page with no text units before it. Names are credited to the heading's own page.
        SpellList? current = null;
        var previousPage = int.MinValue;
        foreach (var page in units.GroupBy(u => u.Page))
        {
            if (page.Key != previousPage + 1)
                current = null;
            previousPage = page.Key;
            var lines = new List<(string Line, bool Heading)>();
            foreach (var unit in page)
                lines.AddRange(unit.Lines.Select((l, n) => (l, n == 0 && IsHeading(unit, body))));
            for (var i = 0; i < lines.Count; i++)
            {
                var (line, heading) = lines[i];
                if (SpellListHeading().Match(line) is { Success: true } listHeading && i + 1 < lines.Count && SpellLevelGroup().IsMatch(lines[i + 1].Line))
                {
                    current = SpellListKey.TryNormalise(listHeading.Groups["name"].Value, out var key) ? new SpellList(key, page.Key, []) : null;
                    if (current is not null)
                        lists.Add(current);
                    continue;
                }
                if (current is null || SpellLevelGroup().IsMatch(line))
                    continue;
                if (heading || !LooksLikeName(line))
                    current = null;
                else
                    current.Names.Add(Key(line));
            }
        }
        return lists;
    }

    /// <summary>The list pages' keys, added (once each) to the spell candidates they name, as an unsure field citing the page.</summary>
    private static List<DraftCandidate> ApplySpellLists(List<DraftCandidate> spells, List<SpellList> lists)
    {
        if (lists.Count == 0)
            return spells;
        var result = new List<DraftCandidate>(spells.Count);
        foreach (var candidate in spells)
        {
            var effect = candidate.ProposedEffects.OfType<SpellEffect>().FirstOrDefault();
            var name = Key(candidate.ProposedName);
            if (effect is null)
            {
                result.Add(candidate);
                continue;
            }
            var keys = effect.Lists.ToList();
            var notes = new List<string>();
            foreach (var list in lists.Where(l => l.Names.Contains(name)))
            {
                if (keys.Contains(list.ListKey, StringComparer.Ordinal))
                    continue;
                keys.Add(list.ListKey);
                notes.Add($"Spell list '{list.ListKey}' was read from page {list.Page.ToString(CultureInfo.InvariantCulture)}; check it.");
            }
            if (notes.Count == 0)
            {
                result.Add(candidate);
                continue;
            }
            var fields = new Dictionary<string, string>(candidate.Fields, StringComparer.Ordinal) { ["lists"] = string.Join(", ", keys) };
            result.Add(candidate with
            {
                ProposedEffects = [.. candidate.ProposedEffects.Select(e => ReferenceEquals(e, effect) ? effect with { Lists = keys } : e)],
                Uncertainties = [.. candidate.Uncertainties.Where(u => u != NoListsNote), .. notes],
                Fields = fields,
                LowConfidenceFields = candidate.LowConfidenceFields.Contains("lists", StringComparer.Ordinal) ? candidate.LowConfidenceFields : [.. candidate.LowConfidenceFields, "lists"],
            });
        }
        return result;
    }

    // ---- feats ----

    private static IEnumerable<DraftCandidate> Feats(List<Unit> units, DetectionContext context, double body)
    {
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            string name, category;
            string? prerequisite;
            string layout;
            var descriptionStart = i + 1;
            var oneBlockLayout = false;
            IEnumerable<string> ownRest = [];
            if (unit.Lines.Length >= 2 && LooksLikeName(unit.Lines[0]) && Feat521().Match(string.Join(' ', unit.Lines.Skip(1))) is { Success: true } modern)
            {
                (name, category, prerequisite, layout) = (TitleCase(unit.Lines[0]), modern.Groups["category"].Value, modern.Groups["prerequisite"].Success ? modern.Groups["prerequisite"].Value : null, RulesFamilies.Srd521);
            }
            else if (IsHeading(unit, body) && unit.Lines.Length == 1 && i + 1 < units.Count && PrerequisitesLine().Match(units[i + 1].Lines[0]) is { Success: true } following)
            {
                (name, category, prerequisite, layout) = (TitleCase(unit.Lines[0]), "Feat", following.Groups["p"].Value.Trim(), RulesFamilies.Srd51);
                // The singular label is the SRD 5.1 layout; the plural is the non-SRD marker (ruling R22), so the family is the reviewer's.
                oneBlockLayout = units[i + 1].Lines[0].StartsWith("Prerequisites:", StringComparison.Ordinal);
                descriptionStart = i + 2;
            }
            else if (OneBlockFeat(unit) is { } oneBlock)
            {
                // A third-party layout: the name, then "Prerequisites: …", then the description, all in one block.
                // It matches neither SRD layout, so its family is the reviewer's to give (ruling R22).
                (name, category, prerequisite, layout) = (TitleCase(unit.Lines[0]), "Feat", oneBlock.Groups["p"].Value.Trim(), RulesFamilies.Srd51);
                oneBlockLayout = true;
                ownRest = unit.Lines.Skip(2);
            }
            else
                continue;

            var description = Description(units, descriptionStart, body, u => IsFeatHeader(u) || ReadSpellHeader(u) is not null || FeatureHeader(u) is not null, out var endPage);
            var excerptLines = unit.Lines.Concat(description).ToList();
            var own = ownRest.ToList();
            if (own.Count > 0 && description.Count > 0)
                own.Add("");
            description = [.. own, .. description];
            var text = Paragraphs(description);
            IReadOnlyList<string> families;
            List<string> uncertainties;
            var low = new List<string>();
            if (oneBlockLayout)
            {
                var (neutral, notes, unsure) = NeutralFamily(context);
                (families, uncertainties) = (neutral, notes);
                if (unsure)
                    low.Add("rulesFamilies");
            }
            else
                (families, uncertainties) = Family(layout, context);
            var effects = new List<Effect>();
            foreach (Match bonus in Bonus().Matches(text))
            {
                var target = bonus.Groups["target"].Value.ToLowerInvariant() is "initiative" ? FieldIds.Initiative : FieldIds.ArmorClass;
                effects.Add(new ModifierEffect { Id = $"bonus-{effects.Count + 1}", Operation = ModifierOperation.Bonus, Target = target, Value = bonus.Groups["amount"].Value });
            }
            foreach (Match skill in SkillProficiency().Matches(text))
            {
                if (CharacterCalculator.Skills.FirstOrDefault(s => s.Label.Equals(skill.Groups["skill"].Value, StringComparison.OrdinalIgnoreCase)) is { Key: { } key })
                    effects.Add(new GrantEffect { Id = $"skill-{key}", Grant = GrantKind.Proficiency, Target = FieldIds.Skill(key) });
            }
            if (text.Contains("choice", StringComparison.OrdinalIgnoreCase) || text.Contains("choose", StringComparison.OrdinalIgnoreCase))
            {
                uncertainties.Add("The feat offers a choice; the proposed effects cover only what is fixed.");
                if (effects.Count > 0)
                    low.Add("effects");
            }
            if (effects.Count > 0)
                uncertainties.Add("The effects were read from the text; check them against the page.");
            if (prerequisite is not null)
                uncertainties.Add("The prerequisite is text only; add a restriction if it should be checked.");
            var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["category"] = category };
            if (prerequisite is not null)
                fields["prerequisite"] = prerequisite;
            yield return new DraftCandidate(
                Guid.NewGuid(), context.SourceId, new PageRef(unit.Page, endPage > unit.Page ? endPage : null), Excerpt(excerptLines),
                ContentKind.Feat, name, families, effects, Clamp(text.Length == 0 ? 0.6 : 0.85), uncertainties)
            {
                Fields = fields,
                LowConfidenceFields = low,
                UnresolvedReferences = [.. ReferencedSpells(text)],
                Summary = text.Length == 0 ? null : text,
            };
        }
    }

    private static bool IsFeatHeader(Unit unit) =>
        unit.Lines.Length >= 2 && ((LooksLikeName(unit.Lines[0]) && Feat521().IsMatch(string.Join(' ', unit.Lines.Skip(1)))) || OneBlockFeat(unit) is not null);

    /// <summary>
    /// A block whose first line is a name and whose second line is the plural "Prerequisites: …" (the singular form heads
    /// option lists such as the SRDs' Eldritch Invocations, which are not feats). A body paragraph that merely starts
    /// with "Prerequisites:" has no name line above it in its block, so it is never a feat on its own. The prerequisite
    /// may be at most 400 characters.
    /// </summary>
    private static Match? OneBlockFeat(Unit unit) =>
        unit.Lines.Length >= 2 && unit.Lines[1].StartsWith("Prerequisites:", StringComparison.Ordinal) && LooksLikeName(unit.Lines[0])
            && PrerequisitesLine().Match(unit.Lines[1]) is { Success: true, Groups: var groups } m && groups["p"].Length <= 400 ? m : null;

    // ---- class features ----

    private sealed record FeatureStart(string Name, int Level, string? Owner, string Layout, IReadOnlyList<string> Rest);

    private static FeatureStart? FeatureHeader(Unit unit)
    {
        if (LevelFeature().Match(unit.Lines[0]) is { Success: true } modern)
            return new(modern.Groups["name"].Value.Trim(), int.Parse(modern.Groups["level"].Value, CultureInfo.InvariantCulture), null, RulesFamilies.Srd521, [.. unit.Lines.Skip(1)]);
        if (unit.Lines.Length >= 2 && LooksLikeName(unit.Lines[0]) && OwnedFeature().Match(unit.Lines[1]) is { Success: true } owned)
            return new(TitleCase(unit.Lines[0]), int.Parse(owned.Groups["level"].Value, CultureInfo.InvariantCulture), owned.Groups["owner"].Value, RulesFamilies.Srd521, [.. unit.Lines.Skip(2)]);
        return null;
    }

    /// <summary>Headings inside a class's "Class Features" section that are not features.</summary>
    private static readonly HashSet<string> SectionHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Class Features", "Hit Points", "Proficiencies", "Equipment", "Quick Build", "Multiclassing", "Spellcasting Ability", "Spell Slots", "Cantrips", "Preparing and Casting Spells", "Ritual Casting", "Spellcasting Focus",
    };

    private static IEnumerable<DraftCandidate> Features(List<Unit> units, DetectionContext context, double body)
    {
        var inClassSection = false;
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            // SRD 5.1: a class's features follow its "Class Features" heading, up to the next chapter title.
            if (unit.Lines[0] == "Class Features")
                inClassSection = true;
            else if (unit.Block.FontSize >= body * 1.8)
                inClassSection = false;

            if (OneBlockFeat(unit) is not null)
                continue; // a feat (Feats), not also a class feature

            var start = FeatureHeader(unit);
            var descriptionStart = i + 1;
            var levelUnknown = false;
            if (start is null && IsHeading(unit, body) && unit.Lines.Length == 1 && LooksLikeName(unit.Lines[0]) && !SectionHeadings.Contains(unit.Lines[0])
                && !unit.Lines[0].StartsWith("The ", StringComparison.OrdinalIgnoreCase) && i + 1 < units.Count) // "The Bard" titles a class table
            {
                // SRD 5.1: a bold heading whose first sentence gives the level ("Starting at 2nd level, …"), or any such
                // heading inside a "Class Features" section (a level-1 feature does not say its level).
                if (LevelSentence().Match(units[i + 1].Lines[0]) is { Success: true } sentence)
                    start = new(unit.Lines[0], int.Parse(sentence.Groups["level"].Value, CultureInfo.InvariantCulture), null, RulesFamilies.Srd51, []);
                else if (inClassSection)
                {
                    start = new(unit.Lines[0], 1, null, RulesFamilies.Srd51, []);
                    levelUnknown = true;
                }
            }
            else if (start is null && inClassSection && !unit.Block.Bold && unit.Lines.Length >= 2 && IsTitleCase(unit.Lines[0]) && !SectionHeadings.Contains(unit.Lines[0])
                && EarlyLevel().Match(string.Join(' ', unit.Lines.Skip(1).Take(2))) is { Success: true } early && early.Index < 120)
            {
                // SRD 5.1 subclass features: a run-in heading line ("Cutting Words"), then "Also at 3rd level, …".
                start = new(unit.Lines[0], int.Parse(early.Groups["level"].Value, CultureInfo.InvariantCulture), null, RulesFamilies.Srd51, [.. unit.Lines.Skip(1)]);
            }
            if (start is null)
                continue;

            var description = start.Rest.ToList();
            var more = Description(units, descriptionStart, body, u => FeatureHeader(u) is not null || IsFeatHeader(u) || ReadSpellHeader(u) is not null, out var endPage);
            if (more.Count > 0 && description.Count > 0)
                description.Add("");
            description.AddRange(more);
            var text = Paragraphs(description);
            var (families, uncertainties) = Family(start.Layout, context);
            uncertainties.Add("Class features are proposed as text; grants, resources and uses are added by hand.");
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            if (levelUnknown)
                uncertainties.Add("The text does not state the level (a level-1 feature, or a sub-feature); check it against the class table.");
            else
                fields["level"] = start.Level.ToString(CultureInfo.InvariantCulture);
            if (start.Owner is not null)
                fields["class"] = start.Owner;
            yield return new DraftCandidate(
                Guid.NewGuid(), context.SourceId, new PageRef(unit.Page, endPage > unit.Page ? endPage : null), Excerpt(unit.Lines.Concat(description)),
                ContentKind.Feature, start.Name, families, [], Clamp(text.Length == 0 ? 0.5 : start.Layout == RulesFamilies.Srd521 ? 0.85 : levelUnknown ? 0.55 : 0.7), uncertainties)
            {
                Fields = fields,
                LowConfidenceFields = levelUnknown ? ["level"] : [],
                UnresolvedReferences = [.. ReferencedSpells(text)],
                Summary = text.Length == 0 ? null : text,
            };
        }
    }

    // ---- weapon and armor rows ----

    private static IEnumerable<DraftCandidate> Weapons(DetectionPage page, List<(int Page, TextLine Line)> lines, DetectionContext context)
    {
        foreach (var (_, line) in lines)
        {
            string name, damage, type, rest;
            List<TextLine> row;
            if (WeaponDamageCell().Match(line.Text) is { Success: true } cell)
            {
                row = SameRow(lines, line);
                var left = row.Where(c => c.X < line.X && !CostOrWeight().IsMatch(c.Text)).ToList();
                if (left.Count == 0)
                    continue;
                (name, damage, type) = (left[0].Text, cell.Groups["dice"].Value, cell.Groups["type"].Value);
                rest = string.Join("  ", row.Where(c => c.X > line.X).Select(c => c.Text));
            }
            else if (WeaponRowLine().Match(line.Text) is { Success: true } whole)
            {
                // One line per row, or a name and damage line with the other cells to its right.
                row = SameRow(lines, line);
                (name, damage, type) = (whole.Groups["name"].Value.Trim(), whole.Groups["dice"].Value, whole.Groups["type"].Value);
                rest = string.Join("  ", new[] { whole.Groups["rest"].Value }.Concat(row.Where(c => c.X > line.X).Select(c => c.Text)));
            }
            else
                continue;
            if (!LooksLikeCellName(name) || name.Contains("Weapons", StringComparison.Ordinal) || IsJunkItemName(name))
                continue;

            var category = CategoryAbove(lines, line, WeaponSection());
            var ranged = category?.Contains("Ranged", StringComparison.OrdinalIgnoreCase) == true || rest.Contains("Ammunition", StringComparison.OrdinalIgnoreCase);
            var properties = WeaponProperty().Matches(rest).Select(m => m.Value.ToLowerInvariant().Replace(' ', '-')).Distinct().ToList();
            var range = WeaponRange().Match(rest);
            var versatile = Versatile().Match(rest);
            var mastery = Masteries.FirstOrDefault(m => Regex.IsMatch(rest, $@"\b{m}\b", RegexOptions.None, TimeSpan.FromMilliseconds(100)));
            var uncertainties = new List<string> { "Read from a table row; check the columns against the page." };
            var low = new List<string>();
            if (category is null)
            {
                uncertainties.Add("No 'Simple' or 'Martial' heading was found above the row; the category is a guess.");
                low.Add("category");
            }
            var layout = mastery is not null ? RulesFamilies.Srd521 : RulesFamilies.Srd51;
            var (families, familyNotes) = Family(layout, context);
            uncertainties.AddRange(familyNotes);
            var effect = new WeaponEffect
            {
                Id = "weapon",
                Category = category?.StartsWith("Martial", StringComparison.OrdinalIgnoreCase) == true ? RulesCore.WeaponCategory.Martial : RulesCore.WeaponCategory.Simple,
                Attack = ranged ? WeaponAttack.Ranged : WeaponAttack.Melee,
                Damage = damage,
                DamageType = type.ToLowerInvariant(),
                Properties = properties,
                Versatile = versatile.Success ? versatile.Groups["dice"].Value : null,
                Range = range.Success ? range.Groups["range"].Value : null,
                WeaponKey = WeaponKey(name),
                Mastery = mastery,
            };
            var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["damage"] = $"{damage} {type.ToLowerInvariant()}", ["properties"] = string.Join(", ", properties) };
            if (category is not null)
                fields["category"] = category;
            yield return new DraftCandidate(
                Guid.NewGuid(), context.SourceId, new PageRef(page.Page), Excerpt([string.Join("  ", row.Select(c => c.Text))]),
                ContentKind.Item, name, families, [effect], Clamp(category is null ? 0.6 : 0.85), uncertainties)
            {
                Fields = fields,
                LowConfidenceFields = low,
            };
        }
    }

    private static readonly string[] Masteries = ["Cleave", "Graze", "Nick", "Push", "Sap", "Slow", "Topple", "Vex"];

    private static string WeaponKey(string name) =>
        string.Join('-', NonWord().Split(name.ToLowerInvariant()).Where(p => p.Length > 0));

    /// <summary>The nearest line above the row, in the same column or to its left, that names a table section (for example "Martial Melee Weapons").</summary>
    private static string? CategoryAbove(List<(int Page, TextLine Line)> lines, TextLine anchor, Regex pattern) =>
        lines.Select(l => l.Line).Where(l => l.Y > anchor.Y && pattern.IsMatch(l.Text)).OrderBy(l => l.Y - anchor.Y).FirstOrDefault()?.Text;

    private static IEnumerable<DraftCandidate> Armor(DetectionPage page, List<(int Page, TextLine Line)> lines, DetectionContext context)
    {
        // Only a page with an armor table: a Light, Medium or Heavy Armor section, and an "Armor Class" header or
        // "+ Dex modifier" cells (a table continued from the previous page has no header).
        if (!lines.Any(l => ArmorCategoryLine().IsMatch(l.Line.Text))
            || !lines.Any(l => l.Line.Text.Contains("Armor Class", StringComparison.Ordinal) || l.Line.Text.Contains("+ Dex modifier", StringComparison.Ordinal)))
            yield break;
        foreach (var (_, line) in lines)
        {
            string name, ac;
            if (ArmorClassCell().Match(line.Text) is { Success: true } cell)
            {
                var left = SameRow(lines, line).Where(c => c.X < line.X).ToList();
                // A bare number is an armor class only under an armor section; "11 + Dex modifier" speaks for itself.
                if (left.Count == 0 || (CategoryAbove(lines, line, ArmorCategoryLine()) is null && !cell.Value.Contains("Dex", StringComparison.Ordinal)))
                    continue;
                (name, ac) = (left[0].Text, cell.Value);
            }
            else if (ArmorRowLine().Match(line.Text) is { Success: true } whole)
            {
                (name, ac) = (whole.Groups["name"].Value.Trim(), whole.Groups["ac"].Value);
            }
            else
                continue;
            if (!LooksLikeName(name) || name.Contains("Armor Class", StringComparison.Ordinal) || IsJunkItemName(name))
                continue;
            var section = CategoryAbove(lines, line, ArmorCategoryLine());
            var shield = ac.StartsWith('+');
            var category = shield ? ArmorCategory.Shield
                : section?.StartsWith("Heavy", StringComparison.OrdinalIgnoreCase) == true ? ArmorCategory.Heavy
                : section?.StartsWith("Medium", StringComparison.OrdinalIgnoreCase) == true || ac.Contains("max 2", StringComparison.Ordinal) ? ArmorCategory.Medium
                : ac.Contains("Dex", StringComparison.Ordinal) ? ArmorCategory.Light : ArmorCategory.Heavy;
            var number = int.Parse(Digits().Match(ac).Value, CultureInfo.InvariantCulture);
            var uncertainties = new List<string> { "Read from a table row; check the columns against the page." };
            var low = new List<string>();
            if (section is null && !shield)
            {
                uncertainties.Add("No 'Light', 'Medium' or 'Heavy Armor' heading was found above the row; the category comes from the Armor Class column.");
                low.Add("category");
            }
            var (families, familyNotes, familyUnsure) = NeutralFamily(context);
            uncertainties.AddRange(familyNotes);
            if (familyUnsure)
                low.Add("rulesFamilies");
            yield return new DraftCandidate(
                Guid.NewGuid(), context.SourceId, new PageRef(page.Page), Excerpt([string.Join("  ", SameRow(lines, line).Select(c => c.Text))]),
                ContentKind.Item, name, families,
                [new ArmorEffect { Id = "armor", Category = category, ArmorClass = number, DexterityCap = category == ArmorCategory.Medium ? 2 : null }],
                Clamp(section is null && !shield ? 0.6 : 0.85), uncertainties)
            {
                Fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["armorClass"] = ac, ["category"] = category.ToString().ToLowerInvariant() },
                LowConfidenceFields = low,
            };
        }
    }

    // ---- class feature tables ----

    /// <summary>The table's last header cell: "Features" or "Class Features" in Title case, or in all capitals; not lower case.</summary>
    private static bool IsFeaturesCell(string text) =>
        text.EndsWith("Features", StringComparison.Ordinal) || text.EndsWith("FEATURES", StringComparison.Ordinal);

    /// <summary>
    /// The text of the pages that follow a class table page, up to the next class's table (at most four pages): the class's
    /// feature descriptions, where its spellcasting ability is named (item 11).
    /// </summary>
    private static List<string> FollowingText(IReadOnlyList<DetectionPage> pages, DetectionPage page, HashSet<string> boilerplate)
    {
        var following = new List<string>();
        foreach (var next in pages.Where(p => p.Page > page.Page).OrderBy(p => p.Page).Take(4))
        {
            var nextLines = PageLines(next, boilerplate);
            // The next class's table (its "Level" and "Features" header cells) ends this class's text; a heading-shaped prose
            // line ("The DC equals 8 plus") must not.
            if (nextLines.Any(l => l.Line.Text.Trim() is "Level" or "LEVEL") && nextLines.Any(l => l.Line.Text.Trim() is "Features" or "FEATURES"))
                break;
            following.AddRange(nextLines.Select(l => l.Line.Text));
        }
        return following;
    }

    private static IEnumerable<DraftCandidate> ClassTables(DetectionPage page, List<(int Page, TextLine Line)> lines, List<Unit> pageUnits, DetectionContext context, Dictionary<Guid, IReadOnlyDictionary<int, IReadOnlyList<string>>> classFeatures, Func<List<string>> followingFor, List<(int Page, TextLine Line)> previous, CancellationToken cancellationToken)
    {
        // "The Bard" (SRD 5.1) or "Bard Features" (SRD 5.2.1); "Class Features" and "Bard Class Features" are section headings.
        var titles = pageUnits.Select(u => u.Lines[0]).Select(t => ClassTableTitle().Match(t)).Where(m => m.Success)
            .Select(m => TitleCase(m.Groups["name"].Value.Trim())).Select(n => n.EndsWith(" Class", StringComparison.Ordinal) ? n[..^" Class".Length] : n)
            .Where(n => n.Length > 0 && n != "Class").ToList();
        if (titles.Count == 0)
            return [];
        var className = titles[0];
        var following = followingFor();

        // The line that names this class's own table ("Bard Features", "The Bard") is the title; other headings with the same shape are not.
        var titleLine = pageUnits.Select(u => u.Lines[0]).FirstOrDefault(t => t.Trim() == $"{className} Features" || t.Trim() == $"The {className}")
            ?? pageUnits.Select(u => u.Lines[0]).First(t => ClassTableTitle().IsMatch(t));
        if (titles.Select(Key).Distinct(StringComparer.Ordinal).Count() > 1 && SplitClasses(page, lines, pageUnits, className, titleLine, cancellationToken) is { Count: > 1 } split)
        {
            // Wave 0/1 minors (d): two classes on one page are read each from its own window, and the page's text after them goes to the last.
            return [.. split.Select((s, i) => ClassCandidate(page, s.Name, s.Read.Features, s.Read.Table, s.Read.TextLines, context, classFeatures, i == split.Count - 1 ? following : [], previous))];
        }
        return ReadClassTable(page, lines, _ => true, titleLine, WholePageLines(page), cancellationToken) is { } read
            ? [ClassCandidate(page, className, read.Features, read.Table, read.TextLines, context, classFeatures, following, previous)]
            : [];
    }

    /// <summary>Most class titles on one page that are split into windows (R127); more are read as one page.</summary>
    internal const int MaxSplitClasses = 8;

    internal sealed record ClassRead(IReadOnlyDictionary<int, IReadOnlyList<string>> Features, ClassTable? Table, List<TextLine> TextLines);

    /// <summary>
    /// The classes of a page with more than one class table, top to bottom, each with the read of its own window: from its
    /// heading (its "Core … Traits" heading when that is printed above it) to the next class's. A title whose window holds no
    /// table is not a class and is left out; null when the page cannot be split, and the whole page is read as one class.
    /// </summary>
    internal static List<(string Name, ClassRead Read)>? SplitClasses(DetectionPage page, List<(int Page, TextLine Line)> lines, List<Unit> pageUnits, string className, string className0Title, CancellationToken cancellationToken)
    {
        var names = new List<(string Name, string Title)> { (className, className0Title) };
        foreach (var unit in pageUnits)
        {
            var title = unit.Lines[0];
            if (ClassTableTitle().Match(title) is not { Success: true } m)
                continue;
            var name = TitleCase(m.Groups["name"].Value.Trim());
            name = name.EndsWith(" Class", StringComparison.Ordinal) ? name[..^" Class".Length] : name;
            if (name.Length == 0 || name == "Class" || names.Any(n => Key(n.Name) == Key(name)))
                continue;
            names.Add((name, pageUnits.Select(u => u.Lines[0]).FirstOrDefault(t => t.Trim() == $"{name} Features" || t.Trim() == $"The {name}") ?? title));
            // R127: more class titles than any real page holds is not split; the page is read whole (the merged read).
            if (names.Count > MaxSplitClasses)
                return null;
        }
        var anchored = new List<(string Name, string Title, double Y)>();
        foreach (var (name, title) in names)
        {
            if (lines.Where(l => l.Line.Text == title).Select(l => l.Line).OrderByDescending(l => l.Y).FirstOrDefault() is not { } titleAt)
                return null;
            var core = lines.Select(l => l.Line).Where(l => l.Y > titleAt.Y && l.Text.Trim().Equals($"Core {name} Traits", StringComparison.OrdinalIgnoreCase)).Select(l => l.Y).DefaultIfEmpty(titleAt.Y).Max();
            anchored.Add((name, title, core));
        }
        // A class whose own window holds no table is dropped, and the windows are drawn again without it.
        // The page's whole text is built once, and a window that did not change between passes is not read again.
        var wholePage = WholePageLines(page);
        var memo = new Dictionary<(string Name, double Upper, double Lower), ClassRead?>();
        while (anchored.Count > 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordered = anchored.OrderByDescending(a => a.Y).ToList();
            var anchorYs = ordered.Select(o => o.Y).ToList();
            var reads = ordered.Select((a, i) =>
            {
                var key = (a.Name, i == 0 ? double.PositiveInfinity : anchorYs[i], i == anchorYs.Count - 1 ? double.NegativeInfinity : anchorYs[i + 1]);
                if (!memo.TryGetValue(key, out var read))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    memo[key] = read = ReadClassTable(page, lines, WindowOf(anchorYs, i), a.Title, wholePage, cancellationToken);
                }
                return (a.Name, Read: read);
            }).ToList();
            if (reads.All(r => r.Read is not null))
                return [.. reads.Select(r => (r.Name, r.Read!))];
            var drop = reads.Select((r, i) => (r, i)).FirstOrDefault(x => x.r.Read is null && x.r.Name != className);
            if (drop.r.Name is null)
                return null; // the first class itself cannot be read in its window: read the page whole
            anchored.RemoveAll(a => a.Name == drop.r.Name);
        }
        return null;
    }

    /// <summary>The lines from the <paramref name="index"/>th anchor down to the next (the first window also takes everything above it).</summary>
    private static Func<TextLine, bool> WindowOf(List<double> anchors, int index) =>
        l => (index == 0 || l.Y <= anchors[index] + 1) && (index == anchors.Count - 1 || l.Y > anchors[index + 1] + 1);

    /// <summary>Every non-blank line of the page's blocks, trimmed, boilerplate included.</summary>
    private static List<TextLine> WholePageLines(DetectionPage page) =>
        page.Blocks.SelectMany(b => b.Lines is { Count: > 0 } l ? l : [new TextLine(b.Text, b.X, b.Y, b.Width, b.Height)]).Where(l => l.Text.Trim().Length > 0).Select(l => l with { Text = l.Text.Trim() }).ToList();

    /// <summary>One class table read from the lines in a window: whole by position (D34), else by the legacy readers; null when no table is found.</summary>
    internal static ClassRead? ReadClassTable(DetectionPage page, List<(int Page, TextLine Line)> allLines, Func<TextLine, bool> inWindow, string titleLine, List<TextLine> wholePageLines, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lines = allLines.Where(l => inWindow(l.Line)).ToList();
        var byLevel = new SortedDictionary<int, List<string>>();

        // Item 11 (D34): the whole table, read back by position; the legacy readers below stay for layouts it cannot read.
        var pageTextLines = lines.Select(l => l.Line).ToList();
        if (ClassTableReconstruction.Reconstruct(pageTextLines, titleLine, cancellationToken) is { Complete: true } table)
            return new(table.FeaturesByLevel, table, pageTextLines);
        // A cell the running-header rule took for boilerplate (text repeated on many pages) can break the table: read it again whole.
        var wholePage = wholePageLines.Where(inWindow).ToList();
        if (wholePage.Count > pageTextLines.Count && ClassTableReconstruction.Reconstruct(wholePage, titleLine, cancellationToken) is { Complete: true } whole)
            return new(whole.FeaturesByLevel, whole, wholePage);

        // The "Level" header cell, alone or fused with the next header ("Level Bonus" under "Proficiency").
        var header = lines.Select(l => l.Line).FirstOrDefault(l =>
        {
            cancellationToken.ThrowIfCancellationRequested(); // the row scan below is quadratic in the window's lines
            return (l.Text is "Level" or "LEVEL" || l.Text.StartsWith("Level ", StringComparison.Ordinal) && l.Text.Length < 20 || l.Text.StartsWith("LEVEL ", StringComparison.Ordinal) && l.Text.Length < 20)
                && SameRow(lines, l).Any(c => IsFeaturesCell(c.Text) && c.X > l.X);
        });
        if (header is not null)
        {
            // Columns: rows are keyed by the level column; wrapped feature lines belong to the row just above them.
            var featuresHeader = SameRow(lines, header).First(c => IsFeaturesCell(c.Text) && c.X > header.X);
            var levels = lines.Select(l => l.Line).Where(l => Math.Abs(l.X - header.X) < 15 && l.Y < header.Y && LevelCell().IsMatch(l.Text)).OrderByDescending(l => l.Y).ToList();
            foreach (var cell in lines.Select(l => l.Line).Where(l => l.X >= featuresHeader.X - 5 && l.X < featuresHeader.X + 150 && l.Y < header.Y))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = levels.Where(l => l.Y + Math.Max(2.5, l.Height * 0.4) >= cell.Y).OrderBy(l => l.Y - cell.Y).FirstOrDefault();
                if (row is null)
                    continue;
                var level = int.Parse(Digits().Match(row.Text).Value, CultureInfo.InvariantCulture);
                byLevel.TryAdd(level, []);
                byLevel[level].Add(cell.Text);
            }
        }
        else if (lines.Select(l => l.Line).FirstOrDefault(l => ClassTableHeaderLine().IsMatch(l.Text)) is { } single)
        {
            // One line per row: "3  +2  Feature, Feature".
            foreach (var row in lines.Select(l => l.Line).Where(l => l.Y < single.Y).Select(l => ClassTableRowLine().Match(l.Text)).Where(m => m.Success))
                byLevel[int.Parse(row.Groups["level"].Value, CultureInfo.InvariantCulture)] = [row.Groups["features"].Value];
        }
        if (byLevel.Count == 0)
            return null;
        var features = byLevel.ToDictionary(r => r.Key, r => (IReadOnlyList<string>)Join(r.Value).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(f => f != "—" && f != "-").ToList());
        return new(features, null, pageTextLines);
    }

    /// <summary>A class candidate: the Features column by level, plus the effects its page supports (<see cref="ClassProposals"/>).</summary>
    private static DraftCandidate ClassCandidate(DetectionPage page, string className, IReadOnlyDictionary<int, IReadOnlyList<string>> features, ClassTable? table, List<TextLine> pageTextLines, DetectionContext context, Dictionary<Guid, IReadOnlyDictionary<int, IReadOnlyList<string>>> classFeatures, List<string> following, List<(int Page, TextLine Line)> previous)
    {
        var ordered = features.OrderBy(f => f.Key).ToList();
        var references = ordered.SelectMany(f => f.Value).Where(f => !f.Equals("Ability Score Improvement", StringComparison.OrdinalIgnoreCase) && !f.EndsWith("feature", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        var (families, uncertainties, familyUnsure) = NeutralFamily(context);
        var proposal = ClassProposals.Propose(className, table, ClassProposals.LabelledLines(pageTextLines, previous.Select(l => l.Line).ToList(), className), features, following);
        uncertainties.AddRange(proposal.Uncertainties);
        if (table is null)
            uncertainties.Add("The level table could not be read whole: only its features are proposed. Add what is missing by hand, or accept it as reference.");
        var fields = ordered.ToDictionary(f => $"level {f.Key}", f => string.Join(", ", f.Value), StringComparer.Ordinal);
        foreach (var field in proposal.Fields)
            fields[field.Key] = field.Value;
        var lowConfidence = new List<string>();
        if (proposal.Effects.Count == 0)
            lowConfidence.Add("effects");
        lowConfidence.AddRange(proposal.LowConfidenceFields);
        if (familyUnsure)
            lowConfidence.Add("rulesFamilies");
        var candidate = new DraftCandidate(
            Guid.NewGuid(), context.SourceId, new PageRef(page.Page), Excerpt(ordered.Select(f => $"{f.Key}: {string.Join(", ", f.Value)}")),
            ContentKind.Class, className, families, proposal.Effects, Clamp(features.Count >= 3 ? 0.7 : 0.5), uncertainties)
        {
            Fields = fields,
            UnresolvedReferences = references,
            LowConfidenceFields = lowConfidence,
        };
        classFeatures[candidate.Id] = features;
        return candidate;
    }

    // ---- patterns (anchored, with a match timeout) ----

    [GeneratedRegex(@"^(?:Level (?<level>[1-9]) (?<school>Abjuration|Conjuration|Divination|Enchantment|Evocation|Illusion|Necromancy|Transmutation)|(?<school>Abjuration|Conjuration|Divination|Enchantment|Evocation|Illusion|Necromancy|Transmutation) Cantrip) \((?<lists>[^()]+)\)$", RegexOptions.None, 100)]
    private static partial Regex Spell521();

    [GeneratedRegex(@"^(?:(?<level>[1-9])(?:st|nd|rd|th)-level (?<school>abjuration|conjuration|divination|enchantment|evocation|illusion|necromancy|transmutation)|(?<school>[Aa]bjuration|[Cc]onjuration|[Dd]ivination|[Ee]nchantment|[Ee]vocation|[Ii]llusion|[Nn]ecromancy|[Tt]ransmutation) cantrip)(?<ritual> \(ritual\))?$", RegexOptions.None, 100)]
    private static partial Regex Spell51();

    [GeneratedRegex(@"^(?<label>Casting Time|Range|Components|Duration):\s*(?<value>.*)$", RegexOptions.None, 100)]
    private static partial Regex StatLine();

    [GeneratedRegex(@"^(?<category>Origin|General|Fighting Style|Epic Boon) Feat(?: \(Prerequisite: (?<prerequisite>[^()]+)\))?$", RegexOptions.None, 100)]
    private static partial Regex Feat521();

    [GeneratedRegex(@"^Level (?<level>[1-9]|1[0-9]|20): (?<name>[A-Z][^:]{1,60})$", RegexOptions.None, 100)]
    private static partial Regex LevelFeature();

    [GeneratedRegex(@"^Level (?<level>[1-9]|1[0-9]|20) (?<owner>[A-Z][\w' ]{1,40}?) Feature$", RegexOptions.None, 100)]
    private static partial Regex OwnedFeature();

    [GeneratedRegex(@"^(?:Starting at|Beginning at|At|When you reach|Beginning when you reach) (?<level>[1-9]|1[0-9]|20)(?:st|nd|rd|th) level\b", RegexOptions.None, 100)]
    private static partial Regex LevelSentence();

    [GeneratedRegex(@"\b\d{1,2}d(?:4|6|8|10|12|20|100)\b", RegexOptions.None, 100)]
    private static partial Regex DiceExpression();

    [GeneratedRegex(@"\b(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma) saving throw", RegexOptions.None, 100)]
    private static partial Regex SavingThrow();

    [GeneratedRegex(@"\+(?<amount>[1-9]) bonus to (?<target>[Ii]nitiative|AC|Armor Class)\b", RegexOptions.None, 100)]
    private static partial Regex Bonus();

    [GeneratedRegex(@"proficiency in the (?<skill>[A-Z][a-z]+(?: of [A-Z][a-z]+| [A-Z][a-z]+)?) skill", RegexOptions.None, 100)]
    private static partial Regex SkillProficiency();

    [GeneratedRegex(@"\bcast (?:the )?(?<name>[A-Z][\w'’]*(?: (?:of |the )?[A-Z][\w'’]*){0,4})\b", RegexOptions.None, 100)]
    private static partial Regex CastsSpell();

    [GeneratedRegex(@"\bthe (?<name>[A-Z][\w'’]*(?: (?:of |the )?[A-Z][\w'’]*){0,4}) spell\b", RegexOptions.None, 100)]
    private static partial Regex NamedSpell();

    [GeneratedRegex(@"^(?<name>\p{L}[\p{L}\p{N}'’ -]{0,48}?) Spells$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex SpellListHeading();

    [GeneratedRegex(@"^(?:Cantrips|\d(?:st|nd|rd|th) Level)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex SpellLevelGroup();

    [GeneratedRegex(@"^(?<dice>\d{1,2}d(?:4|6|8|10|12)|1) (?<type>[Bb]ludgeoning|[Pp]iercing|[Ss]lashing)$", RegexOptions.None, 100)]
    private static partial Regex WeaponDamageCell();

    [GeneratedRegex(@"^(?<name>[A-Z][\w,' ]{1,40}?)\s+(?<dice>\d{1,2}d(?:4|6|8|10|12)|1) (?<type>[Bb]ludgeoning|[Pp]iercing|[Ss]lashing)(?:\s+(?<rest>.*))?$", RegexOptions.None, 100)]
    private static partial Regex WeaponRowLine();

    [GeneratedRegex(@"\b[Aa]t (?<level>[1-9]|1[0-9]|20)(?:st|nd|rd|th) level\b", RegexOptions.None, 100)]
    private static partial Regex EarlyLevel();

    [GeneratedRegex(@"^(?:\d+(?:/\d+)? lb\.|\d+ (?:cp|sp|ep|gp|pp|CP|SP|EP|GP|PP)|—|-)$", RegexOptions.None, 100)]
    private static partial Regex CostOrWeight();

    [GeneratedRegex(@"^(?:Simple|Martial) (?:Melee|Ranged) Weapons", RegexOptions.None, 100)]
    private static partial Regex WeaponSection();

    [GeneratedRegex(@"\b(?:Ammunition|Finesse|Heavy|Light|Loading|Reach|Special|Thrown|Two-Handed|Versatile)\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex WeaponProperty();

    [GeneratedRegex(@"[Rr]ange (?<range>\d{1,3}/\d{1,4})", RegexOptions.None, 100)]
    private static partial Regex WeaponRange();

    [GeneratedRegex(@"[Vv]ersatile \((?<dice>\d{1,2}d(?:4|6|8|10|12))\)", RegexOptions.None, 100)]
    private static partial Regex Versatile();

    [GeneratedRegex(@"^(?:1[0-9](?: \+ Dex modifier(?: \(max 2\))?)?|\+[1-5])$", RegexOptions.None, 100)]
    private static partial Regex ArmorClassCell();

    [GeneratedRegex(@"^(?<name>[A-Z][\w' ]{1,40}?)\s+(?<ac>\d{1,2}(?: \+ Dex modifier(?: \(max 2\))?)?|\+[1-5])\s+(?:-|—|Str \d+)", RegexOptions.None, 100)]
    private static partial Regex ArmorRowLine();

    [GeneratedRegex(@"^(?:Light|Medium|Heavy) Armor\b|^Shield\b", RegexOptions.None, 100)]
    private static partial Regex ArmorCategoryLine();

    [GeneratedRegex(@"^(?:(?:The|THE) (?<name>[A-Z][\w' ]{1,40})|(?<name>[A-Z][\w' ]{1,40}?) (?:Features|FEATURES))$", RegexOptions.None, 100)]
    private static partial Regex ClassTableTitle();

    /// <summary>A feat's prerequisite line: "Prerequisite: …" or "Prerequisites: …".</summary>
    [GeneratedRegex(@"^Prerequisites?:\s*(?<p>.+)$", RegexOptions.None, 100)]
    private static partial Regex PrerequisitesLine();

    /// <summary>
    /// An item name that holds a price ("5 gp") or continues as a sentence (", which counts as": a comma, then two or more
    /// lower-case words). One word after the comma stays a name: the SRD's "Crossbow, hand".
    /// </summary>
    [GeneratedRegex(@"\d+\s*(?:cp|sp|ep|gp|pp|CP|SP|EP|GP|PP)\b|,\s+[a-z]+\s+[a-z]", RegexOptions.None, 100)]
    private static partial Regex JunkItemName();

    [GeneratedRegex(@"^(?:[1-9]|1[0-9]|20)(?:st|nd|rd|th)?$", RegexOptions.None, 100)]
    private static partial Regex LevelCell();

    [GeneratedRegex(@"^Level\s+Proficiency Bonus\b.*\bFeatures$", RegexOptions.None, 100)]
    private static partial Regex ClassTableHeaderLine();

    [GeneratedRegex(@"^(?<level>[1-9]|1[0-9]|20)(?:st|nd|rd|th)?\s+\+[2-6]\s+(?<features>[A-Z—-].*)$", RegexOptions.None, 100)]
    private static partial Regex ClassTableRowLine();

    [GeneratedRegex(@"\d+", RegexOptions.None, 100)]
    private static partial Regex Digits();

    [GeneratedRegex(@"^(?:Level [1-9] (?:Abjuration|Conjuration|Divination|Enchantment|Evocation|Illusion|Necromancy|Transmutation)|(?:Abjuration|Conjuration|Divination|Enchantment|Evocation|Illusion|Necromancy|Transmutation) Cantrip) \(", RegexOptions.None, 100)]
    private static partial Regex SpellLineStart();

    [GeneratedRegex(@"^(?:Origin|General|Fighting Style|Epic Boon) Feat\b", RegexOptions.None, 100)]
    private static partial Regex FeatLineStart();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.None, 100)]
    private static partial Regex NonWord();
}
