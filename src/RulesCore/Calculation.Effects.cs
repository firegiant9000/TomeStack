namespace TomeStack.RulesCore;

public static partial class CharacterCalculator
{
    // ---- effects --------------------------------------------------------------------------------------------

    /// <param name="SkipReason">Set when the rules say this modifier does not apply now (for example, Unarmored Defense while armor is worn); it is traced as not used.</param>
    /// <param name="FollowedToggle">Set when the modifier follows another entry's toggle (content v11 <c>toggleContent</c>): that toggle's label, named in the trace.</param>
    private sealed record Modifier(ActiveContent Content, ModifierEffect Effect, Formula Formula, IReadOnlyList<string> ReadsFields, string? SkipReason = null, string? FollowedToggle = null, bool ModBonus = false);

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
    /// count; extra ones get a diagnostic.
    /// </summary>
    /// <summary>The body armor and shield that count (at most one of each).</summary>
    private sealed record WornArmor((ActiveContent Content, ArmorEffect Effect)? Body, (ActiveContent Content, ArmorEffect Effect)? Shield);

    /// <remarks>
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
                        ? $"This character has no training with shields: '{content.Revision.Name}' still adds to Armor Class under {policy.DisplayName}, with disadvantage on d20 tests that use Strength or Dexterity, and no spellcasting."
                        : $"This character has no training with shields, so '{content.Revision.Name}' adds nothing to Armor Class under {policy.DisplayName}.",
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
                    $"'{revision.Name}' effect '{effectId}' follows toggle '{toggleId}' of content {ownerId}, which is not active or does not declare it; the effect is not applied.",
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
    private static Dictionary<string, RollModeSummary> CollectRollModes(
        List<ActiveContent> active, EffectiveToggles effective, bool autoOn, List<Diagnostic> diagnostics)
    {
        var byField = new Dictionary<string, List<RollModeReason>>(StringComparer.Ordinal);
        foreach (var item in active)
        {
            var revision = item.Revision;
            foreach (var effect in revision.Effects.OfType<RollModeEffect>())
            {
                if (effect.Toggle is { } toggle
                    && GateOnToggle(active, revision, effect.Id, toggle, effect.ToggleContent, effective, autoOn, diagnostics, out _) != ToggleGate.Applies)
                    continue;
                var reason = new RollModeReason(effect.Mode, revision.Name, revision.Reference, effect.Against, effect.Automation);
                foreach (var field in RollModeFields(effect.Target))
                {
                    if (!byField.TryGetValue(field, out var reasons))
                        byField[field] = reasons = [];
                    reasons.Add(reason);
                }
            }
        }
        // Automatic combines only the reasons that need no confirmation (no against) from an automatic effect; the rest are listed notes.
        return byField.ToDictionary(
            p => p.Key,
            p => new RollModeSummary(
                RollModes.Combine(p.Value.Where(r => r.IsAutomatic).Select(r => r.Mode)),
                [.. p.Value]),
            StringComparer.Ordinal);
    }

