namespace TomeStack.RulesCore;

public static partial class CharacterCalculator
{
    // ---- attacks (content schema v5; SPEC C-02, C-04) --------------------------------------------------------

    /// <summary>
    /// The attacks on the sheet, in active-content order: an equipped weapon's, then the attacks a feature gives (content v11, D43).
    /// Every attack effect is its own row (keyed by content and effect id); duplicate attack ids are not merged. Riders that are on
    /// now add to the attacks they match. Pure: reads the toggle state, never writes play state.
    /// </summary>
    private static List<AttackEntry> CollectAttacks(
        List<ActiveContent> active, Dictionary<string, int> values, string family, Dictionary<string, Proficiency> weapons,
        Character character, ClassLevelMap classLevels, EffectiveToggles effective, bool autoOn, List<Diagnostic> diagnostics)
    {
        var attacks = new List<AttackEntry>();
        var rules = new TraceOrigin(TraceOriginKind.RulesPolicy, family);
        var riders = CollectRiders(active, values, character, classLevels, effective, autoOn, diagnostics);
        foreach (var item in active)
        {
            foreach (var attack in item.Revision.Effects.OfType<AttackEffect>().Where(a => a.Automation != AutomationStatus.Reference))
                attacks.Add(FeatureAttack(
                    active, item, attack, values, family, character, classLevels, riders, diagnostics,
                    attack.WhileToggle is { } form ? OutOfForm(active, item, form, effective, autoOn) : null));
            if (item.Revision.Kind != ContentKind.Item)
                continue;
            foreach (var weapon in item.Revision.Effects.OfType<WeaponEffect>().Where(w => w.Automation != AutomationStatus.Reference))
            {
                var str = values[FieldIds.Modifier(Ability.Str)];
                var dex = values[FieldIds.Modifier(Ability.Dex)];
                var ability = weapon.Attack == WeaponAttack.Ranged ? Ability.Dex
                    : weapon.Has("finesse") && dex > str ? Ability.Dex
                    : Ability.Str;
                var mod = ability == Ability.Dex ? dex : str;
                var pb = values[FieldIds.ProficiencyBonus];
                var category = weapon.Category == WeaponCategory.Simple ? "simple" : "martial";
                var proficiency = weapons.GetValueOrDefault(category) ?? weapons.GetValueOrDefault(weapon.WeaponKey);
                var warnings = new List<Diagnostic>();
                var automation = weapon.Automation;
                var trace = new List<TraceEntry>();
                var why = weapon.Has("finesse") && weapon.Attack == WeaponAttack.Melee ? " (finesse: the better of Strength and Dexterity)" : "";
                trace.Add(new(1, "base", $"{AbilityNames[ability]} modifier{why}", mod, mod, rules, FieldIds.Modifier(ability), [new(FieldIds.Modifier(ability), mod)]));
                var toHit = mod;
                if (proficiency is not null)
                {
                    toHit += pb;
                    trace.Add(new(2, "add", $"Proficiency bonus: proficient with {category} weapons or {weapon.WeaponKey}, from {Describe(proficiency.Content)}", pb, toHit, ContentOrigin(family, proficiency.Content, proficiency.Effect), null, [new(FieldIds.ProficiencyBonus, pb)]));
                }
                else if (weapons.Count == 0)
                {
                    // No content says which weapons the character knows (for example classes published before weapons existed).
                    automation = AutomationStatus.Assisted;
                    warnings.Add(new("attack.proficiency-unknown", $"No content records weapon proficiencies for this character, so the proficiency bonus (+{pb}) is not added to '{item.Revision.Name}'. Add it if the character is proficient.", item.Revision.Reference, weapon.Id));
                }
                else
                {
                    warnings.Add(new("attack.not-proficient", $"Not proficient with '{item.Revision.Name}' ({category}): no proficiency bonus.", item.Revision.Reference, weapon.Id));
                }
                // Riders that match (the tags weapon and melee or ranged) add to the numbers; none matching leaves them as they were.
                var applied = ApplyRiders(family, riders, ["weapon", weapon.Attack == WeaponAttack.Ranged ? "ranged" : "melee"], trace, ref toHit, out var riderBonus);
                var damageBonus = mod + riderBonus;
                attacks.Add(new(
                    item.Revision.Reference, item.Revision.Name, weapon.Id, weapon.Attack, weapon.Category, ability, toHit,
                    DamageWith(weapon.Damage, applied, damageBonus), weapon.Versatile is { } versatile ? DamageWith(versatile, applied, damageBonus) : null,
                    weapon.DamageType, weapon.Properties, weapon.Range, weapon.Mastery, proficiency is not null, automation, trace, warnings,
                    ContentOrigin(family, item, weapon),
                    Riders: applied.Count == 0 ? null : [.. applied.Select(r => r.Value)]));
            }
        }
        return attacks;
    }

