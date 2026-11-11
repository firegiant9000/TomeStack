using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v13 (ADR-024): the creationGuide kind, the guideFor, primaryAbilities and startingEquipment effects, and
/// item.contains, typed only from v13. Below v13 they stay unknown effects or extension data, byte for byte.
/// </summary>
public class CreationGuideV13SchemaTests
{
    private const string Head = "{\"contentId\":\"6a7e5500-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e5500-0000-4000-8000-000000000101\",\"schemaVersion\":";

    private const string Subject = "6a7e5500-0000-4000-8000-0000000000a1";
    private const string Mace = "6a7e5500-0000-4000-8000-0000000000a2";
    private const string Hammer = "6a7e5500-0000-4000-8000-0000000000a3";
    private const string Pack = "6a7e5500-0000-4000-8000-0000000000a4";

    private static string Doc(int version, string kind, string effects) =>
        Head + version + ",\"kind\":\"" + kind + "\",\"name\":\"Fixture guide\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[" + effects + "]}";

    private const string Tail = ",\"automation\":\"automatic\",\"timing\":\"always\"";

    private static readonly string GuideForJson = "{\"type\":\"guideFor\",\"id\":\"for\",\"content\":{\"contentId\":\"" + Subject + "\"}" + Tail + "}";

    private const string PrimaryJson = "{\"type\":\"primaryAbilities\",\"id\":\"primary\",\"abilities\":[\"wis\"]" + Tail + "}";

    private static readonly string KitJson =
        "{\"type\":\"startingEquipment\",\"id\":\"kit\",\"picks\":[{\"id\":\"weapon\",\"label\":\"Weapon\",\"options\":["
        + "{\"id\":\"a\",\"lines\":[{\"item\":{\"contentId\":\"" + Mace + "\"},\"quantity\":1}]},"
        + "{\"id\":\"b\",\"lines\":[{\"item\":{\"contentId\":\"" + Hammer + "\"}}],\"requiresProficiency\":\"weapon.warhammer\"},"
        + "{\"id\":\"c\",\"lines\":[{\"anyOf\":\"weapon.simple\"},{\"coins\":{\"gp\":50}},{\"pack\":{\"contentId\":\"" + Pack + "\"}}]}]}]" + Tail + "}";

    private static string GuideDoc(int version) => Doc(version, "creationGuide", GuideForJson + "," + PrimaryJson + "," + KitJson);

    private static readonly string PackJson =
        "{\"type\":\"item\",\"id\":\"item\",\"contains\":[{\"item\":{\"contentId\":\"" + Mace + "\"},\"quantity\":10}]" + Tail + "}";

    [Fact]
    public void V13_guide_round_trips_byte_for_byte_with_typed_effects()
    {
        var input = GuideDoc(13);
        var revision = RevisionHashProperties.Read(input);

        Assert.Equal(ContentKind.CreationGuide, revision.Kind);
        Assert.Equal(3, revision.Effects.Count);
        var guideFor = Assert.IsType<GuideForEffect>(revision.Effects[0]);
        Assert.Equal(Guid.Parse(Subject), guideFor.Content.ContentId);
        Assert.Equal(new[] { "wis" }, Assert.IsType<PrimaryAbilitiesEffect>(revision.Effects[1]).Abilities);
        var kit = Assert.IsType<StartingEquipmentEffect>(revision.Effects[2]);
        var pick = Assert.Single(kit.Picks);
        Assert.Equal(3, pick.Options.Count);
        Assert.Equal("weapon.warhammer", pick.Options[1].RequiresProficiency);
        Assert.Equal(1, pick.Options[0].Lines[0].Quantity);
        Assert.Equal("weapon.simple", pick.Options[2].Lines[0].AnyOf);
        Assert.Equal(50, pick.Options[2].Lines[1].Coins!.Gp);
        Assert.Equal(Guid.Parse(Pack), pick.Options[2].Lines[2].Pack!.ContentId);

        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(RevisionHashProperties.Hash(input), RevisionHashProperties.Hash(stored));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(11)]
    [InlineData(10)]
    public void Below_v13_guide_effects_stay_unknown_with_raw_unchanged(int version)
    {
        var input = Doc(version, "feature", GuideForJson + "," + PrimaryJson + "," + KitJson);
        var revision = RevisionHashProperties.Read(input);

        Assert.All(revision.Effects, e => Assert.IsType<UnknownEffect>(e));
        Assert.Equal(input, RevisionHashProperties.Write(revision));
    }

