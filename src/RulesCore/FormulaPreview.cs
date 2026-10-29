namespace TomeStack.RulesCore;

/// <summary>One sample class level and what the formula gives there; <paramref name="Problem"/> is set (and <paramref name="Value"/> null) when it fails at that level.</summary>
public sealed record FormulaPreviewPoint(int Level, int? Value, string? Problem);

/// <param name="Formula">The formula as given.</param>
/// <param name="Error">A parse error; <paramref name="Points"/> is then empty.</param>
/// <param name="Unavailable">Why an identifier cannot be previewed here; <paramref name="Points"/> is then empty.</param>
public sealed record FormulaPreviewResult(string Formula, FormulaError? Error, string? Unavailable, IReadOnlyList<FormulaPreviewPoint> Points);

/// <summary>
/// Owner decision LIVING_SPECS D61: evaluates a homebrew formula at sample class levels in a hypothetical context, so the
/// studio can show what it gives before anything is saved. Read-only and hypothetical: every ability score is
/// <see cref="SampleScore"/> (so every <c>.MOD</c> is 0), the proficiency bonus comes from the level, and
/// <c>SCALE.&lt;id&gt;</c> reads the draft's own automatic 20-value columns. Anything else cannot be previewed. It never
/// throws on caller input, writes nothing and changes no calculation. Design feedback shares the same context.
/// </summary>
public static class FormulaPreview
{
    /// <summary>The class levels a preview samples by default.</summary>
    public static IReadOnlyList<int> SampleLevels { get; } = [1, 5, 11, 16, 20];

    /// <summary>The ability score every ability reads as in a preview.</summary>
    public const int SampleScore = 10;

    /// <summary>
    /// The revision's own automatic columns with all 20 values, each 0 to <see cref="FormulaLimits.MaxLiteral"/>, when it is a
    /// class or subclass, as the calculator takes them (the first per scale id). Empty when there is no revision.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<int>> Scales(ContentRevision? revision) =>
        revision is null || revision.Kind is not (ContentKind.Class or ContentKind.Subclass)
            ? new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            : revision.Effects.OfType<ScaleEffect>()
                .Where(s => s.Automation == AutomationStatus.Automatic && s.Values.Count == Character.MaxLevel
                    && s.Values.All(v => v is >= 0 and <= FormulaLimits.MaxLiteral))
                .GroupBy(s => s.ScaleId)
                .ToDictionary(g => g.Key, g => g.First().Values, StringComparer.Ordinal);

    /// <summary>
    /// The revision's own automatic named values (content v11, <c>VALUE.&lt;id&gt;</c>) with their formulas, the first per id, when it
    /// is a v11 revision; empty otherwise and when there is no revision. A value of any other entry is not previewable here.
    /// </summary>
    public static IReadOnlyDictionary<string, string> NamedValues(ContentRevision? revision) =>
        revision is null || revision.SchemaVersion < ValueEffect.SchemaVersion
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : revision.Effects.OfType<ValueEffect>()
                .Where(v => v.Automation == AutomationStatus.Automatic && ScaleEffect.IsValidScaleId(v.ValueId) && !string.IsNullOrWhiteSpace(v.Formula))
                .GroupBy(v => v.ValueId)
                .ToDictionary(g => g.Key, g => g.First().Formula, StringComparer.Ordinal);

    /// <summary>The value of an identifier at a class level in the hypothetical context, or null when it cannot be previewed.</summary>
    public static int? Resolve(string identifier, int level, IReadOnlyDictionary<string, IReadOnlyList<int>> scales, IReadOnlyDictionary<string, string>? namedValues = null) =>
        Resolve(identifier, level, scales, namedValues, new());

    /// <summary>How many named values may be read inside one another in a preview; a deeper chain is not previewable.</summary>
    private const int MaxValueDepth = 32;

    /// <summary>The values being evaluated (outermost first, for the cycle and depth checks) and what each came to, null results included.</summary>
    private sealed class ValueState
    {
        public HashSet<string> InProgress { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int?> Done { get; } = new(StringComparer.Ordinal);
    }

