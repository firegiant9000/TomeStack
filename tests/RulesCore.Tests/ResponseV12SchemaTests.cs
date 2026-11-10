using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G6): the response effect's trigger, share, dice, automatic, unlessCondition and supersedes members, typed
/// only from v12. Below v12 the six keys stay extension data and the revision keeps its bytes and hash.
/// </summary>
public class ResponseV12SchemaTests
{
    private const string Head = "{\"contentId\":\"6a7e0200-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0200-0000-4000-8000-000000000101\",\"schemaVersion\":";

    private const string Prefix = "{\"type\":\"response\",\"id\":\"r\",\"responseId\":\"ward\",\"label\":\"Ward\",\"phase\":\"damageReduction\",\"amount\":\"3\",\"automation\":\"automatic\",\"timing\":\"always\"";

    private const string AllKeys =
        ",\"trigger\":{\"from\":\"save\",\"ability\":\"dex\",\"result\":\"success\",\"damageTypes\":[\"fire\"]},\"share\":\"all\",\"dice\":\"1d6\","
        + "\"automatic\":true,\"unlessCondition\":\"incapacitated\",\"supersedes\":{\"responseId\":\"x\"}";

    private static string Doc(int version, string effect) =>
        Head + version + ",\"kind\":\"feature\",\"name\":\"Fixture Ward\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[" + effect + "]}";

    [Fact]
    public void V12_response_reads_every_new_member()
    {
        var response = Assert.IsType<ResponseEffect>(RevisionHashProperties.Read(Doc(12, Prefix + AllKeys + "}")).Effects.Single());

        Assert.Equal(ResponseSource.Save, response.Trigger!.From);
        Assert.Equal("dex", response.Trigger.Ability);
        Assert.Equal(SaveResult.Success, response.Trigger.Result);
        Assert.Equal(["fire"], response.Trigger.DamageTypes);
        Assert.Equal(ResponseShare.All, response.Share);
        Assert.Equal("1d6", response.Dice);
        Assert.True(response.Automatic);
        Assert.Equal("incapacitated", response.UnlessCondition);
        Assert.Equal("x", response.Supersedes!.ResponseId);
        Assert.Null(response.Supersedes.Content);
    }

    [Fact]
    public void V11_response_with_v12_keys_is_typed_without_them()
    {
        var input = Doc(11, Prefix + AllKeys + "}");
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);
        var response = Assert.IsType<ResponseEffect>(revision.Effects.Single());

        Assert.Null(response.Trigger);
        Assert.Null(response.Share);
        Assert.Null(response.Dice);
        Assert.Null(response.Automatic);
        Assert.Null(response.UnlessCondition);
        Assert.Null(response.Supersedes);
        foreach (var key in RevisionHashProperties.ResponseV12Keys)
            Assert.True(response.Extensions!.ContainsKey(key), key);
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }

    [Theory]
    [InlineData(",\"share\":\"Half \"")]
    [InlineData(",\"trigger\":{\"from\":\"swim\"}")]
    [InlineData(",\"trigger\":{\"from\":\"save\",\"result\":\"won\"}")]
    public void V12_response_with_unknown_member_name_stays_unknown(string keys)
    {
        Assert.IsType<UnknownEffect>(RevisionHashProperties.Read(Doc(12, Prefix + keys + "}")).Effects.Single());
    }

    [Fact]
    public void Supersedes_content_is_referenced()
    {
        var other = Guid.Parse("6a7e0200-0000-4000-8000-0000000000b1");
        var json = Prefix + ",\"supersedes\":{\"content\":\"" + other + "\",\"responseId\":\"x\"}}";

        var response = Assert.IsType<ResponseEffect>(RevisionHashProperties.Read(Doc(12, json)).Effects.Single());

        Assert.Contains(other, response.ReferencedContent());
    }
}
