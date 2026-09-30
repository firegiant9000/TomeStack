using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TomeStack.AppService.Extensions;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M6 slice 5: the author guides in docs/authoring are tested by following them. Every JSON block tagged
/// <c>tomestack-example:&lt;name&gt;</c> is read from the guide as written: the class guide's feature and class are
/// published and built, the source-pack steps are followed with that class into a clean data folder, and the extension
/// guide's blocks are zipped, installed, granted and run. If a guide and TomeStack disagree, these fail.
/// </summary>
public partial class AuthoringGuideTests
{
    private static readonly string Guides = Path.Combine(AppContext.BaseDirectory, "authoring");

    [GeneratedRegex("```json tomestack-example:(?<name>[^\\r\\n]+)\\r?\\n(?<body>.*?)\\r?\\n```", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Block();

    /// <summary>The tagged blocks of one guide, by name, exactly as the guide shows them.</summary>
    internal static IReadOnlyDictionary<string, string> Blocks(string guide) =>
        Block().Matches(File.ReadAllText(Path.Combine(Guides, guide))).ToDictionary(m => m.Groups["name"].Value.Trim(), m => m.Groups["body"].Value);

    /// <summary>A guide's entry as the studio saves it: TomeStack adds the ids, the source, the status and the version.</summary>
    private static ContentRevision Draft(string json, Guid sourceId, Func<JsonObject, JsonObject>? edit = null)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node["contentId"] = Guid.NewGuid().ToString("D");
        node["revisionId"] = Guid.NewGuid().ToString("D");
        node["schemaVersion"] = ContentRevision.CurrentSchemaVersion;
        node["provenance"] = new JsonObject { ["sourceId"] = sourceId.ToString("D") };
        node["status"] = "draft";
        return (edit?.Invoke(node) ?? node).Deserialize<ContentRevision>(RulesJson.Options)!;
    }

    /// <summary>Steps 1 to 3 of the class guide: a source, the feature published, then the class that grants it published.</summary>
    private static (SourceRecord Source, ContentReference Feature, ContentReference Class) FollowClassGuide(TempApp temp)
    {
        var blocks = Blocks("class.md");
        var source = temp.App.CreateHomebrewSource(new("Example Lantern Notes", [RulesFamilies.Srd51, RulesFamilies.Srd521], Redistributable: true, ConfirmOwnWork: true));
        var feature = Draft(blocks["feature"], source.Id);
        temp.App.SaveDraft(feature);
        var publishedFeature = temp.App.Publish(feature.Reference).Published;
        // "PUBLISHED-FEATURE is the feature you published in step 2: in the studio you pick it from a list."
        Assert.Contains("\"PUBLISHED-FEATURE\"", blocks["class"], StringComparison.Ordinal);
        var klass = Draft(blocks["class"], source.Id, node =>
        {
            foreach (var effect in node["effects"]!.AsArray().Where(e => e?["content"]?.GetValueKind() == JsonValueKind.String))
                effect!["content"] = new JsonObject { ["contentId"] = publishedFeature.ContentId.ToString("D"), ["revisionId"] = publishedFeature.RevisionId.ToString("D") };
            return node;
        });
        temp.App.SaveDraft(klass);
        return (source, publishedFeature, temp.App.Publish(klass.Reference).Published);
    }

    [Theory]
    [InlineData(RulesFamilies.Srd51)]
    [InlineData(RulesFamilies.Srd521)]
    public void The_class_guide_publishes_and_the_class_builds_from_level_1_to_20(string family)
    {
        using var temp = new TempApp();
        var (_, _, klass) = FollowClassGuide(temp);
        Assert.Equal(9, temp.App.Store.FindRevision(klass)!.SchemaVersion); // "it publishes it in content schema v9"
        int[] lantern = [1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5];
        foreach (var level in new[] { 1, 2, 5, 20 })
        {
            var view = temp.App.SaveCharacter(new Character
            {
                Id = Guid.NewGuid(), Name = "Example Keeper", RulesFamily = family, Level = level,
                Classes = [new(klass, level)], BaseAbilities = new(10, 12, 14, 10, 16, 13),
            });
            var sheet = view.Sheet;
            Assert.DoesNotContain(sheet.Diagnostics, d => d.Code is "content.missing" or "effect.invalid-formula" or "scale.missing" or "content.schema-unsupported");
            Assert.Equal(1 + lantern[level - 1], sheet.Resources!.Single(r => r.Label == "Oil").Maximum);
            Assert.Equal(8 + 2 + ((level - 1) * (5 + 2)), sheet.Field(FieldIds.HitPoints).Value); // d8: 8 + Con, then 5 + Con per level
            // The level-2 feature adds the Lantern column to Perception.
            var perception = sheet.Field(FieldIds.Skill("perception")).Value;
            Assert.Equal(sheet.Field(FieldIds.Modifier(Ability.Wis)).Value + (level >= 2 ? lantern[level - 1] : 0), perception);
        }
    }

    [Fact]
    public void The_source_pack_guide_shares_the_class_into_a_clean_data_folder_unchanged()
    {
        using var author = new TempApp();
        var (source, feature, klass) = FollowClassGuide(author);
        // Step 3: preview, then save the pack.
        var preview = author.App.PreviewSourcePack([source.Id]);
        Assert.Equal((2, "Example-Lantern-Notes-source-pack.tomestack.zip"), (preview.Revisions, preview.FileName));
        var pack = author.App.ExportSourcePack([source.Id]).Content;

        // Step 4: the friend imports it.
        using var friend = new TempApp();
        Assert.True(friend.App.PreviewImport(pack).CanApply);
        friend.App.ApplyImport(pack);
        foreach (var reference in new[] { feature, klass })
            Assert.Equal(TempApp.Json(author.App.Store.FindRevision(reference)), TempApp.Json(friend.App.Store.FindRevision(reference)));
        Assert.Equal(SourceOrigin.Received, friend.App.Store.FindSource(source.Id)!.Origin);
        Assert.Contains("pack.source-received", Assert.Throws<Packages.PackageException>(() => friend.App.ExportSourcePack([source.Id])).Errors.Select(e => e.Code));
    }

    /// <summary>The extension guide's blocks, zipped "holding extension.json and transforms/ at its root, and nothing else".</summary>
    internal static byte[] GuideExtension()
    {
        var blocks = Blocks("extension.md");
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in blocks.Where(b => b.Key == "extension.json" || b.Key.StartsWith("transforms/", StringComparison.Ordinal)))
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(body));
            }
        }
        return output.ToArray();
    }

    [Fact]
    public void The_extension_guide_installs_is_granted_and_writes_its_CSV()
    {
        using var temp = new TempApp();
        using (var manifest = JsonDocument.Parse(Blocks("extension.md")["extension.json"]))
            Assert.Equal("", SchemaTests.Validate("extension-manifest", manifest.RootElement));
        var preview = temp.App.PreviewExtensionInstall(GuideExtension());
        Assert.True(preview.CanInstall, string.Join("; ", preview.Errors.Select(e => e.Code)));
        var installed = temp.App.InstallExtension(preview.Token!.Value, ["read.sheet", "export.file"], confirm: true);
        var character = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json")).Character;
        var run = temp.App.PreviewExtensionRun(new(installed.Id, "features-csv", CharacterId: character.Id));
        Assert.EndsWith("-features-csv.csv", run.FileName, StringComparison.Ordinal);
        var csv = Encoding.UTF8.GetString(temp.App.ExtensionExportOutput(run.Token).Bytes);
        var lines = csv.Split('\n');
        Assert.Equal("name,kind,source", lines[0]);
        Assert.True(lines.Length > 2, csv);
        Assert.All(lines.Skip(1).Where(l => l.Length > 0), l => Assert.Matches("^\"[^\"]+\",[a-z]+,\"[^\"]+\"$", l));
    }
}
