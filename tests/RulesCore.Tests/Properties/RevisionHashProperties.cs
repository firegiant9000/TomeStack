using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// T3 property 1 (ADR-002 insert-only revisions, ADR-003 typed effects): a revision's hash is SHA-256 over its compact
/// serializer output (<c>SqliteStore.Sha256(SqliteStore.Serialize(revision))</c>), so that output must be a fixed point for
/// every content schema version, and an optional field written as null must hash like the same field left out.
/// </summary>
public class RevisionHashProperties
{
    internal static ContentRevision Read(string json) => JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Compact)!;

    internal static string Write(ContentRevision revision) => JsonSerializer.Serialize(revision, RulesJson.Compact);

    internal static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    /// <summary>The toggle keys content v11 adds (D43). Written out here on purpose: a key added to the build must be added here too.</summary>
    internal static readonly string[] ToggleV11Keys = ["group", "groupContent", "layer", "allowedWith", "allowedInBase", "cost", "resourceContent", "level", "duration"];

    /// <summary>The resource keys content v11 adds (D43, slice 3). Written out here on purpose, like <see cref="ToggleV11Keys"/>.</summary>
    internal static readonly string[] ResourceV11Keys = ["counts", "resourceFull", "allowance"];

    /// <summary>The modifier key content v11 adds (D43, slice 4). Written out here on purpose, like <see cref="ToggleV11Keys"/>.</summary>
    internal static readonly string[] ModifierV11Keys = ["toggleContent"];

    /// <summary>The roll key content v11 adds (D43, slice 4). Written out here on purpose, like <see cref="ToggleV11Keys"/>.</summary>
    internal static readonly string[] RollV11Keys = ["whileToggle", "save", "area", "range", "targets", "perUnit", "alsoChange", "drawsDown"];

    /// <summary>The grant key content v11 adds (D43, slice 6). Written out here on purpose, like <see cref="ToggleV11Keys"/>.</summary>
    internal static readonly string[] GrantV11Keys = ["whileToggle"];

    /// <summary>The weapon key content v12 adds (MECH-X item 0). Written out here on purpose, like <see cref="ToggleV11Keys"/>.</summary>
    internal static readonly string[] WeaponV12Keys = ["bonus"];

    /// <summary>The roll keys content v12 adds (MECH-X item 1). Written out here on purpose, like <see cref="ToggleV11Keys"/>.</summary>
    internal static readonly string[] RollV12Keys = ["payment", "castsSpell"];

    /// <summary>The attack key content v12 adds (MECH-X item 1). Written out here on purpose, like <see cref="ToggleV11Keys"/>.</summary>
    internal static readonly string[] AttackV12Keys = ["payment"];

    /// <summary>The toggle key content v12 adds (MECH-X item 1). Written out here on purpose, like <see cref="ToggleV11Keys"/>.</summary>
    internal static readonly string[] ToggleV12Keys = ["payment"];

    /// <summary>The spell key content v12 adds (MECH-X item 1). Written out here on purpose, like <see cref="ToggleV11Keys"/>.</summary>
    internal static readonly string[] SpellV12Keys = ["tags"];

    /// <summary>The choice key content v12 adds (MECH-X item 2). Written out here on purpose, like <see cref="ToggleV11Keys"/>.</summary>
    internal static readonly string[] ChoiceV12Keys = ["replaceable"];

    /// <summary>Content v11 (slice 5): a roll may omit its dice; below v11 a roll with no dice stays an unknown effect, byte for byte.</summary>
    private static bool IsDiceLessRollBelowV11(string type, int version, JsonNode? effect) =>
        type == RollEffect.TypeName && version < ContentRevision.PlayMechanicsSchemaVersion && effect?["dice"] is null;

    public static TheoryData<int> Versions => [.. Enumerable.Range(1, ContentRevision.CurrentSchemaVersion)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void The_golden_revision_of_each_schema_version_keeps_its_hash(int version)
    {
        var golden = GoldenRevisions.Json(version);
        var stored = Write(Read(golden));

        Assert.Equal(version, (int)JsonNode.Parse(golden)!["schemaVersion"]!);
        Assert.True(GoldenRevisions.Hashes[version - 1] == Hash(stored), $"The v{version} golden revision now hashes to {Hash(stored)}.");
        Assert.Equal(stored, Write(Read(stored)));
        // v1 is the one version upcast on read (to v2); every later version is stored as written.
        Assert.Equal(version == 1 ? ContentRevision.TypedEffectsSchemaVersion : version, Read(stored).SchemaVersion);
        // The typed effects cover the version's own fields; the unknown ones (an unknown type, a versioned type below its
        // version) and the unknown top-level field must come back exactly as written.
        Assert.Null(UnknownPartsChanged(version, JsonNode.Parse(golden)!.AsObject(), stored));
        Assert.Contains(Read(stored).Effects, e => e is not UnknownEffect);
    }

    /// <summary>
    /// Null when every effect of <paramref name="input"/> is typed or unknown as <see cref="Generators.StaysUnknown"/> says,
    /// every unknown effect, every below-v9 <c>multiclassCasterTable</c> and every unknown top-level field is in
    /// <paramref name="stored"/> exactly as in the input; otherwise what differs.
    /// </summary>
    internal static string? UnknownPartsChanged(int version, JsonObject input, string stored)
    {
        var output = JsonNode.Parse(stored)!.AsObject();
        var read = Read(stored);
        var inputEffects = input["effects"]?.AsArray() ?? [];
        var outputEffects = output["effects"]?.AsArray() ?? [];
        if (inputEffects.Count != outputEffects.Count || read.Effects.Count != inputEffects.Count)
            return $"{inputEffects.Count} effects in, {outputEffects.Count} out";
        for (var i = 0; i < inputEffects.Count; i++)
        {
            var type = (string)inputEffects[i]!["type"]!;
            // Content v10: a damage-defence grant is a value a v9 build cannot read, so below v10 the effect stays unknown.
            var unknown = Generators.StaysUnknown(type, version)
                || IsDiceLessRollBelowV11(type, version, inputEffects[i])
                || (type == GrantEffect.TypeName && version < ContentRevision.PlayDetailsSchemaVersion
                    && ((string?)inputEffects[i]!["grant"])?.StartsWith("damage", StringComparison.OrdinalIgnoreCase) == true);
            if (read.Effects[i] is UnknownEffect != unknown)
                return $"effect {i} ({type}) is {(unknown ? "typed" : "unknown")} in v{version}";
            if (unknown && !JsonNode.DeepEquals(inputEffects[i], outputEffects[i]))
                return $"unknown effect {i} ({type}) changed: {outputEffects[i]!.ToJsonString()}";
            // Content v10: below v10 these keys stay extension data with the value they had (a v9 build never binds them).
            if (version < ContentRevision.PlayDetailsSchemaVersion && !unknown)
            {
                foreach (var key in new[] { "autoWhen", "activation", "damageType" })
                {
                    if (type != RollEffect.TypeName && inputEffects[i]![key] is { } kept && !JsonNode.DeepEquals(kept, outputEffects[i]![key]))
                        return $"effect {i}'s {key} changed below v10";
                }
            }
            // Content v11: below v11 the keys of these types stay extension data with the value they had, where they were.
            if (version < ContentRevision.PlayMechanicsSchemaVersion && !unknown && type is ToggleEffect.TypeName or ResourceEffect.TypeName or ModifierEffect.TypeName or RollEffect.TypeName or GrantEffect.TypeName)
            {
                foreach (var key in type switch { ToggleEffect.TypeName => ToggleV11Keys, ResourceEffect.TypeName => ResourceV11Keys, ModifierEffect.TypeName => ModifierV11Keys, GrantEffect.TypeName => GrantV11Keys, _ => RollV11Keys })
                {
                    if (inputEffects[i]![key] is { } kept && !JsonNode.DeepEquals(kept, outputEffects[i]![key]))
                        return $"effect {i}'s {key} changed below v11";
                }
            }
            // Content v12: below v12 the weapon's bonus key stays extension data with the value it had (any spelling, any value), where it was.
            if (version < ContentRevision.WeaponBonusSchemaVersion && !unknown && type == WeaponEffect.TypeName)
            {
                foreach (var (name, kept) in inputEffects[i]!.AsObject().Where(p => WeaponV12Keys.Contains(p.Key, StringComparer.OrdinalIgnoreCase)))
                {
                    if (!JsonNode.DeepEquals(kept, outputEffects[i]![name]))
                        return $"effect {i}'s {name} changed below v12";
                }
            }
            // Content v12 (MECH-X item 1): below v12 the payment, castsSpell and tags keys of these types stay extension data with the
            // value they had (any spelling, any value), where they were. Each type has its own list.
            if (version < ContentRevision.PaymentSchemaVersion && !unknown)
            {
                var v12Keys = type switch
                {
                    RollEffect.TypeName => RollV12Keys,
                    AttackEffect.TypeName => AttackV12Keys,
                    ToggleEffect.TypeName => ToggleV12Keys,
                    SpellEffect.TypeName => SpellV12Keys,
                    ChoiceEffect.TypeName => ChoiceV12Keys,
                    _ => [],
                };
                foreach (var (name, kept) in inputEffects[i]!.AsObject().Where(p => v12Keys.Contains(p.Key, StringComparer.OrdinalIgnoreCase)))
                {
                    if (!JsonNode.DeepEquals(kept, outputEffects[i]![name]))
                        return $"effect {i}'s {name} changed below v12";
                }
            }
            if (type == SpellcastingEffect.TypeName && version < SpellcastingEffect.MulticlassTableSchemaVersion
                && inputEffects[i]!["multiclassCasterTable"] is { } table && !JsonNode.DeepEquals(table, outputEffects[i]!["multiclassCasterTable"]))
                return $"effect {i}'s multiclassCasterTable changed below v9";
        }
        foreach (var (name, value) in input.Where(p => p.Key.StartsWith('x')))
        {
            if (!JsonNode.DeepEquals(value, output[name]))
                return $"unknown field {name} changed";
        }
        return null;
    }

    [Property(MaxTest = 1000)]
    public Property Serialize_deserialize_serialize_is_byte_identical_for_every_schema_version() =>
        Prop.ForAll(Generators.AnyRevisionArb(), r =>
        {
            var first = Write(Read(r.Json.ToJsonString()));
            var second = Write(Read(first));
            return (first == second).Label($"v{r.Version}: {first} != {second}");
        });

    [Property(MaxTest = 500)]
    public Property An_optional_field_written_as_null_hashes_like_the_field_left_out() =>
        Prop.ForAll(Generators.AnyRevisionArb(r => r.Version >= ContentRevision.TypedEffectsSchemaVersion), r =>
        {
            var withNulls = WithExplicitNulls(r.Version, (JsonObject)r.Json.DeepClone());
            var expected = Hash(Write(Read(r.Json.ToJsonString())));
            return (Hash(Write(Read(withNulls.ToJsonString()))) == expected).Label($"v{r.Version}: {withNulls.ToJsonString()}");
        });

    /// <summary>
    /// Compared with the generated input, not with a second write: a build that dropped unknown data would drop it from
    /// both writes, and the byte-identity property above could not see it.
    /// </summary>
    [Property(MaxTest = 1000)]
    public Property A_versioned_effect_type_is_typed_only_from_its_schema_version_and_unknown_parts_come_back_as_written() =>
        Prop.ForAll(Generators.AnyRevisionArb(), r =>
            UnknownPartsChanged(r.Version, r.Json, Write(Read(r.Json.ToJsonString()))) is not { } changed
                ? true.ToProperty()
                : false.Label($"v{r.Version}: {changed}"));

    /// <summary>Nullable members of typed effects, by type; unknown effects keep their JSON exactly, so they get none.</summary>
    private static readonly Dictionary<string, string[]> NullableMembers = new(StringComparer.Ordinal)
    {
        [ModifierEffect.TypeName] = ["stackGroup", "toggle", "whileArmored"],
        [GrantEffect.TypeName] = ["content", "level", "onlyAs"],
        [ChoiceEffect.TypeName] = ["level", "onlyAs"],
        [ResourceEffect.TypeName] = [],
        [RecoveryEffect.TypeName] = [],
        [RestrictionEffect.TypeName] = ["multiclass", "group"],
        [RollEffect.TypeName] = ["resourceId", "activation", "resourceContent", "cost", "variableCost", "bonus"],
        [HitDieEffect.TypeName] = [],
    };

    private static JsonObject WithExplicitNulls(int version, JsonObject revision)
    {
        foreach (var name in new[] { "summary", "extendsChoice" })
            revision.TryAdd(name, null);
        foreach (var effect in revision["effects"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var type = (string)effect["type"]!;
            // A damage-defence grant below v10 stays an unknown effect, kept exactly as written (nothing to add).
            if (type == GrantEffect.TypeName && version < ContentRevision.PlayDetailsSchemaVersion && ((string?)effect["grant"])?.StartsWith("damage", StringComparison.OrdinalIgnoreCase) == true)
                continue;
            // A roll with no dice below v11 stays an unknown effect, kept exactly as written (nothing to add).
            if (IsDiceLessRollBelowV11(type, version, effect))
                continue;
            var members = NullableMembers.GetValueOrDefault(type)
                ?? (type == ToggleEffect.TypeName && version >= ToggleEffect.SchemaVersion ? ["resourceId"]
                : type == ArmorEffect.TypeName && version >= ArmorEffect.SchemaVersion ? ["dexterityCap"]
                : null);
            // Content v10 members are typed only from v10; below it a null stays extension data, as a v9 build keeps it.
            if (version >= ContentRevision.PlayDetailsSchemaVersion)
            {
                members = type switch
                {
                    "toggle" => [.. members ?? [], "autoWhen", "activation"],
                    "resource" => [.. members ?? [], "activation"],
                    "grant" => [.. members ?? [], "damageType"],
                    "item" => ["requiresAttunement", "charges"],
                    _ => members,
                };
            }
            // Content v11 members are typed only from v11, for the same reason.
            if (version >= ContentRevision.PlayMechanicsSchemaVersion && type == ToggleEffect.TypeName)
                members = [.. members ?? [], .. ToggleV11Keys];
            if (version >= ContentRevision.PlayMechanicsSchemaVersion && type == ResourceEffect.TypeName)
                members = [.. members ?? [], .. ResourceV11Keys];
            if (version >= ContentRevision.PlayMechanicsSchemaVersion && type == AttackEffect.TypeName)
                members = ["proficient", "damageBonus", "range", "tags", "replacesAttack", "activation", "resourceId", "resourceContent", "cost", "whileToggle"];
            if (version >= ContentRevision.PlayMechanicsSchemaVersion && type == AttackRiderEffect.TypeName)
                members = ["toHit", "dice", "damageBonus", "damageType", "toggle", "toggleContent", "stackGroup"];
            if (version >= ContentRevision.PlayMechanicsSchemaVersion && type == ModifierEffect.TypeName)
                members = [.. members ?? [], .. ModifierV11Keys];
            if (version >= ContentRevision.PlayMechanicsSchemaVersion && type == GrantEffect.TypeName)
                members = [.. members ?? [], .. GrantV11Keys];
            if (version >= ContentRevision.PlayMechanicsSchemaVersion && type == RollModeEffect.TypeName)
                members = ["against", "toggle", "toggleContent"];
            if (version >= ContentRevision.PlayMechanicsSchemaVersion && type == ResponseEffect.TypeName)
                members = ["amount", "when", "resourceId", "resourceContent", "cost", "toggle", "outcomes"];
            if (version >= ContentRevision.PlayMechanicsSchemaVersion && type == RollEffect.TypeName)
                members = [.. members ?? [], .. RollV11Keys, "dice"];
            // Content v12: the weapon's bonus is typed only from v12; below it a null stays extension data (and is generated that way).
            if (version >= ContentRevision.WeaponBonusSchemaVersion && type == WeaponEffect.TypeName)
                members = [.. members ?? [], .. WeaponV12Keys];
            // Content v12 (MECH-X item 1): the payment, castsSpell and tags keys are typed only from v12, so only there a null hashes like an absent key.
            if (version >= ContentRevision.PaymentSchemaVersion)
            {
                members = type switch
                {
                    RollEffect.TypeName => [.. members ?? [], .. RollV12Keys],
                    AttackEffect.TypeName => [.. members ?? [], .. AttackV12Keys],
                    ToggleEffect.TypeName => [.. members ?? [], .. ToggleV12Keys],
                    SpellEffect.TypeName => [.. members ?? [], .. SpellV12Keys],
                    ChoiceEffect.TypeName => [.. members ?? [], .. ChoiceV12Keys],
                    _ => members,
                };
            }
            // Content v12 (MECH-X item 3): the itemMod is typed only from v12, so only there a null bonus or modifiers hashes like an absent key.
            if (version >= ItemModEffect.SchemaVersion && type == ItemModEffect.TypeName)
                members = ["bonus", "modifiers"];
            if (members is null)
                continue;
            foreach (var name in members.Append("text"))
                effect.TryAdd(name, null);
        }
        return revision;
    }
}
