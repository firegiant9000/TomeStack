namespace TomeStack.RulesCore;

public static partial class CharacterCalculator
{
    // ---- effects --------------------------------------------------------------------------------------------

    /// <param name="SkipReason">Set when the rules say this modifier does not apply now (for example, Unarmored Defense while armor is worn); it is traced as not used.</param>
    /// <param name="FollowedToggle">Set when the modifier follows another entry's toggle (content v11 <c>toggleContent</c>): that toggle's label, named in the trace.</param>
    private sealed record Modifier(ActiveContent Content, ModifierEffect Effect, Formula Formula, IReadOnlyList<string> ReadsFields, string? SkipReason = null, string? FollowedToggle = null, bool ModBonus = false, bool FromItemMod = false);

    private const string ResourceTargetPrefix = ContentValidator.ResourceTargetPrefix;

    private static bool IsResourceTarget(ContentRevision revision, ModifierEffect effect) =>
        revision.SchemaVersion >= ContentRevision.PlayMechanicsSchemaVersion && effect.Target.StartsWith(ResourceTargetPrefix, StringComparison.Ordinal);

    private static bool IsResourceBonus(ContentRevision revision, ModifierEffect effect) =>
        IsResourceTarget(revision, effect) && effect.Operation == ModifierOperation.Bonus;

    /// <summary>
    /// A bonus to a resource's maximum (content v11); the resource is found in the same revision. <paramref name="Manual"/> is set when the
    /// modifier cannot be applied automatically (assisted, or not always on), so the resource it names reads assisted. One that is only
    /// gated off by its toggle is never listed.
    /// </summary>
    private sealed record ResourceBonus(ActiveContent Content, ModifierEffect Effect, string? FollowedToggle, bool Manual = false);

    /// <summary>
    /// M2 item 4: worn armor and a shield as Armor Class modifiers. Body armor is a <c>replace</c> of the base (light: AC +
    /// Dex; medium: AC + Dex up to the cap; heavy: AC), and while it is worn every other Armor Class replacement is an
    /// unarmored alternative that does not apply, traced as such. (All SRD alternatives, such as Unarmored Defense,
    /// apply only without armor; a shield does not stop them.) A shield is a bonus. Only one body armor and one shield
    /// count; extra ones get a diagnostic. Content v12 <c>notWearing</c> turns a modifier off while the listed gear is worn
    /// (<see cref="GateOnGear"/>).
    /// </summary>
    /// <summary>The body armor and shield that count (at most one of each).</summary>
    private sealed record WornArmor((ActiveContent Content, ArmorEffect Effect)? Body, (ActiveContent Content, ArmorEffect Effect)? Shield);

