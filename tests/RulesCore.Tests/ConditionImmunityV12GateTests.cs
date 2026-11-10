namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content v12 G11 (condition immunity), Task 4b: an immune condition is not held for the G6 response and G7 roll-mode
/// unlessCondition gates. Incapacitated is implied only by a held, non-immune including condition.
/// </summary>
public class ConditionImmunityV12GateTests
{
    private static readonly Guid Holder = Guid.Parse("5fb12000-0000-4000-8000-0000000000c1");
    private static readonly Guid Ward = Guid.Parse("5fb12000-0000-4000-8000-0000000000d2");

    private static HashSet<string> Set(params string[] keys) => keys.ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Immune_key_is_not_held() =>
        Assert.False(ConditionKeys.Holds(["frightened"], "frightened", Set("frightened")));

    [Fact]
    public void Immune_incapacitated_is_not_implied_by_paralyzed() =>
        Assert.False(ConditionKeys.Holds(["paralyzed"], "incapacitated", Set("incapacitated")));

    [Fact]
    public void Immune_paralyzed_does_not_imply_incapacitated_but_stunned_does()
    {
        Assert.False(ConditionKeys.Holds(["paralyzed"], "incapacitated", Set("paralyzed")));
        Assert.True(ConditionKeys.Holds(["paralyzed", "stunned"], "incapacitated", Set("paralyzed")));
    }

    [Fact]
    public void Two_argument_call_is_unchanged()
    {
        Assert.True(ConditionKeys.Holds(["frightened"], "frightened"));
        Assert.True(ConditionKeys.Holds(["petrified"], "incapacitated"));
        Assert.False(ConditionKeys.Holds(["prone"], "incapacitated"));
        Assert.True(ConditionKeys.Holds(["paralyzed"], "incapacitated", Set("charmed")));
    }

    private static ContentRevision Revision(Guid contentId, string name, params Effect[] effects) => new()
    {
        ContentId = contentId, RevisionId = Guid.NewGuid(), SchemaVersion = 12, Kind = ContentKind.Feat,
        Name = name, RulesFamilies = [RulesFamilies.Srd521], Status = RevisionStatus.Published,
        Provenance = new(Fixtures.Source2024, new(3)),
        Effects = effects,
    };

    private static GrantEffect Immune(string id, string condition) => new() { Id = id, Grant = GrantKind.ConditionImmunity, Condition = condition };

    private static ResponseEffect Response(string unless) => new()
    {
        Id = "r", ResponseId = "calmResolve", Label = "Calm resolve", Phase = ResponsePhase.DamageReduction, Amount = "3", UnlessCondition = unless,
    };

    private static CharacterSheet Sheet(PlayState? play, params ContentRevision[] revisions) =>
        CharacterCalculator.Calculate(
            ToggleAndCostTests.Hero(revisions[0], null, play: play) with { Pins = [.. revisions.Select(r => r.Reference)] },
            ToggleAndCostTests.CatalogWith(revisions));

    [Fact]
    public void Response_unless_frightened_is_offered_while_immune_to_frightened()
    {
        var play = new PlayState { Conditions = ["frightened"] };
        var response = Revision(Holder, "Calm Resolve", Response("frightened"));

        var held = Assert.Single(Sheet(play, response).Responses!);
        Assert.False(held.Available);

        var immune = Revision(Ward, "Even Temper", Immune("even-temper","frightened"));
        var sheet = Sheet(play, response, immune);
        Assert.True(Assert.Single(sheet.Responses!).Available);
    }

    [Fact]
    public void Roll_mode_unless_charmed_applies_while_immune_to_charmed()
    {
        var play = new PlayState { Conditions = ["charmed"] };
        var mode = Revision(Holder, "Clear Head",
            new RollModeEffect { Id = "m", Target = "save.wis", Mode = RollMode.Advantage, UnlessCondition = "charmed" });

        Assert.Null(Sheet(play, mode).Field("save.wis").Mode?.Automatic);

        var immune = Revision(Ward, "Unshaken Mind", Immune("calm", "charmed"));
        Assert.Equal(RollMode.Advantage, Sheet(play, mode, immune).Field("save.wis").Mode!.Automatic);
    }

    [Fact]
    public void Response_unless_incapacitated_is_offered_with_paralyzed_held_and_incapacitated_immunity()
    {
        var play = new PlayState { Conditions = ["paralyzed"] };
        var response = Revision(Holder, "Calm Resolve", Response("incapacitated"));
        var immune = Revision(Ward, "Clear Head", Immune("clear", "incapacitated"));

        Assert.False(Assert.Single(Sheet(play, response).Responses!).Available);
        Assert.True(Assert.Single(Sheet(play, response, immune).Responses!).Available);
    }

    [Fact]
    public void Toggle_off_makes_the_condition_count_again()
    {
        var holder = Revision(Holder, "Fury Stance", new ToggleEffect { Id = "t", ToggleId = "fury", Label = "Fury" });
        var response = Revision(Ward, "Calm Resolve", Response("frightened"));
        var immune = Revision(Guid.Parse("5fb12000-0000-4000-8000-0000000000e3"), "Furious Calm",
            Immune("calm", "frightened") with { WhileToggle = new(Holder, "fury") });

        var off = Sheet(new PlayState { Conditions = ["frightened"] }, holder, response, immune);
        Assert.False(Assert.Single(off.Responses!).Available);

        var on = Sheet(new PlayState { Conditions = ["frightened"], Toggles = [new ActiveToggle(Holder, "fury")] }, holder, response, immune);
        Assert.True(Assert.Single(on.Responses!).Available);
    }
}
