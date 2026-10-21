using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// <c>content.previewFormula</c> (D61): a formula to preview, optionally with the unsaved revision it belongs to (its own
/// scales are read), the levels to sample (null: 1, 5, 11, 16, 20) and whether values below 0 show as 0.
/// </summary>
public sealed record FormulaPreviewRequest(string? Formula, ContentRevision? Revision = null, IReadOnlyList<int>? Levels = null, bool FloorAtZero = false);

public sealed partial class TomeStackApp
{
    /// <summary>
    /// <c>content.previewFormula</c> (D61): what a formula would give at a few levels in a hypothetical context (ability
    /// scores 10, proficiency bonus by level). Read-only: it is not a character, and nothing is stored.
    /// </summary>
    public FormulaPreviewResult PreviewFormula(FormulaPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Formula is null)
            throw new AppValidationException([new("formula-preview.formula", "Give a formula to preview.")]);
        if (request.Levels is { } levels && (levels.Count is 0 or > 20 || levels.Any(l => l is < 1 or > 20)))
            throw new AppValidationException([new("formula-preview.levels", "Preview levels are 1 to 20, at most 20 of them.")]);
        if (request.Revision is { } revision && revision.Effects.Count > MaxCompareEffects)
            throw new AppValidationException([new("formula-preview.too-large", $"Formula preview covers revisions of up to {MaxCompareEffects} rules.", revision.Reference)]);
        return FormulaPreview.Preview(request.Formula, request.Revision, request.Levels, request.FloorAtZero);
    }
}
