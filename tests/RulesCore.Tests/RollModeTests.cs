namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 6, Task 6.2 (D43): roll modes the sheet calculates. Advantage and disadvantage from active content land on
/// the save, skill, ability and initiative fields with their reasons; any advantage and any disadvantage cancel to normal; a mode
/// that needs the player's confirmation (<c>against</c>) is listed and never changes the automatic mode.
/// </summary>
public class RollModeTests
{
    private static readonly ActiveToggle HeavyOn = new(Fixtures.HeavyStance.ContentId, "heavy");

    private static Character Instinct() => Fixtures.Load("pm-instinct-l4.json");

    private static Character WithToggles(Character character, params ActiveToggle[] on) =>
        character with { Play = character.Play with { Toggles = on } };

    private static CharacterSheet Sheet(Character character, InMemoryContentCatalog? catalog = null) =>
        CharacterCalculator.Calculate(character, catalog ?? Fixtures.PlayMechanicsCatalog());

    /// <summary>The play-mechanics catalog with one fixture revision's effects replaced (same ids), as a studio edit would leave it.</summary>
    private static InMemoryContentCatalog CatalogWith(ContentReference reference, params Effect[] effects)
    {
        ContentPack[] packs = [Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack(), Fixtures.TurnsPack(), Fixtures.FeatureAttacksPack(), Fixtures.ActionDetailsPack(), Fixtures.RollModesPack()];
        var revisions = packs.SelectMany(p => p.Revisions).Select(r => r.Reference == reference ? r with { Effects = effects } : r).ToList();
        return new([.. packs.SelectMany(p => p.Sources)], revisions);
    }

    private static RollModeSummary Mode(CharacterSheet sheet, string field) => sheet.Field(field).Mode!;

    [Fact]
    public void Automatic_advantage_is_on_the_field_with_its_reason()
    {
        var mode = Mode(Sheet(Instinct()), "save.dex");

        Assert.Equal(RollMode.Advantage, mode.Automatic);
        Assert.Equal(["Fixture Swift Instinct", "Fixture Light Step"], mode.Reasons.Select(r => r.Source));
        Assert.All(mode.Reasons, r => Assert.Equal(RollMode.Advantage, r.Mode));
        Assert.All(mode.Reasons, r => Assert.Null(r.Against));
        Assert.Equal([Fixtures.SwiftInstinct, Fixtures.LightStep], mode.Reasons.Select(r => r.Content));
    }

    [Fact]
    public void The_sheet_json_says_which_reasons_are_automatic()
    {
        var sheet = Sheet(Instinct());
        var json = System.Text.Json.JsonSerializer.SerializeToNode(sheet, RulesJson.Options)!;
        var fields = json["fields"]!.AsArray();
        System.Text.Json.Nodes.JsonArray Reasons(string id) => fields.Single(f => (string?)f!["field"] == id)!["mode"]!["reasons"]!.AsArray();

        Assert.All(Reasons("save.dex"), r => Assert.True((bool)r!["automatic"]!));
        Assert.All(Reasons("save.con"), r => Assert.False((bool)r!["automatic"]!));
        Assert.Null(Reasons("save.con")[0]!["automation"]);
    }

    [Fact]
    public void Any_advantage_and_any_disadvantage_cancel_to_normal()
    {
        var character = Instinct() with { Pins = [.. Instinct().Pins, Fixtures.WeightedGear] };

        var mode = Mode(Sheet(character), "save.dex");

        Assert.Equal(RollMode.Normal, mode.Automatic);
        Assert.Equal(3, mode.Reasons.Count);
        Assert.Equal(["Fixture Swift Instinct", "Fixture Light Step", "Fixture Weighted Gear"], mode.Reasons.Select(r => r.Source));
        Assert.Equal(RollMode.Disadvantage, mode.Reasons[2].Mode);
    }

    [Fact]
    public void An_assisted_mode_is_a_note_only()
    {
        var mode = Mode(Sheet(Instinct()), "save.con");

        Assert.Equal(RollMode.Normal, mode.Automatic);
        var reason = Assert.Single(mode.Reasons);
        Assert.Equal(RollMode.Advantage, reason.Mode);
        Assert.Equal(["paralyzed", "stunned"], reason.Against);
    }

    [Fact]
    public void An_against_reason_never_changes_the_automatic_mode()
    {
        var automatic = new RollModeEffect { Id = "auto", Target = "save.wis", Mode = RollMode.Advantage };
        var against = new RollModeEffect { Id = "cond", Target = "save.wis", Mode = RollMode.Disadvantage, Against = ["frightened"] };

        var mode = Mode(Sheet(Instinct(), CatalogWith(Fixtures.LightStep, automatic, against)), "save.wis");

        Assert.Equal(RollMode.Advantage, mode.Automatic);
        Assert.Equal(2, mode.Reasons.Count);
        Assert.Contains(mode.Reasons, r => r.Against is not null);
    }

