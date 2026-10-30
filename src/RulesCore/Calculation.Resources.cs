namespace TomeStack.RulesCore;

public static partial class CharacterCalculator
{
    // ---- resources and features (M2 item 2) -----------------------------------------------------------------

    /// <summary>
    /// Every resource of every active revision (the first definition of a resource id per revision). The maximum is the
    /// formula evaluated like a modifier in the content's context; a reference-only resource is listed with no maximum,
    /// and a failing formula disables only that resource, with a warning (SPEC C-03).
    /// </summary>
    private static List<ResourceValue> CollectResources(
        List<ActiveContent> active, Character character, ClassLevelMap classLevels, Dictionary<string, int> values, string family, List<ResourceBonus> bonuses)
    {
        var resources = new List<ResourceValue>();
        foreach (var item in active)
        {
            var revision = item.Revision;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var effect in revision.Effects.OfType<ResourceEffect>())
            {
                if (!seen.Add(effect.ResourceId))
                    continue; // validation refuses duplicates on publish; the first definition counts
                var warnings = new List<Diagnostic>();
                var recoveries = revision.Effects.OfType<RecoveryEffect>()
                    .Where(r => r.ResourceId == effect.ResourceId)
                    .Select(r => Recovery(r, item, character, classLevels, values, warnings))
                    .ToList();
                var spent = character.Play.SpentOf(revision.ContentId, effect.ResourceId);
                var origin = ContentOrigin(family, item, effect);
                int? maximum = null;
                var bonusByHand = false;
                var trace = new List<TraceEntry>();
                if (effect.Automation == AutomationStatus.Reference)
                {
                    trace.Add(new(1, "base", $"Reference only: {Describe(item)} does not track this resource; track it by hand", null, 0, origin));
                }
                else if (!Formula.TryParse(effect.Maximum, Formula.FeaturesFor(revision), out var formula, out var parseError))
                {
                    warnings.Add(InvalidFormula(revision, effect, parseError!));
                }
                else
                {
                    var inputs = new List<TraceInput>();
                    if (formula!.TryEvaluate(id => Resolve(id, item, character, classLevels, values, inputs), out var value, out var error))
                    {
                        maximum = Math.Max(value, 0);
                        trace.Add(new(1, "derive", $"Maximum from {Describe(item)}: {formula.Source}", value, maximum.Value, origin, null, inputs.Count > 0 ? inputs : null));
                        // Content v11: bonuses to this maximum from the same revision, in content order; each is a trace step.
                        // The bonuses are summed and the total clamped once; a bonus the player must apply by hand, or whose formula fails, leaves the pool assisted.
                        var total = value;
                        foreach (var bonus in bonuses.Where(b => ReferenceEquals(b.Content, item) && b.Effect.Target == ResourceTargetPrefix + effect.ResourceId))
                        {
                            if (bonus.Manual)
                            {
                                bonusByHand = true;
                                continue;
                            }
                            var bonusInputs = new List<TraceInput>();
                            if (!Formula.TryParse(bonus.Effect.Value, Formula.FeaturesFor(revision), out var bonusFormula, out var bonusError))
                            {
                                warnings.Add(InvalidFormula(revision, bonus.Effect, bonusError!));
                                bonusByHand = true;
                            }
                            else if (!bonusFormula!.TryEvaluate(id => Resolve(id, item, character, classLevels, values, bonusInputs), out var amount, out bonusError))
                            {
                                warnings.Add(InvalidFormula(revision, bonus.Effect, bonusError!));
                                bonusByHand = true;
                            }
                            else
                            {
                                total += amount;
                                var text = bonus.FollowedToggle ?? (bonus.Effect.Toggle is { } own ? revision.Effects.OfType<ToggleEffect>().FirstOrDefault(t => t.ToggleId == own)?.Label : null);
                                trace.Add(new(trace.Count + 1, "add", text is null ? $"{amount:+0;-0}" : $"{amount:+0;-0} while {text}", amount, Math.Max(total, 0), ContentOrigin(family, item, bonus.Effect), null, bonusInputs.Count > 0 ? bonusInputs : null));
                            }
                        }
                        maximum = Math.Max(total, 0);
                    }
                    else
                    {
                        warnings.Add(InvalidFormula(revision, effect, error!));
                    }
                }
                var automation = maximum is null
                    ? (effect.Automation == AutomationStatus.Reference ? AutomationStatus.Reference : AutomationStatus.Assisted)
                    : bonusByHand && effect.Automation == AutomationStatus.Automatic ? AutomationStatus.Assisted : effect.Automation;
                resources.Add(new(
                    revision.Reference, revision.Name, effect.Id, effect.ResourceId, effect.Label, maximum, spent,
                    maximum is { } max ? Math.Max(max - spent, 0) : null, trace, warnings, automation, recoveries, effect.Text, ActivationOf(revision, effect.Activation),
                    effect.Counts == ResourceCounting.Up, effect.Counts == ResourceCounting.Up ? effect.ResourceFull?.Text : null, effect.Allowance == true));
            }
        }
        return resources;
    }

    /// <summary>
    /// MECH-X item 4 (R60): the companion cards of the active content, in active-content order and then effect order. Reference-only
    /// companions are not calculated. Both formulas are evaluated in the declaring content's context; one that fails leaves that
    /// number null with <c>effect.invalid-formula</c>. Armor class is clamped to 0 to <see cref="Character.MaxCompanionArmorClass"/> and
    /// the hit point maximum to 0 to <see cref="PlayState.MaxHitPoints"/> (<c>companion.out-of-range</c>). A card is made once per
    /// (content, companion id), at most <see cref="Character.MaxCompanions"/> per sheet (<c>companion.too-many</c>). The attacks and
    /// pools a card lists stay in <paramref name="attacks"/> and <paramref name="resources"/>, tagged with the companion id; one that
    /// is not on the sheet is <c>companion.missing-part</c>.
    /// </summary>
    private static List<CompanionValue> CollectCompanions(
        List<ActiveContent> active, Dictionary<string, int> values, Character character, ClassLevelMap classLevels, string family,
        List<AttackEntry> attacks, List<ResourceValue> resources, List<Diagnostic> diagnostics)
    {
        var cards = new List<CompanionValue>();
        var seen = new HashSet<(Guid Content, Guid Companion)>();
        var tooMany = false;
        foreach (var item in active)
        {
            var revision = item.Revision;
            foreach (var effect in revision.Effects.OfType<CompanionEffect>().Where(c => c.Automation != AutomationStatus.Reference))
            {
                if (!seen.Add((revision.ContentId, effect.CompanionId)))
                    continue;
                if (cards.Count >= Character.MaxCompanions)
                {
                    if (!tooMany)
                        diagnostics.Add(new("companion.too-many", $"A sheet shows at most {Character.MaxCompanions} companions; the rest are left out."));
                    tooMany = true;
                    continue;
                }
                var origin = ContentOrigin(family, item, effect);
                var warnings = new List<Diagnostic>();
                void Warn(Diagnostic d)
                {
                    if (!warnings.Contains(d))
                        warnings.Add(d);
                    AddOnce(diagnostics, d);
                }

                int? Number(string source, string what, int low, int high, out List<TraceEntry> trace)
                {
                    trace = [];
                    var inputs = new List<TraceInput>();
                    if (!TryEvaluate(source, item, character, classLevels, values, inputs, out var value, out var failure))
                    {
                        Warn(InvalidFormula(revision, effect, failure!));
                        return null;
                    }
                    var clamped = Math.Clamp(value, low, high);
                    if (clamped != value)
                        Warn(new("companion.out-of-range", $"'{revision.Name}' companion '{effect.Id}': the {what.ToLowerInvariant()} {value} is outside {low} to {high} and was set to {clamped}.", revision.Reference, effect.Id));
                    trace.Add(new(1, "derive", $"{what} from {source}", value, clamped, origin, null, inputs.Count > 0 ? inputs : null));
                    return clamped;
                }

                var armorClass = Number(effect.ArmorClass, "Armor Class", 0, Character.MaxCompanionArmorClass, out var armorClassTrace);
                var hitPointMaximum = Number(effect.HitPointMaximum, "Hit point maximum", 0, PlayState.MaxHitPoints, out var hitPointTrace);

                var attackIds = effect.Attacks ?? [];
                var resourceIds = effect.ResourceIds ?? [];
                foreach (var id in attackIds)
                {
                    var found = false;
                    for (var i = 0; i < attacks.Count; i++)
                    {
                        if (attacks[i].Item.ContentId == revision.ContentId && attacks[i].EffectId == id)
                        {
                            attacks[i] = attacks[i] with { CompanionId = effect.CompanionId };
                            found = true;
                        }
                    }
                    if (!found)
                        Warn(new("companion.missing-part", $"'{revision.Name}' companion '{effect.Id}' lists attack '{id}', which is not on the sheet.", revision.Reference, effect.Id));
                }
                foreach (var id in resourceIds)
                {
                    var found = false;
                    for (var i = 0; i < resources.Count; i++)
                    {
                        if (resources[i].Content.ContentId == revision.ContentId && resources[i].ResourceId == id)
                        {
                            resources[i] = resources[i] with { CompanionId = effect.CompanionId };
                            found = true;
                        }
                    }
                    if (!found)
                        Warn(new("companion.missing-part", $"'{revision.Name}' companion '{effect.Id}' lists pool '{id}', which is not on the sheet.", revision.Reference, effect.Id));
                }

                var recorded = character.Play.Companions?.FirstOrDefault(p => p.ContentId == revision.ContentId && p.Id == effect.CompanionId);
                var current = hitPointMaximum is { } max ? Math.Min(recorded?.Current ?? max, max) : 0;
                var automation = (armorClass is null || hitPointMaximum is null) && effect.Automation == AutomationStatus.Automatic ? AutomationStatus.Assisted : effect.Automation;
                cards.Add(new(
                    revision.Reference, revision.Name, effect.Id, effect.CompanionId, effect.Label, armorClass, armorClassTrace, hitPointMaximum, hitPointTrace,
                    current, recorded?.Temporary ?? 0, effect.Speed, [.. attackIds], [.. resourceIds], automation, warnings, origin));
            }
        }
        return cards;
    }

    /// <summary>A recovery amount: <c>all</c>, or its formula evaluated in the content's context (a failure is a warning).</summary>
    private static RecoveryInfo Recovery(
        RecoveryEffect recovery, ActiveContent item, Character character, ClassLevelMap classLevels, Dictionary<string, int> values, List<Diagnostic> warnings)
    {
        if (string.Equals(recovery.Amount.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            return new(recovery.Id, recovery.On, recovery.Amount, recovery.Text, All: true);
        if (recovery.Automation != AutomationStatus.Automatic)
            return new(recovery.Id, recovery.On, recovery.Amount, recovery.Text); // the player applies it
        FormulaError? failure;
        if (Formula.TryParse(recovery.Amount, Formula.FeaturesFor(item.Revision), out var formula, out failure)
            && formula!.TryEvaluate(id => Resolve(id, item, character, classLevels, values, []), out var value, out failure))
        {
            return new(recovery.Id, recovery.On, recovery.Amount, recovery.Text, Math.Max(value, 0));
        }
        warnings.Add(InvalidFormula(item.Revision, recovery, failure!));
        return new(recovery.Id, recovery.On, recovery.Amount, recovery.Text);
    }

    /// <summary>
    /// Content v8 (M2.2): each roll's <c>bonus</c> formula for this character, with its sign (a Strength 8 bonus is −1).
    /// A formula that fails is a content diagnostic (<c>effect.invalid-formula</c>), and the roll is refused rather than
    /// rolled without it. A revision below v8 has no roll bonus (<see cref="IgnoresV8"/>).
    /// </summary>
    private static Dictionary<(ContentReference, string), int> RollBonuses(
        List<ActiveContent> active, Character character, ClassLevelMap classLevels, Dictionary<string, int> values, List<Diagnostic> diagnostics)
    {
        var bonuses = new Dictionary<(ContentReference, string), int>();
        foreach (var item in active.Where(a => !IgnoresV8(a.Revision)))
        {
            foreach (var roll in item.Revision.Effects.OfType<RollEffect>())
            {
                if (roll.Bonus is not { } source)
                    continue;
                FormulaError? failure;
                if (Formula.TryParse(source, Formula.FeaturesFor(item.Revision), out var formula, out failure)
                    && formula!.TryEvaluate(id => Resolve(id, item, character, classLevels, values, []), out var value, out failure))
                    bonuses[(item.Revision.Reference, roll.Id)] = value;
                else
                    diagnostics.Add(InvalidFormula(item.Revision, roll, failure!));
            }
        }
        return bonuses;
    }

    /// <summary>What a roll's cost and save DC came to for this character (<see cref="RollDetailsOf"/>).</summary>
    private sealed record RollDetail(int? Cost, int? SaveDc, IReadOnlyList<TraceEntry>? SaveTrace, bool Failed, IReadOnlyList<AlsoChangeValue>? AlsoChange = null);

    /// <summary>
    /// Each roll's <c>cost</c> (a number of uses, never negative; one when it has a resource and no formula) and, from content v11
    /// (D43), its save DC with the trace of how it was reached. A formula that fails gives null and the <c>effect.invalid-formula</c>
    /// diagnostic, and the row is assisted (<see cref="RollDetail.Failed"/>); it stays available, like a feature attack's cost.
    /// </summary>
    private static Dictionary<(ContentReference, string), RollDetail> RollDetailsOf(
        List<ActiveContent> active, string family, Character character, ClassLevelMap classLevels, Dictionary<string, int> values, List<Diagnostic> diagnostics)
    {
        var details = new Dictionary<(ContentReference, string), RollDetail>();
        foreach (var item in active)
        {
            var revision = item.Revision;
            foreach (var roll in revision.Effects.OfType<RollEffect>())
            {
                var failed = false;
                int? cost = null;
                if (roll.Cost is { } costSource)
                {
                    if (TryEvaluate(costSource, item, character, classLevels, values, [], out var spent, out var costFailure))
                    {
                        cost = Math.Max(spent, 0);
                    }
                    else
                    {
                        failed = true;
                        AddOnce(diagnostics, InvalidFormula(revision, roll, costFailure!));
                    }
                }
                else if (roll.ResourceId is not null)
                {
                    cost = 1;
                }

                int? saveDc = null;
                List<TraceEntry>? saveTrace = null;
                if (roll.Save is { } save)
                {
                    var inputs = new List<TraceInput>();
                    if (TryEvaluate(save.Dc, item, character, classLevels, values, inputs, out var dc, out var dcFailure))
                    {
                        saveDc = dc;
                        var read = inputs.DistinctBy(i => i.Name, StringComparer.Ordinal).ToList();
                        var classRoot = item.ClassRoot is { } root ? active.FirstOrDefault(a => a.Revision.Reference == root)?.Revision.Name : null;
                        // A named value is traced under the content that declares it.
                        string? ValueOwner(string name) =>
                            item.ClassRoot is { } ownerRoot && classLevels.NamedValues.TryGetValue((ownerRoot, FormulaIdentifiers.ValueId(name)), out var ownerValue) ? ownerValue.Content.Revision.Name : null;
                        var from = read.Count == 0 ? "" : $", from {string.Join(" and ", read.Select(i => $"{InputLabel(i.Name, classRoot, FormulaIdentifiers.IsValue(i.Name) ? ValueOwner(i.Name) : null)} {i.Value}"))}";
                        saveTrace = [new(1, "set", $"{AbilityNames[save.Ability]} save DC{from}", null, dc, ContentOrigin(family, item, roll), null, read)];
                    }
                    else
                    {
                        failed = true;
                        AddOnce(diagnostics, InvalidFormula(revision, roll, dcFailure!));
                    }
                }
                List<AlsoChangeValue>? alsoChange = null;
                if (roll.AlsoChange is { } changes)
                {
                    alsoChange = [];
                    foreach (var change in changes.OfType<AlsoChange>())
                    {
                        int? amount = null;
                        if (TryEvaluate(change.Amount ?? "", item, character, classLevels, values, [], out var evaluated, out var amountFailure))
                        {
                            amount = Math.Max(evaluated, 0);
                        }
                        else
                        {
                            failed = true;
                            AddOnce(diagnostics, InvalidFormula(revision, roll, amountFailure!));
                        }
                        alsoChange.Add(new(change.ResourceContent ?? revision.ContentId, change.ResourceId, change.Change, amount));
                    }
                }
                details[(revision.Reference, roll.Id)] = new(cost, saveDc, saveTrace, failed, alsoChange);
            }
        }
        return details;
    }

    /// <summary>Adds a diagnostic unless an identical one (same code, content, effect and message) is already there.</summary>
    private static void AddOnce(List<Diagnostic> diagnostics, Diagnostic diagnostic)
    {
        if (!diagnostics.Contains(diagnostic))
            diagnostics.Add(diagnostic);
    }

    /// <summary>A formula identifier in words for a trace: "Proficiency bonus", "Wisdom modifier", "Strength score", a scale as "{class} {scaleId}"; any other identifier stays as written.</summary>
    private static string InputLabel(string identifier, string? classRoot, string? valueOwner)
    {
        if (FormulaIdentifiers.IsScale(identifier))
            return $"{classRoot} {FormulaIdentifiers.ScaleId(identifier)}".Trim();
        if (FormulaIdentifiers.IsValue(identifier))
            return $"{valueOwner} {FormulaIdentifiers.ValueId(identifier)}".Trim();
        if (identifier == FormulaIdentifiers.ProficiencyBonus)
            return "Proficiency bonus";
        if (identifier == FormulaIdentifiers.Level)
            return "Character level";
        if (identifier == FormulaIdentifiers.ClassLevel)
            return "Class level";
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var name = ability.ToString().ToUpperInvariant();
            if (identifier == $"{name}.MOD")
                return $"{AbilityNames[ability]} modifier";
            if (identifier == $"{name}.SCORE")
                return $"{AbilityNames[ability]} score";
        }
        return identifier;
    }

    private static FeatureEntry Feature(
        ActiveContent item, string family, IReadOnlyList<Diagnostic> diagnostics, Dictionary<(ContentReference, string), RollDetail> rollDetails, Dictionary<(ContentReference, string), int> bonuses,
        Func<ToggleRef, string?> outOfForm, IReadOnlyList<ResourceValue> resources, Dictionary<(ContentReference, string), CastSpellValue> castSpells)
    {
        var revision = item.Revision;
        // Content v11 (D43): a roll with whileToggle is always listed; it is unavailable, with the reason, while its form is off.
        FeatureEffect Roll(RollEffect roll)
        {
            var reason = roll.WhileToggle is { } form ? outOfForm(form) : null;
            // A roll that draws down needs a pool that counts up on this sheet (a pinned revision may have an ordinary one): it is unavailable
            // with the reason, never given a spent use back.
            if (reason is null && roll.DrawsDown == true && roll.ResourceId is { } drawnPool)
            {
                var drawnContent = roll.ResourceContent ?? revision.ContentId;
                if (!resources.Any(r => r.Content.ContentId == drawnContent && r.ResourceId == drawnPool && r.CountsUp))
                    reason = $"Pool '{drawnPool}' does not count up for this character, so it cannot be drawn down.";
            }
            var detail = rollDetails.GetValueOrDefault((revision.Reference, roll.Id));
            // A cost or save DC that cannot be calculated: the row stays available and the player works it out.
            var automation = detail is { Failed: true } && roll.Automation == AutomationStatus.Automatic ? AutomationStatus.Assisted : roll.Automation;
            return new(
                roll.Id, roll.Type, automation, roll.Text, roll.Label, roll.Dice, roll.ResourceId, roll.Activation,
                roll.ResourceContent, detail?.Cost, roll.VariableCost == true,
                bonuses.TryGetValue((revision.Reference, roll.Id), out var bonus) ? bonus : null, reason is null, reason,
                roll.Save?.Ability, detail?.SaveDc, detail?.SaveTrace, roll.Area, roll.Range, roll.Targets, roll.PerUnit,
                detail?.AlsoChange, roll.DrawsDown == true, roll.RollId,
                PaymentOf(roll.Payment, revision), castSpells.GetValueOrDefault((revision.Reference, roll.Id)));
        }
        var effects = revision.Effects.Select(e => e switch
        {
            RollEffect roll => Roll(roll),
            ResourceEffect resource => new FeatureEffect(e.Id, e.Type, e.Automation, e.Text, resource.Label, ResourceId: resource.ResourceId),
            RecoveryEffect recovery => new FeatureEffect(e.Id, e.Type, e.Automation, e.Text, ResourceId: recovery.ResourceId),
            // A damage defence that is conditional is not applied by the calculator: the player applies it (D19 amended).
            GrantEffect { Timing: not EffectTiming.Always } grant when GrantEffect.IsDamageDefence(grant.Grant) =>
                new FeatureEffect(e.Id, e.Type, e.Automation == AutomationStatus.Automatic ? AutomationStatus.Assisted : e.Automation, e.Text),
            _ => new FeatureEffect(e.Id, e.Type, e.Automation, e.Text),
        }).ToList();
        var automation = effects.Count == 0 ? AutomationStatus.Reference : effects.Max(e => e.Automation);
        if (automation == AutomationStatus.Automatic && diagnostics.Any(d => d.EffectId is not null))
            automation = AutomationStatus.Assisted;
        var via = item.GrantedBy is { } by ? $"granted by {by.Kind.ToString().ToLowerInvariant()} '{by.Name}'"
            : item.ChosenFrom is { } chooser ? $"chosen from {chooser.Kind.ToString().ToLowerInvariant()} '{chooser.Name}'"
            : null;
        var origin = new TraceOrigin(TraceOriginKind.Content, family, revision.Reference, revision.Name, null, item.Source.Id, item.Source.Title, revision.Provenance.Page);
        return new(revision.Reference, revision.Name, revision.Kind, revision.Summary, via, origin, automation, effects, diagnostics, (item.GrantedBy ?? item.ChosenFrom)?.Reference, (item.GrantedBy ?? item.ChosenFrom)?.Name);
    }

    /// <summary>
    /// A formula identifier in the context of <paramref name="content"/>: <c>LEVEL</c>, <c>CLASS_LEVEL</c> (the level in the
    /// class the content belongs to; unavailable outside a class) or a field value calculated so far. Records what it read.
    /// </summary>
    private static int? Resolve(
        string identifier, ActiveContent content, Character character, ClassLevelMap classLevels, Dictionary<string, int> values, List<TraceInput> inputs)
    {
        int? resolved = identifier switch
        {
            FormulaIdentifiers.Level => character.TotalLevel,
            FormulaIdentifiers.ClassLevel => content.ClassRoot is { } root && classLevels.TryGetValue(root, out var level) ? level : null,
            // Content v9: the column's value at the level of the class the content belongs to (ADR-010). Unavailable
            // outside a class, or when neither the class nor its subclass defines the scale.
            _ when FormulaIdentifiers.IsScale(identifier) => content.ClassRoot is { } scaleRoot && classLevels.TryGetValue(scaleRoot, out var scaleLevel)
                && classLevels.Scales.TryGetValue((scaleRoot, FormulaIdentifiers.ScaleId(identifier)), out var scale)
                ? scale.Effect.Values[Math.Clamp(scaleLevel, 1, scale.Effect.Values.Count) - 1]
                : null,
            // Content v11 (D43): a named value its class declares, evaluated where it is declared (ResolveValue).
            _ when FormulaIdentifiers.IsValue(identifier) => ResolveValue(identifier, content, character, classLevels, values),
            _ when FormulaIdentifiers.FieldFor(identifier) is { } read && values.TryGetValue(read, out var v) => v,
            _ => null,
        };
        if (resolved is { } r)
            inputs.Add(new(identifier, r));
        return resolved;
    }
}
