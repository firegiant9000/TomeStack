using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D43 slice 7 (Task 7.5): <c>character.damagePreview</c> and damage with responses on the responses pack and the Fixture Bulwark
/// (grit 5; brace, stand, endure with the surge form, rally, rebound; the surge form raises the hit point maximum from 68 to 76).
/// The one concurrency token is <c>Character.UpdatedAt</c>: the preview returns it and the confirm sends it back.
/// </summary>
public class DamageResponseServiceTests
{
    private static readonly Guid Class = Guid.Parse("6f0dc000-0000-4000-8000-000000000070");
    private static readonly Guid Brace = Guid.Parse("6f0dc000-0000-4000-8000-000000000071");
    private static readonly Guid Stand = Guid.Parse("6f0dc000-0000-4000-8000-000000000072");
    private static readonly Guid Endure = Guid.Parse("6f0dc000-0000-4000-8000-000000000073");
    private static readonly Guid Surge = Guid.Parse("6f0dc000-0000-4000-8000-000000000074");
    private static readonly Guid Rally = Guid.Parse("6f0dc000-0000-4000-8000-000000000075");
    private static readonly Guid Rebound = Guid.Parse("6f0dc000-0000-4000-8000-000000000076");

    private static ResponseChoice Choice(Guid content, string id) => new(content, id);

