using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// D43 slice 5, Task 5.4: <c>VALUE.&lt;id&gt;</c> resolves within a class root. The pack's wave save DC reads
/// <c>VALUE.focusMod</c>, which the chosen focus feature declares (Wisdom 14 so +2, Constitution 12 so +1). Variants are built in memory.
/// </summary>
public class NamedValueTests
{
    private static readonly ContentReference PinnedWave = new(Guid.Parse("6f0dc000-0000-4000-8000-000000000054"), Guid.Parse("6f0de000-0000-4000-8000-000000000054"));

    private static Character Adept() => Fixtures.Load("pm-wave-adept-l5.json");

    private static CharacterSheet Sheet(Character character, InMemoryContentCatalog? catalog = null) =>
        CharacterCalculator.Calculate(character, catalog ?? Fixtures.PlayMechanicsCatalog());

    private static InMemoryContentCatalog Catalog(Func<ContentRevision, ContentRevision> change)
    {
        var pack = Fixtures.ActionDetailsPack();
        ContentPack[] others = [Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack(), Fixtures.TurnsPack(), Fixtures.FeatureAttacksPack()];
        return new([.. others.SelectMany(p => p.Sources), .. pack.Sources], [.. others.SelectMany(p => p.Revisions), .. pack.Revisions.Select(change), .. ExtraRevisions(pack)]);
    }

    /// <summary>A copy of Tide Wave under another id, so a test can pin it outside any class.</summary>
    private static IEnumerable<ContentRevision> ExtraRevisions(ContentPack pack) =>
        [pack.Revisions.Single(r => r.Reference == Fixtures.TideWave) with { ContentId = PinnedWave.ContentId, RevisionId = PinnedWave.RevisionId, Name = "Fixture Pinned Wave" }];

    private static ValueEffect Value(string id, string valueId, string formula) => new() { Id = id, ValueId = valueId, Formula = formula };

    private static ContentRevision WithEffects(ContentRevision revision, ContentReference target, Func<IReadOnlyList<Effect>, IEnumerable<Effect>> change) =>
        revision.Reference == target ? revision with { Effects = [.. change(revision.Effects)] } : revision;

    private static FeatureEffect Wave(CharacterSheet sheet, ContentReference content) =>
        sheet.Features!.Single(f => f.Content == content).Effects.Single(e => e.Id == "wave");

    [Fact]
    public void A_named_value_from_a_class_choice_is_read_by_its_techniques()
    {
        var sheet = Sheet(Adept());
        var wave = Wave(sheet, Fixtures.TideWave);

        Assert.Equal(13, wave.SaveDc); // 8 + PB 3 + the Wisdom value 2
        Assert.Equal(AutomationStatus.Automatic, wave.Automation);
        var step = Assert.Single(wave.SaveTrace!);
        Assert.Contains("Fixture Focus of Mind", step.Description, StringComparison.Ordinal);
        Assert.Contains(step.Inputs!, i => i.Name == "VALUE.focusMod" && i.Value == 2);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code.StartsWith("value.", StringComparison.Ordinal) || d.Code == "effect.invalid-formula");

