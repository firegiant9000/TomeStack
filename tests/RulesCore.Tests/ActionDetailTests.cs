using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// D43 slice 5, Task 5.2: the save DC, area, range, targets and per-unit dice of a roll on the calculated sheet. Original fixtures:
/// the action details pack and <c>characters/pm-wave-adept-l5.json</c> (level 5, PB 3, Wis 14 so +2). Variants are built in memory.
/// </summary>
public class ActionDetailTests
{
    private static Character Adept() => Fixtures.Load("pm-wave-adept-l5.json");

    private static CharacterSheet Sheet(Character character, InMemoryContentCatalog? catalog = null) =>
        CharacterCalculator.Calculate(character, catalog ?? Fixtures.PlayMechanicsCatalog());

    private static FeatureEffect Effect(CharacterSheet sheet, string id) =>
        sheet.Features!.Single(f => f.Content == Fixtures.TideWave).Effects.Single(e => e.Id == id);

    private static InMemoryContentCatalog WithWave(Func<RollEffect, RollEffect> change)
    {
        var pack = Fixtures.ActionDetailsPack();
        var revisions = pack.Revisions
            .Select(r => r.Reference == Fixtures.TideWave
                ? r with { Effects = [.. r.Effects.Select(e => e is RollEffect { Id: "wave" } roll ? change(roll) : e)] }
                : r)
            .ToList();
        return Catalog(pack.Sources, revisions);
    }

    private static InMemoryContentCatalog Catalog(IEnumerable<SourceRecord> actionSources, IEnumerable<ContentRevision> actionRevisions)
    {
        ContentPack[] others = [Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack(), Fixtures.TurnsPack(), Fixtures.FeatureAttacksPack()];
        return new([.. others.SelectMany(p => p.Sources), .. actionSources], [.. others.SelectMany(p => p.Revisions), .. actionRevisions]);
    }

    [Fact]
    public void A_save_dc_is_calculated_and_traced()
    {
        // VALUE is Task 5.4, so the variant reads the Wisdom modifier directly: 8 + PB 3 + WIS.MOD 2.
        var catalog = WithWave(r => r with { Save = new RollSave(Ability.Dex, "8 + PB + WIS.MOD") });

        var wave = Effect(Sheet(Adept(), catalog), "wave");

        Assert.Equal(Ability.Dex, wave.SaveAbility);
        Assert.Equal(13, wave.SaveDc);
        Assert.NotNull(wave.SaveTrace);
        var step = Assert.Single(wave.SaveTrace!);
        Assert.Equal(13, step.Result);
        Assert.Contains("Proficiency bonus", step.Description, StringComparison.Ordinal);
        Assert.Contains("Wisdom modifier", step.Description, StringComparison.Ordinal);
        Assert.Contains(step.Inputs!, i => i.Name == "PB" && i.Value == 3);
        Assert.Contains(step.Inputs!, i => i.Name == "WIS.MOD" && i.Value == 2);
        Assert.Equal(AutomationStatus.Automatic, wave.Automation);
    }

    [Fact]
    public void A_save_dc_that_cannot_be_calculated_is_null_with_a_warning_and_the_row_is_assisted()
    {
        // The pack's own dc reads VALUE, which resolves only after Task 5.4: it fails like any unresolved identifier.
        var sheet = Sheet(Adept());
        var wave = Effect(sheet, "wave");

        Assert.Equal(Ability.Dex, wave.SaveAbility);
        Assert.Null(wave.SaveDc);
        Assert.Null(wave.SaveTrace);
        Assert.Equal(AutomationStatus.Assisted, wave.Automation);
        Assert.True(wave.Available);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.Content == Fixtures.TideWave && d.EffectId == "wave");
    }

    [Fact]
    public void Area_and_range_are_shown_and_never_automated()
    {
        var wave = Effect(Sheet(Adept()), "wave");

        Assert.Equal(new RollArea(AreaShape.Line, 100, 5), wave.Area);
        Assert.Equal("Self", wave.Range);
        Assert.Equal("Each creature in the line", wave.Targets);
        Assert.Equal(new RollPerUnit("1d10", 4), wave.PerUnit);

        // Without content 53 (the class no longer grants it) only the feature entry differs.
        var pack = Fixtures.ActionDetailsPack();
        var revisions = pack.Revisions
            .Select(r => r.Reference == Fixtures.WaveAdept ? r with { Effects = [.. r.Effects.Where(e => e.Id != "wave-tide")] } : r)
            .ToList();
        var with = Sheet(Adept());
        var without = Sheet(Adept(), Catalog(pack.Sources, revisions));

        Assert.Contains(with.Features!, f => f.Content == Fixtures.TideWave);
        Assert.DoesNotContain(without.Features!, f => f.Content == Fixtures.TideWave);
        static string Json(object? o) => JsonSerializer.Serialize(o, RulesJson.Options);
        Assert.Equal(Json(without.Fields), Json(with.Fields));
        Assert.Equal(Json(without.Resources), Json(with.Resources));
        Assert.Equal(Json(without.Attacks), Json(with.Attacks));
        Assert.Equal(Json(without.Toggles), Json(with.Toggles));
        Assert.Equal(Json(without.HitPoints), Json(with.HitPoints));
        Assert.Equal(Json(without.Spellcasting), Json(with.Spellcasting));
        Assert.Equal(Json(without.Upkeeps), Json(with.Upkeeps));
        // The feature list differs by the Tide Wave entry, and by the class's own grant of it (the variant drops that effect to drop the feature).
        static IEnumerable<FeatureEntry> Others(CharacterSheet s) =>
            s.Features!.Where(f => f.Content != Fixtures.TideWave && f.Content != Fixtures.WaveAdept);
        Assert.Equal(Json(Others(without)), Json(Others(with)));
        Assert.Equal(
            Json(without.Features!.Single(f => f.Content == Fixtures.WaveAdept).Effects),
            Json(with.Features!.Single(f => f.Content == Fixtures.WaveAdept).Effects.Where(e => e.Id != "wave-tide")));
    }

    [Fact]
    public void A_dice_less_action_is_listed_with_its_cost()
    {
        var brace = Effect(Sheet(Adept()), "brace");

        Assert.Null(brace.Dice);
        Assert.Equal(2, brace.Cost);
        Assert.Null(brace.SaveAbility);
        Assert.Null(brace.SaveDc);
        Assert.Null(brace.Area);
        Assert.Equal(Activation.Reaction, brace.Activation);
    }

    [Fact]
    public void A_failing_roll_cost_is_reported_not_silent()
    {
        var catalog = WithWave(r => r with { Cost = "SCALE.nonesuch" });
        var sheet = Sheet(Adept(), catalog);

        var wave = Effect(sheet, "wave");

        Assert.Null(wave.Cost);
        Assert.True(wave.Available);
        Assert.Equal(AutomationStatus.Assisted, wave.Automation);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.Content == Fixtures.TideWave && d.EffectId == "wave");
    }
}