    /// <summary>The Bulwark with the given hit points (null: full), with the surge form on when asked; stamps move on every write.</summary>
    private static (TempApp Temp, Guid Id) Bulwark(int? hp = null, bool surge = false)
    {
        var temp = new TempApp(time: new TickingTime(TempApp.Now));
        temp.AddPack("fixture-pack-responses.json");
        var character = TempApp.LoadFixture<Character>("characters/pm-bulwark-l8.json");
        character = character with
        {
            Play = character.Play with
            {
                CurrentHitPoints = hp,
                Toggles = surge ? [new ActiveToggle(Surge, "surge")] : [],
                Concentration = new Concentration(new ContentReference(Class, Guid.Parse("6f0de000-0000-4000-8000-000000000070")), "Held"),
            },
        };
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    private static DateTimeOffset Stamp(TempApp temp, Guid id) => temp.App.GetCharacter(id).Character.UpdatedAt;

    private static string Stored(TempApp temp, Guid id) => TempApp.Json(temp.App.Store.FindCharacter(id));

    private static string StoredPlay(TempApp temp, Guid id) => TempApp.Json(temp.App.Store.FindCharacter(id)!.Play);

    private static int Left(CharacterView view, string resource) => view.Sheet.Resources!.Single(r => r.ResourceId == resource).Current!.Value;

    private static AppValidationException Refused(Action act) => Assert.Throws<AppValidationException>(act);

    private static CharacterView Confirm(TempApp temp, Guid id, int amount, string? type, DateTimeOffset stamp, params ResponseChoice[] chosen) =>
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: amount, DamageType: type, ExpectedUpdatedAt: stamp, Responses: chosen));

    private static DamagePreview Preview(TempApp temp, Guid id, int amount, string? type, params ResponseChoice[] chosen) =>
        temp.App.PreviewDamage(new(id, amount, type, chosen));

    [Fact]
    public void The_damage_preview_lists_offers_and_writes_nothing()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;
        var before = Stored(temp, id);

        var preview = Preview(temp, id, 15, null);

        Assert.Equal(before, Stored(temp, id));
        Assert.Equal(Stamp(temp, id), preview.UpdatedAt);
        Assert.Equal((15, 15, 0), (preview.Amount, preview.Outcome.Adjusted, preview.After.Current));
        Assert.Equal(0, preview.Reduction);
        Assert.Contains(preview.Offers, o => o.Response.ResponseId == "stand" && o.Offered);
        Assert.Contains(preview.Offers, o => o.Response.ResponseId == "brace" && o.Offered);
        Assert.True(preview.ConcentrationEnds);
        Assert.Empty(preview.Notes);
    }

    [Fact]
    public void A_preview_shows_what_a_chosen_survival_would_write()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;

        var preview = Preview(temp, id, 25, "FIRE", Choice(Stand, "stand")); // halved by the fire resistance: 12, enough to drop 10 hit points

        Assert.Equal("fire", preview.Type);
        Assert.Equal(1, preview.After.Current);
        Assert.False(preview.ConcentrationEnds);
        Assert.NotNull(preview.ConcentrationDc);
    }

    [Fact]
    public void Damage_with_responses_needs_the_stamp()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;
        var old = Stamp(temp, id);
        temp.App.Play(new(id, PlayActionKind.Heal, Confirm: true, Amount: 1)); // moves the stamp
        var before = Stored(temp, id);

        var missing = Refused(() => temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 15, Responses: [Choice(Stand, "stand")])));
        var stale = Refused(() => Confirm(temp, id, 15, null, old, Choice(Stand, "stand")));

        Assert.Equal("play.basis-required", missing.Code);
        Assert.Equal("character.stale", stale.Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void A_confirm_that_differs_from_its_preview_is_refused_whole()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;
        var preview = Preview(temp, id, 15, null, Choice(Stand, "stand"));
        var before = Stored(temp, id);

        var refused = Refused(() => Confirm(temp, id, 5, null, preview.UpdatedAt, Choice(Stand, "stand")));

        Assert.Equal("damage.response-unavailable", refused.Code);
        Assert.Equal("damage.response-unavailable", refused.Problems[0].Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void Damage_with_a_survival_is_one_undoable_change()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;
        var play = StoredPlay(temp, id);

        var view = Confirm(temp, id, 15, null, Stamp(temp, id), Choice(Stand, "stand"));

        Assert.Equal(1, view.Sheet.HitPoints!.Current);
        Assert.Equal(0, Left(view, "stand"));
        Assert.Equal("damage 15 (15 taken)", view.UndoLabel);
        Assert.Equal(15, view.Damage!.Adjusted);
        Assert.NotNull(view.Character.Play.Concentration);

        var undone = temp.App.UndoPlay(new(id, view.Character.UpdatedAt, true));

        Assert.Equal(play, StoredPlay(temp, id));
        Assert.Equal(10, undone.Sheet.HitPoints!.Current);
        Assert.Equal(1, Left(undone, "stand"));
    }

    [Fact]
    public void A_typed_damage_label_names_the_type()
    {
        var (temp, id) = Bulwark(hp: 30);
        using var _ = temp;

        var view = Confirm(temp, id, 20, "Fire", Stamp(temp, id), Choice(Brace, "brace"));

        Assert.Equal("damage 20 fire (7 taken)", view.UndoLabel); // 20 - 5 (brace: 3 + CON.MOD) = 15, halved by the fire resistance = 7
    }

    [Fact]
    public void A_double_sent_damage_is_applied_once()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;
        var stamp = Stamp(temp, id);
        Confirm(temp, id, 15, null, stamp, Choice(Stand, "stand"));
        var after = Stored(temp, id);

        var second = Refused(() => Confirm(temp, id, 15, null, stamp, Choice(Stand, "stand")));

        Assert.Equal("character.stale", second.Code);
        Assert.Equal(after, Stored(temp, id));
        Assert.Equal(1, temp.App.GetCharacter(id).Sheet.HitPoints!.Current);
    }

    [Fact]
    public void Plain_damage_names_the_responses_it_skipped()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;

        var view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 15));

        Assert.Equal(0, view.Sheet.HitPoints!.Current); // applied exactly as plain damage always was
        Assert.Null(view.UndoLabel);
        var skipped = view.Notices!.Where(n => n.Code == "damage.responses-skipped").ToList();
        Assert.Contains(skipped, n => n.EffectId == "stand" && n.Content!.ContentId == Stand);
        Assert.Contains(skipped, n => n.EffectId == "brace" && n.Content!.ContentId == Brace);
        Assert.DoesNotContain(skipped, n => n.EffectId == "rebound"); // not eligible: nothing was survived
        Assert.All(skipped, n => Assert.DoesNotContain("Last stand", n.Message));
    }

    [Fact]
    public void Plain_damage_on_a_character_without_responses_has_no_notice()
    {
        using var temp = new TempApp(time: new TickingTime(TempApp.Now));
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        var id = temp.App.SaveCharacter(brenna).Character.Id;

        var view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 3));

        Assert.Null(view.Notices);
    }

    [Fact]
    public void An_offered_roll_comes_back_as_a_notice()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;

        var view = Confirm(temp, id, 15, null, Stamp(temp, id), Choice(Stand, "stand"), Choice(Rebound, "rebound"));

        var roll = Assert.Single(view.Notices!, n => n.Code == "damage.roll-offered");
        Assert.Equal(Rebound, roll.Content!.ContentId);
        Assert.Equal("counter", roll.EffectId);
        Assert.DoesNotContain("Counter", roll.Message);
    }

    [Fact]
    public void Damage_responses_survive_reopen_and_older_characters_stay_at_their_version()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;
        var version = temp.App.GetCharacter(id).Character.SchemaVersion;

        var view = Confirm(temp, id, 15, null, Stamp(temp, id), Choice(Stand, "stand"));

        Assert.Equal(version, view.Character.SchemaVersion);
        Assert.Equal(version, temp.App.GetCharacter(id).Character.SchemaVersion);
        Assert.Equal(1, temp.App.GetCharacter(id).Sheet.HitPoints!.Current);

        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        var brennaId = temp.App.SaveCharacter(brenna).Character.Id;
        var brennaVersion = temp.App.GetCharacter(brennaId).Character.SchemaVersion;
        var hit = temp.App.Play(new(brennaId, PlayActionKind.Damage, Confirm: true, Amount: 2));
        Assert.Equal(brennaVersion, hit.Character.SchemaVersion);
    }

    // The B8a acceptance, verbatim: "Lethal damage with two survival features and a threshold boost: one survival chosen, HP 1, the boost
    // and a counter roll offered; undo restores everything exactly".
    [Fact]
    public void Lethal_damage_with_two_survivals_and_a_threshold_boost()
    {
        var (temp, id) = Bulwark(hp: 10, surge: true); // surge on: the maximum is 76, and stand and endure are both available
        using var _ = temp;
        var play = StoredPlay(temp, id);

        var preview = Preview(temp, id, 15, null, Choice(Stand, "stand"));

        Assert.Empty(preview.Notes);
        Assert.Equal(1, preview.After.Current);
        Assert.Equal(76, preview.After.Maximum);
        foreach (var offered in new[] { "stand", "endure", "rally", "rebound" }) // survivals are radio-style: both are offered
            Assert.Contains(preview.Offers, o => o.Response.ResponseId == offered && o.Offered);
        Assert.Empty(preview.Rolls);
        var withBoth = Preview(temp, id, 15, null, Choice(Stand, "stand"), Choice(Rally, "rally"), Choice(Rebound, "rebound"));
        Assert.Equal("counter", Assert.Single(withBoth.Rolls).EffectId);

        var view = Confirm(temp, id, 15, null, preview.UpdatedAt, Choice(Stand, "stand"), Choice(Rally, "rally"), Choice(Rebound, "rebound"));

        Assert.Equal(1, view.Sheet.HitPoints!.Current);
        Assert.Equal(0, Left(view, "stand"));
        Assert.Equal(0, Left(view, "rally"));
        Assert.Equal(5, Left(view, "grit")); // rally gives back 1, clamped at the maximum
        Assert.Contains(view.Character.Play.Toggles, t => t.ContentId == Surge && t.ToggleId == "surge");
        Assert.Contains(view.Notices!, n => n.Code == "damage.roll-offered" && n.EffectId == "counter");

        temp.App.UndoPlay(new(id, view.Character.UpdatedAt, true));

        Assert.Equal(play, StoredPlay(temp, id));
    }

    [Fact]
    public void Two_survivals_are_refused_as_one_outcome()
    {
        var (temp, id) = Bulwark(hp: 10, surge: true);
        using var _ = temp;
        var before = Stored(temp, id);

        var refused = Refused(() => Confirm(temp, id, 15, null, Stamp(temp, id), Choice(Stand, "stand"), Choice(Endure, "endure")));

        Assert.Equal("damage.one-outcome", refused.Code);
        Assert.Contains(refused.Problems, p => p.Code == "damage.one-outcome");
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void More_than_the_sheet_bound_of_choices_is_refused_before_planning()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;
        var many = Enumerable.Repeat(Choice(Stand, "stand"), CharacterCalculator.MaxResponses + 1).ToArray();

        Assert.Equal("damage.response-unavailable", Refused(() => Confirm(temp, id, 15, null, Stamp(temp, id), many)).Code);
        Assert.Equal("damage.response-unavailable", Refused(() => Preview(temp, id, 15, null, many)).Code);
    }

    [Fact]
    public void A_preview_reports_notes_instead_of_throwing()
    {
        var (temp, id) = Bulwark(hp: 10, surge: true);
        using var _ = temp;

        var preview = Preview(temp, id, 15, null, Choice(Stand, "stand"), Choice(Endure, "endure"));

        Assert.Contains(preview.Notes, n => n.Code == "damage.one-outcome");
        Assert.Empty(preview.Rolls);
    }

    [Fact]
    public void The_preview_refuses_a_missing_character_a_bad_amount_and_an_unknown_type()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;

        Assert.Equal("character.not-found", Refused(() => temp.App.PreviewDamage(new(Guid.NewGuid(), 1, null))).Problems[0].Code);
        Assert.Equal("play.amount-out-of-range", Refused(() => temp.App.PreviewDamage(new(id, PlayState.MaxHitPoints + 1, null))).Problems[0].Code);
        Assert.Equal("play.damage-type-unknown", Refused(() => temp.App.PreviewDamage(new(id, 1, "banana"))).Problems[0].Code);
    }

    [Fact]
    public void The_damage_preview_is_a_registered_command()
    {
        var (temp, id) = Bulwark(hp: 10);
        using var _ = temp;
        Assert.Contains("character.damagePreview", CommandDispatcher.Commands);
        var dispatcher = new CommandDispatcher(temp.App);

        var reply = dispatcher.Dispatch("{\"id\":\"1\",\"command\":\"character.damagePreview\",\"payload\":{\"characterId\":\"" + id + "\",\"amount\":15}}");

        using var json = JsonDocument.Parse(reply);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean(), reply);
        Assert.Equal(15, json.RootElement.GetProperty("result").GetProperty("amount").GetInt32());
    }
}
