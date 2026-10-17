using TomeStack.RulesCore;

namespace TomeStack.AppService.CharacterImport;

/// <summary>
/// Matches what a sheet names against installed content and places each match on a new character
/// (<c>features/ddb-pdf-import.md</c> "Matching rules"), in the spec's order, recalculating the draft between steps:
/// classes and levels, species, background, subclass choices, then feats, skills, equipment, spells and features. A
/// choice is answered only through the builder's check (<c>WithChoice</c>), and every refusal becomes a row note, never an
/// error. Names are proposals: a name installed twice asks the user, and nothing is merged by name. Nothing is stored.
/// </summary>
internal sealed class ImportPlanner
{
    /// <summary>The builder's check: the character with one choice answered, or an <see cref="AppValidationException"/>.</summary>
    public delegate Character Choose(Character character, ContentReference source, string choiceId, IReadOnlyList<ContentReference> selected);

    private readonly Func<Character, CharacterSheet> _calculate;
    private readonly Choose _choose;
    private readonly Func<ContentReference, ContentRevision?> _find;
    private readonly Dictionary<(ContentKind, string), List<ContentOption>> _byName = [];
    private readonly Dictionary<Guid, ContentOption> _byContent = [];
    private readonly Dictionary<string, Resolution> _resolutions = new(StringComparer.Ordinal);
    private readonly List<MatchRow> _rows = [];
    private readonly HashSet<string> _chosenByUser = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ContentReference> _classes = [];
    private readonly bool _equipMatched;
    private readonly StubRequest _stub;
    private readonly List<StubOffer> _offers = [];
    private readonly List<string> _lines = [];
    private Character _character;

    /// <param name="options">The listing for the chosen family and campaign: published, compatible and current only.</param>
    /// <param name="find">Finds a revision through the catalog the calculation uses (the overlay that holds the stub class, item 15).</param>
    /// <param name="stub">Item 15 (D35): the stub class request; its row is created only when the sheet's hit dice settle its die.</param>
    public ImportPlanner(Character start, IReadOnlyList<ContentOption> options, IReadOnlyList<Resolution> resolutions, Func<Character, CharacterSheet> calculate, Choose choose, Func<ContentReference, ContentRevision?> find, bool equipMatched, StubRequest stub)
    {
        _character = start;
        _calculate = calculate;
        _choose = choose;
        _find = find;
        _equipMatched = equipMatched;
        _stub = stub;
        foreach (var option in options)
        {
            var key = (option.Kind, Normalise.Name(option.Name));
            if (!_byName.TryGetValue(key, out var list))
                _byName[key] = list = [];
            list.Add(option);
            _byContent.TryAdd(option.Reference.ContentId, option);
        }
        foreach (var resolution in resolutions.Where(r => r?.RowId is not null))
            _resolutions[resolution.RowId] = resolution;
    }

    public Character Character => _character;

    public IReadOnlyList<MatchRow> Rows => _rows;

    /// <summary>Every class row no installed class matched, with whether a stub can be created for it (item 15).</summary>
    public IReadOnlyList<StubOffer> StubOffers => _offers;

    /// <summary>Report lines the stub adds (saves from the first class only; an unreadable hit dice total). Labels only, never sheet text.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>The stub class as a draft, when the user asked for it and the sheet settled its die.</summary>
    public ContentRevision? StubRevision { get; private set; }

    /// <summary>The stub's whole content, for the review (D35).</summary>
    public StubClassReview? StubReview { get; private set; }

    /// <summary>Rows the user's resolution matched (the report's "chosen").</summary>
    public int ChosenByUser => _rows.Count(r => r.Status == MatchStatus.Matched && _chosenByUser.Contains(r.RowId));

    public void Plan(DdbSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        Classes(sheet);
        Content("species", MatchKind.Species, ContentKind.Species, sheet.Species);
        Content("background", MatchKind.Background, ContentKind.Background, sheet.Background);
        Subclasses(sheet.Classes, sheet.Features);
        for (var i = 0; i < sheet.Feats.Count; i++)
            Content($"feat:{i}", MatchKind.Feat, ContentKind.Feat, sheet.Feats[i]);
        Skills(sheet.SkillProficient);
        Items(sheet.Items);
        Spells(sheet.Spells);
        Features(sheet.Features);
    }

    // ---- the steps ----

