using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2 item 1, SPEC C-07: the builder's drafts. <c>character.preview</c> and <c>character.previewChoice</c> calculate an
/// unsaved character with the same checks as saving and choosing, and write nothing; the draft commits in one create
/// or save.
/// </summary>
public class BuilderDraftTests
{
    private static ContentReference Srd(int n) => new(Guid.Parse($"52c00000-0000-4000-8000-{n:D12}"), Guid.Parse($"52e00000-0000-4000-8000-{n:D12}"));

    private static readonly ContentReference Dwarf = Srd(1);
    private static readonly ContentReference Soldier = Srd(2);
    private static readonly ContentReference StrPlus2ConPlus1 = Srd(4);
    private static readonly ContentReference Barbarian = Srd(11);
    private static readonly ContentReference Athletics = Srd(18);
    private static readonly ContentReference Perception = Srd(21);
    private static readonly ContentReference Survival = Srd(22);
    private static readonly ContentReference Berserker = Srd(23);
    private static readonly ContentReference PrimalKnowledge = Srd(25);
    private static readonly ContentReference AnimalHandling = Srd(17);

    private static Character Draft(int level = 1) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Draft Brenna",
        RulesFamily = RulesFamilies.Srd521,
        BaseAbilities = new(15, 13, 14, 8, 12, 10),
        Pins = [Dwarf, Soldier],
        Classes = [new(Barbarian, level)],
    };

    private static JsonElement Dispatch(TempApp temp, string command, object payload) =>
        JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;

    private static IEnumerable<string> Codes(JsonElement response) =>
        response.GetProperty("error").GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()!);

    [Fact]
    public void Preview_calculates_an_unsaved_draft_and_writes_nothing()
    {
        using var temp = new TempApp();
        var draft = Draft();

        var response = Dispatch(temp, "character.preview", draft);

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var view = response.GetProperty("result").Deserialize<CharacterView>(RulesJson.Compact)!;
        Assert.Equal(1, view.Character.Level);
        Assert.Contains(view.Sheet.Choices!, c => c.ChoiceId == "barbarian-skills" && !c.Resolved);
        Assert.Contains(view.Sheet.Choices!, c => c.ChoiceId == "soldier-ability-scores" && !c.Resolved);
        Assert.DoesNotContain(view.Sheet.Choices!, c => c.ChoiceId == "barbarian-subclass"); // level 3
        Assert.Empty(temp.App.ListCharacters());
    }

    [Fact]
    public void Preview_refuses_what_saving_refuses()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(Draft()).Character;

        Assert.Contains("character.name-required", Codes(Dispatch(temp, "character.preview", Draft() with { Name = " " })));
        Assert.Contains("character.level-out-of-range", Codes(Dispatch(temp, "character.preview", Draft(21))));
        Assert.Contains("character.rules-family-changed", Codes(Dispatch(temp, "character.preview", saved with { RulesFamily = RulesFamilies.Srd51 })));
    }

    [Fact]
    public void A_choice_on_a_draft_is_checked_like_character_choose_and_nothing_is_saved()
    {
        using var temp = new TempApp();
        var draft = Draft();

        Assert.Contains("choice.too-many", Codes(Dispatch(temp, "character.previewChoice", new { draft, source = Barbarian, choiceId = "barbarian-skills", selected = new[] { Athletics, Perception, Survival } })));
        Assert.Contains("choice.not-offered", Codes(Dispatch(temp, "character.previewChoice", new { draft, source = Barbarian, choiceId = "barbarian-subclass", selected = new[] { Berserker } })));
        Assert.Contains("character.draft-required", Codes(Dispatch(temp, "character.previewChoice", new { source = Barbarian, choiceId = "barbarian-skills", selected = new[] { Athletics } })));

        var response = Dispatch(temp, "character.previewChoice", new { draft, source = Barbarian, choiceId = "barbarian-skills", selected = new[] { Athletics, Perception } });

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var next = response.GetProperty("result").Deserialize<CharacterView>(RulesJson.Compact)!;
        Assert.Equal([Athletics, Perception], Assert.Single(next.Character.Choices).Selected);
        Assert.Contains(next.Sheet.Choices!, c => c.ChoiceId == "barbarian-skills" && c.Resolved);
        Assert.Empty(temp.App.ListCharacters());
    }

    [Fact]
    public void A_drafted_character_created_level_1_and_levelled_to_3_matches_the_M1_acceptance_sheet()
    {
        using var temp = new TempApp();

        // Create: the draft answers the level-1 choices, then commits once.
        var draft = Draft();
        draft = temp.App.PreviewChoice(new(draft, Soldier, "soldier-ability-scores", [StrPlus2ConPlus1])).Character;
        draft = temp.App.PreviewChoice(new(draft, Barbarian, "barbarian-skills", [Perception, Survival])).Character;
        var created = temp.App.CreateCharacter(new(draft.Name, draft.RulesFamily, draft.BaseAbilities, draft.Pins, draft.Classes, draft.Choices));
        Assert.DoesNotContain(created.Sheet.Choices!, c => !c.Resolved);

        // Level up to 3 as a draft: the level-3 choices appear unresolved, then are answered.
        var levelUp = created.Character with { Classes = [new(Barbarian, 3)] };
        var preview = temp.App.Preview(levelUp);
        Assert.Contains(preview.Sheet.Choices!, c => c.ChoiceId == "barbarian-subclass" && !c.Resolved);
        Assert.Contains(preview.Sheet.Choices!, c => c.ChoiceId == "primal-knowledge-skill" && !c.Resolved);
        levelUp = temp.App.PreviewChoice(new(preview.Character, Barbarian, "barbarian-subclass", [Berserker])).Character;
        levelUp = temp.App.PreviewChoice(new(levelUp, PrimalKnowledge, "primal-knowledge-skill", [AnimalHandling])).Character;

        // Nothing was saved by the level-up draft until it is committed.
        Assert.Equal(1, temp.App.GetCharacter(created.Character.Id).Character.Level);
        var saved = temp.App.SaveCharacter(levelUp);

        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        var expected = temp.App.Preview(brenna with { Id = saved.Character.Id }).Sheet;
        Assert.Equal(TempApp.Json(expected.Fields), TempApp.Json(saved.Sheet.Fields));
        Assert.Empty(saved.Sheet.Diagnostics);
    }

    [Fact]
    public void Cancelling_a_level_up_draft_leaves_the_stored_character_unchanged()
    {
        using var temp = new TempApp();
        var created = temp.App.SaveCharacter(Draft());
        var before = TempApp.Json(temp.App.GetCharacter(created.Character.Id).Character);

        temp.App.Preview(created.Character with { Classes = [new(Barbarian, 2)] });
        temp.App.PreviewChoice(new(created.Character, Barbarian, "barbarian-skills", [Athletics]));

        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(created.Character.Id).Character));
    }
}
