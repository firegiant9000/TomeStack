using System.Reflection;
using System.Text.Json.Nodes;
using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (MECH-X item 2): <c>choice.replaceable</c>, typed only from v12. Below v12 the key stays extension data and
/// writes back exactly as the build before it wrote it (the hashes below were captured from that build, 254c63b, before any
/// change, and are never updated to make a test pass).
/// </summary>
public class ReplaceableChoiceSchemaTests
{
    private static readonly InMemoryContentCatalog Catalog = Fixtures.M1Catalog();

    /// <summary>The M1 shared fixture source, so a validated document has no missing-source error.</summary>
    private const string KnownSource = "5f0d5001-0000-4000-8000-000000000001";

    private const string OptionsJson = "\"options\":[{\"contentId\":\"6a7e0200-0000-4000-8000-0000000000a1\",\"revisionId\":\"6a7e0200-0000-4000-8000-0000000000b1\"},{\"contentId\":\"6a7e0200-0000-4000-8000-0000000000a2\",\"revisionId\":\"6a7e0200-0000-4000-8000-0000000000b2\"}]";

    private static string J(string json) => json.Replace('\'', '"');

    private static string Doc(int version, string effects, string source = "6a7e0000-0000-4000-8000-000000000001") =>
        "{\"contentId\":\"6a7e0200-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0200-0000-4000-8000-000000000101\",\"schemaVersion\":" + version
        + ",\"kind\":\"feature\",\"name\":\"Fixture Repertoire\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"" + source
        + "\"},\"status\":\"published\",\"effects\":[" + effects + "]}";

    private static ContentRevision Read(string json) => RevisionHashProperties.Read(json);

    private static ValidationReport ValidateJson(int version, string effects) =>
        ContentValidator.Validate(Read(Doc(version, J(effects), KnownSource)), Catalog, null);

    /// <summary>A choice as written in a document: its fragment (<c>"key": value</c>) before the count, or after the options.</summary>
    private static string ChoiceJson(string fragment, bool inTheMiddle) =>
        "{\"type\":\"choice\",\"id\":\"c\",\"choiceId\":\"pick\"," + (inTheMiddle ? fragment + ", " : "") + "\"count\":2," + OptionsJson + (inTheMiddle ? "" : ", " + fragment) + "}";

    /// <summary>The choice as the build before this one wrote it: its typed properties, then the extension key last.</summary>
    private static string WrittenBefore(string key, string value) =>
        "{\"type\":\"choice\",\"id\":\"c\",\"choiceId\":\"pick\",\"count\":2," + OptionsJson + ",\"automation\":\"automatic\",\"timing\":\"always\",\"" + key + "\":" + value + "}";

    [Fact]
    public void A_v12_choice_reads_replaceable()
    {
        var both = Assert.IsType<ChoiceEffect>(Read(Doc(12, ChoiceJson("\"replaceable\": {\"when\":\"longRest\",\"max\":2}", true))).Effects[0]);
        Assert.Equal(new ChoiceReplaceable(ReplaceWindow.LongRest, 2), both.Replaceable);
        Assert.Equal(2, both.Replaceable!.Max);
        Assert.Null(both.Extensions);

        var windowOnly = Assert.IsType<ChoiceEffect>(Read(Doc(12, ChoiceJson("\"replaceable\": {\"when\":\"levelUp\"}", false))).Effects[0]);
        Assert.Equal(ReplaceWindow.LevelUp, windowOnly.Replaceable!.When);
        Assert.Null(windowOnly.Replaceable.Max);
        Assert.Null(windowOnly.Extensions);
    }

    [Fact]
    public void A_v12_choice_writes_replaceable_as_a_typed_property_and_round_trips()
    {
        var stored = RevisionHashProperties.Write(Read(Doc(12, ChoiceJson("\"replaceable\": {\"when\":\"levelUp\",\"max\":1}", true))));

        // The typed properties of the choice come before the base ones, as every typed effect writes them.
        Assert.Contains("}],\"replaceable\":{\"when\":\"levelUp\",\"max\":1},\"automation\":\"automatic\",\"timing\":\"always\"}]}", stored, StringComparison.Ordinal);
        Assert.Equal(stored, RevisionHashProperties.Write(Read(stored)));
    }

