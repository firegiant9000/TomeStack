using System.Text.Json.Nodes;
using FsCheck;
using FsCheck.Fluent;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// T3 generators (docs/testing/properties.md). They produce valid domain values: original names, one rules family per
/// character and only known family ids on content (srd-5.1 and srd-5.2.1 stay separate ids, never merged), and effect
/// fields only from the content schema version that introduced them.
/// </summary>
internal static class Generators
{
    public static readonly string[] Families = [RulesFamilies.Srd51, RulesFamilies.Srd521];

    /// <summary>Original syllables, plus characters the serializer escapes, so escaping is part of every round trip.</summary>
    private static readonly string[] Syllables = ["ka", "lor", "vey", "th", "ion", "mar", "eth", "ul", " ", "'", "\"", "é", "–", "✦", "\\", "<", "&"];

    public static Gen<Guid> Guids { get; } =
        from a in Gen.Choose(int.MinValue, int.MaxValue)
        from b in Gen.Choose(0, ushort.MaxValue)
        from c in Gen.Choose(0, ushort.MaxValue)
        from d in Gen.Choose(int.MinValue, int.MaxValue)
        from e in Gen.Choose(int.MinValue, int.MaxValue)
        select new Guid(a, (short)b, (short)((c & 0x0fff) | 0x4000), [0x80, (byte)d, (byte)(d >> 8), (byte)(d >> 16), (byte)(d >> 24), (byte)e, (byte)(e >> 8), (byte)(e >> 16)]);

    public static Gen<string> Names { get; } =
        from first in Gen.Elements("Ash", "Vey", "Orrin", "Mara", "Tal", "Quill")
        from rest in Gen.Elements(Syllables).ArrayOf().Select(s => s.Take(8))
        select (first + string.Concat(rest)).Trim() is { Length: > 0 } name ? name : first;

    public static Gen<string> Keys { get; } =
        from first in Gen.Elements("a", "b", "k", "m", "s", "z")
        from rest in Gen.Elements("a", "e", "r", "t", "1", "7", "X").ArrayOf().Select(s => s.Take(10))
        select first + string.Concat(rest);

    public static Gen<IReadOnlyList<string>> ContentFamilies { get; } =
        Gen.Elements<IReadOnlyList<string>>([RulesFamilies.Srd51], [RulesFamilies.Srd521], [RulesFamilies.Srd51, RulesFamilies.Srd521]);

    private static Gen<string> Formulas { get; } =
        Gen.Elements("1", "2", "PB", "CON.MOD", "PB + CON.MOD", "max(1, WIS.MOD)", "floor(LEVEL / 2)", "CLASS_LEVEL");

    private static Gen<string?> Optional(Gen<string> gen) =>
        Gen.Frequency((1, Gen.Constant<string?>(null)), (2, gen.Select(s => (string?)s)));

    private static Gen<T?> OptionalValue<T>(Gen<T> gen) where T : struct =>
        Gen.Frequency((1, Gen.Constant<T?>(null)), (2, gen.Select(v => (T?)v)));

    private static JsonObject With(this JsonObject o, string name, JsonNode? value)
    {
        if (value is not null)
            o[name] = value;
        return o;
    }

    private static Gen<JsonObject> Common(string type, Gen<JsonObject> body) =>
        from id in Keys
        from text in Optional(Names)
        from automation in Gen.Elements<string?>(null, "automatic", "assisted", "reference")
        from o in body
        select new JsonObject { ["type"] = type, ["id"] = id }.With("automation", automation).With("text", text).Merge(o);

    private static JsonObject Merge(this JsonObject target, JsonObject source)
    {
        foreach (var (name, value) in source.ToList())
        {
            source.Remove(name);
            target[name] = value;
        }
        return target;
    }

