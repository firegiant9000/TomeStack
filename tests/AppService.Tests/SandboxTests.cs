using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M5 slice 3 (B03; owner decision LIVING_SPECS D14): <c>content.sandbox</c> tries a draft class or subclass on an
/// unsaved copy of a character or on a blank one. The draft counts as published for that one calculation, and nothing
/// is written: only published revisions affect saved characters. All content is original.
/// </summary>
public class SandboxTests
{
    private static ContentRevision Draft(SourceRecord source, Guid contentId, ContentKind kind, string name, params Effect[] effects) => new()
    {
        ContentId = contentId,
        RevisionId = Guid.Empty,
        Kind = kind,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(source.Id),
        Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ModifierEffect Initiative(string value) => new() { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = value };

    private static HitDieEffect D8 => new() { Id = "hd", Die = 8 };

    private static ContentReference Publish(TomeStackApp app, ContentRevision draft) => app.Publish(app.SaveDraft(draft)).Published;

    /// <summary>What the sandbox could conceivably write: revisions, characters (with play state), sources, campaigns and gap notes.</summary>
    private static string Snapshot(TomeStackApp app) =>
        TempApp.Json(new
        {
            revisions = app.Store.ListRevisionsInOrder(),
            characters = app.Store.ListCharacters(),
            sources = app.Store.ListSources(),
            campaigns = app.Store.ListCampaigns(),
            gapNotes = app.Store.ListAllGapNotes(),
        });

    private static ChoiceEffect PathChoice(string choiceId = "path") => new() { Id = choiceId, ChoiceId = choiceId, Level = 3 };

    [Fact]
    public void A_draft_subclass_on_a_copy_joins_the_class_it_has_or_replaces_an_older_revision_it_chose()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Sandbox Source", [RulesFamilies.Srd521]));
        var cls = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Sandbox Class", D8, PathChoice(), PathChoice("other")));
        var subclassId = Guid.NewGuid();
        var older = Publish(app, Draft(source, subclassId, ContentKind.Subclass, "Test Sandbox Path", Initiative("1")) with { ExtendsChoice = new(cls.ContentId, "path") });
        var plain = app.CreateCharacter(new("Test Plain", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null, [new(cls, 4)])).Character;
        var chosen = app.CreateCharacter(new("Test Chosen", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null, [new(cls, 4)], [new(cls, "path", [older])])).Character;
        var draft = Draft(source, subclassId, ContentKind.Subclass, "Test Sandbox Path", Initiative("3")) with { ExtendsChoice = new(cls.ContentId, "path") };
        var before = Snapshot(app);

        var joined = app.Sandbox(new(Revision: draft, CharacterId: plain.Id));
        Assert.Equal([new ClassLevel(cls, 4)], joined.View.Character.Classes); // keeps its level (above the choice's 3)
        Assert.Equal(3, joined.View.Sheet.Field(FieldIds.Initiative).Value);

        var replaced = app.Sandbox(new(Revision: draft, CharacterId: chosen.Id));
        Assert.Equal([replaced.Draft], replaced.View.Character.Choices.Single().Selected);
        var change = Assert.Single(replaced.Changes, c => c.Field == FieldIds.Initiative);
        Assert.Equal((1, 3), (change.Before, change.After));

        // The draft now extends another choice: the older selection no longer applies, and the draft joins the new one.
        var moved = app.Sandbox(new(Revision: draft with { ExtendsChoice = new(cls.ContentId, "other") }, CharacterId: chosen.Id));
        var selection = Assert.Single(moved.View.Character.Choices);
        Assert.Equal(("other", moved.Draft), (selection.ChoiceId, selection.Selected.Single()));
        Assert.Equal(3, moved.View.Sheet.Field(FieldIds.Initiative).Value);
        Assert.DoesNotContain(moved.View.Sheet.Diagnostics, d => d.Code is "choice.invalid-option" or "choice.orphaned");

        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public void A_subclass_offered_only_as_a_declared_option_cannot_be_tried_as_a_draft()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Sandbox Source", [RulesFamilies.Srd521]));
        var subclassId = Guid.NewGuid();
        var listed = Publish(app, Draft(source, subclassId, ContentKind.Subclass, "Test Listed Path", Initiative("1")));
        var cls = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Listing Class", D8, PathChoice() with { Options = [listed] }));
        var hero = app.CreateCharacter(new("Test Listed", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null, [new(cls, 3)], [new(cls, "path", [listed])])).Character;

        var refused = Assert.Throws<AppValidationException>(() => app.Sandbox(new(Revision: Draft(source, subclassId, ContentKind.Subclass, "Test Listed Path", Initiative("2")), CharacterId: hero.Id)));
        Assert.Equal("sandbox.subclass-declared-option", refused.Problems.Single().Code);
    }

