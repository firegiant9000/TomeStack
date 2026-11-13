using System.Text.Json;

namespace TomeStack.AppService.Tests;

/// <summary>Content schema v13 (ADR-024): the published v13 JSON schema accepts a creation guide and a pack with <c>contains</c>, and refuses malformed ones; v12 refuses a guide.</summary>
public class CreationGuideV13SchemaFileTests
{
    private const string ClassId = "6a7e5500-0000-4000-8000-000000000013";
    private const string MaceId = "6a7e5500-0000-4000-8000-000000000014";
    private const string HammerId = "6a7e5500-0000-4000-8000-000000000015";

    private static string Revision(string kind, int version, string effects) =>
        "{\"contentId\":\"6a7e5500-0000-4000-8000-000000000010\",\"revisionId\":\"6a7e5500-0000-4000-8000-000000000110\",\"schemaVersion\":" + version
        + ",\"kind\":\"" + kind + "\",\"name\":\"Fixture guide\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[" + effects + "]}";

    private static string Validate(string file, string json)
    {
        using var document = JsonDocument.Parse(json);
        return SchemaTests.ValidateFile(file, document.RootElement);
    }

    private static string V13(string kind, string effects) => Validate("content-revision.v13.schema.json", Revision(kind, 13, effects));

    private static string GuideFor(string content = "{\"contentId\":\"" + ClassId + "\"}") => "{\"type\":\"guideFor\",\"id\":\"for\",\"content\":" + content + "}";

    private static string Primary(string abilities = "[\"wis\"]") => "{\"type\":\"primaryAbilities\",\"id\":\"primary\",\"abilities\":" + abilities + "}";

    private static string Kit(string picks) => "{\"type\":\"startingEquipment\",\"id\":\"kit\",\"picks\":" + picks + "}";

    private static string Pick(string lines, string optionExtra = "") =>
        "[{\"id\":\"weapon\",\"label\":\"Weapon\",\"options\":[{\"id\":\"a\",\"lines\":" + lines + optionExtra + "}]}]";

    private const string Guide =
        "{\"type\":\"guideFor\",\"id\":\"for\",\"content\":{\"contentId\":\"" + ClassId + "\"}},"
        + "{\"type\":\"primaryAbilities\",\"id\":\"primary\",\"abilities\":[\"wis\"]},"
        + "{\"type\":\"startingEquipment\",\"id\":\"kit\",\"picks\":[{\"id\":\"weapon\",\"label\":\"Weapon\",\"options\":["
        + "{\"id\":\"a\",\"lines\":[{\"item\":{\"contentId\":\"" + MaceId + "\"},\"quantity\":1},{\"anyOf\":\"weapon.simple\"},{\"coins\":{\"gp\":50}},{\"pack\":{\"contentId\":\"" + MaceId + "\"}}]},"
        + "{\"id\":\"b\",\"lines\":[{\"item\":{\"contentId\":\"" + HammerId + "\"}}],\"requiresProficiency\":\"weapon.warhammer\"}]}]}";

    [Fact]
    public void The_v13_schema_accepts_a_guide() => Assert.Equal("", V13("creationGuide", Guide));

    [Fact]
    public void The_v12_schema_refuses_the_same_guide() =>
        Assert.NotEqual("", Validate("content-revision.v12.schema.json", Revision("creationGuide", 12, Guide)));

    [Fact]
    public void The_v13_schema_refuses_a_guide_marked_v12() =>
        Assert.NotEqual("", Validate("content-revision.v13.schema.json", Revision("creationGuide", 12, Guide)));

    [Fact]
    public void The_v13_schema_still_accepts_older_kinds() =>
        Assert.Equal("", V13("feature", "{\"type\":\"modifier\",\"id\":\"m\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"2\",\"stacking\":\"stack\",\"automation\":\"automatic\",\"timing\":\"always\"}"));

