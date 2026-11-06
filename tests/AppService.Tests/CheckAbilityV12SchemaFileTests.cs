using System.Text.Json;

namespace TomeStack.AppService.Tests;

/// <summary>Content schema v12 (G10): the published v12 JSON schema accepts a checkAbility and refuses one without an ability.</summary>
public class CheckAbilityV12SchemaFileTests
{
    private static string Errors(string effect)
    {
        using var document = JsonDocument.Parse(
            "{\"contentId\":\"6a7e1000-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e1000-0000-4000-8000-000000000101\",\"schemaVersion\":12,\"kind\":\"feature\",\"name\":\"Fixture Swap\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"5f0d5210-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[" + effect + "]}");
        return SchemaTests.ValidateFile("content-revision.v12.schema.json", document.RootElement);
    }

    [Theory]
    [InlineData("{\"type\":\"checkAbility\",\"id\":\"c\",\"target\":\"skill.athletics\",\"ability\":\"dex\"}")]
    [InlineData("{\"type\":\"checkAbility\",\"id\":\"c\",\"target\":\"ability.int.check\",\"ability\":\"wis\"}")]
    public void The_v12_schema_accepts_a_check_ability(string effect) => Assert.Equal("", Errors(effect));

    [Theory]
    [InlineData("{\"type\":\"checkAbility\",\"id\":\"c\",\"target\":\"skill.athletics\"}")]
    [InlineData("{\"type\":\"checkAbility\",\"id\":\"c\",\"ability\":\"dex\"}")]
    [InlineData("{\"type\":\"checkAbility\",\"id\":\"c\",\"target\":\"initiative\",\"ability\":\"dex\"}")]
    [InlineData("{\"type\":\"checkAbility\",\"id\":\"c\",\"target\":\"skill.athletics\",\"ability\":\"Dex\"}")]
    public void The_v12_schema_refuses_a_malformed_check_ability(string effect) => Assert.NotEqual("", Errors(effect));
}
