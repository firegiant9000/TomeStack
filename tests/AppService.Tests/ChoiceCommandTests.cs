using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>M1 item 4: <c>character.choose</c> stores a validated selection; the sheet reports every offered choice.</summary>
public class ChoiceCommandTests
{
    private static readonly ContentReference Warden = Ref(10);
    private static readonly ContentReference Athletics = Ref(16);
    private static readonly ContentReference Survival = Ref(17);
    private static readonly ContentReference Nature = Ref(18);
    private static readonly ContentReference Crossroads = Ref(21);
    private static readonly ContentReference CrossroadsWis2014 = Ref(24);

    private static ContentReference Ref(int n) => new(Guid.Parse($"5f1dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"5f1de000-0000-4000-8000-{n:D12}"));

    private static (TempApp App, Guid Id) Setup()
    {
        var temp = new TempApp();
        temp.AddPack("fixture-pack-m1.json");
        var character = TempApp.LoadFixture<Character>("characters/srd521-ash-m1.json") with { Pins = [Crossroads], Classes = [new(Warden, 1)] };
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    private static JsonElement Choose(TempApp temp, Guid id, ContentReference source, string choiceId, params ContentReference[] selected) =>
        JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(JsonSerializer.Serialize(new
        {
            id = "1",
            command = "character.choose",
            payload = new { characterId = id, source, choiceId, selected },
        }, RulesJson.Compact))).RootElement;

    private static IEnumerable<string> Codes(JsonElement response) =>
        response.GetProperty("error").GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()!);

    [Fact]
    public void Choosing_stores_the_selection_and_returns_the_recalculated_sheet_with_the_choice_resolved()
    {
        var (temp, id) = Setup();
        using var _ = temp;

        var before = temp.App.GetCharacter(id).Sheet;
        var response = Choose(temp, id, Warden, "warden-skills", Athletics, Survival);

        Assert.Contains(before.Choices!, c => c.ChoiceId == "warden-skills" && !c.Resolved);
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var stored = temp.App.GetCharacter(id);
        Assert.Equal([Athletics, Survival], Assert.Single(stored.Character.Choices).Selected);
        Assert.Contains(stored.Sheet.Choices!, c => c.ChoiceId == "warden-skills" && c.Resolved);
        Assert.Equal(0 + 2, stored.Sheet.Field(FieldIds.Skill("athletics")).Value);

        // An empty selection clears it again.
        Assert.True(Choose(temp, id, Warden, "warden-skills").GetProperty("ok").GetBoolean());
        Assert.Empty(temp.App.GetCharacter(id).Character.Choices);
    }

    [Fact]
    public void Invalid_selections_are_refused_and_nothing_is_saved()
    {
        var (temp, id) = Setup();
        using var _ = temp;
        var saved = TempApp.Json(temp.App.GetCharacter(id).Character);

        Assert.Contains("choice.too-many", Codes(Choose(temp, id, Warden, "warden-skills", Athletics, Survival, Nature)));
        Assert.Contains("choice.invalid-option", Codes(Choose(temp, id, Warden, "warden-skills", Athletics, Crossroads)));
        Assert.Contains("choice.duplicate-option", Codes(Choose(temp, id, Warden, "warden-skills", Athletics, Athletics)));
        Assert.Contains("choice.option-unavailable", Codes(Choose(temp, id, Crossroads, "crossroads-ability", CrossroadsWis2014)));
        Assert.Contains("choice.not-offered", Codes(Choose(temp, id, Warden, "warden-path", Ref(19)))); // level 3 choice at level 1
        Assert.Contains("choice.not-offered", Codes(Choose(temp, id, Warden, "no-such-choice", Athletics)));

        Assert.Equal(saved, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void An_option_already_chosen_for_another_choice_is_refused()
    {
        // SRD 5.2.1 Barbarian: Primal Knowledge (level 3) picks "another skill" from the same list as the class skills.
        static ContentReference Srd(int n) => new(Guid.Parse($"52c00000-0000-4000-8000-{n:D12}"), Guid.Parse($"52e00000-0000-4000-8000-{n:D12}"));
        using var temp = new TempApp();
        var character = TempApp.LoadFixture<Character>("characters/srd521-ash-m1.json") with { Pins = [], Classes = [new(Srd(11), 3)] };
        var id = temp.App.SaveCharacter(character).Character.Id;
        temp.App.Choose(new(id, Srd(11), "barbarian-skills", [Srd(18), Srd(22)])); // Athletics, Survival

        var response = Choose(temp, id, Srd(25), "primal-knowledge-skill", Srd(18));

        Assert.Contains("choice.option-already-chosen", Codes(response));
        Assert.True(Choose(temp, id, Srd(25), "primal-knowledge-skill", Srd(21)).GetProperty("ok").GetBoolean()); // Perception
    }

    [Fact]
    public void Chosen_content_travels_in_packages()
    {
        var (temp, id) = Setup();
        using var _ = temp;
        temp.App.Choose(new(id, Warden, "warden-skills", [Athletics, Survival]));
        var view = temp.App.GetCharacter(id);

        var export = temp.App.ExportCharacters([id]);
        using var destination = new TempApp();
        destination.App.ApplyImport(export.Content);

        Assert.Contains(export.Manifest.Entries, e => e.Path == $"content/{Athletics.RevisionId:D}.json");
        Assert.Equal(TempApp.Json(view.Sheet), TempApp.Json(destination.App.GetCharacter(id).Sheet));
    }
}
