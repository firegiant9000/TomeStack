using TomeStack.ImportWorker.Detection;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// Item 11, D34 (ADR-016): the class-table reader on invented, third-party-shaped layouts. The geometry imitates what PDF
/// extraction gives for a typical book (centred cells, slot cells fused into one line, dashes as tiny lines, headers split
/// over two lines, features wrapped onto a second line); the classes, features and numbers are invented. Exactness against the
/// SRD tables is <c>SrdClassTableTests</c> in the AppService tests.
/// </summary>
public class ClassTableReconstructionTests
{
    private const double Pitch = 15.1;
    private const double LevelCx = 80;
    private const double BonusCx = 122;
    private const double FeaturesX = 152;
    private const double CantripsCx = 306;
    private const double PreparedCx = 352;
    private const double FirstSlotCx = 386.5;
    private const double SlotPitch = 18;

    private enum Shape { Caster, HalfCaster, Pact, Martial }

    private sealed class Page
    {
        public List<TextLine> Lines { get; } = [];

        public static double WidthOf(string text) => text.Sum(c => c switch { '—' => 9.7, ' ' => 2.4, _ => 4.8 });

        public void Add(string text, double x, double y) => Lines.Add(new(text, x, y, WidthOf(text), text == "—" ? 2.7 : 6.6));

        public void Centred(string text, double cx, double y) => Add(text, cx - WidthOf(text) / 2, y);
    }

    /// <summary>One invented class: 20 levels of features, bonus and the numbers its shape needs.</summary>
    private sealed record Truth(string[][] Features, int[] Cantrips, int[] Prepared, int[][] Slots, int[] PactCount, int[] PactLevel, int[] Charges);

    private static Truth Invent(Shape shape)
    {
        var names = new[] { "Fixture Spark", "Fixture Ward", "Fixture Glow", "Fixture Lantern Rite", "Fixture Beacon", "Fixture Veil", "Fixture Dusk Step" };
        var features = Enumerable.Range(1, 20).Select(level => level switch
        {
            1 => new[] { names[0], names[1] },
            4 or 8 or 12 or 16 or 19 => new[] { "Ability Score Improvement" },
            3 => new[] { "Testy Order feature" },
            _ => level % 3 == 0 ? new[] { names[level % names.Length] } : level % 5 == 0 ? new[] { names[level % names.Length], names[(level + 2) % names.Length], "Fixture Capstone Trial of Long Name" } : [],
        }).ToArray();
        var cantrips = Enumerable.Range(1, 20).Select(l => 2 + l / 5).ToArray();
        var prepared = Enumerable.Range(1, 20).Select(l => 3 + l / 2).ToArray();
        var highest = shape == Shape.HalfCaster ? 5 : 9;
        var slots = Enumerable.Range(1, 20).Select(l =>
        {
            var row = new int[highest];
            for (var k = 0; k < highest; k++)
            {
                var unlock = shape == Shape.HalfCaster ? 2 * k + 1 : 2 * k + 1;
                row[k] = l >= unlock ? 1 + Math.Min(3, (l - unlock) / 5) : 0;
            }
            var length = row.Length;
            while (length > 0 && row[length - 1] == 0)
                length--;
            return row[..length];
        }).ToArray();
        var pactCount = Enumerable.Range(1, 20).Select(l => l < 2 ? 1 : l < 11 ? 2 : l < 17 ? 3 : 4).ToArray();
        var pactLevel = Enumerable.Range(1, 20).Select(l => Math.Min(5, 1 + (l - 1) / 2)).ToArray();
        var charges = Enumerable.Range(1, 20).Select(l => l / 2).ToArray();
        return new(features, cantrips, prepared, slots, pactCount, pactLevel, charges);
    }

