namespace TomeStack.RulesCore;

/// <summary>What <see cref="ContentValidator"/> found. A revision with errors cannot be published (SPEC I-02, I-06; ADR-004).</summary>
public sealed record ValidationReport(ContentReference Revision, IReadOnlyList<Diagnostic> Errors, IReadOnlyList<Diagnostic> Warnings)
{
    public bool CanPublish => Errors.Count == 0;

    /// <summary>
    /// The lowest content schema version that holds everything the revision uses, and never below
    /// <see cref="ContentValidator.MinimumPublishedSchemaVersion"/>. <c>content.publish</c> writes it, so homebrew that
    /// uses no newer field stays readable by older builds.
    /// </summary>
    public int RequiredSchemaVersion { get; init; } = ContentValidator.MinimumPublishedSchemaVersion;
}

/// <summary>
/// M1 item 3: validates one content revision before it is published. It reports schema problems (shape and allowed
/// values), reference problems (source, granted content and choice options), formula problems (modifiers, resource
/// maximums, recovery amounts, roll dice) and dependency cycles. It is pure: the catalog is read, never written.
/// Errors block publishing; warnings (for example an effect type this build does not automate) do not.
/// </summary>
public static class ContentValidator
{
    /// <summary>
    /// The lowest version <c>content.publish</c> writes: builds that write v3 validate before publishing, and a package
    /// import blocks on validation errors only from v3 (older revisions predate validation).
    /// </summary>
    public const int MinimumPublishedSchemaVersion = 3;

    /// <summary>Spell fields (content v5) as modifier or restriction targets; an older build does not calculate them.</summary>
    private static bool IsSpellField(string field) =>
        field is FieldIds.SpellAttack or FieldIds.SpellSaveDc or FieldIds.PactSlots || field.StartsWith("spellSlots.", StringComparison.Ordinal);

    /// <param name="batch">Other unsaved revisions validated together, which may reference each other.</param>
    public static ValidationReport Validate(ContentRevision revision, IContentCatalog catalog, IEnumerable<ContentRevision>? batch = null)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(catalog);
        var errors = new List<Diagnostic>();
        var warnings = new List<Diagnostic>();
        var reference = revision.Reference;
        if (EmptyEntries(revision) is { Count: > 0 } empty)
            return new ValidationReport(reference, empty, []); // nothing else can be checked safely
        var local = (batch ?? []).ToDictionary(r => r.Reference);
        void Error(string code, string message, string? effectId = null) => errors.Add(new(code, message, reference, effectId));
        void Warn(string code, string message, string? effectId = null) => warnings.Add(new(code, message, reference, effectId));

        // ---- schema ----
        if (revision.ContentId == Guid.Empty || revision.RevisionId == Guid.Empty)
            Error("validate.id-missing", "The content and revision ids must be set.");
        if (revision.SchemaVersion is < 1 or > ContentRevision.CurrentSchemaVersion)
            Error("validate.schema-unsupported", $"Content schema v{revision.SchemaVersion} is not supported (v1 to v{ContentRevision.CurrentSchemaVersion}).");
        if (string.IsNullOrWhiteSpace(revision.Name))
            Error("validate.name-required", "A name is required.");
        if (revision.RulesFamilies.Count == 0)
            Error("validate.rules-family-required", "The revision must declare at least one rules family; support for both must be stated, not assumed.");
        foreach (var family in revision.RulesFamilies.Where(f => !RulesFamilies.IsKnown(f)).Distinct())
            Error("validate.rules-family-unknown", $"Rules family '{family}' is not supported.");
        if (revision.RulesFamilies.Distinct().Count() != revision.RulesFamilies.Count)
            Error("validate.rules-family-duplicate", "A rules family is listed more than once.");
        if (revision.Provenance.Page is { } page && (page.Start < 1 || page.End < page.Start))
            Error("validate.page-invalid", $"Page reference {page} is not a valid page or range.");

