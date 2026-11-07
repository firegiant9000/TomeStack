using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G11): the conditionImmunity grant kind and its condition member, typed only from v12. Below v12 the
/// grant stays an unknown effect and the revision keeps its bytes and hash.
/// </summary>
public class ConditionImmunityV12SchemaTests
{
    private const string Head = "{\"contentId\":\"6a7e1100-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e1100-0000-4000-8000-000000000101\",\"schemaVersion\":";

    private const string OwnerId = "6a7e1100-0000-4000-8000-000000000002";

    private static string Doc(int version, string effect) =>
        Head + version + ",\"kind\":\"feature\",\"name\":\"Fixture Ward\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[" + effect + "]}";

    private static readonly string Immunity =
        "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"conditionImmunity\",\"condition\":\"frightened\",\"whileToggle\":{\"contentId\":\"" + OwnerId + "\",\"toggleId\":\"fury\"}}";

    [Fact]
    public void V12_condition_immunity_grant_reads_kind_condition_and_while_toggle()
    {
        var effect = Assert.IsType<GrantEffect>(RevisionHashProperties.Read(Doc(12, Immunity)).Effects.Single());

        Assert.Equal(GrantKind.ConditionImmunity, effect.Grant);
        Assert.Equal("frightened", effect.Condition);
        Assert.NotNull(effect.WhileToggle);
        Assert.Equal("fury", effect.WhileToggle!.ToggleId);
        Assert.Equal(Guid.Parse(OwnerId), effect.WhileToggle.ContentId);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(10)]
    [InlineData(9)]
    public void V11_condition_immunity_grant_stays_unknown_with_bytes_and_hash_unchanged(int version)
    {
        var input = Doc(version, Immunity);
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);

        var unknown = Assert.IsType<UnknownEffect>(revision.Effects.Single());
        Assert.Equal(GrantEffect.TypeName, unknown.DeclaredType);
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(10)]
    [InlineData(9)]
    public void V11_condition_key_on_a_proficiency_grant_stays_extension_data(int version)
    {
        var input = Doc(version, "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"proficiency\",\"target\":\"skill.stealth\",\"automation\":\"automatic\",\"timing\":\"always\",\"condition\":\"charmed\"}");
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);

        var effect = Assert.IsType<GrantEffect>(revision.Effects.Single());
        Assert.Null(effect.Condition);
        Assert.True(effect.Extensions!.ContainsKey("condition"));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }

    [Theory]
    [InlineData(11, "\"CONDITIONIMMUNITY\"")]
    [InlineData(11, "\" conditionImmunity\"")]
    [InlineData(11, "[\"conditionImmunity\"]")]
    [InlineData(11, "6")]
    [InlineData(10, "\"CONDITIONIMMUNITY\"")]
    [InlineData(10, "\" conditionImmunity\"")]
    [InlineData(10, "[\"conditionImmunity\"]")]
    [InlineData(10, "6")]
    public void Odd_spellings_of_the_kind_below_v12_stay_unknown_with_bytes_and_hash_unchanged(int version, string spelling)
    {
        var input = Doc(version, "{\"type\":\"grant\",\"id\":\"g\",\"grant\":" + spelling + ",\"condition\":\"frightened\"}");
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);

        Assert.IsType<UnknownEffect>(revision.Effects.Single());
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }

    [Fact]
    public void V12_condition_immunity_round_trips_byte_for_byte()
    {
        var input = Doc(12, "{\"type\":\"grant\",\"id\":\"g\",\"grant\":\"conditionImmunity\",\"level\":3,\"onlyAs\":\"startingClass\",\"condition\":\"charmed\",\"automation\":\"automatic\",\"timing\":\"always\",\"futureMember\":{\"a\":1}}");

        var revision = RevisionHashProperties.Read(input);

        var effect = Assert.IsType<GrantEffect>(revision.Effects.Single());
        Assert.Equal("charmed", effect.Condition);
        Assert.True(effect.Extensions!.ContainsKey("futureMember"));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(RevisionHashProperties.Hash(input), RevisionHashProperties.Hash(stored));
    }
}
