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
            [new Effect { Id = "swift", Type = Effect.InitiativeBonus, Amount = 5 }],
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
}