    [Fact]
    public void V13_item_reads_contains()
    {
        var revision = RevisionHashProperties.Read(Doc(13, "item", PackJson));

        var item = Assert.IsType<ItemEffect>(revision.Effects.Single());
        var line = Assert.Single(item.Contains!);
        Assert.Equal(Guid.Parse(Mace), line.Item.ContentId);
        Assert.Equal(10, line.Quantity);
        Assert.False(item.Extensions?.ContainsKey("contains") ?? false);
        Assert.Equal(Doc(13, "item", PackJson), RevisionHashProperties.Write(revision));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(11)]
    [InlineData(10)]
    public void Below_v13_contains_stays_extension_data_and_writes_back_unchanged(int version)
    {
        // Extension data is written after the typed members, so the gated key sits last, as in the notWearing tests.
        var input = Doc(version, "item", "{\"type\":\"item\",\"id\":\"item\"" + Tail + ",\"contains\":[{\"item\":{\"contentId\":\"" + Mace + "\"},\"quantity\":10}]}");
        var revision = RevisionHashProperties.Read(input);

        var item = Assert.IsType<ItemEffect>(revision.Effects.Single());
        Assert.Null(item.Contains);
        Assert.True(item.Extensions!.ContainsKey("contains"));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(RevisionHashProperties.Hash(input), RevisionHashProperties.Hash(stored));
    }

    [Fact]
    public void Lowering_to_v12_drops_the_guide_effects_and_contains_with_effect_unsupported()
    {
        var item = "{\"type\":\"item\",\"id\":\"item\",\"contains\":[{\"item\":{\"contentId\":\"" + Mace + "\"},\"quantity\":10}]" + Tail + "}";
        var revision = RevisionHashProperties.Read(Doc(13, "feature", GuideForJson + "," + PrimaryJson + "," + KitJson + "," + item));

        var result = DownConvert.Below(revision, 12);

        Assert.Equal(4, result.Changes.Count(c => c.Code == BelowCodes.EffectUnsupported && c.Action == BelowAction.Dropped));
        var kept = Assert.IsType<ItemEffect>(Assert.Single(result.Revision.Effects));
        Assert.Null(kept.Contains);
        Assert.False(kept.Extensions?.ContainsKey("contains") ?? false);
    }

    [Fact]
    public void V13_unknown_line_member_round_trips()
    {
        var kit = "{\"type\":\"startingEquipment\",\"id\":\"kit\",\"picks\":[{\"id\":\"p\",\"options\":[{\"id\":\"a\",\"lines\":[{\"item\":{\"contentId\":\"" + Mace + "\"},\"quantity\":2,\"futureMember\":{\"a\":1}}]}]}]" + Tail + "}";
        var input = Doc(13, "creationGuide", kit);
        var revision = RevisionHashProperties.Read(input);

        var effect = Assert.IsType<StartingEquipmentEffect>(revision.Effects.Single());
        Assert.True(effect.Picks[0].Options[0].Lines[0].Extensions!.ContainsKey("futureMember"));
        Assert.Equal(input, RevisionHashProperties.Write(revision));
    }

    [Fact]
    public void Versions_are_pinned()
    {
        Assert.Equal(13, ContentRevision.CurrentSchemaVersion);
        Assert.Equal(13, ContentRevision.CreationGuideSchemaVersion);
        Assert.Equal(ContentRevision.WeaponBonusSchemaVersion + 1, ContentRevision.CreationGuideSchemaVersion);
    }
}
