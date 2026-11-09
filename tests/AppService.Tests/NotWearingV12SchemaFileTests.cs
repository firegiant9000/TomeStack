using System.Text.Json;

namespace TomeStack.AppService.Tests;

/// <summary>Content schema v12 (G4): the published v12 JSON schema accepts an armor and shield gate and refuses a misplaced or malformed one.</summary>
public class NotWearingV12SchemaFileTests
{
    private static string Errors(string effect)
    {
        using var document = JsonDocument.Parse(
            "{\"contentId\":\"6a7e5500-0000-4000-8000-000000000004\",\"revisionId\":\"6a7e5500-0000-4000-8000-000000000104\",\"schemaVersion\":12,\"kind\":\"feature\",\"name\":\"Fixture Bare Stance\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"draft\",\"effects\":[" + effect + "]}");
        return SchemaTests.ValidateFile("content-revision.v12.schema.json", document.RootElement);
    }

    private static string Modifier(string notWearing) =>
        "{\"type\":\"modifier\",\"id\":\"m\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"2\",\"stacking\":\"stack\",\"automation\":\"automatic\",\"timing\":\"always\",\"notWearing\":" + notWearing + "}";

    [Theory]
    [InlineData("[\"armor\",\"shield\"]")]
    [InlineData("[\"heavyArmor\"]")]
    public void The_v12_schema_accepts_a_gate(string notWearing) => Assert.Equal("", Errors(Modifier(notWearing)));

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"cloak\"]")]
    [InlineData("[\"shield\",\"shield\"]")]
    [InlineData("\"armor\"")]
    [InlineData("[\"armor\",\"heavyArmor\"]")]
    public void The_v12_schema_refuses_a_malformed_gate(string notWearing) => Assert.NotEqual("", Errors(Modifier(notWearing)));

    [Fact]
    public void The_v12_schema_refuses_a_gate_inside_an_item_mod()
    {
        const string Plain = "{\"type\":\"itemMod\",\"id\":\"im\",\"appliesTo\":\"weapon\",\"modifiers\":[{\"type\":\"modifier\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"2\"}]}";
        Assert.Equal("", Errors(Plain)); // control: the same item mod without the gate is valid
        Assert.NotEqual("", Errors(Plain.Replace("\"value\":\"2\"", "\"value\":\"2\",\"notWearing\":[\"shield\"]", StringComparison.Ordinal)));
    }
}
