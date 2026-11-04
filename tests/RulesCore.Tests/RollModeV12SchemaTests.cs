using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G7): the rollMode effect's attack, initiative and attacksAgainst targets and its tags, ability,
/// unlessCondition and prevent members, typed only from v12. Below v12 the keys stay extension data and a v12 target stays untyped,
/// and the revision keeps its bytes and hash.
/// </summary>
public class RollModeV12SchemaTests
{
    private const string Head = "{\"contentId\":\"6a7e0700-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0700-0000-4000-8000-000000000101\",\"schemaVersion\":";

    private static string Doc(int version, string effect) =>
        Head + version + ",\"kind\":\"feature\",\"name\":\"Fixture Mode\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[" + effect + "]}";

    private static string Mode(string target, string extra = "") =>
        "{\"type\":\"rollMode\",\"id\":\"m\",\"target\":\"" + target + "\",\"mode\":\"advantage\",\"automation\":\"automatic\",\"timing\":\"always\"" + extra + "}";

    private const string AllKeys = ",\"tags\":[\"melee\"],\"ability\":\"str\",\"unlessCondition\":\"incapacitated\",\"prevent\":true";

    [Fact]
    public void V12_rollMode_reads_every_new_member()
    {
        var attack = Assert.IsType<RollModeEffect>(RevisionHashProperties.Read(
            Doc(12, Mode("attack", ",\"tags\":[\"melee\"],\"ability\":\"str\",\"unlessCondition\":\"incapacitated\""))).Effects.Single());
        Assert.Equal("attack", attack.Target);
        Assert.Equal(["melee"], attack.Tags);
        Assert.Equal(Ability.Str, attack.Ability);
        Assert.Equal("incapacitated", attack.UnlessCondition);
        Assert.Null(attack.Prevent);

        var against = Assert.IsType<RollModeEffect>(RevisionHashProperties.Read(
            Doc(12, Mode("attacksAgainst", ",\"prevent\":true"))).Effects.Single());
        Assert.Equal("attacksAgainst", against.Target);
        Assert.True(against.Prevent);
    }

    [Fact]
    public void V11_rollMode_with_v12_keys_is_typed_without_them()
    {
        var input = Doc(11, Mode("save.dex", AllKeys));
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);
        var mode = Assert.IsType<RollModeEffect>(revision.Effects.Single());

        Assert.Null(mode.Tags);
        Assert.Null(mode.Ability);
        Assert.Null(mode.UnlessCondition);
        Assert.Null(mode.Prevent);
        foreach (var key in RevisionHashProperties.RollModeV12Keys)
            Assert.True(mode.Extensions!.ContainsKey(key), key);
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }

    [Theory]
    [InlineData("attack")]
    [InlineData("initiative")]
    [InlineData("attacksAgainst")]
    public void V11_rollMode_with_a_v12_target_stays_unknown(string target)
    {
        var input = Doc(11, Mode(target));

        var revision = RevisionHashProperties.Read(input);

        Assert.IsType<UnknownEffect>(revision.Effects.Single());
        Assert.Equal(input, RevisionHashProperties.Write(revision));
    }

    [Fact]
    public void V12_rollMode_with_unknown_ability_stays_unknown()
    {
        Assert.IsType<UnknownEffect>(RevisionHashProperties.Read(Doc(12, Mode("attack", ",\"tags\":[\"melee\"],\"ability\":\"Str\""))).Effects.Single());
    }
}
