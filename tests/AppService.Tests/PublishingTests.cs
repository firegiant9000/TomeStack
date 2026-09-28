using System.Text.Json;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M1 item 2 (SPEC I-06, ADR-002, ADR-004): a validated draft is published as a new immutable revision; authors see the
/// affected characters; a character adopts the new revision only after a reviewed diff and an explicit confirmation.
/// </summary>
public class PublishingTests
{
    private static readonly Guid Source = Guid.Parse("7c000000-0000-4000-8000-000000000001");
    private static readonly Guid ContentId = Guid.Parse("7c00c000-0000-4000-8000-000000000001");

    /// <summary>Homebrew v1: +1 initiative. Published directly, as if created and published earlier.</summary>
    private static readonly ContentRevision Version1 = new()
    {
        ContentId = ContentId,
        RevisionId = Guid.Parse("7c00e000-0000-4000-8000-000000000001"),
        Kind = ContentKind.Feat,
        Name = "Homebrew Alertness",
        RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
        Provenance = new(Source, new PageRef(3)),
        Status = RevisionStatus.Published,
        Effects = [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" }],
    };

    private static ContentRevision Draft2 => Version1 with
    {
        RevisionId = Guid.Parse("7c00e000-0000-4000-8000-000000000002"),
        Status = RevisionStatus.Draft,
        Provenance = new(Source, new PageRef(4)),
        Effects =
        [
            new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "PB" },
            new GrantEffect { Id = "perception", Grant = GrantKind.Proficiency, Target = FieldIds.Skill("perception") },
        ],
    };

    private static (TempApp Temp, Guid First, Guid Second) Setup()
    {
        var temp = new TempApp();
        temp.App.Store.UpsertSource(new SourceRecord
        {
            Id = Source, Title = "My Homebrew", Publisher = "Me", RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
            EditionVersion = "1", License = "Personal homebrew", Redistributable = false,
        });
        temp.App.Store.AddRevision(Version1);
        var character = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        var first = temp.App.SaveCharacter(character with { Pins = [.. character.Pins, Version1.Reference], Overrides = [new(FieldIds.Initiative, 9, "Table ruling")] }).Character.Id;
        var second = temp.App.SaveCharacter(character with { Id = Guid.NewGuid(), Name = "Second", Pins = [Version1.Reference] }).Character.Id;
        temp.App.SaveCharacter(character with { Id = Guid.NewGuid(), Name = "Unrelated" });
        return (temp, first, second);
    }

    [Fact]
    public void Publishing_a_valid_draft_adds_a_new_immutable_revision_and_leaves_the_draft_and_the_old_revision_alone()
    {
        var (temp, _, _) = Setup();
        using var _t = temp;
        var oldHash = temp.App.Store.RevisionHash(Version1.RevisionId);

        var draft = temp.App.SaveDraft(Draft2);
        var result = temp.App.Publish(draft);

        Assert.Equal(ContentId, result.Published.ContentId);
        Assert.NotEqual(draft.RevisionId, result.Published.RevisionId);
        Assert.True(result.Report.CanPublish);
        var published = temp.App.Store.FindRevision(result.Published)!;
        Assert.Equal(RevisionStatus.Published, published.Status);
        Assert.Equal(Draft2.Effects.Select(e => e.Id), published.Effects.Select(e => e.Id));
        Assert.Equal(RevisionStatus.Draft, temp.App.Store.FindRevision(draft)!.Status);
        Assert.Equal(oldHash, temp.App.Store.RevisionHash(Version1.RevisionId));
        Assert.Equal([Version1.RevisionId, draft.RevisionId, result.Published.RevisionId], temp.App.ListRevisions(ContentId).Select(r => r.RevisionId));
        Assert.Equal(2, result.Affected.Count);
    }

    [Fact]
    public void Publishing_writes_the_lowest_content_schema_the_revision_needs()
    {
        // M2.2: homebrew that uses no newer field stays readable by older builds. The draft keeps its version.
        var (temp, _, _) = Setup();
        using var _t = temp;
        Assert.Equal(ContentRevision.CurrentSchemaVersion, Draft2.SchemaVersion);

        var plain = temp.App.Publish(temp.App.SaveDraft(Draft2));
        Assert.Equal(3, temp.App.Store.FindRevision(plain.Published)!.SchemaVersion);
        Assert.Equal(ContentRevision.CurrentSchemaVersion, temp.App.Store.FindRevision(plain.Draft)!.SchemaVersion);
        Assert.Equal(3, plain.Report.RequiredSchemaVersion);

        var rolled = Draft2 with
        {
            RevisionId = Guid.NewGuid(),
            Effects = [new RollEffect { Id = "roll", RollId = "roll", Label = "Knack", Dice = "1d4", Bonus = "PB" }],
        };
        var v8 = temp.App.Publish(temp.App.SaveDraft(rolled));
        Assert.Equal(ContentRevision.CombatDetailsSchemaVersion, temp.App.Store.FindRevision(v8.Published)!.SchemaVersion);
    }

