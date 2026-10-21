using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// Original test PDFs (SPEC Q-03: fixture text only, every name starts with "Fixture"). <see cref="Import"/> is the M4
/// fixture book, committed as <c>tests/RulesFixtures/pdf/fixture-import.pdf</c>: SRD-shaped blocks (spells in both SRDs'
/// styles, a feat, a feature naming a spell that is not installed, weapon and armor rows and a class table), a two-column
/// page and a page without a text layer. The malformed ones are built at test time.
/// </summary>
internal static class FixturePdfs
{
    public const int ImportPageCount = 7;

    /// <summary>The committed fixture's path in the test output.</summary>
    public static string ImportPath => Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf");

    public static byte[] Import()
    {
        var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        void Lines(PdfPageBuilder page, double x, double top, IEnumerable<(string Text, bool Bold, double Size)> lines)
        {
            var y = top;
            foreach (var (text, isBold, size) in lines)
            {
                if (text.Length > 0)
                    page.AddText(text, size, new PdfPoint(x, y), isBold ? bold : regular);
                y -= size + (text.Length == 0 ? 6 : 4);
            }
        }
        (string, bool, double) H(string text) => (text, true, 14);
        (string, bool, double) B(string text) => (text, false, 10);
        (string, bool, double) Gap() => ("", false, 10);

        // Page 1: a title and an introduction.
        Lines(builder.AddPage(PageSize.Letter), 72, 700,
        [
            ("Fixture Grimoire of Testing", true, 20), Gap(),
            B("This book is an original TomeStack test fixture. Every rule in it is invented for testing."),
            B("Nothing here comes from a published game book."),
        ]);

        // Page 2: two spells in two columns, one in the SRD 5.2.1 style and one in the SRD 5.1 style.
        var spells = builder.AddPage(PageSize.Letter);
        Lines(spells, 50, 720,
        [
            H("Fixture Ember Lance"),
            B("Level 2 Evocation (Fixture Mage)"),
            B("Casting Time: Action"),
            B("Range: 60 feet"),
            B("Components: V, S"),
            B("Duration: Instantaneous"), Gap(),
            B("A lance of fixture flame streaks toward a"),
            B("creature within range. Make a ranged spell"),
            B("attack. On a hit, the target takes 3d6 Fire"),
            B("damage."),
        ]);
        Lines(spells, 320, 720,
        [
            H("Fixture Frost Veil"),
            B("1st-level abjuration"),
            B("Casting Time: 1 reaction"),
            B("Range: Self"),
            B("Components: V, S, M (a fixture snowflake)"),
            B("Duration: 1 round"), Gap(),
            B("A veil of fixture frost surrounds you. Until"),
            B("the start of your next turn, you have a +2"),
            B("bonus to AC. Each creature that hits you"),
            B("must make a Constitution saving throw."),
        ]);

        // Page 3: a feat and a class feature; the feature names a spell that is not installed.
        Lines(builder.AddPage(PageSize.Letter), 72, 720,
        [
            H("Fixture Keen Watcher"),
            B("Origin Feat"), Gap(),
            B("You gain the following benefits."),
            B("Alert Eyes. You gain a +2 bonus to initiative."),
            B("Watchful Mind. You have proficiency in the Perception skill."), Gap(), Gap(),
            H("Fixture Stormcall"),
            B("Level 3 Fixture Warden Feature"), Gap(),
            B("You can cast Fixture Thunder Word once without expending a spell slot."),
            B("You regain the ability to do so when you finish a Long Rest."),
        ]);

        // Page 4: a weapon table and an armor table.
        Lines(builder.AddPage(PageSize.Letter), 72, 720,
        [
            H("Fixture Weapons"),
            B("Name  Damage  Properties  Weight  Cost"),
            B("Martial Melee Weapons"),
            B("Fixture Hookblade  1d8 slashing  Finesse, Light  2 lb.  12 GP"),
            B("Fixture Longstaff  1d10 bludgeoning  Heavy, Reach, Two-Handed  6 lb.  8 GP"), Gap(), Gap(),
            H("Fixture Armor"),
            B("Armor  Armor Class (AC)  Strength  Stealth  Weight  Cost"),
            B("Light Armor"),
            B("Fixture Quilted Coat  11 + Dex modifier  -  -  8 lb.  6 GP"),
            B("Heavy Armor"),
            B("Fixture Plate Shell  17  Str 15  Disadvantage  60 lb.  900 GP"),
        ]);

        // Page 5: a class feature table.
        Lines(builder.AddPage(PageSize.Letter), 72, 720,
        [
            H("Fixture Warden Features"),
            B("Level  Proficiency Bonus  Class Features"),
            B("1  +2  Fixture Focus"),
            B("2  +2  Fixture Resolve"),
            B("3  +2  Fixture Stormcall"),
        ]);

        // Page 6: no text layer (only a drawn rectangle), so extraction reports it and OCR may read it.
        builder.AddPage(PageSize.Letter).DrawRectangle(new PdfPoint(72, 500), 200, 100);

        // Page 7: a spellcasting class (item 11, D34): its core traits as lines, a level table with the proficiency bonus,
        // features, cantrips and nine slot columns, and one of its features as a block of its own.
        var lamplighter = builder.AddPage(PageSize.Letter);
        Lines(lamplighter, 60, 740,
        [
            ("Fixture Lamplighter", true, 20),
            B("Hit Dice: 1d6 per lamplighter level"),
            B("Saving Throws: Wisdom, Charisma"),
            B("Armor: Light armor"),
            B("Weapons: Simple weapons"),
            B("Skills: Choose two from Insight, Investigation, Perception, and Persuasion"),
            B("Wisdom is your spellcasting ability."), Gap(),
            H("Fixture Lamplighter Features"),
        ]);
        LamplighterTable(lamplighter, regular, bold);
        Lines(lamplighter, 60, 320,
        [
            H("Fixture Glow"),
            B("Level 2 Fixture Lamplighter Feature"), Gap(),
            B("You can shed dim light in a 10-foot radius as a bonus action."),
        ]);

        return builder.Build();
    }

