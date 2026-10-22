using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D43 slice 3: the server's one compound switch and the UI's <c>isCompound</c> must agree. Both read
/// <c>tests/RulesFixtures/compound-play-actions.json</c>: a list of <c>{ action, replace?, compound }</c> that names every
/// <see cref="PlayActionKind"/>. A new action kind without a case here fails this test (and the Vitest side), so a
/// compound change cannot ship half-wired.
/// </summary>
public class CompoundParityTests
{
    private sealed record Case(PlayActionKind Action, bool? Replace, bool Compound);

    private static List<Case> Cases() =>
        JsonSerializer.Deserialize<List<Case>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "compound-play-actions.json")), RulesJson.Options)!;

    [Fact]
    public void The_server_switch_agrees_with_every_fixture_case()
    {
        foreach (var c in Cases())
        {
            var command = new PlayCommand(Guid.NewGuid(), c.Action, Confirm: true, Replace: c.Replace ?? false);
            Assert.True(c.Compound == TomeStackApp.CompoundUndo(command, null).Compound, $"{c.Action} replace={c.Replace}");
        }
    }

    [Fact]
    public void Every_server_action_kind_has_a_case_and_toggleOn_has_both_replace_values()
    {
        var cases = Cases();
        foreach (var kind in Enum.GetValues<PlayActionKind>())
            Assert.Contains(cases, c => c.Action == kind);
        Assert.Equal([false, true], cases.Where(c => c.Action == PlayActionKind.ToggleOn).Select(c => c.Replace ?? false).Order().ToArray());
    }
}
