using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M5 slice 4 (B04 before/after, B07 diff viewer): <c>content.compare</c> diffs two revisions by mechanics and text and
/// runs both on unsaved copies of characters and on a blank character, with the update review's computation. Nothing is
/// written or applied. Original content only.
/// </summary>
public class CompareTests
{
    private static ContentRevision Draft(SourceRecord source, Guid contentId, ContentKind kind, string name, string? summary, params Effect[] effects) => new()
    {
        ContentId = contentId,
        RevisionId = Guid.Empty,
        Kind = kind,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(source.Id),
        Status = RevisionStatus.Draft,
        Summary = summary,
        Effects = effects,
    };

    private static ModifierEffect Initiative(string value) => new() { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = value };

    private static HitDieEffect D8 => new() { Id = "hd", Die = 8 };

    private static ContentReference Publish(TomeStackApp app, ContentRevision draft) => app.Publish(app.SaveDraft(draft)).Published;

    private static string Snapshot(TomeStackApp app) =>
        TempApp.Json(new { revisions = app.Store.ListRevisionsInOrder(), characters = app.Store.ListCharacters(), gapNotes = app.Store.ListAllGapNotes() });

    [Fact]
    public void Two_published_revisions_of_a_class_are_compared_by_mechanics_text_and_on_a_characters_copy()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Compare Source", [RulesFamilies.Srd521]));
        var classId = Guid.NewGuid();
        var first = Publish(app, Draft(source, classId, ContentKind.Class, "Test Compare Class", "Quick hands.", D8, Initiative("1")));
        var second = Publish(app, Draft(source, classId, ContentKind.Class, "Test Compare Class", "Quicker hands.", D8, Initiative("CLASS_LEVEL")));
        var hero = app.CreateCharacter(new("Test Comparer", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null, [new(first, 3)])).Character;
        var before = Snapshot(app);

        var result = app.Compare(new(first, To: second, CharacterIds: [hero.Id], Blank: new(Level: 5)));

        Assert.Equal((ChangeKind.Changed, "init"), (result.Mechanics.Effects.Single().Change, result.Mechanics.Effects.Single().EffectId));
        Assert.Equal("summary", Assert.Single(result.Text).Where);
        var copy = result.Runs.Single(r => r.CharacterId == hero.Id);
        Assert.Equal((1, 3), copy.Fields.Single(f => f.Field == FieldIds.Initiative) is var f ? (f.Before, f.After) : default);
        var blank = result.Runs.Single(r => r.CharacterId is null);
        Assert.Equal((1, 5), blank.Fields.Single(x => x.Field == FieldIds.Initiative) is var b ? (b.Before, b.After) : default);

