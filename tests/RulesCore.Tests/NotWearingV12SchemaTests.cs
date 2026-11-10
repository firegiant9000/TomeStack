using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G4): the notWearing member of a modifier, typed only from v12. Below v12 it stays extension data and
/// the revision keeps its bytes and hash.
/// </summary>
public class NotWearingV12SchemaTests
{
    private const string Head = "{\"contentId\":\"6a7e5500-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e5500-0000-4000-8000-000000000101\",\"schemaVersion\":";

    private static string Doc(int version, string effect) =>
        Head + version + ",\"kind\":\"feature\",\"name\":\"Fixture Gauntlet\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[" + effect + "]}";

    private const string GatedBonus =
        "{\"type\":\"modifier\",\"id\":\"m\",\"operation\":\"bonus\",\"target\":\"ability.dex.score\",\"value\":\"1\",\"stacking\":\"stack\",\"automation\":\"automatic\",\"timing\":\"always\",\"notWearing\":[\"armor\",\"shield\"]}";

    [Fact]
    public void V12_modifier_reads_not_wearing()
    {
        var effect = Assert.IsType<ModifierEffect>(RevisionHashProperties.Read(Doc(12, GatedBonus)).Effects.Single());

        Assert.Equal(new[] { "armor", "shield" }, effect.NotWearing);
        Assert.Equal(1, int.Parse(effect.Value));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(10)]
    [InlineData(9)]
    public void V11_not_wearing_stays_extension_data_with_bytes_and_hash_unchanged(int version)
    {
        var input = Doc(version, GatedBonus);
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);

        var effect = Assert.IsType<ModifierEffect>(revision.Effects.Single());
        Assert.Null(effect.NotWearing);
        Assert.True(effect.Extensions!.ContainsKey("notWearing"));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }

    [Fact]
    public void V12_modifier_with_not_wearing_round_trips_byte_for_byte()
    {
        var input = Doc(12, "{\"type\":\"modifier\",\"id\":\"m\",\"operation\":\"bonus\",\"target\":\"ability.con.score\",\"value\":\"2\",\"stacking\":\"stack\",\"notWearing\":[\"heavyArmor\",\"shield\"],\"automation\":\"automatic\",\"timing\":\"always\",\"futureMember\":{\"a\":1}}");

        var revision = RevisionHashProperties.Read(input);

        var effect = Assert.IsType<ModifierEffect>(revision.Effects.Single());
        Assert.Equal(new[] { "heavyArmor", "shield" }, effect.NotWearing);
        Assert.True(effect.Extensions!.ContainsKey("futureMember"));
        Assert.False(effect.Extensions.ContainsKey("notWearing"));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(RevisionHashProperties.Hash(input), RevisionHashProperties.Hash(stored));
    }
}
