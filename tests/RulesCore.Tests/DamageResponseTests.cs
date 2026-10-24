using System.Reflection;
using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// D43 S7 task 7.3 (D45): damage responses on the sheet and <see cref="DamagePipeline.Plan"/>, the order of one damage event with
/// the player's chosen responses. Uses the Fixture Bulwark (HP 68, Con 14, PB 3, grit 5; fire resistance): brace takes 3 + CON.MOD
/// (5) off and costs 1 grit, stand and endure (surge form only) leave 1 hit point, rally is offered at floor(HP.MAX / 4) (17) or
/// less, rebound after a survival.
/// </summary>
public class DamageResponseTests
{
    private static readonly RulesFamilyPolicy Policy = RulesFamilies.Get(RulesFamilies.Srd521);

    private const string AtAnyDrop = """{"type":"response","id":"heal","responseId":"heal","label":"Heal","phase":"threshold","when":{"atOrBelow":"HP.MAX"},"outcomes":[{"kind":"regainHitPoints","amount":"10"}]}""";
    private const string TempAnyDrop = """{"type":"response","id":"warding","responseId":"warding","label":"Warding","phase":"threshold","when":{"atOrBelow":"HP.MAX"},"outcomes":[{"kind":"temporaryHitPoints","amount":"6"}]}""";

    // ---- helpers (the property tests reuse them) ---------------------------------------------------------------------------

    /// <summary>A test-local revision at v11 holding <paramref name="effects"/>, in the responses source; <paramref name="n"/> makes its ids.</summary>
    internal static ContentRevision Holder(int n, string effects)
    {
        var revision = Fixtures.RevisionWithEffect(effects, 11);
        return revision with
        {
            ContentId = Guid.Parse($"6f0dc000-0000-4000-8000-0000000001{n:X2}"),
            RevisionId = Guid.Parse($"6f0de000-0000-4000-8000-0000000001{n:X2}"),
            Provenance = revision.Provenance with { SourceId = Fixtures.ResponsesPack().Sources[0].Id },
        };
    }

    /// <summary>The Bulwark at the given state, calculated against the responses pack plus any <paramref name="extra"/> test-local content (pinned).</summary>
    internal static (Character Character, CharacterSheet Sheet) Stand(
        int? hp = null, int temporary = 0, bool surge = false, bool concentrating = false, Func<PlayState, PlayState>? more = null,
        IReadOnlyList<ContentRevision>? extra = null)
    {
        var character = Fixtures.Load("pm-bulwark-l8.json");
        var play = character.Play with
        {
            CurrentHitPoints = hp,
            TemporaryHitPoints = temporary,
            Toggles = surge ? [new ActiveToggle(Fixtures.SurgeForm.ContentId, "surge")] : [],
            Concentration = concentrating ? new Concentration(Fixtures.Bulwark, "Held") : null,
        };
        character = character with { Play = more is null ? play : more(play) };
        var catalog = Fixtures.PlayMechanicsCatalog();
        if (extra is { Count: > 0 })
        {
            var pack = Fixtures.ResponsesPack();
            catalog = new(pack.Sources, [.. pack.Revisions, .. extra]);
            character = character with { Pins = [.. character.Pins, .. extra.Select(r => r.Reference)] };
        }
        return (character, CharacterCalculator.Calculate(character, catalog));
    }

    internal static IReadOnlyList<ResponseChoice> Choose(CharacterSheet sheet, params string[] ids) =>
        [.. ids.Select(id => sheet.Responses!.Single(r => r.ResponseId == id)).Select(r => new ResponseChoice(r.ContentId, r.ResponseId))];

    private static ResponseValue On(CharacterSheet sheet, string id) => sheet.Responses!.Single(r => r.ResponseId == id);

    private static ResponseOffer? Offer(DamagePlan plan, string id) => plan.Offers.SingleOrDefault(o => o.Response.ResponseId == id);

    private static (string Id, bool Offered, string? Reason)[] Offered(DamagePlan plan) =>
        [.. plan.Offers.Select(o => (o.Response.ResponseId, o.Offered, o.Reason))];

    private static DamagePlan PlanFor((Character Character, CharacterSheet Sheet) c, int amount, string? type, params string[] chosen) =>
        DamagePipeline.Plan(new(amount, type), c.Character, c.Sheet, Choose(c.Sheet, chosen), Policy);

    // ---- the order -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Reduction_applies_before_defences()
    {
        var plan = PlanFor(Stand(hp: 68), 20, "fire", "brace");

        Assert.Empty(plan.Notes);
        Assert.Equal(5, plan.Reduction);
        Assert.Equal((15, DamageEffect.Halved, 7), (plan.Result.Outcome.Amount, plan.Result.Outcome.Effect, plan.Result.Outcome.Adjusted));
        Assert.Equal(new HitPointState(68, 61, 0), plan.Final);
        Assert.Equal(plan.Result.After, plan.Final);
    }

    [Fact]
    public void Concentration_dc_comes_from_the_amount_after_reduction_and_defences()
    {
        var plan = PlanFor(Stand(hp: 68, concentrating: true), 60, "fire", "brace");

        Assert.Empty(plan.Notes);
        Assert.Equal(27, plan.Result.Outcome.Adjusted);
        Assert.Equal(13, plan.Result.ConcentrationDc);
        Assert.False(plan.Result.ConcentrationEnds);
    }

    [Fact]
    public void Temporary_hit_points_absorb_after_defences()
    {
        var plan = PlanFor(Stand(hp: 68, temporary: 10), 20, "fire", "brace");

        Assert.Equal((7, 7, 0), (plan.Result.Outcome.Adjusted, plan.Result.Outcome.Absorbed, plan.Result.Outcome.Lost));
        Assert.Equal(new HitPointState(68, 68, 3), plan.Final);
    }