    /// <summary>One stored effect for a revision of content schema <paramref name="v"/> (v1 uses the legacy shapes).</summary>
    public static Gen<JsonObject> Effect(int v)
    {
        if (v == 1)
        {
            return Gen.OneOf(
                Common(EffectJsonConverter.LegacyAbilityScoreIncrease,
                    from ability in Gen.Elements("str", "dex", "con", "int", "wis", "cha")
                    from amount in Gen.Choose(-2, 4)
                    select new JsonObject { ["ability"] = ability, ["amount"] = amount }),
                Common(EffectJsonConverter.LegacyInitiativeBonus, Gen.Choose(1, 5).Select(a => new JsonObject { ["amount"] = a })),
                Unknown());
        }
        var gens = new List<Gen<JsonObject>>
        {
            Common(ModifierEffect.TypeName,
                from op in Gen.Elements("bonus", "set", "replace")
                from target in Gen.Elements(FieldIds.Initiative, FieldIds.Score(Ability.Dex), FieldIds.ProficiencyBonus, FieldIds.HitPoints)
                from value in Formulas
                from stackGroup in Optional(Keys)
                from toggle in Optional(Keys)
                from armored in OptionalValue(Gen.Elements(true, false))
                select new JsonObject { ["operation"] = op, ["target"] = target, ["value"] = value }
                    .With("stacking", stackGroup is null ? null : "highestInGroup").With("stackGroup", stackGroup)
                    .With("toggle", v >= ToggleEffect.SchemaVersion ? toggle : null)
                    .With("whileArmored", v >= ContentRevision.CombatDetailsSchemaVersion ? armored : null)),
            Common(GrantEffect.TypeName,
                from grant in Gen.Elements("proficiency", "expertise")
                from target in Gen.Elements("skill.stealth", "save.dex", "skill.arcana")
                from level in OptionalValue(Gen.Choose(1, 20))
                from onlyAs in Gen.Elements<string?>(null, "startingClass", "multiclass")
                select new JsonObject { ["grant"] = grant, ["target"] = target }
                    .With("level", v >= 3 ? level : null).With("onlyAs", v >= SpellcastingEffect.SchemaVersion ? onlyAs : null)),
            Common(ResourceEffect.TypeName,
                from id in Keys
                from label in Names
                from max in Formulas
                select new JsonObject { ["resourceId"] = id, ["label"] = label, ["maximum"] = max }),
            Common(RecoveryEffect.TypeName,
                from id in Keys
                from period in Gen.Elements("shortRest", "longRest")
                from amount in Gen.Elements("all", "1", "PB")
                select new JsonObject { ["resourceId"] = id, ["on"] = period, ["amount"] = amount }),
            Common(RollEffect.TypeName,
                from id in Keys
                from label in Names
                from dice in Gen.Elements("1d20", "2d6+1", "1d8+1d6", "d4-1")
                from resource in Optional(Keys)
                from activation in Gen.Elements<string?>(null, "action", "bonusAction", "reaction", "other")
                from cost in Optional(Gen.Elements("1", "2", "PB"))
                from variable in OptionalValue(Gen.Elements(true, false))
                from bonus in Optional(Formulas)
                select new JsonObject { ["rollId"] = id, ["label"] = label, ["dice"] = dice }.With("resourceId", resource)
                    .With("activation", v >= SpellcastingEffect.SchemaVersion ? activation : null)
                    .With("cost", v >= ToggleEffect.SchemaVersion ? cost : null)
                    .With("variableCost", v >= ToggleEffect.SchemaVersion ? variable : null)
                    .With("bonus", v >= ContentRevision.CombatDetailsSchemaVersion ? bonus : null)),
            Common(RestrictionEffect.TypeName,
                from field in Gen.Elements(FieldIds.Score(Ability.Str), FieldIds.Score(Ability.Wis))
                from min in Gen.Choose(1, 20)
                from stackGroup in Optional(Keys)
                select new JsonObject { ["field"] = field, ["minimum"] = min }.With("group", v >= SpellcastingEffect.SchemaVersion ? stackGroup : null)),
            // Versioned types at any version: below their version they are kept as unknown effects, byte for byte.
            Common(ToggleEffect.TypeName,
                from id in Keys
                from label in Names
                from resource in Optional(Keys)
                select new JsonObject { ["toggleId"] = id, ["label"] = label }.With("resourceId", resource)),
            Common(ScaleEffect.TypeName,
                from id in Keys
                from label in Names
                from values in Gen.Choose(0, FormulaLimits.MaxLiteral).ArrayOf(20)
                select new JsonObject { ["scaleId"] = id, ["label"] = label, ["values"] = new JsonArray([.. values.Select(x => (JsonNode)x)]) }),
            Common(ArmorEffect.TypeName,
                from category in Gen.Elements("light", "medium", "heavy", "shield")
                from ac in Gen.Choose(1, 18)
                from cap in OptionalValue(Gen.Choose(0, 3))
                from strength in OptionalValue(Gen.Choose(11, 15))
                from stealth in OptionalValue(Gen.Elements(true, false))
                select new JsonObject { ["category"] = category, ["armorClass"] = ac }.With("dexterityCap", cap)
                    .With("strength", v >= ContentRevision.CombatDetailsSchemaVersion ? strength : null)
                    .With("stealthDisadvantage", v >= ContentRevision.CombatDetailsSchemaVersion ? stealth : null)),
            Common(SpellcastingEffect.TypeName,
                from ability in Gen.Elements("int", "wis", "cha")
                from list in Keys
                from slots in Gen.Choose(0, 4).ArrayOf().Select(r => r.Take(SpellcastingEffect.MaxSpellLevel).ToArray()).ArrayOf(20)
                from multiclass in Gen.Elements<string?>(null, "full", "half", "third")
                // Any version: below v9 the table key must stay extension data, written back where it was.
                from table in Gen.Elements(false, false, true)
                from shares in Gen.Choose(0, 20).ArrayOf(20)
                select new JsonObject
                {
                    ["ability"] = ability,
                    ["spellList"] = list,
                    ["slots"] = new JsonArray([.. slots.Select(r => (JsonNode)new JsonArray([.. r.Select(x => (JsonNode)x)]))]),
                }
                .With("multiclassCaster", v >= SpellcastingEffect.MulticlassSchemaVersion && !table ? multiclass : null)
                .With("multiclassCasterTable", table ? new JsonArray([.. shares.Select(x => (JsonNode)x)]) : null)),
            Unknown(),
        };
        if (v >= 3)
            gens.Add(Common(HitDieEffect.TypeName, Gen.Elements(6, 8, 10, 12).Select(d => new JsonObject { ["die"] = d })));
        return Gen.OneOf(gens);
    }

