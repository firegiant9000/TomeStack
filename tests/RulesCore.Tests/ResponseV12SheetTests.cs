namespace TomeStack.RulesCore.Tests;

/// <summary>Content v12 (G6), Task 3: the six optional response members reach the calculated sheet; a held condition makes a response
/// unavailable; supersession leaves the replaced responses off.</summary>
public class ResponseV12SheetTests
{
    private static ContentRevision Holder(int n, string effects, int version = 12)
    {
        var revision = Fixtures.RevisionWithEffect(effects, version);
        return revision with
        {
            ContentId = Guid.Parse($"6f0dc000-0000-4000-8000-0000000003{n:X2}"),
            RevisionId = Guid.Parse($"6f0de000-0000-4000-8000-0000000003{n:X2}"),
            Provenance = revision.Provenance with { SourceId = Fixtures.ResponsesPack().Sources[0].Id },
        };
    }

    private static IReadOnlyList<ResponseValue> Responses(ContentRevision[] extra, string[]? conditions = null)
    {
        var (_, sheet) = DamageResponseTests.Stand(hp: 68, extra: extra, more: p => conditions is null ? p : p with { Conditions = conditions });
        return sheet.Responses!;
    }

    private const string Everything = """{"type":"response","id":"w","responseId":"ward","label":"Ward","phase":"damageReduction","amount":"2","dice":"1d6","automatic":true,"unlessCondition":"prone","trigger":{"from":"save","ability":"dex","result":"success","damageTypes":["Fire"]}}""";
    private const string Halving = """{"type":"response","id":"h","responseId":"halve","label":"Halve","phase":"damageReduction","share":"half"}""";

    [Fact]
    public void New_members_reach_the_sheet()
    {
        var all = Responses([Holder(1, Everything), Holder(2, Halving)]);

        var ward = all.Single(r => r.ResponseId == "ward");
        Assert.Equal(new ResponseTrigger(ResponseSource.Save, "dex", SaveResult.Success, ["fire"]), ward.Trigger, new TriggerComparer());
        Assert.Equal((2, "1d6", true, "prone", null), (ward.Amount, ward.Dice, ward.Automatic, ward.UnlessCondition, ward.Share));
        Assert.True(ward.Available);

        var halve = all.Single(r => r.ResponseId == "halve");
        Assert.Equal((ResponseShare.Half, null), (halve.Share, halve.Amount));
        Assert.True(halve.Available);
        Assert.Null(halve.UnavailableReason);
    }

    [Fact]
    public void Held_condition_makes_the_response_unavailable()
    {
        var extra = new[] { Holder(1, Everything) };

        var held = Responses(extra, ["prone"]).Single(r => r.ResponseId == "ward");
        Assert.Equal((false, "Not while prone"), (held.Available, held.UnavailableReason));

        var free = Responses(extra, ["stunned"]).Single(r => r.ResponseId == "ward");
        Assert.True(free.Available);

        // the existing reasons come first
        var gated = Holder(3, """{"type":"response","id":"g","responseId":"gated","label":"Gated","phase":"damageReduction","amount":"1","resourceId":"nothing","cost":"1","unlessCondition":"prone"}""");
        var both = Responses([gated], ["prone"]).Single(r => r.ResponseId == "gated");
        Assert.StartsWith("Needs 1", both.UnavailableReason);
    }