    [Fact]
    public void An_assisted_automation_mode_is_listed_and_never_automatic()
    {
        var note = new RollModeEffect { Id = "note", Target = "save.wis", Mode = RollMode.Advantage, Automation = AutomationStatus.Assisted };
        var plain = new RollModeEffect { Id = "plain", Target = "save.wis", Mode = RollMode.Disadvantage };

        var mode = Mode(Sheet(Instinct(), CatalogWith(Fixtures.LightStep, note, plain)), "save.wis");

        Assert.Equal(RollMode.Disadvantage, mode.Automatic);
        Assert.Equal(2, mode.Reasons.Count);
    }

    [Fact]
    public void A_toggled_mode_applies_only_while_on()
    {
        Assert.Null(Sheet(Instinct()).Field("skill.stealth").Mode);

        var on = Mode(Sheet(WithToggles(Instinct(), HeavyOn)), "skill.stealth");

        Assert.Equal(RollMode.Disadvantage, on.Automatic);
        Assert.Equal("Fixture Heavy Stance", Assert.Single(on.Reasons).Source);
    }

    [Fact]
    public void An_ability_check_mode_reaches_its_skills_and_initiative()
    {
        var dexCheck = new RollModeEffect { Id = "heavy-dex-check", Target = "ability.dex.check", Mode = RollMode.Advantage, Toggle = "heavy" };
        var catalog = CatalogWith(Fixtures.HeavyStance, new ToggleEffect { Id = "heavy", ToggleId = "heavy", Label = "Heavy stance" }, dexCheck);
        var sheet = Sheet(WithToggles(Instinct(), HeavyOn), catalog);

        foreach (var field in new[] { "ability.dex.mod", "skill.acrobatics", "skill.stealth", "skill.sleightOfHand", "initiative" })
        {
            var mode = Mode(sheet, field);
            Assert.Equal(RollMode.Advantage, mode.Automatic);
            Assert.Equal("Fixture Heavy Stance", Assert.Single(mode.Reasons).Source);
        }
        Assert.Null(sheet.Field("skill.athletics").Mode);
        Assert.Null(sheet.Field("ability.str.mod").Mode);
        Assert.Null(sheet.Field("save.dex").Mode?.Reasons.FirstOrDefault(r => r.Source == "Fixture Heavy Stance"));
    }

    [Fact]
    public void A_strength_check_mode_reaches_athletics_while_its_toggle_is_on()
    {
        Assert.Null(Sheet(Instinct()).Field("skill.athletics").Mode);

        var sheet = Sheet(WithToggles(Instinct(), HeavyOn));

        Assert.Equal(RollMode.Advantage, Mode(sheet, "skill.athletics").Automatic);
        Assert.Equal(RollMode.Advantage, Mode(sheet, "ability.str.mod").Automatic);
        Assert.Null(sheet.Field("initiative").Mode);
    }

    [Fact]
    public void A_toggled_mode_naming_absent_content_reports_the_diagnostic()
    {
        var absent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000ee");
        var broken = new RollModeEffect { Id = "far", Target = "save.wis", Mode = RollMode.Advantage, Toggle = "t", ToggleContent = absent };

        var sheet = Sheet(Instinct(), CatalogWith(Fixtures.LightStep, broken));

        Assert.Null(sheet.Field("save.wis").Mode);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "modifier.toggle-content-missing" && d.EffectId == "far");
    }

    [Fact]
    public void A_toggled_mode_follows_another_contents_toggle()
    {
        var follows = new RollModeEffect { Id = "far", Target = "save.wis", Mode = RollMode.Advantage, Toggle = "heavy", ToggleContent = Fixtures.HeavyStance.ContentId };
        var catalog = CatalogWith(Fixtures.LightStep, follows);

        Assert.Null(Sheet(Instinct(), catalog).Field("save.wis").Mode);
        Assert.Equal(RollMode.Advantage, Mode(Sheet(WithToggles(Instinct(), HeavyOn), catalog), "save.wis").Automatic);
    }

    [Fact]
    public void A_sheet_without_roll_modes_has_no_mode_on_any_field()
    {
        var sheet = Sheet(Fixtures.Load("pm-adept-l5.json"));

        Assert.All(sheet.Fields, f => Assert.Null(f.Mode));
    }
}
