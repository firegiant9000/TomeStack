using System.Globalization;
using System.Text.RegularExpressions;

namespace TomeStack.ImportWorker.Detection;

/// <summary>What a class-table column holds, read from its header (item 11, D34, ADR-016).</summary>
public enum ClassColumnRole { Level, Features, ProficiencyBonus, Cantrips, SpellsTable, SpellSlot, PactSlots, PactSlotLevel, Other }

/// <summary>One column of a reconstructed class table: its header text, its role, the spell level for a slot column, and where it sits.</summary>
public sealed record ClassTableColumn(string Label, ClassColumnRole Role, int SlotLevel, double Left);

/// <summary>One class level: its number, one text cell per column (a dash is "—", a cell that was not found is empty) and the feature names of its Features cell.</summary>
public sealed record ClassTableRow(int Level, IReadOnlyList<string> Cells, IReadOnlyList<string> Features);

/// <summary>
/// A class's level table read back from positioned page text: the rows, the columns and their roles. Typed accessors return
/// null unless the table covers levels 1 to 20 and every cell of the column reads, so a half-read table never becomes numbers.
/// </summary>
public sealed record ClassTable(IReadOnlyList<ClassTableColumn> Columns, IReadOnlyList<ClassTableRow> Rows)
{
    /// <summary>Rows for levels 1 to 20, in order.</summary>
    public bool Complete => Rows.Count == 20 && Rows.Select(r => r.Level).SequenceEqual(Enumerable.Range(1, 20));

    public bool IsPact => Columns.Any(c => c.Role == ClassColumnRole.PactSlots) && Columns.Any(c => c.Role == ClassColumnRole.PactSlotLevel);

    public bool HasSlotColumns => IsPact || Columns.Any(c => c.Role == ClassColumnRole.SpellSlot);

    /// <summary>The header of the spells column ("Spells Known", "Prepared Spells"), if the table has one.</summary>
    public string? SpellsLabel => Columns.FirstOrDefault(c => c.Role == ClassColumnRole.SpellsTable)?.Label;

    /// <summary>The 20 values of the first column with this role, or null when the table is not complete or a cell does not read.</summary>
    public IReadOnlyList<int>? Numbers(ClassColumnRole role)
    {
        var index = IndexOf(c => c.Role == role);
        return index < 0 || !Complete ? null : ReadColumn(index);
    }

    /// <summary>The class's feature names per level (levels with none are absent).</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> FeaturesByLevel =>
        Rows.Where(r => r.Features.Count > 0).ToDictionary(r => r.Level, r => r.Features);

    /// <summary>20 rows of slot counts by spell level, trailing zeros trimmed. Ordinary slots, or Pact Magic (the count at its slot level's place).</summary>
    public IReadOnlyList<IReadOnlyList<int>>? Slots()
    {
        if (!Complete)
            return null;
        if (IsPact)
        {
            var counts = ReadColumn(IndexOf(c => c.Role == ClassColumnRole.PactSlots));
            var levels = ReadColumn(IndexOf(c => c.Role == ClassColumnRole.PactSlotLevel));
            if (counts is null || levels is null)
                return null;
            var pact = new List<IReadOnlyList<int>>();
            for (var i = 0; i < 20; i++)
            {
                if (counts[i] == 0) { pact.Add([]); continue; }
                if (levels[i] is < 1 or > 9) return null;
                var row = new int[levels[i]];
                row[levels[i] - 1] = counts[i];
                pact.Add(row);
            }
            return pact;
        }
        var slotColumns = Columns.Select((c, i) => (Column: c, Index: i)).Where(c => c.Column.Role == ClassColumnRole.SpellSlot && c.Column.SlotLevel is >= 1 and <= 9).ToList();
        if (slotColumns.Count == 0)
            return null;
        var read = slotColumns.Select(c => (c.Column.SlotLevel, Values: ReadColumn(c.Index))).ToList();
        if (read.Any(r => r.Values is null))
            return null;
        var rows = new List<IReadOnlyList<int>>();
        for (var i = 0; i < 20; i++)
        {
            var row = new int[9];
            foreach (var (level, values) in read)
                row[level - 1] = values![i];
            var length = row.Length;
            while (length > 0 && row[length - 1] == 0)
                length--;
            rows.Add(row[..length]);
        }
        return rows;
    }

