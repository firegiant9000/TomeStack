using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 6, Task 6.1 (D43, content schema v11): the <c>rollMode</c> effect, the <c>speed.fly</c> field and a
/// damage-defence grant that follows a toggle (<c>whileToggle</c>): the version gate that keeps older revisions byte for
/// byte, validation, and the fixture pack. Nothing here calculates a mode or a defence; later tasks read them.
/// </summary>
public class RollModeContentTests
{
    private static ValidationReport Validate(ContentRevision revision, IEnumerable<ContentRevision>? batch = null) =>
        ContentValidator.Validate(revision, Fixtures.PlayMechanicsCatalog(), batch);

    private const string HeavyContent = "6f0dc000-0000-4000-8000-000000000061";

    // ---- the version gate ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(6)]
    public void A_v10_rollMode_entry_stays_unknown(int version)
    {
        var json = """{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage","against":["stunned"],"toggle":"t"}""";
        var revision = Fixtures.RevisionWithEffect(json, version);
        var effect = Assert.IsType<UnknownEffect>(revision.Effects.Single());

        Assert.Equal(json, Fixtures.EffectJson(effect));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(RevisionHashProperties.Hash(stored), RevisionHashProperties.Hash(RevisionHashProperties.Write(RevisionHashProperties.Read(stored))));
        Assert.Contains(json, stored);
    }

    [Fact]
    public void A_v11_rollMode_entry_is_typed_and_round_trips()
    {
        var json = """{"type":"rollMode","id":"m","target":"save.con","mode":"advantage","against":["paralyzed","stunned"],"toggle":"t","toggleContent":"6f0dc000-0000-4000-8000-000000000061","automation":"automatic","timing":"always"}""";
        var mode = Assert.IsType<RollModeEffect>(Fixtures.RevisionWithEffect(json, 11).Effects.Single());

        Assert.Equal("save.con", mode.Target);
        Assert.Equal(RollMode.Advantage, mode.Mode);
        Assert.Equal(["paralyzed", "stunned"], mode.Against);
        Assert.Equal("t", mode.Toggle);
        Assert.Equal(Guid.Parse(HeavyContent), mode.ToggleContent);
        Assert.Equal(json, Fixtures.EffectJson(mode));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(2)]
    public void A_v10_grant_carrying_whileToggle_stays_byte_for_byte_and_untyped(int version)
    {
        var json = """{"type":"grant","id":"g","grant":"damageResistance","damageType":"Fire","automation":"automatic","timing":"always","whileToggle":{"toggleId":"sky"}}""";
        if (version < 10)
        {
            // Below v10 the damage kind itself is unreadable, so the whole effect stays unknown.
            Assert.IsType<UnknownEffect>(Fixtures.RevisionWithEffect(json, version).Effects.Single());
            return;
        }
        var grant = Assert.IsType<GrantEffect>(Fixtures.RevisionWithEffect(json, version).Effects.Single());

        Assert.Null(grant.WhileToggle);
        Assert.True(grant.Extensions?.ContainsKey("whileToggle"));
        Assert.Equal(json, Fixtures.EffectJson(grant));
    }

    [Fact]
    public void A_v11_grant_with_whileToggle_is_typed()
    {
        var json = """{"type":"grant","id":"g","grant":"damageResistance","damageType":"Fire","whileToggle":{"toggleId":"sky"},"automation":"automatic","timing":"always"}""";
        var grant = Assert.IsType<GrantEffect>(Fixtures.RevisionWithEffect(json, 11).Effects.Single());

        Assert.Equal(new ToggleRef(null, "sky"), grant.WhileToggle);
        Assert.Null(grant.Extensions);
        Assert.Equal(json, Fixtures.EffectJson(grant));
    }