    /// <summary>An attack rider that is on now: its numbers are evaluated in its own content's context.</summary>
    private sealed record ActiveRider(ActiveContent Item, AttackRiderEffect Effect, RiderValue Value, double Expected);

    /// <summary>
    /// The riders of the active content whose toggle is on (no toggle: always), in sheet content order. A rider's toggle may be another
    /// content's; an automatic toggle follows its condition, and a toggle nothing declares is off. A rider whose formula fails or whose
    /// dice are not <c>NdM</c> with an allowed die is left out (the invalid-formula warning names a formula).
    /// </summary>
    private static List<ActiveRider> CollectRiders(
        List<ActiveContent> active, Dictionary<string, int> values, Character character, ClassLevelMap classLevels,
        EffectiveToggles effective, bool autoOn, List<Diagnostic> diagnostics)
    {
        var riders = new List<ActiveRider>();
        foreach (var item in active)
        {
            foreach (var rider in item.Revision.Effects.OfType<AttackRiderEffect>().Where(r => r.Automation != AutomationStatus.Reference))
            {
                if (rider.Toggle is { } toggleId)
                {
                    var owner = rider.ToggleContent is { } ownerId ? active.FirstOrDefault(a => a.Revision.ContentId == ownerId) : item;
                    var toggle = owner?.Revision.Effects.OfType<ToggleEffect>().FirstOrDefault(t => t.ToggleId == toggleId);
                    if (owner is null || toggle is null || !ToggleIsOn(owner.Revision, toggle, effective, autoOn))
                        continue;
                }
                var toHit = 0;
                var bonus = 0;
                if (rider.ToHit is { } toHitSource && !TryEvaluate(toHitSource, item, character, classLevels, values, [], out toHit, out var toHitFailure))
                {
                    diagnostics.Add(InvalidFormula(item.Revision, rider, toHitFailure!));
                    continue;
                }
                if (rider.DamageBonus is { } bonusSource && !TryEvaluate(bonusSource, item, character, classLevels, values, [], out bonus, out var bonusFailure))
                {
                    diagnostics.Add(InvalidFormula(item.Revision, rider, bonusFailure!));
                    continue;
                }
                var average = (double)toHit + bonus;
                if (rider.Dice is not null)
                {
                    if (!AttackRiderEffect.TryParseDice(rider.Dice, out var count, out var sides))
                        continue;
                    average += count * (sides + 1) / 2.0;
                }
                riders.Add(new(item, rider, new(item.Revision.Reference, rider.Id, rider.Dice, toHit, bonus, rider.DamageType), average));
            }
        }
        return riders;
    }

    /// <summary>
    /// Applies the riders that match <paramref name="tags"/> (a rider tagged <c>all</c> matches every attack) to an attack, adding a traced
    /// step for each part that applies. Within one stack group only the rider with the highest expected value applies, the first in
    /// sheet content order on a tie; a rider that loses adds a <c>rider.not-applied</c> trace note. Riders without a group all stack.
    /// </summary>
    private static List<ActiveRider> ApplyRiders(
        string family, List<ActiveRider> riders, IReadOnlyCollection<string> tags, List<TraceEntry> trace, ref int toHit, out int bonus)
    {
        var matching = riders.Where(r => r.Effect.Tags.Contains(AttackRiderEffect.AllTag) || r.Effect.Tags.Any(tags.Contains)).ToList();
        var winners = matching
            .Where(r => r.Effect.StackGroup is not null)
            .GroupBy(r => r.Effect.StackGroup!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Aggregate((best, next) => next.Expected > best.Expected ? next : best), StringComparer.Ordinal);
        var applied = new List<ActiveRider>();
        bonus = 0;
        foreach (var rider in matching)
        {
            var name = rider.Item.Revision.Name;
            var origin = ContentOrigin(family, rider.Item, rider.Effect);
            if (rider.Effect.StackGroup is { } group && !ReferenceEquals(winners[group], rider))
            {
                trace.Add(new(trace.Count + 1, "rider.not-applied", $"{name}: attack rider not applied; '{winners[group].Item.Revision.Name}' is stronger in stack group '{group}'", null, toHit, origin));
                continue;
            }
            applied.Add(rider);
            var value = rider.Value;
            if (value.ToHit != 0)
            {
                toHit += value.ToHit;
                trace.Add(new(trace.Count + 1, "add", $"{name}: {value.ToHit:+0;-0} to hit (attack rider)", value.ToHit, toHit, origin));
            }
            if (value.Dice is not null)
                trace.Add(new(trace.Count + 1, "add", $"{name}: +{value.Dice}{(value.DamageType is null ? "" : $" {value.DamageType}")} damage (attack rider)", null, toHit, origin));
            if (value.DamageBonus != 0)
            {
                bonus += value.DamageBonus;
                trace.Add(new(trace.Count + 1, "add", $"{name}: {value.DamageBonus:+0;-0} damage (attack rider)", value.DamageBonus, toHit, origin));
            }
        }
        return applied;
    }

