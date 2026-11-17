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

    private static CreateCharacterRequest Create(Character draft, IReadOnlyList<EquipmentEntry>? equipment = null, Currency? coins = null) =>
        new(draft.Name, draft.RulesFamily, draft.BaseAbilities, draft.Pins, draft.Classes, Equipment: equipment, Coins: coins);

    private static IReadOnlyList<ContentOption> Items(TempApp temp, string family) =>
        [.. temp.App.ListContent(family).Where(o => o.Kind == ContentKind.Item && o.RulesFamilies.Contains(family))];

    [Fact]
    public void Create_carries_the_kit_gear_and_coins_into_the_saved_character()
    {
        using var temp = new TempApp();
        var items = Items(temp, RulesFamilies.Srd521);
        var entries = new[] { new EquipmentEntry(items[0].Reference, Quantity: 2), new EquipmentEntry(items[1].Reference) };

        var view = temp.App.CreateCharacter(Create(Draft(), entries, new Currency { Gp = 15 }));

        var saved = temp.App.GetCharacter(view.Character.Id).Character;
        Assert.Equal(2, saved.Equipment.Count);
        Assert.Equal(2, saved.Equipment[0].Quantity);
        Assert.Equal(items[1].Reference, saved.Equipment[1].Item);
        Assert.Equal(15, saved.Currency.Gp);
    }

    [Fact]
    public void Create_refuses_an_item_from_the_other_rules_family()
    {
        using var temp = new TempApp();
        var only51 = Items(temp, RulesFamilies.Srd51).First(o => !o.RulesFamilies.Contains(RulesFamilies.Srd521));

        var ex = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(Create(Draft(), [new EquipmentEntry(only51.Reference)])));

        Assert.Contains(ex.Problems, p => p.Code == "character.equipment-unknown");
        Assert.Empty(temp.App.ListCharacters());
    }

    [Fact]
    public void Create_refuses_a_reference_that_is_no_published_item()
    {
        using var temp = new TempApp();

        var ex = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(Create(Draft(), [new EquipmentEntry(Srd(999))])));
        Assert.Contains(ex.Problems, p => p.Code == "character.equipment-unknown");

        var notItem = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(Create(Draft(), [new EquipmentEntry(Barbarian)])));
        Assert.Contains(notItem.Problems, p => p.Code == "character.equipment-unknown");
    }

    [Fact]
    public void Create_reports_a_null_entry_or_null_item_as_unknown_not_a_crash()
    {
        using var temp = new TempApp();

        var nullEntry = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(Create(Draft(), [null!])));
        Assert.Contains(nullEntry.Problems, p => p.Code == "character.equipment-unknown");

        var nullItem = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(Create(Draft(), [new EquipmentEntry(null!)])));
        Assert.Contains(nullItem.Problems, p => p.Code == "character.equipment-unknown");
    }

    [Fact]
    public void Create_refuses_a_mod_that_is_not_a_published_mod_in_the_family()
    {
        using var temp = new TempApp();
        var items = Items(temp, RulesFamilies.Srd521);
        var only51 = Items(temp, RulesFamilies.Srd51).First(o => !o.RulesFamilies.Contains(RulesFamilies.Srd521));

        // Another family's revision as a mod: refused by Create's own family check.
        var otherFamily = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(Create(Draft(), [new EquipmentEntry(items[0].Reference, Mods: [only51.Reference])])));
        Assert.Contains(otherFamily.Problems, p => p.Code == "character.equipment-unknown");

        // A plain item as a mod: the save path (checkMods) already refuses it.
        var plain = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(Create(Draft(), [new EquipmentEntry(items[0].Reference, Mods: [items[1].Reference])])));
        Assert.Contains(plain.Problems, p => p.Code == "character.equipment-mod-invalid");

        // A reference to nothing.
        var missing = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(Create(Draft(), [new EquipmentEntry(items[0].Reference, Mods: [Srd(999)])])));
        Assert.Contains(missing.Problems, p => p.Code == "character.equipment-unknown");
        Assert.Empty(temp.App.ListCharacters());
    }

    [Fact]
    public void Create_without_gear_behaves_as_before()
    {
        using var temp = new TempApp();

        var view = temp.App.CreateCharacter(Create(Draft()));

        Assert.Empty(view.Character.Equipment);
        Assert.Equal(new Currency(), view.Character.Currency);
    }

    [Fact]
    public void Create_keeps_the_quantity_and_coin_limits()
    {
        using var temp = new TempApp();
        var item = Items(temp, RulesFamilies.Srd521)[0];

        var quantity = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(Create(Draft(), [new EquipmentEntry(item.Reference, Quantity: 0)])));
        Assert.Contains(quantity.Problems, p => p.Code == "character.equipment-quantity");

        var coins = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(Create(Draft(), null, new Currency { Gp = -1 })));
        Assert.Contains(coins.Problems, p => p.Code == "character.currency-out-of-range");
    }

    // Wave 3 G10 (D99): the level-up options.

    private static ContentReference ClassRef(TempApp temp, string name) =>
        temp.App.ListContent(RulesFamilies.Srd521).Single(o => o.Kind == ContentKind.Class && o.Compatible && o.Name == name && !o.Superseded).Reference;

    private static Character Cleric2(TempApp temp, int str = 10, int dex = 12, int intelligence = 8, int wis = 14, Guid? campaign = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Level Cleric",
        RulesFamily = RulesFamilies.Srd521,
        BaseAbilities = new(str, dex, 14, intelligence, wis, 10),
        Classes = [new(ClassRef(temp, "Cleric"), 2)],
        CampaignId = campaign,
    };

    private static IReadOnlyList<LevelUpOption> Options(TempApp temp, Character character) =>
        temp.App.LevelUpOptions(temp.App.SaveCharacter(character).Character.Id);

    [Fact]
    public void LevelUpOptions_lists_the_current_class_first_with_what_the_next_level_gives()
    {
        using var temp = new TempApp();

        var options = Options(temp, Cleric2(temp));

        var cleric = options[0];
        Assert.Equal("Cleric", cleric.Name);
        Assert.True(cleric.Current);
        Assert.Equal(3, cleric.NextLevel);
        Assert.NotEmpty(cleric.Gains);
        Assert.Empty(cleric.Unmet);
        Assert.All(options.Skip(1), o => Assert.False(o.Current));
        Assert.All(options.Skip(1), o => Assert.Equal(1, o.NextLevel));
        var others = options.Skip(1).Select(o => o.Name).ToList();
        Assert.Equal([.. others.Order(StringComparer.CurrentCultureIgnoreCase)], others);
        Assert.DoesNotContain(options.Skip(1), o => o.Name == "Cleric");
    }

    [Fact]
    public void LevelUpOptions_says_what_a_new_class_needs_in_the_players_words()
    {
        using var temp = new TempApp();

        var options = Options(temp, Cleric2(temp, intelligence: 8));

        Assert.Equal(["Intelligence 13 (you have 8)"], options.Single(o => o.Name == "Wizard").Unmet);
        Assert.Empty(Options(temp, Cleric2(temp, intelligence: 13)).Single(o => o.Name == "Wizard").Unmet);
    }

    [Fact]
    public void LevelUpOptions_gives_one_phrase_for_an_either_or_requirement_and_none_for_a_class_without_one()
    {
        using var temp = new TempApp();

        var options = Options(temp, Cleric2(temp, str: 10, dex: 12));

        Assert.Equal(["Strength 13 or Dexterity 13 (you have 10 and 12)"], options.Single(o => o.Name == "Fighter").Unmet);
        Assert.Empty(Options(temp, Cleric2(temp, str: 13)).Single(o => o.Name == "Fighter").Unmet);
        // A character who meets everything is told nothing is missing, whatever the class.
        Assert.All(Options(temp, Cleric2(temp) with { BaseAbilities = new(15, 15, 15, 15, 15, 15) }), o => Assert.Empty(o.Unmet));
    }

    [Fact]
    public void LevelUpOptions_adds_what_the_current_class_needs_to_every_new_class()
    {
        using var temp = new TempApp();

        var options = Options(temp, Cleric2(temp, intelligence: 14, wis: 12));

        // A Cleric needs Wisdom 13 to multiclass, so any class taken beside it says so.
        Assert.Equal(["Wisdom 13 (you have 12)"], options.Single(o => o.Name == "Wizard").Unmet);
        Assert.Empty(options[0].Unmet);
    }

    [Fact]
    public void LevelUpOptions_leaves_out_classes_a_campaign_does_not_allow_but_keeps_the_characters_own()
    {
        using var temp = new TempApp();
        var campaign = temp.App.SaveCampaign(new Campaign { Id = Guid.Empty, Name = "Closed table", RulesFamily = RulesFamilies.Srd521, AllowedSources = [] });

        var options = Options(temp, Cleric2(temp, campaign: campaign.Id));

        Assert.Equal(["Cleric"], options.Select(o => o.Name));
    }

    [Fact]
    public void LevelUpOptions_leaves_out_classes_of_the_other_rules_family()
    {
        using var temp = new TempApp();
        var options = Options(temp, Cleric2(temp));
        var other = temp.App.ListContent(RulesFamilies.Srd51).Where(o => o.Kind == ContentKind.Class && !o.RulesFamilies.Contains(RulesFamilies.Srd521)).Select(o => o.Reference.ContentId).ToHashSet();

        Assert.DoesNotContain(options, o => other.Contains(o.ContentId));
    }

    [Fact]
    public void LevelUpOptions_is_empty_at_level_20_as_the_sheet_offers_no_level_up_there()
    {
        using var temp = new TempApp();
        var character = Cleric2(temp) with { Classes = [new(ClassRef(temp, "Cleric"), 20)] };

        Assert.Empty(Options(temp, character));
    }

    [Fact]
    public void LevelUpOptions_is_a_command_and_refuses_an_unknown_character()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(Cleric2(temp));

        var ok = Dispatch(temp, "character.levelUpOptions", new { characterId = saved.Character.Id });
        var missing = Dispatch(temp, "character.levelUpOptions", new { characterId = Guid.NewGuid() });

        Assert.True(ok.GetProperty("ok").GetBoolean(), ok.ToString());
        Assert.Equal("Cleric", ok.GetProperty("result")[0].GetProperty("name").GetString());
        Assert.Equal(3, ok.GetProperty("result")[0].GetProperty("nextLevel").GetInt32());
        Assert.Contains("character.not-found", Codes(missing));
    }
}
