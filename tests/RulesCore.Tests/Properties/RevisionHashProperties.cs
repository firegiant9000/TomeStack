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
            var unknown = Generators.StaysUnknown(type, version);
            if (read.Effects[i] is UnknownEffect != unknown)
                return $"effect {i} ({type}) is {(unknown ? "typed" : "unknown")} in v{version}";
            if (unknown && !JsonNode.DeepEquals(inputEffects[i], outputEffects[i]))
                return $"unknown effect {i} ({type}) changed: {outputEffects[i]!.ToJsonString()}";
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
            var members = NullableMembers.GetValueOrDefault(type)
                ?? (type == ToggleEffect.TypeName && version >= ToggleEffect.SchemaVersion ? ["resourceId"]
                : type == ArmorEffect.TypeName && version >= ArmorEffect.SchemaVersion ? ["dexterityCap"]
                : null);
            if (members is null)
                continue;
            foreach (var name in members.Append("text"))
                effect.TryAdd(name, null);
        }
        return revision;
    }
}
