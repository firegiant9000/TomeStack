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

    [Fact]
    public void Coverage_shows_no_row_for_a_guide_published_or_draft()
    {
        using var app = new TempApp();
        var source = Source(app);
        var sage = Publish(app, Background(source, "Guide Catalog Sage"));
        Publish(app, Guide(source, "Sage guide", sage.ContentId));
        var scout = Publish(app, Background(source, "Guide Catalog Scout"));
        app.App.SaveDraft(Guide(source, "Scout draft guide", scout.ContentId));

        var report = app.App.Coverage(new CoverageRequest(source.Id, IncludeDrafts: true));

        var rows = report.Sources.Single().Other;
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.Kind == ContentKind.CreationGuide);
    }

    [Fact]
    public void Guides_in_different_non_overlapping_families_for_one_target_are_both_allowed()
    {
        using var app = new TempApp();
        var source = app.App.CreateHomebrewSource(new("Guide Both Families", [RulesFamilies.Srd51, RulesFamilies.Srd521], Redistributable: true, ConfirmOwnWork: true));
        var sage = Publish(app, Background(source, "Guide Catalog Sage") with { RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521] });
        var older = Publish(app, Guide(source, "Older rules guide", sage.ContentId) with { RulesFamilies = [RulesFamilies.Srd51] });
        var newer = Guide(source, "Newer rules guide", sage.ContentId) with { RulesFamilies = [RulesFamilies.Srd521] };

        Assert.DoesNotContain(app.App.ValidateContent(null, newer).Errors, e => e.Code == "validate.guide-duplicate");
        var published = Publish(app, newer);

        var found = Catalog(app).GuidesFor(sage.ContentId).Select(r => r.Reference).ToList();
        Assert.Equal(2, found.Count);
        Assert.Contains(older, found);
        Assert.Contains(published, found);
    }

    [Fact]
    public void A_draft_guide_that_duplicates_a_published_one_is_reported_and_cannot_publish()
    {
        using var app = new TempApp();
        var source = Source(app);
        var sage = Publish(app, Background(source, "Guide Catalog Sage"));
        var rival = Publish(app, Guide(source, "Sage guide", sage.ContentId));
        var draft = Guide(source, "Rival draft", sage.ContentId);

        Assert.Contains(app.App.ValidateContent(null, draft).Errors, e => e.Code == "validate.guide-duplicate");

        var saved = app.App.SaveDraft(draft);
        var error = Assert.Throws<AppValidationException>(() => app.App.Publish(saved));
        Assert.Contains(error.Problems, d => d.Code == "validate.guide-duplicate");
        Assert.Equal([rival], Catalog(app).GuidesFor(sage.ContentId).Select(r => r.Reference));
        Assert.Equal(RevisionStatus.Draft, Catalog(app).FindRevision(saved)!.Status);
    }

    /// <summary>A package from an origin app with two published backgrounds; the base the import tests build on.</summary>
    private static (TempApp Origin, SourceRecord Source, ContentReference Sage, ContentReference Scout, byte[] Base) OriginWithTargets()
    {
        var origin = new TempApp();
        var source = Source(origin);
        var sage = Publish(origin, Background(source, "Guide Catalog Sage"));
        var scout = Publish(origin, Background(source, "Guide Catalog Scout"));
        return (origin, source, sage, scout, origin.App.ExportSourcePack([source.Id]).Content);
    }

    [Fact]
    public void Importing_a_package_guide_for_a_target_with_a_local_guide_is_refused()
    {
        var (origin, _, sage, _, basePack) = OriginWithTargets();
        using var _o = origin;
        using var local = new TempApp();
        local.App.ApplyImport(basePack);
        Publish(local, Guide(Source(local), "Local sage guide", sage.ContentId));
        var otherSource = Source(origin);
        Publish(origin, Guide(otherSource, "Packaged sage guide", sage.ContentId));
        var package = origin.App.ExportSourcePack([otherSource.Id]).Content;

        var preview = local.App.PreviewImport(package);

        Assert.Contains(preview.Errors, e => e.Code == "validate.guide-duplicate");
        Assert.False(preview.CanApply);
    }

    [Fact]
    public void Package_guides_answer_for_their_target_and_a_package_retarget_frees_the_old_one()
    {
        var (origin, source, sage, scout, basePack) = OriginWithTargets();
        using var _o = origin;
        var first = Publish(origin, Guide(source, "Sage guide", sage.ContentId));
        using var local = new TempApp();
        local.App.ApplyImport(origin.App.ExportSourcePack([source.Id]).Content);
        Assert.Equal([first], Catalog(local).GuidesFor(sage.ContentId).Select(r => r.Reference));

        // The origin moves its guide to the other target and adds a new guide for the freed one.
        Publish(origin, Guide(source, "Scout guide", scout.ContentId, first.ContentId));
        var successor = Publish(origin, Guide(source, "New sage guide", sage.ContentId));
        var preview = local.App.PreviewImport(origin.App.ExportSourcePack([source.Id]).Content);

        Assert.DoesNotContain(preview.Errors, e => e.Code == "validate.guide-duplicate");
        Assert.True(preview.CanApply);
        local.App.ApplyImport(origin.App.ExportSourcePack([source.Id]).Content);
        Assert.Equal([successor], Catalog(local).GuidesFor(sage.ContentId).Select(r => r.Reference));
        Assert.Equal(first.ContentId, Catalog(local).GuidesFor(scout.ContentId).Single().ContentId);
        Assert.NotEmpty(basePack);
    }
}
