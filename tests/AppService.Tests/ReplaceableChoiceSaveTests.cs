using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// MECH-X item 2 (R42, R43, R47): a save that swaps picks of a replaceable choice outside its window lands, and its reply (only)
/// carries one <c>choice.replaced-outside-window</c> notice. Fixture: the golden "Fixture Shifting Repertoire" (techniques: 2 picks,
/// level-up window, max 1) pinned to a character.
/// </summary>
public class ReplaceableChoiceSaveTests
{
    private static readonly Guid FeatureContent = Guid.Parse("6a7e00f2-0000-4000-8000-000000000001");

    private static ContentReference Feature => Ref(0x01, 0x101);

    private static ContentReference Ref(int content, int revision) =>
        new(Guid.Parse($"6a7e00f2-0000-4000-8000-{content:X12}"), Guid.Parse($"6a7e00f2-0000-4000-8000-{revision:X12}"));

    private static ContentReference TechniqueOne => Ref(0x11, 0x111);
    private static ContentReference TechniqueTwo => Ref(0x12, 0x112);
    private static ContentReference TechniqueThree => Ref(0x13, 0x113);

    private static (TempApp Temp, CharacterView Created) Setup(int level = 3, bool pick = true)
    {
        var temp = new TempApp(time: new TickingTime(TempApp.Now));
        temp.AddPack("fixture-pack-m1.json");
        temp.App.Store.UpsertSource(new SourceRecord
        {
            Id = Guid.Parse("6a7e0000-0000-4000-8000-000000000001"),
            Title = "Fixture Replaceable Choices",
            Publisher = "Fixture",
            RulesFamilies = [RulesFamilies.Srd521],
            EditionVersion = "1",
            License = "Fixture",
            Redistributable = false,
        });
        foreach (var revision in TempApp.LoadFixture<List<ContentRevision>>("golden/choice-revisions.json"))
            temp.App.Store.AddRevision(revision);
        var ash = TempApp.LoadFixture<Character>("characters/srd521-ash-m1.json") with
        {
            Pins = [Feature],
            Classes = [],
            Level = level,
            Choices = pick ? [new(Feature, "techniques", [TechniqueOne, TechniqueTwo])] : [],
        };
        return (temp, temp.App.SaveCharacter(ash));
    }

    private static Character WithTechniques(Character character, int level, params ContentReference[] picks) =>
        character with { Level = level, Choices = [new(Feature, "techniques", picks)] };

    [Fact]
    public void A_choices_save_that_swaps_a_level_up_pick_gets_the_notice()
    {
        var (temp, created) = Setup();
        using var _ = temp;

        var view = temp.App.SaveCharacter(WithTechniques(created.Character, 3, TechniqueOne, TechniqueThree));

        var notice = Assert.Single(view.Notices!);
        Assert.Equal("choice.replaced-outside-window", notice.Code);
        Assert.Equal(Feature, notice.Content);
        Assert.Contains("'techniques'", notice.Message, StringComparison.Ordinal);
        var reread = temp.App.GetCharacter(created.Character.Id);
        Assert.Equal([TechniqueOne, TechniqueThree], Assert.Single(reread.Character.Choices).Selected); // the save landed
        Assert.Null(reread.Notices); // never stored
        Assert.Contains(view.Sheet.Choices!, c => c.ChoiceId == "techniques" && c.Replaceable is { When: ReplaceWindow.LevelUp, Max: 1 });
    }

    [Fact]
    public void A_level_up_save_that_swaps_one_pick_gets_none()
    {
        var (temp, created) = Setup();
        using var _ = temp;

        var view = temp.App.SaveCharacter(WithTechniques(created.Character, 4, TechniqueOne, TechniqueThree));

        Assert.Null(view.Notices);
        Assert.Equal([TechniqueOne, TechniqueThree], Assert.Single(temp.App.GetCharacter(created.Character.Id).Character.Choices).Selected);
    }

