namespace TomeStack.RulesCore.Tests;

/// <summary>Content schema v12 (G9): validation of a rollFloor's target, face and total, and its validate.requires-v12 rung.</summary>
public class RollFloorV12ValidationTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private static readonly Guid ContentId = Guid.Parse("6a7e0900-0000-4000-8000-000000000001");

    private static RollFloorEffect Floor(Func<RollFloorEffect, RollFloorEffect>? shape = null)
    {
        var floor = new RollFloorEffect { Id = "f", Target = RollFloorEffect.CheckTarget, Face = 10 };
        return shape is null ? floor : shape(floor);
    }

    private static ContentRevision Built(int version, params Effect[] effects) => new()
    {
        ContentId = ContentId, RevisionId = Guid.Parse("6a7e0900-0000-4000-8000-000000000101"),
        SchemaVersion = version, Kind = ContentKind.Feature, Name = "Fixture Floor", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Fixtures.Source2024), Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ValidationReport Validate(RollFloorEffect floor, int version = 12) => ContentValidator.Validate(Built(version, floor), Catalog);

    private static void AssertError(ValidationReport report, string code) => Assert.Contains(report.Errors, e => e.Code == code);

    private static void AssertClean(ValidationReport report)
    {
        Assert.Empty(report.Errors);
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("ability.dex.check")]
    [InlineData("skill.stealth")]
    public void Valid_targets_are_accepted(string target) =>
        AssertClean(Validate(Floor(f => f with { Target = target })));

    [Theory]
    [InlineData("save.dex")]
    [InlineData("initiative")]
    [InlineData("attack")]
    [InlineData("ability.xyz.check")]
    public void Target_other_than_a_check_is_refused(string target) =>
        AssertError(Validate(Floor(f => f with { Target = target })), "validate.roll-floor-target");

    [Fact]
    public void Target_save_is_refused() =>
        AssertError(Validate(Floor(f => f with { Target = "save.wis" })), "validate.roll-floor-target");

    [Fact]
    public void Target_unknown_skill_is_refused() =>
        AssertError(Validate(Floor(f => f with { Target = "skill.juggling" })), "validate.roll-floor-target");

    [Fact]
    public void Face_and_total_together_are_refused() =>
        AssertError(Validate(Floor(f => f with { Face = 10, Total = "8" })), "validate.roll-floor-value");

    [Fact]
    public void Neither_face_nor_total_is_refused() =>
        AssertError(Validate(Floor(f => f with { Face = null, Total = null })), "validate.roll-floor-value");

    [Fact]
    public void Empty_total_is_refused() =>
        AssertError(Validate(Floor(f => f with { Face = null, Total = "  " })), "validate.roll-floor-value");

    [Theory]
    [InlineData(1)]
    [InlineData(21)]
    [InlineData(0)]
    [InlineData(-3)]
    public void Face_1_and_21_are_refused(int face) =>
        AssertError(Validate(Floor(f => f with { Face = face })), "validate.roll-floor-value");

    [Theory]
    [InlineData(2)]
    [InlineData(20)]
    public void Face_2_and_20_are_accepted(int face) =>
        AssertClean(Validate(Floor(f => f with { Face = face })));

    [Fact]
    public void Total_is_accepted_when_the_formula_reads() =>
        AssertClean(Validate(Floor(f => f with { Face = null, Total = "8 + PB" })));

    [Fact]
    public void Total_with_unknown_identifier_gives_the_formula_diagnostic() =>
        AssertError(Validate(Floor(f => f with { Face = null, Total = "NOPE.THING + 1" })), "validate.formula-invalid");

    [Fact]
    public void Draft_with_rollFloor_below_v12_fails_requires_v12_and_at_v12_requires_12()
    {
        var floor = Floor();
        var below = Validate(floor, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("rollFloor", StringComparison.Ordinal));
        AssertClean(Validate(floor));
    }

    [Fact]
    public void Proficient_false_still_requires_12()
    {
        var floor = Floor(f => f with { Proficient = false });
        Assert.Contains(Validate(floor, 11).Errors, e => e.Code == "validate.requires-v12");
        AssertClean(Validate(floor));
    }

    [Fact]
    public void Untyped_rollFloor_at_v12_is_reported_malformed()
    {
        var json = "{\"contentId\":\"6a7e0900-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0900-0000-4000-8000-000000000101\",\"schemaVersion\":12,\"kind\":\"feature\",\"name\":\"Fixture Floor\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"5f0d5210-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"check\",\"face\":\"ten\"}]}";
        var revision = Properties.RevisionHashProperties.Read(json);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());
        var report = ContentValidator.Validate(revision, Catalog);
        AssertError(report, "validate.effect-malformed");
    }
}