    private static int? Resolve(
        string identifier, int level, IReadOnlyDictionary<string, IReadOnlyList<int>> scales, IReadOnlyDictionary<string, string>? namedValues, ValueState state) => identifier switch
    {
        FormulaIdentifiers.ProficiencyBonus => DesignFeedback.ProficiencyBonus(level),
        FormulaIdentifiers.Level or FormulaIdentifiers.ClassLevel => level,
        _ when FormulaIdentifiers.IsScale(identifier) =>
            level is >= Character.MinLevel and <= Character.MaxLevel && scales.TryGetValue(FormulaIdentifiers.ScaleId(identifier), out var values) ? values[level - 1] : null,
        _ when FormulaIdentifiers.IsValue(identifier) => NamedValue(FormulaIdentifiers.ValueId(identifier), level, scales, namedValues, state),
        _ when identifier.EndsWith(".MOD", StringComparison.Ordinal) => 0,
        _ when identifier.EndsWith(".SCORE", StringComparison.Ordinal) => SampleScore,
        _ => null,
    };

    /// <summary>A named value the revision declares, evaluated in the same context; a value reading itself (directly or through others) has no value.</summary>
    private static int? NamedValue(
        string valueId, int level, IReadOnlyDictionary<string, IReadOnlyList<int>> scales, IReadOnlyDictionary<string, string>? namedValues, ValueState state)
    {
        if (namedValues is null || !namedValues.TryGetValue(valueId, out var source))
            return null;
        if (state.Done.TryGetValue(valueId, out var done))
            return done;
        if (state.InProgress.Count >= MaxValueDepth || !state.InProgress.Add(valueId))
            return null;
        int? result;
        try
        {
            result = Formula.TryParse(source, FormulaFeatures.Scales | FormulaFeatures.Values, out var formula, out _)
                && formula!.TryEvaluate(id => Resolve(id, level, scales, namedValues, state), out var value, out _)
                ? value
                : null;
        }
        finally
        {
            state.InProgress.Remove(valueId);
        }
        state.Done[valueId] = result;
        return result;
    }

    /// <summary>
    /// Evaluates <paramref name="formula"/> at <paramref name="levels"/> (default <see cref="SampleLevels"/>) against <paramref name="revision"/>'s columns.
    /// <paramref name="floorAtZero"/> shows a value below 0 as 0, as the sheet does for resource uses, recoveries and spells.
    /// </summary>
    public static FormulaPreviewResult Preview(string formula, ContentRevision? revision, IReadOnlyList<int>? levels = null, bool floorAtZero = false)
    {
        // VALUE parses in a v11 revision (a draft that is v11 too); a value this revision does not declare reads as unresolved, like an unknown SCALE.
        var features = FormulaFeatures.Scales | (revision is { SchemaVersion: >= ValueEffect.SchemaVersion } ? FormulaFeatures.Values : FormulaFeatures.None);
        if (!Formula.TryParse(formula, features, out var parsed, out var error))
            return new(formula, error, null, []);
        var scales = Scales(revision);
        var namedValues = NamedValues(revision);
        foreach (var id in parsed!.Identifiers.Order(StringComparer.Ordinal))
        {
            if (Resolve(id, Character.MinLevel, scales, namedValues) is null)
            {
                var message = FormulaIdentifiers.IsScale(id)
                    ? $"{id} is not a column of this entry, so it can't be previewed here."
                    : FormulaIdentifiers.IsValue(id)
                        ? $"{id} is not a value this entry declares, so it can't be previewed here."
                        : $"{id} can't be previewed here.";
                return new(formula, null, message, []);
            }
        }
        var points = (levels ?? SampleLevels)
            .Select(level => parsed.TryEvaluate(id => Resolve(id, level, scales, namedValues), out var value, out var problem)
                ? new FormulaPreviewPoint(level, floorAtZero ? Math.Max(value, 0) : value, null)
                : new FormulaPreviewPoint(level, null, problem!.Message))
            .ToList();
        return new(formula, null, null, points);
    }
}