    private int IndexOf(Func<ClassTableColumn, bool> match)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (match(Columns[i]))
                return i;
        }
        return -1;
    }

    private int[]? ReadColumn(int index)
    {
        if (index < 0)
            return null;
        var values = new int[Rows.Count];
        for (var i = 0; i < Rows.Count; i++)
        {
            if (!ClassTableReconstruction.TryNumber(Rows[i].Cells[index], out values[i]))
                return null;
        }
        return values;
    }
}

/// <summary>
/// Reads a class's level table from positioned lines. The level column is the longest run of consecutive levels in one
/// column; each level's row is the lines at its height; slot cells that PDF extraction fuses into one line ("2 — — —") are
/// laid back onto the columns they cover; wrapped feature names belong to the row above. A table that cannot be read whole
/// yields null and the caller keeps the plain features-only candidate.
/// </summary>
public static partial class ClassTableReconstruction
{
    private const double DashHeight = 3.5;

    /// <summary>The widest empty space between two columns of one table, in points.</summary>
    private const double Gutter = 70;

    private sealed record Parsed(ClassTable Table, double TableRight, double TableLeft, double Pitch, double LevelCentre, List<(TextLine Line, int Level)> Levels, List<(TextLine Line, int Level)>? Next);

    /// <remarks>
    /// Not linear: the row cells are found by scanning the lines once per level (up to 20), the column and level-run grouping
    /// compares each cell with the groups so far, so a window of many lines is quadratic in the worst case. The token is
    /// checked in those outer loops (H1/R145).
    /// </remarks>
    public static ClassTable? Reconstruct(IReadOnlyList<TextLine> pageLines, string? titleText = null, CancellationToken cancellationToken = default)
    {
        var lines = pageLines.Where(l => l.Text.Trim().Length > 0).Select(l => l with { Text = l.Text.Trim() }).ToList();
        if (Read(lines, titleText, cancellationToken) is not { } parsed)
            return null;
        // A table that carries on in the next column of the same page, without a header of its own.
        if (!parsed.Table.Complete && Continued(lines, parsed) is { } joined && Read(joined, titleText, cancellationToken) is { Table.Complete: true } whole)
            return whole.Table;
        return parsed.Table;
    }

    /// <summary>
    /// The page with the continuation column moved under the first part: levels that carry on, one above the other, to the
    /// right of the table with no header, are shifted left and down to where the next rows would be.
    /// </summary>
    private static List<TextLine>? Continued(List<TextLine> lines, Parsed parsed)
    {
        if (parsed.Next is not { } run)
            return null;
        var last = parsed.Levels[^1];
        var side = lines.Where(l => l.X >= run.Min(r => r.Line.X) - 12).ToList();
        var runPitch = run.Count > 2 ? Pitch(run) : parsed.Pitch;
        var runLeft = run.Min(l => l.Line.X) - 12;
        var tolerance = Math.Max(2.5, run[0].Line.Height * 0.4);
        var band = side.Where(l => l.X >= runLeft && l.Y <= run[0].Line.Y + tolerance && l.Y >= run[^1].Line.Y - 2.4 * runPitch).ToHashSet();
        var shiftX = run.Average(l => Centre(l.Line)) - parsed.LevelCentre;
        // Under everything the first part's last row wraps onto.
        var bottom = lines.Where(l => l.X >= parsed.TableLeft && l.X < parsed.TableRight && l.Y <= last.Line.Y + tolerance && l.Y >= last.Line.Y - 2.4 * parsed.Pitch).Min(l => l.Y);
        var shiftY = bottom - parsed.Pitch - run[0].Line.Y;
        return [.. lines.Where(l => !band.Contains(l)), .. band.Select(l => l with { X = l.X - shiftX, Y = l.Y + shiftY })];
    }

