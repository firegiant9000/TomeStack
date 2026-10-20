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

    private static (TempApp Temp, Guid Id, ContentReference Veil) Caster(string family = RulesFamilies.Srd521, IReadOnlyList<DamageDefence>? defences = null)
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
            DamageDefences = defences,
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
    public void A_long_rest_preview_lists_the_end_of_concentration_and_a_short_rest_does_not()
    {
        var (temp, id, veil) = Caster();
        using var _ = temp;
        Assert.DoesNotContain(temp.App.PreviewRest(id, RestPeriod.LongRest).Changes, c => c.Kind == RestChangeKind.Concentration); // nothing to end

        temp.App.Play(new(id, PlayActionKind.StartConcentration, Confirm: true, ContentId: veil.ContentId));
        Assert.DoesNotContain(temp.App.PreviewRest(id, RestPeriod.ShortRest).Changes, c => c.Kind == RestChangeKind.Concentration);

        var preview = temp.App.PreviewRest(id, RestPeriod.LongRest);
        var change = Assert.Single(preview.Changes, c => c.Kind == RestChangeKind.Concentration);
        Assert.Contains("Fixture Veil", change.Reason);

        // Unticked, it is kept (the same way a toggle is kept); ticked, the long rest ends it.
        var kept = temp.App.Rest(new(id, RestPeriod.LongRest, Confirm: true, Basis: preview.Basis, Skip: [change.Id]));
        Assert.NotNull(kept.Character.Play.Concentration);
        var ended = temp.App.Rest(new(id, RestPeriod.LongRest, Confirm: true, Basis: temp.App.PreviewRest(id, RestPeriod.LongRest).Basis));
        Assert.Null(ended.Character.Play.Concentration);
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
    public void Concentration_is_refused_for_an_unprepared_spell_and_at_zero_hit_points()
    {
        var (temp, id, veil) = Caster();
        using var _ = temp;
        // Un-prepare Fixture Veil (a choice, saved through the character, not play state).
        var character = temp.App.GetCharacter(id).Character;
        temp.App.SaveCharacter(character with { Spells = [.. character.Spells!.Select(s => s.Spell == veil ? s with { Prepared = false } : s)] });
        Assert.Equal("play.spell-not-prepared", Code(Dispatch(temp, new { characterId = id, action = "startConcentration", contentId = veil.ContentId, confirm = true })));

        temp.App.SaveCharacter(temp.App.GetCharacter(id).Character with { Spells = character.Spells });
        temp.App.Play(new(id, PlayActionKind.SetHitPoints, Confirm: true, Amount: 0));
        Assert.Equal("play.unconscious", Code(Dispatch(temp, new { characterId = id, action = "startConcentration", contentId = veil.ContentId, confirm = true })));
    }

    [Fact]
    public void The_save_dc_is_capped_at_30_under_srd_521_and_only_at_the_validation_bound_under_srd_51()
    {
        // D19: SRD 5.2.1 ("up to a maximum DC of 30") caps it through the family policy; SRD 5.1 has no ceiling, and
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
        Assert.Equal(Character.PreviousSchemaVersion, stored.Character.SchemaVersion); // no v9 field is set, so it is written as v8 (R73)

        using var destination = new TempApp();
        destination.App.ApplyImport(temp.App.ExportCharacters([id]).Content);
        var imported = destination.App.GetCharacter(id);
        Assert.Equal(TempApp.Json(stored.Character.Play), TempApp.Json(imported.Character.Play));
        Assert.Equal(3, imported.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current); // the short rest gave the Rage back
        Assert.Equal(2, Assert.Single(imported.Sheet.HitDice!).Remaining);
    }

    // ---- Wave 3 item 21 (D19 amended): typed damage and damage defences --------------------------------------------------

    private static PlayCommand Typed(Guid id, int amount, string? type) => new(id, PlayActionKind.Damage, Confirm: true, Amount: amount, DamageType: type);

    private static (TempApp Temp, Guid Id) BrennaWith(IReadOnlyList<DamageDefence>? defences, ContentReference? feat = null)
    {
        var temp = new TempApp();
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        var id = temp.App.SaveCharacter(brenna with { DamageDefences = defences, Pins = feat is { } pinned ? [.. brenna.Pins, pinned] : brenna.Pins }).Character.Id;
        return (temp, id);
    }

    private static ContentRevision HideRevision(string revisionSuffix, int schema, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"5fb10d00-0000-4000-8000-0000000000{revisionSuffix}"), RevisionId = Guid.Parse($"5fb10e00-0000-4000-8000-0000000000{revisionSuffix}"),
        SchemaVersion = schema, Kind = ContentKind.Feat, Name = "Testy Hide", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(TempApp.LoadFixture<ContentPack>("fixture-pack-m3-effects.json").Sources[0].Id, new(3)), Status = RevisionStatus.Published, Effects = effects,
    };

    private static ContentReference Install(TempApp temp, ContentRevision revision)
    {
        temp.App.Store.AddRevision(revision);
        return revision.Reference;
    }

    private static void AssertOutcome(DamageOutcome? outcome, int amount, string? type, DamageEffect effect, int adjusted, int absorbed, int lost)
    {
        Assert.Equal((amount, type, effect, adjusted, absorbed, lost), (outcome!.Amount, outcome.Type, outcome.Effect, outcome.Adjusted, outcome.Absorbed, outcome.Lost));
        Assert.Empty(outcome.Warnings);
    }

    private static readonly DamageDefence SlashingResistance = new("slashing", DamageDefenceKind.Resistance);

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void Review_focus_3_typed_damage_is_halved_then_taken_by_temporary_hit_points_then_hit_points_and_undo_is_exact(string family)
    {
        var (temp, id, veil) = Caster(family, [SlashingResistance]);
        using var _ = temp;
        var maximum = temp.App.GetCharacter(id).Sheet.HitPoints!.Maximum;
        temp.App.Play(new(id, PlayActionKind.StartConcentration, Confirm: true, ContentId: veil.ContentId));
        temp.App.Play(new(id, PlayActionKind.SetTemporaryHitPoints, Confirm: true, Amount: 5));

        var view = temp.App.Play(Typed(id, 13, "slashing"));

        AssertOutcome(view.Damage, 13, "slashing", DamageEffect.Halved, 6, 5, 1);
        Assert.Equal((0, maximum - 1), (view.Character.Play.TemporaryHitPoints, view.Sheet.HitPoints!.Current));
        Assert.Equal(10, view.Character.Play.Concentration!.PendingSaveDc); // max(10, 6 / 2): the amount after resistance, before temporary hit points

        // The Undo the UI builds (D23): the exact hit points, then the temporary hit points, and the concentration check cleared.
        temp.App.Play(new(id, PlayActionKind.SetHitPoints, Confirm: true, Amount: maximum));
        view = temp.App.Play(new(id, PlayActionKind.SetTemporaryHitPoints, Confirm: true, Amount: 5));
        Assert.Equal((5, maximum), (view.Sheet.HitPoints!.Temporary, view.Sheet.HitPoints.Current));
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void The_concentration_dc_comes_from_the_amount_after_defences_and_before_temporary_hit_points(string family)
    {
        var cap = RulesFamilies.Get(family).ConcentrationSaveMaximumDc ?? 100; // PlayState validation bound when the family has none
        DamageOutcome Hit(int temporary, int amount, string type, out int? dc)
        {
            var (temp, id, veil) = Caster(family, [SlashingResistance, new("cold", DamageDefenceKind.Vulnerability)]);
            using var _ = temp;
            temp.App.Play(new(id, PlayActionKind.StartConcentration, Confirm: true, ContentId: veil.ContentId));
            temp.App.Play(new(id, PlayActionKind.SetTemporaryHitPoints, Confirm: true, Amount: temporary));
            var view = temp.App.Play(Typed(id, amount, type));
            dc = view.Character.Play.Concentration!.PendingSaveDc;
            return view.Damage!;
        }

        // 60 slashing resisted is 30, so the DC is 15; from the 60 entered it would be 30.
        Hit(1000, 60, "slashing", out var resisted);
        Assert.Equal(15, resisted);

        // 40 cold doubled is 80, so the DC is 40, capped by the family; from the 40 entered it would be 20.
        Hit(1000, 40, "cold", out var doubled);
        Assert.Equal(Math.Min(40, cap), doubled);

        // 30 cold doubled is 60: 50 temporary hit points take part of it, and the DC still comes from the 60 (30), not from the 10 left
        // for hit points (10) nor from the 30 entered (15).
        var partly = Hit(50, 30, "cold", out var absorbed);
        Assert.Equal((60, 50, 10), (partly.Adjusted, partly.Absorbed, partly.Lost));
        Assert.Equal(Math.Min(30, cap), absorbed);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void Immunity_takes_nothing_and_asks_for_no_concentration_check(string family)
    {
        var (temp, id, veil) = Caster(family, [new("fire", DamageDefenceKind.Immunity)]);
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.StartConcentration, Confirm: true, ContentId: veil.ContentId));
        temp.App.Play(new(id, PlayActionKind.SetTemporaryHitPoints, Confirm: true, Amount: 5));
        var before = temp.App.GetCharacter(id).Character.Play;

        var view = temp.App.Play(Typed(id, 13, "fire"));

        AssertOutcome(view.Damage, 13, "fire", DamageEffect.Immune, 0, 0, 0);
        Assert.Equal(TempApp.Json(before), TempApp.Json(view.Character.Play)); // no hit point change, no pending save
        Assert.Null(view.Character.Play.Concentration!.PendingSaveDc);
    }

    [Fact]
    public void Vulnerability_doubles_before_temporary_hit_points_and_resistance_with_vulnerability_halves_then_doubles()
    {
        var (temp, id) = BrennaWith([new("cold", DamageDefenceKind.Vulnerability), new("slashing", DamageDefenceKind.Resistance), new("slashing", DamageDefenceKind.Vulnerability)]);
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.SetTemporaryHitPoints, Confirm: true, Amount: 5));

        var view = temp.App.Play(Typed(id, 13, "cold"));
        AssertOutcome(view.Damage, 13, "cold", DamageEffect.Doubled, 26, 5, 21);
        Assert.Equal((0, 35 - 21), (view.Sheet.HitPoints!.Temporary, view.Sheet.HitPoints.Current));

        view = temp.App.Play(Typed(id, 13, "slashing")); // halved to 6 (rounded down), then doubled
        AssertOutcome(view.Damage, 13, "slashing", DamageEffect.HalvedThenDoubled, 12, 0, 12);
    }

    [Fact]
    public void Several_resistances_to_one_type_count_once_and_a_grant_type_is_matched_in_any_case()
    {
        var grants = HideRevision("e1", 10,
            new GrantEffect { Id = "hide-a", Grant = GrantKind.DamageResistance, DamageType = "Slashing" },
            new GrantEffect { Id = "hide-b", Grant = GrantKind.DamageResistance, DamageType = "SLASHING" });
        var (temp, id) = BrennaWith([SlashingResistance]);
        using var _ = temp;
        var feat = Install(temp, grants);
        var brenna = temp.App.GetCharacter(id).Character;
        temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins, feat] });

        var sheet = temp.App.GetCharacter(id).Sheet;
        Assert.Equal(3, Assert.Single(sheet.DamageDefences!).Sources.Count);

        var view = temp.App.Play(Typed(id, 13, "SLASHING")); // the command's type is normalised too
        AssertOutcome(view.Damage, 13, "slashing", DamageEffect.Halved, 6, 0, 6);
    }

    [Fact]
    public void Untyped_damage_ignores_every_defence_and_a_type_without_a_defence_is_unchanged()
    {
        var (temp, id) = BrennaWith([SlashingResistance, new("fire", DamageDefenceKind.Immunity)]);
        using var _ = temp;

        var view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 13));
        AssertOutcome(view.Damage, 13, null, DamageEffect.None, 13, 0, 13);
        Assert.Equal(35 - 13, view.Sheet.HitPoints!.Current);

        view = temp.App.Play(Typed(id, 4, "acid"));
        AssertOutcome(view.Damage, 4, "acid", DamageEffect.None, 4, 0, 4);
    }

    [Fact]
    public void An_unknown_damage_type_is_refused_with_a_code_and_nothing_changes()
    {
        var (temp, id) = BrennaWith(null);
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        foreach (var type in new[] { "sonic", "", " fire", "fire " })
            Assert.Equal("play.damage-type-unknown", Assert.Throws<AppValidationException>(() => temp.App.Play(Typed(id, 5, type))).Problems[0].Code);
        Assert.Equal("play.damage-type-unknown", Code(Dispatch(temp, new { characterId = id, action = "damage", amount = 5, damageType = "sonic", confirm = true })));
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_damage_grant_in_a_v9_revision_is_inert()
    {
        // Stored by an older build: the kind is unreadable below v10, so it stays an unknown effect and gives no defence.
        var plain = HideRevision("e2", 9, new GrantEffect { Id = "hide", Grant = GrantKind.Proficiency, Target = FieldIds.Skill("stealth") });
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(plain, RulesJson.Compact))!;
        node["effects"]!.AsArray()[0] = System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"grant\",\"id\":\"hide\",\"grant\":\"damageResistance\",\"damageType\":\"fire\"}");
        var stored = JsonSerializer.Deserialize<ContentRevision>(node.ToJsonString(RulesJson.Compact), RulesJson.Compact)!;
        Assert.IsType<UnknownEffect>(stored.Effects[0]);
        var (temp, id) = BrennaWith(null);
        using var _ = temp;
        var feat = Install(temp, stored);
        var brenna = temp.App.GetCharacter(id).Character;
        temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins, feat] });

        Assert.Null(temp.App.GetCharacter(id).Sheet.DamageDefences);
        AssertOutcome(temp.App.Play(Typed(id, 12, "fire")).Damage, 12, "fire", DamageEffect.None, 12, 0, 12);
    }

    [Fact]
    public void Massive_damage_warns_when_the_remainder_at_zero_equals_the_maximum_and_never_kills()
    {
        var (temp, id) = BrennaWith(null);
        using var _ = temp;

        var view = temp.App.Play(Typed(id, 69, "fire")); // 34 left over at 0: one under the maximum of 35
        Assert.Empty(view.Damage!.Warnings);
        Assert.Equal(0, view.Sheet.HitPoints!.Current);

        view = temp.App.Play(new(id, PlayActionKind.SetHitPoints, Confirm: true, Amount: 35));
        view = temp.App.Play(Typed(id, 70, "fire")); // exactly 35 left over
        var warning = Assert.Single(view.Damage!.Warnings);
        Assert.Equal("play.massive-damage", warning.Code);
        Assert.Contains("35", warning.Message, StringComparison.Ordinal);
        Assert.Equal(0, view.Sheet.HitPoints!.Current); // a warning only: no automatic death, no death saves recorded
        Assert.Equal(new DeathSaves(), view.Character.Play.DeathSaves);

        temp.App.Play(new(id, PlayActionKind.SetHitPoints, Confirm: true, Amount: 35));
        temp.App.Play(new(id, PlayActionKind.SetTemporaryHitPoints, Confirm: true, Amount: 10));
        view = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 80)); // 10 absorbed, 35 to 0, 35 left over
        Assert.Equal("play.massive-damage", Assert.Single(view.Damage!.Warnings).Code);
    }

    [Fact]
    public void Resistance_can_keep_a_hit_from_being_massive()
    {
        var (temp, id) = BrennaWith([SlashingResistance]);
        using var _ = temp;

        var view = temp.App.Play(Typed(id, 140, "slashing")); // 70 after resistance: 35 left over, the maximum
        Assert.Equal("play.massive-damage", Assert.Single(view.Damage!.Warnings).Code);
        temp.App.Play(new(id, PlayActionKind.SetHitPoints, Confirm: true, Amount: 35));
        view = temp.App.Play(Typed(id, 138, "slashing")); // 69 after resistance: 34 left over
        Assert.Empty(view.Damage!.Warnings);
    }

    [Fact]
    public void Typed_damage_that_crosses_half_the_hit_points_bloodies_and_undo_puts_it_back()
    {
        var (temp, id) = BrennaWith([new("fire", DamageDefenceKind.Vulnerability)]);
        using var _ = temp;
        Assert.NotEqual(true, temp.App.GetCharacter(id).Sheet.Bloodied);

        var view = temp.App.Play(Typed(id, 9, "fire")); // doubled to 18: 17 of 35 left, which is half rounded down
        Assert.True(view.Sheet.Bloodied);

        view = temp.App.Play(new(id, PlayActionKind.SetHitPoints, Confirm: true, Amount: 35));
        Assert.NotEqual(true, view.Sheet.Bloodied);
    }

    [Fact]
    public void A_play_command_that_is_not_damage_carries_no_outcome()
    {
        var (temp, id) = BrennaWith(null);
        using var _ = temp;

        Assert.Null(temp.App.Play(new(id, PlayActionKind.Heal, Confirm: true, Amount: 1)).Damage);
        Assert.Null(temp.App.GetCharacter(id).Damage);
    }
}
