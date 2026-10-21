using System.Reflection;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 1, Task 1.2 (D43, content schema v11): the validator's checks for toggle groups, overlays and pool
/// costs, and the version ladder that stops a v11 field from publishing below v11.
/// </summary>
public class ToggleGroupValidationTests
{
    private static ValidationReport Validate(ContentRevision revision) => ContentValidator.Validate(revision, Fixtures.PlayMechanicsCatalog());

    [Theory]
    [InlineData("unknown-group", "validate.toggle-group-unknown")]
    [InlineData("unknown-group-own-revision", "validate.toggle-group-unknown")]
    [InlineData("layer-without-group", "validate.toggle-layer")]
    [InlineData("allowed-with-on-primary", "validate.toggle-allowed-with")]
    [InlineData("allowed-in-base-on-primary", "validate.toggle-allowed-with")]
    [InlineData("allowed-with-unknown", "validate.toggle-allowed-with")]
    [InlineData("group-bad-id", "validate.toggle-group-incomplete")]
    [InlineData("group-blank-label", "validate.toggle-group-incomplete")]
    [InlineData("cost-without-resource", "validate.toggle-cost")]
    [InlineData("cost-formula-invalid", "validate.formula-invalid")]
    [InlineData("resource-content-lacks-resource", "validate.toggle-resource-content")]
    [InlineData("level-out-of-range", "validate.level")]
    [InlineData("duplicate-group", "validate.toggle-group-duplicate")]
    [InlineData("auto-with-group", "validate.toggle-auto-group")]
    [InlineData("below-v11", "validate.requires-v11")]
    public void Toggle_group_mistakes_are_errors(string variant, string code) =>
        Assert.Contains(Validate(ToggleGroupVariants.Make(variant)).Errors, e => e.Code == code);

    [Theory]
    [InlineData("group-content-missing", "validate.toggle-group-content-missing")]
    [InlineData("resource-content-missing", "validate.toggle-resource-content-missing")]
    [InlineData("allowed-with-unknown-content", "validate.toggle-allowed-with-content-missing")]
    [InlineData("overlay-unreachable", "validate.toggle-overlay-unreachable")]
    [InlineData("overlay-unreachable-absent", "validate.toggle-overlay-unreachable")]
    public void Toggle_group_oddities_are_warnings_and_do_not_block(string variant, string code)
    {
        var report = Validate(ToggleGroupVariants.Make(variant));
        Assert.Contains(report.Warnings, w => w.Code == code);
        Assert.DoesNotContain(report.Errors, e => e.Code == code);
        Assert.DoesNotContain(report.Errors, e => e.Code.StartsWith("validate.toggle-", StringComparison.Ordinal));
    }

    [Fact]
    public void A_group_content_that_is_installed_but_declares_no_such_group_is_an_error_not_a_warning()
    {
        var report = Validate(ToggleGroupVariants.Make("unknown-group"));
        Assert.Contains(report.Errors, e => e.Code == "validate.toggle-group-unknown");
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-group-content-missing");
    }

    [Fact]
    public void An_allowedWith_on_absent_content_is_a_warning_and_on_present_content_without_the_toggle_an_error()
    {
        var absent = Validate(ToggleGroupVariants.Make("allowed-with-unknown-content"));
        Assert.DoesNotContain(absent.Errors, e => e.Code == "validate.toggle-allowed-with");
        Assert.Contains(absent.Warnings, w => w.Code == "validate.toggle-allowed-with-content-missing");

        var present = Validate(ToggleGroupVariants.Make("allowed-with-unknown"));
        Assert.Contains(present.Errors, e => e.Code == "validate.toggle-allowed-with");
        Assert.DoesNotContain(present.Warnings, w => w.Code == "validate.toggle-allowed-with-content-missing");
    }

