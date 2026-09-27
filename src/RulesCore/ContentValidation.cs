namespace TomeStack.RulesCore;

/// <summary>What <see cref="ContentValidator"/> found. A revision with errors cannot be published (SPEC I-02, I-06; ADR-004).</summary>
public sealed record ValidationReport(ContentReference Revision, IReadOnlyList<Diagnostic> Errors, IReadOnlyList<Diagnostic> Warnings)
{
    public bool CanPublish => Errors.Count == 0;
}

/// <summary>
/// M1 item 3: validates one content revision before it is published. It reports schema problems (shape and allowed
/// values), reference problems (source, granted content and choice options), formula problems (modifiers, resource
/// maximums, recovery amounts, roll dice) and dependency cycles. It is pure: the catalog is read, never written.
/// Errors block publishing; warnings (for example an effect type this build does not automate) do not.
/// </summary>
public static class ContentValidator
{
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
        foreach (var effect in revision.Effects)
        {
            switch (effect)
            {
                case UnknownEffect unknown:
                    Warn("validate.effect-unsupported", $"Effect '{unknown.Id}' has type '{unknown.DeclaredType}', which this version does not automate; it stays reference-only.", unknown.Id);
                    break;
                case ModifierEffect modifier:
                    needsV3 |= modifier.Target is FieldIds.ArmorClass or FieldIds.HitPoints;
                    if (!CharacterCalculator.IsField(modifier.Target))
                        Error("validate.unknown-target", $"Effect '{modifier.Id}' targets '{modifier.Target}', which is not a calculated field.", modifier.Id);
                    if (modifier.Stacking == StackingRule.HighestInGroup && string.IsNullOrWhiteSpace(modifier.StackGroup))
                        Error("validate.stack-group-missing", $"Effect '{modifier.Id}' uses highest-in-group stacking without a stackGroup.", modifier.Id);
                    CheckFormula(modifier.Value, modifier.Id, "value");
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
                    else if (grant.Target is not { } target || !CharacterCalculator.IsField(target) || !(target.StartsWith("save.", StringComparison.Ordinal) || target.StartsWith("skill.", StringComparison.Ordinal)))
                    {
                        Error("validate.unknown-target", $"Effect '{grant.Id}' grants {grant.Grant.ToString().ToLowerInvariant()} in '{grant.Target}', which is not a saving throw or skill.", grant.Id);
                    }
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
                    break;
                case RestrictionEffect restriction:
                    needsV3 |= restriction.Field is FieldIds.ArmorClass or FieldIds.HitPoints;
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
                    if (revision.Kind != ContentKind.Item)
                        Warn("validate.armor-kind", $"Armor counts only on an equipped item; '{revision.Name}' is {revision.Kind.ToString().ToLowerInvariant()} content, so it applies only if pinned.", armor.Id);
                    break;
            }
        }
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

        return new ValidationReport(reference, errors, warnings);

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
