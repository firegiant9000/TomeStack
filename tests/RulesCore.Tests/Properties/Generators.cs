using System.Text.Json;
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

    /// <summary>The effect types every build since v2 types (the v1 shapes are typed as modifiers on read).</summary>
    private static readonly HashSet<string> AlwaysTyped =
    [
        ModifierEffect.TypeName, GrantEffect.TypeName, ResourceEffect.TypeName, ChoiceEffect.TypeName, RestrictionEffect.TypeName,
        RecoveryEffect.TypeName, RollEffect.TypeName, HitDieEffect.TypeName,
        EffectJsonConverter.LegacyAbilityScoreIncrease, EffectJsonConverter.LegacyInitiativeBonus,
    ];

    /// <summary>The versioned effect types and the content schema version each is typed from (ADR-003, <c>VersionedEffects</c>).</summary>
    public static readonly IReadOnlyDictionary<string, int> TypedFrom = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [ArmorEffect.TypeName] = ArmorEffect.SchemaVersion,
        [SpellcastingEffect.TypeName] = SpellcastingEffect.SchemaVersion,
        [SpellEffect.TypeName] = SpellcastingEffect.SchemaVersion,
        [WeaponEffect.TypeName] = SpellcastingEffect.SchemaVersion,
        [ToggleEffect.TypeName] = ToggleEffect.SchemaVersion,
        [ScaleEffect.TypeName] = ScaleEffect.SchemaVersion,
        [ItemEffect.TypeName] = ItemEffect.SchemaVersion,
        [ToggleGroupEffect.TypeName] = ToggleGroupEffect.SchemaVersion,
        [UpkeepEffect.TypeName] = UpkeepEffect.SchemaVersion,
        [AttackEffect.TypeName] = AttackEffect.SchemaVersion,
        [AttackRiderEffect.TypeName] = AttackRiderEffect.SchemaVersion,
        [ValueEffect.TypeName] = ValueEffect.SchemaVersion,
        [RollModeEffect.TypeName] = RollModeEffect.SchemaVersion,
        [ResponseEffect.TypeName] = ResponseEffect.SchemaVersion,
        [ItemModEffect.TypeName] = ItemModEffect.SchemaVersion,
        [CompanionEffect.TypeName] = CompanionEffect.SchemaVersion,
    };

    /// <summary>
    /// Whether an effect of <paramref name="type"/> in a revision written as content schema <paramref name="version"/> must
    /// read as an <see cref="UnknownEffect"/> and be written back exactly as stored: an unknown type, or a versioned type
    /// below its version (v1 is read as v2).
    /// </summary>
    public static bool StaysUnknown(string type, int version) =>
        !AlwaysTyped.Contains(type) && !(TypedFrom.TryGetValue(type, out var from) && Math.Max(version, ContentRevision.TypedEffectsSchemaVersion) >= from);

    /// <summary>One stored effect for a revision of content schema <paramref name="v"/> (v1 uses the legacy shapes).</summary>
    public static Gen<JsonObject> Effect(int v)
    {
        if (v == 1)
        {
            return Gen.OneOf([
                Common(EffectJsonConverter.LegacyAbilityScoreIncrease,
                    from ability in Gen.Elements("str", "dex", "con", "int", "wis", "cha")
                    from amount in Gen.Choose(-2, 4)
                    select new JsonObject { ["ability"] = ability, ["amount"] = amount }),
                Common(EffectJsonConverter.LegacyInitiativeBonus, Gen.Choose(1, 5).Select(a => new JsonObject { ["amount"] = a })),
                Unknown(),
                .. Versioned(v)]);
        }
        List<Gen<JsonObject>> gens =
        [
            Common(ModifierEffect.TypeName,
                from op in Gen.Elements("bonus", "set", "replace")
                from target in Gen.Elements(FieldIds.Initiative, FieldIds.Score(Ability.Dex), FieldIds.ProficiencyBonus, FieldIds.HitPoints)
                from value in Formulas
                from stackGroup in Optional(Keys)
                from toggle in Optional(Keys)
                from armored in OptionalValue(Gen.Elements(true, false))
                // Any version: below v11 the key must stay extension data, written back where it was.
                from toggleContent in OptionalValue(Guids)
                select new JsonObject { ["operation"] = op, ["target"] = target, ["value"] = value }
                    .With("stacking", stackGroup is null ? null : "highestInGroup").With("stackGroup", stackGroup)
                    .With("toggle", v >= ToggleEffect.SchemaVersion ? toggle : null)
                    .With("whileArmored", v >= ContentRevision.CombatDetailsSchemaVersion ? armored : null)
                    .With("toggleContent", toggleContent?.ToString())),
            Common(GrantEffect.TypeName,
                from grant in Gen.Elements("proficiency", "expertise")
                from target in Gen.Elements("skill.stealth", "save.dex", "skill.arcana")
                from level in OptionalValue(Gen.Choose(1, 20))
                from onlyAs in Gen.Elements<string?>(null, "startingClass", "multiclass")
                // Any version: below v10 the key must stay extension data, written back where it was.
                from damageType in Gen.Elements<string?>(null, null, "fire", "Cold")
                // Any version: below v11 the key must stay extension data, written back where it was.
                from whileToggle in Gen.Elements(false, false, true)
                from whileContent in OptionalValue(Guids)
                from whileId in Keys
                select new JsonObject { ["grant"] = grant, ["target"] = target }
                    .With("level", v >= 3 ? level : null).With("onlyAs", v >= SpellcastingEffect.SchemaVersion ? onlyAs : null)
                    .With("damageType", damageType)
                    .With("whileToggle", whileToggle ? new JsonObject { ["toggleId"] = whileId }.With("contentId", whileContent?.ToString()) : null)),
            // The damage defences: below v10 the whole effect stays unknown and unchanged (a v9 build cannot read the kind).
            Common(GrantEffect.TypeName,
                from grant in Gen.Elements("damageResistance", "damageImmunity", "DamageVulnerability")
                from damageType in Gen.Elements("fire", "Cold", "thunder")
                // Any version: below v11 the key must stay extension data (below v10, with the whole effect), written back where it was.
                from whileToggle in Gen.Elements(false, false, true)
                from whileContent in OptionalValue(Guids)
                from whileId in Keys
                select new JsonObject { ["grant"] = grant, ["damageType"] = damageType }
                    .With("whileToggle", whileToggle ? new JsonObject { ["toggleId"] = whileId }.With("contentId", whileContent?.ToString()) : null)),
            Common(ResourceEffect.TypeName,
                from id in Keys
                from label in Names
                from max in Formulas
                // Any version: below v10 the key must stay extension data, written back where it was.
                from activation in Gen.Elements<string?>(null, null, "action", "reaction")
                // Any version: below v11 these keys must stay extension data, written back where they were.
                from counts in Gen.Elements<string?>(null, null, "up")
                from full in Optional(Names)
                from allowance in OptionalValue(Gen.Elements(true, false))
                select new JsonObject { ["resourceId"] = id, ["label"] = label, ["maximum"] = max }.With("activation", activation)
                    .With("counts", counts).With("resourceFull", full is null ? null : new JsonObject { ["text"] = full }).With("allowance", allowance)),
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
                // Any version: below v11 the key must stay extension data, written back where it was.
                from whileToggle in Gen.Elements(false, false, true)
                from whileContent in OptionalValue(Guids)
                from whileId in Keys
                // Content v11 (slice 5), any version: below v11 the keys must stay extension data, written back where they were,
                // and a roll with no dice must stay an unknown effect, byte for byte.
                from noDice in Gen.Elements(false, false, true)
                from save in Gen.Elements(false, false, true)
                from saveAbility in Gen.Elements("str", "dex", "wis")
                from saveDc in Formulas
                from area in Gen.Elements(false, false, true)
                from shape in Gen.Elements("line", "cone", "sphere", "cube", "cylinder", "emanation")
                from size in Gen.Choose(1, 120)
                from width in OptionalValue(Gen.Choose(1, 10))
                from range in Optional(Gen.Elements("Self", "60 ft"))
                from targets in Optional(Names)
                from perUnit in Gen.Elements(false, false, true)
                from perUnitDice in Gen.Elements("1d6", "2d8")
                from maxUnits in OptionalValue(Gen.Choose(1, 100))
                from alsoChange in Gen.Elements(false, false, true)
                from alsoChangeKind in Gen.Elements("spend", "regain")
                from alsoChangeAmount in Formulas
                from drawsDown in Gen.Elements<bool?>(null, null, true, false)
                // Content v12 (MECH-X item 1), any version: typed from v12, below it extension data written back where it was.
                from payment in V12Slot(v, "payment", PaymentValue)
                from casts in V12Slot(v, "castsSpell", Guids.Select(g => (JsonNode?)g.ToString()))
                select new JsonObject { ["rollId"] = id, ["label"] = label }.With("dice", noDice ? null : dice).With("resourceId", noDice ? "pool" : resource)
                    .With("activation", v >= SpellcastingEffect.SchemaVersion ? activation : null)
                    .With("cost", noDice ? "1" : v >= ToggleEffect.SchemaVersion ? cost : null)
                    .With("variableCost", v >= ToggleEffect.SchemaVersion ? variable : null)
                    .With("bonus", v >= ContentRevision.CombatDetailsSchemaVersion ? bonus : null)
                    .With("whileToggle", whileToggle ? new JsonObject { ["toggleId"] = whileId }.With("contentId", whileContent?.ToString()) : null)
                    .With("save", save ? new JsonObject { ["ability"] = saveAbility, ["dc"] = saveDc } : null)
                    .With("area", area ? new JsonObject { ["shape"] = shape, ["size"] = size }.With("width", shape == "line" ? width : null) : null)
                    .With("range", range).With("targets", targets)
                    .With("perUnit", perUnit ? new JsonObject { ["dice"] = perUnitDice }.With("maxUnits", maxUnits) : null)
                    .With("alsoChange", alsoChange ? new JsonArray(new JsonObject { ["resourceId"] = "pool", ["change"] = alsoChangeKind, ["amount"] = alsoChangeAmount }) : null)
                    .With("drawsDown", drawsDown).WithV12(payment).WithV12(casts)),
            Common(RestrictionEffect.TypeName,
                from field in Gen.Elements(FieldIds.Score(Ability.Str), FieldIds.Score(Ability.Wis))
                from min in Gen.Choose(1, 20)
                from stackGroup in Optional(Keys)
                select new JsonObject { ["field"] = field, ["minimum"] = min }.With("group", v >= SpellcastingEffect.SchemaVersion ? stackGroup : null)),
            Common(ChoiceEffect.TypeName,
                from id in Keys
                from count in Gen.Choose(1, 3)
                from options in References.ArrayOf().Select(o => o.Take(4).ToArray())
                from level in OptionalValue(Gen.Choose(1, 20))
                from onlyAs in Gen.Elements<string?>(null, "startingClass", "multiclass")
                // Content v12 (MECH-X item 2), any version: typed from v12, below it extension data written back where it was.
                from replaceable in V12Slot(v, "replaceable", ReplaceableValue)
                select new JsonObject
                {
                    ["choiceId"] = id,
                    ["count"] = count,
                    ["options"] = new JsonArray([.. options.Select(o => (JsonNode)new JsonObject { ["contentId"] = o.ContentId.ToString(), ["revisionId"] = o.RevisionId.ToString() })]),
                }
                .With("level", v >= 3 ? level : null).With("onlyAs", v >= SpellcastingEffect.SchemaVersion ? onlyAs : null).WithV12(replaceable)),
            .. Versioned(v),
            Unknown(),
        ];
        if (v >= 3)
            gens.Add(Common(HitDieEffect.TypeName, Gen.Elements(6, 8, 10, 12).Select(d => new JsonObject { ["die"] = d })));
        return Gen.OneOf(gens);
    }

    /// <summary>
    /// The versioned types, at any version: below their version they must stay unknown effects, byte for byte. Their
    /// own optional fields still appear only from the version that added them.
    /// </summary>
    private static Gen<JsonObject>[] Versioned(int v) =>
    [
            Common(ToggleEffect.TypeName,
                from id in Keys
                from label in Names
                from resource in Optional(Keys)
                // Any version: below v10 the keys must stay extension data, written back where they were.
                from autoWhen in Gen.Elements<string?>(null, null, "bloodied")
                from activation in Gen.Elements<string?>(null, null, "bonusAction", "other")
                // Any version: below v11 the keys must stay extension data, written back where they were.
                from groupName in Gen.Elements<string?>(null, null, "forms")
                from groupContent in OptionalValue(Guids)
                from layer in Gen.Elements<string?>(null, null, "primary", "overlay")
                from allowedWith in Gen.Elements<bool>(false, false, true)
                from partner in Guids
                from allowedInBase in OptionalValue(Gen.Elements(true, false))
                from toggleCost in Optional(Gen.Elements("1", "2", "PB"))
                from costContent in OptionalValue(Guids)
                from toggleLevel in OptionalValue(Gen.Choose(1, 20))
                from duration in Optional(Gen.Elements("1", "3", "PB"))
                // Content v12 (MECH-X item 1), any version: typed from v12, below it extension data written back where it was.
                from payment in V12Slot(v, "payment", PaymentValue)
                select new JsonObject { ["toggleId"] = id, ["label"] = label }.With("resourceId", resource)
                    .With("autoWhen", autoWhen).With("activation", activation)
                    .With("group", groupName).With("groupContent", groupContent?.ToString()).With("layer", layer)
                    .With("allowedWith", allowedWith ? new JsonArray(new JsonObject { ["contentId"] = partner.ToString(), ["toggleId"] = "tide" }) : null)
                    .With("allowedInBase", allowedInBase).With("cost", toggleCost).With("resourceContent", costContent?.ToString())
                    .With("level", toggleLevel).With("duration", duration).WithV12(payment)),
            Common(UpkeepEffect.TypeName,
                from toggle in Optional(Keys)
                from at in Gen.Elements("turnStart", "turnEnd")
                from resourceId in Keys
                from change in Gen.Elements("spend", "regain")
                from amount in Gen.Elements("1", "2", "PB", "all")
                from ifShort in Gen.Elements<string?>(null, "ask", "end")
                select new JsonObject { ["at"] = at, ["resourceId"] = resourceId, ["change"] = change, ["amount"] = amount }
                    .With("toggle", toggle).With("ifShort", ifShort)),
            Common(AttackEffect.TypeName,
                from id in Keys
                from label in Names
                from kind in Gen.Elements("melee", "ranged")
                from abilities in Gen.Elements("str", "dex", "wis").ArrayOf().Select(a => a.Distinct().Take(3).ToArray())
                from proficient in OptionalValue(Gen.Elements(true, false))
                from count in Gen.Elements("1", "2", "PB")
                from die in Gen.Elements("6", "8", "SCALE.k")
                from bonus in Optional(Gen.Elements("2", "STR.MOD"))
                from damageType in Gen.Elements("fire", "bludgeoning")
                from range in Optional(Gen.Elements("60 ft", "10 ft"))
                from tags in Keys.ArrayOf().Select(t => t.Distinct().Take(3).ToArray())
                from replaces in OptionalValue(Gen.Elements(true, false))
                from activation in Gen.Elements<string?>(null, "action", "bonusAction", "other")
                from resource in Optional(Keys)
                from resourceContent in OptionalValue(Guids)
                from cost in Optional(Gen.Elements("1", "2", "PB"))
                from whileToggle in Gen.Elements(false, false, true)
                from whileId in Keys
                // Content v12 (MECH-X item 1), any version: typed from v12, below it extension data written back where it was.
                from payment in V12Slot(v, "payment", PaymentValue)
                select new JsonObject
                {
                    ["attackId"] = id, ["label"] = label, ["kind"] = kind,
                    ["abilities"] = new JsonArray([.. abilities.Select(a => (JsonNode)a)]),
                    ["damage"] = new JsonObject { ["count"] = count, ["die"] = die },
                    ["damageType"] = damageType,
                }
                .With("proficient", proficient).With("damageBonus", bonus).With("range", range)
                .With("tags", tags.Length == 0 ? null : new JsonArray([.. tags.Select(t => (JsonNode)t)]))
                .With("replacesAttack", replaces).With("activation", activation)
                .With("resourceId", resource).With("resourceContent", resourceContent?.ToString()).With("cost", cost)
                .With("whileToggle", whileToggle ? new JsonObject { ["toggleId"] = whileId } : null).WithV12(payment)),
            Common(AttackRiderEffect.TypeName,
                from tags in Keys.ArrayOf().Select(t => t.Distinct().Take(3).ToArray())
                from toHit in Optional(Formulas)
                from dice in Optional(Gen.Elements("1d8", "3d8", "2d6"))
                from bonus in Optional(Formulas)
                from damageType in Optional(Gen.Elements("fire", "cold"))
                from toggle in Optional(Keys)
                from toggleContent in OptionalValue(Guids)
                from stackGroup in Optional(Keys)
                select new JsonObject { ["tags"] = new JsonArray([.. tags.Select(t => (JsonNode)t)]) }
                    .With("toHit", toHit).With("dice", dice).With("damageBonus", bonus).With("damageType", damageType)
                    .With("toggle", toggle).With("toggleContent", toggleContent?.ToString()).With("stackGroup", stackGroup)),
            Common(ValueEffect.TypeName,
                from valueId in Keys
                from formula in Formulas
                select new JsonObject { ["valueId"] = valueId, ["formula"] = formula }),
            Common(RollModeEffect.TypeName,
                from target in Gen.Elements("save.dex", "save.con", "skill.stealth", "skill.sleightOfHand", "ability.str.check")
                from mode in Gen.Elements("advantage", "disadvantage")
                from against in Gen.Elements(0, 0, 1, 2)
                from toggle in Optional(Keys)
                from toggleContent in OptionalValue(Guids)
                select new JsonObject { ["target"] = target, ["mode"] = mode }
                    .With("against", against == 0 ? null : new JsonArray([.. ConditionKeys.All.Take(against).Select(k => (JsonNode)k)]))
                    .With("toggle", toggle).With("toggleContent", toggleContent?.ToString())),
            Common(ResponseEffect.TypeName,
                from responseId in Keys
                from label in Names
                from phase in Gen.Elements("damageReduction", "hitPointOutcome", "threshold")
                from amount in Optional(Formulas)
                from when in Gen.Elements(0, 1, 2)
                from resource in Optional(Keys)
                from resourceContent in OptionalValue(Guids)
                from cost in Optional(Formulas)
                from toggle in Optional(Keys)
                from toggleContent in OptionalValue(Guids)
                from kinds in Gen.Elements("spend", "regain", "toggleOn", "offerRoll", "regainHitPoints", "temporaryHitPoints").ArrayOf().Select(k => k.Take(3).ToArray())
                from outcomeContent in OptionalValue(Guids)
                select new JsonObject { ["responseId"] = responseId, ["label"] = label, ["phase"] = phase }
                    .With("amount", amount)
                    .With("when", when == 0 ? null : when == 1 ? new JsonObject { ["atOrBelow"] = "floor(HP.MAX / 4)" } : new JsonObject { ["survived"] = true })
                    .With("resourceId", resource).With("resourceContent", resourceContent?.ToString()).With("cost", cost)
                    .With("toggle", toggle is null ? null : new JsonObject { ["toggleId"] = toggle }.With("contentId", toggleContent?.ToString()))
                    .With("outcomes", kinds.Length == 0 ? null : new JsonArray([.. kinds.Select(k => (JsonNode)new JsonObject { ["kind"] = k }
                        .With("resourceId", (k == "spend" || k == "regain") ? "pool" : null).With("resourceContent", (k == "spend" || k == "regain") ? outcomeContent?.ToString() : null)
                        .With("amount", k != "toggleOn" && k != "offerRoll" ? "1" : null)
                        .With("toggle", k == "toggleOn" ? new JsonObject { ["toggleId"] = "form" } : null)
                        .With("rollId", k == "offerRoll" ? "roll" : null).With("rollContent", k == "offerRoll" ? outcomeContent?.ToString() : null))]))),
            Common(ToggleGroupEffect.TypeName,
                from groupId in Keys
                from label in Names
                select new JsonObject { ["groupId"] = groupId, ["label"] = label }),
            Common(ItemEffect.TypeName,
                from attune in OptionalValue(Gen.Elements(true, false))
                from max in OptionalValue(Gen.Choose(1, 99))
                from period in Gen.Elements<string?>(null, "shortRest", "longRest")
                from regain in Optional(Gen.Elements("1d4", "1d6+1"))
                select new JsonObject().With("requiresAttunement", attune)
                    .With("charges", max is { } m
                        ? new JsonObject { ["max"] = m }.With("recovery", period is null ? null : new JsonObject { ["period"] = period }.With("regain", regain))
                        : null)),
            // Typed from v12, unknown (and unchanged) below. Nested modifiers are the restricted shape the validator allows.
            Common(ItemModEffect.TypeName,
                from applies in Gen.Elements("weapon", "armor", "any")
                from bonus in OptionalValue(Gen.Choose(0, ItemModEffect.MaxBonus))
                from targets in Gen.Elements(FieldIds.Initiative, FieldIds.Score(Ability.Dex), FieldIds.ProficiencyBonus).ArrayOf().Select(t => t.Take(2).ToArray())
                from value in Formulas
                select new JsonObject { ["appliesTo"] = applies }.With("bonus", bonus)
                    .With("modifiers", targets.Length == 0 ? null : new JsonArray([.. targets.Select((t, i) => (JsonNode)new JsonObject
                    {
                        ["type"] = ModifierEffect.TypeName, ["id"] = "nested" + i, ["operation"] = "bonus", ["target"] = t, ["value"] = value,
                    })]))),
            // Typed from v12, unknown (and unchanged) below. Never lists attacks or pools: those are checked by the validator, not the reader.
            Common(CompanionEffect.TypeName,
                from companionId in Guids
                from label in Gen.Elements("Fixture Hound", "Fixture Hawk", "Fixture Ox", "Fixture Wisp")
                from speed in Optional(Gen.Elements("30 ft.", "40 ft.", "fly 60 ft."))
                select new JsonObject { ["companionId"] = companionId.ToString("D"), ["label"] = label, ["armorClass"] = "13", ["hitPointMaximum"] = "5 * LEVEL" }
                    .With("speed", speed)),
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
            Common(WeaponEffect.TypeName,
                from category in Gen.Elements("simple", "martial")
                from attack in Gen.Elements("melee", "ranged")
                from damage in Gen.Elements("1d4", "1d6", "1d8", "2d6")
                from damageType in Gen.Elements("slashing", "piercing", "bludgeoning")
                from key in Keys
                from properties in Gen.Elements("finesse", "light", "thrown", "versatile", "two-handed").ArrayOf().Select(p => p.Distinct().Take(3).ToArray())
                from range in Optional(Gen.Elements("20/60", "80/320"))
                // The v12 key at every version, sometimes absent and in an arbitrary position: from v12 a whole number 0 to 10; below
                // v12 any JSON value under either spelling, which must stay extension data, written back where it was.
                from bonus in Gen.Elements(Enumerable.Range(0, 11).Select(n => (JsonNode?)n).Concat([null]))
                from oddBonus in Gen.Elements<JsonNode?>(2, "2", 1.5, null, true, -3)
                from spelling in Gen.Elements("bonus", "Bonus")
                from present in Gen.Elements(false, true, true)
                from position in Gen.Choose(0, 6)
                select WithBonus(
                    new JsonObject
                    {
                        ["category"] = category, ["attack"] = attack, ["damage"] = damage, ["damageType"] = damageType, ["weaponKey"] = key,
                        ["properties"] = new JsonArray([.. properties.Select(p => (JsonNode)p)]),
                    }.With("range", range),
                    present, v >= ContentRevision.WeaponBonusSchemaVersion ? "bonus" : spelling,
                    v >= ContentRevision.WeaponBonusSchemaVersion ? (bonus ?? 0) : oddBonus, position)),
            Common(SpellEffect.TypeName,
                from level in Gen.Choose(0, SpellcastingEffect.MaxSpellLevel)
                from school in Optional(Gen.Elements("evocation", "illusion", "conjuration"))
                from lists in Keys.ArrayOf().Select(l => l.Distinct().Take(3).ToArray())
                from concentration in Gen.Elements(true, false)
                from attack in Gen.Elements("none", "melee", "ranged")
                from dice in Optional(Gen.Elements("1d10", "3d6", "8d6"))
                // Content v12 (MECH-X item 1), any version: typed from v12, below it extension data written back where it was.
                from tags in V12Slot(v, "tags", Keys.ArrayOf().Select(t => (JsonNode?)new JsonArray([.. t.Distinct().Take(3).Select(x => (JsonNode)x)])))
                select new JsonObject
                {
                    ["level"] = level, ["concentration"] = concentration, ["lists"] = new JsonArray([.. lists.Select(l => (JsonNode)l)]), ["attack"] = attack,
                }.With("school", school).With("dice", dice).WithV12(tags)),
    ];

    private static JsonObject WithBonus(JsonObject weapon, bool present, string name, JsonNode? value, int position)
    {
        if (present)
            weapon.Insert(Math.Min(position, weapon.Count), name, value?.DeepClone()); // a node has one parent, and the generator reuses values
        return weapon;
    }

    /// <summary>A content v12 key of the MECH-X item 1 types: whether it is there, under which spelling, with what value, and where.</summary>
    private readonly record struct V12Key(bool Present, string Name, JsonNode? Value, int Position);

    /// <summary>
    /// The key <paramref name="name"/> for a revision of content schema <paramref name="v"/>, sometimes absent and in an arbitrary
    /// position: from v12 the typed value under the real name; below v12 any JSON value under either spelling, which must stay
    /// extension data, written back where it was.
    /// </summary>
    private static Gen<V12Key> V12Slot(int v, string name, Gen<JsonNode?> typed) =>
        from present in Gen.Elements(false, true, true)
        from spelling in Gen.Elements(name, char.ToUpperInvariant(name[0]) + name[1..])
        from position in Gen.Choose(0, 12)
        from valid in typed
        from odd in Gen.Elements<JsonNode?>(2, "2", 1.5, null, true, new JsonObject { ["kind"] = "hp" }, new JsonObject { ["kind"] = "slot" }, new JsonArray(1, 2))
        select v >= ContentRevision.PaymentSchemaVersion ? new V12Key(present, name, valid, position) : new V12Key(present, spelling, odd, position);

    private static Gen<JsonNode?> PaymentValue =>
        from kind in Gen.Elements("resource", "hitPoints", "slot")
        from drops in OptionalValue(Gen.Elements(true, false))
        from level in OptionalValue(Gen.Choose(1, 9))
        select (JsonNode?)new JsonObject { ["kind"] = kind }.With("mayDropToZero", drops).With("minSlotLevel", level);

    private static Gen<JsonNode?> ReplaceableValue =>
        from when in Gen.Elements("levelUp", "longRest")
        from max in OptionalValue(Gen.Choose(1, 20))
        select (JsonNode?)new JsonObject { ["when"] = when }.With("max", max);

    private static JsonObject WithV12(this JsonObject o, V12Key key)
    {
        if (key.Present)
            o.Insert(Math.Min(key.Position, o.Count), key.Name, key.Value?.DeepClone()); // a node has one parent, and the generator reuses values
        return o;
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
        // Schema v9 fields, mostly absent: a character is written as v8 unless one of them is set (R73).
        from xp in OptionalValue(Gen.Choose(0, Character.MaxXp))
        from alignment in Optional(Names)
        from attuned in OptionalValue(Gen.Elements(true, false))
        // Schema v10 fields (D43 slice 3), sometimes set: an encounter round, and rounds left on active toggles. A character is written as v10 only then.
        from round in OptionalValue(Gen.Choose(0, PlayState.MaxRounds))
        from toggleIds in Gen.Choose(0, 2)
        from roundsLeft in OptionalValue(Gen.Choose(0, PlayState.MaxRounds)).ArrayOf(2)
        // Schema v11 field (MECH-X item 3), mostly absent: 0 to 2 mods on the first equipment entry, never equal to its item. A character is written as v11 only then.
        from modCount in Gen.Frequency((6, Gen.Constant(0)), (1, Gen.Constant(1)), (1, Gen.Constant(2)))
        from mods in References.ArrayOf(2)
        // Schema v11 field (MECH-X item 4), mostly absent: one play row for a content companion. A character is written as v11 then.
        from hasContentCompanion in Gen.Frequency((6, Gen.Constant(false)), (1, Gen.Constant(true)))
        from companionId in Guids
        from companionContent in Guids
        from companionCurrent in Gen.Choose(0, PlayState.MaxHitPoints)
        // A field this build does not know: it round-trips through Character.Extensions.
        from extra in Optional(Keys)
        from extraNumber in Gen.Choose(-1000, 1000)
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
            Equipment = [.. items.Select((item, i) => new EquipmentEntry(item, equipped[i], quantity[i], Attuned: i == 0 ? attuned : null, Mods: i == 0 && modCount > 0 ? [.. mods.Take(modCount)] : null))],
            Xp = xp,
            Details = alignment is null ? null : new(Alignment: alignment),
            Spells = [.. spells.Select((s, i) => new KnownSpell(caster, s, i % 2 == 0))],
            CrossFamilyExceptions = exception is null || pins.Count == 0 ? [] : [new CrossFamilyException(pins[0], exception)],
            Play = new()
            {
                Inspiration = inspiration,
                TemporaryHitPoints = temporary,
                Encounter = round is { } r ? new EncounterState(r) : null,
                Toggles = [.. Enumerable.Range(0, toggleIds).Select(i => new ActiveToggle(caster, "toggle-" + i, roundsLeft[i]))],
                Companions = hasContentCompanion ? [new CompanionPlay(companionId, companionCurrent, 0, companionContent)] : null,
            },
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(updated),
            ArchivedAt = archived is { } a ? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(a) : null,
            Extensions = extra is null ? null : new()
            {
                ["x" + extra] = JsonSerializer.SerializeToElement(new JsonObject { ["kept"] = extra, ["list"] = new JsonArray(extraNumber, "two", true, null) }),
            },
        };
}
