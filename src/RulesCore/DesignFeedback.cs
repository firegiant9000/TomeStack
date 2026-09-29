namespace TomeStack.RulesCore;

/// <param name="Family">The rules family the hint compares against, when it depends on one.</param>
/// <param name="Level">The class level the hint is about, when it is about one.</param>
public sealed record DesignHint(string Code, string Message, string? EffectId = null, string? Family = null, int? Level = null);

/// <summary>
/// M5 slice 7 (owner decision LIVING_SPECS D14): design feedback, a read-only analyzer that compares homebrew with the
/// bundled SRD classes of its family. The owner's hint set, and nothing else:
/// <list type="bullet">
/// <item><c>design.slots-above-full-caster</c>: more slots of a spell level at a class level than any SRD full caster has;</item>
/// <item><c>design.multiclass-share-above-table</c>: a multiclass share whose Multiclass Spellcaster table row gives more
/// slots than the class's own table at that level;</item>
/// <item><c>design.resource-faster-than-pb</c>: a resource maximum that grows more from level 1 to 20 than the proficiency
/// bonus does (+4);</item>
/// <item><c>design.level-without-feature</c>: class levels where no feature or choice is gained although every bundled
/// class of the family gains one.</item>
/// </list>
/// Hints are opinions, never errors: they never block publishing, never change a calculation, and are never stored or
/// exported. The feature is off by default (a studio setting).
/// </summary>
public static class DesignFeedback
{
    /// <summary>Proficiency bonus by level, as the calculator derives it (a rules constant, not a policy field).</summary>
    public static int ProficiencyBonus(int level) => 2 + ((level - 1) / 4);

