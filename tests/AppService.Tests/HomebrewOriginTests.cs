using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Tester feedback item 12: the studio's species, background and spell editors write plain drafts. These show that what they
/// write validates, publishes (alone, or together with the option features of a newer-rules background) and is then read by
/// the builder and the calculator. Original fixtures only.
/// </summary>
public class HomebrewOriginTests
{
    private static readonly string[] BothFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521];

    private static ContentRevision New(SourceRecord source, ContentKind kind, string name, string[] families, params Effect[] effects) => new()
    {
        ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = kind, Name = name, RulesFamilies = families,
        Provenance = new(source.Id), Status = RevisionStatus.Draft, Effects = effects,
    };

    private static ModifierEffect Bonus(string id, Ability ability, int value) =>
        new() { Id = id, Operation = ModifierOperation.Bonus, Target = FieldIds.Score(ability), Value = $"{value}" };

    private static ContentReference Publish(TomeStackApp app, ContentRevision draft)
    {
        var saved = app.SaveDraft(draft);
        var report = app.ValidateContent(saved, null);
        Assert.True(report.CanPublish, string.Join("; ", report.Errors.Select(e => e.Message)));
        return app.Publish(saved).Published;
    }

    private static CharacterSheet Sheet(TomeStackApp app, string family, ContentReference pinned, params ChoiceSelection[] choices) =>
        app.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = "Testy Origin", RulesFamily = family, Level = 1, Classes = [], Pins = [pinned], Choices = choices,
            BaseAbilities = new(10, 10, 10, 10, 10, 10),
        }).Sheet;

    [Fact]
    public void A_homebrew_species_with_increases_and_a_skill_validates_publishes_and_counts_under_the_older_rules_only()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Origins", BothFamilies));
        var species = Publish(temp.App, New(source, ContentKind.Species, "Testy Folk", BothFamilies,
            Bonus("str-increase", Ability.Str, 2),
            new GrantEffect { Id = "skill-athletics", Grant = GrantKind.Proficiency, Target = FieldIds.Skill("athletics") }));

        Assert.Equal(RevisionStatus.Published, temp.App.Store.ListRevisions(species.ContentId).Last().Status);
        Assert.Equal(12, Sheet(temp.App, RulesFamilies.Srd51, species).Field(FieldIds.Score(Ability.Str)).Value);
        var newer = Sheet(temp.App, RulesFamilies.Srd521, species).Field(FieldIds.Score(Ability.Str));
        Assert.Equal(10, newer.Value); // the newer rules take increases from the background
        Assert.Contains(newer.Warnings, w => w.Code == "policy.ability-increase-source");
    }

    [Fact]
    public void A_homebrew_background_that_grants_a_feature_publishes_and_the_feature_is_active()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Origins", [RulesFamilies.Srd51]));
        var feature = Publish(temp.App, New(source, ContentKind.Feature, "Testy Shelter", [RulesFamilies.Srd51],
            new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" }));
        var background = Publish(temp.App, New(source, ContentKind.Background, "Testy Wanderer", [RulesFamilies.Srd51],
            new GrantEffect { Id = "feature", Grant = GrantKind.Content, Content = feature }));

        var sheet = Sheet(temp.App, RulesFamilies.Srd51, background);

        Assert.Contains(feature, sheet.Active!);
        Assert.Equal(1, sheet.Field(FieldIds.Initiative).Value); // Dex 10 (+0) and the feature's +1
    }

    [Fact]
    public void A_newer_rules_background_and_its_option_features_publish_together_and_the_choice_is_re_pointed()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Origins", [RulesFamilies.Srd521]));
        string[] families = [RulesFamilies.Srd521];
        var options = new[] { (Ability.Str, Ability.Dex), (Ability.Dex, Ability.Str) }
            .Select(p => temp.App.SaveDraft(New(source, ContentKind.Feature, $"Testy Wanderer Ability Scores: {p.Item1} +2, {p.Item2} +1", families,
                Bonus(p.Item1.ToString().ToLowerInvariant(), p.Item1, 2), Bonus(p.Item2.ToString().ToLowerInvariant(), p.Item2, 1))))
            .ToList();
        var background = temp.App.SaveDraft(New(source, ContentKind.Background, "Testy Wanderer", families,
            new ChoiceEffect { Id = "testy-wanderer-ability-scores", ChoiceId = "testy-wanderer-ability-scores", Count = 1, Options = options }));

        var warnings = temp.App.ValidateContent(background, null).Warnings;
        Assert.Contains(warnings, w => w.Code == "validate.reference-unpublished");

        ContentReference[] drafts = [.. options, background];
        var preview = temp.App.PreviewPublishMany([.. drafts.Select(d => d.ContentId)]);
        Assert.True(preview.CanPublish);
        temp.App.PublishMany(new([.. drafts.Select(d => d.ContentId)], [.. drafts.Select(d => d.RevisionId)], Confirm: true));

        var published = temp.App.Store.ListRevisions(background.ContentId).Last();
        Assert.Equal(RevisionStatus.Published, published.Status);
        var pointed = published.Effects.OfType<ChoiceEffect>().Single().Options;
        Assert.All(pointed, o => Assert.Equal(RevisionStatus.Published, temp.App.Store.FindRevision(o)!.Status));

        var sheet = Sheet(temp.App, RulesFamilies.Srd521, new(published.ContentId, published.RevisionId), new ChoiceSelection(new(published.ContentId, published.RevisionId), "testy-wanderer-ability-scores", [pointed[0]]));
        Assert.Equal(12, sheet.Field(FieldIds.Score(Ability.Str)).Value);
        Assert.Equal(11, sheet.Field(FieldIds.Score(Ability.Dex)).Value);
    }

    [Fact]
    public void A_homebrew_spell_validates_and_publishes_in_either_family_and_is_listed_by_its_lists()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Origins", BothFamilies));
        foreach (var family in BothFamilies)
        {
            var spell = Publish(temp.App, New(source, ContentKind.Spell, $"Testy Spark {family}", [family],
                new SpellEffect { Id = "spell", Level = 0, School = "evocation", Range = "30 feet", Lists = ["testy-list"], Text = "A fixture spark." }));
            var stored = Assert.IsType<SpellEffect>(Assert.Single(temp.App.Store.ListRevisions(spell.ContentId).Last().Effects));
            Assert.Equal(["testy-list"], stored.Lists);
        }

        Assert.Contains(temp.App.SpellListKeys(), k => k.Key == "testy-list" && k.Spells == 2);
    }
}
