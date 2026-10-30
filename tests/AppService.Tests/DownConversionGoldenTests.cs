using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D85 part 1, lane C1-4: a library saved "for an older TomeStack" is pinned byte for byte, entry by entry
/// (<c>tests/RulesFixtures/downconvert/&lt;folder&gt;/expected/</c>). Set <c>TOMESTACK_REGENERATE_GOLDENS=1</c> to rewrite them
/// from the implementation, then review them by eye (see the README beside them).
/// </summary>
public class DownConversionGoldenTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static TheoryData<string, string> Folders => new() { { "for-0.6.0", "0.6.0" }, { "for-0.5.0", "0.5.0" } };

    private static string SourceFolder(string folder)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("The repository root was not found."), "tests", "RulesFixtures", "downconvert", folder);
    }

    // The bundled SRD revision a fixture character pins, and its source. Their text is SRD material, which lives only in
    // src/AppService/Content/ (CONTRIBUTING invariant): the goldens do not hold these entries; the test compares them with the store.
    private static readonly Guid BundledContent = Guid.Parse("51c00000-0000-4000-8000-000000000001");
    private static readonly Guid BundledRevision = Guid.Parse("51e00000-0000-4000-8000-000000000001");
    private static readonly Guid BundledSource = Guid.Parse("51500000-0000-4000-8000-000000000001");
    private static readonly string BundledContentPath = $"content/{BundledRevision:D}.json";
    private static readonly string BundledSourcePath = $"sources/{BundledSource:D}.json";

    private static bool IsBundled(string path) => path == BundledContentPath || path == BundledSourcePath;

    /// <summary>
    /// The build's version varies, so it is written as <c>{appVersion}</c> in the manifest (the README says so). The bundled
    /// entries' sha256 and size, and the bundled source's notice text, are SRD-derived, so they become placeholders.
    /// </summary>
    private static string Normalise(string entry, byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n"); // the repository stores LF; the writer uses the platform's line ending
        if (entry != "manifest.json")
            return text;
        var root = JsonNode.Parse(text)!.AsObject();
        root["appVersion"] = "{appVersion}";
        foreach (var item in root["entries"]!.AsArray().Select(n => n!.AsObject()).Where(o => IsBundled(o["path"]!.GetValue<string>())))
        {
            item["sha256"] = "{bundled}";
            item["size"] = "{bundled}";
        }
        var notices = root["notices"]!.AsArray();
        for (var i = 0; i < notices.Count; i++)
            if (notices[i]!["sourceId"]!.GetValue<string>() == BundledSource.ToString("D"))
                notices[i] = new JsonObject { ["sourceId"] = BundledSource.ToString("D"), ["text"] = "{bundled}" };
        return root.ToJsonString(Indented).ReplaceLineEndings("\n");
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void Golden_copy_matches_byte_for_byte(string folder, string target)
    {
        using var app = new TempApp();
        var library = $"downconvert/{folder}/library";
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", library, "pack.json")))
            app.AddPack($"{library}/pack.json");
        var id = app.App.SaveCharacter(TempApp.LoadFixture<Character>($"{library}/character.json")).Character.Id;
        var dispatcher = new CommandDispatcher(app.App);
        var payload = $$"""{"characterIds":["{{id}}"],"purpose":"share","target":"{{target}}"}""";

        var preview = Dispatch(dispatcher, "package.exportPreview", payload);
        var hash = preview.GetProperty("changesHash").GetString()!;
        var export = Dispatch(dispatcher, "package.export", payload.Replace("}", $",\"changesHash\":\"{hash}\"}}", StringComparison.Ordinal));
        var changes = JsonSerializer.Serialize(preview.GetProperty("changes"), Indented).ReplaceLineEndings("\n");

        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using (var zip = new ZipArchive(new MemoryStream(Convert.FromBase64String(export.GetProperty("base64").GetString()!))))
            foreach (var entry in zip.Entries.Where(e => e.Length > 0 || !e.FullName.EndsWith('/')))
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                entries[entry.FullName] = buffer.ToArray();
            }

        // The bundled revision is written as it is, and its source as a character package writes it: byte for byte what the store holds.
        Assert.True(entries.ContainsKey(BundledContentPath) && entries.ContainsKey(BundledSourcePath), "The bundled revision and its source must be in the package.");
        Assert.Equal(PackageService.Json(app.App.Store.FindRevision(new ContentReference(BundledContent, BundledRevision))!), entries[BundledContentPath]);
        var storedSource = app.App.Store.FindSource(BundledSource)!;
        Assert.Equal(
            PackageService.Json(storedSource with { PdfRef = null, AttachmentId = null, ImportDerived = null, Origin = null, ShareConfirmedAt = null }),
            entries[BundledSourcePath]);
        entries.Remove(BundledContentPath);
        entries.Remove(BundledSourcePath);

        var expected = Path.Combine(SourceFolder(folder), "expected");
        if (Environment.GetEnvironmentVariable("TOMESTACK_REGENERATE_GOLDENS") == "1")
        {
            if (Directory.Exists(expected))
                Directory.Delete(expected, recursive: true);
            foreach (var (name, bytes) in entries)
            {
                var path = Path.Combine(expected, "entries", name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Normalise(name, bytes), new UTF8Encoding(false));
            }

            File.WriteAllText(Path.Combine(expected, "changes.json"), changes + "\n", new UTF8Encoding(false));
            Assert.Fail($"Regenerated the expected files under {folder}; review them by eye against spec section 2, unset TOMESTACK_REGENERATE_GOLDENS and run again.");
        }

        var expectedEntries = Path.Combine(expected, "entries");
        Assert.True(Directory.Exists(expectedEntries), $"No expected/ for {folder}; run once with TOMESTACK_REGENERATE_GOLDENS=1 and review it.");
        Assert.Equal(
            Directory.EnumerateFiles(expectedEntries, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(expectedEntries, f).Replace('\\', '/')).Order(StringComparer.Ordinal),
            entries.Keys);
        foreach (var (name, bytes) in entries)
            Assert.Equal(File.ReadAllText(Path.Combine(expectedEntries, name.Replace('/', Path.DirectorySeparatorChar))), Normalise(name, bytes));
        Assert.Equal(File.ReadAllText(Path.Combine(expected, "changes.json")).TrimEnd('\n'), changes);

        using var manifest = JsonDocument.Parse(entries["manifest.json"]);
        Assert.Equal("", SchemaTests.Validate("package-manifest", manifest.RootElement));
        Assert.Equal(target, manifest.RootElement.GetProperty("madeFor").GetString());
    }

    // ---- C2-7 section 2: the packages lane E opens with the real older build (spec section 8, "Real older build") ----

    private static readonly HashSet<Guid> BlockingInPacks =
    [
        Guid.Parse("c1060000-0000-4000-8000-0000000000e9"), // Compat Reservoir
        Guid.Parse("c1060000-0000-4000-8000-0000000000ea"), // Compat Draw Down
    ];

    /// <summary>
    /// The sheet fields the old build must reproduce: ability scores, hit points, armor class, proficiency, initiative, one save
    /// and one skill. The lowered characters keep all of them; none comes from a dropped effect.
    /// </summary>
    private static readonly string[] HarnessFields =
    [
        "ability.str.score", "ability.dex.score", "ability.con.score", "ability.int.score", "ability.wis.score", "ability.cha.score",
        "ability.dex.mod", "proficiencyBonus", "save.dex", "skill.acrobatics", "skill.perception", "passive.perception",
        "speed", "initiative", "armorClass", "hitPoints",
    ];

    // armorClass (a kept bonus in a lowered copy) and hitPoints (a hit die in the class) are what the lowered content feeds.

    /// <summary>
    /// Produces the harness inputs of <paramref name="folder"/> from its golden library with the clock pinned (<see cref="TempApp.Now"/>)
    /// and no bundled pin (R-C17: no SRD text under tests/): the character's bundled revision is dropped from a test-time copy only.
    /// Returns file name to bytes, and the text of <c>expected.json</c>.
    /// </summary>
    private static (SortedDictionary<string, byte[]> Zips, string ExpectedJson) ProduceInputs(string folder, string target)
    {
        var library = $"downconvert/{folder}/library";
        var zips = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        Guid id;
        using (var app = new TempApp())
        {
            // The drawsDown golden revisions (a counting-up pool and the roll that draws it down) block a whole-source export by
            // design (spec section 2), so a test-time copy of the pack leaves them out; the committed library is not edited.
            var pack = TempApp.LoadFixture<ContentPack>($"{library}/pack.json");
            var sourceOfPack = pack.Sources[0].Id;
            // The sheet must depend on lowered content, so a test-time copy adds (the library files are not edited): an always-on
            // armor class bonus in a revision that is lowered (0.6.0: the cascade copy "Compat Tide Mark"; 0.5.0: the v11 revision
            // going straight to v9), and a class level (0.6.0: the library's "Compat Path of Tides", a cascade copy; 0.5.0: a class of its own).
            var bonusRevision = Guid.Parse(target == "0.6.0" ? "c1060000-0000-4000-8000-0000000000e8" : "c1050000-0000-4000-8000-0000000000e4");
            var ward = new ModifierEffect { Id = "mark-ac", Operation = ModifierOperation.Bonus, Target = FieldIds.ArmorClass, Value = "1" };
            ContentReference classReference;
            List<ContentRevision> revisions = [.. pack.Revisions.Where(r => !BlockingInPacks.Contains(r.RevisionId))
                .Select(r => r.RevisionId == bonusRevision ? r with { Effects = [.. r.Effects, ward] } : r)];
            if (target == "0.6.0")
                classReference = new(Guid.Parse("c1060000-0000-4000-8000-0000000000c6"), Guid.Parse("c1060000-0000-4000-8000-0000000000e6"));
            else
            {
                var teacher = new ContentRevision
                {
                    ContentId = Guid.Parse("c1050000-0000-4000-8000-0000000000c9"),
                    RevisionId = Guid.Parse("c1050000-0000-4000-8000-0000000000e9"),
                    Kind = ContentKind.Class,
                    Name = "Compat Test Class",
                    RulesFamilies = [RulesFamilies.Srd51],
                    Provenance = new(sourceOfPack),
                    Status = RevisionStatus.Published,
                    Effects = [new HitDieEffect { Id = "test-hit-die", Die = 8 }],
                };
                revisions.Add(teacher);
                classReference = teacher.Reference;
            }
            pack = pack with { Revisions = revisions };
            app.App.Store.InTransaction(() =>
            {
                foreach (var source in pack.Sources)
                    app.App.Store.UpsertSource(source);
                foreach (var revision in pack.Revisions)
                    app.App.Store.AddRevision(revision);
            });
            var sourceId = pack.Sources[0].Id;
            app.App.SetShareable(new ShareableRequest(sourceId, true, ConfirmOwnWork: true));
            var character = TempApp.LoadFixture<Character>($"{library}/character.json");
            character = character with
            {
                // Tide Mark is pinned directly as well: reached only through class and subclass grants, its bonus did not reach the sheet.
                Pins = [.. character.Pins.Where(p => p.RevisionId != BundledRevision && !BlockingInPacks.Contains(p.RevisionId)).Append(new ContentReference(Guid.Parse(target == "0.6.0" ? "c1060000-0000-4000-8000-0000000000c8" : "c1050000-0000-4000-8000-0000000000c4"), bonusRevision)).DistinctBy(p => p.RevisionId)],
                Classes = [new ClassLevel(classReference, 1)],
            };
            id = app.App.SaveCharacter(character).Character.Id;
            var campaignId = Guid.Parse(sourceId.ToString("D")[..8] + "-0000-4000-8000-0000000000d1");
            var campaign = app.App.SaveCampaign(new() { Id = campaignId,Name = "Compat Campaign", RulesFamily = character.RulesFamily, AllowedSources = [sourceId] });
            var dispatcher = new CommandDispatcher(app.App);

            void Add(string preview, string export, string body)
            {
                var hash = Dispatch(dispatcher, preview, body).GetProperty("changesHash").GetString();
                var result = Dispatch(dispatcher, export, body.Replace("}", $",\"changesHash\":\"{hash}\"}}", StringComparison.Ordinal));
                zips[result.GetProperty("fileName").GetString()!] = Convert.FromBase64String(result.GetProperty("base64").GetString()!);
            }

            Add("package.exportPreview", "package.export", $$"""{"characterIds":["{{id}}"],"purpose":"share","target":"{{target}}"}""");
            Add("package.exportPreview", "package.export", $$"""{"characterIds":["{{id}}"],"purpose":"backup","target":"{{target}}"}""");
            Add("package.sourcePackPreview", "package.sourcePackExport", $$"""{"sourceIds":["{{sourceId}}"],"target":"{{target}}"}""");
            Add("package.campaignPackPreview", "package.campaignPackExport", $$"""{"campaignId":"{{campaign.Id}}","target":"{{target}}"}""");

            var row = ReleaseLadder.Target(target)!;
            using var backup = new MemoryStream();
            app.App.WriteLibraryBackup(backup, row, app.App.PreviewLibraryBackup(row).ChangesHash);
            zips[$"library-backup-for-{target}.tomestack.zip"] = KeepOnlySource(backup.ToArray(), sourceId); // the harness restores files named library-backup*
        }

        // expected.json: import the written character share into a fresh store and calculate it with the current build.
        var share = zips[$"Compat-Hero-for-{target}.tomestack.zip"];
        using var fresh = new TempApp(fresh: true);
        var dispatch = new CommandDispatcher(fresh.App);
        Dispatch(dispatch, "package.apply", $$"""{"base64":"{{Convert.ToBase64String(share)}}"}""");
        var fields = SheetFields(dispatch, id);
        // The committed backup restores to the same sheet (it is a valid backup, not only a well-formed zip).
        Assert.Equal(fields.ToJsonString(Indented), Restores(zips[$"library-backup-for-{target}.tomestack.zip"], id));
        var expected = new JsonObject { ["characters"] = new JsonObject { [id.ToString("D")] = new JsonObject { ["fields"] = fields } } };
        return (zips, expected.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n");
    }

    /// <summary>
    /// A library backup always carries every source in the store, and the bundled SRD sources hold SRD notice text (R-C17: none under
    /// tests/). The committed backup is that backup with every source other than the fixture's, and the content of those sources, taken
    /// out, and the manifest's entries, notices and revisionOrder brought into line. The result is checked by restoring it
    /// (<see cref="Restores"/>), so it is a valid backup.
    /// </summary>
    private static byte[] KeepOnlySource(byte[] backup, Guid sourceId)
    {
        var kept = new List<(string Name, DateTimeOffset Time, byte[] Bytes)>();
        var keptRevisions = new HashSet<string>();
        JsonObject? found = null;
        using (var zip = new ZipArchive(new MemoryStream(backup)))
        {
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                if (entry.FullName == "manifest.json")
                {
                    found = JsonNode.Parse(bytes)!.AsObject();
                    continue;
                }
                if (entry.FullName.StartsWith("sources/", StringComparison.Ordinal) && entry.FullName != $"sources/{sourceId:D}.json")
                    continue;
                if (entry.FullName.StartsWith("content/", StringComparison.Ordinal))
                {
                    var revision = JsonNode.Parse(bytes)!.AsObject();
                    if (revision["provenance"]!["sourceId"]!.GetValue<string>() != sourceId.ToString("D"))
                        continue;
                    keptRevisions.Add(revision["revisionId"]!.GetValue<string>());
                }
                kept.Add((entry.FullName, entry.LastWriteTime, bytes));
            }
        }

        var manifest = found ?? throw new InvalidOperationException("The library backup has no manifest.json.");
        var names = kept.Select(k => k.Name).ToHashSet(StringComparer.Ordinal);
        manifest["entries"] = new JsonArray([.. manifest["entries"]!.AsArray().Where(e => names.Contains(e!["path"]!.GetValue<string>())).Select(e => e!.DeepClone())]);
        manifest["notices"] = new JsonArray([.. manifest["notices"]!.AsArray().Where(n => n!["sourceId"]!.GetValue<string>() == sourceId.ToString("D")).Select(n => n!.DeepClone())]);
        manifest["revisionOrder"] = new JsonArray([.. manifest["revisionOrder"]!.AsArray().Where(r => keptRevisions.Contains(r!.GetValue<string>())).Select(r => r!.DeepClone())]);

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, time, bytes) in kept)
                Write(zip, name, time, bytes);
            Write(zip, "manifest.json", kept[0].Time, Encoding.UTF8.GetBytes(manifest.ToJsonString(Indented)));
        }
        return output.ToArray();

        static void Write(ZipArchive zip, string name, DateTimeOffset time, byte[] bytes)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = time;
            using var stream = entry.Open();
            stream.Write(bytes);
        }
    }

    /// <summary>Restores <paramref name="backup"/> into an empty store with the current build and returns the character's sheet fields as JSON.</summary>
    private static string Restores(byte[] backup, Guid characterId)
    {
        var path = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N") + ".tomestack.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, backup);
        try
        {
            using var restored = new TempApp(fresh: true);
            restored.App.ApplyLibraryRestore(path);
            return SheetFields(new CommandDispatcher(restored.App), characterId).ToJsonString(Indented);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static JsonObject SheetFields(CommandDispatcher dispatcher, Guid id)
    {
        var view = Dispatch(dispatcher, "character.get", $$"""{"id":"{{id}}"}""");
        var fields = new JsonObject();
        foreach (var field in view.GetProperty("sheet").GetProperty("fields").EnumerateArray())
        {
            var name = field.GetProperty("field").GetString()!;
            if (HarnessFields.Contains(name))
                fields[name] = JsonNode.Parse(field.GetProperty("value").GetRawText());
        }
        return fields;
    }

    /// <summary>The zip's entries by name; the manifest's <c>appVersion</c> is neutral, so a version bump does not stale a committed input.</summary>
    private static SortedDictionary<string, string> Entries(byte[] zipBytes)
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using var zip = new ZipArchive(new MemoryStream(zipBytes));
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var text = Convert.ToBase64String(buffer.ToArray());
            if (entry.FullName == "manifest.json")
            {
                var root = JsonNode.Parse(buffer.ToArray())!.AsObject();
                root["appVersion"] = "{appVersion}";
                text = root.ToJsonString();
            }
            entries.Add(entry.FullName, text); // a duplicate entry name throws: a zip with two entries of one name is never expected
        }
        return entries;
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void Harness_inputs_match_the_committed_files(string folder, string target)
    {
        var (zips, expectedJson) = ProduceInputs(folder, target);
        var directory = SourceFolder(folder);
        if (Environment.GetEnvironmentVariable("TOMESTACK_REGENERATE_GOLDENS") == "1")
        {
            foreach (var old in Directory.EnumerateFiles(directory, "*.tomestack.zip"))
                File.Delete(old);
            foreach (var (name, bytes) in zips)
                File.WriteAllBytes(Path.Combine(directory, name), bytes);
            File.WriteAllText(Path.Combine(directory, "expected.json"), expectedJson, new UTF8Encoding(false));
            Assert.Fail($"Regenerated the harness inputs under {folder}; review expected.json, unset TOMESTACK_REGENERATE_GOLDENS and run again.");
        }

        Assert.Equal(
            Directory.EnumerateFiles(directory, "*.tomestack.zip").Select(Path.GetFileName).Order(StringComparer.Ordinal),
            zips.Keys);
        foreach (var (name, bytes) in zips)
            Assert.Equal(Entries(File.ReadAllBytes(Path.Combine(directory, name))), Entries(bytes));
        Assert.Equal(File.ReadAllText(Path.Combine(directory, "expected.json")).ReplaceLineEndings("\n"), expectedJson);
    }

    [Fact]
    public void Cascade_rewritten_grant_points_at_the_subclass_copy_and_the_sheet_resolves_it()
    {
        var (zips, _) = ProduceInputs("for-0.6.0", "0.6.0");
        var share = zips["Compat-Hero-for-0.6.0.tomestack.zip"];
        var classCopy = Guid.Parse("c1060000-0000-4000-8000-0000000000e6");
        var subclassOriginal = Guid.Parse("c1060000-0000-4000-8000-0000000000e7");
        JsonObject? path = null;
        JsonObject? walker = null;
        using (var zip = new ZipArchive(new MemoryStream(share)))
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("content/", StringComparison.Ordinal)))
            {
                using var stream = entry.Open();
                var node = JsonNode.Parse(stream)!.AsObject();
                if (node["name"]!.GetValue<string>() == "Compat Path of Tides")
                    path = node;
                else if (node["name"]!.GetValue<string>() == "Compat Tide Walker")
                    walker = node;
            }

        Assert.NotNull(path);
        Assert.NotNull(walker);
        var walkerId = Guid.Parse(walker["revisionId"]!.GetValue<string>());
        Assert.NotEqual(subclassOriginal, walkerId);
        Assert.Equal(subclassOriginal.ToString("D"), walker["downConvertedFrom"]!["revisionId"]!.GetValue<string>());
        Assert.NotEqual(classCopy.ToString("D"), path["revisionId"]!.GetValue<string>());
        var grant = path["effects"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "grant-walker")!;
        Assert.Equal(walkerId.ToString("D"), grant["content"]!["revisionId"]!.GetValue<string>());

        // The lowered character's sheet resolves the subclass copy (Tide Mark is pinned too, so the subclass itself is what is looked for).
        using var fresh = new TempApp(fresh: true);
        var dispatcher = new CommandDispatcher(fresh.App);
        Dispatch(dispatcher, "package.apply", $$"""{"base64":"{{Convert.ToBase64String(share)}}"}""");
        var id = fresh.App.Store.ListCharacters().Single().Id;
        var view = Dispatch(dispatcher, "character.get", $$"""{"id":"{{id}}"}""");
        Assert.Contains("Compat Tide Walker", view.GetProperty("sheet").GetRawText(), StringComparison.Ordinal);
    }

    private static JsonElement Dispatch(CommandDispatcher dispatcher, string command, string payload)
    {
        var response = JsonDocument.Parse(dispatcher.Dispatch($$"""{"id":"1","command":"{{command}}","payload":{{payload}}}""")).RootElement.Clone();
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        return response.GetProperty("result");
    }
}
