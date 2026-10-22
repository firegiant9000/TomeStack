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
}
