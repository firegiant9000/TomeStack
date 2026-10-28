namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 3, Task 3.2 (D43, content schema v11): the upkeep effect, counting-up pools, allowances and toggle
/// durations: the version gate that keeps older revisions byte for byte, validation, and what the sheet lists.
/// </summary>
public class UpkeepContentTests
{
    private static ValidationReport Validate(ContentRevision revision) => ContentValidator.Validate(revision, Fixtures.PlayMechanicsCatalog());

    private static Character Turns(params ContentReference[] pins) =>
        Fixtures.Srd521Character() with { Pins = [.. Fixtures.Srd521Character().Pins, .. pins] };

    private static readonly ContentReference[] AllTurnPins =
        [Fixtures.SteadyDiscipline, Fixtures.IronStance, Fixtures.FlareOverlay, Fixtures.QuickGuard, Fixtures.SecondWindRhythm];

    // ---- the version gate ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(6)]
    public void A_resource_carrying_v11_keys_below_v11_stays_byte_for_byte_and_untyped(int version)
    {
        var json = """{"type":"resource","id":"r","resourceId":"r","label":"R","maximum":"3","automation":"automatic","timing":"always","resourceFull":{"text":"Full"},"counts":"up","allowance":true}""";
        var resource = Assert.IsType<ResourceEffect>(Fixtures.RevisionWithEffect(json, version).Effects.Single());

        Assert.Null(resource.Counts);
        Assert.Null(resource.ResourceFull);
        Assert.Null(resource.Allowance);
        Assert.Equal(json, Fixtures.EffectJson(resource));
    }

    [Fact]
    public void A_v10_resource_keeps_its_activation_typed_while_the_v11_keys_stay_extension_data()
    {
        var json = """{"type":"resource","id":"r","resourceId":"r","label":"R","maximum":"3","activation":"reaction","automation":"automatic","timing":"always","counts":"up"}""";
        var resource = Assert.IsType<ResourceEffect>(Fixtures.RevisionWithEffect(json, 10).Effects.Single());

        Assert.Equal(Activation.Reaction, resource.Activation);
        Assert.Null(resource.Counts);
        Assert.Equal(json, Fixtures.EffectJson(resource));
    }

    [Fact]
    public void A_v11_resource_types_its_counting_up_keys_and_writes_them_back_unchanged()
    {
        var json = """{"type":"resource","id":"r","resourceId":"r","label":"R","maximum":"3","counts":"up","resourceFull":{"text":"Full"},"allowance":true,"automation":"automatic","timing":"always"}""";
        var resource = Assert.IsType<ResourceEffect>(Fixtures.RevisionWithEffect(json, 11).Effects.Single());

        Assert.Equal(ResourceCounting.Up, resource.Counts);
        Assert.Equal("Full", resource.ResourceFull!.Text);
        Assert.True(resource.Allowance);
        Assert.Null(resource.Extensions);
        Assert.Equal(json, Fixtures.EffectJson(resource));
    }

    [Fact]
    public void A_v11_resource_with_an_unknown_counts_value_stays_untyped()
    {
        var json = """{"type":"resource","id":"r","resourceId":"r","label":"R","maximum":"3","counts":"sideways"}""";
        var effect = Fixtures.RevisionWithEffect(json, 11).Effects.Single();

        Assert.IsType<UnknownEffect>(effect);
        Assert.Equal(json, Fixtures.EffectJson(effect));
    }

    [Fact]
    public void A_resource_without_v11_fields_serializes_without_them()
    {
        var resource = new ResourceEffect { Id = "r", ResourceId = "r", Label = "R", Maximum = "3" };
        Assert.Equal("""{"type":"resource","id":"r","resourceId":"r","label":"R","maximum":"3","automation":"automatic","timing":"always"}""", Fixtures.EffectJson(resource));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(6)]
    public void A_toggle_carrying_duration_below_v11_stays_byte_for_byte(int version)
    {
        var json = """{"type":"toggle","id":"t","toggleId":"t","label":"T","automation":"automatic","timing":"always","duration":"3"}""";
        var toggle = Assert.IsType<ToggleEffect>(Fixtures.RevisionWithEffect(json, version).Effects.Single());

        Assert.Null(toggle.Duration);
        Assert.Equal(json, Fixtures.EffectJson(toggle));
    }