    [Fact]
    public void The_forms_pack_toggleGroup_raises_no_incomplete_error()
    {
        var discipline = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.FormDiscipline)!;
        Assert.Contains(discipline.Effects, e => e is ToggleGroupEffect);
        Assert.DoesNotContain(Validate(discipline).Errors, e => e.Code == "validate.toggle-group-incomplete");
    }

    [Fact]
    public void The_whole_forms_pack_validates_with_no_errors_and_publishes_at_v11()
    {
        var catalog = Fixtures.PlayMechanicsCatalog();
        foreach (var revision in Fixtures.FormsPack().Revisions)
        {
            var report = ContentValidator.Validate(revision, catalog);
            Assert.True(report.CanPublish, $"{revision.Name}: {string.Join("; ", report.Errors.Select(e => e.Code))}");
            Assert.Equal(ContentRevision.PlayMechanicsSchemaVersion, report.RequiredSchemaVersion);
        }
    }

    [Fact]
    public void The_lineage_species_joins_a_group_of_another_source_without_an_error()
    {
        var lineage = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.LineageForm)!;
        var report = Validate(lineage);
        Assert.Empty(report.Errors);
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-group-content-missing");
    }

    [Fact]
    public void The_forms_pack_overlays_that_can_be_on_raise_no_unreachable_warning()
    {
        foreach (var overlay in new[] { Fixtures.SurgeOverlay, Fixtures.BoundOverlay, Fixtures.CalmOverlay, Fixtures.CalmTideOverlay })
            Assert.DoesNotContain(Validate(Fixtures.PlayMechanicsCatalog().FindRevision(overlay)!).Warnings, w => w.Code == "validate.toggle-overlay-unreachable");
    }

    [Fact]
    public void A_toggle_with_a_group_requires_v11()
    {
        var ember = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.EmberForm)!;
        Assert.Equal(11, Validate(ember).RequiredSchemaVersion);
    }

    [Fact]
    public void A_toggleGroup_effect_requires_v11()
    {
        var discipline = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.FormDiscipline)!;
        Assert.Equal(11, Validate(discipline).RequiredSchemaVersion);
    }

    [Fact]
    public void A_toggle_with_only_a_level_requires_v11()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"toggle","id":"t","toggleId":"t","label":"T","level":3}""", schemaVersion: 11);
        Assert.Equal(11, Validate(revision).RequiredSchemaVersion);
    }

    [Fact]
    public void Content_using_no_v11_field_still_publishes_at_its_old_version()
    {
        var stance = Fixtures.AllCatalog().FindRevision(Fixtures.RadiantStance)!;
        Assert.Equal(6, ContentValidator.Validate(stance with { SchemaVersion = 11 }, Fixtures.AllCatalog()).RequiredSchemaVersion);
    }

    [Fact]
    public void A_bad_v11_enum_reports_malformed_and_requires_v11()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms","layer":"sideways"}""", schemaVersion: 11);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_bad_v10_enum_still_requires_v10()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"toggle","id":"t","toggleId":"t","label":"T","activation":"sideways"}""", schemaVersion: 10);
        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(10, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_malformed_v11_toggleGroup_reports_malformed_and_requires_v11()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"toggleGroup","id":"g","label":"Forms"}""", schemaVersion: 11);
        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void The_v11_toggle_properties_are_exactly_the_production_v11_key_list()
    {
        // Pinned: everything a v6 to v10 toggle already carries. A property outside this set is new, so it must be a v11 key.
        HashSet<string> pinned = new(StringComparer.OrdinalIgnoreCase)
        {
            "Id", "Automation", "Timing", "Text", "Extensions", "ToggleId", "Label", "ResourceId", "AutoWhen", "Activation",
        };
        var added = typeof(ToggleEffect).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && !pinned.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();

        Assert.NotEmpty(added);
        // The list the build uses to keep these keys out of older revisions (ToggleEffect.V11Keys, internal), read by reflection.
        var field = typeof(ToggleEffect).GetField("V11Keys", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var production = ((IEnumerable<string>)field.GetValue(null)!).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(production, added.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        // The hash tests keep their own copy; it must not drift from the production list either.
        Assert.Equal(production, Properties.RevisionHashProperties.ToggleV11Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_resource_defined_by_a_later_revision_of_the_holder_content_is_found()
    {
        var holder = Guid.Parse("6f0dc000-0000-4000-8000-0000000000e1");
        var discipline = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.FormDiscipline)!;
        var first = discipline with { ContentId = holder, RevisionId = Guid.Parse("6f0de000-0000-4000-8000-0000000000e1"), Effects = [] };
        var second = discipline with { ContentId = holder, RevisionId = Guid.Parse("6f0de000-0000-4000-8000-0000000000e2"), Effects = [.. discipline.Effects.Where(e => e is ResourceEffect)] };
        var ember = ToggleGroupVariants.Make("resource-content-lacks-resource");
        ember = ember with { Effects = [.. ember.Effects.Select(e => e is ToggleEffect t ? t with { ResourceContent = holder } : e)] };

        var report = ContentValidator.Validate(ember, Fixtures.PlayMechanicsCatalog(), [first, second]);

        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.toggle-resource-content");
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-resource-content-missing");
    }

    [Fact]
    public void A_group_content_without_a_group_is_a_group_unknown_error()
    {
        var revision = ToggleGroupVariants.Make("layer-without-group");
        revision = revision with { Effects = [.. revision.Effects.Select(e => e is ToggleEffect t ? t with { Layer = null, GroupContent = Fixtures.FormDiscipline.ContentId } : e)] };
        Assert.Contains(Validate(revision).Errors, e => e.Code == "validate.toggle-group-unknown");
    }

    [Fact]
    public void A_resource_content_without_a_resource_id_raises_no_diagnostic()
    {
        var revision = ToggleGroupVariants.Make("cost-without-resource");
        revision = revision with { Effects = [.. revision.Effects.Select(e => e is ToggleEffect t ? t with { Cost = null, ResourceId = null, ResourceContent = Fixtures.FormDiscipline.ContentId } : e)] };
        var report = Validate(revision);
        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
    }

    [Theory]
    [InlineData("""{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms","layer":"overlay","cost":"3","allowedWith":[],"level":2}""", 6)]
    [InlineData("""{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms","layer":"overlay","cost":"3","activation":"reaction"}""", 10)]
    public void A_v10_toggle_carrying_v11_keys_keeps_its_old_version_and_gets_no_v11_diagnostic(string json, int required)
    {
        var report = Validate(Fixtures.RevisionWithEffect(json, schemaVersion: 10));
        Assert.Equal(required, report.RequiredSchemaVersion);
        Assert.DoesNotContain(report.Errors.Concat(report.Warnings), d => d.Code.StartsWith("validate.toggle-group", StringComparison.Ordinal)
            || d.Code is "validate.toggle-layer" or "validate.toggle-cost" or "validate.toggle-allowed-with" or "validate.toggle-overlay-unreachable" or "validate.requires-v11" or "validate.level");
    }

    [Fact]
    public void A_v10_toggle_with_a_v11_key_and_a_bad_activation_still_requires_v10_not_v11()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms","activation":"sideways"}""", schemaVersion: 10);
        var report = Validate(revision);
        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(10, report.RequiredSchemaVersion);
    }
}

