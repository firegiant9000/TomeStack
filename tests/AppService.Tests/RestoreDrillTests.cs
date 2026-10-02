using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TomeStack.AppService.Diagnostics;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Roadmap T6 (docs/features/restore-drill-procedure.md): the drill's counting tool reads a data folder without changing
/// it, counts what a library holds, refuses a folder TomeStack has open, reports nothing but counts and digests, and finds
/// only the expected differences between a library and its full-backup restore.
/// </summary>
public class RestoreDrillTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% TomeStack restore drill test PDF\n%%EOF\n");

    private const string SourceTitle = "Test Drill Homebrew";

    private static ContentRevision Feat(Guid source, Guid contentId, string name, int bonus) => new()
    {
        ContentId = contentId, RevisionId = Guid.Empty, Kind = ContentKind.Feat, Name = name,
        RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521], Provenance = new(source), Status = RevisionStatus.Draft,
        Effects = [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = $"{bonus}" }],
    };

    /// <summary>A library with one of everything the drill counts: published and draft content, a PDF, a character with a gap note and a snapshot, and a campaign.</summary>
    private static Guid Fill(TomeStackApp app)
    {
        var source = app.CreateHomebrewSource(new(SourceTitle, [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        var pinned = app.Publish(app.SaveDraft(Feat(source.Id, Guid.NewGuid(), "Test Drill Feat", 1))).Published;
        app.SaveDraft(Feat(source.Id, Guid.NewGuid(), "Test Drill Draft", 2));
        app.AttachPdf(source.Id, "drill-notes.pdf", Pdf);
        var fixture = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        var character = app.SaveCharacter(fixture with { Pins = [.. fixture.Pins, pinned] }).Character.Id;
        app.AddGapNote(new(character, new(GapTargetKind.Field, FieldId: FieldIds.ArmorClass), "Test note: the drill counts this."));
        app.Snapshot(new(character, "Test drill snapshot"));
        app.SaveCampaign(new() { Id = Guid.Empty, Name = "Test Drill Campaign", RulesFamily = RulesFamilies.Srd51, AllowedSources = [source.Id], HouseRules = "Test rules." });
        return source.Id;
    }

    /// <summary>Every file in the folder: relative path, size, last write time and SHA-256.</summary>
    private static string FolderState(string folder) => string.Join('\n', Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(f => new FileInfo(f))
        .Select(f => $"{Path.GetRelativePath(folder, f.FullName)}|{f.Length}|{f.LastWriteTimeUtc:O}|{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f.FullName)))}"));

    [Fact]
    public void Counting_a_closed_data_folder_changes_nothing_in_it()
    {
        using var temp = new TempApp();
        Fill(temp.App);
        temp.Close();
        var before = FolderState(temp.Directory);

        RestoreDrill.Count(temp.Directory);
        RestoreDrill.Count(temp.Directory);

        Assert.Equal(before, FolderState(temp.Directory));
    }

    [Fact]
    public void The_counts_match_what_the_library_holds()
    {
        using var temp = new TempApp();
        var source = Fill(temp.App);
        var store = temp.App.Store;
        var revisions = store.ListRevisions();
        var bundled = TomeStackApp.BundledSourceIds;
        var sources = store.ListSources();
        temp.Close();

        var counts = RestoreDrill.Count(temp.Directory).Counts;

        Assert.Equal(revisions.Count(r => r.Status == RevisionStatus.Published), counts.RevisionsPublished);
        Assert.Equal(revisions.Count(r => r.Status == RevisionStatus.Draft), counts.RevisionsDraft);
        Assert.Equal(revisions.Count(r => bundled.Contains(r.Provenance.SourceId)), counts.RevisionsBundledSrd);
        Assert.Equal(revisions.Count(r => !bundled.Contains(r.Provenance.SourceId)), counts.RevisionsOwn);
        Assert.Equal(sources.Count, counts.Sources);
        Assert.Equal(sources.Count(s => bundled.Contains(s.Id)), counts.SourcesBundledSrd);
        Assert.Equal(1, counts.SourcesLocal); // the homebrew source; the dev fixtures' sources have no recorded origin
        Assert.Equal(sources.Count(s => !bundled.Contains(s.Id) && s.Origin is null), counts.SourcesUnknownOrigin);
        Assert.Equal(1, counts.SourcesImportDerived); // the PDF made it import-derived
        Assert.Equal((1, 0, 1, 1), (counts.Characters, counts.CharactersArchived, counts.Campaigns, counts.GapNotes));
        Assert.Equal((1, 1, 0), (counts.Attachments, counts.AttachmentsManaged, counts.AttachmentsLinked));
        Assert.Equal((1, (long)Pdf.Length, 0), (counts.ManagedFiles, counts.ManagedFileBytes, counts.ManagedFilesMissing));
        Assert.Equal((0, 0, 0, 1), (counts.Extensions, counts.ExtensionsEnabled, counts.ExtensionFiles, counts.Snapshots));
        Assert.NotEqual(Guid.Empty, source);
    }

    [Fact]
    public void A_folder_TomeStack_has_open_is_refused()
    {
        using var temp = new TempApp();

        Assert.Throws<DataFolderInUseException>(() => RestoreDrill.Count(temp.Directory));
    }

    [Fact]
    public void The_report_holds_counts_and_digests_only()
    {
        using var temp = new TempApp();
        Fill(temp.App);
        temp.Close();

        var json = JsonSerializer.Serialize(RestoreDrill.Count(temp.Directory)) + RestoreDrill.Format(RestoreDrill.Count(temp.Directory));

        foreach (var text in new[] { SourceTitle, "Test Drill Feat", "drill-notes", "Test note", "Test Drill Campaign", "Quickfoot", temp.Directory, Environment.UserName })
            Assert.DoesNotContain(text, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_full_backup_restored_into_a_clean_folder_differs_only_where_the_drill_expects()
    {
        using var author = new TempApp();
        Fill(author.App);
        var backup = Path.Combine(author.Directory, "..", "drill.tomestack.zip");
        using (var file = File.Create(backup))
            author.App.WriteLibraryBackup(file);
        author.Close();

        using var clean = new TempApp(now: TempApp.Now.AddDays(1));
        clean.App.ApplyLibraryRestore(backup);
        clean.Close();

        var differences = RestoreDrill.Compare(RestoreDrill.Count(author.Directory), RestoreDrill.Count(clean.Directory));

        Assert.All(differences, d => Assert.True(d.Expected is not null, $"{d.Item}: {d.Before} -> {d.After}"));
        // The one expected difference here: the snapshot stays behind (D14).
        Assert.Contains(differences, d => d.Item == nameof(RestoreDrill.Counts.Snapshots));
        Assert.DoesNotContain(differences, d => d.Item is "table content_revisions" or "table sources" or "table characters" or "table campaigns" or "table gap_notes" or "table attachments" or "table sheets");
        Assert.Equal(1, RestoreDrill.Count(clean.Directory).Tables.Single(t => t.Table == "sheets").Rows);
    }

    [Fact]
    public void An_unexpected_difference_is_reported_as_one()
    {
        using var temp = new TempApp();
        Fill(temp.App);
        temp.Close();
        var before = RestoreDrill.Count(temp.Directory);

        temp.Reopen();
        temp.App.SaveCampaign(new() { Id = Guid.Empty, Name = "Test Second Campaign", RulesFamily = RulesFamilies.Srd521, HouseRules = "Test rules." });
        temp.Close();

        var differences = RestoreDrill.Compare(before, RestoreDrill.Count(temp.Directory));

        Assert.Contains(differences, d => d.Item == nameof(RestoreDrill.Counts.Campaigns) && d.Expected is null);
        Assert.Contains(differences, d => d.Item == "table campaigns" && d.Expected is null);
    }
}