        foreach (var group in revision.Effects.GroupBy(e => e.Id).Where(g => g.Count() > 1 || string.IsNullOrWhiteSpace(g.Key)))
            Error("validate.effect-id", string.IsNullOrWhiteSpace(group.Key) ? "Every effect needs an id." : $"Effect id '{group.Key}' is used more than once.", group.Key);
        foreach (var group in revision.Effects.OfType<ChoiceEffect>().GroupBy(c => c.ChoiceId).Where(g => g.Count() > 1))
            Error("validate.choice-id-duplicate", $"Choice id '{group.Key}' is used more than once.", group.First().Id);
        foreach (var group in revision.Effects.OfType<ResourceEffect>().GroupBy(r => r.ResourceId).Where(g => g.Count() > 1))
            Error("validate.resource-id-duplicate", $"Resource id '{group.Key}' is used more than once.", group.First().Id);

        var needsV3 = false;
        var needsV5 = false;
        var needsV6 = false;
        var needsV8 = false;
        var spellTargets = false; // raises the written version only; older revisions with them were never refused
        foreach (var effect in revision.Effects)
        {
            switch (effect)
            {
                case UnknownEffect unknown:
                    Warn("validate.effect-unsupported", $"Effect '{unknown.Id}' has type '{unknown.DeclaredType}', which this version does not automate; it stays reference-only.", unknown.Id);
                    break;
                case ModifierEffect modifier:
                    needsV3 |= modifier.Target is FieldIds.ArmorClass or FieldIds.HitPoints;
                    needsV8 |= modifier.Target is FieldIds.Attacks or FieldIds.CriticalRange || modifier.WhileArmored is not null;
                    spellTargets |= IsSpellField(modifier.Target);
                    if (!CharacterCalculator.IsField(modifier.Target))
                        Error("validate.unknown-target", $"Effect '{modifier.Id}' targets '{modifier.Target}', which is not a calculated field.", modifier.Id);
                    // Content v8: a lower critical range is better, but set and replace keep the highest value, so only a
                    // bonus (such as -1 for Improved Critical) can lower it.
                    if (modifier.Target == FieldIds.CriticalRange && modifier.Operation != ModifierOperation.Bonus)
                        Error("validate.critical-range-operation", $"Effect '{modifier.Id}' uses {modifier.Operation.ToString().ToLowerInvariant()} on criticalRange; only a bonus can change it (for example -1 for a critical hit on 19-20).", modifier.Id);
                    // Worn armor turns off every Armor Class replacement (they are unarmored alternatives), so a replacement
                    // that applies only while armored could never apply.
                    if (modifier.WhileArmored == true && modifier.Target == FieldIds.ArmorClass && modifier.Operation == ModifierOperation.Replace)
                        Error("validate.while-armored-replace", $"Effect '{modifier.Id}' replaces Armor Class only while armor is worn, but worn armor sets the base Armor Class itself, so it could never apply. Use a bonus.", modifier.Id);
                    if (modifier.Stacking == StackingRule.HighestInGroup && string.IsNullOrWhiteSpace(modifier.StackGroup))
                        Error("validate.stack-group-missing", $"Effect '{modifier.Id}' uses highest-in-group stacking without a stackGroup.", modifier.Id);
                    CheckFormula(modifier.Value, modifier.Id, "value");
                    if (modifier.Toggle is { } toggle)
                    {
                        needsV6 = true;
                        if (modifier.Timing != EffectTiming.WhileActive)
                            Error("validate.toggle-timing", $"Modifier '{modifier.Id}' names toggle '{toggle}', so its timing must be whileActive.", modifier.Id);
                        if (!revision.Effects.OfType<ToggleEffect>().Any(t => t.ToggleId == toggle))
                            Error("validate.toggle-unknown", $"Modifier '{modifier.Id}' names toggle '{toggle}', which this revision does not define.", modifier.Id);
                    }
                    break;
                case ToggleEffect toggleEffect:
                    needsV6 = true;
                    if (string.IsNullOrWhiteSpace(toggleEffect.ToggleId) || string.IsNullOrWhiteSpace(toggleEffect.Label))
                        Error("validate.toggle-incomplete", $"Toggle '{toggleEffect.Id}' needs a toggleId and a label.", toggleEffect.Id);
                    if (toggleEffect.ResourceId is { } spends && !revision.Effects.OfType<ResourceEffect>().Any(r => r.ResourceId == spends))
                        Error("validate.toggle-resource", $"Toggle '{toggleEffect.Id}' spends '{spends}', which this revision does not define.", toggleEffect.Id);
                    break;
                case GrantEffect grant:
                    needsV3 |= grant.Level is not null || grant.Target is FieldIds.ArmorClass or FieldIds.HitPoints;
                    CheckLevel(grant.Level, grant.Id);
                    if (grant.Grant == GrantKind.Content)
                    {
                        if (grant.Content is not { } granted)
                            Error("validate.grant-content-missing", $"Effect '{grant.Id}' grants content but names none.", grant.Id);
                        else
                            CheckReference(granted, grant.Id, "granted content");
                    }
                    else if (grant.Target is { } weaponTarget && weaponTarget.StartsWith(CharacterCalculator.WeaponProficiencyPrefix, StringComparison.Ordinal))
                    {
                        needsV5 = true;
                        if (weaponTarget.Length == CharacterCalculator.WeaponProficiencyPrefix.Length || grant.Grant != GrantKind.Proficiency)
                            Error("validate.unknown-target", $"Effect '{grant.Id}' must grant proficiency in weapon.simple, weapon.martial or weapon.<key>.", grant.Id);
                    }
                    else if (grant.Target is { } armorTarget && armorTarget.StartsWith(CharacterCalculator.ArmorTrainingPrefix, StringComparison.Ordinal))
                    {
                        needsV8 = true;
                        var armorKey = armorTarget[CharacterCalculator.ArmorTrainingPrefix.Length..];
                        if (!(CharacterCalculator.ArmorTrainingKeys.Contains(armorKey) || armorKey == CharacterCalculator.NoArmorTrainingKey) || grant.Grant != GrantKind.Proficiency)
                            Error("validate.unknown-target", $"Effect '{grant.Id}' must grant proficiency in armor.light, armor.medium, armor.heavy or armor.shield, or record armor.none.", grant.Id);
                        if (armorKey == CharacterCalculator.NoArmorTrainingKey && revision.Kind != ContentKind.Class)
                            Warn("validate.armor-none-kind", $"armor.none records that a class gives no armor training; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content, so it has no effect.", grant.Id);
                    }
                    else if (grant.Target is not { } target || !CharacterCalculator.IsField(target) || !(target.StartsWith("save.", StringComparison.Ordinal) || target.StartsWith("skill.", StringComparison.Ordinal)))
                    {
                        Error("validate.unknown-target", $"Effect '{grant.Id}' grants {grant.Grant.ToString().ToLowerInvariant()} in '{grant.Target}', which is not a saving throw, skill or weapon.", grant.Id);
                    }
                    needsV5 |= grant.OnlyAs is not null;
                    break;
                case ChoiceEffect choice:
                    needsV3 |= choice.Level is not null;
                    CheckLevel(choice.Level, choice.Id);
                    if (string.IsNullOrWhiteSpace(choice.ChoiceId))
                        Error("validate.choice-id-required", $"Choice effect '{choice.Id}' needs a choiceId.", choice.Id);
                    if (choice.Options.Count == 0)
                        Error("validate.choice-options-empty", $"Choice '{choice.ChoiceId}' has no options.", choice.Id);
                    if (choice.Options.Distinct().Count() != choice.Options.Count)
                        Error("validate.choice-option-duplicate", $"Choice '{choice.ChoiceId}' lists an option more than once.", choice.Id);
                    if (choice.Count < 1 || choice.Count > choice.Options.Distinct().Count())
                        Error("validate.choice-count", $"Choice '{choice.ChoiceId}' asks for {choice.Count} of {choice.Options.Distinct().Count()} option(s).", choice.Id);
                    foreach (var option in choice.Options.Distinct())
                        CheckReference(option, choice.Id, $"option of choice '{choice.ChoiceId}'");
                    needsV5 |= choice.OnlyAs is not null;
                    break;
                case RestrictionEffect restriction:
                    needsV3 |= restriction.Field is FieldIds.ArmorClass or FieldIds.HitPoints;
                    needsV5 |= restriction.Multiclass is not null || restriction.Group is not null;
                    needsV8 |= restriction.Field is FieldIds.Attacks or FieldIds.CriticalRange;
                    spellTargets |= IsSpellField(restriction.Field);
                    if (restriction.Multiclass == true && revision.Kind != ContentKind.Class)
                        Warn("validate.multiclass-kind", $"A multiclass prerequisite belongs on a class; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content.", restriction.Id);
                    if (!CharacterCalculator.IsField(restriction.Field))
                        Error("validate.unknown-target", $"Restriction '{restriction.Id}' checks '{restriction.Field}', which is not a calculated field.", restriction.Id);
                    if (Math.Abs(restriction.Minimum) > FormulaLimits.MaxLiteral)
                        Error("validate.restriction-minimum", $"Restriction '{restriction.Id}' minimum {restriction.Minimum} is outside ±{FormulaLimits.MaxLiteral}.", restriction.Id);
                    break;
                case ResourceEffect resource:
                    if (string.IsNullOrWhiteSpace(resource.ResourceId) || string.IsNullOrWhiteSpace(resource.Label))
                        Error("validate.resource-incomplete", $"Resource effect '{resource.Id}' needs a resourceId and a label.", resource.Id);
                    CheckFormula(resource.Maximum, resource.Id, "maximum");
                    break;
                case RecoveryEffect recovery:
                    if (!string.Equals(recovery.Amount, "all", StringComparison.Ordinal))
                        CheckFormula(recovery.Amount, recovery.Id, "amount");
                    if (!revision.Effects.OfType<ResourceEffect>().Any(r => r.ResourceId == recovery.ResourceId))
                        Warn("validate.recovery-resource", $"Recovery '{recovery.Id}' restores '{recovery.ResourceId}', which this revision does not define (fine if another revision does).", recovery.Id);
                    break;
                case RollEffect roll:
                    if (!DiceExpression.TryParse(roll.Dice, out _, out var diceError))
                        Error("validate.dice-invalid", $"Roll '{roll.Id}' dice '{roll.Dice}': {diceError!.Message} ({diceError.Code})", roll.Id);
                    needsV5 |= roll.Activation is not null;
                    needsV6 |= roll.ResourceContent is not null || roll.Cost is not null || roll.VariableCost is not null;
                    if (roll.Cost is { } cost)
                        CheckFormula(cost, roll.Id, "cost");
                    if (roll.Bonus is { } bonus)
                    {
                        needsV8 = true;
                        CheckFormula(bonus, roll.Id, "bonus");
                    }
                    if ((roll.Cost is not null || roll.VariableCost == true) && roll.ResourceId is null)
                        Error("validate.roll-cost", $"Roll '{roll.Id}' has a cost but names no resource to spend.", roll.Id);
                    if (roll.ResourceContent is { } holder && holder != revision.ContentId && roll.ResourceId is { } shared)
                    {
                        var definer = local.Values.Concat(catalog.RevisionsOf(holder)).FirstOrDefault(r => r.ContentId == holder);
                        if (definer is null)
                            Warn("validate.roll-resource-content-missing", $"Roll '{roll.Id}' spends '{shared}' of content {holder}, which is not installed.", roll.Id);
                        else if (!definer.Effects.OfType<ResourceEffect>().Any(r => r.ResourceId == shared))
                            Error("validate.roll-resource-content", $"Roll '{roll.Id}': '{definer.Name}' defines no resource '{shared}'.", roll.Id);
                    }
                    break;
                case WeaponEffect weapon:
                    foreach (var (weaponDice, what) in new[] { (weapon.Damage, "damage"), (weapon.Versatile, "versatile damage") })
                    {
                        if (weaponDice is not null && !DiceExpression.TryParse(weaponDice, out _, out var weaponDiceError))
                            Error("validate.dice-invalid", $"Weapon '{weapon.Id}' {what} '{weaponDice}': {weaponDiceError!.Message} ({weaponDiceError.Code})", weapon.Id);
                    }
                    if (string.IsNullOrWhiteSpace(weapon.WeaponKey) || string.IsNullOrWhiteSpace(weapon.DamageType))
                        Error("validate.weapon-incomplete", $"Weapon '{weapon.Id}' needs a weaponKey and a damageType.", weapon.Id);
                    if (weapon.Has("versatile") != (weapon.Versatile is not null))
                        Warn("validate.weapon-versatile", $"Weapon '{weapon.Id}': the versatile property and versatile damage go together.", weapon.Id);
                    if (revision.Kind != ContentKind.Item)
                        Warn("validate.weapon-kind", $"A weapon gives an attack only on an equipped item; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content.", weapon.Id);
                    break;
                case HitDieEffect hitDie:
                    needsV3 = true;
                    if (!HitDieEffect.AllowedDice.Contains(hitDie.Die))
                        Error("validate.hit-die", $"Hit die d{hitDie.Die} is not one of d6, d8, d10 or d12.", hitDie.Id);
                    if (revision.Kind != ContentKind.Class)
                        Warn("validate.hit-die-kind", $"Only a class's hit die is used; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content.", hitDie.Id);
                    break;
                case ArmorEffect armor:
                    if (armor.ArmorClass is < 0 or > 30)
                        Error("validate.armor-class", $"Armor '{armor.Id}' gives Armor Class {armor.ArmorClass}; it must be between 0 and 30.", armor.Id);
                    if (armor.DexterityCap is { } cap && (armor.Category != ArmorCategory.Medium || cap is < 0 or > 10))
                        Error("validate.armor-dexterity-cap", $"Armor '{armor.Id}': a Dexterity cap (0 to 10) applies only to medium armor.", armor.Id);
                    needsV8 |= armor.Strength is not null || armor.StealthDisadvantage is not null;
                    if (armor.Strength is { } strength && (strength is < 1 or > 30 || armor.Category == ArmorCategory.Shield))
                        Error("validate.armor-strength", $"Armor '{armor.Id}': a Strength requirement (1 to 30) applies only to body armor.", armor.Id);
                    if (revision.Kind != ContentKind.Item)
                        Warn("validate.armor-kind", $"Armor counts only on an equipped item; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content, so it applies only if pinned.", armor.Id);
                    break;
                case SpellcastingEffect spellcasting:
                    if (CharacterCalculator.SpellcastingProblem(spellcasting) is { } problem)
                        Error("validate.spellcasting", $"Spellcasting '{spellcasting.Id}': {problem}.", spellcasting.Id);
                    if (spellcasting.SpellsFormula is { } spellsFormula)
                        CheckFormula(spellsFormula, spellcasting.Id, "spellsFormula");
                    if (spellcasting.MulticlassCaster is not null && spellcasting.SlotKind == SpellSlotKind.PactMagic)
                        Error("validate.spellcasting-multiclass-pact", $"Spellcasting '{spellcasting.Id}': Pact Magic slots are never combined with other casters' slots, so it takes no multiclassCaster.", spellcasting.Id);
                    if (revision.Kind is not (ContentKind.Class or ContentKind.Subclass or ContentKind.Feature))
                        Warn("validate.spellcasting-kind", $"Spellcasting is calculated only inside a class; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content.", spellcasting.Id);
                    break;
                case SpellEffect spell:
                    if (spell.Level is < 0 or > SpellcastingEffect.MaxSpellLevel)
                        Error("validate.spell-level", $"Spell '{spell.Id}' level {spell.Level} must be 0 (cantrip) to {SpellcastingEffect.MaxSpellLevel}.", spell.Id);
                    if (spell.Lists.Count == 0 || spell.Lists.Any(string.IsNullOrWhiteSpace))
                        Warn("validate.spell-lists", $"Spell '{spell.Id}' is on no spell list; casters can record it only by hand.", spell.Id);
                    if (spell.Dice is { } dice && !DiceExpression.TryParse(dice, out _, out var spellDiceError))
                        Error("validate.dice-invalid", $"Spell '{spell.Id}' dice '{dice}': {spellDiceError!.Message} ({spellDiceError.Code})", spell.Id);
                    if (revision.Kind != ContentKind.Spell)
                        Warn("validate.spell-kind", $"Spell data is used only on spell content; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content.", spell.Id);
                    break;
            }
        }
        if (revision.Effects.OfType<SpellcastingEffect>().Count() > 1)
            Error("validate.spellcasting-duplicate", "A revision declares at most one spellcasting feature.");
        if (revision.Effects.OfType<SpellEffect>().Count() > 1)
            Error("validate.spell-duplicate", "A spell revision declares its spell data once.");
        if (revision.Kind == ContentKind.Spell && !revision.Effects.OfType<SpellEffect>().Any())
            Warn("validate.spell-data-missing", "This spell has no spell data (level, lists), so no caster can use it from the sheet.");
        if (revision.Effects.Any(e => e is SpellcastingEffect or SpellEffect or WeaponEffect) && revision.SchemaVersion < SpellcastingEffect.SchemaVersion)
            Error("validate.requires-v5", $"This revision has spellcasting, spell or weapon data (content schema v5) but declares v{revision.SchemaVersion}; in an older revision they are reference only.");
        if (needsV6 && revision.SchemaVersion < ToggleEffect.SchemaVersion)
            Error("validate.requires-v6", $"This revision uses content schema v6 features (toggles, toggled modifiers, shared resources, roll costs) but declares v{revision.SchemaVersion}; an older build would ignore them.");
        if (revision.Effects.OfType<SpellcastingEffect>().Any(s => s.MulticlassCaster is not null) && revision.SchemaVersion < SpellcastingEffect.MulticlassSchemaVersion)
            Error("validate.requires-v7", $"This revision says how its caster levels combine (multiclassCaster, content schema v7) but declares v{revision.SchemaVersion}; an older build would ignore it.");
        if (needsV8 && revision.SchemaVersion < ContentRevision.CombatDetailsSchemaVersion)
            Error("validate.requires-v8", $"This revision uses content schema v8 fields (attacks, criticalRange, armor training, armor strength or stealth, whileArmored, roll bonus) but declares v{revision.SchemaVersion}; an older build would ignore or misread them.");
        if (needsV5 && revision.SchemaVersion < SpellcastingEffect.SchemaVersion)
            Error("validate.requires-v5", $"This revision uses content schema v5 fields (onlyAs, multiclass or group restrictions, weapon proficiencies, roll activation) but declares v{revision.SchemaVersion}; an older build would ignore them.");
        if (revision.Effects.OfType<ArmorEffect>().Count(a => a.Category != ArmorCategory.Shield) > 1 || revision.Effects.OfType<ArmorEffect>().Count(a => a.Category == ArmorCategory.Shield) > 1)
            Error("validate.armor-duplicate", "An item is at most one armor and one shield.");
        if (revision.Effects.OfType<HitDieEffect>().Count() > 1)
            Error("validate.hit-die-duplicate", "A class declares one hit die.");
        if (revision.Kind == ContentKind.Class && !revision.Effects.OfType<HitDieEffect>().Any())
            Warn("validate.hit-die-missing", "This class declares no hit die, so it adds no hit points.");
        if (needsV3 && revision.SchemaVersion < 3)
            Error("validate.requires-v3", $"This revision uses content schema v3 features (levels, hitDie, armorClass or hitPoints) but declares v{revision.SchemaVersion}; a v2 build would misread it.");
        if (revision.ExtendsChoice is not null && revision.SchemaVersion < 4)
            Error("validate.requires-v4", $"This revision extends a choice (content schema v4) but declares v{revision.SchemaVersion}; a v3 build would ignore it.");
        if (revision.Effects.OfType<ArmorEffect>().Any() && revision.SchemaVersion < ArmorEffect.SchemaVersion)
            Error("validate.requires-v4", $"This revision has armor (content schema v4) but declares v{revision.SchemaVersion}; armor in an older revision is reference only.");

