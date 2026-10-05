using System.Security.Cryptography;
using System.Text.Json;
using TomeStack.AppService.CharacterImport;
using TomeStack.ImportWorker.Forms;
using TomeStack.ImportWorker.Tests;
using TomeStack.RulesCore;
using static TomeStack.AppService.Tests.DdbTestContent;

namespace TomeStack.AppService.Tests;

/// <summary>
/// A sheet's form fields under the 2014 layout, built item by item through <see cref="FixtureSheets.Ddb2014"/>'s real field
/// names. A key the layout lacks (an equipped mark, death saves, spent hit dice or slots) writes nothing, as on a real
/// export. <c>features</c> and <c>feats</c> are newline lists composed into one features text. Every value is invented or an
/// installed name.
/// </summary>
internal sealed class SheetBuilder
{
    private readonly List<FormField> _fields = [];
    private readonly List<string> _features = [];
    private readonly List<string> _feats = [];
    private int _spells;
    private int _items;

    public SheetBuilder(string classLevel, string name = "Testy McFixture")
    {
        Text("name", name);
        Text("classLevel", classLevel);
    }

    public SheetBuilder Text(string field, string value)
    {
        if (field is "features" or "feats")
        {
            var list = field == "features" ? _features : _feats;
            list.Clear();
            list.AddRange(value.Split('\n').Where(v => !string.IsNullOrWhiteSpace(v)));
            return this;
        }
        if (FixtureSheets.Ddb2014(field) is not { } layout)
            return this;
        _fields.RemoveAll(f => f.Name == layout.Name);
        _fields.Add(new(layout.Name, "text", 1, value));
        return this;
    }

    /// <summary>A mark: "P" or empty text for the layout's text marks, a checkbox otherwise (inspiration).</summary>
    public SheetBuilder Box(string field, bool on)
    {
        if (FixtureSheets.Ddb2014(field) is not { } layout)
            return this;
        _fields.RemoveAll(f => f.Name == layout.Name);
        _fields.Add(layout.TextMark ? new(layout.Name, "text", 1, on ? "P" : "") : new(layout.Name, "checkbox", 1, Checked: on, OnState: "Yes"));
        return this;
    }

    public SheetBuilder Skill(string key) => Box($"skills.{key}.proficient", true);

    public SheetBuilder Score(Ability ability, int score) => Text(FieldIds.Key(ability), score.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public SheetBuilder Spell(string name, bool? prepared = null)
    {
        Text($"spells.{_spells}.name", name);
        if (prepared is { } p)
            Box($"spells.{_spells}.prepared", p);
        _spells++;
        return this;
    }

    public SheetBuilder Item(string name, int quantity = 1, bool equipped = false)
    {
        Text($"equipment.{_items}.name", name);
        Text($"equipment.{_items}.quantity", quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Box($"equipment.{_items}.equipped", equipped);
        _items++;
        return this;
    }

    /// <summary>The fields, with the features text and, like a real export, an empty first equipment row when no item was added.</summary>
    public List<FormField> Build()
    {
        List<FormField> fields = [.. _fields, new("FeaturesTraits1", "text", 2, FixtureSheets.FeaturesText(_features, _feats))];
        if (_items == 0)
            fields.Add(new(FixtureSheets.Ddb2014("equipment.0.name")!.Value.Name, "text", 3, ""));
        return fields;
    }
}

/// <summary>An app whose form reader returns whatever sheet the test sets.</summary>
internal sealed class DdbHarness : IDisposable
{
    public DdbHarness() => Temp = new TempApp(formReader: new FakeFormReader(() => Fields));

    public TempApp Temp { get; }

    public List<FormField> Fields { get; set; } = [];

    public Guid Read(SheetBuilder sheet)
    {
        Fields = sheet.Build();
        return Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;
    }

    public DdbPreview Preview(SheetBuilder sheet, string family, IReadOnlyList<Resolution>? resolutions = null, IReadOnlyList<NumberChoice>? numbers = null, bool play = false, Guid? campaign = null, bool equip = false) =>
        Temp.App.PreviewDdbImport(new(Read(sheet), family, campaign, resolutions, numbers, play, EquipMatched: equip));

    /// <summary>The installed name of a revision (so the tests hold ids, not rules text).</summary>
    public string Name(ContentReference reference, string family = RulesFamilies.Srd521) =>
        Temp.App.ListContent(family).Single(o => o.Reference == reference).Name;

    public void Dispose() => Temp.Dispose();
}

/// <summary>
/// Character-sheet import S3 (<c>features/ddb-pdf-import.md</c> "Matching rules", "Commands"): <c>ddb.preview</c> matches
/// what the sheet names against installed published content of the chosen family, places every match where the draft
/// can take it (a class, a choice through the builder's check, a pin, a known spell or equipment), and proposes the
/// character without writing anything. Names are proposals; nothing is merged by name.
/// </summary>
public class DdbImportTests
{
    private static ContentReference Srd(int n) => new(Guid.Parse($"52c00000-0000-4000-8000-{n:D12}"), Guid.Parse($"52e00000-0000-4000-8000-{n:D12}"));

    private static readonly ContentReference Dwarf = Srd(1);
    private static readonly ContentReference Soldier = Srd(2);
    private static readonly ContentReference SavageAttacker = Srd(10);
    private static readonly ContentReference Barbarian = Srd(11);
    private static readonly ContentReference Rage = Srd(12);
    private static readonly ContentReference AthleticsOption = Srd(18);
    private static readonly ContentReference PerceptionOption = Srd(21);
    private static readonly ContentReference SurvivalOption = Srd(22);
    private static readonly ContentReference Berserker = Srd(23);

    private const string Arcanist = "Fixture Arcanist";
    private const string Chanter = "Fixture Chanter";

    private static MatchRow Row(DdbPreview preview, string rowId) => preview.Matches.Single(r => r.RowId == rowId);

    private static ChoiceSelection? Choice(Character character, string choiceId) => character.Choices.SingleOrDefault(c => c.ChoiceId == choiceId);

    // ---- matching ----

    [Fact]
    public void A_name_installed_once_in_the_family_is_Matched_and_shows_its_source_and_family()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("species", h.Name(Dwarf)), RulesFamilies.Srd521);

        var row = Row(preview, "species");
        Assert.Equal((MatchKind.Species, MatchStatus.Matched), (row.Kind, row.Status));
        Assert.Equal(Dwarf.ContentId, row.Chosen!.Reference.ContentId);
        Assert.False(string.IsNullOrWhiteSpace(row.Chosen.SourceTitle));
        Assert.Contains(RulesFamilies.Srd521, row.Chosen.Families);
        Assert.Equal(PlacementKind.Pin, row.Chosen.Placement.Kind);
        Assert.Contains(preview.Character.Pins, p => p.ContentId == Dwarf.ContentId);
    }