    private static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };

    /// <summary>The invented class's page: a title, headers over two lines, then 20 rows. Odd rows fuse their slot cells into one line.</summary>
    private static Page Build(Shape shape, Truth truth, bool ordinalLevels = false, bool withCharges = false, bool splitAtEleven = false)
    {
        var page = new Page();
        var top = 400.0;
        var header2 = top + 14.8;
        page.Add("Fixture Lamplighter Features", 63, top + 44);
        page.Add("Proficiency", BonusCx + 12 - 45.6, top + 25.5);
        page.Add("Level Bonus", 68.1, header2);
        page.Add("Class Features", FeaturesX + 0.5, header2);
        var slotColumns = shape switch { Shape.Caster => 9, Shape.HalfCaster => 5, _ => 0 };
        if (shape is Shape.Caster or Shape.HalfCaster or Shape.Pact)
            page.Centred("Cantrips", CantripsCx, header2);
        if (shape is Shape.Caster or Shape.HalfCaster)
        {
            page.Centred("Prepared", PreparedCx, top + 25.5);
            page.Centred("Spells", PreparedCx, header2);
            page.Add("——Spell Slots per Spell Level——", FirstSlotCx - 2.4, top + 29.5);
            for (var k = 0; k < slotColumns; k++)
                page.Centred((k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FirstSlotCx + k * SlotPitch, header2);
        }
        if (shape == Shape.Pact)
        {
            page.Centred("Spell", FirstSlotCx, top + 25.5);
            page.Centred("Slots", FirstSlotCx, header2);
            page.Centred("Slot", FirstSlotCx + 40, top + 25.5);
            page.Centred("Level", FirstSlotCx + 40, header2);
        }
        if (withCharges)
        {
            page.Centred("Lantern", 520, top + 25.5);
            page.Centred("Charges", 520, header2);
        }

        var y = top;
        var columnShift = 0.0;
        for (var level = 1; level <= 20; level++)
        {
            if (splitAtEleven && level == 12)
            {
                columnShift = 271;
                y = top + 340; // the next column starts at the top of the page, with no header
            }
            var x = columnShift;
            var yy = y;
            page.Centred(ordinalLevels ? Ordinal(level) : level.ToString(System.Globalization.CultureInfo.InvariantCulture), LevelCx + x, yy);
            page.Centred($"+{2 + (level - 1) / 4}", BonusCx + x, yy);
            var names = truth.Features[level - 1];
            if (names.Length == 0)
                page.Add("—", FeaturesX + x, yy - 0.7);
            else
            {
                // A long list wraps after the second name, the way a narrow column does.
                var first = string.Join(", ", names.Take(2)) + (names.Length > 2 ? "," : string.Empty);
                page.Add(first, FeaturesX + x, yy);
                if (names.Length > 2)
                {
                    page.Add(string.Join(", ", names.Skip(2)), FeaturesX + x, yy - 12.5);
                    y -= 12.5;
                }
            }
            if (shape is Shape.Caster or Shape.HalfCaster or Shape.Pact)
                page.Centred(truth.Cantrips[level - 1].ToString(System.Globalization.CultureInfo.InvariantCulture), CantripsCx + x, yy);
            if (shape is Shape.Caster or Shape.HalfCaster)
            {
                page.Centred(truth.Prepared[level - 1].ToString(System.Globalization.CultureInfo.InvariantCulture), PreparedCx + x, yy);
                var row = truth.Slots[level - 1];
                var counts = Enumerable.Range(0, slotColumns).Select(k => k < row.Length ? row[k] : 0).ToArray();
                if (level % 2 == 1)
                {
                    // One extracted line for all the slot cells.
                    page.Add(string.Join(' ', counts.Select(c => c == 0 ? "—" : c.ToString(System.Globalization.CultureInfo.InvariantCulture))), FirstSlotCx - 2.4 + x, yy);
                }
                else
                {
                    for (var k = 0; k < slotColumns; k++)
                    {
                        if (counts[k] == 0)
                            page.Centred("—", FirstSlotCx + k * SlotPitch + x, yy - 0.7);
                        else
                            page.Centred(counts[k].ToString(System.Globalization.CultureInfo.InvariantCulture), FirstSlotCx + k * SlotPitch + x, yy);
                    }
                }
            }
            if (shape == Shape.Pact)
            {
                page.Centred(truth.PactCount[level - 1].ToString(System.Globalization.CultureInfo.InvariantCulture), FirstSlotCx + x, yy);
                page.Centred(Ordinal(truth.PactLevel[level - 1]), FirstSlotCx + 40 + x, yy);
            }
            if (withCharges)
                page.Centred(level == 1 ? "—" : $"d{4 + 2 * Math.Min(4, level / 5)}", 520 + x, yy);
            y -= Pitch;
        }
        return page;
    }

    private static ClassTable Read(Page page) =>
        ClassTableReconstruction.Reconstruct(page.Lines, "Fixture Lamplighter Features") ?? throw new InvalidOperationException("no table");

    [Fact]
    public void A_full_caster_table_reads_back_slots_cantrips_prepared_bonus_and_features()
    {
        var truth = Invent(Shape.Caster);
        var table = Read(Build(Shape.Caster, truth));

        Assert.True(table.Complete);
        Assert.False(table.IsPact);
        Assert.Equal("Prepared Spells", table.SpellsLabel);
        Assert.Equal(Enumerable.Range(1, 20).Select(l => 2 + (l - 1) / 4), table.Numbers(ClassColumnRole.ProficiencyBonus));
        Assert.Equal(truth.Cantrips, table.Numbers(ClassColumnRole.Cantrips));
        Assert.Equal(truth.Prepared, table.Numbers(ClassColumnRole.SpellsTable));
        Assert.Equal(truth.Slots, table.Slots()!.Select(r => r.ToArray()));
        for (var level = 1; level <= 20; level++)
            Assert.Equal(truth.Features[level - 1], table.FeaturesByLevel.TryGetValue(level, out var names) ? names : []);
    }

    [Fact]
    public void A_half_caster_with_ordinal_levels_and_five_slot_columns_reads_back()
    {
        var truth = Invent(Shape.HalfCaster);
        var table = Read(Build(Shape.HalfCaster, truth, ordinalLevels: true));

        Assert.True(table.Complete);
        Assert.Equal(truth.Slots, table.Slots()!.Select(r => r.ToArray()));
        Assert.Equal(truth.Cantrips, table.Numbers(ClassColumnRole.Cantrips));
    }

    [Fact]
    public void A_pact_table_reads_the_slot_count_at_its_slot_level_with_ordinal_levels_in_the_cells()
    {
        var truth = Invent(Shape.Pact);
        var table = Read(Build(Shape.Pact, truth));

        Assert.True(table.IsPact);
        var slots = table.Slots()!;
        for (var i = 0; i < 20; i++)
        {
            var expected = new int[truth.PactLevel[i]];
            expected[truth.PactLevel[i] - 1] = truth.PactCount[i];
            Assert.Equal(expected, slots[i].ToArray());
        }
        Assert.Null(table.Numbers(ClassColumnRole.SpellsTable)); // this shape has no spells column
    }

    [Fact]
    public void A_martial_table_with_an_extra_column_keeps_that_column_and_has_no_slots()
    {
        var truth = Invent(Shape.Martial);
        var table = Read(Build(Shape.Martial, truth, withCharges: true));

        Assert.True(table.Complete);
        Assert.False(table.HasSlotColumns);
        Assert.Null(table.Slots());
        var extra = Assert.Single(table.Columns, c => c.Role == ClassColumnRole.Other);
        Assert.Equal("Lantern Charges", extra.Label);
        var index = table.Columns.ToList().IndexOf(extra);
        Assert.Equal("—", table.Rows[0].Cells[index]);
        Assert.Equal("d4", table.Rows[1].Cells[index]);
        Assert.Equal("d12", table.Rows[19].Cells[index]);
    }

    [Fact]
    public void A_table_that_carries_on_in_the_next_column_without_a_header_is_joined()
    {
        var truth = Invent(Shape.Caster);
        var table = Read(Build(Shape.Caster, truth, splitAtEleven: true));

        Assert.True(table.Complete);
        Assert.Equal(truth.Slots, table.Slots()!.Select(r => r.ToArray()));
        Assert.Equal(truth.Prepared, table.Numbers(ClassColumnRole.SpellsTable));
    }

    [Fact]
    public void Prose_in_the_next_column_is_not_read_as_table_cells()
    {
        var truth = Invent(Shape.Martial);
        var page = Build(Shape.Martial, truth);
        // A paragraph set beside the table, at the same heights, in the next column.
        for (var i = 0; i < 24; i++)
            page.Add($"Lantern words wrap here in line {i}", 330, 420 - i * 12.5);
        var table = Read(page);

        Assert.True(table.Complete);
        Assert.DoesNotContain(table.Columns, c => c.Role == ClassColumnRole.Other);
        Assert.Equal(Enumerable.Range(1, 20).Select(l => 2 + (l - 1) / 4), table.Numbers(ClassColumnRole.ProficiencyBonus));
    }

    [Fact]
    public void A_table_cut_short_is_not_complete_and_gives_no_numbers()
    {
        var truth = Invent(Shape.Caster);
        var page = Build(Shape.Caster, truth);
        var cut = new Page();
        cut.Lines.AddRange(page.Lines.Where(l => l.Y > 400 - 11.5 * Pitch));
        var table = Read(cut);

        Assert.False(table.Complete);
        Assert.Null(table.Slots());
        Assert.Null(table.Numbers(ClassColumnRole.Cantrips));
        Assert.True(table.FeaturesByLevel.Count > 0);
    }

    [Fact]
    public void A_column_counting_up_beside_the_levels_is_not_taken_for_the_level_column()
    {
        var page = Build(Shape.Martial, Invent(Shape.Martial));
        // "Points" 2..20 (level 1 a dash): nineteen consecutive values, one fewer than the levels.
        page.Centred("Points", 470, 414.8);
        var y = 400.0;
        var truth = Invent(Shape.Martial);
        for (var level = 1; level <= 20; level++)
        {
            page.Centred(level == 1 ? "—" : level.ToString(System.Globalization.CultureInfo.InvariantCulture), 470, level == 1 ? y - 0.7 : y);
            y -= Pitch + (truth.Features[level - 1].Length > 2 ? 12.5 : 0);
        }
        var table = Read(page);

        Assert.True(table.Complete);
        Assert.Equal(1, table.Rows[0].Level);
        Assert.Equal("Points", table.Columns.Single(c => c.Role == ClassColumnRole.Other).Label);
    }

    [Fact]
    public void A_feature_name_wrapped_with_a_hyphen_is_joined()
    {
        var page = Build(Shape.Martial, Invent(Shape.Martial));
        var level1 = page.Lines.Single(l => l.Text.StartsWith("Fixture Spark", StringComparison.Ordinal));
        page.Lines.Remove(level1);
        page.Lines.Add(level1 with { Text = "Fixture Spark, Fixture Un-" });
        page.Lines.Add(new("relenting", FeaturesX, level1.Y - 12.5, 40, 6.6));
        // The rest of the table moves down by one wrapped line, as it would on the page.
        for (var i = 0; i < page.Lines.Count; i++)
        {
            if (page.Lines[i].Y < level1.Y - 1 && page.Lines[i].Text != "relenting" && page.Lines[i].Y < level1.Y - 3)
                page.Lines[i] = page.Lines[i] with { Y = page.Lines[i].Y - 12.5 };
        }
        var table = Read(page);

        Assert.Contains("Fixture Unrelenting", table.FeaturesByLevel[1]);
    }

    [Fact]
    public async Task The_fixture_books_Lamplighter_page_reads_back_from_the_real_extraction()
    {
        var pages = new List<ExtractedPage>();
        await foreach (var item in new Extraction.PdfPigExtractor().ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
        {
            if (item is ExtractedPage page)
                pages.Add(page);
        }
        var lines = pages.Single(p => p.PageNumber == 7).Blocks!.SelectMany(b => b.Lines is { Count: > 0 } l ? l : [new TextLine(b.Text, b.X, b.Y, b.Width, b.Height)]).ToList();

        var table = ClassTableReconstruction.Reconstruct(lines, "Fixture Lamplighter Features");

        Assert.NotNull(table);
        Assert.True(table.Complete);
        Assert.Equal(Enumerable.Range(1, 20).Select(FixturePdfs.Lamplighter.ProficiencyBonus), table.Numbers(ClassColumnRole.ProficiencyBonus));
        Assert.Equal(FixturePdfs.Lamplighter.Cantrips, table.Numbers(ClassColumnRole.Cantrips));
        Assert.Equal(FixturePdfs.Lamplighter.Slots.Select(row => row.Reverse().SkipWhile(c => c == 0).Reverse().ToArray()), table.Slots()!.Select(r => r.ToArray()));
        for (var level = 1; level <= 20; level++)
        {
            var expected = FixturePdfs.Lamplighter.Features[level - 1] == "-" ? [] : ClassTableReconstruction.SplitFeatures(FixturePdfs.Lamplighter.Features[level - 1]);
            Assert.Equal(expected, table.FeaturesByLevel.TryGetValue(level, out var names) ? names : []);
        }
    }

    [Fact]
    public void A_page_without_a_table_gives_null_and_garbage_never_throws()
    {
        Assert.Null(ClassTableReconstruction.Reconstruct([], "Fixture Lamplighter Features"));
        Assert.Null(ClassTableReconstruction.Reconstruct([new("Just a paragraph of text.", 63, 700, 200, 7)]));

        var random = new Random(20261016);
        var words = new[] { "1", "2", "10", "20", "1st", "20th", "+2", "—", "Features", "Class Features", "Level", "Level Bonus", "Cantrips", "Spell Slots", "2 — — 3", "Fixture", "(", ")", ",", "̶" };
        for (var round = 0; round < 200; round++)
        {
            var lines = Enumerable.Range(0, random.Next(5, 120)).Select(_ =>
            {
                var text = words[random.Next(words.Length)];
                return new TextLine(text, random.NextDouble() * 500, random.NextDouble() * 700, Page.WidthOf(text), 2 + random.NextDouble() * 8);
            }).ToList();
            _ = ClassTableReconstruction.Reconstruct(lines, random.Next(2) == 0 ? "Fixture Lamplighter Features" : null);
        }
    }
}