    // ---- validation -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("""{"type":"rollMode","id":"m","target":"initiative","mode":"advantage"}""", "validate.roll-mode-target")]
    [InlineData("""{"type":"rollMode","id":"m","target":"ability.dex.mod","mode":"advantage"}""", "validate.roll-mode-target")]
    [InlineData("""{"type":"rollMode","id":"m","target":"ability.dex.score","mode":"advantage"}""", "validate.roll-mode-target")]
    [InlineData("""{"type":"rollMode","id":"m","target":"skill.nonesuch","mode":"advantage"}""", "validate.roll-mode-target")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.luck","mode":"advantage"}""", "validate.roll-mode-target")]
    [InlineData("""{"type":"rollMode","id":"m","target":"ability.luck.check","mode":"advantage"}""", "validate.roll-mode-target")]
    [InlineData("""{"type":"rollMode","id":"m","target":"Save.dex","mode":"advantage"}""", "validate.roll-mode-target")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"normal"}""", "validate.roll-mode-mode")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage","against":[]}""", "validate.roll-mode-against")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage","against":["dazed"]}""", "validate.roll-mode-against")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage","against":["stunned","Stunned"]}""", "validate.roll-mode-against")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage","toggle":"ghost"}""", "validate.toggle-unknown")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage","toggleContent":"6f0dc000-0000-4000-8000-000000000061"}""", "validate.toggle-unknown")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage","toggle":"ghost","toggleContent":"6f0dc000-0000-4000-8000-000000000061"}""", "validate.toggle-unknown")]
    public void Roll_mode_mistakes_are_errors(string effectJson, string code)
    {
        var revision = Fixtures.RevisionWithEffect(effectJson, 11);
        Assert.IsType<RollModeEffect>(revision.Effects.Single());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == code && e.EffectId == "m");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData("save.str")]
    [InlineData("save.cha")]
    [InlineData("skill.stealth")]
    [InlineData("skill.sleightOfHand")]
    [InlineData("skill.animalHandling")]
    [InlineData("ability.int.check")]
    public void The_roll_mode_targets_are_saves_skills_and_ability_checks(string target)
    {
        var report = Validate(Fixtures.RevisionWithEffect($$"""{"type":"rollMode","id":"m","target":"{{target}}","mode":"disadvantage"}""", 11));

        Assert.Empty(report.Errors);
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void Every_skill_is_a_roll_mode_target()
    {
        foreach (var (key, _, _) in CharacterCalculator.Skills)
        {
            var report = Validate(Fixtures.RevisionWithEffect($$"""{"type":"rollMode","id":"m","target":"skill.{{key}}","mode":"advantage"}""", 11));
            Assert.Empty(report.Errors);
        }
    }

    [Fact]
    public void A_roll_mode_may_follow_a_toggle_of_another_content()
    {
        var report = Validate(Fixtures.RevisionWithEffect($$"""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage","toggle":"heavy","toggleContent":"{{HeavyContent}}"}""", 11));

        Assert.Empty(report.Errors);
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"sideways"}""")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":" advantage"}""")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage, disadvantage"}""")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex"}""")]
    [InlineData("""{"type":"rollMode","id":"m","mode":"advantage"}""")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage","against":"stunned"}""")]
    [InlineData("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage","toggleContent":"not-an-id","toggle":"t"}""")]
    public void A_bad_roll_mode_enum_reports_malformed_and_requires_v11(string json)
    {
        var revision = Fixtures.RevisionWithEffect(json, 11);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_roll_mode_in_a_revision_declaring_v10_requires_v11_when_built_in_code()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"rollMode","id":"m","target":"save.dex","mode":"advantage"}""", 11) with { SchemaVersion = 10 };

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.requires-v11");
        Assert.Contains("roll modes", report.Errors.First(e => e.Code == "validate.requires-v11").Message);
    }

    [Fact]
    public void Fly_speed_below_v11_is_an_error()
    {
        var json = """{"type":"modifier","id":"f","operation":"set","target":"speed.fly","value":"30"}""";

        var v10 = Validate(Fixtures.RevisionWithEffect(json, 10));
        Assert.Contains(v10.Errors, e => e.Code == "validate.requires-v11");
        Assert.Equal(11, v10.RequiredSchemaVersion);

        var v11 = Validate(Fixtures.RevisionWithEffect(json, 11));
        Assert.Empty(v11.Errors);
        Assert.Equal(11, v11.RequiredSchemaVersion);
    }

    [Fact]
    public void A_stored_v10_fly_speed_modifier_is_skipped_with_a_diagnostic()
    {
        var plain = CharacterCalculator.Calculate(Fixtures.Load("pm-instinct-l4.json") with { Pins = [] }, Fixtures.PlayMechanicsCatalog());
        Assert.Equal(0, plain.Field(FieldIds.FlySpeed).Value);
        Assert.Equal("feet", plain.Field(FieldIds.FlySpeed).Units);
        Assert.Equal("Fly speed", plain.Field(FieldIds.FlySpeed).Label);

        ContentRevision Feat(int n, int schemaVersion) => new()
        {
            ContentId = Guid.Parse($"6f0dc000-0000-4000-8000-0000000000f{n}"), RevisionId = Guid.Parse($"6f0de000-0000-4000-8000-0000000000f{n}"),
            Kind = ContentKind.Feat, Name = $"Fixture Flyer v{schemaVersion}", RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(Pack.Sources[0].Id, new PageRef(1)), Status = RevisionStatus.Published, SchemaVersion = schemaVersion,
            Effects = [new ModifierEffect { Id = "f", Operation = ModifierOperation.Set, Target = FieldIds.FlySpeed, Value = "30" }],
        };
        var old = Feat(2, 10);
        var current = Feat(3, 11);
        var withBoth = new InMemoryContentCatalog(Pack.Sources, [.. Pack.Revisions, old, current]);
        var character = Fixtures.Load("pm-instinct-l4.json");

        var sheetOld = CharacterCalculator.Calculate(character with { Pins = [old.Reference] }, withBoth);
        Assert.Equal(0, sheetOld.Field(FieldIds.FlySpeed).Value);
        Assert.Contains(sheetOld.Diagnostics, d => d.Code == "effect.character-only-field" && d.Content == old.Reference && d.EffectId == "f");

        var sheetNew = CharacterCalculator.Calculate(character with { Pins = [current.Reference] }, withBoth);
        Assert.Equal(30, sheetNew.Field(FieldIds.FlySpeed).Value);
        Assert.DoesNotContain(sheetNew.Diagnostics, d => d.Code == "effect.character-only-field");
    }

    [Theory]
    [InlineData("""{"type":"grant","id":"g","grant":"damageResistance","damageType":"Fire","whileToggle":{"toggleId":"ghost"}}""")]
    [InlineData("""{"type":"grant","id":"g","grant":"damageResistance","damageType":"Fire","whileToggle":{"contentId":"6f0dc000-0000-4000-8000-000000000061","toggleId":"ghost"}}""")]
    public void A_grant_whileToggle_naming_an_unknown_toggle_is_an_error(string json)
    {
        var report = Validate(Fixtures.RevisionWithEffect(json, 11));

        Assert.Contains(report.Errors, e => e.Code == "validate.while-toggle-unknown" && e.EffectId == "g");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_grant_whileToggle_naming_a_toggle_of_this_revision_or_another_content_is_clean()
    {
        var own = Validate(Fixtures.RevisionWithEffect(
            """{"type":"toggle","id":"sky","toggleId":"sky","label":"Sky"},{"type":"grant","id":"g","grant":"damageImmunity","damageType":"Cold","whileToggle":{"toggleId":"sky"}}""", 11));
        Assert.Empty(own.Errors);
        Assert.Equal(11, own.RequiredSchemaVersion);

        var other = Validate(Fixtures.RevisionWithEffect(
            """{"type":"grant","id":"g","grant":"damageVulnerability","damageType":"Fire","whileToggle":{"contentId":"6f0dc000-0000-4000-8000-000000000061","toggleId":"heavy"}}""", 11));
        Assert.Empty(other.Errors);
    }

    [Fact]
    public void A_grant_whileToggle_in_a_revision_declaring_v10_requires_v11_when_built_in_code()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"grant","id":"g","grant":"damageResistance","damageType":"Fire","whileToggle":{"toggleId":"sky"}}""", 11) with { SchemaVersion = 10 };

        Assert.Contains(Validate(revision).Errors, e => e.Code == "validate.requires-v11");
    }

    [Fact]
    public void Content_using_no_v11_field_still_publishes_at_its_old_version()
    {
        var plain = Validate(Fixtures.RevisionWithEffect("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1"}""", 10));
        Assert.Empty(plain.Errors);
        Assert.True(plain.RequiredSchemaVersion < 10, $"required v{plain.RequiredSchemaVersion}");

        var defence = Validate(Fixtures.RevisionWithEffect("""{"type":"grant","id":"g","grant":"damageResistance","damageType":"Fire"}""", 10));
        Assert.Empty(defence.Errors);
        Assert.Equal(10, defence.RequiredSchemaVersion);

        var speed = Validate(Fixtures.RevisionWithEffect("""{"type":"modifier","id":"m","operation":"bonus","target":"speed","value":"10"}""", 10));
        Assert.Empty(speed.Errors);
        Assert.Equal(10, speed.RequiredSchemaVersion);
    }

    // ---- the fixture pack ------------------------------------------------------------------------------------------------

    private static ContentPack Pack => Fixtures.RollModesPack();

    [Fact]
    public void The_pack_validates_clean_and_every_revision_needs_v11()
    {
        Assert.Equal(5, Pack.Revisions.Count);
        foreach (var revision in Pack.Revisions)
        {
            var report = Validate(revision, Pack.Revisions);
            Assert.Empty(report.Errors);
            Assert.Equal(11, report.RequiredSchemaVersion);
            Assert.DoesNotContain(revision.Effects, e => e is UnknownEffect);
        }
    }

    [Fact]
    public void The_pack_names_and_effects_are_the_fixtures_the_later_tasks_use()
    {
        var catalog = Fixtures.PlayMechanicsCatalog();
        string Name(ContentReference r) => catalog.FindRevision(r)!.Name;
        Assert.Equal("Fixture Swift Instinct", Name(Fixtures.SwiftInstinct));
        Assert.Equal("Fixture Heavy Stance", Name(Fixtures.HeavyStance));
        Assert.Equal("Fixture Sky Form", Name(Fixtures.SkyForm));
        Assert.Equal("Fixture Light Step", Name(Fixtures.LightStep));
        Assert.Equal("Fixture Weighted Gear", Name(Fixtures.WeightedGear));

        var swift = catalog.FindRevision(Fixtures.SwiftInstinct)!.Effects.OfType<RollModeEffect>().ToList();
        Assert.Equal(2, swift.Count);
        Assert.Equal(["paralyzed", "stunned"], swift.Single(m => m.Target == "save.con").Against);

        var heavy = catalog.FindRevision(Fixtures.HeavyStance)!.Effects.OfType<RollModeEffect>().ToList();
        Assert.All(heavy, m => Assert.Equal("heavy", m.Toggle));
        Assert.Contains(heavy, m => m is { Target: "skill.stealth", Mode: RollMode.Disadvantage });
        Assert.Contains(heavy, m => m is { Target: "ability.str.check", Mode: RollMode.Advantage });

        var sky = catalog.FindRevision(Fixtures.SkyForm)!;
        Assert.Contains(sky.Effects.OfType<ModifierEffect>(), m => m is { Target: FieldIds.FlySpeed, Operation: ModifierOperation.Set, Toggle: "sky" });
        Assert.Equal(new ToggleRef(null, "sky"), sky.Effects.OfType<GrantEffect>().Single().WhileToggle);
    }

    [Fact]
    public void The_character_holds_the_four_entries_and_dex_19_becomes_20()
    {
        var character = Fixtures.Load("pm-instinct-l4.json");
        Assert.Equal(4, character.Level);
        Assert.Equal(
            [Fixtures.SwiftInstinct, Fixtures.HeavyStance, Fixtures.SkyForm, Fixtures.LightStep],
            character.Pins);

        var sheet = CharacterCalculator.Calculate(character, Fixtures.PlayMechanicsCatalog());

        Assert.Equal(20, sheet.Field(FieldIds.Score(Ability.Dex)).Value);
        Assert.Equal(0, sheet.Field(FieldIds.FlySpeed).Value); // the form is off
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code.StartsWith("effect.", StringComparison.Ordinal) && d.Code != "effect.assisted");
    }
}
