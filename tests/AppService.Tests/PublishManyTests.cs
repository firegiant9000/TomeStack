using System.Text.Json;
using TomeStack.RulesCore;
using Xunit.Abstractions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Tester feedback item 9 (D33): publish a source's pending drafts in one previewed, confirmed, all-or-nothing transaction.
/// "Pending" is the newest revision (stored order) of a content id being a draft; the confirmed set must equal the pending
/// set re-read inside the transaction; grants and choice options that pin revisions of what is published are re-pointed.
/// Original fixtures only.
/// </summary>
public class PublishManyTests(ITestOutputHelper output)
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
        // Fixed ids that sort the wrong way: by id the draft (0000…) comes before the published revision (ffff…), so a switch
        // to the id-ordered ListRevisions() would call the published one the newest and find nothing pending.
        ContentRevision Rev(RevisionStatus status, string effect) => w.New(ContentKind.Feat, "Testy Restored", World.Init(effect)) with
        {
            ContentId = contentId, Status = status,
            RevisionId = status == RevisionStatus.Published ? Guid.Parse("ffffffff-0000-4000-8000-000000000001") : Guid.Parse("00000000-0000-4000-8000-000000000001"),
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

    private static PublishManyRequest Pinned(PublishManyRequest request, PublishManyPreview preview) =>
        request with { ExpectedDependents = preview.Dependents.ToDictionary(d => d.Draft.ContentId, d => d.Draft.RevisionId) };

    [Fact]
    public void A_dependent_published_after_the_preview_refuses_the_confirmed_publish_and_writes_nothing()
    {
        // Review fix (item 9, D33): the dependents the preview showed are part of what was confirmed.
        using var w = new World();
        var leaf = w.Save(w.New(ContentKind.Feature, "Testy Leaf", World.Init("1")));
        w.Published(w.New(ContentKind.Class, "Testy Scout", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(leaf)));
        var preview = w.App.PreviewPublishMany([leaf.ContentId]);
        Assert.Single(preview.Dependents);
        w.Published(w.New(ContentKind.Class, "Testy Ranger", new HitDieEffect { Id = "hit-die", Die = 10 }, World.GrantOf(leaf))); // a new dependent, after the preview
        var count = w.RevisionCount;

        Assert.Equal("content.publish-set-changed", Code(() => w.App.PublishMany(Pinned(w.Confirmed(leaf), preview))));

        Assert.Equal(count, w.RevisionCount);
        Assert.Equal(RevisionStatus.Draft, w.Newest(leaf.ContentId).Status);
    }

    [Fact]
    public void A_dependent_with_a_newer_revision_since_the_preview_refuses_the_confirmed_publish()
    {
        using var w = new World();
        var leaf = w.Save(w.New(ContentKind.Feature, "Testy Leaf", World.Init("1")));
        var cls = w.Published(w.New(ContentKind.Class, "Testy Scout", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(leaf)));
        var preview = w.App.PreviewPublishMany([leaf.ContentId]);
        w.Published(w.Newest(cls.ContentId) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, Summary = "Testy later edit" });
        var count = w.RevisionCount;

        Assert.Equal("content.publish-set-changed", Code(() => w.App.PublishMany(Pinned(w.Confirmed(leaf), preview))));

        Assert.Equal(count, w.RevisionCount);
    }

    [Fact]
    public void The_previews_dependents_unchanged_publish_and_a_request_without_them_keeps_the_old_behaviour()
    {
        using var w = new World();
        var leaf = w.Save(w.New(ContentKind.Feature, "Testy Leaf", World.Init("1")));
        w.Published(w.New(ContentKind.Class, "Testy Scout", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(leaf)));
        var preview = w.App.PreviewPublishMany([leaf.ContentId]);

        var result = w.App.PublishMany(Pinned(w.Confirmed(leaf), preview));
        Assert.Equal(2, result.Published.Count);

        var other = w.Save(w.New(ContentKind.Feature, "Testy Other", World.Init("2")));
        w.Published(w.New(ContentKind.Class, "Testy Warden", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(other)));
        w.Published(w.New(ContentKind.Class, "Testy Keeper", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(other)));
        Assert.Equal(3, w.App.PublishMany(w.Confirmed(other)).Published.Count); // no ExpectedDependents: not compared
    }

    [Fact]
    public void A_preview_with_no_dependents_pins_none_and_a_dependent_added_later_refuses()
    {
        using var w = new World();
        var leaf = w.Save(w.New(ContentKind.Feature, "Testy Leaf", World.Init("1")));
        var preview = w.App.PreviewPublishMany([leaf.ContentId]);
        Assert.Empty(preview.Dependents);
        w.Published(w.New(ContentKind.Class, "Testy Scout", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(leaf)));

        Assert.Equal("content.publish-set-changed", Code(() => w.App.PublishMany(Pinned(w.Confirmed(leaf), preview))));
    }

    [Fact]
    public void A_dependent_that_would_fail_after_repointing_blocks_the_preview_and_the_publish_writes_nothing()
    {
        // Fix round 1 (R50, family scenario): the preview runs the real publish, so it sees what re-pointing breaks.
        using var w = new World();
        var f1 = w.Published(w.New(ContentKind.Feature, "Testy Feature", World.Init("1")));
        var cls = w.Published(w.New(ContentKind.Class, "Testy Scout", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(f1)));
        var f2 = w.Save(w.Newest(f1.ContentId) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, RulesFamilies = [RulesFamilies.Srd51] });
        var count = w.RevisionCount;

        var preview = w.App.PreviewPublishMany([f2.ContentId]);

        Assert.False(preview.CanPublish);
        var dependent = Assert.Single(preview.Dependents);
        Assert.Equal("Testy Scout", dependent.Name);
        Assert.Contains(dependent.Report.Errors, e => e.Code == "validate.reference-family");
        Assert.Empty(preview.Entries.Single().Report.Errors);
        Assert.Equal(count, w.RevisionCount); // the dry run left nothing behind

        // Bypassing the preview: the feature is published first, then the class fails, and everything rolls back.
        var refused = Assert.Throws<AppValidationException>(() => w.App.PublishMany(w.Confirmed(f2)));
        Assert.Equal("content.validation-failed", refused.Problems[0].Code);
        Assert.Contains(refused.Problems, p => p.Code == "validate.reference-family");
        Assert.Equal(count, w.RevisionCount);
        Assert.Equal(f2, w.Newest(f2.ContentId).Reference);
        Assert.Equal(RevisionStatus.Draft, w.Newest(f2.ContentId).Status);
        Assert.Equal(cls, w.Newest(cls.ContentId).Reference);
    }

    [Fact]
    public void A_class_and_a_subclass_that_share_a_scale_id_block_the_preview_and_publish_nothing()
    {
        using var w = new World();
        var cls = w.Save(w.New(ContentKind.Class, "Testy Quillmaster", new HitDieEffect { Id = "hit-die", Die = 8 },
            new ChoiceEffect { Id = "subclass", ChoiceId = "subclass", Count = 1, Options = [], Level = 3 },
            new ScaleEffect { Id = "scale-1", ScaleId = "ink", Label = "Ink", Values = [.. Enumerable.Repeat(2, 20)] }));
        var sub = w.Save(w.New(ContentKind.Subclass, "Testy Order of the Margin",
            new ScaleEffect { Id = "scale-1", ScaleId = "ink", Label = "Ink", Values = [.. Enumerable.Repeat(3, 20)] }) with { ExtendsChoice = new(cls.ContentId, "subclass") });
        var count = w.RevisionCount;

        var preview = w.App.PreviewPublishMany([sub.ContentId, cls.ContentId]);

        Assert.False(preview.CanPublish);
        Assert.Contains(preview.Entries.Single(e => e.Name == "Testy Order of the Margin").Report.Errors, e => e.Code == "validate.scale-duplicate");
        Assert.Equal(count, w.RevisionCount);
        Assert.Throws<AppValidationException>(() => w.App.PublishMany(w.Confirmed(sub, cls)));
        Assert.Equal(count, w.RevisionCount);
        Assert.Equal(RevisionStatus.Draft, w.Newest(cls.ContentId).Status);
    }

    [Fact]
    public void A_subclass_publishes_after_the_class_whose_choice_it_extends()
    {
        // Fix round 1 (item 2): extendsChoice is an ordering edge. The request lists the subclass first.
        using var w = new World();
        var cls = w.Save(w.New(ContentKind.Class, "Testy Quillmaster", new HitDieEffect { Id = "hit-die", Die = 8 },
            new ChoiceEffect { Id = "subclass", ChoiceId = "subclass", Count = 1, Options = [], Level = 3 }));
        var sub = w.Save(w.New(ContentKind.Subclass, "Testy Order of the Margin", World.Init("1")) with { ExtendsChoice = new(cls.ContentId, "subclass") });

        var preview = w.App.PreviewPublishMany([sub.ContentId, cls.ContentId]);
        Assert.True(preview.CanPublish, string.Join("; ", preview.Entries.SelectMany(e => e.Report.Errors).Select(e => e.Code)));
        Assert.Equal(["Testy Quillmaster", "Testy Order of the Margin"], preview.Entries.Select(e => e.Name));

        var result = w.App.PublishMany(w.Confirmed(sub, cls));

        Assert.Equal(["Testy Quillmaster", "Testy Order of the Margin"], result.Published.Select(p => w.App.Store.FindRevision(p.Published)!.Name));
    }

    [Fact]
    public void A_class_whose_choice_already_offers_the_extending_subclass_is_not_a_cycle()
    {
        // Fix round 2 (item 3, R58): C offers published subclass S, and S extends C's choice. A new draft of S makes C a dependent
        // that needs S; the extends edge must not make S need C as well.
        using var w = new World();
        var c0 = w.Published(w.New(ContentKind.Class, "Testy Quillmaster", new HitDieEffect { Id = "hit-die", Die = 8 },
            new ChoiceEffect { Id = "subclass", ChoiceId = "subclass", Count = 1, Options = [], Level = 3 }));
        var s0 = w.Published(w.New(ContentKind.Subclass, "Testy Order of the Margin", World.Init("1")) with { ExtendsChoice = new(c0.ContentId, "subclass") });
        w.Published(w.App.Store.FindRevision(c0)! with
        {
            RevisionId = Guid.Empty, Status = RevisionStatus.Draft,
            Effects = [new HitDieEffect { Id = "hit-die", Die = 8 }, new ChoiceEffect { Id = "subclass", ChoiceId = "subclass", Count = 1, Options = [s0], Level = 3 }],
        });
        var s1 = w.Save(w.App.Store.FindRevision(s0)! with { RevisionId = Guid.Empty, Status = RevisionStatus.Draft });

        var preview = w.App.PreviewPublishMany([s1.ContentId]);
        Assert.True(preview.CanPublish, string.Join("; ", preview.Entries.Concat(preview.Dependents).SelectMany(e => e.Report.Errors).Select(e => e.Code)));
        Assert.Equal(["Testy Quillmaster"], preview.Dependents.Select(d => d.Name));

        w.App.PublishMany(w.Confirmed(s1));

        var newSub = w.Newest(s1.ContentId).Reference;
        var option = w.Newest(c0.ContentId).Effects.OfType<ChoiceEffect>().Single().Options.Single();
        Assert.Equal(newSub, option);
        Assert.NotEqual(s0.RevisionId, option.RevisionId);
    }

    [Fact]
    public void A_failure_after_a_write_rolls_back_every_write_and_the_choice_extension_cache()
    {
        // Fix round 1 (item 3). Order: the class, then its subclass (published), then a failing class that grants the subclass.
        // Validating that last class asks for the choice extensions, so the cache is filled while the subclass is published
        // inside the transaction; after the rollback it must not return the subclass.
        using var w = new World();
        var cls = w.Save(w.New(ContentKind.Class, "Testy Quillmaster", new HitDieEffect { Id = "hit-die", Die = 8 },
            new ChoiceEffect { Id = "subclass", ChoiceId = "subclass", Count = 1, Options = [], Level = 3 }));
        var sub = w.Save(w.New(ContentKind.Subclass, "Testy Order of the Margin", World.Init("1")) with { ExtendsChoice = new(cls.ContentId, "subclass") });
        var failing = w.Save(w.New(ContentKind.Class, "Testy Broken", new HitDieEffect { Id = "hit-die", Die = 8 },
            new ChoiceEffect { Id = "pick", ChoiceId = "pick", Count = 1, Options = [] }, World.GrantOf(sub), World.Init("1 + + 2")));
        Assert.Empty(w.App.Store.ChoiceExtensions(cls.ContentId, "subclass")); // warm the cache before the call
        var count = w.RevisionCount;

        var refused = Assert.Throws<AppValidationException>(() => w.App.PublishMany(w.Confirmed(cls, sub, failing)));

        Assert.Equal("content.validation-failed", refused.Problems[0].Code);
        Assert.Equal(count, w.RevisionCount);
        Assert.Equal(cls, w.Newest(cls.ContentId).Reference); // the first entry's newest revision is still its draft
        Assert.Equal(RevisionStatus.Draft, w.Newest(cls.ContentId).Status);
        Assert.Empty(w.App.Store.ChoiceExtensions(cls.ContentId, "subclass"));
    }

    [Fact]
    public void An_old_draft_of_content_that_is_already_published_is_named_as_such()
    {
        // Fix round 1 (item 7): nothing is pending to include, so "include it" would be wrong advice.
        using var w = new World();
        var oldDraft = w.Save(w.New(ContentKind.Feature, "Testy Feature", World.Init("1")));
        w.App.Publish(oldDraft);
        var cls = w.Save(w.New(ContentKind.Class, "Testy Scout", new HitDieEffect { Id = "hit-die", Die = 8 }, World.GrantOf(oldDraft)));

        var preview = w.App.PreviewPublishMany([cls.ContentId]);

        Assert.False(preview.CanPublish);
        var error = Assert.Single(preview.Entries.Single().Report.Errors, e => e.Code == "publish.grants-unpublished");
        Assert.Equal("'Testy Scout' points at an old draft of 'Testy Feature'; choose its published revision.", error.Message);
    }

    [Fact]
    public void Three_hundred_drafts_preview_and_publish_in_one_go()
    {
        using var w = new World();
        var drafts = Enumerable.Range(0, 300).Select(i => w.Save(w.New(ContentKind.Feat, $"Testy Bulk {i:000}", World.Init("1")))).ToList();
        var ids = drafts.Select(d => d.ContentId).ToList();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var preview = w.App.PreviewPublishMany(ids);
        var previewMs = watch.ElapsedMilliseconds;
        watch.Restart();
        var result = w.App.PublishMany(new(ids, [.. drafts.Select(d => d.RevisionId)], Confirm: true));
        var publishMs = watch.ElapsedMilliseconds;

        output.WriteLine($"300 drafts: preview {previewMs} ms, publish {publishMs} ms");
        Assert.True(preview.CanPublish);
        Assert.Equal(300, result.Published.Count);
        Assert.All(ids, id => Assert.Equal(RevisionStatus.Published, w.Newest(id).Status));
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
