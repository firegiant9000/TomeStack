using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>docs/schemas describe the stored and exchanged JSON; fixtures and real exports must conform.</summary>
public class SchemaTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "RulesFixtures");

    /// <summary>
    /// JsonSchema.Net registers each <c>$id</c> globally and refuses to load it twice. <c>GetOrAdd</c> alone may run its
    /// factory twice when test classes validate in parallel, so each schema is loaded once through a <see cref="Lazy{T}"/>.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<JsonSchema>> Loaded = new(StringComparer.Ordinal);

    /// <summary>
    /// Picks <c>{kind}.v{schemaVersion}.schema.json</c>; documents without a version are v1. A source has no version
    /// field: one with an M6 slice 1 field (only in v7 library backups and the database) is <c>source.v2</c>.
    /// </summary>
    internal static string Validate(string kind, JsonElement document)
    {
        var version = kind == "source"
            ? (document.TryGetProperty("importDerived", out _) || document.TryGetProperty("origin", out _) || document.TryGetProperty("shareConfirmedAt", out _) ? 2 : 1)
            : document.TryGetProperty(kind == "package-manifest" ? "formatVersion" : "schemaVersion", out var v) ? v.GetInt32() : 1;
        return ValidateFile($"{kind}.v{version}.schema.json", document);
    }

    /// <summary>Validates against one schema file by name (M6 slice 4: the Foundry schema is named by the pinned system version).</summary>
    internal static string ValidateFile(string fileName, JsonElement document)
    {
        var schema = Loaded.GetOrAdd(
            fileName,
            name => new Lazy<JsonSchema>(() => JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schemas", name))), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        var result = schema.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        if (result.IsValid)
            return "";
        return string.Join("; ", (result.Details ?? []).Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key} {e.Value}")));
    }

    private static void AssertValid(string kind, JsonElement document, string label)
    {
        var errors = Validate(kind, document);
        Assert.True(errors.Length == 0, $"{label} does not match {kind} schema: {errors}");
    }

    public static TheoryData<string> CharacterFixtures() =>
        [.. Directory.GetFiles(Path.Combine(FixtureRoot, "characters"), "*.json").Select(f => Path.GetFileName(f))];

    [Theory]
    [MemberData(nameof(CharacterFixtures))]
    public void Character_fixtures_match_the_character_schema(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "characters", file)));
        AssertValid("character", document.RootElement, file);
    }

    [Fact]
    public void The_v10_golden_revision_matches_the_v10_schema_and_the_schema_is_not_vacuous()
    {
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "golden", "revisions.json")))!.AsArray()[9]!.AsObject();
        using (var document = JsonDocument.Parse(golden.ToJsonString()))
            AssertValid("content-revision", document.RootElement, "the v10 golden revision");

        // Each v10 bound and rule is enforced by the schema, not only described in it.
        string ErrorsWith(Action<JsonArray> change)
        {
            var copy = golden.DeepClone().AsObject();
            change(copy["effects"]!.AsArray());
            using var document = JsonDocument.Parse(copy.ToJsonString());
            return Validate("content-revision", document.RootElement);
        }
        JsonObject Effect(JsonArray effects, string id) => effects.Single(e => (string?)e!["id"] == id)!.AsObject();

        Assert.NotEqual("", ErrorsWith(e => Effect(e, "ember-item")["charges"]!["max"] = 100));
        Assert.NotEqual("", ErrorsWith(e => Effect(e, "ember-item")["charges"]!["max"] = 0));
        Assert.NotEqual("", ErrorsWith(e => Effect(e, "ember-item")["charges"]!["recovery"]!["period"] = "weekly"));
        Assert.NotEqual("", ErrorsWith(e => Effect(e, "ember-glow")["autoWhen"] = "dying"));
        Assert.NotEqual("", ErrorsWith(e => Effect(e, "ember-glow")["activation"] = "free"));
        Assert.NotEqual("", ErrorsWith(e => Effect(e, "ember-pool")["activation"] = "free"));
        Assert.NotEqual("", ErrorsWith(e => Effect(e, "ember-ward").Remove("damageType")));
        Assert.NotEqual("", ErrorsWith(e => Effect(e, "ember-ward")["target"] = "skill.stealth"));
        Assert.NotEqual("", ErrorsWith(e => Effect(e, "ember-pace")["target"] = "passive.stealth"));
        Assert.NotEqual("", ErrorsWith(e => e.Add(new JsonObject { ["type"] = "grant", ["id"] = "x", ["grant"] = "proficiency", ["target"] = "skill.stealth", ["damageType"] = "fire" })));
        Assert.Equal("", ErrorsWith(e => Effect(e, "ember-item")["charges"]!["max"] = 99));
    }

    [Fact]
    public void The_v9_schema_is_not_vacuous()
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "characters", "srd521-courier-v9.json")))!;
        node["xp"] = -1;
        node["details"]!["alignment"] = new string('x', 2001);
        var languages = new JsonArray();
        for (var i = 0; i < 31; i++)
            languages.Add("x");
        node["details"]!["languages"] = languages;
        node["damageDefences"]![0]!["type"] = "sonic";
        node["customAttacks"]![0]!["toHit"] = 99;
        node["companions"]![0]!["hitPointMaximum"] = 10_001;
        node["play"]!["companions"]![0]!["temporary"] = -1;

        using var document = JsonDocument.Parse(node.ToJsonString());
        var errors = Validate("character", document.RootElement);

        foreach (var location in new[] { "/xp", "/details/alignment", "/details/languages", "/damageDefences/0/type", "/customAttacks/0/toHit", "/companions/0/hitPointMaximum", "/play/companions/0/temporary" })
            Assert.Contains(location, errors, StringComparison.Ordinal);
    }

    [Fact]
    public void The_v9_schema_bounds_equal_the_character_constants()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schemas", "character.v9.schema.json")))!;
        var properties = schema["properties"]!;
        var defs = schema["$defs"]!;
        var item = properties["customItems"]!["items"]!["properties"]!;
        var companion = properties["companions"]!["items"]!["properties"]!;
        var attack = defs["customAttack"]!["properties"]!;

        (string Where, int? Actual, int Expected)[] bounds =
        [
            ("details text", defs["detailText"]!["maxLength"]!.GetValue<int>(), Character.MaxDetailText),
            ("details list entries", defs["shortList"]!["maxItems"]!.GetValue<int>(), Character.MaxDetailListEntries),
            ("details list entry length", defs["shortList"]!["items"]!["maxLength"]!.GetValue<int>(), Character.MaxDetailListEntryLength),
            ("customItems", properties["customItems"]!["maxItems"]!.GetValue<int>(), Character.MaxCustomItems),
            ("item name", item["name"]!["maxLength"]!.GetValue<int>(), Character.MaxName),
            ("item note", item["note"]!["maxLength"]!.GetValue<int>(), Character.MaxShortNote),
            ("item quantity", item["quantity"]!["maximum"]!.GetValue<int>(), EquipmentEntry.MaxQuantity),
            ("customAttacks", properties["customAttacks"]!["maxItems"]!.GetValue<int>(), Character.MaxCustomAttacks),
            ("attack name", attack["name"]!["maxLength"]!.GetValue<int>(), Character.MaxName),
            ("attack note", attack["note"]!["maxLength"]!.GetValue<int>(), Character.MaxShortNote),
            ("attack type", attack["damageType"]!["maxLength"]!.GetValue<int>(), Character.MaxDamageTypeLength),
            ("attack damage", attack["damage"]!["maxLength"]!.GetValue<int>(), DiceLimits.MaxLength),
            ("attack to hit min", attack["toHit"]!["minimum"]!.GetValue<int>(), Character.MinToHit),
            ("attack to hit max", attack["toHit"]!["maximum"]!.GetValue<int>(), Character.MaxToHit),
            ("damageDefences", properties["damageDefences"]!["maxItems"]!.GetValue<int>(), DamageTypes.All.Count * Enum.GetValues<DamageDefenceKind>().Length),
            ("companions", properties["companions"]!["maxItems"]!.GetValue<int>(), Character.MaxCompanions),
            ("companion name", companion["name"]!["maxLength"]!.GetValue<int>(), Character.MaxName),
            ("companion kind", companion["kind"]!["maxLength"]!.GetValue<int>(), Character.MaxCompanionTextLength),
            ("companion speed", companion["speed"]!["maxLength"]!.GetValue<int>(), Character.MaxCompanionSpeedLength),
            ("companion note", companion["note"]!["maxLength"]!.GetValue<int>(), Character.MaxShortNote),
            ("companion armor class", companion["armorClass"]!["maximum"]!.GetValue<int>(), Character.MaxCompanionArmorClass),
            ("companion hit points", companion["hitPointMaximum"]!["maximum"]!.GetValue<int>(), PlayState.MaxHitPoints),
            ("companion attacks", companion["attacks"]!["maxItems"]!.GetValue<int>(), Character.MaxCompanionAttacks),
            ("xp", properties["xp"]!["maximum"]!.GetValue<int>(), Character.MaxXp),
        ];
        foreach (var (where, actual, expected) in bounds)
            Assert.True(actual == expected, $"{where}: the schema says {actual}, the code says {expected}");
        Assert.Equal(DamageTypes.All, properties["damageDefences"]!["items"]!["properties"]!["type"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void The_v9_schema_refuses_the_empty_id_like_the_code_does()
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "characters", "srd521-courier-v9.json")))!;
        node["customItems"]![0]!["id"] = Guid.Empty.ToString();

        using var document = JsonDocument.Parse(node.ToJsonString());

        Assert.Contains("/customItems/0/id", Validate("character", document.RootElement), StringComparison.Ordinal);
    }

    /// <summary>Test fixture packs and the bundled SRD packs.</summary>
    public static TheoryData<string> PackFixtures() =>
    [
        .. Directory.GetFiles(FixtureRoot, "fixture-pack*.json").Select(f => Path.Combine("RulesFixtures", Path.GetFileName(f))),
        .. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Content"), "*.json").Select(f => Path.Combine("Content", Path.GetFileName(f))),
    ];

    [Theory]
    [MemberData(nameof(PackFixtures))]
    public void Fixture_pack_sources_and_revisions_match_their_schemas(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)));
        AssertValid("content-pack", document.RootElement, file);
        foreach (var source in document.RootElement.GetProperty("sources").EnumerateArray())
            AssertValid("source", source, $"{file} source {source.GetProperty("id")}");
        foreach (var revision in document.RootElement.GetProperty("revisions").EnumerateArray())
            AssertValid("content-revision", revision, $"{file} revision {revision.GetProperty("name")}");
    }

    [Fact]
    public void Every_entry_and_the_manifest_of_an_exported_package_match_their_schemas()
    {
        using var temp = new TempApp();
        var ids = new[] { "srd51-quickfoot.json", "srd521-courier.json" }
            .Select(f => temp.App.SaveCharacter(TempApp.LoadFixture<Character>($"characters/{f}")).Character.Id)
            .ToList();
        var sheet = temp.App.GetCharacter(ids[0]).Sheet;
        temp.App.AddGapNote(new(ids[0], new(GapTargetKind.Field, FieldId: FieldIds.ArmorClass), "Field note."));
        temp.App.AddGapNote(new(ids[0], new(GapTargetKind.Feature, sheet.Features![0].Content.ContentId), "Feature note."));
        temp.App.Store.InTransaction(() => temp.App.AddImportGapNotes(ids[0], ["Fixture Lost Feat"])); // a v2 note (D16b)
        var package = temp.App.ExportCharacters(ids).Content;

        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var kinds = new Dictionary<string, string> { ["sources"] = "source", ["content"] = "content-revision", ["characters"] = "character", ["campaigns"] = "campaign", ["gaps"] = "gap-note" };
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var kind = entry.FullName == "manifest.json" ? "package-manifest" : kinds[entry.FullName[..entry.FullName.IndexOf('/', StringComparison.Ordinal)]];
            AssertValid(kind, document.RootElement, entry.FullName);
        }
        Assert.Contains(zip.Entries, e => e.FullName.StartsWith("content/", StringComparison.Ordinal));
        Assert.Equal(3, zip.Entries.Count(e => e.FullName.StartsWith("gaps/", StringComparison.Ordinal)));
    }

    [Fact]
    public void Schemas_are_not_vacuous()
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "characters", "srd51-quickfoot.json")))!;
        node["baseAbilities"]!["dex"] = 40;
        node["rulesFamily"] = "5e";

        using var document = JsonDocument.Parse(node.ToJsonString());
        var errors = Validate("character", document.RootElement);

        Assert.Contains("/baseAbilities/dex", errors, StringComparison.Ordinal);
        Assert.Contains("/rulesFamily", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("characters", "character")]
    [InlineData("content", "content")]
    public void Package_entry_with_a_newer_schema_version_is_refused_with_a_clear_diagnostic(string folder, string kind)
    {
        using var origin = new TempApp();
        var saved = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var package = origin.App.ExportCharacters([saved.Character.Id]).Content;
        var future = PackageEditor.Edit(package, path => path.StartsWith(folder + "/", StringComparison.Ordinal), node => node["schemaVersion"] = 99);

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(future);

        Assert.False(preview.CanApply);
        var errors = preview.Errors.Where(e => e.Code == "package.schema-unsupported").ToList();
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Contains($"{kind} schema v99", e.Message, StringComparison.Ordinal));
        Assert.All(errors, e => Assert.Contains("Update TomeStack", e.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public void A_package_content_entry_at_content_schema_v10_is_accepted_and_v11_is_refused(int version, bool refused)
    {
        // Content schema v10 (Wave 3, D42) is the current version; v11 is reserved for a later build and is refused here.
        using var origin = new TempApp();
        var saved = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var package = origin.App.ExportCharacters([saved.Character.Id]).Content;
        var edited = PackageEditor.Edit(package, path => path.StartsWith("content/", StringComparison.Ordinal), node => node["schemaVersion"] = version);

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(edited);

        var errors = preview.Errors.Where(e => e.Code == "package.schema-unsupported").ToList();
        Assert.Equal(refused, errors.Count > 0);
        if (refused)
            Assert.All(errors, e => Assert.Contains("content schema v11", e.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void The_store_keeps_a_v10_revision_typed_and_a_newer_one_is_isolated_by_the_calculator()
    {
        using var temp = new TempApp();
        var character = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        var source = temp.App.Store.ListSources()[0];
        var v10 = new ContentRevision
        {
            ContentId = Guid.Parse("c10a0000-0000-4000-8000-0000000000a1"), RevisionId = Guid.Parse("c10b0000-0000-4000-8000-0000000000a1"),
            Kind = ContentKind.Item, Name = "Fixture Charged Rod", RulesFamilies = [RulesFamilies.Srd51], Provenance = new(source.Id, new PageRef(1)),
            Status = RevisionStatus.Published,
            Effects =
            [
                new ItemEffect { Id = "rod", RequiresAttunement = true, Charges = new ItemCharges { Max = 4, Recovery = new ItemChargeRecovery { Period = RestPeriod.LongRest, Regain = "1d4" } } },
                new GrantEffect { Id = "ward", Grant = GrantKind.DamageResistance, DamageType = "Cold" },
            ],
        };
        Assert.Equal(10, v10.SchemaVersion);

        temp.App.Store.InTransaction(() => temp.App.Store.AddRevision(v10));
        var stored = temp.App.Store.FindRevision(v10.Reference)!;

        Assert.Equal(10, stored.SchemaVersion);
        Assert.Equal(v10.Effects[0], stored.Effects[0]);
        Assert.Equal("Cold", Assert.IsType<GrantEffect>(stored.Effects[1]).DamageType);
        Assert.Equal(Assert.IsType<ItemEffect>(v10.Effects[0]).Charges, Assert.IsType<ItemEffect>(stored.Effects[0]).Charges);

        var future = stored with { RevisionId = Guid.NewGuid(), SchemaVersion = ContentRevision.CurrentSchemaVersion + 1, Status = RevisionStatus.Published };
        var catalog = new InMemoryContentCatalog(temp.App.Store.ListSources(), [.. temp.App.Store.ListRevisions(), future]);
        var sheet = CharacterCalculator.Calculate(character with { Pins = [future.Reference] }, catalog);
        Assert.Equal("content.schema-unsupported", Assert.Single(sheet.Diagnostics).Code);
        Assert.Contains(ContentValidator.Validate(future, temp.App.Store).Errors, e => e.Code == "validate.schema-unsupported");
    }

    [Fact]
    public void Format_v1_package_with_v1_effects_still_imports_and_calculates_the_same()
    {
        using var origin = new TempApp();
        var saved = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var v2 = origin.App.ExportCharacters([saved.Character.Id]).Content;
        var v1 = PackageEditor.Edit(
            v2,
            path => path.StartsWith("content/", StringComparison.Ordinal),
            node =>
            {
                node["schemaVersion"] = 1;
                foreach (var effect in node["effects"]!.AsArray().ToList())
                {
                    var target = (string)effect!["target"]!;
                    var legacy = new JsonObject { ["id"] = (string)effect["id"]!, ["amount"] = int.Parse((string)effect["value"]!, System.Globalization.CultureInfo.InvariantCulture) };
                    if (target == FieldIds.Initiative)
                        legacy["type"] = "initiativeBonus";
                    else
                        (legacy["type"], legacy["ability"]) = ("abilityScoreIncrease", target.Split('.')[1]);
                    effect.ReplaceWith(legacy);
                }
            },
            manifest => manifest["formatVersion"] = 1);

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(v1);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        Assert.Equal(1, preview.Manifest!.FormatVersion);
        // The fixture revisions already exist locally (seeded, v2). v1 bytes upcast to the same revision: unchanged, not a conflict.
        Assert.All(preview.Items.Where(i => i.Kind == "contentRevision"), i => Assert.Equal(PackageItemAction.Unchanged, i.Action));
        destination.App.ApplyImport(v1);
        Assert.Equal(TempApp.Json(saved.Sheet), TempApp.Json(destination.App.GetCharacter(saved.Character.Id).Sheet));
    }

    [Fact]
    public void Newer_content_schema_is_isolated_by_the_calculator_and_newer_character_schema_fails_validation()
    {
        using var temp = new TempApp();
        var character = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        var future = temp.App.Store.ListRevisions().First(r => r.Name == "Fixture Keen Reflexes") with
        {
            RevisionId = Guid.NewGuid(),
            SchemaVersion = 99,
        };
        var catalog = new InMemoryContentCatalog(temp.App.Store.ListSources(), [.. temp.App.Store.ListRevisions(), future]);

        var sheet = CharacterCalculator.Calculate(character with { Pins = [future.Reference] }, catalog);

        Assert.Equal("content.schema-unsupported", Assert.Single(sheet.Diagnostics).Code);
        Assert.Contains((character with { SchemaVersion = 99 }).Validate(), d => d.Code == "character.schema-unsupported");
    }
}

/// <summary>Rewrites package entries and re-signs the manifest so that only the edited content differs.</summary>
internal static class PackageEditor
{
    public static byte[] Edit(byte[] package, Func<string, bool> select, Action<JsonNode> change, Action<JsonNode>? changeManifest = null)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using (var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                files[entry.FullName] = buffer.ToArray();
            }
        }

        var manifest = JsonNode.Parse(files["manifest.json"])!;
        foreach (var path in files.Keys.Where(p => p != "manifest.json" && select(p)).ToList())
        {
            var node = JsonNode.Parse(files[path])!;
            change(node);
            files[path] = Encoding.UTF8.GetBytes(node.ToJsonString(RulesJson.Options) + "\n");
            var listed = manifest["entries"]!.AsArray().Single(e => (string?)e!["path"] == path)!;
            listed["sha256"] = Convert.ToHexStringLower(SHA256.HashData(files[path]));
            listed["size"] = files[path].LongLength;
        }
        changeManifest?.Invoke(manifest);
        files["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString(RulesJson.Options) + "\n");

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in files)
            {
                // PDFs stay stored, as TomeStack writes them (a restore refuses PDF entries that unpack to much more).
                var level = path.StartsWith("files/", StringComparison.Ordinal) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                using var stream = zip.CreateEntry(path, level).Open();
                stream.Write(bytes);
            }
        }
        return output.ToArray();
    }
}