    [Fact]
    public void A_character_with_no_class_levels_recorded_keeps_its_level_and_the_class_is_no_longer_pinned()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Sandbox Source", [RulesFamilies.Srd521]));
        var classId = Guid.NewGuid();
        var published = Publish(app, Draft(source, classId, ContentKind.Class, "Test Sandbox Class", D8, Initiative("1")));
        var pinned = app.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = "Test Pinned", RulesFamily = RulesFamilies.Srd521, BaseAbilities = new(10, 10, 10, 10, 10, 10), Level = 5, Pins = [published],
        }).Character;

        var result = app.Sandbox(new(Revision: Draft(source, classId, ContentKind.Class, "Test Sandbox Class", D8, Initiative("2")), CharacterId: pinned.Id));

        Assert.Equal((5, 5), (result.View.Character.Level, result.View.Character.Classes.Single().Level));
        Assert.Empty(result.View.Character.Pins);
        Assert.DoesNotContain(result.Changes, c => c.Field == FieldIds.ProficiencyBonus); // same level, same PB
    }

    [Fact]
    public void Levels_beyond_twenty_scope_mixups_and_feature_choices_are_refused()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Sandbox Source", [RulesFamilies.Srd521]));
        var other = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Other Class", D8));
        var veteran = app.CreateCharacter(new("Test Veteran", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null, [new(other, 20)])).Character;
        var draftClass = Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Sandbox Class", D8);
        string Code(SandboxRequest request) => Assert.Throws<AppValidationException>(() => app.Sandbox(request)).Problems.First().Code;

        Assert.StartsWith("character.", Code(new(Revision: draftClass, CharacterId: veteran.Id))); // 21 levels in total
        Assert.Equal("sandbox.scope", Code(new(Revision: draftClass, CharacterId: veteran.Id, RulesFamily: RulesFamilies.Srd521)));

        // A subclass of a feature's choice: no blank character has the feature, and a copy that chose it has no class level to set.
        var feature = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Feature, "Test Choosing Feature", PathChoice()));
        var subclassId = Guid.NewGuid();
        var older = Publish(app, Draft(source, subclassId, ContentKind.Subclass, "Test Feature Path") with { ExtendsChoice = new(feature.ContentId, "path") });
        var draftSubclass = Draft(source, subclassId, ContentKind.Subclass, "Test Feature Path", Initiative("2")) with { ExtendsChoice = new(feature.ContentId, "path") };
        Assert.Equal("sandbox.subclass-class", Code(new(Revision: draftSubclass)));
        var chooser = app.CreateCharacter(new("Test Chooser", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), [feature], [new(other, 5)], [new(feature, "path", [older])])).Character;
        Assert.Equal("sandbox.level-class", Code(new(Revision: draftSubclass, CharacterId: chooser.Id, Level: 7)));
        Assert.Equal(2, app.Sandbox(new(Revision: draftSubclass, CharacterId: chooser.Id)).View.Sheet.Field(FieldIds.Initiative).Value);
    }

    [Fact]
    public void An_unsaved_draft_from_a_source_the_campaign_does_not_allow_is_flagged_on_the_copy()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Sandbox Source", [RulesFamilies.Srd521]));
        var other = app.CreateHomebrewSource(new("Test Allowed Source", [RulesFamilies.Srd521]));
        var allowedClass = Publish(app, Draft(other, Guid.NewGuid(), ContentKind.Class, "Test Allowed Class", D8));
        var campaign = app.SaveCampaign(new Campaign { Id = Guid.Empty, Name = "Test Table", RulesFamily = RulesFamilies.Srd521, AllowedSources = [other.Id] });
        var hero = app.CreateCharacter(new("Test Member", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null, [new(allowedClass, 2)], CampaignId: campaign.Id)).Character;

        var result = app.Sandbox(new(Revision: Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Sandbox Class", D8), CharacterId: hero.Id));

        Assert.Contains(result.View.Campaign!.Warnings, w => w.Code == "campaign.source-not-allowed" && w.Content == result.Draft);
    }

    [Fact]
    public void A_draft_class_is_tried_on_a_blank_character_and_nothing_is_written()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Sandbox Source", [RulesFamilies.Srd521]));
        var before = Snapshot(app);

        var result = app.Sandbox(new(Revision: Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Sandbox Class", D8, Initiative("CLASS_LEVEL")), Level: 5));

        var sheet = result.View.Sheet;
        Assert.Equal(5, sheet.Field(FieldIds.Initiative).Value);
        Assert.Equal(8 + (5 * 4), sheet.Field(FieldIds.HitPoints).Value);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code is "content.unpublished" or "content.missing");
        Assert.Equal([new ClassLevel(result.Draft, 5)], result.View.Character.Classes);
        Assert.Empty(result.Changes);
        Assert.True(result.Validation.CanPublish);
        Assert.Equal(before, Snapshot(app));
        Assert.Null(app.Store.FindCharacter(result.View.Character.Id));
    }

    [Fact]
    public void A_new_draft_of_a_class_is_tried_on_a_copy_of_a_saved_character_which_stays_on_its_published_revision()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Sandbox Source", [RulesFamilies.Srd521]));
        var classId = Guid.NewGuid();
        var published = Publish(app, Draft(source, classId, ContentKind.Class, "Test Sandbox Class", D8, Initiative("1")));
        var saved = app.CreateCharacter(new("Test Sandbox Hero", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null, [new(published, 3)])).Character;
        var draft = app.SaveDraft(Draft(source, classId, ContentKind.Class, "Test Sandbox Class", D8, Initiative("CLASS_LEVEL")));
        var before = Snapshot(app);

        var result = app.Sandbox(new(Reference: draft, CharacterId: saved.Id));

        Assert.NotEqual(saved.Id, result.View.Character.Id);
        Assert.Equal([new ClassLevel(draft, 3)], result.View.Character.Classes); // the draft replaces the published revision, same level
        Assert.Equal(3, result.View.Sheet.Field(FieldIds.Initiative).Value);
        var change = Assert.Single(result.Changes, c => c.Field == FieldIds.Initiative);
        Assert.Equal((1, 3), (change.Before, change.After));

        Assert.Equal(before, Snapshot(app));
        var stored = app.GetCharacter(saved.Id);
        Assert.Equal([new ClassLevel(published, 3)], stored.Character.Classes);
        Assert.Equal(1, stored.Sheet.Field(FieldIds.Initiative).Value); // a saved character never calculates a draft
    }

    [Fact]
    public void A_draft_subclass_joins_its_class_at_the_choice_level_on_a_blank_character()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Sandbox Source", [RulesFamilies.Srd521]));
        var cls = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Sandbox Class", D8,
            new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3 }));
        var subclass = Draft(source, Guid.NewGuid(), ContentKind.Subclass, "Test Sandbox Path", Initiative("2")) with { ExtendsChoice = new(cls.ContentId, "path") };

        var result = app.Sandbox(new(Revision: subclass));

        Assert.Equal([new ClassLevel(cls, 3)], result.View.Character.Classes);
        Assert.Equal([new ChoiceSelection(cls, "path", [result.Draft])], result.View.Character.Choices, new JsonComparer<ChoiceSelection>());
        Assert.Equal(2, result.View.Sheet.Field(FieldIds.Initiative).Value);
        Assert.DoesNotContain(result.View.Sheet.Diagnostics, d => d.Code is "choice.invalid-option" or "choice.unresolved");

        // At level 2 the choice is not offered yet, so the subclass does not apply.
        Assert.Equal(0, app.Sandbox(new(Revision: subclass, Level: 2)).View.Sheet.Field(FieldIds.Initiative).Value);
    }

    [Fact]
    public void The_sandbox_refuses_what_it_cannot_try()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Sandbox Source", [RulesFamilies.Srd521]));
        string Code(SandboxRequest request) => Assert.Throws<AppValidationException>(() => app.Sandbox(request)).Problems.First().Code;
        var cls = Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Sandbox Class", D8);

        Assert.Equal("sandbox.draft-required", Code(new()));
        Assert.Equal("sandbox.draft-required", Code(new(Reference: Publish(app, cls))));
        Assert.Equal("sandbox.kind", Code(new(Revision: Draft(source, Guid.NewGuid(), ContentKind.Feature, "Test Feature"))));
        Assert.Equal("sandbox.level", Code(new(Revision: cls, Level: 21)));
        Assert.Equal("sandbox.rules-family", Code(new(Revision: cls, RulesFamily: RulesFamilies.Srd51)));
        Assert.Equal("sandbox.subclass-unplaced", Code(new(Revision: Draft(source, Guid.NewGuid(), ContentKind.Subclass, "Test Unplaced"))));
        Assert.Equal("character.not-found", Code(new(Revision: cls, CharacterId: Guid.NewGuid())));
        Assert.Equal("validate.empty-entry", Code(new(Revision: cls with { Effects = [null!] })));
    }

    [Fact]
    public void The_command_returns_the_unsaved_sheet_with_the_drafts_validation()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Sandbox Source", [RulesFamilies.Srd521]));
        var draft = Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Sandbox Class", Initiative("PB"));
        var dispatcher = new CommandDispatcher(temp.App);

        using var response = JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command = "content.sandbox", payload = new { revision = draft, level = 2 } }, RulesJson.Compact)));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean(), response.RootElement.ToString());
        var result = response.RootElement.GetProperty("result");
        Assert.Equal(2, result.GetProperty("view").GetProperty("character").GetProperty("classes")[0].GetProperty("level").GetInt32());
        Assert.Contains(result.GetProperty("validation").GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "validate.hit-die-missing");
    }

    private sealed class JsonComparer<T> : IEqualityComparer<T>
    {
        public bool Equals(T? x, T? y) => TempApp.Json(x) == TempApp.Json(y);

        public int GetHashCode(T obj) => TempApp.Json(obj).GetHashCode(StringComparison.Ordinal);
    }
}
