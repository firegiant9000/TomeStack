using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M2 item 2: resource maximums are calculated in the rules core with a trace, the sheet lists every active revision as a
/// feature with its automation status, and play state (character schema v4) is read, defaulted and validated.
/// </summary>
public class ResourceAndFeatureTests
{
    private static readonly ContentReference Feat = new(Guid.Parse("5f2dc000-0000-4000-8000-000000000001"), Guid.Parse("5f2de000-0000-4000-8000-000000000001"));
    private static readonly ContentReference Notes = new(Guid.Parse("5f2dc000-0000-4000-8000-000000000002"), Guid.Parse("5f2de000-0000-4000-8000-000000000002"));

    private static ContentRevision Revision(ContentReference reference, string name, params Effect[] effects) => new()
    {
        ContentId = reference.ContentId,
        RevisionId = reference.RevisionId,
        Kind = ContentKind.Feat,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(Fixtures.Source2014, new(7)),
        Status = RevisionStatus.Published,
        Effects = effects,
    };

    private static (Character Character, InMemoryContentCatalog Catalog) Setup(PlayState? play = null)
    {
        var pack = Fixtures.Pack();
        var feat = Revision(
            Feat,
            "Test Stamina",
            new ResourceEffect { Id = "uses", ResourceId = "stamina", Label = "Stamina", Maximum = "PB + 1" },
            new RecoveryEffect { Id = "uses-long", ResourceId = "stamina", On = RestPeriod.LongRest, Amount = "all", Timing = EffectTiming.OnLongRest },
            new ResourceEffect { Id = "broken", ResourceId = "broken", Label = "Broken", Maximum = "PB +" },
            new ResourceEffect { Id = "outside", ResourceId = "outside", Label = "Needs a class", Maximum = "CLASS_LEVEL" },
            new ResourceEffect { Id = "manual", ResourceId = "manual", Label = "Tracked by hand", Maximum = "1", Automation = AutomationStatus.Reference },
            new RollEffect { Id = "burst", RollId = "burst", Label = "Burst", Dice = "1d6", ResourceId = "stamina", Automation = AutomationStatus.Assisted, Timing = EffectTiming.OnRoll });
        var notes = Revision(Notes, "Test Notes"); // no effects: text only
        var fixture = Fixtures.Srd51Character();
        var character = fixture with { Pins = [.. fixture.Pins, Feat, Notes], Play = play ?? new() };
        return (character, new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, feat, notes]));
    }

    [Fact]
    public void A_resource_maximum_is_its_formula_with_a_trace_citing_the_content()
    {
        var (character, catalog) = Setup();

        var sheet = CharacterCalculator.Calculate(character, catalog);

        var stamina = Assert.Single(sheet.Resources!, r => r.ResourceId == "stamina");
        Assert.Equal(3, stamina.Maximum); // PB 2 at level 1, + 1
        Assert.Equal(3, stamina.Current);
        Assert.Equal(AutomationStatus.Automatic, stamina.Automation);
        var step = Assert.Single(stamina.Trace);
        Assert.Equal("Test Stamina", step.Origin.ContentName);
        Assert.Equal(new PageRef(7), step.Origin.Page);
        Assert.Contains(step.Inputs!, i => i.Name == "PB" && i.Value == 2);
        Assert.Equal(RestPeriod.LongRest, Assert.Single(stamina.Recoveries).On);
    }

    [Fact]
    public void A_failing_or_unavailable_formula_disables_only_that_resource_and_the_sheet_still_calculates()
    {
        var (character, catalog) = Setup();

        var sheet = CharacterCalculator.Calculate(character, catalog);

        var broken = Assert.Single(sheet.Resources!, r => r.ResourceId == "broken");
        Assert.Null(broken.Maximum);
        Assert.Equal(AutomationStatus.Assisted, broken.Automation);
        Assert.Contains(broken.Warnings, w => w.Code == "effect.invalid-formula" && w.EffectId == "broken");
        var outside = Assert.Single(sheet.Resources!, r => r.ResourceId == "outside");
        Assert.Contains(outside.Warnings, w => w.Code == "effect.invalid-formula" && w.Message.Contains("CLASS_LEVEL", StringComparison.Ordinal));
        var manual = Assert.Single(sheet.Resources!, r => r.ResourceId == "manual");
        Assert.Equal(AutomationStatus.Reference, manual.Automation);
        Assert.Null(manual.Current);
        Assert.Equal(3, sheet.Resources!.Single(r => r.ResourceId == "stamina").Maximum);

        // The feature says it is not fully automated and names the problems.
        var feature = Assert.Single(sheet.Features!, f => f.Content == Feat);
        Assert.Equal(AutomationStatus.Reference, feature.Automation); // it has a reference-only effect
        Assert.Contains(feature.Diagnostics, d => d.EffectId == "broken");
    }

    [Fact]
    public void Features_list_every_active_revision_with_text_rolls_and_automation()
    {
        var (character, catalog) = Setup();

        var sheet = CharacterCalculator.Calculate(character, catalog);

        Assert.Equal(sheet.Active!, sheet.Features!.Select(f => f.Content));
        var roll = Assert.Single(sheet.Features!.Single(f => f.Content == Feat).Effects, e => e.Type == "roll");
        Assert.Equal(("1d6", "stamina"), (roll.Dice, roll.ResourceId));
        var notes = Assert.Single(sheet.Features!, f => f.Content == Notes);
        Assert.Equal(AutomationStatus.Reference, notes.Automation); // pure text is never shown as automated
        var quickfoot = Assert.Single(sheet.Features!, f => f.Content == Fixtures.Quickfoot);
        Assert.Equal(AutomationStatus.Automatic, quickfoot.Automation);
        Assert.Equal("TomeStack Fixtures: 2014 Family", quickfoot.Origin.SourceTitle);
    }

    [Fact]
    public void Spent_uses_and_hit_points_come_from_the_play_state_and_are_clamped_for_display()
    {
        var play = new PlayState { CurrentHitPoints = 999, TemporaryHitPoints = 4 }.WithSpent(Feat.ContentId, "stamina", 5);
        var (character, catalog) = Setup(play);

        var sheet = CharacterCalculator.Calculate(character, catalog);

        var stamina = sheet.Resources!.Single(r => r.ResourceId == "stamina");
        Assert.Equal((5, 0), (stamina.Spent, stamina.Current)); // more spent than the maximum: none left, never negative
        Assert.Equal(sheet.Field(FieldIds.HitPoints).Value, sheet.HitPoints!.Maximum);
        Assert.Equal(sheet.HitPoints.Maximum, sheet.HitPoints.Current);
        Assert.Equal(4, sheet.HitPoints.Temporary);
    }

    [Fact]
    public void A_v3_character_reads_as_the_current_schema_with_a_fresh_play_state()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "characters", "m1-acceptance-srd51-korga.json"));
        Assert.Contains("\"schemaVersion\": 3", json, StringComparison.Ordinal);

        var character = JsonSerializer.Deserialize<Character>(json, RulesJson.Options)!;

        Assert.Equal(Character.CurrentSchemaVersion, character.SchemaVersion);
        Assert.Equal(new PlayState().CurrentHitPoints, character.Play.CurrentHitPoints);
        Assert.Empty(character.Play.Resources);
        Assert.Empty(character.Validate());
    }

    [Fact]
    public void A_v4_character_reads_as_the_current_schema_with_no_hit_dice_spent_no_death_saves_no_inspiration_and_no_spells()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "characters", "m1-acceptance-srd51-korga.json"))
            .Replace("\"schemaVersion\": 3", "\"schemaVersion\": 4", StringComparison.Ordinal)
            .Replace("\"rulesFamily\"", "\"play\": { \"currentHitPoints\": 7, \"temporaryHitPoints\": 0, \"resources\": [], \"conditions\": [], \"exhaustion\": 1 }, \"rulesFamily\"", StringComparison.Ordinal);

        var character = JsonSerializer.Deserialize<Character>(json, RulesJson.Options)!;

        Assert.Equal(Character.CurrentSchemaVersion, character.SchemaVersion);
        Assert.Equal((7, 1), (character.Play.CurrentHitPoints, character.Play.Exhaustion));
        Assert.Equal((0, new DeathSaves(), false), (character.Play.HitDiceSpent.Count, character.Play.DeathSaves, character.Play.Inspiration));
        Assert.Equal((0, 0, 0), (character.Spells.Count, character.Play.SpellSlotsSpent.Count, character.Play.PactSlotsSpent));
        Assert.Empty(character.Validate());
    }

    [Fact]
    public void Invalid_play_state_is_refused()
    {
        var character = Fixtures.Srd51Character() with
        {
            Play = new PlayState { CurrentHitPoints = -1, Exhaustion = 7, Conditions = ["prone", "prone", "sleepy"] },
        };

        var codes = character.Validate().Select(d => d.Code).ToList();

        Assert.Contains("play.hit-points-out-of-range", codes);
        Assert.Contains("play.exhaustion-out-of-range", codes);
        Assert.Contains("play.condition-unknown", codes);
        Assert.Contains("play.condition-duplicate", codes);
        Assert.Equal("character.empty-entry", Assert.Single((character with { Play = null! }).Validate()).Code);
    }
}