        var body = Adept() with
        {
            Choices = [new(Fixtures.WaveAdept, "focusAbility", [Fixtures.FocusOfBody])],
        };
        var bodyWave = Wave(Sheet(body), Fixtures.TideWave);
        Assert.Equal(12, bodyWave.SaveDc); // the Constitution value 1
        Assert.Contains("Fixture Focus of Body", Assert.Single(bodyWave.SaveTrace!).Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_value_cycle_is_disabled_with_a_diagnostic()
    {
        var catalog = Catalog(r => WithEffects(r, Fixtures.FocusOfMind, _ =>
            [Value("a", "a", "VALUE.b + 1"), Value("b", "b", "VALUE.a"), Value("focus-mod", "focusMod", "VALUE.a + 1")]));

        var sheet = Sheet(Adept(), catalog);
        var wave = Wave(sheet, Fixtures.TideWave);

        Assert.Null(wave.SaveDc);
        Assert.Equal(AutomationStatus.Assisted, wave.Automation);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "value.cycle");
        // focusMod reads the cycle, so it is disabled with it and its reader (the wave) hears value.cycle too.
        Assert.Contains(sheet.Diagnostics, d => d.Code == "value.cycle" && d.Content == Fixtures.TideWave);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.Content == Fixtures.TideWave && d.EffectId == "wave");
    }

    /// <summary>A chain of <paramref name="links"/> values c0 to c{links-1}: each reads the next with <paramref name="read"/> (the next value's name is {0}), the last is PB; focusMod reads c0.</summary>
    private static Effect[] Chain(int links, string read)
    {
        var effects = new List<Effect> { Value("focus-mod", "focusMod", "VALUE.c0") };
        for (var i = 0; i < links; i++)
            effects.Add(Value($"c{i}", $"c{i}", i == links - 1 ? "PB" : string.Format(System.Globalization.CultureInfo.InvariantCulture, read, $"VALUE.c{i + 1}")));
        return [.. effects];
    }

    [Fact]
    public void A_long_chain_of_values_that_each_read_the_next_twice_is_evaluated_once_each()
    {
        // Each link reads the next three times: unmemoised that is 3^29 evaluations.
        var catalog = Catalog(r => WithEffects(r, Fixtures.FocusOfMind, _ => Chain(30, "{0} - {0} + {0}")));

        var sheet = Sheet(Adept(), catalog);

        Assert.Equal(8 + 3 + 3, Wave(sheet, Fixtures.TideWave).SaveDc);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "effect.invalid-formula");
    }

    [Fact]
    public void A_chain_of_values_nested_deeper_than_the_limit_is_disabled_with_the_formula_warning()
    {
        var catalog = Catalog(r => WithEffects(r, Fixtures.FocusOfMind, _ => Chain(40, "{0}")));

        var sheet = Sheet(Adept(), catalog);

        Assert.Null(Wave(sheet, Fixtures.TideWave).SaveDc);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.Content == Fixtures.TideWave && d.EffectId == "wave");
        // Within the limit the same shape works.
        Assert.Equal(8 + 3 + 3, Wave(Sheet(Adept(), Catalog(r => WithEffects(r, Fixtures.FocusOfMind, _ => Chain(25, "{0}")))), Fixtures.TideWave).SaveDc);
    }

    [Fact]
    public void A_modifier_reading_a_chain_deeper_than_the_limit_does_not_overflow_the_stack()
    {
        var catalog = Catalog(r => WithEffects(r, Fixtures.WaveAdept, effects =>
            [.. effects, .. Chain(3000, "{0}").Skip(1), new ModifierEffect { Id = "bump", Operation = ModifierOperation.Bonus, Target = FieldIds.Score(Ability.Str), Value = "VALUE.c0" }]));

        var sheet = Sheet(Adept(), catalog);

        Assert.Equal(14, sheet.Field(FieldIds.Score(Ability.Str)).Value); // the bonus is disabled, with a warning
        Assert.Contains(sheet.Field(FieldIds.Score(Ability.Str)).Warnings, w => w.Code == "effect.invalid-formula" && w.EffectId == "bump");
    }

    [Fact]
    public void A_value_that_reads_itself_is_a_cycle()
    {
        var catalog = Catalog(r => WithEffects(r, Fixtures.FocusOfMind, _ => [Value("focus-mod", "focusMod", "VALUE.focusMod + 1")]));

        var sheet = Sheet(Adept(), catalog);

        Assert.Null(Wave(sheet, Fixtures.TideWave).SaveDc);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "value.cycle");
    }

    [Fact]
    public void A_named_value_outside_a_class_is_unresolved_with_a_diagnostic()
    {
        var sheet = Sheet(Adept() with { Pins = [PinnedWave] }, Catalog(r => r));

        var pinned = Wave(sheet, PinnedWave);

        Assert.Null(pinned.SaveDc);
        Assert.Equal(AutomationStatus.Assisted, pinned.Automation);
        var problem = Assert.Single(sheet.Diagnostics, d => d.Code == "value.no-class");
        Assert.Equal(PinnedWave, problem.Content);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.Content == PinnedWave && d.EffectId == "wave");
        Assert.Equal(13, Wave(sheet, Fixtures.TideWave).SaveDc); // the class's own copy is unaffected
    }

    [Fact]
    public void A_named_value_nothing_declares_is_unknown()
    {
        var catalog = Catalog(r => WithEffects(r, Fixtures.FocusOfMind, _ => []));

        var sheet = Sheet(Adept(), catalog);

        Assert.Null(Wave(sheet, Fixtures.TideWave).SaveDc);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "value.unknown" && d.Content == Fixtures.TideWave);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.invalid-formula" && d.Content == Fixtures.TideWave && d.EffectId == "wave");
    }

    [Fact]
    public void Two_declarations_of_one_value_use_the_later_with_a_diagnostic()
    {
        // The class itself declares focusMod too (1); the chosen feature comes later in the sheet and its Wisdom value (2) applies.
        var catalog = Catalog(r => WithEffects(r, Fixtures.WaveAdept, effects => [.. effects, Value("class-focus", "focusMod", "1")]));

        var sheet = Sheet(Adept(), catalog);

        Assert.Equal(13, Wave(sheet, Fixtures.TideWave).SaveDc);
        var duplicate = Assert.Single(sheet.Diagnostics, d => d.Code == "value.duplicate");
        Assert.Equal(Fixtures.FocusOfMind, duplicate.Content);
        Assert.Null(duplicate.EffectId);
        Assert.Equal(AutomationStatus.Automatic, Wave(sheet, Fixtures.TideWave).Automation);
    }

    [Fact]
    public void A_value_reads_the_class_level_in_the_declaring_context()
    {
        var catalog = Catalog(r => WithEffects(r, Fixtures.FocusOfMind, _ => [Value("focus-mod", "focusMod", "CLASS_LEVEL + PB")]));

        Assert.Equal(8 + 3 + 5 + 3, Wave(Sheet(Adept(), catalog), Fixtures.TideWave).SaveDc);
    }

    [Fact]
    public void A_value_reading_the_field_its_modifier_targets_is_a_dependency_cycle()
    {
        var strength = FieldIds.Score(Ability.Str);
        var catalog = Catalog(r => WithEffects(r, Fixtures.WaveAdept, effects =>
        [
            .. effects,
            Value("a-value", "a", "STR.MOD"),
            new ModifierEffect { Id = "bump", Operation = ModifierOperation.Bonus, Target = strength, Value = "VALUE.a" },
        ]));

        var sheet = Sheet(Adept(), catalog);

        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.dependency-cycle" && d.Content == Fixtures.WaveAdept && d.EffectId == "bump");
        Assert.Equal(14, sheet.Field(strength).Value);
    }

    [Fact]
    public void A_modifier_reading_a_value_is_computed_after_the_fields_the_value_reads()
    {
        // Strength gains a value that reads the proficiency bonus. Strength sorts before PB on its own, so without the value's field
        // reads in the ordering the bonus would be read before PB is calculated and fail.
        var strength = FieldIds.Score(Ability.Str);
        var catalog = Catalog(r => WithEffects(r, Fixtures.WaveAdept, effects =>
        [
            .. effects,
            Value("x-value", "x", "PB"),
            new ModifierEffect { Id = "bump", Operation = ModifierOperation.Bonus, Target = strength, Value = "VALUE.x" },
        ]));

        var sheet = Sheet(Adept(), catalog);

        Assert.Equal(14 + 3, sheet.Field(strength).Value);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code is "effect.dependency-cycle" or "effect.invalid-formula" || d.Code.StartsWith("value.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_on_content_with_no_class_is_ignored()
    {
        // A feat pinned outside any class declares focusMod too, but no class can ever read it: no duplicate, no change.
        var catalog = Catalog(r => WithEffects(r, Fixtures.FocusOfMind, _ => [Value("focus-mod", "focusMod", "WIS.MOD")]));
        var sheet = Sheet(Adept() with { Pins = [Fixtures.FocusOfBody] }, catalog);

        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "value.duplicate");
        Assert.Equal(13, Wave(sheet, Fixtures.TideWave).SaveDc);
    }
}
