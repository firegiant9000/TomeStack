using System.Text.Json;

namespace TomeStack.AppService.Tests;

/// <summary>Content schema v12 (G5): the published v12 JSON schema accepts an ability-score cap and refuses a misplaced or out-of-range one.</summary>
public class AbilityCapV12SchemaFileTests
{
    private static string Errors(string effect)
    {
        using var document = JsonDocument.Parse(
            "{\"contentId\":\"6a7e5500-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e5500-0000-4000-8000-000000000101\",\"schemaVersion\":12,\"kind\":\"feature\",\"name\":\"Fixture Gauntlet\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[" + effect + "]}");
        return SchemaTests.ValidateFile("content-revision.v12.schema.json", document.RootElement);
    }

    private static string Modifier(string operation, string target, string cap) =>
        "{\"type\":\"modifier\",\"id\":\"m\",\"operation\":\"" + operation + "\",\"target\":\"" + target + "\",\"value\":\"4\",\"stacking\":\"stack\",\"automation\":\"automatic\",\"timing\":\"always\",\"cap\":" + cap + "}";

    [Theory]
    [InlineData("bonus", "ability.str.score", "25")]
    [InlineData("bonus", "ability.cha.score", "21")]
    [InlineData("bonus", "ability.dex.score", "30")]
    public void The_v12_schema_accepts_a_capped_score_bonus(string operation, string target, string cap) =>
        Assert.Equal("", Errors(Modifier(operation, target, cap)));

    [Theory]
    [InlineData("bonus", "initiative", "25")]
    [InlineData("bonus", "ability.str.check", "25")]
    [InlineData("set", "ability.str.score", "25")]
    [InlineData("replace", "ability.str.score", "25")]
    [InlineData("bonus", "ability.str.score", "31")]
    [InlineData("bonus", "ability.str.score", "20")]
    [InlineData("bonus", "ability.str.score", "\"25\"")]
    public void The_v12_schema_refuses_a_misplaced_or_out_of_range_cap(string operation, string target, string cap) =>
        Assert.NotEqual("", Errors(Modifier(operation, target, cap)));
}
