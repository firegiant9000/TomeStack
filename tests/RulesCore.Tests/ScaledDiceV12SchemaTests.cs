using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G1): the attackRider's scaledDice, critical and oncePerTurn members and the roll's scaledDice, typed only
/// from v12. Below v12 the keys stay extension data in document order, and the revision keeps its bytes and hash.
/// </summary>
public class ScaledDiceV12SchemaTests
{
    private const string Head = "{\"contentId\":\"6a7e0900-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0900-0000-4000-8000-000000000101\",\"schemaVersion\":";

    private static string Doc(int version, string effect) =>
        Head + version + ",\"kind\":\"feature\",\"name\":\"Fixture Dice\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[" + effect + "]}";

    // The writer emits the type's own members, then automation and timing, then extension data: "stored by an older build" puts the v12 keys last.
    private const string Base = ",\"automation\":\"automatic\",\"timing\":\"always\"";

    private static string Rider(string own, string v12 = "") =>
        "{\"type\":\"attackRider\",\"id\":\"r\",\"tags\":[\"melee\"]" + own + Base + v12 + "}";

    private static string Roll(string own, string v12 = "") =>
        "{\"type\":\"roll\",\"id\":\"x\",\"rollId\":\"x\",\"label\":\"Fixture Burst\"" + own + Base + v12 + "}";

    private static readonly string[] RiderKeys = ["scaledDice", "critical", "oncePerTurn"];

    [Fact]
    public void V12_rider_reads_scaledDice_and_flags()
    {
        var first = Assert.IsType<AttackRiderEffect>(RevisionHashProperties.Read(Doc(12,
            Rider("", ",\"scaledDice\":{\"count\":\"ceil(CLASS_LEVEL/2)\",\"die\":\"6\"},\"oncePerTurn\":true"))).Effects.Single());
        Assert.Equal(new ScaledDice("ceil(CLASS_LEVEL/2)", "6"), first.ScaledDice);
        Assert.True(first.OncePerTurn);
        Assert.Null(first.Critical);

        var second = Assert.IsType<AttackRiderEffect>(RevisionHashProperties.Read(Doc(12,
            Rider("", ",\"scaledDice\":{\"count\":\"1\"},\"critical\":true"))).Effects.Single());
        Assert.Equal("1", second.ScaledDice!.Count);
        Assert.Null(second.ScaledDice.Die);
        Assert.True(second.Critical);
        Assert.Null(second.OncePerTurn);
    }

    [Fact]
    public void V12_roll_reads_scaledDice()
    {
        var roll = Assert.IsType<RollEffect>(RevisionHashProperties.Read(Doc(12,
            Roll("", ",\"scaledDice\":{\"count\":\"CLASS_LEVEL\",\"die\":\"8\"}"))).Effects.Single());
        Assert.Equal(new ScaledDice("CLASS_LEVEL", "8"), roll.ScaledDice);
        Assert.Null(roll.Dice);
    }

    [Fact]
    public void V11_rider_with_v12_keys_is_typed_without_them()
    {
        var input = Doc(11, Rider(",\"dice\":\"1d6\"", ",\"scaledDice\":{\"count\":\"2\",\"die\":\"6\"},\"critical\":true,\"oncePerTurn\":false"));
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);
        var rider = Assert.IsType<AttackRiderEffect>(revision.Effects.Single());

        Assert.Equal("1d6", rider.Dice);
        Assert.Null(rider.ScaledDice);
        Assert.Null(rider.Critical);
        Assert.Null(rider.OncePerTurn);
        Assert.Equal(RiderKeys, rider.Extensions!.Keys.ToArray());
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }

    [Fact]
    public void V11_roll_with_scaledDice_is_typed_without_it()
    {
        var input = Doc(11, Roll(",\"dice\":\"1d6\"", ",\"scaledDice\":{\"count\":\"2\",\"die\":\"6\"}"));
        var hashBefore = RevisionHashProperties.Hash(input);

        var revision = RevisionHashProperties.Read(input);
        var roll = Assert.IsType<RollEffect>(revision.Effects.Single());

        Assert.Equal("1d6", roll.Dice);
        Assert.Null(roll.ScaledDice);
        Assert.True(roll.Extensions!.ContainsKey("scaledDice"));
        var stored = RevisionHashProperties.Write(revision);
        Assert.Equal(input, stored);
        Assert.Equal(hashBefore, RevisionHashProperties.Hash(stored));
    }
}
