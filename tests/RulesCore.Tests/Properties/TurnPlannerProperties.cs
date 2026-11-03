using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// Play mechanics slice 3 (D43), task 3.3: whatever is on, however much is spent and whatever the player unticks or chooses, the
/// same input gives the same plan, and applying a plan never leaves a pool below 0 spent or past its maximum.
/// </summary>
public class TurnPlannerProperties
{
    private sealed record Input(
        bool Iron, bool Flare, int? IronLeft, int Round, int Breath, int Strain, int Guard, TurnMoment At, string[] Skip, bool Keep, bool Choose);

    private static readonly string[] SkippableIds =
    [
        $"duration:{Fixtures.IronStance.ContentId:D}:iron",
        $"upkeep:{Fixtures.IronStance.ContentId:D}:iron-upkeep",
        $"upkeep:{Fixtures.FlareOverlay.ContentId:D}:flare-breath",
        $"upkeep:{Fixtures.FlareOverlay.ContentId:D}:flare-strain",
        $"upkeep:{Fixtures.QuickGuard.ContentId:D}:guard-refresh",
        $"upkeep:{Fixtures.SecondWindRhythm.ContentId:D}:rhythm",
        "round",
    ];

    private static Gen<Input> Inputs { get; } =
        from iron in Gen.Elements(true, false)
        from flare in Gen.Elements(true, false)
        from left in Gen.Elements<int?>(null, 0, 1, 2, 3)
        from round in Gen.Choose(0, 5)
        from breath in Gen.Choose(0, 7)
        from strain in Gen.Choose(0, 3)
        from guard in Gen.Choose(0, 1)
        from at in Gen.Elements(TurnMoment.TurnStart, TurnMoment.TurnEnd)
        from skip in Gen.Elements(SkippableIds).ListOf()
        from keep in Gen.Elements(true, false)
        from choose in Gen.Elements(true, false)
        select new Input(iron, flare, left, round, breath, strain, guard, at, [.. skip.Distinct()], keep, choose);

    private static TurnPlan Plan(Input i, out Character character)
    {
        character = TurnPlannerTests.InEncounter(i.Round, i.Iron, i.Flare, i.IronLeft, i.Breath, i.Strain, i.Guard);
        var choices = i.Choose
            ? new Dictionary<string, TurnChoiceOption> { [$"short:{Fixtures.IronStance.ContentId:D}:iron-upkeep"] = i.Keep ? TurnChoiceOption.Keep : TurnChoiceOption.End }
            : new Dictionary<string, TurnChoiceOption>();
        return TurnPlanner.Plan(character, TurnPlannerTests.Sheet(character), i.At, new TurnSelection(new HashSet<string>(i.Skip), choices));
    }

    [Property(MaxTest = 300)]
    public Property Turn_plan_is_deterministic() =>
        Prop.ForAll(Inputs.ToArbitrary(), i =>
        {
            var first = JsonSerializer.Serialize(Plan(i, out _), RulesJson.Compact);
            var second = JsonSerializer.Serialize(Plan(i, out _), RulesJson.Compact);
            return (first == second).Label(first);
        });

    [Property(MaxTest = 300)]
    public Property Applying_a_plan_never_overspends_or_overfills_a_pool() =>
        Prop.ForAll(Inputs.ToArbitrary(), i =>
        {
            var plan = Plan(i, out var character);
            var after = TurnPlanner.Apply(character.Play, plan);
            var sheet = TurnPlannerTests.Sheet(character with { Play = after });
            var pools = sheet.Resources!.Where(r => r.ResourceId is "breath" or "strain" or "guard").ToList();
            var spentOk = pools.All(r => r.Spent >= 0 && r.Spent <= r.Maximum);
            var currentOk = pools.All(r => r.Current >= 0);
            var roundOk = after.Encounter is { Round: >= 0 and <= PlayState.MaxRounds };
            var leftOk = after.Toggles.All(t => t.RoundsLeft is null or >= 0);
            return (pools.Count == 3 && spentOk && currentOk && roundOk && leftOk).Label(JsonSerializer.Serialize(plan, RulesJson.Compact));
        });
}
