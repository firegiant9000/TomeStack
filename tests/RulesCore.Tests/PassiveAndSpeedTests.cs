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

    private static ContentRevision Feat(int n, string name, int schemaVersion, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"a1000000-0000-4000-8000-00000000010{n}"), RevisionId = Guid.Parse($"b1000000-0000-4000-8000-00000000010{n}"),
        Kind = ContentKind.Feat, Name = name, RulesFamilies = [RulesFamilies.Srd51], Provenance = new(Fixtures.SourceShared, new PageRef(31)),
        Status = RevisionStatus.Published, SchemaVersion = schemaVersion, Effects = effects,
    };

    private static CharacterSheet Calc(Character character, params ContentRevision[] extra)
    {
        var pack = Fixtures.Pack();
        var catalog = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, .. extra]);
        return CharacterCalculator.Calculate(character with { Pins = [.. character.Pins, .. extra.Select(e => e.Reference)] }, catalog);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public void The_calculator_ignores_a_content_modifier_on_speed_or_a_passive_whatever_the_schema_version(int schemaVersion)
    {
        var plain = CharacterCalculator.Calculate(Fixtures.Srd51Character(), Fixtures.Catalog());
        var quick = Feat(1, "Fixture Quickness", schemaVersion,
            new ModifierEffect { Id = "fast", Operation = ModifierOperation.Bonus, Target = FieldIds.Speed, Value = "10" },
            new ModifierEffect { Id = "pp", Operation = ModifierOperation.Bonus, Target = FieldIds.Passive("perception"), Value = "5" });
        var sheet = Calc(Fixtures.Srd51Character(), quick);
        Assert.Equal(30, sheet.Field(FieldIds.Speed).Value);
        Assert.Equal(plain.Field(FieldIds.Passive("perception")).Value, sheet.Field(FieldIds.Passive("perception")).Value);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.character-only-field" && d.Content == quick.Reference && d.EffectId == "fast");
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.character-only-field" && d.EffectId == "pp");
    }

    [Fact]
    public void The_calculator_ignores_a_restriction_on_a_passive_and_does_not_apply_the_content()
    {
        var gated = Feat(2, "Fixture Gated Watchfulness", 2,
            new RestrictionEffect { Id = "needs-pp", Field = FieldIds.Passive("perception"), Minimum = 1 },
            new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "5" });
        var sheet = Calc(Fixtures.Srd51Character(), gated);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.character-only-field" && d.Content == gated.Reference && d.EffectId == "needs-pp");
        Assert.DoesNotContain(gated.Reference, sheet.Active!);
    }

    [Fact]
    public void A_character_override_on_speed_still_applies_beside_content_that_targets_it()
    {
        var quick = Feat(3, "Fixture Quickness", 2, new ModifierEffect { Id = "fast", Operation = ModifierOperation.Bonus, Target = FieldIds.Speed, Value = "10" });
        var sheet = Calc(Fixtures.Srd51Character() with { Overrides = [new(FieldIds.Speed, 25, "Dwarf")] }, quick);
        Assert.Equal((25, 30), (sheet.Field(FieldIds.Speed).Value, sheet.Field(FieldIds.Speed).ComputedValue));
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
