using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 1 (D43, content schema v11): the typed toggle-group fields and the version gate that keeps every
/// older revision byte for byte. Calculation and validation of groups are in later tasks.
/// </summary>
public class ToggleGroupTests
{
    private const string V11Toggle =
        """{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms","groupContent":"6f0dc000-0000-4000-8000-000000000001","layer":"overlay","allowedWith":[{"contentId":"6f0dc000-0000-4000-8000-000000000003","toggleId":"tide"}],"allowedInBase":true,"cost":"3","resourceContent":"6f0dc000-0000-4000-8000-000000000001","level":5}""";

    [Fact]
    public void A_v10_toggle_carrying_v11_keys_stays_byte_for_byte_and_untyped()
    {
        // As a v10 build stores a toggle: typed members, the defaults it always writes, then the unread keys in document order.
        var json = """{"type":"toggle","id":"t","toggleId":"t","label":"T","automation":"automatic","timing":"always","group":"forms","cost":"3","layer":"overlay"}""";
        var revision = Fixtures.RevisionWithEffect(json, schemaVersion: 10);
        var toggle = Assert.IsType<ToggleEffect>(revision.Effects.Single());
        Assert.Null(toggle.Group);
        Assert.Null(toggle.Cost);
        Assert.Equal(json, Fixtures.EffectJson(revision.Effects.Single())); // same keys, same order
    }

