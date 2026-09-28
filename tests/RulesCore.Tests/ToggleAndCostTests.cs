using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M3 B2 (content schema v6, character schema v7): toggled (whileActive) modifiers, actions with a fixed or variable cost,
/// and a resource shared between features. Original fixtures: <c>fixture-pack-m3-effects.json</c>.
/// </summary>
public class ToggleAndCostTests
{
    private static Character Pinned(PlayState? play = null) =>
        Fixtures.Load("srd521-ash-m1.json") with { Pins = [Fixtures.RadiantStance, Fixtures.BorrowedSpark], Play = play ?? new PlayState() };

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.AllCatalog());

    [Fact]
    public void A_toggled_modifier_applies_only_while_its_toggle_is_on_and_is_automatic_either_way()
    {
        var off = Sheet(Pinned());
        var on = Sheet(Pinned(new PlayState { Toggles = [new(Fixtures.RadiantStance.ContentId, "stance")] }));

        var acOff = off.Field(FieldIds.ArmorClass);
        var acOn = on.Field(FieldIds.ArmorClass);
        Assert.Equal(2, acOn.Value - acOff.Value);
        Assert.Equal((AutomationStatus.Automatic, AutomationStatus.Automatic), (acOff.Automation, acOn.Automation)); // the rules decide; nothing is left to the player
        Assert.Contains(acOn.Trace, t => t.Origin.EffectId == "stance-ac");
        Assert.Equal((false, true), (off.Toggles!.Single().On, on.Toggles!.Single().On));
        Assert.Equal("radiance", off.Toggles!.Single().ResourceId);
    }

    [Fact]
    public void Costs_are_evaluated_and_a_shared_resource_names_its_defining_content()
    {
        var sheet = Sheet(Pinned());
        var effects = sheet.Features!.SelectMany(f => f.Effects).Where(e => e.Type == "roll").ToDictionary(e => e.Id);

        Assert.Equal((1, false), (effects["flare"].Cost!.Value, effects["flare"].VariableCost));
        Assert.Equal((3, true), (effects["surge"].Cost!.Value, effects["surge"].VariableCost)); // PB 3 at level 5
        Assert.Equal((Fixtures.RadiantStance.ContentId, 1), (effects["spark"].ResourceContent!.Value, effects["spark"].Cost!.Value)); // default cost 1
        Assert.Single(sheet.Resources!, r => r.ResourceId == "radiance"); // one pool, however many features spend it
    }

    [Fact]
    public void A_long_rest_proposes_switching_active_toggles_off()
    {
        var character = Pinned(new PlayState { Toggles = [new(Fixtures.RadiantStance.ContentId, "stance")] });
        var plan = RestPlanner.LongRest(character, Sheet(character));

        var change = Assert.Single(plan.Changes, c => c.Kind == RestChangeKind.Toggle);
        Assert.Equal(("Radiant stance", 1, 0), (change.Label, change.From, change.To));
        Assert.Empty(RestPlanner.Apply(character.Play, plan, new HashSet<string>()).Toggles);
        Assert.Single(RestPlanner.Apply(character.Play, plan, new HashSet<string> { change.Id }).Toggles); // unticked: it stays on
    }

    [Fact]
    public void Validation_checks_toggles_and_costs_and_refuses_them_below_v6()
    {
        var catalog = Fixtures.AllCatalog();
        var stance = catalog.FindRevision(Fixtures.RadiantStance)!;
        var modifier = stance.Effects.OfType<ModifierEffect>().Single();

        Assert.True(ContentValidator.Validate(stance, catalog).CanPublish);
        Assert.True(ContentValidator.Validate(catalog.FindRevision(Fixtures.BorrowedSpark)!, catalog).CanPublish);
        var unknownToggle = stance with { RevisionId = Guid.NewGuid(), Effects = [.. stance.Effects.Select(e => e == modifier ? modifier with { Toggle = "nope" } : e)] };
        var wrongTiming = stance with { RevisionId = Guid.NewGuid(), Effects = [.. stance.Effects.Select(e => e == modifier ? modifier with { Timing = EffectTiming.Always } : e)] };
        Assert.Contains(ContentValidator.Validate(unknownToggle, catalog).Errors, e => e.Code == "validate.toggle-unknown");
        Assert.Contains(ContentValidator.Validate(wrongTiming, catalog).Errors, e => e.Code == "validate.toggle-timing");
        Assert.Contains(ContentValidator.Validate(stance with { RevisionId = Guid.NewGuid(), SchemaVersion = 5 }, catalog).Errors, e => e.Code == "validate.requires-v6");
    }

    [Fact]
    public void A_toggle_effect_in_a_revision_older_than_v6_stays_unknown_and_byte_for_byte()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "fixture-pack-m3-effects.json"));
        var stance = JsonSerializer.Deserialize<ContentPack>(json, RulesJson.Options)!.Revisions.Single(r => r.Reference == Fixtures.RadiantStance);
        Assert.IsType<ToggleEffect>(stance.Effects[2]);

        var v5 = JsonSerializer.Serialize(stance, RulesJson.Options).Replace("\"schemaVersion\": 6", "\"schemaVersion\": 5", StringComparison.Ordinal);
        var old = JsonSerializer.Deserialize<ContentRevision>(v5, RulesJson.Options)!;

        Assert.IsType<UnknownEffect>(old.Effects[2]);
        Assert.Equal(v5, JsonSerializer.Serialize(old, RulesJson.Options));
    }

    [Fact]
    public void A_whileActive_modifier_without_a_toggle_stays_assisted()
    {
        var feat = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = "Test Untoggled Feat", RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(Fixtures.EffectsPack().Sources[0].Id, new(3)), Status = RevisionStatus.Published,
            Effects = [new ModifierEffect { Id = "ac", Operation = ModifierOperation.Bonus, Target = FieldIds.ArmorClass, Value = "1", Timing = EffectTiming.WhileActive }],
        };
        var pack = Fixtures.EffectsPack();
        var m1 = Fixtures.M1Pack();
        var catalog = new InMemoryContentCatalog([.. Fixtures.Pack().Sources, .. m1.Sources, .. pack.Sources], [.. Fixtures.Pack().Revisions, .. m1.Revisions, feat]);

        var sheet = CharacterCalculator.Calculate(Fixtures.Load("srd521-ash-m1.json") with { Pins = [feat.Reference] }, catalog);

        Assert.Equal(AutomationStatus.Assisted, sheet.Field(FieldIds.ArmorClass).Automation);
    }
}
