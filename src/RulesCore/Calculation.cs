namespace TomeStack.RulesCore;

/// <summary>Read-only access to content revisions and sources. Implemented by persistence, not by the rules core.</summary>
public interface IContentCatalog
{
    ContentRevision? FindRevision(ContentReference reference);
    SourceRecord? FindSource(Guid sourceId);

    /// <summary>Published revisions that declare themselves an option of the choice (content schema v4, M2 item 5).</summary>
    IEnumerable<ContentRevision> ChoiceExtensions(Guid contentId, string choiceId) => [];

    /// <summary>Every stored revision of one content id, any status (validation of <see cref="ContentRevision.ExtendsChoice"/>).</summary>
    IEnumerable<ContentRevision> RevisionsOf(Guid contentId) => [];
}

public sealed record Diagnostic(string Code, string Message, ContentReference? Content = null, string? EffectId = null);

public enum TraceOriginKind { CharacterChoice, Content, RulesPolicy, Override }

/// <summary>Where a trace step came from: the character's own choice, a rules-family policy, pinned content, or a user override.</summary>
public sealed record TraceOrigin(
    TraceOriginKind Kind,
    string RulesFamily,
    ContentReference? Content = null,
    string? ContentName = null,
    string? EffectId = null,
    Guid? SourceId = null,
    string? SourceTitle = null,
    PageRef? Page = null);

/// <summary>A value a step read: a field id or formula identifier, and its value at that point.</summary>
public sealed record TraceInput(string Name, int Value);

/// <param name="Amount">The value this step contributes (for <c>add</c>/<c>set</c>) or its input, when meaningful.</param>
/// <param name="Result">The running value of <paramref name="Field"/> after this step.</param>
/// <param name="Field">The field this step belongs to. A field's trace includes the steps of the fields it reads.</param>
/// <param name="Inputs">Values the step read (ARCHITECTURE "Rules execution" step 5).</param>
public sealed record TraceEntry(
    int Order,
    string Operation,
    string Description,
    int? Amount,
    int Result,
    TraceOrigin Origin,
    string? Field = null,
    IReadOnlyList<TraceInput>? Inputs = null);

/// <summary>ARCHITECTURE "Rules execution" step 5: value, units, trace, warnings and automation status for one field.</summary>
public sealed record DerivedValue(
    string Field,
    string Label,
    int Value,
    int ComputedValue,
    IReadOnlyList<TraceEntry> Trace,
    IReadOnlyList<Diagnostic> Warnings,
    AutomationStatus Automation,
    FieldOverride? Override,
    string Units = "");

/// <summary>
/// SPEC C-01: one choice an active revision offers (and whose level is reached), what was selected for it, and whether
/// it is resolved. The sheet flags unresolved choices; the builder (M2) will answer them.
/// </summary>
public sealed record ChoiceStatus(
    ContentReference Source,
    string SourceName,
    string ChoiceId,
    string? Text,
    int Count,
    IReadOnlyList<ContentReference> Options,
    IReadOnlyList<ContentReference> Selected,
    bool Resolved);

/// <param name="Active">Every content revision that applies to the character (pinned, class, granted or chosen), in resolution order.</param>
/// <param name="Resources">M2 item 2: every resource an active revision defines, with its calculated maximum and what is spent.</param>
/// <param name="Features">M2 item 2: every active revision with its text, automation status, effects and rolls, in resolution order.</param>
/// <param name="HitPoints">M2 item 2: the displayed maximum with current and temporary hit points from the play state.</param>
/// <param name="HitDice">The hit dice pool per die size, largest first, with what is spent (character schema v5).</param>
/// <param name="Spellcasting">Every caster with its spells (D04), primary first.</param>
/// <param name="SpellSlots">Spell slots per spell level that has any, from the <c>spellSlots.N</c> fields.</param>
/// <param name="PactSlots">Pact Magic slots, when a caster has them.</param>
public sealed record CharacterSheet(
    Guid CharacterId,
    string RulesFamily,
    IReadOnlyList<DerivedValue> Fields,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<ChoiceStatus>? Choices = null,
    IReadOnlyList<ContentReference>? Active = null,
    IReadOnlyList<ResourceValue>? Resources = null,
    IReadOnlyList<FeatureEntry>? Features = null,
    HitPointState? HitPoints = null,
    IReadOnlyList<HitDiceValue>? HitDice = null,
    IReadOnlyList<SpellcastingEntry>? Spellcasting = null,
    IReadOnlyList<SlotValue>? SpellSlots = null,
    SlotValue? PactSlots = null,
    IReadOnlyList<AttackEntry>? Attacks = null,
    IReadOnlyList<ToggleValue>? Toggles = null,
    IReadOnlyList<ScaleValue>? Scales = null)
{
    public DerivedValue Field(string field) => Fields.Single(f => f.Field == field);
}

/// <summary>
/// Content v9 (ADR-010): one column of a class table at the character's level in that class, for example "Ink 4" for a
/// level-5 Test Chronicler. <paramref name="Content"/> is the revision that defines it (the class or its subclass).
/// </summary>
public sealed record ScaleValue(ContentReference Class, string ClassName, ContentReference Content, string ScaleId, string Label, int ClassLevel, int Value);

/// <summary>
/// A limited-use resource (ADR-003 <c>resource</c>). <paramref name="Maximum"/> is its formula evaluated in the content's
/// own context (<c>CLASS_LEVEL</c> is the level in its class), or <c>null</c> when it cannot be calculated (a reference-only
/// resource, or a formula that fails, with a warning). <paramref name="Current"/> is the maximum less what is spent, never
/// below 0. The key for spending is the content id and <paramref name="ResourceId"/>.
/// </summary>
public sealed record ResourceValue(
    ContentReference Content,
    string ContentName,
    string EffectId,
    string ResourceId,
    string Label,
    int? Maximum,
    int Spent,
    int? Current,
    IReadOnlyList<TraceEntry> Trace,
    IReadOnlyList<Diagnostic> Warnings,
    AutomationStatus Automation,
    IReadOnlyList<RecoveryInfo> Recoveries,
    string? Text);

/// <summary>
/// A <c>recovery</c> effect of the same revision for this resource. Rests preview it; calculation never applies it.
/// <paramref name="Value"/> is <paramref name="Amount"/> evaluated in the content's context, or <c>null</c> for <c>all</c>
/// and for a formula that fails (with a warning on the resource), which the rest leaves to the player.
/// </summary>
public sealed record RecoveryInfo(string EffectId, RestPeriod On, string Amount, string? Text, int? Value = null, bool All = false);

