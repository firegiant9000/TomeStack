using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>Content schema v12 (G4): validation of a modifier's armor and shield gate, and its validate.requires-v12 rung.</summary>
public class NotWearingV12ValidationTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private static readonly Guid ContentId = Guid.Parse("6a7e5500-0000-4000-8000-000000000004");

    private static ModifierEffect Gated(params string[] gear) =>
        new() { Id = "m", Operation = ModifierOperation.Bonus, Target = "initiative", Value = "2", NotWearing = gear };

    private static ContentRevision Built(ContentKind kind, int version, params Effect[] effects) => new()
    {
        ContentId = ContentId, RevisionId = Guid.Parse("6a7e5500-0000-4000-8000-000000000104"),
        SchemaVersion = version, Kind = kind, Name = "Fixture Bare Stance", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Fixtures.Source2024), Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ValidationReport Validate(Effect effect, int version = 12, ContentKind kind = ContentKind.Feature) =>
        ContentValidator.Validate(Built(kind, version, effect), Catalog);

    private static void AssertError(ValidationReport report, string code) => Assert.Contains(report.Errors, e => e.Code == code);

    private static void AssertClean(ValidationReport report)
    {
        Assert.Empty(report.Errors);
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Gate_on_a_pool_maximum_is_refused()
    {
        var pool = new ResourceEffect { Id = "pool", ResourceId = "pool", Label = "Pool", Maximum = "2" };
        var bonus = new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = "resource.pool", Value = "1", NotWearing = ["shield"] };

        var report = ContentValidator.Validate(Built(ContentKind.Feature, 12, pool, bonus), Catalog);

        Assert.Contains(report.Errors, e => e.Code == "validate.not-wearing"
            && e.Message == "Effect 'm' has notWearing, but it raises a pool maximum, which counts whatever is worn; gear gates apply to calculated fields only.");
    }

    [Theory]
    [InlineData]
    [InlineData("cloak")]
    [InlineData("shield", "shield")]
    [InlineData("armor", "heavyArmor")]
    public void Empty_unknown_or_duplicate_gear_is_refused(params string[] gear)
    {
        var report = Validate(Gated(gear));
        Assert.Contains(report.Errors, e => e.Code == "validate.not-wearing"
            && e.Message.StartsWith("Effect 'm' has notWearing ", StringComparison.Ordinal)
            && e.Message.EndsWith("it lists armor, heavyArmor or shield, each once, and armor already covers heavyArmor.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("armor")]
    [InlineData("heavyArmor")]
    [InlineData("shield")]
    [InlineData("armor", "shield")]
    [InlineData("heavyArmor", "shield")]
    public void Each_gear_and_armor_with_shield_validate(params string[] gear) => AssertClean(Validate(Gated(gear)));

    [Fact]
    public void While_armored_with_armor_gate_is_refused()
    {
        var report = Validate(Gated("armor") with { WhileArmored = true });
        Assert.Contains(report.Errors, e => e.Code == "validate.not-wearing-armored"
            && e.Message == "Effect 'm' applies only while armor is worn and only while none is, so it could never apply.");
    }

    [Fact]
    public void While_armored_with_heavy_armor_gate_validates() =>
        AssertClean(Validate(Gated("heavyArmor") with { WhileArmored = true }));

    [Fact]
    public void Gate_with_toggle_timing_cap_and_stack_group_validates()
    {
        var toggle = new ToggleEffect { Id = "t", ToggleId = "stance", Label = "Stance" };
        var gated = new ModifierEffect
        {
            Id = "m", Operation = ModifierOperation.Bonus, Target = "ability.str.score", Value = "4", Cap = 25,
            Toggle = "stance", Timing = EffectTiming.WhileActive, Stacking = StackingRule.HighestInGroup, StackGroup = "g",
            NotWearing = ["armor", "shield"],
        };
        AssertClean(ContentValidator.Validate(Built(ContentKind.Feature, 12, toggle, gated), Catalog));
    }

    [Fact]
    public void Gate_inside_an_item_mod_is_refused()
    {
        var mod = new ItemModEffect { Id = "im", AppliesTo = ModTarget.Weapon, Modifiers = [Gated("shield")] };
        AssertError(Validate(mod, 12, ContentKind.Item), "validate.item-mod-modifier");
    }

    [Fact]
    public void Gate_on_an_item_mod_revisions_own_modifier_is_refused()
    {
        var mod = new ItemModEffect { Id = "im", AppliesTo = ModTarget.Weapon };
        var report = ContentValidator.Validate(Built(ContentKind.Item, 12, mod, Gated("shield")), Catalog);
        Assert.Contains(report.Errors, e => e.Code == "validate.not-wearing"
            && e.Message == "Effect 'm' has notWearing, but this revision is an item mod, whose modifiers apply whatever is worn.");
    }

    private static ContentRevision Unreadable(string key, string value) => RevisionHashProperties.Read(
        "{\"contentId\":\"6a7e5500-0000-4000-8000-000000000004\",\"revisionId\":\"6a7e5500-0000-4000-8000-000000000104\",\"schemaVersion\":12,\"kind\":\"feature\",\"name\":\"Fixture Bare Stance\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"5f0d5210-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[{\"type\":\"modifier\",\"id\":\"m\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"2\",\"" + key + "\":" + value + "}]}");

    [Fact]
    public void Unreadable_not_wearing_is_malformed_and_names_the_gate()
    {
        var revision = Unreadable("notWearing", "\"abc\"");
        Assert.IsType<UnknownEffect>(revision.Effects.Single());
        var report = ContentValidator.Validate(revision, Catalog);
        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed"
            && e.Message.Contains("notWearing", StringComparison.Ordinal)
            && !e.Message.Contains("cap", StringComparison.Ordinal));
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Unreadable_cap_and_not_wearing_together_name_both()
    {
        var revision = Unreadable("cap\":\"abc\",\"notWearing", "\"abc\"");
        Assert.IsType<UnknownEffect>(revision.Effects.Single());
        var report = ContentValidator.Validate(revision, Catalog);
        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed"
            && e.Message.Contains("its cap must be", StringComparison.Ordinal)
            && e.Message.Contains("its notWearing must be", StringComparison.Ordinal));
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Unreadable_cap_still_names_the_cap()
    {
        var report = ContentValidator.Validate(Unreadable("cap", "\"abc\""), Catalog);
        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed" && e.Message.Contains("its cap must be", StringComparison.Ordinal));
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Draft_with_gate_below_v12_fails_requires_v12_and_at_v12_requires_12()
    {
        var gate = Gated("armor");
        var below = Validate(gate, 11);
        Assert.Contains(below.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("an armor or shield gate", StringComparison.Ordinal));
        AssertClean(Validate(gate));
    }
}