    /// <summary>An effect type no build knows, with nested data, so it must survive unchanged.</summary>
    private static Gen<JsonObject> Unknown() =>
        from type in Keys
        from id in Keys
        from key in Keys
        from number in Gen.Choose(-1000, 1000)
        from text in Names
        select new JsonObject
        {
            ["id"] = id,
            ["type"] = "x" + type,
            [key] = new JsonObject { ["n"] = number, ["list"] = new JsonArray(text, number, true, null) },
            ["note"] = text,
        };

    /// <summary>One stored revision (the compact JSON shape) of content schema <paramref name="v"/>.</summary>
    public static Gen<JsonObject> Revision(int v) =>
        from contentId in Guids
        from revisionId in Guids
        from kind in Gen.Elements("species", "background", "class", "subclass", "feature", "feat", "spell", "item")
        from name in Names
        from families in ContentFamilies
        from source in Guids
        from page in OptionalValue(Gen.Choose(1, 400))
        from status in Gen.Elements("draft", "published")
        from summary in Optional(Names)
        from extends in OptionalValue(Guids)
        from choiceId in Keys
        from effects in Effect(v).ListOf().Select(e => e.Take(5).ToList())
        from extension in Optional(Keys)
        select new JsonObject
        {
            ["contentId"] = contentId.ToString(),
            ["revisionId"] = revisionId.ToString(),
            ["schemaVersion"] = v,
            ["kind"] = kind,
            ["name"] = name,
            ["rulesFamilies"] = new JsonArray([.. families.Select(f => (JsonNode)f)]),
            ["provenance"] = new JsonObject { ["sourceId"] = source.ToString() }.With("page", page is { } p ? new JsonObject { ["start"] = p } : null),
            ["status"] = status,
        }
        .With("summary", summary)
        .With("extendsChoice", v >= 4 && extends is { } e ? new JsonObject { ["contentId"] = e.ToString(), ["choiceId"] = choiceId } : null)
        .With("effects", effects.Count > 0 ? new JsonArray([.. effects]) : null)
        // A field this build does not know: it round-trips through ContentRevision.Extensions.
        .With("x" + (extension ?? ""), extension is null ? null : new JsonObject { ["kept"] = extension });

