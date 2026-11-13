namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content v12 G6 task 4: the hit's source and saving throw, trigger matching, halving and rolled reductions in
/// <see cref="DamagePipeline.Plan"/>. Responses are built directly on the Fixture Bulwark's sheet (HP 68, fire resistance).
/// </summary>
public class DamagePipelineV12Tests
{
    private static readonly RulesFamilyPolicy Policy = RulesFamilies.Get(RulesFamilies.Srd521);
    private static readonly Guid Holder = Guid.Parse("6f0dc000-0000-4000-8000-000000000a01");

    private static ResponseValue R(
        string id, ResponsePhase phase = ResponsePhase.DamageReduction, int? amount = null, ResponseTrigger? trigger = null,
        ResponseShare? share = null, string? dice = null, int? atOrBelow = null, bool survived = false) =>
        new(Holder, "Holder", id, id, phase, amount, null, null, null, true, null, null, atOrBelow, survived, null, trigger, share, dice);

    private static (Character Character, CharacterSheet Sheet) Sheet(int hp, params ResponseValue[] responses)
    {
        var (character, sheet) = DamageResponseTests.Stand(hp: hp);
        return (character, sheet with { Responses = responses });
    }

    private static DamagePlan Run((Character Character, CharacterSheet Sheet) c, DamageEvent damage, params ResponseChoice[] chosen) =>
        DamagePipeline.Plan(damage, c.Character, c.Sheet, chosen, Policy);

    private static ResponseChoice Pick(string id, int? rolled = null) => new(Holder, id, rolled);

    private static string[] OfferedIds(DamagePlan plan) => [.. plan.Offers.Where(o => o.Offered).Select(o => o.Response.ResponseId)];

    private static readonly ResponseTrigger Attack = new(ResponseSource.Attack);
    private static readonly ResponseTrigger Fall = new(ResponseSource.Fall);
    private static readonly ResponseTrigger SaveDex = new(ResponseSource.Save, "dex", SaveResult.Success);

    private static DamageEvent SaveHit(int amount, bool succeeded, bool half = true, string ability = "dex", string? type = null) =>
        new(amount, type, ResponseSource.Save, new DamageSave(ability, succeeded, half));

    [Fact]
    public void Unsaid_source_offers_attack_and_fall_triggers_but_not_save_triggers()
    {
        var c = Sheet(68, R("none"), R("atk", trigger: Attack), R("fall", trigger: Fall), R("sv", trigger: SaveDex));

        var plan = Run(c, new(10, null));

        Assert.Equal(["none", "atk", "fall"], OfferedIds(plan));
    }

    [Fact]
    public void Known_source_offers_only_its_own_triggers()
    {
        var c = Sheet(68, R("none"), R("atk", trigger: Attack), R("fall", trigger: Fall), R("sv", trigger: SaveDex));

        Assert.Equal(["none", "atk"], OfferedIds(Run(c, new(10, null, ResponseSource.Attack))));
        Assert.Equal(["none", "fall"], OfferedIds(Run(c, new(10, null, ResponseSource.Fall))));
        Assert.Equal(["none", "sv"], OfferedIds(Run(c, SaveHit(10, true))));
    }

    [Fact]
    public void Damage_type_trigger_needs_the_hit_type()
    {
        var c = Sheet(68, R("cold", trigger: new(ResponseSource.Attack, DamageTypes: ["cold", "acid"])));

        Assert.Equal(["cold"], OfferedIds(Run(c, new(10, "cold"))));
        Assert.Empty(OfferedIds(Run(c, new(10, "fire"))));
        Assert.Empty(OfferedIds(Run(c, new(10, null))));
        var chosen = Run(c, new(10, null), Pick("cold"));
        Assert.Equal("damage.response-unavailable", Assert.Single(chosen.Notes).Code);
    }

    [Fact]
    public void Save_success_halves_rounding_down()
    {
        var c = Sheet(68);

        var saved = Run(c, SaveHit(7, true));
        var none = Run(c, new(7, null));

        Assert.Equal(3, saved.Result.Outcome.Lost);
        Assert.True(saved.Result.Outcome.SaveHalved);
        Assert.Equal(0, saved.Reduction);
        Assert.Equal(7, none.Result.Outcome.Lost);
        Assert.False(none.Result.Outcome.SaveHalved);
        Assert.Equal(7, Run(c, SaveHit(7, false)).Result.Outcome.Lost);
        Assert.Equal(7, Run(c, SaveHit(7, true, half: false)).Result.Outcome.Lost);
    }

    [Fact]
    public void Save_success_with_share_all_takes_nothing()
    {
        var c = Sheet(68, R("all", share: ResponseShare.All, trigger: SaveDex));

        var plan = Run(c, SaveHit(7, true), Pick("all"));

        Assert.Empty(plan.Notes);
        Assert.Equal(0, plan.Result.Outcome.Lost);
        Assert.Equal(3, plan.Reduction);
    }

    [Fact]
    public void Save_failure_with_share_half_leaves_half()
    {
        var c = Sheet(68, R("half", share: ResponseShare.Half, trigger: new(ResponseSource.Save, "dex", SaveResult.Failure)));

        var plan = Run(c, SaveHit(7, false), Pick("half"));

        Assert.Empty(plan.Notes);
        Assert.Equal(3, plan.Result.Outcome.Lost);
        Assert.Equal(4, plan.Reduction);
    }

    [Fact]
    public void Save_trigger_needs_a_halving_save()
    {
        var c = Sheet(68, R("sv", trigger: SaveDex));

        Assert.Equal(["sv"], OfferedIds(Run(c, SaveHit(7, true))));
        Assert.Empty(OfferedIds(Run(c, SaveHit(7, true, half: false))));
        Assert.Empty(OfferedIds(Run(c, SaveHit(7, false))));
        Assert.Empty(OfferedIds(Run(c, SaveHit(7, true, ability: "con"))));
    }

