using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>M2 item 4: <c>armor</c> effects are validated before publishing (SPEC Q-02).</summary>
public class ArmorValidationTests
{
    private static ValidationReport Validate(ContentKind kind, params Effect[] effects)
    {
        var pack = Fixtures.Pack();
        var revision = new ContentRevision
        {
            ContentId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid(),
            Kind = kind,
            Name = "Test Armor",
            RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(Fixtures.SourceShared),
            Status = RevisionStatus.Draft,
            Effects = effects,
        };
        return ContentValidator.Validate(revision, new InMemoryContentCatalog(pack.Sources, pack.Revisions));
    }

    private static ArmorEffect Armor(ArmorCategory category, int ac, int? cap = null, string id = "armor") =>
        new() { Id = id, Category = category, ArmorClass = ac, DexterityCap = cap };

    [Fact]
    public void Valid_armor_and_shield_on_an_item_publish()
    {
        var report = Validate(ContentKind.Item, Armor(ArmorCategory.Medium, 14, 2), Armor(ArmorCategory.Shield, 2, id: "shield"));

        Assert.True(report.CanPublish, string.Join("; ", report.Errors.Select(e => e.Message)));
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Out_of_range_values_misplaced_caps_and_duplicates_are_errors()
    {
        Assert.Contains(Validate(ContentKind.Item, Armor(ArmorCategory.Light, 31)).Errors, e => e.Code == "validate.armor-class");
        Assert.Contains(Validate(ContentKind.Item, Armor(ArmorCategory.Light, 11, cap: 2)).Errors, e => e.Code == "validate.armor-dexterity-cap");
        Assert.Contains(Validate(ContentKind.Item, Armor(ArmorCategory.Light, 11), Armor(ArmorCategory.Heavy, 16, id: "b")).Errors, e => e.Code == "validate.armor-duplicate");
        Assert.Contains(Validate(ContentKind.Feat, Armor(ArmorCategory.Light, 11)).Warnings, w => w.Code == "validate.armor-kind");
    }
}