    [Theory]
    [InlineData(10, 15, true)]
    [InlineData(0, 15, false)]
    [InlineData(30, 15, false)]
    public void A_survival_outcome_is_offered_only_when_dropping_to_zero(int hp, int damage, bool offered)
    {
        var plan = PlanFor(Stand(hp: hp), damage, null);

        Assert.Equal(offered, Offer(plan, "stand") is { Offered: true });
        Assert.Equal(offered, Offer(plan, "stand") is not null);
    }

    [Fact]
    public void Only_one_hit_point_outcome_applies_per_event()
    {
        var plan = PlanFor(Stand(hp: 10, surge: true), 15, null, "stand", "endure");

        Assert.Contains(plan.Notes, n => n.Code == "damage.one-outcome");
        Assert.Empty(plan.Outcomes);
    }

    [Fact]
    public void A_survival_outcome_keeps_concentration_and_death_saves()
    {
        var (character, sheet) = Stand(hp: 10, concentrating: true);
        var plan = DamagePipeline.Plan(new(15, null), character, sheet, Choose(sheet, "stand"), Policy);

        Assert.Empty(plan.Notes);
        Assert.Equal(new HitPointState(68, 1, 0), plan.Final);
        // The result is coherent with the survival: the hit points left, the loss counted from them, concentration kept with its save.
        Assert.Equal(plan.Final, plan.Result.After);
        Assert.Equal(9, plan.Result.Outcome.Lost);
        Assert.Equal(15, plan.Result.Outcome.Amount);
        Assert.False(plan.Result.ConcentrationEnds);
        Assert.Equal(10, plan.Result.ConcentrationDc);
        Assert.Equal(new DeathSaves(), character.Play.DeathSaves);
    }

    [Fact]
    public void A_survived_threshold_is_eligible_after_a_survival_even_when_the_hit_points_did_not_change()
    {
        // 1 hit point and a lethal hit: the survival leaves 1, so the final hit points equal the ones before.
        var plan = PlanFor(Stand(hp: 1), 15, null, "stand", "rebound");

        Assert.Empty(plan.Notes);
        Assert.Equal(1, plan.Final.Current);
        Assert.Equal(plan.Final, plan.Result.After);
        Assert.Equal(0, plan.Result.Outcome.Lost);
        Assert.True(Offer(plan, "rebound")!.Offered);
        Assert.Equal(ResponseOutcomeKind.OfferRoll, Assert.Single(plan.Outcomes).Kind);
        // At or below still needs the hit points to have dropped.
        Assert.Null(Offer(plan, "rally"));
    }

    [Fact]
    public void Chosen_costs_are_summed_per_pool_and_the_later_response_is_refused()
    {
        var twin = Holder(40, """{"type":"response","id":"twin","responseId":"twin","label":"Twin","phase":"damageReduction","amount":"1","resourceId":"grit","resourceContent":"6f0dc000-0000-4000-8000-000000000070","cost":"1"}""");
        var c = Stand(hp: 68, extra: [twin], more: p => p.WithSpent(Fixtures.Bulwark.ContentId, "grit", 4));

        var one = PlanFor(c, 10, null, "brace");
        var both = PlanFor(c, 10, null, "brace", "twin");

        Assert.Empty(one.Notes);
        var note = Assert.Single(both.Notes);
        Assert.Equal("damage.response-unavailable", note.Code);
        Assert.Contains("twin", note.Message, StringComparison.Ordinal);
        // The refused response shapes nothing: the plan is brace's alone.
        Assert.Equal(5, both.Reduction);
        Assert.Equal(one.Final, both.Final);
        Assert.Equal(JsonSerializer.Serialize(one.Result), JsonSerializer.Serialize(both.Result));
        Assert.Equal(Offered(one), Offered(both));
        // With 2 grit left both fit.
        Assert.Empty(PlanFor(Stand(hp: 68, extra: [twin], more: p => p.WithSpent(Fixtures.Bulwark.ContentId, "grit", 3)), 10, null, "brace", "twin").Notes);
    }

    [Fact]
    public void A_survival_amount_above_current_hit_points_loses_nothing()
    {
        var big = Holder(42, """{"type":"response","id":"bigstand","responseId":"bigstand","label":"Big stand","phase":"hitPointOutcome","amount":"10"}""");
        var plan = PlanFor(Stand(hp: 3, extra: [big]), 15, null, "bigstand");

        Assert.Empty(plan.Notes);
        Assert.Equal(new HitPointState(68, 10, 0), plan.Final);
        Assert.Equal(plan.Final, plan.Result.After);
        Assert.Equal(0, plan.Result.Outcome.Lost);
    }

    [Fact]
    public void A_pool_whose_maximum_does_not_calculate_is_not_tracked()
    {
        var holder = Holder(41, """{"type":"resource","id":"odd","resourceId":"odd","label":"Odd","maximum":"banana"},{"type":"response","id":"oddr","responseId":"oddr","label":"Odd","phase":"damageReduction","amount":"1","resourceId":"odd","cost":"2"}""");
        var (_, sheet) = Stand(extra: [holder]);

        Assert.Null(sheet.Resources!.Single(r => r.ResourceId == "odd").Current);
        Assert.Equal((false, "Needs 2 Odd (not tracked)"), (On(sheet, "oddr").Available, On(sheet, "oddr").UnavailableReason));
    }

    [Fact]
    public void Without_a_survival_hit_points_at_zero_end_concentration()
    {
        var plan = PlanFor(Stand(hp: 10, concentrating: true), 15, null);

        Assert.Equal(0, plan.Final.Current);
        Assert.True(plan.Result.ConcentrationEnds);
        Assert.Null(plan.Result.ConcentrationDc);
    }

    [Fact]
    public void A_survival_needing_a_form_is_unavailable_outside_it()
    {
        var plan = PlanFor(Stand(hp: 10), 15, null);

        var endure = Offer(plan, "endure")!;
        Assert.False(endure.Offered);
        Assert.False(endure.Response.Available);
        Assert.Equal("Available in Fixture Surge Form", endure.Reason);
        Assert.True(Offer(PlanFor(Stand(hp: 10, surge: true), 15, null), "endure")!.Offered);
    }

