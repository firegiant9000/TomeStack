using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Tester feedback item 9 (D33): publish a source's pending drafts in one previewed, confirmed, all-or-nothing transaction.
/// "Pending" is the newest revision (stored order) of a content id being a draft; the confirmed set must equal the pending
/// set re-read inside the transaction; grants and choice options that pin revisions of what is published are re-pointed.
/// Original fixtures only.
/// </summary>
public class PublishManyTests
{
    private static readonly string[] Families = [RulesFamilies.Srd521];

    private sealed class World : IDisposable
    {
        public TempApp Temp { get; } = new();
        public Guid SourceId { get; }
        public TomeStackApp App => Temp.App;

        public World() => SourceId = Temp.App.CreateHomebrewSource(new("Test Bulk Publish", [RulesFamilies.Srd521])).Id;

        public void Dispose() => Temp.Dispose();

        public ContentRevision New(ContentKind kind, string name, params Effect[] effects) => new()
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = kind, Name = name, RulesFamilies = Families,
            Provenance = new(SourceId), Status = RevisionStatus.Draft, Effects = effects,
        };

        public ContentReference Save(ContentRevision draft) => App.SaveDraft(draft);

        public ContentReference Published(ContentRevision draft) => App.Publish(App.SaveDraft(draft)).Published;

        public static ModifierEffect Init(string value) => new() { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = value };

        public static GrantEffect GrantOf(ContentReference target, string id = "grant") => new() { Id = id, Grant = GrantKind.Content, Content = target, Level = 1 };

        public ContentRevision Newest(Guid contentId) => App.Store.ListRevisions(contentId).Last();

        public int RevisionCount => App.Store.ListRevisionsInOrder().Count(r => r.Provenance.SourceId == SourceId);

