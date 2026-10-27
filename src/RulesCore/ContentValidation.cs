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

/// <summary>Revisions validated together, keyed by reference and (once, on first use) by content id.</summary>
internal sealed class ValidationBatch(IReadOnlyDictionary<ContentReference, ContentRevision> byReference)
{
    private ILookup<Guid, ContentRevision>? _byContent;

    public ContentRevision? GetValueOrDefault(ContentReference reference) => byReference.GetValueOrDefault(reference);

    /// <summary>The batch's revisions of one content, in batch order.</summary>
    public IEnumerable<ContentRevision> OfContent(Guid contentId) => (_byContent ??= byReference.Values.ToLookup(r => r.ContentId))[contentId];
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

    /// <summary>
    /// The most a choice can ask for when it declares no options of its own (the v9 JSON schema's choice.count maximum);
    /// with options, the count is bounded by how many there are.
    /// </summary>
    public const int MaxChoiceCountWithoutOptions = 20;

    /// <summary>The most upkeep effects one revision declares (content v11); each runs every turn, so a bound keeps a turn small.</summary>
    public const int MaxUpkeepsPerRevision = 20;

    /// <summary>The most response effects one revision declares (content v11); each is checked on every damage event, so a bound keeps one small.</summary>
    public const int MaxResponsesPerRevision = 20;

    /// <summary>The most outcomes one response declares (content v11).</summary>
    public const int MaxOutcomesPerResponse = 8;

    /// <summary>The most other pools one roll also changes (content v11, <c>alsoChange</c>).</summary>
    public const int MaxAlsoChanges = 4;

    /// <summary>The largest weapon bonus (content v12, <c>weapon.bonus</c>); the smallest is 0.</summary>
    internal const int MaxWeaponBonus = 10;

    /// <summary>The largest area size or line width a roll declares, in feet (content v11).</summary>
    private const int MaxAreaFeet = 1_000;

    /// <summary>The most units a per-unit roll may count (content v11).</summary>
    private const int MaxPerUnitUnits = 100;

    /// <summary>Every formula identifier family, so a feature used in a revision that is too old is named, not called unknown.</summary>
    private const FormulaFeatures AllFormulaFeatures = FormulaFeatures.Scales | FormulaFeatures.Values;

    /// <summary>
    /// A <see cref="SpellcastingEffect.MulticlassCasterTable"/> has 20 entries, each 0 to 20 and at most the class level,
    /// and never lower than the entry before it (a class never loses caster levels as it gains class levels).
    /// </summary>
    internal static string? MulticlassTableProblem(IReadOnlyList<int> table)
    {
        if (table.Count != Character.MaxLevel)
            return $"it needs {Character.MaxLevel} entries (class levels 1 to {Character.MaxLevel}), not {table.Count}";
        for (var i = 0; i < table.Count; i++)
        {
            if (table[i] < 0 || table[i] > i + 1)
                return $"at class level {i + 1} it counts {table[i]} caster level(s); it must be 0 to {i + 1}";
            if (i > 0 && table[i] < table[i - 1])
                return $"at class level {i + 1} it counts {table[i]}, fewer than the {table[i - 1]} at level {i}";
        }
        return null;
    }

    /// <summary>Content v11 (D43): a modifier target <c>resource.&lt;resourceId&gt;</c> raises the maximum of a pool.</summary>
    internal const string ResourceTargetPrefix = "resource.";

