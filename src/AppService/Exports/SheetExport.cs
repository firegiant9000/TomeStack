using TomeStack.RulesCore;

namespace TomeStack.AppService.Exports;

/// <summary>ADR-007 item 11: <c>share</c> (the default) or <c>personal</c>.</summary>
public enum SheetPurpose { Share, Personal }

/// <summary>
/// The sheet export model v1 (ADR-011 "Versioned data shapes", ADR-007 item 11; docs/schemas/sheet-export.v1.schema.json):
/// a character's computed sheet as extension export hooks and ADR-012 adapters read it. It is an allowlist: values,
/// automation status, names and texts, and nothing a path, an attachment id, a gap note, an override reason or a trace
/// could go in. Content from a source the purpose does not let out is dropped whole and counted in <see cref="Dropped"/>;
/// the totals keep its effect. Every source that contributes is in <see cref="Notices"/>.
/// </summary>
public sealed record SheetExport(
    string Format,
    int FormatVersion,
    SheetPurpose Purpose,
    DateTimeOffset ExportedAt,
    SheetCharacter Character,
    IReadOnlyList<SheetAbility> Abilities,
    IReadOnlyList<SheetSkill> Skills,
    IReadOnlyList<SheetField> Fields,
    SheetHitPoints HitPoints,
    IReadOnlyList<SheetHitDice> HitDice,
    IReadOnlyList<SheetSlots> Slots,
    SheetSlots? PactSlots,
    IReadOnlyList<SheetCaster> Spellcasting,
    IReadOnlyList<SheetResource> Resources,
    IReadOnlyList<SheetAttack> Attacks,
    IReadOnlyList<SheetFeature> Features,
    IReadOnlyList<SheetToggle> Toggles,
    IReadOnlyList<SheetScale> Scales,
    SheetConditions Conditions,
    IReadOnlyList<SheetDropped> Dropped,
    IReadOnlyList<SheetNotice> Notices)
{
    public const string FormatName = "tomestack.sheet";
    public const int CurrentFormatVersion = 1;
}

/// <param name="Name">The character's name as the player gave it.</param>
/// <param name="Level">Total character level.</param>
public sealed record SheetCharacter(string Name, string RulesFamily, int Level, IReadOnlyList<SheetClass> Classes);

/// <param name="Name">The class's name; for a class whose source is not let out, "Class (from &lt;source title&gt;)".</param>
/// <param name="Subclass">The chosen subclass's name, when its source is let out.</param>
/// <param name="Ref">Provenance: content and revision ids (null when the class is not let out).</param>
public sealed record SheetClass(string Name, int Level, int? HitDie, string? Subclass, ContentReference? Ref, string Source, ContentReference? SubclassRef = null);

public enum SheetProficiency { None, Proficient, Expertise }

public sealed record SheetAbility(string Ability, int Score, int Modifier, int Save, SheetProficiency SaveProficiency);

public sealed record SheetSkill(string Skill, string Label, string Ability, int Total, SheetProficiency Proficiency);

/// <summary>A total, from the allowlist in <see cref="SheetExportBuilder.ExportedFields"/>.</summary>
/// <param name="Overridden">The player overrode the calculated value (the reason is never exported).</param>
public sealed record SheetField(string Id, string Label, int Value, AutomationStatus Automation, bool Overridden);

public sealed record SheetHitPoints(int Maximum, int Current, int Temporary);

public sealed record SheetHitDice(int Die, int Total, int Spent);

public sealed record SheetSlots(int Level, int Maximum, int Spent);

public sealed record SheetCaster(string Name, string Ability, int AttackBonus, int SaveDc, int ClassLevel, string Preparation, IReadOnlyList<SheetSpell> Spells, ContentReference Ref, string Source);

public sealed record SheetSpell(string Name, int Level, bool Prepared, string? School, string? CastingTime, string? Range, string? Components, string? Duration, bool Concentration, bool Ritual, string? Summary, string? Text, ContentReference Ref, string Source);

public sealed record SheetResource(string Label, string Feature, int? Maximum, int Spent, IReadOnlyList<string> Recovery, ContentReference Ref, string Source);

public sealed record SheetAttack(string Name, int ToHit, string Damage, string? VersatileDamage, string DamageType, IReadOnlyList<string> Properties, string? Range, bool Proficient, ContentReference Ref, string Source);

public sealed record SheetFeature(string Name, string Kind, string? Summary, AutomationStatus Automation, IReadOnlyList<string> Texts, ContentReference Ref, string Source);

public sealed record SheetToggle(string Label, string Feature, bool On, ContentReference Ref, string Source);

public sealed record SheetScale(string Label, string ClassName, int Value, ContentReference Ref, string Source);

public sealed record SheetConditions(IReadOnlyList<string> Active, int Exhaustion);

/// <summary>What a source that is not let out lost from the file: its content is dropped whole (ADR-007 item 11).</summary>
public sealed record SheetDropped(string Source, string Publisher, int Items);

