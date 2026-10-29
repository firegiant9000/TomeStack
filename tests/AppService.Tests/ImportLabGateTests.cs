using System.Security.Cryptography;
using System.Text;
using TomeStack.AppService.Persistence;
using TomeStack.ImportWorker.Tests;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Import Lab Task 14, the D60 gate end to end: a synthetic twenty-level class (the original <c>tests/RulesFixtures/lab/placeholder-class.md</c>)
/// is imported from Markdown or PDF, reviewed, accepted with the confirm ticks and published, and the grid and the coverage report show
/// what it really is, while a published revision never changes. The fixture plants no effect type the engine does not know: what it
/// plants is a class with a hit die (confirmed), four proficiency grants (left as reference), ten text-only features (reference) and
/// three whose text sounds like a mechanic ("regain", "once per"), which the coverage report flags as "may need a mechanic".
/// </summary>
public class ImportLabGateTests
{
    private static string FixtureText => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "lab", "placeholder-class.md"));

    private static byte[] FixtureBytes => Encoding.UTF8.GetBytes(FixtureText);

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static SourceRecord Homebrew(TempApp temp, string title = "Gate Book") => temp.App.CreateHomebrewSource(new(title, [RulesFamilies.Srd521]));

    private static ImportJobRecord ImportMarkdown(TempApp temp, Guid sourceId) =>
        temp.WaitForImport(temp.App.StartMarkdownImport(sourceId, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "lab", "placeholder-class.md"))).Id);

    private static ImportJobRecord ImportPdf(TempApp temp, Guid sourceId)
    {
        temp.App.AttachPdf(sourceId, "placeholder-class.pdf", FixturePdfs.PlaceholderClass(FixtureText));
        return temp.WaitForImport(temp.App.StartImport(new(sourceId, WholeDocument: true)).Id);
    }

    private static StoredCandidate TheClass(TempApp temp, Guid jobId) =>
        temp.App.ListCandidates(new(jobId)).Single(c => c.Candidate.ProposedKind == ContentKind.Class);

    /// <summary>The class and the features it links are drafts; the other features of the page stay pending.</summary>
    private static StoredCandidate AcceptClass(TempApp temp, Guid jobId, params string[] confirmed) =>
        temp.Accept(new(TheClass(temp, jobId).Id, Confirm: true, ConfirmedEffectIds: confirmed));

    private static void PublishAllDrafts(TempApp temp, Guid sourceId)
    {
        var ids = temp.App.Store.ListRevisionsInOrder().Where(r => r.Provenance.SourceId == sourceId).GroupBy(r => r.ContentId)
            .Where(g => g.Last().Status == RevisionStatus.Draft).Select(g => g.Key).ToList();
        var preview = temp.App.PreviewPublishMany(ids);
        Assert.True(preview.CanPublish, string.Join("; ", preview.Entries.SelectMany(e => e.Report.Errors).Select(d => d.Code)));
        temp.App.PublishMany(new(ids, [.. preview.Entries.Select(e => e.Draft.RevisionId)], true));
    }

    private static ContentRevision PublishedClass(TempApp temp, Guid sourceId) =>
        temp.App.Store.ListRevisionsInOrder().Last(r => r.Provenance.SourceId == sourceId && r.Kind == ContentKind.Class && r.Status == RevisionStatus.Published);

    /// <summary>The fixture's own level table (Level | Proficiency Bonus | Class Features), read from the file: level to feature names.</summary>
    private static Dictionary<int, string[]> FixtureTable() =>
        FixtureText.ReplaceLineEndings("\n").Split('\n')
            .Where(l => l.StartsWith("| ", StringComparison.Ordinal) && char.IsAsciiDigit(l[2]))
            .Select(l => l.Trim('|').Split('|').Select(c => c.Trim()).ToArray())
            .ToDictionary(c => int.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture), c => c[2] == "-" ? Array.Empty<string>() : c[2].Split(',', StringSplitOptions.TrimEntries));

    private static (ContentKind Kind, string Name, string? Level)[] Identities(IEnumerable<StoredCandidate> candidates) =>
        [.. candidates.Select(c => (c.Candidate.ProposedKind, c.Candidate.ProposedName, c.Candidate.Fields.GetValueOrDefault("level"))).Order()];

    [Fact]
    public void Markdown_class_becomes_a_reviewed_class()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var job = ImportMarkdown(temp, source.Id);
        Assert.Equal(Sha(FixtureBytes), job.Sha256);

        // Detection planted: one class and eleven features (the repeated "Fixture Rider" is two of them).
        var candidates = temp.App.ListCandidates(new(job.Id));
        Assert.Equal((1, 11), (candidates.Count(c => c.Candidate.ProposedKind == ContentKind.Class), candidates.Count(c => c.Candidate.ProposedKind == ContentKind.Feature)));
        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == source.Id); // nothing is active before review

        var accepted = AcceptClass(temp, job.Id, "hit-die");
        Assert.Equal(CandidateStatus.Accepted, accepted.Status);
        Assert.Equal(0, temp.App.ListContent(RulesFamilies.Srd521).Count(o => o.SourceId == source.Id)); // drafts are not active
        PublishAllDrafts(temp, source.Id);

        // One class and the ten features its table links: the eleventh candidate (the second "Fixture Rider") is a repeat of a name
        // the class already grants, so it stays pending for the reviewer.
        var published = temp.App.Store.ListRevisions().Where(r => r.Provenance.SourceId == source.Id && r.Status == RevisionStatus.Published).ToList();
        Assert.Equal((1, 10), (published.Count(r => r.Kind == ContentKind.Class), published.Count(r => r.Kind == ContentKind.Feature)));
        Assert.Equal(1, temp.App.ListCandidates(new(job.Id)).Count(c => c.Status == CandidateStatus.Pending));

        // content.classTable: twenty rows, the fixture's table. A name already listed at an earlier level is granted once, at the lowest level.
        var cls = PublishedClass(temp, source.Id);
        var table = temp.App.ClassTable(new(cls.RevisionId));
        Assert.Equal(20, table.Rows.Count);
        var fixture = FixtureTable();
        Assert.Equal(Enumerable.Range(1, 20), table.Rows.Select(r => r.Level));
        Assert.Equal(Enumerable.Range(1, 20).Select(l => 2 + (l - 1) / 4), table.Rows.Select(r => r.ProficiencyBonus)); // +2 +2 +2 +2 +3 ... +6 as the fixture sets it
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
        {
            var expected = fixture[row.Level].Where(seen.Add).ToArray();
            Assert.Equal(expected, row.Features.Select(f => f.Name));
        }
        Assert.Equal([1, 2, 3, 5, 6, 9, 11, 15, 18, 20], table.Rows.Where(r => r.Features.Count > 0).Select(r => r.Level));
        Assert.Empty(table.Rows[11].Features); // level 12 repeats "Fixture Rider": the known one-grant-per-name limit, reported with the task

        // content.coverage: what the fixture plants, counted from the capability registry.
        var coverage = temp.App.Coverage(new(source.Id));
        Assert.Equal(new CoverageCounts(Automatic: 1, Assisted: 0, Reference: 14, Unsupported: 0), coverage.Totals);
        var classCoverage = Assert.Single(Assert.Single(coverage.Sources).Classes);
        Assert.Equal(coverage.Totals, classCoverage.Totals);
        var features = classCoverage.Levels.SelectMany(l => l.Features).ToList();
        Assert.Equal(11, features.Count); // the class's own row (hit die and four proficiency grants) and ten text-only features
        Assert.Empty(coverage.Sources[0].Other);
        Assert.Equal(["Fixture Finale", "Fixture Lull", "Fixture Pool"], features.Where(f => f.MissingMechanic).Select(f => f.Name).Order(StringComparer.Ordinal));
        Assert.All(features.SelectMany(f => f.Effects), e => Assert.False(e.Unsupported));
        Assert.Equal(10, features.Where(f => f.Kind == ContentKind.Feature).SelectMany(f => f.Effects).Count(e => e is { EffectId: null, Type: "text", Effective: AutomationStatus.Reference }));

        // Immutability: a second identical import offers only what was not reviewed, and publishing again adds nothing.
        var classHash = temp.App.Store.RevisionHash(cls.RevisionId);
        var revisionsBefore = temp.App.Store.ListRevisionsInOrder().Count;
        var again = ImportMarkdown(temp, source.Id);
        Assert.Equal(job.Sha256, again.Sha256);
        Assert.DoesNotContain(temp.App.ListCandidates(new(again.Id)), c => c.Candidate.ProposedKind == ContentKind.Class);
        Assert.Equal(["Fixture Rider"], temp.App.ListCandidates(new(again.Id)).Select(c => c.Candidate.ProposedName)); // only the one left pending
        Assert.Equal(revisionsBefore, temp.App.Store.ListRevisionsInOrder().Count);
        Assert.Equal(classHash, temp.App.Store.RevisionHash(cls.RevisionId));
        Assert.False(temp.App.Store.AddRevision(cls)); // the same bytes: unchanged, not an error
        Assert.Throws<ImmutableRevisionException>(() => temp.App.Store.AddRevision(cls with { Summary = "Changed after publishing." }));
        Assert.Equal(classHash, temp.App.Store.RevisionHash(cls.RevisionId));
    }

    [Fact]
    public void Pdf_route_gives_the_same_candidates()
    {
        using var temp = new TempApp();
        var markdownSource = Homebrew(temp, "Gate Markdown Book");
        var pdfSource = Homebrew(temp, "Gate Pdf Book");

        var markdown = ImportMarkdown(temp, markdownSource.Id);
        var pdf = ImportPdf(temp, pdfSource.Id);

        Assert.Equal(("markdown", null), (markdown.Format, pdf.Format));
        // Kind, name and level are equal; the pages may differ (the PDF has three, the Markdown file one).
        Assert.Equal(Identities(temp.App.ListCandidates(new(markdown.Id))), Identities(temp.App.ListCandidates(new(pdf.Id))));
        var fromPdf = TheClass(temp, pdf.Id);
        Assert.Equal(TheClass(temp, markdown.Id).Candidate.Fields.Where(f => f.Key.StartsWith("level ", StringComparison.Ordinal)).OrderBy(f => f.Key, StringComparer.Ordinal),
            fromPdf.Candidate.Fields.Where(f => f.Key.StartsWith("level ", StringComparison.Ordinal)).OrderBy(f => f.Key, StringComparer.Ordinal));
        Assert.Equal(TheClass(temp, markdown.Id).Candidate.ProposedEffects.Select(e => e.Id), fromPdf.Candidate.ProposedEffects.Select(e => e.Id));

        // The PDF route reviews to the same class.
        AcceptClass(temp, pdf.Id, "hit-die");
        PublishAllDrafts(temp, pdfSource.Id);
        var table = temp.App.ClassTable(new(PublishedClass(temp, pdfSource.Id).RevisionId));
        Assert.Equal([1, 2, 3, 5, 6, 9, 11, 15, 18, 20], table.Rows.Where(r => r.Features.Count > 0).Select(r => r.Level));
    }

    [Fact]
    public void Unconfirmed_effects_publish_as_reference()
    {
        using var temp = new TempApp();
        var source = Homebrew(temp);
        var job = ImportMarkdown(temp, source.Id);
        var proposed = TheClass(temp, job.Id).Candidate.ProposedEffects.Select(e => e.Id).ToList();
        Assert.Equal(["hit-die", "save-wis", "save-cha", "armor-light", "weapon-simple"], proposed);

        // Only the hit die is ticked (the class cannot be accepted as a draft without it); the proficiencies are not.
        AcceptClass(temp, job.Id, "hit-die");
        PublishAllDrafts(temp, source.Id);

        var cls = PublishedClass(temp, source.Id);
        Assert.Equal(AutomationStatus.Automatic, Assert.IsType<HitDieEffect>(cls.Effects.Single(e => e.Id == "hit-die")).Automation);
        var others = cls.Effects.Where(e => e is not GrantEffect { Grant: GrantKind.Content } && e.Id != "hit-die").ToList();
        Assert.Equal(["save-wis", "save-cha", "armor-light", "weapon-simple"], others.Select(e => e.Id));
        Assert.All(others, e => Assert.Equal(AutomationStatus.Reference, e.Automation));
        var features = temp.App.Store.ListRevisions().Where(r => r.Provenance.SourceId == source.Id && r.Kind == ContentKind.Feature);
        Assert.All(features.SelectMany(f => f.Effects), e => Assert.Equal(AutomationStatus.Reference, e.Automation));
        // The report agrees with what was stored: one automatic mechanic, the rest reference, none unsupported.
        var totals = temp.App.Coverage(new(source.Id)).Totals;
        Assert.Equal((1, 0, 0), (totals.Automatic, totals.Assisted, totals.Unsupported));
    }
}