        // ---- references ----
        if (catalog.FindSource(revision.Provenance.SourceId) is null)
            Error("validate.source-missing", $"Source {revision.Provenance.SourceId} is not installed.");
        if (revision.ExtendsChoice is { } extends)
        {
            var targets = catalog.RevisionsOf(extends.ContentId).Concat(local.Values.Where(r => r.ContentId == extends.ContentId)).ToList();
            if (extends.ContentId == revision.ContentId)
                Error("validate.self-reference", "A revision cannot add itself to one of its own choices.");
            else if (targets.Count == 0)
                Warn("validate.extends-choice-missing", $"The content {extends.ContentId} whose choice '{extends.ChoiceId}' this extends is not installed; the option appears once it is.");
            else if (!targets.Any(t => t.Effects.OfType<ChoiceEffect>().Any(c => c.ChoiceId == extends.ChoiceId)))
                Error("validate.extends-choice-unknown", $"'{targets[^1].Name}' offers no choice '{extends.ChoiceId}'.");
            else if (!targets.Any(t => t.RulesFamilies.Intersect(revision.RulesFamilies).Any()))
                Error("validate.reference-family", $"'{targets[^1].Name}' supports {string.Join(", ", targets[^1].RulesFamilies)}, none of this revision's families.");
        }

        // ---- cycles ----
        foreach (var cycle in CharacterCalculator.DependencyCycles(revision))
            errors.Add(cycle);

