namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G10), Task 3: the climb and swim speed fields (base 0, content may target them only from v12) and the SPEED
/// formula identifier (walking speed, readable only by a speed.climb, speed.swim or speed.fly modifier of a v12 revision).
/// Original fixtures on the companions pack.
/// </summary>
public class ClimbSwimV12Tests
{
    private static readonly Guid SourceId = Guid.Parse("6a7e0000-0000-4000-8000-000000000001");

    private static readonly InMemoryContentCatalog ValidationCatalog = Fixtures.M1Catalog();

    private static InMemoryContentCatalog Catalog(params ContentRevision[] extra)
    {
        var pack = Fixtures.CompanionsPack();
        return new(pack.Sources, [.. pack.Revisions, .. extra]);
    }

    private static Character Handler() => Fixtures.Load("pm-handler-l3.json");

    private static ContentRevision Feature(string name, int n, int version, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"6a7e00a3-0000-4000-8000-{0x200 + n:X12}"), RevisionId = Guid.Parse($"6a7e00a3-0000-4000-8000-{0x300 + n:X12}"),
        SchemaVersion = version, Kind = ContentKind.Feature, Name = name, RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(SourceId), Status = RevisionStatus.Published, Effects = effects,
    };

    private static ModifierEffect Mod(string id, ModifierOperation operation, string target, string value) =>
        new() { Id = id, Operation = operation, Target = target, Value = value };

    private static CharacterSheet Sheet(Character character, params ContentRevision[] extra) =>
        CharacterCalculator.Calculate(character with { Pins = [.. character.Pins, .. extra.Select(e => e.Reference)] }, Catalog(extra));

    private static ValidationReport Validate(int version, params Effect[] effects) => ContentValidator.Validate(new()
    {
        ContentId = Guid.Parse("6a7e00a3-0000-4000-8000-000000000001"), RevisionId = Guid.Parse("6a7e00a3-0000-4000-8000-000000000101"),
        SchemaVersion = version, Kind = ContentKind.Feature, Name = "Fixture Pace", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Fixtures.Source2024), Status = RevisionStatus.Draft, Effects = effects,
    }, ValidationCatalog);

    [Fact]
    public void Sheet_has_climb_and_swim_at_0_with_base_steps()
    {
        var sheet = Sheet(Handler());

        var climb = sheet.Field(FieldIds.ClimbSpeed);
        var swim = sheet.Field(FieldIds.SwimSpeed);
        Assert.Equal("speed.climb", FieldIds.ClimbSpeed);
        Assert.Equal("speed.swim", FieldIds.SwimSpeed);
        Assert.Equal(("Climb speed", 0, "feet"), (climb.Label, climb.Value, climb.Units));
        Assert.Equal(("Swim speed", 0, "feet"), (swim.Label, swim.Value, swim.Units));
        Assert.Equal("No climbing speed unless content gives one", Assert.Single(climb.Trace).Description);
        Assert.Equal("No swimming speed unless content gives one", Assert.Single(swim.Trace).Description);
        Assert.Equal(TraceOriginKind.RulesPolicy, Assert.Single(climb.Trace).Origin.Kind);
    }

    [Fact]
    public void V12_bonus_to_climb_speed_applies()
    {
        var sheet = Sheet(Handler(), Feature("Fixture Pace A", 1, 12, Mod("m", ModifierOperation.Bonus, "speed.climb", "10")));

        Assert.Equal(10, sheet.Field(FieldIds.ClimbSpeed).Value);
        Assert.Equal(0, sheet.Field(FieldIds.SwimSpeed).Value);
    }

    [Fact]
    public void V12_replace_swim_speed_with_SPEED_equals_walking_speed()
    {
        var sheet = Sheet(Handler(),
            Feature("Fixture Pace B", 2, 12, Mod("w", ModifierOperation.Replace, "speed", "35")),
            Feature("Fixture Pace C", 3, 12, Mod("s", ModifierOperation.Replace, "speed.swim", "SPEED")));

        Assert.Equal(35, sheet.Field(FieldIds.Speed).Value);
        Assert.Equal(35, sheet.Field(FieldIds.SwimSpeed).Value);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code is "effect.dependency-cycle" or "effect.invalid-formula");
    }

    [Fact]
    public void V12_half_of_SPEED_on_fly_applies()
    {
        var sheet = Sheet(Handler(), Feature("Fixture Pace D", 4, 12, Mod("f", ModifierOperation.Replace, "speed.fly", "SPEED / 2")));

        Assert.Equal(15, sheet.Field(FieldIds.FlySpeed).Value);
    }

    [Fact]
    public void V11_modifier_on_climb_speed_is_refused_and_ignored_by_the_calculator()
    {
        var feature = Feature("Fixture Pace E", 5, 11, Mod("m", ModifierOperation.Bonus, "speed.climb", "10"));

        var sheet = Sheet(Handler(), feature);

        Assert.Equal(0, sheet.Field(FieldIds.ClimbSpeed).Value);
        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.Code == "effect.character-only-field");
        Assert.Equal("m", diagnostic.EffectId);
        Assert.Contains(Validate(11, Mod("m", ModifierOperation.Bonus, "speed.swim", "10")).Errors, e => e.Code == "validate.requires-v12");
    }

    [Fact]
    public void Climb_and_swim_modifiers_validate_clean_at_v12()
    {
        var report = Validate(12, Mod("m", ModifierOperation.Bonus, "speed.climb", "10"), Mod("n", ModifierOperation.Set, "speed.swim", "20"));

        Assert.Empty(report.Errors);
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData("speed.fly")]
    [InlineData("speed.climb")]
    [InlineData("speed.swim")]
    public void SPEED_in_a_speed_modifier_requires_12(string target)
    {
        var clean = Validate(12, Mod("m", ModifierOperation.Replace, target, "SPEED"));

        Assert.Empty(clean.Errors);
        Assert.Equal(12, clean.RequiredSchemaVersion);
    }

    [Fact]
    public void SPEED_in_a_fly_modifier_requires_12()
    {
        var report = Validate(12, Mod("m", ModifierOperation.Replace, "speed.fly", "SPEED"));

        Assert.Empty(report.Errors);
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData("armorClass")]
    [InlineData("skill.stealth")]
    [InlineData("speed")]
    public void SPEED_in_an_armor_class_or_skill_modifier_is_an_unknown_identifier(string target)
    {
        var report = Validate(12, Mod("m", ModifierOperation.Bonus, target, "SPEED"));

        var error = Assert.Single(report.Errors, e => e.Code == "validate.formula-invalid");
        Assert.Contains("formula.unknown-identifier", error.Message);
        Assert.DoesNotContain("SPEED", error.Message.Split("Use one of:")[1]);
    }

    [Fact]
    public void SPEED_in_a_v11_revision_is_an_unknown_identifier()
    {
        var report = Validate(11, Mod("m", ModifierOperation.Replace, "speed.fly", "SPEED"));

        Assert.Contains(report.Errors, e => e.Code == "validate.formula-invalid" && e.Message.Contains("formula.unknown-identifier"));
    }

    [Fact]
    public void SPEED_in_a_v11_revision_is_not_read_by_the_calculator()
    {
        var sheet = Sheet(Handler(), Feature("Fixture Pace F", 6, 11, Mod("f", ModifierOperation.Replace, "speed.fly", "SPEED")));

        var fly = sheet.Field(FieldIds.FlySpeed);
        Assert.Equal(0, fly.Value);
        Assert.Contains(fly.Warnings, w => w.Code == "effect.invalid-formula" && w.EffectId == "f");
    }

    [Fact]
    public void SPEED_has_no_preview_value()
    {
        Assert.Null(FormulaPreview.Resolve(FormulaIdentifiers.Speed, 3, new Dictionary<string, IReadOnlyList<int>>()));
    }

    [Fact]
    public void Restriction_on_climb_speed_requires_12()
    {
        var restriction = new RestrictionEffect { Id = "r", Field = FieldIds.ClimbSpeed, Minimum = 0 };

        Assert.Contains(Validate(11, restriction).Errors, e => e.Code == "validate.requires-v12");
        var clean = Validate(12, restriction);
        Assert.Empty(clean.Errors);
        Assert.Equal(12, clean.RequiredSchemaVersion);
    }

    [Fact]
    public void Sheet_without_new_content_is_unchanged_except_two_fields()
    {
        var sheet = Sheet(Handler());

        var ids = sheet.Fields.Select(f => f.Field).ToList();
        var fly = ids.IndexOf(FieldIds.FlySpeed);
        Assert.Equal([FieldIds.FlySpeed, FieldIds.ClimbSpeed, FieldIds.SwimSpeed], ids.Skip(fly).Take(3));
        // No other field reads or traces the two new ones, so every other field's value and trace are what they were.
        foreach (var field in sheet.Fields.Where(f => f.Field is not (FieldIds.ClimbSpeed or FieldIds.SwimSpeed)))
        {
            Assert.DoesNotContain(field.Trace, t => t.Field is FieldIds.ClimbSpeed or FieldIds.SwimSpeed);
            Assert.DoesNotContain(field.Trace, t => t.Inputs?.Any(i => i.Name is FieldIds.ClimbSpeed or FieldIds.SwimSpeed) == true);
        }
        Assert.Equal(sheet.Field(FieldIds.Speed).Value, Sheet(Handler(), Feature("Fixture Pace G", 7, 12, Mod("m", ModifierOperation.Bonus, "speed.climb", "5"))).Field(FieldIds.Speed).Value);
    }
}
