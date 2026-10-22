namespace TomeStack.RulesCore;

/// <summary>How the character's defences changed typed damage (R169): immunity wins; otherwise resistance halves (rounding down), then vulnerability doubles.</summary>
public enum DamageEffect { None, Immune, Halved, Doubled, HalvedThenDoubled }

/// <summary>
/// What a <c>damage</c> play command did, for the result line (Wave 3 item 21, D19 amended).
/// <paramref name="Amount"/> is what was entered and <paramref name="Type"/> its canonical damage type (null: untyped, so no
/// defence applies). <paramref name="Adjusted"/> is the amount after defences (what the concentration check uses),
/// <paramref name="Absorbed"/> what temporary hit points took, <paramref name="Lost"/> what hit points lost.
/// <paramref name="Warnings"/> carries codes and numbers only (<c>play.massive-damage</c>).
/// </summary>
public sealed record DamageOutcome(int Amount, string? Type, DamageEffect Effect, int Adjusted, int Absorbed, int Lost, IReadOnlyList<Diagnostic> Warnings);

/// <summary>One hit: <paramref name="Type"/> is already canonical (null: untyped, so no defence applies).</summary>
public sealed record DamageEvent(int Amount, string? Type);

/// <summary>
/// The hit points after a hit, what happened (<paramref name="Outcome"/>), the Constitution save DC when a concentrating
/// character is still up (<paramref name="ConcentrationDc"/>), whether concentration ends because hit points reached 0, and
/// whether the hit was massive (the <c>play.massive-damage</c> warning is in <paramref name="Outcome"/>).
/// </summary>
public sealed record DamageResult(HitPointState After, DamageOutcome Outcome, int? ConcentrationDc, bool ConcentrationEnds, bool Massive);

public static class DamagePipeline
{
    /// <summary>
    /// Wave 3 item 21 (D19 amended, R169). The order: immunity gives 0, otherwise resistance halves (rounding down) and then
    /// vulnerability doubles, each kind counting once however many sources give it; then temporary hit points absorb; then hit
    /// points. Untyped damage ignores every defence. Damage that is 0 after defences changes nothing and asks for no
    /// concentration check.
    /// </summary>
    public static DamageResult Resolve(DamageEvent damage, HitPointState hp, IReadOnlyList<DamageDefence> defences, bool concentrating, RulesFamilyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(damage);
        ArgumentNullException.ThrowIfNull(hp);
        ArgumentNullException.ThrowIfNull(defences);
        ArgumentNullException.ThrowIfNull(policy);
        var type = damage.Type;
        var kinds = type is null
            ? new HashSet<DamageDefenceKind>()
            : defences.Where(d => d.Type == type).Select(d => d.Kind).ToHashSet();
        var halved = kinds.Contains(DamageDefenceKind.Resistance);
        var doubled = kinds.Contains(DamageDefenceKind.Vulnerability);
        var effect = kinds.Contains(DamageDefenceKind.Immunity) ? DamageEffect.Immune
            : halved && doubled ? DamageEffect.HalvedThenDoubled
            : halved ? DamageEffect.Halved
            : doubled ? DamageEffect.Doubled
            : DamageEffect.None;
        var amount = effect switch
        {
            DamageEffect.Immune => 0,
            DamageEffect.Halved => damage.Amount / 2,
            DamageEffect.Doubled => damage.Amount * 2,
            DamageEffect.HalvedThenDoubled => damage.Amount / 2 * 2,
            _ => damage.Amount,
        };
        if (amount == 0)
            return new(hp, new(damage.Amount, type, effect, 0, 0, 0, []), null, false, false);

        var absorbed = Math.Min(hp.Temporary, amount);
        var current = Math.Max(hp.Current - (amount - absorbed), 0);
        // Massive damage is a warning, never an automatic death: the damage left over once hit points reach 0 equals or exceeds the maximum.
        var leftOver = amount - absorbed - (hp.Current - current);
        var massive = hp.Maximum >= 1 && leftOver >= hp.Maximum;
        var warnings = massive
            ? new List<Diagnostic> { new("play.massive-damage", $"{leftOver} damage was left over after reaching 0 hit points, which equals or exceeds the hit point maximum of {hp.Maximum}.") }
            : [];
        var outcome = new DamageOutcome(damage.Amount, type, effect, amount, absorbed, hp.Current - current, warnings);
        // D19 (SRD 5.1 p. 101, SRD 5.2.1 p. 11), amended: damage while concentrating asks for a Constitution save, DC 10 or half the
        // damage after resistance, immunity and vulnerability (before temporary hit points), whichever is higher; at 0 hit points the
        // spell simply ends. The family may cap the DC (SRD 5.2.1: 30); every DC stays within what PlayState validation accepts, so a
        // huge hit is still applied.
        var ends = concentrating && current == 0;
        int? dc = concentrating && current != 0
            ? Math.Min(Math.Max(10, amount / 2), Math.Min(policy.ConcentrationSaveMaximumDc ?? int.MaxValue, PlayState.MaxConcentrationSaveDc))
            : null;
        return new(new(hp.Maximum, current, hp.Temporary - absorbed), outcome, dc, ends, massive);
    }
}
