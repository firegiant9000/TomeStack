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

    private static async Task<IReadOnlyList<DraftCandidate>> Detect(Func<string, bool>? installed = null, IReadOnlyList<string>? families = null)
    {
        var pages = new List<DetectionPage>();
        await foreach (var item in new PdfPigExtractor().ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
        {
            if (item is ExtractedPage page)
                pages.Add(new(page.PageNumber, page.Text, page.Blocks ?? [], page.FromOcr));
        }
        return CandidateDetector.Detect(pages, new(Source, families ?? [RulesFamilies.Srd51, RulesFamilies.Srd521], installed ?? (_ => false)));
    }

    /// <summary>Letters (not digits: running headers ignore digits) that make every generated page's text its own.</summary>
    private static string Letters(int n)
    {
        var s = "";
        do
        {
            s = (char)('a' + n % 26) + s;
            n /= 26;
        }
        while (n > 0);
        return s;
    }

    /// <summary>The fixture's spell page repeated <paramref name="pages"/> times, each with its own invented names: two spells a page.</summary>
    private static async Task<List<DetectionPage>> GeneratedSpellPages(int pages)
    {
        var template = new List<TextBlock>();
        await foreach (var item in new PdfPigExtractor().ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
        {
            if (item is ExtractedPage { PageNumber: 2 } page)
                template.AddRange(page.Blocks ?? []);
        }
        var result = new List<DetectionPage>();
        for (var n = 0; n < pages; n++)
        {
            var tag = Letters(n);
            string Tag(string text) => text.Replace("Fixture Ember Lance", $"Fixture Ember Lance {tag}", StringComparison.Ordinal).Replace("Fixture Frost Veil", $"Fixture Frost Veil {tag}", StringComparison.Ordinal);
            var blocks = template.Select(b => b with
            {
                Text = Tag(b.Text) + (b.Text.Contains("Fixture", StringComparison.Ordinal) ? "" : $" {tag}"),
                Lines = b.Lines?.Select(l => l with { Text = Tag(l.Text) }).ToList(),
            }).ToList();
            result.Add(new(n + 1, "", blocks, false));
        }
        return result;
    }

    [Fact]
    public async Task Detection_says_when_the_candidate_cap_was_hit()
    {
        var context = new DetectionContext(Source, [RulesFamilies.Srd521], _ => false);

        var exactly = CandidateDetector.DetectWithLimit(await GeneratedSpellPages(2_500), context);
        Assert.Equal(CandidateDetector.MaxCandidates, exactly.Candidates.Count);
        Assert.False(exactly.Truncated); // the cap is reached, not exceeded

        var over = CandidateDetector.DetectWithLimit(await GeneratedSpellPages(2_501), context);
        Assert.Equal(CandidateDetector.MaxCandidates, over.Candidates.Count);
        Assert.True(over.Truncated); // 5,002 found

        Assert.False(CandidateDetector.DetectWithLimit(await GeneratedSpellPages(3), context).Truncated);
    }

    private static DraftCandidate Named(IReadOnlyList<DraftCandidate> found, string name) => Assert.Single(found, c => c.ProposedName == name);

    [Fact]
    public async Task Detection_stops_when_its_import_is_cancelled()
    {
        // Review 2026-09-28: cancelling an import during detection used to be ignored, and the job completed anyway.
        var pages = new List<DetectionPage>();
        await foreach (var item in new PdfPigExtractor().ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
        {
            if (item is ExtractedPage page)
                pages.Add(new(page.PageNumber, "", page.Blocks ?? [], page.FromOcr));
        }
        Assert.NotEmpty(CandidateDetector.Detect(pages, new(Source, [RulesFamilies.Srd521], _ => false))); // the text is not needed
        Assert.ThrowsAny<OperationCanceledException>(() => CandidateDetector.Detect(pages, new(Source, [RulesFamilies.Srd521], _ => false), new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task The_fixture_book_gives_one_candidate_per_block_and_nothing_else()
    {
        var found = await Detect();

        Assert.Equal(
            [
                (ContentKind.Spell, "Fixture Ember Lance", 2), (ContentKind.Spell, "Fixture Frost Veil", 2),
                (ContentKind.Feature, "Fixture Stormcall", 3), (ContentKind.Feat, "Fixture Keen Watcher", 3),
                (ContentKind.Item, "Fixture Hookblade", 4), (ContentKind.Item, "Fixture Longstaff", 4), (ContentKind.Item, "Fixture Plate Shell", 4), (ContentKind.Item, "Fixture Quilted Coat", 4),
                (ContentKind.Class, "Fixture Warden", 5), (ContentKind.Feature, "Fixture Focus", 5), (ContentKind.Feature, "Fixture Resolve", 5), // the last two are stubs of the Warden's table
                (ContentKind.Class, "Fixture Lamplighter", 7), (ContentKind.Feature, "Fixture Beacon", 7), (ContentKind.Feature, "Fixture Dusk Step", 7), (ContentKind.Feature, "Fixture Final Light", 7),
                (ContentKind.Feature, "Fixture Glow", 7), (ContentKind.Feature, "Fixture Lantern Rite", 7), (ContentKind.Feature, "Fixture Sconce", 7), (ContentKind.Feature, "Spellcasting", 7), // all but Glow are stubs
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
        Assert.Equal(["fixture-mage"], spell.Lists); // a header's class list is a normalised key (review fix 4)
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

    // Tester feedback item 10: spell list pages ("<Name> Spells" followed by level groups of names). Every name here is invented.

    private static TextBlock Heading(string text) => new(text, 50, 700, 200, 20, 16, true);

    private static TextBlock Body(string text, double y = 600) => new(text, 50, y, 200, 40, 10, false);

    private const string QuickWard = "Fixture Quick Ward\n1st-level abjuration\nCasting Time: 1 action\nRange: Self\nComponents: V\nDuration: 1 round\nA ward of fixture light.";
    private const string SparkCantrip = "Fixture Spark\nevocation cantrip\nCasting Time: 1 action\nRange: 30 feet\nComponents: V, S\nDuration: Instantaneous\nA spark of fixture light.";
    private const string EmberLance = "Fixture Ember Lance\nLevel 2 Evocation (Fixture Mage)\nCasting Time: Action\nRange: 60 feet\nComponents: V, S\nDuration: Instantaneous\nA lance of fixture flame.";

    private static IReadOnlyList<DraftCandidate> DetectPages(params DetectionPage[] pages) =>
        CandidateDetector.Detect(pages, new(Source, [RulesFamilies.Srd51, RulesFamilies.Srd521], _ => false));

    private static DetectionPage SeerListPage(int page, string heading = "Fixture Seer Spells") => new(page, "",
    [
        Heading(heading), Body("Cantrips", 650), Body("Fixture Spark", 630),
        Body("1st Level", 600), Body("Fixture Quick Ward", 580),
    ], false);

    [Fact]
    public void A_spell_list_page_proposes_its_key_on_the_spells_it_names_as_an_unsure_field_citing_the_page()
    {
        var found = DetectPages(new(1, "", [Heading("Fixture Spells"), Body(QuickWard, 600), Body(SparkCantrip, 500), Body(EmberLance, 400)], false), SeerListPage(2));

        var ward = Named(found, "Fixture Quick Ward");
        var spell = Assert.IsType<SpellEffect>(Assert.Single(ward.ProposedEffects));
        Assert.Equal(["fixture-seer"], spell.Lists);
        Assert.Equal("fixture-seer", ward.Fields["lists"]);
        Assert.Contains("lists", ward.LowConfidenceFields);
        Assert.Contains("Spell list 'fixture-seer' was read from page 2; check it.", ward.Uncertainties);
        Assert.DoesNotContain(ward.Uncertainties, u => u.Contains("class lists", StringComparison.Ordinal));

        var spark = Named(found, "Fixture Spark");
        Assert.Equal(["fixture-seer"], Assert.IsType<SpellEffect>(Assert.Single(spark.ProposedEffects)).Lists);
        Assert.Contains("lists", spark.LowConfidenceFields);

        var lance = Named(found, "Fixture Ember Lance");
        Assert.Equal(["fixture-mage"], Assert.IsType<SpellEffect>(Assert.Single(lance.ProposedEffects)).Lists); // not on the page
        Assert.DoesNotContain("lists", lance.LowConfidenceFields);
        Assert.DoesNotContain(lance.Uncertainties, u => u.StartsWith("Spell list ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_spell_on_several_list_pages_gets_every_key_and_a_heading_in_capitals_counts()
    {
        var found = DetectPages(new(1, "", [Heading("Fixture Spells"), Body(QuickWard)], false), SeerListPage(2),
            new(3, "", [Heading("FIXTURE WARDEN SPELLS"), Body("1ST LEVEL", 650), Body("Fixture Quick Ward", 630)], false));

        var ward = Named(found, "Fixture Quick Ward");
        Assert.Equal(["fixture-seer", "fixture-warden"], Assert.IsType<SpellEffect>(Assert.Single(ward.ProposedEffects)).Lists);
        Assert.Equal("fixture-seer, fixture-warden", ward.Fields["lists"]);
        Assert.Contains("Spell list 'fixture-seer' was read from page 2; check it.", ward.Uncertainties);
        Assert.Contains("Spell list 'fixture-warden' was read from page 3; check it.", ward.Uncertainties);
    }

    [Fact]
    public void A_key_the_spell_already_has_is_not_duplicated_or_marked_unsure()
    {
        var found = DetectPages(new(1, "", [Heading("Fixture Spells"), Body(EmberLance.Replace("Fixture Mage", "Wizard", StringComparison.Ordinal))], false),
            new(2, "", [Heading("Wizard Spells"), Body("Cantrips", 650), Body("2nd Level", 630), Body("Fixture Ember Lance", 600)], false));

        var lance = Named(found, "Fixture Ember Lance");
        Assert.Equal(["wizard"], Assert.IsType<SpellEffect>(Assert.Single(lance.ProposedEffects)).Lists);
        Assert.DoesNotContain("lists", lance.LowConfidenceFields);
        Assert.DoesNotContain(lance.Uncertainties, u => u.StartsWith("Spell list ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_list_that_runs_over_to_the_next_page_keeps_its_key_and_cites_the_heading_page()
    {
        var found = DetectPages(
            new DetectionPage(1, "", [Heading("Fixture Spells"), Body(QuickWard, 600), Body(SparkCantrip, 500)], false),
            new DetectionPage(2, "", [Heading("Fixture Seer Spells"), Body("Cantrips", 650), Body("Fixture Spark", 630)], false),
            new DetectionPage(3, "", [Body("1st Level", 650), Body("Fixture Quick Ward", 630)], false)); // no heading: the list goes on

        var ward = Named(found, "Fixture Quick Ward");
        Assert.Equal(["fixture-seer"], Assert.IsType<SpellEffect>(Assert.Single(ward.ProposedEffects)).Lists);
        Assert.Contains("Spell list 'fixture-seer' was read from page 2; check it.", ward.Uncertainties);
        Assert.Contains("lists", ward.LowConfidenceFields);
    }

    [Fact]
    public void A_new_heading_or_a_page_of_other_text_ends_a_list_that_ran_over()
    {
        var found = DetectPages(
            new DetectionPage(1, "", [Heading("Fixture Spells"), Body(QuickWard, 600), Body(SparkCantrip, 500)], false),
            new DetectionPage(2, "", [Heading("Fixture Seer Spells"), Body("Cantrips", 650), Body("Fixture Spark", 630)], false),
            new DetectionPage(3, "", [Heading("Chapter Notes"), Body("Some fixture prose that is not a name at all, and goes on for a while.", 650)], false),
            new DetectionPage(4, "", [Body("1st Level", 650), Body("Fixture Quick Ward", 630)], false));

        Assert.Empty(Assert.IsType<SpellEffect>(Assert.Single(Named(found, "Fixture Quick Ward").ProposedEffects)).Lists);
    }

    [Fact]
    public void A_header_list_that_cannot_be_a_key_is_dropped_with_an_uncertainty_that_names_the_page_only()
    {
        var found = DetectPages(new DetectionPage(7, "", [Heading("Fixture Spells"), Body(EmberLance.Replace("Fixture Mage", "Seer's Rite, Wizard", StringComparison.Ordinal))], false));

        var lance = Named(found, "Fixture Ember Lance");
        Assert.Equal(["wizard"], Assert.IsType<SpellEffect>(Assert.Single(lance.ProposedEffects)).Lists);
        var note = Assert.Single(lance.Uncertainties, u => u.Contains("list key", StringComparison.Ordinal));
        Assert.Contains("page 7", note, StringComparison.Ordinal);
        Assert.DoesNotContain("Seer", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_whose_only_list_cannot_be_a_key_has_no_lists_and_says_so()
    {
        var found = DetectPages(new DetectionPage(7, "", [Heading("Fixture Spells"), Body(EmberLance.Replace("Fixture Mage", "Seer's Rite", StringComparison.Ordinal))], false));

        var lance = Named(found, "Fixture Ember Lance");
        Assert.Empty(Assert.IsType<SpellEffect>(Assert.Single(lance.ProposedEffects)).Lists);
        Assert.Contains(lance.Uncertainties, u => u.Contains("class lists", StringComparison.Ordinal));
        Assert.DoesNotContain(lance.Fields.Keys, k => k == "lists");
    }

    [Fact]
    public void A_heading_without_level_groups_is_not_a_list_page()
    {
        var found = DetectPages(new(1, "", [Heading("Fixture Spells"), Body(QuickWard)], false),
            new(2, "", [Heading("Fixture Seer Spells"), Body("Fixture Quick Ward is a fixture spell that seers like.")], false));

        var ward = Named(found, "Fixture Quick Ward");
        Assert.Empty(Assert.IsType<SpellEffect>(Assert.Single(ward.ProposedEffects)).Lists);
        Assert.Contains(ward.Uncertainties, u => u.Contains("class lists", StringComparison.Ordinal));
        Assert.DoesNotContain("lists", ward.LowConfidenceFields);
    }

    [Fact]
    public void A_list_name_that_cannot_be_a_key_proposes_nothing()
    {
        var found = DetectPages(new(1, "", [Heading("Fixture Spells"), Body(QuickWard)], false), SeerListPage(2, "Seer's Spells"));

        Assert.Empty(Assert.IsType<SpellEffect>(Assert.Single(Named(found, "Fixture Quick Ward").ProposedEffects)).Lists);
    }

    [Fact]
    public void The_list_page_does_not_become_part_of_the_spell_before_it()
    {
        var found = DetectPages(new(1, "", [Heading("Fixture Spells"), Body(QuickWard)], false), SeerListPage(2));

        Assert.DoesNotContain("Cantrips", Named(found, "Fixture Quick Ward").Excerpt, StringComparison.Ordinal);
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
        // Fixture Stormcall is another candidate of the job; the other two are not in the book, so they are stubs (item 11, R54), which resolve the references.
        Assert.Empty(warden.UnresolvedReferences);
        Assert.Contains("effects", warden.LowConfidenceFields);
    }

    [Fact]
    public async Task A_class_page_with_its_table_proposes_the_effects_the_page_supports()
    {
        var lamplighter = Named(await Detect(), "Fixture Lamplighter");

        Assert.Equal(7, lamplighter.Page.Start);
        Assert.Equal(["hit-die", "save-wis", "save-cha", "armor-light", "weapon-simple", "fixture-lamplighter-subclass", "spellcasting"], lamplighter.ProposedEffects.Select(e => e.Id));
        Assert.Equal(6, Assert.IsType<HitDieEffect>(lamplighter.ProposedEffects[0]).Die);
        var wisdom = Assert.IsType<GrantEffect>(lamplighter.ProposedEffects[1]);
        Assert.Equal(("save.wis", ClassEntry.StartingClass), (wisdom.Target, wisdom.OnlyAs));
        Assert.Equal("armor.light", Assert.IsType<GrantEffect>(lamplighter.ProposedEffects[3]).Target);
        var subclass = Assert.IsType<ChoiceEffect>(lamplighter.ProposedEffects[5]);
        Assert.Equal((3, 0), (subclass.Level, subclass.Options.Count)); // the first "Fixture Order feature" is at level 3

        // R52: the class's own spellcasting effect, from its table; R53: the spell list is the class name through the one normaliser; R57: no multiclass share.
        var casting = Assert.IsType<SpellcastingEffect>(lamplighter.ProposedEffects[6]);
        Assert.Equal((Ability.Wis, SpellPreparation.Prepared, "fixture-lamplighter", SpellSlotKind.SpellSlots), (casting.Ability, casting.Preparation, casting.SpellList, casting.SlotKind));
        Assert.Equal(FixturePdfs.Lamplighter.Cantrips, casting.Cantrips);
        Assert.Equal(FixturePdfs.Lamplighter.Slots.Select(r => r.Take(Array.FindLastIndex(r, n => n > 0) + 1).ToArray()), casting.Slots.Select(r => r.ToArray()));
        Assert.Null(casting.SpellsTable);
        Assert.Null(casting.MulticlassCaster);
        Assert.Null(casting.MulticlassCasterTable);
        Assert.Contains("spellList", lamplighter.LowConfidenceFields);
        Assert.DoesNotContain("effects", lamplighter.LowConfidenceFields);
        Assert.Equal("Fixture Glow", lamplighter.Fields["level 2"]);
        Assert.Contains("Insight", lamplighter.Fields["skills"], StringComparison.Ordinal);
    }

    private static async Task<DetectionResult> DetectResult(Func<string, bool>? installed = null)
    {
        var pages = new List<DetectionPage>();
        await foreach (var item in new PdfPigExtractor().ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
        {
            if (item is ExtractedPage page)
                pages.Add(new(page.PageNumber, page.Text, page.Blocks ?? [], page.FromOcr));
        }
        return CandidateDetector.DetectWithLimit(pages, new(Source, [RulesFamilies.Srd51, RulesFamilies.Srd521], installed ?? (_ => false)));
    }

    [Fact]
    public async Task A_class_links_each_feature_its_table_names_to_a_detected_feature_or_a_reference_stub()
    {
        var result = await DetectResult();
        var byId = result.Candidates.ToDictionary(c => c.Id);
        var lamplighter = Named(result.Candidates, "Fixture Lamplighter");

        var links = result.Links.Where(l => l.ClassId == lamplighter.Id).Select(l => (byId[l.FeatureId].ProposedName, l.Level)).OrderBy(l => l.Level).ThenBy(l => l.ProposedName, StringComparer.Ordinal).ToList();
        Assert.Equal(
            [("Fixture Lantern Rite", 1), ("Spellcasting", 1), ("Fixture Glow", 2), ("Fixture Beacon", 5), ("Fixture Glow", 7), ("Fixture Sconce", 9), ("Fixture Beacon", 11), ("Fixture Dusk Step", 15), ("Fixture Final Light", 20)],
            links);

        // Fixture Glow is a block of the book: the link goes to it, not to a stub. The others are stubs of the class page: a name, no effects.
        var glow = Named(result.Candidates, "Fixture Glow");
        Assert.Equal(7, glow.Page.Start);
        Assert.Equal(2, result.Links.Count(l => l.FeatureId == glow.Id));
        var stubs = result.Links.Where(l => l.ClassId == lamplighter.Id).Select(l => byId[l.FeatureId]).Where(c => c.Id != glow.Id).DistinctBy(c => c.Id).ToList();
        Assert.Equal(6, stubs.Count);
        Assert.All(stubs, s =>
        {
            Assert.Equal(ContentKind.Feature, s.ProposedKind);
            Assert.Empty(s.ProposedEffects);
            Assert.Equal(lamplighter.Page, s.Page);
            Assert.Empty(s.LowConfidenceFields);
            Assert.Empty(s.UnresolvedReferences);
        });
        // The class's own references now resolve (the stubs are candidates of the job); no ASI or "... feature" placeholder is linked.
        Assert.Empty(lamplighter.UnresolvedReferences);
        Assert.DoesNotContain(result.Candidates, c => c.ProposedName is CandidateDetector.ReferenceOnlyFeatureName or "Fixture Order feature");

        // The Warden's one-line rows link too: Stormcall is a block of the book, Focus and Resolve are stubs.
        var warden = Named(result.Candidates, "Fixture Warden");
        Assert.Equal(["Fixture Focus", "Fixture Resolve", "Fixture Stormcall"], result.Links.Where(l => l.ClassId == warden.Id).OrderBy(l => l.Level).Select(l => byId[l.FeatureId].ProposedName));
        Assert.Empty(warden.UnresolvedReferences);
    }

    [Fact]
    public async Task A_feature_name_that_is_installed_gets_no_stub_and_no_link()
    {
        var result = await DetectResult(name => name == "Spellcasting");

        var lamplighter = Named(result.Candidates, "Fixture Lamplighter");
        Assert.DoesNotContain(result.Candidates, c => c.ProposedName == "Spellcasting");
        Assert.Equal(8, result.Links.Count(l => l.ClassId == lamplighter.Id));
    }

    private static DraftCandidate Fx(ContentKind kind, string name, int page, string? owner = null) =>
        new(Guid.NewGuid(), Source, new PageRef(page), name, kind, name, [], [], 0.7, [])
        {
            Fields = owner is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["class"] = owner },
        };

    private static (List<DraftCandidate> Found, List<CandidateLink> Links) LinkFixture(List<DraftCandidate> found, Dictionary<Guid, IReadOnlyDictionary<int, IReadOnlyList<string>>> tables, Func<string, bool>? one = null)
    {
        var links = CandidateDetector.LinkClasses(found, tables, new(Source, [RulesFamilies.Srd51], _ => false, one));
        return (found, links);
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<string>> Table(params (int Level, string Feature)[] rows) =>
        rows.ToDictionary(r => r.Level, r => (IReadOnlyList<string>)[r.Feature]);

    [Fact]
    public void Two_classes_with_a_same_name_feature_each_link_their_own()
    {
        var a = Fx(ContentKind.Class, "Fixture Alpha", 10);
        var b = Fx(ContentKind.Class, "Fixture Beta", 30);
        var aRite = Fx(ContentKind.Feature, "Fixture Rite", 11, "Fixture Alpha");
        var bRite = Fx(ContentKind.Feature, "Fixture Rite", 31, "Fixture Beta");
        var (_, links) = LinkFixture([a, b, aRite, bRite], new() { [a.Id] = Table((1, "Fixture Rite")), [b.Id] = Table((1, "Fixture Rite")) });

        Assert.Equal(aRite.Id, Assert.Single(links, l => l.ClassId == a.Id).FeatureId);
        Assert.Equal(bRite.Id, Assert.Single(links, l => l.ClassId == b.Id).FeatureId);
    }

    [Fact]
    public void Two_classes_naming_the_same_stub_only_feature_each_get_their_own_stub()
    {
        var a = Fx(ContentKind.Class, "Fixture Alpha", 10);
        var b = Fx(ContentKind.Class, "Fixture Beta", 30);
        var (found, links) = LinkFixture([a, b], new() { [a.Id] = Table((2, "Fixture Echo")), [b.Id] = Table((4, "Fixture Echo")) });

        var stubA = found.Single(c => c.Id == Assert.Single(links, l => l.ClassId == a.Id).FeatureId);
        var stubB = found.Single(c => c.Id == Assert.Single(links, l => l.ClassId == b.Id).FeatureId);
        Assert.NotEqual(stubA.Id, stubB.Id);
        Assert.Equal(("Fixture Alpha", "2"), (stubA.Fields["class"], stubA.Fields["level"]));
        Assert.Equal(("Fixture Beta", "4"), (stubB.Fields["class"], stubB.Fields["level"]));
    }

    [Fact]
    public void A_feature_with_no_class_field_links_to_the_class_whose_pages_it_is_on()
    {
        var a = Fx(ContentKind.Class, "Fixture Alpha", 10);
        var b = Fx(ContentKind.Class, "Fixture Beta", 30);
        var between = Fx(ContentKind.Feature, "Fixture Spark", 12);
        var (found, links) = LinkFixture([a, b, between], new() { [a.Id] = Table((1, "Fixture Spark")), [b.Id] = Table((1, "Fixture Spark")) });

        Assert.Equal(between.Id, Assert.Single(links, l => l.ClassId == a.Id).FeatureId);
        var bTarget = Assert.Single(links, l => l.ClassId == b.Id).FeatureId;
        Assert.NotEqual(between.Id, bTarget);
        Assert.Equal("Fixture Beta", found.Single(c => c.Id == bTarget).Fields["class"]);
    }

    [Fact]
    public void A_feature_that_names_another_class_is_never_a_target()
    {
        var a = Fx(ContentKind.Class, "Fixture Alpha", 10);
        var b = Fx(ContentKind.Class, "Fixture Beta", 30);
        var bOnly = Fx(ContentKind.Feature, "Fixture Spark", 12, "Fixture Beta");
        var (_, links) = LinkFixture([a, b, bOnly], new() { [a.Id] = Table((1, "Fixture Spark")) });

        Assert.NotEqual(bOnly.Id, Assert.Single(links).FeatureId);
    }

    [Fact]
    public void Linking_stops_when_cancelled()
    {
        var a = Fx(ContentKind.Class, "Fixture Alpha", 10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => CandidateDetector.LinkClasses([a], new Dictionary<Guid, IReadOnlyDictionary<int, IReadOnlyList<string>>> { [a.Id] = Table((1, "Fixture Echo")) }, new(Source, [RulesFamilies.Srd51], _ => false), cts.Token));
    }

    [Fact]
    public async Task A_two_family_context_proposes_no_family_for_layout_neutral_kinds()
    {
        // 2026-10-14 item 3: no family under a two-family context (armour rows and class tables carry no layout).
        var found = await Detect();

        var neutral = found.Where(c => c.ProposedKind == ContentKind.Class || c.ProposedEffects.Any(e => e is ArmorEffect)).ToList();
        Assert.Equal(4, neutral.Count);
        Assert.All(neutral, c =>
        {
            Assert.Empty(c.RulesFamilies);
            Assert.Contains("rulesFamilies", c.LowConfidenceFields);
            Assert.Contains("Say which rules family this is written for before accepting.", c.Uncertainties);
        });
        // Layout-decided kinds still pick their family.
        Assert.Equal([RulesFamilies.Srd521], Named(found, "Fixture Ember Lance").RulesFamilies);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public async Task A_one_family_context_gives_layout_neutral_kinds_that_family(string family)
    {
        var found = await Detect(families: [family]);

        var neutral = found.Where(c => c.ProposedKind == ContentKind.Class || c.ProposedEffects.Any(e => e is ArmorEffect)).ToList();
        Assert.Equal(4, neutral.Count);
        Assert.All(neutral, c =>
        {
            Assert.Equal([family], c.RulesFamilies);
            Assert.DoesNotContain("rulesFamilies", c.LowConfidenceFields);
        });
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
        Assert.Contains("The layout looks like srd-5.1, which is outside the families this reading is for; those families are used.", candidate.Uncertainties);
    }

    // 2026-10-14 items 5 and 6: tolerance for third-party layouts. Every block here is invented.

    private static (IReadOnlyList<DraftCandidate> Candidates, IReadOnlyList<CandidateLink> Links) DetectWithLinks(IReadOnlyList<string> families, params TextBlock[] blocks)
    {
        var result = CandidateDetector.DetectWithLimit([new DetectionPage(1, "", blocks, false)], new(Source, families, _ => false));
        return (result.Candidates, result.Links);
    }

    private static IReadOnlyList<DraftCandidate> DetectBlocks(IReadOnlyList<string> families, params TextBlock[] blocks) =>
        CandidateDetector.Detect([new DetectionPage(1, "", blocks, false)], new(Source, families, _ => false));

    [Fact]
    public void A_feat_whose_name_and_prerequisites_share_one_block_is_found()
    {
        var page = new DetectionPage(1, "", [
            new TextBlock("FIXTURE DEFT HANDS\nPrerequisites: Dexterity 13 or higher\nYou handle fixture tools quickly. You gain a +1 bonus to initiative.", 50, 600, 200, 40, 10, false),
        ], false);

        var feat = Assert.Single(CandidateDetector.Detect([page], new(Source, [RulesFamilies.Srd51], _ => false)), c => c.ProposedKind == ContentKind.Feat);

        Assert.Equal("Fixture Deft Hands", feat.ProposedName);
        Assert.Equal("Dexterity 13 or higher", feat.Fields["prerequisite"]);
        Assert.Equal([RulesFamilies.Srd51], feat.RulesFamilies);
        Assert.Contains("FIXTURE DEFT HANDS", feat.Excerpt, StringComparison.Ordinal);
        Assert.StartsWith("You handle fixture tools quickly.", feat.Summary, StringComparison.Ordinal);
        Assert.Contains(feat.ProposedEffects, e => e is ModifierEffect { Target: FieldIds.Initiative, Value: "1" });
    }

    [Fact]
    public void A_one_block_feat_under_a_two_family_context_proposes_no_family()
    {
        // Ruling R22: the layout matches neither SRD, so the reviewer says which family the book is written for.
        var found = DetectBlocks([RulesFamilies.Srd51, RulesFamilies.Srd521],
            new TextBlock("Fixture Deft Hands\nPrerequisites: Dexterity 13 or higher\nYou handle fixture tools quickly.", 50, 600, 200, 40, 10, false));

        var feat = Assert.Single(found, c => c.ProposedKind == ContentKind.Feat);
        Assert.Empty(feat.RulesFamilies);
        Assert.Contains("rulesFamilies", feat.LowConfidenceFields);
        Assert.Contains("Say which rules family this is written for before accepting.", feat.Uncertainties);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void A_one_block_feat_under_a_one_family_context_takes_that_family(string family)
    {
        var found = DetectBlocks([family],
            new TextBlock("Fixture Deft Hands\nPrerequisites: Dexterity 13 or higher\nYou handle fixture tools quickly.", 50, 600, 200, 40, 10, false));

        var feat = Assert.Single(found, c => c.ProposedKind == ContentKind.Feat);
        Assert.Equal([family], feat.RulesFamilies);
        Assert.DoesNotContain("rulesFamilies", feat.LowConfidenceFields);
    }

    [Fact]
    public void A_heading_feat_keeps_the_2014_family_under_a_two_family_context()
    {
        var found = DetectBlocks([RulesFamilies.Srd51, RulesFamilies.Srd521],
            new TextBlock("Fixture Steady Aim", 50, 640, 200, 20, 16, true),
            new TextBlock("Prerequisite: Wisdom 13 or higher", 50, 610, 200, 12, 10, false),
            new TextBlock("You steady your fixture aim.", 50, 590, 200, 12, 10, false));

        Assert.Equal([RulesFamilies.Srd51], Assert.Single(found, c => c.ProposedKind == ContentKind.Feat).RulesFamilies);
    }

    [Fact]
    public void A_heading_feat_with_the_plural_Prerequisites_label_proposes_no_family_under_a_two_family_context()
    {
        // Ruling R22: the plural label is the non-SRD marker, in the two-block layout as in the one-block one.
        var found = DetectBlocks([RulesFamilies.Srd51, RulesFamilies.Srd521],
            new TextBlock("Fixture Steady Aim", 50, 640, 200, 20, 16, true),
            new TextBlock("Prerequisites: Wisdom 13 or higher", 50, 610, 200, 12, 10, false),
            new TextBlock("You steady your fixture aim.", 50, 590, 200, 12, 10, false));

        var feat = Assert.Single(found, c => c.ProposedKind == ContentKind.Feat);
        Assert.Empty(feat.RulesFamilies);
        Assert.Contains("rulesFamilies", feat.LowConfidenceFields);
        Assert.Contains("Say which rules family this is written for before accepting.", feat.Uncertainties);
    }

    [Fact]
    public void A_description_paragraph_starting_with_Prerequisites_is_not_a_feat()
    {
        // A plain (not heading) one-line block, then a block that starts with "Prerequisites:". A rule that dropped the
        // heading requirement would take the first block for a feat name.
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("Fixture Knack", 50, 600, 200, 12, 10, false),
            new TextBlock("Prerequisites: Dexterity 13 or higher", 50, 580, 200, 12, 10, false),
            new TextBlock("Fixture Hush\nAt 5th level, you learn a second trick.", 50, 500, 200, 40, 10, false),
            new TextBlock("Prerequisites: none are needed for the fixture trick, which anyone may learn at any time.", 50, 450, 200, 30, 10, false),
            new TextBlock("Fixture Quill\nAt 7th level, you learn a third trick.\nPrerequisites: none are needed for this one either.", 50, 350, 200, 40, 10, false));

        Assert.DoesNotContain(found, c => c.ProposedKind == ContentKind.Feat);
    }

    [Fact]
    public void A_heading_feat_keeps_a_long_prerequisite_but_a_one_block_feat_caps_it_at_400_characters()
    {
        var longText = string.Join(' ', Enumerable.Repeat("a long fixture prerequisite", 12)); // 335 characters
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("Fixture Steady Aim", 50, 640, 200, 20, 16, true),
            new TextBlock($"Prerequisite: {longText} {longText}", 50, 610, 200, 12, 10, false),
            new TextBlock("You steady your fixture aim.", 50, 590, 200, 12, 10, false),
            new TextBlock($"Fixture Wide Stance\nPrerequisites: {longText} {longText}\nYou stand wide.", 50, 400, 200, 40, 10, false),
            new TextBlock($"Fixture Deep Stance\nPrerequisites: {longText}\nYou stand deep.", 50, 300, 200, 40, 10, false));

        Assert.Equal(671, Assert.Single(found, c => c.ProposedName == "Fixture Steady Aim").Fields["prerequisite"].Length);
        Assert.DoesNotContain(found, c => c.ProposedName == "Fixture Wide Stance");
        Assert.Single(found, c => c.ProposedName == "Fixture Deep Stance");
    }

    [Fact]
    public void A_one_block_feat_in_a_class_section_is_not_also_a_class_feature()
    {
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("Class Features", 50, 700, 200, 20, 10, false),
            new TextBlock("Fixture Gift\nPrerequisites: Fixture Knack, at 3rd level or higher\nYou gain a fixture gift.", 50, 600, 200, 40, 10, false));

        var candidate = Assert.Single(found);
        Assert.Equal(ContentKind.Feat, candidate.ProposedKind);
    }

    [Fact]
    public void A_name_and_a_singular_Prerequisite_line_in_one_block_is_an_option_list_entry_not_a_feat()
    {
        // An option-list entry (a pick-one option with a prerequisite) has this shape; on the SRDs they measured as 20+ spurious feats.
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("Fixture Quiet Step\nPrerequisite: 5th level\nYou move without a sound.", 50, 600, 200, 40, 10, false));

        Assert.DoesNotContain(found, c => c.ProposedKind == ContentKind.Feat);
    }

    [Fact]
    public void An_item_name_with_a_comma_and_one_lower_case_word_stays_an_item()
    {
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("Martial Melee Weapons", 50, 700, 200, 10, 10, false),
            new TextBlock("Fixture Lantern, small  1d6 piercing  Light", 50, 680, 300, 10, 10, false));

        Assert.Contains(found, c => c.ProposedKind == ContentKind.Item && c.ProposedName == "Fixture Lantern, small");
    }

    [Fact]
    public void A_heading_followed_by_a_Prerequisites_block_is_a_feat_with_the_plural_label_too()
    {
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("Fixture Steady Aim", 50, 640, 200, 20, 16, true),
            new TextBlock("Prerequisites: Wisdom 13 or higher", 50, 610, 200, 12, 10, false),
            new TextBlock("You steady your fixture aim.", 50, 590, 200, 12, 10, false));

        var feat = Assert.Single(found, c => c.ProposedKind == ContentKind.Feat);
        Assert.Equal(("Fixture Steady Aim", "Wisdom 13 or higher"), (feat.ProposedName, feat.Fields["prerequisite"]));
    }

    [Fact]
    public void Spell_names_in_capitals_are_title_cased_and_the_excerpt_keeps_them()
    {
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("FIXTURE BOLT\n1st-level evocation\nCasting Time: 1 action\nRange: 60 feet\nComponents: V, S\nDuration: Instantaneous\nA bolt of fixture light leaps from your hand.", 50, 600, 200, 80, 10, false),
            new TextBlock("FIXTURE BLADE OF DUSK\n2nd-level illusion\nCasting Time: 1 action\nRange: Self\nComponents: V\nDuration: 1 minute\nA blade of fixture dusk forms.", 50, 400, 200, 80, 10, false));

        var bolt = Assert.Single(found, c => c.ProposedName == "Fixture Bolt");
        Assert.Contains("FIXTURE BOLT", bolt.Excerpt, StringComparison.Ordinal);
        Assert.Single(found, c => c.ProposedName == "Fixture Blade of Dusk");
    }

    [Fact]
    public void A_class_table_titled_in_capitals_is_found()
    {
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("THE FIXTURE WARDEN", 50, 700, 200, 20, 10, false),
            new TextBlock("Level", 50, 660, 40, 10, 10, false),
            new TextBlock("Features", 150, 660, 100, 10, 10, false),
            new TextBlock("1st", 50, 640, 40, 10, 10, false), new TextBlock("Fixture Focus", 150, 640, 100, 10, 10, false),
            new TextBlock("2nd", 50, 620, 40, 10, 10, false), new TextBlock("Fixture Resolve", 150, 620, 100, 10, 10, false),
            new TextBlock("3rd", 50, 600, 40, 10, 10, false), new TextBlock("Fixture Stormcall", 150, 600, 100, 10, 10, false));

        var warden = Assert.Single(found, c => c.ProposedKind == ContentKind.Class);
        Assert.Equal("Fixture Warden", warden.ProposedName);
        Assert.Equal("Fixture Resolve", warden.Fields["level 2"]);
    }

    [Fact]
    public void Two_classes_on_one_page_are_read_each_from_its_own_window_with_stubs_of_their_own()
    {
        // Wave 0/1 minors (d): both tables sit on page 1, one under the other; each class takes only the rows of its own window.
        var (found, links) = DetectWithLinks([RulesFamilies.Srd51],
            new TextBlock("Fixture Alpha Features", 50, 700, 200, 20, 10, false),
            new TextBlock("Level", 50, 680, 40, 10, 10, false), new TextBlock("Features", 150, 680, 100, 10, 10, false),
            new TextBlock("1st", 50, 660, 40, 10, 10, false), new TextBlock("Fixture Alpha Rite", 150, 660, 100, 10, 10, false),
            new TextBlock("2nd", 50, 640, 40, 10, 10, false), new TextBlock("Fixture Echo", 150, 640, 100, 10, 10, false),
            new TextBlock("3rd", 50, 620, 40, 10, 10, false), new TextBlock("Fixture Alpha Vigil", 150, 620, 100, 10, 10, false),
            new TextBlock("Fixture Beta Features", 50, 500, 200, 20, 10, false),
            new TextBlock("Level", 50, 480, 40, 10, 10, false), new TextBlock("Features", 150, 480, 100, 10, 10, false),
            new TextBlock("1st", 50, 460, 40, 10, 10, false), new TextBlock("Fixture Beta Rite", 150, 460, 100, 10, 10, false),
            new TextBlock("2nd", 50, 440, 40, 10, 10, false), new TextBlock("Fixture Beta Vigil", 150, 440, 100, 10, 10, false),
            new TextBlock("3rd", 50, 420, 40, 10, 10, false), new TextBlock("Fixture Echo", 150, 420, 100, 10, 10, false));

        var alpha = Assert.Single(found, c => c.ProposedKind == ContentKind.Class && c.ProposedName == "Fixture Alpha");
        var beta = Assert.Single(found, c => c.ProposedKind == ContentKind.Class && c.ProposedName == "Fixture Beta");
        Assert.Equal(["level 1", "level 2", "level 3"], alpha.Fields.Keys.Where(k => k.StartsWith("level ", StringComparison.Ordinal)));
        Assert.Equal("Fixture Alpha Vigil", alpha.Fields["level 3"]);
        Assert.Equal("Fixture Beta Rite", beta.Fields["level 1"]);
        Assert.Equal("Fixture Echo", beta.Fields["level 3"]);
        // Each class links only its own names; the shared name gets one stub per class, owned by that class.
        var alphaStubs = links.Where(l => l.ClassId == alpha.Id).Select(l => found.Single(c => c.Id == l.FeatureId)).ToList();
        var betaStubs = links.Where(l => l.ClassId == beta.Id).Select(l => found.Single(c => c.Id == l.FeatureId)).ToList();
        Assert.Equal(3, alphaStubs.Count);
        Assert.Equal(3, betaStubs.Count);
        Assert.All(alphaStubs, s => Assert.Equal("Fixture Alpha", s.Fields["class"]));
        Assert.All(betaStubs, s => Assert.Equal("Fixture Beta", s.Fields["class"]));
        Assert.Empty(alphaStubs.Select(s => s.Id).Intersect(betaStubs.Select(s => s.Id)));
        Assert.DoesNotContain(alphaStubs, s => s.ProposedName.Contains("Beta", StringComparison.Ordinal));
    }

    [Fact]
    public void Classes_sharing_a_page_split_the_features_that_name_no_class_by_window()
    {
        // A feature on the shared page cannot be placed under one of the two classes, so neither links it; one on a later page
        // (before the next class) belongs to the class whose table comes last on the shared page.
        var a = Fx(ContentKind.Class, "Fixture Alpha", 10);
        var b = Fx(ContentKind.Class, "Fixture Beta", 10);
        var shared = Fx(ContentKind.Feature, "Fixture Spark", 10);
        var later = Fx(ContentKind.Feature, "Fixture Mid", 12);
        var (_, links) = LinkFixture([a, b, shared, later], new() { [a.Id] = Table((1, "Fixture Spark"), (2, "Fixture Mid")), [b.Id] = Table((1, "Fixture Spark"), (2, "Fixture Mid")) });

        Assert.DoesNotContain(links, l => l.FeatureId == shared.Id);
        Assert.DoesNotContain(links, l => l.ClassId == a.Id && l.FeatureId == later.Id);
        Assert.Contains(links, l => l.ClassId == b.Id && l.FeatureId == later.Id);
    }

    [Fact]
    public void A_class_table_titled_with_Features_in_capitals_is_found()
    {
        var found = DetectBlocks([RulesFamilies.Srd521],
            new TextBlock("FIXTURE WARDEN FEATURES", 50, 700, 200, 20, 10, false),
            new TextBlock("Level  Proficiency Bonus  Class Features", 50, 670, 300, 10, 10, false),
            new TextBlock("1  +2  Fixture Focus", 50, 650, 300, 10, 10, false),
            new TextBlock("2  +2  Fixture Resolve", 50, 630, 300, 10, 10, false));

        Assert.Equal("Fixture Warden", Assert.Single(found, c => c.ProposedKind == ContentKind.Class).ProposedName);
    }

    [Fact]
    public void A_class_table_whose_header_cells_are_lower_case_is_not_read()
    {
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("THE FIXTURE WARDEN", 50, 700, 200, 20, 10, false),
            new TextBlock("level", 50, 660, 40, 10, 10, false),
            new TextBlock("class features", 150, 660, 100, 10, 10, false),
            new TextBlock("1st", 50, 640, 40, 10, 10, false), new TextBlock("Fixture Focus", 150, 640, 100, 10, 10, false));

        Assert.DoesNotContain(found, c => c.ProposedKind == ContentKind.Class);
    }

    [Theory]
    [InlineData("Fixture Hook 5 gp")]                  // a price inside a name
    [InlineData("Fixture Big Hook, a fine Tool")]       // a sentence fragment that LooksLikeCellName accepts: only the junk rule drops it
    [InlineData("Fixture Hook, a hook")]                // a comma and two lower-case words
    public void Item_rows_with_prices_or_sentence_fragments_in_the_name_are_dropped(string name)
    {
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("Martial Melee Weapons", 50, 700, 200, 10, 10, false),
            new TextBlock("Fixture Hookblade  1d8 slashing  Finesse, Light", 50, 680, 300, 10, 10, false),
            new TextBlock($"{name}  1d6 slashing  Light", 50, 660, 300, 10, 10, false));

        Assert.Contains(found, c => c.ProposedKind == ContentKind.Item && c.ProposedName == "Fixture Hookblade");
        Assert.DoesNotContain(found, c => c.ProposedKind == ContentKind.Item && c.ProposedName.StartsWith("Fixture Hook", StringComparison.Ordinal) && c.ProposedName != "Fixture Hookblade");
    }

    [Fact]
    public void Armor_rows_with_prices_in_the_name_are_dropped()
    {
        var found = DetectBlocks([RulesFamilies.Srd51],
            new TextBlock("Light Armor", 50, 700, 200, 10, 10, false),
            new TextBlock("Armor  Armor Class (AC)", 50, 715, 300, 10, 10, false),
            new TextBlock("Fixture Quilted Coat  11 + Dex modifier  -  -", 50, 680, 300, 10, 10, false),
            new TextBlock("Fixture Cap 3 sp  11 + Dex modifier  -  -", 50, 660, 300, 10, 10, false));

        Assert.Contains(found, c => c.ProposedName == "Fixture Quilted Coat");
        Assert.DoesNotContain(found, c => c.ProposedName.StartsWith("Fixture Cap", StringComparison.Ordinal));
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
