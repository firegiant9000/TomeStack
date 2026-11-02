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

/// <summary>D24: whether a save or skill carries proficiency or expertise (from a grant); None for every other field.</summary>
public enum ProficiencyMark { None, Proficient, Expertise }

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
    string Units = "",
    ProficiencyMark Mark = ProficiencyMark.None,
    RollModeSummary? Mode = null);

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
    IReadOnlyList<ScaleValue>? Scales = null,
    bool? Bloodied = null,
    IReadOnlyList<DamageDefenceValue>? DamageDefences = null,
    IReadOnlyList<ItemUseValue>? ItemUses = null,
    bool? LevelUpAvailable = null,
    int? XpForNextLevel = null,
    IReadOnlyList<UpkeepValue>? Upkeeps = null,
    IReadOnlyList<ResponseValue>? Responses = null)
{
    public DerivedValue Field(string field) => Fields.Single(f => f.Field == field);
}

/// <summary>Wave 3 item 25: when an item's charges come back (content v10 <c>item.charges.recovery</c>). Null <paramref name="Regain"/>: all of them.</summary>
public sealed record ItemChargeRecoveryInfo(RestPeriod On, string? Regain);

/// <summary>
/// Wave 3 item 25 (R158, R159, R171): one equipment entry of an item with a v10 <c>item</c> effect that needs attunement or has
/// charges, carried or equipped alike. <paramref name="EntryKey"/> names the entry for a change: its <c>id</c>, or <c>#</c> and its
/// position in the list while it has none (<paramref name="EntryId"/> null). <paramref name="Attuned"/> is what is recorded; the item's
/// other effects apply only while it is equipped and, when <paramref name="RequiresAttunement"/>, attuned. Charges are per entry:
/// <paramref name="ChargesMax"/> is null when the item has none; <paramref name="ChargesLeft"/> is the maximum less
/// <paramref name="ChargesSpent"/>, never below 0.
/// </summary>
public sealed record ItemUseValue(
    ContentReference Item,
    string Name,
    string EntryKey,
    Guid? EntryId,
    int EntryIndex,
    bool Equipped,
    bool RequiresAttunement,
    bool Attuned,
    int? ChargesMax,
    int ChargesSpent,
    int? ChargesLeft,
    ItemChargeRecoveryInfo? Recovery);

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
    string? Text,
    Activation? Activation = null,
    bool CountsUp = false,
    string? FullText = null,
    bool Allowance = false);

/// <summary>
/// Content v11 (D43): an upkeep the character's active content declares, for the turn planner. <paramref name="Content"/> and
/// <paramref name="EffectId"/> name the effect. <paramref name="ToggleContent"/> and <paramref name="ToggleId"/> name the toggle it
/// belongs to (both null: always); <paramref name="ResourceContent"/> and <paramref name="ResourceId"/> the pool. <paramref name="Amount"/>
/// is the formula evaluated for the character (never negative), null with <paramref name="All"/> (regain everything) or when the
/// formula fails (an invalid-formula warning). <paramref name="IfShort"/> is what a spend the pool cannot cover does.
/// <paramref name="Label"/> is a short generated name for a list.
/// </summary>
public sealed record UpkeepValue(
    ContentReference Content, string EffectId, Guid? ToggleContent, string? ToggleId, TurnMoment At, Guid ResourceContent, string ResourceId,
    UpkeepChange Change, int? Amount, bool All, UpkeepShortfall IfShort, string Label);

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
    IReadOnlyList<Diagnostic> Diagnostics,
    ContentReference? GrantedBy = null,
    string? GrantedByName = null);

/// <summary>One effect of a feature: its text and automation, plus the dice and linked resource of a roll.</summary>
/// <param name="ResourceContent">Content v6: the content id that defines <paramref name="ResourceId"/> (a shared resource); null for this feature.</param>
/// <param name="Cost">Content v6: uses the action spends (its formula evaluated), or the most it may spend when <paramref name="VariableCost"/>.</param>
/// <param name="Bonus">
/// Content v8: the roll's bonus formula evaluated for this character (for example the Fighter level, or a negative
/// Strength modifier), added to the dice. Null when the roll has none, or when it failed (a diagnostic; the roll is refused).
/// </param>
/// <param name="Available">Content v11 (D43): false while a form-only roll (<c>whileToggle</c>) is out of its form; the roll is still listed.</param>
/// <param name="UnavailableReason">Why it is not available ("Available in Kindled mode"); null when it is.</param>
/// <param name="SaveAbility">Content v11 (D43): the ability of the save the roll forces; set whenever the roll has a save.</param>
/// <param name="SaveDc">The save DC evaluated for this character; null when the roll has no save or the formula failed (a diagnostic; the row is assisted).</param>
/// <param name="SaveTrace">How <paramref name="SaveDc"/> was worked out; null when it has no DC.</param>
/// <param name="Area">The area the roll covers, as written. Shown, never automated.</param>
/// <param name="Range">The roll's range, as written. Shown, never automated.</param>
/// <param name="Targets">Who the roll affects, as written. Shown, never automated.</param>
/// <param name="PerUnit">Extra dice per unit spent, as written; the roll command applies it (Task 5.3).</param>
/// <param name="AlsoChange">Content v11 (D43): the other pools the use also changes, each amount evaluated for this character (never below 0). Null when the roll has none.</param>
/// <param name="DrawsDown">Content v11 (D43): the cost and per-unit amount use up the built-up value of the counting-up pool instead of adding to it.</param>
/// <param name="RollId">The roll's content <c>rollId</c> (what <c>useAction</c> names); it can differ from <paramref name="Id"/>. A computed sheet member, not stored.</param>
public sealed record FeatureEffect(
    string Id, string Type, AutomationStatus Automation, string? Text, string? Label = null, string? Dice = null, string? ResourceId = null, Activation? Activation = null,
    Guid? ResourceContent = null, int? Cost = null, bool VariableCost = false, int? Bonus = null, bool Available = true, string? UnavailableReason = null,
    Ability? SaveAbility = null, int? SaveDc = null, IReadOnlyList<TraceEntry>? SaveTrace = null,
    RollArea? Area = null, string? Range = null, string? Targets = null,
    RollPerUnit? PerUnit = null,
    IReadOnlyList<AlsoChangeValue>? AlsoChange = null, bool DrawsDown = false, string? RollId = null);

