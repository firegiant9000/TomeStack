using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Content v12 (G4): a modifier may apply only while no armor, heavy armor or shield is worn. Gear is equipped through the service's
/// own save path; the values differ with and without the gear, the trace names the worn item, and saving for an older release is refused.
/// Original fixtures (<c>tests/RulesFixtures/fixture-pack-gear-gates.json</c>).
/// </summary>
public class NotWearingV12ServiceTests
{
    private static ContentReference G(int n) => new(Guid.Parse($"7a14c000-0000-4000-8000-{n:D12}"), Guid.Parse($"7a14e000-0000-4000-8000-{n:D12}"));

    private static readonly Guid PackSource = Guid.Parse("7a145000-0000-4000-8000-000000001801");
    private static readonly ContentReference Guard = G(1801);
    private static readonly ContentReference Sprint = G(1802);
    private static readonly ContentReference Stride = G(1803);
    private static readonly ContentReference Jerkin = G(1811);
    private static readonly ContentReference Carapace = G(1812);
    private static readonly ContentReference Buckler = G(1813);

    private static readonly ContentReference Duelist = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000011"), Guid.Parse("5f6de000-0000-4000-8000-000000000011"));

    private static Character Hero(ContentReference[] features, params ContentReference[] gear) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Pip Testwright",
        RulesFamily = RulesFamilies.Srd521,
        Level = 1,
        Classes = [new(Duelist, 1)],
        BaseAbilities = new(12, 16, 12, 10, 20, 10),
        Equipment = [.. gear.Select(g => new EquipmentEntry(g, Equipped: false))],
        Pins = [Duelist, .. features],
    };

    private static (TempApp Temp, Guid Id) Start(ContentReference[] features, params ContentReference[] gear)
    {
        var temp = new TempApp();
        temp.AddPack("fixture-pack-gear-gates.json");
        return (temp, temp.App.SaveCharacter(Hero(features, gear)).Character.Id);
    }

    /// <summary>Equips the named items (and unequips the rest) through the service's save path.</summary>
    private static CharacterView Wear(TempApp temp, Guid id, params ContentReference[] worn)
    {
        var current = temp.App.GetCharacter(id).Character;
        var saved = temp.App.SaveCharacter(current with { Equipment = [.. current.Equipment.Select(e => e with { Equipped = worn.Contains(e.Item) })] });
        return temp.App.GetCharacter(saved.Character.Id);
    }

    private static DerivedValue Field(CharacterView view, string field) => Assert.Single(view.Sheet.Fields, f => f.Field == field);

    [Fact]
    public void Equipping_a_shield_turns_off_the_guard_and_the_stride_bonus()
    {
        var (temp, id) = Start([Guard, Sprint, Stride], Buckler);
        using var _ = temp;

        var bare = temp.App.GetCharacter(id);
        var bareAc = Field(bare, "armorClass").Value;
        var bareSpeed = Field(bare, "speed").Value;
        Assert.Equal(10 + 3 + 5, bareAc); // Dexterity 16 and Wisdom 20: the guard's replacement applies

        var shielded = Wear(temp, id, Buckler);
        Assert.True(Field(shielded, "armorClass").Value < bareAc, "the guard no longer replaces Armor Class behind a shield");
        Assert.Equal(bareSpeed - 10, Field(shielded, "speed").Value); // Gearless Sprint is off, Loose Plate Stride is still on

        var again = Wear(temp, id);
        Assert.Equal((bareAc, bareSpeed), (Field(again, "armorClass").Value, Field(again, "speed").Value));
    }

    [Fact]
    public void Light_armor_keeps_the_heavy_armor_gated_bonus()
    {
        var (temp, id) = Start([Sprint, Stride], Jerkin, Carapace);
        using var _ = temp;
        var bareSpeed = Field(temp.App.GetCharacter(id), "speed").Value;

        var light = Field(Wear(temp, id, Jerkin), "speed").Value;
        Assert.Equal(bareSpeed - 10, light); // light armor turns off Gearless Sprint only

        var heavy = Field(Wear(temp, id, Carapace), "speed").Value;
        Assert.Equal(bareSpeed - 20, heavy); // heavy armor turns off both
    }

    [Fact]
    public void Trace_names_the_worn_gear()
    {
        var (temp, id) = Start([Sprint, Stride], Jerkin, Carapace);
        using var _ = temp;

        var heavy = Field(Wear(temp, id, Carapace), "speed").Trace.Select(t => t.Description).ToList();
        Assert.Contains(heavy, d => d.Contains("it applies only while no heavy armor is worn, and item 'Fixture: Ironbound Carapace' is worn", StringComparison.Ordinal));
        Assert.Contains(heavy, d => d.Contains("it applies only while no armor is worn, and item 'Fixture: Ironbound Carapace' is worn", StringComparison.Ordinal));

        var light = Field(Wear(temp, id, Jerkin), "speed").Trace.Select(t => t.Description).ToList();
        Assert.Contains(light, d => d.Contains("it applies only while no armor is worn, and item 'Fixture: Wisp Jerkin' is worn", StringComparison.Ordinal));
        Assert.DoesNotContain(light, d => d.Contains("no heavy armor is worn", StringComparison.Ordinal));
    }

    [Fact]
    public void Trace_names_the_shield()
    {
        var (temp, id) = Start([Guard], Buckler);
        using var _ = temp;

        var trace = Field(Wear(temp, id, Buckler), "armorClass").Trace.Select(t => t.Description).ToList();
        Assert.Contains(trace, d => d.Contains("it applies only while no shield is used, and item 'Fixture: Oak Buckler' is used", StringComparison.Ordinal));
    }

    [Fact]
    public void Fixture_features_are_counted_automatic()
    {
        var (temp, id) = Start([Guard, Sprint, Stride]);
        using var __ = temp;

        var sheet = temp.App.GetCharacter(id).Sheet;
        var fixtures = sheet.Features!.Where(f => f.Name.StartsWith("Fixture: ", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, fixtures.Count);
        Assert.All(fixtures, f => Assert.Equal(AutomationStatus.Automatic, f.Automation));

        var totals = Assert.Single(temp.App.Coverage(new(PackSource)).Sources).Totals;
        Assert.Equal(6, totals.Automatic); // three gated modifiers and three armor effects
        Assert.Equal((0, 0, 0), (totals.Assisted, totals.Reference, totals.Unsupported));
    }

    [Fact]
    public void Publishing_the_pack_requires_12()
    {
        using var temp = new TempApp();
        var pack = temp.AddPack("fixture-pack-gear-gates.json");

        Assert.Equal(6, pack.Revisions.Count);
        foreach (var revision in pack.Revisions)
        {
            var draft = revision with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft };
            var published = temp.App.Publish(temp.App.SaveDraft(draft));
            var stored = temp.App.Store.FindRevision(published.Published)!.SchemaVersion;
            if (revision.Kind == ContentKind.Item)
                Assert.True(published.Report.RequiredSchemaVersion < 12, revision.Name); // plain gear needs nothing new
            else
                Assert.Equal((12, 12), (stored, published.Report.RequiredSchemaVersion));
        }
    }

    [Fact]
    public void Saving_for_11_is_refused_and_names_the_gate()
    {
        var (temp, id) = Start([Guard]);
        using var _ = temp;
        var row = ReleaseLadder.Rows.Single(r => r.Release == new Version(0, 7, 0));

        var preview = temp.App.PreviewExport([id], ExportPurpose.Share, row);

        var block = Assert.Single(preview.Changes, c => c.Action == "blocks");
        Assert.Equal("package.target-blocked", block.Code); // the service wraps the below.equipment-gate block as a package-level one
        Assert.Equal("'Fixture: Bare Palm Guard' (TomeStack Fixtures: Gear Gates v12): it applies only while a shield is not worn; the older version would apply it whatever is worn.", block.Message);
    }
}
