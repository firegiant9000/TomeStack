namespace TomeStack.RulesCore;

/// <summary>What a proposed rest change is about.</summary>
public enum RestChangeKind { HitPoints, TemporaryHitPoints, Resource, Exhaustion, HitDie, HitDice, DeathSaves }

/// <summary>
/// One proposed change of a rest preview (SPEC C-05). <paramref name="Id"/> is stable for the same character state, so
/// the player can untick it and the confirmed rest skips it. <paramref name="Condition"/> is set when the change depends
/// on the situation (for example, food and drink), and the player decides. <paramref name="Die"/> is the hit die size of
/// a <see cref="RestChangeKind.HitDie"/> or <see cref="RestChangeKind.HitDice"/> change, and <paramref name="Amount"/> the
/// hit points one spent hit die restores.
/// </summary>
public sealed record RestChange(
    string Id,
    RestChangeKind Kind,
    string Label,
    int From,
    int To,
    string Reason,
    TraceOrigin Origin,
    Guid? ContentId = null,
    string? ResourceId = null,
    string? Condition = null,
    int? SpentAfter = null,
    int? Die = null,
    int? Amount = null);

/// <summary>A rest's proposal: every change it would make, plus what the player must handle by hand.</summary>
public sealed record RestPlan(RestPeriod Kind, IReadOnlyList<RestChange> Changes, IReadOnlyList<Diagnostic> Manual);

/// <summary>
/// One hit die the player spends on a short rest: its size and the number the die shows (1 to the size), rolled in
/// TomeStack or at the table. The Constitution modifier is added by the planner.
/// </summary>
public sealed record HitDieRoll(int Die, int Roll);

/// <summary>
/// ARCHITECTURE "commands vs calculation": a rest is planned from the calculated sheet and never applies itself. Pure;
/// the application service shows the plan and applies the confirmed part in one transaction.
/// </summary>
public static class RestPlanner
{
    /// <summary>At most this many hit dice in one short rest (the most a level-20 character can have).</summary>
    public const int MaxHitDiceRolls = Character.MaxLevel;

