using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>The content v13 lowering step (ADR-024): a creation guide is left out whole, a pack loses only its contents.</summary>
public class DownConvertV13Tests
{
    private const string Mace = "6a7e5500-0000-4000-8000-0000000000b2";
    private const string Subject = "6a7e5500-0000-4000-8000-0000000000b1";
    private const string Tail = ",\"automation\":\"automatic\",\"timing\":\"always\"";

    private const string GuideFor = "{\"type\":\"guideFor\",\"id\":\"for\",\"content\":{\"contentId\":\"" + Subject + "\"}" + Tail + "}";
    private const string Primary = "{\"type\":\"primaryAbilities\",\"id\":\"primary\",\"abilities\":[\"wis\"]" + Tail + "}";
    private const string Kit = "{\"type\":\"startingEquipment\",\"id\":\"kit\",\"picks\":[{\"id\":\"p\",\"options\":[{\"id\":\"a\",\"lines\":[{\"item\":{\"contentId\":\"" + Mace + "\"},\"quantity\":1}]}]}]" + Tail + "}";
    private const string Pack = "{\"type\":\"item\",\"id\":\"item\",\"contains\":[{\"item\":{\"contentId\":\"" + Mace + "\"},\"quantity\":10}]" + Tail + "}";
    private const string PackAlone = "{\"type\":\"item\",\"id\":\"item\"" + Tail + "}";

    private static ContentRevision Rev(string effects, ContentKind kind, int schema = 13) =>
        Fixtures.RevisionWithEffect(effects, schema) with { Name = "Fixture Holder", Kind = kind };

    private static string Written(ContentRevision revision) => JsonSerializer.Serialize(revision, RulesJson.Compact);

    [Fact]
    public void A_guide_is_dropped_whole_with_one_listed_change()
    {
        var guide = Rev(GuideFor + "," + Primary + "," + Kit, ContentKind.CreationGuide);

        var result = DownConvert.Below(guide, 12);

        Assert.True(result.Omit);
        Assert.False(result.Blocked);
        var change = Assert.Single(result.Changes);
        Assert.Equal("", change.Path);
        Assert.Equal(BelowAction.Dropped, change.Action);
        Assert.Equal(BelowCodes.CreationGuide, change.Code);
        Assert.Same(guide, result.Revision);
    }

    [Fact]
    public void A_guide_is_dropped_whole_at_every_lower_target()
    {
        var result = DownConvert.Below(Rev(GuideFor, ContentKind.CreationGuide), 10);

        Assert.True(result.Omit);
        Assert.Equal(BelowCodes.CreationGuide, Assert.Single(result.Changes).Code);
    }

    [Fact]
    public void A_pack_loses_only_its_contents()
    {
        var pack = Rev(Pack, ContentKind.Item);

        var result = DownConvert.Below(pack, 12);

        Assert.False(result.Omit);
        Assert.False(result.Blocked);
        var change = Assert.Single(result.Changes);
        Assert.Equal(BelowAction.Dropped, change.Action);
        Assert.Equal(BelowCodes.ItemContains, change.Code);
        Assert.Equal("effects[0].contains", change.Path);
        Assert.Equal(12, result.Revision.SchemaVersion);
        Assert.Equal(Written(Rev(PackAlone, ContentKind.Item, 12)), Written(result.Revision));
    }

    [Fact]
    public void A_v12_revision_at_target_12_is_untouched()
    {
        var revision = Rev(PackAlone, ContentKind.Item, 12);

        var result = DownConvert.Below(revision, 12);

        Assert.Same(revision, result.Revision);
        Assert.Empty(result.Changes);
        Assert.False(result.Omit);
    }

    [Fact]
    public void A_v13_revision_with_no_v13_content_lowers_silently_to_v12()
    {
        var result = DownConvert.Below(Rev(PackAlone, ContentKind.Item), 12);

        Assert.Empty(result.Changes);
        Assert.Equal(12, result.Revision.SchemaVersion);
    }

    [Fact]
    public void A_guide_effect_on_another_kind_is_dropped_on_its_own()
    {
        var result = DownConvert.Below(Rev(Primary + "," + PackAlone, ContentKind.Item), 12);

        Assert.False(result.Omit);
        var change = Assert.Single(result.Changes);
        Assert.Equal(BelowAction.Dropped, change.Action);
        Assert.Equal(BelowCodes.EffectUnsupported, change.Code);
        Assert.Equal("effects[0]", change.Path);
        Assert.IsType<ItemEffect>(Assert.Single(result.Revision.Effects));
    }

    [Fact]
    public void The_lowered_pack_validates_at_the_target()
    {
        var result = DownConvert.Below(Rev(Pack, ContentKind.Item), 12);

        Assert.DoesNotContain(result.Changes, c => c.Code == BelowCodes.ValidationError);
    }

    [Fact]
    public void Lowering_a_guide_to_v13_changes_nothing()
    {
        var guide = Rev(GuideFor, ContentKind.CreationGuide);

        var result = DownConvert.Below(guide, 13);

        Assert.Same(guide, result.Revision);
        Assert.Empty(result.Changes);
        Assert.False(result.Omit);
    }
}