/// <summary>
/// Content v11 (D43): one pool a use also changes, for this character. <paramref name="ResourceContent"/> is the content that
/// defines the pool (the roll's own when the content named none); <paramref name="Amount"/> is the evaluated formula, never below 0,
/// or null when it could not be calculated (a diagnostic; the row is assisted).
/// </summary>
public sealed record AlsoChangeValue(Guid ResourceContent, string ResourceId, UpkeepChange Change, int? Amount);

/// <summary>
/// Content v6 (M3 B2): a toggle the player switches on and off, and whether it is on now. <paramref name="Auto"/> (content v10,
/// D38): true when the sheet switches it itself (<c>autoWhen</c>); it has no switch, is never stored and never spends a use.
/// Null otherwise, so nothing is written for an ordinary toggle.
/// Content v11 (D43): <paramref name="Group"/>, <paramref name="GroupContent"/> and <paramref name="Layer"/> name the form group the toggle
/// joins (null for an ungrouped toggle); <paramref name="Cost"/> is its pool cost for this character (null when it has none, or the
/// formula fails) spent from <paramref name="CostResourceContent"/>'s pool; <paramref name="Available"/> is false, with a reason, when
/// an overlay may not be switched on with the form that is on now.
/// <paramref name="AllowedWith"/> and <paramref name="AllowedInBase"/> (overlays in a group only) are the overlay's own rule as declared, so
/// <see cref="ToggleGroups.EndingWith"/> can tell which overlays a primary that goes off takes with it.
/// </summary>
public sealed record ToggleValue(
    ContentReference Content, string ContentName, string EffectId, string ToggleId, string Label, bool On, string? ResourceId, string? Text, bool? Auto = null, Activation? Activation = null,
    string? Group = null, Guid? GroupContent = null, ToggleLayer? Layer = null, int? Cost = null, Guid? CostResourceContent = null, bool Available = true, string? UnavailableReason = null, string? GroupLabel = null,
    int? Duration = null, int? RoundsLeft = null, IReadOnlyList<ToggleRef>? AllowedWith = null, bool? AllowedInBase = null);

/// <summary>Where a damage defence on the sheet comes from: the character's own list (<paramref name="Content"/> null) or a v10 damage grant.</summary>
public sealed record DamageDefenceSource(string Name, ContentReference? Content = null, string? EffectId = null);

/// <summary>
/// Wave 3 item 21 (D19 amended): one damage resistance, immunity or vulnerability the character has. <paramref name="Type"/> is
/// the canonical lower-case name from <see cref="DamageTypes.All"/>; several sources of the same type and kind are one entry.
/// </summary>
public sealed record DamageDefenceValue(string Type, DamageDefenceKind Kind, IReadOnlyList<DamageDefenceSource> Sources);

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
/// Content v11 (D43): a feature can give an attack too (<see cref="AttackSource.Feature"/>; <paramref name="Category"/> is null,
/// <paramref name="Item"/> is the declaring content's revision and <paramref name="EffectId"/> the attack effect's id), and toggled
/// riders add to either kind. An attack whose die is not a die is listed with <paramref name="Available"/> false and the reason.
/// </summary>
/// <param name="Riders">The riders that apply now, in sheet content order; null when none.</param>
/// <param name="Cost">Uses of <paramref name="ResourceId"/> an explicit action spends; the pool is declared by <paramref name="CostResourceContent"/>.</param>
public sealed record AttackEntry(
    ContentReference Item,
    string Name,
    string EffectId,
    WeaponAttack Attack,
    WeaponCategory? Category,
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
    TraceOrigin Origin,
    AttackSource Source = AttackSource.Weapon,
    IReadOnlyList<string>? Tags = null,
    bool ReplacesAttack = false,
    IReadOnlyList<RiderValue>? Riders = null,
    bool Available = true,
    string? UnavailableReason = null,
    Activation? Activation = null,
    Guid? CostResourceContent = null,
    string? ResourceId = null,
    int? Cost = null);

/// <summary>Content v11 (D43): where an attack on the sheet comes from. Custom attacks are not on the sheet's list.</summary>
public enum AttackSource { Weapon, Feature }

/// <summary>Content v11 (D43): an attack rider that applies to an attack now, with its numbers (formulas evaluated).</summary>
public sealed record RiderValue(ContentReference Content, string EffectId, string? Dice, int ToHit, int DamageBonus, string? DamageType);

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
public static partial class CharacterCalculator
{
    public const string InitiativeField = FieldIds.Initiative;

