using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Wave 3 item 23 (D39, R174): companion hit points are play state written only by the <c>companion*</c> play actions, and a
/// companion's attacks roll through <see cref="RollCommand.CompanionAttack"/>. Fixture names only ("Testy Hound", "Testy Bite").
/// </summary>
public class CompanionPlayTests
{
    private static readonly Guid HoundId = Guid.Parse("c0a00000-0000-4000-8000-000000000a01");
    private static readonly Guid CrowId = Guid.Parse("c0a00000-0000-4000-8000-000000000a02");
    private static readonly Guid BiteId = Guid.Parse("c0a00000-0000-4000-8000-000000000b01");
    private static readonly Guid HowlId = Guid.Parse("c0a00000-0000-4000-8000-000000000b02");
    private const string Marker = "ZQXMARKERZQX";

    private static (TempApp Temp, Guid Id) Start(string family, string hound = "Testy Hound")
    {
        var temp = new TempApp();
        var arcanist = new ContentReference(Guid.Parse("c0a5c000-0000-4000-8000-000000000001"), Guid.Parse("c0a5e000-0000-4000-8000-000000000001"));
        var character = new Character
        {
            Id = Guid.NewGuid(), Name = "Testy Handler", RulesFamily = family, Level = 3, Classes = [new(arcanist, 3)],
            BaseAbilities = new(8, 14, 12, 16, 10, 10),
            Companions =
            [
                new Companion(HoundId, hound, "Fixture beast", 13, 20, "40 ft.",
                    [new CustomAttack(BiteId, "Testy Bite", 4, "1d6+2", "piercing", null), new CustomAttack(HowlId, "Testy Howl", -1, null, null, null)], null),
                new Companion(CrowId, "Testy Crow", null, null, 5, null, null, null),
            ],
        };
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    private static CharacterView Act(TempApp temp, Guid id, PlayActionKind action, Guid? companion, int amount = 1, int? temporary = null) =>
        temp.App.Play(new(id, action, Confirm: true, Amount: amount, CompanionId: companion, Temporary: temporary));

    private static CompanionPlay? PlayOf(CharacterView view, Guid companion) => view.Character.Play.Companions?.FirstOrDefault(c => c.Id == companion);

    private static string CodeOf(Action act) => Assert.Throws<AppValidationException>(act).Problems[0].Code;

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void Damage_takes_temporary_hit_points_first_then_current_and_floors_at_0(string family)
    {
        var (temp, id) = Start(family);
        using var _ = temp;
        Act(temp, id, PlayActionKind.CompanionSetHitPoints, HoundId, 15, 4);

        var first = Act(temp, id, PlayActionKind.CompanionDamage, HoundId, 6);
        Assert.Equal(new CompanionPlay(HoundId, 13, 0), PlayOf(first, HoundId));

        var floored = Act(temp, id, PlayActionKind.CompanionDamage, HoundId, 500);
        Assert.Equal(new CompanionPlay(HoundId, 0, 0), PlayOf(floored, HoundId));
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void Heal_caps_at_the_maximum_and_leaves_temporary_hit_points_alone(string family)
    {
        var (temp, id) = Start(family);
        using var _ = temp;
        Act(temp, id, PlayActionKind.CompanionSetHitPoints, HoundId, 5, 3);

        var healed = Act(temp, id, PlayActionKind.CompanionHeal, HoundId, 4);
        Assert.Equal(new CompanionPlay(HoundId, 9, 3), PlayOf(healed, HoundId));

        var capped = Act(temp, id, PlayActionKind.CompanionHeal, HoundId, 500);
        Assert.Equal(new CompanionPlay(HoundId, null, 3), PlayOf(capped, HoundId)); // null: at the maximum
    }

    [Fact]
    public void Set_takes_exact_current_and_temporary_and_a_full_companion_with_no_temporary_has_no_entry()
    {
        var (temp, id) = Start(RulesFamilies.Srd51);
        using var _ = temp;

        var set = Act(temp, id, PlayActionKind.CompanionSetHitPoints, HoundId, 0, 9);
        Assert.Equal(new CompanionPlay(HoundId, 0, 9), PlayOf(set, HoundId));

        var keepsTemporary = Act(temp, id, PlayActionKind.CompanionSetHitPoints, HoundId, 11);
        Assert.Equal(new CompanionPlay(HoundId, 11, 9), PlayOf(keepsTemporary, HoundId)); // no temporary sent: unchanged

        var full = Act(temp, id, PlayActionKind.CompanionSetHitPoints, HoundId, 20, 0);
        Assert.Null(PlayOf(full, HoundId));
        Assert.Null(full.Character.Play.Companions);
    }

    [Fact]
    public void Set_temporary_changes_only_temporary_and_keeps_the_stored_current_so_an_overlapping_damage_is_kept()
    {
        var (temp, id) = Start(RulesFamilies.Srd51);
        using var _ = temp;
        Act(temp, id, PlayActionKind.CompanionDamage, HoundId, 8); // current 12 of 20

        // The page built this from the view it showed before the damage landed: it can only say "temporary 5".
        var set = Act(temp, id, PlayActionKind.CompanionSetTemporaryHitPoints, HoundId, 5);
        Assert.Equal(new CompanionPlay(HoundId, 12, 5), PlayOf(set, HoundId));

        var cleared = Act(temp, id, PlayActionKind.CompanionSetTemporaryHitPoints, HoundId, 0);
        Assert.Equal(new CompanionPlay(HoundId, 12, 0), PlayOf(cleared, HoundId));

        Assert.Equal("play.amount-out-of-range", CodeOf(() => Act(temp, id, PlayActionKind.CompanionSetTemporaryHitPoints, HoundId, PlayState.MaxHitPoints + 1)));
        Assert.Equal("play.companion-unknown", CodeOf(() => Act(temp, id, PlayActionKind.CompanionSetTemporaryHitPoints, Guid.NewGuid(), 1)));
    }

    [Fact]
    public void Set_above_the_maximum_or_out_of_range_is_refused_and_nothing_changes()
    {
        var (temp, id) = Start(RulesFamilies.Srd51);
        using var _ = temp;
        var before = temp.App.Store.FindCharacter(id)!;

        Assert.Equal("play.companion-hit-points-above-maximum", CodeOf(() => Act(temp, id, PlayActionKind.CompanionSetHitPoints, HoundId, 21)));
        Assert.Equal("play.amount-out-of-range", CodeOf(() => Act(temp, id, PlayActionKind.CompanionSetHitPoints, HoundId, 5, PlayState.MaxHitPoints + 1)));
        Assert.Equal("play.amount-out-of-range", CodeOf(() => Act(temp, id, PlayActionKind.CompanionSetHitPoints, HoundId, 5, -1)));
        Assert.Equal("play.amount-out-of-range", CodeOf(() => Act(temp, id, PlayActionKind.CompanionDamage, HoundId, -1)));

        Assert.Equal(before.UpdatedAt, temp.App.Store.FindCharacter(id)!.UpdatedAt);
        Assert.Null(temp.App.Store.FindCharacter(id)!.Play.Companions);
    }

    [Fact]
    public void An_unknown_companion_is_refused_with_a_code_and_the_names_are_never_quoted()
    {
        var (temp, id) = Start(RulesFamilies.Srd51, Marker);
        using var _ = temp;

        foreach (var kind in new[] { PlayActionKind.CompanionDamage, PlayActionKind.CompanionHeal, PlayActionKind.CompanionSetHitPoints })
        {
            var unknown = Assert.Throws<AppValidationException>(() => Act(temp, id, kind, Guid.NewGuid()));
            var missing = Assert.Throws<AppValidationException>(() => Act(temp, id, kind, null));
            Assert.Equal("play.companion-unknown", Assert.Single(unknown.Problems).Code);
            Assert.Equal("play.companion-unknown", Assert.Single(missing.Problems).Code);
            Assert.DoesNotContain(Marker, JsonSerializer.Serialize(unknown.Problems, RulesJson.Compact) + unknown + missing);
        }
        Assert.Null(temp.App.Store.FindCharacter(id)!.Play.Companions);
    }

    [Fact]
    public void A_companion_action_writes_nothing_but_that_companions_play_entry()
    {
        var (temp, id) = Start(RulesFamilies.Srd51);
        using var _ = temp;
        var before = temp.App.Store.FindCharacter(id)!;

        var view = Act(temp, id, PlayActionKind.CompanionDamage, CrowId, 2);

        var after = view.Character;
        Assert.Equal(new CompanionPlay(CrowId, 3, 0), Assert.Single(after.Play.Companions!));
        // Everything but the companion play entry (and the save time) is as it was; compared as stored JSON, since the lists are references.
        Assert.Equal(TempApp.Json(before with { Play = before.Play with { Companions = after.Play.Companions }, UpdatedAt = after.UpdatedAt }), TempApp.Json(after));
    }

    [Fact]
    public void A_stale_character_save_does_not_revert_companion_hit_points_and_removing_a_companion_drops_its_entry()
    {
        var (temp, id) = Start(RulesFamilies.Srd51);
        using var _ = temp;
        var stale = temp.App.GetCharacter(id).Character;
        Act(temp, id, PlayActionKind.CompanionDamage, HoundId, 8);
        Act(temp, id, PlayActionKind.CompanionDamage, CrowId, 1);

        var saved = temp.App.SaveCharacter(stale with { Name = "Testy Handler Renamed" });
        Assert.Equal(12, PlayOf(saved, HoundId)!.Current);
        Assert.Equal(4, PlayOf(saved, CrowId)!.Current);

        var removed = temp.App.SaveCharacter(saved.Character with { Companions = [saved.Character.Companions![0]] });
        Assert.NotNull(PlayOf(removed, HoundId));
        Assert.Null(PlayOf(removed, CrowId));
    }

    [Fact]
    public void Resting_does_not_change_a_companion()
    {
        var (temp, id) = Start(RulesFamilies.Srd51);
        using var _ = temp;
        Act(temp, id, PlayActionKind.CompanionDamage, HoundId, 8);

        var preview = temp.App.PreviewRest(id, RestPeriod.LongRest);
        var rested = temp.App.Rest(new(id, RestPeriod.LongRest, true, preview.Basis));

        Assert.Equal(12, PlayOf(rested, HoundId)!.Current);
    }

    // ---- rolling -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_companion_attack_rolls_d20_plus_its_to_hit_and_its_damage_dice()
    {
        var (temp, id) = Start(RulesFamilies.Srd51);
        using var _ = temp;
        var target = new CompanionAttackTarget(HoundId, BiteId);

        var attack = temp.App.Roll(new RollCommand(id, CompanionAttack: target, Mode: RollMode.Advantage));
        var damage = temp.App.Roll(new RollCommand(id, CompanionAttack: target, Damage: true, Critical: true));

        Assert.Equal("1d20", attack.Formula);
        Assert.Equal(2, attack.Dice.Count);
        var modifier = Assert.Single(attack.Modifiers);
        Assert.Equal(("Testy Bite to hit", 4), (modifier.Label, modifier.Amount));
        Assert.Equal(attack.Dice.Single(d => d.Kept).Value + 4, attack.Total);
        Assert.Equal(("companion.attack", "Testy Hound: Testy Bite attack"), (attack.Provenance!.RollId, attack.Provenance.Label));
        Assert.Equal("1d6+2", damage.Formula);
        Assert.Equal(2, damage.Dice.Count); // a critical doubles the dice
        Assert.Equal(("companion.damage", "Testy Hound: Testy Bite damage (piercing)"), (damage.Provenance!.RollId, damage.Provenance.Label));
        Assert.Null(temp.App.Store.FindCharacter(id)!.Play.Companions); // rolling changes nothing
    }

    [Fact]
    public void A_companion_attack_refusals_are_codes_and_never_quote_names()
    {
        var (temp, id) = Start(RulesFamilies.Srd51, Marker);
        using var _ = temp;
        AppValidationException Refused(RollCommand command) => Assert.Throws<AppValidationException>(() => temp.App.Roll(command));

        var unknownCompanion = Refused(new(id, CompanionAttack: new(Guid.NewGuid(), BiteId)));
        var unknownAttack = Refused(new(id, CompanionAttack: new(HoundId, Guid.NewGuid())));
        var wrongCompanion = Refused(new(id, CompanionAttack: new(CrowId, BiteId)));
        var noDamage = Refused(new(id, CompanionAttack: new(HoundId, HowlId), Damage: true));
        var ambiguous = Refused(new(id, Field: FieldIds.Initiative, CompanionAttack: new(HoundId, BiteId)));

        Assert.Equal("roll.companion-unknown", Assert.Single(unknownCompanion.Problems).Code);
        Assert.Equal("roll.companion-attack-unknown", Assert.Single(unknownAttack.Problems).Code);
        Assert.Equal("roll.companion-attack-unknown", Assert.Single(wrongCompanion.Problems).Code);
        Assert.Equal("roll.companion-attack-no-damage", Assert.Single(noDamage.Problems).Code);
        Assert.Equal("roll.ambiguous", Assert.Single(ambiguous.Problems).Code);
        foreach (var error in new[] { unknownCompanion, unknownAttack, wrongCompanion, noDamage, ambiguous })
            Assert.DoesNotContain(Marker, JsonSerializer.Serialize(error.Problems, RulesJson.Compact) + error);
    }

    [Fact]
    public void The_play_and_roll_commands_dispatch_with_companion_fields()
    {
        var (temp, id) = Start(RulesFamilies.Srd51);
        using var _ = temp;
        JsonElement Dispatch(string command, object payload) =>
            JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;

        var played = Dispatch("character.play", new { characterId = id, action = "companionDamage", amount = 3, companionId = HoundId, confirm = true });
        Assert.True(played.GetProperty("ok").GetBoolean(), played.ToString());
        var rolled = Dispatch("roll",new { characterId = id, companionAttack = new { companionId = HoundId, attackId = BiteId } });
        Assert.True(rolled.GetProperty("ok").GetBoolean(), rolled.ToString());
        Assert.Equal("companion.attack", rolled.GetProperty("result").GetProperty("provenance").GetProperty("rollId").GetString());
    }
}