    // Captured on the build before this one (254c63b): the revision hash. The effect is written with the key last, whichever
    // position it had, with the value as it was (any spelling, any value, null too). Version 0 is read and never upcast.
    [Theory]
    [InlineData(11, "replaceable", "{\"when\":\"levelUp\",\"max\":1}", "3395d3e85cf4cb1f95e8878814ad50de1f39892d7cfceca54dcf705dcde50fa4")]
    [InlineData(11, "replaceable", "{\"when\":\"longRest\"}", "451d70153c47356faa7cb37bd5463a9e6fdd1561474573bcd5eb8ba82f279abd")]
    [InlineData(11, "Replaceable", "{\"when\":\"longRest\"}", "8f381f44093b9bb4975b80c1cf455b1898db202f45476497c3498efd3e52f705")]
    [InlineData(11, "replaceable", "{\"when\":\"shortRest\"}", "3175d7b0fd6b2e5f06d85984ca540ed1ff8075108c8e6261930f27421b33115b")]
    [InlineData(11, "replaceable", "\"x\"", "24939bc6823cbac82816f574f285a84ef615321a993e8fb30d5ce217f4b31b1c")]
    [InlineData(11, "replaceable", "{\"when\":\"levelUp, longRest\"}", "dc1446170e84f7c3be0db28f6a4e08dbfb188ab58de3c056c8844237d42f3c5a")]
    [InlineData(11, "replaceable", "{\"when\":\" longRest\"}", "337e27370dfe8ebb667473c66da125a4ee360f223dd10a1c704e5458c84ad447")]
    [InlineData(11, "replaceable", "null", "50513a4c4282c5b35494bfd304fc53243b8c41a1b284f59aa6c403f76486e0ef")]
    [InlineData(3, "replaceable", "{\"when\":\"levelUp\",\"max\":1}", "aaa82c674ce5c1695508ad9cb4de3bb2a90372af6c4fd6a00693d945bf61e97a")]
    [InlineData(3, "replaceable", "{\"when\":\"longRest\"}", "094f2c3d58f55cd9da8a3794cc8c25efd4ead8aa7309d560ed06f1ba12962a04")]
    [InlineData(3, "Replaceable", "{\"when\":\"longRest\"}", "b38ca0ab91dbc3bc0fb3eabddbf4c45eacc64f4c8aebd4ac8e2ad31ec1f93bde")]
    [InlineData(3, "replaceable", "{\"when\":\"shortRest\"}", "4b4667b1921f8d773599193aa8f917416d77897b3e05d95c372bd3ac153e6076")]
    [InlineData(3, "replaceable", "\"x\"", "3615127c3082d256eede1374477a23f6faf38c29bd0d9305968b7ecd482936dc")]
    [InlineData(3, "replaceable", "{\"when\":\"levelUp, longRest\"}", "594c582fdee9ea8b7cff6f962049ae5edc1146cdc482c55f04f35dc8e9c14c31")]
    [InlineData(3, "replaceable", "{\"when\":\" longRest\"}", "d84280f2c86317b72b1912470fe38d924d4cee5a0c49d359714f5a3063b7b3af")]
    [InlineData(3, "replaceable", "null", "8dbdbbf5903b6ad9db3e4a01611ea9c75339557b8f21798c356d282cc77506d7")]
    [InlineData(0, "replaceable", "{\"when\":\"levelUp\",\"max\":1}", "fb0fc40d628b48acb913c965a4f2aa3a7b8c20c140ad4f1c95fbff9e72384c9f")]
    [InlineData(0, "replaceable", "{\"when\":\"longRest\"}", "22e971738b0aded2b02f011dd382e298f43c6db33e0b81dd3091cd37e19c0213")]
    [InlineData(0, "Replaceable", "{\"when\":\"longRest\"}", "fa647769ee10fef56cceb8be171bd68a89174a438c907e073735f56f673727f7")]
    [InlineData(0, "replaceable", "{\"when\":\"shortRest\"}", "ae158b92a19b7c9313ee1b9354bd36f54471954bd88d2ce516e3809fd4c0dd74")]
    [InlineData(0, "replaceable", "\"x\"", "39623f0a3869c3790c524c384c13b99f87e87c208c25f737b82810444421712a")]
    [InlineData(0, "replaceable", "{\"when\":\"levelUp, longRest\"}", "2cac72156892366637183f026004233cc0414d25d72f96d6055a51fd915179b5")]
    [InlineData(0, "replaceable", "{\"when\":\" longRest\"}", "3f1262e907a566897ac4ff75c94530b3efdf20d7ee77fe898984c05cb73d8284")]
    [InlineData(0, "replaceable", "null", "84fa3bc2b50aeacec9cc2896e1aab18781107b9c04a8a610338851e741bc9815")]
    public void Below_v12_the_key_is_written_as_the_old_build_wrote_it(int version, string key, string value, string hash)
    {
        foreach (var inTheMiddle in new[] { true, false })
        {
            var revision = Read(Doc(version, ChoiceJson($"\"{key}\": {value}", inTheMiddle)));

            var stored = RevisionHashProperties.Write(revision);

            Assert.Equal(Doc(version, WrittenBefore(key, value)), stored);
            Assert.Equal(hash, RevisionHashProperties.Hash(stored));
            var choice = Assert.IsType<ChoiceEffect>(revision.Effects[0]);
            Assert.Null(choice.Replaceable);
            Assert.True(choice.Extensions!.ContainsKey(key));
        }
    }