    private static IEnumerable<string> RollModeFields(string target)
    {
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
            foreach (var skill in Skills.Where(s => s.Ability == ability))
                yield return FieldIds.Skill(skill.Key);
            if (ability == Ability.Dex)
                yield return FieldIds.Initiative;
        }
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
                    if (effect.Operation == ModifierOperation.Bonus)
                        resourceBonuses.Add(new(item, effect, followedToggle));
                    continue;
                }
                if (!SpecIndex.ContainsKey(effect.Target))
                {
                    diagnostics.Add(new("effect.unknown-target", $"'{revision.Name}' effect '{effect.Id}' targets '{effect.Target}', which is not a calculated field; it is ignored.", revision.Reference, effect.Id));
                    continue;
                }
                if (IsCharacterOnlyField(effect.Target, revision.SchemaVersion) || IsPlayMechanicsOnlyField(effect.Target, revision.SchemaVersion))
                {
                    diagnostics.Add(new("effect.character-only-field", $"'{revision.Name}' effect '{effect.Id}' targets '{effect.Target}', which content may not target yet; it is ignored.", revision.Reference, effect.Id));
                    continue;
                }
                if (IsV8Field(effect.Target) && IgnoresV8(revision))
                {
                    diagnostics.Add(V8FieldIgnored(revision, effect, $"the {effect.Target} field"));
                    continue;
                }
                if (item.Origin is { } origin && IsAbilityScore(effect) && origin != policy.AbilityIncreaseSource)
                {
                    var parent = (item.ChosenFrom ?? item.GrantedBy)!;
                    var via = origin == revision.Kind ? ""
                        : parent.Kind == origin ? $", from {origin.ToString().ToLowerInvariant()} '{parent.Name}'"
                        : $", from {origin.ToString().ToLowerInvariant()} content via {parent.Kind.ToString().ToLowerInvariant()} '{parent.Name}'";
                    warnings[effect.Target].Add(new(
                        "policy.ability-increase-source",
                        $"'{revision.Name}' ({revision.Kind}{via}) cannot change ability scores under {policy.DisplayName}; only {policy.AbilityIncreaseSource} content can. The effect is ignored.",
                        revision.Reference,
                        effect.Id));
                    continue;
                }
                if (effect.Stacking == StackingRule.HighestInGroup && string.IsNullOrWhiteSpace(effect.StackGroup))
                {
                    warnings[effect.Target].Add(new("effect.stack-group-missing", $"'{revision.Name}' effect '{effect.Id}' uses highest-in-group stacking without a stackGroup; it is ignored.", revision.Reference, effect.Id));
                    manual.Add(effect.Target);
                    continue;
                }
                if (!Formula.TryParse(effect.Value, Formula.FeaturesFor(revision), out var formula, out var error))
                {
                    warnings[effect.Target].Add(InvalidFormula(revision, effect, error!));
                    manual.Add(effect.Target);
                    continue;
                }
                // A named value this formula reads counts as the fields that value reads (content v11), so the order and the cycle check see them.
                var reads = FieldReads(formula!, item, classLevels);
                modifiers.Add(new(item, effect, formula!, reads, null, followedToggle));
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

    private static Diagnostic V8FieldIgnored(ContentRevision revision, Effect effect, string field) =>
        new("effect.schema-field-ignored", $"'{revision.Name}' effect '{effect.Id}' uses {field}, a content schema v8 field, but the revision declares v{revision.SchemaVersion}; it is ignored.", revision.Reference, effect.Id);

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
                if (grant.Grant == GrantKind.Content || GrantEffect.IsDamageDefence(grant.Grant))
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
                        diagnostics.Add(new("effect.unknown-target", $"'{item.Revision.Name}' effect '{grant.Id}' grants training in '{armorTarget}', which is not armor.light, armor.medium, armor.heavy, armor.shield or armor.none; it is ignored.", item.Revision.Reference, grant.Id));
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
                    diagnostics.Add(new("effect.unknown-target", $"'{item.Revision.Name}' effect '{grant.Id}' grants {grant.Grant.ToString().ToLowerInvariant()} in '{target}', which is not a calculated saving throw or skill; it is ignored.", item.Revision.Reference, grant.Id));
                    continue;
                }
                if (grant.Grant == GrantKind.Expertise && target.StartsWith("save.", StringComparison.Ordinal))
                {
                    diagnostics.Add(new("effect.expertise-on-save", $"'{item.Revision.Name}' effect '{grant.Id}' grants expertise in a saving throw, which is not supported; proficiency is applied instead.", item.Revision.Reference, grant.Id));
                }
                var kind = target.StartsWith("save.", StringComparison.Ordinal) ? GrantKind.Proficiency : grant.Grant;
                if (!best.TryGetValue(target, out var current) || (kind == GrantKind.Expertise && current.Grant != GrantKind.Expertise))
                    best[target] = new(kind, item, grant);
            }
        }
        return best;
    }
}
