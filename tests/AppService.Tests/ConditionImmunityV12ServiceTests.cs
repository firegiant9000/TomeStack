using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Content v12 (G11): a condition immunity is listed on the sheet while its toggle is on, and the response and roll-mode gates
/// stop counting the condition. Play state is never rewritten because of it.
/// Original fixtures (<c>tests/RulesFixtures/fixture-pack-condition-immunity.json</c>) on the combat pack the development hosts seed.
/// </summary>
public class ConditionImmunityV12ServiceTests
{
    private static ContentReference V12(int n) => new(Guid.Parse($"6f15c000-0000-4000-8000-{n:D12}"), Guid.Parse($"6f15e000-0000-4000-8000-{n:D12}"));

    private static readonly Guid PackSource = Guid.Parse("6f155000-0000-4000-8000-000000001601");
    private static readonly ContentReference StormTemper = V12(1601);
    private static readonly ContentReference IronGut = V12(1602);
    private static readonly ContentReference CalmUnderFire = V12(1603);
    private static readonly ContentReference CorneredRiposte = V12(1604);

    private static readonly ContentReference Longblade = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000001"), Guid.Parse("5f6de000-0000-4000-8000-000000000001"));
    private static readonly ContentReference Duelist = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000011"), Guid.Parse("5f6de000-0000-4000-8000-000000000011"));

    private static Character Hero(params ContentReference[] features) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Pip Testwright",
        RulesFamily = RulesFamilies.Srd521,
        Level = 1,
        Classes = [new(Duelist, 1)],
        BaseAbilities = new(8, 16, 12, 10, 10, 10),
        Equipment = [new(Longblade, Equipped: true)],
        Pins = [Duelist, .. features],
    };

    private static (TempApp Temp, Guid Id) Start(params ContentReference[] features)
    {
        var temp = new TempApp();
        temp.AddPack("fixture-pack-condition-immunity.json");
        return (temp, temp.App.SaveCharacter(Hero(features)).Character.Id);
    }

    private static CharacterView TempestOn(TempApp temp, Guid id) =>
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: StormTemper.ContentId, ToggleId: "tempest"));

    private static CharacterView TempestOff(TempApp temp, Guid id) =>
        temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: StormTemper.ContentId, ToggleId: "tempest"));

    private static CharacterView Add(TempApp temp, Guid id, string condition) =>
        temp.App.Play(new(id, PlayActionKind.AddCondition, Confirm: true, Condition: condition));

    private static ResponseValue Riposte(CharacterView view) => Assert.Single(view.Sheet.Responses!, r => r.ResponseId == "riposte");

    [Fact]
    public void Immunities_follow_the_toggle_in_play()
    {
        var (temp, id) = Start(StormTemper);
        using var _ = temp;

        Assert.Null(temp.App.GetCharacter(id).Sheet.ConditionImmunities);

        var on = TempestOn(temp, id);
        Assert.Equal(["charmed", "frightened"], on.Sheet.ConditionImmunities!.Select(c => c.Condition));
        Assert.All(on.Sheet.ConditionImmunities!, c => Assert.Equal("Fixture: Storm Temper", Assert.Single(c.Sources).Name));

        var off = TempestOff(temp, id);
        Assert.Null(off.Sheet.ConditionImmunities);
    }

    [Fact]
    public void Add_condition_while_immune_is_stored_and_response_is_still_offered()
    {
        var (temp, id) = Start(StormTemper, CorneredRiposte);
        using var _ = temp;
        Assert.True(Riposte(temp.App.GetCharacter(id)).Available);

        var on = TempestOn(temp, id);
        var held = Add(temp, id, "frightened");

        Assert.Equal(["frightened"], held.Character.Play.Conditions); // stored, not blocked
        Assert.True(Riposte(held).Available);                          // immune, so it does not count
        Assert.Contains(held.Sheet.ConditionImmunities!, c => c.Condition == "frightened");
        Assert.NotNull(on.Sheet.ConditionImmunities);

        var off = TempestOff(temp, id);
        var stamp = off.Character.UpdatedAt;

        Assert.Equal(["frightened"], off.Character.Play.Conditions); // still held: nothing rewrote it
        Assert.False(Riposte(off).Available);
        Assert.Equal("Not while frightened", Riposte(off).UnavailableReason);

        // Reading the sheet again writes nothing.
        var reread = temp.App.GetCharacter(id);
        Assert.Equal(stamp, reread.Character.UpdatedAt);
        Assert.Equal(["frightened"], reread.Character.Play.Conditions);
    }

    [Fact]
    public void Cross_content_toggle_immunity_applies()
    {
        var (temp, id) = Start(StormTemper, CalmUnderFire, IronGut);
        using var _ = temp;

        var before = temp.App.GetCharacter(id).Sheet.ConditionImmunities!;
        Assert.Equal(["poisoned"], before.Select(c => c.Condition)); // always on; the toggled ones wait

        var on = TempestOn(temp, id).Sheet.ConditionImmunities!;
        Assert.Equal(["charmed", "frightened", "poisoned"], on.Select(c => c.Condition));
        var frightened = on.Single(c => c.Condition == "frightened");
        Assert.Equal(["Fixture: Calm Under Fire", "Fixture: Storm Temper"], frightened.Sources.Select(s => s.Name).Order());

        var off = TempestOff(temp, id).Sheet.ConditionImmunities!;
        Assert.Equal(["poisoned"], off.Select(c => c.Condition));
    }

    [Fact]
    public void Fixture_features_are_counted_automatic()
    {
        var (temp, id) = Start(StormTemper, IronGut, CalmUnderFire, CorneredRiposte);
        using var __ = temp;

        var sheet = temp.App.GetCharacter(id).Sheet;
        var fixtures = sheet.Features!.Where(f => f.Name.StartsWith("Fixture: ", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, fixtures.Count);
        Assert.All(fixtures, f => Assert.Equal(AutomationStatus.Automatic, f.Automation));

        var report = temp.App.Coverage(new(PackSource));

        var totals = Assert.Single(report.Sources).Totals;
        Assert.Equal(6, totals.Automatic); // one per effect: a toggle, four immunities, one response
        Assert.Equal((0, 0, 0), (totals.Assisted, totals.Reference, totals.Unsupported));
    }

    [Fact]
    public void Publishing_the_pack_requires_12()
    {
        using var temp = new TempApp();
        var pack = temp.AddPack("fixture-pack-condition-immunity.json");

        Assert.Equal(4, pack.Revisions.Count);
        foreach (var revision in pack.Revisions)
        {
            Assert.Equal(12, revision.SchemaVersion);
            var draft = revision with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft };
            var published = temp.App.Publish(temp.App.SaveDraft(draft));
            Assert.Equal((12, 12), (temp.App.Store.FindRevision(published.Published)!.SchemaVersion, published.Report.RequiredSchemaVersion));
        }
    }
}
