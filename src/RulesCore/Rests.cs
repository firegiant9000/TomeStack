namespace TomeStack.RulesCore;

/// <summary>What a proposed rest change is about.</summary>
public enum RestChangeKind { HitPoints, TemporaryHitPoints, Resource, Exhaustion, HitDie, HitDice, DeathSaves, SpellSlots, PactSlots, Toggle, Concentration, Charges }

/// <summary>
/// One proposed change of a rest preview (SPEC C-05). <paramref name="Id"/> is stable for the same character state, so
/// the player can untick it and the confirmed rest skips it. <paramref name="Condition"/> is set when the change depends
/// on the situation (for example, food and drink), and the player decides. <paramref name="Die"/> is the hit die size of
/// a <see cref="RestChangeKind.HitDie"/> or <see cref="RestChangeKind.HitDice"/> change, and <paramref name="Amount"/> the
/// hit points one spent hit die restores. <paramref name="SlotLevel"/> is the spell level of a
/// <see cref="RestChangeKind.SpellSlots"/> change. A <see cref="RestChangeKind.Toggle"/> change carries its toggle id in
/// <paramref name="ResourceId"/>. A <see cref="RestChangeKind.Charges"/> change (Wave 3 item 25) carries the entry key of the
/// equipment entry in <paramref name="ResourceId"/> (<see cref="ItemUseValue.EntryKey"/>) and the charges it regains in
/// <paramref name="Amount"/>; <paramref name="Dice"/> is the expression the player still has to roll while <paramref name="SpentAfter"/> is null.
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
    int? Amount = null,
    int? SlotLevel = null,
    string? Dice = null);