    [Fact]
    public void A_v11_toggle_types_its_duration()
    {
        var json = """{"type":"toggle","id":"t","toggleId":"t","label":"T","duration":"3","automation":"automatic","timing":"always"}""";
        var toggle = Assert.IsType<ToggleEffect>(Fixtures.RevisionWithEffect(json, 11).Effects.Single());

        Assert.Equal("3", toggle.Duration);
        Assert.Equal(json, Fixtures.EffectJson(toggle));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(6)]
    public void An_upkeep_in_an_older_revision_stays_an_unknown_effect_byte_for_byte(int version)
    {
        var json = """{"type":"upkeep","id":"u","at":"turnStart","resourceId":"r","change":"spend","amount":"1"}""";
        var effect = Fixtures.RevisionWithEffect(json, version).Effects.Single();

        Assert.IsType<UnknownEffect>(effect);
        Assert.Equal(json, Fixtures.EffectJson(effect));
    }

    [Fact]
    public void A_v11_upkeep_is_typed_and_a_malformed_one_stays_untyped()
    {
        var typed = Fixtures.RevisionWithEffect("""{"type":"upkeep","id":"u","at":"turnEnd","resourceId":"r","change":"regain","amount":"all","ifShort":"end"}""", 11).Effects.Single();
        var upkeep = Assert.IsType<UpkeepEffect>(typed);
        Assert.Equal((TurnMoment.TurnEnd, UpkeepChange.Regain, UpkeepShortfall.End), (upkeep.At, upkeep.Change, upkeep.IfShort));

        Assert.IsType<UnknownEffect>(Fixtures.RevisionWithEffect("""{"type":"upkeep","id":"u","at":"midnight","resourceId":"r","change":"spend","amount":"1"}""", 11).Effects.Single());
        Assert.IsType<UnknownEffect>(Fixtures.RevisionWithEffect("""{"type":"upkeep","id":"u","resourceId":"r","change":"spend","amount":"1"}""", 11).Effects.Single());
    }

    [Fact]
    public void The_resource_v11_key_lists_match_the_properties_the_build_added()
    {
        string[] pinned = ["Id", "Automation", "Timing", "Text", "Extensions", "ResourceId", "Label", "Maximum", "Activation"];
        var added = typeof(ResourceEffect).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && !pinned.Contains(p.Name))
            .Select(p => p.Name)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var field = typeof(ResourceEffect).GetField("V11Keys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var production = ((IEnumerable<string>)field.GetValue(null)!).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(production, added, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(production, Properties.RevisionHashProperties.ResourceV11Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    // ---- validation ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("incomplete", "validate.upkeep-incomplete")]
    [InlineData("toggle-unknown", "validate.upkeep-toggle-unknown")]
    [InlineData("toggle-content-without-toggle", "validate.upkeep-toggle-unknown")]
    [InlineData("resource", "validate.upkeep-resource")]
    [InlineData("amount-invalid", "validate.upkeep-amount")]
    [InlineData("spend-all", "validate.upkeep-amount")]
    [InlineData("if-short-on-regain", "validate.upkeep-if-short")]
    [InlineData("if-short-without-toggle", "validate.upkeep-if-short")]
    [InlineData("too-many", "validate.upkeep-too-many")]
    [InlineData("resource-full", "validate.resource-full")]
    [InlineData("toggle-duration", "validate.toggle-duration")]
    [InlineData("auto-duration", "validate.toggle-auto-group")]
    [InlineData("below-v11-upkeep", "validate.requires-v11")]
    [InlineData("below-v11-counts", "validate.requires-v11")]
    [InlineData("below-v11-duration", "validate.requires-v11")]
    [InlineData("v10-duration-alone", "validate.requires-v11")]
    [InlineData("v10-counts-alone", "validate.requires-v11")]
    [InlineData("v10-resource-full-alone", "validate.requires-v11")]
    [InlineData("resource-full-blank", "validate.resource-full")]
    [InlineData("toggle-content-missing", "validate.upkeep-toggle-unknown")]
    [InlineData("resource-content-missing", "validate.upkeep-resource")]
    [InlineData("spend-all-padded", "validate.upkeep-amount")]
    public void Upkeep_mistakes_are_errors(string variant, string code) =>
        Assert.Contains(Validate(UpkeepVariants.Make(variant)).Errors, e => e.Code == code);

    [Theory]
    [InlineData("v10-duration-alone")]
    [InlineData("v10-counts-alone")]
    [InlineData("v10-resource-full-alone")]
    public void A_single_v11_key_alone_requires_v11(string variant) =>
        Assert.Equal(11, Validate(UpkeepVariants.Make(variant)).RequiredSchemaVersion);

    [Theory]
    [InlineData("""{"type":"upkeep","id":"u","at":"midnight","resourceId":"r","change":"spend","amount":"1"}""")]
    [InlineData("""{"type":"resource","id":"r","resourceId":"r","label":"R","maximum":"3","counts":"sideways"}""")]
    public void A_malformed_v11_effect_is_an_error_that_needs_v11(string json)
    {
        var report = Validate(Fixtures.RevisionWithEffect(json, 11));
        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_padded_all_regains_everything_in_validation_and_calculation()
    {
        var guard = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.QuickGuard)!;
        var padded = guard with { Effects = [.. guard.Effects.Select(e => e is UpkeepEffect u ? u with { Amount = " all " } : e)] };
        Assert.DoesNotContain(Validate(padded).Errors, e => e.Code == "validate.upkeep-amount");

        var sheet = CharacterCalculator.Calculate(Turns(Fixtures.QuickGuard), CatalogReplacing(padded));
        var upkeep = Assert.Single(sheet.Upkeeps!);
        Assert.True(upkeep.All);
        Assert.Null(upkeep.Amount);
    }

    [Fact]
    public void A_toggle_duration_is_clamped_to_the_most_rounds_a_toggle_can_have()
    {
        var iron = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.IronStance)!;
        var tooLong = iron with { Effects = [.. iron.Effects.Select(e => e is ToggleEffect t ? t with { Duration = "10000 + 10000" } : e)] };

        var sheet = CharacterCalculator.Calculate(Turns(Fixtures.SteadyDiscipline, Fixtures.IronStance), CatalogReplacing(tooLong));

        Assert.Equal(PlayState.MaxRounds, sheet.Toggles!.Single(t => t.ToggleId == "iron").Duration);
    }