/// <summary>Revisions from the forms pack with one mistake each, for the validation theories.</summary>
internal static class ToggleGroupVariants
{
    private static readonly Guid Discipline = Fixtures.FormDiscipline.ContentId;
    private static readonly Guid Tide = Fixtures.TideForm.ContentId;

    private static ContentRevision Pack(ContentReference reference) => Fixtures.PlayMechanicsCatalog().FindRevision(reference)!;

    private static ContentRevision WithToggle(ContentReference reference, Func<ToggleEffect, ToggleEffect> change)
    {
        var revision = Pack(reference);
        return revision with { Effects = [.. revision.Effects.Select(e => e is ToggleEffect t ? change(t) : e)] };
    }

    private static ContentRevision WithGroup(Func<ToggleGroupEffect, ToggleGroupEffect> change)
    {
        var revision = Pack(Fixtures.FormDiscipline);
        return revision with { Effects = [.. revision.Effects.Select(e => e is ToggleGroupEffect g ? change(g) : e)] };
    }

    public static ContentRevision Make(string variant) => variant switch
    {
        "unknown-group" => WithToggle(Fixtures.EmberForm, t => t with { Group = "nonesuch" }),
        "unknown-group-own-revision" => WithToggle(Fixtures.EmberForm, t => t with { Group = "forms", GroupContent = null }),
        "layer-without-group" => WithToggle(Fixtures.EmberForm, t => t with { Group = null, GroupContent = null, Layer = ToggleLayer.Overlay }),
        "allowed-with-on-primary" => WithToggle(Fixtures.EmberForm, t => t with { AllowedWith = [new ToggleRef(Tide, "tide")] }),
        "allowed-in-base-on-primary" => WithToggle(Fixtures.EmberForm, t => t with { AllowedInBase = true }),
        "allowed-with-unknown" => WithToggle(Fixtures.SurgeOverlay, t => t with { AllowedWith = [new ToggleRef(Tide, "nonesuch")] }),
        "allowed-with-unknown-content" => WithToggle(Fixtures.SurgeOverlay, t => t with { AllowedWith = [new ToggleRef(Guid.Parse("6f0dc000-0000-4000-8000-0000000000ee"), "tide")] }),
        "group-bad-id" => WithGroup(g => g with { GroupId = "Bad Id" }),
        "group-blank-label" => WithGroup(g => g with { Label = "  " }),
        "cost-without-resource" => WithToggle(Fixtures.EmberForm, t => t with { ResourceId = null, ResourceContent = null }),
        "cost-formula-invalid" => WithToggle(Fixtures.EmberForm, t => t with { Cost = "3 +" }),
        "resource-content-lacks-resource" => WithToggle(Fixtures.EmberForm, t => t with { ResourceContent = Tide }),
        "resource-content-missing" => WithToggle(Fixtures.EmberForm, t => t with { ResourceContent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000ee") }),
        "level-out-of-range" => WithToggle(Fixtures.DeepForm, t => t with { Level = 0 }),
        "duplicate-group" => DuplicateGroup(),
        "auto-with-group" => WithToggle(Fixtures.EmberForm, t => t with { AutoWhen = ToggleAutoWhen.Bloodied, ResourceId = null, ResourceContent = null, Cost = null }),
        "below-v11" => Pack(Fixtures.EmberForm) with { SchemaVersion = 10 },
        "group-content-missing" => WithToggle(Fixtures.EmberForm, t => t with { GroupContent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000ee") }),
        "overlay-unreachable" => WithToggle(Fixtures.CalmOverlay, t => t with { AllowedInBase = false }),
        "overlay-unreachable-absent" => WithToggle(Fixtures.CalmOverlay, t => t with { AllowedInBase = null, AllowedWith = null }),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "unknown variant"),
    };

    private static ContentRevision DuplicateGroup()
    {
        var revision = Pack(Fixtures.FormDiscipline);
        return revision with { Effects = [.. revision.Effects, new ToggleGroupEffect { Id = "forms-group-again", GroupId = "forms", Label = "Forms again" }] };
    }
}