    [Fact]
    public void Share_half_comes_before_resistance()
    {
        var c = Sheet(68, R("half", share: ResponseShare.Half));

        var plan = Run(c, new(21, "fire"), Pick("half"));

        Assert.Empty(plan.Notes);
        Assert.Equal(11, plan.Reduction);
        Assert.Equal(5, plan.Result.Outcome.Lost);
    }

    [Fact]
    public void Shares_then_flat_reductions()
    {
        // Flat is listed first, but the share still comes before it.
        var c = Sheet(68, R("flat", amount: 3), R("half", share: ResponseShare.Half));

        var plan = Run(c, new(20, null), Pick("flat"), Pick("half"));

        Assert.Equal(7, plan.Result.Outcome.Lost);
        Assert.Equal(13, plan.Reduction);
    }

    [Fact]
    public void Dice_response_adds_the_roll_to_its_amount()
    {
        var c = Sheet(68, R("dice", amount: 4, dice: "1d10"));

        var plan = Run(c, new(20, null), Pick("dice", 6));

        Assert.Empty(plan.Notes);
        Assert.Equal(10, plan.Reduction);
        Assert.Equal(10, plan.Result.Outcome.Lost);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(11)]
    public void Dice_response_without_or_outside_its_roll_is_a_note(int? rolled)
    {
        var c = Sheet(68, R("dice", amount: 4, dice: "1d10"));

        var plan = Run(c, new(20, null), Pick("dice", rolled));

        Assert.Equal("damage.response-roll", Assert.Single(plan.Notes).Code);
        Assert.Empty(plan.Outcomes);
        Assert.Equal(["dice"], OfferedIds(plan));
    }

    [Fact]
    public void Roll_notes_name_the_response_by_its_label_and_never_a_content_id()
    {
        var c = Sheet(68, R("dice", amount: 4, dice: "1d10"), R("flat", amount: 3));

        var outside = Assert.Single(Run(c, new(20, null), Pick("dice", 11)).Notes).Message;
        var notDice = Assert.Single(Run(c, new(20, null), Pick("flat", 4)).Notes).Message;

        Assert.Equal("'dice' needs a roll between 1 and 10.", outside);
        Assert.Equal("'flat' cannot use the roll given.", notDice);
        ServiceWordingTests.AssertPlain(outside, [], Holder.ToString());
        ServiceWordingTests.AssertPlain(notDice, [], Holder.ToString());
    }

    [Fact]
    public void Save_trigger_missing_its_ability_or_result_matches_nothing()
    {
        var c = Sheet(68, R("noAbility", trigger: new(ResponseSource.Save, null, SaveResult.Success)), R("noResult", trigger: new(ResponseSource.Save, "dex", null)));

        Assert.Empty(OfferedIds(Run(c, SaveHit(7, true))));
        Assert.Empty(OfferedIds(Run(c, SaveHit(7, false))));
    }

    [Fact]
    public void A_flat_reduction_never_adds_damage()
    {
        var c = Sheet(68, R("neg", amount: 2, dice: "1d6"));

        // a roll that would drive the total below zero (outside the dice range, so it is a note) cannot be applied
        var plan = Run(c, new(20, null), Pick("neg", 1));
        Assert.Equal(3, plan.Reduction);

        var negative = Sheet(68, R("odd", amount: -4));
        Assert.Equal(20, Run(negative, new(20, null), Pick("odd")).Result.Outcome.Lost);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(15, true)]
    [InlineData(4, false)]
    [InlineData(16, false)]
    public void Dice_with_a_constant_accept_the_whole_range(int rolled, bool accepted)
    {
        var c = Sheet(68, R("dice", amount: 1, dice: "2d6+3"));

        var plan = Run(c, new(30, null), Pick("dice", rolled));

        Assert.Equal(accepted, plan.Notes.Count == 0);
    }

    [Fact]
    public void Rolled_on_a_non_dice_response_is_a_note()
    {
        var c = Sheet(68, R("flat", amount: 3));

        var plan = Run(c, new(20, null), Pick("flat", 4));

        Assert.Equal("damage.response-roll", Assert.Single(plan.Notes).Code);
        Assert.Empty(plan.Outcomes);
    }

    [Fact]
    public void Trigger_gates_thresholds_and_survivals_too()
    {
        var c = Sheet(5,
            R("stand", ResponsePhase.HitPointOutcome, amount: 1, trigger: Fall),
            R("rally", ResponsePhase.Threshold, atOrBelow: 68, trigger: Fall));

        var attack = Run(c, new(90, null, ResponseSource.Attack), Pick("stand"));
        Assert.Empty(OfferedIds(attack));
        Assert.Equal("damage.response-unavailable", Assert.Single(attack.Notes).Code);
        Assert.Equal(0, attack.Final.Current);

        var fall = Run(c, new(90, null, ResponseSource.Fall), Pick("stand"));
        Assert.Empty(fall.Notes);
        Assert.Equal(["stand", "rally"], OfferedIds(fall));
        Assert.Equal(1, fall.Final.Current);
    }

    [Fact]
    public void AfterSave_halves_only_for_a_successful_halving_save()
    {
        Assert.Equal(3, DamagePipeline.AfterSave(SaveHit(7, true)));
        Assert.Equal(7, DamagePipeline.AfterSave(SaveHit(7, true, half: false)));
        Assert.Equal(7, DamagePipeline.AfterSave(new(7, null, ResponseSource.Attack, new DamageSave("dex", true))));
    }
}
