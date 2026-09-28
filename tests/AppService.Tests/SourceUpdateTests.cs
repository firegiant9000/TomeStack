using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M3 C7 (ROADMAP M3 "source updates", SPEC I-06): a new revision of a bundled or homebrew source is offered to the
/// characters that use the older one (<c>character.updates</c>), and only a reviewed, confirmed update moves them. Nothing
/// changes by itself: not when the revision arrives, not on a restart. Original fixtures only.
/// </summary>
public class SourceUpdateTests
{
    private static readonly ContentReference ArcanistV1 = new(Guid.Parse("5f5dc000-0000-4000-8000-000000000001"), Guid.Parse("5f5de000-0000-4000-8000-000000000001"));
    private static readonly ContentReference ArcanistV2 = new(ArcanistV1.ContentId, Guid.Parse("5f5de700-0000-4000-8000-000000000001"));
    private static readonly ContentReference Chanter = new(Guid.Parse("5f5dc000-0000-4000-8000-000000000002"), Guid.Parse("5f5de000-0000-4000-8000-000000000002"));

    [Fact]
    public void A_new_revision_of_a_bundled_source_is_offered_and_applies_only_after_review_and_confirmation()
    {
        using var temp = new TempApp();
        var character = temp.App.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = "Test Updating Caster", RulesFamily = RulesFamilies.Srd521, Level = 5,
            Classes = [new(ArcanistV1, 3), new(Chanter, 2)], BaseAbilities = new(10, 12, 14, 16, 10, 14),
        });
        Assert.Empty(temp.App.AvailableUpdates(character.Character.Id));
        var slotsBefore = character.Sheet.Field(FieldIds.SpellSlots(1));
        Assert.Equal(AutomationStatus.Assisted, slotsBefore.Automation); // v1 does not say how it combines

        // A newer build ships a second revision of the pack's Fixture Arcanist (like the SRD v7 casters, M3 C3).
        temp.AddPack("fixture-pack-m3-source-update.json");
        var json = TempApp.Json(temp.App.GetCharacter(character.Character.Id).Character);

        var offer = Assert.Single(temp.App.AvailableUpdates(character.Character.Id));
        Assert.Equal((ArcanistV1, ArcanistV2, ReferenceRole.Class, true), (offer.From, offer.To, offer.Role, offer.Bundled));
        Assert.Equal("Fixture Arcanist", offer.Name);
        // Offered, not applied: the character is unchanged, also after a restart.
        Assert.Equal(json, TempApp.Json(temp.App.GetCharacter(character.Character.Id).Character));
        temp.Reopen();
        Assert.Equal(json, TempApp.Json(temp.App.GetCharacter(character.Character.Id).Character));
        Assert.Equal(ArcanistV2, Assert.Single(temp.App.AvailableUpdates(character.Character.Id)).To);

        // The review shows what changes; applying needs confirmation.
        var review = temp.App.ReviewUpdate(character.Character.Id, offer.From, offer.To);
        Assert.Contains(review.Mechanics.Effects, e => e.EffectId == "arcanist-spellcasting" && e.Change == ChangeKind.Changed);
        Assert.Equal(json, TempApp.Json(temp.App.GetCharacter(character.Character.Id).Character));
        var refused = Assert.Throws<AppValidationException>(() => temp.App.ApplyUpdate(character.Character.Id, offer.From, offer.To, confirm: false));
        Assert.Equal("update.confirmation-required", Assert.Single(refused.Problems).Code);

        var updated = temp.App.ApplyUpdate(character.Character.Id, offer.From, offer.To, confirm: true);
        Assert.Equal(ArcanistV2, updated.Character.Classes[0].Class);
        Assert.Empty(temp.App.AvailableUpdates(character.Character.Id));
        // The Chanter still has no multiclassCaster, so the slot total stays a manual step: the update changed only what it said.
        Assert.Equal(AutomationStatus.Assisted, updated.Sheet.Field(FieldIds.SpellSlots(1)).Automation);
    }

    [Fact]
    public void A_new_revision_of_a_homebrew_source_is_offered_and_a_revision_for_another_family_is_not()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Update Homebrew", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        var contentId = Guid.NewGuid();
        ContentReference Publish(string summary, params string[] families) =>
            temp.App.Publish(temp.App.SaveDraft(new ContentRevision
            {
                ContentId = contentId, RevisionId = Guid.Empty, Kind = ContentKind.Feat, Name = "Test Fixture Knack",
                RulesFamilies = families, Provenance = new(source.Id, new PageRef(1)), Status = RevisionStatus.Draft, Summary = summary,
                Effects = [new ModifierEffect { Id = "knack", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = summary.Length > 10 ? "2" : "1" }],
            })).Published;
        var first = Publish("Knack one", RulesFamilies.Srd51, RulesFamilies.Srd521);
        var fixture = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        var brenna = temp.App.SaveCharacter(fixture with { Pins = [.. fixture.Pins, first] });
        var initiative = brenna.Sheet.Field(FieldIds.Initiative).Value;
        IReadOnlyList<UpdateOffer> Knack() => [.. temp.App.AvailableUpdates(brenna.Character.Id).Where(o => o.From.ContentId == contentId)];

        // Brenna's M1 SRD Barbarian has newer bundled revisions (M2 and M3 C3), and they are offered too, never applied.
        Assert.Contains(temp.App.AvailableUpdates(brenna.Character.Id), o => o.Role == ReferenceRole.Class && o.Bundled && o.Name == "Barbarian");

        // A 2014-only revision is newer, but the 2024 character cannot adopt it: no offer.
        Publish("Knack for 2014 only", RulesFamilies.Srd51);
        Assert.Empty(Knack());

        var second = Publish("Knack two, stronger", RulesFamilies.Srd51, RulesFamilies.Srd521);
        var offer = Assert.Single(Knack());
        Assert.Equal((first, second, ReferenceRole.Pin, false, "Test Update Homebrew"), (offer.From, offer.To, offer.Role, offer.Bundled, offer.SourceTitle));
        Assert.Equal(initiative, temp.App.GetCharacter(brenna.Character.Id).Sheet.Field(FieldIds.Initiative).Value);

        var updated = temp.App.ApplyUpdate(brenna.Character.Id, offer.From, offer.To, confirm: true);
        Assert.Equal(initiative + 1, updated.Sheet.Field(FieldIds.Initiative).Value);
        Assert.Empty(Knack());
    }

    [Fact]
    public void The_command_is_read_only_and_refuses_an_unknown_character()
    {
        using var temp = new TempApp();
        var refused = Assert.Throws<AppValidationException>(() => temp.App.AvailableUpdates(Guid.NewGuid()));
        Assert.Equal("character.not-found", Assert.Single(refused.Problems).Code);
        Assert.Contains("character.updates", CommandDispatcher.Commands);
    }
}