    private void Classes(DdbSheet sheet)
    {
        var read = sheet.Classes;
        if (read.Status == ReadStatus.Unreadable)
            _rows.Add(new("classes", MatchKind.Class, UnreadableLabel(MatchKind.Class), MatchStatus.Unreadable, [], null, null));
        if (read.Status != ReadStatus.Ok)
            return;
        // First the rows as the installed content settles them; then (item 15) the class rows nothing matched may be offered a stub.
        var parts = new List<ClassPart>();
        var levels = new List<ClassLevel>();
        for (var i = 0; i < read.Value!.Count; i++)
        {
            var text = read.Value[i];
            var rowId = $"class:{i}";
            var candidates = Lookup(ContentKind.Class, text.Name).Select(o => Candidate(o, new(PlacementKind.Class))).ToList();
            var (status, chosen, note) = Decide(rowId, ContentKind.Class, candidates, o => Candidate(o, new(PlacementKind.Class)));
            if (status == MatchStatus.Matched && levels.Any(l => l.Class.ContentId == chosen!.Reference.ContentId))
                (status, note) = (MatchStatus.NoPlace, "character.class-duplicate");
            if (status == MatchStatus.Matched)
            {
                levels.Add(new(chosen!.Reference, text.Level));
                note ??= CampaignNote(chosen);
            }
            parts.Add(new(i, text, rowId, candidates, status, status == MatchStatus.Matched ? chosen : null, note));
        }

        var unmatched = parts.Where(p => p.Status == MatchStatus.NotFound).ToList();
        if (unmatched.Count > 0)
        {
            var matched = parts.Where(p => p.Status == MatchStatus.Matched).Select(p => (p.Text.Level, DieOf(p.Chosen!.Reference))).ToList();
            var hitDice = sheet.HitDiceTotal is { Status: ReadStatus.Ok } total ? total.Value : null;
            var unmatchedLevels = unmatched.Sum(p => p.Text.Level);
            var offers = unmatched.Select(p => (Part: p, Read: StubClasses.ReadHitDie(hitDice, matched, unmatchedLevels))).ToList();
            var chosenOffer = offers.FirstOrDefault(o => o.Part.RowId == _stub.RowId && o.Read.Note is null);
            foreach (var (part, die) in offers)
            {
                // R100: one stub class per import (the overlay holds one draft); the others keep the gap-note path.
                var note = die.Note ?? (chosenOffer.Part is not null && chosenOffer.Part != part ? StubClasses.NoteOnePerImport : null);
                _offers.Add(new(part.RowId, part.Text.Name.Trim(), die.Die, note));
            }
            if (offers.Any(o => o.Read.Note == StubClasses.NoteUnreadable))
                _lines.Add(StubClasses.HitDiceLine);
            if (chosenOffer.Part is { } stubPart)
                CreateStub(sheet, stubPart, chosenOffer.Read.Die!.Value);
        }

        var stubReference = StubRevision?.Reference;
        var order = new List<ClassLevel>();
        foreach (var part in parts)
        {
            if (stubReference is not null && part.RowId == _stub.RowId && part.Status == MatchStatus.NotFound)
            {
                var candidate = new MatchCandidate(stubReference, StubRevision!.Name, _stub.Source.Title, [_stub.Family], null, new(PlacementKind.Class));
                order.Add(new(stubReference, part.Text.Level));
                _classes[part.Index] = stubReference;
                _rows.Add(new(part.RowId, MatchKind.Class, $"{part.Text.Name} {part.Text.Level}", MatchStatus.Matched, [candidate], candidate, "stub.created"));
                continue;
            }
            if (part.Status == MatchStatus.Matched)
            {
                order.Add(new(part.Chosen!.Reference, part.Text.Level));
                _classes[part.Index] = part.Chosen.Reference;
            }
            _rows.Add(new(part.RowId, MatchKind.Class, $"{part.Text.Name} {part.Text.Level}", part.Status, part.Candidates, part.Chosen, part.Note));
        }
        _character = _character with { Classes = order };
    }

    private sealed record ClassPart(int Index, ClassText Text, string RowId, List<MatchCandidate> Candidates, MatchStatus Status, MatchCandidate? Chosen, string? Note);

