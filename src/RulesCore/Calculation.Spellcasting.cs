namespace TomeStack.RulesCore;

public static partial class CharacterCalculator
{
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
                if (Formula.TryParse(source, Formula.FeaturesFor(content.Revision), out var formula, out var failure)
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
}
