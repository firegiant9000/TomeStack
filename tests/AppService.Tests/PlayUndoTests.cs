using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D43 Slice 2 (Task 2.3, D44): a compound play change (a form switch with <c>replace</c>) can be undone once, on the server, while
/// nothing else has written the character. The slot is in memory only. A refused undo writes nothing.
/// </summary>
public class PlayUndoTests
{
    private static readonly Guid Discipline = Guid.Parse("6f0dc000-0000-4000-8000-000000000001");
    private static readonly Guid Stance = Guid.Parse("5f8dc000-0000-4000-8000-000000000001");

    private static Guid Content(int n) => Guid.Parse($"6f0dc000-0000-4000-8000-00000000000{n}");

    private static (TempApp Temp, Guid Id) Forms()
    {
        var temp = new TempApp(time: new TickingTime(TempApp.Now)); // each write moves the stamp
        var pack = temp.AddPack("fixture-pack-forms.json");
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json"); // Focus is 12
        var pins = pack.Revisions.Select(r => r.Reference).ToList();
        pins.Add(new(Stance, Guid.Parse("5f8de000-0000-4000-8000-000000000001")));
        return (temp, temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins, .. pins] }).Character.Id);
    }

    /// <summary>Ember on (cost 3), then the replace switch to tide (cost 5), as the page sends it. Returns the view of the switch.</summary>
    private static CharacterView Switched(TempApp temp, Guid id)
    {
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Content(2), ToggleId: "ember"));
        return temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Content(3), ToggleId: "tide", Replace: true,
            ExpectedUpdatedAt: temp.App.GetCharacter(id).Character.UpdatedAt));
    }

    private static int Focus(CharacterView view) => view.Sheet.Resources!.Single(r => r.ResourceId == "focus").Current!.Value;

    private static string Stored(TempApp temp, Guid id) => TempApp.Json(temp.App.Store.FindCharacter(id));

    private static string Refusal(Action act) => Assert.Throws<AppValidationException>(act).Problems[0].Code;

    private static CharacterView Undo(TempApp temp, Guid id, DateTimeOffset stamp, bool confirm = true) =>
        temp.App.UndoPlay(new(id, stamp, confirm));

    [Fact]
    public void Undo_of_a_replace_switch_restores_the_exact_play_state_and_the_pool()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Content(2), ToggleId: "ember"));
        var before = TempApp.Json(temp.App.Store.FindCharacter(id)!.Play);
        var beforeFocus = Focus(temp.App.GetCharacter(id));
        var view = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Content(3), ToggleId: "tide", Replace: true,
            ExpectedUpdatedAt: temp.App.GetCharacter(id).Character.UpdatedAt));
        Assert.Equal(beforeFocus - 5, Focus(view));

        var undone = Undo(temp, id, view.Character.UpdatedAt);

        Assert.Equal(before, TempApp.Json(temp.App.Store.FindCharacter(id)!.Play));
        Assert.Equal(9, Focus(undone));
        Assert.Equal(beforeFocus, Focus(undone));
        Assert.Null(undone.UndoLabel); // one level: the undo itself cannot be undone
        Assert.Equal(undone.Character.UpdatedAt, temp.App.GetCharacter(id).Character.UpdatedAt);
    }

    [Fact]
    public void The_view_offers_undo_only_while_the_stamp_matches()
    {
        var (temp, id) = Forms();
        using var _ = temp;

        var view = Switched(temp, id);

        Assert.Equal("switch to Tide form", view.UndoLabel);
        Assert.Equal("switch to Tide form", temp.App.GetCharacter(id).UndoLabel);

        var spent = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1));

        Assert.Null(spent.UndoLabel);
        Assert.Null(temp.App.GetCharacter(id).UndoLabel);
    }

    [Theory]
    [InlineData("damage")]
    [InlineData("save")]
    [InlineData("rest")]
    [InlineData("restore")]
    public void Undo_is_refused_after_another_change(string other)
    {
        var (temp, id) = Forms();
        using var _ = temp;
        var switched = Switched(temp, id);

        switch (other)
        {
            case "damage":
                temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1));
                break;
            case "save":
                temp.App.SaveCharacter(temp.App.GetCharacter(id).Character with { Name = "Renamed" });
                break;
            case "rest":
                temp.App.Rest(new(id, RestPeriod.LongRest, true, temp.App.PreviewRest(id, RestPeriod.LongRest).Basis));
                break;
            default:
                var snapshot = temp.App.Snapshot(new(id, "after the switch"));
                temp.App.RestoreSnapshot(new(temp.App.PreviewRestore(new(id, snapshot.Id)).Token, Confirm: true));
                break;
        }
        var current = temp.App.Store.FindCharacter(id)!.UpdatedAt;
        Assert.NotEqual(switched.Character.UpdatedAt, current);
        var before = Stored(temp, id);

        Assert.Equal("undo.stale", Refusal(() => Undo(temp, id, current)));
        Assert.Equal("character.stale", Refusal(() => Undo(temp, id, switched.Character.UpdatedAt)));
        Assert.Equal(before, Stored(temp, id));
        Assert.Null(temp.App.GetCharacter(id).UndoLabel);
    }

    [Fact]
    public void Undo_twice_finds_nothing_the_second_time()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        var undone = Undo(temp, id, Switched(temp, id).Character.UpdatedAt);

        Assert.Equal("undo.nothing", Refusal(() => Undo(temp, id, undone.Character.UpdatedAt)));
    }

    [Fact]
    public void Undo_is_gone_after_reopen()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        Switched(temp, id);

        temp.Reopen();
        var stamp = temp.App.GetCharacter(id).Character.UpdatedAt;

        Assert.Null(temp.App.GetCharacter(id).UndoLabel);
        Assert.Equal("undo.nothing", Refusal(() => Undo(temp, id, stamp)));
    }

    [Fact]
    public void An_undo_slot_belongs_to_one_character()
    {
        var (temp, a) = Forms();
        using var _ = temp;
        var other = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json") with { Id = Guid.NewGuid(), Name = "Second" }).Character;
        var switched = Switched(temp, a);

        Assert.Equal("undo.nothing", Refusal(() => Undo(temp, other.Id, temp.App.GetCharacter(other.Id).Character.UpdatedAt)));
        Assert.Null(temp.App.GetCharacter(other.Id).UndoLabel);

        Undo(temp, a, switched.Character.UpdatedAt); // A's slot still works
    }

    [Fact]
    public void The_sandbox_never_sets_an_undo_slot()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        var source = temp.App.CreateHomebrewSource(new("Test Undo Source", [RulesFamilies.Srd521]));
        var draft = new ContentRevision
        {
            ContentId = Guid.NewGuid(),
            RevisionId = Guid.Empty,
            Kind = ContentKind.Class,
            Name = "Test Undo Class",
            RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(source.Id),
            Status = RevisionStatus.Draft,
            Effects = [new HitDieEffect { Id = "hd", Die = 8 }],
        };

        var tried = temp.App.Sandbox(new(Revision: draft, CharacterId: id));
        Assert.Null(tried.View.UndoLabel);
        Assert.Equal("undo.nothing", Refusal(() => Undo(temp, id, temp.App.GetCharacter(id).Character.UpdatedAt)));

        var switched = Switched(temp, id); // and a real slot is left alone by a sandbox run, whose copy shows no label
        var again = temp.App.Sandbox(new(Revision: draft, CharacterId: id));
        Assert.Null(again.View.UndoLabel);
        Assert.Equal("switch to Tide form", temp.App.GetCharacter(id).UndoLabel);
        Undo(temp, id, switched.Character.UpdatedAt);
    }

    [Fact]
    public void A_simple_action_never_sets_a_slot()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        var spent = temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, Amount: 1, ContentId: Discipline, ResourceId: "focus"));

        Assert.Null(spent.UndoLabel);
        Assert.Equal("undo.nothing", Refusal(() => Undo(temp, id, spent.Character.UpdatedAt)));
    }

    [Fact]
    public void Undo_without_confirmation_is_refused()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        var switched = Switched(temp, id);
        var before = Stored(temp, id);

        Assert.Equal("undo.confirmation-required", Refusal(() => Undo(temp, id, switched.Character.UpdatedAt, confirm: false)));

        Assert.Equal(before, Stored(temp, id));
        Assert.Equal("switch to Tide form", temp.App.GetCharacter(id).UndoLabel); // and the undo is still on offer
    }

    [Fact]
    public void Undo_of_an_unknown_character_is_not_found()
    {
        using var temp = new TempApp();

        Assert.Equal("character.not-found", Refusal(() => Undo(temp, Guid.NewGuid(), TempApp.Now)));
    }

    [Fact]
    public void The_dispatcher_lists_and_runs_undo_play()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        var switched = Switched(temp, id);
        var dispatcher = new CommandDispatcher(temp.App);

        Assert.Contains("character.undoPlay", CommandDispatcher.Commands);
        var response = JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(
            new { id = "1", command = "character.undoPlay", payload = new { characterId = id, updatedAt = switched.Character.UpdatedAt.ToString("O"), confirm = true } }, RulesJson.Compact))).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Null(temp.App.GetCharacter(id).UndoLabel);
    }
}