    public const string Stealth = "stealth";

    // Every static initializer of CharacterCalculator stays in this file: across partial files their order is
    // unspecified (CalculationSplitTests).
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

    /// <summary>The keys an armor training grant may name (the <see cref="ArmorCategory"/> values, in lower case).</summary>
    public static IReadOnlyList<string> ArmorTrainingKeys { get; } = [.. Enum.GetValues<ArmorCategory>().Select(c => c.ToString().ToLowerInvariant())];

    public static bool IsField(string field) => SpecIndex.ContainsKey(field);

    /// <summary>
    /// D24 (2026-10-06): speed and the passive scores are calculated and overridable on the sheet, but a build before
    /// content schema v10 would read content that targets them as supported and drop its bonus silently. So content may
    /// target them only in a v10 (or newer) revision: below v10 the validator refuses it, and the calculator ignores such
    /// modifiers and restrictions with a diagnostic, exactly as those builds do. From v10 they are ordinary targets (D42).
    /// A character's own override (Stats) stays final for them, as for every field.
    /// </summary>
    /// <param name="schemaVersion">The schema version of the revision that targets the field.</param>
    public static bool IsCharacterOnlyField(string field, int schemaVersion) =>
        IsSpeedOrPassive(field) && schemaVersion < ContentRevision.PlayDetailsSchemaVersion;

    /// <summary>
    /// Content schema v11 (D43): fly speed is a content target only from v11. Below it the validator refuses it and the
    /// calculator ignores such a modifier with the same diagnostic as <see cref="IsCharacterOnlyField"/>, so a stored older
    /// revision never moves it, whatever it says.
    /// </summary>
    public static bool IsPlayMechanicsOnlyField(string field, int schemaVersion) =>
        field == FieldIds.FlySpeed && schemaVersion < ContentRevision.PlayMechanicsSchemaVersion;

    /// <summary>Speed and the passive scores: the fields content may target only from schema v10.</summary>
    public static bool IsSpeedOrPassive(string field) =>
        field == FieldIds.Speed || field.StartsWith("passive.", StringComparison.Ordinal);

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
            if (SpecIndex.ContainsKey(effect.Target) && !IsCharacterOnlyField(effect.Target, revision.SchemaVersion) && !IsPlayMechanicsOnlyField(effect.Target, revision.SchemaVersion) && Formula.TryParse(effect.Value, Formula.FeaturesFor(revision), out var formula, out _))
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