    [Fact]
    public void Choose_reports_it_too()
    {
        var (temp, created) = Setup();
        using var _ = temp;

        var view = temp.App.Choose(new(created.Character.Id, Feature, "techniques", [TechniqueTwo, TechniqueThree]));

        Assert.Equal("choice.replaced-outside-window", Assert.Single(view.Notices!).Code);

        // R42: clearing is a replacement; picking again from nothing is a first pick.
        var cleared = temp.App.Choose(new(created.Character.Id, Feature, "techniques", []));
        Assert.Equal("choice.replaced-outside-window", Assert.Single(cleared.Notices!).Code);
        Assert.Null(temp.App.Choose(new(created.Character.Id, Feature, "techniques", [TechniqueOne, TechniqueTwo])).Notices);
    }

    [Fact]
    public void A_stale_save_is_refused_without_a_notice()
    {
        var (temp, created) = Setup();
        using var _ = temp;
        temp.App.SaveCharacter(created.Character); // the stored stamp moves

        var stale = Assert.Throws<AppValidationException>(() => temp.App.SaveCharacter(WithTechniques(created.Character, 3, TechniqueOne, TechniqueThree)));

        Assert.Equal("character.stale", stale.Code);
        Assert.Equal([TechniqueOne, TechniqueTwo], Assert.Single(temp.App.GetCharacter(created.Character.Id).Character.Choices).Selected);
    }

    [Fact]
    public void A_content_update_that_re_pins_picks_gets_none()
    {
        var (temp, created) = Setup();
        using var _ = temp;
        // A new revision of the feature and of the two options picked, with the same content ids.
        ContentRevision Republish(ContentReference old, int revision, Func<ContentRevision, ContentRevision>? adjust = null)
        {
            var next = temp.App.Store.FindRevision(old)! with { RevisionId = Ref(0, revision).RevisionId };
            next = adjust?.Invoke(next) ?? next;
            temp.App.Store.AddRevision(next);
            return next;
        }
        var one = Republish(TechniqueOne, 0x211);
        var two = Republish(TechniqueTwo, 0x212);
        Republish(TechniqueThree, 0x213);
        var feature = Republish(Feature, 0x201, f => f with
        {
            Effects = [.. f.Effects.Select(e => e is ChoiceEffect { ChoiceId: "techniques" } c
                ? c with { Options = [Ref(0x11, 0x211), Ref(0x12, 0x212), Ref(0x13, 0x213)] }
                : e)],
        });

        Assert.Null(temp.App.ApplyUpdate(created.Character.Id, TechniqueOne, one.Reference, confirm: true).Notices);
        Assert.Null(temp.App.ApplyUpdate(created.Character.Id, TechniqueTwo, two.Reference, confirm: true).Notices);
        var view = temp.App.ApplyUpdate(created.Character.Id, Feature, feature.Reference, confirm: true);

        Assert.Null(view.Notices);
        var selection = Assert.Single(temp.App.GetCharacter(created.Character.Id).Character.Choices);
        Assert.Equal(feature.Reference, selection.Source);
        Assert.Equal([one.Reference, two.Reference], selection.Selected);
    }

    [Fact]
    public void A_new_character_gets_none()
    {
        var (temp, created) = Setup();
        using var _ = temp;
        var fresh = created.Character with { Id = Guid.NewGuid(), Choices = [new(Feature, "techniques", [TechniqueOne, TechniqueThree])] };

        var view = temp.App.SaveCharacter(fresh);

        Assert.Null(view.Notices);
        Assert.Equal(FeatureContent, Assert.Single(view.Sheet.Choices!, c => c.ChoiceId == "techniques").Source.ContentId);
    }

    [Fact]
    public void The_notice_is_not_in_the_wire_reply_of_a_later_read()
    {
        var (temp, created) = Setup();
        using var _ = temp;
        temp.App.SaveCharacter(WithTechniques(created.Character, 3, TechniqueOne, TechniqueThree));

        var read = JsonSerializer.Serialize(temp.App.GetCharacter(created.Character.Id), RulesJson.Compact);

        Assert.DoesNotContain("replaced-outside-window", read, StringComparison.Ordinal);
    }
}
