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
    public void The_play_mechanics_version_is_the_play_details_version_plus_one_and_the_current_one_follows_it()
    {
        Assert.Equal(ContentRevision.PlayDetailsSchemaVersion + 1, ContentRevision.PlayMechanicsSchemaVersion);
        Assert.Equal(11, ContentRevision.PlayMechanicsSchemaVersion);
        Assert.Equal(ContentRevision.PlayMechanicsSchemaVersion + 1, ContentRevision.CurrentSchemaVersion);
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

    // ---- Task 1.3: calculation applies one primary per group and the allowed overlays ----------------------------------

    private static readonly ContentReference[] FormPins =
        [Fixtures.FormDiscipline, Fixtures.EmberForm, Fixtures.TideForm, Fixtures.SurgeOverlay, Fixtures.BoundOverlay, Fixtures.DeepForm, Fixtures.CalmOverlay, Fixtures.CalmTideOverlay];

    internal static Character Base() => Fixtures.Srd521Character() with { Pins = [.. Fixtures.Srd521Character().Pins, .. FormPins] };

    internal static Character Forms(params ActiveToggle[] on) => Base() with { Play = new PlayState { Toggles = on } };

    internal static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.PlayMechanicsCatalog());

    private static ActiveToggle Ember => new(Fixtures.EmberForm.ContentId, "ember");
    private static ActiveToggle Tide => new(Fixtures.TideForm.ContentId, "tide");
    private static ActiveToggle Lineage => new(Fixtures.LineageForm.ContentId, "lineage");

    private static CharacterSheet Sheet(params ActiveToggle[] on) => Sheet(Forms(on));

    private static int Delta(CharacterSheet sheet, string field) => sheet.Field(field).Value - Sheet(Base()).Field(field).Value;

    [Fact]
    public void Two_stored_primaries_apply_only_the_last_with_a_diagnostic()
    {
        var sheet = Sheet(Forms(Tide, Ember));
        Assert.Equal(Sheet(Base()).Field(FieldIds.ArmorClass).Value + 2, sheet.Field(FieldIds.ArmorClass).Value); // ember (last) applies
        Assert.Equal(Sheet(Base()).Field(FieldIds.Initiative).Value, sheet.Field(FieldIds.Initiative).Value);     // tide does not
        Assert.Contains(sheet.Diagnostics, d => d.Code == "toggle.group-conflict");
    }

    [Fact]
    public void An_overlay_applies_in_base_or_with_an_allowed_primary_only()
    {
        var surge = new ActiveToggle(Fixtures.SurgeOverlay.ContentId, "surge");
        var ac = Sheet(Base()).Field(FieldIds.ArmorClass).Value;
        Assert.Equal(1, Sheet(Forms(surge)).Field(FieldIds.ArmorClass).Value - ac);                  // base
        Assert.Equal(1, Sheet(Forms(Tide, surge)).Field(FieldIds.ArmorClass).Value - ac);            // with tide
        var withEmber = Sheet(Forms(Ember, surge));
        Assert.Equal(2, withEmber.Field(FieldIds.ArmorClass).Value - ac);                            // ember only; surge ignored
        Assert.Contains(withEmber.Diagnostics, d => d.Code == "toggle.overlay-not-allowed");
    }

    [Fact]
    public void The_sheet_carries_the_label_of_the_declaring_group_even_for_a_toggle_of_another_source()
    {
        var toggles = Sheet(Base() with { Pins = [.. Base().Pins, Fixtures.LineageForm] }).Toggles!;
        Assert.All(toggles.Where(t => t.Group == "forms"), t => Assert.Equal("Forms", t.GroupLabel));
        Assert.Equal("Forms", toggles.Single(t => t.ToggleId == "lineage").GroupLabel); // declared by the Form Discipline, not by its own content
    }

    [Fact]
    public void The_sheet_lists_group_layer_cost_and_availability()
    {
        var surge = Sheet(Forms(Ember)).Toggles!.Single(t => t.ToggleId == "surge");
        Assert.Equal(("forms", ToggleLayer.Overlay, 1, (Guid?)Fixtures.FormDiscipline.ContentId), (surge.Group, surge.Layer, surge.Cost, surge.CostResourceContent));
        Assert.False(surge.Available);
        Assert.Contains("Fixture Tide Form", surge.UnavailableReason);
    }

    [Fact]
    public void A_toggle_below_its_level_is_not_listed() =>
        Assert.DoesNotContain(Sheet(Base()).Toggles!, t => t.ToggleId == "deep"); // level-3 character; deep is level 5

    [Fact]
    public void A_toggle_at_its_level_is_listed_and_can_be_on()
    {
        var deep = new ActiveToggle(Fixtures.DeepForm.ContentId, "deep");
        var sheet = Sheet(Forms(Ember, deep) with { Level = 5 });
        var listed = sheet.Toggles!.Single(t => t.ToggleId == "deep");
        Assert.True(listed.On);
        Assert.False(sheet.Toggles!.Single(t => t.ToggleId == "ember").On); // deep is the last stored primary
    }

    [Fact]
    public void A_primary_is_always_available_and_a_free_overlay_has_cost_null()
    {
        var toggles = Sheet(Base()).Toggles!;
        Assert.True(toggles.Single(t => t.ToggleId == "ember").Available);
        Assert.Equal((ToggleLayer.Primary, 3), (toggles.Single(t => t.ToggleId == "ember").Layer, toggles.Single(t => t.ToggleId == "ember").Cost));
        Assert.Null(toggles.Single(t => t.ToggleId == "bound").Cost);
        Assert.False(toggles.Single(t => t.ToggleId == "bound").Available);   // needs tide
        Assert.True(toggles.Single(t => t.ToggleId == "surge").Available);    // base
    }

    [Fact]
    public void A_cost_formula_that_fails_gives_no_cost_and_the_invalid_formula_warning()
    {
        var broken = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.EmberForm)!;
        broken = broken with { Effects = [.. broken.Effects.Select(e => e is ToggleEffect t ? t with { Cost = "3 +" } : e)] };
        var packs = new[] { Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack() };
        var catalog = new InMemoryContentCatalog(
            [.. packs.SelectMany(p => p.Sources)], [.. packs.SelectMany(p => p.Revisions).Where(r => r.ContentId != broken.ContentId), broken]);
        var sheet = CharacterCalculator.Calculate(Base(), catalog);
        Assert.Null(sheet.Toggles!.Single(t => t.ToggleId == "ember").Cost);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.EffectId == "ember");
    }

    // Delta 5.1 (G-a): overlays exclude each other only through disjoint allowedWith / allowedInBase.
    [Fact]
    public void Disjoint_overlays_never_apply_together()
    {
        var calm = new ActiveToggle(Fixtures.CalmOverlay.ContentId, "calm");
        var calmTide = new ActiveToggle(Fixtures.CalmTideOverlay.ContentId, "calm-tide");
        Assert.Equal(1, Delta(Sheet(calm), FieldIds.Initiative));                        // calm alone
        Assert.Equal(1, Delta(Sheet(calm, calmTide), FieldIds.Initiative));              // no primary: calm-tide is ignored
        Assert.Equal(3 + 2, Delta(Sheet(Tide, calm, calmTide), FieldIds.Initiative));   // tide on: calm ignored, calm-tide applies
    }

    // Delta 5.2 (C6): groupContent may cross sources.
    [Fact]
    public void A_species_toggle_joins_the_class_group()
    {
        var withLineage = Base() with { Pins = [.. Base().Pins, Fixtures.LineageForm] };
        var lineageLast = Sheet(withLineage with { Play = new PlayState { Toggles = [Ember, Lineage] } });
        Assert.Equal(5, Delta(lineageLast, FieldIds.Speed));
        Assert.Equal(0, Delta(lineageLast, FieldIds.ArmorClass));
        Assert.Contains(lineageLast.Diagnostics, d => d.Code == "toggle.group-conflict");

        var emberLast = Sheet(withLineage with { Play = new PlayState { Toggles = [Lineage, Ember] } });
        Assert.Equal(0, Delta(emberLast, FieldIds.Speed));
        Assert.Equal(2, Delta(emberLast, FieldIds.ArmorClass));
    }

    [Fact]
    public void Without_the_group_content_a_toggle_is_a_group_of_one()
    {
        var lineageOnly = Fixtures.Srd521Character() with { Pins = [.. Fixtures.Srd521Character().Pins, Fixtures.LineageForm, Fixtures.EmberForm], Play = new PlayState { Toggles = [Lineage, Ember] } };
        var plain = Sheet(lineageOnly with { Play = new PlayState() });
        var sheet = Sheet(lineageOnly);
        Assert.Equal(5, sheet.Field(FieldIds.Speed).Value - plain.Field(FieldIds.Speed).Value); // lineage applies
        Assert.Equal(2, sheet.Field(FieldIds.ArmorClass).Value - plain.Field(FieldIds.ArmorClass).Value); // alongside ember (its own group is absent too)
        var absent = sheet.Diagnostics.Where(d => d.Code == "toggle.group-absent").ToList();
        Assert.Contains(absent, d => d.Message.Contains("'lineage'", StringComparison.Ordinal));
        Assert.DoesNotContain(absent, d => d.Message.Contains(Fixtures.FormDiscipline.ContentId.ToString(), StringComparison.OrdinalIgnoreCase)); // no raw GUID
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "toggle.group-conflict");
        Assert.DoesNotContain(Sheet(Forms(Ember)).Diagnostics, d => d.Code == "toggle.group-absent");
    }

    [Fact]
    public void A_group_absent_toggle_leaves_its_feature_automatic_with_no_problems_of_its_own()
    {
        var lineageOnly = Fixtures.Srd521Character() with { Pins = [.. Fixtures.Srd521Character().Pins, Fixtures.LineageForm], Play = new PlayState { Toggles = [Lineage] } };
        var sheet = Sheet(lineageOnly);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "toggle.group-absent"); // still informative
        var feature = sheet.Features!.Single(f => f.Content == Fixtures.LineageForm);
        Assert.Equal(AutomationStatus.Automatic, feature.Automation);
        Assert.Empty(feature.Diagnostics);
        Assert.All(feature.Effects, e => Assert.NotEqual(AutomationStatus.Assisted, e.Automation));
    }

    [Fact]
    public void A_group_of_one_toggle_reports_no_group_or_layer_on_the_sheet()
    {
        var lineageOnly = Fixtures.Srd521Character() with { Pins = [.. Fixtures.Srd521Character().Pins, Fixtures.LineageForm] };
        var lineage = Sheet(lineageOnly).Toggles!.Single(t => t.ToggleId == "lineage");
        Assert.Equal((null, null, null, null), (lineage.Group, lineage.GroupContent, lineage.Layer, lineage.GroupLabel));
        // With the group content present it is a member as before.
        var member = Sheet(Base() with { Pins = [.. Base().Pins, Fixtures.LineageForm] }).Toggles!.Single(t => t.ToggleId == "lineage");
        Assert.Equal(("forms", ToggleLayer.Primary), (member.Group, member.Layer));
    }

    private static readonly Guid SameRevisionContent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000f3");

    [Fact]
    public void An_overlay_reason_names_the_form_label_not_its_own_content_and_other_content_is_added_in_brackets()
    {
        var one = new ContentRevision
        {
            ContentId = SameRevisionContent, RevisionId = Guid.Parse("6f0de000-0000-4000-8000-0000000000f3"), SchemaVersion = 11, Kind = ContentKind.Feature,
            Name = "Fixture Studio Feature", RulesFamilies = [RulesFamilies.Srd521], Status = RevisionStatus.Published,
            Provenance = new(Fixtures.FormsPack().Sources[0].Id, new(9)),
            Effects =
            [
                new ToggleGroupEffect { Id = "g", GroupId = "shapes", Label = "Shapes" },
                new ToggleEffect { Id = "alpha", ToggleId = "alpha", Label = "Alpha shape", Group = "shapes" },
                new ToggleEffect { Id = "halo", ToggleId = "halo", Label = "Halo", Group = "shapes", Layer = ToggleLayer.Overlay, AllowedWith = [new ToggleRef(null, "alpha")] },
            ],
        };
        var packs = new[] { Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack() };
        var catalog = new InMemoryContentCatalog([.. packs.SelectMany(p => p.Sources)], [.. packs.SelectMany(p => p.Revisions), one]);
        var sheet = CharacterCalculator.Calculate(Base() with { Pins = [.. Base().Pins, one.Reference] }, catalog);
        Assert.Equal("Needs Alpha shape.", sheet.Toggles!.Single(t => t.ToggleId == "halo").UnavailableReason);
        // The cross-content fixture overlay names the form and its content.
        Assert.Equal("Needs Tide form (Fixture Tide Form).", Sheet(Base()).Toggles!.Single(t => t.ToggleId == "bound").UnavailableReason);
    }

    // ---- Wave 3 auto toggles inside a group (V17) -----------------------------------------------------------------------

    private static readonly Guid GritContent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000f2");

    /// <summary>
    /// A hand-built v11 feature whose toggle is both automatic (bloodied) and in the forms group. Validation refuses that
    /// (validate.toggle-auto-group), but the calculator never throws on invalid stored content, so the behaviour is pinned here.
    /// </summary>
    private static ContentRevision Grit() => new()
    {
        ContentId = GritContent, RevisionId = Guid.Parse("6f0de000-0000-4000-8000-0000000000f2"), SchemaVersion = 11, Kind = ContentKind.Feature,
        Name = "Fixture Grit", RulesFamilies = [RulesFamilies.Srd521], Status = RevisionStatus.Published,
        Provenance = new(Fixtures.FormsPack().Sources[0].Id, new(9)),
        Effects =
        [
            new ToggleEffect { Id = "grit", ToggleId = "grit", Label = "Grit", AutoWhen = ToggleAutoWhen.Bloodied, Group = "forms", GroupContent = Fixtures.FormDiscipline.ContentId },
            new ModifierEffect { Id = "grit-ac", Operation = ModifierOperation.Bonus, Target = FieldIds.ArmorClass, Value = "4", Timing = EffectTiming.WhileActive, Toggle = "grit" },
        ],
    };

    private static CharacterSheet GritSheet(int current, params ActiveToggle[] on)
    {
        var grit = Grit();
        var packs = new[] { Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack() };
        var catalog = new InMemoryContentCatalog([.. packs.SelectMany(p => p.Sources)], [.. packs.SelectMany(p => p.Revisions), grit]);
        var character = Base() with
        {
            Pins = [.. Base().Pins, grit.Reference],
            Overrides = [new FieldOverride(FieldIds.HitPoints, 21, "Fixed maximum")],
            Play = new PlayState { Toggles = on, CurrentHitPoints = current },
        };
        return CharacterCalculator.Calculate(character, catalog);
    }

    [Fact]
    public void An_auto_toggle_in_a_group_follows_its_condition_not_the_group()
    {
        var gritStored = new ActiveToggle(GritContent, "grit");
        var ac = GritSheet(21).Field(FieldIds.ArmorClass).Value; // healthy, nothing stored

        // Healthy: grit is off even when stored; the stored primary applies as usual.
        var healthy = GritSheet(21, gritStored, Ember);
        Assert.Equal(ac + 2, healthy.Field(FieldIds.ArmorClass).Value);
        Assert.False(healthy.Toggles!.Single(t => t.ToggleId == "grit").On);

        // Bloodied: grit is on whichever primary is stored, and neither knocks out the other.
        var bloodiedEmber = GritSheet(10, Ember);
        Assert.Equal(ac + 4 + 2, bloodiedEmber.Field(FieldIds.ArmorClass).Value);
        Assert.True(bloodiedEmber.Toggles!.Single(t => t.ToggleId == "grit").On);
        var bloodiedTide = GritSheet(10, Tide);
        Assert.Equal(ac + 4, bloodiedTide.Field(FieldIds.ArmorClass).Value);
        Assert.Equal(3, bloodiedTide.Field(FieldIds.Initiative).Value - GritSheet(21).Field(FieldIds.Initiative).Value);
        Assert.DoesNotContain(bloodiedEmber.Diagnostics, d => d.Code == "toggle.group-conflict");
        Assert.DoesNotContain(bloodiedEmber.Diagnostics, d => d.Code == "toggle.group-absent");
    }

    [Fact]
    public void Resolve_runs_in_both_passes()
    {
        // Bloodied only after the first pass (auto toggles off): the final sheet is the second pass, and group exclusivity must hold there.
        var gritStored = new ActiveToggle(GritContent, "grit");
        var ac = GritSheet(21).Field(FieldIds.ArmorClass).Value;
        var initiative = GritSheet(21).Field(FieldIds.Initiative).Value;
        var sheet = GritSheet(10, Tide, Ember, gritStored);

        Assert.True(sheet.Bloodied);
        Assert.True(sheet.Toggles!.Single(t => t.ToggleId == "grit").On);       // proves the second pass produced this sheet
        Assert.Equal(ac + 4 + 2, sheet.Field(FieldIds.ArmorClass).Value);       // ember (last stored) and grit
        Assert.Equal(initiative, sheet.Field(FieldIds.Initiative).Value);       // tide knocked out in the second pass
        Assert.Equal((true, false), (sheet.Toggles!.Single(t => t.ToggleId == "ember").On, sheet.Toggles!.Single(t => t.ToggleId == "tide").On));
        Assert.Contains(sheet.Diagnostics, d => d.Code == "toggle.group-conflict");
    }

    // ---- the helpers a damage response's toggleOn uses (D43 S7 task 7.4) --------------------------------------------------------

    private static readonly Guid FormsContent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000a1");

    private static ToggleValue Form(string id, bool on, ToggleLayer layer = ToggleLayer.Primary, bool? inBase = null, params string[] allowedWith) =>
        new(new(FormsContent, Guid.Empty), "Forms", id, id, id, on, null, null, Group: "forms", GroupContent: FormsContent, Layer: layer,
            AllowedWith: layer == ToggleLayer.Overlay ? [.. allowedWith.Select(w => new ToggleRef(FormsContent, w))] : null, AllowedInBase: inBase);

    [Fact]
    public void OverlayAllowedNow_follows_the_form_that_is_on()
    {
        var overlay = Form("aura", false, ToggleLayer.Overlay, inBase: false, "tide");
        var baseOnly = Form("calm", false, ToggleLayer.Overlay, inBase: true);

        Assert.False(ToggleGroups.OverlayAllowedNow(overlay, [Form("tide", false), overlay]));      // no form on: the base character
        Assert.True(ToggleGroups.OverlayAllowedNow(overlay, [Form("tide", true), overlay]));
        Assert.False(ToggleGroups.OverlayAllowedNow(overlay, [Form("ember", true), overlay]));
        Assert.True(ToggleGroups.OverlayAllowedNow(baseOnly, [Form("tide", false)]));
        Assert.False(ToggleGroups.OverlayAllowedNow(baseOnly, [Form("tide", true)]));
        Assert.True(ToggleGroups.OverlayAllowedNow(Form("tide", false), [])); // a primary is always allowed
    }

    [Fact]
    public void OverlaysEndedBySwitchingOn_ends_only_overlays_the_new_form_does_not_allow()
    {
        var guard = Form("guard", true);
        var surge = Form("surge", false);
        var withGuard = Form("with-guard", true, ToggleLayer.Overlay, inBase: false, "guard");
        var withBoth = Form("with-both", true, ToggleLayer.Overlay, inBase: false, "guard", "surge");
        var storedOnly = Form("stored-only", false, ToggleLayer.Overlay, inBase: true);
        var allowedStored = Form("stored-allowed", false, ToggleLayer.Overlay, inBase: false, "surge");
        IReadOnlyList<ToggleValue> toggles = [guard, surge, withGuard, withBoth, storedOnly, allowedStored];
        IReadOnlySet<(Guid, string)> stored = new HashSet<(Guid, string)> { (FormsContent, "stored-only"), (FormsContent, "stored-allowed") };

        Assert.Equal(["with-guard"], ToggleGroups.OverlaysEndedBySwitchingOn(surge, toggles, null).Select(t => t.ToggleId));
        // A stored overlay that is not on counts too, when the stored set is given.
        Assert.Equal(["with-guard", "stored-only"], ToggleGroups.OverlaysEndedBySwitchingOn(surge, toggles, stored).Select(t => t.ToggleId));
        // An overlay is never a form to switch to; a toggle outside a group ends nothing.
        Assert.Empty(ToggleGroups.OverlaysEndedBySwitchingOn(withGuard, toggles, stored));
        Assert.Empty(ToggleGroups.OverlaysEndedBySwitchingOn(surge with { Group = null, GroupContent = null }, toggles, stored));
    }
}