    [Theory]
    [InlineData("{'when':'levelUp, longRest'}")]
    [InlineData("{'when':' longRest'}")]
    public void A_v12_window_that_is_padded_or_combined_does_not_read(string replaceable)
    {
        var effect = J("{'type':'choice','id':'c','choiceId':'pick','count':2,'replaceable':" + replaceable + "}");

        Assert.IsType<UnknownEffect>(Read(Doc(12, effect)).Effects[0]);
        Assert.Contains(ValidateJson(12, effect).Errors, e => e.Code == "validate.effect-malformed");
    }

    [Theory]
    [InlineData("'replaceable':{'when':'levelUp','max':1}", 1, "")]
    [InlineData("'replaceable':{'when':'levelUp','max':0}", 2, "validate.choice-replaceable")]
    [InlineData("'replaceable':{'when':'longRest','max':21}", 20, "validate.choice-replaceable")]
    [InlineData("'replaceable':{'when':'longRest','max':3}", 2, "validate.choice-replaceable")]
    [InlineData("'replaceable':{'when':'longRest','max':2}", 1, "validate.choice-replaceable")]
    [InlineData("'replaceable':{'when':'longRest'}", 2, "")]
    [InlineData("'replaceable':{'when':'longRest','max':20}", 20, "")]
    [InlineData("'replaceable':{'when':'longRest','max':-1}", 20, "validate.choice-replaceable")]
    public void Replaceable_rules(string fragment, int count, string code)
    {
        var report = ValidateJson(12, "{'type':'choice','id':'c','choiceId':'pick','count':" + count + "," + fragment + "}");

        if (code == "")
            Assert.Empty(report.Errors);
        else
            Assert.Contains(report.Errors, e => e.Code == code);
    }

    [Fact]
    public void The_too_many_message_names_the_choice_and_both_numbers()
    {
        var report = ValidateJson(12, "{'type':'choice','id':'c','choiceId':'pick','count':2,'replaceable':{'when':'levelUp','max':3}}");

        Assert.Equal("Choice 'pick' lets 3 picks be replaced but has only 2.", Assert.Single(report.Errors, e => e.Code == "validate.choice-replaceable").Message);
    }

