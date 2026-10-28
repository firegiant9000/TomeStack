namespace TomeStack.RulesCore;

public static partial class CharacterCalculator
{
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
        // D24: passive scores (SRD: 10 + the skill's total). Content may target them from schema v10; in an older revision the
        // calculator ignores such modifiers and restrictions with a diagnostic (IsCharacterOnlyField).
        foreach (var (key, label, _) in Skills.Where(s => s.Key is "perception" or "insight" or "investigation"))
        {
            var skill = FieldIds.Skill(key);
            specs.Add(new(FieldIds.Passive(key), $"Passive {label}", "score", [skill], (c, steps) =>
            {
                var value = 10 + c.Values[skill];
                steps.Add(new(FieldIds.Passive(key), "base", $"Passive {label} is 10 + {label}", value, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(skill, c.Values[skill])]));
                return value;
            }));
        }
        // D24: speed. The bundled species carry no speed data, so the base is the SRD's common 30 feet by rules policy. From
        // content v10 a species (homebrew) sets its own with a `replace` modifier on speed, which replaces this base before
        // bonuses; otherwise a different speed is an override on Stats.
        specs.Add(new(FieldIds.Speed, "Speed", "feet", [], (c, steps) =>
        {
            steps.Add(new(FieldIds.Speed, "base", "Speed starts at 30 feet (rules policy, unless the species sets it; override it if yours differs)", 30, 30, new(TraceOriginKind.RulesPolicy, c.Family)));
            return 30;
        }));
        // Content v11 (D43): a flying speed. None by default; a form or species sets it with a v11 modifier (the validator and
        // the calculator refuse it below v11, IsPlayMechanicsOnlyField).
        specs.Add(new(FieldIds.FlySpeed, "Fly speed", "feet", [], (c, steps) =>
        {
            steps.Add(new(FieldIds.FlySpeed, "base", "No flying speed unless content gives one", 0, 0, new(TraceOriginKind.RulesPolicy, c.Family)));
            return 0;
        }));
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

    private static ProficiencyMark MarkOf(string field, IReadOnlyDictionary<string, Proficiency> proficiencies) =>
        (field.StartsWith("save.", StringComparison.Ordinal) || field.StartsWith("skill.", StringComparison.Ordinal)) && proficiencies.TryGetValue(field, out var p)
            ? p.Grant == GrantKind.Expertise ? ProficiencyMark.Expertise : ProficiencyMark.Proficient
            : ProficiencyMark.None;

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