    /// <summary>
    /// Why the character cannot take this rest now, or <c>null</c>. Both SRDs need at least 1 hit point for a long rest
    /// (SRD 5.1 p. 87, SRD 5.2.1 p. 185); 2024 rules also for a short rest (<see cref="RulesFamilyPolicy.ShortRestNeedsOneHitPoint"/>).
    /// </summary>
    public static Diagnostic? CannotRest(Character character, CharacterSheet sheet, RestPeriod kind)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(sheet);
        var needsOne = kind == RestPeriod.LongRest || RulesFamilies.Get(character.RulesFamily).ShortRestNeedsOneHitPoint;
        return needsOne && sheet.HitPoints is { Current: < 1, Maximum: > 0 }
            ? new("rest.needs-hit-points", $"A {(kind == RestPeriod.LongRest ? "long" : "short")} rest needs at least 1 hit point to start under these rules. Stabilize the character and record the hit point it regains first.")
            : null;
    }

    /// <summary>
    /// The problems with the hit dice the player wants to spend: an unknown or unavailable die size, more dice of a size
    /// than remain, a roll outside 1 to the size, or too many dice. Empty when they can all be spent.
    /// </summary>
    public static IReadOnlyList<Diagnostic> CheckHitDice(CharacterSheet sheet, IReadOnlyList<HitDieRoll> rolls)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rolls);
        var problems = new List<Diagnostic>();
        if (rolls.Count > MaxHitDiceRolls)
            problems.Add(new("rest.hit-dice-too-many", $"At most {MaxHitDiceRolls} hit dice can be spent in one rest."));
        foreach (var roll in rolls.Where(r => r is null || r.Roll < 1 || r.Roll > r.Die))
            problems.Add(new("rest.hit-die-roll-invalid", roll is null ? "A hit die roll is empty." : $"A d{roll.Die} shows 1 to {roll.Die}; {roll.Roll} is not possible."));
        foreach (var size in rolls.Where(r => r is not null).GroupBy(r => r.Die))
        {
            var pool = sheet.HitDice?.FirstOrDefault(h => h.Die == size.Key);
            if (pool is null)
                problems.Add(new("rest.hit-die-unknown", $"This character has no d{size.Key} hit dice."));
            else if (size.Count() > pool.Remaining)
                problems.Add(new("rest.hit-dice-insufficient", $"This character has {pool.Remaining} of {pool.Total} d{size.Key} hit dice left; {size.Count()} cannot be spent."));
        }
        return problems;
    }

    /// <summary>
    /// SRD short rest (SRD 5.1 p. 87, SRD 5.2.1 p. 187): each hit die the player spent restores its roll plus the
    /// Constitution modifier (at least <see cref="RulesFamilyPolicy.HitDieHealingMinimum"/>), up to the maximum, and every
    /// resource recovers by its <c>shortRest</c> recovery. Check the rolls with <see cref="CheckHitDice"/> first.
    /// </summary>
    public static RestPlan ShortRest(Character character, CharacterSheet sheet, IReadOnlyList<HitDieRoll> rolls)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rolls);
        var policy = RulesFamilies.Get(character.RulesFamily);
        var rules = new TraceOrigin(TraceOriginKind.RulesPolicy, character.RulesFamily);
        var changes = new List<RestChange>();
        var manual = new List<Diagnostic>();

        if (sheet.HitPoints is { } hp && rolls.Count > 0)
        {
            var con = sheet.Field(FieldIds.Modifier(Ability.Con)).Value;
            var current = hp.Current;
            for (var i = 0; i < rolls.Count; i++)
            {
                var (die, roll) = (rolls[i].Die, rolls[i].Roll);
                var healed = Math.Max(roll + con, policy.HitDieHealingMinimum);
                var to = Math.Min(current + healed, hp.Maximum);
                var minimum = roll + con < policy.HitDieHealingMinimum ? $" (at least {policy.HitDieHealingMinimum})" : "";
                changes.Add(new(
                    $"hitDie:{i}",
                    RestChangeKind.HitDie,
                    $"Spend a d{die} hit die",
                    current,
                    to,
                    $"Rolled {roll}, Constitution modifier {(con >= 0 ? "+" : "")}{con}: {healed} hit point(s){minimum}",
                    rules,
                    Die: die,
                    Amount: healed));
                current = to;
            }
            if (current > hp.Current && character.Play.DeathSaves is { } saves && (saves.Successes > 0 || saves.Failures > 0))
                changes.Add(DeathSavesReset(saves, rules));
        }

        AddResourceRecoveries(sheet, RestPeriod.ShortRest, changes, manual, rules);
        return new(RestPeriod.ShortRest, changes, manual);
    }

    /// <summary>
    /// SRD long rest (both families): regain all lost hit points, lose temporary hit points, regain spent hit dice
    /// (<see cref="RulesFamilyPolicy.LongRestHitDice"/>), recover every resource by its <c>longRest</c> recovery, and remove
    /// one exhaustion level (2014: only with food and drink, <see cref="RulesFamilyPolicy.LongRestExhaustionNeedsFoodAndDrink"/>).
    /// Short-rest recoveries are not part of a long rest; content that recovers on both declares both. Spell slots are not
    /// tracked yet.
    /// </summary>
    public static RestPlan LongRest(Character character, CharacterSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(sheet);
        var policy = RulesFamilies.Get(character.RulesFamily);
        var rules = new TraceOrigin(TraceOriginKind.RulesPolicy, character.RulesFamily);
        var changes = new List<RestChange>();
        var manual = new List<Diagnostic>();

        if (sheet.HitPoints is { } hp)
        {
            if (hp.Current < hp.Maximum)
            {
                changes.Add(new("hitPoints", RestChangeKind.HitPoints, "Hit points", hp.Current, hp.Maximum, "A long rest restores all lost hit points", rules));
                if (character.Play.DeathSaves is { } saves && (saves.Successes > 0 || saves.Failures > 0))
                    changes.Add(DeathSavesReset(saves, rules));
            }
            if (hp.Temporary > 0)
                changes.Add(new("temporaryHitPoints", RestChangeKind.TemporaryHitPoints, "Temporary hit points", hp.Temporary, 0, "Temporary hit points last until you finish a long rest", rules));
        }

        AddHitDiceRecovery(sheet, policy, changes, rules);
        AddResourceRecoveries(sheet, RestPeriod.LongRest, changes, manual, rules);

        if (character.Play.Exhaustion > 0)
        {
            changes.Add(new(
                "exhaustion",
                RestChangeKind.Exhaustion,
                "Exhaustion level",
                character.Play.Exhaustion,
                character.Play.Exhaustion - 1,
                "A long rest removes one exhaustion level",
                rules,
                Condition: policy.LongRestExhaustionNeedsFoodAndDrink ? "Only if the character has had food and drink (2014 rules)" : null));
        }

        return new(RestPeriod.LongRest, changes, manual);
    }

    private static RestChange DeathSavesReset(DeathSaves saves, TraceOrigin rules) => new(
        "deathSaves",
        RestChangeKind.DeathSaves,
        "Death saving throws",
        saves.Successes + saves.Failures,
        0,
        $"Regaining hit points resets them ({saves.Successes} success(es), {saves.Failures} failure(s) now)",
        rules);

    /// <summary>
    /// 2024: every spent hit die comes back. 2014: at most half the total number of hit dice (at least one). With several
    /// sizes, which dice come back is the player's choice; TomeStack proposes the largest first, and says so.
    /// </summary>
    private static void AddHitDiceRecovery(CharacterSheet sheet, RulesFamilyPolicy policy, List<RestChange> changes, TraceOrigin rules)
    {
        var pools = (sheet.HitDice ?? []).Where(h => h.Spent > 0).OrderByDescending(h => h.Die).ToList();
        if (pools.Count == 0)
            return;
        var total = (sheet.HitDice ?? []).Sum(h => h.Total);
        var allowance = policy.LongRestHitDice == HitDiceRecovery.All ? int.MaxValue : Math.Max(total / 2, 1);
        var several = policy.LongRestHitDice == HitDiceRecovery.HalfTotal && pools.Sum(p => Math.Min(p.Spent, p.Total)) > allowance && pools.Count > 1;
        foreach (var pool in pools)
        {
            // A spent count above the total (after an update lowered it) is cleared along with the dice regained.
            var spent = Math.Min(pool.Spent, pool.Total);
            var regained = Math.Min(spent, allowance);
            allowance -= regained;
            var spentAfter = spent - regained;
            if (spentAfter == pool.Spent)
                continue;
            changes.Add(new(
                $"hitDice:d{pool.Die}",
                RestChangeKind.HitDice,
                $"d{pool.Die} hit dice",
                pool.Remaining,
                pool.Total - spentAfter,
                policy.LongRestHitDice == HitDiceRecovery.All
                    ? "A long rest restores all spent hit dice"
                    : $"A long rest restores spent hit dice up to half the character's {total} hit dice (at least one)",
                rules,
                Condition: several ? "You choose which hit dice come back; TomeStack proposes the largest first" : null,
                SpentAfter: spentAfter,
                Die: pool.Die));
        }
    }

    private static void AddResourceRecoveries(CharacterSheet sheet, RestPeriod period, List<RestChange> changes, List<Diagnostic> manual, TraceOrigin rules)
    {
        var rest = period == RestPeriod.LongRest ? "long rest" : "short rest";
        foreach (var resource in sheet.Resources ?? [])
        {
            if (resource.Spent == 0)
                continue;
            if (resource.Recoveries.Count == 0)
            {
                // Nothing says how it recovers; its text may, so the player decides instead of it being silently skipped.
                manual.Add(new(
                    "rest.no-recovery-encoded",
                    $"'{resource.Label}' ({resource.ContentName}) has no recovery rule in its content. If its text says it recovers on a {rest}, recover it by hand.",
                    resource.Content,
                    resource.EffectId));
                continue;
            }
            var recovery = resource.Recoveries.FirstOrDefault(r => r.On == period);
            if (recovery is null)
                continue; // it recovers on the other kind of rest only
            var origin = resource.Trace.Count > 0 ? resource.Trace[0].Origin : rules;
            if (resource.Current is not { } current || resource.Maximum is not { } maximum || (!recovery.All && recovery.Value is null))
            {
                manual.Add(new(
                    "rest.recover-by-hand",
                    $"'{resource.Label}' ({resource.ContentName}) recovers on a {rest} ({recovery.Amount}), but TomeStack cannot calculate it; recover it by hand.",
                    resource.Content,
                    recovery.EffectId));
                continue;
            }
            var regained = recovery.All ? resource.Spent : Math.Min(recovery.Value!.Value, resource.Spent);
            if (regained == 0)
                continue;
            var spentAfter = resource.Spent - regained;
            changes.Add(new(
                $"resource:{resource.Content.ContentId:D}:{resource.ResourceId}",
                RestChangeKind.Resource,
                resource.Label,
                current,
                Math.Max(maximum - spentAfter, 0),
                recovery.All ? $"{resource.ContentName}: regain all expended uses on a {rest}" : $"{resource.ContentName}: regain {regained} use(s) on a {rest} ({recovery.Amount})",
                origin with { EffectId = recovery.EffectId },
                resource.Content.ContentId,
                resource.ResourceId,
                SpentAfter: spentAfter));
        }
    }

    /// <summary>
    /// The play state after the plan's changes, except those in <paramref name="skipped"/>. <paramref name="maximumHitPoints"/>
    /// caps hit points a spent hit die restores; each one is applied to the actual current value, so skipping one never
    /// gives the others more than they restore.
    /// </summary>
    public static PlayState Apply(PlayState play, RestPlan plan, IReadOnlySet<string> skipped, int maximumHitPoints = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(plan);
        var regainedHitPoints = false;
        foreach (var change in plan.Changes.Where(c => !skipped.Contains(c.Id)))
        {
            switch (change.Kind)
            {
                case RestChangeKind.HitPoints:
                    play = play with { CurrentHitPoints = null }; // null is "at the maximum"
                    regainedHitPoints = true;
                    break;
                case RestChangeKind.TemporaryHitPoints:
                    play = play with { TemporaryHitPoints = change.To };
                    break;
                case RestChangeKind.Exhaustion:
                    play = play with { Exhaustion = change.To };
                    break;
                case RestChangeKind.Resource:
                    play = play.WithSpent(change.ContentId!.Value, change.ResourceId!, change.SpentAfter!.Value);
                    break;
                case RestChangeKind.HitDice:
                    play = play.WithHitDiceSpent(change.Die!.Value, change.SpentAfter!.Value);
                    break;
                case RestChangeKind.HitDie:
                    var current = play.CurrentHitPoints ?? maximumHitPoints;
                    var healed = Math.Min(current + change.Amount!.Value, maximumHitPoints);
                    regainedHitPoints |= healed > current;
                    play = play.WithHitDiceSpent(change.Die!.Value, play.HitDiceSpentOf(change.Die.Value) + 1) with
                    {
                        CurrentHitPoints = healed >= maximumHitPoints ? null : healed,
                    };
                    break;
                case RestChangeKind.DeathSaves:
                    // Only when hit points were actually regained: unticking every hit die keeps the saves.
                    if (regainedHitPoints)
                        play = play with { DeathSaves = new() };
                    break;
            }
        }
        return play;
    }
}