        // The same numbers the update review shows, and nothing changed.
        var review = app.ReviewUpdate(hero.Id, first, second);
        Assert.Equal(TempApp.Json(review.Fields), TempApp.Json(copy.Fields));
        Assert.Equal(TempApp.Json(review.NewDiagnostics), TempApp.Json(copy.NewDiagnostics));
        Assert.Equal(TempApp.Json(review.ResolvedDiagnostics), TempApp.Json(copy.ResolvedDiagnostics));
        Assert.Equal(before, Snapshot(app));
        Assert.Equal([new ClassLevel(first, 3)], app.GetCharacter(hero.Id).Character.Classes);
    }

    [Fact]
    public void A_problem_that_persists_is_in_neither_list_and_one_that_goes_away_or_arrives_is_reported_the_same_as_the_update_review()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Compare Source", [RulesFamilies.Srd521]));
        var featId = Guid.NewGuid();
        // CLASS_LEVEL has no value in a feat, so a roll bonus of it is an effect.invalid-formula diagnostic.
        static RollEffect Roll(string id, string bonus) => new() { Id = id, RollId = id, Label = id, Dice = "1d6", Bonus = bonus };
        ContentReference Add(params Effect[] effects)
        {
            var revision = Draft(source, featId, ContentKind.Feat, "Test Problem Feat", null, effects) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };
            app.Store.AddRevision(revision);
            return revision.Reference;
        }
        var first = Add(Roll("stays", "CLASS_LEVEL"), Roll("goes", "CLASS_LEVEL"));
        var second = Add(Roll("stays", "CLASS_LEVEL"), Roll("goes", "1"), Roll("arrives", "CLASS_LEVEL"));
        var user = app.CreateCharacter(new("Test Problem User", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), [first])).Character;

        var run = app.Compare(new(first, To: second, CharacterIds: [user.Id])).Runs.Single();

        Assert.Empty(run.Problems);
        Assert.Equal(["arrives"], run.NewDiagnostics.Select(d => d.EffectId));
        Assert.Equal(["goes"], run.ResolvedDiagnostics.Select(d => d.EffectId));
        var review = app.ReviewUpdate(user.Id, first, second);
        Assert.Equal(TempApp.Json(review.NewDiagnostics), TempApp.Json(run.NewDiagnostics));
        Assert.Equal(TempApp.Json(review.ResolvedDiagnostics), TempApp.Json(run.ResolvedDiagnostics));
    }

    [Fact]
    public void A_published_revision_is_compared_with_the_unsaved_draft_through_the_sandbox_overlay()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Compare Source", [RulesFamilies.Srd521]));
        var classId = Guid.NewGuid();
        var published = Publish(app, Draft(source, classId, ContentKind.Class, "Test Compare Class", null, D8, Initiative("1")));
        var before = Snapshot(app);

        var result = app.Compare(new(published, ToRevision: Draft(source, classId, ContentKind.Class, "Test Compare Class", null, D8, Initiative("4")), Blank: new(Level: 2)));

        var run = Assert.Single(result.Runs);
        Assert.Equal((1, 4), run.Fields.Single(x => x.Field == FieldIds.Initiative) is var f ? (f.Before, f.After) : default);
        Assert.DoesNotContain(run.NewDiagnostics, d => d.Code is "content.unpublished" or "content.missing");
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public void Other_content_runs_on_characters_that_use_it_and_explains_those_that_do_not()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Compare Source", [RulesFamilies.Srd521]));
        var featId = Guid.NewGuid();
        var first = Publish(app, Draft(source, featId, ContentKind.Feat, "Test Compare Feat", null, Initiative("1")));
        var second = Publish(app, Draft(source, featId, ContentKind.Feat, "Test Compare Feat", null, Initiative("2")));
        var user = app.CreateCharacter(new("Test User", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), [first])).Character;
        var stranger = app.CreateCharacter(new("Test Stranger", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), [])).Character;

        var result = app.Compare(new(first, To: second, CharacterIds: [user.Id, stranger.Id]));

        Assert.Equal((1, 2), result.Runs.Single(r => r.CharacterId == user.Id).Fields.Single() is var f ? (f.Before, f.After) : default);
        Assert.Equal("compare.unused", result.Runs.Single(r => r.CharacterId == stranger.Id).Problems.Single().Code);
    }

    [Fact]
    public void A_stored_draft_is_compared_with_an_unsaved_one_each_through_its_own_overlay()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Compare Source", [RulesFamilies.Srd521]));
        var classId = Guid.NewGuid();
        var stored = app.SaveDraft(Draft(source, classId, ContentKind.Class, "Test Compare Class", null, D8, Initiative("2")));
        var before = Snapshot(app);

        var result = app.Compare(new(stored, ToRevision: Draft(source, classId, ContentKind.Class, "Test Compare Class", null, D8, Initiative("5")), Blank: new(Level: 1)));

        var run = Assert.Single(result.Runs);
        Assert.Equal((2, 5), run.Fields.Single(x => x.Field == FieldIds.Initiative) is var f ? (f.Before, f.After) : default);
        Assert.Empty(run.Problems);
        Assert.DoesNotContain(run.NewDiagnostics.Concat(run.ResolvedDiagnostics), d => d.Code is "content.unpublished" or "content.missing");
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public void Characters_are_compared_the_way_the_update_review_moves_them()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Compare Source", [RulesFamilies.Srd521]));

        // A subclass the class lists as a declared option: a copy that has it is moved like applyUpdate, not placed.
        var subclassId = Guid.NewGuid();
        var listed = Publish(app, Draft(source, subclassId, ContentKind.Subclass, "Test Listed Path", null, Initiative("1")));
        var relisted = Publish(app, Draft(source, subclassId, ContentKind.Subclass, "Test Listed Path", null, Initiative("2")));
        var cls = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Listing Class", null, D8,
            new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3, Options = [listed] }));
        var pathed = app.CreateCharacter(new("Test Pathed", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null, [new(cls, 3)], [new(cls, "path", [listed])])).Character;
        var run = app.Compare(new(listed, To: relisted, CharacterIds: [pathed.Id])).Runs.Single();
        Assert.Empty(run.Problems);
        Assert.Equal(TempApp.Json(app.ReviewUpdate(pathed.Id, listed, relisted).Fields), TempApp.Json(run.Fields));

        // A feature the character gets only through a grant: named as such, not as "does not use".
        var featureId = Guid.NewGuid();
        var gift = Publish(app, Draft(source, featureId, ContentKind.Feature, "Test Gift", null, Initiative("1")));
        var regift = Publish(app, Draft(source, featureId, ContentKind.Feature, "Test Gift", null, Initiative("2")));
        var giver = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Giver", null, D8, new GrantEffect { Id = "gift", Grant = GrantKind.Content, Content = gift, Level = 1 }));
        var gifted = app.CreateCharacter(new("Test Gifted", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null, [new(giver, 1)])).Character;
        Assert.Equal("compare.granted", app.Compare(new(gift, To: regift, CharacterIds: [gifted.Id])).Runs.Single().Problems.Single().Code);
    }

    [Fact]
    public void A_recorded_cross_family_exception_lets_a_character_compare_content_of_another_family()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Compare Source", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        var featId = Guid.NewGuid();
        var first = Publish(app, Draft(source, featId, ContentKind.Feat, "Test Old Feat", null, Initiative("1")) with { RulesFamilies = [RulesFamilies.Srd51] });
        var second = Publish(app, Draft(source, featId, ContentKind.Feat, "Test Old Feat", null, Initiative("2")) with { RulesFamilies = [RulesFamilies.Srd51] });
        var borrower = app.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = "Test Borrower", RulesFamily = RulesFamilies.Srd521, BaseAbilities = new(10, 10, 10, 10, 10, 10),
            Pins = [first], CrossFamilyExceptions = [new(first, "Table ruling for the test")],
        }).Character;

        var run = app.Compare(new(first, To: second, CharacterIds: [borrower.Id])).Runs.Single();

        Assert.Empty(run.Problems);
        Assert.Equal(TempApp.Json(app.ReviewUpdate(borrower.Id, first, second).Fields), TempApp.Json(run.Fields));
    }

    [Fact]
    public void Compare_refuses_what_it_cannot_compare()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Compare Source", [RulesFamilies.Srd521]));
        var feat = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Feat, "Test Feat", null));
        var other = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Feat, "Test Other Feat", null));
        string Code(CompareRequest request) => Assert.Throws<AppValidationException>(() => app.Compare(request)).Problems.First().Code;

        Assert.Equal("compare.to-required", Code(new(feat)));
        Assert.Equal("compare.different-content", Code(new(feat, To: other)));
        Assert.Equal("compare.same-revision", Code(new(feat, To: feat)));
        var featAgain = Publish(app, Draft(source, feat.ContentId, ContentKind.Feat, "Test Feat", "Again"));
        Assert.Equal("compare.too-many", Code(new(feat, To: featAgain, CharacterIds: [.. Enumerable.Range(0, 21).Select(_ => Guid.NewGuid())])));
        Assert.Equal("compare.blank-kind", Code(new(feat, ToRevision: Draft(source, feat.ContentId, ContentKind.Feat, "Test Feat", "x"), Blank: new())));
        Assert.Equal("content.not-found", Code(new(new(Guid.NewGuid(), Guid.NewGuid()), To: feat)));
        var huge = Draft(source, feat.ContentId, ContentKind.Feat, "Test Feat", null, [.. Enumerable.Range(0, TomeStackApp.MaxCompareEffects + 1).Select(i => Initiative("1") with { Id = $"r{i}" })]);
        Assert.Equal("compare.too-large", Code(new(feat, ToRevision: huge)));
        // A bad blank-character level is refused before any character is calculated.
        Assert.Equal("sandbox.level", Code(new(feat, ToRevision: Draft(source, feat.ContentId, ContentKind.Class, "Test Feat", null), Blank: new(Level: 0))));
    }
}