        // The same features as the requires-vN checks above, highest first. Typed effects are covered too: armor (v4),
        // spellcasting, spell and weapon (v5) and toggle (v6) are read as typed only from their version.
        var required =
            needsV8 ? ContentRevision.CombatDetailsSchemaVersion
            : revision.Effects.OfType<SpellcastingEffect>().Any(s => s.MulticlassCaster is not null) ? SpellcastingEffect.MulticlassSchemaVersion
            : needsV6 ? ToggleEffect.SchemaVersion
            : needsV5 || spellTargets || revision.Effects.Any(e => e is SpellcastingEffect or SpellEffect or WeaponEffect) ? SpellcastingEffect.SchemaVersion
            : revision.ExtendsChoice is not null || revision.Effects.OfType<ArmorEffect>().Any() ? ArmorEffect.SchemaVersion
            : MinimumPublishedSchemaVersion;
        return new ValidationReport(reference, errors, warnings) { RequiredSchemaVersion = Math.Max(required, MinimumPublishedSchemaVersion) };

        void CheckFormula(string source, string effectId, string what)
        {
            if (!Formula.TryParse(source, out _, out var error))
                Error("validate.formula-invalid", $"Effect '{effectId}' {what} '{source}': {error!.Message} ({error.Code})", effectId);
        }