    private static InMemoryContentCatalog CatalogReplacing(ContentRevision replacement)
    {
        ContentPack[] packs = [Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack(), Fixtures.TurnsPack()];
        return new([.. packs.SelectMany(p => p.Sources)], [.. packs.SelectMany(p => p.Revisions).Where(r => r.Reference != replacement.Reference), replacement]);
    }

    [Fact]
    public void The_turns_pack_validates_without_errors_or_warnings()
    {
        foreach (var reference in AllTurnPins)
        {
            var report = Validate(Fixtures.PlayMechanicsCatalog().FindRevision(reference)!);
            Assert.Empty(report.Errors);
            Assert.Empty(report.Warnings);
            Assert.Equal(11, report.RequiredSchemaVersion);
        }
    }

    [Theory]
    [InlineData("cost-from-counting-up-roll", "validate.cost-counts-up")]
    [InlineData("cost-from-counting-up-toggle", "validate.cost-counts-up")]
    [InlineData("allowance-no-refresh", "validate.allowance-no-refresh")]
    public void A_cost_from_a_counting_up_pool_or_an_unrefreshed_allowance_warns(string variant, string code)
    {
        var report = Validate(UpkeepVariants.Make(variant));
        Assert.Contains(report.Warnings, w => w.Code == code);
        Assert.DoesNotContain(report.Errors, e => e.Code == code);
    }

