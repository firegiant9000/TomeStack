using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TomeStack.RulesCore;

namespace TomeStack.ImportWorker.Detection;

/// <summary>One extracted page, as detection reads it.</summary>
public sealed record DetectionPage(int Page, string Text, IReadOnlyList<TextBlock> Blocks, bool FromOcr);

/// <param name="SourceFamilies">The families the source declares; a candidate's family comes from its layout, within these.</param>
/// <param name="IsInstalled">Whether content of that name is installed (for unresolved references).</param>
public sealed record DetectionContext(Guid SourceId, IReadOnlyList<string> SourceFamilies, Func<string, bool> IsInstalled);

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

    private sealed record Unit(int Page, int Index, TextBlock Block, string[] Lines, bool FromOcr);

    private sealed record Row(int Page, double Y, List<TextLine> Cells);

    /// <param name="cancellationToken">Checked between passes and pages: cancelling an import also stops its detection.</param>
    public static IReadOnlyList<DraftCandidate> Detect(IReadOnlyList<DetectionPage> pages, DetectionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(context);
        var boilerplate = Boilerplate(pages);
        var units = Units(pages, boilerplate);
        var body = BodySize(units);
        var found = new List<DraftCandidate>();
        cancellationToken.ThrowIfCancellationRequested();
        found.AddRange(Spells(units, context, body));
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
            found.AddRange(ClassTables(page, lines, units.Where(u => u.Page == page.Page).ToList(), context));
        }

        // A candidate's references resolve against installed content and the job's other candidates.
        var named = found.Select(c => Key(c.ProposedName)).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. found
                .Select(c => c with { UnresolvedReferences = [.. c.UnresolvedReferences.Where(r => !named.Contains(Key(r)) && !context.IsInstalled(r)).Distinct(StringComparer.OrdinalIgnoreCase)] })
                .OrderBy(c => c.Page.Start).ThenBy(c => c.ProposedKind).ThenBy(c => c.ProposedName, StringComparer.Ordinal)
                .Take(MaxCandidates),
        ];
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
    private static List<TextLine> SameRow(List<(int Page, TextLine Line)> lines, TextLine anchor) =>
        [.. lines.Select(l => l.Line).Where(l => Math.Abs(l.Y - anchor.Y) <= Math.Max(2.5, anchor.Height * 0.4)).OrderBy(l => l.X)];

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
        return (context.SourceFamilies, [$"The layout looks like {layout}, which this source does not declare; the source's families are used."]);
    }

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

    private sealed record SpellHeader(string Name, int Level, string School, bool Ritual, IReadOnlyList<string> Lists, string Family, int HeaderLines);

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
                var lists = modern.Groups["lists"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(c => c.ToLowerInvariant()).ToList();
                return new(unit.Lines[0], level, modern.Groups["school"].Value.ToLowerInvariant(), false, lists, RulesFamilies.Srd521, 1 + take);
            }
        }
        if (Spell51().Match(unit.Lines[1]) is { Success: true } old)
        {
            var level = old.Groups["level"].Success ? int.Parse(old.Groups["level"].Value, CultureInfo.InvariantCulture) : 0;
            return new(unit.Lines[0], level, old.Groups["school"].Value.ToLowerInvariant(), old.Groups["ritual"].Success, [], RulesFamilies.Srd51, 2);
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
            if (header.Lists.Count == 0)
                uncertainties.Add("The spell block names no class lists (SRD 5.1 prints them separately); add them to use the spell with a caster.");
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
            if (unit.Lines.Length >= 2 && LooksLikeName(unit.Lines[0]) && Feat521().Match(string.Join(' ', unit.Lines.Skip(1))) is { Success: true } modern)
            {
                (name, category, prerequisite, layout) = (unit.Lines[0], modern.Groups["category"].Value, modern.Groups["prerequisite"].Success ? modern.Groups["prerequisite"].Value : null, RulesFamilies.Srd521);
            }
            else if (IsHeading(unit, body) && unit.Lines.Length == 1 && i + 1 < units.Count && units[i + 1].Lines[0].StartsWith("Prerequisite:", StringComparison.Ordinal))
            {
                (name, category, prerequisite, layout) = (unit.Lines[0], "Feat", units[i + 1].Lines[0]["Prerequisite:".Length..].Trim(), RulesFamilies.Srd51);
                descriptionStart = i + 2;
            }
            else
                continue;

            var description = Description(units, descriptionStart, body, u => IsFeatHeader(u) || ReadSpellHeader(u) is not null || FeatureHeader(u) is not null, out var endPage);
            var text = Paragraphs(description);
            var (families, uncertainties) = Family(layout, context);
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
            var low = new List<string>();
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
                Guid.NewGuid(), context.SourceId, new PageRef(unit.Page, endPage > unit.Page ? endPage : null), Excerpt(unit.Lines.Concat(description)),
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
        unit.Lines.Length >= 2 && LooksLikeName(unit.Lines[0]) && Feat521().IsMatch(string.Join(' ', unit.Lines.Skip(1)));

    // ---- class features ----

    private sealed record FeatureStart(string Name, int Level, string? Owner, string Layout, IReadOnlyList<string> Rest);

    private static FeatureStart? FeatureHeader(Unit unit)
    {
        if (LevelFeature().Match(unit.Lines[0]) is { Success: true } modern)
            return new(modern.Groups["name"].Value.Trim(), int.Parse(modern.Groups["level"].Value, CultureInfo.InvariantCulture), null, RulesFamilies.Srd521, [.. unit.Lines.Skip(1)]);
        if (unit.Lines.Length >= 2 && LooksLikeName(unit.Lines[0]) && OwnedFeature().Match(unit.Lines[1]) is { Success: true } owned)
            return new(unit.Lines[0], int.Parse(owned.Groups["level"].Value, CultureInfo.InvariantCulture), owned.Groups["owner"].Value, RulesFamilies.Srd521, [.. unit.Lines.Skip(2)]);
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

            var start = FeatureHeader(unit);
            var descriptionStart = i + 1;
            var levelUnknown = false;
            if (start is null && IsHeading(unit, body) && unit.Lines.Length == 1 && LooksLikeName(unit.Lines[0]) && !SectionHeadings.Contains(unit.Lines[0])
                && !unit.Lines[0].StartsWith("The ", StringComparison.Ordinal) && i + 1 < units.Count) // "The Bard" titles a class table
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
            if (!LooksLikeCellName(name) || name.Contains("Weapons", StringComparison.Ordinal))
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
            if (!LooksLikeName(name) || name.Contains("Armor Class", StringComparison.Ordinal))
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
            var (families, familyNotes) = Family(context.SourceFamilies.Count == 1 ? context.SourceFamilies[0] : RulesFamilies.Srd521, context);
            uncertainties.AddRange(familyNotes);
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

    private static IEnumerable<DraftCandidate> ClassTables(DetectionPage page, List<(int Page, TextLine Line)> lines, List<Unit> pageUnits, DetectionContext context)
    {
        // "The Bard" (SRD 5.1) or "Bard Features" (SRD 5.2.1); "Class Features" and "Bard Class Features" are section headings.
        var titles = pageUnits.Select(u => u.Lines[0]).Select(t => ClassTableTitle().Match(t)).Where(m => m.Success)
            .Select(m => m.Groups["name"].Value.Trim()).Select(n => n.EndsWith(" Class", StringComparison.Ordinal) ? n[..^" Class".Length] : n)
            .Where(n => n.Length > 0 && n != "Class").ToList();
        if (titles.Count == 0)
            return [];
        var className = titles[0];
        var byLevel = new SortedDictionary<int, List<string>>();

        // The "Level" header cell, alone or fused with the next header ("Level Bonus" under "Proficiency").
        var header = lines.Select(l => l.Line).FirstOrDefault(l => (l.Text == "Level" || l.Text.StartsWith("Level ", StringComparison.Ordinal) && l.Text.Length < 20)
            && SameRow(lines, l).Any(c => c.Text.EndsWith("Features", StringComparison.Ordinal) && c.X > l.X));
        if (header is not null)
        {
            // Columns: rows are keyed by the level column; wrapped feature lines belong to the row just above them.
            var featuresHeader = SameRow(lines, header).First(c => c.Text.EndsWith("Features", StringComparison.Ordinal) && c.X > header.X);
            var levels = lines.Select(l => l.Line).Where(l => Math.Abs(l.X - header.X) < 15 && l.Y < header.Y && LevelCell().IsMatch(l.Text)).OrderByDescending(l => l.Y).ToList();
            foreach (var cell in lines.Select(l => l.Line).Where(l => l.X >= featuresHeader.X - 5 && l.X < featuresHeader.X + 150 && l.Y < header.Y))
            {
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
            return [];
        var features = byLevel.ToDictionary(r => r.Key, r => Join(r.Value).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(f => f != "—" && f != "-").ToList());
        var references = features.Values.SelectMany(f => f).Where(f => !f.Equals("Ability Score Improvement", StringComparison.OrdinalIgnoreCase) && !f.EndsWith("feature", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        var (families, uncertainties) = Family(context.SourceFamilies.Count == 1 ? context.SourceFamilies[0] : RulesFamilies.Srd521, context);
        uncertainties.Add("A class is proposed from its features table only: add its hit die, proficiencies and grants by hand, or accept it as reference.");
        return
        [
            new DraftCandidate(
                Guid.NewGuid(), context.SourceId, new PageRef(page.Page), Excerpt(features.Select(f => $"{f.Key}: {string.Join(", ", f.Value)}")),
                ContentKind.Class, className, families, [], Clamp(features.Count >= 3 ? 0.7 : 0.5), uncertainties)
            {
                Fields = features.ToDictionary(f => $"level {f.Key}", f => string.Join(", ", f.Value), StringComparer.Ordinal),
                UnresolvedReferences = references,
                LowConfidenceFields = ["effects"],
            },
        ];
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

    [GeneratedRegex(@"^(?:The (?<name>[A-Z][\w' ]{1,40})|(?<name>[A-Z][\w' ]{1,40}?) Features)$", RegexOptions.None, 100)]
    private static partial Regex ClassTableTitle();

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
