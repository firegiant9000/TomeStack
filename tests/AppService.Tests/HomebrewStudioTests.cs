using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2 item 5, SPEC I-04, I-05, I-06, MVP "Homebrew": a homebrew subclass authored as drafts, validated, published, and
/// offered in the SRD Barbarian's subclass choice (content schema v4 <c>extendsChoice</c>). The synthetic acceptance
/// stands in for the private Stardust Guardian material (DoD 3): one modifier, one class resource, one limited-use
/// action and one reference-only feature. All names are original.
/// </summary>
public class HomebrewStudioTests
{
    private static ContentReference Srd(int n) => new(Guid.Parse($"52c00000-0000-4000-8000-{n:D12}"), Guid.Parse($"52e00000-0000-4000-8000-{n:D12}"));

    private static readonly ContentReference Barbarian = Srd(11);

    private sealed record Authored(SourceRecord Source, ContentReference Ward, ContentReference Lore, ContentReference Path);

    private static ContentRevision Draft(SourceRecord source, Guid contentId, ContentKind kind, string name, string? summary, params Effect[] effects) => new()
    {
        ContentId = contentId,
        RevisionId = Guid.Empty, // the service assigns one
        Kind = kind,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(source.Id, new(12)),
        Status = RevisionStatus.Draft,
        Summary = summary,
        Effects = effects,
    };

    private static ContentReference Publish(TomeStackApp app, ContentRevision draft)
    {
        var saved = app.SaveDraft(draft);
        Assert.True(app.ValidateContent(saved, null).CanPublish, string.Join("; ", app.ValidateContent(saved, null).Errors.Select(e => e.Message)));
        return app.Publish(saved).Published;
    }

    private static ContentRevision PathDraft(SourceRecord source, Guid contentId, ContentReference ward, ContentReference lore, int initiative) =>
        Draft(source, contentId, ContentKind.Subclass, "Path of the Test Storm", "A homebrew subclass written for TomeStack tests.",
            new ModifierEffect { Id = "storm-initiative", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = $"{initiative}" },
            new GrantEffect { Id = "grant-ward", Grant = GrantKind.Content, Content = ward, Level = 3 },
            new GrantEffect { Id = "grant-lore", Grant = GrantKind.Content, Content = lore, Level = 3 })
        with { ExtendsChoice = new(Barbarian.ContentId, "barbarian-subclass") };

