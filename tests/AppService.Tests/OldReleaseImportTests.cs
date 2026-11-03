using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Files made by the real released builds open on this one (tester note "export from a previous version to a newer version").
/// <c>tests/RulesFixtures/upgrade/&lt;version&gt;/</c> holds, for each release, a character backup, a character share, a source pack,
/// a campaign pack and a full library backup, each exported by a build of that release's tag driving its own command dispatcher,
/// and <c>folder/</c>, that build's data folder (bundled SRD rows emptied, as for the v0.5.0 folder; they seed again on open).
/// The content is original only: a homebrew species and feat (whose second revision was published after the character pinned the
/// first), a draft, a one-page PDF, a campaign and a gap note, so no SRD revision travels in any package. <c>expected.json</c> is
/// every sheet value the old release itself calculated; this build must calculate the same. Both characters are level 0 (no class),
/// so this covers abilities, saves, skills, initiative, armour class and the pinned feat, not hit points or spellcasting.
/// </summary>
public sealed class OldReleaseImportTests : IDisposable
{
    public static TheoryData<string> Releases => new() { "v0.4.0", "v0.5.0", "v0.6.0", "v0.7.0" };

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly List<string> _directories = [];

    private static string Fixture(string release, string name) => Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "upgrade", release, name);

    private string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        _directories.Add(directory);
        return directory;
    }

    /// <summary>Runs after each test, once its app is closed.</summary>
    public void Dispose()
    {
        foreach (var directory in _directories)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best effort: a leftover temp folder does not fail a test
            }
        }
    }

    /// <summary>As the shipped app opens: the bundled packs only, no test fixtures.</summary>
    private static TomeStackApp Open(string directory) => TomeStackApp.Open(directory, new FixedTime(Now), syncRoots: []);

    private static JsonObject Expected(string release) => JsonNode.Parse(File.ReadAllText(Fixture(release, "expected.json")))!.AsObject();

    private static Dictionary<Guid, string> ExpectedNames(string release) =>
        Expected(release)["characters"]!.AsObject().ToDictionary(c => Guid.Parse(c.Key), c => c.Value!["name"]!.GetValue<string>());

    /// <summary>Every field the old release calculated has the same value here; newer builds may add fields.</summary>
    private static void AssertSameSheets(string release, TomeStackApp app, IEnumerable<Guid> characterIds)
    {
        var expected = Expected(release)["characters"]!.AsObject();
        foreach (var id in characterIds)
        {
            var old = expected[id.ToString("D")]!["fields"]!.AsObject();
            var sheet = JsonSerializer.SerializeToNode(app.GetCharacter(id).Sheet, RulesJson.Compact)!["fields"]!.AsArray()
                .ToDictionary(f => f!["field"]!.GetValue<string>(), f => f!["value"]?.ToJsonString());
            Assert.True(old.Count > 40, $"{release}: expected.json lists only {old.Count} fields");
            foreach (var (field, value) in old)
            {
                Assert.True(sheet.ContainsKey(field), $"{release}: {field} is no longer calculated");
                Assert.True(value?.ToJsonString() == sheet[field], $"{release}: {field} was {value?.ToJsonString()} in {release}, is {sheet[field]} now");
            }
        }
    }

    private static Guid HomebrewSource(TomeStackApp app, string title) => app.ListSources().Single(s => s.Title == title).Id;

    private static Guid Hero(string release) => ExpectedNames(release).Single(c => c.Value == "Compat Hero").Key;

    [Theory]
    [MemberData(nameof(Releases))]
    public void The_fixture_was_made_by_that_release(string release)
    {
        Assert.Equal(release[1..], Expected(release)["appVersion"]!.GetValue<string>());
        using var zip = System.IO.Compression.ZipFile.OpenRead(Fixture(release, "character-backup.tomestack.zip"));
        using var manifest = zip.GetEntry("manifest.json")!.Open();
        Assert.Equal(release[1..], JsonNode.Parse(manifest)!["appVersion"]!.GetValue<string>());
        // Original content only: every revision in every package is one of the fixture's own, none a bundled SRD revision.
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(Fixture(release, "x"))!, "*.tomestack.zip"))
        {
            using var package = System.IO.Compression.ZipFile.OpenRead(file);
            foreach (var entry in package.Entries.Where(e => e.FullName.StartsWith("content/", StringComparison.Ordinal)))
            {
                using var stream = entry.Open();
                var name = JsonNode.Parse(stream)!["name"]!.GetValue<string>();
                Assert.StartsWith("Compat ", name, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void A_character_backup_imports_with_the_same_sheets_its_campaign_and_its_gap_note(string release)
    {
        using var app = Open(NewDirectory());
        var bytes = File.ReadAllBytes(Fixture(release, "character-backup.tomestack.zip"));

        var preview = app.PreviewImport(bytes);
        Assert.Empty(preview.Errors);
        var result = app.ApplyImport(bytes);

        Assert.Equal(ExpectedNames(release).Keys.Order(), result.Characters.Order());
        AssertSameSheets(release, app, result.Characters);
        Assert.Equal("Compat Campaign", Assert.Single(app.ListCampaigns()).Name);
        Assert.Single(app.Store.ListAllGapNotes());
        Assert.Equal(SourceOrigin.Received, app.ListSources().Single(s => s.Title == "Compat Homebrew A").Origin);
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void A_character_share_imports_with_the_same_sheets_and_no_gap_note(string release)
    {
        using var app = Open(NewDirectory());
        var bytes = File.ReadAllBytes(Fixture(release, "character-share.tomestack.zip"));

        Assert.Empty(app.PreviewImport(bytes).Errors);
        var result = app.ApplyImport(bytes);

        Assert.Equal(ExpectedNames(release).Keys.Order(), result.Characters.Order());
        AssertSameSheets(release, app, result.Characters);
        Assert.Empty(app.Store.ListAllGapNotes());
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void A_source_pack_then_its_campaign_pack_import_and_the_newest_revision_stays_newest(string release)
    {
        using var app = Open(NewDirectory());
        var sourcePack = File.ReadAllBytes(Fixture(release, "source-pack.tomestack.zip"));
        var campaignPack = File.ReadAllBytes(Fixture(release, "campaign-pack.tomestack.zip"));

        Assert.Empty(app.PreviewImport(sourcePack).Errors);
        app.ApplyImport(sourcePack);
        Assert.Empty(app.PreviewImport(campaignPack).Errors);
        app.ApplyImport(campaignPack);

        var source = app.ListSources().Single(s => s.Title == "Compat Homebrew A");
        Assert.Equal(SourceOrigin.Received, source.Origin);
        var featId = app.Store.ListRevisions().First(r => r.Name == "Compat Steady Hands").ContentId;
        var published = app.Store.ListRevisions(featId).Where(r => r.Status == RevisionStatus.Published).ToList(); // in stored order
        Assert.Equal(2, published.Count);
        Assert.Equal(["2", "3"], published.Select(r => Assert.IsType<ModifierEffect>(Assert.Single(r.Effects)).Value)); // newest is last stored
        var campaign = Assert.Single(app.ListCampaigns());
        Assert.Equal([source.Id], campaign.AllowedSources);
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void A_library_backup_restores_into_a_new_folder_with_its_pdf_draft_and_update_offer(string release)
    {
        using var app = Open(NewDirectory());
        var path = Fixture(release, "library-backup.tomestack.zip");

        Assert.Empty(app.PreviewLibraryRestore(path).Errors);
        var result = app.ApplyLibraryRestore(path);

        Assert.Equal(1, result.PdfsCopied);
        AssertSameSheets(release, app, ExpectedNames(release).Keys);
        Assert.NotNull(app.GetAttachment(HomebrewSource(app, "Compat Notes B")));
        Assert.Contains(app.Store.ListRevisions(), r => r.Name == "Compat Unfinished Idea" && r.Status == RevisionStatus.Draft);
        Assert.Single(app.Store.ListAllGapNotes());
        AssertUpdateOffered(app, Hero(release));
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void The_release_data_folder_opens_in_place_with_the_same_sheets_its_pdf_and_one_app_version_copy(string release)
    {
        var directory = NewDirectory();
        CopyDirectory(Path.GetDirectoryName(Fixture(release, Path.Combine("folder", "x")))!, directory);
        File.Move(Path.Combine(directory, "database.sqlite"), Path.Combine(directory, TomeStackApp.DatabaseFileName));

        using (var app = Open(directory))
        {
            Assert.Equal(SqliteStore.LatestSchemaVersion, app.GetInfo().SchemaVersion);
            AssertSameSheets(release, app, ExpectedNames(release).Keys);
            Assert.NotNull(app.GetAttachment(HomebrewSource(app, "Compat Notes B")));
            AssertUpdateOffered(app, Hero(release));
        }

        var appCopies = Directory.GetFiles(directory, "tomestack.db.app-*.bak");
        Assert.Single(appCopies); // the copy to go back to the release it came from
        Assert.Equal(AppVersionStamp.CurrentVersion, File.ReadAllText(Path.Combine(directory, AppVersionStamp.FileName)).Trim());
    }

    /// <summary>The feat's second revision was published after the character pinned the first: the offer is there, and nothing changes until it is confirmed.</summary>
    private static void AssertUpdateOffered(TomeStackApp app, Guid hero)
    {
        var offer = Assert.Single(app.AvailableUpdates(hero));
        Assert.Equal("Compat Steady Hands", offer.Name);
        var before = app.GetCharacter(hero).Sheet.Field(CharacterCalculator.InitiativeField).Value;
        var refused = Assert.Throws<AppValidationException>(() => app.ApplyUpdate(hero, offer.From, offer.To, confirm: false));
        Assert.Contains(refused.Problems, p => p.Code == "update.confirmation-required");
        Assert.Equal(before, app.GetCharacter(hero).Sheet.Field(CharacterCalculator.InitiativeField).Value);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }
}
