using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TomeStack.AppService.Exports;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;
using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.AppService.Tests.Properties;

/// <summary>
/// Import Lab L5 (design 9.1): for a generated library, what each share export writes is what <c>package.preflight</c> says
/// it includes. A character package, a source pack of the sources that pass the guard and a campaign pack write exactly the
/// preflight's <c>Included</c> revisions; a sheet share carries nothing the preflight omits. Where the guard refuses a
/// source, the preflight blocks and the export throws, and the other way round.
/// </summary>
public class PreflightProperties
{
    internal static Gen<IReadOnlyList<PreflightSourceSpec>> Libraries { get; } =
        (from kind in Gen.Elements(PreflightSourceKind.Shareable, PreflightSourceKind.ImportDerived, PreflightSourceKind.NotRedistributable)
         from feats in Gen.Choose(0, 3)
         from draft in Gen.Elements(true, false)
         from name in Generators.Names
         select (kind, feats, draft, name)).ArrayOf().Select(items =>
            (IReadOnlyList<PreflightSourceSpec>)[.. items.Take(4).Select((s, i) => new PreflightSourceSpec($"Test {s.name} {i}", s.kind, s.feats, s.draft))]);

    private static IReadOnlyList<Guid> Exported(PackageManifest manifest) =>
        [.. manifest.Entries.Where(e => e.Kind == "contentRevision").Select(e => Guid.Parse(e.Path["content/".Length..^".json".Length])).Order()];

    private static IReadOnlyList<Guid> Included(TomeStackApp app, PreflightKind kind, IReadOnlyList<Guid> selection) =>
        [.. app.PackagePreflight(new(kind, selection)).Included.Where(r => r.RevisionId != Guid.Empty).Select(r => r.RevisionId).Order()];

    [Property(MaxTest = 25)]
    public Property Export_output_equals_preflight_included() =>
        Prop.ForAll(Libraries.ToArbitrary(), specs =>
        {
            using var temp = new TempApp();
            var library = PreflightTests.Fill(temp.App, specs);
            var app = temp.App;

            // A character package for sharing.
            Assert.Equal(Included(app, PreflightKind.Package, [library.Character]), Exported(app.ExportCharacters([library.Character], ExportPurpose.Share).Manifest));

            // A campaign pack.
            Assert.Equal(Included(app, PreflightKind.CampaignPack, [library.Campaign]), Exported(app.ExportCampaignPack(library.Campaign).Manifest));

            // A source pack: blocking and refusing are the same thing; the sources that pass write what the preflight includes.
            if (library.Sources.Count > 0)
            {
                var report = app.PackagePreflight(new(PreflightKind.SourcePack, library.Sources));
                if (report.Blocking.Count > 0)
                    Assert.Throws<PackageException>(() => app.ExportSourcePack(library.Sources));
                else
                    Assert.Equal(Included(app, PreflightKind.SourcePack, library.Sources), Exported(app.ExportSourcePack(library.Sources).Manifest));
            }
            var shareable = specs.Select((s, i) => (s, id: library.Sources[i])).Where(x => x.s.Kind == PreflightSourceKind.Shareable && x.s.Feats > 0).Select(x => x.id).ToList();
            if (shareable.Count > 0)
            {
                Assert.Equal(Included(app, PreflightKind.SourcePack, shareable), Exported(app.ExportSourcePack(shareable).Manifest));
                Assert.Empty(app.PackagePreflight(new(PreflightKind.SourcePack, shareable)).Blocking);
            }

            // A sheet share: whatever it carries, the preflight includes, and nothing the preflight omits.
            var preflight = app.PackagePreflight(new(PreflightKind.Sheet, [library.Character]));
            var view = app.GetCharacter(library.Character);
            var sheet = SheetExportBuilder.Build(view.Character, view.Sheet, app.Store, SheetPurpose.Share, new HashSet<Guid>(), TempApp.Now);
            var carried = new[]
            {
                sheet.Character.Classes.SelectMany(c => new[] { c.Ref, c.SubclassRef }), sheet.Spellcasting.SelectMany(c => c.Spells.Select(s => (ContentReference?)s.Ref).Append(c.Ref)),
                sheet.Resources.Select(r => (ContentReference?)r.Ref), sheet.Attacks.Select(a => (ContentReference?)a.Ref), sheet.Features.Select(f => (ContentReference?)f.Ref),
                sheet.Toggles.Select(t => (ContentReference?)t.Ref), sheet.Scales.Select(s => (ContentReference?)s.Ref),
            }.SelectMany(refs => refs).OfType<ContentReference>().Select(r => r.RevisionId).ToHashSet();
            var includedIds = preflight.Included.Select(r => r.RevisionId).ToHashSet();
            Assert.Subset(includedIds, carried);
            Assert.DoesNotContain(preflight.Omitted, r => carried.Contains(r.RevisionId));
            return true;
        });
}
