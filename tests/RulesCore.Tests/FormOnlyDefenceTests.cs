namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 6, Task 6.3 (D43): a fly speed and a damage defence that apply only while a form (toggle) is on, plus
/// regression tests that the global ability-score cap (20) already covers "+1, maximum 20" (controller ruling C1).
/// </summary>
public class FormOnlyDefenceTests
{
    private static readonly ActiveToggle SkyOn = new(Fixtures.SkyForm.ContentId, "sky");

    private static Character Instinct() => Fixtures.Load("pm-instinct-l4.json");

    private static Character WithDex(Character character, int dex) =>
        character with { BaseAbilities = character.BaseAbilities with { Dex = dex } };

    private static Character WithSky(Character character) =>
        character with { Play = character.Play with { Toggles = [SkyOn] } };

    private static CharacterSheet Sheet(Character character, InMemoryContentCatalog? catalog = null) =>
        CharacterCalculator.Calculate(character, catalog ?? Fixtures.PlayMechanicsCatalog());

    /// <summary>The play-mechanics catalog with one fixture revision's effects replaced (same ids).</summary>
    private static InMemoryContentCatalog CatalogWith(ContentReference reference, params Effect[] effects)
    {
        ContentPack[] packs = [Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack(), Fixtures.TurnsPack(), Fixtures.FeatureAttacksPack(), Fixtures.ActionDetailsPack(), Fixtures.RollModesPack()];
        var revisions = packs.SelectMany(p => p.Revisions).Select(r => r.Reference == reference ? r with { Effects = effects } : r).ToList();
        return new([.. packs.SelectMany(p => p.Sources)], revisions);
    }

    private static ModifierEffect DexModifier(string id, ModifierOperation operation, int amount) =>
        new() { Id = id, Operation = operation, Target = FieldIds.Score(Ability.Dex), Value = $"{amount}" };

    [Fact]
    public void The_global_cap_never_reduces_a_replaced_score()
    {
        var catalog = CatalogWith(Fixtures.LightStep, DexModifier("rep", ModifierOperation.Replace, 22));

        Assert.Equal(22, Sheet(Instinct(), catalog).Field(FieldIds.Score(Ability.Dex)).Value);
    }

    [Fact]
    public void A_set_and_an_override_still_win_over_a_capped_bonus()
    {
        var catalog = CatalogWith(Fixtures.LightStep, DexModifier("set", ModifierOperation.Set, 18));
        Assert.Equal(20, Sheet(Instinct()).Field(FieldIds.Score(Ability.Dex)).Value);
        Assert.Equal(18, Sheet(Instinct(), catalog).Field(FieldIds.Score(Ability.Dex)).Value);

        var overridden = Instinct() with { Overrides = [new FieldOverride(FieldIds.Score(Ability.Dex), 15)] };
        Assert.Equal(15, Sheet(overridden).Field(FieldIds.Score(Ability.Dex)).Value);
    }

    [Fact]
    public void The_plus_one_dex_reaches_twenty_and_stops()
    {
        Assert.Equal(20, Sheet(Instinct()).Field(FieldIds.Score(Ability.Dex)).Value);

        var capped = Sheet(WithDex(Instinct(), 20)).Field(FieldIds.Score(Ability.Dex));
        Assert.Equal(20, capped.Value);
        Assert.Equal(0, capped.Trace.Single(t => t.Description.Contains("capped", StringComparison.Ordinal)).Amount);
    }

    [Fact]
    public void Fly_speed_appears_only_in_the_form()
    {
        var off = Sheet(Instinct()).Field(FieldIds.FlySpeed);
        Assert.Equal(0, off.Value);

        var on = Sheet(WithSky(Instinct())).Field(FieldIds.FlySpeed);
        Assert.Equal(30, on.Value);
        Assert.Equal("feet", on.Units);
    }

    [Fact]
    public void A_form_only_defence_is_listed_only_in_the_form()
    {
        Assert.Null(Sheet(Instinct()).DamageDefences);

        var entry = Assert.Single(Sheet(WithSky(Instinct())).DamageDefences!);
        Assert.Equal(("fire", DamageDefenceKind.Resistance), (entry.Type, entry.Kind));
        Assert.Equal("Fixture Sky Form", Assert.Single(entry.Sources).Name);
    }

    [Fact]
    public void A_form_only_defence_of_absent_content_is_not_listed()
    {
        var absent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000ee");
        var broken = new GrantEffect { Id = "far", Grant = GrantKind.DamageResistance, DamageType = "Fire", WhileToggle = new ToggleRef(absent, "sky") };
        var catalog = CatalogWith(Fixtures.SkyForm, new ToggleEffect { Id = "sky", ToggleId = "sky", Label = "Sky form" }, broken);

        var sheet = Sheet(WithSky(Instinct()), catalog);

        Assert.Null(sheet.DamageDefences);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "modifier.toggle-content-missing" && d.EffectId == "far");
    }
}
