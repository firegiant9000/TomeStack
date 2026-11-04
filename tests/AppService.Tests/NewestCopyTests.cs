using System.Text.Json;
using TomeStack.AppService.Exports;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Owner answer C2-Q1: the Q14 rule (a stored copy with a verified marker never counts as the newest while an unmarked revision
/// of its content exists) at every "newest" site. Each test stores an original, then a verified copy after it, and asserts the
/// site uses the original. The sites already covered in <see cref="DownConversionExportTests"/> are not repeated.
/// </summary>
public class NewestCopyTests
{
    /// <summary>
    /// A verified copy of <paramref name="original"/> (D85 part 2): <paramref name="change"/> applied, the marker naming the original,
    /// and the id <see cref="DownConversion.CopyId"/> gives its bytes. Not stored.
    /// </summary>
    internal static ContentRevision CopyOf(ContentRevision original, Func<ContentRevision, ContentRevision>? change = null)
    {
        var marker = JsonDocument.Parse($$"""{"revisionId":"{{original.RevisionId:D}}","schemaVersion":{{original.SchemaVersion}}}""").RootElement.Clone();
        var copy = (change?.Invoke(original) ?? original) with { Extensions = new Dictionary<string, JsonElement> { [DownConversion.MarkerKey] = marker } };
        copy = copy with { RevisionId = DownConversion.CopyId(original.RevisionId, copy.SchemaVersion, DownConversion.CanonicalBytes(copy)) };
        Assert.True(DownConversion.IsCopy(copy));
        return copy;
    }

    /// <summary>Stores <see cref="CopyOf"/> the stored <paramref name="original"/>, after every revision already here.</summary>
    internal static ContentRevision StoreCopy(TempApp app, ContentReference original, Func<ContentRevision, ContentRevision>? change = null)
    {
        var copy = CopyOf(app.App.Store.FindRevision(original)!, change);
        app.App.Store.AddRevision(copy);
        return copy;
    }

    private static SourceRecord Source(TempApp app) =>
        app.App.CreateHomebrewSource(new("Newest Copy Homebrew", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));

    private static ContentRevision Spell(SourceRecord source, string name, params string[] lists) => new()
    {
        ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Spell, Name = name, RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(source.Id), Status = RevisionStatus.Draft, Effects = [new SpellEffect { Id = "spell", Level = 1, Lists = lists }],
    };

    private static ContentReference Publish(TempApp app, ContentRevision draft) => app.App.Publish(app.App.SaveDraft(draft)).Published;

    private static ContentRevision Renamed(ContentRevision revision) => revision with { Name = revision.Name + " (copy)" };

    [Fact]
    public void Copy_never_counts_as_newest_for_the_store_or_spell_list_keys()
    {
        using var app = new TempApp();
        var source = Source(app);
        var frost = Publish(app, Spell(source, "Newest Frost Mote", "original-list"));
        StoreCopy(app, frost, r => Renamed(r) with { Effects = [new SpellEffect { Id = "spell", Level = 1, Lists = ["copy-list"] }] });
        // A copy holding none of the effects asked for still gives way to an original that holds one.
        var ember = Publish(app, Spell(source, "Newest Ember Mote", "ember-list"));
        StoreCopy(app, ember, r => Renamed(r) with { Effects = [] });

        Assert.Equal(frost, app.App.Store.NewestPublished(frost.ContentId)!.Reference);
        Assert.Equal(frost, ((IContentCatalog)app.App.Store).NewestPublished(frost.ContentId)!.Reference);
        var listed = app.App.Store.ListNewestPublishedWithEffects([SpellEffect.TypeName]).Where(r => r.Provenance.SourceId == source.Id).Select(r => r.Reference).ToList();
        Assert.Equal([frost, ember], listed);
        var keys = app.App.SpellListKeys().Select(k => k.Key).ToList();
        Assert.Contains("original-list", keys);
        Assert.Contains("ember-list", keys);
        Assert.DoesNotContain("copy-list", keys);
    }

    [Fact]
    public void Copy_never_counts_as_newest_when_adding_to_a_spell_list()
    {
        using var app = new TempApp();
        var frost = Publish(app, Spell(Source(app), "Newest Frost Mote", "original-list"));
        StoreCopy(app, frost, r => Renamed(r) with { Effects = [new SpellEffect { Id = "spell", Level = 1, Lists = ["copy-list"] }] });

        var result = app.App.AddToSpellList(new([frost.ContentId], "added-list"));

        var draft = app.App.Store.FindRevision(Assert.Single(result.Drafts))!;
        Assert.Equal("Newest Frost Mote", draft.Name);
        Assert.Equal(["original-list", "added-list"], Assert.IsType<SpellEffect>(Assert.Single(draft.Effects)).Lists);
        Assert.Null(draft.Extensions);
    }

    [Fact]
    public void Copy_never_counts_as_newest_in_the_studio_list()
    {
        using var app = new TempApp();
        var source = Source(app);
        var frost = Publish(app, Spell(source, "Newest Frost Mote", "original-list"));
        StoreCopy(app, frost, Renamed);

        var entry = Assert.Single(app.App.ContentBySource(source.Id));

        Assert.Equal("Newest Frost Mote", entry.Name);
        Assert.Equal(frost, entry.Latest.Reference);
        Assert.Equal(frost, entry.LatestPublished!.Reference);
        Assert.Equal(3, entry.Revisions.Count); // the draft, the original and the copy are all still listed
    }

    [Fact]
    public void Copy_never_counts_as_newest_for_coverage()
    {
        using var app = new TempApp();
        var source = Source(app);
        var frost = Publish(app, Spell(source, "Newest Frost Mote", "original-list"));
        StoreCopy(app, frost, Renamed);
        var draftOnly = app.App.SaveDraft(Spell(source, "Newest Draft Mote", "draft-list"));
        StoreCopy(app, draftOnly, Renamed);

        var (published, drafts) = Coverage.Choose(app.App.Store.ListRevisionsInOrder(), includeDrafts: true);

        Assert.Equal(frost, published[frost.ContentId].Reference);
        Assert.Equal(draftOnly, drafts[draftOnly.ContentId].Reference);
        Assert.DoesNotContain(frost.ContentId, drafts.Keys);
    }

    [Fact]
    public void Copy_never_counts_as_newest_for_a_roll_that_casts_a_spell_and_its_export()
    {
        using var app = new TempApp();
        var source = Source(app);
        var spark = Publish(app, Spell(source, "Newest Frost Mote"));
        var copy = StoreCopy(app, spark, Renamed); // stored after the spell, so by stored order alone it would be the one cast
        var feature = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feature, Name = "Newest Mote Rite", RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(source.Id), Status = RevisionStatus.Published, SchemaVersion = ContentRevision.PaymentSchemaVersion,
            Effects = [new RollEffect { Id = "cast", RollId = "cast", Label = "Cast Mote", CastsSpell = spark.ContentId }],
        };
        app.App.Store.AddRevision(feature);
        var character = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        var id = app.App.SaveCharacter(character with { Pins = [.. character.Pins, feature.Reference] }).Character.Id;

        var cast = app.App.GetCharacter(id).Sheet.Features!.SelectMany(f => f.Effects).Single(e => e.Id == "cast").CastsSpell;

        Assert.Equal("Newest Frost Mote", cast!.Name);
        Assert.Equal(spark, cast.Spell);

        var export = app.App.ExportCharacters([id], ExportPurpose.Backup);

        Assert.Contains(export.Manifest.Entries, e => e.Path == $"content/{spark.RevisionId:D}.json");
        Assert.DoesNotContain(export.Manifest.Entries, e => e.Path == $"content/{copy.RevisionId:D}.json");
    }

    [Fact]
    public void Copy_never_counts_as_newest_in_content_validation()
    {
        using var app = new TempApp();
        var source = Source(app);
        var path = new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3 };
        var scale = new ScaleEffect { Id = "ink-scale", ScaleId = "ink", Label = "Ink", Values = [.. Enumerable.Repeat(2, 20)] };
        var cls = Publish(app, new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Class, Name = "Newest Ink Class", RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(source.Id), Status = RevisionStatus.Draft, Effects = [new HitDieEffect { Id = "hd", Die = 8 }, path, scale],
        });
        // The copy is stored after the class and declares no scale: if it counted as the newest, the class would seem to hold no "ink".
        StoreCopy(app, cls, r => Renamed(r) with { Effects = [.. r.Effects.Where(e => e is not ScaleEffect)] });
        var subclass = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Subclass, Name = "Newest Ink Path", RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(source.Id), Status = RevisionStatus.Draft, ExtendsChoice = new(cls.ContentId, "path"), Effects = [scale with { Id = "path-scale" }],
        };

        var report = ContentValidator.Validate(subclass, app.App.Store);

        // A clash with the newest class revision (the original) is an error; as a mere older revision it would only be a warning.
        Assert.Contains(report.Errors, e => e.Code == "validate.scale-duplicate");
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.scale-duplicate-older");
    }

    [Fact]
    public void Copy_never_counts_as_newest_in_coverage_validation()
    {
        using var app = new TempApp();
        var source = Source(app);
        var path = new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3 };
        var scale = new ScaleEffect { Id = "ink-scale", ScaleId = "ink", Label = "Ink", Values = [.. Enumerable.Repeat(2, 20)] };
        var cls = Publish(app, new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Class, Name = "Newest Ink Class", RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(source.Id), Status = RevisionStatus.Draft, Effects = [new HitDieEffect { Id = "hd", Die = 8 }, path, scale],
        });
        StoreCopy(app, cls, r => Renamed(r) with { Effects = [.. r.Effects.Where(e => e is not ScaleEffect)] }); // stored last, declares no scale
        ContentRevision Path(string name, string scaleId) => new()
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Subclass, Name = name, RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(source.Id), Status = RevisionStatus.Draft, ExtendsChoice = new(cls.ContentId, "path"), Effects = [scale with { ScaleId = scaleId }],
        };
        app.App.SaveDraft(Path("Newest Clashing Path", "ink"));
        app.App.SaveDraft(Path("Newest Clear Path", "dye"));

        var report = app.App.Coverage(new CoverageRequest(source.Id, IncludeDrafts: true));

        var rows = report.Sources.SelectMany(s => s.Classes.SelectMany(c => c.Levels.SelectMany(l => l.Features)).Concat(s.Other))
            .Where(f => f.Kind == ContentKind.Subclass && f.Status == RevisionStatus.Draft).ToList();
        var clashing = Assert.Single(rows, f => f.Name == "Newest Clashing Path").Effects.Single(e => e.EffectId == "ink-scale");
        var clear = Assert.Single(rows, f => f.Name == "Newest Clear Path").Effects.Single(e => e.EffectId == "ink-scale");
        // The clash with the original class's "ink" is a validation error, so that effect is not applied as written; the other is.
        Assert.NotEqual(clear.Effective, clashing.Effective);
    }

    [Fact]
    public void Copy_never_counts_as_newest_when_publishing_many()
    {
        using var app = new TempApp();
        var source = Source(app);
        var frost = Publish(app, Spell(source, "Newest Frost Mote", "original-list"));
        var edit = app.App.SaveDraft(app.App.Store.FindRevision(frost)! with { RevisionId = Guid.Empty, Status = RevisionStatus.Draft, Name = "Newest Frost Mote, edited" });
        StoreCopy(app, frost, Renamed); // stored after the edit: by stored order alone the copy would be the newest, so nothing is pending

        var preview = app.App.PreviewPublishMany([frost.ContentId]);

        Assert.Equal(edit, Assert.Single(preview.Entries).Draft);
    }
}