    [Fact]
    public void Thresholds_use_the_final_hit_points_and_the_pre_event_maximum()
    {
        var plan = PlanFor(Stand(hp: 30), 14, null);

        // 30 - 14 = 16, at or below floor(68 / 4) = 17.
        Assert.Equal(16, plan.Final.Current);
        Assert.Equal(17, On(Stand().Sheet, "rally").AtOrBelow);
        Assert.True(Offer(plan, "rally")!.Offered);

        // Rally's outcomes switch the surge form on (which raises the maximum); choosing it changes no offer.
        var chosen = PlanFor(Stand(hp: 30), 14, null, "rally");
        Assert.Equal(Offered(plan), Offered(chosen));
        Assert.Empty(chosen.Notes);
        Assert.Collection(
            chosen.Outcomes,
            o => Assert.Equal((ResponseOutcomeKind.ToggleOn, new ToggleRef(Fixtures.SurgeForm.ContentId, "surge")), (o.Kind, o.Toggle)),
            o => Assert.Equal((ResponseOutcomeKind.Regain, Fixtures.Bulwark.ContentId, "grit", 1), (o.Kind, o.ResourceContent, o.ResourceId, o.Amount)));

        // 30 - 12 = 18 is above 17: not offered. The same hit on a sheet with the surge form on (maximum 76, threshold 19) is.
        Assert.Null(Offer(PlanFor(Stand(hp: 30), 12, null), "rally"));
        Assert.True(Offer(PlanFor(Stand(hp: 30, surge: true), 12, null), "rally")!.Offered);
    }

    [Fact]
    public void A_threshold_is_not_offered_when_hit_points_did_not_drop()
    {
        var plan = PlanFor(Stand(hp: 12, temporary: 20), 15, null);

        Assert.Equal(new HitPointState(68, 12, 5), plan.Final);
        Assert.Null(Offer(plan, "rally"));
        Assert.Null(Offer(plan, "rebound"));
    }

    [Fact]
    public void A_survived_threshold_needs_a_chosen_survival()
    {
        var without = PlanFor(Stand(hp: 10), 15, null);
        var with = PlanFor(Stand(hp: 10), 15, null, "stand");

        Assert.Null(Offer(without, "rebound"));
        Assert.True(Offer(with, "rebound")!.Offered);
        Assert.Empty(with.Notes);
        var chosen = PlanFor(Stand(hp: 10), 15, null, "stand", "rebound");
        Assert.Empty(chosen.Notes);
        Assert.Equal(ResponseOutcomeKind.OfferRoll, Assert.Single(chosen.Outcomes).Kind);
        // Choosing rebound without a survival is not eligible: a note.
        Assert.Contains(PlanFor(Stand(hp: 10), 15, null, "rebound").Notes, n => n.Code == "damage.response-unavailable");

        // A drop that is survived without a survival outcome is not "survived" in this rule's sense (a heal is not a survival).
        Assert.Null(Offer(PlanFor(Stand(hp: 30), 14, null), "rebound"));
    }

