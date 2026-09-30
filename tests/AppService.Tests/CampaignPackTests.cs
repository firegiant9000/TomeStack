using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M6 slice 2 (ROADMAP "M6 plan", B13): campaign packs. One campaign profile travels with the shareable content of its
/// allowed sources; the slice 1 guard decides what is shareable; SRD sources are referenced, never copied; everything else
/// is named in omitted[] and becomes a pending reference on the receiving machine. All content is original test data.
/// </summary>
public class CampaignPackTests
{
    private static readonly Guid Srd521Source = Guid.Parse("52500000-0000-4000-8000-000000000001");
    private static readonly Guid FixtureShared = Guid.Parse("5f0d5000-0000-4000-8000-000000000001");
    private static readonly Guid Fixture2024 = Guid.Parse("5f0d5210-0000-4000-8000-000000000001");
    private static readonly Guid EquipmentFixtures = Guid.Parse("5f4d5000-0000-4000-8000-000000000001");
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% TomeStack test PDF\n%%EOF\n");

    private static IReadOnlyList<string> PackCodes(Action action) => [.. Assert.Throws<PackageException>(action).Errors.Select(e => e.Code)];

    /// <summary>A homebrew source with one published feat; marked as shareable when <paramref name="shareable"/>.</summary>
    private static SourceRecord Homebrew(TempApp temp, string title, bool shareable = true, bool withContent = true)
    {
        var source = temp.App.CreateHomebrewSource(new(title, [RulesFamilies.Srd521], Redistributable: shareable, ConfirmOwnWork: shareable));
        if (withContent)
        {
            var draft = new ContentRevision
            {
                ContentId = Guid.NewGuid(),
                RevisionId = Guid.NewGuid(),
                Kind = ContentKind.Feat,
                Name = $"{title} Feat",
                RulesFamilies = [RulesFamilies.Srd521],
                Provenance = new(source.Id),
                Status = RevisionStatus.Draft,
                Effects = [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" }],
            };
            temp.App.SaveDraft(draft);
            temp.App.Publish(draft.Reference);
        }
        return temp.App.Store.FindSource(source.Id)!;
    }

    private static Campaign Campaign(TempApp temp, string name, params Guid[] sources) =>
        temp.App.SaveCampaign(new() { Id = Guid.Empty, Name = name, RulesFamily = RulesFamilies.Srd521, AllowedSources = sources, HouseRules = "Test house rule: no flanking." });

    private static JsonDocument Entry(byte[] package, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return JsonDocument.Parse(new StreamReader(zip.GetEntry(path)!.Open()).ReadToEnd());
    }

    [Fact]
    public void A_campaign_pack_round_trips_into_a_clean_data_folder_and_compares_equal()
    {
        using var origin = new TempApp();
        var shared = Homebrew(origin, "Test Harbor Notes");
        var campaign = Campaign(origin, "Test Harbor Table", Srd521Source, shared.Id);

        var preview = origin.App.PreviewCampaignPack(campaign.Id);
        Assert.Equal((shared.Id, Srd521Source, 0, 1), (Assert.Single(preview.Included).SourceId, Assert.Single(preview.Referenced).SourceId, preview.LeftOut.Count, preview.Revisions));
        Assert.Equal("Test-Harbor-Table-campaign-pack.tomestack.zip", preview.FileName);
        var pack = origin.App.ExportCampaignPack(campaign.Id);
        var manifest = pack.Manifest;
        Assert.Equal((PackageScope.Campaign, ExportPurpose.Share, PackageManifest.CampaignFormatVersion), (manifest.Scope, manifest.Purpose, manifest.FormatVersion));
        Assert.Equal((0, 0), (manifest.Characters.Count, manifest.Omitted.Count));
        Assert.Equal(shared.Id, Assert.Single(manifest.Attestations!).SourceId);
        Assert.DoesNotContain(manifest.Entries, e => e.Path.Contains(Srd521Source.ToString("D"), StringComparison.Ordinal)); // referenced, never copied

        using var destination = new TempApp();
        var importPreview = destination.App.PreviewImport(pack.Content);
        Assert.True(importPreview.CanApply, string.Join("; ", importPreview.Errors.Select(e => e.Code)));
        Assert.Contains(importPreview.Items, i => i.Kind == "campaign" && i.Action == PackageItemAction.Add);
        var result = destination.App.ApplyImport(pack.Content);
        Assert.EndsWith(".db", result.BackupFile, StringComparison.Ordinal); // the pre-import copy of the database
        Assert.True(File.Exists(Path.Combine(destination.Directory, result.BackupFile!)));

        Assert.Equal(TempApp.Json(campaign), TempApp.Json(Assert.Single(destination.App.ListCampaigns())));
        foreach (var revision in origin.App.Store.ListRevisionsInOrder().Where(r => r.Provenance.SourceId == shared.Id && r.Status == RevisionStatus.Published))
            Assert.Equal(TempApp.Json(revision), TempApp.Json(destination.App.Store.FindRevision(revision.Reference)));
        Assert.Equal(SourceOrigin.Received, destination.App.Store.FindSource(shared.Id)!.Origin);

        var again = destination.App.ApplyImport(pack.Content);
        Assert.Equal((0, (string?)null), (again.Added, again.BackupFile));
    }

    [Fact]
    public void The_pack_carries_only_what_the_guard_allows_and_names_the_rest_in_omitted()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var shared = Homebrew(temp, "Test Harbor Notes");
        var unmarked = Homebrew(temp, "Test Private Notes", shareable: false);
        var derived = Homebrew(temp, "Test Scanned Notes");
        app.AttachPdf(derived.Id, "scan.pdf", Pdf);
        var empty = Homebrew(temp, "Test Empty Notes", withContent: false);
        var campaign = Campaign(temp, "Test Mixed Table", Srd521Source, shared.Id, unmarked.Id, derived.Id, empty.Id, FixtureShared);
        // A character and a gap note in the campaign never travel with it.
        var character = app.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { CampaignId = campaign.Id });