    /// <summary>The dice, each applied rider's dice, then the flat total: <c>1d8+3d8+3</c> (the same format <see cref="WithBonus"/> gives a weapon).</summary>
    private static string DamageWith(string dice, List<ActiveRider> applied, int flat) =>
        WithBonus(dice + string.Concat(applied.Where(r => r.Value.Dice is not null).Select(r => "+" + r.Value.Dice)), flat);

    private static bool TryEvaluate(
        string source, ActiveContent item, Character character, ClassLevelMap classLevels, Dictionary<string, int> values, List<TraceInput> inputs,
        out int value, out FormulaError? failure)
    {
        value = 0;
        if (!Formula.TryParse(source, Formula.FeaturesFor(item.Revision), out var formula, out failure))
            return false;
        return formula!.TryEvaluate(id => Resolve(id, item, character, classLevels, values, inputs), out value, out failure);
    }

    /// <summary>
    /// An attack a feature gives (content v11, D43). To hit = the best of the listed abilities' modifiers (ties: the first listed) plus
    /// the proficiency bonus unless the attack says it is not proficient; damage = <c>{count}d{die}</c> (formulas in the declaring
    /// content's context, so a scale resolves in its class), the applied riders' dice, then the damage bonus (default: that modifier).
    /// A die that is not a d4, d6, d8, d10 or d12, a count outside 1 to the dice limit, or a formula that fails lists the attack as
    /// unavailable with the reason and reports <c>attack.die-invalid</c> on the feature; it never throws.
    /// </summary>
    private static AttackEntry FeatureAttack(
        List<ActiveContent> active, ActiveContent item, AttackEffect attack, Dictionary<string, int> values, string family, Character character,
        ClassLevelMap classLevels, List<ActiveRider> riders, List<Diagnostic> diagnostics, string? outOfForm)
    {
        var revision = item.Revision;
        var origin = ContentOrigin(family, item, attack);
        var rules = new TraceOrigin(TraceOriginKind.RulesPolicy, family);
        var trace = new List<TraceEntry>();
        var pb = values[FieldIds.ProficiencyBonus];

        var ability = attack.Abilities.Count > 0 ? attack.Abilities[0] : Ability.Str;
        var mod = values[FieldIds.Modifier(ability)];
        foreach (var listed in attack.Abilities.Skip(1))
        {
            if (values[FieldIds.Modifier(listed)] > mod)
            {
                ability = listed;
                mod = values[FieldIds.Modifier(listed)];
            }
        }
        var best = attack.Abilities.Count > 1 ? $" (best of {string.Join(", ", attack.Abilities)})" : "";
        trace.Add(new(1, "base", $"{AbilityNames[ability]} modifier{best}", mod, mod, rules, FieldIds.Modifier(ability), [new(FieldIds.Modifier(ability), mod)]));
        var toHit = mod;
        var proficient = attack.Proficient ?? true;
        if (proficient)
        {
            toHit += pb;
            trace.Add(new(trace.Count + 1, "add", $"Proficiency bonus: '{attack.Label}' adds it", pb, toHit, origin, null, [new(FieldIds.ProficiencyBonus, pb)]));
        }

        var inputs = new List<TraceInput>();
        string? invalid = null;
        var countOk = TryEvaluate(attack.Damage.Count, item, character, classLevels, values, inputs, out var count, out var countFailure);
        var dieOk = TryEvaluate(attack.Damage.Die, item, character, classLevels, values, inputs, out var die, out var dieFailure);
        if (attack.Abilities.Count == 0)
            invalid = "The attack lists no ability.";
        else if (!dieOk)
            invalid = $"The die formula '{attack.Damage.Die}' could not be evaluated: {dieFailure!.Message}";
        else if (!AttackRiderEffect.AllowedDice.Contains(die))
            invalid = $"d{die} is not a die this app rolls (d4, d6, d8, d10 or d12).";
        else if (!countOk)
            invalid = $"The dice count formula '{attack.Damage.Count}' could not be evaluated: {countFailure!.Message}";
        else if (count < 1 || count > DiceLimits.MaxDice)
            invalid = $"A dice count of {count} is outside 1 to {DiceLimits.MaxDice}.";
        var damageBonus = mod;
        if (invalid is null && attack.DamageBonus is { } bonusSource)
        {
            if (TryEvaluate(bonusSource, item, character, classLevels, values, inputs, out var bonus, out var bonusFailure))
            {
                damageBonus = bonus;
            }
            else
            {
                invalid = $"The damage bonus formula '{bonusSource}' could not be evaluated: {bonusFailure!.Message}";
                diagnostics.Add(InvalidFormula(revision, attack, bonusFailure));
            }
        }
        else if (invalid is not null)
        {
            diagnostics.Add(new("attack.die-invalid", $"'{revision.Name}' attack '{attack.Label}' is unavailable: {invalid}", revision.Reference, attack.Id));
        }

        var tags = (attack.Tags ?? []).Append(attack.Kind == WeaponAttack.Ranged ? "ranged" : "melee").ToList();
        string damage = "";
        List<RiderValue>? appliedRiders = null;
        if (invalid is null)
        {
            var classRoot = item.ClassRoot is { } root ? active.FirstOrDefault(a => a.Revision.Reference == root)?.Revision.Name : null;
            var scales = inputs.Where(i => FormulaIdentifiers.IsScale(i.Name)).Select(i => $"{classRoot} {FormulaIdentifiers.ScaleId(i.Name)}".Trim()).Distinct(StringComparer.Ordinal).ToList();
            var from = scales.Count == 0 ? "" : $", the die from {string.Join(" and ", scales)}";
            var plus = attack.DamageBonus is null ? $"{AbilityNames[ability]} modifier" : "its damage bonus";
            trace.Add(new(trace.Count + 1, "set", $"Damage {count}d{die}{from}, plus {plus}", null, toHit, origin, null, inputs));
            var applied = ApplyRiders(family, riders, tags, trace, ref toHit, out var riderBonus);
            damage = DamageWith($"{count}d{die}", applied, damageBonus + riderBonus);
            appliedRiders = applied.Count == 0 ? null : [.. applied.Select(r => r.Value)];
        }

        int? cost = null;
        var costFailed = false;
        if (attack.Cost is { } costSource)
        {
            if (TryEvaluate(costSource, item, character, classLevels, values, [], out var spent, out var costFailure))
            {
                cost = Math.Max(spent, 0);
            }
            else
            {
                costFailed = true; // the row stays available; the player works out what it costs
                diagnostics.Add(InvalidFormula(revision, attack, costFailure!));
            }
        }
        else if (attack.ResourceId is not null)
        {
            cost = 1;
        }
        return new(
            revision.Reference, attack.Label, attack.Id, attack.Kind, null, ability, toHit, damage, null, attack.DamageType, [], attack.Range, null,
            proficient,
            // An attack that cannot be calculated, or whose cost cannot, is assisted (the player works it out); one that is only out of its form keeps its automation.
            (invalid is not null || costFailed) && attack.Automation == AutomationStatus.Automatic ? AutomationStatus.Assisted : attack.Automation,
            trace, [], origin,
            AttackSource.Feature, attack.Tags, attack.ReplacesAttack ?? false, appliedRiders, invalid is null && outOfForm is null, invalid ?? outOfForm, attack.Activation,
            attack.ResourceId is null ? null : attack.ResourceContent ?? revision.ContentId, attack.ResourceId, cost);
    }

    /// <summary>Dice plus a flat modifier, as a dice expression: <c>1d8+3</c>, <c>1d8-1</c>, or <c>1d8</c> for 0.</summary>
    internal static string WithBonus(string dice, int bonus) =>
        bonus switch
        {
            0 => dice,
            > 0 => $"{dice}+{bonus}",
            _ => $"{dice}{bonus}",
        };
}
