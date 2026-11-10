using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G1): validation of scaledDice on an attackRider and a roll, the rider's critical and oncePerTurn flags,
/// the validate.requires-v12 rung each registers, and the formula edges in the content graph.
/// </summary>
public class ScaledDiceV12ValidationTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private static readonly Guid ContentId = Guid.Parse("6a7e0100-0000-4000-8000-000000000001");

    private static AttackRiderEffect Rider(Func<AttackRiderEffect, AttackRiderEffect>? shape = null)
    {
        var rider = new AttackRiderEffect { Id = "r", Tags = ["melee"], ScaledDice = new ScaledDice("2", "8") };
        return shape is null ? rider : shape(rider);
    }

    private static RollEffect Roll(Func<RollEffect, RollEffect>? shape = null)
    {
        var roll = new RollEffect { Id = "ro", RollId = "burst", Label = "Burst", Dice = null, ScaledDice = new ScaledDice("2", "8") };
        return shape is null ? roll : shape(roll);
    }

    private static ContentRevision Built(int version, params Effect[] effects) => new()
    {
        ContentId = ContentId, RevisionId = Guid.Parse("6a7e0100-0000-4000-8000-000000000101"),
        SchemaVersion = version, Kind = ContentKind.Feature, Name = "Fixture Scaled", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Fixtures.Source2024), Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ValidationReport Validate(Effect effect, int version = 12) => ContentValidator.Validate(Built(version, effect), Catalog);

    private static ValidationReport ValidateJson(int version, string effect) =>
        ContentValidator.Validate(RevisionHashProperties.Read(
            "{\"contentId\":\"" + ContentId + "\",\"revisionId\":\"6a7e0100-0000-4000-8000-000000000101\",\"schemaVersion\":" + version
            + ",\"kind\":\"feature\",\"name\":\"Fixture Scaled\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"" + Fixtures.Source2024 + "\"},\"status\":\"draft\",\"effects\":[" + effect + "]}"),
            Catalog);

    private static void AssertError(ValidationReport report, string code) => Assert.Contains(report.Errors, e => e.Code == code);

    private static void AssertNoError(ValidationReport report, string code) => Assert.DoesNotContain(report.Errors, e => e.Code == code);

    private static void AssertClean(ValidationReport report)
    {
        Assert.Empty(report.Errors);
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Fact]
    public void V11_rider_without_v12_members_still_requires_11()
    {
        var report = Validate(Rider(r => r with { ScaledDice = null, Dice = "2d6" }), 11);
        Assert.Empty(report.Errors);
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Draft_with_scaledDice_below_v12_fails_requires_v12_and_at_v12_requires_12()
    {
        var rider = Rider();
        var below = Validate(rider, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("attackRider scaledDice", StringComparison.Ordinal));
        AssertClean(Validate(rider));

        var roll = Roll();
        var rollBelow = Validate(roll, 11);
        Assert.Contains(rollBelow.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("roll scaledDice", StringComparison.Ordinal));
        AssertClean(Validate(roll));
    }

    [Fact]
    public void Critical_requires_v12_and_false_still_requires_12()
    {
        var rider = Rider(r => r with { ScaledDice = null, Dice = "2d6", Critical = true });
        var below = Validate(rider, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a critical-only attackRider", StringComparison.Ordinal));
        AssertClean(Validate(rider));
    }

    [Fact]
    public void Critical_false_still_requires_12()
    {
        var rider = Rider(r => r with { ScaledDice = null, Dice = "2d6", Critical = false });
        Assert.Contains(Validate(rider, 11).Errors, e => e.Code == "validate.requires-v12");
        AssertClean(Validate(rider));
    }

    [Fact]
    public void OncePerTurn_requires_v12_and_false_still_requires_12()
    {
        var rider = Rider(r => r with { ScaledDice = null, Dice = "2d6", OncePerTurn = true });
        var below = Validate(rider, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a once-per-turn attackRider", StringComparison.Ordinal));
        AssertClean(Validate(rider));
        var off = Rider(r => r with { ScaledDice = null, Dice = "2d6", OncePerTurn = false });
        Assert.Contains(Validate(off, 11).Errors, e => e.Code == "validate.requires-v12");
        AssertClean(Validate(off));
    }

    [Fact]
    public void ScaledDice_missing_count_is_refused()
    {
        AssertError(Validate(Rider(r => r with { ScaledDice = new ScaledDice("") })), "validate.scaled-dice");
        AssertError(Validate(Roll(r => r with { ScaledDice = new ScaledDice("  ", "8") })), "validate.scaled-dice");
    }

    [Fact]
    public void Roll_scaledDice_without_die_is_refused_and_rider_without_die_is_clean()
    {
        AssertError(Validate(Roll(r => r with { ScaledDice = new ScaledDice("2") })), "validate.scaled-dice");
        AssertClean(Validate(Rider(r => r with { ScaledDice = new ScaledDice("2") })));
    }

    [Fact]
    public void Dice_and_scaledDice_together_are_refused()
    {
        AssertError(Validate(Rider(r => r with { Dice = "1d6" })), "validate.scaled-dice");
        AssertError(Validate(Roll(r => r with { Dice = "1d6" })), "validate.scaled-dice");
    }

    [Fact]
    public void Constant_die_outside_the_allowed_dice_is_refused()
    {
        AssertError(Validate(Rider(r => r with { ScaledDice = new ScaledDice("2", "7") })), "validate.scaled-dice");
        AssertError(Validate(Roll(r => r with { ScaledDice = new ScaledDice("2", "20") })), "validate.scaled-dice");
        AssertClean(Validate(Rider(r => r with { ScaledDice = new ScaledDice("2", "4") })));
    }

    [Fact]
    public void Constant_count_outside_zero_to_max_is_refused()
    {
        AssertError(Validate(Rider(r => r with { ScaledDice = new ScaledDice((DiceLimits.MaxDice + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "8") })), "validate.scaled-dice");
        AssertError(Validate(Rider(r => r with { ScaledDice = new ScaledDice("-1", "8") })), "validate.scaled-dice");
        AssertClean(Validate(Rider(r => r with { ScaledDice = new ScaledDice("0", "8") })));
        AssertClean(Validate(Rider(r => r with { ScaledDice = new ScaledDice(DiceLimits.MaxDice.ToString(System.Globalization.CultureInfo.InvariantCulture), "8") })));
    }

    [Fact]
    public void Rider_stackGroup_with_a_dieless_scaledDice_is_refused()
    {
        AssertError(Validate(Rider(r => r with { ScaledDice = new ScaledDice("2"), StackGroup = "g" })), "validate.scaled-dice");
        AssertNoError(Validate(Rider(r => r with { StackGroup = "g" })), "validate.scaled-dice");
    }

    [Fact]
    public void Critical_refuses_toHit_damageBonus_stackGroup_oncePerTurn_and_needs_dice()
    {
        AssertError(Validate(Rider(r => r with { Critical = true, ToHit = "1" })), "validate.rider-critical");
        AssertError(Validate(Rider(r => r with { Critical = true, DamageBonus = "1" })), "validate.rider-critical");
        AssertError(Validate(Rider(r => r with { Critical = true, StackGroup = "g" })), "validate.rider-critical");
        AssertError(Validate(Rider(r => r with { Critical = true, OncePerTurn = true })), "validate.rider-critical");
        AssertError(Validate(Rider(r => r with { Critical = true, ScaledDice = null, DamageBonus = "1" })), "validate.rider-critical");
        AssertClean(Validate(Rider(r => r with { Critical = true })));
        AssertClean(Validate(Rider(r => r with { Critical = true, ScaledDice = null, Dice = "1d6" })));
    }

    [Fact]
    public void OncePerTurn_refuses_toHit_damageBonus_stackGroup_and_needs_dice()
    {
        AssertError(Validate(Rider(r => r with { OncePerTurn = true, ToHit = "1" })), "validate.rider-once-per-turn");
        AssertError(Validate(Rider(r => r with { OncePerTurn = true, DamageBonus = "1" })), "validate.rider-once-per-turn");
        AssertError(Validate(Rider(r => r with { OncePerTurn = true, StackGroup = "g" })), "validate.rider-once-per-turn");
        AssertError(Validate(Rider(r => r with { OncePerTurn = true, ScaledDice = null, DamageBonus = "1" })), "validate.rider-once-per-turn");
        AssertClean(Validate(Rider(r => r with { OncePerTurn = true })));
    }

    [Fact]
    public void Scaled_formula_with_SCALE_identifier_is_checked()
    {
        // A SCALE identifier is gated per revision version exactly as damageBonus gates it: validate.requires-v9 below v9.
        var reference = Validate(Rider(r => r with { ScaledDice = null, DamageBonus = "SCALE.nothing" }), 8);
        AssertError(reference, "validate.requires-v9");
        AssertError(Validate(Rider(r => r with { ScaledDice = new ScaledDice("SCALE.nothing", "8") }), 8), "validate.requires-v9");
        AssertError(Validate(Roll(r => r with { ScaledDice = new ScaledDice("2", "SCALE.nothing") }), 8), "validate.requires-v9");
        AssertNoError(Validate(Rider(r => r with { ScaledDice = new ScaledDice("SCALE.nothing", "8") })), "validate.requires-v9");
    }

    [Fact]
    public void Scaled_formula_that_does_not_parse_is_a_formula_error()
    {
        AssertError(Validate(Rider(r => r with { ScaledDice = new ScaledDice("2 +", "8") })), "validate.formula-invalid");
    }

    [Fact]
    public void Malformed_scaledDice_at_v12_stays_unknown_and_is_reported_not_thrown()
    {
        var rider = RevisionHashProperties.Read(Json("{\"type\":\"attackRider\",\"id\":\"r\",\"tags\":[\"melee\"],\"scaledDice\":{\"count\":3}}"));
        Assert.IsType<UnknownEffect>(Assert.Single(rider.Effects));
        var riderReport = ContentValidator.Validate(rider, Catalog);
        AssertError(riderReport, "validate.effect-malformed");
        Assert.Equal(12, riderReport.RequiredSchemaVersion);

        // A missing count reads as null (the record's property) and is reported by the validator, never thrown.
        var missing = ValidateJson(12, "{\"type\":\"attackRider\",\"id\":\"r\",\"tags\":[\"melee\"],\"scaledDice\":{\"die\":\"8\"}}");
        AssertError(missing, "validate.scaled-dice");

        var roll = RevisionHashProperties.Read(Json("{\"type\":\"roll\",\"id\":\"ro\",\"rollId\":\"burst\",\"label\":\"Burst\",\"scaledDice\":{\"count\":3,\"die\":\"8\"}}"));
        Assert.IsType<UnknownEffect>(Assert.Single(roll.Effects));
        var rollReport = ContentValidator.Validate(roll, Catalog);
        AssertError(rollReport, "validate.effect-malformed");
        Assert.Equal(12, rollReport.RequiredSchemaVersion);
    }

    [Fact]
    public void Scaled_dice_formulas_are_graph_edges()
    {
        var revision = Built(12, Rider(r => r with { ScaledDice = new ScaledDice("floor(CLASS_LEVEL / 3)", "8") }),
            Roll(r => r with { ScaledDice = new ScaledDice("CLASS_LEVEL", "6") }));
        var edges = ContentGraph.Formulas(revision).ToList();
        Assert.Contains(("r", "floor(CLASS_LEVEL / 3)"), edges);
        Assert.Contains(("r", "8"), edges);
        Assert.Contains(("ro", "CLASS_LEVEL"), edges);
        Assert.Contains(("ro", "6"), edges);
    }

    private static string Json(string effect) =>
        "{\"contentId\":\"" + ContentId + "\",\"revisionId\":\"6a7e0100-0000-4000-8000-000000000101\",\"schemaVersion\":12"
        + ",\"kind\":\"feature\",\"name\":\"Fixture Scaled\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"" + Fixtures.Source2024 + "\"},\"status\":\"draft\",\"effects\":[" + effect + "]}";
}
