using System.Reflection;
using System.Text.Json;
using TomeStack.ImportWorker.Detection;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// D64 (Import Lab L4, approval A1): every proposed effect the detector makes says where on the page it was read (a block range,
/// the pattern that read it, the pass's own confidence), and nothing else about detection changes.
/// </summary>
public class DetectionSpanTests
{
    private static readonly Guid Source = Guid.Parse("5fad5000-0000-4000-8000-000000000001");

    private static async Task<(List<DetectionPage> Pages, IReadOnlyList<DraftCandidate> Found)> Run()
    {
        var pages = new List<DetectionPage>();
        await foreach (var item in new PdfPigExtractor().ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
        {
            if (item is ExtractedPage page)
                pages.Add(new(page.PageNumber, page.Text, page.Blocks ?? [], page.FromOcr));
        }
        return (pages, CandidateDetector.Detect(pages, new(Source, [RulesFamilies.Srd51, RulesFamilies.Srd521], _ => false)));
    }

    /// <summary>The text of the span's blocks on its page, whitespace collapsed.</summary>
    private static string BlockText(List<DetectionPage> pages, CandidateSpan span)
    {
        var blocks = pages.Single(p => p.Page == span.Page).Blocks;
        var text = string.Join(' ', Enumerable.Range(span.BlockStart, span.BlockEnd - span.BlockStart + 1).Select(b => blocks[b].Text));
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static CandidateSpan EffectSpan(DraftCandidate candidate, string effectId) =>
        Assert.Contains(effectId, Assert.IsAssignableFrom<IReadOnlyDictionary<string, CandidateSpan>>(candidate.EffectSpans));

    [Fact]
    public async Task Every_proposed_effect_has_a_span_inside_the_candidate_pages()
    {
        var (pages, found) = await Run();

        Assert.Contains(found, c => c.ProposedKind == ContentKind.Spell);
        Assert.Contains(found, c => c.ProposedKind == ContentKind.Feat);
        Assert.Contains(found, c => c.ProposedKind == ContentKind.Class && c.ProposedEffects.Count > 0);
        Assert.Contains(found, c => c.ProposedEffects.OfType<WeaponEffect>().Any());
        Assert.Contains(found, c => c.ProposedEffects.OfType<ArmorEffect>().Any());
        foreach (var candidate in found)
        {
            var spans = candidate.ProposedEffects.Select(e => (e.Id, Span: EffectSpan(candidate, e.Id))).ToList();
            foreach (var (_, span) in spans.Concat(candidate.NameSpan is { } name ? [("name", name)] : []))
            {
                Assert.InRange(span.Page, candidate.Page.Start, candidate.Page.End ?? candidate.Page.Start);
                var blocks = pages.Single(p => p.Page == span.Page).Blocks;
                Assert.True(span.BlockStart >= 0 && span.BlockStart <= span.BlockEnd && span.BlockEnd < blocks.Count, $"{candidate.ProposedName}: blocks {span.BlockStart}-{span.BlockEnd} of {blocks.Count}");
                Assert.Null(span.PageEnd); // a span stays on its first page
                Assert.Null(span.LineStart); // a PDF has blocks, not source lines
                Assert.Null(span.LineEnd);
                Assert.Equal(candidate.Confidence, span.Confidence);
            }
            Assert.Equal(candidate.ProposedEffects.Count == 0 ? 0 : candidate.ProposedEffects.Select(e => e.Id).Distinct().Count(), candidate.EffectSpans?.Count ?? 0);
        }
        // A candidate read from the page names where; only a reference stub (the class table named it) has nothing to point at.
        Assert.All(found.Where(c => c.Confidence > 0.3), c => Assert.NotNull(c.NameSpan));
    }

    [Fact]
    public async Task Pattern_ids_are_declared()
    {
        var (_, found) = await Run();
        var declared = typeof(DetectionPatterns).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);
        Assert.All(declared, id => Assert.Matches(@"^[a-z]+(\.[a-z]+(-[a-z]+)*)+$", id)); // ids, not prose
        var used = found.SelectMany(c => (c.EffectSpans?.Values ?? []).Concat(c.NameSpan is { } n ? [n] : [])).Select(s => s.PatternId).ToList();
        Assert.NotEmpty(used);
        Assert.All(used, id => Assert.Contains(id, declared));
    }

    [Fact]
    public async Task Span_text_contains_the_effect_evidence()
    {
        // The Features pass proposes no uses-per-rest effect today, so the evidence is checked on the effects that are proposed.
        var (pages, found) = await Run();
        var feat = Assert.Single(found, c => c.ProposedName == "Fixture Keen Watcher");

        Assert.Equal(DetectionPatterns.FeatBonus, EffectSpan(feat, "bonus-1").PatternId);
        Assert.Contains("+2 bonus to initiative", BlockText(pages, EffectSpan(feat, "bonus-1")), StringComparison.Ordinal);
        Assert.Contains("proficiency in the Perception skill", BlockText(pages, EffectSpan(feat, "skill-perception")), StringComparison.Ordinal);
        Assert.Contains("Origin Feat", BlockText(pages, feat.NameSpan!), StringComparison.Ordinal);

        var spell = Assert.Single(found, c => c.ProposedName == "Fixture Ember Lance");
        Assert.Contains("Components: V, S", BlockText(pages, EffectSpan(spell, "spell")), StringComparison.Ordinal);
        Assert.Contains("Level 2 Evocation", BlockText(pages, EffectSpan(spell, "spell")), StringComparison.Ordinal); // the header is part of the read
        Assert.Contains("ranged spell attack", BlockText(pages, EffectSpan(spell, "spell")), StringComparison.Ordinal); // the description is part of the read
        Assert.Contains("3d6", BlockText(pages, EffectSpan(spell, "spell")), StringComparison.Ordinal);
        Assert.Contains("Fixture Ember Lance", BlockText(pages, spell.NameSpan!), StringComparison.Ordinal);

        var weapon = Assert.Single(found, c => c.ProposedName == "Fixture Hookblade");
        Assert.Equal(DetectionPatterns.WeaponRow, EffectSpan(weapon, "weapon").PatternId);
        Assert.Contains("1d8", BlockText(pages, EffectSpan(weapon, "weapon")), StringComparison.Ordinal);

        var armor = Assert.Single(found, c => c.ProposedName == "Fixture Plate Shell");
        Assert.Contains("17", BlockText(pages, EffectSpan(armor, "armor")), StringComparison.Ordinal);

        var lamplighter = Assert.Single(found, c => c.ProposedName == "Fixture Lamplighter");
        Assert.Contains("per", BlockText(pages, EffectSpan(lamplighter, "hit-die")), StringComparison.Ordinal);
        Assert.Equal(DetectionPatterns.ClassHitDie, EffectSpan(lamplighter, "hit-die").PatternId);
        Assert.Equal(DetectionPatterns.ClassSpellcasting, EffectSpan(lamplighter, "spellcasting").PatternId);
        Assert.Equal(DetectionPatterns.ClassSubclass, EffectSpan(lamplighter, "fixture-lamplighter-subclass").PatternId);
    }

    [Fact]
    public async Task Detection_output_is_otherwise_unchanged()
    {
        // Captured in Step 0, before any pass set a span: the fixture book's candidates, ids zeroed (they are fresh Guids).
        var (_, found) = await Run();
        var stripped = found.Select(c => c with { Id = Guid.Empty, EffectSpans = null, NameSpan = null }).ToList();
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "detection-before-spans.json"));

        Assert.Equal(golden.ReplaceLineEndings("\n").TrimEnd(), JsonSerializer.Serialize(stripped, RulesJson.Options).ReplaceLineEndings("\n").TrimEnd());
    }
}