    private static Parsed? Read(List<TextLine> lines, string? titleText, CancellationToken cancellationToken)
    {
        if (LevelColumn(lines, cancellationToken) is not { } levels)
            return null;
        var first = levels[0].Line;
        var tolerance = Math.Max(2.5, first.Height * 0.4);
        var pitch = Pitch(levels);
        var levelLeft = levels.Min(l => l.Line.X);
        var levelRight = levels.Max(l => l.Line.X + l.Line.Width);
        var levelCentre = levels.Average(l => Centre(l.Line));
        // The next levels, one above the other to the right: the table carries on in the next column of the page.
        var next = LevelRuns(lines, cancellationToken).Where(r => r[0].Level == levels[^1].Level + 1 && r[0].Line.X > levelRight).OrderBy(r => r[0].Line.X).FirstOrDefault();
        var cap = next is null ? double.MaxValue : next.Min(r => r.Line.X) - 6;

        var header = lines.Where(l => IsFeaturesHeader(l.Text) && l.Y > first.Y + tolerance && l.Y <= first.Y + 4 * pitch && l.X > levelRight).OrderBy(l => l.Y).FirstOrDefault();
        if (header is null)
            return null;
        var title = titleText is null ? null : lines.Where(l => l.Text == titleText.Trim() && l.Y > header.Y).OrderBy(l => l.Y).FirstOrDefault();
        var top = Math.Min(header.Y + 3 * pitch, title is null ? double.MaxValue : title.Y - 1);
        var tableLeft = levelLeft - 12;

        bool InFeatures(TextLine l) => Math.Abs(l.X - header.X) <= 10 || Math.Abs(Centre(l) - Centre(header)) <= 8;

        // Row cells. The table ends where the next cell to the right is more than a gutter away, so prose in a neighbouring
        // column, or the continuation of the table in the next column, is not read as part of it.
        var anchors = levels.Select(l => l.Line).ToHashSet();
        var singles = new List<(int Row, TextLine Line)>();
        var fused = new List<(int Row, TextLine Line, string[] Tokens)>();
        var featureParts = levels.Select(_ => new List<TextLine>()).ToList();
        for (var r = 0; r < levels.Count; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var cell in CandidateDetector.SameRow(lines, levels[r].Line))
            {
                if (anchors.Contains(cell) || cell.X < tableLeft || cell.X >= cap)
                    continue;
                if (InFeatures(cell))
                {
                    featureParts[r].Add(cell);
                    continue;
                }
                var tokens = cell.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (IsDash(cell.Text))
                    singles.Add((r, cell));
                else if (tokens.Length >= 2 && tokens.All(NumericToken))
                    fused.Add((r, cell, tokens));
                else if (cell.Text.Length <= 16)
                    singles.Add((r, cell));
            }
        }
        var tableRight = featureParts.SelectMany(p => p).Select(p => p.X + p.Width).Append(header.X + header.Width).Max();
        var beyondFeatures = false; // the gap between the features and the first column after them is not a gutter
        foreach (var edge in singles.Select(s => s.Line).Concat(fused.Select(f => f.Line)).OrderBy(l => l.X))
        {
            if (edge.X >= tableRight)
            {
                if (beyondFeatures && edge.X - tableRight > Gutter)
                    break;
                beyondFeatures = true;
            }
            tableRight = Math.Max(tableRight, edge.X + edge.Width);
        }
        tableRight += 8;
        singles.RemoveAll(s => s.Line.X >= tableRight);
        fused.RemoveAll(f => f.Line.X >= tableRight);
        var headerZone = lines.Where(l => l.Y > first.Y + tolerance && l.Y < top && l.X >= tableLeft && l.X < tableRight && !ReferenceEquals(l, header)).ToList();

        // Wrapped feature lines: in the features column, off every level's row, under the row they continue.
        var rowOf = levels.Select(l => l.Line).ToList();
        var lastBound = rowOf[^1].Y - 2.4 * pitch;
        foreach (var line in lines.Where(l => InFeatures(l) && l.Y < header.Y - tolerance && l.X >= tableLeft && !anchors.Contains(l)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rowOf.Any(a => Math.Abs(a.Y - line.Y) <= Math.Max(2.5, a.Height * 0.4)))
                continue;
            var above = Enumerable.Range(0, rowOf.Count).Where(i => rowOf[i].Y > line.Y).DefaultIfEmpty(-1).Max();
            if (above < 0)
                continue;
            if (above == rowOf.Count - 1 && line.Y < lastBound)
                continue;
            if (above < rowOf.Count - 1 && line.Y <= rowOf[above + 1].Y)
                continue;
            featureParts[above].Add(line);
        }

