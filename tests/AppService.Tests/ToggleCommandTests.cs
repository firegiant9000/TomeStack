using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M3 B2 (character schema v7): toggles are switched only by confirmed play actions, turning one on spends its use in the
/// same change, a shared resource is spent through its defining content, and a variable cost is a chosen amount.
/// </summary>
public class ToggleCommandTests
{
    private static readonly ContentReference Stance = new(Guid.Parse("5f8dc000-0000-4000-8000-000000000001"), Guid.Parse("5f8de000-0000-4000-8000-000000000001"));
    private static readonly ContentReference Spark = new(Guid.Parse("5f8dc000-0000-4000-8000-000000000002"), Guid.Parse("5f8de000-0000-4000-8000-000000000002"));

    private static (TempApp Temp, Guid Id) Stanced()
    {
        var temp = new TempApp();
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json"); // PB 2 at level 3
        return (temp, temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins, Stance, Spark] }).Character.Id);
    }

    private static int Radiance(CharacterView view) => view.Sheet.Resources!.Single(r => r.ResourceId == "radiance").Current!.Value;

    [Fact]
    public void Turning_a_toggle_on_spends_its_use_and_changes_the_sheet_and_off_changes_it_back()
    {
        var (temp, id) = Stanced();
        using var _ = temp;
        var before = temp.App.GetCharacter(id);

        Assert.Equal("play.confirmation-required", Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.ToggleOn, ContentId: Stance.ContentId, ToggleId: "stance"))).Problems[0].Code);
        var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Stance.ContentId, ToggleId: "stance"));

        Assert.Equal(before.Sheet.Field(FieldIds.ArmorClass).Value + 2, on.Sheet.Field(FieldIds.ArmorClass).Value);
        Assert.Equal(Radiance(before) - 1, Radiance(on));
        Assert.Equal("toggle.already-on", Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Stance.ContentId, ToggleId: "stance"))).Problems[0].Code);

        var off = temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: Stance.ContentId, ToggleId: "stance"));
        Assert.Equal(before.Sheet.Field(FieldIds.ArmorClass).Value, off.Sheet.Field(FieldIds.ArmorClass).Value);
        Assert.Equal(Radiance(on), Radiance(off)); // switching off gives nothing back
        Assert.Equal("toggle.not-found", Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Stance.ContentId, ToggleId: "nope"))).Problems[0].Code);
    }

    [Fact]
    public void A_toggle_that_needs_a_use_cannot_be_turned_on_without_one_and_nothing_changes()
    {
        var (temp, id) = Stanced();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, Amount: 2, ContentId: Stance.ContentId, ResourceId: "radiance")); // PB 2: none left
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Stance.ContentId, ToggleId: "stance")));

        Assert.Equal("resource.insufficient", refused.Problems[0].Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_shared_resource_is_spent_through_its_defining_content_and_a_roll_names_it()
    {
        var (temp, id) = Stanced();
        using var _ = temp;

        var record = temp.App.Roll(new(id, Content: Spark, EffectId: "spark"));
        Assert.Equal(("radiance", Stance.ContentId), (record.Provenance!.LinkedResourceId, record.Provenance.LinkedResourceContent!.Value));

        var spent = temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, Amount: 1, ContentId: record.Provenance.LinkedResourceContent.Value, ResourceId: "radiance"));
        Assert.Equal(1, Radiance(spent));
    }

    [Fact]
    public void A_toggle_survives_reopening_and_a_package_round_trip()
    {
        var (temp, id) = Stanced();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Stance.ContentId, ToggleId: "stance"));
        temp.Reopen();

        var stored = temp.App.GetCharacter(id);
        Assert.Equal([new ActiveToggle(Stance.ContentId, "stance")], stored.Character.Play.Toggles);
        Assert.Equal(Character.CurrentSchemaVersion, stored.Character.SchemaVersion);
        using var clean = new TempApp();
        clean.App.ApplyImport(temp.App.ExportCharacters([id]).Content);
        Assert.True(clean.App.GetCharacter(id).Sheet.Toggles!.Single().On);
    }
}