    [Fact]
    public void Massive_damage_warning_shows_beside_survival()
    {
        var plan = PlanFor(Stand(hp: 10), 90, null, "stand");

        Assert.True(plan.Result.Massive);
        Assert.Contains(plan.Result.Outcome.Warnings, w => w.Code == "play.massive-damage");
        Assert.True(Offer(plan, "stand")!.Offered);
        Assert.Equal(1, plan.Final.Current);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51, 50, 47)]
    [InlineData(RulesFamilies.Srd521, 30, 30)]
    public void The_order_holds_in_both_families(string family, int plainDc, int reducedDc)
    {
        var c = Stand(hp: 68, temporary: 200, concentrating: true);
        var policy = RulesFamilies.Get(family);

        var plain = DamagePipeline.Plan(new(100, null), c.Character, c.Sheet, [], policy);
        var reduced = DamagePipeline.Plan(new(100, null), c.Character, c.Sheet, Choose(c.Sheet, "brace"), policy);

        Assert.Equal((100, plainDc), (plain.Result.Outcome.Adjusted, plain.Result.ConcentrationDc));
        Assert.Equal((95, reducedDc), (reduced.Result.Outcome.Adjusted, reduced.Result.ConcentrationDc));
        Assert.Equal(new HitPointState(68, 68, 105), reduced.Final);
    }

    // ---- notes -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_chosen_response_that_cannot_apply_is_a_note_naming_ids_and_changes_nothing()
    {
        var c = Stand(hp: 10);
        var stand = On(c.Sheet, "stand");
        var endure = On(c.Sheet, "endure");
        var brace = On(c.Sheet, "brace");
        var ghost = new ResponseChoice(Guid.Parse("6f0dc000-0000-4000-8000-0000000000ee"), "ghost");

        DamagePlan Run(params ResponseChoice[] chosen) => DamagePipeline.Plan(new(15, null), c.Character, c.Sheet, chosen, Policy);

        // Not available (its form is off), not on the sheet, listed twice, and not eligible (a survival when the hit does not drop to 0).
        var endureChoice = new ResponseChoice(endure.ContentId, "endure");
        var braceChoice = new ResponseChoice(brace.ContentId, "brace");
        foreach (var (plan, choice) in new[] { (Run(endureChoice), "endure"), (Run(ghost), "ghost"), (Run(braceChoice, braceChoice), "brace") })
        {
            var note = Assert.Single(plan.Notes);
            Assert.Equal("damage.response-unavailable", note.Code);
            Assert.Contains(choice, note.Message, StringComparison.Ordinal);
            Assert.Empty(plan.Outcomes);
        }
        var notDropping = DamagePipeline.Plan(new(5, null), c.Character, c.Sheet, [new(stand.ContentId, "stand")], Policy);
        Assert.Contains(notDropping.Notes, n => n.Code == "damage.response-unavailable" && n.Message.Contains(stand.ContentId.ToString(), StringComparison.Ordinal));
    }

    // ---- hit point outcomes from the new kinds (G-b) ---------------------------------------------------------------------------

    [Fact]
    public void A_heal_outcome_counts_as_the_one_hit_point_outcome()
    {
        var c = Stand(hp: 10, extra: [Holder(1, AtAnyDrop)]);

        var both = PlanFor(c, 15, null, "stand", "heal");
        var healOnly = PlanFor(c, 15, null, "heal");

        Assert.Contains(both.Notes, n => n.Code == "damage.one-outcome");
        Assert.Empty(both.Outcomes);
        Assert.Empty(healOnly.Notes);
        Assert.Equal(ResponseOutcomeKind.RegainHitPoints, Assert.Single(healOnly.Outcomes).Kind);
    }

    [Fact]
    public void A_heal_applies_after_thresholds_are_decided()
    {
        var c = Stand(hp: 30, extra: [Holder(1, AtAnyDrop), Holder(2, TempAnyDrop)]);

        var without = PlanFor(c, 14, null);
        var with = PlanFor(c, 14, null, "heal");

        Assert.Equal(Offered(without), Offered(with));
        Assert.Equal(new HitPointState(68, 16, 0), with.Final);
        Assert.Equal(without.Final, with.Final);
        var heal = Assert.Single(with.Outcomes);
        Assert.Equal((ResponseOutcomeKind.RegainHitPoints, 10), (heal.Kind, heal.Amount));
        Assert.Equal(6, Assert.Single(PlanFor(c, 14, null, "warding").Outcomes).Amount);
    }

    // ---- the sheet --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Responses_on_the_sheet_carry_evaluated_amounts_and_reasons()
    {
        var (_, sheet) = Stand(hp: 68);

        var brace = On(sheet, "brace");
        Assert.Equal((Fixtures.BraceResponse.ContentId, "Fixture Brace Response", "Brace", ResponsePhase.DamageReduction), (brace.ContentId, brace.ContentName, brace.Label, brace.Phase));
        Assert.Equal((5, 1, "grit", Fixtures.Bulwark.ContentId, true, null), (brace.Amount, brace.Cost, brace.ResourceId, brace.CostResourceContent, brace.Available, brace.UnavailableReason));

        var rally = On(sheet, "rally");
        Assert.Equal((ResponsePhase.Threshold, 17, false, null), (rally.Phase, rally.AtOrBelow, rally.Survived, rally.Amount));
        Assert.Equal(2, rally.Outcomes!.Count);

        Assert.True(On(sheet, "rebound").Survived);
        Assert.Null(On(sheet, "rebound").AtOrBelow);
        var roll = Assert.Single(On(sheet, "rebound").Outcomes!);
        Assert.Equal((ResponseOutcomeKind.OfferRoll, Fixtures.Rebound, "counter", "counter"), (roll.Kind, roll.Roll, roll.RollEffectId, roll.RollId));

        var endure = On(sheet, "endure");
        Assert.False(endure.Available);
        Assert.Equal("Available in Fixture Surge Form", endure.UnavailableReason);

        var spent = Stand(hp: 68, more: p => p.WithSpent(Fixtures.LastStand.ContentId, "stand", 1).WithSpent(Fixtures.Bulwark.ContentId, "grit", 5)).Sheet;
        Assert.Equal((false, "Needs 1 Last stand (0 left)"), (On(spent, "stand").Available, On(spent, "stand").UnavailableReason));
        Assert.Equal((false, "Needs 1 Grit (0 left)"), (On(spent, "brace").Available, On(spent, "brace").UnavailableReason));
        Assert.Null(sheet.Diagnostics.FirstOrDefault(d => d.Code == "effect.invalid-formula"));
    }

    [Fact]
    public void HP_MAX_uses_the_sheet_maximum()
    {
        var off = Stand(hp: 68).Sheet;
        var on = Stand(hp: 68, surge: true).Sheet;

        Assert.Equal(68, off.HitPoints!.Maximum);
        Assert.Equal(17, On(off, "rally").AtOrBelow);
        Assert.Equal(76, on.HitPoints!.Maximum);
        Assert.Equal(19, On(on, "rally").AtOrBelow);
        // HP.MAX is not a field: the sheet carries no field for it.
        Assert.DoesNotContain(off.Fields, f => f.Field.Contains("hp.max", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void More_than_64_responses_are_dropped_with_a_diagnostic()
    {
        string Many(int from) => string.Join(",", Enumerable.Range(from, 20).Select(i =>
            $$"""{"type":"response","id":"r{{i}}","responseId":"x{{i}}","label":"X{{i}}","phase":"damageReduction","amount":"1"}"""));
        var (_, sheet) = Stand(extra: [Holder(10, Many(0)), Holder(11, Many(20)), Holder(12, Many(40)), Holder(13, Many(60))]);

        Assert.Equal(64, sheet.Responses!.Count);
        var tooMany = Assert.Single(sheet.Diagnostics, d => d.Code == "response.too-many");
        Assert.Null(tooMany.Content);
        Assert.Null(tooMany.EffectId);
        // The kept ones are the first 64 in sheet order: the fixture's own responses come first, the last holder's are all dropped.
        Assert.DoesNotContain(sheet.Responses, r => r.ResponseId == "x79");
        Assert.Contains(sheet.Responses, r => r.ResponseId == "brace");
    }

    [Fact]
    public void Exactly_64_responses_are_kept_without_a_diagnostic()
    {
        var own = Stand().Sheet.Responses!.Count;
        var effects = string.Join(",", Enumerable.Range(0, 64 - own).Select(i =>
            $$"""{"type":"response","id":"r{{i}}","responseId":"x{{i}}","label":"X","phase":"damageReduction","amount":"1"}"""));
        var (_, sheet) = Stand(extra: [Holder(20, effects)]);

        Assert.Equal(64, sheet.Responses!.Count);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "response.too-many");
    }

    [Fact]
    public void A_failing_formula_makes_the_response_unavailable_and_the_feature_assisted()
    {
        var holder = Holder(30, """{"type":"response","id":"bad","responseId":"bad","label":"Bad","phase":"damageReduction","amount":"banana"}""");
        var (_, sheet) = Stand(extra: [holder]);

        var response = On(sheet, "bad");
        Assert.False(response.Available);
        Assert.Equal("Formula failed", response.UnavailableReason);
        Assert.Null(response.Amount);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.Content == holder.Reference && d.EffectId == "bad");
        Assert.Equal(AutomationStatus.Assisted, sheet.Features!.Single(f => f.Content == holder.Reference).Automation);
    }

    [Fact]
    public void A_roll_that_cannot_be_found_makes_the_response_unavailable()
    {
        var holder = Holder(31, """{"type":"response","id":"lost","responseId":"lost","label":"Lost","phase":"threshold","when":{"survived":true},"outcomes":[{"kind":"offerRoll","rollId":"ghost"}]}""");
        var (_, sheet) = Stand(extra: [holder]);

        Assert.Equal((false, "Roll not found"), (On(sheet, "lost").Available, On(sheet, "lost").UnavailableReason));
    }

    [Fact]
    public void A_response_that_is_only_reference_text_is_not_on_the_sheet()
    {
        var holder = Holder(32, """{"type":"response","id":"note","responseId":"note","label":"Note","phase":"damageReduction","amount":"1","automation":"reference"}""");
        var (_, sheet) = Stand(extra: [holder]);

        Assert.DoesNotContain(sheet.Responses!, r => r.ResponseId == "note");
    }

    // ---- the plan's cost pass (task 7.3 review, carried into 7.4) -------------------------------------------------------------------

    private const string GritPool = "\"resourceId\":\"grit\",\"resourceContent\":\"6f0dc000-0000-4000-8000-000000000070\"";

    [Fact]
    public void An_ineligible_response_does_not_use_up_a_pool_a_later_eligible_one_needs()
    {
        // Sheet order: X (a threshold at 5 or less, costing 1 grit) then the twin reduction (costing 1 grit); 1 grit left.
        var x = Holder(60, $$"""{"type":"response","id":"x","responseId":"x","label":"X","phase":"threshold","when":{"atOrBelow":"5"},{{GritPool}},"cost":"1"}""");
        var twin = Holder(61, $$"""{"type":"response","id":"twin","responseId":"twin","label":"Twin","phase":"damageReduction","amount":"5",{{GritPool}},"cost":"1"}""");
        var c = Stand(hp: 68, extra: [x, twin], more: p => p.WithSpent(Fixtures.Bulwark.ContentId, "grit", 4));
        Assert.True(c.Sheet.Responses!.ToList().FindIndex(r => r.ResponseId == "x") < c.Sheet.Responses!.ToList().FindIndex(r => r.ResponseId == "twin"));

        var plan = PlanFor(c, 10, null, "x", "twin");

        Assert.Equal(5, plan.Reduction);
        var note = Assert.Single(plan.Notes);
        Assert.Equal("damage.response-unavailable", note.Code);
        Assert.Contains("'x'", note.Message, StringComparison.Ordinal);
        Assert.Equal(new HitPointState(68, 63, 0), plan.Final);
    }

    [Fact]
    public void A_response_that_is_both_ineligible_and_over_budget_gets_one_note()
    {
        // Each costs 1 and each is available alone (1 grit left), but together they are over budget; X is also not eligible (63 > 5).
        var twin = Holder(62, $$"""{"type":"response","id":"twin","responseId":"twin","label":"Twin","phase":"damageReduction","amount":"5",{{GritPool}},"cost":"1"}""");
        var x = Holder(64, $$"""{"type":"response","id":"x","responseId":"x","label":"X","phase":"threshold","when":{"atOrBelow":"5"},{{GritPool}},"cost":"1"}""");
        var c = Stand(hp: 68, extra: [twin, x], more: p => p.WithSpent(Fixtures.Bulwark.ContentId, "grit", 4));

        var plan = PlanFor(c, 10, null, "twin", "x");

        var note = Assert.Single(plan.Notes);
        Assert.Equal("damage.response-unavailable", note.Code);
        Assert.Contains("'x'", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chosen_threshold_is_cost_fitted_against_what_the_reductions_left()
    {
        // Rally (its own pool) is eligible after brace; a test threshold that wants the last grit fits once brace took none of it.
        var late = Holder(63, $$"""{"type":"response","id":"late","responseId":"late","label":"Late","phase":"threshold","when":{"atOrBelow":"HP.MAX"},{{GritPool}},"cost":"1"}""");
        var c = Stand(hp: 30, extra: [late], more: p => p.WithSpent(Fixtures.Bulwark.ContentId, "grit", 4));

        var braceAndLate = PlanFor(c, 20, null, "brace", "late");
        var lateAlone = PlanFor(c, 20, null, "late");

        // brace (reduction) is fitted first and takes the last grit, so the later threshold is refused.
        Assert.Equal("damage.response-unavailable", Assert.Single(braceAndLate.Notes).Code);
        Assert.Empty(lateAlone.Notes);
    }

    // ---- applying a plan (task 7.4) -------------------------------------------------------------------------------------------------

    private static (PlayState Play, IReadOnlyList<OfferedRoll> Rolls, IReadOnlyList<Diagnostic> Errors) Run(
        (Character Character, CharacterSheet Sheet) c, int amount, params string[] chosen)
    {
        var plan = PlanFor(c, amount, null, chosen);
        Assert.Empty(plan.Notes);
        return DamagePipeline.Apply(c.Character.Play, plan, c.Sheet);
    }

    private static PlayState SpentGrit(PlayState play, int spent) => play.WithSpent(Fixtures.Bulwark.ContentId, "grit", spent);

    [Fact]
    public void Applying_pays_costs_and_runs_outcomes_in_order()
    {
        var c = Stand(hp: 30, more: p => SpentGrit(p, 2));

        var (play, rolls, errors) = Run(c, 14, "rally");

        Assert.Empty(errors);
        Assert.Empty(rolls);
        Assert.Equal(16, play.CurrentHitPoints);
        Assert.Equal(1, play.SpentOf(Fixtures.Rally.ContentId, "rally")); // the response's cost
        Assert.True(play.IsOn(Fixtures.SurgeForm.ContentId, "surge"));
        Assert.Equal(1, play.SpentOf(Fixtures.Bulwark.ContentId, "grit")); // regain 1 of the 2 spent; turning the form on cost no grit

        // Regain never goes below 0 spent.
        var (none, _, noErrors) = Run(Stand(hp: 30), 14, "rally");
        Assert.Empty(noErrors);
        Assert.Equal(0, none.SpentOf(Fixtures.Bulwark.ContentId, "grit"));
        Assert.DoesNotContain(none.Resources, r => r.ResourceId == "grit");
    }

    [Fact]
    public void A_toggle_outcome_into_an_exclusive_group_switches_without_cost()
    {
        var c = Stand(hp: 30, more: p => SpentGrit(p, 3) with { Toggles = [new ActiveToggle(Fixtures.GuardForm.ContentId, "guard")] });

        var (play, _, errors) = Run(c, 14, "rally");

        Assert.Empty(errors);
        Assert.False(play.IsOn(Fixtures.GuardForm.ContentId, "guard"));
        Assert.True(play.IsOn(Fixtures.SurgeForm.ContentId, "surge"));
        Assert.Equal(2, play.SpentOf(Fixtures.Bulwark.ContentId, "grit")); // only the rally's regain of 1: no cost for the switch, no refund for the form that went off
        Assert.Equal([new ActiveToggle(Fixtures.SurgeForm.ContentId, "surge")], play.Toggles);
    }

    [Fact]
    public void A_toggle_outcome_ends_overlays_that_needed_the_old_form_and_counts_rounds_in_an_encounter()
    {
        var aura = Holder(50, """{"type":"toggle","id":"aura","toggleId":"aura","label":"Aura","group":"forms","groupContent":"6f0dc000-0000-4000-8000-000000000074","layer":"overlay","allowedWith":[{"contentId":"6f0dc000-0000-4000-8000-000000000077","toggleId":"guard"}]}""");
        var c = Stand(hp: 30, extra: [aura], more: p => p with { Toggles = [new ActiveToggle(Fixtures.GuardForm.ContentId, "guard"), new ActiveToggle(aura.ContentId, "aura")], Encounter = new(2) });
        Assert.True(c.Sheet.Toggles!.Single(t => t.ToggleId == "aura").On);

        var (play, _, errors) = Run(c, 14, "rally");

        Assert.Empty(errors);
        // The guard form and the overlay that needed it are gone; the surge form (no duration) has no count.
        Assert.Equal([new ActiveToggle(Fixtures.SurgeForm.ContentId, "surge")], play.Toggles);
    }

    [Fact]
    public void An_offered_roll_is_listed_and_rolls_nothing()
    {
        var c = Stand(hp: 10);

        var (play, rolls, errors) = Run(c, 15, "stand", "rebound");

        Assert.Empty(errors);
        var roll = Assert.Single(rolls);
        Assert.Equal(new OfferedRoll(Fixtures.Rebound, "counter", "counter", "Counter"), roll);
        Assert.Equal(1, play.CurrentHitPoints);
        Assert.Equal(1, play.SpentOf(Fixtures.LastStand.ContentId, "stand"));
        Assert.Equal(c.Character.Play.DeathSaves, play.DeathSaves);
    }

    [Fact]
    public void Apply_is_all_or_nothing()
    {
        // Brace (a reduction) pays first and fits; rally then cannot pay because its pool is spent in the play state being applied to.
        var c = Stand(hp: 30);
        var plan = PlanFor(c, 20, null, "brace", "rally");
        Assert.Empty(plan.Notes);
        var spent = c.Character.Play.WithSpent(Fixtures.Rally.ContentId, "rally", 1);

        var (play, rolls, errors) = DamagePipeline.Apply(spent, plan, c.Sheet);

        Assert.Same(spent, play);
        Assert.Empty(rolls);
        var error = Assert.Single(errors);
        Assert.Equal("damage.response-unavailable", error.Code);
        Assert.Contains("rally", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_with_notes_is_returned_unchanged_with_those_notes()
    {
        var c = Stand(hp: 10);
        var plan = PlanFor(c, 15, null, "endure"); // not available outside the surge form

        var (play, rolls, errors) = DamagePipeline.Apply(c.Character.Play, plan, c.Sheet);

        Assert.Same(c.Character.Play, play);
        Assert.Empty(rolls);
        Assert.Equal(plan.Notes, errors);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void The_applied_play_state_validates()
    {
        var c = Stand(hp: 10, concentrating: true);
        var (play, _, errors) = Run(c, 15, "stand", "rebound");

        Assert.Empty(errors);
        Assert.Empty(play.Validate());

        // A plan that would leave an invalid state is refused whole, with the validation codes.
        var plan = PlanFor(c, 15, null, "stand");
        var broken = plan with { Final = plan.Final with { Temporary = PlayState.MaxHitPoints + 1 } };
        var (same, rolls, problems) = DamagePipeline.Apply(c.Character.Play, broken, c.Sheet);
        Assert.Same(c.Character.Play, same);
        Assert.Empty(rolls);
        Assert.Contains(problems, d => d.Code == "play.hit-points-out-of-range");
    }

    [Fact]
    public void A_survival_keeps_concentration_and_sets_its_dc()
    {
        var c = Stand(hp: 10, concentrating: true, more: p => p with { DeathSaves = new DeathSaves(1, 1) });

        var (play, _, errors) = Run(c, 15, "stand");

        Assert.Empty(errors);
        Assert.Equal(1, play.CurrentHitPoints);
        Assert.Equal(new Concentration(Fixtures.Bulwark, "Held", 10), play.Concentration);
        Assert.Equal(new DeathSaves(1, 1), play.DeathSaves);
    }

    [Fact]
    public void A_hit_to_zero_without_a_survival_ends_concentration()
    {
        var (play, _, errors) = Run(Stand(hp: 10, concentrating: true), 15);

        Assert.Empty(errors);
        Assert.Equal(0, play.CurrentHitPoints);
        Assert.Null(play.Concentration);
    }

    [Fact]
    public void Temporary_hit_points_keep_the_higher()
    {
        var ward = Holder(51, """{"type":"response","id":"ward8","responseId":"ward8","label":"Ward","phase":"damageReduction","amount":"1","outcomes":[{"kind":"temporaryHitPoints","amount":"8"}]}""");

        // 3 temporary, 3 damage less 1 reduced: 2 absorbed, 1 left; the outcome's 8 is higher.
        var (low, _, lowErrors) = Run(Stand(hp: 68, temporary: 3, extra: [ward]), 3, "ward8");
        // 14 temporary, 4 damage less 1: 11 left, which is higher than 8.
        var (high, _, highErrors) = Run(Stand(hp: 68, temporary: 14, extra: [ward]), 4, "ward8");

        Assert.Empty(lowErrors);
        Assert.Empty(highErrors);
        Assert.Equal(8, low.TemporaryHitPoints);
        Assert.Equal(11, high.TemporaryHitPoints);
    }

    [Fact]
    public void A_heal_outcome_applies_after_the_hit_and_is_clamped()
    {
        var big = Holder(52, """{"type":"response","id":"bigheal","responseId":"bigheal","label":"Big heal","phase":"threshold","when":{"atOrBelow":"HP.MAX"},"outcomes":[{"kind":"regainHitPoints","amount":"100"}]}""");
        var c = Stand(hp: 30, extra: [Holder(1, AtAnyDrop), big]);

        var (healed, _, errors) = Run(c, 14, "heal");
        var (clamped, _, bigErrors) = Run(c, 14, "bigheal");

        Assert.Empty(errors);
        Assert.Empty(bigErrors);
        Assert.Equal(26, healed.CurrentHitPoints); // 30 - 14 = 16, then +10
        Assert.Null(clamped.CurrentHitPoints); // up to the maximum, stored as null
    }

    [Fact]
    public void A_heal_from_zero_resets_death_saves()
    {
        var mend = Holder(53, """{"type":"response","id":"mend","responseId":"mend","label":"Mend","phase":"damageReduction","amount":"1","outcomes":[{"kind":"regainHitPoints","amount":"5"}]}""");
        var c = Stand(hp: 0, extra: [mend], more: p => p with { DeathSaves = new DeathSaves(1, 1) });

        var (play, _, errors) = Run(c, 1, "mend");

        Assert.Empty(errors);
        Assert.Equal(5, play.CurrentHitPoints);
        Assert.Equal(new DeathSaves(), play.DeathSaves);
    }

    [Fact]
    public void Apply_stores_full_hit_points_as_null()
    {
        Assert.Null(new PlayState { CurrentHitPoints = 3 }.WithHitPoints(68, 4, 68).CurrentHitPoints);
        Assert.Equal(4, new PlayState().WithHitPoints(68, 4, 68).TemporaryHitPoints);
        Assert.Equal(67, new PlayState().WithHitPoints(67, 0, 68).CurrentHitPoints);
        Assert.Null(new PlayState { CurrentHitPoints = 3 }.WithCurrentHitPoints(70, 68).CurrentHitPoints);
        // Through Apply: damage fully absorbed by temporary hit points leaves the character at full hit points, stored as null.
        var (play, _, errors) = Run(Stand(hp: 68, temporary: 20), 5);
        Assert.Empty(errors);
        Assert.Null(play.CurrentHitPoints);
        Assert.Equal(15, play.TemporaryHitPoints);
    }

    [Fact]
    public void A_response_unavailable_at_apply_time_names_the_ids_only()
    {
        // The pool is spent between planning and applying (rally's cost cannot be paid), so apply refuses and changes nothing.
        var c = Stand(hp: 30);
        var plan = PlanFor(c, 14, null, "rally");
        var spent = c.Character.Play.WithSpent(Fixtures.Rally.ContentId, "rally", 1);

        var (play, _, errors) = DamagePipeline.Apply(spent, plan, c.Sheet);

        Assert.Same(spent, play);
        var error = Assert.Single(errors);
        Assert.Contains(Fixtures.Rally.ContentId.ToString(), error.Message, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> ServiceRows() =>
        typeof(DamagePipelineTests).GetMethod(nameof(DamagePipelineTests.Resolve_matches_the_service_for_every_existing_case))!
            .GetCustomAttributes<InlineDataAttribute>()
            .SelectMany(a => a.GetData(null!))
            // The columns this test uses: the inputs (0-7), the amount after defences (9), and the hit points, temporary hit points, DC and end flag after (12-15).
            .Select(row => new[] { row[0], row[1], row[2], row[3], row[4], row[5], row[6], row[7], row[9], row[12], row[13], row[14], row[15] });

    [Theory]
    [MemberData(nameof(ServiceRows))]
    public void Plan_then_apply_matches_plain_damage_without_responses(
        string family, int amount, string? type, string defences, int temporary, int current, int maximum, bool concentrating,
        int adjusted, int afterCurrent, int afterTemporary, int? dc, bool ends)
    {
        var policy = RulesFamilies.Get(family);
        var character = Fixtures.Load("pm-bulwark-l8.json");
        var play = new PlayState
        {
            CurrentHitPoints = current >= maximum ? null : current,
            TemporaryHitPoints = temporary,
            Concentration = concentrating ? new Concentration(Fixtures.Bulwark, "Held") : null,
        };
        character = character with { Play = play };
        var sheet = new CharacterSheet(
            character.Id, family, [], [], HitPoints: new(maximum, current, temporary),
            DamageDefences: [.. Defences(defences).Select(d => new DamageDefenceValue(d.Type, d.Kind, []))]);

        var plan = DamagePipeline.Plan(new(amount, type), character, sheet, [], policy);
        var (applied, rolls, errors) = DamagePipeline.Apply(play, plan, sheet);

        Assert.Empty(errors);
        Assert.Empty(rolls);
        // What the service's plain damage writes: nothing for a hit of 0 after defences, else the hit points (null at the maximum),
        // the temporary hit points, and the concentration (ended, or kept with the pending DC).
        if (adjusted == 0)
        {
            Assert.Same(play, applied);
            return;
        }
        Assert.Equal(afterCurrent >= maximum ? null : afterCurrent, applied.CurrentHitPoints);
        Assert.Equal(afterTemporary, applied.TemporaryHitPoints);
        var expectedConcentration = ends ? null : dc is { } pending && play.Concentration is { } held ? held with { PendingSaveDc = pending } : play.Concentration;
        Assert.Equal(expectedConcentration, applied.Concentration);
        Assert.Equal(play.DeathSaves, applied.DeathSaves);
        Assert.Equal(play.Toggles, applied.Toggles);
    }

    // ---- no responses = task 7.1 -------------------------------------------------------------------------------------------------

    private static IReadOnlyList<DamageDefence> Defences(string spec) =>
        spec.Length == 0
            ? []
            : [.. spec.Split(',').Select(part =>
            {
                var pieces = part.Split(':');
                return new DamageDefence(pieces[0], pieces[1] switch { "R" => DamageDefenceKind.Resistance, "I" => DamageDefenceKind.Immunity, _ => DamageDefenceKind.Vulnerability });
            })];

    [Theory]
    [InlineData(RulesFamilies.Srd51, 13, "slashing", "slashing:R", 5, 35, 35, true)]
    [InlineData(RulesFamilies.Srd521, 13, "slashing", "slashing:R", 5, 35, 35, true)]
    [InlineData(RulesFamilies.Srd521, 7, null, "", 100, 24, 24, true)]
    [InlineData(RulesFamilies.Srd521, 30, null, "", 100, 24, 24, true)]
    [InlineData(RulesFamilies.Srd521, 24, null, "", 5, 35, 35, true)]
    [InlineData(RulesFamilies.Srd521, 500, null, "", 0, 24, 24, true)]
    [InlineData(RulesFamilies.Srd51, 60, "slashing", "slashing:R,cold:V", 1000, 24, 24, true)]
    [InlineData(RulesFamilies.Srd521, 60, "slashing", "slashing:R,cold:V", 1000, 24, 24, true)]
    [InlineData(RulesFamilies.Srd51, 40, "cold", "slashing:R,cold:V", 1000, 24, 24, true)]
    [InlineData(RulesFamilies.Srd521, 40, "cold", "slashing:R,cold:V", 1000, 24, 24, true)]
    [InlineData(RulesFamilies.Srd51, 30, "cold", "slashing:R,cold:V", 50, 24, 24, true)]
    [InlineData(RulesFamilies.Srd521, 30, "cold", "slashing:R,cold:V", 50, 24, 24, true)]
    [InlineData(RulesFamilies.Srd51, 13, "fire", "fire:I", 5, 24, 24, true)]
    [InlineData(RulesFamilies.Srd521, 13, "cold", "cold:V,slashing:R,slashing:V", 5, 35, 35, false)]
    [InlineData(RulesFamilies.Srd521, 13, "slashing", "cold:V,slashing:R,slashing:V", 0, 14, 35, false)]
    [InlineData(RulesFamilies.Srd521, 13, "slashing", "slashing:R,slashing:R,slashing:R", 0, 35, 35, false)]
    [InlineData(RulesFamilies.Srd521, 13, null, "slashing:R,fire:I", 0, 35, 35, false)]
    [InlineData(RulesFamilies.Srd521, 4, "acid", "slashing:R,fire:I", 0, 35, 35, false)]
    [InlineData(RulesFamilies.Srd521, 12, null, "", 5, 35, 35, false)]
    [InlineData(RulesFamilies.Srd521, 69, "fire", "", 0, 35, 35, false)]
    [InlineData(RulesFamilies.Srd521, 70, "fire", "", 0, 35, 35, false)]
    [InlineData(RulesFamilies.Srd521, 80, null, "", 10, 35, 35, false)]
    [InlineData(RulesFamilies.Srd521, 140, "slashing", "slashing:R", 0, 35, 35, false)]
    [InlineData(RulesFamilies.Srd521, 138, "slashing", "slashing:R", 0, 35, 35, false)]
    [InlineData(RulesFamilies.Srd521, 9, "fire", "fire:V", 0, 35, 35, false)]
    public void No_responses_chosen_matches_task_7_1(string family, int amount, string? type, string defences, int temporary, int current, int maximum, bool concentrating)
    {
        var policy = RulesFamilies.Get(family);
        var hp = new HitPointState(maximum, current, temporary);
        var character = Fixtures.Load("pm-bulwark-l8.json");
        character = character with { Play = character.Play with { Concentration = concentrating ? new Concentration(Fixtures.Bulwark, "Held") : null } };
        var sheet = new CharacterSheet(
            character.Id, family, [], [], HitPoints: hp,
            DamageDefences: [.. Defences(defences).Select(d => new DamageDefenceValue(d.Type, d.Kind, []))]);

        var expected = DamagePipeline.Resolve(new(amount, type), hp, Defences(defences), concentrating, policy);
        var plan = DamagePipeline.Plan(new(amount, type), character, sheet, [], policy);

        Assert.Empty(plan.Notes);
        Assert.Empty(plan.Offers);
        Assert.Empty(plan.Outcomes);
        Assert.Equal(0, plan.Reduction);
        Assert.Equal(expected.After, plan.Result.After);
        Assert.Equal(expected.After, plan.Final);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(plan.Result));
    }
}
