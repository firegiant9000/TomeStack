namespace TomeStack.RulesCore.Tests;

/// <summary>
/// D43 S7 task 7.1: typed damage as a pure pipeline. Each row mirrors a Wave 3 damage case in the service tests
/// (<c>PlayCommandTests</c>) as a pure call; the pipeline must give exactly what the service gave.
/// </summary>
public class DamagePipelineTests
{
    // "slashing:R,cold:V" -> resistance to slashing, vulnerability to cold (R resistance, I immunity, V vulnerability).
    private static IReadOnlyList<DamageDefence> Defences(string spec) =>
        spec.Length == 0
            ? []
            : [.. spec.Split(',').Select(part =>
            {
                var pieces = part.Split(':');
                return new DamageDefence(pieces[0], pieces[1] switch { "R" => DamageDefenceKind.Resistance, "I" => DamageDefenceKind.Immunity, _ => DamageDefenceKind.Vulnerability });
            })];

    [Theory]
    // Review focus 3: 13 slashing, halved to 6, 5 temporary absorb, 1 lost; DC is max(10, 6 / 2).
    [InlineData(RulesFamilies.Srd51, 13, "slashing", "slashing:R", 5, 35, 35, true, DamageEffect.Halved, 6, 5, 1, 34, 0, 10, false, false)]
    [InlineData(RulesFamilies.Srd521, 13, "slashing", "slashing:R", 5, 35, 35, true, DamageEffect.Halved, 6, 5, 1, 34, 0, 10, false, false)]
    // Plain concentration DCs (7 -> 10, 30 -> 15) with temporary hit points keeping the caster up.
    [InlineData(RulesFamilies.Srd521, 7, null, "", 100, 24, 24, true, DamageEffect.None, 7, 7, 0, 24, 93, 10, false, false)]
    [InlineData(RulesFamilies.Srd521, 30, null, "", 100, 24, 24, true, DamageEffect.None, 30, 30, 0, 24, 70, 15, false, false)]
    // 24 with 5 temporary: the DC uses the 24 dealt.
    [InlineData(RulesFamilies.Srd521, 24, null, "", 5, 35, 35, true, DamageEffect.None, 24, 5, 19, 16, 0, 12, false, false)]
    // Damage to 0 ends concentration (and the hit is massive here).
    [InlineData(RulesFamilies.Srd521, 500, null, "", 0, 24, 24, true, DamageEffect.None, 500, 0, 24, 0, 0, null, true, true)]
    // The DC comes from the amount after defences and before temporary hit points.
    [InlineData(RulesFamilies.Srd51, 60, "slashing", "slashing:R,cold:V", 1000, 24, 24, true, DamageEffect.Halved, 30, 30, 0, 24, 970, 15, false, false)]
    [InlineData(RulesFamilies.Srd521, 60, "slashing", "slashing:R,cold:V", 1000, 24, 24, true, DamageEffect.Halved, 30, 30, 0, 24, 970, 15, false, false)]
    [InlineData(RulesFamilies.Srd51, 40, "cold", "slashing:R,cold:V", 1000, 24, 24, true, DamageEffect.Doubled, 80, 80, 0, 24, 920, 40, false, false)]
    [InlineData(RulesFamilies.Srd521, 40, "cold", "slashing:R,cold:V", 1000, 24, 24, true, DamageEffect.Doubled, 80, 80, 0, 24, 920, 30, false, false)]
    [InlineData(RulesFamilies.Srd51, 30, "cold", "slashing:R,cold:V", 50, 24, 24, true, DamageEffect.Doubled, 60, 50, 10, 14, 0, 30, false, false)]
    [InlineData(RulesFamilies.Srd521, 30, "cold", "slashing:R,cold:V", 50, 24, 24, true, DamageEffect.Doubled, 60, 50, 10, 14, 0, 30, false, false)]
    // Immunity takes nothing and asks for no check; the state is unchanged.
    [InlineData(RulesFamilies.Srd51, 13, "fire", "fire:I", 5, 24, 24, true, DamageEffect.Immune, 0, 0, 0, 24, 5, null, false, false)]
    [InlineData(RulesFamilies.Srd521, 13, "fire", "fire:I", 5, 24, 24, true, DamageEffect.Immune, 0, 0, 0, 24, 5, null, false, false)]
    // Vulnerability doubles before temporary hit points; resistance with vulnerability halves (down) then doubles.
    [InlineData(RulesFamilies.Srd521, 13, "cold", "cold:V,slashing:R,slashing:V", 5, 35, 35, false, DamageEffect.Doubled, 26, 5, 21, 14, 0, null, false, false)]
    [InlineData(RulesFamilies.Srd521, 13, "slashing", "cold:V,slashing:R,slashing:V", 0, 14, 35, false, DamageEffect.HalvedThenDoubled, 12, 0, 12, 2, 0, null, false, false)]
    // Several resistances to one type count once.
    [InlineData(RulesFamilies.Srd521, 13, "slashing", "slashing:R,slashing:R,slashing:R", 0, 35, 35, false, DamageEffect.Halved, 6, 0, 6, 29, 0, null, false, false)]
    // Untyped damage ignores every defence; a type without a defence is unchanged.
    [InlineData(RulesFamilies.Srd521, 13, null, "slashing:R,fire:I", 0, 35, 35, false, DamageEffect.None, 13, 0, 13, 22, 0, null, false, false)]
    [InlineData(RulesFamilies.Srd521, 4, "acid", "slashing:R,fire:I", 0, 35, 35, false, DamageEffect.None, 4, 0, 4, 31, 0, null, false, false)]
    // Plain temporary-hit-point absorption (12 with 5 temporary: 28 of 35 left).
    [InlineData(RulesFamilies.Srd521, 12, null, "", 5, 35, 35, false, DamageEffect.None, 12, 5, 7, 28, 0, null, false, false)]
    // Massive damage: 69 leaves 34 over (one under the maximum of 35), 70 leaves exactly 35.
    [InlineData(RulesFamilies.Srd521, 69, "fire", "", 0, 35, 35, false, DamageEffect.None, 69, 0, 35, 0, 0, null, false, false)]
    [InlineData(RulesFamilies.Srd521, 70, "fire", "", 0, 35, 35, false, DamageEffect.None, 70, 0, 35, 0, 0, null, false, true)]
    [InlineData(RulesFamilies.Srd521, 80, null, "", 10, 35, 35, false, DamageEffect.None, 80, 10, 35, 0, 0, null, false, true)]
    // Resistance can keep a hit from being massive: 140 -> 70 (35 left over), 138 -> 69 (34).
    [InlineData(RulesFamilies.Srd521, 140, "slashing", "slashing:R", 0, 35, 35, false, DamageEffect.Halved, 70, 0, 35, 0, 0, null, false, true)]
    [InlineData(RulesFamilies.Srd521, 138, "slashing", "slashing:R", 0, 35, 35, false, DamageEffect.Halved, 69, 0, 35, 0, 0, null, false, false)]
    // Doubled to 18 leaves 17 of 35.
    [InlineData(RulesFamilies.Srd521, 9, "fire", "fire:V", 0, 35, 35, false, DamageEffect.Doubled, 18, 0, 18, 17, 0, null, false, false)]
    public void Resolve_matches_the_service_for_every_existing_case(
        string family, int amount, string? type, string defences, int temporary, int current, int maximum, bool concentrating,
        DamageEffect effect, int adjusted, int absorbed, int lost, int afterCurrent, int afterTemporary, int? dc, bool ends, bool massive)
    {
        var result = DamagePipeline.Resolve(new(amount, type), new(maximum, current, temporary), Defences(defences), concentrating, RulesFamilies.Get(family));

        Assert.Equal((amount, type, effect, adjusted, absorbed, lost), (result.Outcome.Amount, result.Outcome.Type, result.Outcome.Effect, result.Outcome.Adjusted, result.Outcome.Absorbed, result.Outcome.Lost));
        Assert.Equal(new HitPointState(maximum, afterCurrent, afterTemporary), result.After);
        Assert.Equal((dc, ends, massive), (result.ConcentrationDc, result.ConcentrationEnds, result.Massive));
        Assert.Equal(massive, result.Outcome.Warnings.Count == 1);
    }