    [Fact]
    public void A_name_installed_in_two_sources_is_Choose_and_never_picked_silently()
    {
        using var h = new DdbHarness();
        var first = Publish(h.Temp, Source(h.Temp, "Fixture First Notes", RulesFamilies.Srd521), ContentKind.Feat, "Fixture Twin Feat", [RulesFamilies.Srd521]);
        var second = Publish(h.Temp, Source(h.Temp, "Fixture Second Notes", RulesFamilies.Srd521), ContentKind.Feat, "Fixture Twin Feat", [RulesFamilies.Srd521]);
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Text("feats", "Fixture Twin Feat");

        var open = h.Preview(sheet, RulesFamilies.Srd521);
        var row = Row(open, "feat:0");
        Assert.Equal(MatchStatus.Choose, row.Status);
        Assert.Equal(new[] { first.ContentId, second.ContentId }.Order(), row.Candidates.Select(c => c.Reference.ContentId).Order());
        Assert.Null(row.Chosen);
        Assert.DoesNotContain(open.Character.Pins, p => p == first || p == second);
        Assert.False(open.CanApply);

        var resolved = h.Preview(sheet, RulesFamilies.Srd521, [new("feat:0", second, false)]);
        Assert.Equal(MatchStatus.Matched, Row(resolved, "feat:0").Status);
        Assert.Contains(second, resolved.Character.Pins);
        Assert.Equal(1, resolved.Report.Chosen);
    }

    [Fact]
    public void A_name_installed_only_in_the_other_family_is_NotFound()
    {
        using var h = new DdbHarness();
        Publish(h.Temp, Source(h.Temp, "Fixture Elder Notes", RulesFamilies.Srd51), ContentKind.Feat, "Fixture Elder Feat", [RulesFamilies.Srd51]);

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("feats", "Fixture Elder Feat"), RulesFamilies.Srd521);