    [Fact]
    public void A_cost_from_a_counting_up_pool_warns()
    {
        var report = Validate(UpkeepVariants.Make("cost-from-counting-up-roll"));
        var warning = Assert.Single(report.Warnings, w => w.Code == "validate.cost-counts-up");
        Assert.Equal("parry", warning.EffectId);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Content_using_no_v11_field_still_publishes_at_its_old_version()
    {
        var stance = Fixtures.AllCatalog().FindRevision(Fixtures.RadiantStance)!;
        Assert.Equal(6, ContentValidator.Validate(stance with { SchemaVersion = 11 }, Fixtures.AllCatalog()).RequiredSchemaVersion);
    }

    [Fact]
    public void A_resource_or_roll_without_v11_fields_still_publishes_at_v6()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"resource","id":"pool","resourceId":"pool","label":"Pool","maximum":"2"}""", 11);
        var withRoll = revision with
        {
            Effects = [.. revision.Effects, new RollEffect { Id = "r", RollId = "r", Label = "R", Dice = "1d4", ResourceId = "pool", Cost = "1" }],
        };
        Assert.Equal(6, Validate(withRoll).RequiredSchemaVersion);
    }

    [Fact]
    public void A_pool_field_added_to_a_resource_raises_the_required_version_to_11()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"resource","id":"pool","resourceId":"pool","label":"Pool","maximum":"2","allowance":true}""", 11);
        Assert.Equal(11, Validate(revision).RequiredSchemaVersion);
    }

    // ---- the sheet -----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_sheet_lists_upkeeps_in_content_order_with_evaluated_amounts()
    {
        var sheet = CharacterCalculator.Calculate(Turns(AllTurnPins), Fixtures.PlayMechanicsCatalog());

        var upkeeps = sheet.Upkeeps!;
        Assert.Equal(["iron-upkeep", "flare-breath", "flare-strain", "guard-refresh", "rhythm"], upkeeps.Select(u => u.EffectId));
        Assert.Equal([Fixtures.IronStance, Fixtures.FlareOverlay, Fixtures.FlareOverlay, Fixtures.QuickGuard, Fixtures.SecondWindRhythm], upkeeps.Select(u => u.Content));
        Assert.Equal([2, 1, 1, null, 1], upkeeps.Select(u => u.Amount));
        Assert.Equal([false, false, false, true, false], upkeeps.Select(u => u.All));
        Assert.Equal([UpkeepShortfall.Ask, UpkeepShortfall.End, UpkeepShortfall.Ask, UpkeepShortfall.Ask, UpkeepShortfall.Ask], upkeeps.Select(u => u.IfShort));
        Assert.Equal([TurnMoment.TurnStart, TurnMoment.TurnStart, TurnMoment.TurnStart, TurnMoment.TurnStart, TurnMoment.TurnEnd], upkeeps.Select(u => u.At));
        Assert.Equal(["iron", "flare", "flare", null, null], upkeeps.Select(u => u.ToggleId));
        Assert.Equal(
            [Fixtures.SteadyDiscipline.ContentId, Fixtures.SteadyDiscipline.ContentId, Fixtures.FlareOverlay.ContentId, Fixtures.QuickGuard.ContentId, Fixtures.SteadyDiscipline.ContentId],
            upkeeps.Select(u => u.ResourceContent));
        Assert.Equal(["strain", "guard"], upkeeps.Skip(2).Take(2).Select(u => u.ResourceId));
        Assert.All(upkeeps, u => Assert.False(string.IsNullOrWhiteSpace(u.Label)));
    }

    [Fact]
    public void The_mechanics_inventory_lists_upkeep_as_supported()
    {
        var sheet = CharacterCalculator.Calculate(Turns(AllTurnPins), Fixtures.PlayMechanicsCatalog());
        var report = MechanicsInventory.Of(sheet, new HashSet<Guid> { Fixtures.TurnsPack().Sources.Single().Id });

        Assert.Contains(report.Mechanics, m => m.EffectType == UpkeepEffect.TypeName);
        Assert.DoesNotContain(report.Unsupported, m => m.EffectType == UpkeepEffect.TypeName);
    }

    [Fact]
    public void A_character_with_no_upkeep_content_has_no_upkeep_list()
    {
        Assert.Null(CharacterCalculator.Calculate(Fixtures.Srd521Character(), Fixtures.PlayMechanicsCatalog()).Upkeeps);
    }

    [Fact]
    public void A_broken_upkeep_amount_gives_a_null_amount_and_a_warning_and_does_not_throw()
    {
        var broken = UpkeepVariants.Make("amount-invalid");
        ContentPack[] packs = [Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack(), Fixtures.TurnsPack()];
        var catalog = new InMemoryContentCatalog(
            [.. packs.SelectMany(p => p.Sources)],
            [.. packs.SelectMany(p => p.Revisions).Where(r => r.Reference != broken.Reference), broken]);
        var sheet = CharacterCalculator.Calculate(Turns(Fixtures.SteadyDiscipline, Fixtures.IronStance), catalog);

        var upkeep = Assert.Single(sheet.Upkeeps!, u => u.EffectId == "iron-upkeep");
        Assert.Null(upkeep.Amount);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.EffectId == "iron-upkeep");
    }

    [Fact]
    public void A_counting_up_pool_shows_built_up()
    {
        var character = Turns(AllTurnPins) with { Play = new PlayState { Resources = [new ResourceUse(Fixtures.FlareOverlay.ContentId, "strain", 2)] } };
        var sheet = CharacterCalculator.Calculate(character, Fixtures.PlayMechanicsCatalog());

        var strain = sheet.Resources!.Single(r => r.ResourceId == "strain");
        Assert.True(strain.CountsUp);
        Assert.Equal((2, 3), (strain.Spent, strain.Maximum));
        Assert.Equal("Flare ends; take a level of exhaustion", strain.FullText);
        Assert.False(strain.Allowance);

        var breath = sheet.Resources!.Single(r => r.ResourceId == "breath");
        Assert.False(breath.CountsUp);
        Assert.Null(breath.FullText);
        Assert.True(sheet.Resources!.Single(r => r.ResourceId == "guard").Allowance);
    }

    [Fact]
    public void A_toggle_shows_its_duration_and_the_rounds_left_while_on()
    {
        var off = CharacterCalculator.Calculate(Turns(AllTurnPins), Fixtures.PlayMechanicsCatalog());
        var iron = off.Toggles!.Single(t => t.ToggleId == "iron");
        Assert.Equal((3, null), (iron.Duration, iron.RoundsLeft));

        var on = CharacterCalculator.Calculate(
            Turns(AllTurnPins) with { Play = new PlayState { Encounter = new EncounterState(1), Toggles = [new ActiveToggle(Fixtures.IronStance.ContentId, "iron", 2)] } },
            Fixtures.PlayMechanicsCatalog());
        iron = on.Toggles!.Single(t => t.ToggleId == "iron");
        Assert.Equal((true, 3, 2), (iron.On, iron.Duration, iron.RoundsLeft));
        Assert.Null(on.Toggles!.Single(t => t.ToggleId == "flare").Duration);
    }

    [Fact]
    public void A_stored_count_outside_an_encounter_shows_no_rounds_left()
    {
        // A v10 character can carry a count with no encounter (a hand-edited or imported file); the count is inert until an encounter starts.
        var sheet = CharacterCalculator.Calculate(
            Turns(AllTurnPins) with { Play = new PlayState { Toggles = [new ActiveToggle(Fixtures.IronStance.ContentId, "iron", 2)] } },
            Fixtures.PlayMechanicsCatalog());

        var iron = sheet.Toggles!.Single(t => t.ToggleId == "iron");
        Assert.Equal((true, 3, (int?)null), (iron.On, iron.Duration, iron.RoundsLeft));
    }

    [Fact]
    public void A_stored_count_on_a_toggle_whose_content_has_no_duration_shows_no_rounds_left()
    {
        var iron = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.IronStance)!;
        var untimed = iron with { Effects = [.. iron.Effects.Select(e => e is ToggleEffect t ? t with { Duration = null } : e)] };
        ContentPack[] packs = [Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack(), Fixtures.TurnsPack()];
        var catalog = new InMemoryContentCatalog(
            [.. packs.SelectMany(p => p.Sources)],
            [.. packs.SelectMany(p => p.Revisions).Where(r => r.Reference != untimed.Reference), untimed]);
        var character = Turns(AllTurnPins) with { Play = new PlayState { Encounter = new EncounterState(2), Toggles = [new ActiveToggle(Fixtures.IronStance.ContentId, "iron", 2)] } };

        var toggle = CharacterCalculator.Calculate(character, catalog).Toggles!.Single(t => t.ToggleId == "iron");
        Assert.Equal((true, (int?)null, (int?)null), (toggle.On, toggle.Duration, toggle.RoundsLeft));
    }
}

