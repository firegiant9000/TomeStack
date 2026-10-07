using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2 item 2, SPEC C-05: play state changes only through the confirmed <c>character.play</c> command. Uses the M1
/// acceptance character Brenna (SRD 5.2.1 Barbarian 3: Rage 3 uses, Stonecunning PB uses, 35 hit points).
/// </summary>
public class PlayCommandTests
{
    private static readonly Guid RageContent = Guid.Parse("52c00000-0000-4000-8000-000000000012");
    private static readonly Guid DwarfContent = Guid.Parse("52c00000-0000-4000-8000-000000000001");

    private static (TempApp Temp, Guid Id) Brenna()
    {
        var temp = new TempApp();
        var id = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json")).Character.Id;
        return (temp, id);
    }

    private static JsonElement Dispatch(TempApp temp, object payload) =>
        JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(JsonSerializer.Serialize(new { id = "1", command = "character.play", payload }, RulesJson.Compact))).RootElement;

    private static string Code(JsonElement response) =>
        response.GetProperty("error").GetProperty("diagnostics").EnumerateArray().First().GetProperty("code").GetString()!;

    // The M2 spell pack (seeded by TempApp): Fixture Arcanist (1), Fixture Spark (11, a cantrip), Fixture Veil (13, concentration).
    private static ContentReference SpellRef(int n) => new(Guid.Parse($"5f5dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"5f5de000-0000-4000-8000-{n:D12}"));

    private static (TempApp Temp, Guid Id, ContentReference Veil) Caster(string family = RulesFamilies.Srd521)
    {
        var temp = new TempApp();
        var arcanist = SpellRef(1);
        var veil = SpellRef(13);
        var character = new Character
        {
            Id = Guid.NewGuid(),
            Name = "Test Arcanist",
            RulesFamily = family,
            Level = 3,
            Classes = [new(arcanist, 3)],
            BaseAbilities = new(8, 14, 12, 16, 10, 10),
            Spells = [new(arcanist.ContentId, veil), new(arcanist.ContentId, SpellRef(11))],
        };
        return (temp, temp.App.SaveCharacter(character).Character.Id, veil);
    }

    [Fact]
    public void Concentration_starts_on_a_prepared_concentration_spell_and_damage_sets_the_save_dc()
    {
        var (temp, id, veil) = Caster();
        using var _ = temp;

        var view = temp.App.Play(new(id, PlayActionKind.StartConcentration, Confirm: true, ContentId: veil.ContentId));
        Assert.Equal(("Fixture Veil", (int?)null), (view.Character.Play.Concentration!.Name, view.Character.Play.Concentration.PendingSaveDc));

        temp.App.Play(new(id, PlayActionKind.SetTemporaryHitPoints, Confirm: true, Amount: 100)); // keeps the caster above 0; the DC uses the damage dealt
        view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 7));
        Assert.Equal(10, view.Character.Play.Concentration!.PendingSaveDc); // max(10, floor(7 / 2))
        view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 30));
        Assert.Equal(15, view.Character.Play.Concentration!.PendingSaveDc);

        view = temp.App.Play(new(id, PlayActionKind.ClearConcentrationCheck, Confirm: true));
        Assert.Null(view.Character.Play.Concentration!.PendingSaveDc);
        Assert.Equal("Fixture Veil", view.Character.Play.Concentration.Name);

        view = temp.App.Play(new(id, PlayActionKind.EndConcentration, Confirm: true));
        Assert.Null(view.Character.Play.Concentration);
        Assert.Equal("play.not-concentrating", Code(Dispatch(temp, new { characterId = id, action = "clearConcentrationCheck", confirm = true })));
    }

    [Fact]
    public void Damage_to_zero_hit_points_ends_concentration_and_temporary_hit_points_do_not_change_the_dc()
    {
        var (temp, id, veil) = Caster();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.StartConcentration, Confirm: true, ContentId: veil.ContentId));
        temp.App.Play(new(id, PlayActionKind.SetTemporaryHitPoints, Confirm: true, Amount: 5));

        var view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 24)); // 5 absorbed; the DC uses the 24 dealt
        Assert.Equal(12, view.Character.Play.Concentration!.PendingSaveDc);

        view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 500));
        Assert.Equal(0, view.Sheet.HitPoints!.Current);
        Assert.Null(view.Character.Play.Concentration); // SRD: concentration ends at 0 hit points
    }

    [Fact]
    public void Concentration_is_refused_for_an_unknown_or_non_concentration_spell()
    {
        var (temp, id, _) = Caster();
        using var _ = temp;
        Assert.Equal("play.spell-not-found", Code(Dispatch(temp, new { characterId = id, action = "startConcentration", contentId = Guid.NewGuid(), confirm = true })));
        // Fixture Spark (SpellRef(11)) is a cantrip without concentration in the M2 spell pack.
        Assert.Equal("play.spell-not-concentration", Code(Dispatch(temp, new { characterId = id, action = "startConcentration", contentId = SpellRef(11).ContentId, confirm = true })));
    }

    [Fact]
    public void The_save_dc_is_capped_at_30_under_srd_521_and_only_at_the_validation_bound_under_srd_51()
    {
        // R11: SRD 5.2.1 ("up to a maximum DC of 30") caps it through the family policy; SRD 5.1 has no ceiling, and
        // either way the DC stays within what PlayState validation accepts (100) so a huge hit is still applied.
        Assert.Equal(30, RulesFamilies.Get(RulesFamilies.Srd521).ConcentrationSaveMaximumDc);
        Assert.Null(RulesFamilies.Get(RulesFamilies.Srd51).ConcentrationSaveMaximumDc);

        foreach (var (family, damage, expected) in new[] { (RulesFamilies.Srd521, 80, 30), (RulesFamilies.Srd51, 250, 100) })
        {
            var (temp, id, veil) = Caster(family);
            using var _ = temp;
            temp.App.Play(new(id, PlayActionKind.StartConcentration, Confirm: true, ContentId: veil.ContentId));
            temp.App.Play(new(id, PlayActionKind.SetTemporaryHitPoints, Confirm: true, Amount: 1000)); // keeps the caster above 0

            var view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: damage));

            Assert.Equal(expected, view.Character.Play.Concentration!.PendingSaveDc);
            Assert.Equal(1000 - damage, view.Character.Play.TemporaryHitPoints);
        }
    }

    [Fact]
    public void Death_saves_follow_the_srd_and_regaining_hit_points_resets_them()
    {
        var (temp, id) = Brenna();
        using var _ = temp;

        Assert.Equal("play.not-dying", Code(Dispatch(temp, new { characterId = id, action = "recordDeathSave", amount = 12, confirm = true })));
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 40));
        Assert.Equal("play.confirmation-required", Code(Dispatch(temp, new { characterId = id, action = "recordDeathSave", amount = 12 })));
        Assert.Equal("play.amount-out-of-range", Code(Dispatch(temp, new { characterId = id, action = "recordDeathSave", amount = 21, confirm = true })));

        temp.App.Play(new(id, PlayActionKind.RecordDeathSave, Confirm: true, Amount: 12)); // success
        var view = temp.App.Play(new(id, PlayActionKind.RecordDeathSave, Confirm: true, Amount: 1)); // natural 1: two failures
        Assert.Equal(new DeathSaves(1, 2), view.Character.Play.DeathSaves);

        view = temp.App.Play(new(id, PlayActionKind.RecordDeathSave, Confirm: true, Amount: 20)); // natural 20: 1 hit point
        Assert.Equal((1, new DeathSaves()), (view.Sheet.HitPoints!.Current, view.Character.Play.DeathSaves));

        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 5));
        temp.App.Play(new(id, PlayActionKind.AddDeathSaveFailure, Confirm: true, Amount: 2)); // a critical hit at 0
        view = temp.App.Play(new(id, PlayActionKind.Heal, Confirm: true, Amount: 4));
        Assert.Equal((4, new DeathSaves()), (view.Sheet.HitPoints!.Current, view.Character.Play.DeathSaves));
    }

    [Fact]
    public void Inspiration_is_a_confirmed_toggle()
    {
        var (temp, id) = Brenna();
        using var _ = temp;

        Assert.True(temp.App.Play(new(id, PlayActionKind.SetInspiration, Confirm: true, Amount: 1)).Character.Play.Inspiration);
        Assert.Equal("play.amount-out-of-range", Code(Dispatch(temp, new { characterId = id, action = "setInspiration", amount = 2, confirm = true })));
        Assert.False(temp.App.Play(new(id, PlayActionKind.SetInspiration, Confirm: true, Amount: 0)).Character.Play.Inspiration);
    }

    [Fact]
    public void A_hit_die_roll_rolls_only_a_die_the_character_has_and_changes_nothing()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var record = temp.App.Roll(new(id, HitDie: 12));
        var save = temp.App.Roll(new(id, DeathSave: true));

        Assert.Equal(("1d12", "Hit die (d12)"), (record.Formula, record.Provenance!.Label));
        Assert.InRange(record.Total, 1, 12);
        Assert.Equal("1d20", save.Formula);
        Assert.Equal("rest.hit-die-unknown", Assert.Throws<AppValidationException>(() => temp.App.Roll(new(id, HitDie: 8))).Problems[0].Code);
        Assert.Equal("roll.ambiguous", Assert.Throws<AppValidationException>(() => temp.App.Roll(new(id, HitDie: 12, DeathSave: true))).Problems[0].Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void The_sheet_shows_SRD_resources_with_calculated_maximums_and_their_recoveries()
    {
        var (temp, id) = Brenna();
        using var _ = temp;

        var sheet = temp.App.GetCharacter(id).Sheet;

        var rage = Assert.Single(sheet.Resources!, r => r.ResourceId == "rage");
        Assert.Equal(3, rage.Maximum); // min(2 + floor(3 / 3), 4)
        Assert.Contains(rage.Trace.Single().Inputs!, i => i.Name == "CLASS_LEVEL" && i.Value == 3);
        Assert.Equal([RestPeriod.ShortRest, RestPeriod.LongRest], rage.Recoveries.Select(r => r.On));
        var stonecunning = Assert.Single(sheet.Resources!, r => r.ResourceId == "stonecunning");
        Assert.Equal((2, AutomationStatus.Assisted), (stonecunning.Maximum, stonecunning.Automation));
        Assert.Equal(new HitPointState(35, 35, 0), sheet.HitPoints);
        Assert.Contains(sheet.Features!, f => f.Name == "Frenzy" && f.Effects.Any(e => e.Type == "roll" && e.Dice == "2d6"));
    }

    [Fact]
    public void Nothing_changes_without_confirmation()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var response = Dispatch(temp, new { characterId = id, action = "spend", contentId = RageContent, resourceId = "rage" });

        Assert.Equal("play.confirmation-required", Code(response));
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void Saving_the_character_never_changes_its_play_state()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        var stale = temp.App.GetCharacter(id).Character; // for example, the copy an open screen holds
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 10));
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, ContentId: RageContent, ResourceId: "rage"));

        // A save for another reason (a name, an override, equipment) with a stale or edited play state keeps the stored one.
        var saved = temp.App.SaveCharacter(stale with { Name = "Brenna Stonefist", Play = new PlayState { CurrentHitPoints = 1, Exhaustion = 6 } });

        Assert.Equal("Brenna Stonefist", saved.Character.Name);
        Assert.Equal((25, 0), (saved.Character.Play.CurrentHitPoints, saved.Character.Play.Exhaustion));
        Assert.Equal(2, saved.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current);
        Assert.Equal(TempApp.Json(saved.Character.Play), TempApp.Json(temp.App.GetCharacter(id).Character.Play));
    }

    [Fact]
    public void Spending_and_regaining_uses_is_bounded_by_the_calculated_maximum()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        PlayCommand Rage(PlayActionKind action, int amount = 1) => new(id, action, Confirm: true, Amount: amount, ContentId: RageContent, ResourceId: "rage");

        var view = temp.App.Play(Rage(PlayActionKind.Spend, 2));
        Assert.Equal(1, view.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current);
        Assert.Equal("resource.insufficient", Assert.Throws<AppValidationException>(() => temp.App.Play(Rage(PlayActionKind.Spend, 2))).Problems[0].Code);

        view = temp.App.Play(Rage(PlayActionKind.Regain, 5));
        Assert.Equal(3, view.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current);
        Assert.Empty(view.Character.Play.Resources);
        Assert.Equal("resource.nothing-spent", Assert.Throws<AppValidationException>(() => temp.App.Play(Rage(PlayActionKind.Regain))).Problems[0].Code);
        Assert.Equal("resource.not-found", Assert.Throws<AppValidationException>(() => temp.App.Play(Rage(PlayActionKind.Spend) with { ContentId = DwarfContent })).Problems[0].Code);
    }

    [Fact]
    public void Damage_uses_temporary_hit_points_first_and_healing_stops_at_the_maximum()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        PlayCommand Hp(PlayActionKind action, int amount) => new(id, action, Confirm: true, Amount: amount);

        temp.App.Play(Hp(PlayActionKind.SetTemporaryHitPoints, 5));
        var view = temp.App.Play(Hp(PlayActionKind.Damage, 12));
        Assert.Equal(new HitPointState(35, 28, 0), view.Sheet.HitPoints);

        view = temp.App.Play(Hp(PlayActionKind.Damage, 100));
        Assert.Equal(0, view.Sheet.HitPoints!.Current);

        view = temp.App.Play(Hp(PlayActionKind.Heal, 100));
        Assert.Equal(35, view.Sheet.HitPoints!.Current);
        Assert.Null(view.Character.Play.CurrentHitPoints); // at the maximum, so it follows a later level-up

        Assert.Equal("play.hit-points-above-maximum", Assert.Throws<AppValidationException>(() => temp.App.Play(Hp(PlayActionKind.SetHitPoints, 36))).Problems[0].Code);
    }

    [Fact]
    public void Conditions_and_exhaustion_are_stored_and_survive_reopening_and_a_package_round_trip()
    {
        var (temp, id) = Brenna();
        using var _ = temp;

        temp.App.Play(new(id, PlayActionKind.AddCondition, Confirm: true, Condition: "poisoned"));
        temp.App.Play(new(id, PlayActionKind.SetExhaustion, Confirm: true, Amount: 2));
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, ContentId: RageContent, ResourceId: "rage"));
        temp.App.Play(new(id, PlayActionKind.SetInspiration, Confirm: true, Amount: 1));
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 10));
        var shortRest = temp.App.PreviewRest(id, RestPeriod.ShortRest, [new(12, 4)]);
        temp.App.Rest(new(id, RestPeriod.ShortRest, Confirm: true, Basis: shortRest.Basis, HitDice: [new(12, 4)]));
        Assert.Equal("play.condition-unknown", Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.AddCondition, Confirm: true, Condition: "sleepy"))).Problems[0].Code);
        temp.Reopen();

        var stored = temp.App.GetCharacter(id);
        Assert.Equal(["poisoned"], stored.Character.Play.Conditions);
        Assert.Equal(2, stored.Character.Play.Exhaustion);
        Assert.True(stored.Character.Play.Inspiration);
        Assert.Equal([new HitDiceUse(12, 1)], stored.Character.Play.HitDiceSpent);
        Assert.Equal(Character.CurrentSchemaVersion, stored.Character.SchemaVersion);

        using var destination = new TempApp();
        destination.App.ApplyImport(temp.App.ExportCharacters([id]).Content);
        var imported = destination.App.GetCharacter(id);
        Assert.Equal(TempApp.Json(stored.Character.Play), TempApp.Json(imported.Character.Play));
        Assert.Equal(3, imported.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current); // the short rest gave the Rage back
        Assert.Equal(2, Assert.Single(imported.Sheet.HitDice!).Remaining);
    }
}
