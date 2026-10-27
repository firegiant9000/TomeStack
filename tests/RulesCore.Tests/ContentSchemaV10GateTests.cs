using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v10 (Wave 3, item 24, D42), Review Focus 1: the version gate. A revision stored by an older build (v9 or
/// below) may already carry a v10 key. It must read, write back and hash exactly as a v9 build does, and the key must have
/// no effect. These tests use JSON only, so they run unchanged against a v9 build: that is how "what a v9 build does today"
/// was established before the gate was written.
/// </summary>
public class ContentSchemaV10GateTests
{
    private static string Write(ContentRevision revision) => JsonSerializer.Serialize(revision, RulesJson.Compact);

    private static ContentRevision Read(string json) => JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Compact)!;

    private static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static ContentRevision Revision(ContentKind kind, int version, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse("c10a0000-0000-4000-8000-000000000001"), RevisionId = Guid.Parse("c10b0000-0000-4000-8000-000000000001"),
        Kind = kind, Name = "Fixture Gate", RulesFamilies = [RulesFamilies.Srd51], Provenance = new(Fixtures.SourceShared, new PageRef(40)),
        Status = RevisionStatus.Published, SchemaVersion = version, Effects = effects,
    };

    private static ToggleEffect Toggle() => new() { Id = "t", ToggleId = "stance", Label = "Fixture Stance" };

    private static ResourceEffect Pool() => new() { Id = "p", ResourceId = "pool", Label = "Fixture Pool", Maximum = "2" };

    private static GrantEffect Skill() => new() { Id = "g", Grant = GrantKind.Proficiency, Target = FieldIds.Skill("stealth") };

    /// <summary>Compact JSON of the revision with <paramref name="name"/> added to effect 0, at the end (where a stored extension goes) or at <paramref name="at"/>.</summary>
    private static string WithKey(ContentRevision revision, string name, string valueJson, int? at = null)
    {
        var node = JsonNode.Parse(Write(revision))!;
        var effect = node["effects"]![0]!.AsObject();
        var value = JsonNode.Parse(valueJson);
        if (at is { } index)
            effect.Insert(index, name, value);
        else
            effect.Add(name, value);
        return node.ToJsonString(RulesJson.Compact);
    }

    /// <summary>Compact JSON of the revision with effect 0 replaced by <paramref name="effectJson"/>.</summary>
    private static string WithEffect(ContentRevision revision, string effectJson)
    {
        var node = JsonNode.Parse(Write(revision))!;
        node["effects"]!.AsArray()[0] = JsonNode.Parse(effectJson);
        return node.ToJsonString(RulesJson.Compact);
    }

    private static CharacterSheet Sheet(ContentRevision revision)
    {
        var pack = Fixtures.Pack();
        var character = Fixtures.Srd51Character();
        var catalog = new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, revision]);
        return CharacterCalculator.Calculate(character with { Pins = [.. character.Pins, revision.Reference] }, catalog);
    }

    private static string SheetJson(ContentRevision revision) => JsonSerializer.Serialize(Sheet(revision), RulesJson.Options);

    private static string Codes(ValidationReport report) =>
        string.Join(",", report.Errors.Select(e => "E:" + e.Code).Concat(report.Warnings.Select(w => "W:" + w.Code))) + "|v" + report.RequiredSchemaVersion;

    /// <summary>The stored form: read, written back, hashed. Equal bytes and hash, key still extension data, no effect on the sheet or validation.</summary>
    private static void AssertInert(ContentKind kind, int version, Effect effect, string key, string valueJson, Type typedAs)
    {
        var plain = Revision(kind, version, effect);
        var stored = WithKey(plain, key, valueJson);

        var read = Read(stored);
        Assert.IsType(typedAs, read.Effects[0]);
        Assert.True(read.Effects[0].Extensions?.ContainsKey(key) == true, "The key must stay extension data.");
        var written = Write(read);
        Assert.Equal(stored, written);
        Assert.Equal(Hash(stored), Hash(written));

        // As a v9 build reads it: only the effect is read, the key adds nothing. Same sheet, same validation.
        var withoutKey = Read(Write(plain));
        Assert.Equal(SheetJson(withoutKey), SheetJson(read));
        var catalog = Fixtures.M1Catalog();
        Assert.Equal(Codes(ContentValidator.Validate(withoutKey, catalog)), Codes(ContentValidator.Validate(read, catalog)));
    }

    public static TheoryData<int> ToggleVersions => [6, 8, 9];

    [Theory]
    [MemberData(nameof(ToggleVersions))]
    public void Below_v10_a_toggle_autoWhen_stays_extension_data_byte_for_byte_and_does_nothing(int version) =>
        AssertInert(ContentKind.Feat, version, Toggle(), "autoWhen", "\"bloodied\"", typeof(ToggleEffect));

    [Theory]
    [MemberData(nameof(ToggleVersions))]
    public void Below_v10_a_toggle_activation_stays_extension_data_byte_for_byte_and_does_nothing(int version) =>
        AssertInert(ContentKind.Feat, version, Toggle(), "activation", "\"bonusAction\"", typeof(ToggleEffect));

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(9)]
    public void Below_v10_a_resource_activation_stays_extension_data_byte_for_byte_and_does_nothing(int version) =>
        AssertInert(ContentKind.Feat, version, Pool(), "activation", "\"reaction\"", typeof(ResourceEffect));

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void Below_v10_a_grant_damageType_stays_extension_data_byte_for_byte_and_does_nothing(int version) =>
        AssertInert(ContentKind.Feat, version, Skill(), "damageType", "\"fire\"", typeof(GrantEffect));

    [Theory]
    [InlineData("AutoWhen", "\"bloodied\"")]
    [InlineData("AUTOWHEN", "\"Bloodied\"")]
    [InlineData("autoWhen", "null")]
    [InlineData("autoWhen", "\"not-a-value\"")]
    [InlineData("autoWhen", "3")]
    [InlineData("Activation", "\"action\"")]
    [InlineData("activation", "null")]
    [InlineData("activation", "{\"x\":1}")]
    public void Below_v10_every_spelling_and_value_of_a_toggle_key_stays_extension_data(string key, string value) =>
        AssertInert(ContentKind.Feat, 9, Toggle(), key, value, typeof(ToggleEffect));

    [Theory]
    [InlineData("Activation", "\"action\"")]
    [InlineData("ACTIVATION", "7")]
    [InlineData("activation", "null")]
    public void Below_v10_every_spelling_and_value_of_a_resource_key_stays_extension_data(string key, string value) =>
        AssertInert(ContentKind.Feat, 9, Pool(), key, value, typeof(ResourceEffect));

    [Theory]
    [InlineData("DamageType", "\"fire\"")]
    [InlineData("DAMAGETYPE", "\"not a damage type\"")]
    [InlineData("damageType", "null")]
    [InlineData("damageType", "[1,2]")]
    public void Below_v10_every_spelling_and_value_of_a_grant_damageType_stays_extension_data(string key, string value) =>
        AssertInert(ContentKind.Feat, 9, Skill(), key, value, typeof(GrantEffect));

    [Theory]
    [InlineData("autoWhen", 0)]
    [InlineData("Activation", 1)]
    [InlineData("autoWhen", 3)]
    public void Below_v10_a_toggle_key_in_the_middle_is_written_where_a_v9_build_writes_it(string key, int at)
    {
        // A hand-written revision can carry the key anywhere. A v9 build moves extension data after the typed properties;
        // this build must do the same, so the hash after the first write is what a v9 build stores.
        var plain = Revision(ContentKind.Feat, 9, Toggle());
        var middle = WithKey(plain, key, "\"bloodied\"", at);
        var expected = WithKey(plain, key, "\"bloodied\"");

        var read = Read(middle);
        Assert.True(read.Effects[0].Extensions?.ContainsKey(key) == true);
        Assert.Equal(expected, Write(read));
        Assert.Equal(expected, Write(Read(Write(read))));
    }

    [Fact]
    public void Below_v10_two_stripped_keys_and_another_extension_key_keep_document_order()
    {
        var plain = Revision(ContentKind.Feat, 9, Toggle());
        var node = JsonNode.Parse(Write(plain))!;
        var effect = node["effects"]![0]!.AsObject();
        effect.Add("zzFirst", 1);
        effect.Add("autoWhen", "bloodied");
        effect.Add("aaSecond", true);
        effect.Add("Activation", "action");
        var stored = node.ToJsonString(RulesJson.Compact);

        Assert.Equal(stored, Write(Read(stored)));
        Assert.Equal(["zzFirst", "autoWhen", "aaSecond", "Activation"], Read(stored).Effects[0].Extensions!.Keys);
    }

    [Theory]
    [InlineData("damageResistance")]
    [InlineData("damageImmunity")]
    [InlineData("damageVulnerability")]
    [InlineData("DamageResistance")]
    [InlineData("damageresistance")]
    [InlineData("DAMAGEVULNERABILITY")]
    public void Below_v10_a_damage_grant_kind_stays_an_unknown_effect_byte_for_byte_and_does_nothing(string kind)
    {
        // A v9 build cannot read this grant kind, so the effect stays unknown and reference-only. This build must not bind it.
        var plain = Revision(ContentKind.Feat, 9, Skill());
        var stored = WithEffect(plain, $"{{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"{kind}\",\"damageType\":\"fire\"}}");

        var read = Read(stored);
        var unknown = Assert.IsType<UnknownEffect>(read.Effects[0]);
        Assert.Equal("grant", unknown.DeclaredType);
        Assert.Equal(stored, Write(read));
        Assert.Equal(Hash(stored), Hash(Write(read)));

        var without = Read(WithEffect(plain, "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"bogus\"}"));
        Assert.Equal(SheetJson(without), SheetJson(read));
        var report = ContentValidator.Validate(read, Fixtures.M1Catalog());
        Assert.Empty(report.Errors);
        Assert.Contains(report.Warnings, w => w.Code == "validate.effect-unsupported");
        Assert.Equal(Codes(ContentValidator.Validate(without, Fixtures.M1Catalog())), Codes(report));
    }

    [Fact]
    public void Below_v10_a_damage_grant_kind_with_a_target_and_content_also_stays_unknown()
    {
        var plain = Revision(ContentKind.Feat, 3, Skill());
        var stored = WithEffect(plain, "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"damageResistance\",\"target\":\"skill.stealth\",\"damageType\":\"cold\",\"level\":2}");
        var read = Read(stored);
        Assert.IsType<UnknownEffect>(read.Effects[0]);
        Assert.Equal(stored, Write(read));
        Assert.Equal(Hash(stored), Hash(Write(read)));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(9)]
    public void Below_v10_an_item_effect_stays_an_unknown_effect_byte_for_byte_and_does_nothing(int version)
    {
        var plain = Revision(ContentKind.Item, version, Skill());
        var stored = WithEffect(plain, "{\"type\":\"item\",\"id\":\"i\",\"requiresAttunement\":true,\"charges\":{\"max\":3,\"recovery\":{\"period\":\"longRest\",\"regain\":\"1d4\"}}}");

        var read = Read(stored);
        var unknown = Assert.IsType<UnknownEffect>(read.Effects[0]);
        Assert.Equal("item", unknown.DeclaredType);
        Assert.Equal(stored, Write(read));
        Assert.Equal(Hash(stored), Hash(Write(read)));

        // Any other unknown type is the baseline: the sheet differs only in the type name its diagnostic quotes.
        var without = Read(WithEffect(plain, "{\"type\":\"bogusType\",\"id\":\"i\"}"));
        Assert.Equal(SheetJson(without).Replace("bogusType", "item", StringComparison.Ordinal), SheetJson(read));
        var report = ContentValidator.Validate(read, Fixtures.M1Catalog());
        Assert.Empty(report.Errors);
        Assert.Contains(report.Warnings, w => w.Code == "validate.effect-unsupported");
        Assert.Equal(Codes(ContentValidator.Validate(without, Fixtures.M1Catalog())), Codes(report));
    }

    [Theory]
    [InlineData("Item")]
    [InlineData("ITEM")]
    public void An_item_type_spelled_differently_is_not_the_item_effect_in_any_version(string type)
    {
        var plain = Revision(ContentKind.Item, 10, Skill());
        var stored = WithEffect(plain, $"{{\"type\":\"{type}\",\"id\":\"i\",\"charges\":{{\"max\":3}}}}");
        var read = Read(stored);
        Assert.IsType<UnknownEffect>(read.Effects[0]);
        Assert.Equal(stored, Write(read));
    }

    // ---- v10 revisions: the keys are typed, so they leave extension data --------------------------------------------

    [Fact]
    public void A_v10_revision_is_supported_and_keeps_its_version()
    {
        var read = Read(Write(Revision(ContentKind.Feat, 10, Toggle())));
        Assert.Equal(10, read.SchemaVersion);
        Assert.Equal(10, ContentRevision.PlayDetailsSchemaVersion);
    }

    [Theory]
    [InlineData("autoWhen", "\"bloodied\"")]
    [InlineData("AutoWhen", "\"bloodied\"")]
    [InlineData("activation", "\"bonusAction\"")]
    public void In_a_v10_revision_a_toggle_key_is_typed_not_extension_data(string key, string value)
    {
        var stored = WithKey(Revision(ContentKind.Feat, 10, Toggle()), key, value);
        var read = Read(stored);
        Assert.IsType<ToggleEffect>(read.Effects[0]);
        Assert.Null(read.Effects[0].Extensions);
        Assert.Equal(Hash(Write(read)), Hash(Write(Read(Write(read)))));
    }

    [Fact]
    public void In_a_v10_revision_a_resource_activation_and_a_grant_damageType_are_typed()
    {
        var resource = Read(WithKey(Revision(ContentKind.Feat, 10, Pool()), "activation", "\"reaction\""));
        Assert.Null(resource.Effects[0].Extensions);
        var grant = Read(WithEffect(Revision(ContentKind.Feat, 10, Skill()), "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"damageResistance\",\"damageType\":\"fire\"}"));
        var typed = Assert.IsType<GrantEffect>(grant.Effects[0]);
        Assert.Null(typed.Extensions);
    }
}