    [Fact]
    public void Short_rest_is_not_a_window()
    {
        var report = ValidateJson(12, "{'type':'choice','id':'c','choiceId':'pick','count':2,'replaceable':{'when':'shortRest'}}");

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(12, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData("'replaceable':'x'")]
    [InlineData("'Replaceable':{'when':'levelUp','max':'many'}")]
    [InlineData("'replaceable':{'max':1}")]
    public void Unreadable_replaceable_blocks_from_v12(string fragment)
    {
        var effect = "{'type':'choice','id':'c','choiceId':'pick','count':2," + fragment + "}";

        var v12 = ValidateJson(12, effect);
        Assert.Contains(v12.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(12, v12.RequiredSchemaVersion);

        // Below v12 the key is stripped first: nothing about it blocks.
        Assert.DoesNotContain(ValidateJson(11, effect).Errors, e => e.Code == "validate.effect-malformed");
    }

    private static ContentRevision Built(int version, ChoiceReplaceable? replaceable) => new()
    {
        ContentId = Guid.Parse("6a7e0200-0000-4000-8000-0000000000c1"), RevisionId = Guid.NewGuid(), SchemaVersion = version, Kind = ContentKind.Feature,
        Name = "Fixture Content", RulesFamilies = [RulesFamilies.Srd521], Provenance = new(Guid.Parse(KnownSource)), Status = RevisionStatus.Draft,
        Effects = [new ChoiceEffect { Id = "c", ChoiceId = "pick", Count = 2, Replaceable = replaceable }],
    };

    [Fact]
    public void Replaceable_requires_v12()
    {
        var replaceable = new ChoiceReplaceable(ReplaceWindow.LevelUp);

        var at11 = ContentValidator.Validate(Built(11, replaceable), Catalog, null);
        var message = Assert.Single(at11.Errors, e => e.Code == "validate.requires-v12").Message;
        Assert.Contains("a replaceable choice", message, StringComparison.Ordinal);

        var at12 = ContentValidator.Validate(Built(12, replaceable), Catalog, null);
        Assert.DoesNotContain(at12.Errors, e => e.Code == "validate.requires-v12");
        Assert.Equal(12, at12.RequiredSchemaVersion);

        // Without the key the choice keeps the version it needed before.
        var without = ContentValidator.Validate(Built(12, null), Catalog, null);
        Assert.DoesNotContain(without.Errors, e => e.Code == "validate.requires-v12");
        Assert.True(without.RequiredSchemaVersion < 12);
    }

    [Fact]
    public void Replaceable_schema_version_is_current()
    {
        Assert.Equal(12, ContentRevision.ReplaceableChoiceSchemaVersion);
        Assert.Equal(ContentRevision.ReplaceableChoiceSchemaVersion, ContentRevision.CurrentSchemaVersion);
    }

    /// <summary>The gotcha of 2026-10-20: the production key list is internal and this project cannot see it, so reflection compares it with the list written out here.</summary>
    [Fact]
    public void Choice_v12_keys_match()
    {
        var field = typeof(ChoiceEffect).GetField("V12Keys", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(["replaceable"], (string[])field.GetValue(null)!);
    }

    /// <summary>
    /// "Fixture Shifting Repertoire" (tests/RulesFixtures/golden/choice-revisions.json): the golden harness holds one revision per
    /// version, so this v12 revision has its own file and hash, pinned in GoldenRevisions.cs.
    /// </summary>
    [Fact]
    public void Choice_goldens_keep_their_hashes()
    {
        var golden = GoldenRevisions.ChoiceJson(0);
        var stored = RevisionHashProperties.Write(Read(golden));

        Assert.True(GoldenRevisions.ChoiceHashes[0] == RevisionHashProperties.Hash(stored), $"Choice golden 0 now hashes to {RevisionHashProperties.Hash(stored)}.");
        Assert.Equal(stored, RevisionHashProperties.Write(Read(stored)));
        Assert.Null(RevisionHashProperties.UnknownPartsChanged(12, JsonNode.Parse(golden)!.AsObject(), stored));
        Assert.All(Read(stored).Effects, e => Assert.IsType<ChoiceEffect>(e));
        Assert.Equal(new ChoiceReplaceable(ReplaceWindow.LevelUp, 1), ((ChoiceEffect)Read(stored).Effects[0]).Replaceable);
    }
}
