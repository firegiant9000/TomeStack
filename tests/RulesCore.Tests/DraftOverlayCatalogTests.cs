using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M5 slice 3 (B03, LIVING_SPECS D14): the sandbox overlay. One draft calculates as if published, in memory, for one
/// calculation; the catalog underneath never sees it change. Original content (prefix 5fd1) on the Chronicler source.
/// </summary>
public class DraftOverlayCatalogTests
{
    private static readonly Guid Source = Guid.Parse("5fc05000-0000-4000-8000-000000000001");

    private static ContentReference Ref(int n, int revision = 0) =>
        new(Guid.Parse($"5fd1c000-0000-4000-8000-{n:D12}"), Guid.Parse($"5fd1e{revision:D3}-0000-4000-8000-{n:D12}"));

    private static ContentRevision Revision(ContentReference reference, ContentKind kind, RevisionStatus status, params Effect[] effects) => new()
    {
        ContentId = reference.ContentId,
        RevisionId = reference.RevisionId,
        Kind = kind,
        Name = $"Overlay {kind} {reference.RevisionId.ToString()[..8]}",
        RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
        Provenance = new(Source),
        Status = status,
        Effects = effects,
    };

    private static ModifierEffect Initiative(string value) => new() { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = value };

    private static Character Blank(string family, params ClassLevel[] classes) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Overlay Tester",
        RulesFamily = family,
        BaseAbilities = new(10, 10, 10, 10, 10, 10),
        Classes = classes,
        Level = classes.Sum(c => c.Level),
    };

    [Fact]
    public void A_draft_class_calculates_only_through_the_overlay_and_the_catalog_underneath_is_untouched()
    {
        var draft = Revision(Ref(1), ContentKind.Class, RevisionStatus.Draft, new HitDieEffect { Id = "hd", Die = 10 }, Initiative("CLASS_LEVEL"));
        var pack = Fixtures.ChroniclerPack();
        var catalog = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, draft]);
        foreach (var family in new[] { RulesFamilies.Srd51, RulesFamilies.Srd521 })
        {
            var character = Blank(family, new ClassLevel(draft.Reference, 4));

            var plain = CharacterCalculator.Calculate(character, catalog);
            Assert.Contains(plain.Diagnostics, d => d.Code == "content.unpublished" && d.Content == draft.Reference);
            Assert.Equal(0, plain.Field(FieldIds.Initiative).Value);

            var overlay = new DraftOverlayCatalog(catalog, draft);
            var tried = CharacterCalculator.Calculate(character, overlay);
            Assert.DoesNotContain(tried.Diagnostics, d => d.Code == "content.unpublished");
            Assert.Equal(4, tried.Field(FieldIds.Initiative).Value);
            Assert.Equal(10 + (6 * 3), tried.Field(FieldIds.HitPoints).Value); // d10: 10, then 6 per level, Con +0

            Assert.Equal(RevisionStatus.Draft, catalog.FindRevision(draft.Reference)!.Status);
            Assert.Equal(RevisionStatus.Draft, draft.Status);
        }
    }

    [Fact]
    public void A_draft_subclass_replaces_its_published_revisions_in_the_choice_it_extends()
    {
        var cls = Revision(Ref(1), ContentKind.Class, RevisionStatus.Published, new HitDieEffect { Id = "hd", Die = 8 }, new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3 });
        var published = Revision(Ref(2, 1), ContentKind.Subclass, RevisionStatus.Published, Initiative("1")) with { ExtendsChoice = new(cls.ContentId, "path") };
        var draft = Revision(Ref(2, 2), ContentKind.Subclass, RevisionStatus.Draft, Initiative("5")) with { ExtendsChoice = new(cls.ContentId, "path") };
        var other = Revision(Ref(3), ContentKind.Subclass, RevisionStatus.Published) with { ExtendsChoice = new(cls.ContentId, "path") };
        var pack = Fixtures.ChroniclerPack();
        var catalog = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, cls, published, draft, other]);
        var overlay = new DraftOverlayCatalog(catalog, draft);

        Assert.Equal([other.Reference, draft.Reference], overlay.ChoiceExtensions(cls.ContentId, "path").Select(r => r.Reference));
        Assert.Equal([published.Reference, other.Reference], catalog.ChoiceExtensions(cls.ContentId, "path").Select(r => r.Reference));
        Assert.Contains(overlay.RevisionsOf(draft.ContentId), r => r.Reference == draft.Reference && r.Status == RevisionStatus.Published);

        var character = Blank(RulesFamilies.Srd521, new ClassLevel(cls.Reference, 3)) with { Choices = [new(cls.Reference, "path", [draft.Reference])] };
        Assert.Equal(5, CharacterCalculator.Calculate(character, overlay).Field(FieldIds.Initiative).Value);
        // Without the overlay a draft is never offered in the choice, so the selection is refused.
        var plain = CharacterCalculator.Calculate(character, catalog);
        Assert.Contains(plain.Diagnostics, d => d.Code == "choice.invalid-option" && d.Content == draft.Reference);
        Assert.Equal(0, plain.Field(FieldIds.Initiative).Value);
    }
}