        public PublishManyRequest Confirmed(params ContentReference[] drafts) =>
            new([.. drafts.Select(d => d.ContentId)], [.. drafts.Select(d => d.RevisionId)], Confirm: true);
    }

    private static string Code(Action action) => Assert.Throws<AppValidationException>(action).Problems[0].Code;

    private static CharacterView View(World w, ContentReference classRef) => w.App.SaveCharacter(new Character
    {
        Id = Guid.NewGuid(), Name = "Testy Bulk", RulesFamily = RulesFamilies.Srd521, Level = 1, Classes = [new(classRef, 1)],
        BaseAbilities = new(10, 10, 10, 10, 10, 10),
    });

    private static CharacterSheet Sheet(World w, ContentReference classRef) => View(w, classRef).Sheet;

    [Fact]
    public void Two_pending_feats_publish_together_and_leave_the_drafts_as_they_were()
    {
        using var w = new World();
        var a = w.Save(w.New(ContentKind.Feat, "Testy Alpha", World.Init("1")));
        var b = w.Save(w.New(ContentKind.Feat, "Testy Beta", World.Init("2")));

        var preview = w.App.PreviewPublishMany([a.ContentId, b.ContentId]);
        Assert.True(preview.CanPublish);
        Assert.Equal(["Testy Alpha", "Testy Beta"], preview.Entries.Select(e => e.Name));
        Assert.All(preview.Entries, e => Assert.False(e.ReplacesPublished));
        Assert.Empty(preview.Dependents);
        Assert.Equal(2, w.RevisionCount); // the preview wrote nothing

        var result = w.App.PublishMany(w.Confirmed(a, b));

        Assert.Equal(2, result.Published.Count);
        Assert.Equal(4, w.RevisionCount);
        foreach (var draft in new[] { a, b })
        {
            Assert.Equal(RevisionStatus.Published, w.Newest(draft.ContentId).Status);
            Assert.Equal(RevisionStatus.Draft, w.App.Store.FindRevision(draft)!.Status);
        }
    }

    [Fact]
    public void A_second_identical_call_finds_nothing_pending_and_writes_nothing()
    {
        // Review Focus 1.
        using var w = new World();
        var a = w.Save(w.New(ContentKind.Feat, "Testy Alpha", World.Init("1")));
        var b = w.Save(w.New(ContentKind.Feat, "Testy Beta", World.Init("2")));
        var request = w.Confirmed(a, b);
        w.App.PublishMany(request);
        var count = w.RevisionCount;

        Assert.Equal("content.publish-set-changed", Code(() => w.App.PublishMany(request)));
        Assert.Equal("content.publish-set-changed", Code(() => w.App.PreviewPublishMany(request.ContentIds)));

        Assert.Equal(count, w.RevisionCount);
    }

    [Fact]
    public void One_invalid_entry_publishes_nothing()
    {
        using var w = new World();
        var good = w.Save(w.New(ContentKind.Feat, "Testy Good", World.Init("1")));
        var bad = w.Save(w.New(ContentKind.Feat, "Testy Bad", World.Init("1 + + 2")));
        var count = w.RevisionCount;

        var preview = w.App.PreviewPublishMany([good.ContentId, bad.ContentId]);
        Assert.False(preview.CanPublish);
        Assert.Empty(preview.Entries.Single(e => e.Name == "Testy Good").Report.Errors);
        Assert.NotEmpty(preview.Entries.Single(e => e.Name == "Testy Bad").Report.Errors);

        var refused = Assert.Throws<AppValidationException>(() => w.App.PublishMany(w.Confirmed(good, bad)));
        Assert.Equal("content.validation-failed", refused.Problems[0].Code);
        Assert.Equal(count, w.RevisionCount);
        Assert.Equal(RevisionStatus.Draft, w.Newest(good.ContentId).Status);
    }

    [Fact]
    public void A_pending_class_with_two_pending_features_ends_up_granting_the_published_features()
    {
        using var w = new World();
        var f1 = w.Save(w.New(ContentKind.Feature, "Testy Keen Eye", World.Init("1")));
        var f2 = w.Save(w.New(ContentKind.Feature, "Testy Steady Hand", World.Init("2")));
        var cls = w.Save(w.New(ContentKind.Class, "Testy Scout", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(f1, "g1"), World.GrantOf(f2, "g2")));

        var preview = w.App.PreviewPublishMany([cls.ContentId, f1.ContentId, f2.ContentId]);
        Assert.True(preview.CanPublish);
        var classEntry = preview.Entries.Single(e => e.Name == "Testy Scout");
        Assert.Equal(2, classEntry.RePointings.Single().GrantsMoved);
        Assert.Empty(classEntry.Report.Errors);

        var result = w.App.PublishMany(w.Confirmed(cls, f1, f2));

        Assert.Equal(["Testy Keen Eye", "Testy Steady Hand", "Testy Scout"], result.Published.Select(p => w.App.Store.FindRevision(p.Published)!.Name)); // leaves first
        var published = w.Newest(cls.ContentId);
        Assert.Equal(RevisionStatus.Published, published.Status);
        Assert.Equal([w.Newest(f1.ContentId).Reference, w.Newest(f2.ContentId).Reference], published.Effects.OfType<GrantEffect>().Select(g => g.Content!));
        Assert.All(published.Effects.OfType<GrantEffect>(), g => Assert.Equal(RevisionStatus.Published, w.App.Store.FindRevision(g.Content!)!.Status));

        // The calculator follows the grants: +1 and +2 initiative from the two features.
        var sheet = Sheet(w, published.Reference);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "content.unpublished");
        Assert.Equal(3, sheet.Field(FieldIds.Initiative).Value);
    }

    [Fact]
    public void A_class_published_one_entry_at_a_time_is_repointed_when_its_features_are_published()
    {
        // Review Focus 2: the 0.5.0 situation. The class is not in the request and not pending; it is a dependent.
        using var w = new World();
        var f1 = w.Save(w.New(ContentKind.Feature, "Testy Keen Eye", World.Init("1")));
        var f2 = w.Save(w.New(ContentKind.Feature, "Testy Steady Hand", World.Init("2")));
        var oldClass = w.Published(w.New(ContentKind.Class, "Testy Scout", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(f1, "g1"), World.GrantOf(f2, "g2")));
        var beforeView = View(w, oldClass);
        var before = beforeView.Sheet;
        Assert.Contains(before.Diagnostics, d => d.Code == "content.unpublished");
        Assert.Equal(0, before.Field(FieldIds.Initiative).Value);
        var classContent = oldClass.ContentId;
        var revisions = w.App.Store.ListRevisions(classContent).Count;

        var preview = w.App.PreviewPublishMany([f1.ContentId, f2.ContentId]);
        Assert.True(preview.CanPublish);
        var dependent = Assert.Single(preview.Dependents);
        Assert.Equal(("Testy Scout", true), (dependent.Name, dependent.ReplacesPublished));
        Assert.Equal(2, dependent.RePointings.Single().GrantsMoved);

        var result = w.App.PublishMany(w.Confirmed(f1, f2));

        Assert.Equal(3, result.Published.Count);
        Assert.Equal(revisions + 2, w.App.Store.ListRevisions(classContent).Count); // a re-pointed draft, then its published revision
        var newest = w.Newest(classContent);
        Assert.Equal(RevisionStatus.Published, newest.Status);
        Assert.Equal([w.Newest(f1.ContentId).Reference, w.Newest(f2.ContentId).Reference], newest.Effects.OfType<GrantEffect>().Select(g => g.Content!));
        Assert.Equal(RevisionStatus.Published, w.App.Store.FindRevision(oldClass)!.Status); // the old revision is untouched
        Assert.Equal(oldClass.RevisionId, w.App.Store.ListRevisions(classContent)[1].RevisionId);
        var after = Sheet(w, newest.Reference);
        Assert.DoesNotContain(after.Diagnostics, d => d.Code == "content.unpublished");
        Assert.Equal(3, after.Field(FieldIds.Initiative).Value);
        // A character is not re-pinned by publishing.
        Assert.Equal(oldClass, w.App.Store.FindCharacter(beforeView.Character.Id)!.Classes[0].Class);
    }

    [Fact]
    public void Dependents_are_followed_transitively_and_choice_options_are_repointed_too()
    {
        using var w = new World();
        var leaf = w.Save(w.New(ContentKind.Feature, "Testy Leaf", World.Init("1")));
        var middle = w.Published(w.New(ContentKind.Feature, "Testy Middle", World.GrantOf(leaf)));
        var top = w.Published(w.New(ContentKind.Class, "Testy Top", new HitDieEffect { Id = "hit-die", Die = 8 },
            new ChoiceEffect { Id = "pick", ChoiceId = "pick", Count = 1, Options = [middle] }));

        var preview = w.App.PreviewPublishMany([leaf.ContentId]);
        Assert.Equal(["Testy Middle", "Testy Top"], preview.Dependents.Select(d => d.Name));
        Assert.Equal(1, preview.Dependents[1].RePointings.Single().OptionsMoved);

        w.App.PublishMany(w.Confirmed(leaf));

        var newLeaf = w.Newest(leaf.ContentId).Reference;
        var newMiddle = w.Newest(middle.ContentId);
        Assert.Equal(newLeaf, newMiddle.Effects.OfType<GrantEffect>().Single().Content);
        Assert.Equal([newMiddle.Reference], w.Newest(top.ContentId).Effects.OfType<ChoiceEffect>().Single().Options);
        Assert.Equal(RevisionStatus.Published, w.Newest(top.ContentId).Status);
    }

    [Fact]
    public void A_restored_draft_that_is_newest_is_shown_as_replacing_the_published_version()
    {
        // Review Focus 3: stored order makes the draft the newest even though the content has a published revision.
        using var w = new World();
        var contentId = Guid.NewGuid();
        ContentRevision Rev(RevisionStatus status, string effect) => w.New(ContentKind.Feat, "Testy Restored", World.Init(effect)) with
        {
            ContentId = contentId, RevisionId = Guid.NewGuid(), Status = status,
        };
        w.App.Store.AddRevision(Rev(RevisionStatus.Published, "1"));
        var draft = Rev(RevisionStatus.Draft, "2");
        w.App.Store.AddRevision(draft);
        var fresh = w.Save(w.New(ContentKind.Feat, "Testy Fresh", World.Init("1")));

        var preview = w.App.PreviewPublishMany([contentId, fresh.ContentId]);

        Assert.Equal([(true, draft.Reference), (false, fresh)], preview.Entries.Select(e => (e.ReplacesPublished, e.Draft)));
        w.App.PublishMany(new([contentId], [draft.RevisionId], Confirm: true));
        Assert.Equal(RevisionStatus.Published, w.Newest(contentId).Status);
        Assert.Equal(3, w.App.Store.ListRevisions(contentId).Count);
    }

    [Fact]
    public void A_grant_to_a_draft_outside_the_batch_is_a_blocking_error()
    {
        using var w = new World();
        var outside = w.Save(w.New(ContentKind.Feature, "Testy Outside", World.Init("1")));
        var cls = w.Save(w.New(ContentKind.Class, "Testy Scout", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(outside)));
        var count = w.RevisionCount;

        var preview = w.App.PreviewPublishMany([cls.ContentId]);

        Assert.False(preview.CanPublish);
        Assert.Contains(preview.Entries.Single().Report.Errors, e => e.Code == "publish.grants-unpublished");
        var refused = Assert.Throws<AppValidationException>(() => w.App.PublishMany(w.Confirmed(cls)));
        Assert.Contains(refused.Problems, p => p.Code == "publish.grants-unpublished");
        Assert.Equal(count, w.RevisionCount);
        // The single publish keeps its warning (validate.reference-unpublished), as before.
        Assert.Contains(w.App.Publish(cls).Report.Warnings, x => x.Code == "validate.reference-unpublished");
    }

    [Fact]
    public void A_dependent_with_its_own_unpublished_draft_is_named_and_not_swept_in()
    {
        // Ruling R41.
        using var w = new World();
        var leaf = w.Save(w.New(ContentKind.Feature, "Testy Leaf", World.Init("1")));
        var cls = w.Published(w.New(ContentKind.Class, "Testy Scout", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(leaf)));
        var halfEdited = w.Save(w.Newest(cls.ContentId) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, Summary = "Testy half edit" });
        var count = w.RevisionCount;

        var preview = w.App.PreviewPublishMany([leaf.ContentId]);

        Assert.False(preview.CanPublish);
        var dependent = Assert.Single(preview.Dependents);
        Assert.Equal(halfEdited, dependent.Draft);
        Assert.Contains(dependent.Report.Errors, e => e.Code == "publish.dependent-pending");
        Assert.Contains("Testy Scout", dependent.Report.Errors.Single(e => e.Code == "publish.dependent-pending").Message, StringComparison.Ordinal);
        Assert.Equal("content.validation-failed", Assert.Throws<AppValidationException>(() => w.App.PublishMany(w.Confirmed(leaf))).Problems[0].Code);
        Assert.Equal(count, w.RevisionCount);
    }

    [Fact]
    public void Without_confirm_or_with_a_missing_or_different_expected_set_nothing_is_written()
    {
        using var w = new World();
        var a = w.Save(w.New(ContentKind.Feat, "Testy Alpha", World.Init("1")));
        var b = w.Save(w.New(ContentKind.Feat, "Testy Beta", World.Init("2")));
        var count = w.RevisionCount;

        Assert.Equal("content.publish-confirmation-required", Code(() => w.App.PublishMany(w.Confirmed(a, b) with { Confirm = false })));
        Assert.Equal("content.publish-set-changed", Code(() => w.App.PublishMany(new([a.ContentId, b.ContentId], null!, Confirm: true))));
        Assert.Equal("content.publish-set-changed", Code(() => w.App.PublishMany(new([a.ContentId, b.ContentId], [a.RevisionId], Confirm: true))));
        Assert.Equal("content.publish-set-changed", Code(() => w.App.PublishMany(new([a.ContentId, b.ContentId], [a.RevisionId, Guid.NewGuid()], Confirm: true))));
        Assert.Equal("content.publish-set-changed", Code(() => w.App.PublishMany(new([a.ContentId, Guid.NewGuid()], [a.RevisionId, b.RevisionId], Confirm: true))));
        Assert.Equal("content.publish-set-changed", Code(() => w.App.PublishMany(new([], [], Confirm: true))));
        Assert.Equal(count, w.RevisionCount);

        // A draft saved after the preview changes the pending set: the old confirmation no longer fits.
        var newer = w.Save(w.Newest(a.ContentId) with { RevisionId = Guid.NewGuid() });
        Assert.Equal("content.publish-set-changed", Code(() => w.App.PublishMany(w.Confirmed(a, b))));
        Assert.Equal(count + 1, w.RevisionCount);
        w.App.PublishMany(w.Confirmed(newer, b));
    }

    [Fact]
    public void Both_commands_work_through_the_dispatcher()
    {
        using var w = new World();
        var a = w.Save(w.New(ContentKind.Feat, "Testy Alpha", World.Init("1")));
        var dispatcher = new CommandDispatcher(w.App);
        Assert.Contains("content.publishManyPreview", CommandDispatcher.Commands);
        Assert.Contains("content.publishMany", CommandDispatcher.Commands);
        JsonDocument Send(string command, object payload) =>
            JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact)));

        using var preview = Send("content.publishManyPreview", new { contentIds = new[] { a.ContentId } });
        var data = preview.RootElement.GetProperty("result");
        Assert.True(data.GetProperty("canPublish").GetBoolean());
        Assert.Equal("Testy Alpha", data.GetProperty("entries")[0].GetProperty("name").GetString());
        Assert.False(data.GetProperty("entries")[0].GetProperty("replacesPublished").GetBoolean());

        using var refused = Send("content.publishMany", new { contentIds = new[] { a.ContentId }, expected = new[] { a.RevisionId }, confirm = false });
        Assert.Equal("content.publish-confirmation-required", refused.RootElement.GetProperty("error").GetProperty("diagnostics")[0].GetProperty("code").GetString());

        using var done = Send("content.publishMany", new { contentIds = new[] { a.ContentId }, expected = new[] { a.RevisionId }, confirm = true });
        Assert.True(done.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, done.RootElement.GetProperty("result").GetProperty("published").GetArrayLength());

        using var again = Send("content.publishMany", new { contentIds = new[] { a.ContentId }, expected = new[] { a.RevisionId }, confirm = true });
        Assert.Equal("content.publish-set-changed", again.RootElement.GetProperty("error").GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }
}
