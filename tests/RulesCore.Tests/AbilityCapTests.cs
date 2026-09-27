using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M2 item 4 (owner decision 2026-09-27): SRD ability score increases cannot raise a score above 20, in both families.
/// Bonuses stop at 20; a score already above 20 is not raised further but not lowered; set effects may exceed 20.
/// </summary>
public class AbilityCapTests
{
    private static readonly ContentReference Feat = new(Guid.Parse("5f5dc000-0000-4000-8000-000000000001"), Guid.Parse("5f5de000-0000-4000-8000-000000000001"));

    private static DerivedValue Strength(string family, int baseStr, params Effect[] effects)
    {
        var pack = Fixtures.Pack();
        var feat = new ContentRevision
        {
            ContentId = Feat.ContentId,
            RevisionId = Feat.RevisionId,
            Kind = ContentKind.Feat, // feats may raise scores in both families
            Name = "Test Might",
            RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
            Provenance = new(Fixtures.SourceShared),
            Status = RevisionStatus.Published,
            Effects = effects,
        };
        var fixture = family == RulesFamilies.Srd51 ? Fixtures.Srd51Character() : Fixtures.Srd521Character();
        var character = fixture with { Pins = [.. fixture.Pins, Feat], BaseAbilities = fixture.BaseAbilities with { Str = baseStr } };
        return CharacterCalculator.Calculate(character, new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, feat])).Field(FieldIds.Score(Ability.Str));
    }

    private static ModifierEffect Bonus(string id, int amount) => new() { Id = id, Operation = ModifierOperation.Bonus, Target = FieldIds.Score(Ability.Str), Value = $"{amount}" };

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void Bonuses_stop_at_20_in_both_families(string family)
    {
        Assert.Equal(20, Strength(family, 18, Bonus("a", 1), Bonus("b", 2)).Value); // 19, then 21 → 20
        Assert.Equal(19, Strength(family, 18, Bonus("a", 1)).Value);
    }

    [Fact]
    public void A_score_already_above_20_is_not_raised_by_a_bonus_or_lowered_by_the_cap()
    {
        var strength = Strength(RulesFamilies.Srd521, 22, Bonus("a", 2));

        Assert.Equal(22, strength.Value);
        Assert.Equal(0, strength.Trace.Single(t => t.Description.Contains("capped", StringComparison.Ordinal)).Amount);
    }

    [Fact]
    public void Penalties_and_set_effects_are_not_capped()
    {
        var set = new ModifierEffect { Id = "set", Operation = ModifierOperation.Set, Target = FieldIds.Score(Ability.Str), Value = "23" };

        Assert.Equal(23, Strength(RulesFamilies.Srd51, 18, Bonus("a", 4), set).Value);
        Assert.Equal(17, Strength(RulesFamilies.Srd51, 18, Bonus("a", -1)).Value);
    }
}