public sealed record SheetNotice(string Title, string Publisher, string License, bool Redistributable, string? Attribution, string? ModificationNotice, bool TotalsOnly);

/// <summary>
/// Builds the sheet export model from a character and its calculated sheet. <c>RulesCore</c> does not change: this reads
/// the sheet, the catalog and the sources, and applies the purpose filter of ADR-007 item 11.
/// </summary>
public static class SheetExportBuilder
{
    /// <summary>The totals exported, and only these (ADR-007 item 11 lists them as what always stays).</summary>
    public static IReadOnlyList<string> ExportedFields { get; } =
    [
        FieldIds.ArmorClass, FieldIds.Initiative, FieldIds.ProficiencyBonus, FieldIds.HitPoints, FieldIds.SpellAttack, FieldIds.SpellSaveDc,
    ];

    /// <summary>
    /// Whether a source's content goes out in full for <paramref name="purpose"/> (ADR-007 item 11). A shareable source
    /// (redistributable and not import-derived) always does. For <c>personal</c>, so does your own homebrew: made here and
    /// not import-derived, or of unknown origin (before database v8) once its author marked it as shareable.
    /// </summary>
    public static bool LetsOut(SourceRecord source, SheetPurpose purpose, IReadOnlySet<Guid> bundled)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.MayBeShared || bundled.Contains(source.Id))
            return true;
        if (purpose != SheetPurpose.Personal || source.ImportDerived == true)
            return false;
        return source.Origin == SourceOrigin.Local || (source.Origin is null && source.ShareConfirmedAt is not null);
    }

    public static SheetExport Build(Character character, CharacterSheet sheet, IContentCatalog catalog, SheetPurpose purpose, IReadOnlySet<Guid> bundled, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(catalog);
        var sources = new Dictionary<Guid, SourceRecord?>();
        var dropped = new Dictionary<Guid, int>();
        var contributing = new HashSet<Guid>();
        SourceRecord? SourceOf(ContentReference reference)
        {
            if (catalog.FindRevision(reference) is not { } revision)
                return null;
            var id = revision.Provenance.SourceId;
            if (!sources.TryGetValue(id, out var source))
                sources[id] = source = catalog.FindSource(id);
            return source;
        }
        // True when the content goes out; otherwise it is counted as dropped. Content whose source is unknown never goes out.
        bool Out(ContentReference reference, out string title)
        {
            var source = SourceOf(reference);
            title = source?.Title ?? "an unknown source";
            if (source is null)
                return false;
            contributing.Add(source.Id);
            if (LetsOut(source, purpose, bundled))
                return true;
            dropped[source.Id] = dropped.GetValueOrDefault(source.Id) + 1;
            return false;
        }
        // Every active revision contributes to the totals, so its source is noticed even when its content is dropped.
        foreach (var active in sheet.Active ?? [])
        {
            if (SourceOf(active) is { } source)
                contributing.Add(source.Id);
        }

        var classes = new List<SheetClass>();
        foreach (var entry in character.Classes)
        {
            var revision = catalog.FindRevision(entry.Class);
            var hitDie = revision?.Effects.OfType<HitDieEffect>().FirstOrDefault()?.Die;
            var subclass = (sheet.Choices ?? []).Where(c => c.Source.ContentId == entry.Class.ContentId)
                .SelectMany(c => c.Selected)
                .FirstOrDefault(s => catalog.FindRevision(s)?.Kind == ContentKind.Subclass);
            string? subclassName = null;
            ContentReference? subclassRef = null;
            if (subclass is not null && Out(subclass, out _))
            {
                subclassName = catalog.FindRevision(subclass)!.Name;
                subclassRef = subclass;
            }
            classes.Add(revision is not null && Out(entry.Class, out var classSource)
                ? new SheetClass(revision.Name, entry.Level, hitDie, subclassName, entry.Class, classSource, subclassRef)
                : new SheetClass($"Class (from {SourceOf(entry.Class)?.Title ?? "an unknown source"})", entry.Level, hitDie, null, null, SourceOf(entry.Class)?.Title ?? "an unknown source"));
        }

        var fields = sheet.Fields.ToDictionary(f => f.Field);
        SheetProficiency Proficiency(string id)
        {
            if (!fields.TryGetValue(id, out var field) || !fields.TryGetValue(FieldIds.ProficiencyBonus, out var pb) || pb.Value == 0)
                return SheetProficiency.None;
            var step = field.Trace.FirstOrDefault(t => t.Field == id && t.Operation == "add" && t.Inputs?.Any(i => i.Name == FieldIds.ProficiencyBonus) == true);
            return step?.Amount is not { } amount ? SheetProficiency.None : amount >= pb.Value * 2 ? SheetProficiency.Expertise : SheetProficiency.Proficient;
        }
        int Value(string id) => fields.TryGetValue(id, out var field) ? field.Value : 0;

        List<SheetAbility> abilities =
        [
            .. Enum.GetValues<Ability>().Select(a => new SheetAbility(FieldIds.Key(a), Value(FieldIds.Score(a)), Value(FieldIds.Modifier(a)), Value(FieldIds.Save(a)), Proficiency(FieldIds.Save(a)))),
        ];
        List<SheetSkill> skills =
        [
            .. CharacterCalculator.Skills.Select(s => new SheetSkill(s.Key, s.Label, FieldIds.Key(s.Ability), Value(FieldIds.Skill(s.Key)), Proficiency(FieldIds.Skill(s.Key)))),
        ];
        List<SheetField> exported =
        [
            .. ExportedFields.Where(fields.ContainsKey).Select(id => fields[id]).Select(f => new SheetField(f.Field, f.Label, f.Value, f.Automation, f.Override is not null)),
        ];

        var casters = new List<SheetCaster>();
        foreach (var caster in sheet.Spellcasting ?? [])
        {
            if (!Out(caster.Content, out var casterSource))
                continue;
            var spells = new List<SheetSpell>();
            foreach (var spell in caster.Spells)
            {
                if (Out(spell.Spell, out var spellSource))
                    spells.Add(new(spell.Name, spell.Level, spell.Prepared, spell.School, spell.CastingTime, spell.Range, spell.Components, spell.Duration, spell.Concentration, spell.Ritual, spell.Summary, spell.Text, spell.Spell, spellSource));
            }
            casters.Add(new(caster.Name, FieldIds.Key(caster.Ability), caster.AttackBonus, caster.SaveDc, caster.ClassLevel, caster.Preparation.ToString().ToLowerInvariant(), spells, caster.Content, casterSource));
        }

        List<SheetResource> resources = [];
        foreach (var resource in sheet.Resources ?? [])
        {
            if (Out(resource.Content, out var source))
                resources.Add(new(resource.Label, resource.ContentName, resource.Maximum, resource.Spent, [.. resource.Recoveries.Select(r => r.All ? $"{r.On}: all" : $"{r.On}: {r.Amount}")], resource.Content, source));
        }
        List<SheetAttack> attacks = [];
        foreach (var attack in sheet.Attacks ?? [])
        {
            if (Out(attack.Item, out var source))
                attacks.Add(new(attack.Name, attack.ToHit, attack.Damage, attack.VersatileDamage, attack.DamageType, attack.Properties, attack.Range, attack.Proficient, attack.Item, source));
        }
        List<SheetFeature> features = [];
        foreach (var feature in sheet.Features ?? [])
        {
            if (Out(feature.Content, out var source))
                features.Add(new(feature.Name, feature.Kind.ToString().ToLowerInvariant(), feature.Summary, feature.Automation, [.. feature.Effects.Select(e => e.Text).OfType<string>()], feature.Content, source));
        }
        List<SheetToggle> toggles = [];
        foreach (var toggle in sheet.Toggles ?? [])
        {
            if (Out(toggle.Content, out var source))
                toggles.Add(new(toggle.Label, toggle.ContentName, toggle.On, toggle.Content, source));
        }
        List<SheetScale> scales = [];
        foreach (var scale in sheet.Scales ?? [])
        {
            if (Out(scale.Content, out var source))
                scales.Add(new(scale.Label, scale.ClassName, scale.Value, scale.Content, source));
        }

        var hitPoints = sheet.HitPoints ?? new HitPointState(Value(FieldIds.HitPoints), Value(FieldIds.HitPoints), 0);
        var notices = contributing.Select(id => sources.GetValueOrDefault(id)).OfType<SourceRecord>().OrderBy(s => s.Title, StringComparer.Ordinal)
            .Select(s => new SheetNotice(s.Title, s.Publisher, s.License, s.Redistributable, s.Attribution, s.ModificationNotice, !LetsOut(s, purpose, bundled)))
            .ToList();
        var droppedList = dropped.Where(d => d.Value > 0).Select(d => sources[d.Key]!).OrderBy(s => s.Title, StringComparer.Ordinal)
            .Select(s => new SheetDropped(s.Title, s.Publisher, dropped[s.Id])).ToList();

        return new SheetExport(
            SheetExport.FormatName, SheetExport.CurrentFormatVersion, purpose, now,
            new SheetCharacter(character.Name, character.RulesFamily, character.Classes.Count > 0 ? character.Classes.Sum(c => c.Level) : character.Level, classes),
            abilities, skills, exported,
            new SheetHitPoints(hitPoints.Maximum, hitPoints.Current, hitPoints.Temporary),
            [.. (sheet.HitDice ?? []).Select(h => new SheetHitDice(h.Die, h.Total, h.Spent))],
            [.. (sheet.SpellSlots ?? []).Select(s => new SheetSlots(s.Level, s.Maximum, s.Spent))],
            sheet.PactSlots is { } pact ? new SheetSlots(pact.Level, pact.Maximum, pact.Spent) : null,
            casters, resources, attacks, features, toggles, scales,
            new SheetConditions([.. character.Play.Conditions], character.Play.Exhaustion),
            droppedList, notices);
    }
}
