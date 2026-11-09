using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G5): the cap member of a modifier, typed only from v12. Below v12 it stays extension data and the
/// revision keeps its bytes and hash.
/// </summary>
public class AbilityCapV12SchemaTests
{
    private const string Head = "{\"contentId\":\"6a7e5500-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e5500-0000-4000-8000-000000000101\",\"schemaVersion\":";

    private static string Doc(int version, string effect) =>
        Head + version + ",\"kind\":\"feature\",\"name\":\"Fixture Gauntlet\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[" + effect + "]}";

    private const string CappedBonus =
        "{\"type\":\"modifier\",\"id\":\"m\",\"operation\":\"bonus\",\"target\":\"ability.str.score\",\"value\":\"4\",\"stacking\":\"stack\",\"automation\":\"automatic\",\"timing\":\"always\",\"cap\":25}";

    [Fact]
    public void V12_modifier_reads_cap()
    {
        var effect = Assert.IsType<ModifierEffect>(RevisionHashProperties.Read(Doc(12, CappedBonus)).Effects.Single());

        Assert.Equal(25, effect.Cap);
        Assert.Equal(4, int.Parse(effect.Value));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(10)]
    [InlineData(9)]
    public void V11_cap_stays_extension_data_with_bytes_and_hash_unchanged(int version)
    {
        var input = Doc(version, CappedBonus);
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);

        var effect = Assert.IsType<ModifierEffect>(revision.Effects.Single());
        Assert.Null(effect.Cap);
        Assert.True(effect.Extensions!.ContainsKey("cap"));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }

    [Fact]
    public void V12_modifier_with_cap_round_trips_byte_for_byte()
    {
        var input = Doc(12, "{\"type\":\"modifier\",\"id\":\"m\",\"operation\":\"bonus\",\"target\":\"ability.con.score\",\"value\":\"2\",\"stacking\":\"stack\",\"cap\":22,\"automation\":\"automatic\",\"timing\":\"always\",\"futureMember\":{\"a\":1}}");

        var revision = RevisionHashProperties.Read(input);

        var effect = Assert.IsType<ModifierEffect>(revision.Effects.Single());
        Assert.Equal(22, effect.Cap);
        Assert.True(effect.Extensions!.ContainsKey("futureMember"));
        Assert.False(effect.Extensions.ContainsKey("cap"));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(RevisionHashProperties.Hash(input), RevisionHashProperties.Hash(stored));
    }
}
