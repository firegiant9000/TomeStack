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

    [Fact]
    public void A_confirmed_effect_keeps_its_automation_after_read_back_and_every_other_effect_is_reference()
    {
        // Item 11 (D34, ADR-016): the counterpart of the test above. A class candidate read back from storage carries its
        // spellcasting effect as an unknown effect; confirming it by id keeps it automatic in the draft and after the draft is
        // stored and read again. The hit die, not confirmed, is reference. The one-argument conversion is unchanged.
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Confirm Book", [RulesFamilies.Srd521]));
        var original = new DraftCandidate(
            Guid.NewGuid(), source.Id, new PageRef(7), "Original excerpt placeholder.", ContentKind.Class, "Candidate Lampwright", [RulesFamilies.Srd521],
            [
                new HitDieEffect { Id = "hit-die", Die = 8 },
                new SpellcastingEffect { Id = "spellcasting", Ability = Ability.Wis, SpellList = "candidate-lampwright", Slots = [[2], [3]] },
            ],
            Confidence: 0.7, Uncertainties: []);
        var stored = JsonSerializer.Deserialize<DraftCandidate>(JsonSerializer.Serialize(original, RulesJson.Compact), RulesJson.Compact)!;
        Assert.Contains(stored.ProposedEffects, e => e is UnknownEffect); // how the spellcasting effect arrives

        var draft = CandidateQuarantine.ToDraftRevision(stored, ["spellcasting"]);
        Assert.Equal(AutomationStatus.Automatic, Assert.IsType<SpellcastingEffect>(draft.Effects.Single(e => e.Id == "spellcasting")).Automation);
        Assert.Equal(AutomationStatus.Reference, Assert.IsType<HitDieEffect>(draft.Effects.Single(e => e.Id == "hit-die")).Automation);

        var reference = temp.App.SaveDraft(draft);
        var read = temp.App.Store.FindRevision(reference)!;
        Assert.Equal(AutomationStatus.Automatic, Assert.IsType<SpellcastingEffect>(read.Effects.Single(e => e.Id == "spellcasting")).Automation);
        Assert.Equal(AutomationStatus.Reference, read.Effects.Single(e => e.Id == "hit-die").Automation);

        // The one-argument conversion has no confirmation: everything is reference.
        Assert.All(CandidateQuarantine.ToDraftRevision(stored).Effects, e => Assert.Equal(AutomationStatus.Reference, e.Automation));
        Assert.All(CandidateQuarantine.ToDraftRevision(stored, []).Effects, e => Assert.Equal(AutomationStatus.Reference, e.Automation));
    }
}
