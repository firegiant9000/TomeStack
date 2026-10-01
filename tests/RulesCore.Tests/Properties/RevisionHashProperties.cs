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
        // Each golden revision uses only types its version knows, so the hash covers that version's typed fields.
        Assert.DoesNotContain(Read(stored).Effects, e => e is UnknownEffect);
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

    [Property(MaxTest = 500)]
    public Property A_versioned_effect_type_is_typed_only_from_its_schema_version_and_kept_unchanged_below_it() =>
        Prop.ForAll(Generators.AnyRevisionArb(), r =>
        {
            var revision = Read(r.Json.ToJsonString());
            var effects = r.Json["effects"]?.AsArray() ?? [];
            return effects.Select((e, i) => (Type: (string)e!["type"]!, Effect: revision.Effects[i])).All(e => e.Type switch
            {
                ToggleEffect.TypeName => e.Effect is ToggleEffect == (r.Version >= ToggleEffect.SchemaVersion),
                ScaleEffect.TypeName => e.Effect is ScaleEffect == (r.Version >= ScaleEffect.SchemaVersion),
                ArmorEffect.TypeName => e.Effect is ArmorEffect == (r.Version >= ArmorEffect.SchemaVersion),
                SpellcastingEffect.TypeName => e.Effect is SpellcastingEffect == (r.Version >= SpellcastingEffect.SchemaVersion),
                _ => true,
            }).Label($"v{r.Version}");
        });

    /// <summary>Nullable members of typed effects, by type; unknown effects keep their JSON exactly, so they get none.</summary>
    private static readonly Dictionary<string, string[]> NullableMembers = new(StringComparer.Ordinal)
    {
        [ModifierEffect.TypeName] = ["stackGroup", "toggle", "whileArmored"],
        [GrantEffect.TypeName] = ["content", "level", "onlyAs"],
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
