namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 5, Task 5.1 (D43, content schema v11): the roll details (save, area, range, targets, per-unit dice),
/// a roll with no dice, and named values (the <c>value</c> effect and <c>VALUE.&lt;id&gt;</c> in a formula): the version gate that
/// keeps older revisions byte for byte, validation, and the fixture pack.
/// </summary>
public class ActionDetailContentTests
{
    private static ContentPack[] Packs => [Fixtures.ActionDetailsPack()];

    private static ContentReference[] PackReferences => [Fixtures.WaveAdept, Fixtures.FocusOfBody, Fixtures.FocusOfMind, Fixtures.TideWave];

    private static ValidationReport Validate(ContentRevision revision, IEnumerable<ContentRevision>? batch = null) =>
        ContentValidator.Validate(revision, Fixtures.PlayMechanicsCatalog(), batch);

    // ---- the version gate ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(6)]
    public void A_v10_roll_carrying_v11_keys_stays_byte_for_byte_and_untyped(int version)
    {
        var json = """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","automation":"automatic","timing":"always","save":{"ability":"dex","dc":"10"},"area":{"shape":"cone","size":15},"range":"30 ft","targets":"One creature","perUnit":{"dice":"1d6"}}""";
        var roll = Assert.IsType<RollEffect>(Fixtures.RevisionWithEffect(json, version).Effects.Single());

        Assert.Null(roll.Save);
        Assert.Null(roll.Area);
        Assert.Null(roll.Range);
        Assert.Null(roll.Targets);
        Assert.Null(roll.PerUnit);
        Assert.Equal(json, Fixtures.EffectJson(roll));
    }

