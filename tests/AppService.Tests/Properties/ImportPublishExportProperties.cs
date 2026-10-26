using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;
using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.AppService.Tests.Properties;

/// <summary>
/// Import Lab Task 14 (D60 gate), property: content that is drafted, published and exported in a source pack arrives in a clean store with
/// every revision's stored hash equal, and neither importing the pack again nor adding a published revision again is ever a
/// <see cref="ImmutableRevisionException"/> (the same bytes are "unchanged").
/// <para>
/// The sources are not import-derived. A source that an import wrote into is flagged import-derived and the sharing guard refuses it
/// (<c>Preflight.ShareProblem</c>, D14 item 6), so it can never be in a source pack; the property therefore starts at the draft, which
/// is where the accept flow ends (<c>CandidateQuarantine.ToDraftRevision</c> writes a draft, and the accept flow itself is covered by
/// <see cref="ImportLabGateTests"/>). The drafts come from the shared revision generator (all eight kinds it produces), with valid
/// effects substituted. Nothing is filtered: every draft must publish (a refusal fails the case), each case has at least one, and the
/// companion test publishes one draft of every kind and checks the libraries cover all eight kinds.
/// </para>
/// </summary>
public class ImportPublishExportProperties
{
    private static readonly string[] Family = [RulesFamilies.Srd521];

    /// <summary>
    /// A generated draft: the content id, kind, name and summary are the generator's (the kind is any of the eight it produces). Its effects
    /// are one valid modifier, or none, and its choice extension is dropped: the generator's own effects are random, and a probe of 30
    /// of them found none that validates, so they could never reach "published" and would make the property vacuous.
    /// </summary>
    private static readonly Gen<ContentRevision> Drafts =
        from json in Generators.Revision(ContentRevision.CurrentSchemaVersion)
        from bonus in Gen.Choose(-3, 5)
        from withEffect in Gen.Elements(true, false)
        let revision = JsonSerializer.Deserialize<ContentRevision>(json.ToJsonString(), RulesJson.Compact)!
        select revision with
        {
            RevisionId = Guid.Empty, Status = RevisionStatus.Draft, RulesFamilies = Family, ExtendsChoice = null, Provenance = new(Guid.Empty),
            Name = "Test " + revision.Name,
            Effects = withEffect ? [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = bonus.ToString(System.Globalization.CultureInfo.InvariantCulture) }] : [],
        };

    /// <summary>One to four drafts, never none: every case publishes at least one revision.</summary>
    public static Gen<IReadOnlyList<ContentRevision>> Libraries { get; } =
        Drafts.NonEmptyListOf().Select(d => (IReadOnlyList<ContentRevision>)[.. d.Take(4).GroupBy(r => r.ContentId).Select(g => g.First())]);

    private static readonly ContentKind[] GeneratorKinds =
        [ContentKind.Species, ContentKind.Background, ContentKind.Class, ContentKind.Subclass, ContentKind.Feature, ContentKind.Feat, ContentKind.Spell, ContentKind.Item];

    [Fact]
    public void Every_kind_the_generator_makes_publishes_and_the_libraries_cover_them_all_and_are_never_empty()
    {
        using var app = new TempApp();
        var source = app.App.CreateHomebrewSource(new("Test Kind Book", Family));
        foreach (var kind in GeneratorKinds)
        {
            var draft = Drafts.Where(d => d.Kind == kind).Sample(30, 1).First(); // Where retries until the kind comes up, so this never misses one
            var saved = app.App.SaveDraft(draft with { Provenance = new(source.Id) });
            Assert.Equal(RevisionStatus.Published, app.App.Store.FindRevision(app.App.Publish(saved).Published)!.Status);
        }

        var libraries = Libraries.Sample(300, 300).ToList(); // 300 libraries whichever argument is the count: a missing kind is ~(7/8)^300
        Assert.All(libraries, l => Assert.NotEmpty(l));
        Assert.Equal(GeneratorKinds.Order(), libraries.SelectMany(l => l).Select(d => d.Kind).Distinct().Order());
    }

    [Property(MaxTest = 25)]
    public Property Import_publish_export_round_trips() =>
        Prop.ForAll(Libraries.ToArbitrary(), drafts =>
        {
            using var author = new TempApp();
            var source = author.App.CreateHomebrewSource(new("Test Generated Book", Family, Redistributable: true, ConfirmOwnWork: true));
            var published = new List<ContentReference>();
            foreach (var draft in drafts)
            {
                var saved = author.App.SaveDraft(draft with { Provenance = new(source.Id) });
                published.Add(author.App.Publish(saved).Published);
            }
            Assert.Equal(drafts.Count, published.Count); // every generated draft publishes; a refusal throws above and fails the case
            Assert.NotEmpty(published);

            var pack = author.App.ExportSourcePack([source.Id]).Content;
            using var clean = new TempApp(now: TempApp.Now.AddDays(1));
            Assert.True(clean.App.PreviewImport(pack).CanApply);
            clean.App.ApplyImport(pack);

            foreach (var reference in published)
            {
                Assert.Equal(author.App.Store.RevisionHash(reference.RevisionId), clean.App.Store.RevisionHash(reference.RevisionId));
                var arrived = clean.App.Store.FindRevision(reference)!;
                Assert.Equal(TempApp.Json(author.App.Store.FindRevision(reference)), TempApp.Json(arrived));
                // The same bytes again are unchanged, never an immutability error, on either side.
                Assert.False(clean.App.Store.AddRevision(arrived));
                Assert.False(author.App.Store.AddRevision(author.App.Store.FindRevision(reference)!));
            }
            var again = clean.App.ApplyImport(pack); // importing the pack again throws nothing and adds nothing
            Assert.Equal(0, again.Added);
            return true;
        });
}
