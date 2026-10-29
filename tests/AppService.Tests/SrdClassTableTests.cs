using System.Security.Cryptography;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Detection;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;
using Xunit.Abstractions;
using TextLine = TomeStack.ImportWorker.TextLine;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Item 11, D34 (ADR-016): the class-table reconstruction read back from both SRD PDFs and compared cell by cell with the
/// bundled packs (the Spellcasting and Pact Magic effects, the proficiency bonus and the features granted at each level).
/// The PDFs are not in the repository: the test runs only when <c>TOMESTACK_SRD_PDF_DIR</c> names a folder holding them
/// with the SHA-256 hashes of <c>docs/licensing/srd-pack-review.md</c>, and skips otherwise. The output is counts only.
/// </summary>
public class SrdClassTableTests(ITestOutputHelper output)
{
    private static readonly (string Family, string File, string Sha256, string Version)[] Pdfs =
    [
        (RulesFamilies.Srd51, "SRD_CC_v5.1.pdf", "2504d2a0abb0a4d491a939be4f17910a2dde0312570ab8d208080225ccf0a1f0", "5.1"),
        (RulesFamilies.Srd521, "SRD_CC_v5.2.1.pdf", "8974902d109d6e63672d7c490bde9ccf052410503d9cfa768237154fbc5e3d87", "5.2.1"),
    ];

    private static string? Folder => Environment.GetEnvironmentVariable("TOMESTACK_SRD_PDF_DIR");

    /// <summary>The class packs of a family, in bundling order (the Barbarian's later revisions come last, then the Rogue).</summary>
    private static List<ContentPack> BundledClassPacks(string version) =>
        [.. new[] { $"srd-{version}-classes.json", $"srd-{version}-fighter.json", $"srd-{version}-barbarian.json", $"srd-{version}-rogue.json" }
            .Select(f => TomeStackApp.LoadBundledPack($"TomeStack.Content.{f}"))];

    public sealed class SrdPdfFactAttribute : FactAttribute
    {
        public SrdPdfFactAttribute()
        {
            if (Folder is not { Length: > 0 } folder || !Pdfs.All(p => File.Exists(Path.Combine(folder, p.File))))
                Skip = "Set TOMESTACK_SRD_PDF_DIR to a folder with SRD_CC_v5.1.pdf and SRD_CC_v5.2.1.pdf (not in the repository).";
        }
    }

    private static List<TextLine> LinesOf(DetectionPage page) =>
        [.. page.Blocks.SelectMany(b => b.Lines is { Count: > 0 } lines ? lines : [new TextLine(b.Text, b.X, b.Y, b.Width, b.Height)])];

    [SrdPdfFact]
    public async Task The_reconstructed_class_tables_match_the_bundled_SRD_packs_cell_for_cell()
    {
        var failures = new List<string>();
        foreach (var (family, file, sha, version) in Pdfs)
        {
            var path = Path.Combine(Folder!, file);
            await using (var stream = File.OpenRead(path))
                Assert.Equal(sha, Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)));
            var pages = new List<DetectionPage>();
            await foreach (var item in new PdfPigExtractor().ExtractAsync(path, PageScope.WholeDocument, CancellationToken.None))
            {
                if (item is ExtractedPage page)
                    pages.Add(new(page.PageNumber, page.Text, page.Blocks ?? [], page.FromOcr));
            }
            var classPages = CandidateDetector.Detect(pages, new(Guid.NewGuid(), [family], _ => false))
                .Where(c => c.ProposedKind == ContentKind.Class).Select(c => (c.ProposedName, c.Page.Start)).ToList();

            var packs = BundledClassPacks(version);
            var newest = packs.SelectMany(p => p.Revisions).GroupBy(r => r.ContentId).Select(g => g.Last()).ToList();
            var byRevision = packs.SelectMany(p => p.Revisions).GroupBy(r => r.RevisionId).ToDictionary(g => g.Key, g => g.First());

