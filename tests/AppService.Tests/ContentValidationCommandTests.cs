using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>M1 item 3: <c>content.validate</c> reports problems for a stored or an unsaved revision and writes nothing.</summary>
public class ContentValidationCommandTests
{
    private static JsonElement Dispatch(TempApp temp, object payload) =>
        JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(JsonSerializer.Serialize(new { id = "1", command = "content.validate", payload }, RulesJson.Compact))).RootElement;

    [Fact]
    public void A_stored_revision_is_validated_by_reference()
    {
        using var temp = new TempApp();
        var pack = temp.AddPack("fixture-pack-m1.json");
        var warden = pack.Revisions.Single(r => r.Name == "Fixture Warden");

        var response = Dispatch(temp, new { reference = warden.Reference });

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.True(response.GetProperty("result").GetProperty("canPublish").GetBoolean());
        Assert.Empty(response.GetProperty("result").GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public void An_unsaved_revision_is_validated_inline_and_its_problems_are_listed()
    {
        using var temp = new TempApp();
        var revisionsBefore = temp.App.Store.ListRevisions().Count;
        var draft = new ContentRevision
        {
            ContentId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid(),
            Kind = ContentKind.Feat,
            Name = "Draft With Problems",
            RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(Guid.NewGuid()),
            Status = RevisionStatus.Draft,
            Effects =
            [
                new ModifierEffect { Id = "bad", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "PB *" },
                new ModifierEffect { Id = "loop", Operation = ModifierOperation.Bonus, Target = FieldIds.Score(Ability.Con), Value = "CON.MOD" },
                new GrantEffect { Id = "missing", Grant = GrantKind.Content, Content = new(Guid.NewGuid(), Guid.NewGuid()) },
            ],
        };

        var result = Dispatch(temp, new { revision = draft }).GetProperty("result");
        var codes = result.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();

        Assert.False(result.GetProperty("canPublish").GetBoolean());
        Assert.Contains("validate.formula-invalid", codes);
        Assert.Contains("effect.dependency-cycle", codes);
        Assert.Contains("validate.reference-missing", codes);
        Assert.Contains("validate.source-missing", codes);
        Assert.Equal(revisionsBefore, temp.App.Store.ListRevisions().Count);
    }

    [Fact]
    public void An_unknown_reference_is_a_clear_error()
    {
        using var temp = new TempApp();

        var response = Dispatch(temp, new { reference = new ContentReference(Guid.NewGuid(), Guid.NewGuid()) });

        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal("content.not-found", response.GetProperty("error").GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }
}