    /// <summary>Whether an untyped toggle carries a content v11 key (any spelling), which decides the version its failure needs.</summary>
    private static bool CarriesV11Key(UnknownEffect effect, string[] keys) =>
        effect.Raw.ValueKind == System.Text.Json.JsonValueKind.Object
        && effect.Raw.EnumerateObject().Any(p => keys.Contains(p.Name, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Content v11 (D43): the targets of a <c>rollMode</c>, exactly <c>save.&lt;ability&gt;</c>, <c>skill.&lt;skill&gt;</c> or
    /// <c>ability.&lt;ability&gt;.check</c> (the ability check is a roll target only, not a sheet field).
    /// </summary>
    internal static bool IsRollModeTarget(string? target)
    {
        if (target is null)
            return false;
        var abilities = Enum.GetValues<Ability>().Select(FieldIds.Key).ToList();
        if (target.StartsWith("save.", StringComparison.Ordinal))
            return abilities.Contains(target["save.".Length..], StringComparer.Ordinal);
        if (target.StartsWith("skill.", StringComparison.Ordinal))
            return CharacterCalculator.Skills.Any(s => s.Key == target["skill.".Length..]);
        const string abilityPrefix = "ability.";
        const string checkSuffix = ".check";
        return target.StartsWith(abilityPrefix, StringComparison.Ordinal) && target.EndsWith(checkSuffix, StringComparison.Ordinal)
            && target.Length > abilityPrefix.Length + checkSuffix.Length
            && abilities.Contains(target[abilityPrefix.Length..^checkSuffix.Length], StringComparer.Ordinal);
    }

    private static bool IsCharacterOnlyField(string field, ContentRevision revision) =>
        CharacterCalculator.IsCharacterOnlyField(field, revision.SchemaVersion);

    /// <summary>Spell fields (content v5) as modifier or restriction targets; an older build does not calculate them.</summary>
    private static bool IsSpellField(string field) =>
        field is FieldIds.SpellAttack or FieldIds.SpellSaveDc or FieldIds.PactSlots || field.StartsWith("spellSlots.", StringComparison.Ordinal);

    /// <param name="batch">Other unsaved revisions validated together, which may reference each other.</param>
    public static ValidationReport Validate(ContentRevision revision, IContentCatalog catalog, IEnumerable<ContentRevision>? batch = null) =>
        Validate(revision, catalog, new ValidationBatch((batch ?? []).ToDictionary(r => r.Reference)));

    /// <summary>
    /// The same, with the batch already keyed by reference and by content id (the debugger validates a whole source
    /// against itself, keying it once). The batch may hold the revision itself: every batch lookup is of other content.
    /// </summary>
    internal static ValidationReport Validate(ContentRevision revision, IContentCatalog catalog, ValidationBatch local)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(local);
        var errors = new List<Diagnostic>();
        var warnings = new List<Diagnostic>();
        var reference = revision.Reference;
        if (EmptyEntries(revision) is { Count: > 0 } empty)
            return new ValidationReport(reference, empty, []); // nothing else can be checked safely
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
        var needsV9 = false;
        var needsV10 = false; // content v10 (Wave 3, D42): speed/passive targets, autoWhen, activation, damage grants, item
        var needsV11 = false; // content v11 (D43): toggleGroup and upkeep effects, the toggle's group, layer, cost, level and duration keys, and the resource's counts, resourceFull and allowance keys
        var needsV12 = false; // content v12 (MECH-X item 0): weapon.bonus
        var upkeeps = 0;
        var responses = 0;
        var diceLessRoll = false; // an untyped roll with no dice: publishing it needs v11, where a roll may omit them
        var usedScales = new HashSet<string>(StringComparer.Ordinal); // SCALE.<id> in any formula (content v9)
        var usedValues = new HashSet<string>(StringComparer.Ordinal); // VALUE.<id> in any formula (content v11)
        var spellTargets = false; // raises the written version only; older revisions with them were never refused
        foreach (var effect in revision.Effects)
        {
            switch (effect)
            {
                // A scale in a revision below v9 is never typed (it stays unknown), so it is named as needing v9 rather
                // than as an effect this version cannot automate (review fix).
                case UnknownEffect { DeclaredType: ScaleEffect.TypeName } unknownScale:
                    needsV9 = true;
                    // From v9 on a scale is typed; one that stays unknown did not match the shape (no label, values that are
                    // not whole numbers), so its column would silently not exist (review fix).
                    if (revision.SchemaVersion >= ScaleEffect.SchemaVersion)
                        Error("validate.scale-incomplete", $"Scale '{unknownScale.Id}' does not match the scale shape: it needs a scaleId, a label and {Character.MaxLevel} whole-number values.", unknownScale.Id);
                    break;
                // From v10 an item effect is typed; one that stays unknown did not match the shape (a max that is not a whole
                // number, a period that is not a rest), so its charges would silently not exist. Below v10 it stays what a v9
                // build made of it: an unsupported effect, a warning (hand-written v9 content is never newly refused).
                case UnknownEffect { DeclaredType: ItemEffect.TypeName } unknownItem when revision.SchemaVersion >= ItemEffect.SchemaVersion:
                    needsV10 = true;
                    Error("validate.item-incomplete", $"Item effect '{unknownItem.Id}' does not match the item shape: charges need a whole-number max and, if present, a recovery with a shortRest or longRest period.", unknownItem.Id);
                    break;
                // From v10 a toggle, resource or grant that stays unknown did not read (an activation, autoWhen or grant kind that
                // is not one of the allowed names). Publishing would write it at a lower version, where it reads back as a
                // different, typed effect, so it blocks. Below v10 it is what it always was: a warning.
                // Content v11 (D43) does the same for a toggle with a layer that names no member and for a toggleGroup that does not
                // read. The version it needs is the field's own tier: a v11 key (or a toggleGroup) needs 11, anything else 10.
                case UnknownEffect { DeclaredType: ToggleEffect.TypeName or ResourceEffect.TypeName or GrantEffect.TypeName or ToggleGroupEffect.TypeName or UpkeepEffect.TypeName or AttackEffect.TypeName or AttackRiderEffect.TypeName } malformed
                    when revision.SchemaVersion >= (malformed.DeclaredType is ToggleGroupEffect.TypeName or UpkeepEffect.TypeName or AttackEffect.TypeName or AttackRiderEffect.TypeName ? ContentRevision.PlayMechanicsSchemaVersion : ContentRevision.PlayDetailsSchemaVersion):
                    if (malformed.DeclaredType is ToggleGroupEffect.TypeName or UpkeepEffect.TypeName or AttackEffect.TypeName or AttackRiderEffect.TypeName
                        || (revision.SchemaVersion >= ContentRevision.PlayMechanicsSchemaVersion
                            && ((malformed.DeclaredType == ToggleEffect.TypeName && CarriesV11Key(malformed, ToggleEffect.V11Keys))
                                || (malformed.DeclaredType == ResourceEffect.TypeName && CarriesV11Key(malformed, ResourceEffect.V11Keys))
                                || (malformed.DeclaredType == GrantEffect.TypeName && CarriesV11Key(malformed, GrantEffect.V11Keys)))))
                        needsV11 = true;
                    else
                        needsV10 = true;
                    Error("validate.effect-malformed", $"Effect '{malformed.Id}' has type '{malformed.DeclaredType}' but a value or field that does not read (a name that is not allowed, such as an activation, autoWhen, layer, kind, ability or grant kind, or a missing field); fix it before publishing.", malformed.Id);
                    break;
                // Content v11 (D43): a modifier or roll whose v11 key does not read (toggleContent that is not an id, a whileToggle
                // without a toggleId). Published at a lower version it would read back as a modifier or roll that ignores the key and
                // applies always, so it blocks like the other malformed effects. Without a v11 key it is what it always was: a warning.
                case UnknownEffect { DeclaredType: ModifierEffect.TypeName or RollEffect.TypeName } unreadable
                    when revision.SchemaVersion >= ContentRevision.PlayMechanicsSchemaVersion
                        && CarriesV11Key(unreadable, unreadable.DeclaredType == ModifierEffect.TypeName ? ModifierEffect.V11Keys : RollEffect.V11Keys):
                    needsV11 = true;
                    Error("validate.effect-malformed", $"Effect '{unreadable.Id}' has type '{unreadable.DeclaredType}' but a value or field that does not read (its toggleContent or whileToggle, or a missing field); fix it before publishing.", unreadable.Id);
                    break;
                // Content v11 (D43): from v11 a value is typed; one that stays unknown did not match the shape (no formula, a valueId
                // that is not a string), so the name would silently not exist.
                case UnknownEffect { DeclaredType: ValueEffect.TypeName } unknownValue when revision.SchemaVersion >= ValueEffect.SchemaVersion:
                    needsV11 = true;
                    Error("validate.value-incomplete", $"Value '{unknownValue.Id}' does not match the value shape: it needs a valueId and a formula.", unknownValue.Id);
                    break;
                // Content v11 (D43): from v11 a rollMode is typed; one that stays unknown did not match the shape (a mode that names no
                // member, no target, a list that is not a list), so the rule would silently not exist. Published at a lower version it
                // would be an unsupported effect, so it blocks.
                case UnknownEffect { DeclaredType: RollModeEffect.TypeName } unknownMode when revision.SchemaVersion >= RollModeEffect.SchemaVersion:
                    needsV11 = true;
                    Error("validate.effect-malformed", $"Effect '{unknownMode.Id}' has type '{unknownMode.DeclaredType}' but a value or field that does not read (its mode must be advantage or disadvantage; it needs a target; against is a list of condition keys; toggleContent is an id); fix it before publishing.", unknownMode.Id);
                    break;
                // Content v11 (D43): from v11 a response is typed; one that stays unknown did not match the shape (a phase or an outcome
                // kind that names no member, no responseId, label or phase, a toggle or content that is not an id), so the response would
                // silently not exist. Published at a lower version it would be an unsupported effect, so it blocks.
                case UnknownEffect { DeclaredType: ResponseEffect.TypeName } unknownResponse when revision.SchemaVersion >= ResponseEffect.SchemaVersion:
                    needsV11 = true;
                    Error("validate.effect-malformed", $"Effect '{unknownResponse.Id}' has type '{unknownResponse.DeclaredType}' but a value or field that does not read (it needs a responseId, a label and a phase of damageReduction, hitPointOutcome or threshold; each outcome needs a kind of spend, regain, toggleOn, offerRoll, regainHitPoints or temporaryHitPoints; a content is an id); fix it before publishing.", unknownResponse.Id);
                    break;
                // Content v12 (MECH-X item 0): from v12 a weapon that carries a bonus key is read with it; one that stays unknown did
                // not read (a bonus that is not a whole number, or another field that does not read), so the bonus would silently do
                // nothing. Published at a lower version the key is stripped and the weapon reads without it, so it blocks. Without a
                // bonus key it is what it always was: a warning. Below v12 the key is stripped first, so nothing changes there.
                case UnknownEffect { DeclaredType: WeaponEffect.TypeName } unreadableWeapon
                    when revision.SchemaVersion >= ContentRevision.WeaponBonusSchemaVersion && CarriesV11Key(unreadableWeapon, WeaponEffect.V12Keys):
                    needsV12 = true;
                    Error("validate.effect-malformed", $"Effect '{unreadableWeapon.Id}' has type 'weapon' but a value or field that does not read (its bonus must be a whole number from 0 to 10; a category, attack, damage, damageType and weaponKey are needed); fix it before publishing.", unreadableWeapon.Id);
                    break;
                case UnknownEffect unknown:
                    // Content v11 (D43): a roll with no dice is typed only from v11, so below it the roll stays reference-only as it
                    // always did, but publishing it needs v11.
                    diceLessRoll |= unknown.DeclaredType == RollEffect.TypeName && RollEffect.LacksDice(unknown.Raw);
                    Warn("validate.effect-unsupported", $"Effect '{unknown.Id}' is of a type this version does not automate; it stays reference-only.", unknown.Id);
                    break;
                case ModifierEffect modifier:
                    needsV3 |= modifier.Target is FieldIds.ArmorClass or FieldIds.HitPoints;
                    needsV8 |= modifier.Target is FieldIds.Attacks or FieldIds.CriticalRange || modifier.WhileArmored is not null;
                    spellTargets |= IsSpellField(modifier.Target);
                    if (modifier.Target.StartsWith(ResourceTargetPrefix, StringComparison.Ordinal))
                        CheckResourceTarget(modifier);
                    else if (!CharacterCalculator.IsField(modifier.Target))
                        Error("validate.unknown-target", $"Effect '{modifier.Id}' targets '{modifier.Target}', which is not a calculated field.", modifier.Id);
                    else if (IsCharacterOnlyField(modifier.Target, revision))
                        Error("validate.unknown-target", $"Effect '{modifier.Id}' targets '{modifier.Target}', which cannot be targeted by content yet (needs content schema v{ContentRevision.PlayDetailsSchemaVersion}).", modifier.Id);
                    else
                        needsV10 |= CharacterCalculator.IsSpeedOrPassive(modifier.Target); // allowed: this revision is v10
                    // Content v11 (D43): fly speed. Below v11 the revision gets validate.requires-v11 (and the calculator ignores it).
                    needsV11 |= modifier.Target == FieldIds.FlySpeed;
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
                        // Content v11 (D43): the toggle may be another content's (toggleContent); the same lookup as a rider's.
                        if (modifier.ToggleContent is null)
                        {
                            if (!revision.Effects.OfType<ToggleEffect>().Any(t => t.ToggleId == toggle))
                                Error("validate.toggle-unknown", $"Modifier '{modifier.Id}' names toggle '{toggle}', which this revision does not define.", modifier.Id);
                        }
                        else
                        {
                            CheckToggleReference(modifier.ToggleContent, toggle, modifier.Id, "validate.toggle-unknown");
                        }
                    }
                    if (modifier.ToggleContent is not null)
                    {
                        needsV11 = true;
                        if (modifier.Toggle is null)
                            Error("validate.toggle-unknown", $"Modifier '{modifier.Id}' names the content that declares its toggle but names no toggle; add the toggle, or remove toggleContent.", modifier.Id);
                    }
                    break;
                case ToggleEffect toggleEffect:
                    needsV6 = true;
                    needsV10 |= toggleEffect.AutoWhen is not null || toggleEffect.Activation is not null;
                    if (string.IsNullOrWhiteSpace(toggleEffect.ToggleId) || string.IsNullOrWhiteSpace(toggleEffect.Label))
                        Error("validate.toggle-incomplete", $"Toggle '{toggleEffect.Id}' needs a toggleId and a label.", toggleEffect.Id);
                    // A v11 toggle may spend a pool another content defines (resourceContent); that is checked below.
                    if (toggleEffect.ResourceId is { } spends && (toggleEffect.ResourceContent is null || toggleEffect.ResourceContent == revision.ContentId)
                        && !revision.Effects.OfType<ResourceEffect>().Any(r => r.ResourceId == spends))
                        Error("validate.toggle-resource", $"Toggle '{toggleEffect.Id}' spends '{spends}', which this revision does not define.", toggleEffect.Id);
                    if (toggleEffect.AutoWhen is not null)
                    {
                        // D38: an auto toggle follows the hit points, so it never spends a use, and it may not change the values
                        // Bloodied is judged by (the hit point maximum, an ability score or modifier), which would feed back.
                        if (toggleEffect.ResourceId is not null)
                            Error("validate.toggle-auto-resource", $"Toggle '{toggleEffect.Id}' switches itself, so it cannot spend a use of a resource.", toggleEffect.Id);
                        foreach (var changes in revision.Effects.OfType<ModifierEffect>().Where(m =>
                            m.Toggle == toggleEffect.ToggleId && m.Timing == EffectTiming.WhileActive
                            && (m.Target == FieldIds.HitPoints || m.Target.StartsWith("ability.", StringComparison.Ordinal))))
                            Error("validate.toggle-auto-target", $"Modifier '{changes.Id}' belongs to toggle '{toggleEffect.ToggleId}', which switches itself; it cannot change hit points or an ability.", changes.Id);
                    }
                    CheckToggleV11(toggleEffect);
                    break;
                case ToggleGroupEffect group:
                    needsV11 = true;
                    if (!ScaleEffect.IsValidScaleId(group.GroupId) || string.IsNullOrWhiteSpace(group.Label))
                        Error("validate.toggle-group-incomplete", $"Toggle group '{group.Id}' needs a label and a groupId of a lowercase letter followed by up to {ScaleEffect.MaxScaleIdLength - 1} letters or digits.", group.Id);
                    break;
                case GrantEffect grant:
                    needsV3 |= grant.Level is not null || grant.Target is FieldIds.ArmorClass or FieldIds.HitPoints;
                    CheckLevel(grant.Level, grant.Id);
                    needsV10 |= GrantEffect.IsDamageDefence(grant.Grant) || grant.DamageType is not null;
                    if (grant.DamageType is not null && !GrantEffect.IsDamageDefence(grant.Grant))
                        Error("validate.grant-damage-type-kind", $"Effect '{grant.Id}' has a damageType, which only a damageResistance, damageImmunity or damageVulnerability grant takes.", grant.Id);
                    if (GrantEffect.IsDamageDefence(grant.Grant))
                    {
                        // Content v10: a resistance, immunity or vulnerability names a damage type and no target or content.
                        if (!DamageTypes.TryNormalize(grant.DamageType, out _))
                            Error("validate.grant-damage-type", $"Effect '{grant.Id}' grants {grant.Grant.ToString().ToLowerInvariant()} and needs a damageType: one of {string.Join(", ", DamageTypes.All)}.", grant.Id);
                        if (grant.Target is not null || grant.Content is not null)
                            Error("validate.grant-damage-fields", $"Effect '{grant.Id}' grants a damage defence, which names a damageType only: it takes no target and no content.", grant.Id);
                    }
                    else if (grant.Grant == GrantKind.Content)
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
                    // Content v11 (D43): a damage defence that applies only while a toggle is on (a form-only resistance). The v11 schema
                    // refuses the key on any other grant kind; here it is checked the way a roll's whileToggle is.
                    if (grant.WhileToggle is { } defenceToggle)
                    {
                        needsV11 = true;
                        CheckToggleReference(defenceToggle.ContentId, defenceToggle.ToggleId, grant.Id, "validate.while-toggle-unknown");
                    }
                    break;
                case ChoiceEffect choice:
                    needsV3 |= choice.Level is not null;
                    CheckLevel(choice.Level, choice.Id);
                    if (string.IsNullOrWhiteSpace(choice.ChoiceId))
                        Error("validate.choice-id-required", $"Choice effect '{choice.Id}' needs a choiceId.", choice.Id);
                    // Content v9 (M5 slice 1b): a choice may declare no options of its own when its options come from
                    // content that extends it (extendsChoice), such as the subclass choice of a new homebrew class. Older
                    // builds refused it, so it needs v9: they refuse it by version instead of with this error.
                    if (choice.Options.Count == 0)
                    {
                        needsV9 = true;
                        Warn("validate.choice-options-none", $"Choice '{choice.ChoiceId}' lists no options of its own; it offers only published content that extends it (for example a subclass that names this choice).", choice.Id);
                    }
                    if (choice.Options.Distinct().Count() != choice.Options.Count)
                        Error("validate.choice-option-duplicate", $"Choice '{choice.ChoiceId}' lists an option more than once.", choice.Id);
                    if (choice.Count < 1
                        || (choice.Options.Count > 0 && choice.Count > choice.Options.Distinct().Count())
                        || (choice.Options.Count == 0 && choice.Count > MaxChoiceCountWithoutOptions))
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
                    else if (IsCharacterOnlyField(restriction.Field, revision))
                        Error("validate.unknown-target", $"Restriction '{restriction.Id}' checks '{restriction.Field}', which cannot be targeted by content yet (needs content schema v{ContentRevision.PlayDetailsSchemaVersion}).", restriction.Id);
                    else
                        needsV10 |= CharacterCalculator.IsSpeedOrPassive(restriction.Field);
                    needsV11 |= restriction.Field == FieldIds.FlySpeed;
                    if (Math.Abs(restriction.Minimum) > FormulaLimits.MaxLiteral)
                        Error("validate.restriction-minimum", $"Restriction '{restriction.Id}' minimum {restriction.Minimum} is outside ±{FormulaLimits.MaxLiteral}.", restriction.Id);
                    break;
                case ItemEffect item:
                    needsV10 = true;
                    if (revision.Kind != ContentKind.Item)
                        Error("validate.item-kind", $"An item effect (attunement, charges) belongs on an item; this revision is {revision.Kind.ToString().ToLowerInvariant()} content.", item.Id);
                    if (item.Charges is { } charges)
                    {
                        if (charges.Max is < 1 or > ItemCharges.MaxCharges)
                            Error("validate.item-charges", $"Item '{item.Id}' has {charges.Max} charges; it must be 1 to {ItemCharges.MaxCharges}.", item.Id);
                        if (charges.Recovery?.Regain is { } regain && !DiceExpression.TryParse(regain, out _, out var regainError))
                            Error("validate.dice-invalid", $"Item '{item.Id}' regain is not a dice expression such as 1d4+1 ({regainError!.Code})", item.Id);
                    }
                    break;
                case ResourceEffect resource:
                    needsV10 |= resource.Activation is not null;
                    needsV11 |= resource.Counts is not null || resource.ResourceFull is not null || resource.Allowance is not null;
                    if (string.IsNullOrWhiteSpace(resource.ResourceId) || string.IsNullOrWhiteSpace(resource.Label))
                        Error("validate.resource-incomplete", $"Resource effect '{resource.Id}' needs a resourceId and a label.", resource.Id);
                    CheckFormula(resource.Maximum, resource.Id, "maximum");
                    if (resource.ResourceFull is not null && resource.Counts != ResourceCounting.Up)
                        Error("validate.resource-full", $"Resource '{resource.ResourceId}' has a resourceFull prompt but does not count up; add counts: up, or remove the prompt.", resource.Id);
                    else if (resource.ResourceFull is not null && string.IsNullOrWhiteSpace(resource.ResourceFull.Text))
                        Error("validate.resource-full", $"Resource '{resource.ResourceId}' has a resourceFull prompt with no text; write the text, or remove the prompt.", resource.Id);
                    break;
                case UpkeepEffect upkeep:
                    needsV11 = true;
                    upkeeps++;
                    CheckUpkeep(upkeep);
                    break;
                case AttackEffect attack:
                    needsV11 = true;
                    CheckAttack(attack);
                    break;
                case AttackRiderEffect rider:
                    needsV11 = true;
                    CheckRider(rider);
                    break;
                case ValueEffect value:
                    needsV11 = true;
                    if (!ScaleEffect.IsValidScaleId(value.ValueId) || string.IsNullOrWhiteSpace(value.Formula))
                        Error("validate.value-incomplete", $"Value '{value.Id}' needs a formula and a valueId of a lowercase letter followed by up to {ScaleEffect.MaxScaleIdLength - 1} letters or digits.", value.Id);
                    if (value.Automation != AutomationStatus.Automatic)
                        Error("validate.value-incomplete", $"Value '{value.Id}' must be automatic; a named value is always calculated.", value.Id);
                    if (!string.IsNullOrWhiteSpace(value.Formula))
                        CheckFormula(value.Formula, value.Id, "formula");
                    break;
                case RollModeEffect rollMode:
                    needsV11 = true;
                    if (!IsRollModeTarget(rollMode.Target))
                        Error("validate.roll-mode-target", $"Roll mode '{rollMode.Id}' targets '{rollMode.Target}'; use save.<ability> (such as save.dex), skill.<skill> (such as skill.stealth) or ability.<ability>.check (such as ability.str.check).", rollMode.Id);
                    if (rollMode.Mode == RollMode.Normal)
                        Error("validate.roll-mode-mode", $"Roll mode '{rollMode.Id}' has mode normal, which changes nothing; use advantage or disadvantage.", rollMode.Id);
                    if (rollMode.Against is { } against)
                    {
                        if (against.Count == 0)
                            Error("validate.roll-mode-against", $"Roll mode '{rollMode.Id}' has an empty against list; name at least one condition, or remove it.", rollMode.Id);
                        foreach (var key in against.Where(k => !ConditionKeys.All.Contains(k)).Distinct())
                            Error("validate.roll-mode-against", $"Roll mode '{rollMode.Id}' is against '{key}', which is not a condition key ({string.Join(", ", ConditionKeys.All)}).", rollMode.Id);
                    }
                    if (rollMode.Toggle is { } modeToggle)
                    {
                        // The same lookup as a modifier's toggle (this revision's, or another content's with toggleContent).
                        if (rollMode.ToggleContent is null)
                        {
                            if (!revision.Effects.OfType<ToggleEffect>().Any(t => t.ToggleId == modeToggle))
                                Error("validate.toggle-unknown", $"Roll mode '{rollMode.Id}' names toggle '{modeToggle}', which this revision does not define.", rollMode.Id);
                        }
                        else
                        {
                            CheckToggleReference(rollMode.ToggleContent, modeToggle, rollMode.Id, "validate.toggle-unknown");
                        }
                    }
                    else if (rollMode.ToggleContent is not null)
                    {
                        Error("validate.toggle-unknown", $"Roll mode '{rollMode.Id}' names the content that declares its toggle but names no toggle; add the toggle, or remove toggleContent.", rollMode.Id);
                    }
                    break;
                case ResponseEffect response:
                    needsV11 = true;
                    responses++;
                    CheckResponse(response);
                    break;
                case RecoveryEffect recovery:
                    if (!string.Equals(recovery.Amount, "all", StringComparison.Ordinal))
                        CheckFormula(recovery.Amount, recovery.Id, "amount");
                    if (!revision.Effects.OfType<ResourceEffect>().Any(r => r.ResourceId == recovery.ResourceId))
                        Warn("validate.recovery-resource", $"Recovery '{recovery.Id}' restores '{recovery.ResourceId}', which this revision does not define (fine if another revision does).", recovery.Id);
                    break;
                case RollEffect roll:
                    // Content v11 (D43): a roll may have no dice at all (an action that only spends a cost); it then needs a cost.
                    if (roll.Dice is null)
                    {
                        needsV11 = true;
                        if (roll.Cost is null)
                            Error("validate.roll-dice-or-cost", $"Roll '{roll.Id}' has neither dice nor a cost, so it does nothing; add dice or a cost.", roll.Id);
                    }
                    else if (!DiceExpression.TryParse(roll.Dice, out _, out var diceError))
                    {
                        Error("validate.dice-invalid", $"Roll '{roll.Id}' dice '{roll.Dice}': {diceError!.Message} ({diceError.Code})", roll.Id);
                    }
                    needsV11 |= roll.Save is not null || roll.Area is not null || roll.Range is not null || roll.Targets is not null || roll.PerUnit is not null
                        || roll.AlsoChange is not null || roll.DrawsDown is not null;
                    CheckRollDetails(roll);
                    needsV5 |= roll.Activation is not null;
                    needsV6 |= roll.ResourceContent is not null || roll.Cost is not null || roll.VariableCost is not null;
                    if (roll.Cost is { } cost)
                        CheckFormula(cost, roll.Id, "cost");
                    if (roll.Bonus is { } bonus)
                    {
                        needsV8 = true;
                        CheckFormula(bonus, roll.Id, "bonus");
                    }
                    if (roll.WhileToggle is { } whileToggle)
                    {
                        needsV11 = true;
                        CheckToggleReference(whileToggle.ContentId, whileToggle.ToggleId, roll.Id, "validate.while-toggle-unknown");
                    }
                    if ((roll.Cost is not null || roll.VariableCost == true) && roll.ResourceId is null)
                        Error("validate.roll-cost", $"Roll '{roll.Id}' has a cost but names no resource to spend.", roll.Id);
                    if (roll.ResourceId is { } rollPool && roll.DrawsDown != true && FindResource(roll.ResourceContent, rollPool)?.Counts == ResourceCounting.Up)
                        Warn("validate.cost-counts-up", $"Roll '{roll.Id}' spends from '{rollPool}', a pool that counts up; a cost fills it instead of using it up.", roll.Id);
                    if (roll.ResourceContent is { } holder && holder != revision.ContentId && roll.ResourceId is { } shared)
                    {
                        var definer = local.OfContent(holder).Concat(catalog.RevisionsOf(holder)).FirstOrDefault(r => r.ContentId == holder);
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
                    if (weapon.Bonus is { } weaponBonus)
                    {
                        needsV12 = true; // present at all (0 too) is a v12 key
                        if (weaponBonus is < 0 or > MaxWeaponBonus)
                            Error("validate.weapon-bonus", $"Weapon '{weapon.Id}' has bonus {weaponBonus}; it must be a whole number from 0 to {MaxWeaponBonus}.", weapon.Id);
                    }
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
                    if ((spellcasting.MulticlassCaster is not null || spellcasting.MulticlassCasterTable is not null) && spellcasting.SlotKind == SpellSlotKind.PactMagic)
                        Error("validate.spellcasting-multiclass-pact", $"Spellcasting '{spellcasting.Id}': Pact Magic slots are never combined with other casters' slots, so it takes no multiclassCaster or multiclassCasterTable.", spellcasting.Id);
                    // Below v9 the table stays extension data (never typed), so it is found there (review fix).
                    if (spellcasting.Extensions?.Keys.Any(k => string.Equals(k, SpellcastingEffect.MulticlassCasterTableProperty, StringComparison.OrdinalIgnoreCase)) == true)
                        needsV9 = true;
                    if (spellcasting.MulticlassCasterTable is { } casterTable)
                    {
                        needsV9 = true;
                        if (spellcasting.MulticlassCaster is not null)
                            Error("validate.spellcasting-multiclass-both", $"Spellcasting '{spellcasting.Id}' has both multiclassCaster and multiclassCasterTable; use one.", spellcasting.Id);
                        if (MulticlassTableProblem(casterTable) is { } tableProblem)
                            Error("validate.spellcasting-multiclass-table", $"Spellcasting '{spellcasting.Id}' multiclassCasterTable: {tableProblem}.", spellcasting.Id);
                    }
                    if (revision.Kind is not (ContentKind.Class or ContentKind.Subclass or ContentKind.Feature))
                        Warn("validate.spellcasting-kind", $"Spellcasting is calculated only inside a class; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content.", spellcasting.Id);
                    break;
                case ScaleEffect scale:
                    needsV9 = true;
                    if (!ScaleEffect.IsValidScaleId(scale.ScaleId) || string.IsNullOrWhiteSpace(scale.Label))
                        Error("validate.scale-incomplete", $"Scale '{scale.Id}' needs a label and a scaleId of a lowercase letter followed by up to {ScaleEffect.MaxScaleIdLength - 1} letters or digits.", scale.Id);
                    if (scale.Values.Count != Character.MaxLevel || scale.Values.Any(v => v is < 0 or > FormulaLimits.MaxLiteral))
                        Error("validate.scale-values", $"Scale '{scale.Id}' needs {Character.MaxLevel} values (class levels 1 to {Character.MaxLevel}), each 0 to {FormulaLimits.MaxLiteral}.", scale.Id);
                    if (revision.Kind is not (ContentKind.Class or ContentKind.Subclass))
                        Error("validate.scale-kind", $"A scale is a column of a class table, so it belongs on a class or subclass; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content.", scale.Id);
                    break;
                case SpellEffect spell:
                    if (spell.Level is < 0 or > SpellcastingEffect.MaxSpellLevel)
                        Error("validate.spell-level", $"Spell '{spell.Id}' level {spell.Level} must be 0 (cantrip) to {SpellcastingEffect.MaxSpellLevel}.", spell.Id);
                    if (spell.Lists.Count == 0 || spell.Lists.Any(string.IsNullOrWhiteSpace))
                        Warn("validate.spell-lists", $"Spell '{spell.Id}' is on no spell list, so no caster can choose it. Add it to a spell list.", spell.Id);
                    if (spell.Dice is { } dice && !DiceExpression.TryParse(dice, out _, out var spellDiceError))
                        Error("validate.dice-invalid", $"Spell '{spell.Id}' dice '{dice}': {spellDiceError!.Message} ({spellDiceError.Code})", spell.Id);
                    if (revision.Kind != ContentKind.Spell)
                        Warn("validate.spell-kind", $"Spell data is used only on spell content; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content.", spell.Id);
                    break;
            }
        }
        foreach (var group in revision.Effects.OfType<ToggleGroupEffect>().GroupBy(g => g.GroupId).Where(g => g.Count() > 1))
            Error("validate.toggle-group-duplicate", $"Toggle group '{group.Key}' is declared {group.Count()} times; declare each group once.", group.Skip(1).First().Id);
        foreach (var group in revision.Effects.OfType<ValueEffect>().GroupBy(v => v.ValueId).Where(g => g.Count() > 1))
            Error("validate.value-duplicate", $"Value id '{group.Key}' is declared {group.Count()} times in this revision; declare each value once.", group.Skip(1).First().Id);
        foreach (var group in revision.Effects.OfType<ResponseEffect>().GroupBy(r => r.ResponseId).Where(g => g.Count() > 1))
            Error("validate.response-duplicate", $"Response id '{group.Key}' is declared {group.Count()} times in this revision; declare each response once.", group.Skip(1).First().Id);
        if (responses > MaxResponsesPerRevision)
            Error("validate.response-too-many", $"This revision declares {responses} response effects; the most is {MaxResponsesPerRevision}.");
        if (upkeeps > MaxUpkeepsPerRevision)
            Error("validate.upkeep-too-many", $"This revision declares {upkeeps} upkeep effects; the most is {MaxUpkeepsPerRevision}.");
        foreach (var allowance in revision.Effects.OfType<ResourceEffect>().Where(r => r.Allowance == true))
        {
            if (!revision.Effects.OfType<UpkeepEffect>().Any(u => u.Change == UpkeepChange.Regain && u.ResourceId == allowance.ResourceId && (u.ResourceContent is null || u.ResourceContent == revision.ContentId)))
                Warn("validate.allowance-no-refresh", $"Resource '{allowance.ResourceId}' is an allowance but no upkeep regains it, so nothing refreshes it each turn.", allowance.Id);
        }
        if (revision.Effects.OfType<SpellcastingEffect>().Count() > 1)
            Error("validate.spellcasting-duplicate", "A revision declares at most one spellcasting feature.");
        if (revision.Effects.OfType<SpellEffect>().Count() > 1)
            Error("validate.spell-duplicate", "A spell revision declares its spell data once.");
        if (revision.Effects.OfType<ItemEffect>().Count() > 1)
            Error("validate.item-duplicate", "An item declares its attunement and charges once.");
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
        // Typed v10 details exist only in a v10 revision when read from JSON (below v10 the keys stay extension data); a
        // revision built in code can still declare an older version, and an older build would ignore or refuse them.
        needsV11 |= usedValues.Count > 0;
        if (needsV12 && revision.SchemaVersion < ContentRevision.WeaponBonusSchemaVersion)
            Error("validate.requires-v12", $"This revision uses content schema v12 features (a weapon bonus) but declares v{revision.SchemaVersion}; an older build would ignore or refuse them.");
        if (needsV11 && revision.SchemaVersion < ContentRevision.PlayMechanicsSchemaVersion)
            Error("validate.requires-v11", $"This revision uses content schema v11 features (toggle groups, toggle layers, costs, levels or durations, upkeeps, counting-up pools or allowances, attacks, attack riders, a toggle of another content or a resource target, roll saves, areas, ranges, targets or per-unit dice, a roll with no dice, named values or VALUE in a formula, roll modes, damage responses, fly speed, form-only defences) but declares v{revision.SchemaVersion}; an older build would ignore, misread or refuse them.");
        if (needsV10 && revision.SchemaVersion < ContentRevision.PlayDetailsSchemaVersion)
            Error("validate.requires-v10", $"This revision uses content schema v10 features (passive or speed targets, autoWhen, activation, damage defence grants, an item effect) but declares v{revision.SchemaVersion}; an older build would ignore, misread or refuse them.");
        needsV9 |= usedScales.Count > 0;
        if (needsV9 && revision.SchemaVersion < ScaleEffect.SchemaVersion)
            Error("validate.requires-v9", $"This revision uses content schema v9 features (scales, SCALE in a formula, multiclassCasterTable, a choice with no options of its own) but declares v{revision.SchemaVersion}; an older build would ignore, misread or refuse them.");
        CheckScales();
        CheckValues();
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
            var targets = catalog.RevisionsOf(extends.ContentId).Concat(local.OfContent(extends.ContentId)).ToList();
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
            needsV12 ? ContentRevision.WeaponBonusSchemaVersion
            : needsV11 || diceLessRoll ? ContentRevision.PlayMechanicsSchemaVersion
            : needsV10 ? ContentRevision.PlayDetailsSchemaVersion
            : needsV9 ? ScaleEffect.SchemaVersion
            : needsV8 ? ContentRevision.CombatDetailsSchemaVersion
            : revision.Effects.OfType<SpellcastingEffect>().Any(s => s.MulticlassCaster is not null) ? SpellcastingEffect.MulticlassSchemaVersion
            : needsV6 ? ToggleEffect.SchemaVersion
            : needsV5 || spellTargets || revision.Effects.Any(e => e is SpellcastingEffect or SpellEffect or WeaponEffect) ? SpellcastingEffect.SchemaVersion
            : revision.ExtendsChoice is not null || revision.Effects.OfType<ArmorEffect>().Any() ? ArmorEffect.SchemaVersion
            : MinimumPublishedSchemaVersion;
        return new ValidationReport(reference, errors, warnings) { RequiredSchemaVersion = Math.Max(required, MinimumPublishedSchemaVersion) };

        // Parsed with scales allowed, so a SCALE in a revision that declares an older version is named as needing v9
        // (validate.requires-v9) rather than as an unknown value. Every formula field goes through here: modifier value,
        // resource maximum, recovery amount, roll cost and bonus, and spellsFormula (ADR-010).
        // Likewise VALUE (content v11) is parsed as known, so a VALUE in an older revision is named as needing v11.
        void CheckFormula(string source, string effectId, string what, string code = "validate.formula-invalid", FormulaFeatures features = AllFormulaFeatures)
        {
            if (!Formula.TryParse(source, features, out var formula, out var error))
                Error(code, $"Effect '{effectId}' {what} '{source}': {error!.Message} ({error.Code})", effectId);
            else
                NoteUsed(formula!);
        }

        void NoteUsed(Formula formula)
        {
            usedScales.UnionWith(formula.Identifiers.Where(FormulaIdentifiers.IsScale).Select(FormulaIdentifiers.ScaleId));
            usedValues.UnionWith(formula.Identifiers.Where(FormulaIdentifiers.IsValue).Select(FormulaIdentifiers.ValueId));
        }

        // Content v11 (D43): a roll's save, area, range, targets and per-unit dice.
        void CheckRollDetails(RollEffect roll)
        {
            var id = roll.Id;
            if (roll.Save is { } save)
                CheckFormula(save.Dc ?? "", id, "save dc", "validate.roll-save");
            if (roll.Area is { } area)
            {
                if (area.Size is < 1 or > MaxAreaFeet)
                    Error("validate.roll-area", $"Roll '{id}' area size {area.Size} must be 1 to {MaxAreaFeet} feet.", id);
                if (area.Width is { } width && (area.Shape != AreaShape.Line || width is < 1 or > MaxAreaFeet))
                    Error("validate.roll-area", $"Roll '{id}' area width {width} applies only to a line, and must be 1 to {MaxAreaFeet} feet.", id);
            }
            if (roll.Range is { Length: > RollEffect.MaxTextLength })
                Error("validate.roll-text-too-long", $"Roll '{id}' range is {roll.Range.Length} characters; the limit is {RollEffect.MaxTextLength}.", id);
            if (roll.Targets is { Length: > RollEffect.MaxTextLength })
                Error("validate.roll-text-too-long", $"Roll '{id}' targets is {roll.Targets.Length} characters; the limit is {RollEffect.MaxTextLength}.", id);
            if (roll.PerUnit is { } perUnit)
            {
                if (!AttackRiderEffect.TryParseDice(perUnit.Dice, out _, out _))
                    Error("validate.roll-per-unit", $"Roll '{id}' per-unit dice '{perUnit.Dice}' must be a count and a die such as 1d6 (1 to {DiceLimits.MaxDice} dice of d4, d6, d8, d10 or d12).", id);
                if (roll.Cost is null || roll.ResourceId is null)
                    Error("validate.roll-per-unit", $"Roll '{id}' has per-unit dice but no cost and resource to count units of; add both.", id);
                if (perUnit.MaxUnits is < 1 or > MaxPerUnitUnits)
                    Error("validate.roll-per-unit", $"Roll '{id}' maxUnits {perUnit.MaxUnits} must be 1 to {MaxPerUnitUnits}.", id);
                // The most dice one roll can throw: the base dice plus the per-unit dice for every unit. Only when both are known.
                if (roll.Dice is null)
                    Error("validate.roll-per-unit", $"Roll '{id}' has per-unit dice but no dice to add them to.", id);
                var unitsKnown = perUnit.MaxUnits is null or (>= 1 and <= MaxPerUnitUnits);
                var tooManyDice = false;
                if (perUnit.MaxUnits is >= 1 and <= MaxPerUnitUnits && roll.Dice is not null && DiceExpression.TryParse(roll.Dice, out var baseDice, out _)
                    && AttackRiderEffect.TryParseDice(perUnit.Dice, out var unitCount, out _)
                    && baseDice!.Terms.Where(t => t.IsDice).Sum(t => t.Count) + (long)unitCount * perUnit.MaxUnits > DiceLimits.MaxDice)
                {
                    tooManyDice = true;
                    Error("validate.roll-per-unit", $"Roll '{id}' could throw more than {DiceLimits.MaxDice} dice with {perUnit.MaxUnits} units; lower maxUnits or the dice.", id);
                }
                // The formula the roll would use at its most units must itself be rollable (term count and length), not only the dice count.
                if (!tooManyDice && unitsKnown && roll.Dice is not null && DiceExpression.TryParse(roll.Dice, out _, out _)
                    && AttackRiderEffect.TryParseDice(perUnit.Dice, out _, out _)
                    && !(DiceRoller.TryPerUnitFormula(roll.Dice, perUnit.Dice, 0, perUnit.MaxUnits ?? 1, out var widest) && DiceExpression.TryParse(widest, out _, out _)))
                    Error("validate.roll-per-unit", $"Roll '{id}' cannot add its per-unit dice: too many terms or too long.", id);
                if (roll.VariableCost == true)
                    Error("validate.roll-per-unit", $"Roll '{id}' has both per-unit dice and a variable cost; they contradict each other. Use one.", id);
            }
            if (roll.AlsoChange is { } alsoChange)
            {
                if (alsoChange.Count > MaxAlsoChanges)
                    Error("validate.also-change", $"Roll '{id}' changes {alsoChange.Count} other pools; the limit is {MaxAlsoChanges}.", id);
                var rollHolder = roll.ResourceContent ?? revision.ContentId;
                if (alsoChange.Count > 0 && string.IsNullOrWhiteSpace(roll.ResourceId))
                    Error("validate.also-change", $"Roll '{id}' changes {alsoChange.Count} other pools but has no pool of its own to spend; Play uses the roll through that pool. Add a resource and cost, or remove alsoChange.", id);
                if (alsoChange.Count > 0 && roll.VariableCost == true)
                    Error("validate.also-change", $"Roll '{id}' changes {alsoChange.Count} other pools and has a variable cost; a compound use charges the calculated cost, so the chosen amount would be ignored. Remove one.", id);
                for (var i = 0; i < alsoChange.Count; i++)
                {
                    if (alsoChange[i] is not { } entry)
                        continue; // a null item is reported once, as validate.empty-entry
                    var number = i + 1;
                    if (string.IsNullOrWhiteSpace(entry.ResourceId))
                    {
                        Error("validate.also-change", $"Roll '{id}' also-change entry {number} names no pool.", id);
                    }
                    else
                    {
                        var holder = entry.ResourceContent ?? revision.ContentId;
                        if (FindResource(entry.ResourceContent, entry.ResourceId) is null)
                            Error("validate.also-change", $"Roll '{id}' also-change entry {number} changes '{entry.ResourceId}', which content {holder} does not define or is not installed.", id);
                        if (roll.ResourceId is { } ownPool && holder == rollHolder && string.Equals(entry.ResourceId, ownPool, StringComparison.Ordinal))
                            Error("validate.also-change", $"Roll '{id}' also-change entry {number} names the roll's own pool '{ownPool}'; its cost already changes that.", id);
                    }
                    CheckFormula(entry.Amount ?? "", id, $"also-change entry {number} amount", "validate.also-change");
                }
            }
            if (roll.DrawsDown == true && !(roll.ResourceId is { } drawn && FindResource(roll.ResourceContent, drawn)?.Counts == ResourceCounting.Up))
                Error("validate.draws-down", $"Roll '{id}' draws down a pool but its resource is not a pool that counts up; name one, or remove drawsDown.", id);
            if (roll.DrawsDown == true && roll.VariableCost == true)
                Error("validate.draws-down", $"Roll '{id}' draws down a pool and has a variable cost; a draw-down charges the calculated cost, so the chosen amount would be ignored. Remove one.", id);
        }

        // Content v11 (D43): a VALUE.<id> the revision reads that nothing it can see declares is a warning, not an error: whether
        // it resolves depends on the class a character has (the calculator guards that). Seen: this revision, its class (a subclass),
        // and the revisions this revision grants or offers as a choice (the catalog cannot be listed whole).
        void CheckValues()
        {
            // The same scope as validate.scale-unknown: only a class, or a subclass, knows what its class declares; any other
            // content is guarded by the calculator.
            if (usedValues.Count == 0 || revision.Kind is not (ContentKind.Class or ContentKind.Subclass))
                return;
            var seen = new List<ContentRevision> { revision };
            if (revision.Kind == ContentKind.Subclass && revision.ExtendsChoice is { } parent)
            {
                seen.AddRange(local.OfContent(parent.ContentId).Where(r => r.Kind == ContentKind.Class));
                seen.AddRange(catalog.RevisionsOf(parent.ContentId).Where(r => r.Status == RevisionStatus.Published && r.Kind == ContentKind.Class).TakeLast(1));
            }
            var references = revision.Effects.OfType<GrantEffect>().Select(g => g.Content).OfType<ContentReference>()
                .Concat(revision.Effects.OfType<ChoiceEffect>().SelectMany(c => c.Options));
            // The revision a grant or option pins; only when that one is not found, the newest published revision of the content.
            foreach (var reference in references)
            {
                var pinned = local.GetValueOrDefault(reference) ?? catalog.FindRevision(reference);
                if (pinned is not null)
                    seen.Add(pinned);
                else
                    seen.AddRange(local.OfContent(reference.ContentId).Concat(catalog.RevisionsOf(reference.ContentId).Where(r => r.Status == RevisionStatus.Published).TakeLast(1)));
            }
            var declared = seen.SelectMany(r => r.Effects.OfType<ValueEffect>().Select(v => v.ValueId)).ToHashSet(StringComparer.Ordinal);
            foreach (var missing in usedValues.Where(u => !declared.Contains(u)).Order(StringComparer.Ordinal))
                Warn("validate.value-unknown", $"A formula reads VALUE.{missing}, which this revision and the content it grants or offers do not declare; it is unavailable until a feature of the character's class does.");
        }

        // Scale ids are unique within a class and its subclasses. The catalog check is best-effort (a class revision
        // published later can still collide); the calculation reports scale.duplicate as the backstop. A subclass's
        // class is known only when it extends a choice of a class (not of a feature, and not as a declared option).
        void CheckScales()
        {
            var own = revision.Effects.OfType<ScaleEffect>().ToList();
            foreach (var group in own.GroupBy(s => s.ScaleId).Where(g => g.Count() > 1))
                Error("validate.scale-duplicate", $"Scale id '{group.Key}' is used more than once.", group.First().Id);
            var related = new List<ContentRevision>();
            // A subclass is checked against the class whose choice it extends: its newest published revision, which new
            // characters get, plus unsaved revisions validated with it. A draft or superseded class revision never blocks
            // it, and a choice on a feature rather than a class says nothing about the class (review fix).
            var publishedParents = revision.Kind == ContentKind.Subclass && revision.ExtendsChoice is { } parent
                ? catalog.RevisionsOf(parent.ContentId).Where(r => r.Status == RevisionStatus.Published && r.Kind == ContentKind.Class).ToList()
                : [];
            var parentClass = revision.Kind == ContentKind.Subclass && revision.ExtendsChoice is { } extended
                ? [.. local.OfContent(extended.ContentId).Where(r => r.Kind == ContentKind.Class), .. publishedParents.TakeLast(1)]
                : new List<ContentRevision>();
            related.AddRange(parentClass);
            // Older published class revisions still calculate for the characters that pin them, so a clash with one is a
            // warning (they get scale.duplicate, and the class's column wins); it never blocks the subclass for good.
            var olderScales = publishedParents.SkipLast(1).SelectMany(r => r.Effects.OfType<ScaleEffect>().Select(s => (s.ScaleId, r.Name))).ToList();
            if (revision.Kind == ContentKind.Class)
            {
                foreach (var choice in revision.Effects.OfType<ChoiceEffect>())
                {
                    related.AddRange(catalog.ChoiceExtensions(revision.ContentId, choice.ChoiceId).Where(r => r.Kind == ContentKind.Subclass));
                    related.AddRange(choice.Options.Select(o => local.GetValueOrDefault(o) ?? catalog.FindRevision(o)).OfType<ContentRevision>().Where(r => r.Kind == ContentKind.Subclass));
                }
            }
            var relatedScales = related.Where(r => r.ContentId != revision.ContentId).SelectMany(r => r.Effects.OfType<ScaleEffect>().Select(s => (s.ScaleId, r.Name))).ToList();
            foreach (var scale in own)
            {
                if (relatedScales.FirstOrDefault(r => r.ScaleId == scale.ScaleId) is { Name: { } other })
                    Error("validate.scale-duplicate", $"Scale id '{scale.ScaleId}' is also defined by '{other}'; a class and its subclasses share one set of scale ids.", scale.Id);
                else if (olderScales.FirstOrDefault(r => r.ScaleId == scale.ScaleId) is { Name: { } older })
                    Warn("validate.scale-duplicate-older", $"Scale id '{scale.ScaleId}' is also defined by an older revision of '{older}'; characters still on that revision use the class's column.", scale.Id);
            }
            // SCALE ids the revision reads that neither it nor its class defines. Only a class, or a subclass that names its
            // class (extendsChoice), knows its class here; a feature's class, or that of a subclass a class lists as an
            // option, is known only when a character has it.
            if (revision.Kind == ContentKind.Class || parentClass.Count > 0)
            {
                // A subclass reads its own and its class's scales; a class reads only its own (a subclass may be absent).
                var defined = own.Select(s => s.ScaleId).Concat(revision.Kind == ContentKind.Subclass ? relatedScales.Select(r => r.ScaleId) : []).ToHashSet(StringComparer.Ordinal);
                foreach (var missing in usedScales.Where(u => !defined.Contains(u)).Order(StringComparer.Ordinal))
                    Warn("validate.scale-unknown", $"A formula reads SCALE.{missing}, which neither this revision nor its class defines; it is unavailable until one does.");
            }
        }

        // The resource a pool reference names: this revision's (content null or this one) or another content's, found among the
        // revisions validated together and the catalog. Null when it is not found (checked by the caller where it matters).
        ResourceEffect? FindResource(Guid? content, string resourceId)
        {
            var holder = content ?? revision.ContentId;
            // Drafts first (the ones validated together, then any in the catalog, the studio sandbox's included), then the newest published
            // revision: an older one must not decide whether a pool counts up for a roll that draws it down.
            var definers = holder == revision.ContentId
                ? new List<ContentRevision> { revision }
                : [.. local.OfContent(holder).Where(r => r.ContentId == holder),
                    .. catalog.RevisionsOf(holder).Where(r => r.ContentId == holder && r.Status != RevisionStatus.Published),
                    .. catalog.RevisionsOf(holder).Where(r => r.ContentId == holder && r.Status == RevisionStatus.Published).TakeLast(1)];
            return definers.SelectMany(r => r.Effects.OfType<ResourceEffect>()).FirstOrDefault(r => r.ResourceId == resourceId);
        }

        // Content v11 (D43): a toggle named by another entry (or by this revision when no content is named). Content that is not
        // installed is a warning (it may arrive later); installed content that declares no such toggle is an error with the
        // referring effect's own code.
        void CheckToggleReference(Guid? content, string? toggleId, string effectId, string errorCode)
        {
            // A reference with no toggle id names nothing, whether or not its content is installed.
            if (string.IsNullOrWhiteSpace(toggleId))
            {
                Error(errorCode, $"Effect '{effectId}' follows a toggle but names no toggle id.", effectId);
                return;
            }
            var holder = content ?? revision.ContentId;
            var declarers = holder == revision.ContentId
                ? new List<ContentRevision> { revision }
                : local.OfContent(holder).Concat(catalog.RevisionsOf(holder)).Where(r => r.ContentId == holder).ToList();
            if (declarers.Count == 0)
                Warn("validate.toggle-content-missing", $"Effect '{effectId}' follows toggle '{toggleId}' of content {holder}, which is not installed; it is unchecked until that content is.", effectId);
            else if (!declarers.SelectMany(r => r.Effects.OfType<ToggleEffect>()).Any(t => t.ToggleId == toggleId))
                Error(errorCode, $"Effect '{effectId}' follows toggle '{toggleId}', which content {holder} does not define.", effectId);
        }

        // Content v11 (D43): a damage-time response. Messages name ids and counts, never the label. A reference with no content
        // names this revision's; a pool or roll in content that is not installed is an error (unlike a toggle, which warns).
        void CheckResponse(ResponseEffect response)
        {
            var id = response.Id;
            var rid = response.ResponseId;
            if (string.IsNullOrWhiteSpace(response.ResponseId) || string.IsNullOrWhiteSpace(response.Label))
                Error("validate.response-incomplete", $"Response '{id}' needs a responseId and a label.", id);
            if (response.Cost is not null && string.IsNullOrWhiteSpace(response.ResourceId))
                Error("validate.response-incomplete", $"Response '{rid}' has a cost but names no resourceId to spend it from.", id);
            if (!string.IsNullOrWhiteSpace(response.ResourceId) && FindResource(response.ResourceContent, response.ResourceId) is null)
                Error("validate.response-incomplete", $"Response '{rid}' spends pool '{response.ResourceId}', which content {response.ResourceContent ?? revision.ContentId} does not define.", id);
            if (response.Cost is { } cost)
                CheckFormula(cost, id, "cost");

            if (response.Phase == ResponsePhase.Threshold)
            {
                if (response.Amount is not null)
                    Error("validate.response-amount", $"Response '{rid}' is a threshold and takes no amount; its outcomes carry the amounts.", id);
                var when = response.When;
                if (when is null)
                    Error("validate.response-when", $"Response '{rid}' is a threshold and needs a when (atOrBelow or survived).", id);
                else
                {
                    if ((when.AtOrBelow is null) == (when.Survived is null))
                        Error("validate.response-when", $"Response '{rid}' when needs exactly one of atOrBelow and survived.", id);
                    if (when.Survived == false)
                        Error("validate.response-when", $"Response '{rid}' when.survived can only be true.", id);
                    if (when.AtOrBelow is { } atOrBelow)
                        CheckFormula(atOrBelow, id, "when.atOrBelow", features: AllFormulaFeatures | FormulaFeatures.HitPointMaximum);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(response.Amount))
                    Error("validate.response-amount", $"Response '{rid}' ({response.Phase.ToString().ToLowerInvariant()}) needs an amount.", id);
                else
                    CheckFormula(response.Amount, id, "amount");
                if (response.When is not null)
                    Error("validate.response-when", $"Response '{rid}' has a when, which only a threshold response takes.", id);
            }

            if (response.Toggle is { } toggle)
                CheckToggleReference(toggle.ContentId, toggle.ToggleId, id, "validate.while-toggle-unknown");

            var outcomes = response.Outcomes ?? [];
            if (outcomes.Count > MaxOutcomesPerResponse)
                Error("validate.response-too-many", $"Response '{rid}' declares {outcomes.Count} outcomes; the most is {MaxOutcomesPerResponse}.", id);
            var hitPointOutcomes = 0;
            foreach (var outcome in outcomes)
            {
                switch (outcome.Kind)
                {
                    case ResponseOutcomeKind.Spend:
                    case ResponseOutcomeKind.Regain:
                        if (string.IsNullOrWhiteSpace(outcome.ResourceId) || string.IsNullOrWhiteSpace(outcome.Amount))
                            Error("validate.response-outcome", $"Response '{rid}' outcome {outcome.Kind.ToString().ToLowerInvariant()} needs a resourceId and an amount.", id);
                        else
                        {
                            if (FindResource(outcome.ResourceContent, outcome.ResourceId) is null)
                                Error("validate.response-outcome", $"Response '{rid}' outcome names pool '{outcome.ResourceId}', which content {outcome.ResourceContent ?? revision.ContentId} does not define.", id);
                            CheckFormula(outcome.Amount, id, "outcome amount");
                        }
                        break;
                    case ResponseOutcomeKind.ToggleOn:
                        if (outcome.Toggle is null)
                            Error("validate.response-outcome", $"Response '{rid}' outcome toggleon needs a toggle.", id);
                        else
                            CheckToggleReference(outcome.Toggle.ContentId, outcome.Toggle.ToggleId, id, "validate.response-outcome");
                        break;
                    case ResponseOutcomeKind.OfferRoll:
                        if (string.IsNullOrWhiteSpace(outcome.RollId))
                            Error("validate.response-outcome", $"Response '{rid}' outcome offerroll needs a rollId.", id);
                        else
                        {
                            var holder = outcome.RollContent ?? revision.ContentId;
                            var declarers = holder == revision.ContentId
                                ? new List<ContentRevision> { revision }
                                : local.OfContent(holder).Concat(catalog.RevisionsOf(holder)).Where(r => r.ContentId == holder).ToList();
                            if (!declarers.SelectMany(r => r.Effects.OfType<RollEffect>()).Any(r => r.RollId == outcome.RollId))
                                Error("validate.response-outcome", $"Response '{rid}' offers roll '{outcome.RollId}', which content {holder} does not define.", id);
                        }
                        break;
                    default:
                        hitPointOutcomes++;
                        if (string.IsNullOrWhiteSpace(outcome.Amount))
                            Error("validate.response-outcome-amount", $"Response '{rid}' outcome {outcome.Kind.ToString().ToLowerInvariant()} needs an amount.", id);
                        else
                            CheckFormula(outcome.Amount, id, "outcome amount", "validate.response-outcome-amount");
                        break;
                }
            }
            if (hitPointOutcomes > 1)
                Error("validate.response-outcome", $"Response '{rid}' has {hitPointOutcomes} hit point outcomes; a response changes hit points once.", id);
            if (hitPointOutcomes > 0 && response.Phase == ResponsePhase.HitPointOutcome)
                Error("validate.response-outcome", $"Response '{rid}' sets the hit points it leaves, so it cannot also regain hit points or give temporary hit points.", id);
        }

        // Content v11 (D43): a modifier on resource.<id> adds to the maximum of a pool this revision declares, as a bonus only.
        void CheckResourceTarget(ModifierEffect modifier)
        {
            if (revision.SchemaVersion < ContentRevision.PlayMechanicsSchemaVersion)
            {
                Error("validate.unknown-target", $"Effect '{modifier.Id}' targets '{modifier.Target}', which cannot be targeted by content yet (needs content schema v{ContentRevision.PlayMechanicsSchemaVersion}).", modifier.Id);
                return;
            }
            needsV11 = true;
            var resourceId = modifier.Target[ResourceTargetPrefix.Length..];
            if (!revision.Effects.OfType<ResourceEffect>().Any(r => r.ResourceId == resourceId))
                Error("validate.resource-target", $"Effect '{modifier.Id}' targets resource '{resourceId}', which this revision does not declare; a resource target names a resource of the same revision.", modifier.Id);
            if (modifier.Operation != ModifierOperation.Bonus)
                Error("validate.resource-target-operation", $"Effect '{modifier.Id}' uses {modifier.Operation.ToString().ToLowerInvariant()} on a resource; only a bonus can raise a pool's maximum.", modifier.Id);
        }

        // Content v11 (D43): attack and rider tags are keys like a scale id; "all" (a rider's) matches every attack.
        void CheckTags(IReadOnlyList<string>? tags, string effectId)
        {
            foreach (var tag in (tags ?? []).Where(t => t != AttackRiderEffect.AllTag && !ScaleEffect.IsValidScaleId(t)))
                Error("validate.tag-invalid", $"Effect '{effectId}' has the tag '{tag}'; a tag is a lowercase letter followed by up to {ScaleEffect.MaxScaleIdLength - 1} letters or digits (or all).", effectId);
        }

        // Content v11 (D43): a feature attack's shape, damage, pool and toggle.
        void CheckAttack(AttackEffect attack)
        {
            var id = attack.Id;
            var (count, die) = (attack.Damage?.Count, attack.Damage?.Die);
            if (!ScaleEffect.IsValidScaleId(attack.AttackId) || string.IsNullOrWhiteSpace(attack.Label) || string.IsNullOrWhiteSpace(attack.DamageType)
                || string.IsNullOrWhiteSpace(count) || string.IsNullOrWhiteSpace(die))
                Error("validate.attack-incomplete", $"Attack '{id}' needs an attackId of a lowercase letter followed by up to {ScaleEffect.MaxScaleIdLength - 1} letters or digits, a label, damage dice (a count and a die) and a damageType.", id);
            if (attack.Abilities.Count == 0)
                Error("validate.attack-abilities", $"Attack '{id}' lists no ability; name at least one of str, dex, con, int, wis or cha.", id);
            if (!string.IsNullOrWhiteSpace(count))
                CheckFormula(count, id, "damage count", "validate.attack-damage");
            if (!string.IsNullOrWhiteSpace(die))
                CheckFormula(die, id, "damage die", "validate.attack-damage");
            if (attack.DamageBonus is { } damageBonus)
                CheckFormula(damageBonus, id, "damageBonus", "validate.attack-damage");
            if (!string.IsNullOrWhiteSpace(attack.DamageType) && !DamageTypes.TryNormalize(attack.DamageType, out _))
                Error("validate.grant-damage-type", $"Attack '{id}' has the damageType '{attack.DamageType}'; use one of {string.Join(", ", DamageTypes.All)}.", id);
            CheckTags(attack.Tags, id);
            if (attack.Cost is { } cost)
                CheckFormula(cost, id, "cost");
            if (attack.Cost is not null && attack.ResourceId is null)
                Error("validate.attack-resource", $"Attack '{id}' has a cost but names no resource to spend.", id);
            if (attack.ResourceId is { } pool)
            {
                // Content that is not installed is an error here, unlike a roll's: an attack is calculated, so a pool nobody declares is a typo.
                var holder = attack.ResourceContent ?? revision.ContentId;
                var definers = holder == revision.ContentId
                    ? new List<ContentRevision> { revision }
                    : local.OfContent(holder).Concat(catalog.RevisionsOf(holder)).Where(r => r.ContentId == holder).ToList();
                if (!definers.Any(d => d.Effects.OfType<ResourceEffect>().Any(r => r.ResourceId == pool)))
                    Error("validate.attack-resource", $"Attack '{id}' spends '{pool}', which content {holder} does not define or is not installed.", id);
            }
            if (attack.WhileToggle is { } whileToggle)
                CheckToggleReference(whileToggle.ContentId, whileToggle.ToggleId, id, "validate.while-toggle-unknown");
        }

        // Content v11 (D43): a rider's shape, dice, formulas, damage type and toggle.
        void CheckRider(AttackRiderEffect rider)
        {
            var id = rider.Id;
            if (rider.Tags.Count == 0 || (string.IsNullOrWhiteSpace(rider.ToHit) && string.IsNullOrWhiteSpace(rider.Dice) && string.IsNullOrWhiteSpace(rider.DamageBonus)))
                Error("validate.rider-incomplete", $"Rider '{id}' needs at least one tag and at least one of toHit, dice or damageBonus.", id);
            CheckTags(rider.Tags, id);
            if (rider.Dice is { } dice && !AttackRiderEffect.TryParseDice(dice, out _, out _))
                Error("validate.rider-dice", $"Rider '{id}' dice '{dice}' must be a count and a die such as 3d8 (1 to {DiceLimits.MaxDice} dice of d4, d6, d8, d10 or d12).", id);
            if (rider.ToHit is { } toHit)
                CheckFormula(toHit, id, "toHit");
            if (rider.DamageBonus is { } damageBonus)
                CheckFormula(damageBonus, id, "damageBonus");
            if (rider.DamageType is { } damageType && !DamageTypes.TryNormalize(damageType, out _))
                Error("validate.grant-damage-type", $"Rider '{id}' has the damageType '{damageType}'; use one of {string.Join(", ", DamageTypes.All)}.", id);
            if (rider.Toggle is null)
            {
                if (rider.ToggleContent is not null)
                    Error("validate.rider-toggle-unknown", $"Rider '{id}' names the content that declares its toggle but names no toggle; add the toggle, or remove toggleContent.", id);
            }
            else
            {
                CheckToggleReference(rider.ToggleContent, rider.Toggle, id, "validate.rider-toggle-unknown");
            }
        }

        // Content v11 (D43): an upkeep's shape, its toggle and pool, and its amount.
        void CheckUpkeep(UpkeepEffect upkeep)
        {
            var id = upkeep.Id;
            if (string.IsNullOrWhiteSpace(upkeep.ResourceId) || string.IsNullOrWhiteSpace(upkeep.Amount))
                Error("validate.upkeep-incomplete", $"Upkeep '{id}' needs a resourceId and an amount.", id);
            if (upkeep.Toggle is null)
            {
                if (upkeep.ToggleContent is not null)
                    Error("validate.upkeep-toggle-unknown", $"Upkeep '{id}' names the content that declares its toggle but names no toggle; add the toggle, or remove toggleContent.", id);
            }
            else
            {
                var holder = upkeep.ToggleContent ?? revision.ContentId;
                var declarers = holder == revision.ContentId
                    ? new List<ContentRevision> { revision }
                    : local.OfContent(holder).Concat(catalog.RevisionsOf(holder)).Where(r => r.ContentId == holder).ToList();
                if (!declarers.SelectMany(r => r.Effects.OfType<ToggleEffect>()).Any(t => t.ToggleId == upkeep.Toggle))
                    Error("validate.upkeep-toggle-unknown", $"Upkeep '{id}' belongs to toggle '{upkeep.Toggle}', which content {holder} does not define or is not installed.", id);
            }
            if (FindResource(upkeep.ResourceContent, upkeep.ResourceId) is null)
                Error("validate.upkeep-resource", $"Upkeep '{id}' changes '{upkeep.ResourceId}', which content {upkeep.ResourceContent ?? revision.ContentId} does not define or is not installed.", id);
            if (string.Equals(upkeep.Amount?.Trim(), "all", StringComparison.Ordinal))
            {
                if (upkeep.Change == UpkeepChange.Spend)
                    Error("validate.upkeep-amount", $"Upkeep '{id}' spends 'all'; only a regain can take all. Give a formula.", id);
            }
            else if (!Formula.TryParse(upkeep.Amount, AllFormulaFeatures, out var amount, out var amountError))
            {
                Error("validate.upkeep-amount", $"Upkeep '{id}' amount is not a formula ({amountError!.Code}).", id);
            }
            else
            {
                NoteUsed(amount!);
            }
            if (upkeep.IfShort is not null && (upkeep.Change != UpkeepChange.Spend || upkeep.Toggle is null))
                Error("validate.upkeep-if-short", $"Upkeep '{id}' has ifShort, which only a spend that belongs to a toggle takes.", id);
        }

        // Content v11 (D43): a toggle's group, layer, overlay rules, pool cost, duration and level.
        void CheckToggleV11(ToggleEffect toggle)
        {
            var id = toggle.Id;
            needsV11 |= toggle.Group is not null || toggle.GroupContent is not null || toggle.Layer is not null || toggle.AllowedWith is not null
                || toggle.AllowedInBase is not null || toggle.Cost is not null || toggle.ResourceContent is not null || toggle.Level is not null
                || toggle.Duration is not null;

            if (toggle.Duration is { } duration)
            {
                if (!Formula.TryParse(duration, AllFormulaFeatures, out var durationFormula, out var durationError))
                    Error("validate.toggle-duration", $"Toggle '{id}' duration is not a formula ({durationError!.Code}).", id);
                else
                    NoteUsed(durationFormula!);
            }
            if (toggle.ResourceId is { } spendsFrom && FindResource(toggle.ResourceContent, spendsFrom)?.Counts == ResourceCounting.Up)
                Warn("validate.cost-counts-up", $"Toggle '{id}' spends from '{spendsFrom}', a pool that counts up; a cost fills it instead of using it up.", id);
            if (toggle.AutoWhen is not null && toggle.Duration is not null)
                Error("validate.toggle-auto-group", $"Toggle '{id}' switches itself, so it cannot have a duration; remove the duration.", id);

            if (toggle.Group is null)
            {
                if (toggle.Layer is not null)
                    Error("validate.toggle-layer", $"Toggle '{id}' has a layer but joins no group; add a group, or remove the layer.", id);
                if (toggle.GroupContent is not null)
                    Error("validate.toggle-group-unknown", $"Toggle '{id}' names the content that declares its group but names no group; add the group, or remove groupContent.", id);
            }
            else
            {
                var holder = toggle.GroupContent ?? revision.ContentId;
                if (holder == revision.ContentId)
                {
                    if (!revision.Effects.OfType<ToggleGroupEffect>().Any(g => g.GroupId == toggle.Group))
                        Error("validate.toggle-group-unknown", $"Toggle '{id}' joins group '{toggle.Group}', which this revision does not declare; add a toggleGroup effect, or name the content that does.", id);
                }
                else
                {
                    var declarers = local.OfContent(holder).Concat(catalog.RevisionsOf(holder)).ToList();
                    if (declarers.Count == 0)
                        Warn("validate.toggle-group-content-missing", $"Toggle '{id}' joins group '{toggle.Group}' of content {holder}, which is not installed; it acts alone until that content is.", id);
                    else if (!declarers.Any(r => r.Effects.OfType<ToggleGroupEffect>().Any(g => g.GroupId == toggle.Group)))
                        Error("validate.toggle-group-unknown", $"Toggle '{id}' joins group '{toggle.Group}', which content {holder} does not declare.", id);
                }
            }

            if (toggle.AutoWhen is not null && toggle.Group is not null)
                Error("validate.toggle-auto-group", $"Toggle '{id}' switches itself, so it cannot join a group; remove the group.", id);

            if (toggle.Layer != ToggleLayer.Overlay)
            {
                if (toggle.AllowedWith is { Count: > 0 } || toggle.AllowedInBase == true)
                    Error("validate.toggle-allowed-with", $"Toggle '{id}' is not an overlay, so it takes no allowedWith or allowedInBase; make it an overlay, or remove them.", id);
            }
            else
            {
                if (toggle.AllowedInBase != true && toggle.AllowedWith is not { Count: > 0 })
                    Warn("validate.toggle-overlay-unreachable", $"Overlay '{id}' is allowed in no base and with no form, so it can never be on; set allowedInBase or list a form in allowedWith.", id);
            }
            foreach (var allowed in toggle.AllowedWith ?? [])
            {
                var holder = allowed.ContentId ?? revision.ContentId;
                var declarers = holder == revision.ContentId
                    ? new List<ContentRevision> { revision }
                    : local.OfContent(holder).Concat(catalog.RevisionsOf(holder)).ToList();
                if (declarers.Count == 0)
                {
                    Warn("validate.toggle-allowed-with-content-missing", $"Toggle '{id}' is allowed with toggle '{allowed.ToggleId}' of content {holder}, which is not installed; the pairing is unchecked until that content is.", id);
                    continue;
                }
                var named = declarers.SelectMany(r => r.Effects.OfType<ToggleEffect>()).Where(t => t.ToggleId == allowed.ToggleId).ToList();
                if (named.Count == 0)
                {
                    Error("validate.toggle-allowed-with", $"Toggle '{id}' is allowed with toggle '{allowed.ToggleId}' of content {holder}, which is not defined there.", id);
                    continue;
                }
                // Only a primary of the overlay's own group is ever the form that is on beside it (ToggleGroups.Resolve): a group is a
                // group id and the content that declares it (a toggle naming no groupContent belongs to the group of its own content).
                var ownGroupHolder = toggle.GroupContent ?? revision.ContentId;
                if (toggle.Layer == ToggleLayer.Overlay && !named.Any(n => n.Layer != ToggleLayer.Overlay && n.Group is not null && n.Group == toggle.Group && (n.GroupContent ?? holder) == ownGroupHolder))
                    Warn("validate.toggle-allowed-with-not-primary", $"Toggle '{id}' is allowed with toggle '{allowed.ToggleId}' of content {holder}, which is not a primary of group '{toggle.Group}' of content {ownGroupHolder}, so the pairing can never be on.", id);
            }

            if (toggle.Cost is { } cost)
            {
                CheckFormula(cost, id, "cost");
                if (toggle.ResourceId is null)
                    Error("validate.toggle-cost", $"Toggle '{id}' has a cost but names no resource to spend.", id);
            }
            if (toggle.ResourceContent is { } resourceHolder && resourceHolder != revision.ContentId && toggle.ResourceId is { } shared)
            {
                var definers = local.OfContent(resourceHolder).Concat(catalog.RevisionsOf(resourceHolder)).Where(r => r.ContentId == resourceHolder).ToList();
                if (definers.Count == 0)
                    Warn("validate.toggle-resource-content-missing", $"Toggle '{id}' spends '{shared}' of content {resourceHolder}, which is not installed.", id);
                else if (!definers.Any(d => d.Effects.OfType<ResourceEffect>().Any(r => r.ResourceId == shared)))
                    Error("validate.toggle-resource-content", $"Toggle '{id}': content {resourceHolder} defines no resource '{shared}'.", id);
            }
            CheckLevel(toggle.Level, id);
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
        Check("response outcomes", revision.Effects?.OfType<ResponseEffect>().Any(r => r.Outcomes?.Any(o => o is null) == true) == true);
        Check("roll alsoChange", revision.Effects?.OfType<RollEffect>().Any(r => r.AlsoChange?.Any(a => a is null) == true) == true);
        return problems;
    }
}
