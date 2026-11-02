using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D43 slice 3: the server's one compound switch and the UI's <c>isCompound</c> must agree. Both read
/// <c>tests/RulesFixtures/compound-play-actions.json</c>: a list of <c>{ action, replace?, responses?, compound }</c> that names every
/// <see cref="PlayActionKind"/>. A new action kind without a case here fails this test (and the Vitest side), so a
/// compound change cannot ship half-wired.
/// </summary>
public class CompoundParityTests
{
    private sealed record Case(PlayActionKind Action, bool? Replace, bool Compound, bool? Responses = null, bool? Paying = null);

    /// <summary>MECH-X item 1: a minimal sheet with one toggle row; its <c>Payment</c> is a hit-point payment when the case is <c>paying</c>.</summary>
    private static CharacterSheet SheetWithToggle(Guid contentId, bool paying) =>
        new(Guid.NewGuid(), "srd-5.2.1", [], [], Toggles:
        [
            new ToggleValue(new ContentReference(contentId, Guid.NewGuid()), "Fixture", "e1", "t1", "Fixture toggle", false, null, null,
                Payment: paying ? new PaymentValue(PaymentKind.HitPoints, true) : null),
        ]);

    private static List<Case> Cases() =>
        JsonSerializer.Deserialize<List<Case>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "compound-play-actions.json")), RulesJson.Options)!;

    [Fact]
    public void The_server_switch_agrees_with_every_fixture_case()
    {
        foreach (var c in Cases())
        {
            var contentId = Guid.NewGuid();
            var command = new PlayCommand(Guid.NewGuid(), c.Action, Confirm: true, Replace: c.Replace ?? false, ContentId: contentId, ToggleId: "t1",
                Responses: c.Responses == true ? [new ResponseChoice(Guid.NewGuid(), "dummy")] : null);
            Assert.True(c.Compound == TomeStackApp.CompoundUndo(command, SheetWithToggle(contentId, c.Paying == true)).Compound, $"{c.Action} replace={c.Replace} responses={c.Responses} paying={c.Paying}");
        }
    }

    [Fact]
    public void Every_server_action_kind_has_a_case_and_toggleOn_has_both_replace_values()
    {
        var cases = Cases();
        foreach (var kind in Enum.GetValues<PlayActionKind>())
            Assert.Contains(cases, c => c.Action == kind);
        // MECH-X item 1: the replace values are [false, true] plus a second false, the paying case (compound because its sheet row pays hit points or a slot).
        Assert.Equal([false, false, true], cases.Where(c => c.Action == PlayActionKind.ToggleOn).Select(c => c.Replace ?? false).Order().ToArray());
        Assert.Single(cases, c => c is { Action: PlayActionKind.ToggleOn, Paying: true, Compound: true });
    }
}