    [Fact]
    public void A_v11_toggle_types_its_group_fields()
    {
        var revision = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.SurgeOverlay)!;
        var toggle = revision.Effects.OfType<ToggleEffect>().Single();
        Assert.Equal((ToggleLayer.Overlay, true), (toggle.Layer, toggle.AllowedInBase));
        Assert.Equal("1", toggle.Cost);
    }

    [Fact]
    public void A_v11_toggle_types_every_v11_key_and_writes_them_back_unchanged()
    {
        var revision = Fixtures.RevisionWithEffect(V11Toggle, schemaVersion: 11);
        var toggle = Assert.IsType<ToggleEffect>(revision.Effects.Single());

        Assert.Equal("forms", toggle.Group);
        Assert.Equal(Guid.Parse("6f0dc000-0000-4000-8000-000000000001"), toggle.GroupContent);
        Assert.Equal(ToggleLayer.Overlay, toggle.Layer);
        Assert.Equal(new ToggleRef(Guid.Parse("6f0dc000-0000-4000-8000-000000000003"), "tide"), Assert.Single(toggle.AllowedWith!));
        Assert.True(toggle.AllowedInBase);
        Assert.Equal("3", toggle.Cost);
        Assert.Equal(Guid.Parse("6f0dc000-0000-4000-8000-000000000001"), toggle.ResourceContent);
        Assert.Equal(5, toggle.Level);
        Assert.Null(toggle.Extensions);
    }

    [Fact]
    public void A_toggle_without_v11_fields_serializes_without_them()
    {
        var toggle = new ToggleEffect { Id = "t", ToggleId = "t", Label = "T" };
        Assert.Equal("""{"type":"toggle","id":"t","toggleId":"t","label":"T","automation":"automatic","timing":"always"}""", Fixtures.EffectJson(toggle));
    }

    [Fact]
    public void A_v10_toggle_keeps_its_v10_fields_typed_while_the_v11_keys_stay_extension_data()
    {
        var json = """{"type":"toggle","id":"t","toggleId":"t","label":"T","autoWhen":"bloodied","activation":"reaction","automation":"automatic","timing":"always","layer":"overlay","level":3}""";
        var toggle = Assert.IsType<ToggleEffect>(Fixtures.RevisionWithEffect(json, schemaVersion: 10).Effects.Single());

        Assert.Equal((ToggleAutoWhen.Bloodied, Activation.Reaction), (toggle.AutoWhen, toggle.Activation));
        Assert.Null(toggle.Layer);
        Assert.Null(toggle.Level);
        Assert.Equal(json, Fixtures.EffectJson(toggle));
    }

    [Fact]
    public void A_v9_toggle_strips_both_the_v10_and_the_v11_keys()
    {
        var json = """{"type":"toggle","id":"t","toggleId":"t","label":"T","automation":"automatic","timing":"always","autoWhen":"bloodied","group":"forms","activation":"reaction"}""";
        var toggle = Assert.IsType<ToggleEffect>(Fixtures.RevisionWithEffect(json, schemaVersion: 9).Effects.Single());

        Assert.Null(toggle.AutoWhen);
        Assert.Null(toggle.Activation);
        Assert.Null(toggle.Group);
        Assert.Equal(json, Fixtures.EffectJson(toggle));
    }

    [Theory]
    [InlineData("layer", "\"sideways\"")]
    [InlineData("layer", "5")]
    [InlineData("layer", "[\"primary\"]")]
    [InlineData("layer", "\" overlay\"")]
    public void A_bad_v11_enum_value_leaves_the_toggle_untyped_in_a_v11_revision(string key, string value)
    {
        var json = $$"""{"type":"toggle","id":"t","toggleId":"t","label":"T","{{key}}":{{value}}}""";
        var effect = Fixtures.RevisionWithEffect(json, schemaVersion: 11).Effects.Single();

        Assert.IsType<UnknownEffect>(effect);
        Assert.Equal(json, Fixtures.EffectJson(effect));
    }

    [Fact]
    public void A_v11_toggle_with_a_malformed_group_content_stays_untyped()
    {
        var json = """{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms","groupContent":"not-a-guid"}""";
        Assert.IsType<UnknownEffect>(Fixtures.RevisionWithEffect(json, schemaVersion: 11).Effects.Single());
    }

    [Fact]
    public void A_v11_toggleGroup_is_typed()
    {
        var revision = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.FormDiscipline)!;
        var group = revision.Effects.OfType<ToggleGroupEffect>().Single();
        Assert.Equal(("forms", "Forms"), (group.GroupId, group.Label));
        Assert.Equal(ContentRevision.PlayMechanicsSchemaVersion, ToggleGroupEffect.SchemaVersion);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(5)]
    public void A_toggleGroup_in_an_older_revision_stays_an_unknown_effect_byte_for_byte(int version)
    {
        var json = """{"type":"toggleGroup","id":"g","groupId":"forms","label":"Forms"}""";
        var effect = Fixtures.RevisionWithEffect(json, schemaVersion: version).Effects.Single();

        Assert.IsType<UnknownEffect>(effect);
        Assert.Equal(json, Fixtures.EffectJson(effect));
    }

    [Fact]
    public void A_malformed_v11_toggleGroup_stays_an_unknown_effect()
    {
        var json = """{"type":"toggleGroup","id":"g","label":"Forms"}""";
        Assert.IsType<UnknownEffect>(Fixtures.RevisionWithEffect(json, schemaVersion: 11).Effects.Single());
    }

    [Fact]
    public void The_play_mechanics_version_is_the_play_details_version_plus_one_and_is_current()
    {
        Assert.Equal(ContentRevision.PlayDetailsSchemaVersion + 1, ContentRevision.PlayMechanicsSchemaVersion);
        Assert.Equal(11, ContentRevision.PlayMechanicsSchemaVersion);
        Assert.Equal(ContentRevision.PlayMechanicsSchemaVersion, ContentRevision.CurrentSchemaVersion);
    }

    [Fact]
    public void The_forms_pack_loads_with_its_nine_revisions_in_two_sources()
    {
        var pack = Fixtures.FormsPack();
        Assert.Equal(2, pack.Sources.Count);
        Assert.Equal(9, pack.Revisions.Count);
        Assert.All(pack.Revisions, r => Assert.Equal(11, r.SchemaVersion));
        Assert.All(pack.Revisions, r => Assert.DoesNotContain(r.Effects, e => e is UnknownEffect));
        var lineage = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.LineageForm)!;
        Assert.Equal(ContentKind.Species, lineage.Kind);
        Assert.NotEqual(Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.FormDiscipline)!.Provenance.SourceId, lineage.Provenance.SourceId);
    }

    [Fact]
    public void Play_mechanics_catalog_adds_the_forms_pack_and_all_catalog_does_not()
    {
        Assert.NotNull(Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.EmberForm));
        Assert.Null(Fixtures.AllCatalog().FindRevision(Fixtures.EmberForm));
        Assert.NotNull(Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.Quickfoot));
    }

    [Fact]
    public void A_v11_toggle_revision_round_trips_through_the_serializer_unchanged()
    {
        var revision = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.SurgeOverlay)!;
        var once = JsonSerializer.Serialize(revision, RulesJson.Compact);
        var twice = JsonSerializer.Serialize(JsonSerializer.Deserialize<ContentRevision>(once, RulesJson.Compact)!, RulesJson.Compact);
        Assert.Equal(once, twice);
    }
}
