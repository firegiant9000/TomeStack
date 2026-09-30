using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>M5 slice 7 (LIVING_SPECS D14): <c>content.feedback</c>, design hints against the bundled SRD classes. Read-only.</summary>
public class DesignFeedbackCommandTests
{
    [Fact]
    public void The_bundled_SRD_casters_raise_no_slot_hints_against_themselves()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var srd = app.ListSources().Where(s => s.Title.StartsWith("System Reference Document", StringComparison.Ordinal)).Select(s => s.Id).ToHashSet();
        var casters = app.Store.ListRevisionsInOrder()
            .Where(r => r.Status == RevisionStatus.Published && srd.Contains(r.Provenance.SourceId) && r.Effects.OfType<SpellcastingEffect>().Any())
            .GroupBy(r => r.ContentId).Select(g => g.Last()).ToList();
        Assert.True(casters.Count >= 8, $"{casters.Count} SRD casters");

        foreach (var caster in casters)
        {
            var hints = app.Feedback(new(Reference: caster.Reference));
            Assert.True(!hints.Any(h => h.Code is "design.slots-above-full-caster" or "design.multiclass-share-above-table"),
                $"{caster.Name} ({string.Join(", ", caster.RulesFamilies)}): {string.Join("; ", hints.Select(h => h.Message))}");
        }

