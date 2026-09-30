using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Exports;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M6 slice 4 (ADR-012, accepted): export adapters. The Foundry VTT dnd5e adapter (pinned to Foundry 14.367 with dnd5e
/// 6.0.5) and the neutral sheet JSON read only the sheet export model v1, filtered by purpose (ADR-007 item 11). Golden
/// files are original fixture characters only, so no SRD or third-party text is copied into the repository (SPEC Q-03).
/// Set TOMESTACK_WRITE_GOLDEN to the tests/AppService.Tests/Golden folder to rewrite them after a deliberate change.
/// </summary>
public class ExportAdapterTests
{
    private static readonly string GoldenFolder = Path.Combine(AppContext.BaseDirectory, "Golden");

    private static ContentReference Ref(string content, string revision) => new(Guid.Parse(content), Guid.Parse(revision));

    private static readonly ContentReference Arcanist = Ref("5f5dc000-0000-4000-8000-000000000001", "5f5de000-0000-4000-8000-000000000001");
    private static readonly ContentReference Chanter = Ref("5f5dc000-0000-4000-8000-000000000002", "5f5de000-0000-4000-8000-000000000002");
    private static readonly ContentReference Spark = Ref("5f5dc000-0000-4000-8000-000000000011", "5f5de000-0000-4000-8000-000000000011");
    private static readonly ContentReference FrostRing = Ref("5f5dc000-0000-4000-8000-000000000012", "5f5de000-0000-4000-8000-000000000012");
    private static readonly ContentReference Duelist = Ref("5f6dc000-0000-4000-8000-000000000011", "5f6de000-0000-4000-8000-000000000011");
    private static readonly ContentReference Longblade = Ref("5f6dc000-0000-4000-8000-000000000001", "5f6de000-0000-4000-8000-000000000001");
    private static readonly ContentReference IronHarness = Ref("5f4dc000-0000-4000-8000-000000000003", "5f4de000-0000-4000-8000-000000000013");
    private static readonly ContentReference KiteShield = Ref("5f4dc000-0000-4000-8000-000000000004", "5f4de000-0000-4000-8000-000000000014");
    private static readonly ContentReference Chronicler = Ref("5fc0c000-0000-4000-8000-000000000001", "5fc0e000-0000-4000-8000-000000000001");

    /// <summary>The golden characters: one per family, a multiclass caster, a weapon user in armor, and the Test Chronicler.</summary>
    public static TheoryData<string> Goldens() => ["fixture-2014", "fixture-2024", "multiclass-caster", "armored-duelist", "chronicler"];

    private static Character Golden(TempApp temp, string name) => name switch
    {
        "fixture-2014" => TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"),
        "fixture-2024" => TempApp.LoadFixture<Character>("characters/srd521-courier.json"),
        "multiclass-caster" => new Character
        {
            Id = Guid.Parse("6f4e0000-0000-4000-8000-000000000001"), Name = "Test Twin Caster", RulesFamily = RulesFamilies.Srd521, Level = 5,
            Classes = [new(Arcanist, 3), new(Chanter, 2)], BaseAbilities = new(8, 14, 12, 16, 10, 14),
            Spells = [new(Arcanist.ContentId, Spark), new(Arcanist.ContentId, FrostRing)],
        },
        "armored-duelist" => new Character
        {
            Id = Guid.Parse("6f4e0000-0000-4000-8000-000000000002"), Name = "Test Armored Duelist", RulesFamily = RulesFamilies.Srd51, Level = 3,
            Classes = [new(Duelist, 3)], BaseAbilities = new(16, 12, 14, 10, 10, 8),
            Equipment = [new(Longblade, Equipped: true), new(IronHarness, Equipped: true), new(KiteShield, Equipped: true)],
        },
        "chronicler" => ChroniclerOf(temp),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static Character ChroniclerOf(TempApp temp)
    {
        temp.AddPack("fixture-pack-m5-chronicler.json");
        return new Character
        {
            Id = Guid.Parse("6f4e0000-0000-4000-8000-000000000003"), Name = "Test Chronicler Five", RulesFamily = RulesFamilies.Srd521, Level = 5,
            Classes = [new(Chronicler, 5)], BaseAbilities = new(8, 12, 14, 16, 12, 10),
        };
    }

    private static string Export(TempApp temp, Guid characterId, string target, SheetPurpose purpose = SheetPurpose.Share)
    {
        var preview = temp.App.PreviewVttExport(characterId, target, purpose);
        return Encoding.UTF8.GetString(temp.App.VttExportOutput(preview.Token).Bytes);
    }

    [Theory]
    [MemberData(nameof(Goldens))]
    public void Each_golden_character_exports_the_same_Foundry_and_sheet_files_every_time(string name)
    {
        using var temp = new TempApp();
        var character = temp.App.SaveCharacter(Golden(temp, name)).Character;
        foreach (var (target, suffix) in new[] { (FoundryDnd5e.Target, FoundryDnd5e.FileSuffix), (SheetJson.Target, SheetJson.FileSuffix) })
        {
            var text = Export(temp, character.Id, target);
            Assert.Equal(text, Export(temp, character.Id, target)); // deterministic
            var golden = Path.Combine(GoldenFolder, name + suffix);
            if (Environment.GetEnvironmentVariable("TOMESTACK_WRITE_GOLDEN") is { Length: > 0 } folder)
                File.WriteAllText(Path.Combine(folder, name + suffix), text.ReplaceLineEndings("\n") + "\n");
            else
                Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n").TrimEnd('\n'), text.ReplaceLineEndings("\n"));

            // Fixture content only: no bundled SRD source contributes text to a file kept in the repository (SPEC Q-03).
            using var document = JsonDocument.Parse(text);
            Assert.DoesNotContain("System Reference Document", text, StringComparison.Ordinal);
            if (target == SheetJson.Target)
                Assert.Equal("", SchemaTests.Validate("sheet-export", document.RootElement));
            else
                Assert.Equal("", SchemaTests.ValidateFile($"export-foundry-dnd5e.{FoundryDnd5e.SystemVersion}.schema.json", document.RootElement));
        }
    }

    [Fact]
    public void The_Foundry_actor_maps_abilities_skills_hit_points_armor_class_classes_and_spells_to_the_pinned_data_model()
    {
        using var temp = new TempApp();
        var character = temp.App.SaveCharacter(Golden(temp, "multiclass-caster"));
        var sheet = character.Sheet;
        var actor = JsonNode.Parse(Export(temp, character.Character.Id, FoundryDnd5e.Target))!;
        Assert.Equal(("character", "dnd5e", FoundryDnd5e.SystemVersion, FoundryDnd5e.CoreVersion), (
            actor["type"]!.GetValue<string>(), actor["_stats"]!["systemId"]!.GetValue<string>(),
            actor["_stats"]!["systemVersion"]!.GetValue<string>(), actor["_stats"]!["coreVersion"]!.GetValue<string>()));
        Assert.Equal(sheet.Field(FieldIds.Score(Ability.Int)).Value, actor["system"]!["abilities"]!["int"]!["value"]!.GetValue<int>());
        Assert.Equal(sheet.Field(FieldIds.ArmorClass).Value, actor["system"]!["attributes"]!["ac"]!["flat"]!.GetValue<int>());
        Assert.Equal(sheet.HitPoints!.Maximum, actor["system"]!["attributes"]!["hp"]!["max"]!.GetValue<int>());
        Assert.Equal(18, actor["system"]!["skills"]!.AsObject().Count);
        var items = actor["items"]!.AsArray();
        Assert.Equal([3, 2], items.Where(i => i!["type"]!.GetValue<string>() == "class").Select(i => i!["system"]!["levels"]!.GetValue<int>()));
        Assert.Equal(["Fixture Frost Ring", "Fixture Spark"], items.Where(i => i!["type"]!.GetValue<string>() == "spell").Select(i => i!["name"]!.GetValue<string>()).Order());
        // Each spell carries its caster's ability and a casting method dnd5e can prepare and slot (review fix).
        Assert.All(items.Where(i => i!["type"]!.GetValue<string>() == "spell"), s =>
            Assert.Equal(("int", "spell"), (s!["system"]!["ability"]!.GetValue<string>(), s["system"]!["method"]!.GetValue<string>())));
        Assert.All(items, i => Assert.NotNull(i!["flags"]?["tomestack"]?["revisionId"])); // provenance ids only
        Assert.Contains("not affiliated with Foundry Gaming LLC", actor["system"]!["details"]!["biography"]!["value"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(FoundryDnd5e.Validate(actor.AsObject()));
    }

    [Fact]
    public void A_share_export_keeps_only_totals_from_homebrew_that_may_not_leave_and_personal_lets_out_your_own()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var mine = app.CreateHomebrewSource(new("Test Own Notes", [RulesFamilies.Srd521]));
        var draft = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = "Test Own Knack", RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(mine.Id), Status = RevisionStatus.Draft, Summary = "Original test text.",
            Effects = [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "3" }],
        };
        app.SaveDraft(draft);
        var knack = app.Publish(draft.Reference).Published;
        var courier = TempApp.LoadFixture<Character>("characters/srd521-courier.json");
        var character = app.SaveCharacter(courier with { Pins = [.. courier.Pins, knack] }).Character;

        var share = app.PreviewVttExport(character.Id, FoundryDnd5e.Target, SheetPurpose.Share);
        Assert.Contains(share.Dropped, d => d.Source == "Test Own Notes");
        var shared = Encoding.UTF8.GetString(app.VttExportOutput(share.Token).Bytes);
        Assert.DoesNotContain("Test Own Knack", shared, StringComparison.Ordinal);
        Assert.Contains(share.Differences, d => d.StartsWith("Initiative", StringComparison.Ordinal)); // the total keeps its effect

        var personal = app.PreviewVttExport(character.Id, FoundryDnd5e.Target, SheetPurpose.Personal);
        Assert.Contains(personal.Warnings, w => w.Code == "export.personal");
        var personalText = Encoding.UTF8.GetString(app.VttExportOutput(personal.Token).Bytes);
        Assert.Contains("Test Own Knack", personalText, StringComparison.Ordinal);
        Assert.Contains("Personal copy: includes your own homebrew. Do not share it.", personalText, StringComparison.Ordinal);
    }

    [Fact]
    public void No_path_user_name_attachment_id_or_gap_note_reaches_an_export_file()
    {
        const string sentinel = "SentinelExport9k";
        using var temp = new TempApp(folder: sentinel);
        var app = temp.App;
        app.ScanIdentity = () => ($"C:\\Users\\{sentinel}", sentinel);
        var pdfFolder = Path.GetFullPath(Path.Combine(temp.Directory, "..", "books"));
        Directory.CreateDirectory(pdfFolder);
        var pdf = Path.Combine(pdfFolder, "book.pdf");
        File.WriteAllBytes(pdf, Encoding.ASCII.GetBytes("%PDF-1.4\n% test\n%%EOF\n"));
        var book = app.CreateHomebrewSource(new("Test Book Notes", [RulesFamilies.Srd521]));
        var attachment = app.AttachPdfFile(book.Id, pdf, AttachmentMode.Linked);
        var character = app.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { Name = $"Test {sentinel} Hero" }).Character;
        app.AddGapNote(new(character.Id, new(GapTargetKind.Field, FieldId: FieldIds.Initiative), "Gap note text Zq7"));

        foreach (var target in TomeStackApp.ExportTargets)
        {
            foreach (var purpose in new[] { SheetPurpose.Share, SheetPurpose.Personal })
            {
                var text = Export(temp, character.Id, target, purpose);
                foreach (var forbidden in new[] { pdfFolder, pdf.Replace('\\', '/'), temp.Directory, attachment.AttachmentId.ToString("D"), "Gap note text Zq7" })
                    Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
            }
        }
        // The character's own name holds the sentinel, but not as a path segment, so the file is written.
        Assert.Contains(sentinel, Export(temp, character.Id, SheetJson.Target), StringComparison.Ordinal);
    }

    /// <summary>Your own homebrew feat with <paramref name="summary"/>, pinned to the 2024 fixture character.</summary>
    private static Guid WithOwnFeat(TempApp temp, string summary)
    {
        var mine = temp.App.CreateHomebrewSource(new("Test Own Notes", [RulesFamilies.Srd521]));
        var draft = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = "Test Own Knack", RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(mine.Id), Status = RevisionStatus.Draft, Summary = summary,
        };
        temp.App.SaveDraft(draft);
        var knack = temp.App.Publish(draft.Reference).Published;
        var courier = TempApp.LoadFixture<Character>("characters/srd521-courier.json");
        return temp.App.SaveCharacter(courier with { Pins = [.. courier.Pins, knack] }).Character.Id;
    }

    [Theory]
    [InlineData("Jos\u00e9 Tester")]
    [InlineData("O'Brien Tester")]
    public void A_path_or_user_name_that_HTML_encoding_would_change_is_still_refused(string userName)
    {
        // Review fix: the Foundry file writes é and ' as entities, so the scan also reads the unencoded sheet.
        using var temp = new TempApp();
        temp.App.ScanIdentity = () => ($"C:\\Users\\{userName}", userName);
        var id = WithOwnFeat(temp, $"Notes kept in C:\\Users\\{userName}\\Documents.");
        foreach (var target in TomeStackApp.ExportTargets)
        {
            var refused = Assert.Throws<AppValidationException>(() => temp.App.PreviewVttExport(id, target, SheetPurpose.Personal));
            Assert.Contains(refused.Problems, e => e.Code == "export.output-refused");
        }
    }

    [Fact]
    public void Foundry_enrichers_in_exported_text_are_left_inert()
    {
        using var temp = new TempApp();
        var id = WithOwnFeat(temp, "See @UUID[Actor.abc]{a friend} and roll [[/r 1d20]] or @Embed[Item.x].");
        var actor = JsonNode.Parse(Export(temp, id, FoundryDnd5e.Target, SheetPurpose.Personal))!;
        var html = string.Concat(actor["items"]!.AsArray().Select(i => i!["system"]!["description"]?["value"]?.GetValue<string>()));
        foreach (var enricher in new[] { "@UUID[", "@Embed[", "[[/r" })
            Assert.DoesNotContain(enricher, html, StringComparison.Ordinal);
        Assert.Contains("@\u2060UUID[Actor.abc]", html, StringComparison.Ordinal); // still readable
    }

    [Fact]
    public void Recoveries_classes_hit_dice_and_pact_slots_map_only_what_dnd5e_can_hold()
    {
        // Review fixes: recoverAll only for "all", whole numbers as a formula, the rest named as lost.
        Assert.Equal("""{"period":"sr","type":"recoverAll"}""", FoundryDnd5e.Recovery("ShortRest: all")!.ToJsonString());
        Assert.Equal("""{"formula":"1","period":"lr","type":"formula"}""", FoundryDnd5e.Recovery("LongRest: 1")!.ToJsonString());
        Assert.Null(FoundryDnd5e.Recovery("ShortRest: floor(SCALE.ink / 2)"));
        Assert.Equal((1, 2, 3, 6), (FoundryDnd5e.ProficiencyBonus(0), FoundryDnd5e.ProficiencyBonus(1), FoundryDnd5e.ProficiencyBonus(5), FoundryDnd5e.ProficiencyBonus(20)));

        using var temp = new TempApp();
        var character = temp.App.SaveCharacter(Golden(temp, "armored-duelist")).Character;
        var sheet = temp.App.SheetExportFor(character.Id, SheetPurpose.Share);
        var only = sheet.Character.Classes.Single();
        // Two classes whose names give one identifier, one without an accepted hit die, and pact slots of level 3.
        var twin = sheet with
        {
            Character = sheet.Character with { Classes = [only, only with { Name = only.Name + "!", Level = 1, HitDie = null, Subclass = "Test Branch" }] },
            PactSlots = new(3, 2, 0),
        };
        var actor = FoundryDnd5e.Map(twin);
        Assert.Empty(FoundryDnd5e.Validate(actor));
        var items = actor["items"]!.AsArray();
        var classes = items.Where(i => i!["type"]!.GetValue<string>() == "class").Select(i => i!["system"]!).ToList();
        Assert.Equal(2, classes.Select(c => c["identifier"]!.GetValue<string>()).Distinct().Count());
        Assert.Equal(classes[1]["identifier"]!.GetValue<string>(), items.Single(i => i!["type"]!.GetValue<string>() == "subclass")!["system"]!["classIdentifier"]!.GetValue<string>());
        Assert.Equal(0, classes[1]["hd"]!["spent"]!.GetValue<int>());
        var losses = FoundryDnd5e.Losses(twin).ToList();
        Assert.Contains(losses, l => l.StartsWith("Pact Magic slots: level 3", StringComparison.Ordinal));
        Assert.Contains(losses, l => l.Contains("no hit die", StringComparison.Ordinal));

        // A duplicate identifier is refused by the validator.
        classes[1]["identifier"] = classes[0]["identifier"]!.GetValue<string>();
        Assert.Contains("every class has its own identifier", FoundryDnd5e.Validate(actor));
    }

    [Fact]
    public void The_Foundry_preview_compares_with_the_proficiency_bonus_Foundry_works_out()
    {
        // Review fix: a character with no class levels in the file gets Foundry's +1, not TomeStack's +2.
        using var temp = new TempApp();
        var character = temp.App.SaveCharacter(Golden(temp, "fixture-2014")).Character;
        var preview = temp.App.PreviewVttExport(character.Id, FoundryDnd5e.Target, SheetPurpose.Share);
        Assert.Contains(preview.Differences, d => d.StartsWith("Proficiency bonus: TomeStack +2; Foundry works out +1", StringComparison.Ordinal));
    }

    [Fact]
    public void The_validators_refuse_output_the_target_would_not_read()
    {
        var actor = JsonNode.Parse("""{"type":"character","name":"x","_stats":{"systemId":"dnd5e","systemVersion":"6.0.5"},"system":{"abilities":{},"attributes":{"hp":{"max":1,"value":1},"ac":{"flat":10}},"skills":{"xyz":{"value":3}},"spells":{"spell10":{"value":1}}},"items":[{"type":"script","name":"x"}]}""")!.AsObject();
        var problems = FoundryDnd5e.Validate(actor);
        Assert.Contains(problems, p => p.StartsWith("abilities.str", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("skills.xyz", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("spells.spell10", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("class, subclass, feat, spell or weapon", StringComparison.Ordinal));
        Assert.Contains("a value has the wrong type", FoundryDnd5e.Validate(JsonNode.Parse("""{"type":7}""")!.AsObject()));
        Assert.NotEmpty(SheetJson.Validate(JsonNode.Parse("""{"format":"other"}""")!));
    }

    [Fact]
    public void The_dispatcher_previews_saves_and_downloads_an_export_once()
    {
        using var temp = new TempApp();
        var character = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json")).Character;
        var dispatcher = new CommandDispatcher(temp.App);
        string Send(string command, object payload) => dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact));

        using var preview = JsonDocument.Parse(Send("export.preview", new { characterId = character.Id, target = "foundry-dnd5e" }));
        var result = preview.RootElement.GetProperty("result");
        Assert.EndsWith(".foundry-dnd5e.json", result.GetProperty("fileName").GetString(), StringComparison.Ordinal);
        Assert.Contains("dnd5e 6.0.5", result.GetProperty("targetVersion").GetString(), StringComparison.Ordinal);
        var token = result.GetProperty("token").GetGuid();
        Assert.Contains("\"host.unsupported\"", Send("export.saveAs", new { token }), StringComparison.Ordinal);
        Assert.Contains("\"base64\"", Send("export.download", new { token }), StringComparison.Ordinal);
        Assert.Contains("\"export.expired\"", Send("export.download", new { token }), StringComparison.Ordinal);
        Assert.Contains("\"export.target-unknown\"", Send("export.preview", new { characterId = character.Id, target = "roll20" }), StringComparison.Ordinal);
    }
}
