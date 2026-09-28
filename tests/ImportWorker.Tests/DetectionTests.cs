using TomeStack.ImportWorker.Detection;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// M4 D3 (SPEC I-01, I-02; ADR-004): rule-based detection of SRD-shaped blocks in the original fixture book. Every
/// candidate is a proposal: an excerpt, a page, a kind and name, a family, proposed effects, a confidence and its
/// uncertainties, and the names it refers to that are not installed.
/// </summary>
public class DetectionTests
{
    private static readonly Guid Source = Guid.Parse("5fad5000-0000-4000-8000-000000000001");

    private static async Task<IReadOnlyList<DraftCandidate>> Detect(Func<string, bool>? installed = null)
    {
        var pages = new List<DetectionPage>();
        await foreach (var item in new PdfPigExtractor().ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
        {
            if (item is ExtractedPage page)
                pages.Add(new(page.PageNumber, page.Text, page.Blocks ?? [], page.FromOcr));
        }
        return CandidateDetector.Detect(pages, new(Source, [RulesFamilies.Srd51, RulesFamilies.Srd521], installed ?? (_ => false)));
    }

    private static DraftCandidate Named(IReadOnlyList<DraftCandidate> found, string name) => Assert.Single(found, c => c.ProposedName == name);

    [Fact]
    public async Task The_fixture_book_gives_one_candidate_per_block_and_nothing_else()
    {
        var found = await Detect();

        Assert.Equal(
            [
                (ContentKind.Spell, "Fixture Ember Lance", 2), (ContentKind.Spell, "Fixture Frost Veil", 2),
                (ContentKind.Feature, "Fixture Stormcall", 3), (ContentKind.Feat, "Fixture Keen Watcher", 3),
                (ContentKind.Item, "Fixture Hookblade", 4), (ContentKind.Item, "Fixture Longstaff", 4), (ContentKind.Item, "Fixture Plate Shell", 4), (ContentKind.Item, "Fixture Quilted Coat", 4),
                (ContentKind.Class, "Fixture Warden", 5),
            ],
            found.Select(c => (c.ProposedKind, c.ProposedName, c.Page.Start)).OrderBy(c => c.Start).ThenBy(c => c.ProposedKind).ThenBy(c => c.ProposedName));
        Assert.All(found, c =>
        {
            Assert.Equal(Source, c.SourceId);
            Assert.InRange(c.Confidence, 0.05, 0.99);
            Assert.False(string.IsNullOrWhiteSpace(c.Excerpt));
            Assert.Contains("Fixture", c.Excerpt, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_spell_block_in_the_2024_layout_is_read_field_by_field()
    {
        var ember = Named(await Detect(), "Fixture Ember Lance");

        Assert.Equal([RulesFamilies.Srd521], ember.RulesFamilies);
        var spell = Assert.IsType<SpellEffect>(Assert.Single(ember.ProposedEffects));
        Assert.Equal((2, "evocation", "Action", "60 feet", "V, S", "Instantaneous"), (spell.Level, spell.School, spell.CastingTime, spell.Range, spell.Components, spell.Duration));
        Assert.Equal(["fixture mage"], spell.Lists);
        Assert.Equal((SpellAttackKind.Ranged, "3d6", false), (spell.Attack, spell.Dice, spell.Concentration));
        Assert.StartsWith("A lance of fixture flame streaks toward a creature within range.", ember.Summary, StringComparison.Ordinal);
        Assert.Empty(ember.LowConfidenceFields);
        Assert.True(ember.Confidence >= 0.9);
    }

    [Fact]
    public async Task A_spell_block_in_the_2014_layout_is_read_and_its_missing_lists_are_an_uncertainty()
    {
        var veil = Named(await Detect(), "Fixture Frost Veil");

        Assert.Equal([RulesFamilies.Srd51], veil.RulesFamilies);
        var spell = Assert.IsType<SpellEffect>(Assert.Single(veil.ProposedEffects));
        Assert.Equal((1, "abjuration", "1 reaction", "Self", "V, S, M (a fixture snowflake)", "1 round"), (spell.Level, spell.School, spell.CastingTime, spell.Range, spell.Components, spell.Duration));
        Assert.Equal((Ability.Con, SpellAttackKind.None), (spell.Save, spell.Attack));
        Assert.Empty(spell.Lists);
        Assert.Contains(veil.Uncertainties, u => u.Contains("class lists", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_feat_proposes_the_effects_its_text_states()
    {
        var feat = Named(await Detect(), "Fixture Keen Watcher");

        Assert.Equal(ContentKind.Feat, feat.ProposedKind);
        Assert.Equal("Origin", feat.Fields["category"]);
        Assert.Contains(feat.ProposedEffects, e => e is ModifierEffect { Target: FieldIds.Initiative, Value: "2", Operation: ModifierOperation.Bonus });
        Assert.Contains(feat.ProposedEffects, e => e is GrantEffect { Grant: GrantKind.Proficiency, Target: "skill.perception" });
        Assert.Contains(feat.Uncertainties, u => u.Contains("read from the text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_feature_that_casts_a_spell_that_is_not_installed_lists_it_as_unresolved()
    {
        var found = await Detect();
        var feature = Named(found, "Fixture Stormcall");

        Assert.Equal(("3", "Fixture Warden"), (feature.Fields["level"], feature.Fields["class"]));
        Assert.Equal(["Fixture Thunder Word"], feature.UnresolvedReferences);
        Assert.Empty(feature.ProposedEffects);

        // Installed content resolves the reference.
        var installed = Named(await Detect(name => name == "Fixture Thunder Word"), "Fixture Stormcall");
        Assert.Empty(installed.UnresolvedReferences);
    }

    [Fact]
    public async Task Weapon_and_armor_table_rows_become_items_with_their_effects()
    {
        var found = await Detect();

        var hookblade = Assert.IsType<WeaponEffect>(Assert.Single(Named(found, "Fixture Hookblade").ProposedEffects));
        Assert.Equal((WeaponCategory.Martial, WeaponAttack.Melee, "1d8", "slashing", "fixture-hookblade"), (hookblade.Category, hookblade.Attack, hookblade.Damage, hookblade.DamageType, hookblade.WeaponKey));
        Assert.Equal(["finesse", "light"], hookblade.Properties);
        var longstaff = Assert.IsType<WeaponEffect>(Assert.Single(Named(found, "Fixture Longstaff").ProposedEffects));
        Assert.Equal(["heavy", "reach", "two-handed"], longstaff.Properties);

        var coat = Assert.IsType<ArmorEffect>(Assert.Single(Named(found, "Fixture Quilted Coat").ProposedEffects));
        Assert.Equal((ArmorCategory.Light, 11, (int?)null), (coat.Category, coat.ArmorClass, coat.DexterityCap));
        var shell = Assert.IsType<ArmorEffect>(Assert.Single(Named(found, "Fixture Plate Shell").ProposedEffects));
        Assert.Equal((ArmorCategory.Heavy, 17), (shell.Category, shell.ArmorClass));
    }

    [Fact]
    public async Task A_class_features_table_proposes_the_class_and_lists_the_features_not_found()
    {
        var warden = Named(await Detect(), "Fixture Warden");

        Assert.Equal(ContentKind.Class, warden.ProposedKind);
        Assert.Equal(("Fixture Focus", "Fixture Resolve", "Fixture Stormcall"), (warden.Fields["level 1"], warden.Fields["level 2"], warden.Fields["level 3"]));
        // Fixture Stormcall is another candidate of the job; the other two are not in the book.
        Assert.Equal(["Fixture Focus", "Fixture Resolve"], warden.UnresolvedReferences);
        Assert.Contains("effects", warden.LowConfidenceFields);
    }

    [Fact]
    public void Running_headers_and_footers_are_ignored()
    {
        // The same footer on every page (with a page number) never becomes a candidate or part of one.
        var pages = Enumerable.Range(1, 6).Select(n => new DetectionPage(n, "", [
            new TextBlock("Fixture Footer Book", 50, 20, 100, 10, 10, true),
            new TextBlock($"{n}", 20, 20, 5, 10, 10, true),
            new TextBlock("Fixture Mirror Step\nLevel 1 Conjuration (Fixture Mage)\nCasting Time: Bonus Action\nRange: Self\nComponents: V\nDuration: Instantaneous", 50, 600, 200, 60, 10, false),
        ], false)).ToList();

        var found = CandidateDetector.Detect(pages, new(Source, [RulesFamilies.Srd521], _ => false));

        Assert.Equal(6, found.Count(c => c.ProposedName == "Fixture Mirror Step"));
        Assert.DoesNotContain(found, c => c.Excerpt.Contains("Fixture Footer Book", StringComparison.Ordinal));
    }

    [Fact]
    public void A_layout_the_source_does_not_declare_uses_the_sources_family_and_says_so()
    {
        var page = new DetectionPage(1, "", [new TextBlock("Fixture Quick Ward\n1st-level abjuration\nCasting Time: 1 action\nRange: Self\nComponents: V\nDuration: 1 round\nA ward of fixture light.", 50, 600, 200, 60, 10, false)], false);

        var candidate = Assert.Single(CandidateDetector.Detect([page], new(Source, [RulesFamilies.Srd521], _ => false)));

        Assert.Equal([RulesFamilies.Srd521], candidate.RulesFamilies);
        Assert.Contains(candidate.Uncertainties, u => u.Contains("does not declare", StringComparison.Ordinal));
    }

    [Fact]
    public void Prose_and_long_text_do_not_become_candidates_or_hang()
    {
        var prose = string.Join('\n', Enumerable.Repeat("The fixture wind blows across 1d8 slashing plains, and nobody casts anything here at all.", 2_000));
        var page = new DetectionPage(1, prose, [new TextBlock(prose, 50, 50, 500, 700, 10, false)], false);

        var started = DateTime.UtcNow;
        var found = CandidateDetector.Detect([page], new(Source, [RulesFamilies.Srd521], _ => false));

        Assert.Empty(found);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }
}
