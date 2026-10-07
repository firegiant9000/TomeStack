namespace TomeStack.RulesCore.Tests;

/// <summary>D24 (owner, 2026-10-06): passive scores and a speed field as calculated data.</summary>
public class PassiveAndSpeedTests
{
    [Fact]
    public void Passive_scores_are_ten_plus_the_skill_with_a_trace_that_says_so()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        foreach (var key in new[] { "perception", "insight", "investigation" })
        {
            var passive = sheet.Field(FieldIds.Passive(key));
            Assert.Equal(10 + sheet.Field(FieldIds.Skill(key)).Value, passive.Value);
            Assert.Equal("score", passive.Units);
            Assert.StartsWith("Passive ", passive.Label);
            Assert.Contains(passive.Trace, s => s.Operation == "base" && s.Description.Contains("10 +"));
        }
    }

    [Fact]
    public void A_content_bonus_on_a_passive_applies_like_any_field()
    {
        var observant = new ContentRevision
        {
            ContentId = Guid.Parse("a1000000-0000-4000-8000-000000000001"), RevisionId = Guid.Parse("b1000000-0000-4000-8000-000000000001"),
            Kind = ContentKind.Feat, Name = "Fixture Watchfulness", RulesFamilies = [RulesFamilies.Srd51], Provenance = new(Fixtures.SourceShared, new PageRef(31)),
            Status = RevisionStatus.Published,
            Effects = [new ModifierEffect { Id = "pp", Operation = ModifierOperation.Bonus, Target = FieldIds.Passive("perception"), Value = "5" }],
        };
        var pack = Fixtures.Pack();
        var catalog = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, observant]);
        var character = Fixtures.Srd51Character();
        var sheet = CharacterCalculator.Calculate(character with { Pins = [.. character.Pins, observant.Reference] }, catalog);
        Assert.Equal(15 + sheet.Field(FieldIds.Skill("perception")).Value, sheet.Field(FieldIds.Passive("perception")).Value);
    }

    [Fact]
    public void Speed_starts_at_thirty_feet_by_rules_policy_and_can_be_overridden()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        var speed = sheet.Field(FieldIds.Speed);
        Assert.Equal((30, "feet"), (speed.Value, speed.Units));
        Assert.Equal(TraceOriginKind.RulesPolicy, speed.Trace[0].Origin.Kind);
        var dwarf = CharacterCalculator.Calculate(Fixtures.Srd51Character() with { Overrides = [new(FieldIds.Speed, 25, "Dwarf")] }, Fixtures.Catalog());
        Assert.Equal((25, 30), (dwarf.Field(FieldIds.Speed).Value, dwarf.Field(FieldIds.Speed).ComputedValue));
    }

    [Fact]
    public void A_granted_feature_names_what_granted_it()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Load("srd521-ash-m1.json"), Fixtures.M1Catalog());
        var granted = sheet.Features!.First(f => f.GrantedBy is not null);
        Assert.False(string.IsNullOrEmpty(granted.GrantedByName));
        Assert.Contains(sheet.Features!, f => f.Content == granted.GrantedBy); // the granter is itself on the sheet
    }
}