        // Columns: merged horizontal extents of the single cells, plus the one-token ordinal headers ("1st", "2").
        var spans = singles.Select(s => (L: s.Line.X, R: s.Line.X + s.Line.Width)).ToList();
        spans.AddRange(headerZone.Where(l => OrdinalHeader().IsMatch(l.Text) && l.X > levelRight && l.Width < 30).Select(l => (L: l.X, R: l.X + l.Width)));
        spans.Sort((a, b) => a.L.CompareTo(b.L));
        var merged = new List<(double L, double R)>();
        foreach (var span in spans)
        {
            if (merged.Count > 0 && span.L <= merged[^1].R + 1.5)
                merged[^1] = (merged[^1].L, Math.Max(merged[^1].R, span.R));
            else
                merged.Add(span);
        }
        var columnCount = merged.Count;
        double CentreOf(int c) => (merged[c].L + merged[c].R) / 2;
        int NearestColumn(double centre) => columnCount == 0 ? -1 : Enumerable.Range(0, columnCount).MinBy(c => Math.Abs(CentreOf(c) - centre));

        var values = levels.Select(_ => new string[columnCount]).ToList();
        foreach (var (row, line) in singles)
            values[row][NearestColumn(Centre(line))] = NormaliseCell(line.Text);
        var digitWidth = DigitWidth(singles.Select(s => s.Line));
        foreach (var (row, line, tokens) in fused)
        {
            var start = NearestColumn(line.X + digitWidth * tokens[0].Length / 2);
            if (start < 0 || start + tokens.Length > columnCount)
                return null;
            for (var t = 0; t < tokens.Length; t++)
                values[row][start + t] = NormaliseCell(tokens[t]);
        }

        // Header labels: a one-column line labels its column; a line over two columns is split by word; wider is a group heading.
        var labelParts = Enumerable.Range(0, columnCount).Select(_ => new List<(double Y, string Text)>()).ToList();
        foreach (var line in headerZone.Where(l => !IsDash(l.Text)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Math.Abs(Centre(line) - levelCentre) <= 6 || line.X + line.Width <= levelRight + 1)
                continue;
            var covered = Enumerable.Range(0, columnCount).Where(c => CentreOf(c) >= line.X - 1 && CentreOf(c) <= line.X + line.Width + 1).ToList();
            var coversLevel = levelCentre >= line.X - 1 && levelCentre <= line.X + line.Width + 1;
            if (coversLevel && covered.Count == 1)
                labelParts[covered[0]].Add((line.Y, FirstWordsAfterLevel(line.Text)));
            else if (coversLevel || InFeatures(line))
                continue;
            else if (covered.Count == 1)
                labelParts[covered[0]].Add((line.Y, line.Text));
            else if (covered.Count == 0)
            {
                var near = NearestColumn(Centre(line));
                if (near >= 0 && Math.Abs(CentreOf(near) - Centre(line)) <= 8)
                    labelParts[near].Add((line.Y, line.Text));
            }
            else if (covered.Count == 2)
            {
                var charWidth = line.Width / line.Text.Length;
                foreach (Match word in Words().Matches(line.Text))
                {
                    var centre = line.X + (word.Index + word.Length / 2.0) * charWidth;
                    var target = covered.MinBy(c => Math.Abs(CentreOf(c) - centre));
                    labelParts[target].Add((line.Y, word.Value));
                }
            }
        }

