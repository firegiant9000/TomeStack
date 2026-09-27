using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// SPEC Q-02: commands given empty (null) list entries, which nullable annotations let through, answer with a
/// diagnostic instead of an internal error, and store nothing.
/// </summary>
public class HostileInputTests
{
    /// <summary>Seeded by every test data folder (tests/RulesFixtures/fixture-pack.json).</summary>
    private const string SeededSource = "5f0d5000-0000-4000-8000-000000000001";

    private static JsonElement Dispatch(TempApp app, string command, JsonNode payload) =>
        JsonDocument.Parse(new CommandDispatcher(app.App).Dispatch(
            new JsonObject { ["id"] = "1", ["command"] = command, ["payload"] = payload }.ToJsonString())).RootElement;

    private static void AssertValidation(JsonElement response, string code)
    {
        Assert.False(response.GetProperty("ok").GetBoolean(), response.ToString());
        var error = response.GetProperty("error");
        Assert.Equal("validation", error.GetProperty("code").GetString());
        Assert.Contains(error.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("code").GetString() == code);
    }

    private static JsonObject Revision(string effects) => JsonNode.Parse($$"""
        {"contentId":"{{Guid.NewGuid()}}","revisionId":"{{Guid.NewGuid()}}","schemaVersion":3,"kind":"feat","name":"Hostile",
         "rulesFamilies":["srd-5.1"],"provenance":{"sourceId":"{{SeededSource}}"},"status":"draft","effects":{{effects}}}
        """)!.AsObject();

    [Theory]
    [InlineData("pins")]
    [InlineData("classes")]
    [InlineData("choices")]
    [InlineData("crossFamilyExceptions")]
    [InlineData("overrides")]
    public void Saving_a_character_with_an_empty_list_entry_is_refused_and_stores_nothing(string list)
    {
        using var app = new TempApp();
        var created = app.App.CreateCharacter(new("Hostile", RulesFamilies.Srd51, new(10, 10, 10, 10, 10, 10), null)).Character;
        var payload = JsonSerializer.SerializeToNode(created, RulesJson.Compact)!.AsObject();
        payload[list] = new JsonArray((JsonNode?)null);
        payload["name"] = "Changed";

        AssertValidation(Dispatch(app, "character.save", payload), "character.empty-entry");

        app.Reopen();
        Assert.Equal("Hostile", app.App.GetCharacter(created.Id).Character.Name); // the stored copy still opens, unchanged
    }

    [Fact]
    public void A_choice_with_an_empty_selected_option_is_refused()
    {
        using var app = new TempApp();
        var created = app.App.CreateCharacter(new("Hostile", RulesFamilies.Srd51, new(10, 10, 10, 10, 10, 10), null)).Character;
        var payload = new JsonObject
        {
            ["characterId"] = created.Id,
            ["source"] = new JsonObject { ["contentId"] = Guid.NewGuid(), ["revisionId"] = Guid.NewGuid() },
            ["choiceId"] = "pick",
            ["selected"] = new JsonArray((JsonNode?)null),
        };

        AssertValidation(Dispatch(app, "character.choose", payload), "choice.empty-entry");
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("""[{"type":"choice","id":"pick","choiceId":"pick","options":[null]}]""")]
    public void A_draft_with_an_empty_entry_is_not_saved_and_validation_reports_it(string effects)
    {
        using var app = new TempApp();

        AssertValidation(Dispatch(app, "content.saveDraft", new JsonObject { ["revision"] = Revision(effects) }), "validate.empty-entry");

        var validated = Dispatch(app, "content.validate", new JsonObject { ["revision"] = Revision(effects) });
        Assert.True(validated.GetProperty("ok").GetBoolean(), validated.ToString());
        Assert.False(validated.GetProperty("result").GetProperty("canPublish").GetBoolean());
        Assert.Equal("validate.empty-entry", validated.GetProperty("result").GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public void A_draft_with_an_empty_rules_family_is_not_saved()
    {
        using var app = new TempApp();
        var revision = Revision("[]");
        revision["rulesFamilies"] = new JsonArray((JsonNode?)null);

        AssertValidation(Dispatch(app, "content.saveDraft", new JsonObject { ["revision"] = revision }), "validate.empty-entry");
    }
}
