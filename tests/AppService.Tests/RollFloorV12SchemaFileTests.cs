using System.Text.Json;

namespace TomeStack.AppService.Tests;

/// <summary>Content schema v12 (G9): the published v12 JSON schema accepts a rollFloor with a face or a total and refuses both or neither.</summary>
public class RollFloorV12SchemaFileTests
{
    private static string Doc(string effect) =>
        "{\"contentId\":\"6a7e0900-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0900-0000-4000-8000-000000000101\",\"schemaVersion\":12,\"kind\":\"feature\",\"name\":\"Fixture Floor\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"5f0d5210-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[" + effect + "]}";

    private static string Errors(string effect)
    {
        using var document = JsonDocument.Parse(Doc(effect));
        return SchemaTests.ValidateFile("content-revision.v12.schema.json", document.RootElement);
    }

    [Theory]
    [InlineData("{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"check\",\"face\":10}")]
    [InlineData("{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"skill.stealth\",\"face\":2,\"proficient\":true}")]
    [InlineData("{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"ability.str.check\",\"total\":\"STR.SCORE\"}")]
    public void The_v12_schema_accepts_a_roll_floor(string effect) => Assert.Equal("", Errors(effect));

    [Theory]
    [InlineData("{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"check\",\"face\":10,\"total\":\"8\"}")]
    [InlineData("{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"check\"}")]
    [InlineData("{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"check\",\"face\":1}")]
    [InlineData("{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"check\",\"face\":21}")]
    [InlineData("{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"save.dex\",\"face\":10}")]
    [InlineData("{\"type\":\"rollFloor\",\"id\":\"f\",\"face\":10}")]
    public void The_v12_schema_refuses_a_malformed_roll_floor(string effect) => Assert.NotEqual("", Errors(effect));
}