    [Fact]
    public void Publishing_refuses_an_invalid_draft_or_a_published_revision_and_adds_nothing()
    {
        var (temp, _, _) = Setup();
        using var _t = temp;
        var broken = temp.App.SaveDraft(Draft2 with
        {
            RevisionId = Guid.NewGuid(),
            Effects = [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "PB +" }],
        });
        var count = temp.App.Store.ListRevisions().Count;

        var invalid = Assert.Throws<AppValidationException>(() => temp.App.Publish(broken));
        var published = Assert.Throws<AppValidationException>(() => temp.App.Publish(Version1.Reference));

        Assert.Equal(["content.validation-failed", "validate.formula-invalid"], invalid.Problems.Select(p => p.Code));
        Assert.Equal("content.not-a-draft", Assert.Single(published.Problems).Code);
        Assert.Equal(count, temp.App.Store.ListRevisions().Count);
        Assert.Equal("content.draft-required", Assert.Single(Assert.Throws<AppValidationException>(() => temp.App.SaveDraft(Version1 with { RevisionId = Guid.NewGuid() })).Problems).Code);
    }

    [Fact]
    public void Affected_characters_are_listed_with_how_they_use_the_content()
    {
        var (temp, first, second) = Setup();
        using var _t = temp;

        var affected = temp.App.AffectedCharacters(ContentId);

        Assert.Equal(new[] { first, second }.Order(), affected.Select(a => a.CharacterId).Order());
        Assert.All(affected, a => Assert.Equal((Version1.Reference, ReferenceRole.Pin), (a.Pinned, a.Role)));
        Assert.DoesNotContain(affected, a => a.Name == "Unrelated");
    }

    [Fact]
    public void Review_shows_the_mechanics_diff_and_recalculated_fields_and_changes_nothing()
    {
        var (temp, first, _) = Setup();
        using var _t = temp;
        var published = temp.App.Publish(temp.App.SaveDraft(Draft2)).Published;
        var before = TempApp.Json(temp.App.Store.FindCharacter(first));

        var review = temp.App.ReviewUpdate(first, Version1.Reference, published);

        Assert.Equal(TempApp.Json(temp.App.Store.FindCharacter(first)), before);
        Assert.Contains(review.Mechanics.Effects, e => e.EffectId == "init" && e.Change == ChangeKind.Changed && e.Before!.Contains("\"1\"", StringComparison.Ordinal) && e.After!.Contains("\"PB\"", StringComparison.Ordinal));
        Assert.Contains(review.Mechanics.Effects, e => e.EffectId == "perception" && e.Change == ChangeKind.Added);
        Assert.Contains(review.Mechanics.Properties, p => p.Property == "page" && p.Before == "p. 3" && p.After == "p. 4");
        // Level 1 (PB 2): initiative bonus 1 -> 2; the override stays the displayed value, so initiative does not move.
        Assert.Contains(review.Fields, f => f.Field == FieldIds.Skill("perception") && f.After == f.Before + 2);
        Assert.DoesNotContain(review.Fields, f => f.Field == FieldIds.Initiative);
        Assert.Equal(FieldIds.Initiative, Assert.Single(review.AffectedOverrides).Field);
    }

    [Fact]
    public void Applying_needs_explicit_confirmation_and_then_moves_the_pin_and_keeps_overrides()
    {
        var (temp, first, second) = Setup();
        using var _t = temp;
        var published = temp.App.Publish(temp.App.SaveDraft(Draft2)).Published;
        var dispatcher = new CommandDispatcher(temp.App);
        string Apply(bool confirm) => dispatcher.Dispatch(JsonSerializer.Serialize(new
        {
            id = "1", command = "character.applyUpdate",
            payload = new { characterId = first, from = Version1.Reference, to = published, confirm },
        }, RulesJson.Compact));

        var refused = JsonDocument.Parse(Apply(false)).RootElement;
        Assert.False(refused.GetProperty("ok").GetBoolean());
        Assert.Equal("update.confirmation-required", refused.GetProperty("error").GetProperty("diagnostics")[0].GetProperty("code").GetString());
        Assert.Contains(Version1.Reference, temp.App.Store.FindCharacter(first)!.Pins);

        Assert.True(JsonDocument.Parse(Apply(true)).RootElement.GetProperty("ok").GetBoolean());
        var updated = temp.App.GetCharacter(first);
        Assert.Contains(published, updated.Character.Pins);
        Assert.DoesNotContain(Version1.Reference, updated.Character.Pins);
        Assert.Equal(9, updated.Sheet.Field(FieldIds.Initiative).Value);
        Assert.Equal(new FieldOverride(FieldIds.Initiative, 9, "Table ruling"), updated.Sheet.Field(FieldIds.Initiative).Override);
        // Only the character that opted in moved.
        Assert.Contains(Version1.Reference, temp.App.Store.FindCharacter(second)!.Pins);
    }

    [Fact]
    public void Updates_are_refused_across_content_to_drafts_or_from_unused_revisions()
    {
        var (temp, first, _) = Setup();
        using var _t = temp;
        var draft = temp.App.SaveDraft(Draft2);
        var other = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json").Pins[0];

        string Code(Action act) => Assert.Throws<AppValidationException>(act).Problems[0].Code;

        Assert.Equal("update.not-published", Code(() => temp.App.ReviewUpdate(first, Version1.Reference, draft)));
        Assert.Equal("update.different-content", Code(() => temp.App.ReviewUpdate(first, Version1.Reference, other)));
        Assert.Equal("update.not-referenced", Code(() => temp.App.ReviewUpdate(first, new(ContentId, Guid.NewGuid()), Version1.Reference)));
    }

    [Fact]
    public void A_class_update_carries_choices_over_to_the_new_revision()
    {
        using var temp = new TempApp();
        var pack = temp.AddPack("fixture-pack-m1.json");
        var warden = pack.Revisions.Single(r => r.Name == "Fixture Warden");
        var athletics = pack.Revisions.Single(r => r.Name == "Fixture Warden Skill: Athletics").Reference;
        var survival = pack.Revisions.Single(r => r.Name == "Fixture Warden Skill: Survival").Reference;
        var character = TempApp.LoadFixture<Character>("characters/srd521-ash-m1.json") with { Pins = [], Classes = [new(warden.Reference, 1)] };
        var id = temp.App.SaveCharacter(character).Character.Id;
        temp.App.Choose(new(id, warden.Reference, "warden-skills", [athletics, survival]));
        var newWarden = temp.App.Publish(temp.App.SaveDraft(warden with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, Summary = "Revised" })).Published;

        var review = temp.App.ReviewUpdate(id, warden.Reference, newWarden);
        var updated = temp.App.ApplyUpdate(id, warden.Reference, newWarden, confirm: true);

        Assert.Empty(review.UnresolvedChoices);
        Assert.Empty(review.Fields);
        Assert.Equal(newWarden, Assert.Single(updated.Character.Choices).Source);
        Assert.Equal(newWarden, Assert.Single(updated.Character.Classes).Class);
        Assert.All(updated.Sheet.Choices!, c => Assert.True(c.Resolved));
    }

    [Fact]
    public void An_updated_character_round_trips_through_a_package_to_a_clean_machine()
    {
        var (temp, first, _) = Setup();
        using var _t = temp;
        var published = temp.App.Publish(temp.App.SaveDraft(Draft2)).Published;
        temp.App.ApplyUpdate(first, Version1.Reference, published, confirm: true);
        var view = temp.App.GetCharacter(first);

        var export = temp.App.ExportCharacters([first]);
        using var destination = new TempApp();
        destination.App.ApplyImport(export.Content);

        Assert.Contains(export.Manifest.Entries, e => e.Path == $"content/{published.RevisionId:D}.json");
        Assert.DoesNotContain(export.Manifest.Entries, e => e.Path == $"content/{Version1.RevisionId:D}.json");
        Assert.Equal(TempApp.Json(view.Sheet), TempApp.Json(destination.App.GetCharacter(first).Sheet));
        Assert.Equal(RevisionStatus.Published, destination.App.Store.FindRevision(published)!.Status);
    }
}