    [Fact]
    public void The_v13_schema_accepts_a_pack_with_contains() =>
        Assert.Equal("", V13("item", "{\"type\":\"item\",\"id\":\"item\",\"contains\":[{\"item\":{\"contentId\":\"" + MaceId + "\"},\"quantity\":10},{\"item\":{\"contentId\":\"" + HammerId + "\"},\"quantity\":1}]}"));

    [Theory]
    [InlineData("{\"item\":{\"contentId\":\"" + MaceId + "\"}}")]
    [InlineData("\"mace\"")]
    [InlineData("[{\"item\":{\"contentId\":\"not-a-guid\"},\"quantity\":1}]")]
    [InlineData("[{\"item\":{\"contentId\":\"" + MaceId + "\"},\"quantity\":\"ten\"}]")]
    [InlineData("[{\"quantity\":1}]")]
    public void The_v13_schema_refuses_a_malformed_contains(string contains) =>
        Assert.NotEqual("", V13("item", "{\"type\":\"item\",\"id\":\"item\",\"contains\":" + contains + "}"));

    [Fact]
    public void The_v13_schema_refuses_a_guide_effect_missing_its_payload()
    {
        Assert.NotEqual("", V13("creationGuide", "{\"type\":\"guideFor\",\"id\":\"for\"}"));
        Assert.NotEqual("", V13("creationGuide", "{\"type\":\"primaryAbilities\",\"id\":\"primary\"}"));
        Assert.NotEqual("", V13("creationGuide", "{\"type\":\"startingEquipment\",\"id\":\"kit\"}"));
    }

    [Fact]
    public void The_v13_schema_refuses_a_malformed_guide_for() => Assert.NotEqual("", V13("creationGuide", GuideFor("{\"contentId\":\"nope\"}")));

    [Theory]
    [InlineData("[\"luck\"]")]
    [InlineData("\"wis\"")]
    [InlineData("[1]")]
    public void The_v13_schema_refuses_malformed_primary_abilities(string abilities) => Assert.NotEqual("", V13("creationGuide", Primary(abilities)));

    [Theory]
    [InlineData("[{\"item\":{\"contentId\":\"" + MaceId + "\"}}]", "")]
    [InlineData("[{\"item\":{\"contentId\":\"" + MaceId + "\"},\"quantity\":2,\"futureMember\":true}]", "")]
    [InlineData("[{\"coins\":{\"cp\":1,\"sp\":2,\"ep\":3,\"gp\":4,\"pp\":5}}]", ",\"requiresProficiency\":\"armor.heavy\",\"futureMember\":1")]
    [InlineData("[{\"coins\":{\"gp\":0,\"dc\":1}}]", "")]
    public void The_v13_schema_accepts_line_shapes_and_unknown_members(string lines, string optionExtra) =>
        Assert.Equal("", V13("creationGuide", Kit(Pick(lines, optionExtra))));

    [Theory]
    [InlineData("[{\"quantity\":1}]")]
    [InlineData("[{\"item\":{\"contentId\":\"" + MaceId + "\"},\"quantity\":\"two\"}]")]
    [InlineData("[{\"anyOf\":3}]")]
    [InlineData("[{\"coins\":{\"gp\":-1}}]")]
    [InlineData("[{\"coins\":{\"gp\":1.5}}]")]
    [InlineData("[{\"pack\":\"x\"}]")]
    [InlineData("\"none\"")]
    public void The_v13_schema_refuses_malformed_lines(string lines) => Assert.NotEqual("", V13("creationGuide", Kit(Pick(lines))));

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"id\":\"p\",\"options\":[]}]")]
    [InlineData("[{\"options\":[{\"id\":\"a\",\"lines\":[]}]}]")]
    [InlineData("[{\"id\":\"p\",\"options\":[{\"lines\":[]}]}]")]
    [InlineData("[{\"id\":\"p\",\"options\":[{\"id\":\"a\"}]}]")]
    public void The_v13_schema_refuses_malformed_picks(string picks) => Assert.NotEqual("", V13("creationGuide", Kit(picks)));
}