    [Theory]
    [InlineData(10, """{"type":"roll","id":"r","rollId":"r","label":"R","resourceId":"p","cost":"1","automation":"automatic","timing":"always"}""")]
    [InlineData(9, """{"type":"roll","id":"r","rollId":"r","label":"R","resourceId":"p","cost":"1","automation":"automatic","timing":"always"}""")]
    [InlineData(10, """{"type":"roll","id":"r","rollId":"r","label":"R","resourceId":"p","cost":"1","automation":"automatic","timing":"always","range":"Self"}""")]
    [InlineData(10, """{"type":"roll","id":"r","rollId":"r","label":"R","resourceId":"p","cost":"1","automation":"automatic","timing":"always","save":{"ability":"dex","dc":"10"}}""")]
    public void Dice_are_still_required_below_v11(int version, string json)
    {
        var revision = Fixtures.RevisionWithEffect(json, version);
        var effect = Assert.IsType<UnknownEffect>(revision.Effects.Single());
        Assert.Equal(json, Fixtures.EffectJson(effect));

        var report = Validate(revision);

        // What fires today for an untyped roll: the unsupported-effect warning. Nothing newly refuses old content.
        Assert.Contains(report.Warnings, w => w.Code == "validate.effect-unsupported" && w.EffectId == "r");
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.requires-v11");
        // A draft that omits dice publishes at v11 (the dice-less roll is typed only there).
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_v11_roll_may_omit_dice_when_it_has_a_cost()
    {
        var json = """{"type":"roll","id":"r","rollId":"r","label":"R","resourceId":"p","cost":"1","automation":"automatic","timing":"always"}""";
        var roll = Assert.IsType<RollEffect>(Fixtures.RevisionWithEffect(json, 11).Effects.Single());
        Assert.Null(roll.Dice);
        Assert.Equal(json, Fixtures.EffectJson(roll));

        var brace = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.TideWave)!.Effects.OfType<RollEffect>().Single(r => r.Id == "brace");
        Assert.Null(brace.Dice);
        var report = Validate(Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.TideWave)!, Packs.SelectMany(p => p.Revisions));
        Assert.Empty(report.Errors);
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_v11_roll_with_a_blank_dice_string_is_still_a_dice_error()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"","automation":"automatic","timing":"always"}""", 11);

        Assert.Contains(Validate(revision).Errors, e => e.Code == "validate.dice-invalid");
    }

    [Fact]
    public void Rolling_a_dice_less_roll_fails_clearly_instead_of_throwing()
    {
        var tide = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.TideWave)!;
        var brace = tide.Effects.OfType<RollEffect>().Single(r => r.Id == "brace");

        var request = DiceRoller.FromEffect(brace, tide, null);
        var rolled = DiceRoller.TryRoll(request, new FixedRandom(), out var record, out var error);

        Assert.False(rolled);
        Assert.Null(record);
        Assert.NotNull(error);
    }

    private sealed class FixedRandom : IRandomSource
    {
        public int Next(int sides) => 1;
    }

    [Theory]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","save":{"ability":"luck","dc":"10"}}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","save":{"ability":"dex, con","dc":"10"}}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","save":{"ability":" dex","dc":"10"}}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","save":{"ability":"dex"}}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","area":{"shape":"blob","size":10}}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","area":{"shape":" line","size":10}}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","area":{"shape":"line"}}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","perUnit":"1d6"}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","perUnit":{"maxUnits":2}}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","resourceId":"p","cost":"1","save":{"ability":"luck","dc":"10"}}""")]
    public void A_roll_with_an_unreadable_v11_key_stays_untyped_reports_malformed_and_requires_v11(string json)
    {
        var revision = Fixtures.RevisionWithEffect(json, 11);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_v11_value_effect_is_typed_below_v11_it_stays_untyped_byte_for_byte()
    {
        var json = """{"type":"value","id":"v","valueId":"focusMod","formula":"WIS.MOD","automation":"automatic","timing":"always"}""";
        var value = Assert.IsType<ValueEffect>(Fixtures.RevisionWithEffect(json, 11).Effects.Single());
        Assert.Equal("focusMod", value.ValueId);
        Assert.Equal("WIS.MOD", value.Formula);
        Assert.Equal(json, Fixtures.EffectJson(value));

        foreach (var version in new[] { 10, 9, 6 })
        {
            var old = Fixtures.RevisionWithEffect(json, version).Effects.Single();
            Assert.IsType<UnknownEffect>(old);
            Assert.Equal(json, Fixtures.EffectJson(old));
        }
    }

    [Fact]
    public void A_v10_formula_with_VALUE_is_invalid()
    {
        Assert.False(Formula.TryParse("VALUE.x", FormulaFeatures.Scales, out _, out var error));
        Assert.Equal("formula.unknown-identifier", error!.Code);
        Assert.False(Formula.TryParse("VALUE.x", allowScales: true, out _, out _));
        Assert.True(Formula.TryParse("VALUE.x + SCALE.y", FormulaFeatures.Scales | FormulaFeatures.Values, out _, out _));
        Assert.False(Formula.TryParse("VALUE.Bad", FormulaFeatures.Values, out _, out _));

        var v10 = Fixtures.RevisionWithEffect("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"VALUE.x"}""", 10);
        var report = Validate(v10);
        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v11");
        Assert.Equal(11, report.RequiredSchemaVersion);

        var v11 = Fixtures.RevisionWithEffect("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"VALUE.x"}""", 11);
        Assert.DoesNotContain(Validate(v11).Errors, e => e.Code is "validate.requires-v11" or "validate.formula-invalid");
    }

    [Fact]
    public void Content_using_no_v11_field_still_publishes_at_its_old_version()
    {
        var stance = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.RadiantStance)!;

        Assert.Equal(6, ContentValidator.Validate(stance with { SchemaVersion = 11 }, Fixtures.PlayMechanicsCatalog()).RequiredSchemaVersion);
    }

    [Fact]
    public void The_roll_v11_key_list_has_the_new_keys()
    {
        string[] keys = ["whileToggle", "save", "area", "range", "targets", "perUnit", "alsoChange", "drawsDown"];

        Assert.Equal(keys.Order(StringComparer.Ordinal), RollKeys().Order(StringComparer.Ordinal));

        static IEnumerable<string> RollKeys() =>
            (IEnumerable<string>)typeof(RollEffect).GetField("V11Keys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    }

    // ---- validation: errors --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("save-dc-invalid", "validate.roll-save", "wave")]
    [InlineData("save-dc-blank", "validate.roll-save", "wave")]
    [InlineData("save-dc-unknown-identifier", "validate.roll-save", "wave")]
    [InlineData("area-size-zero", "validate.roll-area", "wave")]
    [InlineData("area-size-too-big", "validate.roll-area", "wave")]
    [InlineData("area-width-on-cone", "validate.roll-area", "wave")]
    [InlineData("area-width-zero", "validate.roll-area", "wave")]
    [InlineData("area-width-too-big", "validate.roll-area", "wave")]
    [InlineData("range-too-long", "validate.roll-text-too-long", "wave")]
    [InlineData("targets-too-long", "validate.roll-text-too-long", "wave")]
    [InlineData("per-unit-bad-die", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-zero-count", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-with-modifier", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-padded", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-no-cost", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-no-resource", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-max-zero", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-max-too-big", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-variable-cost", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-too-many-dice", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-no-dice", "validate.roll-per-unit", "wave")]
    [InlineData("per-unit-unrollable-formula", "validate.roll-per-unit", "wave")]
    [InlineData("neither-dice-nor-cost", "validate.roll-dice-or-cost", "brace")]
    [InlineData("detail-below-v11", "validate.requires-v11", null)]
    [InlineData("dice-less-typed-below-v11", "validate.requires-v11", null)]
    [InlineData("value-bad-id", "validate.value-incomplete", "focus-mod")]
    [InlineData("value-blank-id", "validate.value-incomplete", "focus-mod")]
    [InlineData("value-blank-formula", "validate.value-incomplete", "focus-mod")]
    [InlineData("value-duplicate", "validate.value-duplicate", null)]
    [InlineData("value-formula-invalid", "validate.formula-invalid", "focus-mod")]
    [InlineData("value-below-v11", "validate.requires-v11", null)]
    [InlineData("value-assisted", "validate.value-incomplete", "focus-mod")]
    [InlineData("value-reference", "validate.value-incomplete", "focus-mod")]
    public void Roll_and_value_mistakes_are_errors(string variant, string code, string? effectId)
    {
        var errors = Validate(ActionDetailVariants.Make(variant)).Errors.Where(e => e.Code == code).ToList();

        Assert.NotEmpty(errors);
        if (effectId is not null)
            Assert.Contains(errors, e => e.EffectId == effectId);
    }

    [Fact]
    public void The_most_dice_a_per_unit_roll_can_throw_is_100_and_101_is_refused()
    {
        // The wave rolls 4d10; one per-unit die for each of 96 units makes exactly 100 dice, and 97 units make 101.
        var at100 = Validate(ActionDetailVariants.WaveWith(r => r with { PerUnit = new("1d10", 96) })).Errors;
        var at101 = Validate(ActionDetailVariants.WaveWith(r => r with { PerUnit = new("1d10", 97) })).Errors;

        Assert.DoesNotContain(at100, e => e.Code == "validate.roll-per-unit");
        Assert.Contains(at101, e => e.Code == "validate.roll-per-unit" && e.EffectId == "wave");
    }

    [Fact]
    public void A_per_unit_formula_that_cannot_be_rolled_says_why()
    {
        var errors = Validate(ActionDetailVariants.Make("per-unit-unrollable-formula")).Errors.Where(e => e.Code == "validate.roll-per-unit").ToList();

        Assert.Contains(errors, e => e.Message.Contains("too many terms or too long", StringComparison.Ordinal));
    }

    [Fact]
    public void A_non_automatic_value_says_a_named_value_is_always_calculated()
    {
        var errors = Validate(ActionDetailVariants.Make("value-assisted")).Errors;

        Assert.Contains(errors, e => e.Code == "validate.value-incomplete" && e.Message.Contains("must be automatic", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_with_no_formula_in_json_stays_untyped_and_is_value_incomplete()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"value","id":"v","valueId":"focusMod"}""", 11);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.value-incomplete" && e.EffectId == "v");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_bad_save_dc_reports_the_roll_save_code_not_the_generic_formula_code()
    {
        var errors = Validate(ActionDetailVariants.Make("save-dc-invalid")).Errors;

        Assert.DoesNotContain(errors, e => e.Code == "validate.formula-invalid" && e.EffectId == "wave");
    }

    [Fact]
    public void A_value_read_that_nothing_declares_is_a_warning_and_a_declaration_clears_it()
    {
        var reader = Fixtures.RevisionWithEffect("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"VALUE.focusMod"}""", 11) with { Kind = ContentKind.Class };

        var alone = Validate(reader);
        Assert.Contains(alone.Warnings, w => w.Code == "validate.value-unknown");
        Assert.DoesNotContain(alone.Errors, e => e.Code == "validate.value-unknown");

        var focus = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.FocusOfMind)!;
        var granting = reader with { Effects = [.. reader.Effects, new GrantEffect { Id = "g", Grant = GrantKind.Content, Content = Fixtures.FocusOfMind }] };
        Assert.DoesNotContain(Validate(granting, [focus]).Warnings, w => w.Code == "validate.value-unknown");

        // A declaration in the batch that the class neither grants nor offers does not count.
        Assert.Contains(Validate(reader, [focus]).Warnings, w => w.Code == "validate.value-unknown");

        var own = reader with { Effects = [.. reader.Effects, new ValueEffect { Id = "v", ValueId = "focusMod", Formula = "1" }] };
        Assert.DoesNotContain(Validate(own).Warnings, w => w.Code == "validate.value-unknown");
    }

    [Fact]
    public void A_dice_less_roll_renders_in_the_content_tree_without_empty_parentheses()
    {
        var pack = Fixtures.ActionDetailsPack();
        var tree = ContentTree.Build(ContentGraph.Build(pack.Revisions), Fixtures.WaveAdept.ContentId);
        static IEnumerable<TreeNode> All(TreeNode n) => n.Children.SelectMany(All).Prepend(n);

        var labels = All(tree.Root).Select(n => n.Label).Where(l => l.StartsWith("Roll:", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(labels);
        Assert.DoesNotContain(labels, l => l.Contains("()", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_feature_is_a_known_mechanic_not_an_unsupported_one()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Load("pm-wave-adept-l5.json"), Fixtures.PlayMechanicsCatalog());

        var report = MechanicsInventory.Of(sheet, new HashSet<Guid> { Fixtures.ActionDetailsPack().Sources.Single().Id });

        Assert.Contains(report.Mechanics, m => m.EffectType == ValueEffect.TypeName);
        Assert.Empty(report.Unsupported);
    }

    [Fact]
    public void A_feature_reading_a_value_gets_no_value_unknown_warning()
    {
        var tide = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.TideWave)!;

        Assert.DoesNotContain(Validate(tide).Warnings, w => w.Code == "validate.value-unknown");
    }

    // ---- the fixture pack ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_action_details_pack_validates_without_errors_or_warnings_at_v11()
    {
        var batch = Packs.SelectMany(p => p.Revisions).ToList();
        foreach (var reference in PackReferences)
        {
            var revision = Fixtures.PlayMechanicsCatalog().FindRevision(reference)!;
            var report = Validate(revision, batch);
            Assert.True(report.Errors.Count == 0, $"{revision.Name}: {string.Join("; ", report.Errors.Select(e => $"{e.Code} {e.Message}"))}");
            Assert.True(report.Warnings.Count == 0, $"{revision.Name}: {string.Join("; ", report.Warnings.Select(e => $"{e.Code} {e.Message}"))}");
            // The class uses no v11 field itself (its choice and grant are older); the three features each use one.
            Assert.Equal(reference == Fixtures.WaveAdept ? 3 : ContentRevision.PlayMechanicsSchemaVersion, report.RequiredSchemaVersion);
        }
    }

    [Fact]
    public void The_pack_types_every_new_effect_and_key()
    {
        var catalog = Fixtures.PlayMechanicsCatalog();
        Assert.All(PackReferences, r => Assert.DoesNotContain(catalog.FindRevision(r)!.Effects, e => e is UnknownEffect));
        Assert.Equal("CON.MOD", catalog.FindRevision(Fixtures.FocusOfBody)!.Effects.OfType<ValueEffect>().Single().Formula);
        Assert.Equal("WIS.MOD", catalog.FindRevision(Fixtures.FocusOfMind)!.Effects.OfType<ValueEffect>().Single().Formula);

        var wave = catalog.FindRevision(Fixtures.TideWave)!.Effects.OfType<RollEffect>().Single(r => r.Id == "wave");
        Assert.Equal("4d10", wave.Dice);
        Assert.Equal(Fixtures.WaveAdept.ContentId, wave.ResourceContent);
        Assert.Equal(new RollSave(Ability.Dex, "8 + PB + VALUE.focusMod"), wave.Save);
        Assert.Equal(new RollArea(AreaShape.Line, 100, 5), wave.Area);
        Assert.Equal(new RollPerUnit("1d10", 4), wave.PerUnit);
        Assert.Equal("Self", wave.Range);
        Assert.Equal("Each creature in the line", wave.Targets);
    }

    [Fact]
    public void The_wave_adept_at_level_5_has_the_chosen_value_feature_and_the_tide_wave()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Load("pm-wave-adept-l5.json"), Fixtures.PlayMechanicsCatalog());

        Assert.NotNull(sheet.Active);
        Assert.Contains(Fixtures.WaveAdept, sheet.Active!);
        Assert.Contains(Fixtures.FocusOfMind, sheet.Active!);
        Assert.Contains(Fixtures.TideWave, sheet.Active!);
        Assert.DoesNotContain(Fixtures.FocusOfBody, sheet.Active!);
    }
}