    /// <summary>The Fixture Lamplighter's invented level table: for the tests, which read it back.</summary>
    internal static class Lamplighter
    {
        /// <summary>The table's reference-only feature (R181): the detector's exclusion is what these rows test.</summary>
        private const string Ref = TomeStack.ImportWorker.Detection.CandidateDetector.ReferenceOnlyFeatureName;

        public static readonly string[] Features =
        [
            "Spellcasting, Fixture Lantern Rite", "Fixture Glow", "Fixture Order feature", Ref, "Fixture Beacon",
            "Fixture Order feature", "Fixture Glow", Ref, "Fixture Sconce", "Fixture Order feature",
            "Fixture Beacon", Ref, "-", "Fixture Order feature", "Fixture Dusk Step",
            Ref, "-", "Fixture Order feature", Ref, "Fixture Final Light",
        ];

        public static int[] Cantrips { get; } = [.. Enumerable.Range(1, 20).Select(l => 2 + l / 6)];

        /// <summary>Slots by spell level 1-9 for class level 1-20: each spell level opens at class level 2n-1, then gains one every five levels up to four.</summary>
        public static int[][] Slots { get; } = [.. Enumerable.Range(1, 20).Select(l => Enumerable.Range(1, 9).Select(n => l >= 2 * n - 1 ? 1 + Math.Min(3, (l - (2 * n - 1)) / 5) : 0).ToArray())];

        public static int ProficiencyBonus(int level) => 2 + (level - 1) / 4;
    }

    private static void LamplighterTable(PdfPageBuilder page, PdfDocumentBuilder.AddedFont regular, PdfDocumentBuilder.AddedFont bold)
    {
        const double size = 8;
        const double pitch = 11;
        var top = 560.0;
        void Cell(string text, double x, double y, bool isBold = false) => page.AddText(text, size, new PdfPoint(x, y), isBold ? bold : regular);
        // Headers over two lines, as the SRDs set them.
        Cell("Proficiency", 94, top + 2 * pitch, true);
        Cell("Level", 60, top + pitch, true);
        Cell("Bonus", 94, top + pitch, true);
        Cell("Features", 140, top + pitch, true);
        Cell("Cantrips", 292, top + 2 * pitch, true);
        Cell("Known", 295, top + pitch, true);
        string[] ordinals = ["1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th"];
        for (var n = 0; n < 9; n++)
            Cell(ordinals[n], 340 + n * 28, top + pitch, true);
        for (var level = 1; level <= 20; level++)
        {
            var y = top - level * pitch + pitch;
            Cell(level.ToString(System.Globalization.CultureInfo.InvariantCulture), level < 10 ? 64 : 61, y);
            Cell($"+{Lamplighter.ProficiencyBonus(level)}", 98, y);
            Cell(Lamplighter.Features[level - 1], 140, y);
            Cell(Lamplighter.Cantrips[level - 1].ToString(System.Globalization.CultureInfo.InvariantCulture), 303, y);
            for (var n = 0; n < 9; n++)
            {
                var count = Lamplighter.Slots[level - 1][n];
                Cell(count == 0 ? "-" : count.ToString(System.Globalization.CultureInfo.InvariantCulture), 345 + n * 28, y);
            }
        }
    }

    /// <summary>The committed text-only character sheet's path in the test output (item 19b, D37).</summary>
    public static string TextSheetPath => Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-text-sheet.pdf");

