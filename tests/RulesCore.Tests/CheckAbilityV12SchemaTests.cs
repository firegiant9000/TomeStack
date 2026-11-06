using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G10): the checkAbility effect kind, typed only from v12. Below v12 it stays an unknown effect and the
/// revision keeps its bytes and hash.
/// </summary>
public class CheckAbilityV12SchemaTests
{
    private const string Head = "{\"contentId\":\"6a7e1000-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e1000-0000-4000-8000-000000000101\",\"schemaVersion\":";

    private static string Doc(int version, string effect) =>
        Head + version + ",\"kind\":\"feature\",\"name\":\"Fixture Swap\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[" + effect + "]}";

    [Fact]
    public void V12_checkAbility_reads_skill_target_and_ability()
    {
        var effect = Assert.IsType<CheckAbilityEffect>(RevisionHashProperties.Read(Doc(12, "{\"type\":\"checkAbility\",\"id\":\"c\",\"target\":\"skill.athletics\",\"ability\":\"dex\"}")).Effects.Single());

        Assert.Equal("skill.athletics", effect.Target);
        Assert.Equal("dex", effect.Ability);
        Assert.True(CheckAbilityEffect.TryParseAbility(effect.Ability, out var ability));
        Assert.Equal(Ability.Dex, ability);
    }

    [Fact]
    public void V12_checkAbility_reads_ability_check_target()
    {
        var effect = Assert.IsType<CheckAbilityEffect>(RevisionHashProperties.Read(Doc(12, "{\"type\":\"checkAbility\",\"id\":\"c\",\"target\":\"ability.int.check\",\"ability\":\"wis\"}")).Effects.Single());

        Assert.Equal("ability.int.check", effect.Target);
        Assert.Equal("wis", effect.Ability);
        Assert.False(CheckAbilityEffect.TryParseAbility("Wis", out _));
        Assert.False(CheckAbilityEffect.TryParseAbility("", out _));
    }

    [Fact]
    public void V11_checkAbility_stays_unknown_with_bytes_and_hash_unchanged()
    {
        var input = Doc(11, "{\"type\":\"checkAbility\",\"id\":\"c\",\"target\":\"skill.athletics\",\"ability\":\"dex\"}");
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);

        var unknown = Assert.IsType<UnknownEffect>(revision.Effects.Single());
        Assert.Equal(CheckAbilityEffect.TypeName, unknown.DeclaredType);
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }

    [Fact]
    public void V12_checkAbility_round_trips_byte_for_byte()
    {
        var input = Doc(12, "{\"type\":\"checkAbility\",\"id\":\"c\",\"target\":\"ability.str.check\",\"ability\":\"dex\",\"automation\":\"automatic\",\"timing\":\"always\",\"futureMember\":{\"a\":1}}");

        var revision = RevisionHashProperties.Read(input);

        var effect = Assert.IsType<CheckAbilityEffect>(revision.Effects.Single());
        Assert.True(effect.Extensions!.ContainsKey("futureMember"));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(RevisionHashProperties.Hash(input), RevisionHashProperties.Hash(stored));
    }
}