    [Fact]
    public void Superseded_response_is_left_off()
    {
        var low = Holder(1, """{"type":"response","id":"a","responseId":"low","label":"Low","phase":"damageReduction","amount":"1"}""");
        var mid = Holder(2, """{"type":"response","id":"b","responseId":"mid","label":"Mid","phase":"damageReduction","amount":"2","supersedes":{"responseId":"low"}}""");
        var other = Holder(3, """{"type":"response","id":"c","responseId":"low","label":"Other low","phase":"damageReduction","amount":"1"}""");

        // `mid` names no content, so it means its own content: it replaces nothing in `low`'s content
        var ids = Responses([low, mid, other]).Where(r => r.ContentId != Fixtures.Bulwark.ContentId).Select(r => (r.ContentId, r.ResponseId)).ToList();
        Assert.Contains((low.ContentId, "low"), ids);
        Assert.Contains((mid.ContentId, "mid"), ids);
        Assert.Contains((other.ContentId, "low"), ids);

        // in one content, a superseder naming no content replaces the response beside it
        var pair = Holder(5, """{"type":"response","id":"a","responseId":"plain","label":"Plain","phase":"damageReduction","amount":"1"},{"type":"response","id":"b","responseId":"better","label":"Better","phase":"damageReduction","amount":"2","supersedes":{"responseId":"plain"}}""");
        var kept = Responses([pair]).Where(r => r.ContentId == pair.ContentId).Select(r => r.ResponseId).ToList();
        Assert.Equal(["better"], kept);
    }

    [Fact]
    public void A_superseded_response_stays_dropped_when_its_superseder_is_superseded()
    {
        var c1 = Holder(1, """{"type":"response","id":"a","responseId":"one","label":"One","phase":"damageReduction","amount":"1"}""");
        var c2 = Holder(2, $$$"""{"type":"response","id":"b","responseId":"two","label":"Two","phase":"damageReduction","amount":"2","supersedes":{"content":"{{{c1.ContentId}}}","responseId":"one"}}""");
        var c3 = Holder(3, $$$"""{"type":"response","id":"c","responseId":"three","label":"Three","phase":"damageReduction","amount":"3","supersedes":{"content":"{{{c2.ContentId}}}","responseId":"two"}}""");

        var ids = Responses([c1, c2, c3]).Select(r => r.ResponseId).ToList();

        Assert.DoesNotContain("one", ids);
        Assert.DoesNotContain("two", ids);
        Assert.Contains("three", ids);
    }

    [Fact]
    public void Superseding_across_contents_uses_the_named_content()
    {
        var target = Holder(1, """{"type":"response","id":"a","responseId":"base","label":"Base","phase":"damageReduction","amount":"1"}""");
        var decoy = Holder(2, """{"type":"response","id":"a","responseId":"base","label":"Base too","phase":"damageReduction","amount":"1"}""");
        var over = Holder(3, $$$"""{"type":"response","id":"o","responseId":"over","label":"Over","phase":"damageReduction","amount":"2","supersedes":{"content":"{{{target.ContentId}}}","responseId":"base"}}""");

        var all = Responses([target, decoy, over]);

        Assert.DoesNotContain(all, r => r.ContentId == target.ContentId);
        Assert.Contains(all, r => r.ContentId == decoy.ContentId && r.ResponseId == "base");
        Assert.Contains(all, r => r.ResponseId == "over");
    }

    [Fact]
    public void V11_response_value_is_unchanged()
    {
        var (_, sheet) = DamageResponseTests.Stand(hp: 68);

        var brace = sheet.Responses!.Single(r => r.ResponseId == "brace");

        Assert.Equal(
            new ResponseValue(
                brace.ContentId, brace.ContentName, "brace", brace.Label, ResponsePhase.DamageReduction, 5, Fixtures.Bulwark.ContentId, "grit", 1,
                true, null, brace.Text, null, false, null),
            brace);
        Assert.Equal((null, null, null, false, null), (brace.Trigger, brace.Share, brace.Dice, brace.Automatic, brace.UnlessCondition));
    }

    /// <summary>Compares triggers by value, including the damage type list (a record compares lists by reference).</summary>
    private sealed class TriggerComparer : IEqualityComparer<ResponseTrigger?>
    {
        public bool Equals(ResponseTrigger? x, ResponseTrigger? y) =>
            x is not null && y is not null && (x.From, x.Ability, x.Result) == (y.From, y.Ability, y.Result)
            && (x.DamageTypes ?? []).SequenceEqual(y.DamageTypes ?? []);

        public int GetHashCode(ResponseTrigger? obj) => 0;
    }
}
