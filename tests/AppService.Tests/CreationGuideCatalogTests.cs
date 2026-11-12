using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>Content v13 (ADR-024): the catalogue's guide lookup, one guide per target, and guides kept out of the content list.</summary>
public class CreationGuideCatalogTests
{
    private static SourceRecord Source(TempApp app) =>
        app.App.CreateHomebrewSource(new("Guide Catalog Homebrew", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));

    private static ContentRevision Background(SourceRecord source, string name) => new()
    {
        ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, SchemaVersion = 13, Kind = ContentKind.Background, Name = name, RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(source.Id), Status = RevisionStatus.Draft, Effects = [],
    };

    private static ContentRevision Guide(SourceRecord source, string name, Guid target, Guid? contentId = null) => new()
    {
        ContentId = contentId ?? Guid.NewGuid(), RevisionId = Guid.Empty, SchemaVersion = 13, Kind = ContentKind.CreationGuide, Name = name,
        RulesFamilies = [RulesFamilies.Srd51], Provenance = new(source.Id), Status = RevisionStatus.Draft,
        Effects = [new GuideForEffect { Id = "for", Content = new GuideTarget(target) }, new PrimaryAbilitiesEffect { Id = "primary", Abilities = ["wis"] }],
    };

    private static ContentReference Publish(TempApp app, ContentRevision draft) => app.App.Publish(app.App.SaveDraft(draft)).Published;

    private static IContentCatalog Catalog(TempApp app) => app.App.Store;

    private static SourceRecord BatchSource() => new()
    {
        Id = Guid.NewGuid(), Title = "Batch fixture", Publisher = "Fixture", RulesFamilies = [RulesFamilies.Srd51], EditionVersion = "1", License = "Original", Redistributable = true,
    };

    [Fact]
    public void A_published_guide_is_found_by_its_target_only()
    {
        using var app = new TempApp();
        var source = Source(app);
        var sage = Publish(app, Background(source, "Guide Catalog Sage"));
        var scout = Publish(app, Background(source, "Guide Catalog Scout"));
        var guide = Publish(app, Guide(source, "Sage guide", sage.ContentId));

        Assert.Equal([guide], Catalog(app).GuidesFor(sage.ContentId).Select(r => r.Reference));
        Assert.Empty(Catalog(app).GuidesFor(scout.ContentId));
    }

    [Fact]
    public void A_draft_guide_is_not_returned()
    {
        using var app = new TempApp();
        var source = Source(app);
        var sage = Publish(app, Background(source, "Guide Catalog Sage"));
        app.App.SaveDraft(Guide(source, "Draft guide", sage.ContentId));

        Assert.Empty(Catalog(app).GuidesFor(sage.ContentId));
    }

    [Fact]
    public void A_new_revision_of_the_same_guide_replaces_it()
    {
        using var app = new TempApp();
        var source = Source(app);
        var sage = Publish(app, Background(source, "Guide Catalog Sage"));
        var first = Publish(app, Guide(source, "Sage guide", sage.ContentId));
        Assert.Single(Catalog(app).GuidesFor(sage.ContentId));

        var second = Publish(app, Guide(source, "Sage guide, second", sage.ContentId, first.ContentId));

        Assert.Equal([second], Catalog(app).GuidesFor(sage.ContentId).Select(r => r.Reference));
    }

    [Fact]
    public void After_a_new_revision_retargets_the_old_target_returns_nothing()
    {
        using var app = new TempApp();
        var source = Source(app);
        var sage = Publish(app, Background(source, "Guide Catalog Sage"));
        var scout = Publish(app, Background(source, "Guide Catalog Scout"));
        var first = Publish(app, Guide(source, "Sage guide", sage.ContentId));
        Assert.Single(Catalog(app).GuidesFor(sage.ContentId));

        var moved = Publish(app, Guide(source, "Scout guide", scout.ContentId, first.ContentId));

        Assert.Empty(Catalog(app).GuidesFor(sage.ContentId));
        Assert.Equal([moved], Catalog(app).GuidesFor(scout.ContentId).Select(r => r.Reference));
    }

    [Fact]
    public void A_second_guide_for_the_same_target_fails_to_publish()
    {
        using var app = new TempApp();
        var source = Source(app);
        var sage = Publish(app, Background(source, "Guide Catalog Sage"));
        Publish(app, Guide(source, "Sage guide", sage.ContentId));

        var saved = app.App.SaveDraft(Guide(source, "Another sage guide", sage.ContentId));

        var error = Assert.Throws<AppValidationException>(() => app.App.Publish(saved));
        Assert.Contains(error.Problems, d => d.Code == "validate.guide-duplicate");
    }

    [Fact]
    public void A_new_revision_of_the_same_guide_is_not_a_duplicate()
    {
        using var app = new TempApp();
        var source = Source(app);
        var sage = Publish(app, Background(source, "Guide Catalog Sage"));
        var first = Publish(app, Guide(source, "Sage guide", sage.ContentId));

        var report = app.App.ValidateContent(null, Guide(source, "Sage guide again", sage.ContentId, first.ContentId));

        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.guide-duplicate");
    }

    [Fact]
    public void Two_guides_for_one_target_in_one_batch_fail()
    {
        var source = BatchSource();
        var target = Background(source, "Batch sage") with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };
        var catalog = new InMemoryContentCatalog([source], [target]);
        var one = Guide(source, "One", target.ContentId) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };
        var two = Guide(source, "Two", target.ContentId) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };

        Assert.Contains(ContentValidator.Validate(one, catalog, [one, two]).Errors, e => e.Code == "validate.guide-duplicate");
        Assert.Contains(ContentValidator.Validate(two, catalog, [one, two]).Errors, e => e.Code == "validate.guide-duplicate");
        Assert.DoesNotContain(ContentValidator.Validate(one, catalog, [one]).Errors, e => e.Code == "validate.guide-duplicate");
    }

    [Fact]
    public void A_batch_revision_that_retargets_a_stored_guide_frees_its_old_target()
    {
        var source = BatchSource();
        var sage = Background(source, "Batch sage") with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };
        var scout = Background(source, "Batch scout") with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };
        var stored = Guide(source, "Stored", sage.ContentId) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };
        var catalog = new InMemoryContentCatalog([source], [sage, scout, stored]);
        var retargeted = Guide(source, "Stored", scout.ContentId, stored.ContentId) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };
        var newcomer = Guide(source, "Newcomer", sage.ContentId) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };

        Assert.DoesNotContain(ContentValidator.Validate(newcomer, catalog, [retargeted, newcomer]).Errors, e => e.Code == "validate.guide-duplicate");
        Assert.Contains(ContentValidator.Validate(newcomer, catalog).Errors, e => e.Code == "validate.guide-duplicate");
    }

    [Fact]
    public void ListContent_omits_guides()
    {
        using var app = new TempApp();
        var source = Source(app);
        var sage = Publish(app, Background(source, "Guide Catalog Sage"));
        var guide = Publish(app, Guide(source, "Sage guide", sage.ContentId));

        var listed = app.App.ListContent(RulesFamilies.Srd51);

        Assert.Contains(listed, o => o.Reference == sage);
        Assert.DoesNotContain(listed, o => o.Kind == ContentKind.CreationGuide || o.Reference == guide);
    }
}
