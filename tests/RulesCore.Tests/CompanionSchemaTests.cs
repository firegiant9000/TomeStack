using System.Text.Json.Nodes;
using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (MECH-X item 4): the <c>companion</c> effect, typed only from v12. Below v12 it stays an unknown effect
/// and writes back exactly as the v11 build wrote it (the hashes below were captured from that build before any change, and
/// are never updated to make a test pass).
/// </summary>
public class CompanionSchemaTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private const string Head = "{\"contentId\":\"6a7e0100-0000-4000-8000-000000000021\",\"revisionId\":\"6a7e0100-0000-4000-8000-000000000221\",\"schemaVersion\":";

    private const string Middle = ",\"kind\":\"feature\",\"name\":\"Fixture Bond\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[";

    private const string Before = "{\"type\":\"modifier\",\"id\":\"a\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"1\",\"stacking\":\"stack\",\"automation\":\"automatic\",\"timing\":\"always\"}";

    private const string After = "{\"type\":\"modifier\",\"id\":\"b\",\"operation\":\"bonus\",\"target\":\"initiative\",\"value\":\"1\",\"stacking\":\"stack\",\"automation\":\"automatic\",\"timing\":\"always\"}";

    private const string Good = "{\"type\":\"companion\",\"id\":\"c1\",\"companionId\":\"6a7e0c00-0000-4000-8000-000000000001\",\"label\":\"Fixture Hound\",\"armorClass\":\"13\",\"hitPointMaximum\":\"5 * LEVEL\"}";

    private const string BadGuid = "{\"type\":\"companion\",\"id\":\"c1\",\"companionId\":\"not-a-guid\",\"label\":\"Fixture Hound\",\"armorClass\":\"13\",\"hitPointMaximum\":\"5 * LEVEL\"}";

    private const string EmptyGuid = "{\"type\":\"companion\",\"id\":\"c1\",\"companionId\":\"00000000-0000-0000-0000-000000000000\",\"label\":\"Fixture Hound\",\"armorClass\":\"13\",\"hitPointMaximum\":\"5 * LEVEL\"}";

    private const string UpperGuid = "{\"type\":\"companion\",\"id\":\"c1\",\"companionId\":\"6A7E0C00-0000-4000-8000-000000000001\",\"label\":\"Fixture Hound\",\"armorClass\":\"13\",\"hitPointMaximum\":\"5 * LEVEL\"}";

    private const string NullLabel = "{\"type\":\"companion\",\"id\":\"c1\",\"companionId\":\"6a7e0c00-0000-4000-8000-000000000001\",\"label\":null,\"armorClass\":\"13\",\"hitPointMaximum\":\"5 * LEVEL\"}";

    private const string AttacksNotArray = "{\"type\":\"companion\",\"id\":\"c1\",\"companionId\":\"6a7e0c00-0000-4000-8000-000000000001\",\"label\":\"Fixture Hound\",\"armorClass\":\"13\",\"hitPointMaximum\":\"5 * LEVEL\",\"attacks\":\"bite\"}";

    private static string Feature(int version, string companion) => Head + version + Middle + Before + "," + companion + "," + After + "]}";

    private static ContentRevision Read(string json) => RevisionHashProperties.Read(json);

    private static ValidationReport Validate(ContentRevision revision) => ContentValidator.Validate(revision, Catalog);

    private static ContentRevision Built(int version, ContentKind kind = ContentKind.Feature, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse("6a7e0100-0000-4000-8000-000000000021"), RevisionId = Guid.Parse("6a7e0100-0000-4000-8000-000000000221"),
        SchemaVersion = version, Kind = kind, Name = "Fixture Bond", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Guid.Parse("6a7e0000-0000-4000-8000-000000000001")), Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static CompanionEffect Companion(int n = 1, IReadOnlyList<string>? attacks = null, IReadOnlyList<string>? resourceIds = null) => new()
    {
        Id = "c" + n, CompanionId = new Guid(0x6a7e0c00, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, (byte)n), Label = "Fixture Hound",
        ArmorClass = "13", HitPointMaximum = "5 * LEVEL", Attacks = attacks, ResourceIds = resourceIds,
    };

    private static AttackEffect Bite(string id = "bite") => new()
    {
        Id = id, AttackId = id, Label = "Bite", Kind = WeaponAttack.Melee, Abilities = [Ability.Str], Damage = new("1", "6"), DamageType = "piercing",
    };

    private static ResourceEffect Pool(string resourceId = "fury") => new() { Id = resourceId + "-pool", ResourceId = resourceId, Label = "Fury", Maximum = "2" };

    private static ValidationReport ValidateCompanions(ContentKind kind, params Effect[] effects) => Validate(Built(12, kind, effects));

    [Fact]
    public void A_v12_companion_reads()
    {
        var json = Head + 12 + Middle
            + "{\"type\":\"resource\",\"id\":\"fury-pool\",\"resourceId\":\"fury\",\"label\":\"Fury\",\"maximum\":\"2\"},"
            + "{\"type\":\"attack\",\"id\":\"bite\",\"attackId\":\"bite\",\"label\":\"Bite\",\"kind\":\"melee\",\"abilities\":[\"str\"],\"damage\":{\"count\":\"1\",\"die\":\"6\"},\"damageType\":\"piercing\"},"
            + "{\"type\":\"companion\",\"id\":\"c1\",\"companionId\":\"6a7e0c00-0000-4000-8000-000000000001\",\"label\":\"Fixture Hound\",\"armorClass\":\"13\",\"hitPointMaximum\":\"5 * LEVEL\",\"attacks\":[\"bite\"],\"resourceIds\":[\"fury\"]}]}";
        var revision = Read(json);

        var companion = Assert.IsType<CompanionEffect>(revision.Effects[2]);
        Assert.Equal(["bite"], companion.Attacks);
        Assert.Equal(["fury"], companion.ResourceIds);
        Assert.Equal(Guid.Parse("6a7e0c00-0000-4000-8000-000000000001"), companion.CompanionId);
        Assert.Null(companion.Extensions);
        Assert.Equal(12, Validate(revision).RequiredSchemaVersion);
        Assert.DoesNotContain(Validate(revision).Errors, e => e.Code.StartsWith("validate.companion", StringComparison.Ordinal));
    }

    /// <summary>Captured on the v12 build before this change (b8e624c4), when a companion was an unknown effect at every version.</summary>
    [Theory]
    [InlineData(Good, "c19b672aac987b3ca94d2071f5e54a5b52d828272403962b92f49ec7ad0c235c")]
    [InlineData(BadGuid, "159d2839ad50fb4528f3d360828f2f941ee9dcd902ee734fab04b42d732bfa68")]
    [InlineData(EmptyGuid, "828ebf3e6fd16a39c1ac3e75529593f40bb1e5d35b684dbf20b74b80238dd7bb")]
    [InlineData(UpperGuid, "9146c5ebf9d9a59165479ac949bd3bce6d8dad6a0405ce5f3047f533214ae399")]
    [InlineData(NullLabel, "f1656fb08d69f9dc166e89d1476b9ea684b56209a37b69118a2c14c71c392477")]
    [InlineData(AttacksNotArray, "8ccb5ffe1882af27329d4fda08dca8fb3c505d4dd0660a3452140612a4d7c18e")]
    public void Below_v12_a_companion_stays_unknown_byte_for_byte(string companion, string hash)
    {
        var json = Feature(11, companion);
        var revision = Read(json);

        var stored = RevisionHashProperties.Write(revision);

        Assert.Equal(json, stored);
        Assert.Equal(hash, RevisionHashProperties.Hash(stored));
        Assert.IsType<UnknownEffect>(revision.Effects[1]);
        var report = Validate(revision);
        Assert.Contains(report.Warnings, w => w.Code == "validate.effect-unsupported");
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.effect-malformed");
        var without = Validate(Read(Head + 11 + Middle + Before + "," + After + "]}"));
        Assert.Equal(without.RequiredSchemaVersion, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_plain_feature_companion_passes_clean()
    {
        var report = ValidateCompanions(ContentKind.Feature, Companion());
        Assert.DoesNotContain(report.Errors, e => e.Code.StartsWith("validate.companion", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.formula-invalid");
        Assert.DoesNotContain(report.Warnings, w => w.Code.StartsWith("validate.companion", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("item")]
    [InlineData("spell")]
    [InlineData("label-blank")]
    [InlineData("label-long")]
    [InlineData("speed-long")]
    [InlineData("id-duplicate")]
    [InlineData("attack-unknown")]
    [InlineData("attack-twice")]
    [InlineData("attack-shared")]
    [InlineData("resource-unknown")]
    [InlineData("attacks-six")]
    [InlineData("companions-eleven")]
    [InlineData("armor-class")]
    public void Companion_rules(string rule)
    {
        var (code, report) = rule switch
        {
            "item" => ("validate.companion-kind", ValidateCompanions(ContentKind.Item, Companion())),
            "spell" => ("validate.companion-kind", ValidateCompanions(ContentKind.Spell, Companion())),
            "label-blank" => ("validate.companion-incomplete", ValidateCompanions(ContentKind.Feature, Companion() with { Label = " " })),
            "label-long" => ("validate.companion-incomplete", ValidateCompanions(ContentKind.Feature, Companion() with { Label = new string('x', 81) })),
            "speed-long" => ("validate.companion-incomplete", ValidateCompanions(ContentKind.Feature, Companion() with { Speed = new string('x', 81) })),
            "id-duplicate" => ("validate.companion-id-duplicate", ValidateCompanions(ContentKind.Feature, Companion(), Companion() with { Id = "c2" })),
            "attack-unknown" => ("validate.companion-attack", ValidateCompanions(ContentKind.Feature, Companion(1, ["nope"]))),
            "attack-twice" => ("validate.companion-attack", ValidateCompanions(ContentKind.Feature, Bite(), Companion(1, ["bite", "bite"]))),
            "attack-shared" => ("validate.companion-attack", ValidateCompanions(ContentKind.Feature, Bite(), Companion(1, ["bite"]), Companion(2, ["bite"]))),
            "resource-unknown" => ("validate.companion-resource", ValidateCompanions(ContentKind.Feature, Companion(1, null, ["nope"]))),
            "attacks-six" => ("validate.companion-too-many", ValidateCompanions(ContentKind.Feature,
                [.. Enumerable.Range(0, 6).Select(i => (Effect)Bite("b" + i)), Companion(1, [.. Enumerable.Range(0, 6).Select(i => "b" + i)])])),
            "companions-eleven" => ("validate.companion-too-many", ValidateCompanions(ContentKind.Feature, [.. Enumerable.Range(1, 11).Select(i => (Effect)Companion(i))])),
            _ => ("validate.formula-invalid", ValidateCompanions(ContentKind.Feature, Companion() with { ArmorClass = "1 +" })),
        };

        var error = Assert.Single(report.Errors, e => e.Code == code);
        if (rule == "armor-class")
            Assert.Contains("armor class", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.requires-v12");
    }

    [Theory]
    [InlineData(BadGuid)]
    [InlineData(EmptyGuid)]
    [InlineData(UpperGuid)]
    [InlineData(NullLabel)]
    [InlineData(AttacksNotArray)]
    public void Unreadable_companion_blocks_from_v12(string companion)
    {
        var json = Feature(12, companion);
        var revision = Read(json);

        var report = Validate(revision);

        Assert.IsType<UnknownEffect>(revision.Effects[1]);
        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed" && e.Message.Contains("type 'companion'", StringComparison.Ordinal));
        Assert.Equal(12, report.RequiredSchemaVersion);
        Assert.Equal(json, RevisionHashProperties.Write(revision));
        Assert.DoesNotContain(Validate(Read(Feature(11, companion))).Errors, e => e.Code == "validate.effect-malformed");
        Assert.Contains(Validate(Read(Feature(11, companion))).Warnings, w => w.Code == "validate.effect-unsupported");
    }

    [Fact]
    public void Companion_requires_v12()
    {
        var old = Validate(Built(11, ContentKind.Feature, Companion()));
        Assert.Contains(old.Errors, e => e.Code == "validate.requires-v12" && e.Message.Contains("a companion", StringComparison.Ordinal));

        var ok = Validate(Built(12, ContentKind.Feature, Companion()));
        Assert.DoesNotContain(ok.Errors, e => e.Code == "validate.requires-v12");
        Assert.Equal(12, ok.RequiredSchemaVersion);
    }

    [Fact]
    public void Companion_schema_version_is_current()
    {
        Assert.Equal(12, ContentRevision.CompanionSchemaVersion);
        Assert.Equal(ContentRevision.CompanionSchemaVersion, ContentRevision.CurrentSchemaVersion);
        Assert.Equal(ContentRevision.WeaponBonusSchemaVersion, ContentRevision.CompanionSchemaVersion);
        Assert.Equal(Character.MaxCompanionAttacks, CompanionEffect.MaxAttacks);
    }

    /// <summary>The three revisions of tests/RulesFixtures/golden/companion-revisions.json, hashes pinned in GoldenRevisions.cs.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Companion_goldens_keep_their_hashes(int index)
    {
        var golden = GoldenRevisions.CompanionJson(index);
        var stored = RevisionHashProperties.Write(Read(golden));

        Assert.True(GoldenRevisions.CompanionHashes[index] == RevisionHashProperties.Hash(stored), $"Companion golden {index} now hashes to {RevisionHashProperties.Hash(stored)}.");
        Assert.Equal(stored, RevisionHashProperties.Write(Read(stored)));
        Assert.Null(RevisionHashProperties.UnknownPartsChanged(12, JsonNode.Parse(golden)!.AsObject(), stored));
        Assert.All(Read(stored).Effects, e => Assert.IsNotType<UnknownEffect>(e));
        Assert.All(Validate(Read(stored)).Errors, e => Assert.Equal("validate.source-missing", e.Code)); // the fixture catalog has no source; nothing else is wrong
        Assert.IsType<CompanionEffect>(Read(stored).Effects[^1]);
    }
}