        Assert.Equal(MatchStatus.NotFound, Row(preview, "feat:0").Status);
        Assert.Equal(1, preview.Report.NotFound);
    }

    [Fact]
    public void A_matched_subclass_is_recorded_as_the_class_subclass_choice_not_a_pin()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 3 ({h.Name(Berserker)})"), RulesFamilies.Srd521);

        var row = Row(preview, "class:0:subclass");
        Assert.Equal((MatchKind.Subclass, MatchStatus.Matched), (row.Kind, row.Status));
        Assert.Equal(PlacementKind.Choice, row.Chosen!.Placement.Kind);
        Assert.Equal([Berserker.ContentId], Choice(preview.Character, "barbarian-subclass")!.Selected.Select(s => s.ContentId));
        Assert.DoesNotContain(preview.Character.Pins, p => p.ContentId == Berserker.ContentId);
        Assert.Equal([(Barbarian.ContentId, 3)], preview.Character.Classes.Select(c => (c.Class.ContentId, c.Level)));
    }

    [Fact]
    public void A_homebrew_subclass_that_extends_the_SRD_class_choice_is_a_candidate()
    {
        using var h = new DdbHarness();
        var path = Publish(h.Temp, Source(h.Temp, "Fixture Path Notes", RulesFamilies.Srd521), ContentKind.Subclass, "Fixture Path of Tests", [RulesFamilies.Srd521],
            extendsChoice: new(Barbarian.ContentId, "barbarian-subclass"));

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 3 (Fixture Path of Tests)"), RulesFamilies.Srd521);

        Assert.Equal(MatchStatus.Matched, Row(preview, "class:0:subclass").Status);
        Assert.Equal([path], Choice(preview.Character, "barbarian-subclass")!.Selected);
    }

    [Fact]
    public void A_subclass_row_is_not_offered_for_a_class_below_its_choice_level()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 2 ({h.Name(Berserker)})"), RulesFamilies.Srd521);

        var row = Row(preview, "class:0:subclass");
        Assert.Equal(MatchStatus.NoPlace, row.Status);
        Assert.Equal("choice.not-offered", row.Note);
        Assert.Null(Choice(preview.Character, "barbarian-subclass"));
    }

    // ---- D16f: a 2014 sheet names no subclass ----

    [Fact]
    public void A_2014_sheet_with_no_subclass_offers_the_class_choice_options_and_blocks_create_until_one_is_picked_or_left_out()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 3"), RulesFamilies.Srd521);

        var row = Row(preview, "class:0:subclass");
        Assert.Equal((MatchKind.Subclass, MatchStatus.Choose), (row.Kind, row.Status));
        Assert.Equal($"{h.Name(Barbarian)} subclass (not on the sheet)", row.Label);
        Assert.Contains(row.Candidates, c => c.Reference.ContentId == Berserker.ContentId && c.Placement.Kind == PlacementKind.Choice);
        Assert.Null(row.Note);
        Assert.False(preview.CanApply);
    }

    [Fact]
    public void A_subclass_whose_granted_feature_is_on_the_sheet_is_detected_and_recorded_as_the_choice()
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Path Notes", RulesFamilies.Srd521);
        var sense = Publish(h.Temp, source, ContentKind.Feature, "Fixture Storm Sense", [RulesFamilies.Srd521]);
        var path = Publish(h.Temp, source, ContentKind.Subclass, "Fixture Path of Storms", [RulesFamilies.Srd521],
            effects: [new GrantEffect { Id = "grant-sense", Grant = GrantKind.Content, Content = sense, Level = 3 }],
            extendsChoice: new(Barbarian.ContentId, "barbarian-subclass"));

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 3").Text("features", $"Fixture Storm Sense\n{h.Name(Rage)}"), RulesFamilies.Srd521);

        var row = Row(preview, "class:0:subclass");
        Assert.Equal((MatchStatus.Matched, "subclass.detected-from-features"), (row.Status, row.Note));
        Assert.Equal(path, row.Chosen!.Reference);
        Assert.Equal(path, Assert.Single(row.Candidates.Take(1)).Reference); // detected first
        Assert.Equal([path], Choice(preview.Character, "barbarian-subclass")!.Selected);
        // The subclass's feature is now on the draft, so its sheet row matches instead of becoming a gap note.
        Assert.Equal(MatchStatus.Matched, preview.Matches.Single(r => r.Kind == MatchKind.Feature && r.Label == "Fixture Storm Sense").Status);
    }

    [Fact]
    public void Two_subclasses_whose_granted_features_are_both_on_the_sheet_need_a_choice_with_both_listed_first()
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Twin Notes", RulesFamilies.Srd521);
        var shared = Publish(h.Temp, source, ContentKind.Feature, "Fixture Twin Gift", [RulesFamilies.Srd521]);
        var embers = Publish(h.Temp, source, ContentKind.Subclass, "Fixture Path of Embers", [RulesFamilies.Srd521],
            effects: [new GrantEffect { Id = "grant-embers", Grant = GrantKind.Content, Content = shared, Level = 3 }],
            extendsChoice: new(Barbarian.ContentId, "barbarian-subclass"));
        var ash = Publish(h.Temp, source, ContentKind.Subclass, "Fixture Path of Ash", [RulesFamilies.Srd521],
            effects: [new GrantEffect { Id = "grant-ash", Grant = GrantKind.Content, Content = shared, Level = 3 }],
            extendsChoice: new(Barbarian.ContentId, "barbarian-subclass"));

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 3").Text("features", "Fixture Twin Gift"), RulesFamilies.Srd521);

        var row = Row(preview, "class:0:subclass");
        Assert.Equal(MatchStatus.Choose, row.Status); // never a silent pick between two detections
        Assert.Null(row.Chosen);
        Assert.Equal(
            new[] { embers, ash }.Select(r => r.ContentId).Order(),
            row.Candidates.Take(2).Select(c => c.Reference.ContentId).Order());
        Assert.False(preview.CanApply);
    }

    [Fact]
    public void A_user_pick_for_the_offered_subclass_row_is_recorded_and_counted_as_chosen()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 3"), RulesFamilies.Srd521,
            resolutions: [new Resolution("class:0:subclass", Berserker, LeaveOut: false)]);

        var row = Row(preview, "class:0:subclass");
        Assert.Equal(MatchStatus.Matched, row.Status);
        Assert.Equal([Berserker.ContentId], Choice(preview.Character, "barbarian-subclass")!.Selected.Select(s => s.ContentId));
        Assert.Equal(1, preview.Report.Chosen);
        Assert.True(preview.CanApply);
    }

    [Fact]
    public void No_subclass_row_is_offered_when_the_sheet_names_none_and_the_class_is_below_its_choice_level_or_unmatched()
    {
        using var h = new DdbHarness();
        var below = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 2"), RulesFamilies.Srd521);
        Assert.DoesNotContain(below.Matches, r => r.RowId == "class:0:subclass");
        Assert.True(below.CanApply);

        var unmatched = h.Preview(new SheetBuilder("Fixture Nobody 5"), RulesFamilies.Srd521);
        Assert.DoesNotContain(unmatched.Matches, r => r.RowId == "class:0:subclass");
    }

    [Fact]
    public void A_proficient_skill_is_matched_by_the_options_grant_target_and_recorded_on_the_first_choice_that_offers_it()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Skill("perception").Skill("survival"), RulesFamilies.Srd521);

        var row = Row(preview, "skill:perception");
        Assert.Equal((MatchKind.Skill, MatchStatus.Matched, PlacementKind.Choice), (row.Kind, row.Status, row.Chosen!.Placement.Kind));
        Assert.Equal(PerceptionOption.ContentId, row.Chosen.Reference.ContentId);
        Assert.Equal(new[] { PerceptionOption.ContentId, SurvivalOption.ContentId }.Order(), Choice(preview.Character, "barbarian-skills")!.Selected.Select(s => s.ContentId).Order());
    }

    [Fact]
    public void A_skill_both_the_class_and_the_background_offer_is_recorded_once()
    {
        using var h = new DdbHarness();
        // A homebrew background whose own choice offers the Barbarian's Athletics option too.
        var source = Source(h.Temp, "Fixture Wanderer Notes", RulesFamilies.Srd521);
        var wanderer = Publish(h.Temp, source, ContentKind.Background, "Fixture Wanderer", [RulesFamilies.Srd521],
            [new ChoiceEffect { Id = "fixture-skill", ChoiceId = "fixture-skill", Count = 1, Options = [AthleticsOption] }]);

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("background", "Fixture Wanderer").Skill("athletics"), RulesFamilies.Srd521);

        Assert.Equal(MatchStatus.Matched, Row(preview, "skill:athletics").Status);
        Assert.Single(preview.Character.Choices.SelectMany(c => c.Selected), s => s.ContentId == AthleticsOption.ContentId);
        Assert.Contains(wanderer, preview.Character.Pins);
    }

    /// <summary>A homebrew skill option: one effect, the skill's proficiency, as the SRD's are.</summary>
    private static ContentReference SkillOption(TempApp temp, Guid source, string name, string skill) =>
        Publish(temp, source, ContentKind.Feature, name, [RulesFamilies.Srd521], [new GrantEffect { Id = "skill", Grant = GrantKind.Proficiency, Target = FieldIds.Skill(skill) }]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Skills_are_placed_together_so_a_wide_choice_never_takes_the_only_skill_a_narrow_choice_offers(bool wideOnSpecies)
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Lore Notes", RulesFamilies.Srd521);
        var arcana = SkillOption(h.Temp, source, "Fixture Arcana Lesson", "arcana");
        var history = SkillOption(h.Temp, source, "Fixture History Lesson", "history");
        ChoiceEffect Wide() => new() { Id = "fixture-wide", ChoiceId = "fixture-wide", Count = 1, Options = [arcana, history] };
        ChoiceEffect Narrow() => new() { Id = "fixture-narrow", ChoiceId = "fixture-narrow", Count = 1, Options = [arcana] };
        Publish(h.Temp, source, ContentKind.Species, "Fixture Lorefolk", [RulesFamilies.Srd521], [wideOnSpecies ? Wide() : Narrow()]);
        Publish(h.Temp, source, ContentKind.Background, "Fixture Scholar", [RulesFamilies.Srd521], [wideOnSpecies ? Narrow() : Wide()]);

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("species", "Fixture Lorefolk").Text("background", "Fixture Scholar").Skill("arcana").Skill("history"), RulesFamilies.Srd521);

        Assert.Equal((MatchStatus.Matched, MatchStatus.Matched), (Row(preview, "skill:arcana").Status, Row(preview, "skill:history").Status));
        var chosen = preview.Character.Choices.ToDictionary(c => c.ChoiceId, c => Assert.Single(c.Selected).ContentId);
        Assert.Equal((arcana.ContentId, history.ContentId), (chosen["fixture-narrow"], chosen["fixture-wide"]));
    }

    [Fact]
    public void A_skill_is_never_placed_by_selecting_an_option_that_is_more_than_that_skill()
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Talent Notes", RulesFamilies.Srd521);
        // A homebrew feat that grants Arcana and something else, offered by a background choice.
        var feat = Publish(h.Temp, source, ContentKind.Feat, "Fixture Arcane Dabbler", [RulesFamilies.Srd521],
            [new GrantEffect { Id = "skill", Grant = GrantKind.Proficiency, Target = FieldIds.Skill("arcana") }, Modifier("bonus", FieldIds.Initiative, ModifierOperation.Bonus, 1)]);
        Publish(h.Temp, source, ContentKind.Background, "Fixture Dabbler", [RulesFamilies.Srd521], [new ChoiceEffect { Id = "fixture-talent", ChoiceId = "fixture-talent", Count = 1, Options = [feat] }]);

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("background", "Fixture Dabbler").Skill("arcana"), RulesFamilies.Srd521);

        Assert.Equal((MatchStatus.NoPlace, "skill.no-open-choice"), (Row(preview, "skill:arcana").Status, Row(preview, "skill:arcana").Note));
        Assert.DoesNotContain(preview.Character.Choices.SelectMany(c => c.Selected), s => s.ContentId == feat.ContentId);
    }

    [Fact]
    public void A_resolution_that_no_longer_resolves_asks_again_instead_of_falling_back_to_the_name_match()
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Revision Notes", RulesFamilies.Srd521);
        Publish(h.Temp, source, ContentKind.Feat, "Fixture Alpha Feat", [RulesFamilies.Srd521]);
        var beta = Publish(h.Temp, source, ContentKind.Feat, "Fixture Beta Feat", [RulesFamilies.Srd521]);
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Text("feats", "Fixture Alpha Feat");
        Assert.Equal(MatchStatus.Matched, Row(h.Preview(sheet, RulesFamilies.Srd521, [new("feat:0", beta, false)]), "feat:0").Status);

        // A newer revision of the picked feat is published between two previews: the pick names a superseded revision.
        h.Temp.App.Publish(h.Temp.App.SaveDraft(h.Temp.App.Store.FindRevision(beta)! with { RevisionId = Guid.Empty, Status = RevisionStatus.Draft, Summary = "Fixture content, second revision." }));
        var stale = h.Preview(sheet, RulesFamilies.Srd521, [new("feat:0", beta, false)]);

        var row = Row(stale, "feat:0");
        Assert.Equal((MatchStatus.Choose, "resolution.not-found"), (row.Status, row.Note));
        Assert.False(stale.CanApply);
    }

    [Fact]
    public void Play_state_fields_that_cannot_be_read_are_reported_when_play_state_is_asked_for()
    {
        // The 2014 layout has no spent-hit-dice fields (S0); the placeholder 2024 layout still names them.
        using var h = new DdbHarness();
        DdbPreview Preview(bool play)
        {
            h.Fields = [new("fixture.2024.name", "text", 1, "Testy McFixture"), new("fixture.2024.classLevel", "text", 1, $"{h.Name(Barbarian)} 1"), new("fixture.2024.hitDiceSpent.d12", "text", 1, "Fixture")];
            return h.Temp.App.PreviewDdbImport(new(h.Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token, RulesFamilies.Srd521, null, null, null, play));
        }

        Assert.DoesNotContain(Preview(play: false).Diagnostics, d => d.Code == "ddb.play-unreadable");
        var asked = Preview(play: true);
        Assert.Contains(asked.Diagnostics, d => d.Code == "ddb.play-unreadable" && !d.Message.Contains("Fixture", StringComparison.Ordinal));
    }

    [Fact]
    public void A_skill_a_grant_already_gives_is_Matched_without_a_choice()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("background", h.Name(Soldier)).Skill("athletics"), RulesFamilies.Srd521);

        var row = Row(preview, "skill:athletics");
        Assert.Equal((MatchStatus.Matched, PlacementKind.None), (row.Status, row.Chosen!.Placement.Kind));
        Assert.DoesNotContain(preview.Character.Choices.SelectMany(c => c.Selected), s => s.ContentId == AthleticsOption.ContentId);
    }

    [Fact]
    public void A_2024_general_feat_with_no_open_choice_is_NoPlace_and_listed()
    {
        using var h = new DdbHarness();
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Text("feats", h.Name(SavageAttacker));

        var alone = h.Preview(sheet, RulesFamilies.Srd521);
        Assert.Equal(MatchStatus.NoPlace, Row(alone, "feat:0").Status);
        Assert.Equal(1, alone.Report.NoPlace);
        Assert.DoesNotContain(alone.Character.Pins, p => p.ContentId == SavageAttacker.ContentId);

        // With the background that grants it, it is already there: matched, and not pinned again.
        var granted = h.Preview(sheet.Text("background", h.Name(Soldier)), RulesFamilies.Srd521);
        Assert.Equal((MatchStatus.Matched, PlacementKind.None), (Row(granted, "feat:0").Status, Row(granted, "feat:0").Chosen!.Placement.Kind));
        Assert.DoesNotContain(granted.Character.Pins, p => p.ContentId == SavageAttacker.ContentId);
    }

    [Fact]
    public void Classes_keep_the_sheets_order_and_the_first_is_the_starting_class()
    {
        using var h = new DdbHarness();
        var arcanist = h.Temp.App.ListContent(RulesFamilies.Srd521).Single(o => o.Name == Arcanist && !o.Superseded).Reference;

        var preview = h.Preview(new SheetBuilder($"{Arcanist} 2 / {h.Name(Barbarian)} 1"), RulesFamilies.Srd521);

        Assert.Equal([(arcanist.ContentId, 2), (Barbarian.ContentId, 1)], preview.Character.Classes.Select(c => (c.Class.ContentId, c.Level)));
        Assert.Equal(["class:0", "class:1"], preview.Matches.Where(r => r.Kind == MatchKind.Class).Select(r => r.RowId));
    }

    [Fact]
    public void A_spell_on_one_casters_list_goes_to_that_caster_and_one_on_two_lists_asks()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{Arcanist} 3 / {Chanter} 2").Spell("Fixture Frost Ring").Spell("Fixture Veil").Spell("Fixture Mending Word"), RulesFamilies.Srd521);

        var casters = preview.Character.Classes.ToDictionary(c => h.Name(c.Class), c => c.Class.ContentId);
        Assert.Equal(casters[Arcanist], Row(preview, "spell:0").Chosen!.Placement.Caster);
        Assert.Equal(casters[Chanter], Row(preview, "spell:2").Chosen!.Placement.Caster);
        var both = Row(preview, "spell:1");
        Assert.Equal(MatchStatus.Choose, both.Status);
        Assert.Equal(new[] { casters[Arcanist], casters[Chanter] }.Order(), both.Candidates.Select(c => c.Placement.Caster!.Value).Order());
        Assert.Equal(2, preview.Character.Spells.Count);
    }

    [Fact]
    public void A_spell_resolved_to_a_caster_that_is_not_a_caster_of_the_draft_is_NoPlace_not_an_error()
    {
        using var h = new DdbHarness();
        var sheet = new SheetBuilder($"{Arcanist} 3 / {Chanter} 2").Spell("Fixture Veil");
        var open = h.Preview(sheet, RulesFamilies.Srd521);
        var chanter = Row(open, "spell:0").Candidates.Single(c => c.Placement.Caster == open.Character.Classes[1].Class.ContentId);

        // The user picks the Chanter for the spell, then leaves the Chanter class out: the pick is stale.
        var stale = h.Preview(sheet, RulesFamilies.Srd521, [new("spell:0", chanter.Reference, false, chanter.Placement.Caster), new("class:1", null, true)]);
        Assert.Equal((MatchStatus.NoPlace, "spell.caster-not-found"), (Row(stale, "spell:0").Status, Row(stale, "spell:0").Note));
        Assert.Empty(stale.Character.Spells);

        // A caster id that was never on the sheet is treated the same way.
        var crafted = h.Preview(sheet, RulesFamilies.Srd521, [new("spell:0", chanter.Reference, false, Guid.NewGuid())]);
        Assert.Equal((MatchStatus.NoPlace, "spell.caster-not-found"), (Row(crafted, "spell:0").Status, Row(crafted, "spell:0").Note));
    }

    [Fact]
    public void An_unreadable_row_has_a_label_that_names_its_kind_and_no_value()
    {
        using var h = new DdbHarness();
        // More feats than a list keeps: the rest is one unreadable row.
        var feats = string.Join("\n", Enumerable.Range(0, DdbParser.MaxListItems + 3).Select(i => $"Fixture Many Feat {i:D4}"));

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("feats", feats), RulesFamilies.Srd521);

        var row = Row(preview, $"feat:{DdbParser.MaxListItems}");
        Assert.Equal((MatchStatus.Unreadable, "Unreadable feat"), (row.Status, row.Label));
        Assert.Equal(DdbParser.MaxListItems + 1, preview.Matches.Count(r => r.Kind == MatchKind.Feat));
    }

    [Fact]
    public void A_spell_on_no_casters_list_asks_with_every_caster()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{Arcanist} 3").Spell("Fixture Mending Word"), RulesFamilies.Srd521);

        var row = Row(preview, "spell:0");
        Assert.Equal(MatchStatus.Choose, row.Status);
        Assert.Equal(preview.Character.Classes[0].Class.ContentId, Assert.Single(row.Candidates).Placement.Caster);

        var resolved = h.Preview(new SheetBuilder($"{Arcanist} 3").Spell("Fixture Mending Word"), RulesFamilies.Srd521,
            [new("spell:0", row.Candidates[0].Reference, false, row.Candidates[0].Placement.Caster)]);
        Assert.Single(resolved.Character.Spells);
    }

    [Fact]
    public void A_spell_with_no_caster_at_all_is_NoPlace()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Spell("Fixture Frost Ring"), RulesFamilies.Srd521);
        Assert.Equal(MatchStatus.NoPlace, Row(preview, "spell:0").Status);
    }

    [Fact]
    public void A_cantrip_is_always_prepared()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{Arcanist} 3").Spell("Fixture Spark", prepared: false).Spell("Fixture Frost Ring", prepared: false), RulesFamilies.Srd521);

        Assert.Equal([true, false], preview.Character.Spells.Select(s => s.Prepared));
    }

    [Fact]
    public void The_same_spell_twice_for_one_caster_is_recorded_once_and_under_two_casters_twice()
    {
        using var h = new DdbHarness();
        var once = h.Preview(new SheetBuilder($"{Arcanist} 3").Spell("Fixture Frost Ring").Spell("Fixture Frost Ring"), RulesFamilies.Srd521);
        Assert.Single(once.Character.Spells);
        // The repeat adds nothing, so it is not counted as a match; it is left out, with the reason.
        Assert.Equal((MatchStatus.LeftOut, "spell.duplicate"), (Row(once, "spell:1").Status, Row(once, "spell:1").Note));
        Assert.Equal(1, once.Report.LeftOut);

        var sheet = new SheetBuilder($"{Arcanist} 3 / {Chanter} 2").Spell("Fixture Veil").Spell("Fixture Veil");
        var choose = h.Preview(sheet, RulesFamilies.Srd521);
        var veil = Row(choose, "spell:0").Candidates;
        var arcanist = veil.Single(c => c.Placement.Caster == choose.Character.Classes[0].Class.ContentId);
        var chanter = veil.Single(c => c.Placement.Caster == choose.Character.Classes[1].Class.ContentId);
        var twice = h.Preview(sheet, RulesFamilies.Srd521,
            [new("spell:0", arcanist.Reference, false, arcanist.Placement.Caster), new("spell:1", chanter.Reference, false, chanter.Placement.Caster)]);
        Assert.Equal(2, twice.Character.Spells.Count);
        Assert.Equal(2, twice.Character.Spells.Select(s => s.Caster).Distinct().Count());
    }

    [Fact]
    public void More_than_MaxSpells_spells_record_the_first_five_hundred_and_list_the_rest()
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Spellbook Notes", RulesFamilies.Srd521);
        // 501 distinct original cantrips on the Arcanist's list, stored in one transaction (publishing each would be slow).
        var names = Enumerable.Range(0, RulesCore.Character.MaxSpells + 1).Select(i => $"Fixture Cantrip {i:D3}").ToList();
        h.Temp.App.Store.InTransaction(() =>
        {
            foreach (var name in names)
            {
                h.Temp.App.Store.AddRevision(new ContentRevision
                {
                    ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Spell, Name = name, RulesFamilies = [RulesFamilies.Srd521],
                    Provenance = new(source), Status = RevisionStatus.Published, Effects = [new SpellEffect { Id = "spell", Level = 0, Lists = ["fixture-arcane"] }],
                });
            }
        });
        var sheet = new SheetBuilder($"{Arcanist} 3");
        foreach (var name in names)
            sheet.Spell(name);

        var preview = h.Preview(sheet, RulesFamilies.Srd521);

        Assert.Equal(RulesCore.Character.MaxSpells, preview.Character.Spells.Count);
        var last = Row(preview, $"spell:{RulesCore.Character.MaxSpells}");
        Assert.Equal((MatchStatus.NoPlace, "character.spells-too-many"), (last.Status, last.Note));
    }

    [Fact]
    public void Two_rows_of_the_same_item_merge_into_one_entry_with_the_summed_quantity_capped()
    {
        using var h = new DdbHarness();
        var item = Publish(h.Temp, Source(h.Temp, "Fixture Gear Notes", RulesFamilies.Srd521), ContentKind.Item, "Fixture Rope Coil", [RulesFamilies.Srd521]);

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Item("Fixture Rope Coil", 6_000).Item("fixture rope coil", 5_000, equipped: true), RulesFamilies.Srd521);

        // The 2014 layout has no equipped mark (S0), so the asked-for mark is not on the sheet and the entry is not equipped.
        Assert.Equal([new EquipmentEntry(item, Equipped: false, Quantity: EquipmentEntry.MaxQuantity)], preview.Character.Equipment);
        Assert.All(preview.Matches.Where(r => r.Kind == MatchKind.Item), r => Assert.Equal(PlacementKind.Equipment, r.Chosen!.Placement.Kind));
    }

    [Fact]
    public void A_feature_the_sheet_has_but_the_calculated_sheet_lacks_is_NotFound_and_a_granted_one_is_not_pinned()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("features", $"{h.Name(Rage)}\nFixture Missing Feature"), RulesFamilies.Srd521);

        Assert.Equal((MatchStatus.Matched, PlacementKind.None), (Row(preview, "feature:0").Status, Row(preview, "feature:0").Chosen!.Placement.Kind));
        Assert.Equal(MatchStatus.NotFound, Row(preview, "feature:1").Status);
        Assert.DoesNotContain(preview.Character.Pins, p => p.ContentId == Rage.ContentId);
    }

    [Fact]
    public void Normalisation_folds_case_whitespace_quotes_and_dashes_and_nothing_else()
    {
        Assert.Equal("fixture o'brien-style feat", Normalise.Name("  Fixture  O’Brien–Style\tFEAT "));
        Assert.Equal("\"fixture\" - x", Normalise.Name("“Fixture” — X"));

        using var h = new DdbHarness();
        var feat = Publish(h.Temp, Source(h.Temp, "Fixture Quote Notes", RulesFamilies.Srd521), ContentKind.Feat, "Fixture O’Brien–Style Feat", [RulesFamilies.Srd521]);

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("feats", "fixture  o'brien-style feat\nFixture OBrien-Style Feat"), RulesFamilies.Srd521);

        Assert.Equal(feat, Row(preview, "feat:0").Chosen!.Reference);
        Assert.Equal(MatchStatus.NotFound, Row(preview, "feat:1").Status);
    }

    [Fact]
    public void A_campaign_that_does_not_allow_the_option_source_still_lists_it_with_a_warning()
    {
        using var h = new DdbHarness();
        var feat = Publish(h.Temp, Source(h.Temp, "Fixture Table Notes", RulesFamilies.Srd521), ContentKind.Feat, "Fixture Table Feat", [RulesFamilies.Srd521]);
        var campaign = h.Temp.App.SaveCampaign(new() { Id = Guid.Empty, Name = "Fixture Table", RulesFamily = RulesFamilies.Srd521, AllowedSources = [Guid.Parse("52500000-0000-4000-8000-000000000001")], HouseRules = "Fixture notes." });

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("feats", "Fixture Table Feat"), RulesFamilies.Srd521, campaign: campaign.Id);

        var row = Row(preview, "feat:0");
        Assert.Equal((MatchStatus.Matched, "campaign.source-not-allowed"), (row.Status, row.Note));
        Assert.False(row.Chosen!.AllowedInCampaign);
        Assert.Contains(feat, preview.Character.Pins);
        Assert.Equal(campaign.Id, preview.Character.CampaignId);
        Assert.Empty(preview.Character.CampaignExceptions);
    }

    [Fact]
    public void A_resolution_may_pick_another_installed_option_of_the_kind_but_not_of_another_kind()
    {
        using var h = new DdbHarness();
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Text("species", "Fixture Unknown Folk");
        var dwarf = h.Temp.App.ListContent(RulesFamilies.Srd521).Single(o => o.Reference.ContentId == Dwarf.ContentId && !o.Superseded).Reference;

        Assert.Equal(MatchStatus.NotFound, Row(h.Preview(sheet, RulesFamilies.Srd521), "species").Status);
        var picked = h.Preview(sheet, RulesFamilies.Srd521, [new("species", dwarf, false)]);
        Assert.Equal(MatchStatus.Matched, Row(picked, "species").Status);
        Assert.Contains(dwarf, picked.Character.Pins);

        var wrongKind = h.Preview(sheet, RulesFamilies.Srd521, [new("species", h.Temp.App.ListContent(RulesFamilies.Srd521).First(o => o.Kind == ContentKind.Class && !o.Superseded).Reference, false)]);
        // A pick of another kind cannot be honoured, so the row asks again (never a silent fallback).
        Assert.Equal((MatchStatus.Choose, "resolution.not-found"), (Row(wrongKind, "species").Status, Row(wrongKind, "species").Note));
    }

    [Fact]
    public void A_row_left_out_is_not_applied_and_is_counted()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1").Text("species", h.Name(Dwarf)), RulesFamilies.Srd521, [new("species", null, true)]);

        Assert.Equal(MatchStatus.LeftOut, Row(preview, "species").Status);
        Assert.DoesNotContain(preview.Character.Pins, p => p.ContentId == Dwarf.ContentId);
        Assert.Equal(1, preview.Report.LeftOut);
    }

    // ---- classes, numbers, report ----

    [Fact]
    public void A_refused_answer_does_not_keep_a_choice_the_sheet_filled_listed_as_if_answered()
    {
        using var h = new DdbHarness();
        // The sheet fills the Barbarian's skill choice; the answer names an option the choice does not offer.
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Skill("perception").Skill("survival");
        var filled = h.Preview(sheet, RulesFamilies.Srd521).Character.Choices.Single(c => c.ChoiceId == "barbarian-skills");
        var refused = new ChoiceSelection(filled.Source, filled.ChoiceId, [Dwarf]);

        var preview = h.Temp.App.PreviewDdbImport(new(h.Read(sheet), RulesFamilies.Srd521, null, null, null, false, [refused]));

        Assert.NotEmpty(preview.Diagnostics);
        Assert.DoesNotContain(preview.OpenChoices, c => c.ChoiceId == "barbarian-skills");
        Assert.Equal(filled.Selected, preview.Character.Choices.Single(c => c.ChoiceId == "barbarian-skills").Selected);
    }

    [Fact]
    public void An_open_choice_the_user_answered_stays_listed_so_the_answer_can_be_changed()
    {
        using var h = new DdbHarness();
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Text("background", h.Name(Soldier));
        var open = h.Preview(sheet, RulesFamilies.Srd521);
        var choice = open.OpenChoices.First(c => c.Source.ContentId == Soldier.ContentId);
        var answer = new ChoiceSelection(choice.Source, choice.ChoiceId, [.. choice.Options.Take(choice.Count)]);

        var answered = h.Temp.App.PreviewDdbImport(new(h.Read(sheet), RulesFamilies.Srd521, null, null, null, false, [answer]));

        var kept = Assert.Single(answered.OpenChoices, c => c.Source == choice.Source && c.ChoiceId == choice.ChoiceId);
        Assert.True(kept.Resolved);
        Assert.Equal(answer.Selected, kept.Selected);
    }

    [Fact]
    public void Unreadable_class_levels_give_no_classes_and_the_preview_cannot_apply()
    {
        using var h = new DdbHarness();
        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 25"), RulesFamilies.Srd521);

        Assert.Equal(MatchStatus.Unreadable, Row(preview, "classes").Status);
        Assert.Empty(preview.Character.Classes);
        Assert.False(preview.CanApply);
    }

    [Fact]
    public void A_clean_sheet_can_apply_and_its_base_scores_come_from_the_back_solve()
    {
        using var h = new DdbHarness();
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Score(Ability.Str, 16).Score(Ability.Dex, 14).Score(Ability.Con, 15)
            .Score(Ability.Int, 8).Score(Ability.Wis, 12).Score(Ability.Cha, 10);

        var preview = h.Preview(sheet, RulesFamilies.Srd521);

        Assert.True(preview.CanApply, string.Join("; ", preview.Diagnostics.Select(d => d.Code)));
        Assert.Equal(new AbilityScores(16, 14, 15, 8, 12, 10), preview.Character.BaseAbilities);
        Assert.Equal(preview.AbilityPlan.ProposedBase, preview.Character.BaseAbilities);
        Assert.All(preview.Comparison.Where(n => n.Field.StartsWith("ability.", StringComparison.Ordinal)), n => Assert.False(n.Differs));
    }

    [Fact]
    public void Differences_come_first_and_keeping_the_sheets_number_adds_an_override_with_the_import_reason()
    {
        using var h = new DdbHarness();
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Score(Ability.Dex, 14).Text("armorClass", "19").Text("initiative", "+2");

        var open = h.Preview(sheet, RulesFamilies.Srd521);
        Assert.Equal(FieldIds.ArmorClass, open.Comparison[0].Field);
        Assert.True(open.Comparison[0].Differs);
        Assert.Equal(19, open.Comparison[0].Sheet);
        Assert.False(open.Comparison.Single(n => n.Field == FieldIds.Initiative).Differs);
        Assert.Empty(open.Character.Overrides);

        var kept = h.Preview(sheet, RulesFamilies.Srd521, numbers: [new(FieldIds.ArmorClass, NumberAction.KeepSheet)]);
        Assert.Equal([new FieldOverride(FieldIds.ArmorClass, 19, "Imported from D&D Beyond")], kept.Character.Overrides);
    }

    [Fact]
    public void Play_state_comes_over_only_when_asked()
    {
        using var h = new DdbHarness();
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Text("currentHitPoints", "7").Text("temporaryHitPoints", "3").Text("deathFailures", "1")
            .Box("inspiration", true).Text("hitDiceSpent.d12", "1");

        var rested = h.Preview(sheet, RulesFamilies.Srd521).Character.Play;
        Assert.Equal((null, 0, 0, false, 0), (rested.CurrentHitPoints, rested.TemporaryHitPoints, rested.DeathSaves.Failures, rested.Inspiration, rested.HitDiceSpentOf(12)));
        var report = h.Preview(sheet, RulesFamilies.Srd521).Report;
        Assert.Contains("playState", report.NotBroughtOver);

        // The 2014 layout has no death-save or spent-hit-dice fields (S0): those stay rested even when play state is asked for.
        var play = h.Preview(sheet, RulesFamilies.Srd521, play: true).Character.Play;
        Assert.Equal((7, 3, 0, true, 0),(play.CurrentHitPoints, play.TemporaryHitPoints, play.DeathSaves.Failures, play.Inspiration, play.HitDiceSpentOf(12)));
    }

    [Fact]
    public void The_report_lists_what_is_not_brought_over_a_same_name_and_a_family_mismatch()
    {
        using var h = new DdbHarness();
        h.Temp.App.CreateCharacter(new("Testy McFixture", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null));

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 1"), RulesFamilies.Srd521);

        Assert.True(preview.Report.SameNameExists);
        Assert.True(preview.Report.FamilyMismatch); // the 2014 layout suggests srd-5.1
        Assert.Contains("currency", preview.Report.NotBroughtOver);
        Assert.Contains("speed", preview.Report.NotBroughtOver);
        Assert.NotEqual(Guid.Empty, preview.Character.Id);
        Assert.Equal("Testy McFixture", preview.Character.Name);
    }

    [Fact]
    public void A_preview_with_an_unknown_token_or_family_is_refused()
    {
        using var h = new DdbHarness();
        Assert.Equal("ddb.token-invalid", Assert.Throws<AppValidationException>(() => h.Temp.App.PreviewDdbImport(new(Guid.NewGuid(), RulesFamilies.Srd521, null, null, null, false))).Code);
        var token = h.Read(new SheetBuilder($"{h.Name(Barbarian)} 1"));
        Assert.Equal("rules-family.unknown", Assert.Throws<AppValidationException>(() => h.Temp.App.PreviewDdbImport(new(token, "fixture-family", null, null, null, false))).Problems.Single().Code);
    }

    // ---- writes nothing, quotes nothing ----

    [Fact]
    public void A_preview_writes_nothing()
    {
        using var h = new DdbHarness();
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 3 ({h.Name(Berserker)})").Text("species", h.Name(Dwarf)).Skill("perception").Spell("Fixture Spark").Item("Fixture Rope Coil");
        var token = h.Read(sheet);
        string Digest() => string.Join(",", Directory.EnumerateFiles(h.Temp.Directory, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && Path.GetFileName(f) != "tomestack.lock")
            .Order(StringComparer.Ordinal)
            .Select(f => $"{Path.GetRelativePath(h.Temp.Directory, f)}:{Convert.ToHexString(SHA256.HashData(ReadShared(f)))}"));
        var before = Digest();

        h.Temp.App.PreviewDdbImport(new(token, RulesFamilies.Srd521, null, null, null, true));
        h.Temp.App.PreviewDdbImport(new(token, RulesFamilies.Srd51, null, null, null, false));

        Assert.Equal(before, Digest());
        Assert.Empty(h.Temp.App.ListCharacters());
        Assert.NotNull(h.Temp.App.DdbSessions.Peek(token)); // a preview does not spend the token
    }

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void No_error_message_and_no_diagnostic_from_any_ddb_command_quotes_a_field_value()
    {
        using var h = new DdbHarness();
        var sentinels = Enumerable.Range(0, 8).Select(i => $"FixtureSentinel{i}").ToList();
        var dispatcher = new CommandDispatcher(h.Temp.App);
        var strings = new List<string>();
        void Collect(JsonElement element, string? property = null)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in element.EnumerateObject())
                        Collect(p.Value, p.Name);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        Collect(item, property);
                    break;
                case JsonValueKind.String when property is "message" or "note":
                    strings.Add(element.GetString()!);
                    break;
            }
        }
        JsonElement Send(string command, object payload)
        {
            var response = JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;
            Collect(response);
            return response;
        }

        // Every value is a sentinel the parser or the matcher cannot use.
        h.Fields = new SheetBuilder($"{sentinels[0]} 3 ({sentinels[1]})", name: sentinels[2]).Text("species", sentinels[3]).Text("str", sentinels[4])
            .Text("feats", sentinels[5]).Spell(sentinels[6]).Item(sentinels[7]).Build();
        var read = Send("ddb.readData", new { fileName = "fixture.pdf", data = Convert.ToBase64String("%PDF-1.7 fixture"u8.ToArray()) });
        var token = read.GetProperty("result").GetProperty("token").GetGuid();
        Send("ddb.preview", new { token, rulesFamily = RulesFamilies.Srd521, includePlayState = true });
        Send("ddb.preview", new { token = Guid.NewGuid(), rulesFamily = RulesFamilies.Srd521 });
        Send("ddb.discard", new { token });
        h.Fields = [new FormField("fixture.other", "text", 1, sentinels[0])];
        Send("ddb.readData", new { fileName = "fixture.pdf", data = Convert.ToBase64String("%PDF-1.7 fixture"u8.ToArray()) });

        Assert.NotEmpty(strings);
        Assert.All(sentinels, s => Assert.DoesNotContain(strings, m => m.Contains(s, StringComparison.Ordinal)));
    }

    [Fact]
    public void Equip_matched_equips_weapons_and_armour_but_not_other_items_and_is_off_by_default()
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Gear Notes", RulesFamilies.Srd521);
        var rope = Publish(h.Temp, source, ContentKind.Item, "Fixture Rope Coil", [RulesFamilies.Srd521]);
        var blade = Publish(h.Temp, source, ContentKind.Item, "Fixture Short Blade", [RulesFamilies.Srd521],
            effects: [new WeaponEffect { Id = "blade", Category = WeaponCategory.Simple, Attack = WeaponAttack.Melee, Damage = "1d6", DamageType = "piercing", WeaponKey = "fixture-blade" }]);
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Item("Fixture Rope Coil", 1).Item("Fixture Short Blade", 1);

        var off = h.Preview(sheet, RulesFamilies.Srd521);
        Assert.All(off.Character.Equipment, e => Assert.False(e.Equipped));

        var on = h.Preview(sheet, RulesFamilies.Srd521, equip: true);
        Assert.Equal([(rope, false), (blade, true)], on.Character.Equipment.Select(e => (e.Item, e.Equipped)));
    }

    [Fact]
    public void Equip_matched_wears_only_the_first_body_armour_and_the_first_shield()
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Armoury Notes", RulesFamilies.Srd521);
        ContentReference Armour(string name, ArmorCategory category, int ac) => Publish(h.Temp, source, ContentKind.Item, name, [RulesFamilies.Srd521],
            effects: [new ArmorEffect { Id = $"armor-{ac}-{category}", Category = category, ArmorClass = ac }]);
        var mail = Armour("Fixture Mail", ArmorCategory.Heavy, 16);
        var spare = Armour("Fixture Spare Coat", ArmorCategory.Light, 11);
        var shield = Armour("Fixture Buckler", ArmorCategory.Shield, 2);
        var second = Armour("Fixture Second Buckler", ArmorCategory.Shield, 2);
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1")
            .Item("Fixture Mail", 1).Item("Fixture Spare Coat", 1).Item("Fixture Buckler", 1).Item("Fixture Second Buckler", 1);

        var on = h.Preview(sheet, RulesFamilies.Srd521, equip: true);

        Assert.Equal([(mail, true), (spare, false), (shield, true), (second, false)], on.Character.Equipment.Select(e => (e.Item, e.Equipped)));
        Assert.DoesNotContain(on.Diagnostics, w => w.Code is "equipment.multiple-armor" or "equipment.multiple-shields");
    }

    [Fact]
    public void Equip_matched_does_not_give_the_body_slot_to_armour_the_calculator_ignores()
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Odd Armoury Notes", RulesFamilies.Srd521);
        var coat = Publish(h.Temp, source, ContentKind.Item, "Fixture Assisted Coat", [RulesFamilies.Srd521],
            effects: [new ArmorEffect { Id = "assisted-coat", Category = ArmorCategory.Light, ArmorClass = 11, Automation = AutomationStatus.Assisted }]);
        var cloak = Publish(h.Temp, source, ContentKind.Item, "Fixture Timed Cloak", [RulesFamilies.Srd521],
            effects: [new ArmorEffect { Id = "timed-cloak", Category = ArmorCategory.Light, ArmorClass = 12, Timing = EffectTiming.WhileActive }]);
        var mail = Publish(h.Temp, source, ContentKind.Item, "Fixture Real Mail", [RulesFamilies.Srd521],
            effects: [new ArmorEffect { Id = "real-mail", Category = ArmorCategory.Heavy, ArmorClass = 16 }]);
        var sheet = new SheetBuilder($"{h.Name(Barbarian)} 1").Item("Fixture Assisted Coat", 1).Item("Fixture Timed Cloak", 1).Item("Fixture Real Mail", 1);

        var on = h.Preview(sheet, RulesFamilies.Srd521, equip: true);

        Assert.Equal([(coat, false), (cloak, false), (mail, true)], on.Character.Equipment.Select(e => (e.Item, e.Equipped)));
    }

    [Fact]
    public void A_sheets_own_equipped_marks_win_and_the_guess_only_fills_items_with_no_mark()
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Marked Notes", RulesFamilies.Srd521);
        ContentReference Armour(string name, ArmorCategory category, int ac) => Publish(h.Temp, source, ContentKind.Item, name, [RulesFamilies.Srd521],
            effects: [new ArmorEffect { Id = $"armor-{ac}-{category}", Category = category, ArmorClass = ac }]);
        var weapon = new WeaponEffect { Id = "marked-blade", Category = WeaponCategory.Simple, Attack = WeaponAttack.Melee, Damage = "1d6", DamageType = "piercing", WeaponKey = "fixture-marked-blade" };
        var spare = Armour("Fixture Spare Coat", ArmorCategory.Light, 11);
        var mail = Armour("Fixture Marked Mail", ArmorCategory.Heavy, 16);
        var shield = Armour("Fixture Marked Buckler", ArmorCategory.Shield, 2);
        var other = Armour("Fixture Other Buckler", ArmorCategory.Shield, 2);
        var blade = Publish(h.Temp, source, ContentKind.Item, "Fixture Marked Blade", [RulesFamilies.Srd521], effects: [weapon]);
        var token = h.Read(new SheetBuilder($"{h.Name(Barbarian)} 1")
            .Item("Fixture Spare Coat", 1).Item("Fixture Marked Mail", 1).Item("Fixture Marked Buckler", 1).Item("Fixture Other Buckler", 1).Item("Fixture Marked Blade", 1));
        var read = h.Temp.App.DdbSessions.Peek(token)!;
        // The 2014 layout has no equipped mark, so the marks are set on the read sheet itself (what a layout with a mark would give).
        bool?[] marks = [null, true, false, null, false];
        var marked = read with { Items = [.. read.Items.Select((r, i) => Read<ItemText>.Ok(r.Value! with { Equipped = marks[i] }))] };

        var preview = h.Temp.App.ProposeDdbCharacter(marked, new DdbPreviewRequest(token, RulesFamilies.Srd521, null, null, null, false, EquipMatched: true), Guid.NewGuid());

        Assert.Equal(
            [(spare, false), (mail, true), (shield, false), (other, true), (blade, false)],
            preview.Character.Equipment.Select(e => (e.Item, e.Equipped)));
        Assert.DoesNotContain(preview.Diagnostics, w => w.Code is "equipment.multiple-armor" or "equipment.multiple-shields");
    }

    [Fact]
    public void A_detected_subclass_from_a_source_the_campaign_does_not_allow_carries_the_campaign_warning()
    {
        using var h = new DdbHarness();
        var source = Source(h.Temp, "Fixture Path Notes", RulesFamilies.Srd521);
        var sense = Publish(h.Temp, source, ContentKind.Feature, "Fixture Storm Sense", [RulesFamilies.Srd521]);
        Publish(h.Temp, source, ContentKind.Subclass, "Fixture Path of Storms", [RulesFamilies.Srd521],
            effects: [new GrantEffect { Id = "grant-sense", Grant = GrantKind.Content, Content = sense, Level = 3 }],
            extendsChoice: new(Barbarian.ContentId, "barbarian-subclass"));
        var campaign = h.Temp.App.SaveCampaign(new() { Id = Guid.Empty, Name = "Fixture Table", RulesFamily = RulesFamilies.Srd521, AllowedSources = [Guid.Parse("52500000-0000-4000-8000-000000000001")], HouseRules = "Fixture notes." });

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 3").Text("features", "Fixture Storm Sense"), RulesFamilies.Srd521, campaign: campaign.Id);

        var row = Row(preview, "class:0:subclass");
        Assert.Equal((MatchStatus.Matched, "campaign.source-not-allowed"), (row.Status, row.Note));
    }

    [Fact]
    public void A_resolution_for_a_subclass_the_class_choice_does_not_offer_asks_again_and_keeps_create_blocked()
    {
        using var h = new DdbHarness();
        var elsewhere = Publish(h.Temp, Source(h.Temp, "Fixture Elsewhere Notes", RulesFamilies.Srd521), ContentKind.Subclass, "Fixture Path of Elsewhere", [RulesFamilies.Srd521]);

        var preview = h.Preview(new SheetBuilder($"{h.Name(Barbarian)} 3"), RulesFamilies.Srd521,
            resolutions: [new Resolution("class:0:subclass", elsewhere, LeaveOut: false)]);

        var row = Row(preview, "class:0:subclass");
        Assert.Equal((MatchStatus.Choose, "resolution.not-found"), (row.Status, row.Note));
        Assert.Contains(row.Candidates, c => c.Reference.ContentId == Berserker.ContentId);
        Assert.False(preview.CanApply);
    }
}
