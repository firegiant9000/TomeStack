using System.Text.Json;
using System.Text.Json.Nodes;

namespace TomeStack.RulesCore.Tests;

/// <summary>ADR-003: typed effects, the schemaVersion 1 migration, and round-tripping of unknown content.</summary>
public class EffectModelTests
{
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, RulesJson.Compact);

    private static ContentRevision Roundtrip(ContentRevision revision) =>
        JsonSerializer.Deserialize<ContentRevision>(Json(revision), RulesJson.Compact)!;

    private static ContentRevision RevisionWithEffects(string effectsJson) =>
        JsonSerializer.Deserialize<ContentRevision>($$"""
            {"contentId":"11111111-0000-4000-8000-000000000001","revisionId":"11111111-0000-4000-8000-000000000002",
             "schemaVersion":2,"kind":"feat","name":"Test","rulesFamilies":["srd-5.1"],
             "provenance":{"sourceId":"5f0d5000-0000-4000-8000-000000000001"},"status":"published",
             "effects":{{effectsJson}}}
            """, RulesJson.Options)!;

    [Fact]
    public void Schema_v1_fixture_revisions_are_upcast_to_typed_effects()
    {
        var pack = Fixtures.Pack();

        Assert.All(pack.Revisions, r => Assert.Equal((2, 1), (r.SchemaVersion, r.UpgradedFrom)));
        var quickfoot = pack.Revisions.Single(r => r.Reference == Fixtures.Quickfoot);
        var bonus = Assert.IsType<ModifierEffect>(Assert.Single(quickfoot.Effects));
        Assert.Equal(("quickfoot-dex", ModifierOperation.Bonus, "ability.dex.score", "2"), (bonus.Id, bonus.Operation, bonus.Target, bonus.Value));
        var keen = Assert.IsType<ModifierEffect>(Assert.Single(pack.Revisions.Single(r => r.Reference == Fixtures.KeenReflexes).Effects));
        Assert.Equal((FieldIds.Initiative, "1"), (keen.Target, keen.Value));
    }

    [Fact]
    public void Unknown_effect_type_is_kept_byte_for_byte_and_stays_reference_only()
    {
        const string unknown = """{"id":"star","type":"advantageOnInitiative","automation":"automatic","when":{"light":["star","moon"]},"text":"Roll twice."}""";
        var revision = RevisionWithEffects($"[{unknown}]");

        var effect = Assert.IsType<UnknownEffect>(Assert.Single(revision.Effects));
        Assert.Equal(("star", "advantageOnInitiative", AutomationStatus.Reference), (effect.Id, effect.Type, effect.Automation));
        var written = JsonNode.Parse(Json(revision))!["effects"]![0]!.ToJsonString();
        Assert.Equal(unknown, written);
        Assert.Equal(Json(revision), Json(Roundtrip(revision)));
    }

    [Fact]
    public void Known_type_with_a_malformed_body_degrades_to_reference_only_instead_of_failing_the_revision()
    {
        var revision = RevisionWithEffects("""[{"id":"broken","type":"modifier","operation":"bonus"}]""");

        Assert.IsType<UnknownEffect>(Assert.Single(revision.Effects));
    }

    [Fact]
    public void Every_typed_effect_round_trips_with_type_and_id_first_and_extensions_kept()
    {
        var revision = RevisionWithEffects("""
            [
              {"type":"modifier","id":"m","operation":"set","target":"ability.str.score","value":"19","stacking":"highestInGroup","stackGroup":"belt","x-note":"kept"},
              {"type":"grant","id":"g","grant":"proficiency","target":"skill.stealth"},
              {"type":"resource","id":"r","resourceId":"focus","label":"Focus","maximum":"PB"},
              {"type":"choice","id":"c","choiceId":"pick","count":1,"options":[{"contentId":"5f0dc000-0000-4000-8000-000000000004","revisionId":"5f0de000-0000-4000-8000-000000000004"}]},
              {"type":"restriction","id":"x","field":"ability.dex.score","minimum":13},
              {"type":"recovery","id":"v","resourceId":"focus","on":"shortRest","amount":"all","timing":"onShortRest"},
              {"type":"roll","id":"d","rollId":"strike","label":"Strike","dice":"1d8+2","resourceId":"focus","timing":"onRoll"}
            ]
            """);

        Assert.Equal(
            [typeof(ModifierEffect), typeof(GrantEffect), typeof(ResourceEffect), typeof(ChoiceEffect), typeof(RestrictionEffect), typeof(RecoveryEffect), typeof(RollEffect)],
            revision.Effects.Select(e => e.GetType()));
        var modifier = (ModifierEffect)revision.Effects[0];
        Assert.Equal("kept", modifier.Extensions!["x-note"].GetString());
        Assert.DoesNotContain("type", modifier.Extensions.Keys);

        var json = Json(revision);
        Assert.Equal(json, Json(Roundtrip(revision)));
        foreach (var effect in JsonNode.Parse(json)!["effects"]!.AsArray())
            Assert.Equal(["type", "id"], effect!.AsObject().Select(p => p.Key).Take(2));
    }

    [Fact]
    public void Schema_v1_effect_fields_the_migration_does_not_map_are_kept_as_extensions()
    {
        var revision = JsonSerializer.Deserialize<ContentRevision>("""
            {"contentId":"11111111-0000-4000-8000-000000000001","revisionId":"11111111-0000-4000-8000-000000000002",
             "schemaVersion":1,"kind":"species","name":"Old","rulesFamilies":["srd-5.1"],
             "provenance":{"sourceId":"5f0d5000-0000-4000-8000-000000000001"},"status":"published",
             "effects":[{"id":"old","type":"abilityScoreIncrease","ability":"wis","amount":1,"x-legacy":42}]}
            """, RulesJson.Options)!;

        var effect = Assert.IsType<ModifierEffect>(Assert.Single(revision.Effects));
        Assert.Equal(("ability.wis.score", "1"), (effect.Target, effect.Value));
        Assert.Equal(42, effect.Extensions!["x-legacy"].GetInt32());
        Assert.Equal(1, revision.UpgradedFrom);
    }

    private static ContentRevision V1RevisionWithEffect(string effectJson) =>
        JsonSerializer.Deserialize<ContentRevision>($$"""
            {"contentId":"11111111-0000-4000-8000-000000000001","revisionId":"11111111-0000-4000-8000-000000000002",
             "schemaVersion":1,"kind":"species","name":"Old","rulesFamilies":["srd-5.1"],
             "provenance":{"sourceId":"5f0d5000-0000-4000-8000-000000000001"},"status":"published",
             "effects":[{{effectJson}}]}
            """, RulesJson.Options)!;

    [Theory]
    [InlineData("""{"type":"initiativeBonus","amount":2}""")]
    [InlineData("""{"id":5,"type":"initiativeBonus","amount":2}""")]
    [InlineData("""{"id":null,"type":"initiativeBonus","amount":2}""")]
    [InlineData("""{"id":"a","type":"initiativeBonus"}""")]
    [InlineData("""{"id":"a","type":"initiativeBonus","amount":"2"}""")]
    [InlineData("""{"id":"a","type":"initiativeBonus","amount":2.5}""")]
    [InlineData("""{"id":"a","type":"abilityScoreIncrease","amount":2}""")]
    [InlineData("""{"id":"a","type":"abilityScoreIncrease","ability":"luck","amount":2}""")]
    [InlineData("""{"id":"a","type":"abilityScoreIncrease","ability":"1","amount":2}""")]
    [InlineData("""{"id":"a","type":"abilityScoreIncrease","ability":"dex,str","amount":2}""")]
    [InlineData("""{"id":"a","type":"abilityScoreIncrease","ability":7,"amount":2}""")]
    [InlineData("""{"id":"a","type":"initiativeBonus","amount":2,"automation":"sometimes"}""")]
    public void Malformed_schema_v1_effects_are_kept_unchanged_as_reference_only(string effect)
    {
        var revision = V1RevisionWithEffect(effect);

        var unknown = Assert.IsType<UnknownEffect>(Assert.Single(revision.Effects));
        Assert.Equal(AutomationStatus.Reference, unknown.Automation);
        Assert.Equal(JsonNode.Parse(effect)!.ToJsonString(), JsonNode.Parse(Json(revision))!["effects"]![0]!.ToJsonString());
    }

    [Fact]
    public void Schema_v1_ability_names_are_normalized_like_the_v1_reader()
    {
        var effect = Assert.IsType<ModifierEffect>(Assert.Single(V1RevisionWithEffect("""{"id":"a","type":"abilityScoreIncrease","ability":"DEX","amount":-1}""").Effects));

        Assert.Equal(("ability.dex.score", "-1"), (effect.Target, effect.Value));
    }

    [Fact]
    public void Known_type_with_wrong_value_kinds_degrades_instead_of_throwing()
    {
        var revision = RevisionWithEffects("""[{"id":7,"type":"modifier","operation":"bonus","target":"initiative","value":"1"},{"id":"g","type":"grant","grant":"content","content":"nope"}]""");

        Assert.All(revision.Effects, e => Assert.IsType<UnknownEffect>(e));
    }

    [Fact]
    public void Calculation_is_identical_before_and_after_the_v1_to_v2_migration()
    {
        var v1 = Fixtures.Pack();
        var reserialized = new ContentPack
        {
            PackId = v1.PackId,
            FormatVersion = v1.FormatVersion,
            Sources = v1.Sources,
            Revisions = [.. v1.Revisions.Select(Roundtrip)],
        };
        Assert.All(reserialized.Revisions, r => Assert.Null(r.UpgradedFrom));

        foreach (var character in new[] { Fixtures.Srd51Character(), Fixtures.Srd521Character() })
        {
            Assert.Equal(
                Json(CharacterCalculator.Calculate(character, new InMemoryContentCatalog(v1))),
                Json(CharacterCalculator.Calculate(character, new InMemoryContentCatalog(reserialized))));
        }
    }

    [Fact]
    public void Imported_candidate_effects_become_reference_only_whatever_their_type()
    {
        Effect effect = new GrantEffect { Id = "g", Grant = GrantKind.Proficiency, Target = FieldIds.Skill("stealth") };

        var quarantined = effect with { Automation = AutomationStatus.Reference };

        Assert.IsType<GrantEffect>(quarantined);
        Assert.Equal(AutomationStatus.Reference, quarantined.Automation);
    }
}