    /// <summary>
    /// An invented character sheet printed as plain text: no form fields at all (item 19b, ADR-013). Two pages of 12-point lines;
    /// a blank entry is a gap, as in a note a player prints. The names match the DevHost's original fixture content, so the e2e
    /// flow resolves its class, species and spells.
    /// </summary>
    public static IReadOnlyList<string[]> TextSheetPages { get; } =
    [
        [
            "Name: Testy Printout",
            "Class: Fixture Arcanist 3",
            "Race: Fixture Quickfoot",
            "Background: Fixture Archivist",
            "HP: 28",
            "AC: 16",
            "Speed: 30 ft.",
            "",
            "Strength 16, Dexterity 14, Constitution 15, Intelligence 10, Wisdom 12, Charisma 8",
            "",
            "Saves: Strength",
            "Skills: Athletics",
            "",
            "Spells",
            "Fixture Frost Ring",
            "",
            "Equipment",
            "Fixture Rope Coil",
            "Fixture Lantern",
            "12gp",
        ],
        [
            "Features",
            "Fixture Steady Breath",
            "A line of invented description.",
            "",
            "Fixture Bold Surge",
            "Another invented line.",
        ],
    ];

    /// <summary>The text-only sheet (<see cref="TextSheetPages"/>): letters on the page, no form, no annotations.</summary>
    public static byte[] TextSheet() => TextPages(TextSheetPages);

    /// <summary>A PDF with one page per entry, each line drawn at 12 points from the top (a blank line is a gap).</summary>
    public static byte[] TextPages(IReadOnlyList<string[]> pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.Letter);
            var y = 720.0;
            foreach (var line in lines)
            {
                if (line.Length > 0)
                    page.AddText(line, 12, new PdfPoint(60, y), font);
                y -= line.Length == 0 ? 14 : 18;
            }
        }
        return builder.Build();
    }

    /// <summary>A text PDF of <paramref name="pages"/> pages (for the page limit).</summary>
    public static byte[] Pages(int pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 1; i <= pages; i++)
            builder.AddPage(PageSize.A4).AddText($"Fixture page {i}", 10, new PdfPoint(50, 700), font);
        return builder.Build();
    }

    /// <summary>A hand-built PDF from object bodies (object 1 first), with a correct cross-reference table.</summary>
    public static byte[] Raw(IReadOnlyList<byte[]> objects, string trailerExtra = "")
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write($"{i + 1} 0 obj\n");
            output.Write(objects[i]);
            Write("\nendobj\n");
        }
        var xref = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R {trailerExtra}>>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    /// <summary>A one-page PDF whose content stream is <paramref name="streamBody"/> with <paramref name="filter"/>.</summary>
    public static byte[] OnePage(byte[] streamBody, string filter = "", string pagesExtra = "/Count 1")
    {
        var stream = new List<byte>();
        stream.AddRange(Ascii($"<< /Length {streamBody.Length} {filter}>>\nstream\n"));
        stream.AddRange(streamBody);
        stream.AddRange(Ascii("\nendstream"));
        return Raw(
        [
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii($"<< /Type /Pages /Kids [3 0 R] {pagesExtra} >>"),
            Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << >> >>"),
            [.. stream],
        ]);
    }

    /// <summary>Encrypted with the standard handler and a user password (not the empty one), so it cannot be opened.</summary>
    public static byte[] Encrypted()
    {
        var o = new string('A', 64);
        var u = new string('B', 64);
        return Raw(
        [
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>"),
            Ascii($"<< /Filter /Standard /V 1 /R 2 /Length 40 /P -4 /O <{o}> /U <{u}> >>"),
        ], "/Encrypt 4 0 R /ID [<0123456789ABCDEF0123456789ABCDEF> <0123456789ABCDEF0123456789ABCDEF>] ");
    }

    /// <summary>A small Flate stream that inflates to <paramref name="inflatedBytes"/> bytes of spaces: a decompression bomb.</summary>
    public static byte[] Bomb(int inflatedBytes)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var chunk = new byte[1 << 20];
            Array.Fill(chunk, (byte)' ');
            for (var written = 0; written < inflatedBytes; written += chunk.Length)
                zlib.Write(chunk, 0, Math.Min(chunk.Length, inflatedBytes - written));
        }
        return OnePage(compressed.ToArray(), "/Filter /FlateDecode ");
    }

    /// <summary>A file in a fresh temp folder, deleted by the returned handle.</summary>
    public static TempFile Write(byte[] bytes, string name = "fixture.pdf") => new(bytes, name);

    internal sealed class TempFile : IDisposable
    {
        private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tomestack-import-tests", Guid.NewGuid().ToString("N"));

        public TempFile(byte[] bytes, string name)
        {
            Directory.CreateDirectory(_folder);
            Path = System.IO.Path.Combine(_folder, name);
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException)
            {
                // A child process may still hold the file for a moment; the temp folder is cleaned up later.
            }
        }
    }
}