/// <summary>Revisions from the action details pack with one mistake each, for the validation theory.</summary>
internal static class ActionDetailVariants
{
    private static ContentRevision Pack(ContentReference reference) => Fixtures.PlayMechanicsCatalog().FindRevision(reference)!;

    private static ContentRevision WithWave(Func<RollEffect, RollEffect> change)
    {
        var revision = Pack(Fixtures.TideWave);
        return revision with { Effects = [.. revision.Effects.Select(e => e is RollEffect { Id: "wave" } r ? change(r) : e)] };
    }

    private static ContentRevision WithBrace(Func<RollEffect, RollEffect> change)
    {
        var revision = Pack(Fixtures.TideWave);
        return revision with { Effects = [.. revision.Effects.Select(e => e is RollEffect { Id: "brace" } r ? change(r) : e)] };
    }

    private static ContentRevision WithValue(Func<ValueEffect, ValueEffect> change)
    {
        var revision = Pack(Fixtures.FocusOfBody);
        return revision with { Effects = [.. revision.Effects.Select(e => e is ValueEffect v ? change(v) : e)] };
    }

    public static ContentRevision WaveWith(Func<RollEffect, RollEffect> change) => WithWave(change);

    public static ContentRevision Make(string variant) => variant switch
    {
        "save-dc-invalid" => WithWave(r => r with { Save = new(Ability.Dex, "8 +") }),
        "save-dc-blank" => WithWave(r => r with { Save = new(Ability.Dex, " ") }),
        "save-dc-unknown-identifier" => WithWave(r => r with { Save = new(Ability.Dex, "8 + NOPE") }),
        "area-size-zero" => WithWave(r => r with { Area = new(AreaShape.Line, 0, 5) }),
        "area-size-too-big" => WithWave(r => r with { Area = new(AreaShape.Line, 1001, 5) }),
        "area-width-on-cone" => WithWave(r => r with { Area = new(AreaShape.Cone, 15, 5) }),
        "area-width-zero" => WithWave(r => r with { Area = new(AreaShape.Line, 100, 0) }),
        "area-width-too-big" => WithWave(r => r with { Area = new(AreaShape.Line, 100, 1001) }),
        "range-too-long" => WithWave(r => r with { Range = new string('x', 201) }),
        "targets-too-long" => WithWave(r => r with { Targets = new string('x', 201) }),
        "per-unit-bad-die" => WithWave(r => r with { PerUnit = new("1d7", 4) }),
        "per-unit-zero-count" => WithWave(r => r with { PerUnit = new("0d6", 4) }),
        "per-unit-with-modifier" => WithWave(r => r with { PerUnit = new("1d6+1", 4) }),
        "per-unit-padded" => WithWave(r => r with { PerUnit = new(" 1d6", 4) }),
        "per-unit-no-cost" => WithWave(r => r with { Cost = null }),
        "per-unit-no-resource" => WithWave(r => r with { ResourceId = null, ResourceContent = null }),
        "per-unit-max-zero" => WithWave(r => r with { PerUnit = new("1d10", 0) }),
        "per-unit-max-too-big" => WithWave(r => r with { PerUnit = new("1d10", 101) }),
        "per-unit-too-many-dice" => WithWave(r => r with { PerUnit = new("1d10", 100) }),
        "per-unit-variable-cost" =>WithWave(r => r with { VariableCost = true }),
        "per-unit-no-dice" => WithWave(r => r with { Dice = null }),
        // Eight terms already: one more per-unit term would be a ninth, which the dice parser refuses.
        "per-unit-unrollable-formula" => WithWave(r => r with { Dice = "1d4+1d4+1d4+1d4+1d4+1d4+1d4+1d4", PerUnit = new("1d10", 4) }),
        "neither-dice-nor-cost" => WithBrace(r => r with { Cost = null }),
        "detail-below-v11" => WithWave(r => r) with { SchemaVersion = 10 },
        "dice-less-typed-below-v11" => WithBrace(r => r) with { SchemaVersion = 10 },
        "value-bad-id" => WithValue(v => v with { ValueId = "Bad Id" }),
        "value-blank-id" => WithValue(v => v with { ValueId = "" }),
        "value-blank-formula" => WithValue(v => v with { Formula = " " }),
        "value-duplicate" => AddValue(WithValue(v => v)),
        "value-formula-invalid" => WithValue(v => v with { Formula = "CON.MOD +" }),
        "value-below-v11" => WithValue(v => v) with { SchemaVersion = 10 },
        "value-assisted" => WithValue(v => v with { Automation = AutomationStatus.Assisted }),
        "value-reference" => WithValue(v => v with { Automation = AutomationStatus.Reference }),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
    };

    private static ContentRevision AddValue(ContentRevision revision) =>
        revision with { Effects = [.. revision.Effects, new ValueEffect { Id = "focus-mod-again", ValueId = "focusMod", Formula = "1" }] };
}