    private static Authored Author(TomeStackApp app)
    {
        var source = app.CreateHomebrewSource(new("Test Homebrew", [RulesFamilies.Srd521]));
        var ward = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Feature, "Test Storm Ward", null,
            new ResourceEffect { Id = "charges", ResourceId = "storm", Label = "Storm charges", Maximum = "PB" },
            new RecoveryEffect { Id = "charges-long", ResourceId = "storm", On = RestPeriod.LongRest, Amount = "all", Timing = EffectTiming.OnLongRest },
            new RollEffect { Id = "bolt", RollId = "bolt", Label = "Storm bolt", Dice = "1d8 + 2", ResourceId = "storm", Automation = AutomationStatus.Assisted, Timing = EffectTiming.OnRoll }));
        var lore = Publish(app, Draft(source, Guid.NewGuid(), ContentKind.Feature, "Test Sky Lore", "You can read the weather a day ahead. (Reference only: no rules effect.)"));
        var path = Publish(app, PathDraft(source, Guid.NewGuid(), ward, lore, initiative: 1));
        return new(source, ward, lore, path);
    }

    private static Guid Brenna(TomeStackApp app)
    {
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        return app.SaveCharacter(brenna with { Choices = [.. brenna.Choices.Where(c => c.ChoiceId != "barbarian-subclass")] }).Character.Id;
    }

    [Fact]
    public void A_homebrew_source_needs_a_title_and_families_and_is_not_shareable_by_default()
    {
        using var temp = new TempApp();

        var source = temp.App.CreateHomebrewSource(new("  My Homebrew  ", [RulesFamilies.Srd51, RulesFamilies.Srd521]));

        Assert.Equal(("My Homebrew", "Personal homebrew", false), (source.Title, source.Publisher, source.Redistributable));
        Assert.Contains(temp.App.ListSources(), s => s.Id == source.Id);
        Assert.Equal("source.title-required", Assert.Throws<AppValidationException>(() => temp.App.CreateHomebrewSource(new(" ", [RulesFamilies.Srd51]))).Problems[0].Code);
        Assert.Equal("source.rules-family-required", Assert.Throws<AppValidationException>(() => temp.App.CreateHomebrewSource(new("X", []))).Problems[0].Code);
    }

    [Fact]
    public void A_published_homebrew_subclass_is_offered_in_the_SRD_class_choice_and_its_features_follow_the_class_level()
    {
        using var temp = new TempApp();
        var authored = Author(temp.App);
        var id = Brenna(temp.App);

        var offered = temp.App.GetCharacter(id).Sheet.Choices!.Single(c => c.ChoiceId == "barbarian-subclass");
        Assert.Equal([Srd(23), authored.Path], offered.Options); // Berserker (declared), then the extension

        var sheet = temp.App.Choose(new(id, Barbarian, "barbarian-subclass", [authored.Path])).Sheet;

        Assert.Equal(1 + 1, sheet.Field(FieldIds.Initiative).Value); // modifier: Dex +1, Test Storm +1
        var storm = Assert.Single(sheet.Resources!, r => r.ResourceId == "storm");
        Assert.Equal(2, storm.Maximum); // class resource: PB at level 3
        var lore = Assert.Single(sheet.Features!, f => f.Content == authored.Lore);
        Assert.Equal(AutomationStatus.Reference, lore.Automation); // reference-only text
        Assert.Contains("granted by subclass 'Path of the Test Storm'", lore.Via, StringComparison.Ordinal);
        Assert.Empty(sheet.Diagnostics);

        // Limited-use action: the roll names the resource but spends nothing; spending is its own confirmed command.
        var roll = temp.App.Roll(new(id, authored.Ward, "bolt"));
        Assert.Equal("storm", roll.Provenance!.LinkedResourceId);
        Assert.Equal(2, temp.App.GetCharacter(id).Sheet.Resources!.Single(r => r.ResourceId == "storm").Current);
        var spent = temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, ContentId: authored.Ward.ContentId, ResourceId: "storm"));
        Assert.Equal(1, spent.Sheet.Resources!.Single(r => r.ResourceId == "storm").Current);
    }

    [Fact]
    public void Choice_extensions_are_read_from_the_database_once_and_follow_new_and_rolled_back_revisions()
    {
        using var temp = new TempApp();
        var authored = Author(temp.App);
        var id = Brenna(temp.App);
        temp.App.GetCharacter(id);
        var scans = temp.App.Store.ChoiceExtensionScans;

        // Every calculation asks for the extensions of every offered choice; the store answers from memory.
        for (var i = 0; i < 3; i++)
        {
            temp.App.GetCharacter(id);
            temp.App.Play(new(id, PlayActionKind.Heal, Confirm: true, Amount: 1));
        }
        Assert.Equal(scans, temp.App.Store.ChoiceExtensionScans);

        // A newly published extension is offered at once (revisions are insert-only, so adding one is the only change).
        var second = Publish(temp.App, PathDraft(authored.Source, Guid.NewGuid(), authored.Ward, authored.Lore, initiative: 2) with { Name = "Path of the Second Test Storm" });
        Assert.Contains(second, temp.App.GetCharacter(id).Sheet.Choices!.Single(c => c.ChoiceId == "barbarian-subclass").Options);

        // One added in a transaction that rolls back is not.
        var rolledBack = PathDraft(authored.Source, Guid.NewGuid(), authored.Ward, authored.Lore, initiative: 3) with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Published };
        Assert.Throws<InvalidOperationException>(() => temp.App.Store.InTransaction(() =>
        {
            temp.App.Store.AddRevision(rolledBack);
            Assert.Contains(rolledBack.Reference, temp.App.GetCharacter(id).Sheet.Choices!.Single(c => c.ChoiceId == "barbarian-subclass").Options);
            throw new InvalidOperationException("roll back");
        }));
        Assert.DoesNotContain(rolledBack.Reference, temp.App.GetCharacter(id).Sheet.Choices!.Single(c => c.ChoiceId == "barbarian-subclass").Options);
    }

    [Fact]
    public void An_extension_is_not_offered_until_published_and_never_to_another_family()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Homebrew", [RulesFamilies.Srd521]));
        var draft = temp.App.SaveDraft(PathDraft(source, Guid.NewGuid(), Srd(12), Srd(13), 1));
        var id = Brenna(temp.App);

        Assert.DoesNotContain(draft, temp.App.GetCharacter(id).Sheet.Choices!.Single(c => c.ChoiceId == "barbarian-subclass").Options);
        Assert.Equal("choice.invalid-option", Assert.Throws<AppValidationException>(() => temp.App.Choose(new(id, Barbarian, "barbarian-subclass", [draft]))).Problems[0].Code);
    }

    [Fact]
    public void Extending_a_choice_that_does_not_exist_is_a_validation_error()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Homebrew", [RulesFamilies.Srd521]));
        var bad = PathDraft(source, Guid.NewGuid(), Srd(12), Srd(13), 1) with { RevisionId = Guid.NewGuid(), ExtendsChoice = new(Barbarian.ContentId, "no-such-choice") };

        var report = temp.App.ValidateContent(null, bad);

        Assert.Contains(report.Errors, e => e.Code == "validate.extends-choice-unknown");
        Assert.Equal(ContentRevision.CurrentSchemaVersion, bad.SchemaVersion); // new revisions are written in the current version
    }

    [Fact]
    public void Republishing_the_subclass_changes_no_character_until_a_reviewed_update_is_applied()
    {
        using var temp = new TempApp();
        var authored = Author(temp.App);
        var id = Brenna(temp.App);
        temp.App.Choose(new(id, Barbarian, "barbarian-subclass", [authored.Path]));

        var republished = Publish(temp.App, PathDraft(authored.Source, authored.Path.ContentId, authored.Ward, authored.Lore, initiative: 3));

        Assert.Equal(2, temp.App.GetCharacter(id).Sheet.Field(FieldIds.Initiative).Value); // still pinned to the old revision
        var affected = Assert.Single(temp.App.AffectedCharacters(authored.Path.ContentId));
        Assert.Equal((id, ReferenceRole.Choice), (affected.CharacterId, affected.Role));
        var review = temp.App.ReviewUpdate(id, authored.Path, republished);
        Assert.Contains(review.Fields, f => f.Field == FieldIds.Initiative && (f.Before, f.After) == (2, 4));
        Assert.Contains(review.Mechanics.Effects, e => e.EffectId == "storm-initiative" && e.Change == ChangeKind.Changed);

        var updated = temp.App.ApplyUpdate(id, authored.Path, republished, confirm: true);
        Assert.Equal(4, updated.Sheet.Field(FieldIds.Initiative).Value);
        Assert.Contains(updated.Character.Choices, c => c.Selected.Contains(republished));
    }

    [Fact]
    public void Homebrew_travels_in_a_backup_but_is_left_out_of_a_share_by_default()
    {
        using var temp = new TempApp();
        var authored = Author(temp.App);
        var id = Brenna(temp.App);
        var view = temp.App.Choose(new(id, Barbarian, "barbarian-subclass", [authored.Path]));

        using var restored = new TempApp();
        var backup = temp.App.ExportCharacters([id], ExportPurpose.Backup).Content;
        restored.App.ApplyImport(backup);
        Assert.Equal(TempApp.Json(view.Sheet), TempApp.Json(restored.App.GetCharacter(id).Sheet));

        // The exported revisions (written in the current content schema) and the character match docs/schemas.
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(backup));
        var path = zip.GetEntry($"content/{authored.Path.RevisionId:D}.json")!;
        using (var document = System.Text.Json.JsonDocument.Parse(new StreamReader(path.Open()).ReadToEnd()))
        {
            Assert.Equal(ContentRevision.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("", SchemaTests.Validate("content-revision", document.RootElement));
        }
        using (var character = System.Text.Json.JsonDocument.Parse(new StreamReader(zip.GetEntry($"characters/{id:D}.json")!.Open()).ReadToEnd()))
            Assert.Equal("", SchemaTests.Validate("character", character.RootElement));

        var share = temp.App.PreviewExport([id], ExportPurpose.Share);
        Assert.Contains(share.Omitted, o => o.SourceId == authored.Source.Id);
    }

    [Fact]
    public void The_studio_lists_every_revision_of_the_source_in_order()
    {
        using var temp = new TempApp();
        var authored = Author(temp.App);

        var entries = temp.App.ContentBySource(authored.Source.Id);

        Assert.Equal(3, entries.Count);
        var path = Assert.Single(entries, e => e.ContentId == authored.Path.ContentId);
        Assert.Equal([RevisionStatus.Draft, RevisionStatus.Published], path.Revisions.Select(r => r.Status));
        Assert.Equal(authored.Path, path.LatestPublished!.Reference);
    }
}