            var unreadable = new List<string>();
            int tables = 0, complete = 0, casters = 0, cellsCompared = 0, cellsRight = 0, bonusRight = 0, featureLevels = 0, featureLevelsRight = 0;
            foreach (var (name, pageNumber) in classPages)
            {
                var lines = LinesOf(pages.First(p => p.Page == pageNumber));
                var title = lines.Select(l => l.Text.Trim()).FirstOrDefault(t => t == $"{name} Features" || t == $"The {name}");
                var table = ClassTableReconstruction.Reconstruct(lines, title);
                if (table is null)
                {
                    unreadable.Add(name);
                    continue;
                }
                tables++;
                if (!table.Complete)
                {
                    failures.Add($"{version} table {tables} incomplete ({table.Rows.Count} rows)");
                    continue;
                }
                complete++;
                var bonus = table.Numbers(ClassColumnRole.ProficiencyBonus);
                if (bonus is not null && Enumerable.Range(1, 20).All(l => bonus[l - 1] == 2 + (l - 1) / 4))
                    bonusRight++;
                else
                    failures.Add($"{version} proficiency bonus off in table {tables}");

                var klass = newest.FirstOrDefault(r => r.Kind == ContentKind.Class && r.Name == name);
                if (klass is not null)
                {
                    foreach (var grant in klass.Effects.OfType<GrantEffect>().Where(g => g is { Grant: GrantKind.Content, Level: > 0 }))
                    {
                        if (grant.Content is not { } target || !byRevision.TryGetValue(target.RevisionId, out var granted) || granted.Name.Contains(':', StringComparison.Ordinal))
                            continue;
                        featureLevels++;
                        if (table.FeaturesByLevel.TryGetValue(grant.Level!.Value, out var names) && names.Any(n => SameFeature(n, granted.Name)))
                            featureLevelsRight++;
                    }
                }

                var caster = newest.FirstOrDefault(r => r.Effects.OfType<SpellcastingEffect>().Any() && r.Effects.OfType<SpellcastingEffect>().First().SpellList == name.ToLowerInvariant());
                if (caster is null)
                {
                    if (table.HasSlotColumns)
                        failures.Add($"{version} slot columns on a class the pack does not cast with (table {tables})");
                    continue;
                }
                casters++;
                var effect = caster.Effects.OfType<SpellcastingEffect>().First();
                var slots = table.Slots();
                for (var level = 0; level < 20; level++)
                {
                    cellsCompared++;
                    if (slots is not null && slots[level].SequenceEqual(Trim(effect.Slots[level])))
                        cellsRight++;
                }
                foreach (var (read, truth) in new[] { (table.Numbers(ClassColumnRole.Cantrips), effect.Cantrips), (table.Numbers(ClassColumnRole.SpellsTable), effect.SpellsTable) })
                {
                    if (truth is null)
                    {
                        if (read is not null)
                            failures.Add($"{version} a column the pack lacks was read in table {tables}");
                        continue;
                    }
                    for (var level = 0; level < 20; level++)
                    {
                        cellsCompared++;
                        if (read is not null && read[level] == truth[level])
                            cellsRight++;
                    }
                }
                if (table.IsPact != (effect.SlotKind == SpellSlotKind.PactMagic))
                    failures.Add($"{version} pact kind wrong in table {tables}");
            }
            output.WriteLine($"{version}: class pages {classPages.Count}, tables read {tables}, complete {complete}, unreadable {unreadable.Count}, proficiency bonus right {bonusRight}/{complete}, "
                + $"casters {casters}, spellcasting cells right {cellsRight}/{cellsCompared}, granted features found at their level {featureLevelsRight}/{featureLevels}");
            // The one table that is not read: the SRD 5.1 Sorcerer's extraction interleaves its columns ("Cantrips Spells",
            // "Known Known 1st 2nd ..." are single lines), so it stays a features-only candidate rather than a wrong table.
            if (!unreadable.SequenceEqual(version == "5.1" ? new[] { "Sorcerer" } : Array.Empty<string>()))
                failures.Add($"{version} unreadable tables {unreadable.Count}");
            if (cellsRight != cellsCompared)
                failures.Add($"{version} spellcasting cells {cellsRight}/{cellsCompared}");
            if (bonusRight != complete || complete != tables)
                failures.Add($"{version} tables {tables}, complete {complete}, bonus {bonusRight}");
            if (featureLevelsRight != featureLevels)
                failures.Add($"{version} features {featureLevelsRight}/{featureLevels}");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures)); // counts only, never names
    }

    /// <summary>
    /// Fix round 1, item 6: what the proposals read from each SRD class page, counts only (which of the six parts were proposed
    /// for how many classes, and for the classes that miss one, how many). The SRD classes are the layouts the rules were written
    /// against, so these are floors, not a claim about other books.
    /// </summary>
    [SrdPdfFact]
    public async Task The_class_proposals_are_measured_on_both_SRDs()
    {
        var failures = new List<string>();
        foreach (var (family, file, _, version) in Pdfs)
        {
            var pages = new List<DetectionPage>();
            await foreach (var item in new PdfPigExtractor().ExtractAsync(Path.Combine(Folder!, file), PageScope.WholeDocument, CancellationToken.None))
            {
                if (item is ExtractedPage page)
                    pages.Add(new(page.PageNumber, page.Text, page.Blocks ?? [], page.FromOcr));
            }
            var classes = CandidateDetector.Detect(pages, new(Guid.NewGuid(), [family], _ => false)).Where(c => c.ProposedKind == ContentKind.Class).OrderBy(c => c.Page.Start).ToList();
            var packs = BundledClassPacks(version);
            var packCasting = packs.SelectMany(p => p.Revisions).GroupBy(r => r.ContentId).Select(g => g.Last())
                .SelectMany(r => r.Effects.OfType<SpellcastingEffect>()).GroupBy(e => e.SpellList).ToDictionary(g => g.Key, g => g.First().Ability);
            int hit = 0, saves = 0, armor = 0, armorNone = 0, weapons = 0, weaponsQualified = 0, skills = 0, casting = 0, slotTables = 0, unexplained = 0, abilityRight = 0;
            foreach (var c in classes)
            {
                bool Has(string prefix) => c.ProposedEffects.Any(e => e.Id.StartsWith(prefix, StringComparison.Ordinal));
                bool Noted(string text) => c.Uncertainties.Any(u => u.Contains(text, StringComparison.OrdinalIgnoreCase));
                var row = new System.Text.StringBuilder();
                row.Append(Has("hit-die") ? 'H' : '-');
                if (Has("hit-die")) hit++;
                row.Append(Has("save-") ? 'S' : Noted("saving throw") ? 's' : '?');
                if (Has("save-")) saves++; else if (!Noted("saving throw")) unexplained++;
                // No armor proposed and no note: the page read "None".
                row.Append(Has("armor-") ? 'A' : Noted("armor proficiencies") ? 'a' : '0');
                if (Has("armor-")) armor++; else if (!Noted("armor proficiencies")) armorNone++;
                row.Append(Has("weapon-") ? 'W' : Noted("condition") ? 'q' : Noted("weapon") ? 'w' : '?');
                if (Has("weapon-")) weapons++; else if (Noted("condition")) weaponsQualified++; else if (!Noted("weapon")) unexplained++;
                row.Append(c.Fields.ContainsKey("skills") ? 'K' : '-');
                if (c.Fields.ContainsKey("skills")) skills++;
                var slotTable = Noted("spell slot table");
                row.Append(Has("spellcasting") ? 'C' : slotTable ? 'c' : '-');
                if (c.ProposedEffects.OfType<SpellcastingEffect>().FirstOrDefault() is { } proposed)
                {
                    casting++;
                    if (packCasting.TryGetValue(c.ProposedName.ToLowerInvariant(), out var truth) && truth == proposed.Ability)
                        abilityRight++;
                }
                if (Has("spellcasting") || slotTable) slotTables++;
                // Diagnostics for SRD pages only (CC-BY): the class name and the effect ids read, no page text. The weekly job keeps
                // this output in its test-results artifact for 7 days.
                output.WriteLine($"  {version} {c.ProposedName}: {row} [{string.Join(' ', c.ProposedEffects.Select(e => e.Id).Where(i => i.StartsWith("weapon-", StringComparison.Ordinal) || i.StartsWith("armor-", StringComparison.Ordinal)))}]");
            }
            output.WriteLine($"{version}: classes {classes.Count}; proposed hit die {hit}, saves {saves}, armor {armor} (+{armorNone} read as none), weapons {weapons} (+{weaponsQualified} qualified, not proposed), "
                + $"skills shown {skills}, spellcasting {casting} of {slotTables} with a slot table (ability as the pack {abilityRight}/{casting}); a missing saves/weapons part with no uncertainty: {unexplained}");
            if (hit != classes.Count) failures.Add($"{version} hit die {hit}/{classes.Count}");
            if (abilityRight != casting) failures.Add($"{version} spellcasting ability {abilityRight}/{casting}");
            if (unexplained != 0) failures.Add($"{version} {unexplained} missing parts with no uncertainty");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    /// <summary>
    /// Wave 0/1 minor (R81): the "add spellcasting by hand" note is for a class whose table has no spell slot columns. On both SRDs
    /// no class is in that state: every caster's table is read with its slot columns, and a non-caster never mentions spellcasting
    /// (its following text ends where the next class begins). The assertion is on that slotless note only: the older notes that
    /// also end "add spellcasting by hand" (a slot table read without its ability, or not read whole, or a class name that is not a spell list key) are about a caster's own
    /// table, so they are only held at today's count (one caster per SRD). Counts only, never names.
    /// </summary>
    [SrdPdfFact]
    public async Task No_SRD_class_gets_the_add_spellcasting_by_hand_note()
    {
        const string Note = "its table has no spell slot columns";
        const string AnyByHand = "add spellcasting by hand";
        var failures = new List<string>();
        foreach (var (family, file, _, version) in Pdfs)
        {
            var pages = new List<DetectionPage>();
            await foreach (var item in new PdfPigExtractor().ExtractAsync(Path.Combine(Folder!, file), PageScope.WholeDocument, CancellationToken.None))
            {
                if (item is ExtractedPage page)
                    pages.Add(new(page.PageNumber, page.Text, page.Blocks ?? [], page.FromOcr));
            }
            var classes = CandidateDetector.Detect(pages, new(Guid.NewGuid(), [family], _ => false)).Where(c => c.ProposedKind == ContentKind.Class).ToList();
            var noted = classes.Count(c => c.Uncertainties.Any(u => u.Contains(Note, StringComparison.OrdinalIgnoreCase)));
            var otherNotes = classes.Count(c => c.Uncertainties.Any(u => u.Contains(AnyByHand, StringComparison.OrdinalIgnoreCase) && !u.Contains(Note, StringComparison.OrdinalIgnoreCase)));
            output.WriteLine($"{version}: classes {classes.Count}, with the slotless note {noted}, with another add-spellcasting note {otherNotes}");
            Assert.NotEmpty(classes);
            if (noted != 0) failures.Add($"{version} {noted} of {classes.Count} classes carry the slotless note");
            // Today's baseline for the older notes is one caster per SRD; lower it when those are read.
            if (otherNotes > 1) failures.Add($"{version} {otherNotes} of {classes.Count} classes carry another add-spellcasting note");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    /// <summary>The table names a feature with a qualifier ("Rage (2 dice)") or in the plural where the pack does not.</summary>
    private static bool SameFeature(string tableName, string packName)
    {
        static string Stem(string name) => CandidateDetector.Key(System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^)]*\)", string.Empty));
        var (a, b) = (Stem(tableName), Stem(packName));
        return a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal);
    }

    private static IReadOnlyList<int> Trim(IReadOnlyList<int> row)
    {
        var length = row.Count;
        while (length > 0 && row[length - 1] == 0)
            length--;
        return [.. row.Take(length)];
    }
}