        var preview = app.PreviewCampaignPack(campaign.Id);
        Assert.Equal(shared.Id, Assert.Single(preview.Included).SourceId);
        Assert.Equal(
            new Dictionary<Guid, string>
            {
                [unmarked.Id] = "pack.source-not-shareable",
                [derived.Id] = "pack.source-import-derived",
                [empty.Id] = "pack.source-empty",
                [FixtureShared] = "pack.source-not-shareable",
            },
            preview.LeftOut.ToDictionary(l => l.SourceId, l => l.Reason.Code));

        var pack = app.ExportCampaignPack(campaign.Id);
        Assert.Equal(preview.LeftOut.Select(l => l.SourceId).Order(), pack.Manifest.Omitted.Select(o => o.SourceId).Order());
        Assert.All(pack.Manifest.Omitted, o => Assert.Empty(o.Revisions));
        using (var zip = new ZipArchive(new MemoryStream(pack.Content), ZipArchiveMode.Read))
        {
            Assert.All(zip.Entries, e => Assert.Matches("^(manifest\\.json|(sources|content|campaigns)/[0-9a-f-]{36}\\.json)$", e.FullName));
            Assert.Equal([$"sources/{shared.Id:D}.json"], zip.Entries.Where(e => e.FullName.StartsWith("sources/", StringComparison.Ordinal)).Select(e => e.FullName));
            Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains(character.Character.Id.ToString("D"), StringComparison.Ordinal));
        }
        using var manifest = Entry(pack.Content, "manifest.json");
        Assert.Equal("", SchemaTests.Validate("package-manifest", manifest.RootElement));
        using var profile = Entry(pack.Content, $"campaigns/{campaign.Id:D}.json");
        Assert.Equal("", SchemaTests.Validate("campaign", profile.RootElement));
        Assert.False(profile.RootElement.TryGetProperty("pendingSources", out _));
        using var record = Entry(pack.Content, $"sources/{shared.Id:D}.json");
        foreach (var field in new[] { "importDerived", "origin", "shareConfirmedAt", "attachmentId", "pdfRef", "sha256" })
            Assert.False(record.RootElement.TryGetProperty(field, out _), field);

        Assert.Contains("pack.campaign-missing", PackCodes(() => app.PreviewCampaignPack(Guid.NewGuid())));
    }

    [Fact]
    public void An_omitted_allowed_source_is_imported_as_pending_until_it_is_installed()
    {
        using var origin = new TempApp();
        var shared = Homebrew(origin, "Test Harbor Notes");
        var later = Homebrew(origin, "Test Tide Notes", shareable: false);
        var campaign = Campaign(origin, "Test Tide Table", Srd521Source, shared.Id, later.Id);
        var pack = origin.App.ExportCampaignPack(campaign.Id);

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(pack.Content);
        var warning = Assert.Single(preview.Warnings, w => w.Code == "campaign.source-pending");
        Assert.Contains("Test Tide Notes", warning.Message, StringComparison.Ordinal);
        destination.App.ApplyImport(pack.Content);

        var stored = Assert.Single(destination.App.ListCampaigns());
        Assert.Contains(later.Id, stored.AllowedSources);
        var pending = Assert.Single(stored.PendingSources!);
        Assert.Equal((later.Id, "Test Tide Notes", later.Publisher, later.License), (pending.SourceId, pending.Title, pending.Publisher, pending.License));
        using (var document = JsonDocument.Parse(TempApp.Json(stored)))
            Assert.Equal("", SchemaTests.Validate("campaign", document.RootElement));

        // The campaign can still be saved while the source is missing, but only with the pending entry it already had.
        Assert.NotNull(destination.App.SaveCampaign(stored with { Name = "Test Tide Table, renamed" }).PendingSources);
        var invented = Guid.NewGuid();
        Assert.Contains(
            Assert.Throws<AppValidationException>(() => destination.App.SaveCampaign(stored with
            {
                AllowedSources = [.. stored.AllowedSources, invented],
                PendingSources = [.. stored.PendingSources!, new PendingSource(invented, "Invented", "Nobody", "None")],
            })).Problems,
            p => p.Code == "campaign.source-missing");

        // Once the author shares it and it is installed, it is simply allowed.
        origin.App.SetShareable(new(later.Id, Shareable: true, ConfirmOwnWork: true));
        destination.App.ApplyImport(origin.ExportPack(later.Id));
        var resaved = destination.App.SaveCampaign(Assert.Single(destination.App.ListCampaigns()));
        Assert.Null(resaved.PendingSources);
        Assert.True(destination.App.ListContent(RulesFamilies.Srd521, resaved.Id).Single(o => o.Name == "Test Tide Notes Feat").AllowedInCampaign);
    }

    [Fact]
    public void A_campaign_that_differs_needs_a_choice_and_the_preview_lists_characters_that_would_lose_content()
    {
        using var origin = new TempApp();
        var shared = Homebrew(origin, "Test Harbor Notes");
        var campaign = Campaign(origin, "Test Harbor Table", Srd521Source, shared.Id);
        var pack = origin.App.ExportCampaignPack(campaign.Id);

        using var destination = new TempApp();
        var app = destination.App;
        app.ApplyImport(pack.Content);
        // Here the table also allows the fixture sources, and a character uses them.
        var local = app.SaveCampaign(Assert.Single(app.ListCampaigns()) with { AllowedSources = [Srd521Source, shared.Id, Fixture2024, FixtureShared, EquipmentFixtures] });
        var courier = app.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { CampaignId = local.Id });
        Assert.DoesNotContain(courier.Campaign!.Warnings, w => w.Code == "campaign.source-not-allowed");

        var preview = app.PreviewImport(pack.Content);
        var item = Assert.Single(preview.Items, i => i.Kind == "campaign");
        Assert.Equal(PackageItemAction.Replace, item.Action);
        Assert.Equal("allowedSources", Assert.Single(item.Changes!).Field);
        Assert.Contains(preview.Warnings, w => w.Code == "package.campaign-differs");
        Assert.All(preview.Items.Where(i => i.Kind != "campaign"), i => Assert.True(i.Action == PackageItemAction.Unchanged, $"{i.Kind} {i.Name} {i.Action} {string.Join(",", i.Changes?.Select(c => $"{c.Field}:{c.Local}->{c.Imported}") ?? [])}"));
        var impact = Assert.Single(preview.CampaignImpact!);
        Assert.Equal((courier.Character.Id, courier.Character.Name), (impact.CharacterId, impact.CharacterName));
        Assert.NotEmpty(impact.NotAllowed);

        Assert.Contains("package.campaign-choice-required", PackCodes(() => app.ApplyImport(pack.Content)));

        var kept = app.ApplyImport(pack.Content, campaignChoices: new Dictionary<Guid, SourceChoice> { [campaign.Id] = SourceChoice.KeepLocal });
        Assert.Equal(TempApp.Json(local), TempApp.Json(Assert.Single(app.ListCampaigns())));
        Assert.Null(kept.BackupFile); // nothing changed, so nothing was copied

        var replaced = app.ApplyImport(pack.Content, campaignChoices: new Dictionary<Guid, SourceChoice> { [campaign.Id] = SourceChoice.UseImported });
        Assert.Equal(1, replaced.Replaced);
        Assert.EndsWith(".db", replaced.BackupFile, StringComparison.Ordinal);
        Assert.Equal([Srd521Source, shared.Id], Assert.Single(app.ListCampaigns()).AllowedSources);
        Assert.Contains(app.GetCharacter(courier.Character.Id).Campaign!.Warnings, w => w.Code == "campaign.source-not-allowed");
    }

    [Fact]
    public void A_character_package_that_replaces_a_campaign_copies_the_database_first()
    {
        using var origin = new TempApp();
        var campaign = Campaign(origin, "Test Travel Table", Srd521Source, Fixture2024, FixtureShared, EquipmentFixtures);
        var first = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { CampaignId = campaign.Id });
        var second = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { Id = Guid.NewGuid(), Name = "Test Second Courier", CampaignId = campaign.Id });
        var firstPackage = origin.App.ExportCharacters([first.Character.Id]).Content;

        using var destination = new TempApp();
        destination.App.ApplyImport(firstPackage);
        destination.App.SaveCampaign(Assert.Single(destination.App.ListCampaigns()) with { Name = "Test Travel Table, renamed here" });

        // A new character whose campaign differs here: no character is replaced, so the database copy is the backup.
        var secondResult = destination.App.ApplyImport(origin.App.ExportCharacters([second.Character.Id]).Content);
        Assert.EndsWith(".db", secondResult.BackupFile, StringComparison.Ordinal);
        Assert.Null(secondResult.DatabaseCopy);

        // A replaced character and a replaced campaign: the character backup and a database copy.
        destination.App.SaveCampaign(Assert.Single(destination.App.ListCampaigns()) with { Name = "Test Travel Table, renamed again" });
        var firstResult = destination.App.ApplyImport(firstPackage);
        Assert.EndsWith(".tomestack.zip", firstResult.BackupFile, StringComparison.Ordinal);
        Assert.EndsWith(".db", firstResult.DatabaseCopy, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(destination.Directory, firstResult.DatabaseCopy!)));
    }

    [Fact]
    public void Hostile_campaign_packs_are_refused()
    {
        using var origin = new TempApp();
        var shared = Homebrew(origin, "Test Harbor Notes");
        var other = Homebrew(origin, "Test Private Notes", shareable: false);
        var campaign = Campaign(origin, "Test Harbor Table", Srd521Source, shared.Id, other.Id);
        var pack = origin.App.ExportCampaignPack(campaign.Id).Content;
        var campaignPath = $"campaigns/{campaign.Id:D}.json";
        using var destination = new TempApp();
        IReadOnlyList<string> Codes(byte[] package) => [.. destination.App.PreviewImport(package).Errors.Select(e => e.Code)];

        Assert.Empty(Codes(pack));
        var secondCampaign = campaign with { Id = Guid.NewGuid(), Name = "Test Second Table" };
        Assert.Contains("pack.campaign-count", Codes(SourcePackTests.AddEntry(pack, $"campaigns/{secondCampaign.Id:D}.json", "campaign", secondCampaign)));
        var character = TempApp.LoadFixture<Character>("characters/srd521-courier.json");
        Assert.Contains("package.entry-not-allowed", Codes(SourcePackTests.AddEntry(pack, $"characters/{character.Id:D}.json", "character", character)));
        Assert.Contains("pack.campaign-source-unlisted", Codes(PackageEditor.Edit(pack, p => p == campaignPath, c => c["allowedSources"]!.AsArray().Add(Guid.NewGuid().ToString("D")))));
        Assert.Contains("pack.source-not-allowed", Codes(PackageEditor.Edit(pack, p => p == campaignPath, c => c["allowedSources"] = new JsonArray(Srd521Source.ToString("D"), other.Id.ToString("D")))));
        Assert.Contains("pack.source-not-shareable", Codes(PackageEditor.Edit(pack, p => p.StartsWith("sources/", StringComparison.Ordinal), s => s["importDerived"] = true)));
        Assert.Contains("pack.draft-not-allowed", Codes(PackageEditor.Edit(pack, p => p.StartsWith("content/", StringComparison.Ordinal), r => r["status"] = "draft")));
        Assert.Contains("pack.attestation-missing", Codes(PackageEditor.Edit(pack, _ => false, _ => { }, m => m["attestations"] = new JsonArray())));
        Assert.Contains("package.invalid-json", Codes(PackageEditor.Edit(pack, _ => false, _ => { }, m => m["formatVersion"] = 7)));
        Assert.Contains("package.invalid-json", Codes(PackageEditor.Edit(pack, _ => false, _ => { }, m => m["purpose"] = "backup")));
        // A pending entry in the file is not trusted: the receiver works out what is pending from omitted[].
        var withPending = PackageEditor.Edit(pack, p => p == campaignPath, c => c["pendingSources"] = new JsonArray(new JsonObject
        {
            ["sourceId"] = shared.Id.ToString("D"), ["title"] = "Claimed", ["publisher"] = "x", ["license"] = "x",
        }));
        Assert.Empty(Codes(withPending));
        destination.App.ApplyImport(withPending);
        Assert.Equal("Test Private Notes", Assert.Single(Assert.Single(destination.App.ListCampaigns()).PendingSources!).Title);
    }

    [Fact]
    public void Names_the_pack_cannot_store_are_clamped_so_the_campaign_and_later_backups_stay_valid()
    {
        using var origin = new TempApp();
        var shared = Homebrew(origin, "Test Harbor Notes");
        var longPublisher = origin.App.CreateHomebrewSource(new("Test Verbose Notes", [RulesFamilies.Srd521], Publisher: new string('p', 250)));
        var campaign = Campaign(origin, "Test Verbose Table", Srd521Source, shared.Id, longPublisher.Id);

        // The exporter names the source as the receiver will store it.
        var pack = origin.App.ExportCampaignPack(campaign.Id).Content;
        Assert.Equal(AppService.Campaign.MaxNameLength, Assert.Single(origin.App.ExportCampaignPack(campaign.Id).Manifest.Omitted).Publisher.Length);
        // A hand-edited pack with an empty title and an oversized license still stores a valid campaign.
        var edited = PackageEditor.Edit(pack, _ => false, _ => { }, m =>
        {
            var named = m["omitted"]![0]!;
            named["title"] = " ";
            named["license"] = new string('l', 500);
        });

        using var destination = new TempApp();
        Assert.True(destination.App.PreviewImport(edited).CanApply);
        destination.App.ApplyImport(edited);
        var stored = Assert.Single(destination.App.ListCampaigns());
        Assert.Empty(stored.Validate());
        var pending = Assert.Single(stored.PendingSources!);
        Assert.Equal(($"Source {longPublisher.Id}", AppService.Campaign.MaxNameLength), (pending.Title, pending.License.Length));
        destination.App.SaveCampaign(stored with { HouseRules = "Test house rule: edited here." });

        // And the library backup holding it restores into a clean data folder.
        var backup = Path.Combine(destination.Directory, "test-backup.tomestack.zip");
        using (var file = File.Create(backup))
            destination.App.WriteLibraryBackup(file);
        using var clean = new TempApp();
        var restore = clean.App.PreviewLibraryRestore(backup);
        Assert.True(restore.CanApply, string.Join("; ", restore.Errors.Select(e => e.Code)));
    }

    [Fact]
    public void A_damaged_or_planted_pending_list_is_dropped_on_read_never_refused()
    {
        using var origin = new TempApp();
        var shared = Homebrew(origin, "Test Harbor Notes");
        var campaign = Campaign(origin, "Test Harbor Table", Srd521Source, shared.Id);
        var campaignPath = $"campaigns/{campaign.Id:D}.json";
        var outsider = Guid.NewGuid().ToString("D");
        var planted = new JsonArray(
            new JsonObject { ["sourceId"] = outsider, ["title"] = "Not allowed", ["publisher"] = "x", ["license"] = "x" },
            new JsonObject { ["sourceId"] = outsider, ["title"] = "Repeated", ["publisher"] = "x", ["license"] = "x" });

        using var destination = new TempApp();
        // In a campaign pack it is ignored; in a character package it is cleaned.
        Assert.Empty(destination.App.PreviewImport(PackageEditor.Edit(origin.App.ExportCampaignPack(campaign.Id).Content, p => p == campaignPath, c => c["pendingSources"] = planted.DeepClone())).Errors);
        var character = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { CampaignId = campaign.Id });
        var package = PackageEditor.Edit(origin.App.ExportCharacters([character.Character.Id]).Content, p => p == campaignPath, c => c["pendingSources"] = planted.DeepClone());
        Assert.Empty(destination.App.PreviewImport(package).Errors);
        destination.App.ApplyImport(package);
        Assert.Null(Assert.Single(destination.App.ListCampaigns()).PendingSources);
    }

    [Fact]
    public void A_source_whose_content_spans_another_is_left_out_not_the_whole_pack()
    {
        using var temp = new TempApp();
        var shared = Homebrew(temp, "Test Harbor Notes");
        var other = Homebrew(temp, "Test Other Notes");
        var split = temp.App.Store.ListRevisionsInOrder().First(r => r.Provenance.SourceId == shared.Id && r.Status == RevisionStatus.Published);
        temp.App.Store.AddRevision(split with { RevisionId = Guid.NewGuid(), Provenance = new(other.Id) });
        var clean = Homebrew(temp, "Test Clean Notes");
        var campaign = Campaign(temp, "Test Split Table", Srd521Source, shared.Id, clean.Id);

        var preview = temp.App.PreviewCampaignPack(campaign.Id);
        Assert.Equal(clean.Id, Assert.Single(preview.Included).SourceId);
        Assert.Equal("pack.content-spans-sources", Assert.Single(preview.LeftOut).Reason.Code);
    }

    [Fact]
    public void Pending_entries_are_this_machines_record_and_survive_a_character_package_or_an_unchanged_pack()
    {
        using var origin = new TempApp();
        var shared = Homebrew(origin, "Test Harbor Notes");
        var later = Homebrew(origin, "Test Tide Notes", shareable: false);
        var campaign = Campaign(origin, "Test Tide Table", Srd521Source, shared.Id, later.Id, Fixture2024, FixtureShared, EquipmentFixtures);
        var pack = origin.App.ExportCampaignPack(campaign.Id).Content;
        var character = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { CampaignId = campaign.Id });
        // The origin has every source, so its campaign has no pending entry.
        var characterPackage = origin.App.ExportCharacters([character.Character.Id], ExportPurpose.Share).Content;

        // A campaign pack, then a character package with the same profile: the pending entry stays, and the campaign saves.
        using var player = new TempApp();
        player.App.ApplyImport(pack);
        player.App.ApplyImport(characterPackage);
        var afterShare = Assert.Single(player.App.ListCampaigns());
        Assert.Equal(later.Id, Assert.Single(afterShare.PendingSources!).SourceId);
        player.App.SaveCampaign(afterShare);

        // A character package first (no pending entry), then the same profile as a pack: the pack's name is recorded.
        using var other = new TempApp();
        other.App.ApplyImport(characterPackage);
        Assert.Null(Assert.Single(other.App.ListCampaigns()).PendingSources);
        var result = other.App.ApplyImport(pack);
        Assert.Equal(0, result.Replaced);
        Assert.Equal("Test Tide Notes", Assert.Single(Assert.Single(other.App.ListCampaigns()).PendingSources!).Title);
    }

    [Fact]
    public void A_character_share_writes_its_campaign_as_a_campaign_pack_does_and_a_backup_keeps_it_whole()
    {
        // M6 stack review: a share wrote the stored campaign, pending list and unknown properties included, which a
        // campaign pack leaves out on purpose.
        using var origin = new TempApp();
        var shared = Homebrew(origin, "Test Harbor Notes");
        var later = Homebrew(origin, "Test Tide Notes", shareable: false);
        var campaign = Campaign(origin, "Test Tide Table", Srd521Source, shared.Id, later.Id, Fixture2024, FixtureShared, EquipmentFixtures);
        using var player = new TempApp();
        player.App.ApplyImport(origin.App.ExportCampaignPack(campaign.Id).Content);
        var received = Assert.Single(player.App.ListCampaigns());
        Assert.NotNull(received.PendingSources);
        using (var planted = JsonDocument.Parse("\"Test planted value\""))
            player.App.Store.SaveCampaign(received with { Extensions = new() { ["testPlanted"] = planted.RootElement.Clone() } });
        var character = player.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { CampaignId = campaign.Id });

        JsonElement CampaignIn(byte[] package)
        {
            using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
            using var document = JsonDocument.Parse(zip.GetEntry($"campaigns/{campaign.Id:D}.json")!.Open());
            return document.RootElement.Clone();
        }
        var share = CampaignIn(player.App.ExportCharacters([character.Character.Id], ExportPurpose.Share).Content);
        Assert.False(share.TryGetProperty("pendingSources", out _));
        Assert.False(share.TryGetProperty("testPlanted", out _));
        var backup = CampaignIn(player.App.ExportCharacters([character.Character.Id]).Content);
        Assert.True(backup.TryGetProperty("pendingSources", out _));
        Assert.True(backup.TryGetProperty("testPlanted", out _));
    }

    [Fact]
    public void The_dispatcher_offers_campaign_pack_commands()
    {
        using var temp = new TempApp();
        var shared = Homebrew(temp, "Test Harbor Notes");
        var campaign = Campaign(temp, "Test Harbor Table", Srd521Source, shared.Id);
        var dispatcher = new CommandDispatcher(temp.App);
        string Send(string command, object payload) => dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact));

        Assert.Contains("\"included\"", Send("package.campaignPackPreview", new { campaignId = campaign.Id }), StringComparison.Ordinal);
        using var exported = JsonDocument.Parse(Send("package.campaignPackExport", new { campaignId = campaign.Id }));
        var base64 = exported.RootElement.GetProperty("result").GetProperty("base64").GetString()!;
        Assert.Contains("\"host.unsupported\"", Send("package.campaignPackSaveAs", new { campaignId = campaign.Id }), StringComparison.Ordinal);
        Assert.Contains("\"pack.campaign-missing\"", Send("package.campaignPackPreview", new { campaignId = Guid.NewGuid() }), StringComparison.Ordinal);

        temp.App.SaveCampaign(campaign with { Name = "Test Harbor Table, renamed" });
        Assert.Contains("\"package.campaign-choice-required\"", Send("package.apply", new { base64 }), StringComparison.Ordinal);
        Assert.Contains("\"ok\":true", Send("package.apply", new { base64, campaignChoices = new Dictionary<Guid, string> { [campaign.Id] = "useImported" } }), StringComparison.Ordinal);
        Assert.Equal("Test Harbor Table", Assert.Single(temp.App.ListCampaigns()).Name);
    }
}