    /// <summary>A revision of any content schema version, 1 to 9.</summary>
    public static Gen<(int Version, JsonObject Json)> AnyRevision { get; } =
        from v in Gen.Choose(1, ContentRevision.CurrentSchemaVersion)
        from json in Revision(v)
        select (v, json);

    /// <summary>
    /// <see cref="AnyRevision"/> with a shrinker that drops one effect, or one optional field, at a time, so a failure is
    /// reported on the smallest revision that still fails.
    /// </summary>
    public static Arbitrary<(int Version, JsonObject Json)> AnyRevisionArb(Func<(int Version, JsonObject Json), bool>? where = null)
    {
        where ??= _ => true;
        static IEnumerable<(int, JsonObject)> Shrink((int Version, JsonObject Json) r)
        {
            var count = r.Json["effects"]?.AsArray().Count ?? 0;
            for (var i = 0; i < count; i++)
            {
                var copy = (JsonObject)r.Json.DeepClone();
                copy["effects"]!.AsArray().RemoveAt(i);
                yield return (r.Version, copy);
            }
            foreach (var name in r.Json.Select(p => p.Key).Where(k => k is "summary" or "extendsChoice" || k.StartsWith('x')).ToList())
            {
                var copy = (JsonObject)r.Json.DeepClone();
                copy.Remove(name);
                yield return (r.Version, copy);
            }
        }
        return Arb.From(AnyRevision.Where(where), r => Shrink(r).Where(where));
    }

    private static Gen<ContentReference> References { get; } =
        from c in Guids
        from r in Guids
        select new ContentReference(c, r);

    /// <summary>A valid character (Character.Validate() finds nothing) of one rules family.</summary>
    public static Gen<Character> Characters { get; } =
        from id in Guids
        from name in Names
        from family in Gen.Elements(Families)
        from levels in Gen.Choose(1, 6).ArrayOf().Select(l => l.Take(3).ToArray())
        from classes in References.ArrayOf(levels.Length)
        from scores in Gen.Choose(1, 30).ArrayOf(6)
        from pins in References.ListOf().Select(p => p.Take(6).ToList())
        from overrides in (from field in Gen.Elements(FieldIds.Initiative, FieldIds.ArmorClass, FieldIds.HitPoints)
                           from value in Gen.Choose(-5, 40)
                           from reason in Optional(Names)
                           select new FieldOverride(field, value, reason)).ListOf().Select(o => o.Take(3).DistinctBy(x => x.Field).ToList())
        from items in References.ListOf().Select(i => i.Take(4).ToList())
        from equipped in Gen.Elements(true, false).ArrayOf(items.Count)
        from quantity in Gen.Choose(1, EquipmentEntry.MaxQuantity).ArrayOf(items.Count)
        from caster in Guids
        from spells in References.ListOf().Select(s => s.Take(5).ToList())
        from exception in Optional(Names)
        from inspiration in Gen.Elements(true, false)
        from temporary in Gen.Choose(0, 20)
        from updated in Gen.Choose(0, 1_000_000)
        from archived in OptionalValue(Gen.Choose(0, 1_000_000))
        select new Character
        {
            Id = id,
            Name = name,
            RulesFamily = family,
            Level = levels.Length > 0 ? levels.Sum() : 1,
            Classes = [.. classes.Zip(levels, (c, l) => new ClassLevel(c, l))],
            BaseAbilities = new(scores[0], scores[1], scores[2], scores[3], scores[4], scores[5]),
            Pins = pins,
            Overrides = overrides,
            Equipment = [.. items.Select((item, i) => new EquipmentEntry(item, equipped[i], quantity[i]))],
            Spells = [.. spells.Select((s, i) => new KnownSpell(caster, s, i % 2 == 0))],
            CrossFamilyExceptions = exception is null || pins.Count == 0 ? [] : [new CrossFamilyException(pins[0], exception)],
            Play = new() { Inspiration = inspiration, TemporaryHitPoints = temporary },
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(updated),
            ArchivedAt = archived is { } a ? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(a) : null,
        };
}
