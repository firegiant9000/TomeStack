namespace TomeStack.RulesCore;

/// <summary>A feature a class grants at a level (Import Lab, D62 L2).</summary>
public sealed record FeatureRef(Guid ContentId, string Name);

/// <summary>An option a class offers at a level through a choice: a listed option, or a published revision that extends the choice.</summary>
public sealed record ChoiceRef(Guid ContentId, string Name);

/// <param name="Scales">Scale values as text, by column name (<see cref="ClassTableView.Columns"/>); empty where the scale has no value for this level.</param>
/// <param name="Slots">Spell slots by spell level (1, 2, …) at this class level; null without ordinary slots at this level.</param>
/// <param name="PactSlots">Pact Magic slots at this class level; null without Pact Magic.</param>
/// <param name="PactLevel">The spell level of the Pact Magic slots; null when there are none at this class level.</param>
public sealed record ClassLevelRow(
    int Level, int ProficiencyBonus, IReadOnlyList<FeatureRef> Features, IReadOnlyList<ChoiceRef> Choices,
    IReadOnlyDictionary<string, string> Scales, IReadOnlyList<int>? Slots, int? PactSlots, int? PactLevel);

/// <summary>The 20-level progression of one class revision: one column per scale effect, one row per class level.</summary>
public sealed record ClassTableView(IReadOnlyList<string> Columns, IReadOnlyList<ClassLevelRow> Rows);

/// <summary>
/// Import Lab (D62 L2; design 6): the progression of one class revision as a 20-row grid. Pure: the catalog is read, never written.
/// The class's features and choices come from <see cref="ClassLevels.Walk"/>, so the catalog must hold only the chosen (latest
/// published) revision of each content, as the coverage view's does; the class revision itself may be a draft.
/// </summary>
public static class ClassProjection
{
    public const int Levels = 20;

    /// <param name="newestName">
    /// Display only (R11): the name of a content's newest revision, for a content with no published revision (a draft-only feature).
    /// Never used to decide what a class grants. Null when the caller has no such source.
    /// </param>
    public static ClassTableView Of(ContentRevision classRevision, IContentCatalog catalog, Func<Guid, string?>? newestName = null)
    {
        ArgumentNullException.ThrowIfNull(classRevision);
        ArgumentNullException.ThrowIfNull(catalog);

        var columns = new List<string>();
        var scales = new List<(string Column, IReadOnlyList<int> Values)>();
        foreach (var scale in classRevision.Effects.OfType<ScaleEffect>())
        {
            var name = string.IsNullOrWhiteSpace(scale.Label) ? scale.ScaleId : scale.Label;
            if (columns.Contains(name))
                name = $"{name} ({scale.ScaleId})";
            if (columns.Contains(name))
                continue; // the same scale id twice is invalid content; the first one is shown
            columns.Add(name);
            scales.Add((name, scale.Values));
        }

        var casters = classRevision.Effects.OfType<SpellcastingEffect>().ToList();
        var slotCaster = casters.FirstOrDefault(c => c.SlotKind == SpellSlotKind.SpellSlots);
        var pactCaster = casters.FirstOrDefault(c => c.SlotKind == SpellSlotKind.PactMagic);

        var walk = ClassLevels.Walk(classRevision, catalog);
        var names = new Dictionary<Guid, string>();
        string NameOf(Guid contentId)
        {
            if (!names.TryGetValue(contentId, out var name))
                names[contentId] = name = catalog.RevisionsOf(contentId).LastOrDefault(r => r.Status == RevisionStatus.Published)?.Name ?? newestName?.Invoke(contentId) ?? contentId.ToString("D");
            return name;
        }

        var rows = new List<ClassLevelRow>(Levels);
        for (var level = 1; level <= Levels; level++)
        {
            var here = walk.Where(w => w.Level == level).ToList();
            int? pactSlots = null;
            int? pactLevel = null;
            if (RowAt(pactCaster, level) is { } pactRow)
            {
                var spellLevel = pactRow.Select((n, i) => n > 0 ? i + 1 : 0).DefaultIfEmpty(0).Max();
                pactSlots = spellLevel > 0 ? pactRow[spellLevel - 1] : 0;
                pactLevel = spellLevel > 0 ? spellLevel : null;
            }
            rows.Add(new(
                level,
                DesignFeedback.ProficiencyBonus(level),
                [.. here.Where(w => !w.IsChoiceOption).Select(w => new FeatureRef(w.ContentId, NameOf(w.ContentId)))],
                [.. here.Where(w => w.IsChoiceOption).Select(w => new ChoiceRef(w.ContentId, NameOf(w.ContentId)))],
                scales.ToDictionary(s => s.Column, s => s.Values.Count >= level ? s.Values[level - 1].ToString(System.Globalization.CultureInfo.InvariantCulture) : ""),
                RowAt(slotCaster, level),
                pactSlots,
                pactLevel));
        }
        return new(columns, rows);
    }

    private static IReadOnlyList<int>? RowAt(SpellcastingEffect? caster, int level) =>
        caster is not null && caster.Slots.Count >= level ? caster.Slots[level - 1] : null;
}
