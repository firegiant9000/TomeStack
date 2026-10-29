using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>D43 S7 task 7.1: for any valid state, any damage and any defences, hit points and temporary hit points stay valid.</summary>
public class DamagePipelineProperties
{
    private static readonly string[] Types = [.. DamageTypes.All];

    private static Gen<HitPointState> States { get; } =
        from maximum in Gen.Choose(0, PlayState.MaxHitPoints)
        from current in Gen.Choose(0, maximum)
        from temporary in Gen.Choose(0, PlayState.MaxHitPoints)
        select new HitPointState(maximum, current, temporary);

    private static Gen<DamageDefence> Defence { get; } =
        from type in Gen.Elements(Types)
        from kind in Gen.Elements(DamageDefenceKind.Resistance, DamageDefenceKind.Immunity, DamageDefenceKind.Vulnerability)
        select new DamageDefence(type, kind);

    private static Gen<DamageEvent> Damage { get; } =
        from amount in Gen.Choose(0, PlayState.MaxHitPoints)
        from type in Gen.Elements(Types.Cast<string?>().Append(null).ToArray())
        select new DamageEvent(amount, type);

    private static Gen<(HitPointState Hp, DamageEvent Damage, DamageDefence[] Defences, bool Concentrating, string Family)> Cases { get; } =
        from hp in States
        from damage in Damage
        from defences in Defence.ListOf().Select(l => l.ToArray())
        from concentrating in Gen.Elements(true, false)
        from family in Gen.Elements(RulesFamilies.Srd51, RulesFamilies.Srd521)
        select (hp, damage, defences, concentrating, family);

    private static DamageResult Run((HitPointState Hp, DamageEvent Damage, DamageDefence[] Defences, bool Concentrating, string Family) c) =>
        DamagePipeline.Resolve(c.Damage, c.Hp, c.Defences, c.Concentrating, RulesFamilies.Get(c.Family));

    [Property(MaxTest = 300)]
    public Property Hit_points_stay_within_zero_and_the_maximum() =>
        Prop.ForAll(Cases.ToArbitrary(), c =>
        {
            var after = Run(c).After;
            return after.Maximum == c.Hp.Maximum && after.Current >= 0 && after.Current <= c.Hp.Maximum && after.Current <= c.Hp.Current;
        });

    [Property(MaxTest = 300)]
    public Property Temporary_hit_points_never_go_negative() =>
        Prop.ForAll(Cases.ToArbitrary(), c =>
        {
            var after = Run(c).After;
            return after.Temporary >= 0 && after.Temporary <= c.Hp.Temporary;
        });

    // ---- D43 S7 task 7.3: planning with responses ---------------------------------------------------------------------------------

    // The Bulwark with a heal and a temporary hit point response added, once with the surge form off and once on. Each case reuses the
    // calculated sheet and changes only the hit points (the plan reads sheet.HitPoints), so a case costs nothing to set up.
    private static readonly IReadOnlyList<ContentRevision> HitPointResponses =
    [
        DamageResponseTests.Holder(1, """{"type":"response","id":"heal","responseId":"heal","label":"Heal","phase":"threshold","when":{"atOrBelow":"HP.MAX"},"outcomes":[{"kind":"regainHitPoints","amount":"10"}]}"""),
        DamageResponseTests.Holder(2, """{"type":"response","id":"warding","responseId":"warding","label":"Warding","phase":"threshold","when":{"atOrBelow":"HP.MAX"},"outcomes":[{"kind":"temporaryHitPoints","amount":"6"}]}"""),
    ];

    private static readonly (Character Character, CharacterSheet Sheet)[] ResponseSheets =
    [
        DamageResponseTests.Stand(extra: HitPointResponses),
        DamageResponseTests.Stand(surge: true, extra: HitPointResponses),
    ];

