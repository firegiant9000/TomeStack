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
        Character character, ClassLevelMap classLevels, EffectiveToggles effective, bool autoOn, List<Diagnostic> diagnostics, IReadOnlyList<ModdedEntry> modded)
    {
        var attacks = new List<AttackEntry>();
        var rules = new TraceOrigin(TraceOriginKind.RulesPolicy, family);
        var riders = CollectRiders(active, values, character, classLevels, effective, autoOn, diagnostics);
        // Interim rule (F0): a weapon's mastery property is shown only to a character with a feature named "Weapon Mastery".
        // A real grant replaces this name check later.
        var hasWeaponMastery = active.Any(a => a.Revision.Kind == ContentKind.Feature && string.Equals(a.Revision.Name, "Weapon Mastery", StringComparison.Ordinal));
        foreach (var item in active)
        {
            foreach (var attack in item.Revision.Effects.OfType<AttackEffect>().Where(a => a.Automation != AutomationStatus.Reference))
                attacks.Add(FeatureAttack(
                    active, item, attack, values, family, character, classLevels, riders, diagnostics,
                    attack.WhileToggle is { } form ? OutOfForm(active, item, form, effective, autoOn) : null));
            if (item.Revision.Kind != ContentKind.Item)
                continue;
            // R159 keeps one row for an item equipped twice. When one of its equipped entries carries mods, each equipped entry that passes the
            // host's attunement gate gets its own row instead (the modded ones add their bonuses), told apart by EntryIndex.
            var copies = modded.Where(m => m.Host.Revision.Reference == item.Revision.Reference).ToList();
            var variants = new List<(int? EntryIndex, ModdedEntry? Modded)>();
            if (copies.Count == 0)
                variants.Add((null, null));
            else
            {
                for (var i = 0; i < character.Equipment.Count; i++)
                {
                    var entry = character.Equipment[i];
                    if (entry.Equipped && entry.Item == item.Revision.Reference && (!RequiresAttunement(item.Revision) || entry.Attuned == true))
                        variants.Add((i, copies.FirstOrDefault(m => m.EntryIndex == i)));
                }
            }
            foreach (var weapon in item.Revision.Effects.OfType<WeaponEffect>().Where(w => w.Automation != AutomationStatus.Reference))
            {
                foreach (var variant in variants)
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
                    // The weapon's own bonus (content v12): to hit and damage, after the proficiency bonus and before the riders. A revision
                    // below v12 gets nothing, even one built in code with the property set. Two steps, like a rider's, so a damage roll's
                    // explanation keeps the damage one (R21); the last step's result stays the to-hit.
                    var weaponBonus = 0;
                    if (weapon.Bonus is int b and not 0 && item.Revision.SchemaVersion >= ContentRevision.WeaponBonusSchemaVersion)
                    {
                        weaponBonus = b;
                        var weaponOrigin = ContentOrigin(family, item, weapon);
                        toHit += b;
                        trace.Add(new(trace.Count + 1, "add", $"{item.Revision.Name}: +{b} to hit (weapon)", b, toHit, weaponOrigin));
                        trace.Add(new(trace.Count + 1, "add", $"{item.Revision.Name}: +{b} damage (weapon)", b, toHit, weaponOrigin));
                    }
                    // MECH-X item 3 (R49): this copy's weapon mods add after the weapon's own bonus and before the riders, two steps each.
                    var modBonus = 0;
                    foreach (var (modContent, modEffect) in variant.Modded?.Mods ?? [])
                    {
                        if (modEffect.AppliesTo is not (ModTarget.Weapon or ModTarget.Any) || modEffect.Bonus is not (int mb and not 0))
                            continue;
                        modBonus += mb;
                        var modOrigin = ContentOrigin(family, modContent, modEffect);
                        toHit += mb;
                        trace.Add(new(trace.Count + 1, "add", $"{modContent.Revision.Name}: +{mb} to hit (mod)", mb, toHit, modOrigin));
                        trace.Add(new(trace.Count + 1, "add", $"{modContent.Revision.Name}: +{mb} damage (mod)", mb, toHit, modOrigin));
                    }
                    // Riders that match (the tags weapon and melee or ranged) add to the numbers; none matching leaves them as they were.
                    var applied = ApplyRiders(family, riders, ["weapon", weapon.Attack == WeaponAttack.Ranged ? "ranged" : "melee"], trace, ref toHit, out var riderBonus, weapon.Damage, weapon.Versatile, diagnostics);
                    var damageBonus = mod + weaponBonus + modBonus + riderBonus;
                    attacks.Add(new(
                        item.Revision.Reference, item.Revision.Name, weapon.Id, weapon.Attack, weapon.Category, ability, toHit,
                        DamageWith(weapon.Damage, applied, damageBonus), weapon.Versatile is { } versatile ? DamageWith(versatile, applied, damageBonus, versatile: true) : null,
                        weapon.DamageType, weapon.Properties, weapon.Range, hasWeaponMastery ? weapon.Mastery : null, proficiency is not null, automation, trace, warnings,
                        ContentOrigin(family, item, weapon),
                        Riders: applied.Count == 0 ? null : [.. applied.Select(r => r.Value)],
                        EntryIndex: variant.EntryIndex));
                }
            }
        }
        return attacks;
    }

    /// <summary>An attack rider that is on now: its numbers are evaluated in its own content's context.</summary>
    /// <remarks><paramref name="DielessCount"/>: a scaled count whose die is the attack's own, resolved per entry. <paramref name="NoDice"/>: the count came to 0.</remarks>
    private sealed record ActiveRider(ActiveContent Item, AttackRiderEffect Effect, RiderValue Value, double Expected, int? DielessCount = null, bool NoDice = false);

    private enum ScaledDiceStatus { Ok, Formula, Range }

    /// <summary>
    /// Evaluates a v12 <c>scaledDice</c> in the declaring content's context: the count, and the die when the effect names one (or
    /// <paramref name="dieRequired"/>). A blank or missing formula is a formula failure; formulas are checked before the range
    /// (count 0 to <see cref="DiceLimits.MaxDice"/>, an allowed die). Never throws.
    /// </summary>
    private static ScaledDiceStatus EvaluateScaledDice(
        ScaledDice scaled, bool dieRequired, ActiveContent item, Character character, ClassLevelMap classLevels, Dictionary<string, int> values,
        out int count, out int die, out FormulaError? failure)
    {
        count = die = 0;
        failure = null;
        if (string.IsNullOrWhiteSpace(scaled.Count))
        {
            failure = new("formula.empty", "The dice count formula is missing.");
            return ScaledDiceStatus.Formula;
        }
        if (!TryEvaluate(scaled.Count, item, character, classLevels, values, [], out count, out failure))
            return ScaledDiceStatus.Formula;
        var hasDie = dieRequired || scaled.Die is not null;
        if (hasDie)
        {
            if (string.IsNullOrWhiteSpace(scaled.Die))
            {
                failure = new("formula.empty", "The die formula is missing.");
                return ScaledDiceStatus.Formula;
            }
            if (!TryEvaluate(scaled.Die, item, character, classLevels, values, [], out die, out failure))
                return ScaledDiceStatus.Formula;
        }
        return count < 0 || count > DiceLimits.MaxDice || (hasDie && !AttackRiderEffect.AllowedDice.Contains(die)) ? ScaledDiceStatus.Range : ScaledDiceStatus.Ok;
    }

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
                var dice = rider.Dice;
                int? dielessCount = null;
                var noDice = false;
                // Content v12 (G1): the flags and scaledDice count only on a v12 revision, even one built in code with them set.
                var v12 = item.Revision.SchemaVersion >= ContentRevision.ScaledDiceSchemaVersion;
                if (v12 && rider.ScaledDice is { } scaled)
                {
                    // Never assumes the content was validated: a missing count or die is an invalid formula, a bad range leaves the rider out.
                    if (EvaluateScaledDice(scaled, false, item, character, classLevels, values, out var scaledCount, out var scaledDie, out _) != ScaledDiceStatus.Ok)
                    {
                        diagnostics.Add(new("rider.dice-invalid", $"'{item.Revision.Name}' has an attack extra that is left out: its dice count or die is not valid.", item.Revision.Reference, rider.Id));
                        continue;
                    }
                    dice = null;
                    if (scaledCount == 0)
                        noDice = true;
                    else if (scaled.Die is null)
                        dielessCount = scaledCount;
                    else
                        dice = $"{scaledCount}d{scaledDie}";
                }
                if (dice is not null)
                {
                    if (!AttackRiderEffect.TryParseDice(dice, out var count, out var sides))
                        continue;
                    average += count * (sides + 1) / 2.0;
                }
                riders.Add(new(
                    item, rider,
                    new(item.Revision.Reference, rider.Id, dice, toHit, bonus, rider.DamageType, v12 && rider.Critical == true, v12 && rider.OncePerTurn == true),
                    average, dielessCount, noDice));
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
        string family, List<ActiveRider> riders, IReadOnlyCollection<string> tags, List<TraceEntry> trace, ref int toHit, out int bonus,
        string baseDamage, string? versatileDamage, List<Diagnostic> diagnostics)
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
            var value = rider.Value;
            var entry = rider;
            if (rider.DielessCount is { } dielessCount)
            {
                // The die is the entry's own: the weapon's, the versatile die for a two-handed roll, a feature attack's evaluated die.
                if (!AttackRiderEffect.TryParseDice(baseDamage, out _, out var sides)
                    || (versatileDamage is not null && !AttackRiderEffect.TryParseDice(versatileDamage, out _, out _)))
                {
                    AddOnce(diagnostics, new("rider.dice-invalid", $"'{name}' has an attack extra that is left out of an attack: the attack's own die could not be read.", rider.Item.Revision.Reference, rider.Effect.Id));
                    continue;
                }
                var dice = $"{dielessCount}d{sides}";
                string? versatileDice = null;
                if (versatileDamage is not null && AttackRiderEffect.TryParseDice(versatileDamage, out _, out var versatileSides) && versatileSides != sides)
                    versatileDice = $"{dielessCount}d{versatileSides}";
                value = value with { Dice = dice, VersatileDice = versatileDice };
                entry = rider with { Value = value };
            }
            if (rider.NoDice)
            {
                trace.Add(new(trace.Count + 1, "rider.no-dice", $"{name}: attack rider adds no dice (its dice count is 0)", null, toHit, origin));
                if (value.ToHit == 0 && value.DamageBonus == 0)
                    continue;
            }
            applied.Add(entry);
            if (value.ToHit != 0)
            {
                toHit += value.ToHit;
                trace.Add(new(trace.Count + 1, "add", $"{name}: {value.ToHit:+0;-0} to hit (attack rider)", value.ToHit, toHit, origin));
            }
            if (value.Dice is not null && value.Critical)
                trace.Add(new(trace.Count + 1, "add", $"{name}: +{value.Dice} on a critical hit (attack rider)", null, toHit, origin));
            else if (value.Dice is not null && value.OncePerTurn)
                trace.Add(new(trace.Count + 1, "add", $"{name}: +{value.Dice} once per turn, when you add it (attack rider)", null, toHit, origin));
            else if (value.Dice is not null)
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
    private static string DamageWith(string dice, List<ActiveRider> applied, int flat, bool versatile = false) =>
        WithBonus(
            dice + string.Concat(applied
                .Where(r => r.Value.Dice is not null && r.Value is { Critical: false, OncePerTurn: false })
                .Select(r => "+" + (versatile ? r.Value.VersatileDice ?? r.Value.Dice : r.Value.Dice))),
            flat);

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
            var applied = ApplyRiders(family, riders, tags, trace, ref toHit, out var riderBonus, $"{count}d{die}", null, diagnostics);
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
            attack.ResourceId is null ? null : attack.ResourceContent ?? revision.ContentId, attack.ResourceId, cost, PaymentOf(attack.Payment, revision));
    }

    /// <summary>
    /// MECH-X item 1: the payment a roll, feature attack or toggle shows, before hit points and slots are known (so
    /// <see cref="PaymentValue.Payable"/> is true here; <see cref="Affordability"/> settles it). Null for a pool payment, none, and any
    /// revision below v12 (even one built in code with the property set).
    /// </summary>
    private static PaymentValue? PaymentOf(CostPayment? payment, ContentRevision revision) =>
        payment is { Kind: not PaymentKind.Resource } && revision.SchemaVersion >= ContentRevision.PaymentSchemaVersion
            ? new(payment.Kind, true, null, payment.Kind == PaymentKind.HitPoints && payment.MayDropToZero == true, payment.Kind == PaymentKind.Slot ? payment.MinSlotLevel : null)
            : null;

    /// <summary>
    /// R34, R35, R40: whether <paramref name="payment"/> can be made now. Hit points: the evaluated <paramref name="cost"/> must be below
    /// current hit points, or at most equal with <c>mayDropToZero</c> (a cost that failed to calculate, or 0, blocks nothing). Slot: the
    /// slot <see cref="SlotPayments.Pick"/> gives, or none left. Temporary hit points never count.
    /// </summary>
    private static PaymentValue Affordability(PaymentValue payment, int? cost, HitPointState hitPoints, IReadOnlyList<SlotValue>? slots, SlotValue? pact)
    {
        switch (payment.Kind)
        {
            case PaymentKind.HitPoints when cost is > 0 and var amount:
                var payable = payment.MayDropToZero ? hitPoints.Current >= amount : hitPoints.Current > amount;
                return payable
                    ? payment with { Payable = true, Reason = null }
                    : payment with { Payable = false, Reason = $"Needs {(payment.MayDropToZero ? "at least" : "more than")} {amount} current hit points." };
            case PaymentKind.Slot:
                var level = payment.MinSlotLevel ?? 1;
                return SlotPayments.Pick(slots, pact, level) is { } slot
                    ? payment with { Payable = true, Reason = null, Slot = slot }
                    : payment with { Payable = false, Reason = $"No spell slot of level {level} or higher is left.", Slot = null };
            default:
                return payment;
        }
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
