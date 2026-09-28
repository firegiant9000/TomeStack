namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M1 item 3: restriction effects are prerequisites. Content whose prerequisite is not met is not applied, with a
/// diagnostic scoped to it, and content cannot qualify itself. "Fixture Iron Grip": Strength 13+, grants +1 Str and +1
/// initiative. Base Dex 14 gives initiative +2.
/// </summary>
public class RestrictionTests
{
    private static CharacterSheet Sheet(int strength, params ContentReference[] pins) => Sheet(strength, [], pins);

    private static CharacterSheet Sheet(int strength, IReadOnlyList<FieldOverride> overrides, params ContentReference[] pins)
    {
        var character = Fixtures.Load("srd521-ash-m1.json");
        character = character with { BaseAbilities = character.BaseAbilities with { Str = strength }, Pins = pins, Overrides = overrides };
        return CharacterCalculator.Calculate(character, Fixtures.M1Catalog());
    }

    [Fact]
    public void Content_whose_prerequisite_is_not_met_is_not_applied_and_the_diagnostic_names_it()
    {
        var sheet = Sheet(10, Fixtures.IronGrip);

        var unmet = Assert.Single(sheet.Diagnostics, d => d.Code == "restriction.unmet");
        Assert.Equal((Fixtures.IronGrip, "grip-prerequisite"), (unmet.Content, unmet.EffectId));
        Assert.Equal("'Fixture Iron Grip' requires Strength score 13 or higher; this character has 10 without it. Its effects are not applied.", unmet.Message);
        Assert.Equal((10, 2), (sheet.Field(FieldIds.Score(Ability.Str)).Value, sheet.Field(FieldIds.Initiative).Value));
        Assert.DoesNotContain(sheet.Fields.SelectMany(f => f.Trace), t => t.Origin.Content == Fixtures.IronGrip);
    }

    [Fact]
    public void Content_whose_prerequisite_is_met_applies()
    {
        var sheet = Sheet(13, Fixtures.IronGrip);

        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "restriction.unmet");
        Assert.Equal((14, 3), (sheet.Field(FieldIds.Score(Ability.Str)).Value, sheet.Field(FieldIds.Initiative).Value));
    }

    [Fact]
    public void Content_cannot_meet_its_own_prerequisite()
    {
        // Strength 12 + Iron Grip's own +1 would be 13, but the prerequisite is checked without Iron Grip.
        var sheet = Sheet(12, Fixtures.IronGrip);

        Assert.Contains(sheet.Diagnostics, d => d.Code == "restriction.unmet" && d.Message.Contains("has 12 without it", StringComparison.Ordinal));
        Assert.Equal(12, sheet.Field(FieldIds.Score(Ability.Str)).Value);
    }

    [Fact]
    public void Other_content_can_meet_a_prerequisite()
    {
        var feat = Fixtures.M1Catalog().FindRevision(Fixtures.Hardy)!; // any feat; give it +1 Str
        var strong = feat with
        {
            RevisionId = Guid.Parse("5f1de000-0000-4000-8000-0000000000f9"),
            Effects = [new ModifierEffect { Id = "str", Operation = ModifierOperation.Bonus, Target = FieldIds.Score(Ability.Str), Value = "1" }],
        };
        var catalog = new InMemoryContentCatalog([.. Fixtures.Pack().Sources, .. Fixtures.M1Pack().Sources], [.. Fixtures.Pack().Revisions, .. Fixtures.M1Pack().Revisions, strong]);
        var character = Fixtures.Load("srd521-ash-m1.json");

        var sheet = CharacterCalculator.Calculate(character with { BaseAbilities = character.BaseAbilities with { Str = 12 }, Pins = [strong.Reference, Fixtures.IronGrip] }, catalog);

        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "restriction.unmet");
        Assert.Equal(12 + 1 + 1, sheet.Field(FieldIds.Score(Ability.Str)).Value);
    }

    [Fact]
    public void An_override_counts_for_a_prerequisite_and_the_trace_shows_it()
    {
        var sheet = Sheet(10, [new FieldOverride(FieldIds.Score(Ability.Str), 13, "Belt of strength (table ruling)")], Fixtures.IronGrip);

        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "restriction.unmet");
        Assert.Equal(3, sheet.Field(FieldIds.Initiative).Value);
        Assert.Equal("override", sheet.Field(FieldIds.Score(Ability.Str)).Trace[^1].Operation);
    }

    [Fact]
    public void A_restriction_on_an_unknown_field_keeps_its_content_out()
    {
        var grip = Fixtures.M1Catalog().FindRevision(Fixtures.IronGrip)!;
        var broken = grip with
        {
            RevisionId = Guid.Parse("5f1de000-0000-4000-8000-0000000000f8"),
            Effects = [new RestrictionEffect { Id = "odd", Field = "speed", Minimum = 30 }, .. grip.Effects.Where(e => e is not RestrictionEffect)],
        };
        var catalog = new InMemoryContentCatalog([.. Fixtures.Pack().Sources, .. Fixtures.M1Pack().Sources], [.. Fixtures.Pack().Revisions, .. Fixtures.M1Pack().Revisions, broken]);

        var sheet = CharacterCalculator.Calculate(Fixtures.Load("srd521-ash-m1.json") with { Pins = [broken.Reference] }, catalog);

        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.unknown-target" && d.EffectId == "odd");
        Assert.Equal(2, sheet.Field(FieldIds.Initiative).Value);
    }
}
