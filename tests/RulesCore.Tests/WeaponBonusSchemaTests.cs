using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (MECH-X item 0): <c>weapon.bonus</c>, 0 to 10, typed only from v12. Below v12 the key stays extension
/// data and writes back exactly as the v11 build wrote it (the literals below were captured from that build before any
/// change, and are never updated to make a test pass).
/// </summary>
public class WeaponBonusSchemaTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    private const string Head = "{\"contentId\":\"6a7e0100-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0100-0000-4000-8000-000000000101\",\"schemaVersion\":";

    private const string Middle = ",\"kind\":\"item\",\"name\":\"Fixture Blade\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"6a7e0000-0000-4000-8000-000000000001\"},\"status\":\"published\",\"effects\":[";

    /// <summary>An item whose single weapon has these extra fragments before <c>damage</c> (<paramref name="mid"/>) and after <c>weaponKey</c> (<paramref name="last"/>).</summary>
    private static string Item(int version, string mid = "", string last = "", string category = "simple") =>
        Head + version + Middle
        + "{\"type\":\"weapon\",\"id\":\"blade\",\"category\":\"" + category + "\",\"attack\":\"melee\"," + mid
        + "\"damage\":\"1d6\",\"damageType\":\"slashing\",\"weaponKey\":\"blade\"" + last + "}]}";

    private static ContentRevision Read(string json) => RevisionHashProperties.Read(json);

    private static ValidationReport Validate(ContentRevision revision) => ContentValidator.Validate(revision, Catalog);

    private static ContentRevision Built(int version, int? bonus) => new()
    {
        ContentId = Guid.Parse("6a7e0100-0000-4000-8000-000000000001"), RevisionId = Guid.Parse("6a7e0100-0000-4000-8000-000000000101"),
        SchemaVersion = version, Kind = ContentKind.Item, Name = "Fixture Blade", RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Guid.Parse("6a7e0000-0000-4000-8000-000000000001")), Status = RevisionStatus.Draft,
        Effects = [new WeaponEffect { Id = "blade", Category = WeaponCategory.Simple, Attack = WeaponAttack.Melee, Damage = "1d6", DamageType = "slashing", WeaponKey = "blade", Bonus = bonus }],
    };

    [Fact]
    public void A_v12_weapon_reads_its_bonus()
    {
        var weapon = Assert.IsType<WeaponEffect>(Read(Item(12, mid: "\"bonus\":2,")).Effects[0]);
        Assert.Equal(2, weapon.Bonus);
        Assert.Null(weapon.Extensions);
    }

    [Fact]
    public void A_v12_weapon_writes_its_bonus_as_a_typed_property()
    {
        var stored = RevisionHashProperties.Write(Read(Item(12, mid: "\"bonus\":0,")));
        Assert.Contains("\"weaponKey\":\"blade\",\"bonus\":0,\"automation\":\"automatic\",\"timing\":\"always\"}]}", stored, StringComparison.Ordinal);
        Assert.Equal(stored, RevisionHashProperties.Write(Read(stored)));
    }

    /// <summary>Captured on the v11 build (0c9d90f) before any change: the written effect and the revision hash.</summary>
    [Theory]
    [InlineData(5, "bonus", "2", true, "d3cf6726f09c7e58e50ed320356e6884cd108adc4a6aa17442538510e531e2c2")]
    [InlineData(5, "bonus", "2", false, "d3cf6726f09c7e58e50ed320356e6884cd108adc4a6aa17442538510e531e2c2")]
    [InlineData(5, "bonus", "\"2\"", true, "9202d7b57db15449f849d0c15a7acb80cb8510959d33384cbb504fd1f22bbdd0")]
    [InlineData(5, "bonus", "\"2\"", false, "9202d7b57db15449f849d0c15a7acb80cb8510959d33384cbb504fd1f22bbdd0")]
    [InlineData(5, "bonus", "1.5", true, "797dd3b033fe1014b44eaba0eb84ddad8b622c2efff796853e22ee161366fbcd")]
    [InlineData(5, "bonus", "null", true, "c5792fc204fa9efc98bf19750d20b199dea6d88fc5a15872d829e227cd1b73d4")]
    [InlineData(5, "Bonus", "2", true, "0c0ea3c71794c93d9435bcc0f6a3480eacf02351bc894a1ab0886adc0d6ceff8")]
    [InlineData(11, "bonus", "2", true, "63dd2ac25d3feb7bec1948943bafa25b2591daa75840a34846e975c899de229b")]
    [InlineData(11, "bonus", "2", false, "63dd2ac25d3feb7bec1948943bafa25b2591daa75840a34846e975c899de229b")]
    [InlineData(11, "bonus", "\"2\"", true, "b04d0cb6ba6ac9ca50af12c917da579d9aaf82078654a228e35c2e7d8987628a")]
    [InlineData(11, "bonus", "\"2\"", false, "b04d0cb6ba6ac9ca50af12c917da579d9aaf82078654a228e35c2e7d8987628a")]
    [InlineData(11, "bonus", "1.5", true, "2f9dcea67fc886d74968ad2ee047a918edfbbd7b8d1f057e3531262ebe849b26")]
    [InlineData(11, "bonus", "null", true, "46371c90ad068916040cf893940c21d522889e56304b382be8648c85e10c03f2")]
    [InlineData(11, "Bonus", "2", true, "ee01e7ec70e870c1d2dc20dcb74c52d783c472ac36823d3642526ab1e8604fc7")]
    [InlineData(11, "Bonus", "2", false, "ee01e7ec70e870c1d2dc20dcb74c52d783c472ac36823d3642526ab1e8604fc7")]
    [InlineData(11, "bonus", "null", false, "46371c90ad068916040cf893940c21d522889e56304b382be8648c85e10c03f2")]
    public void Below_v12_the_bonus_key_is_written_as_the_old_build_wrote_it(int version, string key, string value, bool inTheMiddle, string hash)
    {
        var fragment = $"\"{key}\": {value}";
        var revision = Read(inTheMiddle ? Item(version, mid: fragment + ", ") : Item(version, last: ", " + fragment));

        var stored = RevisionHashProperties.Write(revision);

        Assert.Equal(
            Head + version + Middle
            + "{\"type\":\"weapon\",\"id\":\"blade\",\"category\":\"simple\",\"attack\":\"melee\",\"damage\":\"1d6\",\"damageType\":\"slashing\",\"properties\":[],\"weaponKey\":\"blade\",\"automation\":\"automatic\",\"timing\":\"always\",\"" + key + "\":" + value + "}]}",
            stored);
        var weapon = Assert.IsType<WeaponEffect>(revision.Effects[0]);
        Assert.Null(weapon.Bonus);
        Assert.True(weapon.Extensions!.ContainsKey(key));
        Assert.Equal(hash, RevisionHashProperties.Hash(stored));
    }

    /// <summary>Captured on the v11 build (0c9d90f): the old build does not throw on two spellings of one key; it keeps both, in order.</summary>
    [Fact]
    public void Below_v12_two_spellings_of_the_bonus_key_are_both_kept_as_the_old_build_kept_them()
    {
        var revision = Read(Item(11, mid: "\"bonus\": 1, \"Bonus\": 2, "));

        var stored = RevisionHashProperties.Write(revision);

        Assert.Equal(
            Head + 11 + Middle
            + "{\"type\":\"weapon\",\"id\":\"blade\",\"category\":\"simple\",\"attack\":\"melee\",\"damage\":\"1d6\",\"damageType\":\"slashing\",\"properties\":[],\"weaponKey\":\"blade\",\"automation\":\"automatic\",\"timing\":\"always\",\"bonus\":1,\"Bonus\":2}]}",
            stored);
        Assert.Null(Assert.IsType<WeaponEffect>(revision.Effects[0]).Bonus);
        Assert.Equal("57a6dd5cd3c59271ee14149d1a7361d88d6e271040c551ad2637361d6d2b8cc2", RevisionHashProperties.Hash(stored));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void A_bonus_in_bounds_is_valid(int bonus) =>
        Assert.DoesNotContain(Validate(Built(12, bonus)).Errors, e => e.Code == "validate.weapon-bonus");

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void A_bonus_out_of_bounds_is_an_error(int bonus) =>
        Assert.Contains(Validate(Built(12, bonus)).Errors, e => e.Code == "validate.weapon-bonus");

    [Fact]
    public void A_bonus_requires_v12()
    {
        Assert.Contains(Validate(Built(11, 1)).Errors, e => e.Code == "validate.requires-v12");
        var ok = Validate(Built(12, 1));
        Assert.DoesNotContain(ok.Errors, e => e.Code is "validate.requires-v12" or "validate.weapon-bonus");
        Assert.Equal(12, ok.RequiredSchemaVersion);
        Assert.Equal(12, Validate(Built(12, 0)).RequiredSchemaVersion);
        Assert.Equal(5, Validate(Built(12, null)).RequiredSchemaVersion);
    }

    [Theory]
    [InlineData("\"bonus\":\"two\",", "simple")]
    [InlineData("\"bonus\":1.5,", "simple")]
    [InlineData("\"bonus\":true,", "simple")]
    [InlineData("\"Bonus\":\"x\",", "simple")]
    [InlineData("\"bonus\":2,", "heavy")]
    public void A_bonus_that_does_not_read_blocks_from_v12(string fragment, string category)
    {
        var v12 = Validate(Read(Item(12, mid: fragment, category: category)));
        Assert.Contains(v12.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(12, v12.RequiredSchemaVersion);

        // Below v12 the key is stripped first: nothing about it blocks.
        Assert.DoesNotContain(Validate(Read(Item(11, mid: fragment, category: category))).Errors, e => e.Code == "validate.effect-malformed");
    }

    /// <summary>The serializer's web defaults read a number written as a string (as for every whole-number field); it is stored as a number.</summary>
    [Theory]
    [InlineData("2", 2)]
    [InlineData("+2", 2)]
    [InlineData("007", 7)]
    [InlineData("-0", 0)]
    public void A_bonus_written_as_a_numeral_string_reads_as_the_number_like_every_other_whole_number_field(string numeral, int expected)
    {
        var revision = Read(Item(12, mid: $"\"bonus\":\"{numeral}\","));
        Assert.Equal(expected, Assert.IsType<WeaponEffect>(revision.Effects[0]).Bonus);
        Assert.Contains($"\"bonus\":{expected},", RevisionHashProperties.Write(revision), StringComparison.Ordinal);
    }

    [Fact]
    public void A_weapon_that_does_not_read_and_has_no_bonus_is_still_only_a_warning()
    {
        var report = Validate(Read(Item(12, category: "heavy")));
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Contains(report.Warnings, w => w.Code == "validate.effect-unsupported");
    }

    [Fact]
    public void Weapon_bonus_schema_version_is_current()
    {
        Assert.Equal(12, ContentRevision.WeaponBonusSchemaVersion);
        Assert.Equal(ContentRevision.WeaponBonusSchemaVersion, ContentRevision.CurrentSchemaVersion);
        Assert.Equal(ContentRevision.PlayMechanicsSchemaVersion + 1, ContentRevision.WeaponBonusSchemaVersion);
    }
}
