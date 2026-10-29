using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>The content v12 lowering step: what a v11 reader cannot hold is dropped with a code, or stops the export.</summary>
public class DownConvertV12Tests
{
    private static ContentRevision Rev(string effects, int schema = 12) =>
        Fixtures.RevisionWithEffect(effects, schema) with { Name = "Compat Steady Hands" };

    private static ContentRevision Item(string effects, int schema = 12) =>
        Rev(effects, schema) with { Kind = ContentKind.Item };

    private static string Written(ContentBelow below) => JsonSerializer.Serialize(below.Revision, RulesJson.Compact);

    private const string Weapon = """{"type":"weapon","id":"blade","category":"simple","attack":"melee","damage":"1d6","damageType":"slashing","weaponKey":"blade"{0}}""";

    private static string WeaponWith(string extra) => Weapon.Replace("{0}", extra, StringComparison.Ordinal);

    [Fact]
    public void Weapon_bonus_drops_to_v11_with_its_code()
    {
        var result = DownConvert.Below(Item(WeaponWith(""","bonus":2""")), 11);

        Assert.False(result.Blocked);
        Assert.Equal(11, result.Revision.SchemaVersion);
        Assert.DoesNotContain("\"bonus\"", Written(result), StringComparison.OrdinalIgnoreCase);
        var change = Assert.Single(result.Changes);
        Assert.Equal(BelowCodes.WeaponBonus, change.Code);
        Assert.Equal(BelowAction.Dropped, change.Action);
        Assert.Equal("effects[0].bonus", change.Path);
    }

    [Theory]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","payment":{"kind":"resource"}}""", "effects[0].payment", "below.payment")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","castsSpell":"6f0dc000-0000-4000-8000-0000000000b1"}""", "effects[0].castsSpell", "below.casts-spell")]
    [InlineData("""{"type":"attack","id":"a","attackId":"a","label":"Strike","kind":"melee","abilities":["str"],"payment":{"kind":"resource"}}""", "effects[0].payment", "below.payment")]
    [InlineData("""{"type":"toggle","id":"t","toggleId":"t","label":"T","payment":{"kind":"resource"}}""", "effects[0].payment", "below.payment")]
    public void Payment_and_casts_spell_block(string effect, string path, string code)
    {
        var result = DownConvert.Below(Rev(effect), 11);

        Assert.True(result.Blocked);
        Assert.Contains(result.Changes, c => c.Action == BelowAction.Blocks && c.Code == code && c.Path == path);
    }

    [Fact]
    public void Tags_and_replaceable_drop()
    {
        var spell = """{"type":"spell","id":"s","level":1,"tags":["alpha"]}""";
        var choice = """{"type":"choice","id":"c","choiceId":"pick","count":1,"options":[],"replaceable":{"when":"levelUp"}}""";
        var result = DownConvert.Below(Rev($"{spell},{choice}"), 11);

        Assert.False(result.Blocked);
        Assert.Collection(result.Changes.OrderBy(c => c.Path, StringComparer.Ordinal),
            c => { Assert.Equal("effects[0].tags", c.Path); Assert.Equal(BelowCodes.V12Key, c.Code); Assert.Equal(BelowAction.Dropped, c.Action); },
            c => { Assert.Equal("effects[1].replaceable", c.Path); Assert.Equal(BelowCodes.V12Key, c.Code); Assert.Equal(BelowAction.Dropped, c.Action); });
        var written = Written(result);
        Assert.DoesNotContain("tags", written, StringComparison.Ordinal);
        Assert.DoesNotContain("replaceable", written, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_mod_effect_drops_as_unsupported()
    {
        var mod = """{"type":"itemMod","id":"m","appliesTo":"weapon","bonus":1}""";
        var result = DownConvert.Below(Item($"{WeaponWith("")},{mod}"), 11);

        Assert.False(result.Blocked);
        Assert.Single(result.Revision.Effects);
        var change = Assert.Single(result.Changes);
        Assert.Equal(BelowCodes.EffectUnsupported, change.Code);
        Assert.Equal(BelowAction.Dropped, change.Action);
        Assert.Equal("effects[1]", change.Path);
    }

    [Fact]
    public void A_v12_revision_with_no_v12_feature_lowers_silently()
    {
        var result = DownConvert.Below(Item(WeaponWith("")), 11);

        Assert.Empty(result.Changes);
        Assert.False(result.Blocked);
        Assert.Equal(11, result.Revision.SchemaVersion);
        Assert.Single(result.Revision.Effects);
    }

    [Fact]
    public void Lowering_to_10_runs_both_steps()
    {
        var toggle = """{"type":"toggle","id":"t","toggleId":"t","label":"T","group":"forms"}""";
        var result = DownConvert.Below(Item($"{WeaponWith(""","bonus":1""")},{toggle}"), 10);

        Assert.Contains(result.Changes, c => c.Code == BelowCodes.WeaponBonus);
        Assert.Contains(result.Changes, c => c.Code == BelowCodes.ToggleGroup);
    }
}
