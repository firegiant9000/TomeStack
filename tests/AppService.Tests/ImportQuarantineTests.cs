using System.Text.Json;
using TomeStack.ImportWorker;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

public class ImportQuarantineTests
{
    [Fact]
    public void Imported_candidate_becomes_an_inactive_draft_even_with_high_confidence()
    {
        using var temp = new TempApp();
        var sourceId = Guid.Parse("5f0d5000-0000-4000-8000-000000000001");
        var candidate = new DraftCandidate(
            Guid.NewGuid(), sourceId, new PageRef(40), "Original excerpt placeholder.",
            ContentKind.Feat, "Candidate Swiftness", [RulesFamilies.Srd51],
            [new ModifierEffect { Id = "swift", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "5" }],
            Confidence: 0.99, Uncertainties: []);

        var draft = CandidateQuarantine.ToDraftRevision(candidate);
        temp.App.Store.AddRevision(draft);
        var view = temp.App.CreateCharacter(new CreateCharacterRequest(
            "Quarantine Test", RulesFamilies.Srd51, new AbilityScores(10, 10, 10, 10, 10, 10), [draft.Reference]));

        Assert.Equal(RevisionStatus.Draft, draft.Status);
        Assert.All(draft.Effects, e => Assert.Equal(AutomationStatus.Reference, e.Automation));
        Assert.Equal(0, view.Sheet.Field(CharacterCalculator.InitiativeField).Value);
        Assert.Contains(view.Sheet.Diagnostics, d => d.Code == "content.unpublished");
        Assert.DoesNotContain(temp.App.ListContent(RulesFamilies.Srd51), o => o.Reference == draft.Reference);
    }

    [Fact]
    public void A_stored_candidates_versioned_effects_stay_reference_only_in_the_draft_and_after_it_is_read_back()
    {
        // M4 D4 review: a candidate read back from storage (or sent by the UI) carries weapon, armor and spell effects as
        // unknown effects, because those types are typed only inside a revision of their schema version (ADR-003). The
        // quarantine must still give typed, reference-only effects, also after the draft is stored and read again.
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Quarantine Book", [RulesFamilies.Srd521]));
        var original = new DraftCandidate(
            Guid.NewGuid(), source.Id, new PageRef(4), "Original excerpt placeholder.", ContentKind.Item, "Candidate Hookblade", [RulesFamilies.Srd521],
            [new WeaponEffect { Id = "weapon", Category = WeaponCategory.Martial, Attack = WeaponAttack.Melee, Damage = "1d8", DamageType = "slashing", WeaponKey = "candidate-hookblade" }],
            Confidence: 0.9, Uncertainties: []);
        var stored = JsonSerializer.Deserialize<DraftCandidate>(JsonSerializer.Serialize(original, RulesJson.Compact), RulesJson.Compact)!;
        Assert.IsType<UnknownEffect>(Assert.Single(stored.ProposedEffects)); // how it arrives

        var draft = CandidateQuarantine.ToDraftRevision(stored);
        var typed = Assert.IsType<WeaponEffect>(Assert.Single(draft.Effects));
        Assert.Equal(AutomationStatus.Reference, typed.Automation);

        var reference = temp.App.SaveDraft(draft);
        Assert.Equal(AutomationStatus.Reference, Assert.IsType<WeaponEffect>(Assert.Single(temp.App.Store.FindRevision(reference)!.Effects)).Automation);
        var published = temp.App.Publish(reference).Published;
        Assert.Equal(AutomationStatus.Reference, Assert.Single(temp.App.Store.FindRevision(published)!.Effects).Automation);
    }
}
