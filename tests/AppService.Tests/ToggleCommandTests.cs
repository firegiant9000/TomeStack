using TomeStack.AppService.Exports;
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

    private static ContentReference InstallAutoGrit(TempApp temp)
    {
        var source = TempApp.LoadFixture<ContentPack>("fixture-pack-m3-effects.json").Sources[0].Id;
        var revision = new ContentRevision
        {
            ContentId = Guid.Parse("5fb10d00-0000-4000-8000-0000000000a2"), RevisionId = Guid.Parse("5fb10e00-0000-4000-8000-0000000000a2"), Kind = ContentKind.Feat,
            Name = "Testy Grit", RulesFamilies = [RulesFamilies.Srd521], Provenance = new(source, new(3)), Status = RevisionStatus.Published,
            Effects =
            [
                new ToggleEffect { Id = "grit-toggle", ToggleId = "grit", Label = "Testy grit", AutoWhen = ToggleAutoWhen.Bloodied },
                new ModifierEffect { Id = "grit-ac", Operation = ModifierOperation.Bonus, Target = FieldIds.ArmorClass, Value = "2", Timing = EffectTiming.WhileActive, Toggle = "grit" },
            ],
        };
        temp.App.Store.AddRevision(revision);
        return revision.Reference;
    }

    [Fact]
    public void An_auto_toggle_refuses_both_directions_writes_nothing_and_follows_the_hit_points()
    {
        using var temp = new TempApp();
        var grit = InstallAutoGrit(temp);
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        var id = temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins, grit] }).Character.Id;
        var healthy = temp.App.GetCharacter(id);
        var maximum = healthy.Sheet.HitPoints!.Maximum;

        // Turning it on or off by hand is refused, with nothing written (so no use is spent either).
        var stored = TempApp.Json(healthy.Character);
        foreach (var action in new[] { PlayActionKind.ToggleOn, PlayActionKind.ToggleOff })
        {
            var refused = Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, action, Confirm: true, ContentId: grit.ContentId, ToggleId: "grit")));
            Assert.Equal("toggle.automatic", refused.Problems[0].Code);
            Assert.Equal(stored, TempApp.Json(temp.App.GetCharacter(id).Character));
        }

        // Damage to half switches it on; healing above half switches it off; the stored play state never names it.
        var bloodied = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: maximum - maximum / 2));
        Assert.True(bloodied.Sheet.Bloodied);
        Assert.Equal((true, true), (bloodied.Sheet.Toggles!.Single(t => t.ToggleId == "grit").On, bloodied.Sheet.Toggles!.Single(t => t.ToggleId == "grit").Auto == true));
        Assert.Equal(healthy.Sheet.Field(FieldIds.ArmorClass).Value + 2, bloodied.Sheet.Field(FieldIds.ArmorClass).Value);
        Assert.DoesNotContain(bloodied.Character.Play.Toggles, t => t.ToggleId == "grit");
        Assert.Equal("toggle.automatic", Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: grit.ContentId, ToggleId: "grit"))).Problems[0].Code);

        var mended = temp.App.Play(new(id, PlayActionKind.Heal, Confirm: true, Amount: 1));
        Assert.NotEqual(true, mended.Sheet.Bloodied);
        Assert.False(mended.Sheet.Toggles!.Single(t => t.ToggleId == "grit").On);
        Assert.Equal(healthy.Sheet.Field(FieldIds.ArmorClass).Value, mended.Sheet.Field(FieldIds.ArmorClass).Value);
    }

    [Fact]
    public void The_sheet_export_lists_manual_toggles_but_never_an_auto_toggle_bloodied_or_not()
    {
        using var temp = new TempApp();
        var grit = InstallAutoGrit(temp);
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        var id = temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins, Stance, grit] }).Character.Id;
        string Exported()
        {
            var preview = temp.App.PreviewVttExport(id, SheetJson.Target, SheetPurpose.Personal);
            return System.Text.Encoding.UTF8.GetString(temp.App.VttExportOutput(preview.Token).Bytes);
        }

        var healthy = Exported();
        Assert.Contains("Radiant stance", healthy, StringComparison.Ordinal);
        Assert.DoesNotContain("Testy grit", healthy, StringComparison.Ordinal);
        var maximum = temp.App.GetCharacter(id).Sheet.HitPoints!.Maximum;
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: maximum - maximum / 2));
        Assert.True(temp.App.GetCharacter(id).Sheet.Bloodied);
        var bloodied = Exported();
        Assert.Contains("Radiant stance", bloodied, StringComparison.Ordinal);
        Assert.DoesNotContain("Testy grit", bloodied, StringComparison.Ordinal);
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
        Assert.Equal(Character.PreviousSchemaVersion, stored.Character.SchemaVersion); // no v9 field is set, so it is written as v8 (R73)
        using var clean = new TempApp();
        clean.App.ApplyImport(temp.App.ExportCharacters([id]).Content);
        Assert.True(clean.App.GetCharacter(id).Sheet.Toggles!.Single().On);
    }
}