        // Assemble columns left to right, with Level and Features among them.
        var columns = new List<(ClassTableColumn Column, string[] Cells)>();
        var cellCount = levels.Count;
        var featureCells = featureParts.Select(parts => JoinParts(parts.OrderByDescending(p => p.Y).Where(p => !IsDash(p.Text)).Select(p => p.Text))).ToArray();
        columns.Add((new("Level", ClassColumnRole.Level, 0, levelLeft), levels.Select(l => l.Line.Text).ToArray()));
        columns.Add((new(header.Text, ClassColumnRole.Features, 0, header.X), featureCells));
        for (var c = 0; c < columnCount; c++)
        {
            var label = string.Join(" ", labelParts[c].OrderByDescending(p => p.Y).Select(p => p.Text));
            var (role, slot) = Classify(label);
            columns.Add((new(label, role, slot, merged[c].L), values.Select(v => v[c] ?? string.Empty).ToArray()));
        }
        columns.Sort((a, b) => a.Column.Left.CompareTo(b.Column.Left));
        if (!columns.Any(c => c.Column.Role == ClassColumnRole.PactSlots) || !columns.Any(c => c.Column.Role == ClassColumnRole.PactSlotLevel))
        {
            // "Spell Slots" without "Slot Level" is not Pact Magic.
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i].Column.Role is ClassColumnRole.PactSlots or ClassColumnRole.PactSlotLevel)
                    columns[i] = (columns[i].Column with { Role = ClassColumnRole.Other }, columns[i].Cells);
            }
        }

        var rows = new List<ClassTableRow>();
        for (var r = 0; r < cellCount; r++)
        {
            var features = SplitFeatures(featureCells[r]);
            rows.Add(new(levels[r].Level, columns.Select(c => c.Cells[r]).ToList(), features));
        }
        rows.Sort((a, b) => a.Level.CompareTo(b.Level));
        return new Parsed(new ClassTable(columns.Select(c => c.Column).ToList(), rows), tableRight, tableLeft, pitch, levelCentre, levels, next);
    }

    /// <summary>A table cell as a number: a plus sign is dropped and a dash is zero.</summary>
    public static bool TryNumber(string cell, out int value)
    {
        value = 0;
        if (cell.Length > 0 && IsDash(cell))
            return true;
        var text = cell.TrimStart('+');
        if (text.Length > 2 && char.IsAsciiDigit(text[0]) && text[^2..] is "st" or "nd" or "rd" or "th")
            text = text[..^2]; // a slot level written "3rd"
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Feature names in a Features cell: comma separated outside brackets, a dash alone meaning none.</summary>
    public static IReadOnlyList<string> SplitFeatures(string cell)
    {
        var names = new List<string>();
        var depth = 0;
        var current = new System.Text.StringBuilder();
        foreach (var c in cell)
        {
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            if (c == ',' && depth == 0)
            {
                names.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(c);
        }
        names.Add(current.ToString());
        return [.. names.Select(n => n.Trim()).Where(n => n.Length > 0 && !IsDash(n))];
    }

    /// <summary>Joins the wrapped lines of one cell, repairing a word split by a line-end hyphen ("En-" + "tertainer").</summary>
    private static string JoinParts(IEnumerable<string> parts)
    {
        var text = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (text.Length > 0 && text[^1] == '-' && text.Length > 1 && char.IsLetter(text[^2]) && part.Length > 0 && char.IsLower(part[0]))
                text.Length--;
            else if (text.Length > 0)
                text.Append(' ');
            text.Append(part);
        }
        return text.ToString();
    }

    internal static bool IsDash(string text) => text.Length > 0 && text.All(c => c is '—' or '–' or '-' or '‒' or '−' or '―' or '̶');

    private static string NormaliseCell(string text) => IsDash(text) ? "—" : text;

    private static bool NumericToken(string token) => IsDash(token) || NumberToken().IsMatch(token);

    private static bool IsFeaturesHeader(string text) =>
        text.EndsWith("Features", StringComparison.Ordinal) || text.EndsWith("FEATURES", StringComparison.Ordinal);

    private static double Centre(TextLine line) => line.X + line.Width / 2;

    private static string FirstWordsAfterLevel(string text) =>
        text.StartsWith("Level", StringComparison.OrdinalIgnoreCase) ? text["Level".Length..].Trim() : text;

    private static double DigitWidth(IEnumerable<TextLine> singles)
    {
        var widths = singles.Where(s => s.Text.Length == 1 && char.IsAsciiDigit(s.Text[0]) && s.Height > DashHeight).Select(s => s.Width).Order().ToList();
        return widths.Count == 0 ? 5 : widths[widths.Count / 2];
    }

    private static double Pitch(List<(TextLine Line, int Level)> levels)
    {
        var gaps = new List<double>();
        for (var i = 1; i < levels.Count; i++)
            gaps.Add(levels[i - 1].Line.Y - levels[i].Line.Y);
        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    private static (ClassColumnRole Role, int Slot) Classify(string label)
    {
        var text = string.Join(' ', label.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text.StartsWith("proficiency", StringComparison.Ordinal))
            return (ClassColumnRole.ProficiencyBonus, 0);
        if (OrdinalHeader().Match(text) is { Success: true } ordinal)
            return (ClassColumnRole.SpellSlot, int.Parse(ordinal.Groups["n"].Value, CultureInfo.InvariantCulture));
        if (text.StartsWith("cantrip", StringComparison.Ordinal))
            return (ClassColumnRole.Cantrips, 0);
        if (text is "spell slots" or "slots")
            return (ClassColumnRole.PactSlots, 0);
        if (text is "slot level" or "slots level")
            return (ClassColumnRole.PactSlotLevel, 0);
        if (text.Contains("spells", StringComparison.Ordinal) && (text.Contains("known", StringComparison.Ordinal) || text.Contains("prepared", StringComparison.Ordinal)) || text == "spells")
            return (ClassColumnRole.SpellsTable, 0);
        return (ClassColumnRole.Other, 0);
    }

    /// <summary>Every run of two or more consecutive levels (1st or 1) one above the other in one column.</summary>
    private static List<List<(TextLine Line, int Level)>> LevelRuns(List<TextLine> lines, CancellationToken cancellationToken)
    {
        var clusters = new List<List<TextLine>>();
        foreach (var line in lines.Where(l => LevelCell().IsMatch(l.Text)).OrderBy(l => l.X))
        {
            cancellationToken.ThrowIfCancellationRequested(); // each cell is compared with every cluster so far
            var home = clusters.FirstOrDefault(c => Math.Abs(Centre(c[0]) - Centre(line)) <= 5 || Math.Abs(c[0].X - line.X) <= 2);
            if (home is null)
                clusters.Add([line]);
            else
                home.Add(line);
        }
        var runs = new List<List<(TextLine Line, int Level)>>();
        foreach (var cluster in clusters)
        {
            var run = new List<(TextLine Line, int Level)>();
            foreach (var line in cluster.OrderByDescending(l => l.Y))
            {
                var level = int.Parse(new string([.. line.Text.TakeWhile(char.IsAsciiDigit)]), CultureInfo.InvariantCulture);
                if (run.Count > 0 && level == run[^1].Level + 1 && line.Y < run[^1].Line.Y - 3)
                {
                    run.Add((line, level));
                    continue;
                }
                if (run.Count >= 2)
                    runs.Add(run);
                run = [(line, level)];
            }
            if (run.Count >= 2)
                runs.Add(run);
        }
        return runs;
    }

    /// <summary>The longest run of consecutive levels (1st or 1) one above the other in one column, leftmost on a tie; at least three.</summary>
    private static List<(TextLine Line, int Level)>? LevelColumn(List<TextLine> lines, CancellationToken cancellationToken) =>
        LevelRuns(lines, cancellationToken).Where(r => r.Count >= 3).OrderByDescending(r => r.Count).ThenBy(r => r[0].Line.X).FirstOrDefault();

    [GeneratedRegex(@"^(?:[1-9]|1[0-9]|20)(?:st|nd|rd|th)?$", RegexOptions.None, 100)]
    private static partial Regex LevelCell();

    [GeneratedRegex(@"^(?<n>[1-9])(?:st|nd|rd|th)?$", RegexOptions.None, 100)]
    private static partial Regex OrdinalHeader();

    [GeneratedRegex(@"^\+?[0-9]{1,3}$", RegexOptions.None, 100)]
    private static partial Regex NumberToken();

    [GeneratedRegex(@"\S+", RegexOptions.None, 100)]
    private static partial Regex Words();
}