    private static Gen<(int Sheet, int Current, int Temporary, int Amount, string? Type, bool Concentrating, int Mask)> ResponseCases { get; } =
        from sheet in Gen.Choose(0, ResponseSheets.Length - 1)
        from biased in Gen.Elements(true, false)
        from current in biased ? Gen.Choose(1, 10) : Gen.Choose(0, 68)
        from temporary in Gen.Elements(0, 0, 5, 20)
        from amount in biased ? Gen.Elements(90, 150) : Gen.Elements(0, 1, 5, 14, 15, 20, 60, 90, 150)
        from type in Gen.Elements<string?>("fire", null, "cold")
        from concentrating in Gen.Elements(true, false)
        // Biased cases pick any subset of the hit point outcomes (stand, endure, heal, warding) so two of them are often chosen together.
        from mask in biased ? Gen.Choose(0, 15).Select(HitPointMask) : Gen.Choose(0, (1 << ResponseSheets[0].Sheet.Responses!.Count) - 1)
        select (sheet, current, temporary, amount, type, concentrating, mask);

    private static int HitPointMask(int subset)
    {
        string[] ids = ["stand", "endure", "heal", "warding"];
        var responses = ResponseSheets[0].Sheet.Responses!;
        var mask = 0;
        for (var i = 0; i < ids.Length; i++)
        {
            if ((subset & (1 << i)) != 0)
            {
                var index = responses.ToList().FindIndex(r => r.ResponseId == ids[i]);
                mask |= 1 << (index >= 0 ? index : throw new InvalidOperationException($"The test sheet has no response '{ids[i]}'."));
            }
        }
        return mask;
    }

    private static DamagePlan PlanOf((int Sheet, int Current, int Temporary, int Amount, string? Type, bool Concentrating, int Mask) c)
    {
        var (character, sheet) = ResponseSheets[c.Sheet];
        sheet = sheet with { HitPoints = new HitPointState(sheet.HitPoints!.Maximum, Math.Min(c.Current, sheet.HitPoints.Maximum), c.Temporary) };
        character = character with { Play = character.Play with { Concentration = c.Concentrating ? new Concentration(Fixtures.Bulwark, "Held") : null } };
        var chosen = sheet.Responses!.Where((_, i) => (c.Mask & (1 << i)) != 0).Select(r => new ResponseChoice(r.ContentId, r.ResponseId)).ToList();
        return DamagePipeline.Plan(new(c.Amount, c.Type), character, sheet, chosen, RulesFamilies.Get(RulesFamilies.Srd521));
    }

    private static bool IsHitPointOutcome(ResponseValue response) =>
        response.Phase == ResponsePhase.HitPointOutcome
        || (response.Outcomes ?? []).Any(o => o.Kind is ResponseOutcomeKind.RegainHitPoints or ResponseOutcomeKind.TemporaryHitPoints);

    [Property(MaxTest = 300)]
    public Property At_most_one_hit_point_outcome_is_ever_applied() =>
        Prop.ForAll(ResponseCases.ToArbitrary(), c =>
        {
            var plan = PlanOf(c);
            var sheet = ResponseSheets[c.Sheet].Sheet;
            var chosen = plan.Chosen.Select(x => sheet.Responses!.Single(r => r.ContentId == x.ContentId && r.ResponseId == x.ResponseId)).ToList();
            // Two or more hit point outcomes that are each eligible and available are always refused.
            var usableHitPointOutcomes = chosen.Count(r => IsHitPointOutcome(r) && plan.Offers.Any(o => o.Offered && o.Response == r));
            if (usableHitPointOutcomes >= 2 && !plan.Notes.Any(n => n.Code == "damage.one-outcome"))
                return false;
            if (plan.Notes.Count > 0)
                return plan.Outcomes.Count == 0;
            var healOrTemporary = plan.Outcomes.Count(o => o.Kind is ResponseOutcomeKind.RegainHitPoints or ResponseOutcomeKind.TemporaryHitPoints);
            return chosen.Count(IsHitPointOutcome) <= 1 && healOrTemporary <= 1;
        });

    [Property(MaxTest = 300)]
    public Property Planning_is_deterministic() =>
        Prop.ForAll(ResponseCases.ToArbitrary(), c => JsonSerializer.Serialize(PlanOf(c)) == JsonSerializer.Serialize(PlanOf(c)));
}