    /// <summary>The die of a matched class (0 when its revision has none, so the sheet's total cannot be reduced by it).</summary>
    private int DieOf(ContentReference reference) =>
        _find(reference)?.Effects.OfType<HitDieEffect>().FirstOrDefault(d => d.Automation == AutomationStatus.Automatic)?.Die ?? 0;

    /// <summary>
    /// Builds the stub (D35): the hit die read from the sheet, and the sheet's ticked saves only when it is the first class
    /// (R101). Tells the catalog about the draft before anything is calculated, and lists it as an option for the lookups.
    /// </summary>
    private void CreateStub(DdbSheet sheet, ClassPart part, int die)
    {
        var name = part.Text.Name.Trim();
        var saves = part.Index == 0
            ? Enum.GetValues<Ability>().Where(a => sheet.SaveProficient.TryGetValue(a, out var s) && s is { Status: ReadStatus.Ok, Value: true }).ToList()
            : [];
        if (part.Index != 0)
            _lines.Add(StubClasses.SavesLine);
        var draft = StubClasses.Build(name, _stub.Family, _stub.Source.Id, die, saves);
        StubRevision = draft;
        StubReview = new(part.RowId, name, _stub.Family, die, [.. saves.Select(FieldIds.Key)], _stub.Source.Title);
        _stub.Created(draft);
        _byContent.TryAdd(draft.ContentId, new ContentOption(draft.Reference, ContentKind.Class, name, draft.RulesFamilies, true, _stub.Source.Id, _stub.Source.Title, null, draft.Summary));
    }

