using System.Globalization;
using System.Text.RegularExpressions;
using TomeStack.RulesCore;

namespace TomeStack.AppService.CharacterImport;

/// <summary>
/// What the fillable 2014 sheet brings over beyond the rules content (item 14, Q14, D16e amended): the short details, custom
/// items from the equipment text, custom attacks from the weapon rows, and session notes for the prose (R79). Anything that
/// does not fit or does not parse goes to one "Import note" session note instead of being lost, and is counted in
/// <see cref="Lines"/> with the field's label, never its text.
/// </summary>
/// <param name="Brought">Codes of the report's "not brought over" list that this import does bring (<c>notes</c>, <c>backstory</c>, <c>languages</c>, <c>toolProficiencies</c>, <c>appearance</c>).</param>
public sealed record ImportExtras(
    CharacterDetails? Details,
    IReadOnlyList<CustomItem> Items,
    IReadOnlyList<CustomAttack> Attacks,
    IReadOnlyList<SessionNote> Notes,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Brought)
{
    private const string ImportNoteTitle = "Import note";

    /// <summary>The title of the notes that keep a text import's unread lines (item 19a); the splitter adds "(part n of m)".</summary>
    internal const string NotReadTitle = "Not read from the text";

    public static readonly ImportExtras None = new(null, [], [], [], [], []);

    public override string ToString() => $"ImportExtras {{ Items = {Items.Count}, Attacks = {Attacks.Count}, Notes = {Notes.Count}, Lines = {Lines.Count} }}";

    /// <summary>Builds the extras for a sheet; a layout with no free text (D&amp;D Beyond) gives <see cref="None"/>. <paramref name="noteRoom"/> is how many notes the character can still take.</summary>
    public static ImportExtras Build(DdbSheet sheet, DateOnly day, DateTimeOffset now, int noteRoom = Character.MaxNotes, bool keepNotRead = true)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (sheet.Texts is not { } texts)
            return None;
        var lines = new List<string>();
        var overflow = new List<string>(); // sections of the import note
        var attacks = ReadAttacks(texts.Weapons, lines, overflow);
        var items = ReadItems(texts.Equipment, lines, overflow);
        var (languages, tools) = Proficiencies(texts.LanguagesAndTools, lines, overflow);
        var details = DetailsFrom(texts, languages, tools, lines, overflow);

        var notes = new List<SessionNote>();
        if (overflow.Count > 0)
            notes.AddRange(Chunk(ImportNoteTitle, string.Join("\n\n", overflow), day, now));
        var backstoryAt = -1;
        foreach (var (key, title) in DdbSemantics.Prose)
        {
            if (!texts.Prose.TryGetValue(key, out var read) || read.Status != ReadStatus.Ok)
                continue;
            if (key == "backstory")
                backstoryAt = notes.Count;
            notes.AddRange(Chunk(title, read.Value!, day, now));
        }
        // Item 19a: the text importer's lines that were not understood, the user's own text going into their own character.
        // Last, so the structured notes are the ones the character's note limit keeps first; what does not fit is counted below.
        if (sheet.NotRead is { Count: > 0 } notRead)
        {
            if (keepNotRead)
                notes.AddRange(Chunk(NotReadTitle, string.Join('\n', notRead.Select(n => $"Line {n.Line.ToString(CultureInfo.InvariantCulture)}: {n.Text}")), day, now));
            else
                lines.Add($"{notRead.Count.ToString(CultureInfo.InvariantCulture)} line{(notRead.Count == 1 ? "" : "s")} that {(notRead.Count == 1 ? "was" : "were")} not read {(notRead.Count == 1 ? "was" : "were")} dropped, as you chose.");
        }
        var room = Math.Max(0, noteRoom);
        if (notes.Count > room)
        {
            var dropped = notes.Count - room;
            notes.RemoveRange(room, dropped);
            lines.Add($"{dropped.ToString(CultureInfo.InvariantCulture)} session note{(dropped == 1 ? "" : "s")} could not be added: a character holds at most {Character.MaxNotes.ToString(CultureInfo.InvariantCulture)}.");
        }

        var brought = new List<string>();
        if (notes.Count > 0)
            brought.Add("notes");
        if (backstoryAt >= 0 && backstoryAt < notes.Count)
            brought.Add("backstory");
        if (details?.Languages is not null)
            brought.Add("languages");
        if (details?.ToolsAndOther is not null)
            brought.Add("toolProficiencies");
        if (details?.Appearance is not null)
            brought.Add("appearance");
        return new ImportExtras(details, items, attacks, notes, lines, brought);
    }

    // ---- weapon rows (R83) ----

    private static List<CustomAttack> ReadAttacks(IReadOnlyList<WeaponRow> rows, List<string> lines, List<string> overflow)
    {
        var attacks = new List<CustomAttack>();
        for (var i = 0; i < rows.Count; i++)
        {
            var number = (i + 1).ToString(CultureInfo.InvariantCulture);
            var row = rows[i];
            if (row.Name.Status == ReadStatus.Missing && row.ToHit.Status == ReadStatus.Missing && row.Damage.Status == ReadStatus.Missing)
                continue;
            var raw = $"Weapon row {number} (as written): {row.Name.Value} | {row.ToHit.Value} | {row.Damage.Value}";
            string? only = null;
            if (row.Name.Status != ReadStatus.Ok)
                only = "the name is blank";
            else if (row.Name.Value!.Length > Character.MaxName)
                only = $"the name is longer than {Character.MaxName.ToString(CultureInfo.InvariantCulture)} characters";
            else if (ToHit(row.ToHit) is not { } toHit)
                only = "the attack bonus could not be read";
            else
            {
                var (dice, type, problem) = Damage(row.Damage.Status == ReadStatus.Ok ? row.Damage.Value : null);
                attacks.Add(new CustomAttack(Guid.NewGuid(), row.Name.Value, toHit, dice, type, null));
                if (problem is not null)
                {
                    lines.Add($"Weapon row {number}: the {problem} could not be read and was left empty.");
                    overflow.Add(raw);
                }
                continue;
            }
            lines.Add($"Weapon row {number}: {only}, so the row is in the import note only.");
            overflow.Add(raw);
        }
        return attacks;
    }

    private static int? ToHit(Read<string> read)
    {
        if (read.Status != ReadStatus.Ok)
            return null;
        try
        {
            var match = ExtrasPatterns.Bonus().Match(read.Value!);
            if (!match.Success)
                return null;
            var number = int.Parse(match.Groups["digits"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
            if (match.Groups["sign"].Value is "-" or "−")
                number = -number;
            return number is >= Character.MinToHit and <= Character.MaxToHit ? number : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>Splits "1d8+3 slashing" into dice and a damage type; a part that does not parse is null and named in the problem ("damage" or "damage type").</summary>
    private static (string? Dice, string? Type, string? Problem) Damage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null, null);
        try
        {
            var trimmed = text.Trim();
            var match = ExtrasPatterns.DamageText().Match(trimmed);
            string? dice = null;
            string? problem = null;
            var rest = trimmed;
            if (match.Success)
            {
                var candidate = string.Concat(match.Groups["dice"].Value.Where(c => !char.IsWhiteSpace(c))).Replace('−', '-').ToLowerInvariant();
                if (DiceExpression.TryParse(candidate, out _, out _))
                    dice = candidate;
                else
                    problem = "damage";
                rest = match.Groups["rest"].Value.Trim();
            }
            rest = ExtrasPatterns.TrailingDamage().Replace(rest, "").Trim();
            string? type = null;
            if (rest.Length > 0)
            {
                var lower = rest.ToLowerInvariant();
                if (DamageTypes.All.Contains(lower))
                    type = lower;
                else
                    problem ??= match.Success ? "damage type" : "damage";
            }
            return (dice, type, problem);
        }
        catch (RegexMatchTimeoutException)
        {
            return (null, null, "damage");
        }
    }

    // ---- equipment ----

    private static List<CustomItem> ReadItems(Read<string> equipment, List<string> lines, List<string> overflow)
    {
        var items = new List<CustomItem>();
        if (equipment.Status != ReadStatus.Ok)
            return items;
        var left = new List<string>();
        foreach (var raw in equipment.Value!.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            var (name, quantity) = Item(line);
            if (name.Length == 0)
                continue;
            if (name.Length > Character.MaxName || items.Count >= Character.MaxCustomItems)
                left.Add(line);
            else
                items.Add(new CustomItem(Guid.NewGuid(), name, quantity, null, false));
        }
        if (left.Count > 0)
        {
            lines.Add(left.Count == 1
                ? "1 equipment line was not added as an item and is in the import note."
                : $"{left.Count.ToString(CultureInfo.InvariantCulture)} equipment lines were not added as items and are in the import note.");
            overflow.Add("Equipment lines not added as items:\n" + string.Join('\n', left));
        }
        return items;
    }

    /// <summary>A line's name and quantity: "(3)", "2 x", "2x" and "x4" are counts; a bare leading number is part of the name.</summary>
    private static (string Name, int Quantity) Item(string line)
    {
        try
        {
            line = ExtrasPatterns.Bullet().Replace(line, "").Trim();
            foreach (var pattern in new[] { ExtrasPatterns.LeadingCount(), ExtrasPatterns.TrailingTimes(), ExtrasPatterns.TrailingParen() })
            {
                var match = pattern.Match(line);
                if (match.Success && int.TryParse(match.Groups["q"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var q) && q is >= 1 and <= EquipmentEntry.MaxQuantity)
                    return (match.Groups["n"].Value.Trim(), q);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // An unreadable line is kept whole as a one-count item.
        }
        return (line, 1);
    }

    // ---- languages and tools ----

    /// <param name="labelMatch">How a line is tested for a "Languages:" or "Tools:" label; a seam so a pattern timeout can be tested.</param>
    internal static (List<string>? Languages, List<string>? Tools) Proficiencies(Read<string> read, List<string> lines, List<string> overflow, Func<string, Match>? labelMatch = null)
    {
        if (read.Status != ReadStatus.Ok)
            return (null, null);
        labelMatch ??= l => ExtrasPatterns.ProficiencyLabel().Match(l);
        var languages = new List<string>();
        var tools = new List<string>();
        var left = new List<string>();
        var unread = new List<string>();
        var current = tools;
        var all = read.Value!.Split('\n');
        for (var i = 0; i < all.Length; i++)
        {
            var line = all[i].Trim();
            var target = current;
            var entries = new List<string>();
            try
            {
                var content = line;
                if (labelMatch(line) is { Success: true } label)
                {
                    target = label.Groups["label"].Value.StartsWith("lang", StringComparison.OrdinalIgnoreCase) ? languages : tools;
                    content = label.Groups["rest"].Value;
                }
                foreach (var part in SplitOutsideBrackets(content))
                {
                    var entry = ExtrasPatterns.Bullet().Replace(part.Trim(), "").Trim();
                    if (entry.Length > 0)
                        entries.Add(entry);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Lines read so far are kept; this one and the rest are not guessed at: they go to the note, counted.
                unread = [.. all[i..].Select(l => l.Trim()).Where(l => l.Length > 0)];
                break;
            }
            current = target;
            foreach (var entry in entries)
            {
                if (entry.Length > Character.MaxDetailListEntryLength || current.Count >= Character.MaxDetailListEntries)
                    left.Add(entry);
                else
                    current.Add(entry);
            }
        }
        if (unread.Count > 0)
        {
            lines.Add(unread.Count == 1
                ? "1 proficiency or language line could not be read and is in the import note."
                : $"{unread.Count.ToString(CultureInfo.InvariantCulture)} proficiency or language lines could not be read and are in the import note.");
            overflow.Add("Proficiency and language lines not read:\n" + string.Join('\n', unread));
        }
        if (left.Count > 0)
        {
            lines.Add(left.Count == 1
                ? "1 language or tool entry did not fit the lists and is in the import note."
                : $"{left.Count.ToString(CultureInfo.InvariantCulture)} language or tool entries did not fit the lists and are in the import note.");
            overflow.Add("Language and tool entries not added to the lists:\n" + string.Join('\n', left));
        }
        return (languages.Count > 0 ? languages : null, tools.Count > 0 ? tools : null);
    }

    /// <summary>Splits a list line on commas and semicolons that are not inside brackets, so "Common (old, regional)" stays one entry.</summary>
    private static List<string> SplitOutsideBrackets(string content)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            switch (content[i])
            {
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ',' or ';' when depth == 0:
                    parts.Add(content[start..i]);
                    start = i + 1;
                    break;
            }
        }
        parts.Add(content[start..]);
        return parts;
    }

    // ---- details ----

    private static CharacterDetails? DetailsFrom(DdbTexts texts, List<string>? languages, List<string>? tools, List<string> lines, List<string> overflow)
    {
        string? Short(string key)
        {
            if (!texts.Details.TryGetValue(key, out var read) || read.Status != ReadStatus.Ok)
                return null;
            if (read.Value!.Length <= Character.MaxDetailText)
                return read.Value;
            var title = DdbSemantics.TextTitles[key == "alignment" ? "details.alignment" : $"details.appearance.{key}"];
            lines.Add($"{title} is longer than {Character.MaxDetailText.ToString("N0", CultureInfo.InvariantCulture)} characters and is in the import note instead.");
            overflow.Add($"{title} (as written):\n{read.Value}");
            return null;
        }
        var look = new Appearance(Short("age"), Short("height"), Short("weight"), Short("eyes"), Short("skin"), Short("hair"));
        var appearance = look == new Appearance() ? null : look;
        var alignment = Short("alignment");
        var details = new CharacterDetails(Alignment: alignment, Appearance: appearance, Languages: languages, ToolsAndOther: tools);
        return details == new CharacterDetails() ? null : details;
    }

    // ---- notes ----

    /// <summary>One note for a title and text, or consecutive "(part i of n)" notes when it is longer than a note can hold (cut at whitespace).</summary>
    private static List<SessionNote> Chunk(string title, string body, DateOnly day, DateTimeOffset now)
    {
        var budget = SessionNote.MaxText - (title.Length + " (part 99 of 99)".Length + 2);
        var parts = new List<string>();
        var rest = body.Trim();
        while (rest.Length > budget)
        {
            var at = -1;
            for (var i = budget; i >= budget / 2; i--)
            {
                if (char.IsWhiteSpace(rest[i]))
                {
                    at = i;
                    break;
                }
            }
            if (at < 0)
                at = char.IsHighSurrogate(rest[budget - 1]) ? budget - 1 : budget;
            var head = rest[..at].Trim();
            if (head.Length > 0)
                parts.Add(head);
            rest = rest[at..].TrimStart();
        }
        if (rest.Length > 0)
            parts.Add(rest);
        return
        [
            .. parts.Select((part, i) => new SessionNote(
                Guid.NewGuid(),
                day,
                parts.Count == 1 ? $"{title}\n\n{part}" : $"{title} (part {(i + 1).ToString(CultureInfo.InvariantCulture)} of {parts.Count.ToString(CultureInfo.InvariantCulture)})\n\n{part}",
                now)),
        ];
    }
}

/// <summary>The patterns of <see cref="ImportExtras"/>: anchored where they read a whole value, and time-limited (100 ms).</summary>
internal static partial class ExtrasPatterns
{
    [GeneratedRegex(@"^(?<sign>[+\-−]?)\s*(?<digits>[0-9]{1,5})$", RegexOptions.None, ImportRegex.TimeoutMs)]
    internal static partial Regex Bonus();

    /// <summary>Dice characters up to the last digit, then whatever follows (a damage type, usually).</summary>
    [GeneratedRegex(@"^(?<dice>[0-9dD\s+\-−]*[0-9])\s*(?<rest>.*)$", RegexOptions.Singleline, ImportRegex.TimeoutMs)]
    internal static partial Regex DamageText();

    [GeneratedRegex(@"(?:^|\s)[Dd][Aa][Mm][Aa][Gg][Ee]$", RegexOptions.None, ImportRegex.TimeoutMs)]
    internal static partial Regex TrailingDamage();

    [GeneratedRegex(@"^(?:[-*·]\s+|•\s*)", RegexOptions.None, ImportRegex.TimeoutMs)]
    internal static partial Regex Bullet();

    [GeneratedRegex(@"^(?<q>[0-9]{1,4})\s*[xX×]\s+(?<n>\S.*)$", RegexOptions.None, ImportRegex.TimeoutMs)]
    internal static partial Regex LeadingCount();

    [GeneratedRegex(@"^(?<n>\S.*?)\s+[xX×](?<q>[0-9]{1,4})$", RegexOptions.None, ImportRegex.TimeoutMs)]
    internal static partial Regex TrailingTimes();

    [GeneratedRegex(@"^(?<n>\S.*?)\s*\((?<q>[0-9]{1,4})\)$", RegexOptions.None, ImportRegex.TimeoutMs)]
    internal static partial Regex TrailingParen();

    [GeneratedRegex(@"^(?<label>languages?|tools?|tool proficiencies)\s*:\s*(?<rest>.*)$", RegexOptions.IgnoreCase, ImportRegex.TimeoutMs)]
    internal static partial Regex ProficiencyLabel();
}
