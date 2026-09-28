using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>M3 B1: the mechanics inventory classifies every effect of the chosen sources and flags a gap without instructions.</summary>
public class MechanicsInventoryTests
{
    private static readonly Guid Homebrew = Guid.Parse("5f7d5000-0000-4000-8000-000000000001");

    [Fact]
    public void Every_effect_is_classified_and_an_unsupported_one_without_text_is_a_gap()
    {
        var feat = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = "Test Gap Feat",
            RulesFamilies = [RulesFamilies.Srd521], Provenance = new(Homebrew, new(4)), Status = RevisionStatus.Published,
            Effects =
            [
                new ModifierEffect { Id = "bonus", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" },
                new RollEffect { Id = "silent", RollId = "silent", Label = "Silent roll", Dice = "1d4", Automation = AutomationStatus.Assisted, Timing = EffectTiming.OnRoll },
            ],
        };
        var source = new SourceRecord
        {
            Id = Homebrew, Title = "Test Homebrew", Publisher = "Personal homebrew", RulesFamilies = [RulesFamilies.Srd521],
            EditionVersion = "homebrew", License = "Personal homebrew", Redistributable = false,
        };
        var pack = Fixtures.Pack();
        var catalog = new InMemoryContentCatalog([.. pack.Sources, source], [.. pack.Revisions, feat]);
        var character = Fixtures.Srd521Character() with { Pins = [.. Fixtures.Srd521Character().Pins, feat.Reference] };

        var report = MechanicsInventory.Of(CharacterCalculator.Calculate(character, catalog), new HashSet<Guid> { Homebrew });

        Assert.Equal([("bonus", AutomationStatus.Automatic, false), ("silent", AutomationStatus.Assisted, true)],
            report.Mechanics.Select(m => (m.EffectId!, m.Automation, m.MissingManualStep)));
        Assert.True(report.HasModifier);
        Assert.False(report.HasLimitedUseAction); // the roll names no resource
        Assert.Empty(report.Unsupported);
        Assert.Empty(MechanicsInventory.Of(CharacterCalculator.Calculate(character, catalog), new HashSet<Guid>()).Mechanics); // other sources only
    }
}
