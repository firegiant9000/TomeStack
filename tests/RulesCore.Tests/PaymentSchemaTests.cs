using System.Reflection;
using System.Text.Json.Nodes;
using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (MECH-X item 1): <c>payment</c> on a roll, a feature attack and a toggle, <c>castsSpell</c> on a roll and
/// <c>tags</c> on a spell, typed only from v12. Below v12 each key stays extension data and writes back exactly as the v11 build
/// wrote it (the literals below were captured from that build, ff4067d, before any change, and are never updated to make a test
/// pass).
/// </summary>
public class PaymentSchemaTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private const string SpellId = "6a7e0100-0000-4000-8000-0000000000a1";

    private const string Head = "{\"contentId\":\"6a7e0100-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0100-0000-4000-8000-000000000101\",\"schemaVersion\":";

    /// <summary>Quotes are written as apostrophes in the rows below, for readability.</summary>
    private static string J(string json) => json.Replace('\'', '"');

    private static string Doc(int version, string kind, string effects, string source = "6a7e0000-0000-4000-8000-000000000001") =>
        Head + version + ",\"kind\":\"" + kind + "\",\"name\":\"Fixture Rite\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\""
        + source + "\"},\"status\":\"published\",\"effects\":[" + effects + "]}";

    /// <summary>The M1 shared fixture source, so a validated document has no missing-source error.</summary>
    private const string KnownSource = "5f0d5001-0000-4000-8000-000000000001";

    private static ContentRevision Read(string json) => RevisionHashProperties.Read(json);

    private static ValidationReport Validate(ContentRevision revision, IEnumerable<ContentRevision>? batch = null) => ContentValidator.Validate(revision, Catalog, batch);

    private static ValidationReport ValidateJson(int version, string kind, string effects) => Validate(Read(Doc(version, kind, J(effects), KnownSource)));

    private const string RollPrefix = "{'type':'roll','id':'r','rollId':'r','label':'Rite','dice':'1d6',";
    private const string CastPrefix = "{'type':'roll','id':'r','rollId':'r','label':'Cast',";
    private const string AttackPrefix = "{'type':'attack','id':'a','attackId':'a','label':'Bolt','kind':'ranged','abilities':['int'],'damage':{'count':'1','die':'6'},'damageType':'fire',";
    private const string TogglePrefix = "{'type':'toggle','id':'t','toggleId':'t','label':'Stance',";
    private const string Pool = "{'type':'resource','id':'pool','resourceId':'pool','label':'Pool','maximum':'2'},";

    private static string Effect(string type, string mid, string last) => type switch
    {
        "roll" => "{\"type\":\"roll\",\"id\":\"r\",\"rollId\":\"r\",\"label\":\"Rite\"," + mid + "\"dice\":\"1d6\"" + last + "}",
        "attack" => "{\"type\":\"attack\",\"id\":\"a\",\"attackId\":\"a\",\"label\":\"Bolt\"," + mid + "\"kind\":\"ranged\",\"abilities\":[\"int\"],\"damage\":{\"count\":\"1\",\"die\":\"6\"},\"damageType\":\"fire\"" + last + "}",
        "toggle" => "{\"type\":\"toggle\",\"id\":\"t\",\"toggleId\":\"t\"," + mid + "\"label\":\"Stance\"" + last + "}",
        _ => "{\"type\":\"spell\",\"id\":\"s\"," + mid + "\"level\":1,\"lists\":[\"x\"]" + last + "}",
    };

    /// <summary>The effect as the v11 build wrote it: its typed properties, then the extension key (<paramref name="key"/>) last.</summary>
    private static string WrittenBelowV12(string type, string key, string value) => (type switch
    {
        "roll" => "{\"type\":\"roll\",\"id\":\"r\",\"rollId\":\"r\",\"label\":\"Rite\",\"dice\":\"1d6\",\"automation\":\"automatic\",\"timing\":\"always\"",
        "attack" => "{\"type\":\"attack\",\"id\":\"a\",\"attackId\":\"a\",\"label\":\"Bolt\",\"kind\":\"ranged\",\"abilities\":[\"int\"],\"damage\":{\"count\":\"1\",\"die\":\"6\"},\"damageType\":\"fire\",\"automation\":\"automatic\",\"timing\":\"always\"",
        "toggle" => "{\"type\":\"toggle\",\"id\":\"t\",\"toggleId\":\"t\",\"label\":\"Stance\",\"automation\":\"automatic\",\"timing\":\"always\"",
        _ => "{\"type\":\"spell\",\"id\":\"s\",\"level\":1,\"concentration\":false,\"ritual\":false,\"lists\":[\"x\"],\"attack\":\"none\",\"automation\":\"automatic\",\"timing\":\"always\"",
    }) + ",\"" + key + "\":" + value + "}";

    [Fact]
    public void A_v12_roll_reads_its_payment_and_cast()
    {
        var roll = Assert.IsType<RollEffect>(Read(Doc(12, "feature", Effect("roll", "\"payment\":{\"kind\":\"hitPoints\",\"mayDropToZero\":true},\"castsSpell\":\"" + SpellId + "\",", ""))).Effects[0]);

        Assert.Equal(new CostPayment(PaymentKind.HitPoints, true), roll.Payment);
        Assert.Equal(Guid.Parse(SpellId), roll.CastsSpell);
        Assert.Null(roll.Extensions);
    }

    [Fact]
    public void A_v12_roll_writes_its_payment_and_cast_as_typed_properties_and_round_trips()
    {
        var stored = RevisionHashProperties.Write(Read(Doc(12, "feature", Effect("roll", "\"castsSpell\":\"" + SpellId + "\",", ", \"payment\":{\"kind\":\"hitPoints\"}"))));

        Assert.Contains("\"dice\":\"1d6\",\"payment\":{\"kind\":\"hitPoints\"},\"castsSpell\":\"" + SpellId + "\",\"automation\":\"automatic\",\"timing\":\"always\"}]}", stored, StringComparison.Ordinal);
        Assert.Equal(stored, RevisionHashProperties.Write(Read(stored)));
    }

    [Theory]
    [InlineData("attack", "{\"kind\":\"slot\",\"minSlotLevel\":2}")]
    [InlineData("toggle", "{\"kind\":\"slot\",\"minSlotLevel\":2}")]
    [InlineData("attack", "{\"kind\":\"resource\"}")]
    public void Attack_and_toggle_read_a_slot_payment_from_v12(string type, string payment)
    {
        var effect = Read(Doc(12, "feature", Effect(type, "\"payment\":" + payment + ",", ""))).Effects[0];

        var read = effect switch { AttackEffect a => a.Payment, ToggleEffect t => t.Payment, _ => null };
        Assert.NotNull(read);
        Assert.Equal(payment.Contains("slot", StringComparison.Ordinal) ? PaymentKind.Slot : PaymentKind.Resource, read.Kind);
        Assert.Equal(payment.Contains("slot", StringComparison.Ordinal) ? (int?)2 : null, read.MinSlotLevel);
        Assert.Null(effect.Extensions);
    }

    [Fact]
    public void A_v12_spell_reads_its_tags()
    {
        var spell = Assert.IsType<SpellEffect>(Read(Doc(12, "spell", Effect("spell", "\"tags\":[\"time\",\"ritualLike\"],", ""))).Effects[0]);

        Assert.Equal(["time", "ritualLike"], spell.Tags);
        Assert.Null(spell.Extensions);
    }

    // Captured on the v11 build (ff4067d) before any change: the revision hash. The effect is written with the key last, whichever
    // position it had, with the value as it was (any spelling, any value, null too).
    [Theory]
    [InlineData("roll", 8, "payment", "{\"kind\":\"hitPoints\",\"mayDropToZero\":true}", "71ccb225a7660d307413ab54bcd988b3d86071ea8e690d235babca25ac70ceb3")]
    [InlineData("roll", 8, "payment", "{\"kind\":\"hp\"}", "d28d03fa04bb13b1ce0ded6ea23d1d4d2167ac0a1d0585b95ba54645e0603e14")]
    [InlineData("roll", 8, "Payment", "{\"kind\":\"slot\"}", "46ecb5f2f72498b2714ca5312cd68955e4c0bcc7f36b29bcb3329035e314f302")]
    [InlineData("roll", 8, "castsSpell", "\"6a7e0100-0000-4000-8000-0000000000a1\"", "c5cafee5443d67268ca702c652a415089f660f29aa42187394cc12165c737f85")]
    [InlineData("roll", 8, "castsSpell", "\"x\"", "7c06052d6ea64042ae1447e2efa87630958fe1254f2f894784e913e1f5d6f174")]
    [InlineData("roll", 8, "castsSpell", "null", "c0f1c4457b36c7b5bc66ef28f29bc87c954a686c770f65aff7d85dc6edb0da20")]
    [InlineData("roll", 11, "payment", "{\"kind\":\"hitPoints\",\"mayDropToZero\":true}", "a9aa85fc6187bc9c3ee8700a4bfc5b7a17559fa58bfe2ae75d37b8fe3be64e6c")]
    [InlineData("roll", 11, "payment", "{\"kind\":\"hp\"}", "13bafaf206a2469b98f9ce3ab19d363daf310f7aec385c4a1ba589d7a94983e1")]
    [InlineData("roll", 11, "Payment", "{\"kind\":\"slot\"}", "75f9e0bece7d770cf3bd6827043b93c52f75db00244827f2228d7ca20e6d37f8")]
    [InlineData("roll", 11, "castsSpell", "\"6a7e0100-0000-4000-8000-0000000000a1\"", "52b4c15157ab7de4ae4aac6979fc2b14c9b86b02cf292709c42bb815ea1a8ac1")]
    [InlineData("roll", 11, "castsSpell", "\"x\"", "dc0e5590d31e78d508b4e1fd5437c68dad4e8f9e5e4f448944b69b518868442d")]
    [InlineData("roll", 11, "castsSpell", "null", "f687e3c1775a6b057813f3c166f3e400aedb0a5c1fc1e7a1a43ed869b1121a31")]
    [InlineData("attack", 11, "payment", "{\"kind\":\"hitPoints\",\"mayDropToZero\":true}", "957fa9e9867861677f9430a88d851455dc41f44ba6fc7770c3f3399623ab0784")]
    [InlineData("attack", 11, "payment", "{\"kind\":\"hp\"}", "2ed3cf719261768fb602200124b190189dbfbb427a755dec339510fc4a038d6e")]
    [InlineData("attack", 11, "Payment", "{\"kind\":\"slot\",\"minSlotLevel\":2}", "313d1493c651eb68b051b6d746baf94e1231fc081cbdbd15f8e13ec80ac94902")]
    [InlineData("toggle", 6, "payment", "{\"kind\":\"hitPoints\",\"mayDropToZero\":true}", "95714d126a2edd7325e7bcaa850a7e20d0e6f460c2798c1843a5894e1313afb3")]
    [InlineData("toggle", 6, "payment", "{\"kind\":\"hp\"}", "9cd3f5f73265829abb684cb38135b90ab3c0287ac021a9055d376f02001323d3")]
    [InlineData("toggle", 6, "Payment", "{\"kind\":\"slot\"}", "6df356a13fa84adeb4e56415de85d44a82ebc10f99f5c3d4f653e7245f5a2eba")]
    [InlineData("toggle", 11, "payment", "{\"kind\":\"hitPoints\",\"mayDropToZero\":true}", "0f5c2374a0afdb9fdb9518f8469f276e7f2a47708c83d2c67592e9184bbe6386")]
    [InlineData("toggle", 11, "payment", "{\"kind\":\"hp\"}", "2b34107193330165afbd413c81dea952da7fe9372d0ee18fe9cc528639e08492")]
    [InlineData("toggle", 11, "Payment", "{\"kind\":\"slot\"}", "85fc70c4bcf7d12302d63b3c9cbef7b0a7d6ec4d4fe88a3d96870b70ac038791")]
    [InlineData("spell", 5, "tags", "[\"time\",\"ritualLike\"]", "4f34677b4f60763fc9b0e64844961f2a78d243593a69f94c8cee208d4c8ddc08")]
    [InlineData("spell", 5, "tags", "\"a\"", "8d8756bf8ce8c03d52208218fb6b51ede21d1ed7d52a4a212e094bd490292f36")]
    [InlineData("spell", 5, "tags", "null", "c0b4eda1cec7a1153ed580c3cda2bf6ce1a17ecdbac816985c50367ee00b03e4")]
    [InlineData("spell", 5, "Tags", "[\"a\"]", "abfe4e20aa903f04b23673d34ac592d1c6fe50020ee8014f8fbc6cb5a151c89a")]
    [InlineData("spell", 11, "tags", "[\"time\",\"ritualLike\"]", "ea466f74ee2ec5d078ac27e93630b5a72deee4bab0767270fa9af16428411fc4")]
    [InlineData("spell", 11, "tags", "\"a\"", "9d4c44c2429832140c26eaa86230a9abb0ec9d45c2d7e190029fcf03655016b3")]
    [InlineData("spell", 11, "tags", "null", "731d1fb17dc51dcc086606dd8af392f9a4a8ea903ada3b74fa50c542accd3cf3")]
    [InlineData("spell", 11, "Tags", "[\"a\"]", "d21dee90b49c1c09f45bafcf81e14d47e0bce121304aa34c9b721a73e86cf897")]
    public void Below_v12_the_keys_are_written_as_the_old_build_wrote_them(string type, int version, string key, string value, string hash)
    {
        var kind = type == "spell" ? "spell" : "feature";
        foreach (var inTheMiddle in new[] { true, false })
        {
            var fragment = $"\"{key}\": {value}";
            var revision = Read(Doc(version, kind, inTheMiddle ? Effect(type, fragment + ", ", "") : Effect(type, "", ", " + fragment)));

            var stored = RevisionHashProperties.Write(revision);

            Assert.Equal(Doc(version, kind, WrittenBelowV12(type, key, value)), stored);
            Assert.Equal(hash, RevisionHashProperties.Hash(stored));
            var effect = revision.Effects[0];
            Assert.Null(effect switch { RollEffect r => (object?)r.Payment ?? r.CastsSpell, AttackEffect a => a.Payment, ToggleEffect t => t.Payment, SpellEffect s => s.Tags, _ => null });
            Assert.True(effect.Extensions!.ContainsKey(key));
        }
    }

    [Theory]
    // Every row is a v12 revision; "" means no error at all, otherwise the code that must be among the errors.
    [InlineData(RollPrefix + "'cost':'2','payment':{'kind':'hitPoints'}}", "")]
    [InlineData(RollPrefix + "'cost':'2','perUnit':{'dice':'1d6','maxUnits':3},'payment':{'kind':'hitPoints','mayDropToZero':true}}", "")]
    [InlineData(AttackPrefix + "'cost':'3','payment':{'kind':'hitPoints'}}", "")]
    [InlineData(TogglePrefix + "'cost':'1','payment':{'kind':'hitPoints','mayDropToZero':true}}", "")]
    [InlineData(CastPrefix + "'castsSpell':'" + SpellId + "'}", "")]
    [InlineData(CastPrefix + "'cost':'1','castsSpell':'" + SpellId + "','payment':{'kind':'hitPoints'}}", "")]
    [InlineData(RollPrefix + "'payment':{'kind':'slot','minSlotLevel':9}}", "")]
    [InlineData(AttackPrefix + "'payment':{'kind':'slot'}}", "")]
    [InlineData(TogglePrefix + "'payment':{'kind':'slot','minSlotLevel':1}}", "")]
    [InlineData(Pool + RollPrefix + "'resourceId':'pool','cost':'1','payment':{'kind':'resource'}}", "")]
    [InlineData(Pool + RollPrefix + "'resourceId':'pool','cost':'1','payment':{'kind':'hitPoints'}}", "validate.payment-combination")]
    [InlineData(Pool + RollPrefix + "'resourceId':'pool','cost':'1','variableCost':true,'payment':{'kind':'hitPoints'}}", "validate.payment-combination")]
    [InlineData(Pool + AttackPrefix + "'resourceId':'pool','cost':'1','payment':{'kind':'hitPoints'}}", "validate.payment-combination")]
    [InlineData(Pool + TogglePrefix + "'resourceId':'pool','cost':'1','payment':{'kind':'slot'}}", "validate.payment-combination")]
    [InlineData(RollPrefix + "'payment':{'kind':'hitPoints'}}", "validate.payment-cost")]
    [InlineData(AttackPrefix + "'payment':{'kind':'hitPoints'}}", "validate.payment-cost")]
    [InlineData(TogglePrefix + "'payment':{'kind':'hitPoints'}}", "validate.payment-cost")]
    [InlineData(RollPrefix + "'cost':'1','payment':{'kind':'slot'}}", "validate.payment-combination")]
    [InlineData(AttackPrefix + "'cost':'1','payment':{'kind':'slot'}}", "validate.payment-combination")]
    [InlineData(TogglePrefix + "'cost':'1','payment':{'kind':'slot'}}", "validate.payment-combination")]
    [InlineData(RollPrefix + "'perUnit':{'dice':'1d6'},'payment':{'kind':'slot'}}", "validate.payment-combination")]
    [InlineData(RollPrefix + "'castsSpell':'" + SpellId + "','payment':{'kind':'slot'}}", "validate.payment-combination")]
    [InlineData(RollPrefix + "'payment':{'kind':'slot','mayDropToZero':true}}", "validate.payment-field")]
    [InlineData(RollPrefix + "'cost':'1','payment':{'kind':'hitPoints','minSlotLevel':2}}", "validate.payment-field")]
    [InlineData(Pool + RollPrefix + "'resourceId':'pool','cost':'1','payment':{'kind':'resource','mayDropToZero':true}}", "validate.payment-field")]
    [InlineData(RollPrefix + "'payment':{'kind':'slot','minSlotLevel':10}}", "validate.payment-slot-level")]
    [InlineData(AttackPrefix + "'payment':{'kind':'slot','minSlotLevel':0}}", "validate.payment-slot-level")]
    [InlineData(TogglePrefix + "'payment':{'kind':'slot','minSlotLevel':-1}}", "validate.payment-slot-level")]
    public void Payment_rules(string effects, string code)
    {
        var report = ValidateJson(12, "feature", effects);

        if (code == "")
            Assert.Empty(report.Errors);
        else
            Assert.Contains(report.Errors, e => e.Code == code);
    }

    [Fact]
    public void A_paying_attack_may_not_share_its_id_with_a_roll()
    {
        const string RollNamedDup = "{'type':'roll','id':'r','rollId':'dup','label':'Rite','dice':'1d6'},";
        const string AttackDup = "{'type':'attack','id':'dup','attackId':'a','label':'Bolt','kind':'ranged','abilities':['int'],'damage':{'count':'1','die':'6'},'damageType':'fire',";

        Assert.Contains(ValidateJson(12, "feature", RollNamedDup + AttackDup + "'cost':'1','payment':{'kind':'hitPoints'}}").Errors, e => e.Code == "validate.payment-id-clash");
        Assert.Contains(ValidateJson(12, "feature", RollNamedDup + AttackDup + "'payment':{'kind':'slot'}}").Errors, e => e.Code == "validate.payment-id-clash");
        Assert.DoesNotContain(ValidateJson(12, "feature", RollNamedDup + AttackDup.Replace("'dup'", "'other'", StringComparison.Ordinal) + "'cost':'1','payment':{'kind':'hitPoints'}}").Errors, e => e.Code == "validate.payment-id-clash");
        Assert.DoesNotContain(ValidateJson(12, "feature", RollNamedDup + AttackDup.TrimEnd(',') + "}").Errors, e => e.Code == "validate.payment-id-clash");
    }

    [Theory]
    [InlineData("all", true)]
    [InlineData("time", false)]
    public void Spell_tag_all_is_refused(string tag, bool refused)
    {
        var report = ValidateJson(12, "spell", Effect("spell", "'tags':['" + tag + "'],", "").Replace("\"", "'", StringComparison.Ordinal));

        Assert.Equal(refused, report.Errors.Any(e => e.Code == "validate.tag-invalid"));
    }

    [Fact]
    public void Spell_tags_are_keys()
    {
        var report = ValidateJson(12, "spell", Effect("spell", "'tags':['Bad Tag'],", "").Replace("\"", "'", StringComparison.Ordinal));

        Assert.Contains(report.Errors, e => e.Code == "validate.tag-invalid");
    }

    private static ContentRevision Built(Guid content, ContentKind kind, params Effect[] effects) => new()
    {
        ContentId = content, RevisionId = Guid.NewGuid(), SchemaVersion = 12, Kind = kind, Name = "Fixture Content",
        RulesFamilies = [RulesFamilies.Srd521], Provenance = new(Guid.Parse(KnownSource)), Status = RevisionStatus.Draft, Effects = effects,
    };

    private static ContentRevision Caster(Guid spell) =>
        Built(Guid.Parse("6a7e0100-0000-4000-8000-0000000000b1"), ContentKind.Feature,
            new RollEffect { Id = "r", RollId = "r", Label = "Cast", CastsSpell = spell });

    [Fact]
    public void Casts_spell_must_name_a_spell()
    {
        var spell = Built(Guid.Parse(SpellId), ContentKind.Spell, new SpellEffect { Id = "sp", Level = 1, Lists = ["fixture"] });
        var feature = Built(Guid.Parse("6a7e0100-0000-4000-8000-0000000000a2"), ContentKind.Feature);

        Assert.DoesNotContain(Validate(Caster(spell.ContentId), [spell]).Errors, e => e.Code == "validate.casts-spell");
        var refused = Assert.Single(Validate(Caster(feature.ContentId), [feature]).Errors, e => e.Code == "validate.casts-spell");
        Assert.Equal($"Roll 'r' casts content {feature.ContentId}, which is not a spell.", refused.Message);
    }

    [Fact]
    public void Casts_spell_looks_at_every_revision_of_the_content_not_only_one()
    {
        var older = Built(Guid.Parse(SpellId), ContentKind.Spell, new SpellEffect { Id = "sp", Level = 1, Lists = ["fixture"] });
        var newer = Built(Guid.Parse(SpellId), ContentKind.Spell, new SpellEffect { Id = "sp", Level = 2, Lists = ["fixture"] });

        Assert.DoesNotContain(Validate(Caster(older.ContentId), [newer, older]).Errors, e => e.Code == "validate.casts-spell");
    }

    [Fact]
    public void Casts_spell_of_content_that_is_not_installed_is_only_a_warning()
    {
        var report = Validate(Caster(Guid.Parse("6a7e0100-0000-4000-8000-0000000000ff")));

        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.casts-spell");
        Assert.Contains(report.Warnings, w => w.Code == "validate.casts-spell-missing");
    }

    public static TheoryData<string, string> NewKeys => new()
    {
        { "roll payment", "a cost payment" },
        { "roll casts", "casting without a slot" },
        { "attack payment", "a cost payment" },
        { "toggle payment", "a cost payment" },
        { "spell tags", "spell tags" },
    };

    private static ContentRevision WithNewKey(string name, int version, bool withKey)
    {
        var (kind, effect) = name switch
        {
            "roll payment" => (ContentKind.Feature, (Effect)new RollEffect { Id = "r", RollId = "r", Label = "Rite", Dice = "1d6", Cost = "1", Payment = withKey ? new(PaymentKind.HitPoints) : null }),
            "roll casts" => (ContentKind.Feature, new RollEffect { Id = "r", RollId = "r", Label = "Rite", Dice = "1d6", CastsSpell = withKey ? Guid.Parse(SpellId) : null }),
            "attack payment" => (ContentKind.Feature, new AttackEffect { Id = "a", AttackId = "a", Label = "Bolt", Kind = WeaponAttack.Ranged, Abilities = [Ability.Int], Damage = new("1", "6"), DamageType = "fire", Cost = "1", Payment = withKey ? new(PaymentKind.HitPoints) : null }),
            "toggle payment" => (ContentKind.Feature, new ToggleEffect { Id = "t", ToggleId = "t", Label = "Stance", Cost = "1", Payment = withKey ? new(PaymentKind.HitPoints) : null }),
            _ => (ContentKind.Spell, new SpellEffect { Id = "s", Level = 1, Lists = ["x"], Tags = withKey ? ["time"] : null }),
        };
        return Built(Guid.Parse("6a7e0100-0000-4000-8000-0000000000c1"), kind, effect) with { SchemaVersion = version };
    }

    [Theory]
    [MemberData(nameof(NewKeys))]
    public void New_keys_require_v12(string name, string phrase)
    {
        var at11 = Validate(WithNewKey(name, 11, true));
        var message = Assert.Single(at11.Errors, e => e.Code == "validate.requires-v12").Message;
        Assert.Contains(phrase, message, StringComparison.Ordinal);

        var at12 = Validate(WithNewKey(name, 12, true));
        Assert.DoesNotContain(at12.Errors, e => e.Code == "validate.requires-v12");
        Assert.Equal(12, at12.RequiredSchemaVersion);

        // Without the key the revision keeps the version it needed before.
        var without = Validate(WithNewKey(name, 12, false));
        Assert.DoesNotContain(without.Errors, e => e.Code == "validate.requires-v12");
        Assert.True(without.RequiredSchemaVersion < 12);
    }

    [Fact]
    public void The_requires_v12_message_names_everything_it_found()
    {
        var both = Built(Guid.Parse("6a7e0100-0000-4000-8000-0000000000c2"), ContentKind.Feature,
            new RollEffect { Id = "r", RollId = "r", Label = "Rite", Dice = "1d6", Cost = "1", Payment = new(PaymentKind.HitPoints), CastsSpell = Guid.Parse(SpellId) },
            new WeaponEffect { Id = "w", Category = WeaponCategory.Simple, Attack = WeaponAttack.Melee, Damage = "1d6", DamageType = "slashing", WeaponKey = "w", Bonus = 1 }) with { SchemaVersion = 11 };

        var message = Assert.Single(Validate(both).Errors, e => e.Code == "validate.requires-v12").Message;

        Assert.Contains("a weapon bonus", message, StringComparison.Ordinal);
        Assert.Contains("a cost payment", message, StringComparison.Ordinal);
        Assert.Contains("casting without a slot", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RollPrefix + "'payment':{'kind':'hp'}}", "feature")]
    [InlineData(RollPrefix + "'castsSpell':'x'}", "feature")]
    [InlineData(RollPrefix + "'Payment':'slot'}", "feature")]
    [InlineData(AttackPrefix + "'payment':{'kind':'hp'}}", "feature")]
    [InlineData(TogglePrefix + "'payment':{'kind':'hp'}}", "feature")]
    [InlineData(TogglePrefix + "'payment':{'minSlotLevel':2}}", "feature")]
    [InlineData("{'type':'spell','id':'s','level':1,'tags':'a'}", "spell")]
    [InlineData("{'type':'spell','id':'s','level':1,'Tags':[1]}", "spell")]
    public void Unreadable_new_keys_block_from_v12(string effects, string kind)
    {
        var v12 = ValidateJson(12, kind, effects);
        Assert.Contains(v12.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(12, v12.RequiredSchemaVersion);

        // Below v12 the key is stripped first: nothing about it blocks.
        Assert.DoesNotContain(ValidateJson(11, kind, effects).Errors, e => e.Code == "validate.effect-malformed");
    }

    [Fact]
    public void Payment_schema_version_is_current()
    {
        Assert.Equal(12, ContentRevision.PaymentSchemaVersion);
        Assert.Equal(ContentRevision.PaymentSchemaVersion, ContentRevision.CurrentSchemaVersion);
        Assert.Equal(ContentRevision.WeaponBonusSchemaVersion, ContentRevision.PaymentSchemaVersion);
    }

    /// <summary>The gotcha of 2026-10-20: the production key lists are internal and this project cannot see them, so reflection compares them with the lists written out here.</summary>
    [Theory]
    [InlineData(typeof(RollEffect), new[] { "payment", "castsSpell" })]
    [InlineData(typeof(AttackEffect), new[] { "payment" })]
    [InlineData(typeof(ToggleEffect), new[] { "payment" })]
    [InlineData(typeof(SpellEffect), new[] { "tags" })]
    public void V12_key_lists_match(Type type, string[] expected)
    {
        var field = type.GetField("V12Keys", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(expected, (string[])field.GetValue(null)!);
    }

    /// <summary>
    /// "Fixture Bloodprice Rite" and "Fixture Hourglass Ward" (tests/RulesFixtures/golden/payment-revisions.json): the golden harness
    /// holds one revision per version, so these two v12 revisions have their own file and hashes, pinned in GoldenRevisions.cs.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Payment_goldens_keep_their_hashes(int index)
    {
        var golden = GoldenRevisions.PaymentJson(index);
        var stored = RevisionHashProperties.Write(Read(golden));

        Assert.True(GoldenRevisions.PaymentHashes[index] == RevisionHashProperties.Hash(stored), $"Payment golden {index} now hashes to {RevisionHashProperties.Hash(stored)}.");
        Assert.Equal(stored, RevisionHashProperties.Write(Read(stored)));
        Assert.Null(RevisionHashProperties.UnknownPartsChanged(12, JsonNode.Parse(golden)!.AsObject(), stored));
        // Every known effect is typed (the unknown lore effect stays as written).
        Assert.All(Read(stored).Effects, e => Assert.True(e is not UnknownEffect || e.Type == "xRiteLore"));
    }
}
