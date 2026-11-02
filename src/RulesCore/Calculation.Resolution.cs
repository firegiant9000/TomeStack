namespace TomeStack.RulesCore;

public static partial class CharacterCalculator
{
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

        /// <summary>Content v11 (D43): the named values of each class root, keyed by the class and the value id (<see cref="CollectValues"/>).</summary>
        public Dictionary<(ContentReference Class, string ValueId), (ActiveContent Content, ValueEffect Effect)> NamedValues { get; } = [];

        /// <summary>What each value came to in this pass (null: it is disabled). Values are evaluated lazily, once.</summary>
        public Dictionary<(ContentReference Class, string ValueId), int?> ValueResults { get; } = [];

        /// <summary>The values being evaluated right now, outermost first; a read of one of them again is a cycle.</summary>
        public List<(ContentReference Class, string ValueId)> ValueStack { get; } = [];

        /// <summary>The values that lie on a cycle; they stay disabled for the whole pass.</summary>
        public HashSet<(ContentReference Class, string ValueId)> ValueCycles { get; } = [];

        /// <summary>The pass's diagnostics, where a value read that fails says why (set by <see cref="CollectValues"/>).</summary>
        public List<Diagnostic>? Diagnostics { get; set; }
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
                if (scale.Values.Count != Character.MaxLevel || !ScaleEffect.IsValidScaleId(scale.ScaleId) || scale.Values.Any(v => v is < 0 or > FormulaLimits.MaxLiteral))
                {
                    // Validation refuses this on publish and import; stored content from elsewhere is isolated here (SPEC C-03).
                    diagnostics.Add(new("scale.invalid", $"'{owner.Revision.Name}' scale '{scale.Id}' needs a valid scale id and {Character.MaxLevel} values of 0 to {FormulaLimits.MaxLiteral}; it is ignored.", owner.Revision.Reference, scale.Id));
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

    /// <summary>
    /// Content v11 (D43): the named values of every class root, from all active content of that root (any kind) in sheet order.
    /// When two declare one value id, the later applies and <c>value.duplicate</c> says so (informational: it carries the content
    /// but no effect id, so it does not downgrade the content's automation). A value on content with no class root can never be
    /// read, so it is ignored; so is one that is not automatic or has no usable id (validation refuses both with
    /// <c>validate.value-incomplete</c> on publish and import, so this is a defence for stored content from elsewhere).
    /// </summary>
    private static void CollectValues(List<ActiveContent> active, ClassLevelMap classLevels, List<Diagnostic> diagnostics)
    {
        classLevels.Diagnostics = diagnostics;
        foreach (var owner in active.Where(a => a.ClassRoot is not null && a.Revision.SchemaVersion >= ValueEffect.SchemaVersion))
        {
            foreach (var value in owner.Revision.Effects.OfType<ValueEffect>().Where(v => v.Automation == AutomationStatus.Automatic))
            {
                if (!ScaleEffect.IsValidScaleId(value.ValueId) || string.IsNullOrWhiteSpace(value.Formula))
                    continue;
                var key = (owner.ClassRoot!, value.ValueId);
                if (classLevels.NamedValues.TryGetValue(key, out var earlier))
                {
                    diagnostics.Add(new(
                        "value.duplicate",
                        $"'{owner.Revision.Name}' declares value '{value.ValueId}', which '{earlier.Content.Revision.Name}' also declares for this class; the later one, from '{owner.Revision.Name}', is used.",
                        owner.Revision.Reference));
                }
                classLevels.NamedValues[key] = (owner, value);
            }
        }
    }

    /// <summary>
    /// <c>VALUE.&lt;id&gt;</c> read by <paramref name="reader"/>: the named value its class root declares, evaluated in the context of
    /// the content that declares it (its class level, scales, and the fields calculated so far), once per pass. Null when the read
    /// is outside a class (<c>value.no-class</c>), nothing declares the value (<c>value.unknown</c>), the value is disabled because
    /// it lies on a cycle or reads one (<c>value.cycle</c>), or its own formula fails (<c>effect.invalid-formula</c> on the
    /// declaring content). It never reads as 0; the caller's formula fails like any unresolved identifier.
    /// </summary>
    private static int? ResolveValue(string identifier, ActiveContent reader, Character character, ClassLevelMap classLevels, Dictionary<string, int> values)
    {
        var valueId = FormulaIdentifiers.ValueId(identifier);
        var readerRevision = reader.Revision;
        if (reader.ClassRoot is not { } root)
        {
            ReportValue(classLevels, new("value.no-class", $"'{readerRevision.Name}' reads {identifier}, but it is not part of a class; named values are read within a class, so the formula is disabled.", readerRevision.Reference));
            return null;
        }
        var key = (root, valueId);
        if (!classLevels.NamedValues.TryGetValue(key, out var declared))
        {
            ReportValue(classLevels, new("value.unknown", $"'{readerRevision.Name}' reads {identifier}, which no content of this class declares; the formula is disabled.", readerRevision.Reference));
            return null;
        }

        int? result;
        if (classLevels.ValueResults.TryGetValue(key, out var done))
        {
            result = done;
        }
        else if (classLevels.ValueStack.Contains(key))
        {
            // Re-entry: every value from the first read of this one to the top of the stack is on the cycle.
            foreach (var member in classLevels.ValueStack.Skip(classLevels.ValueStack.IndexOf(key)))
                classLevels.ValueCycles.Add(member);
            result = null;
        }
        else if (classLevels.ValueStack.Count >= MaxValueDepth)
        {
            // Nested too deep (bounds the recursion): the read fails, and the formula that made it reports itself through the usual path.
            // The cut value is cached as null for this sheet pass, and so is every value waiting on it up the stack (each fails and caches
            // null in turn), so the outcome never depends on how often or from where a value is read.
            result = null;
            classLevels.ValueResults[key] = result;
        }
        else
        {
            classLevels.ValueStack.Add(key);
            try
            {
                var revision = declared.Content.Revision;
                FormulaError? failure;
                if (Formula.TryParse(declared.Effect.Formula, Formula.FeaturesFor(revision), out var formula, out failure)
                    && formula!.TryEvaluate(id => Resolve(id, declared.Content, character, classLevels, values, []), out var value, out failure))
                {
                    result = value;
                }
                else
                {
                    result = null;
                    // A value that fails because a value it reads is on a cycle is disabled with it, so its readers hear value.cycle too.
                    if (formula is not null && formula.Identifiers.Any(i => FormulaIdentifiers.IsValue(i)
                        && declared.Content.ClassRoot is { } declaredRoot && classLevels.ValueCycles.Contains((declaredRoot, FormulaIdentifiers.ValueId(i)))))
                        classLevels.ValueCycles.Add(key);
                    if (!classLevels.ValueCycles.Contains(key))
                        ReportValue(classLevels, InvalidFormula(revision, declared.Effect, failure!));
                }
            }
            finally
            {
                classLevels.ValueStack.RemoveAt(classLevels.ValueStack.Count - 1);
            }
            classLevels.ValueResults[key] = result;
        }

        if (result is null && classLevels.ValueCycles.Contains(key))
        {
            ReportValue(classLevels, new(
                "value.cycle",
                $"'{readerRevision.Name}' reads {identifier}, which depends on itself through '{declared.Content.Revision.Name}'; the value and every formula reading it are disabled.",
                readerRevision.Reference));
        }
        return result;
    }

    /// <summary>How many named values may be read inside one another; a deeper chain fails (content is validated far below this).</summary>
    private const int MaxValueDepth = 32;

    private static void ReportValue(ClassLevelMap classLevels, Diagnostic diagnostic)
    {
        if (classLevels.Diagnostics is { } diagnostics)
            AddOnce(diagnostics, diagnostic);
    }

    /// <summary>
    /// The fields a formula of <paramref name="reader"/> reads, including (content v11) those read by the value formulas it names,
    /// transitively; a value met again is not followed, so a cycle among values ends here (the value resolution disables it).
    /// </summary>
    private static List<string> FieldReads(Formula formula, ActiveContent reader, ClassLevelMap classLevels)
    {
        var fields = new List<string>();
        var followed = new HashSet<(ContentReference, string)>();
        void Collect(Formula source, ActiveContent context, int depth)
        {
            foreach (var identifier in source.Identifiers)
            {
                if (FormulaIdentifiers.FieldFor(identifier) is { } field)
                {
                    if (!fields.Contains(field, StringComparer.Ordinal))
                        fields.Add(field);
                }
                else if (depth < MaxValueDepth && FormulaIdentifiers.IsValue(identifier) && context.ClassRoot is { } root
                    && classLevels.NamedValues.TryGetValue((root, FormulaIdentifiers.ValueId(identifier)), out var declared)
                    && followed.Add((root, declared.Effect.ValueId))
                    && Formula.TryParse(declared.Effect.Formula, Formula.FeaturesFor(declared.Content.Revision), out var inner, out _))
                {
                    Collect(inner!, declared.Content, depth + 1);
                }
            }
        }
        Collect(formula, reader, 0);
        return fields;
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

        // The attunement gate (Wave 3 item 25): an item that needs attunement applies only while an entry of it is recorded as attuned.
        bool NeedsAttunement(ActiveContent item) => item.Revision.Kind == ContentKind.Item && item.Revision.Effects.OfType<ItemEffect>().Any(e => e.RequiresAttunement == true);
        // R189: content a grant or a choice brings in passes the same gate, through an attuned equipped copy.
        bool AttunedCopyEquipped(ContentReference reference) => character.Equipment.Any(e => e.Equipped && e.Item == reference && e.Attuned == true);
        void NotAttuned(ContentReference reference) =>
            diagnostics.Add(new("item.not-attuned", "An equipped or pinned item needs attunement, which is not recorded; its effects are not applied. Mark it attuned in the Inventory.", reference));
        foreach (var pin in character.Pins.Concat(character.Classes.Select(c => c.Class)))
        {
            if (!seen.Add(pin) || Admit(pin, null) is not { } item)
                continue;
            // An item-kind pin passes the same gate as an equipped item (an attuned entry of it, equipped or not), so a package pin
            // or a pin that is also equipped cannot bypass attunement.
            if (NeedsAttunement(item) && !character.Equipment.Any(e => e.Item == pin && e.Attuned == true))
            {
                NotAttuned(pin);
                continue;
            }
            active.Add(item);
        }
        // Equipped items apply like pins (M2 item 4); carried but unequipped items do not.
        // Wave 3 item 25 (R159): an item equipped twice applies once, and an item that needs attunement (content v10, Q13) applies only
        // while at least one equipped entry of it is attuned. A revision below v10 has no typed item effect, so no gate.
        var equipped = character.Equipment.Where(e => e.Equipped).ToList();
        foreach (var entry in equipped)
        {
            if (!seen.Add(entry.Item) || Admit(entry.Item, null) is not { } item)
                continue;
            if (item.Revision.Kind != ContentKind.Item)
            {
                diagnostics.Add(new("equipment.not-an-item", $"'{item.Revision.Name}' is equipped but is {item.Revision.Kind.ToString().ToLowerInvariant()} content, not an item; it is not applied.", entry.Item));
                continue;
            }
            if (NeedsAttunement(item) && !equipped.Any(e => e.Item == entry.Item && e.Attuned == true))
            {
                NotAttuned(entry.Item);
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
                        if (NeedsAttunement(granted) && !AttunedCopyEquipped(reference))
                        {
                            NotAttuned(reference);
                            continue;
                        }
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
                        if (NeedsAttunement(chosen) && !AttunedCopyEquipped(option))
                        {
                            NotAttuned(option);
                            continue;
                        }
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
        CollectValues(active, classLevels, diagnostics);
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
}