/// <summary>Revisions from the turns pack with one mistake each, for the validation theories.</summary>
internal static class UpkeepVariants
{
    private static ContentRevision Pack(ContentReference reference) => Fixtures.PlayMechanicsCatalog().FindRevision(reference)!;

    private static ContentRevision With(ContentReference reference, Func<Effect, Effect> change)
    {
        var revision = Pack(reference);
        return revision with { Effects = [.. revision.Effects.Select(change)] };
    }

    private static ContentRevision WithUpkeep(ContentReference reference, string id, Func<UpkeepEffect, UpkeepEffect> change) =>
        With(reference, e => e is UpkeepEffect u && u.Id == id ? change(u) : e);

    public static ContentRevision Make(string variant) => variant switch
    {
        "incomplete" => WithUpkeep(Fixtures.IronStance, "iron-upkeep", u => u with { Amount = " " }),
        "toggle-unknown" => WithUpkeep(Fixtures.IronStance, "iron-upkeep", u => u with { Toggle = "nonesuch" }),
        "toggle-content-without-toggle" => WithUpkeep(Fixtures.SecondWindRhythm, "rhythm", u => u with { ToggleContent = Fixtures.IronStance.ContentId }),
        "resource" => WithUpkeep(Fixtures.SecondWindRhythm, "rhythm", u => u with { ResourceId = "nonesuch" }),
        "amount-invalid" => WithUpkeep(Fixtures.IronStance, "iron-upkeep", u => u with { Amount = "2 +" }),
        "spend-all" => WithUpkeep(Fixtures.IronStance, "iron-upkeep", u => u with { Amount = "all" }),
        "if-short-on-regain" => WithUpkeep(Fixtures.SecondWindRhythm, "rhythm", u => u with { IfShort = UpkeepShortfall.End }),
        "if-short-without-toggle" => WithUpkeep(Fixtures.QuickGuard, "guard-refresh", u => u with { Change = UpkeepChange.Spend, Amount = "1", IfShort = UpkeepShortfall.End }),
        "too-many" => TooMany(),
        "resource-full" => With(Fixtures.FlareOverlay, e => e is ResourceEffect { ResourceId: "strain" } r ? r with { Counts = null } : e),
        "toggle-duration" => With(Fixtures.IronStance, e => e is ToggleEffect t ? t with { Duration = "3 +" } : e),
        "auto-duration" => With(Fixtures.IronStance, e => e is ToggleEffect t ? t with { AutoWhen = ToggleAutoWhen.Bloodied, ResourceId = null, ResourceContent = null, Cost = null, Group = null, GroupContent = null } : e),
        "below-v11-upkeep" => Pack(Fixtures.SecondWindRhythm) with { SchemaVersion = 10 },
        "below-v11-counts" => Pack(Fixtures.FlareOverlay) with { SchemaVersion = 10, Effects = [.. Pack(Fixtures.FlareOverlay).Effects.Where(e => e is ResourceEffect)] },
        "below-v11-duration" => Pack(Fixtures.IronStance) with { SchemaVersion = 10, Effects = [.. Pack(Fixtures.IronStance).Effects.Where(e => e is ToggleEffect)] },
        "v10-duration-alone" => Pack(Fixtures.IronStance) with
        {
            SchemaVersion = 10,
            Effects = [new ToggleEffect { Id = "iron", ToggleId = "iron", Label = "Iron stance", Duration = "3" }],
        },
        "v10-counts-alone" => Pack(Fixtures.SteadyDiscipline) with
        {
            SchemaVersion = 10,
            Effects = [new ResourceEffect { Id = "strain", ResourceId = "strain", Label = "Strain", Maximum = "3", Counts = ResourceCounting.Up }],
        },
        "v10-resource-full-alone" => Pack(Fixtures.SteadyDiscipline) with
        {
            SchemaVersion = 10,
            Effects = [new ResourceEffect { Id = "strain", ResourceId = "strain", Label = "Strain", Maximum = "3", ResourceFull = new("Full") }],
        },
        "resource-full-blank" => With(Fixtures.FlareOverlay, e => e is ResourceEffect { ResourceId: "strain" } r ? r with { ResourceFull = new("  ") } : e),
        "toggle-content-missing" => WithUpkeep(Fixtures.IronStance, "iron-upkeep", u => u with { ToggleContent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000ee") }),
        "resource-content-missing" => WithUpkeep(Fixtures.SecondWindRhythm, "rhythm", u => u with { ResourceContent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000ee") }),
        "spend-all-padded" => WithUpkeep(Fixtures.IronStance, "iron-upkeep", u => u with { Amount = " all " }),
        "cost-from-counting-up-roll" => With(Fixtures.QuickGuard, e => e is ResourceEffect r ? r with { Counts = ResourceCounting.Up } : e),
        "cost-from-counting-up-toggle" => With(Fixtures.FlareOverlay, e => e is ToggleEffect t ? t with { ResourceId = "strain" } : e),
        "allowance-no-refresh" => Pack(Fixtures.QuickGuard) with { Effects = [.. Pack(Fixtures.QuickGuard).Effects.Where(e => e is not UpkeepEffect)] },
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "unknown variant"),
    };

    private static ContentRevision TooMany()
    {
        var revision = Pack(Fixtures.SecondWindRhythm);
        var one = revision.Effects.OfType<UpkeepEffect>().Single();
        return revision with { Effects = [.. Enumerable.Range(0, ContentValidator.MaxUpkeepsPerRevision + 1).Select(i => one with { Id = $"rhythm-{i}" })] };
    }
}