    /// <summary>
    /// The sheet's subclass, or (D16f) when it names none and the matched class offers its subclass choice at the imported
    /// level, the choice's options: one whose granted features appear on the sheet is proposed, else the user picks or leaves
    /// it out. Below the choice level, or with an unmatched class, nothing is offered.
    /// </summary>
    private void Subclasses(Read<IReadOnlyList<ClassText>> read, IReadOnlyList<Read<string>> features)
    {
        if (read.Status != ReadStatus.Ok)
            return;
        var onSheet = features.Where(f => f.Status == ReadStatus.Ok).Select(f => Normalise.Name(f.Value!)).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < read.Value!.Count; i++)
        {
            var text = read.Value[i];
            var rowId = $"class:{i}:subclass";
            ChoiceStatus? choice = null;
            if (_classes.TryGetValue(i, out var classRef))
                choice = Calculate().Choices?.FirstOrDefault(c => c.Source == classRef && c.Options.Any(o => _find(o)?.Kind == ContentKind.Subclass));
            MatchCandidate For(ContentOption o) => choice?.Options.FirstOrDefault(x => x.ContentId == o.Reference.ContentId) is { } inChoice
                ? Candidate(o, new(PlacementKind.Choice, choice.Source, choice.ChoiceId), inChoice)
                : Candidate(o, new(PlacementKind.Nowhere));

            if (text.Subclass is { } name)
            {
                var options = Lookup(ContentKind.Subclass, name);
                var candidates = options.Select(For).ToList();
                if (choice is null && !LeftOut(rowId))
                {
                    _rows.Add(new(rowId, MatchKind.Subclass, name, MatchStatus.NoPlace, candidates, null, classRef is null ? "class.not-matched" : "choice.not-offered"));
                    continue;
                }
                Place(rowId, MatchKind.Subclass, name, ContentKind.Subclass, candidates, For);
                continue;
            }

            if (choice is null)
                continue;
            var label = $"{text.Name} subclass (not on the sheet)";
            // The choice can offer several published revisions of one subclass: one candidate each.
            var offered = choice.Options.Select(o => _byContent.GetValueOrDefault(o.ContentId)).OfType<ContentOption>().Where(o => o.Kind == ContentKind.Subclass)
                .DistinctBy(o => o.Reference.ContentId).ToList();
            // Detected: every feature the subclass grants up to the imported level is on the sheet (a shared generic name is
            // not enough). Likely: any one of them is.
            var granted = offered.ToDictionary(o => o.Reference.ContentId, o => GrantedNames(o.Reference, text.Level));
            var detected = offered.Where(o => granted[o.Reference.ContentId] is { Count: > 0 } names && names.IsSubsetOf(onSheet)).ToList();
            var likely = offered.Except(detected).Where(o => granted[o.Reference.ContentId].Overlaps(onSheet)).ToList();
            var ordered = detected.Concat(likely).Concat(offered.Except(detected).Except(likely)).Select(For).ToList();
            if (detected.Count == 1 && !_resolutions.ContainsKey(rowId))
            {
                var pick = For(detected[0]);
                var refused = Apply(pick);
                _rows.Add(refused is null
                    ? new(rowId, MatchKind.Subclass, label, MatchStatus.Matched, ordered, pick, CampaignNote(pick) ?? "subclass.detected-from-features")
                    : new(rowId, MatchKind.Subclass, label, MatchStatus.NoPlace, ordered, null, refused));
                continue;
            }
            // A pick that is not one of this choice's options (a stale resolution) would end as "No place" and unblock Create
            // with the choice still open: ask again instead.
            if (_resolutions.GetValueOrDefault(rowId) is { LeaveOut: false, Chosen: { } picked } && !offered.Any(o => o.Reference.ContentId == picked.ContentId))
            {
                _rows.Add(new(rowId, MatchKind.Subclass, label, MatchStatus.Choose, ordered, null, "resolution.not-found"));
                continue;
            }
            // One option is never taken silently: the sheet did not name it (autoPick: false).
            Place(rowId, MatchKind.Subclass, label, ContentKind.Subclass, ordered, For, autoPick: false);
        }
    }

    /// <summary>Species, background or a feat: the draft may already have it, a choice may offer it, or it is pinned.</summary>
    private void Content(string rowId, MatchKind kind, ContentKind contentKind, Read<string> read)
    {
        if (read.Status == ReadStatus.Missing)
            return;
        if (read.Status == ReadStatus.Unreadable)
        {
            _rows.Add(new(rowId, kind, UnreadableLabel(kind), MatchStatus.Unreadable, [], null, null));
            return;
        }
        // Calculated only when an option needs placing: most names on a real sheet match nothing.
        CharacterSheet? draft = null;
        MatchCandidate For(ContentOption o) => Candidate(o, PlaceContent(o, draft ??= Calculate(), out var inChoice), inChoice);
        Place(rowId, kind, read.Value!, contentKind, [.. Lookup(contentKind, read.Value!).Select(For)], For);
    }

    /// <summary>
    /// Proficient skills: one a grant already gives is matched as it is; the rest are placed together on the open choices'
    /// free slots (a maximum matching), so a choice that offers many skills never takes the only skill another choice could
    /// have held. Only a skill option counts as an offer: one effect, that skill's proficiency, nothing more.
    /// </summary>
    private void Skills(IReadOnlyDictionary<string, Read<bool>> proficient)
    {
        var draft = Calculate();
        var rows = new Dictionary<string, MatchRow>(StringComparer.Ordinal);
        var open = new List<(string Key, string RowId, string Label, string Field)>();
        foreach (var (key, label, _) in CharacterCalculator.Skills)
        {
            if (!proficient.TryGetValue(key, out var read) || read is not { Status: ReadStatus.Ok, Value: true })
                continue;
            var rowId = $"skill:{key}";
            var field = FieldIds.Skill(key);
            if (LeftOut(rowId))
                rows[key] = new(rowId, MatchKind.Skill, label, MatchStatus.LeftOut, [], null, null);
            else if (draft.Field(field).Trace.FirstOrDefault(t => t.Operation == "add" && t.Field == field && IsProficiencyGrant(t.Origin, field)) is { Origin.Content: { } granter } step)
            {
                var already = Candidate(granter, step.Origin.ContentName ?? "", new(PlacementKind.None));
                rows[key] = new(rowId, MatchKind.Skill, label, MatchStatus.Matched, [already], already, null);
            }
            else
                open.Add((key, rowId, label, field));
        }

        // An option already chosen anywhere has given its proficiency, so the grant branch above took that skill.
        var chosenAnywhere = _character.Choices.SelectMany(c => c.Selected).ToHashSet();
        var slots = (draft.Choices ?? []).Where(c => c.Selected.Count < c.Count).SelectMany(c => Enumerable.Repeat(c, c.Count - c.Selected.Count)).ToList();
        ContentReference? OptionFor(ChoiceStatus choice, string field) => choice.Options.FirstOrDefault(o => !chosenAnywhere.Contains(o) && IsSkillOption(o, field));
        var slotOf = new int?[open.Count];
        var skillIn = new int?[slots.Count];
        // Kuhn's augmenting paths: at most 18 skills over a handful of slots.
        bool Assign(int skill, bool[] seen)
        {
            for (var slot = 0; slot < slots.Count; slot++)
            {
                if (seen[slot] || OptionFor(slots[slot], open[skill].Field) is null)
                    continue;
                seen[slot] = true;
                if (skillIn[slot] is not { } other || Assign(other, seen))
                {
                    (skillIn[slot], slotOf[skill]) = (skill, slot);
                    return true;
                }
            }
            return false;
        }
        for (var skill = 0; skill < open.Count; skill++)
            Assign(skill, new bool[slots.Count]);

        for (var skill = 0; skill < open.Count; skill++)
        {
            var (key, rowId, label, field) = open[skill];
            if (slotOf[skill] is not { } slot)
            {
                rows[key] = new(rowId, MatchKind.Skill, label, MatchStatus.NoPlace, [], null, "skill.no-open-choice");
                continue;
            }
            var choice = slots[slot];
            var option = OptionFor(choice, field)!;
            var candidate = Candidate(option, _find(option)?.Name ?? "", new(PlacementKind.Choice, choice.Source, choice.ChoiceId));
            var refused = Apply(candidate);
            rows[key] = refused is null
                ? new(rowId, MatchKind.Skill, label, MatchStatus.Matched, [candidate], candidate, null)
                : new(rowId, MatchKind.Skill, label, MatchStatus.NoPlace, [candidate], null, refused);
        }
        _rows.AddRange(CharacterCalculator.Skills.Where(s => rows.ContainsKey(s.Key)).Select(s => rows[s.Key]));
    }

    /// <summary>A skill option: its only effect is the proficiency in <paramref name="field"/> (as every SRD skill option is).</summary>
    private bool IsSkillOption(ContentReference option, string field) =>
        _find(option)?.Effects is [GrantEffect { Grant: GrantKind.Proficiency } grant] && grant.Target == field;

    private void Items(IReadOnlyList<Read<ItemText>> items)
    {
        // Equipped is null when no merged row carried a mark (the 2014 layout has none); an explicit false is a mark too.
        var merged = new Dictionary<ContentReference, (int Quantity, bool? Equipped)>();
        var order = new List<ContentReference>();
        for (var i = 0; i < items.Count; i++)
        {
            var rowId = $"item:{i}";
            if (items[i].Status != ReadStatus.Ok)
            {
                _rows.Add(new(rowId, MatchKind.Item, items[i].Value?.Name ?? UnreadableLabel(MatchKind.Item), MatchStatus.Unreadable, [], null, null));
                continue;
            }
            var item = items[i].Value!;
            MatchCandidate For(ContentOption o) => Candidate(o, new(PlacementKind.Equipment));
            var candidates = Lookup(ContentKind.Item, item.Name).Select(For).ToList();
            var (status, chosen, note) = Decide(rowId, ContentKind.Item, candidates, For);
            if (status == MatchStatus.Matched)
            {
                var (quantity, equipped) = merged.GetValueOrDefault(chosen!.Reference);
                if (!merged.ContainsKey(chosen.Reference))
                    order.Add(chosen.Reference);
                merged[chosen.Reference] = ((int)Math.Min(EquipmentEntry.MaxQuantity, (long)quantity + item.Quantity), equipped is null ? item.Equipped : item.Equipped is null ? equipped : equipped == true || item.Equipped == true);
                note ??= CampaignNote(chosen);
            }
            _rows.Add(new(rowId, MatchKind.Item, item.Name, status, candidates, status == MatchStatus.Matched ? chosen : null, note));
        }
        // D16g: the 2014 sheet has no equipped mark. When asked, equip every matched weapon, the first body armour and the
        // first shield, in sheet order, so an import never raises equipment.multiple-armor or equipment.multiple-shields.
        // The sheet's own marks win: armour it marks equipped takes the slot first, and an item it marks unequipped stays so.
        // Only armour and weapons the calculator would count (automatic, always on; not reference-only) take part.
        var wornBody = false;
        var wornShield = false;
        ArmorEffect? CountedArmor(ContentReference reference) =>
            (_find(reference)?.Effects ?? []).OfType<ArmorEffect>().FirstOrDefault(a => a.Automation == AutomationStatus.Automatic && a.Timing == EffectTiming.Always);
        foreach (var r in order.Where(r => merged[r].Equipped == true))
        {
            if (CountedArmor(r) is not { } marked)
                continue;
            if (marked.Category == ArmorCategory.Shield)
                wornShield = true;
            else
                wornBody = true;
        }
        bool Wear(ContentReference reference)
        {
            if (CountedArmor(reference) is { } armor)
            {
                if (armor.Category == ArmorCategory.Shield)
                {
                    if (wornShield)
                        return false;
                    wornShield = true;
                    return true;
                }
                if (wornBody)
                    return false;
                wornBody = true;
                return true;
            }
            return (_find(reference)?.Effects ?? []).OfType<WeaponEffect>().Any(w => w.Automation != AutomationStatus.Reference);
        }
        _character = _character with { Equipment = [.. order.Select(r => new EquipmentEntry(r, merged[r].Equipped ?? (_equipMatched && Wear(r)), merged[r].Quantity))] };
    }

    private void Spells(IReadOnlyList<Read<SpellText>> spells)
    {
        var casters = Calculate().Spellcasting ?? [];
        var known = new List<KnownSpell>();
        for (var i = 0; i < spells.Count; i++)
        {
            var rowId = $"spell:{i}";
            if (spells[i].Status != ReadStatus.Ok)
            {
                _rows.Add(new(rowId, MatchKind.Spell, spells[i].Value?.Name ?? UnreadableLabel(MatchKind.Spell), MatchStatus.Unreadable, [], null, null));
                continue;
            }
            var spell = spells[i].Value!;
            var options = Lookup(ContentKind.Spell, spell.Name);
            // A caster whose list names the spell takes it; a spell on several lists, or on none, asks with those casters
            // (or every caster).
            IEnumerable<MatchCandidate> For(ContentOption o)
            {
                var lists = o.Spell?.Lists ?? [];
                var onList = casters.Where(c => lists.Contains(c.SpellList)).ToList();
                return (onList.Count > 0 ? onList : casters).Select(c => Candidate(o, new(PlacementKind.Spell, Caster: c.Content.ContentId)));
            }
            if (casters.Count == 0)
            {
                var nowhere = options.Select(o => Candidate(o, new(PlacementKind.Nowhere))).ToList();
                _rows.Add(LeftOut(rowId)
                    ? new(rowId, MatchKind.Spell, spell.Name, MatchStatus.LeftOut, nowhere, null, null)
                    : new(rowId, MatchKind.Spell, spell.Name, options.Count == 0 ? MatchStatus.NotFound : MatchStatus.NoPlace, nowhere, null, options.Count == 0 ? null : "spell.no-caster"));
                continue;
            }
            var candidates = options.SelectMany(For).ToList();
            var single = options.Count == 1 && casters.Count(c => (options[0].Spell?.Lists ?? []).Contains(c.SpellList)) == 1;
            var (status, chosen, note) = Decide(rowId, ContentKind.Spell, candidates, o => For(o).FirstOrDefault(), autoPick: single);
            if (status == MatchStatus.Matched)
            {
                var caster = chosen!.Placement.Caster!.Value;
                if (known.Any(k => k.Caster == caster && k.Spell.ContentId == chosen.Reference.ContentId))
                    (status, note) = (MatchStatus.LeftOut, "spell.duplicate"); // adds nothing, so not counted as a match
                else if (known.Count >= Character.MaxSpells)
                    (status, note) = (MatchStatus.NoPlace, "character.spells-too-many");
                // A resolution's caster that is not a caster of this draft (its class left out, or never on the sheet).
                else if (casters.FirstOrDefault(c => c.Content.ContentId == caster) is not { } entry)
                    (status, note) = (MatchStatus.NoPlace, "spell.caster-not-found");
                else
                {
                    var level = _byContent.GetValueOrDefault(chosen.Reference.ContentId)?.Spell?.Level;
                    known.Add(new KnownSpell(caster, chosen.Reference, level == 0 || entry.Preparation == SpellPreparation.Known || (spell.Prepared ?? true)));
                    note ??= CampaignNote(chosen);
                }
            }
            _rows.Add(new(rowId, MatchKind.Spell, spell.Name, status, candidates, status == MatchStatus.Matched ? chosen : null, note));
        }
        _character = _character with { Spells = known };
    }

    /// <summary>Features come with the class, species and background; one the calculated sheet lacks is not found.</summary>
    private void Features(IReadOnlyList<Read<string>> features)
    {
        var have = new Dictionary<string, FeatureEntry>(StringComparer.Ordinal);
        foreach (var entry in Calculate().Features ?? [])
            have.TryAdd(Normalise.Name(entry.Name), entry);
        for (var i = 0; i < features.Count; i++)
        {
            var rowId = $"feature:{i}";
            if (features[i].Status != ReadStatus.Ok)
            {
                _rows.Add(new(rowId, MatchKind.Feature, UnreadableLabel(MatchKind.Feature), MatchStatus.Unreadable, [], null, null));
                continue;
            }
            var name = features[i].Value!;
            if (LeftOut(rowId))
                _rows.Add(new(rowId, MatchKind.Feature, name, MatchStatus.LeftOut, [], null, null));
            else if (have.TryGetValue(Normalise.Name(name), out var entry))
            {
                var present = Candidate(entry.Content, entry.Name, new(PlacementKind.None));
                _rows.Add(new(rowId, MatchKind.Feature, name, MatchStatus.Matched, [present], present, null));
            }
            else
                _rows.Add(new(rowId, MatchKind.Feature, name, MatchStatus.NotFound, [], null, null));
        }
    }

    // ---- shared ----

    private CharacterSheet Calculate() => _calculate(_character);

    private List<ContentOption> Lookup(ContentKind kind, string name) => _byName.GetValueOrDefault((kind, Normalise.Name(name))) ?? [];

    private bool LeftOut(string rowId) => _resolutions.GetValueOrDefault(rowId)?.LeaveOut == true;

    /// <summary>
    /// The row's outcome: the user's resolution (left out, or a candidate, which may be any installed option of the kind),
    /// else the only candidate, else "Choose" for several and "Not found" for none. A spell with one option but no single
    /// caster is never picked by itself (<paramref name="autoPick"/>).
    /// </summary>
    private (MatchStatus Status, MatchCandidate? Chosen, string? Note) Decide(string rowId, ContentKind kind, IReadOnlyList<MatchCandidate> candidates, Func<ContentOption, MatchCandidate?> candidateFor, bool autoPick = true)
    {
        if (_resolutions.TryGetValue(rowId, out var resolution))
        {
            if (resolution.LeaveOut)
                return (MatchStatus.LeftOut, null, null);
            // The user may pick any installed option of the kind, not only the name's candidates ("The user can change it").
            if (resolution.Chosen is { } picked)
            {
                var chosen = candidates.FirstOrDefault(c => c.Reference == picked && (resolution.Caster is null || c.Placement.Caster == resolution.Caster))
                    ?? (_byContent.GetValueOrDefault(picked.ContentId) is { } other && other.Reference == picked && other.Kind == kind ? candidateFor(other) : null);
                if (chosen is not null && resolution.Caster is { } caster && chosen.Placement.Kind == PlacementKind.Spell)
                    chosen = chosen with { Placement = chosen.Placement with { Caster = caster } };
                if (chosen is not null)
                {
                    _chosenByUser.Add(rowId);
                    return (MatchStatus.Matched, chosen, null);
                }
                // The user's pick no longer resolves (a newer revision was published, or it is not installed in this
                // family): ask again rather than quietly falling back to the name's own match.
                return (MatchStatus.Choose, null, "resolution.not-found");
            }
        }
        return candidates.Count switch
        {
            0 => (MatchStatus.NotFound, null, null),
            1 when autoPick => (MatchStatus.Matched, candidates[0], null),
            _ => (MatchStatus.Choose, null, null),
        };
    }

    /// <summary>Decides a content row and applies its placement; a match with nowhere to go, or one the builder refuses, is "No place".</summary>
    private void Place(string rowId, MatchKind kind, string label, ContentKind contentKind, List<MatchCandidate> candidates, Func<ContentOption, MatchCandidate?> candidateFor, bool autoPick = true)
    {
        var (status, chosen, note) = Decide(rowId, contentKind, candidates, candidateFor, autoPick);
        if (status == MatchStatus.Matched && chosen!.Placement.Kind == PlacementKind.Nowhere)
            (status, note) = (MatchStatus.NoPlace, "content.no-open-choice");
        else if (status == MatchStatus.Matched && Apply(chosen!) is { } refused)
            (status, note) = (MatchStatus.NoPlace, refused);
        if (status == MatchStatus.Matched)
            note ??= CampaignNote(chosen!);
        _rows.Add(new(rowId, kind, label, status, candidates, status == MatchStatus.Matched ? chosen : null, note));
    }

    /// <summary>Applies a pin or a choice selection; returns the refusal's code when the builder's check refuses it.</summary>
    private string? Apply(MatchCandidate candidate)
    {
        switch (candidate.Placement.Kind)
        {
            case PlacementKind.Pin:
                if (!_character.Pins.Contains(candidate.Reference))
                    _character = _character with { Pins = [.. _character.Pins, candidate.Reference] };
                return null;
            case PlacementKind.Choice:
                var source = candidate.Placement.ChoiceSource!;
                var choiceId = candidate.Placement.ChoiceId!;
                var selected = _character.Choices.FirstOrDefault(c => c.Source == source && c.ChoiceId == choiceId)?.Selected ?? [];
                try
                {
                    _character = _choose(_character, source, choiceId, [.. selected, candidate.Reference]);
                    return null;
                }
                catch (AppValidationException ex)
                {
                    return ex.Problems.FirstOrDefault()?.Code ?? ex.Code;
                }
            default:
                return null;
        }
    }

    /// <summary>Already on the draft (granted or active), offered by an open choice, pinned when standalone, else nowhere.</summary>
    private Placement PlaceContent(ContentOption option, CharacterSheet draft, out ContentReference? inChoice)
    {
        inChoice = null;
        if (draft.Active?.Any(a => a.ContentId == option.Reference.ContentId) == true)
            return new(PlacementKind.None);
        var chosenAnywhere = _character.Choices.SelectMany(c => c.Selected).ToHashSet();
        foreach (var choice in draft.Choices ?? [])
        {
            if (choice.Selected.Count >= choice.Count)
                continue;
            if (choice.Options.FirstOrDefault(o => o.ContentId == option.Reference.ContentId && !chosenAnywhere.Contains(o)) is { } offered)
            {
                inChoice = offered;
                return new(PlacementKind.Choice, choice.Source, choice.ChoiceId);
            }
        }
        return option.Standalone ? new(PlacementKind.Pin) : new(PlacementKind.Nowhere);
    }

    /// <summary>The calculator traced the skill's proficiency to a grant (proficiency or expertise) of active content.</summary>
    private bool IsProficiencyGrant(TraceOrigin origin, string field) =>
        origin.Content is { } content && origin.EffectId is { } effect
        && _find(content)?.Effects.OfType<GrantEffect>().Any(g => g.Id == effect && g.Grant is GrantKind.Proficiency or GrantKind.Expertise && g.Target == field) == true;

    /// <summary>
    /// The normalised names of the content a revision grants (its features) at or below the class level (a grant with no
    /// level counts), for D16f detection.
    /// </summary>
    private HashSet<string> GrantedNames(ContentReference reference, int classLevel) =>
        (_find(reference)?.Effects ?? []).OfType<GrantEffect>()
            .Where(g => g.Content is not null && (g.Level is not { } level || level <= classLevel))
            .Select(g => _find(g.Content!)?.Name)
            .OfType<string>()
            .Select(Normalise.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The label of a row whose name could not be read: its kind only, never a value, so its gap note says what it was.</summary>
    private static string UnreadableLabel(MatchKind kind) => $"Unreadable {kind.ToString().ToLowerInvariant()}";

    private static string? CampaignNote(MatchCandidate candidate) => candidate.AllowedInCampaign == false ? "campaign.source-not-allowed" : null;

    /// <param name="reference">The revision a choice offers, when it is not the listing's newest one.</param>
    private static MatchCandidate Candidate(ContentOption option, Placement placement, ContentReference? reference = null) =>
        new(reference ?? option.Reference, option.Name, option.SourceTitle, option.RulesFamilies, option.AllowedInCampaign, placement);

    private MatchCandidate Candidate(ContentReference reference, string name, Placement placement) =>
        _byContent.GetValueOrDefault(reference.ContentId) is { } option
            ? new(reference, name, option.SourceTitle, option.RulesFamilies, option.AllowedInCampaign, placement)
            : new(reference, name, "", [], null, placement);
}