    /// <param name="bundled">
    /// The newest published revision of each bundled (SRD) content. Full casters are found by their spellcasting effect,
    /// whatever content holds it (the SRD packs put it on a class's granted "Spellcasting" feature).
    /// </param>
    public static IReadOnlyList<DesignHint> Analyze(ContentRevision revision, IEnumerable<ContentRevision> bundled)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(bundled);
        var hints = new List<DesignHint>();
        var srd = bundled.ToList();
        var caster = CalculatedSlots(revision);
        foreach (var family in revision.RulesFamilies.Where(RulesFamilies.IsKnown).Distinct())
        {
            if (caster is not null)
            {
                SlotsAboveFullCaster(caster, family, srd, hints);
                ShareAboveTable(revision, caster, family, hints);
            }
            if (revision.Kind == ContentKind.Class)
                LevelsWithoutFeature(revision, family, srd, hints);
        }
        ResourcesFasterThanPb(revision, hints);
        return hints;
    }

    /// <summary>
    /// The spellcasting the calculator uses (review fix): the revision's first one that is not reference-only, and only
    /// when it has ordinary spell slots and a well-formed table (<see cref="CharacterCalculator.SpellcastingProblem"/>).
    /// Anything else (Pact Magic, a malformed table) gets no slot hint.
    /// </summary>
    private static SpellcastingEffect? CalculatedSlots(ContentRevision revision) =>
        revision.Effects.OfType<SpellcastingEffect>().FirstOrDefault(s => s.Automation != AutomationStatus.Reference) is { SlotKind: SpellSlotKind.SpellSlots } caster
            && CharacterCalculator.SpellcastingProblem(caster) is null
            ? caster
            : null;

    private static void SlotsAboveFullCaster(SpellcastingEffect caster, string family, List<ContentRevision> srd, List<DesignHint> hints)
    {
        var full = srd
            .Where(r => r.RulesFamilies.Contains(family))
            .Select(CalculatedSlots)
            .OfType<SpellcastingEffect>()
            .Where(s => s.MulticlassCaster == MulticlassCaster.Full)
            .ToList();
        if (full.Count == 0)
            return; // nothing to compare with
        for (var level = 1; level <= Character.MaxLevel; level++)
        {
            var own = caster.Slots[level - 1];
            for (var spell = 1; spell <= own.Count; spell++)
            {
                var most = full.Max(f => f.Slots[level - 1].ElementAtOrDefault(spell - 1));
                if (own[spell - 1] > most)
                {
                    hints.Add(new("design.slots-above-full-caster",
                        $"At class level {level} it has {own[spell - 1]} level-{spell} slot(s); the most any SRD full caster has at that level is {most}.",
                        caster.Id, family, level));
                    return; // the first level is enough to see the pattern
                }
            }
        }
    }

    private static void ShareAboveTable(ContentRevision revision, SpellcastingEffect caster, string family, List<DesignHint> hints)
    {
        var policy = RulesFamilies.Get(family);
        var table = revision.SchemaVersion >= SpellcastingEffect.MulticlassTableSchemaVersion && caster.MulticlassCaster is null
            && caster.MulticlassCasterTable is { } t && ContentValidator.MulticlassTableProblem(t) is null ? t : null;
        if (caster.MulticlassCaster is null && table is null)
            return; // its slots are never combined
        static int Fraction(int level, int divisor, CasterLevelRounding rounding) =>
            rounding == CasterLevelRounding.Up ? (level + divisor - 1) / divisor : level / divisor;
        for (var level = 1; level <= Character.MaxLevel; level++)
        {
            var casterLevel = table is not null ? table[level - 1] : caster.MulticlassCaster switch
            {
                MulticlassCaster.Half => Fraction(level, 2, policy.HalfCasterLevels),
                MulticlassCaster.Third => Fraction(level, 3, policy.ThirdCasterLevels),
                _ => level,
            };
            if (casterLevel == 0)
                continue;
            var shared = policy.MulticlassSpellSlots[Math.Min(casterLevel, Character.MaxLevel) - 1];
            var own = caster.Slots[level - 1];
            if (shared.Sum() > own.Sum() || shared.Count > own.Count(n => n > 0))
            {
                hints.Add(new("design.multiclass-share-above-table",
                    $"At class level {level} its multiclass share counts {casterLevel} caster level(s), which the Multiclass Spellcaster table turns into {Describe(shared)}; its own table gives {Describe(own)}. A multiclass character could get more slots than a single-class one.",
                    caster.Id, family, level));
                return;
            }
        }
    }

    private static string Describe(IReadOnlyList<int> row) =>
        row.All(n => n == 0) ? "no slots" : string.Join(", ", row.Select((n, i) => (n, i)).Where(x => x.n > 0).Select(x => $"{x.n} of level {x.i + 1}"));

    private static void ResourcesFasterThanPb(ContentRevision revision, List<DesignHint> hints)
    {
        var scales = revision.SchemaVersion >= ScaleEffect.SchemaVersion
            ? revision.Effects.OfType<ScaleEffect>().Where(s => s.Values.Count == Character.MaxLevel).GroupBy(s => s.ScaleId).ToDictionary(g => g.Key, g => g.First().Values, StringComparer.Ordinal)
            : [];
        var growthOfPb = ProficiencyBonus(Character.MaxLevel) - ProficiencyBonus(Character.MinLevel);
        foreach (var resource in revision.Effects.OfType<ResourceEffect>().Where(r => r.Automation != AutomationStatus.Reference))
        {
            if (!Formula.TryParse(resource.Maximum, revision.SchemaVersion >= ScaleEffect.SchemaVersion, out var formula, out _))
                continue; // validation reports it
            int? At(int level) => formula!.TryEvaluate(id => id switch
            {
                FormulaIdentifiers.ProficiencyBonus => ProficiencyBonus(level),
                FormulaIdentifiers.Level or FormulaIdentifiers.ClassLevel => level,
                _ when FormulaIdentifiers.IsScale(id) => scales.TryGetValue(FormulaIdentifiers.ScaleId(id), out var values) ? values[level - 1] : null,
                // Ability scores of 10 (modifier +0): the hint is about growth with level, not about a character.
                _ when id.EndsWith(".MOD", StringComparison.Ordinal) => 0,
                _ when id.EndsWith(".SCORE", StringComparison.Ordinal) => 10,
                _ => null,
            }, out var value, out _) ? value : null;
            if (At(Character.MinLevel) is { } first && At(Character.MaxLevel) is { } last && last - first > growthOfPb)
            {
                // The owner's rule is literal: some SRD pools grow faster too (a pool of five times the class level), so
                // this is a prompt to check the design, not a claim that it is wrong.
                hints.Add(new("design.resource-faster-than-pb",
                    $"'{resource.ResourceId}' grows from {first} to {last} uses between levels 1 and 20 (+{last - first}); the proficiency bonus grows by +{growthOfPb}. Some SRD pools grow faster too, so check it is meant.",
                    resource.Id));
            }
        }
    }

    /// <summary>The class levels at which a class gains a content grant or a choice (one with no level counts at level 1).</summary>
    private static HashSet<int> LevelsWithSomething(ContentRevision revision) =>
        [.. revision.Effects
            .Select(e => e switch
            {
                GrantEffect { Grant: GrantKind.Content } g => g.Level ?? Character.MinLevel,
                ChoiceEffect c => c.Level ?? Character.MinLevel,
                _ => 0,
            })
            .Where(l => l >= Character.MinLevel)];

    /// <summary>
    /// Levels where this class gains nothing although every bundled class of the family gains something (review fix: many
    /// SRD levels bring only slots, a column or a subclass feature, which are not class grants, so the SRD itself is the
    /// measure; no bundled class trips this).
    /// </summary>
    private static void LevelsWithoutFeature(ContentRevision revision, string family, List<ContentRevision> srd, List<DesignHint> hints)
    {
        var classes = srd.Where(r => r.Kind == ContentKind.Class && r.RulesFamilies.Contains(family)).Select(LevelsWithSomething).ToList();
        if (classes.Count == 0)
            return; // nothing to compare with
        var own = LevelsWithSomething(revision);
        var empty = Enumerable.Range(Character.MinLevel, Character.MaxLevel).Where(l => !own.Contains(l) && classes.All(c => c.Contains(l))).ToList();
        if (empty.Count > 0)
            hints.Add(new("design.level-without-feature", $"Class level(s) {string.Join(", ", empty)} give no feature or choice, although every bundled SRD class gives one there.", Family: family));
    }
}
