using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TomeStack.AppService.Packages;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M6 slice 1 (ROADMAP "M6 plan"; LIVING_SPECS D14 item 6): source packs, "Mark as shareable" and the durable
/// import-derived flag. The guard runs at every export and every import path; a source pack round-trips into a clean data
/// folder. All content is original test data.
/// </summary>
public class SourcePackTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% TomeStack test PDF\n%%EOF\n");

    private static string Code(Action action) => Assert.Throws<AppValidationException>(action).Problems[0].Code;

    private static IReadOnlyList<string> PackCodes(Action action) => [.. Assert.Throws<PackageException>(action).Errors.Select(e => e.Code)];

    /// <summary>A homebrew source marked as shareable when it was made (the author confirmed it is their own work).</summary>
    private static SourceRecord Shared(TempApp temp, string title = "Test Stormwright Notes") =>
        temp.App.CreateHomebrewSource(new(title, [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));

    private static ContentRevision Feat(Guid sourceId, string name, Guid contentId, Guid revisionId, int bonus = 1) => new()
    {
        ContentId = contentId,
        RevisionId = revisionId,
        Kind = ContentKind.Feat,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(sourceId),
        Status = RevisionStatus.Draft,
        Effects = [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = bonus.ToString(System.Globalization.CultureInfo.InvariantCulture) }],
    };

    /// <summary>Saves and publishes; returns the published reference.</summary>
    private static ContentReference Publish(TempApp temp, ContentRevision draft)
    {
        temp.App.SaveDraft(draft);
        return temp.App.Publish(draft.Reference).Published;
    }

    private static readonly Guid FeatContent = Guid.Parse("6e510000-0000-4000-8000-000000000001");

    // Stored in this order; the second sorts first by id, so only the pack's revision order keeps it the newest.
    private static readonly Guid FirstRevision = Guid.Parse("6e51e000-0000-4000-8000-0000000000f1");
    private static readonly Guid SecondRevision = Guid.Parse("6e51e000-0000-4000-8000-000000000001");

    /// <summary>A shared source with two published revisions of one feat (stored with fixed ids) and one draft.</summary>
    private static (SourceRecord Source, ContentReference First, ContentReference Second) SharedWithTwoRevisions(TempApp temp)
    {
        var source = Shared(temp);
        var first = Feat(source.Id, "Test Storm Step", FeatContent, FirstRevision, bonus: 1) with { Status = RevisionStatus.Published, SchemaVersion = 3 };
        var second = Feat(source.Id, "Test Storm Step", FeatContent, SecondRevision, bonus: 2) with { Status = RevisionStatus.Published, SchemaVersion = 3 };
        temp.App.Store.AddRevision(first);
        temp.App.Store.AddRevision(second);
        temp.App.SaveDraft(Feat(source.Id, "Test Unfinished Idea", Guid.Parse("6e510000-0000-4000-8000-000000000002"), Guid.Parse("6e51d000-0000-4000-8000-000000000002")));
        return (source, first.Reference, second.Reference);
    }

    [Fact]
    public void A_source_pack_round_trips_into_a_clean_data_folder_with_its_published_revisions_in_stored_order()
    {
        using var origin = new TempApp();
        var (source, first, second) = SharedWithTwoRevisions(origin);
        Assert.True(string.CompareOrdinal(second.RevisionId.ToString(), first.RevisionId.ToString()) < 0); // id order is not stored order

        var preview = origin.App.PreviewSourcePack([source.Id]);
        Assert.Equal((2, 1), (preview.Revisions, preview.Drafts));
        var pack = origin.App.ExportSourcePack([source.Id]);
        Assert.Equal((PackageScope.Source, ExportPurpose.Share, PackageManifest.SourceFormatVersion), (pack.Manifest.Scope, pack.Manifest.Purpose, pack.Manifest.FormatVersion));
        Assert.Equal([first.RevisionId, second.RevisionId], pack.Manifest.RevisionOrder);
        Assert.Equal(source.Id, Assert.Single(pack.Manifest.Attestations!).SourceId);

        using var destination = new TempApp();
        var importPreview = destination.App.PreviewImport(pack.Content);
        Assert.True(importPreview.CanApply, string.Join("; ", importPreview.Errors.Select(e => e.Code)));
        var result = destination.App.ApplyImport(pack.Content);

        // The pre-import backup is a copy of the database.
        Assert.NotNull(result.BackupFile);
        Assert.EndsWith(".db", result.BackupFile, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(destination.Directory, result.BackupFile!)));

        // Published revisions compare equal, the newest is the same one, and no draft came along.
        foreach (var reference in new[] { first, second })
            Assert.Equal(TempApp.Json(origin.App.Store.FindRevision(reference)), TempApp.Json(destination.App.Store.FindRevision(reference)));
        Assert.Equal(second, destination.App.Store.ListRevisions(FeatContent)[^1].Reference);
        Assert.DoesNotContain(destination.App.Store.ListRevisionsInOrder(), r => r.Provenance.SourceId == source.Id && r.Status == RevisionStatus.Draft);

        // The receiver records the source as received: it may travel on in a character share, never as their own work.
        var received = destination.App.Store.FindSource(source.Id)!;
        Assert.Equal((SourceOrigin.Received, (DateTimeOffset?)null, true), (received.Origin, received.ShareConfirmedAt, received.Redistributable));
        Assert.Equal((source.Title, source.Publisher, source.License), (received.Title, received.Publisher, received.License));
        Assert.Equal("source.received", Code(() => destination.App.SetShareable(new(source.Id, Shareable: true, ConfirmOwnWork: true))));
        Assert.Contains("pack.source-received", PackCodes(() => destination.App.ExportSourcePack([source.Id])));

        // Importing it again changes nothing and takes no backup.
        var again = destination.App.ApplyImport(pack.Content);
        Assert.Equal((0, null), (again.Added, again.BackupFile));
    }

    [Fact]
    public void A_source_pack_carries_no_machine_local_field_and_matches_the_v7_schemas()
    {
        using var origin = new TempApp();
        var (source, _, _) = SharedWithTwoRevisions(origin);
        var pack = origin.App.ExportSourcePack([source.Id]);

        using var zip = new ZipArchive(new MemoryStream(pack.Content), ZipArchiveMode.Read);
        Assert.All(zip.Entries, e => Assert.Matches("^(manifest\\.json|(sources|content)/[0-9a-f-]{36}\\.json)$", e.FullName));
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        Assert.Equal("", SchemaTests.Validate("package-manifest", manifest.RootElement));
        using var record = JsonDocument.Parse(zip.GetEntry($"sources/{source.Id:D}.json")!.Open());
        foreach (var field in new[] { "importDerived", "origin", "shareConfirmedAt", "attachmentId", "pdfRef", "sha256" })
            Assert.False(record.RootElement.TryGetProperty(field, out _), field);
        Assert.Equal("", SchemaTests.Validate("source", record.RootElement)); // v1: nothing only this machine sets
    }

    [Fact]
    public void The_guard_refuses_every_source_that_is_not_the_authors_own_shareable_work()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var unmarked = app.CreateHomebrewSource(new("Test Private Notes", [RulesFamilies.Srd51]));
        Publish(temp, Feat(unmarked.Id, "Test Quiet Step", Guid.NewGuid(), Guid.NewGuid()));
        var derived = Shared(temp, "Test Book Notes");
        Publish(temp, Feat(derived.Id, "Test Borrowed Step", Guid.NewGuid(), Guid.NewGuid()));
        app.AttachPdf(derived.Id, "book.pdf", Pdf);
        var empty = Shared(temp, "Test Empty Notes");
        var bundled = app.ListSources().First(s => s.EditionVersion != "homebrew");

        Assert.Contains("pack.source-not-shareable", PackCodes(() => app.ExportSourcePack([unmarked.Id])));
        Assert.Contains("pack.source-import-derived", PackCodes(() => app.ExportSourcePack([derived.Id])));
        Assert.Contains("pack.source-empty", PackCodes(() => app.ExportSourcePack([empty.Id])));
        Assert.Contains("pack.source-bundled", PackCodes(() => app.ExportSourcePack([bundled.Id])));
        Assert.Contains("pack.source-missing", PackCodes(() => app.ExportSourcePack([Guid.NewGuid()])));
        Assert.Contains("pack.sources-required", PackCodes(() => app.ExportSourcePack([])));
        Assert.Contains("pack.source-not-shareable", PackCodes(() => app.PreviewSourcePack([unmarked.Id]))); // the preview refuses too

        // A content with revisions in two sources cannot leave with one of them.
        var shared = Shared(temp, "Test Shared Notes");
        var content = Guid.NewGuid();
        Publish(temp, Feat(shared.Id, "Test Split Step", content, Guid.NewGuid()));
        Publish(temp, Feat(unmarked.Id, "Test Split Step", content, Guid.NewGuid()));
        Assert.Contains("pack.content-spans-sources", PackCodes(() => app.ExportSourcePack([shared.Id])));
    }

    [Fact]
    public void Mark_as_shareable_needs_the_authors_confirmation_and_is_refused_for_bundled_and_import_derived_sources()
    {
        using var temp = new TempApp();
        var app = temp.App;
        Assert.Equal("source.confirm-own-work", Code(() => app.CreateHomebrewSource(new("Test Notes", [RulesFamilies.Srd51], Redistributable: true))));

        var source = app.CreateHomebrewSource(new("Test Notes", [RulesFamilies.Srd51]));
        Assert.Equal((SourceOrigin.Local, false, (DateTimeOffset?)null), (source.Origin, source.Redistributable, source.ShareConfirmedAt));
        Assert.Equal("source.confirm-own-work", Code(() => app.SetShareable(new(source.Id, Shareable: true))));

        var marked = app.SetShareable(new(source.Id, Shareable: true, ConfirmOwnWork: true));
        Assert.Equal((true, (DateTimeOffset?)TempApp.Now), (marked.Redistributable, marked.ShareConfirmedAt));
        var stopped = app.SetShareable(new(source.Id, Shareable: false));
        Assert.Equal((false, (DateTimeOffset?)null), (stopped.Redistributable, stopped.ShareConfirmedAt));

        var bundled = app.ListSources().First(s => s.EditionVersion != "homebrew");
        Assert.Equal("source.bundled", Code(() => app.SetShareable(new(bundled.Id, Shareable: true, ConfirmOwnWork: true))));
        app.AttachPdf(source.Id, "book.pdf", Pdf);
        Assert.Equal("source.import-derived", Code(() => app.SetShareable(new(source.Id, Shareable: true, ConfirmOwnWork: true))));
    }

    [Fact]
    public void Attaching_a_PDF_marks_a_source_for_good_turns_sharing_off_and_keeps_it_out_of_a_character_share()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = Shared(temp);
        var feat = Publish(temp, Feat(source.Id, "Test Storm Step", Guid.NewGuid(), Guid.NewGuid()));
        var hero = app.SaveCharacter(HeroPinning(feat));
        Assert.Empty(app.PreviewExport([hero.Character.Id], ExportPurpose.Share).Omitted); // shareable before

        app.AttachPdf(source.Id, "book.pdf", Pdf);
        var derived = app.Store.FindSource(source.Id)!;
        Assert.Equal((true, false, (DateTimeOffset?)null), (derived.ImportDerived, derived.Redistributable, derived.ShareConfirmedAt));

        app.Detach(source.Id, confirm: true);
        Assert.True(app.Store.FindSource(source.Id)!.ImportDerived); // never cleared by a detach
        Assert.Equal(source.Id, Assert.Single(app.PreviewExport([hero.Character.Id], ExportPurpose.Share).Omitted).SourceId);

        // Even a caller that writes the source back without the flag cannot lower it (the store and its column hold it).
        app.Store.UpsertSource(derived with { ImportDerived = null, Redistributable = true });
        Assert.Equal((true, false), (app.Store.FindSource(source.Id)!.ImportDerived, app.Store.FindSource(source.Id)!.Redistributable));
        temp.Reopen();
        Assert.True(temp.App.Store.FindSource(source.Id)!.ImportDerived);

        // A bundled SRD source may have a PDF attached for its page links; its content is this build's own, so it is exempt.
        var srd = temp.App.ListSources().First(s => s.EditionVersion != "homebrew");
        temp.App.AttachPdf(srd.Id, "srd.pdf", Pdf);
        Assert.Null(temp.App.Store.FindSource(srd.Id)!.ImportDerived);
    }

    [Fact]
    public void Importing_pages_and_accepting_a_candidate_mark_the_source_too()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Review Book", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        app.AttachPdf(source.Id, "fixture-import.pdf", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf")));
        var job = app.StartImport(new(source.Id, WholeDocument: true));
        app.RunningImport?.Wait(TimeSpan.FromSeconds(30));

        // As if the flag had been lost (a folder from before it existed, rewritten by hand): each import path sets it again.
        void Clear() => Execute(temp, $"UPDATE sources SET import_derived = 0, json = json_remove(json, '$.importDerived') WHERE id = '{source.Id:D}';");
        Clear();
        Assert.Null(app.Store.FindSource(source.Id)!.ImportDerived);
        var candidate = app.ListCandidates(new(job.Id)).First(c => app.CheckCandidate(c.Id).CanAcceptAsReference);
        app.AcceptCandidate(new(candidate.Id, AsReference: true, Confirm: true));
        Assert.True(app.Store.FindSource(source.Id)!.ImportDerived);

        Clear();
        app.ImportPages(new(source.Id, 1, 2));
        Assert.True(app.Store.FindSource(source.Id)!.ImportDerived);
    }

    [Fact]
    public void A_full_backup_carries_the_flag_origin_and_confirmation_and_a_restore_keeps_them()
    {
        using var origin = new TempApp();
        var mine = Shared(origin, "Test Own Notes");
        Publish(origin, Feat(mine.Id, "Test Own Step", Guid.NewGuid(), Guid.NewGuid()));
        var book = origin.App.CreateHomebrewSource(new("Test Book Notes", [RulesFamilies.Srd51]));
        origin.App.AttachPdf(book.Id, "book.pdf", Pdf);
        origin.App.Detach(book.Id, confirm: true);
        var file = Path.Combine(origin.Directory, "full.tomestack.zip");
        using (var stream = File.Create(file))
            origin.App.WriteLibraryBackup(stream);

        using var destination = new TempApp();
        var preview = destination.App.PreviewLibraryRestore(file);
        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Code)));
        Assert.Equal(PackageManifest.LibraryFormatVersion, preview.Manifest!.FormatVersion);
        destination.App.ApplyLibraryRestore(file);

        var restoredMine = destination.App.Store.FindSource(mine.Id)!;
        Assert.Equal((SourceOrigin.Local, (DateTimeOffset?)TempApp.Now, true), (restoredMine.Origin, restoredMine.ShareConfirmedAt, restoredMine.Redistributable));
        destination.App.ExportSourcePack([mine.Id]); // still yours, still shareable
        var restoredBook = destination.App.Store.FindSource(book.Id)!;
        Assert.Equal((true, false), (restoredBook.ImportDerived, restoredBook.Redistributable));
    }

    [Fact]
    public void A_backup_written_before_the_flag_marks_every_source_that_comes_back_with_a_PDF()
    {
        using var origin = new TempApp();
        var book = origin.App.CreateHomebrewSource(new("Test Book Notes", [RulesFamilies.Srd51]));
        origin.App.AttachPdf(book.Id, "book.pdf", Pdf);
        var file = Path.Combine(origin.Directory, "full.tomestack.zip");
        using (var stream = File.Create(file))
            origin.App.WriteLibraryBackup(stream);
        // As a pre-M6 build wrote it: format v6, and the source without the M6 fields.
        var old = PackageEditor.Edit(
            File.ReadAllBytes(file),
            path => path.StartsWith("sources/", StringComparison.Ordinal),
            source => { source.AsObject().Remove("importDerived"); source.AsObject().Remove("origin"); source["redistributable"] = true; },
            manifest => manifest["formatVersion"] = 6);
        var oldFile = Path.Combine(origin.Directory, "old.tomestack.zip");
        File.WriteAllBytes(oldFile, old);

        using var destination = new TempApp();
        destination.App.ApplyLibraryRestore(oldFile);
        var restored = destination.App.Store.FindSource(book.Id)!;
        Assert.Equal((true, false), (restored.ImportDerived, restored.Redistributable));
    }

    [Fact]
    public void No_package_import_lowers_the_flag_raises_redistributable_or_takes_over_a_bundled_source()
    {
        var sourceId = Guid.Parse("6e5a0000-0000-4000-8000-000000000001");
        var feat = Feat(sourceId, "Test Borrowed Step", Guid.NewGuid(), Guid.NewGuid()) with { Status = RevisionStatus.Published };
        SourceRecord Record(string title, bool redistributable) => new()
        {
            Id = sourceId, Title = title, Publisher = "Test author", RulesFamilies = [RulesFamilies.Srd51],
            EditionVersion = "homebrew", License = "Personal homebrew", Redistributable = redistributable,
        };

        using var sender = new TempApp();
        sender.App.Store.UpsertSource(Record("Test Notes (sender)", redistributable: true));
        sender.App.Store.AddRevision(feat);
        var hero = sender.App.SaveCharacter(HeroPinning(feat.Reference));
        var backup = sender.App.ExportCharacters([hero.Character.Id]).Content;

        // A local import-derived source with the same id (stored before v8, origin unknown; a source made here would refuse the
        // new revision, package.own-source): "use imported" replaces its title, never its flag or origin.
        using var flagged = new TempApp();
        flagged.App.Store.UpsertSource(Record("Test Notes (mine)", redistributable: false) with { ImportDerived = true });
        flagged.App.ApplyImport(backup, new Dictionary<Guid, SourceChoice> { [sourceId] = SourceChoice.UseImported });
        var kept = flagged.App.Store.FindSource(sourceId)!;
        Assert.Equal(("Test Notes (sender)", true, false, (SourceOrigin?)null), (kept.Title, kept.ImportDerived, kept.Redistributable, kept.Origin));

        // A local source that is not shared: an import never raises its redistributable flag.
        using var unshared = new TempApp();
        unshared.App.Store.UpsertSource(Record("Test Notes (mine)", redistributable: false));
        unshared.App.ApplyImport(backup, new Dictionary<Guid, SourceChoice> { [sourceId] = SourceChoice.UseImported });
        Assert.False(unshared.App.Store.FindSource(sourceId)!.Redistributable);

        // A new source from a character package is received, and cannot be marked as the receiver's own work.
        using var clean = new TempApp();
        clean.App.ApplyImport(backup);
        Assert.Equal(SourceOrigin.Received, clean.App.Store.FindSource(sourceId)!.Origin);
        Assert.Equal("source.received", Code(() => clean.App.SetShareable(new(sourceId, Shareable: true, ConfirmOwnWork: true))));

        // A package that carries a different record for a bundled SRD source leaves this build's record alone.
        var srd = clean.App.ListSources().First(s => s.EditionVersion != "homebrew");
        var forged = PackageEditor.Edit(backup, _ => false, _ => { });
        forged = AddEntry(forged, $"sources/{srd.Id:D}.json", "source", srd with { Attribution = "Forged attribution" });
        var preview = clean.App.PreviewImport(forged);
        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Code)));
        Assert.Contains(preview.Warnings, w => w.Code == "package.bundled-source-kept");
        clean.App.ApplyImport(forged);
        Assert.Equal(TempApp.Json(srd), TempApp.Json(clean.App.Store.FindSource(srd.Id)));
    }

    [Fact]
    public void A_character_package_leaves_out_the_machine_local_source_fields_and_stays_v5()
    {
        using var temp = new TempApp();
        var source = Shared(temp);
        var feat = Publish(temp, Feat(source.Id, "Test Storm Step", Guid.NewGuid(), Guid.NewGuid()));
        var hero = temp.App.SaveCharacter(HeroPinning(feat));
        var package = temp.App.ExportCharacters([hero.Character.Id], ExportPurpose.Share);

        Assert.Equal(PackageManifest.CharacterFormatVersion, package.Manifest.FormatVersion);
        using var zip = new ZipArchive(new MemoryStream(package.Content), ZipArchiveMode.Read);
        using var record = JsonDocument.Parse(zip.GetEntry($"sources/{source.Id:D}.json")!.Open());
        Assert.False(record.RootElement.TryGetProperty("origin", out _));
        Assert.False(record.RootElement.TryGetProperty("shareConfirmedAt", out _));
    }

    public static TheoryData<string, string> HostilePacks() => new()
    {
        { "character", "package.entry-not-allowed" },
        { "draft", "pack.draft-not-allowed" },
        { "not-shareable", "pack.source-not-shareable" },
        { "import-derived", "pack.source-not-shareable" },
        { "foreign-revision", "pack.revision-source" },
        { "no-attestation", "pack.attestation-missing" },
        { "no-order", "package.invalid-json" },
        { "bundled", "pack.source-bundled" },
        { "not-a-share", "package.invalid-json" },
        { "split-content", "pack.content-conflict" },
    };

    private static readonly Guid SecondSource = Guid.Parse("6e5b0000-0000-4000-8000-000000000002");
    private static readonly Guid SplitRevision = Guid.Parse("6e5be000-0000-4000-8000-000000000002");

    private static readonly SourceRecord SecondSourceRecord = new()
    {
        Id = SecondSource, Title = "Test Second Notes", Publisher = "Test author", RulesFamilies = [RulesFamilies.Srd51],
        EditionVersion = "homebrew", License = "Personal homebrew", Redistributable = true,
    };

    [Theory]
    [MemberData(nameof(HostilePacks))]
    public void A_hostile_source_pack_is_refused_before_anything_is_written(string attack, string code)
    {
        using var origin = new TempApp();
        var (source, first, _) = SharedWithTwoRevisions(origin);
        var pack = origin.App.ExportSourcePack([source.Id]).Content;
        var srd = origin.App.ListSources().First(s => s.EditionVersion != "homebrew");
        var firstPath = $"content/{first.RevisionId:D}.json";
        var sourcePath = $"sources/{source.Id:D}.json";

        var hostile = attack switch
        {
            "character" => AddEntry(pack, $"characters/{Guid.NewGuid():D}.json", "character", TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json")),
            "draft" => PackageEditor.Edit(pack, p => p == firstPath, r => r["status"] = "draft"),
            "not-shareable" => PackageEditor.Edit(pack, p => p == sourcePath, s => s["redistributable"] = false),
            "import-derived" => PackageEditor.Edit(pack, p => p == sourcePath, s => s["importDerived"] = true), // still "redistributable"
            "split-content" => SplitContent(pack),
            "foreign-revision" => PackageEditor.Edit(pack, p => p == firstPath, r => r["provenance"]!["sourceId"] = srd.Id.ToString("D")),
            "no-attestation" => PackageEditor.Edit(pack, _ => false, _ => { }, m => m["attestations"] = new JsonArray()),
            "no-order" => PackageEditor.Edit(pack, _ => false, _ => { }, m => m.AsObject().Remove("revisionOrder")),
            "bundled" => AddEntry(pack, $"sources/{srd.Id:D}.json", "source", srd),
            "not-a-share" => PackageEditor.Edit(pack, _ => false, _ => { }, m => m["purpose"] = "backup"),
            _ => throw new ArgumentOutOfRangeException(nameof(attack)),
        };

        using var destination = new TempApp();
        var before = destination.App.Store.ListRevisionsInOrder().Count;
        var preview = destination.App.PreviewImport(hostile);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.Code == code);
        Assert.Throws<PackageException>(() => destination.App.ApplyImport(hostile));
        Assert.Equal(before, destination.App.Store.ListRevisionsInOrder().Count);
        Assert.Null(destination.App.Store.FindSource(source.Id));
    }

    [Fact]
    public void A_pack_may_not_add_content_to_your_own_source_or_to_content_that_belongs_to_another_source()
    {
        using var origin = new TempApp();
        var (source, _, _) = SharedWithTwoRevisions(origin);
        var pack = origin.App.ExportSourcePack([source.Id]).Content;

        // Your own pack imported back where it was made: nothing new, so nothing is refused.
        Assert.True(origin.App.PreviewImport(pack).CanApply);
        // A pack that adds a revision to a source you made here is refused.
        var extra = Feat(source.Id, "Test Slipped In", Guid.NewGuid(), Guid.NewGuid()) with { Status = RevisionStatus.Published };
        var added = AddEntry(pack, $"content/{extra.RevisionId:D}.json", "contentRevision", extra, order: extra.RevisionId);
        Assert.Contains(origin.App.PreviewImport(added).Errors, e => e.Code == "package.own-source");

        // A pack whose content id already belongs to another source here is refused.
        using var destination = new TempApp();
        var other = destination.App.CreateHomebrewSource(new("Test Other Notes", [RulesFamilies.Srd51]));
        Publish(destination, Feat(other.Id, "Test Storm Step", FeatContent, Guid.NewGuid()));
        Assert.Contains(destination.App.PreviewImport(pack).Errors, e => e.Code == "pack.content-conflict");
    }

    [Fact]
    public void A_source_from_before_v8_keeps_its_unknown_origin_when_your_own_backup_is_imported_again()
    {
        var sourceId = Guid.Parse("6e5c0000-0000-4000-8000-000000000001");
        var feat = Feat(sourceId, "Test Old Step", Guid.NewGuid(), Guid.NewGuid()) with { Status = RevisionStatus.Published, SchemaVersion = 3 };
        using var temp = new TempApp();
        // As stored before database v8: no origin, redistributable, and marked as shareable since.
        temp.App.Store.UpsertSource(new SourceRecord
        {
            Id = sourceId, Title = "Test Old Notes", Publisher = "Personal homebrew", RulesFamilies = [RulesFamilies.Srd51],
            EditionVersion = "homebrew", License = "Personal homebrew", Redistributable = true, ShareConfirmedAt = TempApp.Now,
        });
        temp.App.Store.AddRevision(feat);
        var hero = temp.App.SaveCharacter(HeroPinning(feat.Reference));
        var backup = temp.App.ExportCharacters([hero.Character.Id]).Content;

        temp.App.ApplyImport(backup);
        var kept = temp.App.Store.FindSource(sourceId)!;
        Assert.Equal(((SourceOrigin?)null, (DateTimeOffset?)TempApp.Now), (kept.Origin, kept.ShareConfirmedAt)); // not relabelled "received"
        temp.App.ExportSourcePack([sourceId]);

        // New content arriving for it from a package withdraws the share confirmation (it may not be yours).
        var extra = Feat(sourceId, "Test Slipped In", Guid.NewGuid(), Guid.NewGuid()) with { Status = RevisionStatus.Published, SchemaVersion = 3 };
        var withExtra = AddEntry(backup, $"content/{extra.RevisionId:D}.json", "contentRevision", extra);
        var preview = temp.App.PreviewImport(withExtra);
        Assert.Contains(preview.Warnings, w => w.Code == "package.source-unconfirmed");
        temp.App.ApplyImport(withExtra);
        Assert.Null(temp.App.Store.FindSource(sourceId)!.ShareConfirmedAt);
        Assert.Contains("pack.source-not-shareable", PackCodes(() => temp.App.ExportSourcePack([sourceId])));
    }

    [Fact]
    public void No_package_adds_content_to_a_source_you_made_here_or_to_a_bundled_SRD_source()
    {
        using var temp = new TempApp();
        var mine = Shared(temp);
        var feat = Publish(temp, Feat(mine.Id, "Test Storm Step", Guid.NewGuid(), Guid.NewGuid()));
        var hero = temp.App.SaveCharacter(HeroPinning(feat));
        var backup = temp.App.ExportCharacters([hero.Character.Id]).Content;

        // A character package (not only a source pack) that adds a revision to your own source is refused.
        var intruder = Feat(mine.Id, "Test Intruder", Guid.NewGuid(), Guid.NewGuid()) with { Status = RevisionStatus.Published, SchemaVersion = 3 };
        var adding = AddEntry(backup, $"content/{intruder.RevisionId:D}.json", "contentRevision", intruder);
        Assert.Contains(temp.App.PreviewImport(adding).Errors, e => e.Code == "package.own-source");

        // New content under a bundled SRD source id is refused on import and when saving a draft.
        var srd = temp.App.ListSources().First(s => s.EditionVersion != "homebrew");
        var posing = Feat(srd.Id, "Test Posing As SRD", Guid.NewGuid(), Guid.NewGuid()) with { Status = RevisionStatus.Published, SchemaVersion = 3 };
        var smuggled = AddEntry(backup, $"content/{posing.RevisionId:D}.json", "contentRevision", posing);
        using var clean = new TempApp();
        Assert.Contains(clean.App.PreviewImport(smuggled).Errors, e => e.Code == "package.bundled-source-content");
        Assert.Equal("content.source-not-editable", Code(() => temp.App.SaveDraft(posing with { Status = RevisionStatus.Draft })));
    }

    [Fact]
    public void A_pack_whose_newest_revision_is_already_here_warns_that_an_older_one_becomes_the_newest()
    {
        using var origin = new TempApp();
        var (source, _, second) = SharedWithTwoRevisions(origin);
        var pack = origin.App.ExportSourcePack([source.Id]).Content;

        using var destination = new TempApp();
        // The receiver already has the newest revision (for example from a character share) and its received source.
        destination.App.Store.UpsertSource(origin.App.Store.FindSource(source.Id)! with { Origin = SourceOrigin.Received, ShareConfirmedAt = null });
        destination.App.Store.AddRevision(origin.App.Store.FindRevision(second)!);
        Assert.Contains(destination.App.PreviewImport(pack).Warnings, w => w.Code == "pack.newest-changes");
    }

    [Fact]
    public void A_full_restore_gives_a_bundled_SRD_source_its_PDF_back()
    {
        using var origin = new TempApp();
        var srd = origin.App.ListSources().First(s => s.EditionVersion != "homebrew");
        origin.App.AttachPdf(srd.Id, "srd.pdf", Pdf);
        var file = Path.Combine(origin.Directory, "full.tomestack.zip");
        using (var stream = File.Create(file))
            origin.App.WriteLibraryBackup(stream);

        using var destination = new TempApp();
        destination.App.ApplyLibraryRestore(file);
        Assert.Equal("available", destination.App.GetAttachment(srd.Id)?.Status);
        Assert.Null(destination.App.Store.FindSource(srd.Id)!.ImportDerived);
    }

    [Fact]
    public void Library_backup_sources_match_the_source_v2_schema()
    {
        using var temp = new TempApp();
        var mine = Shared(temp, "Test Own Notes");
        var book = temp.App.CreateHomebrewSource(new("Test Book Notes", [RulesFamilies.Srd51]));
        temp.App.AttachPdf(book.Id, "book.pdf", Pdf);
        using var buffer = new MemoryStream();
        temp.App.WriteLibraryBackup(buffer);

        using var zip = new ZipArchive(new MemoryStream(buffer.ToArray()), ZipArchiveMode.Read);
        foreach (var id in new[] { mine.Id, book.Id })
        {
            using var record = JsonDocument.Parse(zip.GetEntry($"sources/{id:D}.json")!.Open());
            Assert.True(record.RootElement.TryGetProperty("origin", out _));
            Assert.Equal("", SchemaTests.Validate("source", record.RootElement));
        }
    }

    [Fact]
    public void A_package_from_a_newer_TomeStack_is_refused_as_newer_even_with_a_scope_this_build_does_not_know()
    {
        using var origin = new TempApp();
        var (source, _, _) = SharedWithTwoRevisions(origin);
        var newer = PackageEditor.Edit(origin.ExportPack(source.Id), _ => false, _ => { }, m => { m["formatVersion"] = PackageManifest.CurrentFormatVersion + 1; m["scope"] = "campaign"; });

        using var destination = new TempApp();
        var preview = destination.App.PreviewImport(newer);
        Assert.Equal("package.unsupported-format", Assert.Single(preview.Errors).Code);
    }

    [Fact]
    public void Database_v8_marks_existing_sources_that_show_an_import_and_never_the_bundled_ones()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
            using (new SqliteStore(database, SqliteStore.Migrations.Take(7).ToArray())) { }
            SourceRecord Homebrew(Guid id, string title) => new()
            {
                Id = id, Title = title, Publisher = "Test author", RulesFamilies = [RulesFamilies.Srd51],
                EditionVersion = "homebrew", License = "Personal homebrew", Redistributable = true,
            };
            var attached = Guid.NewGuid();
            var legacy = Guid.NewGuid();
            var imported = Guid.NewGuid();
            var clean = Guid.NewGuid();
            var srd = TomeStackApp.BundledSourceIds.First();
            using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                connection.Open();
                void Insert(SourceRecord source, string? attachment, string? legacyRef)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "INSERT INTO sources (id, json, attachment_id, legacy_pdf_ref) VALUES ($id, $json, $a, $l);";
                    command.Parameters.AddWithValue("$id", source.Id.ToString("D"));
                    command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(source, RulesJson.Compact));
                    command.Parameters.AddWithValue("$a", (object?)attachment ?? DBNull.Value);
                    command.Parameters.AddWithValue("$l", (object?)legacyRef ?? DBNull.Value);
                    command.ExecuteNonQuery();
                }
                Insert(Homebrew(attached, "Test Attached"), Guid.NewGuid().ToString("D"), null);
                Insert(Homebrew(legacy, "Test Legacy"), null, @"C:\books\old.pdf");
                Insert(Homebrew(imported, "Test Imported"), null, null);
                Insert(Homebrew(clean, "Test Clean"), null, null);
                Insert(Homebrew(srd, "Test SRD record") with { EditionVersion = "5.1" }, Guid.NewGuid().ToString("D"), null);
                using var job = connection.CreateCommand();
                var record = new ImportJobRecord { Id = Guid.NewGuid(), SourceId = imported, Sha256 = new string('0', 64), Status = ImportJobStatus.Completed };
                job.CommandText = "INSERT INTO import_jobs (id, source_id, status, json) VALUES ($id, $source, 'Completed', $json);";
                job.Parameters.AddWithValue("$id", record.Id.ToString("D"));
                job.Parameters.AddWithValue("$source", imported.ToString("D"));
                job.Parameters.AddWithValue("$json", JsonSerializer.Serialize(record, RulesJson.Compact));
                job.ExecuteNonQuery();
            }

            using (var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: []))
            {
                Assert.Equal(SqliteStore.LatestSchemaVersion, app.GetInfo().SchemaVersion);
                foreach (var id in new[] { attached, legacy, imported })
                    Assert.Equal((true, false), (app.Store.FindSource(id)!.ImportDerived, app.Store.FindSource(id)!.Redistributable));
                Assert.Equal(((bool?)null, true), (app.Store.FindSource(clean)!.ImportDerived, app.Store.FindSource(clean)!.Redistributable));
                Assert.Null(app.Store.FindSource(srd)!.ImportDerived);
                Assert.Null(app.Store.FindSource(clean)!.Origin); // not known for a source stored before v8
            }
            Assert.True(File.Exists(SqliteStore.BackupPath(database, 7)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public void The_commands_go_through_the_dispatcher()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Notes", [RulesFamilies.Srd51]));
        Publish(temp, Feat(source.Id, "Test Storm Step", Guid.NewGuid(), Guid.NewGuid()));
        var dispatcher = new CommandDispatcher(temp.App);
        string Send(string command, object payload) => dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact));

        Assert.Contains("\"pack.source-not-shareable\"", Send("package.sourcePackPreview", new { sourceIds = new[] { source.Id } }), StringComparison.Ordinal);
        Assert.Contains("\"source.confirm-own-work\"", Send("source.setShareable", new { sourceId = source.Id, shareable = true }), StringComparison.Ordinal);
        Assert.Contains("\"ok\":true", Send("source.setShareable", new { sourceId = source.Id, shareable = true, confirmOwnWork = true }), StringComparison.Ordinal);
        using var exported = JsonDocument.Parse(Send("package.sourcePackExport", new { sourceIds = new[] { source.Id } }));
        Assert.True(exported.RootElement.GetProperty("ok").GetBoolean());
        Assert.NotEmpty(exported.RootElement.GetProperty("result").GetProperty("base64").GetString()!);
        Assert.Contains("\"pack.sources-required\"", Send("package.sourcePackExport", new { }), StringComparison.Ordinal);
    }

    /// <summary>A second, attested source in the pack that holds a revision of the first source's content.</summary>
    private static byte[] SplitContent(byte[] pack)
    {
        var withSource = AddEntry(pack, $"sources/{SecondSource:D}.json", "source", SecondSourceRecord);
        var revision = Feat(SecondSource, "Test Storm Step", FeatContent, SplitRevision) with { Status = RevisionStatus.Published, SchemaVersion = 3 };
        var split = AddEntry(withSource, $"content/{SplitRevision:D}.json", "contentRevision", revision, order: SplitRevision);
        return PackageEditor.Edit(split, _ => false, _ => { }, m => m["attestations"]!.AsArray().Add(
            new JsonObject { ["sourceId"] = SecondSource.ToString("D"), ["statement"] = "x", ["confirmedAt"] = "2026-09-24T12:00:00+00:00" }));
    }

    private static Character HeroPinning(ContentReference feat)
    {
        var hero = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        return hero with { Pins = [.. hero.Pins, feat] };
    }

    private static void Execute(TempApp temp, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(temp.Directory, TomeStackApp.DatabaseFileName)};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Adds one entry to a package and lists it in the manifest (and in the revision order when given).</summary>
    private static byte[] AddEntry<T>(byte[] package, string path, string kind, T value, Guid? order = null)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, RulesJson.Options) + "\n");
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
        files[path] = bytes;
        var manifest = JsonNode.Parse(files["manifest.json"])!;
        manifest["entries"]!.AsArray().Add(new JsonObject { ["path"] = path, ["kind"] = kind, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)), ["size"] = bytes.LongLength });
        if (order is { } id)
            manifest["revisionOrder"]!.AsArray().Add(id.ToString("D"));
        files["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString(RulesJson.Options) + "\n");
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(content);
            }
        }
        return output.ToArray();
    }
}

internal static class SourcePackTestExtensions
{
    public static byte[] ExportPack(this TempApp temp, Guid sourceId) => temp.App.ExportSourcePack([sourceId]).Content;
}
