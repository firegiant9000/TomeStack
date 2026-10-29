using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace TomeStack.ImportWorker.Extraction;

/// <summary>
/// Import Lab L7 (design 7.2): reads a Markdown file into the same <see cref="ExtractedPage"/> shape the PDF extractor
/// produces, so the existing detector reads both. The grammar is a deterministic subset (ATX headings, paragraphs, run-in
/// heads, list items, GFM pipe tables, page markers, front matter, code fences); anything else is plain text. It parses
/// untrusted bytes with a hand-written line scanner: no Markdown library, no HTML kept as markup (tags are removed, and
/// <c>script</c> / <c>style</c> / comment content is dropped), nothing executed or fetched, and every limit is checked
/// before the heavy work. Positions follow PDF conventions (origin bottom-left, so a line below another has a smaller Y).
/// </summary>
public sealed class MarkdownExtractor : IDocumentExtractor
{
    public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(scope);
        var bytes = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        var pages = Parse(bytes);
        var pageCount = pages.Count == 0 ? 0 : pages[^1].PageNumber;
        yield return new DocumentOpened(pageCount);

        var (first, last) = scope.Resolve(pageCount);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (page.PageNumber >= first && page.PageNumber <= last)
                yield return page;
        }
    }

    private static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new ExtractionException("markdown.missing", "The Markdown file is missing.");
        if (info.Length > ExtractionLimits.MarkdownMaxBytes)
            throw TooLarge();
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked by another program or not readable: a code, never the path the exception names.
            throw new ExtractionException("markdown.unreadable", "The Markdown file could not be opened. Close it in other programs and check you can read it, then try again.");
        }
    }

    private static ExtractionException TooLarge() =>
        new("markdown.too-large", $"The Markdown file is over a limit: {ExtractionLimits.MarkdownMaxBytes / (1024 * 1024)} MB, {ExtractionLimits.MarkdownMaxLines:N0} lines, {ExtractionLimits.MarkdownMaxLineLength:N0} characters in a line, {ExtractionLimits.MarkdownMaxPages:N0} pages or {ExtractionLimits.MarkdownMaxTableColumns} table columns.");

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The pure core: the pages of a Markdown file. Throws <see cref="ExtractionException"/> with <c>markdown.too-large</c> or
    /// <c>markdown.encoding</c> and nothing else; a result is never partial.
    /// </summary>
    public static IReadOnlyList<ExtractedPage> Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > ExtractionLimits.MarkdownMaxBytes)
            throw TooLarge();
        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
            utf8 = utf8[3..];
        string text;
        try
        {
            text = StrictUtf8.GetString(utf8);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException)
        {
            throw new ExtractionException("markdown.encoding", "The file is not valid UTF-8 text. Save it as UTF-8, then import it again.");
        }
        return new Scanner(SplitLines(text)).Run();
    }

    /// <summary>Lines split at \n, \r\n or a lone \r, each with control characters made spaces; the line and length limits are checked as they are found.</summary>
    private static string[] SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] is not ('\n' or '\r'))
                continue;
            if (i == text.Length && start == i)
                break; // the text ended at a line break
            if (i - start > ExtractionLimits.MarkdownMaxLineLength || lines.Count >= ExtractionLimits.MarkdownMaxLines)
                throw TooLarge();
            lines.Add(Sanitize(text.AsSpan(start, i - start)));
            if (i < text.Length && text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            start = i + 1;
        }
        return [.. lines];
    }

    /// <summary>Control characters (and a stray byte-order mark) must not reach page text: tabs and other controls become spaces.</summary>
    private static string Sanitize(ReadOnlySpan<char> line)
    {
        if (line.IndexOfAnyExceptInRange(' ', '~') < 0)
            return line.ToString();
        var sb = new StringBuilder(line.Length);
        foreach (var c in line)
        {
            if (c == (char)0xFEFF)
                continue;
            sb.Append(c < ' ' || c is >= '\u007F' and <= '\u009F' ? ' ' : c);
        }
        return sb.ToString();
    }

    private enum Html { None, Comment, Script, Style }

    private enum Open { None, Paragraph, ListItem }

    private sealed class PageBuilder(int number)
    {
        public int Number { get; } = number;
        public List<TextBlock> Blocks { get; } = [];
        public List<string> Warnings { get; } = [];
        public int Chars { get; set; }
        public int LineCount { get; set; }
    }

    private sealed class Scanner(string[] lines)
    {
        private const double Left = 72;
        private const double Top = 756;
        private const double Pitch = 12;
        private const double BodySize = 10;
        private const double ColumnWidth = 90;
        private const double PageWidth = 612;
        private const double PageHeight = 792;
        private const int LinesPerPage = 60;
        private const int MaxLinkText = 300;
        private const int MaxLinkUrl = 2_000;

        private static readonly SearchValues<char> InlineMarks = SearchValues.Create("[*_`~\\!");

        private readonly List<ExtractedPage> _pages = [];
        private readonly List<string> _documentWarnings = [];
        private PageBuilder? _page;
        private int _pageNumber;
        private int _pageStartLine;
        private int _row;
        private bool _markersSeen;

        // The last line holding each closer: an opener with no closer after it is a one-line tag, not the start of a span that swallows the rest.
        private int _lastComment = -1;
        private int _lastScript = -1;
        private int _lastStyle = -1;

        private Open _open = Open.None;
        private readonly List<(string Text, int Row)> _openLines = [];

        private static double YOf(int row) => Top - Pitch * row;

        public IReadOnlyList<ExtractedPage> Run()
        {
            var n = lines.Length;
            for (var k = 0; k < n; k++)
            {
                if (lines[k].Contains("-->", StringComparison.Ordinal))
                    _lastComment = k;
                if (lines[k].Contains("</script", StringComparison.OrdinalIgnoreCase))
                    _lastScript = k;
                if (lines[k].Contains("</style", StringComparison.OrdinalIgnoreCase))
                    _lastStyle = k;
            }
            var i = FrontMatterEnd();
            _pageStartLine = i;
            var html = Html.None;
            while (i < n)
            {
                var raw = lines[i];
                if (html == Html.None && TryMarker(raw, out var marker))
                {
                    FlushOpen();
                    if (marker > _pageNumber)
                    {
                        FinishPage();
                        _pageNumber = marker;
                        _markersSeen = true;
                        _pageStartLine = i + 1;
                        _row = 0;
                    }
                    else
                    {
                        AddWarning("markdown.page-order");
                    }
                    i++;
                    continue;
                }

                var t = CleanHtml(raw, i, ref html).Trim();
                if (t.Length == 0)
                {
                    FlushOpen();
                    _row++;
                    i++;
                    continue;
                }

                if (TryFence(t, out var fenceChar, out var fenceLength))
                {
                    FlushOpen();
                    i = Fence(i, fenceChar, fenceLength);
                    continue;
                }

                if (Heading(t, out var level, out var headingText))
                {
                    FlushOpen();
                    StartBlock(i);
                    var size = level switch { 1 => 24d, 2 => 18d, 3 => 14d, _ => 12d };
                    var text = Text(headingText);
                    if (text.Length > 0)
                        AddBlock(OneLine(text, size, bold: true, _row));
                    _row++;
                    i++;
                    continue;
                }

                if (IsThematicBreak(t))
                {
                    FlushOpen();
                    _row++;
                    i++;
                    continue;
                }

                if (html == Html.None && t.Contains('|', StringComparison.Ordinal) && i + 1 < n && IsSeparator(lines[i + 1], SplitRaw(t).Count))
                {
                    FlushOpen();
                    i = Table(i, t, ref html);
                    continue;
                }

                if (TryListItem(t, out var item))
                {
                    FlushOpen();
                    StartBlock(i);
                    _open = Open.ListItem;
                    _openLines.Add((item, _row));
                    _row++;
                    i++;
                    continue;
                }

                if (_open == Open.None)
                {
                    StartBlock(i);
                    _open = Open.Paragraph;
                }
                _openLines.Add((t, _row));
                _row++;
                i++;
            }
            FlushOpen();
            FinishPage();
            // Warnings about the document (front matter, a marker out of order) go on its first page; with no page they have nowhere to go.
            if (_pages.Count > 0 && _documentWarnings.Count > 0)
                _pages[0] = _pages[0] with { Warnings = [.. _documentWarnings, .. _pages[0].Warnings ?? []] };
            else if (_pages.Count == 0 && _documentWarnings.Count > 0)
            {
                // No page to carry them (only front matter, or markers and nothing else): like a PDF page with no text, one empty
                // page says "page.no-text" and carries the warnings, so they reach the output.
                _pages.Add(new ExtractedPage(Math.Max(1, _pageNumber), "", false, [], PageWidth, PageHeight, [.. _documentWarnings, "page.no-text"]));
            }
            return _pages;
        }

        // ---- pages ----

        private void AddWarning(string code)
        {
            if (!_documentWarnings.Contains(code))
                _documentWarnings.Add(code);
        }

        /// <summary>Front matter: a first line "---" closed by a later "---" or "..."; the lines up to and including the closing one are skipped. Returns the first line to read.</summary>
        private int FrontMatterEnd()
        {
            if (lines.Length == 0 || lines[0].Trim() != "---")
                return 0;
            for (var j = 1; j < lines.Length; j++)
            {
                var t = lines[j].Trim();
                if (t is "---" or "...")
                {
                    AddWarning("markdown.front-matter-ignored");
                    return j + 1;
                }
            }
            return 0;
        }

        /// <summary>At a block boundary: without any page marker, a page ends after 60 source lines.</summary>
        private void StartBlock(int line)
        {
            if (_markersSeen || _page is not { Blocks.Count: > 0 } || line - _pageStartLine < LinesPerPage)
                return;
            FinishPage();
            _pageNumber++;
            if (_pageNumber > ExtractionLimits.MarkdownMaxPages)
                throw TooLarge();
            _pageStartLine = line;
            _row = 0;
        }

        private void AddBlock(TextBlock block)
        {
            if (_page is null)
            {
                if (_pageNumber == 0)
                    _pageNumber = 1;
                if (_pageNumber > ExtractionLimits.MarkdownMaxPages)
                    throw TooLarge();
                _page = new PageBuilder(_pageNumber);
            }
            var limits = ExtractionLimits.Default;
            if (_page.Blocks.Count >= limits.MaxBlocksPerPage)
            {
                if (!_page.Warnings.Contains("page.blocks-truncated"))
                    _page.Warnings.Add("page.blocks-truncated");
                return;
            }
            // Like PdfPigExtractor.Bounded: a page keeps a bounded prefix of text and of lines, and says so.
            var room = limits.MaxTextPerPage - _page.Chars;
            var lineRoom = limits.MaxLinesPerPage - _page.LineCount;
            if (room <= 0 || lineRoom <= 0)
            {
                Warn(_page, "page.text-truncated");
                return;
            }
            var text = block.Text;
            var kept = block.Lines;
            if (text.Length > room)
            {
                text = Prefix(text, room);
                Warn(_page, "page.text-truncated");
            }
            if (kept is not null)
            {
                var lineChars = 0;
                var cut = new List<TextLine>(Math.Min(kept.Count, lineRoom));
                foreach (var line in kept)
                {
                    if (cut.Count == lineRoom || lineChars >= room)
                    {
                        Warn(_page, "page.text-truncated");
                        break;
                    }
                    var lineText = line.Text.Length > room - lineChars ? Prefix(line.Text, room - lineChars) : line.Text;
                    if (lineText.Length != line.Text.Length)
                        Warn(_page, "page.text-truncated");
                    lineChars += lineText.Length;
                    cut.Add(lineText.Length == line.Text.Length ? line : line with { Text = lineText });
                }
                if (cut.Count != kept.Count || cut.Where((l, k) => !ReferenceEquals(l, kept[k])).Any())
                    kept = cut;
            }
            _page.Chars += text.Length;
            _page.LineCount += kept?.Count ?? 0;
            _page.Blocks.Add(ReferenceEquals(text, block.Text) && ReferenceEquals(kept, block.Lines) ? block : block with { Text = text, Lines = kept });
        }

        private static void Warn(PageBuilder page, string code)
        {
            if (!page.Warnings.Contains(code))
                page.Warnings.Add(code);
        }

        /// <summary>The first <paramref name="length"/> characters, not ending inside a surrogate pair.</summary>
        private static string Prefix(string text, int length) =>
            length > 0 && char.IsHighSurrogate(text[length - 1]) ? text[..(length - 1)] : text[..length];

        private void FinishPage()
        {
            if (_page is { Blocks.Count: > 0 } page)
                _pages.Add(new ExtractedPage(page.Number, string.Join("\n\n", page.Blocks.Select(b => b.Text)), false, page.Blocks, PageWidth, PageHeight, page.Warnings));
            _page = null;
        }

        /// <summary><c>&lt;!-- page: N --&gt;</c> on a line of its own. A number over the page limit fails the file; page 0 is an ordinary comment.</summary>
        private static bool TryMarker(string line, out int number)
        {
            number = 0;
            var t = line.AsSpan().Trim();
            if (!t.StartsWith("<!--", StringComparison.Ordinal) || !t.EndsWith("-->", StringComparison.Ordinal) || t.Length < 7)
                return false;
            var inner = t[4..^3].Trim();
            if (!inner.StartsWith("page:", StringComparison.OrdinalIgnoreCase))
                return false;
            var digits = inner[5..].Trim();
            if (digits.Length == 0 || digits.ContainsAnyExceptInRange('0', '9'))
                return false;
            if (digits.Length > 7)
                throw TooLarge();
            number = int.Parse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            if (number > ExtractionLimits.MarkdownMaxPages)
                throw TooLarge();
            return number > 0;
        }

        // ---- blocks ----

        private static double WidthOf(string text, double size) => Math.Min(468, text.Length * size * 0.5);

        private static TextBlock OneLine(string text, double size, bool bold, int row)
        {
            var y = YOf(row);
            var width = WidthOf(text, size);
            return new TextBlock(text, Left, y, width, size, size, bold, [new TextLine(text, Left, y, width, size)]);
        }

        /// <summary>The open paragraph or list item becomes one block with one line per source line, like a PDF text block.</summary>
        private void FlushOpen()
        {
            if (_open == Open.None)
                return;
            _open = Open.None;
            var text = new List<TextLine>();
            foreach (var (source, row) in _openLines)
            {
                var line = Text(source);
                if (line.Length > 0)
                    text.Add(new TextLine(line, Left, YOf(row), WidthOf(line, BodySize), BodySize));
            }
            _openLines.Clear();
            if (text.Count == 0)
                return;
            var bottom = text[^1].Y;
            AddBlock(new TextBlock(string.Join('\n', text.Select(l => l.Text)), Left, bottom, text.Max(l => l.Width), text[0].Y + BodySize - bottom, BodySize, false, text));
        }

        private static bool Heading(string t, out int level, out string text)
        {
            level = 0;
            text = "";
            var hashes = 0;
            while (hashes < t.Length && t[hashes] == '#')
                hashes++;
            if (hashes is < 1 or > 4 || (hashes < t.Length && t[hashes] != ' '))
                return false;
            level = hashes;
            var body = t[hashes..].Trim();
            // A closing run of '#' after a space is not part of the text.
            var end = body.Length;
            while (end > 0 && body[end - 1] == '#')
                end--;
            if (end < body.Length && (end == 0 || body[end - 1] == ' '))
                body = body[..end].TrimEnd();
            text = body;
            return true;
        }

        private static bool IsThematicBreak(string t)
        {
            var mark = t[0];
            if (mark is not ('-' or '*' or '_'))
                return false;
            var count = 0;
            foreach (var c in t)
            {
                if (c == mark)
                    count++;
                else if (c != ' ')
                    return false;
            }
            return count >= 3;
        }

        private static bool TryListItem(string t, out string text)
        {
            text = "";
            if (t[0] is '-' or '*' or '+' && (t.Length == 1 || t[1] == ' '))
            {
                text = t[1..].Trim();
                return true;
            }
            var digits = 0;
            while (digits < t.Length && digits < 10 && t[digits] is >= '0' and <= '9')
                digits++;
            if (digits is >= 1 and <= 9 && digits + 1 < t.Length && t[digits] is '.' or ')' && t[digits + 1] == ' ')
            {
                text = t[(digits + 2)..].Trim();
                return true;
            }
            return false;
        }

        private static bool TryFence(string t, out char mark, out int length)
        {
            mark = t[0];
            length = 0;
            if (mark is not ('`' or '~'))
                return false;
            while (length < t.Length && t[length] == mark)
                length++;
            return length >= 3;
        }

        /// <summary>A code fence: its lines become one plain-text block (no Markdown inside is read; HTML in it is still removed). An unclosed fence runs to the end.</summary>
        private int Fence(int open, char mark, int length)
        {
            StartBlock(open);
            var rows = new List<(string Text, int Row)>();
            _row++;
            var i = open + 1;
            for (; i < lines.Length; i++)
            {
                var closing = lines[i].Trim();
                if (closing.Length >= length && closing.All(c => c == mark))
                {
                    _row++;
                    i++;
                    break;
                }
                var state = Html.None;
                var line = Normalize(CleanHtml(lines[i], i, ref state).Trim());
                if (line.Length > 0)
                    rows.Add((line, _row));
                _row++;
            }
            if (rows.Count > 0)
            {
                var textLines = rows.Select(r => new TextLine(r.Text, Left, YOf(r.Row), WidthOf(r.Text, BodySize), BodySize)).ToList();
                var bottom = textLines[^1].Y;
                AddBlock(new TextBlock(string.Join('\n', textLines.Select(l => l.Text)), Left, bottom, textLines.Max(l => l.Width), textLines[0].Y + BodySize - bottom, BodySize, false, textLines));
            }
            return i;
        }

        // ---- tables ----

        /// <summary>The cells of a pipe row, with the outer pipes removed and "\|" kept as a pipe. Not processed further.</summary>
        private static List<string> SplitRaw(string row)
        {
            var s = row.Trim();
            if (s.StartsWith('|'))
                s = s[1..];
            if (s.EndsWith('|') && !s.EndsWith("\\|", StringComparison.Ordinal))
                s = s[..^1];
            var cells = new List<string>();
            var cell = new StringBuilder();
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] == '|')
                {
                    cell.Append('|');
                    i++;
                }
                else if (s[i] == '|')
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else
                {
                    cell.Append(s[i]);
                }
            }
            cells.Add(cell.ToString());
            return cells;
        }

        /// <summary>The delimiter row of a table: as many cells as the header, each only hyphens with an optional colon at either end.</summary>
        private static bool IsSeparator(string line, int headerCells)
        {
            if (!line.Contains('|', StringComparison.Ordinal) || line.Contains('<', StringComparison.Ordinal))
                return false;
            var cells = SplitRaw(line);
            if (cells.Count != headerCells)
                return false;
            foreach (var cell in cells)
            {
                var c = cell.AsSpan().Trim();
                if (c.StartsWith(":"))
                    c = c[1..];
                if (c.EndsWith(":"))
                    c = c[..^1];
                if (c.Length == 0 || c.ContainsAnyExceptInRange('-', '-'))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// A GFM pipe table: one block per row, each non-empty cell a text line at <c>X = 72 + column * 90</c> on the row's own Y, the
        /// header bold. A table whose rows do not all have the header's cell count is not a table: each row becomes a text block.
        /// </summary>
        private int Table(int start, string header, ref Html html)
        {
            StartBlock(start);
            var rows = new List<(List<string> Cells, int Row)> { (Cells(header), _row) };
            _row++;
            var i = start + 2; // the separator takes no row
            while (i < lines.Length)
            {
                var state = html;
                var line = CleanHtml(lines[i], i, ref state).Trim();
                if (state != Html.None || line.Length == 0 || !line.Contains('|', StringComparison.Ordinal))
                    break;
                rows.Add((Cells(line), _row));
                _row++;
                i++;
            }
            var ragged = rows.Any(r => r.Cells.Count != rows[0].Cells.Count);
            for (var r = 0; r < rows.Count; r++)
            {
                var (cells, row) = rows[r];
                var y = YOf(row);
                var text = new List<TextLine>();
                for (var col = 0; col < cells.Count; col++)
                {
                    if (cells[col].Length > 0)
                        text.Add(new TextLine(cells[col], ragged ? Left : Left + col * ColumnWidth, y, Math.Min(ColumnWidth, WidthOf(cells[col], BodySize)), BodySize));
                }
                if (text.Count == 0)
                    continue;
                var joined = string.Join("  ", text.Select(l => l.Text));
                if (ragged)
                {
                    text = [new TextLine(joined, Left, y, WidthOf(joined, BodySize), BodySize)];
                }
                var x = text[0].X;
                AddBlock(new TextBlock(joined, x, y, text[^1].X + text[^1].Width - x, BodySize, BodySize, r == 0 && !ragged, text));
            }
            return i;
        }

        private static List<string> Cells(string row)
        {
            var raw = SplitRaw(row);
            if (raw.Count > ExtractionLimits.MarkdownMaxTableColumns)
                throw TooLarge();
            return [.. raw.Select(Text)];
        }

        // ---- text ----

        private static string Text(string source) => Normalize(Inline(source.Trim())).Trim();

        /// <summary>
        /// The last pass, after normalisation: stripping can leave a tag opener behind ("&lt;&lt;b&gt;script&gt;" leaves "&lt;script&gt;") and
        /// FormKC folds a fullwidth U+FF1C into "&lt;" after the strip. Any run of '&lt;' that is followed by a letter, '!', '?' or '/'
        /// is removed, so no text ever holds a tag opener. Entities ("&amp;lt;") are NOT decoded anywhere, so they stay literal text and
        /// can never become an opener.
        /// </summary>
        private static string Defang(string text)
        {
            if (text.IndexOf('<', StringComparison.Ordinal) < 0)
                return text;
            var sb = new StringBuilder(text.Length);
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] != '<')
                {
                    sb.Append(text[i]);
                    i++;
                    continue;
                }
                var end = i;
                while (end < text.Length && text[end] == '<')
                    end++;
                var opens = end < text.Length && (IsAsciiLetter(text[end]) || text[end] is '!' or '?' or '/');
                if (!opens)
                    sb.Append('<', end - i);
                i = end;
            }
            return sb.ToString();
        }

        /// <summary>The PDF extractor's normalisation (compatibility forms, soft hyphens and hyphen variants), so both readers feed the detector alike.</summary>
        private static string Normalize(string text)
        {
            try
            {
                return Defang(PdfPigExtractor.Normalize(text));
            }
            catch (ArgumentException)
            {
                return Defang(text);
            }
        }

        /// <summary>Links keep their text, images go, emphasis and code marks go, and a backslash escape gives the character itself.</summary>
        private static string Inline(string s)
        {
            if (s.AsSpan().IndexOfAny(InlineMarks) < 0)
                return s;
            return Emphasis(Links(s));
        }

        private static string Links(string s)
        {
            StringBuilder? sb = null;
            var copied = 0;
            var from = 0;
            while (from < s.Length - 1)
            {
                var k = s.IndexOf("](", from, StringComparison.Ordinal);
                if (k < 0)
                    break;
                from = k + 2;
                var window = Math.Min(MaxLinkText, k);
                var back = s.AsSpan(k - window, window).LastIndexOf('[');
                if (back < 0)
                    continue;
                var open = k - window + back;
                if (open < copied || s.AsSpan(open + 1, k - open - 1).Contains(']'))
                    continue;
                var forward = s.AsSpan(k + 2, Math.Min(MaxLinkUrl, s.Length - k - 2)).IndexOf(')');
                if (forward < 0)
                    continue;
                var close = k + 2 + forward;
                sb ??= new StringBuilder(s.Length);
                var image = open > copied && s[open - 1] == '!';
                sb.Append(s, copied, (image ? open - 1 : open) - copied);
                if (!image)
                    sb.Append(s, open + 1, k - open - 1);
                copied = close + 1;
                from = copied;
            }
            if (sb is null)
                return s;
            return sb.Append(s, copied, s.Length - copied).ToString();
        }

        private static string Emphasis(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\\' && i + 1 < s.Length && s[i + 1] is > ' ' and < '\u007F' and not ('<' or '>') && !char.IsLetterOrDigit(s[i + 1]))
                {
                    sb.Append(s[i + 1]);
                    i++;
                }
                else if (c == '`')
                {
                    // dropped: the code's text stays
                }
                else if (c == '~' && i + 1 < s.Length && s[i + 1] == '~')
                {
                    i++;
                }
                else if (c is '*' or '_')
                {
                    var end = i;
                    while (end + 1 < s.Length && s[end + 1] == c)
                        end++;
                    var runLength = end - i + 1;
                    var before = i == 0 ? ' ' : s[i - 1];
                    var after = end + 1 == s.Length ? ' ' : s[end + 1];
                    // A mark inside a word (snake_case, 5*4) or between spaces (2 * 3) is text; a run of "**" is always emphasis.
                    var literal = (runLength == 1 || c == '_') && ((char.IsLetterOrDigit(before) && char.IsLetterOrDigit(after)) || (char.IsWhiteSpace(before) && char.IsWhiteSpace(after)));
                    if (literal)
                        sb.Append(c, runLength);
                    i = end;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        // ---- HTML ----

        private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
        {
            "br", "p", "div", "li", "ul", "ol", "tr", "td", "th", "table", "hr", "blockquote", "section", "article", "h1", "h2", "h3", "h4", "h5", "h6", "pre",
        };

        /// <summary>
        /// Removes HTML: tags go (a block-level tag leaves a space), comments, <c>script</c> and <c>style</c> content go, and an
        /// opened comment or element carries to the following lines through <paramref name="state"/>. A tag with no closing '&gt;'
        /// on its line ends at the line's end. A '&lt;' that does not start a tag stays text.
        /// </summary>
        private string CleanHtml(string line, int lineIndex, ref Html state)
        {
            if (state == Html.None && line.IndexOf('<', StringComparison.Ordinal) < 0)
                return line;
            var sb = new StringBuilder(line.Length);
            var i = 0;
            while (i < line.Length)
            {
                switch (state)
                {
                    case Html.Comment:
                        var endComment = line.IndexOf("-->", i, StringComparison.Ordinal);
                        if (endComment < 0)
                            return sb.ToString();
                        i = endComment + 3;
                        state = Html.None;
                        continue;
                    case Html.Script or Html.Style:
                        var closeTag = state == Html.Script ? "</script" : "</style";
                        var close = line.IndexOf(closeTag, i, StringComparison.OrdinalIgnoreCase);
                        var gt = close < 0 ? -1 : line.IndexOf('>', close);
                        if (gt < 0)
                        {
                            if (close >= 0)
                                state = Html.None; // an unfinished closing tag: the rest of the line goes
                            return sb.ToString();
                        }
                        i = gt + 1;
                        state = Html.None;
                        continue;
                }

                if (line[i] != '<')
                {
                    sb.Append(line[i]);
                    i++;
                    continue;
                }
                var rest = line.AsSpan(i + 1);
                if (rest.StartsWith("!--", StringComparison.Ordinal))
                {
                    // With no "-->" later in the document, the opener is a one-line tag: the rest of its line goes, the following lines stay.
                    if (line.IndexOf("-->", i + 4, StringComparison.Ordinal) < 0 && _lastComment <= lineIndex)
                        return sb.ToString();
                    state = Html.Comment;
                    i += 4;
                }
                else if (OpensElement(rest, "script"))
                {
                    if (line.IndexOf("</script", i + 7, StringComparison.OrdinalIgnoreCase) < 0 && _lastScript <= lineIndex)
                        return sb.ToString();
                    state = Html.Script;
                    i += 7;
                }
                else if (OpensElement(rest, "style"))
                {
                    if (line.IndexOf("</style", i + 6, StringComparison.OrdinalIgnoreCase) < 0 && _lastStyle <= lineIndex)
                        return sb.ToString();
                    state = Html.Style;
                    i += 6;
                }
                else if (rest.Length > 0 && (IsAsciiLetter(rest[0]) || rest[0] is '!' or '?' || (rest[0] == '/' && rest.Length > 1 && IsAsciiLetter(rest[1]))))
                {
                    var end = line.IndexOf('>', i + 1);
                    if (IsBlockTag(rest) && sb.Length > 0 && sb[^1] != ' ')
                        sb.Append(' ');
                    i = end < 0 ? line.Length : end + 1;
                }
                else
                {
                    sb.Append('<');
                    i++;
                }
            }
            return sb.ToString();
        }

        private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

        private static bool OpensElement(ReadOnlySpan<char> afterAngle, string name) =>
            afterAngle.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && (afterAngle.Length == name.Length || afterAngle[name.Length] is ' ' or '>' or '/');

        private static bool IsBlockTag(ReadOnlySpan<char> afterAngle)
        {
            var start = afterAngle.Length > 0 && afterAngle[0] == '/' ? 1 : 0;
            var end = start;
            while (end < afterAngle.Length && end - start < 12 && char.IsAsciiLetterOrDigit(afterAngle[end]))
                end++;
            return BlockTags.Contains(afterAngle[start..end].ToString().ToLowerInvariant());
        }
    }
}