        // D38 / R154: Bloodied is decided by a first pass in which auto toggles are off, so the maximum is the pre-toggle one
        // and an auto toggle can never move the line it is judged by. Only when the character is bloodied and has an auto
        // toggle is the sheet calculated again with those toggles on (the common case costs one pass).
        var pass = CalculateSheet(character, catalog, autoOn: false);
        if (pass.Bloodied != true || pass.Toggles is not { } toggles || !toggles.Any(t => t.Auto == true))
            return WithItemUses(pass, character, catalog);
        return WithItemUses(CalculateSheet(character, catalog, autoOn: true) with { Bloodied = true }, character, catalog);
    }

    /// <summary>
    /// The item uses read the catalog once per equipment entry (a lookup and a deserialise each), and nothing in the passes depends on
    /// them, so they are collected once for the sheet that is returned, not in every inner pass (the restriction and Bloodied passes).
    /// </summary>
    private static CharacterSheet WithItemUses(CharacterSheet sheet, Character character, IContentCatalog catalog)
    {
        var itemUses = CollectItemUses(character, catalog);
        return itemUses.Count > 0 ? sheet with { ItemUses = itemUses } : sheet;
    }

    /// <param name="autoOn">Whether the toggles that switch themselves (<c>autoWhen</c>) are on for this pass.</param>
    private static CharacterSheet CalculateSheet(Character character, IContentCatalog catalog, bool autoOn)
    {
        var first = Calculate(character, catalog, excluded: new HashSet<ContentReference>(), autoOn);
        var unmet = new List<(ContentReference Content, Diagnostic Diagnostic)>();
        foreach (var restricted in first.Active.Where(a => a.Revision.Effects.OfType<RestrictionEffect>().Any(IsPrerequisite)))
        {
            var without = Calculate(character, catalog, excluded: new HashSet<ContentReference> { restricted.Revision.Reference }, autoOn);
            foreach (var failed in Unmet(restricted.Revision, restricted.Revision.Effects.OfType<RestrictionEffect>().Where(IsPrerequisite), without.Sheet, "without it", "Its effects are not applied."))
                unmet.Add((restricted.Revision.Reference, failed));
        }
        var final = unmet.Count == 0 ? first : Calculate(character, catalog, excluded: unmet.Select(u => u.Content).ToHashSet(), autoOn);

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
                if (IsCharacterOnlyField(restriction.Field, revision.SchemaVersion) || IsPlayMechanicsOnlyField(restriction.Field, revision.SchemaVersion))
                {
                    // As in an older build, where the field is not a content target: the content is not applied.
                    yield return new(
                        "effect.character-only-field",
                        $"'{revision.Name}' restriction '{restriction.Id}' checks '{restriction.Field}', which content may not target yet; the content is not applied.",
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
    private static Calculation Calculate(Character character, IContentCatalog catalog, IReadOnlySet<ContentReference> excluded, bool autoOn)
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
        // Content v11 (D43): one primary per form group and the allowed overlays, resolved in every pass.
        var declaredToggles = DeclaredToggles(active, character, resolved.ClassLevels);
        var effective = ToggleGroups.Resolve(character.Play, declaredToggles, diagnostics, active.Select(a => a.Revision.ContentId).ToHashSet());
        var resourceBonuses = new List<ResourceBonus>();
        var modifiers = CollectModifiers(active, character, policy, diagnostics, warnings, manual, effective, autoOn, resourceBonuses, resolved.ClassLevels);
        var rollModes = CollectRollModes(active, effective, autoOn, diagnostics);
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
                spec.Units,
                MarkOf(spec.Id, proficiencies),
                rollModes.GetValueOrDefault(spec.Id));
        }).ToList();

        // Before the feature list: an attack whose die is invalid reports on its feature (attack.die-invalid).
        var attacks = CollectAttacks(active, values, family, weaponProficiencies, character, resolved.ClassLevels, effective, autoOn, diagnostics);
        var resources = CollectResources(active, character, resolved.ClassLevels, values, family, resourceBonuses);
        var rollBonuses = RollBonuses(active, character, resolved.ClassLevels, values, diagnostics);
        var rollDetails = RollDetailsOf(active, family, character, resolved.ClassLevels, values, diagnostics);
        // Before the feature list: a response whose formula fails reports on its feature (effect.invalid-formula). HP.MAX is this sheet's own maximum.
        var responses = CollectResponses(active, character, resolved.ClassLevels, values, resources, effective, autoOn, values[FieldIds.HitPoints], diagnostics);
        // Before the feature list: a form-only defence of absent content reports on its feature (modifier.toggle-content-missing).
        var damageDefences = CollectDamageDefences(active, character, resolved.ClassLevels, effective, autoOn, diagnostics);
        var scoped = diagnostics.Concat(warnings.Values.SelectMany(w => w)).Concat(resources.SelectMany(r => r.Warnings)).Where(d => d.Content is not null).Distinct().ToList();
        var features = active.Select(item => Feature(
            item, family, [.. scoped.Where(d => d.Content == item.Revision.Reference)], rollDetails, rollBonuses, form => OutOfForm(active, item, form, effective, autoOn), resources)).ToList();
        var toggles = active
            .SelectMany(item => item.Revision.Effects.OfType<ToggleEffect>()
                .Where(t => t.Automation != AutomationStatus.Reference && LevelReached(item, t, character, resolved.ClassLevels))
                .Select(t => ToggleOf(item, t)))
            .ToList();
        ToggleValue ToggleOf(ActiveContent item, ToggleEffect t)
        {
            var revision = item.Revision;
            var allowed = effective.IsAllowed(revision.ContentId, t.ToggleId);
            int? cost = null;
            if (t.Cost is { } source)
            {
                // Like a resource maximum: a formula that fails gives no cost and the invalid-formula warning.
                if (!Formula.TryParse(source, Formula.FeaturesFor(revision), out var formula, out var failure)
                    || !formula!.TryEvaluate(id => Resolve(id, item, character, resolved.ClassLevels, values, []), out var amount, out failure))
                    diagnostics.Add(InvalidFormula(revision, t, failure!));
                else
                    cost = Math.Max(amount, 0);
            }
            int? duration = null;
            if (t.Duration is { } rounds)
            {
                // Rounds: a formula that fails gives no duration and the invalid-formula warning.
                if (!Formula.TryParse(rounds, Formula.FeaturesFor(revision), out var durationFormula, out var durationFailure)
                    || !durationFormula!.TryEvaluate(id => Resolve(id, item, character, resolved.ClassLevels, values, []), out var roundCount, out durationFailure))
                    diagnostics.Add(InvalidFormula(revision, t, durationFailure!));
                else
                    duration = Math.Clamp(roundCount, 0, PlayState.MaxRounds);
            }
            var isOn = ToggleIsOn(revision, t, effective, autoOn);
            // The count means something only inside an encounter and while the content still has a duration; otherwise a stored count is inert.
            var roundsLeft = isOn && character.Play.Encounter is not null && duration is not null
                ? character.Play.Toggles.FirstOrDefault(a => a.ContentId == revision.ContentId && a.ToggleId == t.ToggleId)?.RoundsLeft
                : null;
            // A toggle whose group content is not on the character is a group of one (C6): it shows as a plain toggle.
            var inGroup = t.Group is not null && active.Any(a => a.Revision.ContentId == (t.GroupContent ?? revision.ContentId));
            return new ToggleValue(
                revision.Reference, revision.Name, t.Id, t.ToggleId, t.Label, isOn, t.ResourceId, t.Text,
                IsAuto(revision, t) ? true : null, ActivationOf(revision, t.Activation),
                inGroup ? t.Group : null, inGroup ? t.GroupContent ?? revision.ContentId : null, inGroup ? t.Layer ?? ToggleLayer.Primary : null,
                cost, t.ResourceContent, allowed, allowed ? null : UnavailableReason(active, item, t), GroupLabelOf(active, item, t), duration, roundsLeft,
                inGroup && t.Layer == ToggleLayer.Overlay ? t.AllowedWith : null, inGroup && t.Layer == ToggleLayer.Overlay ? t.AllowedInBase : null);
        }
        // A response with an outcome that can never run (a form that is not on the sheet, is automatic or is not allowed now; a pool the character lacks) is not offered.
        responses = [.. responses.Select(r => r.Available ? WithRunnableOutcomes(r, toggles, resources) : r)];
        var upkeeps = CollectUpkeeps(active, character, resolved.ClassLevels, values, diagnostics);
        var maximum = values[FieldIds.HitPoints];
        var hitPoints = new HitPointState(maximum, Math.Clamp(character.Play.CurrentHitPoints ?? maximum, 0, Math.Max(maximum, 0)), character.Play.TemporaryHitPoints);
        // R168: bloodied = a maximum of at least 1 and current hit points at or below half of it, rounded down. Temporary hit points do not count.
        var bloodied = maximum >= 1 && hitPoints.Current <= maximum / 2;
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

        var scales = resolved.ClassLevels.Scales
            .Where(s => resolved.ClassLevels.ContainsKey(s.Key.Class))
            .Select(s =>
            {
                var classLevel = resolved.ClassLevels[s.Key.Class];
                var className = active.FirstOrDefault(a => a.Revision.Reference == s.Key.Class)?.Revision.Name ?? "";
                return new ScaleValue(s.Key.Class, className, s.Value.Content.Revision.Reference, s.Key.ScaleId, s.Value.Effect.Label, classLevel, s.Value.Effect.Values[Math.Clamp(classLevel, 1, s.Value.Effect.Values.Count) - 1]);
            })
            .ToList();
        // Wave 3 item 27 (R177): display only. Judged by the total character level; absent when XP is not tracked or there is no next level.
        var xpForNextLevel = character.Xp is null ? null : Advancement.XpForNextLevel(character.TotalLevel);
        return new(new CharacterSheet(
            character.Id, family, fields, diagnostics, resolved.Choices, [.. active.Select(a => a.Revision.Reference)], resources, features, hitPoints, hitDice,
            spellcasting, spellSlots, pactSlots, attacks, toggles, scales.Count > 0 ? scales : null, bloodied ? true : null,
            damageDefences.Count > 0 ? damageDefences : null, null, // the item uses are added once by Calculate (WithItemUses)
            xpForNextLevel is { } next && character.Xp >= next ? true : null, xpForNextLevel, upkeeps.Count > 0 ? upkeeps : null,
            responses.Count > 0 ? responses : null), active);
    }

    /// <summary>
    /// A response whose toggleOn outcome names a form that is missing, automatic or unavailable, or whose spend or regain outcome names a pool
    /// the character does not have, is unavailable. Apply checks the same again as a backstop.
    /// </summary>
    private static ResponseValue WithRunnableOutcomes(ResponseValue response, List<ToggleValue> toggles, List<ResourceValue> resources)
    {
        foreach (var outcome in response.Outcomes ?? [])
        {
            switch (outcome.Kind)
            {
                case ResponseOutcomeKind.ToggleOn:
                    var toggle = outcome.Toggle is { ContentId: { } contentId } reference
                        ? toggles.FirstOrDefault(t => t.Content.ContentId == contentId && t.ToggleId == reference.ToggleId)
                        : null;
                    if (toggle is null || toggle.Auto == true || !toggle.Available)
                        return response with { Available = false, UnavailableReason = "Form not available" };
                    break;
                case ResponseOutcomeKind.Spend or ResponseOutcomeKind.Regain:
                    if (!resources.Any(r => r.Content.ContentId == outcome.ResourceContent && r.ResourceId == outcome.ResourceId))
                        return response with { Available = false, UnavailableReason = "Pool not found" };
                    break;
            }
        }
        return response;
    }

    /// <summary>The most damage responses one sheet lists (risk C5); later ones are dropped in sheet order with <c>response.too-many</c>.</summary>
    public const int MaxResponses = 64;

    /// <summary>
    /// Content v11 (D43, D45): the damage responses of the active content, in active-content order and then effect order. Reference-only
    /// and conditional (timing) responses are not calculated. Every formula is evaluated here, once the hit point maximum is known:
    /// <c>amount</c>, <c>cost</c>, each outcome's amount and a threshold's <c>atOrBelow</c> (the only place <c>HP.MAX</c> resolves, to
    /// <paramref name="hitPointMaximum"/>); an evaluated amount is within 0 and <see cref="PlayState.MaxHitPoints"/>. A formula that fails, or
    /// a roll the character does not have, makes the response unavailable and the formula failure an <c>effect.invalid-formula</c>
    /// diagnostic. A response is also unavailable while its toggle is off or when its pool cannot pay its cost.
    /// </summary>
    private static List<ResponseValue> CollectResponses(
        List<ActiveContent> active, Character character, ClassLevelMap classLevels, Dictionary<string, int> values, List<ResourceValue> resources,
        EffectiveToggles effective, bool autoOn, int hitPointMaximum, List<Diagnostic> diagnostics)
    {
        var candidates = active
            .SelectMany(item => item.Revision.Effects.OfType<ResponseEffect>()
                .Where(r => r.Automation != AutomationStatus.Reference && r.Timing == EffectTiming.Always)
                .Select(r => (Item: item, Effect: r)))
            .ToList();
        if (candidates.Count > MaxResponses)
        {
            AddOnce(diagnostics, new("response.too-many", $"The character has {candidates.Count} damage responses; only the first {MaxResponses} are listed."));
            candidates = candidates.GetRange(0, MaxResponses);
        }

        ResponseValue ResponseOf(ActiveContent item, ResponseEffect response)
        {
            var revision = item.Revision;
            var own = revision.ContentId;
            var failed = false;
            int? Evaluate(string? source, bool threshold = false)
            {
                if (source is null)
                    return null;
                var features = Formula.FeaturesFor(revision) | (threshold ? FormulaFeatures.HitPointMaximum : FormulaFeatures.None);
                FormulaError? failure;
                if (Formula.TryParse(source, features, out var formula, out failure)
                    && formula!.TryEvaluate(
                        id => threshold && id == FormulaIdentifiers.HitPointMaximum ? hitPointMaximum : Resolve(id, item, character, classLevels, values, []),
                        out var value, out failure))
                    return Math.Clamp(value, 0, PlayState.MaxHitPoints);
                failed = true;
                AddOnce(diagnostics, InvalidFormula(revision, response, failure!));
                return null;
            }

            var amount = Evaluate(response.Amount);
            var cost = Evaluate(response.Cost);
            var atOrBelow = Evaluate(response.When?.AtOrBelow, threshold: true);
            var rollMissing = false;
            var outcomes = new List<ResponseOutcomeValue>();
            foreach (var outcome in response.Outcomes ?? [])
            {
                switch (outcome.Kind)
                {
                    case ResponseOutcomeKind.Spend:
                    case ResponseOutcomeKind.Regain:
                        outcomes.Add(new(outcome.Kind, outcome.ResourceContent ?? own, outcome.ResourceId, Evaluate(outcome.Amount), null, null, null, null));
                        break;
                    case ResponseOutcomeKind.RegainHitPoints:
                    case ResponseOutcomeKind.TemporaryHitPoints:
                        outcomes.Add(new(outcome.Kind, null, null, Evaluate(outcome.Amount), null, null, null, null));
                        break;
                    case ResponseOutcomeKind.ToggleOn:
                        outcomes.Add(new(outcome.Kind, null, null, null, outcome.Toggle is { } toggle ? new ToggleRef(toggle.ContentId ?? own, toggle.ToggleId) : null, null, null, null));
                        break;
                    default:
                        var rollContent = outcome.RollContent ?? own;
                        var owner = active.FirstOrDefault(a => a.Revision.ContentId == rollContent);
                        var roll = owner?.Revision.Effects.OfType<RollEffect>().FirstOrDefault(r => r.RollId == outcome.RollId);
                        rollMissing |= roll is null;
                        outcomes.Add(new(outcome.Kind, null, null, null, null, roll is null ? null : owner!.Revision.Reference, roll?.Id, outcome.RollId));
                        break;
                }
            }

            var poolContent = response.ResourceId is null ? (Guid?)null : response.ResourceContent ?? own;
            string? reason = null;
            if (failed)
            {
                reason = "Formula failed";
            }
            else if (rollMissing)
            {
                reason = "Roll not found";
            }
            else if (response.Toggle is { } form)
            {
                var formOwner = active.FirstOrDefault(a => a.Revision.ContentId == (form.ContentId ?? own));
                var formToggle = formOwner?.Revision.Effects.OfType<ToggleEffect>().FirstOrDefault(t => t.ToggleId == form.ToggleId);
                if (formOwner is null || formToggle is null || !ToggleIsOn(formOwner.Revision, formToggle, effective, autoOn))
                    reason = $"Available in {formOwner?.Revision.Name ?? form.ToggleId}";
            }
            if (reason is null && cost is { } needed && response.ResourceId is { } poolId)
            {
                var pool = resources.FirstOrDefault(r => r.Content.ContentId == poolContent && r.ResourceId == poolId);
                if (pool is null)
                    reason = $"Needs {needed} {poolId} (0 left)";
                else if (pool.Current is not { } left)
                    reason = $"Needs {needed} {pool.Label} (not tracked)";
                else if (left < needed)
                    reason = $"Needs {needed} {pool.Label} ({left} left)";
            }
            return new(
                own, revision.Name, response.ResponseId, response.Label, response.Phase, amount, poolContent, response.ResourceId, cost,
                reason is null, reason, response.Text, atOrBelow, response.When?.Survived == true, outcomes.Count > 0 ? outcomes : null);
        }

        return [.. candidates.Select(c => ResponseOf(c.Item, c.Effect))];
    }

    /// <summary>
    /// Content v11 (D43): the upkeeps of the active content, in active-content order and then effect order. An amount is a formula
    /// evaluated like a resource maximum (never negative); one that fails is null with the invalid-formula warning, and the
    /// calculation goes on. <c>all</c> is a regain of everything and has no amount. A pool that names no content is the declarer's.
    /// </summary>
    private static List<UpkeepValue> CollectUpkeeps(
        List<ActiveContent> active, Character character, ClassLevelMap classLevels, Dictionary<string, int> values, List<Diagnostic> diagnostics)
    {
        var upkeeps = new List<UpkeepValue>();
        foreach (var item in active)
        {
            var revision = item.Revision;
            foreach (var upkeep in revision.Effects.OfType<UpkeepEffect>())
            {
                var all = upkeep.Change == UpkeepChange.Regain && string.Equals(upkeep.Amount.Trim(), "all", StringComparison.Ordinal);
                int? amount = null;
                if (!all)
                {
                    if (!Formula.TryParse(upkeep.Amount, Formula.FeaturesFor(revision), out var formula, out var failure)
                        || !formula!.TryEvaluate(id => Resolve(id, item, character, classLevels, values, []), out var value, out failure))
                        diagnostics.Add(InvalidFormula(revision, upkeep, failure!));
                    else
                        amount = Math.Max(value, 0);
                }
                var poolContent = upkeep.ResourceContent ?? revision.ContentId;
                var toggleContent = upkeep.Toggle is null ? (Guid?)null : upkeep.ToggleContent ?? revision.ContentId;
                var label = (upkeep.Toggle is null ? null : active.FirstOrDefault(a => a.Revision.ContentId == toggleContent)?.Revision.Effects.OfType<ToggleEffect>().FirstOrDefault(t => t.ToggleId == upkeep.Toggle)?.Label)
                    ?? active.FirstOrDefault(a => a.Revision.ContentId == poolContent)?.Revision.Effects.OfType<ResourceEffect>().FirstOrDefault(r => r.ResourceId == upkeep.ResourceId)?.Label
                    ?? upkeep.ResourceId;
                upkeeps.Add(new(revision.Reference, upkeep.Id, toggleContent, upkeep.Toggle, upkeep.At, poolContent, upkeep.ResourceId, upkeep.Change, amount, all,
                    upkeep.IfShort ?? UpkeepShortfall.Ask, $"{label} upkeep"));
            }
        }
        return upkeeps;
    }

    /// <summary>
    /// Wave 3 item 25 (R158 to R160, R171): every equipment entry of a published item whose v10 <c>item</c> effect needs attunement or
    /// has charges, equipped or only carried, in list order. Charges belong to the entry, not the item: two copies have two counts.
    /// The spent count is recorded as the player says; the sheet only shows what is left (never below 0).
    /// </summary>
    private static List<ItemUseValue> CollectItemUses(Character character, IContentCatalog catalog)
    {
        var uses = new List<ItemUseValue>();
        for (var index = 0; index < character.Equipment.Count; index++)
        {
            var entry = character.Equipment[index];
            if (catalog.FindRevision(entry.Item) is not { Kind: ContentKind.Item, Status: RevisionStatus.Published } revision
                || revision.Effects.OfType<ItemEffect>().FirstOrDefault() is not { } item
                || (item.RequiresAttunement != true && item.Charges is null))
                continue;
            var spent = entry.ChargesSpent ?? 0;
            var recovery = item.Charges?.Recovery is { } r ? new ItemChargeRecoveryInfo(r.Period, r.Regain) : null;
            uses.Add(new(
                entry.Item, revision.Name, entry.Id is { } id ? id.ToString("D") : $"#{index}", entry.Id, index, entry.Equipped,
                item.RequiresAttunement == true, entry.Attuned == true, item.Charges?.Max, spent,
                item.Charges is { } charges ? Math.Max(charges.Max - spent, 0) : null, recovery));
        }
        return uses;
    }

    /// <summary>
    /// Wave 3 item 21 (D19 amended): the character's own <c>damageDefences</c> (character v9) and the damage grants of content
    /// v10, merged. A grant counts only when the calculator can apply it by itself (automatic, always on) and its <c>level</c>
    /// and <c>onlyAs</c> are met, the same rules as the other grants. Grants are stored as written, so the type is normalised
    /// here. Several sources of one type and kind are one entry (R169), ordered by damage type, then kind. A damage grant in
    /// a revision below v10 is an unknown effect (Task 1) and never arrives here.
    /// </summary>
    private static List<DamageDefenceValue> CollectDamageDefences(
        List<ActiveContent> active, Character character, ClassLevelMap classLevels, EffectiveToggles effective, bool autoOn, List<Diagnostic> diagnostics)
    {
        var found = new List<(string Type, DamageDefenceKind Kind, DamageDefenceSource Source)>();
        foreach (var defence in character.DamageDefences ?? [])
        {
            if (defence is not null && DamageTypes.TryNormalize(defence.Type, out var type))
                found.Add((type, defence.Kind, new("Entered by hand")));
        }
        foreach (var item in active)
        {
            foreach (var grant in item.Revision.Effects.OfType<GrantEffect>())
            {
                if (!GrantEffect.IsDamageDefence(grant.Grant) || grant.Automation != AutomationStatus.Automatic || grant.Timing != EffectTiming.Always)
                    continue;
                if (grant.Level is { } needed && GateLevel(item, character, classLevels) < needed)
                    continue;
                if (!EntryApplies(item, grant.OnlyAs, character) || !DamageTypes.TryNormalize(grant.DamageType, out var type))
                    continue;
                // Content v11 (D43): a form-only defence is listed only while its toggle is on, gated like a toggled modifier.
                if (grant.WhileToggle is { } form
                    && GateOnToggle(active, item.Revision, grant.Id, form.ToggleId, form.ContentId ?? item.Revision.ContentId, effective, autoOn, diagnostics, out _) != ToggleGate.Applies)
                    continue;
                var kind = grant.Grant switch
                {
                    GrantKind.DamageImmunity => DamageDefenceKind.Immunity,
                    GrantKind.DamageVulnerability => DamageDefenceKind.Vulnerability,
                    _ => DamageDefenceKind.Resistance,
                };
                found.Add((type, kind, new(item.Revision.Name, item.Revision.Reference, grant.Id)));
            }
        }
        var order = DamageTypes.All.ToList();
        return [.. found
            .GroupBy(f => (f.Type, f.Kind))
            .OrderBy(g => order.IndexOf(g.Key.Type)).ThenBy(g => g.Key.Kind)
            .Select(g => new DamageDefenceValue(g.Key.Type, g.Key.Kind, [.. g.Select(f => f.Source).Distinct()]))];
    }

    /// <summary>Content v10 (D38): a toggle that switches itself while the character is bloodied. Below v10 <c>autoWhen</c> is extension data and never reaches here.</summary>
    private static bool IsAuto(ContentRevision revision, ToggleEffect toggle) =>
        toggle.AutoWhen == ToggleAutoWhen.Bloodied && revision.SchemaVersion >= ContentRevision.PlayDetailsSchemaVersion;

    /// <summary>Wave 3 item 22: a toggle's or resource's <c>activation</c> reaches the sheet from v10 only (below it the key is extension data and is never read).</summary>
    private static Activation? ActivationOf(ContentRevision revision, Activation? activation) =>
        revision.SchemaVersion >= ContentRevision.PlayDetailsSchemaVersion ? activation : null;

    /// <summary>An auto toggle is on only when the pass says so; a stored <c>ActiveToggle</c> naming it is ignored. Any other toggle follows the play state.</summary>
    private static bool ToggleIsOn(ContentRevision revision, ToggleEffect toggle, EffectiveToggles effective, bool autoOn) =>
        IsAuto(revision, toggle) ? autoOn : effective.IsOn(revision.ContentId, toggle.ToggleId);

    /// <summary>
    /// Content v11 (D43): why a form-only action (<c>whileToggle</c>) is unavailable now, or null when its toggle is on. The toggle belongs to
    /// the named content, or to <paramref name="item"/> when none is named. A toggle that is not declared by active content is off and is
    /// named by its id (the validator already warns about the reference).
    /// </summary>
    private static string? OutOfForm(List<ActiveContent> active, ActiveContent item, ToggleRef whileToggle, EffectiveToggles effective, bool autoOn)
    {
        var owner = active.FirstOrDefault(a => a.Revision.ContentId == (whileToggle.ContentId ?? item.Revision.ContentId));
        var toggle = owner?.Revision.Effects.OfType<ToggleEffect>().FirstOrDefault(t => t.ToggleId == whileToggle.ToggleId);
        if (owner is null || toggle is null)
            return $"Available in {whileToggle.ToggleId}";
        return ToggleIsOn(owner.Revision, toggle, effective, autoOn) ? null : $"Available in {toggle.Label}";
    }

    /// <summary>Content v11 (D43): the label of the <c>toggleGroup</c> effect that declares the toggle's group, found on the group content among the active content; null when ungrouped or the declarer is not active.</summary>
    private static string? GroupLabelOf(List<ActiveContent> active, ActiveContent item, ToggleEffect toggle) =>
        toggle.Group is not { } group
            ? null
            : active.FirstOrDefault(a => a.Revision.ContentId == (toggle.GroupContent ?? item.Revision.ContentId))?.Revision.Effects
                .OfType<ToggleGroupEffect>().FirstOrDefault(g => g.GroupId == group)?.Label;

    /// <summary>Content v11 (D43): why an overlay cannot be switched on now, naming the form content it needs.</summary>
    private static string UnavailableReason(List<ActiveContent> active, ActiveContent item, ToggleEffect overlay)
    {
        // Name the form (the toggle's label); add the content name only when the reference points at other content.
        var needed = (overlay.AllowedWith ?? [])
            .Select(r =>
            {
                var owner = active.FirstOrDefault(a => a.Revision.ContentId == (r.ContentId ?? item.Revision.ContentId));
                var label = owner?.Revision.Effects.OfType<ToggleEffect>().FirstOrDefault(t => t.ToggleId == r.ToggleId)?.Label;
                if (string.IsNullOrWhiteSpace(label))
                    label = r.ToggleId;
                return owner is not null && owner.Revision.ContentId != item.Revision.ContentId ? $"{label} ({owner.Revision.Name})" : label;
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (needed.Count == 0)
            return "Only available when no other form of its group is on.";
        return $"Needs {string.Join(" or ", needed)}" + (overlay.AllowedInBase == true ? ", or no other form of its group on." : ".");
    }

    /// <summary>Content v11 (D43): the toggles the groups are resolved over. Automatic toggles follow their condition and a toggle whose <c>level</c> is not reached is not offered.</summary>
    private static List<DeclaredToggle> DeclaredToggles(List<ActiveContent> active, Character character, ClassLevelMap classLevels) =>
    [
        .. active.SelectMany(item => item.Revision.Effects.OfType<ToggleEffect>()
            .Where(t => !IsAuto(item.Revision, t) && LevelReached(item, t, character, classLevels))
            .Select(t => new DeclaredToggle(item.Revision.ContentId, item.Revision.Name, t, item.Revision.Reference))),
    ];

    private static bool LevelReached(ActiveContent item, ToggleEffect toggle, Character character, ClassLevelMap classLevels) =>
        toggle.Level is not { } needed || GateLevel(item, character, classLevels) >= needed;

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
}
