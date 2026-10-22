using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D43 Slice 3 (Task 3.4): <c>character.turnPreview</c> and <c>character.turn</c> on the turns pack (Iron stance: 3 breath to enter,
/// upkeep 2; Flare overlay: upkeep 1 breath or it ends, and 1 strain on a counting-up pool of 3; Second Wind Rhythm: regains 1 breath
/// at the end of a turn). The token is <c>Character.UpdatedAt</c>.
/// </summary>
public class TurnCommandTests
{
    private static readonly Guid Breath = Guid.Parse("6f0dc000-0000-4000-8000-000000000030");
    private static readonly Guid Iron = Guid.Parse("6f0dc000-0000-4000-8000-000000000031");
    private static readonly Guid Flare = Guid.Parse("6f0dc000-0000-4000-8000-000000000032");
    private static string IronUpkeep => $"upkeep:{Iron:D}:iron-upkeep";
    private static string IronShort => $"short:{Iron:D}:iron-upkeep";

    /// <param name="ironDuration">False drops Iron stance's 3-round duration in memory (the fixture file is unchanged): a stance lasting until switched off.</param>
    private static (TempApp Temp, Guid Id) Turns(bool ironDuration = true)
    {
        var temp = new TempApp(time: new TickingTime(TempApp.Now)); // each write moves the stamp
        var pack = temp.AddPack("fixture-pack-turns.json", ironDuration ? null : r => r.ContentId != Iron ? r : r with
        {
            Effects = [.. r.Effects.Select(e => e is ToggleEffect t ? t with { Duration = null } : e)],
        });
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        var pins = pack.Revisions.Select(r => r.Reference);
        return (temp, temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins, .. pins] }).Character.Id);
    }

    private static DateTimeOffset Stamp(TempApp temp, Guid id) => temp.App.GetCharacter(id).Character.UpdatedAt;

    private static CharacterView StartEncounter(TempApp temp, Guid id) =>
        temp.App.Play(new(id, PlayActionKind.StartEncounter, Confirm: true, ExpectedUpdatedAt: Stamp(temp, id)));

    private static CharacterView On(TempApp temp, Guid id, Guid content, string toggle) =>
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: content, ToggleId: toggle));

    private static CharacterView SpendBreath(TempApp temp, Guid id, int amount) =>
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, Amount: amount, ContentId: Breath, ResourceId: "breath"));

    /// <summary>In an encounter at round 0 with Iron on (breath 4 of 7 left).</summary>
    private static (TempApp Temp, Guid Id) InEncounterWithIron()
    {
        var (temp, id) = Turns();
        StartEncounter(temp, id);
        On(temp, id, Iron, "iron");
        return (temp, id);
    }

    private static TurnRequest Request(TempApp temp, Guid id, TurnMoment at = TurnMoment.TurnStart, DateTimeOffset? stamp = null,
        IReadOnlyList<string>? skip = null, IReadOnlyDictionary<string, TurnChoiceOption>? choices = null, bool confirm = true) =>
        new(id, at, confirm, stamp ?? Stamp(temp, id), skip, choices);

    private static int Left(CharacterView view, string resource) => view.Sheet.Resources!.Single(r => r.ResourceId == resource).Current!.Value;

    private static int Round(CharacterView view) => view.Character.Play.Encounter!.Round;

    private static string Refusal(Action act) => Assert.Throws<AppValidationException>(act).Problems[0].Code;

    // ---- Step 0: the bare StartEncounter action -------------------------------------------------------------------

    [Fact]
    public void Start_encounter_sets_round_0()
    {
        var (temp, id) = Turns();
        using var _ = temp;

        var view = StartEncounter(temp, id);

        Assert.Equal(0, Round(view));
        Assert.Equal("start encounter", view.UndoLabel);
        Assert.Equal(10, view.Character.SchemaVersion);
        Assert.Equal("encounter.already-started", Refusal(() => StartEncounter(temp, id)));
    }

    [Fact]
    public void Start_encounter_without_the_stamp_is_refused()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.StartEncounter, Confirm: true)));

        Assert.Equal("play.basis-required", refused.Problems[0].Code);
        Assert.Equal("play.basis-required", refused.Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void Start_encounter_can_be_undone()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        var started = StartEncounter(temp, id);

        var undone = temp.App.UndoPlay(new(id, started.Character.UpdatedAt, Confirm: true));

        Assert.Null(undone.Character.Play.Encounter);
    }

    // ---- preview and confirm --------------------------------------------------------------------------------------

    [Fact]
    public void A_turn_preview_writes_nothing()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var preview = temp.App.PreviewTurn(Request(temp, id, confirm: false));

        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
        Assert.Equal(Stamp(temp, id), preview.UpdatedAt);
        Assert.Equal(TurnMoment.TurnStart, preview.At);
        Assert.Contains(preview.Changes, c => c.Id == "round");
        Assert.Contains(preview.Changes, c => c.Id == IronUpkeep);
    }

    [Fact]
    public void A_turn_preview_outside_an_encounter_is_refused()
    {
        var (temp, id) = Turns();
        using var _ = temp;

        Assert.Equal("turn.not-in-encounter", Refusal(() => temp.App.PreviewTurn(Request(temp, id))));
    }

    [Fact]
    public void A_turn_applies_the_previewed_plan()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        Assert.Equal(4, Left(temp.App.GetCharacter(id), "breath"));
        var preview = temp.App.PreviewTurn(Request(temp, id, confirm: false));

        var view = temp.App.Turn(Request(temp, id, stamp: preview.UpdatedAt));

        Assert.Equal(2, Left(view, "breath"));
        Assert.Equal(1, Round(view));
        Assert.Equal("turn start", view.UndoLabel);
        Assert.Null(temp.App.GetCharacter(id).Notices); // notices are reply-only
    }

    [Fact]
    public void A_turn_end_labels_its_undo_and_regains_breath()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;

        var view = temp.App.Turn(Request(temp, id, TurnMoment.TurnEnd));

        Assert.Equal(5, Left(view, "breath")); // Second Wind Rhythm regains 1; Iron's upkeep is at the start
        Assert.Equal(0, Round(view));
        Assert.Equal("turn end", view.UndoLabel);
    }

    [Fact]
    public void A_turn_without_confirm_is_refused_and_nothing_changes()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Turn(Request(temp, id, confirm: false)));

        Assert.Equal("turn.confirmation-required", refused.Problems[0].Code);
        Assert.Equal("turn.confirmation-required", refused.Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_turn_without_the_stamp_is_refused_and_nothing_changes()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Turn(new(id, Confirm: true)));

        Assert.Equal("play.basis-required", refused.Problems[0].Code);
        Assert.Equal("play.basis-required", refused.Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_turn_outside_an_encounter_is_refused()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Turn(Request(temp, id)));

        Assert.Equal("turn.not-in-encounter", refused.Problems[0].Code);
        Assert.Equal("turn.not-in-encounter", refused.Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_turn_for_an_unknown_character_is_not_found()
    {
        var (temp, _) = Turns();
        using var _ = temp;

        Assert.Equal("character.not-found", Refusal(() => temp.App.Turn(new(Guid.NewGuid(), Confirm: true, ExpectedUpdatedAt: TempApp.Now))));
    }

    [Fact]
    public void A_turn_after_another_change_is_refused_stale()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        var preview = temp.App.PreviewTurn(Request(temp, id, confirm: false));
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1)); // another change after the preview
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Turn(Request(temp, id, stamp: preview.UpdatedAt)));

        Assert.Equal("turn.preview-stale", refused.Problems[0].Code);
        Assert.Equal("turn.preview-stale", refused.Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_double_sent_turn_is_applied_once()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        var request = Request(temp, id);

        var first = temp.App.Turn(request);
        var second = Assert.Throws<AppValidationException>(() => temp.App.Turn(request));

        Assert.Equal("turn.preview-stale", second.Problems[0].Code);
        Assert.Equal(1, Round(temp.App.GetCharacter(id)));
        Assert.Equal(2, Left(temp.App.GetCharacter(id), "breath"));
        Assert.Equal(1, Round(first));
    }

    // ---- choices --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_spend_that_cannot_be_paid_asks_and_the_turn_waits_for_the_choice()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        SpendBreath(temp, id, 3); // breath 1 left; Iron's upkeep needs 2
        var preview = temp.App.PreviewTurn(Request(temp, id, confirm: false));
        Assert.Equal(IronShort, Assert.Single(preview.Choices).Id);
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Turn(Request(temp, id)));
        Assert.Equal("turn.choice-required", refused.Problems[0].Code);
        Assert.Equal("turn.choice-required", refused.Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));

        var ended = temp.App.Turn(Request(temp, id, choices: new Dictionary<string, TurnChoiceOption> { [IronShort] = TurnChoiceOption.End }));
        Assert.Empty(ended.Character.Play.Toggles);
        Assert.Equal(1, Round(ended));
    }

    [Fact]
    public void A_choice_for_a_prompt_not_in_the_plan_is_refused()
    {
        var (temp, id) = InEncounterWithIron(); // breath 4: nothing is short
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() =>
            temp.App.Turn(Request(temp, id, choices: new Dictionary<string, TurnChoiceOption> { [IronShort] = TurnChoiceOption.End })));

        Assert.Equal("turn.choice-unknown", refused.Problems[0].Code);
        Assert.Equal("turn.choice-unknown", refused.Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_choice_whose_prompt_vanished_after_unticking_the_spend_is_refused()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        SpendBreath(temp, id, 3); // breath 1 left
        var keep = new Dictionary<string, TurnChoiceOption> { [IronShort] = TurnChoiceOption.Keep };
        Assert.Single(temp.App.PreviewTurn(Request(temp, id, choices: keep, confirm: false)).Choices); // the prompt exists with Keep chosen
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        // The player then unticks that same spend: the plan ends the stance instead, the prompt is gone, and the old Keep is unknown.
        var refused = Assert.Throws<AppValidationException>(() => temp.App.Turn(Request(temp, id, skip: [IronUpkeep], choices: keep)));

        Assert.Equal("turn.choice-unknown", refused.Problems[0].Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    // ---- notices --------------------------------------------------------------------------------------------------

    [Fact]
    public void Keep_reports_the_short_line_as_a_notice()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        SpendBreath(temp, id, 3); // breath 1 left

        var view = temp.App.Turn(Request(temp, id, choices: new Dictionary<string, TurnChoiceOption> { [IronShort] = TurnChoiceOption.Keep }));

        Assert.Contains(view.Character.Play.Toggles, t => t.ToggleId == "iron");
        Assert.Equal(0, Left(view, "breath")); // spends what is left
        var notice = Assert.Single(view.Notices!);
        Assert.Equal("turn.settle-by-hand", notice.Code);
        Assert.Contains("Iron stance", notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_counting_up_pool_reports_its_prompt_as_a_notice()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        On(temp, id, Flare, "flare");
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, Amount: 2, ContentId: Flare, ResourceId: "strain")); // strain 2 of 3

        var view = temp.App.Turn(Request(temp, id));

        var notice = Assert.Single(view.Notices!, n => n.Code == "turn.resource-full");
        Assert.Equal("Flare ends; take a level of exhaustion", notice.Message);
    }

    // ---- undo -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_confirmed_turn_can_be_undone_exactly()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        var beforePlay = TempApp.Json(temp.App.GetCharacter(id).Character.Play);

        var turned = temp.App.Turn(Request(temp, id));
        Assert.NotEqual(beforePlay, TempApp.Json(turned.Character.Play));
        var undone = temp.App.UndoPlay(new(id, turned.Character.UpdatedAt, Confirm: true));

        Assert.Equal(beforePlay, TempApp.Json(undone.Character.Play));
        Assert.Equal(beforePlay, TempApp.Json(temp.App.GetCharacter(id).Character.Play));
    }

    [Fact]
    public void A_turn_and_its_undo_write_nothing_outside_the_play_state()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        string Outside(Character c) => TempApp.Json(c with { Play = new(), UpdatedAt = default });
        var before = Outside(temp.App.GetCharacter(id).Character);

        var turned = temp.App.Turn(Request(temp, id));
        Assert.Equal(before, Outside(turned.Character));
        var undone = temp.App.UndoPlay(new(id, turned.Character.UpdatedAt, Confirm: true));

        Assert.Equal(before, Outside(undone.Character));
    }

    // ---- the dispatcher (3.4 review) ------------------------------------------------------------------------------

    private static JsonElement Dispatch(TempApp temp, string command, object payload) =>
        JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;

    [Fact]
    public void The_dispatcher_lists_and_runs_the_turn_commands_with_a_choice()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        SpendBreath(temp, id, 3); // breath 1 left; Iron's upkeep needs 2
        Assert.Contains("character.turnPreview", CommandDispatcher.Commands);
        Assert.Contains("character.turn", CommandDispatcher.Commands);

        var preview = Dispatch(temp, "character.turnPreview", new { characterId = id, at = "turnStart" });
        Assert.True(preview.GetProperty("ok").GetBoolean(), preview.ToString());
        var stamp = preview.GetProperty("result").GetProperty("updatedAt").GetString();

        var without = Dispatch(temp, "character.turn", new { characterId = id, at = "turnStart", confirm = true, expectedUpdatedAt = stamp });
        Assert.False(without.GetProperty("ok").GetBoolean());
        Assert.Equal("turn.choice-required", without.GetProperty("error").GetProperty("code").GetString());

        var with = Dispatch(temp, "character.turn", new { characterId = id, at = "turnStart", confirm = true, expectedUpdatedAt = stamp, choices = new Dictionary<string, string> { [IronShort] = "keep" } });
        Assert.True(with.GetProperty("ok").GetBoolean(), with.ToString());
        Assert.Equal(1, Round(temp.App.GetCharacter(id)));
    }

    // ---- the order of the checks (3.4 review) ---------------------------------------------------------------------

    [Fact]
    public void A_missing_choice_and_an_unknown_one_together_report_the_unknown_one()
    {
        var (temp, id) = InEncounterWithIron();
        using var _ = temp;
        SpendBreath(temp, id, 3); // Iron's prompt exists and has no choice

        var code = Refusal(() => temp.App.Turn(Request(temp, id, choices: new Dictionary<string, TurnChoiceOption> { ["short:nobody"] = TurnChoiceOption.End })));

        Assert.Equal("turn.choice-unknown", code);
    }

    [Fact]
    public void A_stale_request_outside_an_encounter_reports_stale()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        var old = Stamp(temp, id);
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1));

        Assert.Equal("turn.preview-stale", Refusal(() => temp.App.Turn(Request(temp, id, stamp: old))));
    }

    // ---- Task 3.5: start and end the encounter --------------------------------------------------------------------

    private static CharacterView EndEncounter(TempApp temp, Guid id) =>
        temp.App.Play(new(id, PlayActionKind.EndEncounter, Confirm: true, ExpectedUpdatedAt: Stamp(temp, id)));

    private static int? RoundsLeft(CharacterView view, string toggle) => view.Character.Play.Toggles.Single(t => t.ToggleId == toggle).RoundsLeft;

    [Fact]
    public void Start_encounter_sets_round_0_and_durations_of_toggles_already_on()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        On(temp, id, Iron, "iron");
        Assert.Null(RoundsLeft(temp.App.GetCharacter(id), "iron"));

        var view = StartEncounter(temp, id);

        Assert.Equal(0, Round(view));
        Assert.Equal(3, RoundsLeft(view, "iron")); // Iron stance lasts 3 rounds
        Assert.Equal(10, view.Character.SchemaVersion);
    }

    [Fact]
    public void Start_encounter_twice_is_refused()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        StartEncounter(temp, id);
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        Assert.Equal("encounter.already-started", Refusal(() => StartEncounter(temp, id)));
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void End_encounter_returns_the_character_to_its_lower_version()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        Assert.Equal(8, temp.App.GetCharacter(id).Character.SchemaVersion);
        Assert.Contains("\"schemaVersion\": 8", TempApp.Json(temp.App.GetCharacter(id).Character));

        StartEncounter(temp, id);
        Assert.Equal(10, temp.App.GetCharacter(id).Character.SchemaVersion);
        On(temp, id, Iron, "iron");
        Assert.Equal(3, RoundsLeft(temp.App.GetCharacter(id), "iron"));

        var ended = EndEncounter(temp, id);

        Assert.Null(ended.Character.Play.Encounter);
        Assert.Null(RoundsLeft(ended, "iron"));
        Assert.Equal(8, ended.Character.SchemaVersion);
        Assert.Equal(8, temp.App.GetCharacter(id).Character.SchemaVersion);
        Assert.Contains("\"schemaVersion\": 8", TempApp.Json(temp.App.GetCharacter(id).Character));
        Assert.Equal("end encounter", ended.UndoLabel);
    }

    [Fact]
    public void End_encounter_without_one_or_without_the_stamp_is_refused()
    {
        var (temp, id) = Turns();
        using var _ = temp;

        Assert.Equal("encounter.not-started", Refusal(() => EndEncounter(temp, id)));
        StartEncounter(temp, id);
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);
        var refused = Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.EndEncounter, Confirm: true)));

        Assert.Equal("play.basis-required", refused.Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void End_encounter_names_the_toggles_that_lost_a_duration()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        StartEncounter(temp, id);
        On(temp, id, Iron, "iron");
        On(temp, id, Flare, "flare"); // no duration: nothing to lose

        var ended = EndEncounter(temp, id);

        var notice = Assert.Single(ended.Notices!);
        Assert.Equal("encounter.duration-by-hand", notice.Code);
        Assert.Contains("Iron stance", notice.Message, StringComparison.Ordinal);
        Assert.Contains("duration: track by hand", notice.Message, StringComparison.Ordinal);
        Assert.Null(notice.Content);
        Assert.Null(notice.EffectId);
        Assert.Null(temp.App.GetCharacter(id).Notices);
    }

    [Fact]
    public void End_encounter_is_undone_exactly()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        StartEncounter(temp, id);
        On(temp, id, Iron, "iron");
        temp.App.Turn(Request(temp, id)); // round 1, iron down to 2 rounds
        var beforePlay = TempApp.Json(temp.App.GetCharacter(id).Character.Play);
        string Outside(Character c) => TempApp.Json(c with { Play = new(), UpdatedAt = default });
        var beforeOutside = Outside(temp.App.GetCharacter(id).Character);

        var ended = EndEncounter(temp, id);
        Assert.NotEqual(beforePlay, TempApp.Json(ended.Character.Play));
        Assert.Equal(beforeOutside, Outside(ended.Character));
        var undone = temp.App.UndoPlay(new(id, ended.Character.UpdatedAt, Confirm: true));

        Assert.Equal(beforePlay, TempApp.Json(undone.Character.Play));
        Assert.Equal(beforeOutside, Outside(undone.Character));
    }

    [Fact]
    public void A_toggle_turned_on_in_an_encounter_gets_its_duration()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        StartEncounter(temp, id);

        var view = On(temp, id, Iron, "iron");

        Assert.Equal(3, RoundsLeft(view, "iron"));
        Assert.Equal(10, view.Character.SchemaVersion);
    }

    [Fact]
    public void A_toggle_turned_on_outside_an_encounter_has_no_rounds_left()
    {
        var (temp, id) = Turns();
        using var _ = temp;

        var view = On(temp, id, Iron, "iron");

        Assert.Null(RoundsLeft(view, "iron"));
        Assert.Equal(8, view.Character.SchemaVersion);
        Assert.Equal(3, view.Sheet.Toggles!.Single(t => t.ToggleId == "iron").Duration); // the sheet shows a duration with no count: track by hand
    }

    [Fact]
    public void Spending_a_counting_up_pool_to_full_returns_its_prompt()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        On(temp, id, Iron, "iron"); // Flare is an overlay of Iron stance
        On(temp, id, Flare, "flare");
        Strain(temp, id, 2);
        Assert.Null(temp.App.GetCharacter(id).Notices);

        var view = Strain(temp, id, 1); // 3 of 3

        var notice = Assert.Single(view.Notices!);
        Assert.Equal("resource.full", notice.Code);
        Assert.Equal("Flare ends; take a level of exhaustion", notice.Message);
        Assert.Null(temp.App.GetCharacter(id).Notices);
    }

    [Fact]
    public void Spending_a_counting_up_pool_short_of_full_has_no_notice()
    {
        var (temp, id) = Turns();
        using var _ = temp;

        Assert.Null(Strain(temp, id, 2).Notices);
        Assert.Null(SpendBreath(temp, id, 7).Notices); // an ordinary pool never prompts
    }

    private static CharacterView Strain(TempApp temp, Guid id, int amount) =>
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, Amount: amount, ContentId: Flare, ResourceId: "strain"));

    [Fact]
    public void Long_rest_proposes_ending_the_encounter()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        StartEncounter(temp, id);
        On(temp, id, Iron, "iron");

        var preview = temp.App.PreviewRest(id, RestPeriod.LongRest);
        var change = Assert.Single(preview.Changes, c => c.Kind == RestChangeKind.Encounter);
        Assert.Equal(("encounter", "End the encounter"), (change.Id, change.Label));

        // Keep the stance on, so its rounds left are what the encounter's end clears.
        var view = temp.App.Rest(new(id, Confirm: true, Basis: preview.Basis, Skip: [$"toggle:{Iron:D}:iron"]));

        Assert.Null(view.Character.Play.Encounter);
        Assert.Contains(view.Character.Play.Toggles, t => t.ToggleId == "iron");
        Assert.Null(RoundsLeft(view, "iron"));
        Assert.Equal(8, view.Character.SchemaVersion);
        Assert.DoesNotContain(temp.App.PreviewRest(id, RestPeriod.LongRest).Changes, c => c.Kind == RestChangeKind.Encounter);
    }

    [Fact]
    public void Long_rest_outside_an_encounter_does_not_propose_it()
    {
        var (temp, id) = Turns();
        using var _ = temp;

        Assert.DoesNotContain(temp.App.PreviewRest(id, RestPeriod.LongRest).Changes, c => c.Kind == RestChangeKind.Encounter);
    }

    // ---- the slice's acceptance line ------------------------------------------------------------------------------

    /// <summary>
    /// "A cost 3 / upkeep 2 form on a pool of 7: on, then three turns (5, 3, 1)". From a full 7 the form's cost of 3 leaves 4, so the
    /// numbers fit a pool that is at 7 after the form is on. All by commands: the encounter starts, Iron goes on (breath 4 left),
    /// the player regains the 3 (breath 7). The pack has Iron without its 3-round duration (a stance until switched off), or the
    /// third turn would end it. Three turn starts give 5, 3 and 1.
    /// </summary>
    private static (TempApp Temp, Guid Id) AfterThreeTurnsWithBreathAt1()
    {
        var (temp, id) = Turns(ironDuration: false);
        StartEncounter(temp, id);
        On(temp, id, Iron, "iron");
        Assert.Equal(4, Left(temp.App.GetCharacter(id), "breath"));
        temp.App.Play(new(id, PlayActionKind.Regain, Confirm: true, Amount: 3, ContentId: Breath, ResourceId: "breath"));
        Assert.Equal(7, Left(temp.App.GetCharacter(id), "breath"));
        Assert.Null(RoundsLeft(temp.App.GetCharacter(id), "iron"));
        foreach (var expected in new[] { 5, 3, 1 })
            Assert.Equal(expected, Left(temp.App.Turn(Request(temp, id)), "breath"));
        return (temp, id);
    }

    [Fact]
    public void Acceptance_cost_3_upkeep_2_form_on_a_pool_of_7()
    {
        // End: "Iron stance needs 2, has 1"; ending it ends its overlay (Flare, switched on just before) and leaves the breath at 1.
        var (temp, id) = AfterThreeTurnsWithBreathAt1();
        using var _ = temp;
        On(temp, id, Flare, "flare");
        var asking = temp.App.PreviewTurn(Request(temp, id, confirm: false));
        var prompt = Assert.Single(asking.Choices);
        Assert.Equal((IronShort, "Iron stance needs 2, has 1", 2, 1), (prompt.Id, prompt.Label, prompt.Needs, prompt.Has));

        var ended = temp.App.Turn(Request(temp, id, choices: new Dictionary<string, TurnChoiceOption> { [IronShort] = TurnChoiceOption.End }));

        Assert.Empty(ended.Character.Play.Toggles); // Iron and its overlay
        Assert.Equal(1, Left(ended, "breath"));
        Assert.Equal(4, Round(ended));

        // Keep: the stance stays, the breath that is left is spent, and the rest is a line to settle by hand.
        var (kept, keptId) = AfterThreeTurnsWithBreathAt1();
        using var __ = kept;
        var keep = kept.App.Turn(Request(kept, keptId, choices: new Dictionary<string, TurnChoiceOption> { [IronShort] = TurnChoiceOption.Keep }));

        Assert.Equal(0, Left(keep, "breath"));
        Assert.Contains(keep.Character.Play.Toggles, t => t.ToggleId == "iron");
        var notice = Assert.Single(keep.Notices!);
        Assert.Equal("turn.settle-by-hand", notice.Code);
        Assert.EndsWith("short 1 Breath, settle by hand", notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_real_three_round_stance_ends_on_its_third_turn_before_any_upkeep()
    {
        var (temp, id) = Turns();
        using var _ = temp;
        StartEncounter(temp, id);
        On(temp, id, Iron, "iron");
        temp.App.Play(new(id, PlayActionKind.Regain, Confirm: true, Amount: 3, ContentId: Breath, ResourceId: "breath"));
        Assert.Equal(7, Left(temp.App.GetCharacter(id), "breath"));

        var left = new List<int>();
        CharacterView view = temp.App.GetCharacter(id);
        for (var turn = 0; turn < 3; turn++)
        {
            view = temp.App.Turn(Request(temp, id));
            left.Add(Left(view, "breath"));
        }

        Assert.Equal([5, 3, 3], left); // the third turn spends nothing: the duration ran out first
        Assert.Empty(view.Character.Play.Toggles);
        Assert.Equal(3, Round(view));
    }
}