/// <summary>What the player rolled (or saw at the table) for the <c>regain</c> dice of one item entry's charges on a rest: the whole total.</summary>
public sealed record ChargeRoll(string Entry, int Roll);

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
    public static RestPlan ShortRest(Character character, CharacterSheet sheet, IReadOnlyList<HitDieRoll> rolls, IReadOnlyList<ChargeRoll>? chargeRolls = null)
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

        AddPactSlots(sheet, "a short rest", changes, rules);
        AddResourceRecoveries(sheet, RestPeriod.ShortRest, changes, manual, rules);
        AddChargeRecoveries(sheet, RestPeriod.ShortRest, chargeRolls ?? [], changes, manual, character.RulesFamily);
        return new(RestPeriod.ShortRest, changes, manual);
    }

    /// <summary>
    /// SRD long rest (both families): regain all lost hit points, lose temporary hit points, regain spent hit dice
    /// (<see cref="RulesFamilyPolicy.LongRestHitDice"/>), recover every resource by its <c>longRest</c> recovery, and remove
    /// one exhaustion level (2014: only with food and drink, <see cref="RulesFamilyPolicy.LongRestExhaustionNeedsFoodAndDrink"/>).
    /// Short-rest recoveries are not part of a long rest; content that recovers on both declares both. Every spent spell
    /// slot and Pact Magic slot comes back.
    /// </summary>
    public static RestPlan LongRest(Character character, CharacterSheet sheet, IReadOnlyList<ChargeRoll>? chargeRolls = null)
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
        foreach (var slots in (sheet.SpellSlots ?? []).Where(s => s.Spent > 0))
        {
            changes.Add(new(
                $"spellSlots:{slots.Level}", RestChangeKind.SpellSlots, $"Level {slots.Level} spell slots", slots.Remaining, slots.Maximum,
                "A long rest restores all expended spell slots", rules, SpentAfter: 0, SlotLevel: slots.Level));
        }
        AddPactSlots(sheet, "a long rest", changes, rules);
        AddResourceRecoveries(sheet, RestPeriod.LongRest, changes, manual, rules);
        AddChargeRecoveries(sheet, RestPeriod.LongRest, chargeRolls ?? [], changes, manual, character.RulesFamily);
        // Content v6 toggles (stances, auras) rarely outlast a night's rest; each is proposed off, and the player can keep it.
        // An auto toggle (D38) follows the hit points, so a rest has nothing to switch off.
        foreach (var toggle in (sheet.Toggles ?? []).Where(t => t.On && t.Auto != true))
        {
            changes.Add(new(
                $"toggle:{toggle.Content.ContentId:D}:{toggle.ToggleId}", RestChangeKind.Toggle, toggle.Label, 1, 0,
                $"{toggle.ContentName}: switched off after a long rest (untick to keep it on)", rules, toggle.Content.ContentId, toggle.ToggleId));
        }

        // Character schema v8 (D19): a long rest ends the spell being concentrated on. Listed like the toggles, so it is seen before it happens.
        if (character.Play.Concentration is { } concentration)
        {
            changes.Add(new(
                "concentration", RestChangeKind.Concentration, "Concentration", 1, 0,
                $"{concentration.Name}: concentration ends after a long rest (untick to keep it)", rules, concentration.Spell.ContentId));
        }

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

    /// <summary>
    /// The problems with the charge rolls (Wave 3 item 25): an entry that regains no dice on this rest (unknown, nothing spent, another
    /// rest, no dice, or rolled twice), or a total the dice cannot show. Codes only. Empty when every roll can be used.
    /// </summary>
    public static IReadOnlyList<Diagnostic> CheckChargeRolls(CharacterSheet sheet, RestPeriod period, IReadOnlyList<ChargeRoll> rolls)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(rolls);
        var problems = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roll in rolls)
        {
            var use = roll is null ? null : sheet.ItemUses?.FirstOrDefault(u => u.EntryKey == roll.Entry);
            if (use is null || use.ChargesSpent < 1 || use.Recovery is not { Regain: not null } recovery || recovery.On != period || !seen.Add(use.EntryKey)
                || !DiceExpression.TryParse(recovery.Regain, out var dice, out _))
            {
                problems.Add(new("rest.charge-roll-unknown", "A charge roll names an item that regains no dice on this rest."));
                continue;
            }
            if (roll!.Roll < dice!.Minimum || roll.Roll > dice.Maximum)
                problems.Add(new("rest.charge-roll-invalid", $"The charge dice show {dice.Minimum} to {dice.Maximum}; {roll.Roll} is not possible."));
        }
        return problems;
    }

    /// <summary>
    /// Wave 3 item 25 (R160, R171): an item's charges come back by its own recovery rule, on its rest only: all of them with no
    /// <c>regain</c>, else the dice the player rolled (<paramref name="rolls"/>, checked first with <see cref="CheckChargeRolls"/>),
    /// capped at the spent count. Until the roll is in, the change is listed with its dice and applies nothing. No recovery: none.
    /// </summary>
    private static void AddChargeRecoveries(CharacterSheet sheet, RestPeriod period, IReadOnlyList<ChargeRoll> rolls, List<RestChange> changes, List<Diagnostic> manual, string family)
    {
        var rest = period == RestPeriod.LongRest ? "long rest" : "short rest";
        foreach (var use in sheet.ItemUses ?? [])
        {
            if (use.ChargesMax is not { } maximum || use.ChargesSpent < 1 || use.Recovery is not { } recovery || recovery.On != period)
                continue;
            var spent = Math.Min(use.ChargesSpent, maximum); // a spent count above the maximum (after an update lowered it) is cleared along with the rest
            var left = maximum - spent;
            var origin = new TraceOrigin(TraceOriginKind.Content, family, use.Item, use.Name);
            var id = $"charges:{use.EntryKey}";
            var label = $"{use.Name} charges";
            if (recovery.Regain is null)
            {
                changes.Add(new(id, RestChangeKind.Charges, label, left, maximum, $"{use.Name}: regain all expended charges on a {rest}", origin, ResourceId: use.EntryKey, SpentAfter: 0, Amount: spent));
                continue;
            }
            if (!DiceExpression.TryParse(recovery.Regain, out _, out _))
            {
                manual.Add(new("rest.recover-by-hand", $"'{use.Name}' regains charges on a {rest}, but its dice could not be read; recover them by hand.", use.Item));
                continue;
            }
            var roll = rolls.FirstOrDefault(r => r.Entry == use.EntryKey);
            if (roll is null)
            {
                changes.Add(new(id, RestChangeKind.Charges, label, left, left, $"{use.Name}: roll {recovery.Regain} for the charges it regains on a {rest}", origin, ResourceId: use.EntryKey, Dice: recovery.Regain));
                continue;
            }
            var regained = Math.Clamp(roll.Roll, 0, spent);
            changes.Add(new(
                id, RestChangeKind.Charges, label, left, left + regained,
                $"{use.Name}: regain {regained} charge(s) on a {rest} ({recovery.Regain} showed {roll.Roll})", origin,
                ResourceId: use.EntryKey, SpentAfter: spent - regained, Amount: regained, Dice: recovery.Regain));
        }
    }

    /// <summary>
    /// The equipment after the plan's charge changes, except those in <paramref name="skipped"/> (Wave 3 item 25, R160): each rest
    /// writes <c>chargesSpent</c> on its entry, through the play path, never the save path. A change still waiting for its roll applies
    /// nothing. An entry that has no id yet gets one here (R158).
    /// </summary>
    public static IReadOnlyList<EquipmentEntry> ApplyEquipment(IReadOnlyList<EquipmentEntry> equipment, RestPlan plan, IReadOnlySet<string> skipped)
    {
        ArgumentNullException.ThrowIfNull(equipment);
        ArgumentNullException.ThrowIfNull(plan);
        var result = equipment.ToList();
        foreach (var change in plan.Changes.Where(c => c.Kind == RestChangeKind.Charges && c.SpentAfter is not null && !skipped.Contains(c.Id)))
        {
            var index = result.FindIndex(e => e.Id is { } id && id.ToString("D") == change.ResourceId);
            if (index < 0 && change.ResourceId is ['#', .. var position] && int.TryParse(position, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var at) && at < result.Count && result[at].Id is null)
                index = at;
            if (index >= 0)
                result[index] = result[index] with { ChargesSpent = change.SpentAfter, Id = result[index].Id ?? Guid.NewGuid() };
        }
        return result;
    }

    /// <summary>Pact Magic slots come back on a short or a long rest (both SRDs).</summary>
    private static void AddPactSlots(CharacterSheet sheet, string rest, List<RestChange> changes, TraceOrigin rules)
    {
        if (sheet.PactSlots is { Spent: > 0 } pact)
        {
            changes.Add(new(
                "pactSlots", RestChangeKind.PactSlots, "Pact Magic slots", pact.Remaining, pact.Maximum,
                $"Pact Magic slots are regained on {rest}", rules, SpentAfter: 0));
        }
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
                case RestChangeKind.SpellSlots:
                    play = play.WithSlotsSpent(change.SlotLevel!.Value, 0);
                    break;
                case RestChangeKind.PactSlots:
                    play = play with { PactSlotsSpent = 0 };
                    break;
                case RestChangeKind.Toggle:
                    play = play with { Toggles = [.. play.Toggles.Where(t => !(t.ContentId == change.ContentId && t.ToggleId == change.ResourceId))] };
                    break;
                case RestChangeKind.Concentration:
                    play = play with { Concentration = null };
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