/// <summary>
/// SPEC I-05: one active revision as the sheet lists it. <paramref name="Automation"/> is the least automated of its
/// effects (<c>reference</c> when it has none, so pure text is never mistaken for automation), and at most
/// <c>assisted</c> when a diagnostic names one of its effects. <paramref name="Diagnostics"/> are the problems scoped to it.
/// </summary>
public sealed record FeatureEntry(
    ContentReference Content,
    string Name,
    ContentKind Kind,
    string? Summary,
    string? Via,
    TraceOrigin Origin,
    AutomationStatus Automation,
    IReadOnlyList<FeatureEffect> Effects,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>One effect of a feature: its text and automation, plus the dice and linked resource of a roll.</summary>
/// <param name="ResourceContent">Content v6: the content id that defines <paramref name="ResourceId"/> (a shared resource); null for this feature.</param>
/// <param name="Cost">Content v6: uses the action spends (its formula evaluated), or the most it may spend when <paramref name="VariableCost"/>.</param>
/// <param name="Bonus">
/// Content v8: the roll's bonus formula evaluated for this character (for example the Fighter level, or a negative
/// Strength modifier), added to the dice. Null when the roll has none, or when it failed (a diagnostic; the roll is refused).
/// </param>
public sealed record FeatureEffect(
    string Id, string Type, AutomationStatus Automation, string? Text, string? Label = null, string? Dice = null, string? ResourceId = null, Activation? Activation = null,
    Guid? ResourceContent = null, int? Cost = null, bool VariableCost = false, int? Bonus = null);

/// <summary>Content v6 (M3 B2): a toggle the player switches on and off, and whether it is on now.</summary>
public sealed record ToggleValue(ContentReference Content, string ContentName, string EffectId, string ToggleId, string Label, bool On, string? ResourceId, string? Text);

/// <summary>A calculated field id and its display label.</summary>
public sealed record FieldInfo(string Id, string Label);

/// <summary>Hit points for play: the displayed maximum (after any override), current (at most the maximum) and temporary.</summary>
public sealed record HitPointState(int Maximum, int Current, int Temporary);

/// <summary>
/// Hit dice of one size: one per level in every class with that hit die (SRD: hit dice of the same size pool together).
/// <paramref name="Remaining"/> is the total less what is spent, never below 0 (a lower total after an update does not go negative).
/// </summary>
public sealed record HitDiceValue(int Die, int Total, int Spent, int Remaining, IReadOnlyList<string> Classes);

/// <summary>
/// Slots of one spell level (or the Pact Magic slots, <paramref name="Level"/> being their spell level) for play: the
/// maximum is the displayed field (after any override), remaining = maximum − spent, never below 0.
/// </summary>
public sealed record SlotValue(int Level, int Maximum, int Spent, int Remaining, string Field);

/// <summary>
/// D04: one caster (a class or subclass with a <c>spellcasting</c> effect) at its class level. The first one in class order
/// is <paramref name="Primary"/>: its attack bonus, save DC and slots are the sheet fields. Others are calculated here the
/// same way, with the same <c>spellAttack</c> and <c>spellSaveDc</c> modifiers (an item's "+1 to spell attacks" counts
/// for every caster); a user override of the sheet field is the primary's only (M2.1). <paramref name="AttackTrace"/> and
/// <paramref name="SaveDcTrace"/> explain both numbers for every caster.
/// </summary>
public sealed record SpellcastingEntry(
    ContentReference Content,
    string Name,
    string EffectId,
    int ClassLevel,
    Ability Ability,
    int AttackBonus,
    int SaveDc,
    SpellPreparation Preparation,
    string SpellList,
    SpellSlotKind SlotKind,
    IReadOnlyList<int> Slots,
    int? CantripsAllowed,
    int? SpellsAllowed,
    bool Primary,
    TraceOrigin Origin,
    IReadOnlyList<SpellEntry> Spells,
    IReadOnlyList<Diagnostic> Warnings,
    IReadOnlyList<TraceEntry>? AttackTrace = null,
    IReadOnlyList<TraceEntry>? SaveDcTrace = null)
{
    /// <summary>The highest spell level this caster has a slot for (0: cantrips only).</summary>
    public int HighestSlotLevel => Slots.Select((count, i) => (count, level: i + 1)).Where(s => s.count > 0).Select(s => s.level).DefaultIfEmpty(0).Max();
}

/// <summary>
/// SPEC C-02, C-04: an attack with an equipped weapon. <paramref name="ToHit"/> = the ability modifier (Strength for
/// melee, Dexterity for ranged, the better one with finesse) + the proficiency bonus if proficient + the weapon's own
/// bonus; <paramref name="Damage"/> is the dice plus the same modifier and bonus. <paramref name="Trace"/> explains to-hit.
/// </summary>
public sealed record AttackEntry(
    ContentReference Item,
    string Name,
    string EffectId,
    WeaponAttack Attack,
    WeaponCategory Category,
    Ability Ability,
    int ToHit,
    string Damage,
    string? VersatileDamage,
    string DamageType,
    IReadOnlyList<string> Properties,
    string? Range,
    string? Mastery,
    bool Proficient,
    AutomationStatus Automation,
    IReadOnlyList<TraceEntry> Trace,
    IReadOnlyList<Diagnostic> Warnings,
    TraceOrigin Origin);

/// <summary>A known or prepared spell with its game data, for the sheet (a spell is never active content).</summary>
public sealed record SpellEntry(
    ContentReference Spell,
    string Name,
    int Level,
    bool Prepared,
    string? Summary,
    string? Text,
    string? School,
    string? CastingTime,
    string? Range,
    string? Components,
    string? Duration,
    bool Concentration,
    bool Ritual,
    SpellAttackKind Attack,
    Ability? Save,
    string? Dice,
    TraceOrigin Origin,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Pure, dependency-ordered calculation of derived character values (ARCHITECTURE "Rules execution"; ADR-003).
/// Fields form a graph: each field's base inputs, plus an edge for every identifier an effect formula reads.
/// Effects that would create a cycle are disabled with a diagnostic, and the rest of the sheet still calculates.
/// There are no persistence, UI or Windows dependencies. Invalid content is isolated (SPEC C-03).
/// </summary>
public static class CharacterCalculator
{
    public const string InitiativeField = FieldIds.Initiative;

    public const string Stealth = "stealth";

    // Declared before Specs: static initializers run in textual order and BuildSpecs reads this.
    private static readonly Dictionary<Ability, string> AbilityNames = new()
    {
        [Ability.Str] = "Strength", [Ability.Dex] = "Dexterity", [Ability.Con] = "Constitution",
        [Ability.Int] = "Intelligence", [Ability.Wis] = "Wisdom", [Ability.Cha] = "Charisma",
    };

    /// <summary>The 18 skills of both SRDs and the ability each uses. Field ids are <c>skill.&lt;key&gt;</c>. Before Specs, like AbilityNames.</summary>
    public static IReadOnlyList<(string Key, string Label, Ability Ability)> Skills { get; } =
    [
        ("acrobatics", "Acrobatics", Ability.Dex), ("animalHandling", "Animal Handling", Ability.Wis), ("arcana", "Arcana", Ability.Int),
        ("athletics", "Athletics", Ability.Str), ("deception", "Deception", Ability.Cha), ("history", "History", Ability.Int),
        ("insight", "Insight", Ability.Wis), ("intimidation", "Intimidation", Ability.Cha), ("investigation", "Investigation", Ability.Int),
        ("medicine", "Medicine", Ability.Wis), ("nature", "Nature", Ability.Int), ("perception", "Perception", Ability.Wis),
        ("performance", "Performance", Ability.Cha), ("persuasion", "Persuasion", Ability.Cha), ("religion", "Religion", Ability.Int),
        ("sleightOfHand", "Sleight of Hand", Ability.Dex), (Stealth, "Stealth", Ability.Dex), ("survival", "Survival", Ability.Wis),
    ];

    private static readonly IReadOnlyList<FieldSpec> Specs = BuildSpecs();

    private static readonly IReadOnlyDictionary<string, int> SpecIndex =
        Specs.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);

    public static IEnumerable<string> Fields => Specs.Select(s => s.Id);

    /// <summary>Every calculated field with its label, for authoring UIs (modifier targets, restrictions).</summary>
    public static IReadOnlyList<FieldInfo> FieldInfos { get; } = [.. Specs.Select(s => new FieldInfo(s.Id, s.Label))];

    public static bool IsField(string field) => SpecIndex.ContainsKey(field);

    /// <summary>
    /// Content validation (M1 item 3): the dependency cycles this revision's own modifiers would create with the base
    /// field graph, as <c>effect.dependency-cycle</c> diagnostics. Effects that do not parse or target no field are
    /// reported by <see cref="ContentValidator"/> instead, and are skipped here.
    /// </summary>
    public static IReadOnlyList<Diagnostic> DependencyCycles(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var source = new SourceRecord
        {
            Id = revision.Provenance.SourceId, Title = "", Publisher = "", RulesFamilies = revision.RulesFamilies,
            EditionVersion = "", License = "", Redistributable = false,
        };
        var content = new ActiveContent(revision, source);
        var modifiers = new List<Modifier>();
        foreach (var effect in revision.Effects.OfType<ModifierEffect>())
        {
            if (SpecIndex.ContainsKey(effect.Target) && Formula.TryParse(effect.Value, AllowsScales(revision), out var formula, out _))
                modifiers.Add(new(content, effect, formula!, [.. formula!.Identifiers.Select(FormulaIdentifiers.FieldFor).OfType<string>().Distinct(StringComparer.Ordinal)]));
        }
        var diagnostics = new List<Diagnostic>();
        RemoveCycles(modifiers, diagnostics, Specs.ToDictionary(s => s.Id, _ => new List<Diagnostic>(), StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        return diagnostics;
    }

    /// <summary>
    /// Calculates the sheet. Content with <c>restriction</c> effects (prerequisites such as "Strength 13 or higher") is
    /// checked first against the sheet calculated without that content, so content cannot qualify itself (a feat that
    /// raises Strength does not meet its own Strength prerequisite). Content whose prerequisite is not met is left out,
    /// with a <c>restriction.unmet</c> diagnostic scoped to it; everything else still calculates (SPEC C-03). Checks use
    /// the displayed values, so a user override counts (SPEC C-06), and the trace shows it.
    /// </summary>
    public static CharacterSheet Calculate(Character character, IContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(catalog);

        var first = Calculate(character, catalog, excluded: new HashSet<ContentReference>());
        var unmet = new List<(ContentReference Content, Diagnostic Diagnostic)>();
        foreach (var restricted in first.Active.Where(a => a.Revision.Effects.OfType<RestrictionEffect>().Any(IsPrerequisite)))
        {
            var without = Calculate(character, catalog, excluded: new HashSet<ContentReference> { restricted.Revision.Reference });
            foreach (var failed in Unmet(restricted.Revision, restricted.Revision.Effects.OfType<RestrictionEffect>().Where(IsPrerequisite), without.Sheet, "without it", "Its effects are not applied."))
                unmet.Add((restricted.Revision.Reference, failed));
        }
        var final = unmet.Count == 0 ? first : Calculate(character, catalog, excluded: unmet.Select(u => u.Content).ToHashSet());

        // D04: multiclass prerequisites are checked once the character has two or more classes, against the sheet as it is
        // (a class's own features count: its levels are already taken). Unmet ones warn; the class stays applied.
        var multiclass = new List<Diagnostic>();
        if (character.Classes.Count >= 2)
        {
            foreach (var item in final.Active.Where(a => a.Revision.Effects.OfType<RestrictionEffect>().Any(IsMulticlassPrerequisite)))
            {
                multiclass.AddRange(Unmet(item.Revision, item.Revision.Effects.OfType<RestrictionEffect>().Where(IsMulticlassPrerequisite), final.Sheet, "",
                    "Multiclassing into or out of it needs it; the class stays applied, so check the character.", "restriction.multiclass-unmet"));
            }
        }
        if (unmet.Count == 0 && multiclass.Count == 0)
            return final.Sheet;
        return final.Sheet with { Diagnostics = [.. unmet.Select(u => u.Diagnostic), .. multiclass, .. final.Sheet.Diagnostics] };
    }

    private static bool IsEvaluated(RestrictionEffect restriction) =>
        restriction.Automation == AutomationStatus.Automatic && restriction.Timing == EffectTiming.Always;

    private static bool IsPrerequisite(RestrictionEffect restriction) => IsEvaluated(restriction) && restriction.Multiclass != true;

    private static bool IsMulticlassPrerequisite(RestrictionEffect restriction) => IsEvaluated(restriction) && restriction.Multiclass == true;

    /// <summary>
    /// The restrictions of <paramref name="revision"/> that <paramref name="sheet"/> does not meet. Restrictions without a
    /// group each must be met; within a group (content v5), meeting any one is enough, and an unmet group is reported once.
    /// </summary>
    private static IEnumerable<Diagnostic> Unmet(
        ContentRevision revision, IEnumerable<RestrictionEffect> restrictions, CharacterSheet sheet, string basis, string consequence, string code = "restriction.unmet")
    {
        var on = basis.Length > 0 ? $" {basis}" : "";
        foreach (var group in restrictions.GroupBy(r => r.Group))
        {
            var failed = new List<(RestrictionEffect Restriction, string Label, int Actual)>();
            var met = false;
            foreach (var restriction in group)
            {
                if (!SpecIndex.TryGetValue(restriction.Field, out var index))
                {
                    yield return new(
                        "effect.unknown-target",
                        $"'{revision.Name}' restriction '{restriction.Id}' checks '{restriction.Field}', which is not a calculated field; the content is not applied.",
                        revision.Reference, restriction.Id);
                    continue;
                }
                if (IsV8Field(restriction.Field) && IgnoresV8(revision))
                {
                    // As in an older build, where the field does not exist: the content is not applied.
                    yield return new(
                        "effect.schema-field-ignored",
                        $"'{revision.Name}' restriction '{restriction.Id}' checks '{restriction.Field}', a content schema v8 field, but the revision declares v{revision.SchemaVersion}; the content is not applied.",
                        revision.Reference, restriction.Id);
                    continue;
                }
                var actual = sheet.Field(restriction.Field).Value;
                if (actual >= restriction.Minimum)
                    met = true;
                else
                    failed.Add((restriction, Specs[index].Label, actual));
            }
            if (group.Key is null)
            {
                foreach (var (restriction, label, actual) in failed)
                    yield return new(code, $"'{revision.Name}' requires {label} {restriction.Minimum} or higher; this character has {actual}{on}. {consequence}", revision.Reference, restriction.Id);
            }
            else if (!met && failed.Count > 0)
            {
                var options = string.Join(" or ", failed.Select(f => $"{f.Label} {f.Restriction.Minimum}"));
                var has = string.Join(", ", failed.Select(f => $"{f.Label} {f.Actual}"));
                yield return new(code, $"'{revision.Name}' requires {options} or higher; this character has {has}{on}. {consequence}", revision.Reference, failed[0].Restriction.Id);
            }
        }
    }

    private sealed record Calculation(CharacterSheet Sheet, IReadOnlyList<ActiveContent> Active);

    /// <param name="excluded">Revisions left out (restriction checks); anything they would grant or offer is left out with them.</param>
    private static Calculation Calculate(Character character, IContentCatalog catalog, IReadOnlySet<ContentReference> excluded)
    {
        var policy = RulesFamilies.Get(character.RulesFamily);
        var family = character.RulesFamily;
        var diagnostics = new List<Diagnostic>();
        var resolved = ResolveActiveContent(character, catalog, policy, diagnostics, excluded);
        var active = resolved.Active;
        var warnings = Specs.ToDictionary(s => s.Id, _ => new List<Diagnostic>(), StringComparer.Ordinal);

        foreach (var item in active)
        {
            foreach (var effect in item.Revision.Effects.OfType<UnknownEffect>())
            {
                diagnostics.Add(new(
                    "effect.unsupported",
                    $"'{item.Revision.Name}' effect '{effect.Id}' of type '{effect.Type}' is not automated; its text is kept for reference.",
                    item.Revision.Reference,
                    effect.Id));
            }
            if (IgnoresV8(item.Revision))
            {
                foreach (var effect in item.Revision.Effects)
                {
                    var field = effect switch
                    {
                        ModifierEffect { WhileArmored: not null } => "whileArmored",
                        ArmorEffect a when a.Strength is not null || a.StealthDisadvantage is not null => "armor strength or stealthDisadvantage",
                        RollEffect { Bonus: not null } => "a roll bonus",
                        _ => null,
                    };
                    if (field is not null)
                        diagnostics.Add(V8FieldIgnored(item.Revision, effect, field));
                }
            }
        }

        // Fields with an effect the calculator could not apply (not automatic, invalid or disabled): the user may need to
        // account for it by hand, so the field and its dependents are only assisted.
        var manual = new HashSet<string>(StringComparer.Ordinal);
        var modifiers = CollectModifiers(active, character, policy, diagnostics, warnings, manual);
        var weaponProficiencies = new Dictionary<string, Proficiency>(StringComparer.Ordinal);
        var armorTraining = new Dictionary<string, Proficiency>(StringComparer.Ordinal);
        var unseenArmor = new Dictionary<string, Proficiency>(StringComparer.Ordinal);
        var proficiencies = CollectProficiencies(active, character, diagnostics, manual, content => GateLevel(content, character, resolved.ClassLevels), weaponProficiencies, armorTraining, unseenArmor);
        // Every class the character records, not only those that resolved: a missing, unsupported or wrong-family class
        // has unknown training, so it turns the check off too. So does an assisted or conditional armor grant on any
        // content (a feat, species or item): the calculator cannot tell what it gives.
        var classes = character.Classes.Where(e => e.Level > 0).ToList();
        var checkTraining = unseenArmor.Count == 0 && classes.Count > 0 && classes.All(e =>
            resolved.Classes.FirstOrDefault(c => c.Content.Revision.Reference == e.Class) is { } info && RecordsArmorTraining(info.Content.Revision));
        var worn = AddArmor(active, modifiers, diagnostics, armorTraining, checkTraining, policy, warnings);
        RemoveCycles(modifiers, diagnostics, warnings, manual);
        var order = TopologicalOrder(modifiers);
        var casters = CollectCasters(active, character, resolved.ClassLevels, diagnostics);

        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        var ownSteps = new Dictionary<string, List<Step>>(StringComparer.Ordinal);
        var results = new Dictionary<string, (int Value, int Computed, FieldOverride? Override)>(StringComparer.Ordinal);
        foreach (var id in order)
        {
            var spec = Specs[SpecIndex[id]];
            var steps = new List<Step>();
            var context = new BaseContext(character, family, values, proficiencies, resolved.Classes, warnings[id], manual, casters);
            var value = spec.Base(context, steps);
            value = ApplyModifiers(id, value, modifiers.Where(m => m.Effect.Target == id).ToList(), character, resolved.ClassLevels, values, family, steps, warnings[id], manual);
            value = Bound(spec, value, steps, warnings[id], family);

            var computed = value;
            var fieldOverride = character.Overrides.LastOrDefault(o => o.Field == id);
            if (fieldOverride is not null)
            {
                value = fieldOverride.Value;
                var reason = string.IsNullOrWhiteSpace(fieldOverride.Reason) ? "" : $": {fieldOverride.Reason}";
                steps.Add(new(id, "override", $"User override (computed value {computed}){reason}", fieldOverride.Value, value, new(TraceOriginKind.Override, family)));
            }
            values[id] = value;
            ownSteps[id] = steps;
            results[id] = (value, computed, fieldOverride);
        }
        ArmorRequirements(worn, values, warnings);

        IReadOnlyList<string> casterReads = casters.Count == 0 ? [] : [FieldIds.ProficiencyBonus, FieldIds.Modifier(casters[0].Effect.Ability)];
        var actualReads = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [FieldIds.SpellAttack] = casterReads,
            [FieldIds.SpellSaveDc] = casterReads,
        };
        var fields = Specs.Select(spec =>
        {
            var closure = Closure(spec.Id, modifiers, actualReads);
            return new DerivedValue(
                spec.Id,
                spec.Label,
                results[spec.Id].Value,
                results[spec.Id].Computed,
                Flatten(closure, ownSteps, order),
                // A field explains itself with its inputs' warnings too, e.g. an ignored Dex increase explains initiative.
                [.. order.Where(closure.Contains).SelectMany(id => warnings[id]).Distinct()],
                closure.Any(manual.Contains) ? AutomationStatus.Assisted : AutomationStatus.Automatic,
                results[spec.Id].Override,
                spec.Units);
        }).ToList();

        var resources = CollectResources(active, character, resolved.ClassLevels, values, family);
        var rollBonuses = RollBonuses(active, character, resolved.ClassLevels, values, diagnostics);
        var scoped = diagnostics.Concat(warnings.Values.SelectMany(w => w)).Concat(resources.SelectMany(r => r.Warnings)).Where(d => d.Content is not null).Distinct().ToList();
        // A roll's cost is a number of uses, so never negative.
        int? Cost(ActiveContent item, string source) =>
            Formula.TryParse(source, AllowsScales(item.Revision), out var formula, out _) && formula!.TryEvaluate(id => Resolve(id, item, character, resolved.ClassLevels, values, []), out var value, out _)
                ? Math.Max(value, 0)
                : null;
        var features = active.Select(item => Feature(item, family, [.. scoped.Where(d => d.Content == item.Revision.Reference)], Cost, rollBonuses)).ToList();
        var toggles = active
            .SelectMany(item => item.Revision.Effects.OfType<ToggleEffect>().Where(t => t.Automation != AutomationStatus.Reference).Select(t => new ToggleValue(
                item.Revision.Reference, item.Revision.Name, t.Id, t.ToggleId, t.Label, character.Play.IsOn(item.Revision.ContentId, t.ToggleId), t.ResourceId, t.Text)))
            .ToList();
        var maximum = values[FieldIds.HitPoints];
        var hitPoints = new HitPointState(maximum, Math.Clamp(character.Play.CurrentHitPoints ?? maximum, 0, Math.Max(maximum, 0)), character.Play.TemporaryHitPoints);
        var hitDice = resolved.Classes
            .Where(c => c.Die is not null && c.Level > 0)
            .GroupBy(c => c.Die!.Die)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var total = g.Sum(c => c.Level);
                var spent = character.Play.HitDiceSpentOf(g.Key);
                return new HitDiceValue(g.Key, total, spent, Math.Max(total - spent, 0), [.. g.Select(c => c.Content.Revision.Name)]);
            })
            .ToList();

        var casterNumbers = new CasterNumbers(modifiers, ownSteps, order, fields.ToDictionary(f => f.Field, f => f.Trace, StringComparer.Ordinal));
        var spellcasting = SpellcastingEntries(casters, character, catalog, resolved.ClassLevels, values, family, diagnostics, casterNumbers);
        SlotValue Slot(int level, string field, int spent)
        {
            var maximum = Math.Max(values[field], 0);
            return new(level, maximum, spent, Math.Max(maximum - spent, 0), field);
        }
        var spellSlots = Enumerable.Range(1, SpellcastingEffect.MaxSpellLevel)
            .Where(level => values[FieldIds.SpellSlots(level)] > 0 || character.Play.SlotsSpentOf(level) > 0)
            .Select(level => Slot(level, FieldIds.SpellSlots(level), character.Play.SlotsSpentOf(level)))
            .ToList();
        var pactCaster = casters.FirstOrDefault(c => c.Effect.SlotKind == SpellSlotKind.PactMagic);
        var pactSlots = values[FieldIds.PactSlots] > 0 || character.Play.PactSlotsSpent > 0
            ? Slot(pactCaster is null ? 0 : PactSlotLevel(Row(pactCaster.Effect.Slots, pactCaster.ClassLevel)), FieldIds.PactSlots, character.Play.PactSlotsSpent)
            : null;

        var attacks = CollectAttacks(active, values, family, weaponProficiencies);

        var scales = resolved.ClassLevels.Scales
            .Where(s => resolved.ClassLevels.ContainsKey(s.Key.Class))
            .Select(s =>
            {
                var classLevel = resolved.ClassLevels[s.Key.Class];
                var className = active.FirstOrDefault(a => a.Revision.Reference == s.Key.Class)?.Revision.Name ?? "";
                return new ScaleValue(s.Key.Class, className, s.Value.Content.Revision.Reference, s.Key.ScaleId, s.Value.Effect.Label, classLevel, s.Value.Effect.Values[Math.Clamp(classLevel, 1, s.Value.Effect.Values.Count) - 1]);
            })
            .ToList();
        return new(new CharacterSheet(
            character.Id, family, fields, diagnostics, resolved.Choices, [.. active.Select(a => a.Revision.Reference)], resources, features, hitPoints, hitDice,
            spellcasting, spellSlots, pactSlots, attacks, toggles, scales.Count > 0 ? scales : null), active);
    }

    /// <summary>
    /// Content v8 (M2.2): the attack count is at least 1, and the critical range is a d20 roll, 1 to 20. Content that takes
    /// either outside that is bounded, with a trace step and an <c>effect.out-of-range</c> warning naming the last effect
    /// that changed the field. Overrides are not bounded: they are the player's value.
    /// </summary>
    private static int Bound(FieldSpec spec, int value, List<Step> steps, List<Diagnostic> warnings, string family)
    {
        var (minimum, maximum) = spec.Id switch
        {
            FieldIds.Attacks => (1, int.MaxValue),
            FieldIds.CriticalRange => (1, 20),
            _ => (int.MinValue, int.MaxValue),
        };
        if (value >= minimum && value <= maximum)
            return value;
        var bounded = Math.Clamp(value, minimum, maximum);
        var cause = steps.LastOrDefault(s => s.Origin.Content is not null)?.Origin;
        var range = maximum == int.MaxValue ? $"at least {minimum}" : $"{minimum} to {maximum}";
        warnings.Add(new(
            "effect.out-of-range",
            $"{spec.Label} would be {value}{(cause is null ? "" : $" after '{cause.ContentName}'")}; it is {range}, so {bounded} is used.",
            cause?.Content,
            cause?.EffectId));
        steps.Add(new(spec.Id, "bound", $"Bounded to {range} ({value} is out of range)", null, bounded, cause ?? new(TraceOriginKind.RulesPolicy, family)));
        return bounded;
    }

    // ---- attacks (content schema v5; SPEC C-02, C-04) --------------------------------------------------------

    private static List<AttackEntry> CollectAttacks(List<ActiveContent> active, Dictionary<string, int> values, string family, Dictionary<string, Proficiency> weapons)
    {
        var attacks = new List<AttackEntry>();
        var rules = new TraceOrigin(TraceOriginKind.RulesPolicy, family);
        foreach (var item in active.Where(a => a.Revision.Kind == ContentKind.Item))
        {
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
                var damageBonus = mod;
                attacks.Add(new(
                    item.Revision.Reference, item.Revision.Name, weapon.Id, weapon.Attack, weapon.Category, ability, toHit,
                    WithBonus(weapon.Damage, damageBonus), weapon.Versatile is { } versatile ? WithBonus(versatile, damageBonus) : null,
                    weapon.DamageType, weapon.Properties, weapon.Range, weapon.Mastery, proficiency is not null, automation, trace, warnings,
                    ContentOrigin(family, item, weapon)));
            }
        }
        return attacks;
    }

    /// <summary>Dice plus a flat modifier, as a dice expression: <c>1d8+3</c>, <c>1d8-1</c>, or <c>1d8</c> for 0.</summary>
    internal static string WithBonus(string dice, int bonus) =>
        bonus switch
        {
            0 => dice,
            > 0 => $"{dice}+{bonus}",
            _ => $"{dice}{bonus}",
        };

    // ---- spellcasting (content schema v5, D04) --------------------------------------------------------------

    /// <param name="ClassLevel">The level in the class the spellcasting content belongs to; its tables are read at this row.</param>
    private sealed record CasterInfo(ActiveContent Content, SpellcastingEffect Effect, int ClassLevel);

    /// <summary>
    /// Every active revision with a <c>spellcasting</c> effect that is not reference-only (the first one per revision), in
    /// the order the classes were taken. Spellcasting outside a class, or with tables that are not 20 rows of at most 9
    /// levels, is disabled with a diagnostic (SPEC C-03); validation refuses both on publish.
    /// </summary>
    private static List<CasterInfo> CollectCasters(List<ActiveContent> active, Character character, ClassLevelMap classLevels, List<Diagnostic> diagnostics)
    {
        var casters = new List<CasterInfo>();
        foreach (var item in active)
        {
            var effect = item.Revision.Effects.OfType<SpellcastingEffect>().FirstOrDefault(e => e.Automation != AutomationStatus.Reference);
            if (effect is null)
                continue;
            if (item.ClassRoot is not { } root || !classLevels.TryGetValue(root, out var level))
            {
                diagnostics.Add(new("spellcasting.no-class", $"'{item.Revision.Name}' has spellcasting, but it does not belong to a class the character has levels in; it is not calculated.", item.Revision.Reference, effect.Id));
                continue;
            }
            if (SpellcastingProblem(effect) is { } problem)
            {
                diagnostics.Add(new("spellcasting.table-invalid", $"'{item.Revision.Name}' spellcasting '{effect.Id}': {problem}; it is not calculated.", item.Revision.Reference, effect.Id));
                continue;
            }
            casters.Add(new(item, effect, level));
        }
        var order = character.Classes.Select((c, i) => (c.Class, i)).DistinctBy(p => p.Class).ToDictionary(p => p.Class, p => p.i);
        return [.. casters.OrderBy(c => order.GetValueOrDefault(c.Content.ClassRoot!, int.MaxValue))];
    }

    /// <summary>What is wrong with a spellcasting effect's tables, or <c>null</c>. Shared with content validation.</summary>
    internal static string? SpellcastingProblem(SpellcastingEffect effect)
    {
        if (effect.Slots is null || effect.Slots.Count != Character.MaxLevel || effect.Slots.Any(r => r is null || r.Count > SpellcastingEffect.MaxSpellLevel || r.Any(n => n is < 0 or > 20)))
            return $"the slot table needs {Character.MaxLevel} rows (class levels 1–{Character.MaxLevel}) of at most {SpellcastingEffect.MaxSpellLevel} counts between 0 and 20";
        if (effect.SlotKind == SpellSlotKind.PactMagic && effect.Slots.Any(r => r.Count(n => n > 0) > 1))
            return "Pact Magic slots are all of one level, so each row may have one non-zero count";
        if (effect.Cantrips is { } cantrips && (cantrips.Count != Character.MaxLevel || cantrips.Any(n => n is < 0 or > 50)))
            return $"the cantrips table needs {Character.MaxLevel} counts between 0 and 50";
        if (effect.SpellsTable is { } spells && (spells.Count != Character.MaxLevel || spells.Any(n => n is < 0 or > 100)))
            return $"the spells table needs {Character.MaxLevel} counts between 0 and 100";
        if (effect.SpellsTable is not null && effect.SpellsFormula is not null)
            return "give the number of spells as a table or a formula, not both";
        if (string.IsNullOrWhiteSpace(effect.SpellList))
            return "a spell list key is required";
        return null;
    }

    /// <summary>The slot counts at <paramref name="classLevel"/>, padded to 9 spell levels.</summary>
    private static IReadOnlyList<int> Row(IReadOnlyList<IReadOnlyList<int>> table, int classLevel)
    {
        var row = table[Math.Clamp(classLevel, 1, Character.MaxLevel) - 1];
        return [.. Enumerable.Range(0, SpellcastingEffect.MaxSpellLevel).Select(i => i < row.Count ? row[i] : 0)];
    }

    private static int PactSlotLevel(IReadOnlyList<int> row) => row.Select((n, i) => (n, i + 1)).Where(p => p.n > 0).Select(p => p.Item2).DefaultIfEmpty(0).Max();

    /// <summary>What a secondary caster's attack bonus and save DC need from the sheet calculation (M2.1).</summary>
    /// <param name="FieldTraces">The finished sheet fields' traces; the primary caster's numbers are those fields.</param>
    private sealed record CasterNumbers(
        List<Modifier> Modifiers, Dictionary<string, List<Step>> OwnSteps, List<string> Order, Dictionary<string, IReadOnlyList<TraceEntry>> FieldTraces);

    /// <summary>
    /// A secondary caster's spell attack bonus or save DC: the same base as <see cref="CasterBase"/> with its own ability,
    /// then every modifier of the sheet field, in ADR-003 order. The trace starts with its inputs' steps, like a field's.
    /// </summary>
    private static (int Value, IReadOnlyList<TraceEntry> Trace) SecondaryCasterNumber(
        CasterInfo caster, string field, int constant, string description, Character character, ClassLevelMap classLevels,
        Dictionary<string, int> values, string family, CasterNumbers numbers, List<Diagnostic> warnings)
    {
        var (content, effect, _) = caster;
        var modifier = FieldIds.Modifier(effect.Ability);
        var mod = values[modifier];
        var pb = values[FieldIds.ProficiencyBonus];
        var steps = new List<Step>();
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, description, AbilityNames[effect.Ability]);
        var value = constant + pb + mod;
        steps.Add(new(field, "base", $"{text}, from {Describe(content)}", constant, value, ContentOrigin(family, content, effect), [new(FieldIds.ProficiencyBonus, pb), new(modifier, mod)]));
        var applying = numbers.Modifiers.Where(m => m.Effect.Target == field).ToList();
        value = ApplyModifiers(field, value, applying, character, classLevels, values, family, steps, warnings, new HashSet<string>(StringComparer.Ordinal));

        var inputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var read in applying.SelectMany(m => m.ReadsFields).Append(FieldIds.ProficiencyBonus).Append(modifier))
            inputs.UnionWith(Closure(read, numbers.Modifiers, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)));
        inputs.Remove(field);
        var trace = Flatten(inputs, numbers.OwnSteps, numbers.Order);
        foreach (var step in steps)
            trace.Add(new(trace.Count + 1, step.Operation, step.Description, step.Amount, step.Result, step.Origin, step.Field, step.Inputs));
        return (value, trace);
    }

    private static List<SpellcastingEntry> SpellcastingEntries(
        List<CasterInfo> casters, Character character, IContentCatalog catalog, ClassLevelMap classLevels, Dictionary<string, int> values, string family, List<Diagnostic> diagnostics,
        CasterNumbers numbers)
    {
        var entries = new List<SpellcastingEntry>();
        for (var i = 0; i < casters.Count; i++)
        {
            var (content, effect, level) = casters[i];
            var warnings = new List<Diagnostic>();
            var row = Row(effect.Slots, level);
            int? cantrips = effect.Cantrips?[level - 1];
            int? allowed = effect.SpellsTable?[level - 1];
            if (effect.SpellsFormula is { } source)
            {
                if (Formula.TryParse(source, AllowsScales(content.Revision), out var formula, out var failure)
                    && formula!.TryEvaluate(id => Resolve(id, content, character, classLevels, values, []), out var value, out failure))
                {
                    allowed = Math.Max(value, 0);
                }
                else
                {
                    warnings.Add(InvalidFormula(content.Revision, effect, failure!));
                }
            }

            var casterSpells = character.Spells.Where(s => s.Caster == content.Revision.ContentId).ToList();
            var spells = new List<SpellEntry>();
            var highest = effect.SlotKind == SpellSlotKind.PactMagic ? PactSlotLevel(row) : row.Select((n, x) => n > 0 ? x + 1 : 0).Max();
            foreach (var known in casterSpells)
            {
                if (Spell(known, catalog, character, family, effect, highest, warnings) is { } entry)
                    spells.Add(entry);
            }
            var cantripCount = spells.Count(s => s.Level == 0);
            var readyCount = spells.Count(s => s.Level > 0 && (s.Prepared || effect.Preparation == SpellPreparation.Known));
            if (cantrips is { } maxCantrips && cantripCount > maxCantrips)
                warnings.Add(new("spells.too-many-cantrips", $"'{content.Revision.Name}' knows {maxCantrips} cantrip(s) at level {level}; {cantripCount} are recorded.", content.Revision.Reference, effect.Id));
            if (allowed is { } maxSpells && readyCount > maxSpells)
            {
                var what = effect.Preparation == SpellPreparation.Known ? "knows" : "prepares";
                warnings.Add(new("spells.too-many", $"'{content.Revision.Name}' {what} {maxSpells} spell(s) at level {level}; {readyCount} are recorded.", content.Revision.Reference, effect.Id));
            }

            var primary = i == 0;
            var (attack, attackTrace) = primary
                ? (values[FieldIds.SpellAttack], numbers.FieldTraces[FieldIds.SpellAttack])
                : SecondaryCasterNumber(casters[i], FieldIds.SpellAttack, 0, "Spell attack bonus = proficiency bonus + {0} modifier", character, classLevels, values, family, numbers, warnings);
            var (saveDc, saveDcTrace) = primary
                ? (values[FieldIds.SpellSaveDc], numbers.FieldTraces[FieldIds.SpellSaveDc])
                : SecondaryCasterNumber(casters[i], FieldIds.SpellSaveDc, 8, "Spell save DC = 8 + proficiency bonus + {0} modifier", character, classLevels, values, family, numbers, warnings);
            entries.Add(new(
                content.Revision.Reference, content.Revision.Name, effect.Id, level, effect.Ability,
                attack, saveDc,
                effect.Preparation, effect.SpellList, effect.SlotKind, row, cantrips, allowed, primary,
                ContentOrigin(family, content, effect), spells, warnings, attackTrace, saveDcTrace));
        }
        var casterIds = casters.Select(c => c.Content.Revision.ContentId).ToHashSet();
        foreach (var orphan in character.Spells.Where(s => !casterIds.Contains(s.Caster)))
            diagnostics.Add(new("spells.caster-missing", $"A spell is recorded for caster {orphan.Caster}, which gives this character no spellcasting; it is not listed.", orphan.Spell));
        return entries;
    }

    /// <summary>One recorded spell, or <c>null</c> (with a warning) when it cannot be used at all.</summary>
    private static SpellEntry? Spell(
        KnownSpell known, IContentCatalog catalog, Character character, string family, SpellcastingEffect caster, int highest, List<Diagnostic> warnings)
    {
        var revision = catalog.FindRevision(known.Spell);
        string? refused = revision switch
        {
            null => "is not available",
            { Status: not RevisionStatus.Published } => "is not published",
            { SchemaVersion: > ContentRevision.CurrentSchemaVersion } => "needs a newer version of TomeStack",
            _ when !revision.RulesFamilies.Contains(character.RulesFamily) => $"supports {string.Join(", ", revision.RulesFamilies)}, not {character.RulesFamily}",
            _ when revision.Kind != ContentKind.Spell || !revision.Effects.OfType<SpellEffect>().Any() => "is not a spell",
            _ => null,
        };
        if (refused is not null)
        {
            warnings.Add(new("spells.unusable", $"Spell {revision?.Name ?? known.Spell.RevisionId.ToString()} {refused}; it is not listed.", known.Spell));
            return null;
        }
        var data = revision!.Effects.OfType<SpellEffect>().First();
        var diagnostics = new List<Diagnostic>();
        if (!data.Lists.Contains(caster.SpellList, StringComparer.Ordinal))
            diagnostics.Add(new("spells.not-on-list", $"'{revision.Name}' is not on the {caster.SpellList} spell list. Keep it only if a feature adds it.", known.Spell));
        if (data.Level > 0 && data.Level > highest)
            diagnostics.Add(new("spells.level-too-high", $"'{revision.Name}' is a level {data.Level} spell; this caster has slots up to level {highest}.", known.Spell));
        warnings.AddRange(diagnostics);
        var source = catalog.FindSource(revision.Provenance.SourceId);
        var origin = new TraceOrigin(TraceOriginKind.Content, family, revision.Reference, revision.Name, data.Id, source?.Id, source?.Title, revision.Provenance.Page);
        return new(
            revision.Reference, revision.Name, data.Level, known.Prepared || caster.Preparation == SpellPreparation.Known, revision.Summary, data.Text,
            data.School, data.CastingTime, data.Range, data.Components, data.Duration, data.Concentration, data.Ritual, data.Attack, data.Save, data.Dice,
            origin, diagnostics);
    }

    // ---- resources and features (M2 item 2) -----------------------------------------------------------------

    /// <summary>
    /// Every resource of every active revision (the first definition of a resource id per revision). The maximum is the
    /// formula evaluated like a modifier in the content's context; a reference-only resource is listed with no maximum,
    /// and a failing formula disables only that resource, with a warning (SPEC C-03).
    /// </summary>
    private static List<ResourceValue> CollectResources(
        List<ActiveContent> active, Character character, ClassLevelMap classLevels, Dictionary<string, int> values, string family)
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
                var trace = new List<TraceEntry>();
                if (effect.Automation == AutomationStatus.Reference)
                {
                    trace.Add(new(1, "base", $"Reference only: {Describe(item)} does not track this resource; track it by hand", null, 0, origin));
                }
                else if (!Formula.TryParse(effect.Maximum, AllowsScales(revision), out var formula, out var parseError))
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
                    }
                    else
                    {
                        warnings.Add(InvalidFormula(revision, effect, error!));
                    }
                }
                var automation = maximum is null
                    ? (effect.Automation == AutomationStatus.Reference ? AutomationStatus.Reference : AutomationStatus.Assisted)
                    : effect.Automation;
                resources.Add(new(
                    revision.Reference, revision.Name, effect.Id, effect.ResourceId, effect.Label, maximum, spent,
                    maximum is { } max ? Math.Max(max - spent, 0) : null, trace, warnings, automation, recoveries, effect.Text));
            }
        }
        return resources;
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
        if (Formula.TryParse(recovery.Amount, AllowsScales(item.Revision), out var formula, out failure)
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
                if (Formula.TryParse(source, AllowsScales(item.Revision), out var formula, out failure)
                    && formula!.TryEvaluate(id => Resolve(id, item, character, classLevels, values, []), out var value, out failure))
                    bonuses[(item.Revision.Reference, roll.Id)] = value;
                else
                    diagnostics.Add(InvalidFormula(item.Revision, roll, failure!));
            }
        }
        return bonuses;
    }

    private static FeatureEntry Feature(
        ActiveContent item, string family, IReadOnlyList<Diagnostic> diagnostics, Func<ActiveContent, string, int?> cost, Dictionary<(ContentReference, string), int> bonuses)
    {
        var revision = item.Revision;
        var effects = revision.Effects.Select(e => e switch
        {
            RollEffect roll => new FeatureEffect(
                e.Id, e.Type, e.Automation, e.Text, roll.Label, roll.Dice, roll.ResourceId, roll.Activation,
                roll.ResourceContent, roll.Cost is { } spend ? cost(item, spend) : roll.ResourceId is null ? null : 1, roll.VariableCost == true,
                bonuses.TryGetValue((revision.Reference, roll.Id), out var bonus) ? bonus : null),
            ResourceEffect resource => new FeatureEffect(e.Id, e.Type, e.Automation, e.Text, resource.Label, ResourceId: resource.ResourceId),
            RecoveryEffect recovery => new FeatureEffect(e.Id, e.Type, e.Automation, e.Text, ResourceId: recovery.ResourceId),
            _ => new FeatureEffect(e.Id, e.Type, e.Automation, e.Text),
        }).ToList();
        var automation = effects.Count == 0 ? AutomationStatus.Reference : effects.Max(e => e.Automation);
        if (automation == AutomationStatus.Automatic && diagnostics.Any(d => d.EffectId is not null))
            automation = AutomationStatus.Assisted;
        var via = item.GrantedBy is { } by ? $"granted by {by.Kind.ToString().ToLowerInvariant()} '{by.Name}'"
            : item.ChosenFrom is { } chooser ? $"chosen from {chooser.Kind.ToString().ToLowerInvariant()} '{chooser.Name}'"
            : null;
        var origin = new TraceOrigin(TraceOriginKind.Content, family, revision.Reference, revision.Name, null, item.Source.Id, item.Source.Title, revision.Provenance.Page);
        return new(revision.Reference, revision.Name, revision.Kind, revision.Summary, via, origin, automation, effects, diagnostics);
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
            _ when FormulaIdentifiers.FieldFor(identifier) is { } read && values.TryGetValue(read, out var v) => v,
            _ => null,
        };
        if (resolved is { } r)
            inputs.Add(new(identifier, r));
        return resolved;
    }

    // ---- content resolution ---------------------------------------------------------------------------------

    /// <param name="GrantedBy">Set when a <c>grant</c> effect of another active revision brought this one in.</param>
    /// <param name="ClassRoot">
    /// The class this content belongs to: itself for a class, or the class that granted it or offered the choice it was
    /// chosen from. It gives <c>CLASS_LEVEL</c> and the level that gates its grants and choices.
    /// </param>
    /// <param name="ChosenFrom">Set when the character picked this revision for a choice offered by another active revision.</param>
    /// <param name="Origin">
    /// The origin content (species or background) this revision counts as for <see cref="RulesFamilyPolicy"/>: its own
    /// kind for species and background, and inherited along any chain of features granted or chosen from origin
    /// content. Feats, classes and other kinds start no origin and pass none on.
    /// </param>
    private sealed record ActiveContent(
        ContentRevision Revision, SourceRecord Source, ContentRevision? GrantedBy = null, ContentReference? ClassRoot = null, ContentRevision? ChosenFrom = null,
        ContentKind? Origin = null)
    {
        /// <summary>Pins, classes and chosen content are roots: their grants are followed (one level).</summary>
        public bool IsRoot => GrantedBy is null;
    }

    /// <summary>A class the character has levels in, with its hit die when the class declares one.</summary>
    private sealed record ClassInfo(ActiveContent Content, int Level, HitDieEffect? Die);

    /// <summary>
    /// The level in each class (keyed by the class revision), and since content v9 each class's scales: the columns its
    /// class revision and its active subclass define (ADR-010), keyed by the class and the scale id.
    /// </summary>
    private sealed class ClassLevelMap : Dictionary<ContentReference, int>
    {
        public Dictionary<(ContentReference Class, string ScaleId), (ActiveContent Content, ScaleEffect Effect)> Scales { get; } = [];
    }

    /// <summary>
    /// Content v9 (ADR-010): the scales of every class and its active subclasses. The class's own columns come first, so
    /// when a subclass repeats a class's scale id, the class's column wins and <c>scale.duplicate</c> says so. Only
    /// automatic scales of a v9 revision with 20 values apply; a subclass pinned outside a class has none.
    /// </summary>
    private static void CollectScales(List<ActiveContent> active, ClassLevelMap classLevels, List<Diagnostic> diagnostics)
    {
        var owners = active
            .Where(a => a.ClassRoot is not null && a.Revision.Kind is (ContentKind.Class or ContentKind.Subclass) && a.Revision.SchemaVersion >= ScaleEffect.SchemaVersion)
            .OrderBy(a => a.Revision.Kind == ContentKind.Class ? 0 : 1);
        foreach (var owner in owners)
        {
            foreach (var scale in owner.Revision.Effects.OfType<ScaleEffect>().Where(s => s.Automation == AutomationStatus.Automatic))
            {
                if (scale.Values.Count != Character.MaxLevel || !ScaleEffect.IsValidScaleId(scale.ScaleId))
                {
                    // Validation refuses this on publish and import; stored content from elsewhere is isolated here (SPEC C-03).
                    diagnostics.Add(new("scale.invalid", $"'{owner.Revision.Name}' scale '{scale.Id}' needs a valid scale id and {Character.MaxLevel} values; it is ignored.", owner.Revision.Reference, scale.Id));
                    continue;
                }
                if (!classLevels.Scales.TryAdd((owner.ClassRoot!, scale.ScaleId), (owner, scale)))
                {
                    var kept = classLevels.Scales[(owner.ClassRoot!, scale.ScaleId)].Content.Revision.Name;
                    diagnostics.Add(new("scale.duplicate", $"'{owner.Revision.Name}' defines scale '{scale.ScaleId}', which '{kept}' already defines for this class; the first one is used.", owner.Revision.Reference, scale.Id));
                }
            }
        }
    }

    private sealed record ResolvedContent(
        List<ActiveContent> Active, List<ClassInfo> Classes, ClassLevelMap ClassLevels, List<ChoiceStatus> Choices);

    /// <summary>
    /// Pins and classes first. Then, in a worklist: content granted by a root (one level deep only: granted content's own
    /// content grants are not followed, so user content cannot create chains or loops), and the character's selections
    /// for every choice an active revision offers. Chosen content is a root, like a pin. A grant or choice with a
    /// <c>level</c> applies from that class level (character level outside a class). Every revision is admitted once, so
    /// selections cannot loop. Cross-family use needs a recorded exception (B06), which only pins can have.
    /// </summary>
    private static ResolvedContent ResolveActiveContent(
        Character character, IContentCatalog catalog, RulesFamilyPolicy policy, List<Diagnostic> diagnostics, IReadOnlySet<ContentReference> excluded)
    {
        var active = new List<ActiveContent>();
        var seen = new HashSet<ContentReference>();
        var classLevels = new ClassLevelMap();
        foreach (var entry in character.Classes)
            classLevels.TryAdd(entry.Class, entry.Level);
        var choices = new List<ChoiceStatus>();
        var answered = new HashSet<(ContentReference, string)>();

        ActiveContent? Admit(
            ContentReference reference, ContentRevision? grantedBy, ContentReference? classRoot = null, ContentRevision? chosenFrom = null, ContentKind? parentOrigin = null)
        {
            if (excluded.Contains(reference))
                return null; // left out by a restriction check; the caller reports why
            var prefix = grantedBy is not null ? $"Granted by '{grantedBy.Name}': " : chosenFrom is not null ? $"Chosen from '{chosenFrom.Name}': " : "";
            var revision = catalog.FindRevision(reference);
            if (revision is null)
            {
                diagnostics.Add(new("content.missing", $"{prefix}Revision {reference.RevisionId} of content {reference.ContentId} is not available.", reference));
                return null;
            }
            if (revision.SchemaVersion is < 1 or > ContentRevision.CurrentSchemaVersion)
            {
                diagnostics.Add(new("content.schema-unsupported", $"{prefix}'{revision.Name}' uses content schema v{revision.SchemaVersion}; this version supports up to v{ContentRevision.CurrentSchemaVersion}. It is not applied.", reference));
                return null;
            }
            if (revision.Status != RevisionStatus.Published)
            {
                diagnostics.Add(new("content.unpublished", $"{prefix}'{revision.Name}' is a {revision.Status.ToString().ToLowerInvariant()} revision and is not active until it is reviewed and published.", reference));
                return null;
            }
            if (!revision.RulesFamilies.Contains(character.RulesFamily))
            {
                // A recorded exception names an exact revision the character pins or chose; granted content follows its granter.
                var exception = grantedBy is null ? character.CrossFamilyExceptions.LastOrDefault(e => e.Content == reference) : null;
                if (exception is null)
                {
                    diagnostics.Add(new("content.rules-family-mismatch", $"{prefix}'{revision.Name}' supports {string.Join(", ", revision.RulesFamilies)}, not {character.RulesFamily}; it is not applied.", reference));
                    return null;
                }
                diagnostics.Add(new(
                    "content.cross-family-exception",
                    $"'{revision.Name}' is written for {string.Join(", ", revision.RulesFamilies)} but is used under {character.RulesFamily} by a recorded exception: {exception.Reason}. It follows {policy.DisplayName} rules.",
                    reference));
            }
            var source = catalog.FindSource(revision.Provenance.SourceId);
            if (source is null)
            {
                diagnostics.Add(new("content.source-missing", $"{prefix}'{revision.Name}' has no known source record; it is not applied.", reference));
                return null;
            }
            var origin = revision.Kind is ContentKind.Species or ContentKind.Background ? revision.Kind
                : revision.Kind == ContentKind.Feature ? parentOrigin
                : null;
            return new(revision, source, grantedBy, revision.Kind == ContentKind.Class ? reference : classRoot, chosenFrom, origin);
        }

        // SRD 5.1: a background (or a feature that comes from one) may not bring in a feat, whether it grants the feat
        // or offers it as a choice.
        bool BackgroundFeatRefused(ActiveContent offering, ContentReference target, string effectId, string verb)
        {
            if (offering.Origin != ContentKind.Background || policy.BackgroundGrantsFeat || catalog.FindRevision(target)?.Kind != ContentKind.Feat)
                return false;
            var what = offering.Revision.Kind == ContentKind.Background ? "background" : $"{offering.Revision.Kind.ToString().ToLowerInvariant()} from a background";
            diagnostics.Add(new(
                "policy.background-feat",
                $"'{offering.Revision.Name}' ({what}) cannot {verb} a feat under {policy.DisplayName}; the {(verb == "grant" ? "grant" : "selection")} is ignored.",
                offering.Revision.Reference,
                effectId));
            return true;
        }

        foreach (var pin in character.Pins.Concat(character.Classes.Select(c => c.Class)))
        {
            if (seen.Add(pin) && Admit(pin, null) is { } item)
                active.Add(item);
        }
        // Equipped items apply like pins (M2 item 4); carried but unequipped items do not.
        foreach (var entry in character.Equipment.Where(e => e.Equipped))
        {
            if (!seen.Add(entry.Item) || Admit(entry.Item, null) is not { } item)
                continue;
            if (item.Revision.Kind != ContentKind.Item)
            {
                diagnostics.Add(new("equipment.not-an-item", $"'{item.Revision.Name}' is equipped but is {item.Revision.Kind.ToString().ToLowerInvariant()} content, not an item; it is not applied.", entry.Item));
                continue;
            }
            active.Add(item);
        }

        var classes = new List<ClassInfo>();
        foreach (var entry in character.Classes)
        {
            var item = active.FirstOrDefault(a => a.Revision.Reference == entry.Class);
            if (item is null)
                continue; // already explained by Admit (missing, draft, wrong family, ...)
            if (item.Revision.Kind != ContentKind.Class)
            {
                diagnostics.Add(new("character.class-not-a-class", $"'{item.Revision.Name}' is recorded as a class level but is {item.Revision.Kind.ToString().ToLowerInvariant()} content; it gives no class levels.", entry.Class));
                continue;
            }
            if (classes.Any(c => c.Content.Revision.Reference == entry.Class))
                continue;
            var hitDie = item.Revision.Effects.OfType<HitDieEffect>().FirstOrDefault(d => d.Automation == AutomationStatus.Automatic);
            if (hitDie is not null && !HitDieEffect.AllowedDice.Contains(hitDie.Die))
            {
                // Validation refuses this on publish and import; stored content from elsewhere is isolated here (SPEC C-03).
                diagnostics.Add(new("class.hit-die-invalid", $"'{item.Revision.Name}' declares a d{hitDie.Die} hit die, which is not one of d6, d8, d10 or d12; it is ignored.", entry.Class, hitDie.Id));
                hitDie = null;
            }
            classes.Add(new(item, entry.Level, hitDie));
        }
        foreach (var pinnedClass in active.Where(a => a.Revision.Kind == ContentKind.Class && !classLevels.ContainsKey(a.Revision.Reference)))
            diagnostics.Add(new("character.class-without-levels", $"'{pinnedClass.Revision.Name}' is pinned as content but no levels are recorded in it, so its level features and hit points do not apply.", pinnedClass.Revision.Reference));

        var referenced = character.AllReferences().ToHashSet();
        foreach (var exception in character.CrossFamilyExceptions.Where(e => !referenced.Contains(e.Content)))
            diagnostics.Add(new("character.exception-unused", $"A cross-family exception is recorded for revision {exception.Content.RevisionId}, which this character does not pin or choose.", exception.Content));

        var pending = new Queue<ActiveContent>(active);
        while (pending.Count > 0)
        {
            var item = pending.Dequeue();
            var revision = item.Revision;
            if (item.IsRoot)
            {
                foreach (var grant in revision.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content))
                {
                    if (grant.Automation != AutomationStatus.Automatic || grant.Timing != EffectTiming.Always)
                        continue;
                    if (grant.Level is { } needed && GateLevel(item, character, classLevels) < needed)
                        continue; // not reached yet: a level-3 feature at class level 2 is simply not there
                    if (!EntryApplies(item, grant.OnlyAs, character))
                        continue; // D04: only for the starting class, or only for a later class
                    if (grant.Content is not { } reference)
                    {
                        diagnostics.Add(new("effect.grant-content-missing", $"'{revision.Name}' effect '{grant.Id}' grants content but names none; it is ignored.", revision.Reference, grant.Id));
                        continue;
                    }
                    // Only feats are restricted: a 2014 background may still grant other content, such as its feature.
                    if (BackgroundFeatRefused(item, reference, grant.Id, "grant"))
                        continue;
                    if (seen.Add(reference) && Admit(reference, revision, item.ClassRoot, parentOrigin: item.Origin) is { } granted)
                    {
                        active.Add(granted);
                        pending.Enqueue(granted);
                        foreach (var nested in granted.Revision.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content))
                            diagnostics.Add(new("grant.nested-ignored", $"'{granted.Revision.Name}' was granted by '{revision.Name}'; its own content grant '{nested.Id}' is not followed (grants are one level deep).", granted.Revision.Reference, nested.Id));
                    }
                }
            }

            foreach (var choice in revision.Effects.OfType<ChoiceEffect>())
            {
                if (choice.Level is { } needed && GateLevel(item, character, classLevels) < needed)
                    continue; // not offered yet, so not unresolved either
                if (!EntryApplies(item, choice.OnlyAs, character))
                    continue; // D04: not offered to this class's entry (starting or later), so not unresolved either
                var key = (revision.Reference, choice.ChoiceId);
                if (!answered.Add(key))
                    continue;
                if (choice.Count < 1)
                {
                    // Validation refuses this on publish and import; a count below 1 would otherwise accept any number of selections.
                    diagnostics.Add(new("choice.invalid-count", $"'{revision.Name}' choice '{choice.ChoiceId}' asks for {choice.Count} selection(s); it is not offered and nothing selected for it is applied.", revision.Reference, choice.Id));
                    continue;
                }
                var selected = character.Choices.LastOrDefault(c => c.Source == revision.Reference && c.ChoiceId == choice.ChoiceId)?.Selected ?? [];
                // Content schema v4: published revisions that name this choice (by content id, any revision of it) are
                // options too, after the declared ones; for example a homebrew subclass for the SRD Barbarian.
                IReadOnlyList<ContentReference> options =
                [
                    .. choice.Options,
                    .. catalog.ChoiceExtensions(revision.ContentId, choice.ChoiceId)
                        .OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.RevisionId)
                        .Select(r => r.Reference)
                        .Where(r => !choice.Options.Contains(r)),
                ];
                var applied = new List<ContentReference>();
                foreach (var option in selected.Distinct())
                {
                    if (!options.Contains(option))
                    {
                        diagnostics.Add(new("choice.invalid-option", $"'{revision.Name}' choice '{choice.ChoiceId}': revision {option.RevisionId} is not one of its options; it is not applied.", option, choice.Id));
                        continue;
                    }
                    if (applied.Count >= choice.Count)
                    {
                        diagnostics.Add(new("choice.too-many", $"'{revision.Name}' choice '{choice.ChoiceId}' allows {choice.Count} selection(s); the extra selection {option.RevisionId} is not applied.", option, choice.Id));
                        continue;
                    }
                    if (BackgroundFeatRefused(item, option, choice.Id, "offer"))
                        continue;
                    // Only an option that is actually active answers the choice: one refused by Admit (wrong family,
                    // missing, draft) leaves it unresolved, with Admit's diagnostic saying why.
                    if (seen.Add(option))
                    {
                        if (Admit(option, null, item.ClassRoot, revision, item.Origin) is not { } chosen)
                            continue;
                        active.Add(chosen);
                        pending.Enqueue(chosen);
                    }
                    else if (!active.Any(a => a.Revision.Reference == option))
                    {
                        continue;
                    }
                    applied.Add(option);
                }
                var resolved = applied.Count >= choice.Count;
                if (!resolved)
                {
                    diagnostics.Add(new(
                        "choice.unresolved",
                        $"'{revision.Name}' offers a choice ('{choice.ChoiceId}') of {choice.Count}; {applied.Count} selected.{(choice.Text is { } text ? $" {text}" : "")}",
                        revision.Reference,
                        choice.Id));
                }
                choices.Add(new(revision.Reference, revision.Name, choice.ChoiceId, choice.Text, choice.Count, options, applied, resolved));
            }
        }

        foreach (var orphan in character.Choices.Where(c => !answered.Contains((c.Source, c.ChoiceId))))
            diagnostics.Add(new("choice.orphaned", $"A selection is recorded for choice '{orphan.ChoiceId}' of revision {orphan.Source.RevisionId}, which is not active or does not offer that choice (yet); it is not applied.", orphan.Source));
        CollectScales(active, classLevels, diagnostics);
        return new(active, classes, classLevels, choices);
    }

    /// <summary>
    /// D04 (content v5 <c>onlyAs</c>): whether a class's grant or choice applies to this class's entry. The starting class is
    /// the first class taken; content outside a class ignores <paramref name="onlyAs"/>.
    /// </summary>
    private static bool EntryApplies(ActiveContent item, ClassEntry? onlyAs, Character character)
    {
        if (onlyAs is null || item.ClassRoot is not { } root)
            return true;
        var starting = character.Classes.Count > 0 && character.Classes[0].Class == root;
        return onlyAs == ClassEntry.StartingClass ? starting : !starting;
    }

    /// <summary>The level that gates <paramref name="content"/>'s grants: its class's level, or the character level.</summary>
    private static int GateLevel(ActiveContent content, Character character, ClassLevelMap classLevels) =>
        content.ClassRoot is { } root ? classLevels.GetValueOrDefault(root) : character.TotalLevel;

    // ---- effects --------------------------------------------------------------------------------------------

    /// <param name="SkipReason">Set when the rules say this modifier does not apply now (for example, Unarmored Defense while armor is worn); it is traced as not used.</param>
    private sealed record Modifier(ActiveContent Content, ModifierEffect Effect, Formula Formula, IReadOnlyList<string> ReadsFields, string? SkipReason = null);

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
        RulesFamilyPolicy policy, Dictionary<string, List<Diagnostic>> warnings)
    {
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
                $"'{content.Revision.Name}' needs Strength {needed}; with Strength {strength} the wearer's speed is 10 feet lower. Adjust speed by hand.",
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

    private static List<Modifier> CollectModifiers(
        List<ActiveContent> active, Character character, RulesFamilyPolicy policy, List<Diagnostic> diagnostics, Dictionary<string, List<Diagnostic>> warnings,
        HashSet<string> manual)
    {
        var modifiers = new List<Modifier>();
        foreach (var item in active)
        {
            var revision = item.Revision;
            foreach (var effect in revision.Effects.OfType<ModifierEffect>())
            {
                // Content v6 (M3 B2): a whileActive modifier that names a toggle of its revision applies while that toggle
                // is on, and simply does not apply while it is off (the rules say so; nothing is left to the player).
                if (effect.Automation == AutomationStatus.Automatic && effect.Timing == EffectTiming.WhileActive && effect.Toggle is { } toggle
                    && revision.Effects.OfType<ToggleEffect>().Any(t => t.ToggleId == toggle))
                {
                    if (!character.Play.IsOn(revision.ContentId, toggle))
                        continue;
                }
                else if (effect.Automation != AutomationStatus.Automatic || effect.Timing != EffectTiming.Always)
                {
                    if (SpecIndex.ContainsKey(effect.Target))
                        manual.Add(effect.Target);
                    continue;
                }
                if (!SpecIndex.ContainsKey(effect.Target))
                {
                    diagnostics.Add(new("effect.unknown-target", $"'{revision.Name}' effect '{effect.Id}' targets '{effect.Target}', which is not a calculated field; it is ignored.", revision.Reference, effect.Id));
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
                if (!Formula.TryParse(effect.Value, AllowsScales(revision), out var formula, out var error))
                {
                    warnings[effect.Target].Add(InvalidFormula(revision, effect, error!));
                    manual.Add(effect.Target);
                    continue;
                }
                var reads = formula!.Identifiers.Select(FormulaIdentifiers.FieldFor).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
                modifiers.Add(new(item, effect, formula, reads));
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

    /// <summary>The keys an armor training grant may name (the <see cref="ArmorCategory"/> values, in lower case).</summary>
    public static IReadOnlyList<string> ArmorTrainingKeys { get; } = [.. Enum.GetValues<ArmorCategory>().Select(c => c.ToString().ToLowerInvariant())];

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

    /// <summary>
    /// Content v9 (ADR-010): <c>SCALE.&lt;id&gt;</c> is a known identifier only in a v9 revision. Below v9 it is an unknown
    /// identifier, as in builds before v9, so a stored revision never calculates differently on this build.
    /// </summary>
    private static bool AllowsScales(ContentRevision revision) => revision.SchemaVersion >= ScaleEffect.SchemaVersion;

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
                if (grant.Grant == GrantKind.Content)
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

    // ---- graph ----------------------------------------------------------------------------------------------

    private static IEnumerable<(string From, string To, Modifier? Via)> Edges(IEnumerable<Modifier> modifiers)
    {
        foreach (var spec in Specs)
        {
            foreach (var dependency in spec.Reads)
                yield return (dependency, spec.Id, null);
        }
        foreach (var modifier in modifiers)
        {
            foreach (var read in modifier.ReadsFields)
                yield return (read, modifier.Effect.Target, modifier);
        }
    }

    /// <summary>
    /// Tarjan SCC. Base edges are acyclic by construction, so every cycle runs through at least one effect edge. Disable
    /// every effect whose edge lies inside a strongly connected component (or reads its own target), except an edge
    /// that base edges already imply (for example a Dex modifier effect reading the Dex score): it adds no
    /// reachability, so it cannot close a cycle, and the graph stays acyclic without it.
    /// </summary>
    private static void RemoveCycles(List<Modifier> modifiers, List<Diagnostic> diagnostics, Dictionary<string, List<Diagnostic>> warnings, HashSet<string> manual)
    {
        var edges = Edges(modifiers).ToList();
        var adjacency = Specs.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to, _) in edges)
            adjacency[from].Add(to);

        var component = StronglyConnectedComponents(adjacency);
        var cyclic = edges
            .Where(e => e.Via is not null && (e.From == e.To || (component[e.From] == component[e.To] && ComponentSize(component, component[e.From]) > 1 && !BaseReaches(e.From, e.To))))
            .Select(e => e.Via!)
            .Distinct()
            .ToList();

        foreach (var modifier in cyclic)
        {
            var members = component.Where(c => c.Value == component[modifier.Effect.Target]).Select(c => c.Key).OrderBy(f => SpecIndex[f]).ToList();
            var cycle = members.Count > 1 ? string.Join(" → ", [.. members, members[0]]) : $"{modifier.Effect.Target} → {modifier.Effect.Target}";
            var revision = modifier.Content.Revision;
            var diagnostic = new Diagnostic(
                "effect.dependency-cycle",
                $"'{revision.Name}' effect '{modifier.Effect.Id}' is disabled: its formula '{modifier.Formula.Source}' reads {string.Join(", ", modifier.ReadsFields)}, which depends on {modifier.Effect.Target} (cycle: {cycle}).",
                revision.Reference,
                modifier.Effect.Id);
            diagnostics.Add(diagnostic);
            warnings[modifier.Effect.Target].Add(diagnostic);
            manual.Add(modifier.Effect.Target);
            modifiers.Remove(modifier);
        }
    }

    private static int ComponentSize(Dictionary<string, int> component, int id) => component.Count(c => c.Value == id);

    /// <summary>True if <paramref name="to"/> depends on <paramref name="from"/> through base inputs alone (a path of length ≥ 1).</summary>
    private static bool BaseReaches(string from, string to)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([from]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var dependent in Specs.Where(s => s.Reads.Contains(current, StringComparer.Ordinal)).Select(s => s.Id))
            {
                if (dependent == to)
                    return true;
                if (seen.Add(dependent))
                    pending.Push(dependent);
            }
        }
        return false;
    }

    private static Dictionary<string, int> StronglyConnectedComponents(Dictionary<string, List<string>> adjacency)
    {
        var index = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var component = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 0;

        // Recursion depth is bounded by the fixed field count (not by user content).
        void Visit(string node)
        {
            indices[node] = lowLinks[node] = index++;
            stack.Push(node);
            onStack.Add(node);
            foreach (var target in adjacency[node])
            {
                if (!indices.ContainsKey(target))
                {
                    Visit(target);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[target]);
                }
                else if (onStack.Contains(target))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], indices[target]);
                }
            }
            if (lowLinks[node] != indices[node])
                return;
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component[member] = next;
            }
            while (member != node);
            next++;
        }

        foreach (var node in adjacency.Keys)
        {
            if (!indices.ContainsKey(node))
                Visit(node);
        }
        return component;
    }

    /// <summary>Kahn's algorithm; ties break by declaration order, so the order is deterministic.</summary>
    private static List<string> TopologicalOrder(IEnumerable<Modifier> modifiers)
    {
        var incoming = Specs.ToDictionary(s => s.Id, _ => 0, StringComparer.Ordinal);
        var outgoing = Specs.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to, _) in Edges(modifiers))
        {
            outgoing[from].Add(to);
            incoming[to]++;
        }
        var ready = new SortedSet<int>(Specs.Where(s => incoming[s.Id] == 0).Select(s => SpecIndex[s.Id]));
        var order = new List<string>();
        while (ready.Count > 0)
        {
            var id = Specs[ready.Min].Id;
            ready.Remove(ready.Min);
            order.Add(id);
            foreach (var target in outgoing[id])
            {
                if (--incoming[target] == 0)
                    ready.Add(SpecIndex[target]);
            }
        }
        return order.Count == Specs.Count
            ? order
            : throw new InvalidOperationException("Dependency cycle survived cycle removal."); // unreachable by construction
    }

    // ---- evaluation -----------------------------------------------------------------------------------------

    private sealed record Step(string Field, string Operation, string Description, int? Amount, int Result, TraceOrigin Origin, IReadOnlyList<TraceInput>? Inputs = null);

    /// <summary>ADR-003 order: highest replace, then bonuses (stack / highest in group), then highest set.</summary>
    private static int ApplyModifiers(
        string field, int value, List<Modifier> modifiers, Character character, ClassLevelMap classLevels,
        Dictionary<string, int> values, string family, List<Step> steps, List<Diagnostic> warnings, HashSet<string> manual)
    {
        var evaluated = new List<(Modifier Modifier, int Amount, List<TraceInput> Inputs)>();
        var skipped = new List<Modifier>();
        foreach (var modifier in modifiers)
        {
            if (modifier.SkipReason is not null)
            {
                skipped.Add(modifier);
                continue;
            }
            var inputs = new List<TraceInput>();
            if (modifier.Formula.TryEvaluate(id => Resolve(id, modifier.Content, character, classLevels, values, inputs), out var amount, out var error))
                evaluated.Add((modifier, amount, inputs));
            else
            {
                warnings.Add(InvalidFormula(modifier.Content.Revision, modifier.Effect, error!));
                manual.Add(field);
            }
        }

        TraceOrigin Origin(Modifier m) => ContentOrigin(family, m.Content, m.Effect);
        string Name(Modifier m) => Describe(m.Content);
        IReadOnlyList<TraceInput>? Inputs(List<TraceInput> i) => i.Count > 0 ? i : null;

        var replacements = evaluated.Where(e => e.Modifier.Effect.Operation == ModifierOperation.Replace).ToList();
        if (replacements.Count > 0)
        {
            var best = replacements.MaxBy(r => r.Amount);
            value = best.Amount;
            steps.Add(new(field, "replace", $"Base replaced by {Name(best.Modifier)}", best.Amount, value, Origin(best.Modifier), Inputs(best.Inputs)));
            foreach (var other in replacements.Where(r => r != best))
                steps.Add(new(field, "ignored", $"Replacement from {Name(other.Modifier)} not used; the highest replacement applies", other.Amount, value, Origin(other.Modifier), Inputs(other.Inputs)));
        }
        foreach (var modifier in skipped)
            steps.Add(new(field, "ignored", $"{Name(modifier)} not used: {modifier.SkipReason}", null, value, Origin(modifier)));

        // Ability scores: increases first (capped at 20 below), then penalties, so the result does not depend on the
        // order of the content (19 + 2 - 2 is 18 either way). A stable sort keeps content order within each group.
        var bonuses = evaluated.Where(e => e.Modifier.Effect.Operation == ModifierOperation.Bonus)
            .OrderBy(e => IsAbilityScoreField(field) && e.Amount < 0)
            .ToList();
        var winners = bonuses
            .Where(b => b.Modifier.Effect.Stacking == StackingRule.HighestInGroup)
            .GroupBy(b => b.Modifier.Effect.StackGroup, StringComparer.Ordinal)
            .Select(g => g.MaxBy(b => b.Amount).Modifier)
            .ToHashSet();
        foreach (var bonus in bonuses)
        {
            if (bonus.Modifier.Effect.Stacking == StackingRule.HighestInGroup && !winners.Contains(bonus.Modifier))
            {
                steps.Add(new(field, "ignored", $"Bonus from {Name(bonus.Modifier)} does not stack with a higher '{bonus.Modifier.Effect.StackGroup}' bonus", bonus.Amount, value, Origin(bonus.Modifier), Inputs(bonus.Inputs)));
                continue;
            }
            // Each formula is bounded, but their number is not: bound the running value too, so it cannot overflow.
            var next = (long)value + bonus.Amount;
            if (Math.Abs(next) > FormulaLimits.MaxMagnitude)
            {
                var revision = bonus.Modifier.Content.Revision;
                warnings.Add(new(
                    "effect.out-of-range",
                    $"'{revision.Name}' effect '{bonus.Modifier.Effect.Id}' is disabled: it would take {field} outside ±{FormulaLimits.MaxMagnitude:0}.",
                    revision.Reference,
                    bonus.Modifier.Effect.Id));
                manual.Add(field);
                steps.Add(new(field, "ignored", $"Bonus from {Name(bonus.Modifier)} not applied; the result would be out of range", bonus.Amount, value, Origin(bonus.Modifier), Inputs(bonus.Inputs)));
                continue;
            }
            // SRD (both families, owner decision 2026-09-27): increases cannot raise an ability score above 20. A bonus
            // stops at 20 (or at the score it started from, if that was already higher); set effects and overrides may
            // exceed it. A higher content-declared maximum (for example 24) is not modeled yet.
            if (IsAbilityScoreField(field) && bonus.Amount > 0 && next > Math.Max(AbilityScoreIncreaseCap, value))
            {
                var capped = Math.Max(AbilityScoreIncreaseCap, value);
                steps.Add(new(field, "add", $"Bonus from {Name(bonus.Modifier)}, capped: increases cannot raise an ability score above {AbilityScoreIncreaseCap} (+{bonus.Amount} would give {next})", capped - value, capped, Origin(bonus.Modifier), Inputs(bonus.Inputs)));
                value = capped;
                continue;
            }
            value = (int)next;
            steps.Add(new(field, "add", $"Bonus from {Name(bonus.Modifier)}", bonus.Amount, value, Origin(bonus.Modifier), Inputs(bonus.Inputs)));
        }

        var sets = evaluated.Where(e => e.Modifier.Effect.Operation == ModifierOperation.Set).ToList();
        if (sets.Count > 0)
        {
            var best = sets.MaxBy(s => s.Amount);
            value = best.Amount;
            steps.Add(new(field, "set", $"Set by {Name(best.Modifier)}", best.Amount, value, Origin(best.Modifier), Inputs(best.Inputs)));
            foreach (var other in sets.Where(s => s != best))
                steps.Add(new(field, "ignored", $"Set from {Name(other.Modifier)} not used; the highest set applies", other.Amount, value, Origin(other.Modifier), Inputs(other.Inputs)));
        }
        return value;
    }

    /// <summary>
    /// The field plus every field it transitively reads (base inputs and effect formulas). <paramref name="actualReads"/>
    /// narrows a spec's static inputs for this character: the spell fields statically read every ability modifier (for
    /// ordering), but depend only on the proficiency bonus and the primary caster's ability.
    /// </summary>
    private static HashSet<string> Closure(string field, List<Modifier> modifiers, IReadOnlyDictionary<string, IReadOnlyList<string>> actualReads)
    {
        var reads = Specs.ToDictionary(
            s => s.Id,
            s => new HashSet<string>(actualReads.TryGetValue(s.Id, out var actual) ? actual : s.Reads, StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var modifier in modifiers)
            reads[modifier.Effect.Target].UnionWith(modifier.ReadsFields);

        var needed = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([field]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (needed.Add(current))
            {
                foreach (var dependency in reads[current])
                    pending.Push(dependency);
            }
        }
        return needed;
    }

    /// <summary>A field's trace: the own steps of every field in its closure, in dependency order, ending with its own.</summary>
    private static List<TraceEntry> Flatten(HashSet<string> needed, Dictionary<string, List<Step>> ownSteps, List<string> order)
    {
        var entries = new List<TraceEntry>();
        foreach (var id in order.Where(needed.Contains))
        {
            foreach (var step in ownSteps[id])
                entries.Add(new(entries.Count + 1, step.Operation, step.Description, step.Amount, step.Result, step.Origin, step.Field, step.Inputs));
        }
        return entries;
    }

    private static string Describe(ActiveContent content)
    {
        var revision = content.Revision;
        var from = content.GrantedBy is { } by ? $" (granted by {by.Kind.ToString().ToLowerInvariant()} '{by.Name}')"
            : content.ChosenFrom is { } chooser ? $" (chosen from {chooser.Kind.ToString().ToLowerInvariant()} '{chooser.Name}')"
            : "";
        return $"{revision.Kind.ToString().ToLowerInvariant()} '{revision.Name}'{from}";
    }

    private static Diagnostic InvalidFormula(ContentRevision revision, Effect effect, FormulaError error) =>
        new("effect.invalid-formula", $"'{revision.Name}' effect '{effect.Id}' is disabled: {error.Message} ({error.Code})", revision.Reference, effect.Id);

    private static TraceOrigin ContentOrigin(string family, ActiveContent content, Effect effect) =>
        new(TraceOriginKind.Content, family, content.Revision.Reference, content.Revision.Name, effect.Id, content.Source.Id, content.Source.Title, content.Revision.Provenance.Page);

    // ---- field definitions ----------------------------------------------------------------------------------

    /// <param name="Warnings">This field's warnings; a base derivation may add to them.</param>
    /// <param name="Manual">Fields that are only assisted; a base derivation that cannot complete adds its field.</param>
    private sealed record BaseContext(
        Character Character, string Family, Dictionary<string, int> Values, Dictionary<string, Proficiency> Proficiencies,
        IReadOnlyList<ClassInfo> Classes, List<Diagnostic> Warnings, HashSet<string> Manual, IReadOnlyList<CasterInfo> Casters);

    private sealed record FieldSpec(string Id, string Label, string Units, IReadOnlyList<string> Reads, Func<BaseContext, List<Step>, int> Base);

    private static List<FieldSpec> BuildSpecs()
    {
        var specs = new List<FieldSpec>();
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var name = AbilityNames[ability];
            var score = FieldIds.Score(ability);
            specs.Add(new(score, $"{name} score", "score", [], (c, steps) =>
            {
                var value = c.Character.BaseAbilities.Get(ability);
                steps.Add(new(score, "base", $"{name} score (character choice)", value, value, new(TraceOriginKind.CharacterChoice, c.Family)));
                return value;
            }));
        }
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var name = AbilityNames[ability];
            var score = FieldIds.Score(ability);
            var modifier = FieldIds.Modifier(ability);
            specs.Add(new(modifier, $"{name} modifier", "modifier", [score], (c, steps) =>
            {
                var input = c.Values[score];
                var value = (int)Math.Floor((input - 10) / 2.0);
                steps.Add(new(modifier, "derive", $"{name} modifier = floor((score - 10) / 2)", input, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(score, input)]));
                return value;
            }));
        }
        specs.Add(new(FieldIds.ProficiencyBonus, "Proficiency bonus", "bonus", [], (c, steps) =>
        {
            var level = c.Character.TotalLevel;
            var value = 2 + ((level - 1) / 4);
            steps.Add(new(FieldIds.ProficiencyBonus, "derive", $"Proficiency bonus by character level {level} = 2 + floor((level - 1) / 4)", level, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(FormulaIdentifiers.Level, level)]));
            return value;
        }));
        foreach (var ability in Enum.GetValues<Ability>())
            specs.Add(Proficient(FieldIds.Save(ability), $"{AbilityNames[ability]} saving throw", ability));
        foreach (var (key, label, ability) in Skills)
            specs.Add(Proficient(FieldIds.Skill(key), label, ability));
        specs.Add(new(FieldIds.Initiative, "Initiative", "modifier", [FieldIds.Modifier(Ability.Dex)], (c, steps) =>
        {
            var value = c.Values[FieldIds.Modifier(Ability.Dex)];
            steps.Add(new(FieldIds.Initiative, "base", "Initiative starts at the Dexterity modifier", value, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(FieldIds.Modifier(Ability.Dex), value)]));
            return value;
        }));
        specs.Add(new(FieldIds.ArmorClass, "Armor Class", "score", [FieldIds.Modifier(Ability.Dex)], (c, steps) =>
        {
            var dex = c.Values[FieldIds.Modifier(Ability.Dex)];
            var value = 10 + dex;
            // The unarmored base. Worn armor and alternatives such as Unarmored Defense are `replace` effects (armor is
            // synthesized from equipped items, M2 item 4); the highest replacement wins, and while armor is worn only the
            // armor replaces it (ADR-003). A shield is a bonus.
            steps.Add(new(FieldIds.ArmorClass, "base", "Armor Class without armor = 10 + Dexterity modifier", 10, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(FieldIds.Modifier(Ability.Dex), dex)]));
            return value;
        }));
        specs.Add(new(FieldIds.HitPoints, "Hit point maximum", "score", [FieldIds.Modifier(Ability.Con)], HitPoints));
        // Content v8 (M2.2). Extra Attack sets the count (highest wins); Improved and Superior Critical lower the range.
        specs.Add(new(FieldIds.Attacks, "Attacks per Attack action", "score", [], (c, steps) =>
        {
            steps.Add(new(FieldIds.Attacks, "base", "One attack when you take the Attack action", 1, 1, new(TraceOriginKind.RulesPolicy, c.Family)));
            return 1;
        }));
        specs.Add(new(FieldIds.CriticalRange, "Weapon critical hit on a d20 roll of at least", "score", [], (c, steps) =>
        {
            steps.Add(new(FieldIds.CriticalRange, "base", "A weapon attack is a critical hit on a roll of 20", 20, 20, new(TraceOriginKind.RulesPolicy, c.Family)));
            return 20;
        }));

        // Spellcasting (content schema v5, D04). Every ability modifier is an input, because the caster's ability is data.
        IReadOnlyList<string> casterInputs = [FieldIds.ProficiencyBonus, .. Enum.GetValues<Ability>().Select(FieldIds.Modifier)];
        specs.Add(new(FieldIds.SpellAttack, "Spell attack bonus", "modifier", casterInputs, (c, steps) => CasterBase(c, steps, FieldIds.SpellAttack, 0, "Spell attack bonus = proficiency bonus + {0} modifier")));
        specs.Add(new(FieldIds.SpellSaveDc, "Spell save DC", "score", casterInputs, (c, steps) => CasterBase(c, steps, FieldIds.SpellSaveDc, 8, "Spell save DC = 8 + proficiency bonus + {0} modifier")));
        for (var level = 1; level <= SpellcastingEffect.MaxSpellLevel; level++)
        {
            var spellLevel = level;
            specs.Add(new(FieldIds.SpellSlots(level), $"Level {level} spell slots", "slots", [], (c, steps) => SlotBase(c, steps, spellLevel)));
        }
        specs.Add(new(FieldIds.PactSlots, "Pact Magic slots", "slots", [], PactBase));
        return specs;
    }

    private static int CasterBase(BaseContext c, List<Step> steps, string field, int constant, string description)
    {
        if (c.Casters.Count == 0)
        {
            steps.Add(new(field, "base", "No spellcasting", 0, 0, new(TraceOriginKind.RulesPolicy, c.Family)));
            return 0;
        }
        var (content, effect, _) = c.Casters[0];
        var modifier = FieldIds.Modifier(effect.Ability);
        var mod = c.Values[modifier];
        var pb = c.Values[FieldIds.ProficiencyBonus];
        var value = constant + pb + mod;
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, description, AbilityNames[effect.Ability]);
        steps.Add(new(field, "base", $"{text}, from {Describe(content)}", constant, value, ContentOrigin(c.Family, content, effect), [new(FieldIds.ProficiencyBonus, pb), new(modifier, mod)]));
        if (c.Casters.Count > 1)
            c.Warnings.Add(new("spellcasting.multiclass", $"This character has {c.Casters.Count} casters. The sheet's spell attack and save DC are '{content.Revision.Name}''s; each caster's own are listed with its spells.", content.Revision.Reference, effect.Id));
        return value;
    }

    /// <summary>
    /// D04: slots come from the one caster with ordinary spell slots, at its class level. With two or more, and when each
    /// declares how its levels count (<see cref="SpellcastingEffect.MulticlassCaster"/>, content v7), the SRD Multiclass
    /// Spellcaster table gives the slots (M3 C3). Otherwise TomeStack cannot combine them: the field shows the first
    /// caster's slots, assisted, and the player records the total as an override. Pact Magic is never combined.
    /// </summary>
    private static int SlotBase(BaseContext c, List<Step> steps, int level)
    {
        var field = FieldIds.SpellSlots(level);
        var slotCasters = c.Casters.Where(x => x.Effect.SlotKind == SpellSlotKind.SpellSlots).ToList();
        if (slotCasters.Count == 0)
        {
            steps.Add(new(field, "base", "No spell slots", 0, 0, new(TraceOriginKind.RulesPolicy, c.Family)));
            return 0;
        }
        // A caster combines when it says how: the v7 enum, or the v9 table (ADR-010; never both, validation refuses it).
        if (slotCasters.Count > 1 && slotCasters.All(x => x.Effect.MulticlassCaster is not null || CasterTable(x) is not null))
            return CombinedSlots(c, steps, level, slotCasters);
        var (content, effect, classLevel) = slotCasters[0];
        var value = Row(effect.Slots, classLevel)[level - 1];
        steps.Add(new(field, "base", $"{Describe(content)} at class level {classLevel}: {value} level {level} slot(s), from its table", value, value, ContentOrigin(c.Family, content, effect), [new(FormulaIdentifiers.ClassLevel, classLevel)]));
        if (slotCasters.Count > 1)
        {
            var others = string.Join(", ", slotCasters.Skip(1).Select(x => $"'{x.Content.Revision.Name}' {x.ClassLevel}"));
            c.Warnings.Add(new(
                "spellcasting.multiclass-slots",
                $"Spell slots of several classes ({others}) are not combined yet. TomeStack shows '{content.Revision.Name}''s slots; work out the total with the multiclass spellcaster table and record it as an override.",
                content.Revision.Reference,
                effect.Id));
            c.Manual.Add(field);
        }
        return value;
    }

    /// <summary>
    /// SRD multiclass spellcasting (both families): add every full caster's class levels, half of each half caster's and a
    /// third of each third caster's, rounded as the family's policy says, then read the Multiclass Spellcaster table at
    /// that caster level. Each caster's own spells, counts and highest spell level stay per class (<see cref="SpellcastingEntry"/>).
    /// </summary>
    private static int CombinedSlots(BaseContext c, List<Step> steps, int level, IReadOnlyList<CasterInfo> casters)
    {
        var field = FieldIds.SpellSlots(level);
        var policy = RulesFamilies.Get(c.Family);
        var total = 0;
        foreach (var caster in casters)
        {
            var (content, effect, classLevel) = caster;
            // A table caster counts its table's entry exactly, with no family rounding: the content states the numbers.
            var (part, how) = CasterTable(caster) is { } table
                ? (table[Math.Clamp(classLevel, 1, table.Count) - 1], "its multiclass table")
                : effect.MulticlassCaster switch
                {
                    MulticlassCaster.Half => (Fraction(classLevel, 2, policy.HalfCasterLevels), $"half, rounded {Rounding(policy.HalfCasterLevels)}"),
                    MulticlassCaster.Third => (Fraction(classLevel, 3, policy.ThirdCasterLevels), $"a third, rounded {Rounding(policy.ThirdCasterLevels)}"),
                    _ => (classLevel, "all levels"),
                };
            total += part;
            steps.Add(new(field, "base", $"{Describe(content)} at class level {classLevel} counts {part} caster level(s) ({how})", part, total, ContentOrigin(c.Family, content, effect), [new(FormulaIdentifiers.ClassLevel, classLevel)]));
        }
        var casterLevel = Math.Min(total, Character.MaxLevel);
        var value = casterLevel == 0 ? 0 : Row(policy.MulticlassSpellSlots, casterLevel)[level - 1];
        steps.Add(new(field, "base", $"Multiclass Spellcaster table at caster level {casterLevel}: {value} level {level} slot(s)", casterLevel, value, new(TraceOriginKind.RulesPolicy, c.Family)));
        return value;
    }

    /// <summary>
    /// Content v9: a caster's multiclass table, when its revision is v9 (deserialization keeps an older revision's key as
    /// extension data anyway) and the table is valid; an invalid one is refused on publish and import, and ignored here.
    /// </summary>
    private static IReadOnlyList<int>? CasterTable(CasterInfo caster) =>
        caster.Effect.MulticlassCasterTable is { } table && caster.Content.Revision.SchemaVersion >= SpellcastingEffect.MulticlassTableSchemaVersion
            && caster.Effect.MulticlassCaster is null && caster.Effect.SlotKind == SpellSlotKind.SpellSlots
            && ContentValidator.MulticlassTableProblem(table) is null
            ? table
            : null;

    private static int Fraction(int classLevel, int divisor, CasterLevelRounding rounding) =>
        rounding == CasterLevelRounding.Up ? (classLevel + divisor - 1) / divisor : classLevel / divisor;

    private static string Rounding(CasterLevelRounding rounding) => rounding == CasterLevelRounding.Up ? "up" : "down";

    private static int PactBase(BaseContext c, List<Step> steps)
    {
        var pact = c.Casters.FirstOrDefault(x => x.Effect.SlotKind == SpellSlotKind.PactMagic);
        if (pact is null)
        {
            steps.Add(new(FieldIds.PactSlots, "base", "No Pact Magic", 0, 0, new(TraceOriginKind.RulesPolicy, c.Family)));
            return 0;
        }
        var row = Row(pact.Effect.Slots, pact.ClassLevel);
        var value = row.Sum();
        steps.Add(new(FieldIds.PactSlots, "base", $"{Describe(pact.Content)} at class level {pact.ClassLevel}: {value} Pact Magic slot(s) of level {PactSlotLevel(row)}", value, value, ContentOrigin(c.Family, pact.Content, pact.Effect), [new(FormulaIdentifiers.ClassLevel, pact.ClassLevel)]));
        return value;
    }

    /// <summary>
    /// SRD hit points: the starting class's hit die maximum at level 1, the fixed value (half the die plus 1) for every
    /// other class level, and the Constitution modifier once per character level. Rolled hit points are recorded as an
    /// override. Without a class there is nothing to derive, so the field is assisted with a warning.
    /// </summary>
    private static int HitPoints(BaseContext c, List<Step> steps)
    {
        const string field = FieldIds.HitPoints;
        var policy = new TraceOrigin(TraceOriginKind.RulesPolicy, c.Family);
        if (c.Classes.Count == 0)
        {
            steps.Add(new(field, "base", "No class levels are recorded, so hit points cannot be derived; record a class or override the value", 0, 0, policy));
            c.Warnings.Add(new("hit-points.no-class", "Hit points need at least one class level. Record the character's class, or override the value."));
            c.Manual.Add(field);
            return 0;
        }

        var value = 0;
        for (var i = 0; i < c.Classes.Count; i++)
        {
            var (content, level, hitDie) = c.Classes[i];
            var name = content.Revision.Name;
            if (hitDie is null)
            {
                c.Warnings.Add(new("class.hit-die-missing", $"'{name}' declares no hit die, so its {level} level(s) add no hit points.", content.Revision.Reference));
                c.Manual.Add(field);
                continue;
            }
            var die = hitDie.Die;
            var origin = ContentOrigin(c.Family, content, hitDie);
            var fixedValue = (die / 2) + 1;
            var fromLevel = 1;
            if (i == 0)
            {
                value += die;
                steps.Add(new(field, "add", $"{name} level 1: the hit die maximum (d{die})", die, value, origin));
                fromLevel = 2;
            }
            var remaining = level - fromLevel + 1;
            if (remaining > 0)
            {
                var amount = remaining * fixedValue;
                value += amount;
                var levels = remaining == 1 ? $"level {fromLevel}" : $"levels {fromLevel}–{level}";
                steps.Add(new(field, "add", $"{name} {levels}: {remaining} × {fixedValue} (fixed value for d{die})", amount, value, origin));
            }
        }

        var con = c.Values[FieldIds.Modifier(Ability.Con)];
        var total = c.Character.TotalLevel;
        value += con * total;
        steps.Add(new(field, "add", $"Constitution modifier ({(con >= 0 ? "+" : "")}{con}) × {total} character level(s)", con * total, value, policy, [new(FieldIds.Modifier(Ability.Con), con), new(FormulaIdentifiers.Level, total)]));
        return value;
    }

    /// <summary>A saving throw or skill: ability modifier, plus the proficiency bonus (doubled for expertise) if granted.</summary>
    private static FieldSpec Proficient(string id, string label, Ability ability)
    {
        var modifier = FieldIds.Modifier(ability);
        return new(id, label, "modifier", [modifier, FieldIds.ProficiencyBonus], (c, steps) =>
        {
            var value = c.Values[modifier];
            steps.Add(new(id, "base", $"{label} starts at the {AbilityNames[ability]} modifier", value, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(modifier, value)]));
            if (c.Proficiencies.TryGetValue(id, out var proficiency))
            {
                var pb = c.Values[FieldIds.ProficiencyBonus];
                var amount = proficiency.Grant == GrantKind.Expertise ? pb * 2 : pb;
                value += amount;
                var what = proficiency.Grant == GrantKind.Expertise ? "Expertise (twice the proficiency bonus)" : "Proficiency bonus";
                steps.Add(new(id, "add", $"{what} from {Describe(proficiency.Content)}", amount, value, ContentOrigin(c.Family, proficiency.Content, proficiency.Effect), [new(FieldIds.ProficiencyBonus, pb)]));
            }
            return value;
        });
    }
}