    /// <remarks>
    /// Content v12 (G4): a modifier's <c>notWearing</c> gate is applied before the armor branch, so the synthetic armor, shield and
    /// armor-mod modifiers added below are never gated.
    /// Content v8 (M2.2): a <c>whileArmored</c> modifier does not apply without body armor. Armor training is checked only
    /// when <paramref name="checkTraining"/>: every class the character has levels in records its armor training
    /// (<see cref="RecordsArmorTraining"/>). A class written before v8 records none, so its training is unknown, and
    /// neither the training warnings nor the untrained-shield rule apply. Missing training is a warning in both families;
    /// an untrained shield's bonus follows <see cref="RulesFamilyPolicy.UntrainedShieldGivesArmorClass"/>.
    /// </remarks>
    private static WornArmor AddArmor(
        List<ActiveContent> active, List<Modifier> modifiers, List<Diagnostic> diagnostics, Dictionary<string, Proficiency> training, bool checkTraining,
        RulesFamilyPolicy policy, Dictionary<string, List<Diagnostic>> warnings, IReadOnlyList<ModdedEntry> modded)
    {
        // MECH-X item 3 (R49): the armor mods of the first modded entry (list order) of the worn item add their bonus to Armor Class,
        // built like Synthetic (origin the mod's revision). Nothing for a mod that applies to weapons only or has no bonus.
        List<Modifier> ModBonuses(ActiveContent host, string? skipReason) =>
        [
            .. modded.Where(m => m.Host.Revision.Reference == host.Revision.Reference).Take(1)
                .SelectMany(m => m.Mods)
                .Where(x => x.Effect.AppliesTo is ModTarget.Armor or ModTarget.Any && x.Effect.Bonus is > 0)
                .Select(x =>
                {
                    var value = x.Effect.Bonus!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var effect = new ModifierEffect { Id = x.Effect.Id, Operation = ModifierOperation.Bonus, Target = FieldIds.ArmorClass, Value = value, Text = $"{x.Mod.Revision.Name} on {host.Revision.Name}" };
                    var formula = Formula.TryParse(value, out var parsed, out _) ? parsed! : throw new InvalidOperationException($"Armor mod bonus '{value}' does not parse."); // an integer, as in Synthetic
                    return new Modifier(x.Mod, effect, formula, [], skipReason, ModBonus: true);
                }),
        ];
        var armor = active
            .SelectMany(a => a.Revision.Effects.OfType<ArmorEffect>()
                .Where(e => e.Automation == AutomationStatus.Automatic && e.Timing == EffectTiming.Always)
                .Select(e => (Content: a, Effect: e)))
            .ToList();
        var body = armor.Where(a => a.Effect.Category != ArmorCategory.Shield).ToList();
        var shields = armor.Where(a => a.Effect.Category == ArmorCategory.Shield).ToList();
        foreach (var extra in body.Skip(1))
            diagnostics.Add(new("equipment.multiple-armor", $"'{extra.Content.Revision.Name}' is armor, but '{body[0].Content.Revision.Name}' is already worn; only one armor counts.", extra.Content.Revision.Reference, extra.Effect.Id));
        foreach (var extra in shields.Skip(1))
            diagnostics.Add(new("equipment.multiple-shields", $"'{extra.Content.Revision.Name}' is a shield, but '{shields[0].Content.Revision.Name}' is already used; only one shield counts.", extra.Content.Revision.Reference, extra.Effect.Id));
        GateOnGear(modifiers, body.Count > 0 ? body[0] : null, shields.Count > 0 ? shields[0] : null);

        if (body.Count > 0)
        {
            var (content, effect) = body[0];
            var reason = $"it applies only while no armor is worn, and {Describe(content)} is worn";
            for (var i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Effect.Target == FieldIds.ArmorClass && modifiers[i].Effect.Operation == ModifierOperation.Replace)
                    modifiers[i] = modifiers[i] with { SkipReason = reason };
            }
            var value = effect.Category switch
            {
                ArmorCategory.Light => $"{effect.ArmorClass} + DEX.MOD",
                ArmorCategory.Medium => $"{effect.ArmorClass} + min(DEX.MOD, {effect.DexterityCap ?? ArmorEffect.DefaultMediumDexterityCap})",
                _ => $"{effect.ArmorClass}",
            };
            modifiers.Add(Synthetic(content, effect, ModifierOperation.Replace, value));
            modifiers.AddRange(ModBonuses(content, null));
            var key = effect.Category.ToString().ToLowerInvariant();
            if (checkTraining && !training.ContainsKey(key))
            {
                warnings[FieldIds.ArmorClass].Add(new(
                    "equipment.armor-untrained",
                    $"This character has no training with {key} armor, which '{content.Revision.Name}' is: disadvantage on d20 tests that use Strength or Dexterity, and no spellcasting, while it is worn.",
                    content.Revision.Reference,
                    effect.Id));
            }
        }
        else
        {
            for (var i = 0; i < modifiers.Count; i++)
            {
                if (modifiers[i].Effect.WhileArmored == true && modifiers[i].SkipReason is null && !IgnoresV8(modifiers[i].Content.Revision))
                    modifiers[i] = modifiers[i] with { SkipReason = "it applies only while armor is worn, and none is" };
            }
        }
        if (shields.Count > 0)
        {
            var (content, effect) = shields[0];
            var shield = Synthetic(content, effect, ModifierOperation.Bonus, $"{effect.ArmorClass}");
            if (checkTraining && !training.ContainsKey("shield"))
            {
                warnings[FieldIds.ArmorClass].Add(new(
                    "equipment.shield-untrained",
                    policy.UntrainedShieldGivesArmorClass
                        ? $"This character has no training with shields: '{content.Revision.Name}' still adds to Armor Class under the {policy.ShortLabel}, with disadvantage on d20 tests that use Strength or Dexterity, and no spellcasting."
                        : $"This character has no training with shields, so '{content.Revision.Name}' adds nothing to Armor Class under the {policy.ShortLabel}.",
                    content.Revision.Reference,
                    effect.Id));
                if (!policy.UntrainedShieldGivesArmorClass)
                    shield = shield with { SkipReason = "the character has no training with shields" };
            }
            modifiers.Add(shield);
            modifiers.AddRange(ModBonuses(content, shield.SkipReason));
        }
        return new(body.Count > 0 ? body[0] : null, shields.Count > 0 ? shields[0] : null);
    }

    /// <summary>
    /// Content v12 (G4): a modifier with <see cref="ModifierEffect.NotWearing"/> does not apply while any listed gear is worn (the first
    /// listed in the order armor, heavy armor, shield names the reason). A shield counts as used even when it is untrained and adds no
    /// Armor Class. Not honoured on an item mod's modifier, an armor mod's synthetic bonus, or a revision below v12 (the sandbox
    /// calculates drafts that were never validated); an unknown value is ignored.
    /// </summary>
    private static void GateOnGear(List<Modifier> modifiers, (ActiveContent Content, ArmorEffect Effect)? body, (ActiveContent Content, ArmorEffect Effect)? shield)
    {
        for (var i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (modifier.Effect.NotWearing is not { Count: > 0 } gear || modifier.SkipReason is not null || modifier.FromItemMod || modifier.ModBonus
                || modifier.Content.Revision.SchemaVersion < ContentRevision.AbilityCapSchemaVersion)
                continue;
            string? reason = null;
            foreach (var kind in ModifierEffect.Gear)
            {
                if (!gear.Contains(kind, StringComparer.Ordinal))
                    continue;
                reason = kind switch
                {
                    ModifierEffect.GearArmor when body is { } worn => $"it applies only while no armor is worn, and {Describe(worn.Content)} is worn",
                    ModifierEffect.GearHeavyArmor when body is { Effect.Category: ArmorCategory.Heavy } worn => $"it applies only while no heavy armor is worn, and {Describe(worn.Content)} is worn",
                    ModifierEffect.GearShield when shield is { } used => $"it applies only while no shield is used, and {Describe(used.Content)} is used",
                    _ => null,
                };
                if (reason is not null)
                    break;
            }
            if (reason is not null)
                modifiers[i] = modifier with { SkipReason = reason };
        }
    }

    /// <summary>
    /// Content v8 (M2.2): worn armor's Strength requirement (speed 10 feet lower below it; TomeStack has no speed field, so
    /// Armor Class warns) and Stealth disadvantage (the Stealth field warns). Both SRDs state them alike.
    /// </summary>
    private static void ArmorRequirements(WornArmor worn, Dictionary<string, int> values, Dictionary<string, List<Diagnostic>> warnings)
    {
        if (worn.Body is not { } body || IgnoresV8(body.Content.Revision))
            return;
        var (content, effect) = body;
        var strength = values[FieldIds.Score(Ability.Str)];
        if (effect.Strength is { } needed && strength < needed)
        {
            warnings[FieldIds.ArmorClass].Add(new(
                "equipment.armor-strength",
                $"'{content.Revision.Name}' needs Strength {needed}; with Strength {strength} the wearer's speed is 10 feet lower. Lower Speed on Stats by an override.",
                content.Revision.Reference,
                effect.Id));
        }
        if (effect.StealthDisadvantage == true)
        {
            warnings[FieldIds.Skill(Stealth)].Add(new(
                "equipment.stealth-disadvantage",
                $"'{content.Revision.Name}' gives disadvantage on Dexterity (Stealth) checks; choose disadvantage when you roll.",
                content.Revision.Reference,
                effect.Id));
        }
    }

    private static Modifier Synthetic(ActiveContent content, ArmorEffect armor, ModifierOperation operation, string value)
    {
        var effect = new ModifierEffect { Id = armor.Id, Operation = operation, Target = FieldIds.ArmorClass, Value = value, Text = armor.Text };
        var formula = Formula.TryParse(value, out var parsed, out _) ? parsed! : throw new InvalidOperationException($"Armor formula '{value}' does not parse."); // built from integers above
        return new(content, effect, formula, [.. formula.Identifiers.Select(FormulaIdentifiers.FieldFor).OfType<string>().Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>How an effect's toggle gate came out: it applies now, it does not apply now, or the effect names no toggle the gate can resolve.</summary>
    private enum ToggleGate { Applies, Skips, Unresolved }

    /// <summary>
    /// The toggle gate modifiers and roll modes share. Content v11 (D43): an effect that names another content's toggle (<paramref name="toggleContent"/>)
    /// applies while that toggle is on. Content that is not active, or that does not declare the toggle, is a broken reference the user should see:
    /// the effect does not apply and the sheet says why (<see cref="ToggleGate.Skips"/> plus a diagnostic). Without toggleContent (content v6, M3 B2),
    /// an effect that names a toggle of its revision applies while that toggle is on and simply does not apply while it is off (the rules say so;
    /// nothing is left to the player). A toggle the revision does not declare is <see cref="ToggleGate.Unresolved"/>. <paramref name="followedLabel"/>
    /// is the other content's toggle label when the gate follows one.
    /// </summary>
    private static ToggleGate GateOnToggle(
        List<ActiveContent> active, ContentRevision revision, string effectId, string toggleId, Guid? toggleContent,
        EffectiveToggles effective, bool autoOn, List<Diagnostic> diagnostics, out string? followedLabel)
    {
        followedLabel = null;
        if (toggleContent is { } ownerId)
        {
            var owner = active.FirstOrDefault(a => a.Revision.ContentId == ownerId);
            var declared = owner?.Revision.Effects.OfType<ToggleEffect>().FirstOrDefault(t => t.ToggleId == toggleId);
            if (owner is null || declared is null)
            {
                diagnostics.Add(new(
                    "modifier.toggle-content-missing",
                    $"'{revision.Name}' has an effect that follows a toggle of other content, which is not active or does not have that toggle; the effect is not applied.",
                    revision.Reference,
                    effectId));
                return ToggleGate.Skips;
            }
            if (!ToggleIsOn(owner.Revision, declared, effective, autoOn))
                return ToggleGate.Skips;
            followedLabel = declared.Label;
            return ToggleGate.Applies;
        }
        if (revision.Effects.OfType<ToggleEffect>().FirstOrDefault(t => t.ToggleId == toggleId) is { } named)
            return ToggleIsOn(revision, named, effective, autoOn) ? ToggleGate.Applies : ToggleGate.Skips;
        return ToggleGate.Unresolved;
    }

    /// <summary>
    /// Content v11 (D43): the roll modes of the active content, by the sheet fields they reach (<c>save.&lt;x&gt;</c>, <c>skill.&lt;s&gt;</c>;
    /// <c>ability.&lt;x&gt;.check</c> reaches <c>ability.&lt;x&gt;.mod</c> and every skill of that ability, and Dexterity also initiative).
    /// Toggled entries are gated like modifiers. A rollMode has no timing, so only its toggle gates it. Order is sheet content order.
    /// </summary>
    private static RollModeCollection CollectRollModes(
        List<ActiveContent> active, Character character, EffectiveToggles effective, bool autoOn, IReadOnlySet<string> immune, List<Diagnostic> diagnostics)
    {
        var targeted = new List<(string Target, RollModeReason Reason)>();
        var attackModes = new List<AttackModeValue>();
        var against = new List<RollModeReason>();
        foreach (var item in active)
        {
            var revision = item.Revision;
            foreach (var effect in revision.Effects.OfType<RollModeEffect>())
            {
                // Content v12 (G7): while the character has the condition the effect is left out, like a toggle that is off.
                if (effect.UnlessCondition is { } unless && ConditionKeys.Holds(character.Play.Conditions, unless, immune))
                    continue;
                if (effect.Toggle is { } toggle
                    && GateOnToggle(active, revision, effect.Id, toggle, effect.ToggleContent, effective, autoOn, diagnostics, out _) != ToggleGate.Applies)
                    continue;
                var reason = new RollModeReason(effect.Mode, revision.Name, revision.Reference, effect.Against, effect.Automation, effect.Prevent == true);
                if (effect.Target == RollModeEffect.AttackTarget)
                    attackModes.Add(new AttackModeValue(reason, effect.Tags ?? [], effect.Ability));
                else if (effect.Target == RollModeEffect.AttacksAgainstTarget)
                    against.Add(reason);
                targeted.Add((effect.Target, reason));
            }
        }
        return new(
            targeted,
            attackModes.Count > 0 ? attackModes : null,
            against.Count > 0 ? new RollModeSummary(RollModes.CombineAgainst(against), against) : null);
    }

    /// <summary>
    /// The roll modes of the active content: each reason with the target it names (expanded to fields after the values are known, because
    /// a skill that uses another ability answers to that ability's checks), the <c>attack</c> modes (v12), and the summary of attacks against the character (v12).
    /// </summary>
    private sealed record RollModeCollection(
        List<(string Target, RollModeReason Reason)> Targeted, IReadOnlyList<AttackModeValue>? AttackModes, RollModeSummary? AttacksAgainst);

    /// <summary>The roll modes by sheet field. Automatic combines only the reasons that need no confirmation (no against) from an automatic effect; the rest are listed notes.</summary>
    private static Dictionary<string, RollModeSummary> RollModesByField(
        List<(string Target, RollModeReason Reason)> targeted, IReadOnlyDictionary<string, Ability> skillAbility)
    {
        var byField = new Dictionary<string, List<RollModeReason>>(StringComparer.Ordinal);
        foreach (var (target, reason) in targeted)
        {
            foreach (var field in RollModeFields(target, skillAbility))
            {
                if (!byField.TryGetValue(field, out var reasons))
                    byField[field] = reasons = [];
                reasons.Add(reason);
            }
        }
        return byField.ToDictionary(
            p => p.Key,
            p => new RollModeSummary(
                RollModes.Combine(p.Value.Where(r => r.IsAutomatic).Select(r => r.Mode)),
                [.. p.Value]),
            StringComparer.Ordinal);
    }

    /// <summary>Content v12 (G10): a stored candidate for a skill to use <paramref name="Ability"/> instead of its printed ability.</summary>
    private sealed record CheckAbilityCandidate(Ability Ability, ActiveContent Content, CheckAbilityEffect Effect);

    /// <summary>
    /// Content v12 (G10): the skills that may use another ability, by skill field, in content order. <c>skill.&lt;s&gt;</c> reaches one skill,
    /// <c>ability.&lt;x&gt;.check</c> every skill whose printed ability is x. A stored effect that is not automatic and always, whose ability
    /// or target does not parse, or that names the printed ability, is skipped (validation refuses them).
    /// </summary>
    private static Dictionary<string, List<CheckAbilityCandidate>> CollectCheckAbilities(List<ActiveContent> active)
    {
        var byField = new Dictionary<string, List<CheckAbilityCandidate>>(StringComparer.Ordinal);
        foreach (var item in active)
        {
            foreach (var effect in item.Revision.Effects.OfType<CheckAbilityEffect>())
            {
                if (effect.Automation != AutomationStatus.Automatic || effect.Timing != EffectTiming.Always
                    || !CheckAbilityEffect.TryParseAbility(effect.Ability, out var ability))
                    continue;
                foreach (var skill in Skills.Where(s => s.Ability != ability && CheckAbilityReaches(effect.Target, s.Key, s.Ability)))
                {
                    var field = FieldIds.Skill(skill.Key);
                    if (!byField.TryGetValue(field, out var list))
                        byField[field] = list = [];
                    list.Add(new(ability, item, effect));
                }
            }
        }
        return byField;
    }

    private static bool CheckAbilityReaches(string target, string skillKey, Ability printed)
    {
        if (target.StartsWith("skill.", StringComparison.Ordinal))
            return target == FieldIds.Skill(skillKey);
        var mod = FieldIds.Modifier(printed);
        return target == string.Concat(mod.AsSpan(0, mod.Length - ".mod".Length), ".check");
    }

    /// <summary>
    /// The ability a skill (or save) uses: the best automatic roll mode first (advantage, then normal, then disadvantage), then the highest
    /// modifier among equals. A tie keeps the printed ability, then the earlier candidate. Roll floors are not compared. Null candidate: the printed ability.
    /// </summary>
    private static (Ability Ability, CheckAbilityCandidate? Candidate) ChosenAbility(
        Ability printed, List<CheckAbilityCandidate>? candidates, IReadOnlyDictionary<string, int> values, Func<Ability, RollMode> modeOf)
    {
        static int Rank(RollMode mode) => mode == RollMode.Advantage ? 2 : mode == RollMode.Normal ? 1 : 0;
        var best = printed;
        var bestRank = Rank(modeOf(printed));
        CheckAbilityCandidate? chosen = null;
        foreach (var candidate in candidates ?? [])
        {
            var rank = Rank(modeOf(candidate.Ability));
            if (rank > bestRank || (rank == bestRank && values[FieldIds.Modifier(candidate.Ability)] > values[FieldIds.Modifier(best)]))
            {
                best = candidate.Ability;
                bestRank = rank;
                chosen = candidate;
            }
        }
        return (best, chosen);
    }

    /// <summary>
    /// The automatic roll mode a check of <paramref name="skillField"/> would get if it used <paramref name="ability"/>: the reasons aimed at
    /// that skill or at that ability's checks, combined as the sheet combines them.
    /// </summary>
    private static RollMode CheckModeOf(List<(string Target, RollModeReason Reason)> targeted, string skillField, Ability ability)
    {
        var mod = FieldIds.Modifier(ability);
        var check = string.Concat(mod.AsSpan(0, mod.Length - ".mod".Length), ".check");
        return RollModes.Combine(targeted.Where(t => t.Reason.IsAutomatic && (t.Target == skillField || t.Target == check)).Select(t => t.Reason.Mode));
    }

    /// <summary>
    /// Content v12 (G10): drops the candidates that would close a dependency cycle (the candidate's modifier depends, through modifiers, on the
    /// skill it would feed), each with <c>effect.dependency-cycle</c>. Returns the dependency edges of the kept candidates, modifier to skill.
    /// Runs after <see cref="RemoveCycles"/>, so the modifier edges are acyclic; kept candidates are added one by one.
    /// </summary>
    private static List<(string From, string To, Modifier? Via)> AcceptCheckAbilities(
        Dictionary<string, List<CheckAbilityCandidate>> candidates, List<Modifier> modifiers, List<Diagnostic> diagnostics,
        Dictionary<string, List<Diagnostic>> warnings)
    {
        var extra = new List<(string From, string To, Modifier? Via)>();
        if (candidates.Count == 0)
            return extra;
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (from, to, _) in Edges(modifiers))
        {
            if (!adjacency.TryGetValue(from, out var list))
                adjacency[from] = list = [];
            list.Add(to);
        }
        bool Reaches(string from, string to)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { from };
            var pending = new Stack<string>([from]);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (current == to)
                    return true;
                foreach (var next in adjacency.GetValueOrDefault(current) ?? [])
                {
                    if (seen.Add(next))
                        pending.Push(next);
                }
            }
            return false;
        }

        foreach (var (field, list) in candidates.ToList())
        {
            var kept = new List<CheckAbilityCandidate>();
            foreach (var candidate in list)
            {
                var mod = FieldIds.Modifier(candidate.Ability);
                if (Reaches(field, mod))
                {
                    var revision = candidate.Content.Revision;
                    var diagnostic = new Diagnostic(
                        "effect.dependency-cycle",
                        $"'{revision.Name}' has an effect that is ignored: using {FieldLabel(mod)} for {FieldLabel(field)} would make {FieldLabel(mod)} depend on {FieldLabel(field)}.",
                        revision.Reference,
                        candidate.Effect.Id);
                    diagnostics.Add(diagnostic);
                    warnings[field].Add(diagnostic);
                    continue;
                }
                kept.Add(candidate);
                extra.Add((mod, field, null));
                if (!adjacency.TryGetValue(mod, out var outgoing))
                    adjacency[mod] = outgoing = [];
                outgoing.Add(field);
            }
            if (kept.Count == 0)
                candidates.Remove(field);
            else
                candidates[field] = kept;
        }
        return extra;
    }

    private static IEnumerable<string> RollModeFields(string target, IReadOnlyDictionary<string, Ability> skillAbility)
    {
        if (target == RollModeEffect.InitiativeTarget)
        {
            yield return FieldIds.Initiative;
            yield break;
        }
        if (target is RollModeEffect.AttackTarget or RollModeEffect.AttacksAgainstTarget)
            yield break;
        if (target.StartsWith("save.", StringComparison.Ordinal) || target.StartsWith("skill.", StringComparison.Ordinal))
        {
            if (SpecIndex.ContainsKey(target))
                yield return target;
            yield break;
        }
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var mod = FieldIds.Modifier(ability);
            if (target != string.Concat(mod.AsSpan(0, mod.Length - ".mod".Length), ".check"))
                continue;
            yield return mod;
            // Content v12 (G10): a skill answers to the ability it uses, which is its printed one unless a checkAbility won.
            foreach (var skill in Skills.Where(s => skillAbility.GetValueOrDefault(FieldIds.Skill(s.Key), s.Ability) == ability))
                yield return FieldIds.Skill(skill.Key);
            if (ability == Ability.Dex)
                yield return FieldIds.Initiative;
        }
    }

    /// <summary>One field's roll floor: the summary, and the trace steps (one per reason) appended after the field's own steps.</summary>
    private sealed record FloorCollection(RollFloorSummary Summary, List<FloorStep> Steps);

    private sealed record FloorStep(string Description, int Amount, TraceOrigin Origin);

    /// <summary>
    /// Content v12 (G9): the roll floors of the active content, by the check field they reach (<c>check</c>: every ability modifier, skill
    /// and initiative; <c>ability.&lt;x&gt;.check</c> and <c>skill.&lt;s&gt;</c> as for roll modes). <c>proficient</c> keeps only fields marked
    /// Proficient or Expertise. A total formula is evaluated in the declaring content's context; one that fails leaves that reason out
    /// (<c>effect.invalid-formula</c>). The highest face and the highest total win separately; every reason is listed. A field's value never changes.
    /// </summary>
    private static Dictionary<string, FloorCollection> CollectRollFloors(
        List<ActiveContent> active, Character character, ClassLevelMap classLevels, Dictionary<string, int> values,
        Func<string, ProficiencyMark> markOf, string family, List<Diagnostic> diagnostics, IReadOnlyDictionary<string, Ability> skillAbility)
    {
        var byField = new Dictionary<string, List<(RollFloorReason Reason, FloorStep Step)>>(StringComparer.Ordinal);
        foreach (var item in active)
        {
            var revision = item.Revision;
            foreach (var effect in revision.Effects.OfType<RollFloorEffect>())
            {
                // Validation refuses these; stored content that has them is skipped, not applied.
                if (effect.Automation != AutomationStatus.Automatic || effect.Timing != EffectTiming.Always)
                    continue;
                int floor;
                string description;
                RollFloorReason reason;
                if (effect.Face is { } face)
                {
                    floor = face;
                    description = $"Roll floor from {revision.Name}: a d20 below {face} counts as {face}";
                    reason = new(revision.Name, revision.Reference, face, null);
                }
                else if (effect.Total is { } source)
                {
                    if (!TryEvaluate(source, item, character, classLevels, values, [], out floor, out var failure))
                    {
                        diagnostics.Add(InvalidFormula(revision, effect, failure!));
                        continue;
                    }
                    description = $"Roll floor from {revision.Name}: a total below {floor} counts as {floor}";
                    reason = new(revision.Name, revision.Reference, null, floor);
                }
                else
                    continue;
                var step = new FloorStep(description, floor, ContentOrigin(family, item, effect));
                foreach (var field in RollFloorFields(effect.Target, skillAbility))
                {
                    if (effect.Proficient == true && markOf(field) == ProficiencyMark.None)
                        continue;
                    if (!byField.TryGetValue(field, out var list))
                        byField[field] = list = [];
                    list.Add((reason, step));
                }
            }
        }
        return byField.ToDictionary(
            p => p.Key,
            p => new FloorCollection(
                new(p.Value.Select(v => v.Reason.Face).Max(), p.Value.Select(v => v.Reason.Total).Max(), [.. p.Value.Select(v => v.Reason)]),
                [.. p.Value.Select(v => v.Step)]),
            StringComparer.Ordinal);
    }

    private static IEnumerable<string> RollFloorFields(string target, IReadOnlyDictionary<string, Ability> skillAbility)
    {
        if (target != RollFloorEffect.CheckTarget)
            return RollModeFields(target, skillAbility).Where(f => !f.StartsWith("save.", StringComparison.Ordinal));
        return [.. Enum.GetValues<Ability>().Select(FieldIds.Modifier), .. Skills.Select(s => FieldIds.Skill(s.Key)), FieldIds.Initiative];
    }

    private static List<Modifier> CollectModifiers(
        List<ActiveContent> active, Character character, RulesFamilyPolicy policy, List<Diagnostic> diagnostics, Dictionary<string, List<Diagnostic>> warnings,
        HashSet<string> manual, EffectiveToggles effective, bool autoOn, List<ResourceBonus> resourceBonuses, ClassLevelMap classLevels,
        IReadOnlyList<ModdedEntry> modded)
    {
        var modifiers = new List<Modifier>();
        // MECH-X item 3 (R49, R50): the modifiers of the active content, then those of each modded entry's mods (a mod's own top-level
        // modifiers and its nested ones), once per entry: the same mod on two entries applies twice. A mod is a leaf: it names no toggle
        // of the character's, so its modifiers are always on or, when not, left for the player like any assisted modifier.
        var sources = active
            .SelectMany(a => a.Revision.Effects.OfType<ModifierEffect>().Select(e => (Item: a, Effect: e, Leaf: false)))
            .Concat(modded.SelectMany(m => m.Mods.SelectMany(x =>
                x.Mod.Revision.Effects.OfType<ModifierEffect>().Concat(x.Effect.Modifiers ?? []).Select(e => (Item: x.Mod, Effect: e, Leaf: true)))));
        {
            foreach (var (item, effect, leaf) in sources)
            {
                var revision = item.Revision;
                string? followedToggle = null;
                var gate = !leaf && effect.Automation == AutomationStatus.Automatic && effect.Timing == EffectTiming.WhileActive && effect.Toggle is { } followed
                    ? GateOnToggle(active, revision, effect.Id, followed, effect.ToggleContent, effective, autoOn, diagnostics, out followedToggle)
                    : ToggleGate.Unresolved;
                if (gate == ToggleGate.Skips)
                    continue;
                if (gate == ToggleGate.Unresolved && (effect.Automation != AutomationStatus.Automatic || effect.Timing != EffectTiming.Always))
                {
                    if (SpecIndex.ContainsKey(effect.Target))
                        manual.Add(effect.Target);
                    else if (IsResourceBonus(revision, effect))
                        resourceBonuses.Add(new(item, effect, null, Manual: true)); // the player adds it; the pool reads assisted
                    continue;
                }
                // Content v11 (D43): resource.<id> raises that resource's maximum (CollectResources applies it). It is not a field, so it never
                // enters the field pipeline; only a bonus counts, and one naming no resource of its revision changes nothing (validation errors).
                if (IsResourceTarget(revision, effect))
                {
                    // Content v12 (G4): a pool maximum is not a calculated field, so a gear gate cannot be applied to it (validation refuses
                    // it); in an unvalidated draft the pool reads assisted and the player adds the bonus. Not on an item mod's leaf.
                    if (effect.Operation == ModifierOperation.Bonus)
                        resourceBonuses.Add(new(item, effect, followedToggle, Manual: !leaf && effect.NotWearing is { Count: > 0 } && revision.SchemaVersion >= ContentRevision.AbilityCapSchemaVersion));
                    continue;
                }
                if (!SpecIndex.ContainsKey(effect.Target))
                {
                    diagnostics.Add(new("effect.unknown-target", $"'{revision.Name}' changes '{effect.Target}', which is not something the sheet calculates; it is ignored.", revision.Reference, effect.Id));
                    continue;
                }
                if (IsCharacterOnlyField(effect.Target, revision.SchemaVersion) || IsPlayMechanicsOnlyField(effect.Target, revision.SchemaVersion))
                {
                    diagnostics.Add(new("effect.character-only-field", $"'{revision.Name}' changes '{FieldLabel(effect.Target)}', which content may not change yet; it is ignored.", revision.Reference, effect.Id));
                    continue;
                }
                if (IsV8Field(effect.Target) && IgnoresV8(revision))
                {
                    diagnostics.Add(V8FieldIgnored(revision, effect, $"the {FieldLabel(effect.Target)} field"));
                    continue;
                }
                if (item.Origin is { } origin && IsAbilityScore(effect) && origin != policy.AbilityIncreaseSource)
                {
                    var parent = (item.ChosenFrom ?? item.GrantedBy)!;
                    var via = origin == revision.Kind ? ""
                        : parent.Kind == origin ? $", from {KindText(origin)} '{parent.Name}'"
                        : $", from {KindText(origin)} content via {KindText(parent.Kind)} '{parent.Name}'";
                    warnings[effect.Target].Add(new(
                        "policy.ability-increase-source",
                        $"'{revision.Name}' ({KindText(revision.Kind)}{via}) cannot change ability scores under the {policy.ShortLabel}; only {KindText(policy.AbilityIncreaseSource)} content can. The effect is ignored.",
                        revision.Reference,
                        effect.Id));
                    continue;
                }
                if (effect.Stacking == StackingRule.HighestInGroup && string.IsNullOrWhiteSpace(effect.StackGroup))
                {
                    warnings[effect.Target].Add(new("effect.stack-group-missing", $"'{revision.Name}' has an effect that uses highest-in-group stacking without a stackGroup; it is ignored.", revision.Reference, effect.Id));
                    manual.Add(effect.Target);
                    continue;
                }
                if (!Formula.TryParse(effect.Value, Formula.ModifierFeaturesFor(revision, effect.Target), out var formula, out var error))
                {
                    warnings[effect.Target].Add(InvalidFormula(revision, effect, error!));
                    manual.Add(effect.Target);
                    continue;
                }
                // A named value this formula reads counts as the fields that value reads (content v11), so the order and the cycle check see them.
                var reads = FieldReads(formula!, item, classLevels);
                modifiers.Add(new(item, effect, formula!, reads, null, followedToggle, FromItemMod: leaf));
            }
        }
        return modifiers;
    }

    // SPEC C-01 / RulesFamilyPolicy: which *origin* content (species or background) may raise ability scores differs by
    // family; ActiveContent.Origin says which origin content counts as. Feats and class features may raise scores in
    // both families, so they are not restricted. A feature chosen from or granted by origin content, however many
    // features away, counts as that origin (a background's "+2 Str, +1 Con" option must not bypass the policy by being
    // a separate feature). Every operation counts, so `set` or `replace` cannot bypass it either.
    /// <summary>SRD: ability score increases stop at 20 (both families, so a rules constant rather than a policy field).</summary>
    public const int AbilityScoreIncreaseCap = 20;

    /// <summary>
    /// The ceiling of one ability-score increase: its own cap (schema 12 and later, 21 to 30, never on an item mod's modifier nor an
    /// armor mod's synthetic bonus), else 20. A cap out of range is ignored (validation refuses it).
    /// </summary>
    private static int AbilityCeiling(Modifier modifier) =>
        modifier.Effect.Cap is { } cap && cap is >= ModifierEffect.MinCap and <= ModifierEffect.MaxCap
            && !modifier.ModBonus && !modifier.FromItemMod && modifier.Content.Revision.SchemaVersion >= ContentRevision.AbilityCapSchemaVersion
            ? cap : AbilityScoreIncreaseCap;

    private static bool IsAbilityScoreField(string field) =>
        field.StartsWith("ability.", StringComparison.Ordinal) && field.EndsWith(".score", StringComparison.Ordinal);

    private static bool IsAbilityScore(ModifierEffect effect) =>
        effect.Target.StartsWith("ability.", StringComparison.Ordinal) && effect.Target.EndsWith(".score", StringComparison.Ordinal);

    private sealed record Proficiency(GrantKind Grant, ActiveContent Content, GrantEffect Effect);

    /// <summary>The prefix of weapon proficiency grant targets (content v5): <c>weapon.simple</c>, <c>weapon.martial</c> or <c>weapon.&lt;key&gt;</c>.</summary>
    public const string WeaponProficiencyPrefix = "weapon.";

    /// <summary>The prefix of armor training grant targets (content v8): <c>armor.light</c>, <c>armor.medium</c>, <c>armor.heavy</c>, <c>armor.shield</c>.</summary>
    public const string ArmorTrainingPrefix = "armor.";

    /// <summary>
    /// <c>armor.none</c> (content v8): a class records that it gives no armor training (the SRD Wizard and Sorcerer). It
    /// grants nothing; it lets the armor training check run for a character with such a class.
    /// </summary>
    public const string NoArmorTrainingKey = "none";

    /// <summary>
    /// Whether a class records its armor training (content v8): at least one <c>armor.*</c> grant, gated or not, including
    /// <c>armor.none</c>, and every one of them automatic and always on (the only ones that grant training). Classes
    /// written before v8 record none, and a class with an assisted or conditional armor grant has training the
    /// calculator cannot see, so the training check cannot tell what either gives.
    /// </summary>
    private static bool RecordsArmorTraining(ContentRevision revision)
    {
        if (revision.SchemaVersion < ContentRevision.CombatDetailsSchemaVersion)
            return false;
        var grants = revision.Effects.OfType<GrantEffect>()
            .Where(g => g.Grant == GrantKind.Proficiency && g.Target is { } target && target.StartsWith(ArmorTrainingPrefix, StringComparison.Ordinal))
            .ToList();
        return grants.Count > 0 && grants.All(g =>
            g.Automation == AutomationStatus.Automatic && g.Timing == EffectTiming.Always
            && (ArmorTrainingKeys.Contains(g.Target![ArmorTrainingPrefix.Length..]) || g.Target![ArmorTrainingPrefix.Length..] == NoArmorTrainingKey));
    }

    /// <summary>
    /// A content v8 field in a revision that declares an older schema. The validator refuses to publish that, but an
    /// imported or hand-edited revision can carry it; the calculator ignores the field, as an older build would.
    /// </summary>
    private static bool IgnoresV8(ContentRevision revision) => revision.SchemaVersion < ContentRevision.CombatDetailsSchemaVersion;

    /// <summary>The fields content v8 adds (<see cref="FieldIds.Attacks"/>, <see cref="FieldIds.CriticalRange"/>); older builds do not know them.</summary>
    private static bool IsV8Field(string field) => field is FieldIds.Attacks or FieldIds.CriticalRange;

    /// <summary>A content kind in running text: "feat", "creation guide".</summary>
    private static string KindText(ContentKind kind) => kind == ContentKind.CreationGuide ? "creation guide" : kind.ToString().ToLowerInvariant();

    private static Diagnostic V8FieldIgnored(ContentRevision revision, Effect effect, string field) =>
        new("effect.schema-field-ignored", $"'{revision.Name}' has an effect that uses {field}, which needs file version 8, but this entry is file version {revision.SchemaVersion}; it is ignored.", revision.Reference, effect.Id);

    /// <param name="unseenArmor">
    /// Armor training grants that apply to the character but that the calculator cannot apply (assisted, reference or
    /// conditional), on any content. Their training is unknown, so they turn the armor training check off.
    /// </param>
    private static Dictionary<string, Proficiency> CollectProficiencies(
        List<ActiveContent> active, Character character, List<Diagnostic> diagnostics, HashSet<string> manual, Func<ActiveContent, int> gateLevel,
        Dictionary<string, Proficiency> weapons, Dictionary<string, Proficiency> armor, Dictionary<string, Proficiency> unseenArmor)
    {
        var best = new Dictionary<string, Proficiency>(StringComparer.Ordinal);
        foreach (var item in active)
        {
            foreach (var grant in item.Revision.Effects.OfType<GrantEffect>())
            {
                // Damage defences (content v10) are display data here: not a proficiency, and nothing to calculate yet.
                if (grant.Grant == GrantKind.Content || GrantEffect.IsDamageDefence(grant.Grant) || GrantEffect.IsConditionImmunity(grant.Grant))
                    continue;
                if (grant.Level is { } needed && gateLevel(item) < needed)
                    continue;
                if (!EntryApplies(item, grant.OnlyAs, character))
                    continue;
                if (grant.Target is { } weapon && weapon.StartsWith(WeaponProficiencyPrefix, StringComparison.Ordinal) && weapon.Length > WeaponProficiencyPrefix.Length)
                {
                    if (grant.Automation == AutomationStatus.Automatic && grant.Timing == EffectTiming.Always)
                        weapons.TryAdd(weapon[WeaponProficiencyPrefix.Length..], new(GrantKind.Proficiency, item, grant));
                    continue;
                }
                if (grant.Target is { } armorTarget && armorTarget.StartsWith(ArmorTrainingPrefix, StringComparison.Ordinal))
                {
                    var key = armorTarget[ArmorTrainingPrefix.Length..];
                    if (IgnoresV8(item.Revision))
                        diagnostics.Add(V8FieldIgnored(item.Revision, grant, "armor training"));
                    else if (key == NoArmorTrainingKey)
                        continue; // a record only (RecordsArmorTraining)
                    else if (!ArmorTrainingKeys.Contains(key))
                        diagnostics.Add(new("effect.unknown-target", $"'{item.Revision.Name}' grants training in '{armorTarget}', which is not light armor, medium armor, heavy armor, a shield or none; it is ignored.", item.Revision.Reference, grant.Id));
                    else if (grant.Automation == AutomationStatus.Automatic && grant.Timing == EffectTiming.Always)
                        armor.TryAdd(key, new(GrantKind.Proficiency, item, grant));
                    else
                        unseenArmor.TryAdd(key, new(GrantKind.Proficiency, item, grant)); // training the calculator cannot apply
                    continue;
                }
                if (grant.Automation != AutomationStatus.Automatic || grant.Timing != EffectTiming.Always)
                {
                    if (grant.Target is { } pending && SpecIndex.ContainsKey(pending))
                        manual.Add(pending);
                    continue;
                }
                var target = grant.Target ?? "";
                if (!SpecIndex.ContainsKey(target) || !(target.StartsWith("save.", StringComparison.Ordinal) || target.StartsWith("skill.", StringComparison.Ordinal)))
                {
                    diagnostics.Add(new("effect.unknown-target", $"'{item.Revision.Name}' grants {grant.Grant.ToString().ToLowerInvariant()} in '{target}', which is not a saving throw or skill; it is ignored.", item.Revision.Reference, grant.Id));
                    continue;
                }
                if (grant.Grant == GrantKind.Expertise && target.StartsWith("save.", StringComparison.Ordinal))
                {
                    diagnostics.Add(new("effect.expertise-on-save", $"'{item.Revision.Name}' grants expertise in a saving throw, which is not supported; proficiency is applied instead.", item.Revision.Reference, grant.Id));
                }
                var kind = target.StartsWith("save.", StringComparison.Ordinal) ? GrantKind.Proficiency : grant.Grant;
                if (!best.TryGetValue(target, out var current) || (kind == GrantKind.Expertise && current.Grant != GrantKind.Expertise))
                    best[target] = new(kind, item, grant);
            }
        }
        return best;
    }
}