        void CheckLevel(int? level, string effectId)
        {
            if (level is < Character.MinLevel or > Character.MaxLevel)
                Error("validate.level", $"Effect '{effectId}' level {level} must be between {Character.MinLevel} and {Character.MaxLevel}.", effectId);
        }

        void CheckReference(ContentReference target, string effectId, string what)
        {
            if (target.ContentId == revision.ContentId)
            {
                Error("validate.self-reference", $"Effect '{effectId}' names this content itself as its {what}.", effectId);
                return;
            }
            var found = local.GetValueOrDefault(target) ?? catalog.FindRevision(target);
            if (found is null)
            {
                Error("validate.reference-missing", $"Effect '{effectId}': {what} {target.RevisionId} is not installed.", effectId);
                return;
            }
            if (found.Status != RevisionStatus.Published)
                Warn("validate.reference-unpublished", $"Effect '{effectId}': {what} '{found.Name}' is a draft; it stays inactive until published.", effectId);
            if (!found.RulesFamilies.Intersect(revision.RulesFamilies).Any())
                Error("validate.reference-family", $"Effect '{effectId}': {what} '{found.Name}' supports {string.Join(", ", found.RulesFamilies)}, none of this revision's families.", effectId);
        }
    }

    /// <summary>
    /// Empty (null) list items, which untrusted JSON can carry past nullable annotations (SPEC Q-02): in the effects,
    /// the rules families, and choice options. Even a draft must be free of them (<c>content.saveDraft</c>).
    /// </summary>
    public static IReadOnlyList<Diagnostic> EmptyEntries(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var problems = new List<Diagnostic>();
        void Check(string list, bool empty)
        {
            if (empty)
                problems.Add(new("validate.empty-entry", $"The revision's {list} list contains an empty entry.", revision.Reference));
        }
        Check("effects", revision.Effects is null || revision.Effects.Any(e => e is null));
        Check("rulesFamilies", revision.RulesFamilies is null || revision.RulesFamilies.Any(f => f is null));
        Check("choice options", revision.Effects?.OfType<ChoiceEffect>().Any(c => c.Options is null || c.Options.Any(o => o is null)) == true);
        return problems;
    }
}