    [Fact]
    public void A_form_only_resistance_halves_damage_only_in_the_form()
    {
        var family = RulesFamilies.Get(RulesFamilies.Srd521);
        var catalog = Fixtures.PlayMechanicsCatalog();
        var character = Fixtures.Load("pm-instinct-l4.json");
        var inForm = character with { Play = character.Play with { Toggles = [new(Fixtures.SkyForm.ContentId, "sky")] } };

        IReadOnlyList<DamageDefence> Map(Character c) =>
            CharacterCalculator.Calculate(c, catalog).DamageDefences?.Select(d => new DamageDefence(d.Type, d.Kind)).ToList() ?? [];

        var on = DamagePipeline.Resolve(new(10, "fire"), new(35, 35, 0), Map(inForm), false, family);
        var off = DamagePipeline.Resolve(new(10, "fire"), new(35, 35, 0), Map(character), false, family);

        Assert.Equal(5, on.Outcome.Adjusted);
        Assert.Equal(10, off.Outcome.Adjusted);
    }

    [Fact]
    public void A_massive_hit_carries_the_warning_code_and_the_left_over_number()
    {
        var result = DamagePipeline.Resolve(new(70, "fire"), new(35, 35, 0), [], false, RulesFamilies.Get(RulesFamilies.Srd521));

        var warning = Assert.Single(result.Outcome.Warnings);
        Assert.Equal("play.massive-damage", warning.Code);
        Assert.Equal("35 damage was left over after reaching 0 hit points, which equals or exceeds the hit point maximum of 35.", warning.Message);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51, 150, 75)] // no family cap: the DC is half the damage, above 30
    [InlineData(RulesFamilies.Srd521, 150, 30)] // the family caps it at 30
    public void Concentration_dc_is_capped_by_family(string family, int amount, int expected)
    {
        var result = DamagePipeline.Resolve(new(amount, null), new(100, 100, 10000), [], true, RulesFamilies.Get(family));

        Assert.Equal(expected, result.ConcentrationDc);
        Assert.False(result.ConcentrationEnds);
    }

    [Fact]
    public void Concentration_dc_never_exceeds_what_play_state_accepts()
    {
        var result = DamagePipeline.Resolve(new(PlayState.MaxHitPoints, null), new(100, 100, PlayState.MaxHitPoints), [], true, RulesFamilies.Get(RulesFamilies.Srd51));

        Assert.Equal(PlayState.MaxConcentrationSaveDc, result.ConcentrationDc);
    }
}
