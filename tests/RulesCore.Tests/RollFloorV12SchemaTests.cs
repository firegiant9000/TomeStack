using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G9): the rollFloor effect kind, typed only from v12. Below v12 it stays an unknown effect and the revision
/// keeps its bytes and hash.
/// </summary>
public class RollFloorV12SchemaTests
{
    private const string Head = "{\"contentId\":\"6a7e0900-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0900-0000-4000-8000-000000000101\",\"schemaVersion\":";

    private static string Doc(int version, string effect) =>
        Head + version + ",\"kind\":\"feature\",\"name\":\"Fixture Floor\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[" + effect + "]}";

    [Fact]
    public void V12_rollFloor_reads_face_and_proficient()
    {
        var floor = Assert.IsType<RollFloorEffect>(RevisionHashProperties.Read(Doc(12, "{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"check\",\"face\":10,\"proficient\":true}")).Effects.Single());

        Assert.Equal(RollFloorEffect.CheckTarget, floor.Target);
        Assert.Equal(10, floor.Face);
        Assert.Null(floor.Total);
        Assert.True(floor.Proficient);
    }

    [Fact]
    public void V12_rollFloor_reads_total()
    {
        var floor = Assert.IsType<RollFloorEffect>(RevisionHashProperties.Read(Doc(12, "{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"ability.str.check\",\"total\":\"STR.SCORE\"}")).Effects.Single());

        Assert.Equal("ability.str.check", floor.Target);
        Assert.Equal("STR.SCORE", floor.Total);
        Assert.Null(floor.Face);
        Assert.Null(floor.Proficient);
    }

    [Fact]
    public void V11_rollFloor_stays_unknown_with_bytes_and_hash_unchanged()
    {
        var input = Doc(11, "{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"check\",\"face\":10,\"proficient\":true}");
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);

        var unknown = Assert.IsType<UnknownEffect>(revision.Effects.Single());
        Assert.Equal(RollFloorEffect.TypeName, unknown.DeclaredType);
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }

    [Fact]
    public void V12_rollFloor_round_trips_byte_for_byte()
    {
        var input = Doc(12, "{\"type\":\"rollFloor\",\"id\":\"f\",\"target\":\"skill.athletics\",\"face\":8,\"proficient\":true,\"automation\":\"automatic\",\"timing\":\"always\",\"futureMember\":{\"a\":1}}");

        var revision = RevisionHashProperties.Read(input);

        var floor = Assert.IsType<RollFloorEffect>(revision.Effects.Single());
        Assert.True(floor.Extensions!.ContainsKey("futureMember"));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(RevisionHashProperties.Hash(input), RevisionHashProperties.Hash(stored));
    }
}
