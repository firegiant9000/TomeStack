using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v10, review fix round 1. M2: at v10 a toggle, resource or grant whose enum value does not read is an
/// error, not a silent downgrade. M5: grant spellings other than case (padding, lists, numbers) behave below v10 exactly as a
/// v9 build does, and are not accepted at v10 either.
/// </summary>
public class ContentSchemaV10ReviewTests
{
    /// <summary>The v9 grant kinds, read with the repo's options: what a v9 build does with a spelling.</summary>
    public enum V9GrantKind { Proficiency, Expertise, Content }

    public sealed record V9Grant(V9GrantKind Grant);

    private static string Write(ContentRevision revision) => JsonSerializer.Serialize(revision, RulesJson.Compact);

    private static ContentRevision Read(string json) => JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Compact)!;

    private static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static ContentRevision WithEffect(int version, string effectJson)
    {
        var plain = new ContentRevision
        {
            ContentId = Guid.Parse("c10a0000-0000-4000-8000-000000000002"), RevisionId = Guid.Parse("c10b0000-0000-4000-8000-000000000002"),
            Kind = ContentKind.Feat, Name = "Fixture Review", RulesFamilies = [RulesFamilies.Srd51], Provenance = new(Fixtures.SourceShared, new PageRef(40)),
            Status = RevisionStatus.Published, SchemaVersion = version,
            Effects = [new GrantEffect { Id = "g", Grant = GrantKind.Proficiency, Target = FieldIds.Skill("stealth") }],
        };
        var node = JsonNode.Parse(Write(plain))!;
        node["effects"]!.AsArray()[0] = JsonNode.Parse(effectJson);
        return Read(node.ToJsonString(RulesJson.Compact));
    }

    private static string Stored(int version, string effectJson)
    {
        var plain = WithEffect(version, effectJson);
        return Write(plain);
    }

    private const string BadToggleActivation = "{\"type\":\"toggle\",\"id\":\"t\",\"toggleId\":\"s\",\"label\":\"Fixture\",\"activation\":\"bogus\"}";
    private const string BadToggleAutoWhen = "{\"type\":\"toggle\",\"id\":\"t\",\"toggleId\":\"s\",\"label\":\"Fixture\",\"autoWhen\":\"never\"}";
    private const string BadResourceActivation = "{\"type\":\"resource\",\"id\":\"p\",\"resourceId\":\"pool\",\"label\":\"Fixture\",\"maximum\":\"2\",\"activation\":\"bogus\"}";
    private const string BadGrantKind = "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"notAKind\",\"target\":\"skill.stealth\"}";

    public static TheoryData<string> MalformedAtV10 =>
    [
        BadToggleActivation, BadToggleAutoWhen, BadResourceActivation, BadGrantKind,
        // numbers and numeric strings do not bind (the converter refuses integers)
        "{\"type\":\"toggle\",\"id\":\"t\",\"toggleId\":\"s\",\"label\":\"Fixture\",\"activation\":1}",
        "{\"type\":\"resource\",\"id\":\"p\",\"resourceId\":\"pool\",\"label\":\"Fixture\",\"maximum\":\"2\",\"activation\":\"1\"}",
        // padding and lists bind in the serializer but are not names: refused rather than guessed
        "{\"type\":\"toggle\",\"id\":\"t\",\"toggleId\":\"s\",\"label\":\"Fixture\",\"activation\":\" action\"}",
        "{\"type\":\"toggle\",\"id\":\"t\",\"toggleId\":\"s\",\"label\":\"Fixture\",\"activation\":\"action,bonusAction\"}",
        "{\"type\":\"toggle\",\"id\":\"t\",\"toggleId\":\"s\",\"label\":\"Fixture\",\"autoWhen\":\" bloodied\"}",
        "{\"type\":\"resource\",\"id\":\"p\",\"resourceId\":\"pool\",\"label\":\"Fixture\",\"maximum\":\"2\",\"activation\":\"reaction,other\"}",
        "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\" damageResistance\",\"damageType\":\"fire\"}",
        "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"damageResistance,damageImmunity\",\"damageType\":\"fire\"}",
        "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"proficiency,damageResistance\",\"damageType\":\"fire\"}",
        "{\"type\":\"grant\",\"id\":\"g\",\"grant\":3,\"damageType\":\"fire\"}",
        "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"3\",\"damageType\":\"fire\"}",
    ];

    [Theory]
    [MemberData(nameof(MalformedAtV10))]
    public void At_v10_an_effect_of_a_known_type_that_does_not_read_is_an_error(string effectJson)
    {
        var revision = WithEffect(10, effectJson);
        Assert.IsType<UnknownEffect>(revision.Effects[0]);
        var report = ContentValidator.Validate(revision, Fixtures.M1Catalog());
        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.effect-unsupported");
    }

    [Theory]
    [InlineData(BadToggleActivation)]
    [InlineData(BadToggleAutoWhen)]
    [InlineData(BadResourceActivation)]
    [InlineData(BadGrantKind)]
    public void Below_v10_the_same_effect_validates_as_before(string effectJson)
    {
        foreach (var version in new[] { 3, 6, 9 })
        {
            if (effectJson.Contains("toggle", StringComparison.Ordinal) && version < 6)
                continue;
            var report = ContentValidator.Validate(WithEffect(version, effectJson), Fixtures.M1Catalog());
            Assert.Empty(report.Errors);
            Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.effect-malformed");
        }
    }

    [Fact]
    public void An_unknown_effect_type_at_v10_is_still_only_a_warning()
    {
        var report = ContentValidator.Validate(WithEffect(10, "{\"type\":\"futureThing\",\"id\":\"x\"}"), Fixtures.M1Catalog());
        Assert.Empty(report.Errors);
        Assert.Contains(report.Warnings, w => w.Code == "validate.effect-unsupported");
    }

    // What a v9 build does with a grant spelling, established with the v9 enum and the repo's options.
    public static TheoryData<string> V9Rejects =>
    [
        "\" damageResistance\"", "\"damageResistance \"", "\"3\"", "3", "\"damageResistance,damageImmunity\"",
        "\"proficiency,damageResistance\"", "\"4\"", "5",
    ];

    [Theory]
    [MemberData(nameof(V9Rejects))]
    public void A_v9_build_cannot_read_these_grant_spellings(string value) =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<V9Grant>("{\"grant\":" + value + "}", RulesJson.Compact));

    [Theory]
    [MemberData(nameof(V9Rejects))]
    public void Below_v10_those_grant_spellings_stay_an_unknown_effect_with_the_same_bytes_and_no_effect(string value)
    {
        foreach (var version in new[] { 3, 9 })
        {
            var stored = Stored(version, "{\"type\":\"grant\",\"id\":\"g\",\"grant\":" + value + ",\"target\":\"skill.stealth\"}");
            var read = Read(stored);
            Assert.IsType<UnknownEffect>(read.Effects[0]);
            Assert.Equal(stored, Write(read));
            Assert.Equal(Hash(stored), Hash(Write(read)));
            Assert.Empty(ContentValidator.Validate(read, Fixtures.M1Catalog()).Errors);
        }
    }

    public static TheoryData<string, int> V9Binds => new()
    {
        { "\" proficiency\"", 0 }, { "\"expertise \"", 1 }, { "\"proficiency,expertise\"", 1 }, { "\"EXPERTISE\"", 1 },
    };

    [Theory]
    [MemberData(nameof(V9Binds))]
    public void Below_v10_a_grant_spelling_a_v9_build_binds_is_bound_the_same_way(string value, int kind)
    {
        Assert.Equal((V9GrantKind)kind, JsonSerializer.Deserialize<V9Grant>("{\"grant\":" + value + "}", RulesJson.Compact)!.Grant);
        var read = Read(Stored(9, "{\"type\":\"grant\",\"id\":\"g\",\"grant\":" + value + ",\"target\":\"skill.stealth\"}"));
        var grant = Assert.IsType<GrantEffect>(read.Effects[0]);
        Assert.Equal((GrantKind)kind, grant.Grant);
    }
}