        // No bundled SRD class has a level "without a feature" against the SRD classes (review fix).
        var classes = app.Store.ListRevisionsInOrder()
            .Where(r => r.Status == RevisionStatus.Published && r.Kind == ContentKind.Class && srd.Contains(r.Provenance.SourceId))
            .GroupBy(r => r.ContentId).Select(g => g.Last()).ToList();
        Assert.True(classes.Count >= 10, $"{classes.Count} SRD classes");
        foreach (var cls in classes)
            Assert.DoesNotContain(app.Feedback(new(Reference: cls.Reference)), h => h.Code == "design.level-without-feature");
    }

    [Fact]
    public void An_imported_source_is_never_part_of_the_SRD_baseline()
    {
        using var temp = new TempApp();
        var app = temp.App;
        // A package source that calls itself anything but homebrew, with a full caster of 9 slots at every level.
        var imported = new SourceRecord
        {
            Id = Guid.NewGuid(), Title = "Test Imported Book", Publisher = "Test", RulesFamilies = [RulesFamilies.Srd521],
            EditionVersion = "1.0", License = "Test", Redistributable = false,
        };
        IReadOnlyList<IReadOnlyList<int>> nine = [.. Enumerable.Range(1, 20).Select(_ => (IReadOnlyList<int>)[9])];
        var generousFeature = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feature, Name = "Test Imported Spellcasting",
            RulesFamilies = [RulesFamilies.Srd521], Provenance = new(imported.Id), Status = RevisionStatus.Published,
            Effects = [new SpellcastingEffect { Id = "casting", Ability = Ability.Int, SpellList = "test", MulticlassCaster = MulticlassCaster.Full, Slots = nine }],
        };
        app.Store.InTransaction(() =>
        {
            app.Store.UpsertSource(imported);
            app.Store.AddRevision(generousFeature);
        });
        var homebrew = app.CreateHomebrewSource(new("Test Feedback Source", [RulesFamilies.Srd521]));

        var hints = app.Feedback(new(Revision: generousFeature with { RevisionId = Guid.NewGuid(), Provenance = new(homebrew.Id), Status = RevisionStatus.Draft }));

        Assert.Contains(hints, h => h.Code == "design.slots-above-full-caster");
    }

    [Fact]
    public void A_revision_added_under_an_SRD_source_or_content_id_does_not_change_the_baseline()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var srdSource = app.ListSources().First(s => s.Title.StartsWith("System Reference Document", StringComparison.Ordinal));
        var srdCaster = app.Store.ListRevisionsInOrder()
            .Where(r => r.Status == RevisionStatus.Published && r.Provenance.SourceId == srdSource.Id && r.Effects.OfType<SpellcastingEffect>().Any())
            .GroupBy(r => r.ContentId).Select(g => g.Last()).First();
        var homebrew = app.CreateHomebrewSource(new("Test Feedback Source", [RulesFamilies.Srd521]));
        var generous = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Class, Name = "Test Feedback Class",
            RulesFamilies = [RulesFamilies.Srd521], Provenance = new(homebrew.Id), Status = RevisionStatus.Draft,
            Effects = [new SpellcastingEffect { Id = "casting", Ability = Ability.Int, SpellList = "test", MulticlassCaster = MulticlassCaster.Full, Slots = [.. Enumerable.Range(1, 20).Select(_ => (IReadOnlyList<int>)[9])] }],
        };
        var before = app.Feedback(new(Revision: generous));
        Assert.Contains(before, h => h.Code == "design.slots-above-full-caster");

        IReadOnlyList<IReadOnlyList<int>> nine = [.. Enumerable.Range(1, 20).Select(_ => (IReadOnlyList<int>)[9, 9, 9, 9, 9, 9, 9, 9, 9])];
        var inflatedSpellcasting = new SpellcastingEffect { Id = "casting", Ability = Ability.Int, SpellList = "test", MulticlassCaster = MulticlassCaster.Full, Slots = nine };
        // A published revision under the SRD source id, and a newer revision of an SRD caster feature's content id.
        var underSrdSource = generous with { ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feature, Provenance = new(srdSource.Id), Status = RevisionStatus.Published, Effects = [inflatedSpellcasting] };
        var newerRevision = srdCaster with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published, Effects = [inflatedSpellcasting] };
        app.Store.InTransaction(() =>
        {
            app.Store.AddRevision(underSrdSource);
            app.Store.AddRevision(newerRevision);
        });

        Assert.Equal(TempApp.Json(before), TempApp.Json(app.Feedback(new(Revision: generous))));
    }

    [Fact]
    public void An_unsaved_class_gets_hints_and_nothing_is_written()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Feedback Source", [RulesFamilies.Srd521]));
        var draft = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Class, Name = "Test Feedback Class",
            RulesFamilies = [RulesFamilies.Srd521], Provenance = new(source.Id), Status = RevisionStatus.Draft,
            // Faster than every SRD pool (the fastest is five times the class level, +95 from level 1 to 20).
            Effects = [new ResourceEffect { Id = "surge", ResourceId = "surge", Label = "Surge", Maximum = "10 * CLASS_LEVEL" },
                new ResourceEffect { Id = "focus", ResourceId = "focus", Label = "Focus", Maximum = "CLASS_LEVEL" },
                new ResourceEffect { Id = "equal", ResourceId = "equal", Label = "Equal", Maximum = "5 * CLASS_LEVEL" }],
        };
        var before = TempApp.Json(app.Store.ListRevisionsInOrder());

        var hints = app.Feedback(new(Revision: draft));

        var surge = Assert.Single(hints, h => h.Code == "design.resource-faster-than-srd" && h.EffectId == "surge");
        Assert.Contains("grows by +95", surge.Message, StringComparison.Ordinal); // the documented threshold, today
        Assert.DoesNotContain(hints, h => h.EffectId is "focus" or "equal"); // ordinary SRD growth, and equal is not faster
        Assert.Equal(before, TempApp.Json(app.Store.ListRevisionsInOrder()));
        Assert.Equal("feedback.scope", Assert.Throws<AppValidationException>(() => app.Feedback(new())).Problems.Single().Code);

        // The real SRD full casters are the baseline (review fix: they hold spellcasting on a granted feature).
        var generous = draft with
        {
            Effects = [new SpellcastingEffect { Id = "casting", Ability = Ability.Int, SpellList = "test", MulticlassCaster = MulticlassCaster.Full, Slots = [.. Enumerable.Range(1, 20).Select(_ => (IReadOnlyList<int>)[9])] }],
        };
        Assert.Contains(app.Feedback(new(Revision: generous)), h => h.Code == "design.slots-above-full-caster" && h.Level == 1 && h.Family == RulesFamilies.Srd521);
    }
}
