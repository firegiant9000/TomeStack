using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Content v12 (G1, Task 5): damage rolls add picked once-per-turn riders and crit-only dice, a roll effect with <c>scaledDice</c> rolls its
/// calculated dice, and the fixture pack (<c>tests/RulesFixtures/fixture-pack-scaled-dice.json</c>) calculates and is counted as the
/// capability registry says. Original fixtures on the combat pack the development hosts seed.
/// </summary>
public class ScaledDiceV12ServiceTests
{
    private static ContentReference V12(int n) => new(Guid.Parse($"6f0dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"6f0de000-0000-4000-8000-{n:D12}"));

    private static readonly Guid PackSource = Guid.Parse("6f0d5000-0000-4000-8000-000000001401");
    private static readonly ContentReference GapInTheGuard = V12(1401);
    private static readonly ContentReference HeavyFollowThrough = V12(1402);
    private static readonly ContentReference EmberBurst = V12(1403);
    private static readonly ContentReference PlainEdge = V12(1404);
    private static readonly ContentReference TailStrike = V12(1405);
    private static readonly ContentReference FirstToMove = new(Guid.Parse("6f0dc000-0000-4000-8000-000000001302"), Guid.Parse("6f0de000-0000-4000-8000-000000001302"));

    private static readonly ContentReference Longblade = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000001"), Guid.Parse("5f6de000-0000-4000-8000-000000000001"));
    private static readonly ContentReference Duelist = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000011"), Guid.Parse("5f6de000-0000-4000-8000-000000000011"));

    private static Character Fighter(params ContentReference[] features) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Testy Scaler",
        RulesFamily = RulesFamilies.Srd521,
        Level = 9,
        Classes = [new(Duelist, 9)],
        BaseAbilities = new(14, 16, 12, 10, 10, 10),
        Equipment = [new(Longblade, Equipped: true)],
        Pins = [Duelist, .. features],
    };

    private static (TempApp Temp, Guid Id) Start(Character character, Func<ContentRevision, ContentRevision>? adjust = null)
    {
        var temp = new TempApp();
        temp.App.Random = new SeededRandomSource(7);
        temp.AddPack("fixture-pack-scaled-dice.json", adjust);
        temp.AddPack("fixture-pack-roll-modes-v12.json");
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    private static string Code(TempApp temp, RollCommand command) =>
        Assert.Single(Assert.Throws<AppValidationException>(() => temp.App.Roll(command)).Problems).Code;

    private static int Count(RollRecord record, int sides) => record.Dice.Count(d => d.Sides == sides);

    private static int FromCritical(RollRecord record, int sides) => record.Dice.Count(d => d.Sides == sides && d.FromCritical);

    private static RiderPick Pick(ContentReference content, string effect) => new(content, effect);

    [Fact]
    public void Weapon_damage_adds_a_picked_once_per_turn_rider()
    {
        var (temp, id) = Start(Fighter(GapInTheGuard));
        using var _ = temp;
        var entry = temp.App.GetCharacter(id).Sheet.Attacks!.Single(a => a.Source == AttackSource.Weapon && a.Item == Longblade);
        var rider = Assert.Single(entry.Riders!);
        Assert.Equal((true, "5d6"), (rider.OncePerTurn, rider.Dice));

        var plain = temp.App.Roll(new RollCommand(id, Weapon: Longblade, Damage: true));
        var picked = temp.App.Roll(new RollCommand(id, Weapon: Longblade, Damage: true, WithRiders: [Pick(GapInTheGuard, "gap")]));
        // A duplicate pick collapses.
        var twice = temp.App.Roll(new RollCommand(id, Weapon: Longblade, Damage: true, WithRiders: [Pick(GapInTheGuard, "gap"), Pick(GapInTheGuard, "gap")]));

        Assert.Equal(entry.Damage, plain.Formula);
        Assert.Equal(0, Count(plain, 6));
        Assert.Equal($"{entry.Damage}+5d6", picked.Formula);
        Assert.Equal(5, Count(picked, 6));
        Assert.Equal(picked.Formula, twice.Formula);
    }

    [Fact]
    public void Picked_rider_doubles_on_a_critical()
    {
        var (temp, id) = Start(Fighter(GapInTheGuard));
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Weapon: Longblade, Damage: true, Critical: true, WithRiders: [Pick(GapInTheGuard, "gap")]));

        Assert.Equal((10, 5), (Count(record, 6), FromCritical(record, 6)));
    }

    [Fact]
    public void Critical_rider_dice_added_once_only_on_a_critical()
    {
        var (temp, id) = Start(Fighter(HeavyFollowThrough, PlainEdge));
        using var _ = temp;
        var entry = temp.App.GetCharacter(id).Sheet.Attacks!.Single(a => a.Source == AttackSource.Weapon && a.Item == Longblade);
        Assert.Equal((true, "2d8"), (entry.Riders!.Single(r => r.Critical).Critical, entry.Riders!.Single(r => r.Critical).Dice));

        var normal = temp.App.Roll(new RollCommand(id, Weapon: Longblade, Damage: true));
        var critical = temp.App.Roll(new RollCommand(id, Weapon: Longblade, Damage: true, Critical: true));

        // Not critical: the weapon die and the plain rider's d4 only.
        Assert.Equal((1, 1), (Count(normal, 8), Count(normal, 4)));
        // Critical: the weapon die and the plain rider double; the crit-only 2d8 is rolled once and flagged.
        Assert.Equal((2 + 2, 2), (Count(critical, 8), Count(critical, 4)));
        Assert.Equal((1 + 2, 1), (FromCritical(critical, 8), FromCritical(critical, 4)));
        // The flat bonus is not doubled and the formula is the entry's own.
        Assert.Equal(normal.ExpressionConstant, critical.ExpressionConstant);
        Assert.Equal(normal.Formula, critical.Formula);
    }

    [Fact]
    public void Versatile_critical_uses_the_versatile_die()
    {
        var (temp, id) = Start(Fighter(HeavyFollowThrough));
        using var _ = temp;
        var entry = temp.App.GetCharacter(id).Sheet.Attacks!.Single(a => a.Source == AttackSource.Weapon && a.Item == Longblade);
        Assert.Equal("2d10", entry.Riders!.Single().VersatileDice);

        var oneHanded = temp.App.Roll(new RollCommand(id, Weapon: Longblade, Damage: true, Critical: true));
        var twoHanded = temp.App.Roll(new RollCommand(id, Weapon: Longblade, Damage: true, Critical: true, Versatile: true));

        Assert.Equal((4, 0), (Count(oneHanded, 8), Count(oneHanded, 10)));
        Assert.Equal((0, 4), (Count(twoHanded, 8), Count(twoHanded, 10)));
        Assert.Equal(3, FromCritical(twoHanded, 10));
    }

    [Fact]
    public void Feature_attack_damage_takes_picks_and_critical_riders()
    {
        var (temp, id) = Start(Fighter(TailStrike, HeavyFollowThrough));
        using var _ = temp;

        var plain = temp.App.Roll(new RollCommand(id, Content: TailStrike, EffectId: "tail", Damage: true));
        var picked = temp.App.Roll(new RollCommand(id, Content: TailStrike, EffectId: "tail", Damage: true, WithRiders: [Pick(TailStrike, "tail-extra")]));
        var critical = temp.App.Roll(new RollCommand(id, Content: TailStrike, EffectId: "tail", Damage: true, Critical: true, WithRiders: [Pick(TailStrike, "tail-extra")]));

        Assert.Equal((1, 0), (Count(plain, 6), Count(plain, 4)));
        Assert.Equal((1, 1), (Count(picked, 6), Count(picked, 4)));
        // The pick doubles; the crit-only rider uses the attack's own die (d6), twice as many as the level gives (2), rolled once.
        Assert.Equal((2 + 2, 2), (Count(critical, 6), Count(critical, 4)));
        Assert.Equal((1 + 2, 1), (FromCritical(critical, 6), FromCritical(critical, 4)));
    }

    [Fact]
    public void Picks_refused_on_to_hit_and_roll_effects()
    {
        var (temp, id) = Start(Fighter(GapInTheGuard, EmberBurst, TailStrike));
        using var _ = temp;
        RiderPick[] picks = [Pick(GapInTheGuard, "gap")];

        Assert.Equal("roll.riders-not-allowed", Code(temp, new(id, Weapon: Longblade, WithRiders: picks)));
        Assert.Equal("roll.riders-not-allowed", Code(temp, new(id, Content: TailStrike, EffectId: "tail", WithRiders: [Pick(TailStrike, "tail-extra")])));
        Assert.Equal("roll.riders-not-allowed", Code(temp, new(id, Content: EmberBurst, EffectId: "burst", WithRiders: picks)));
        Assert.Equal("roll.riders-not-allowed", Code(temp, new(id, Field: FieldIds.Initiative, WithRiders: picks)));
        // An empty list is no pick.
        Assert.NotNull(temp.App.Roll(new RollCommand(id, Weapon: Longblade, WithRiders: [])));
    }

    [Fact]
    public void Pick_of_a_rider_not_on_the_entry_is_refused()
    {
        var (temp, id) = Start(Fighter(GapInTheGuard, PlainEdge, TailStrike));
        using var _ = temp;

        // Another entry's once-per-turn rider, a rider that is not once per turn, and one that does not exist.
        Assert.Equal("roll.rider-unknown", Code(temp, new(id, Weapon: Longblade, Damage: true, WithRiders: [Pick(TailStrike, "tail-extra")])));
        Assert.Equal("roll.rider-unknown", Code(temp, new(id, Weapon: Longblade, Damage: true, WithRiders: [Pick(PlainEdge, "edge")])));
        Assert.Equal("roll.rider-unknown", Code(temp, new(id, Weapon: Longblade, Damage: true, WithRiders: [Pick(GapInTheGuard, "nothing")])));
        Assert.Equal("roll.rider-unknown", Code(temp, new(id, Content: TailStrike, EffectId: "tail", Damage: true, WithRiders: [Pick(GapInTheGuard, "gap")])));
    }

    [Fact]
    public void Scaled_roll_rolls_the_calculated_dice()
    {
        var (temp, id) = Start(Fighter(EmberBurst));
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Content: EmberBurst, EffectId: "burst"));

        Assert.Equal("5d4", record.Formula);
        Assert.Equal(5, Count(record, 4));
        Assert.Equal("burst", record.Provenance!.RollId);
    }

    [Fact]
    public void Scaled_roll_refuses_with_the_diagnostics_code()
    {
        // A count past what a roll may throw (180 at level 9): the sheet reports roll.dice-invalid and the roll is refused with it.
        var (tooMany, tooManyId) = Start(Fighter(EmberBurst), Scaled("LEVEL*20"));
        using var _ = tooMany;
        Assert.Equal("roll.dice-invalid", Code(tooMany, new(tooManyId, Content: EmberBurst, EffectId: "burst")));

        // A count of 0 adds no dice and no diagnostic: the roll has nothing to roll.
        var (none, noneId) = Start(Fighter(EmberBurst), Scaled("floor(LEVEL/20)"));
        using var __ = none;
        Assert.Equal("roll.no-dice", Code(none, new(noneId, Content: EmberBurst, EffectId: "burst")));
    }

    private static Func<ContentRevision, ContentRevision> Scaled(string count) => revision => revision.Name != "Fixture: Ember Burst"
        ? revision
        : revision with { Effects = [.. revision.Effects.Select(e => e is RollEffect roll ? roll with { ScaledDice = new ScaledDice(count, "4") } : e)] };

    [Fact]
    public void Field_roll_still_takes_its_automatic_mode()
    {
        var (temp, id) = Start(Fighter(FirstToMove));
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: FieldIds.Initiative));

        Assert.Equal((RollMode.Advantage, 2), (record.Mode, record.Dice.Count));
        Assert.Equal(RollMode.Advantage, record.AutomaticMode);
        Assert.Equal(["Fixture: First to Move"], record.ModeSources);
        // The player's disadvantage cancels it, as before.
        var cancelled = temp.App.Roll(new RollCommand(id, Field: FieldIds.Initiative, Mode: RollMode.Disadvantage));
        Assert.Equal((RollMode.Normal, 1), (cancelled.Mode, cancelled.Dice.Count));
        Assert.Equal(RollMode.Advantage, cancelled.AutomaticMode);
    }

    [Fact]
    public void Fixture_features_calculate_and_are_counted()
    {
        var all = new[] { GapInTheGuard, HeavyFollowThrough, EmberBurst, PlainEdge, TailStrike };
        var (temp, id) = Start(Fighter(all));
        using var _ = temp;
        var sheet = temp.App.GetCharacter(id).Sheet;

        Assert.DoesNotContain(sheet.Diagnostics, d => d.Content is { } c && all.Contains(c));
        Assert.Equal("5d4", Assert.Single(sheet.Features!, f => f.Content == EmberBurst).Effects.Single(e => e.Id == "burst").Dice);

        // Gap in the Guard (Assisted) | Heavy Follow-Through, Ember Burst, Plain Edge (Automatic) | Tail Strike: attack Automatic, rider Assisted.
        var report = temp.App.Coverage(new(PackSource));
        Assert.Equal(new CoverageCounts(Automatic: 4, Assisted: 2, Reference: 0, Unsupported: 0), report.Totals);
        var effects = report.Sources.SelectMany(s => s.Other).SelectMany(f => f.Effects).ToDictionary(e => e.EffectId!);
        Assert.Equal(AutomationStatus.Assisted, effects["gap"].Effective);
        Assert.Equal(AutomationStatus.Assisted, effects["tail-extra"].Effective);
        Assert.Equal(AutomationStatus.Automatic, effects["follow"].Effective);
        Assert.Equal(AutomationStatus.Automatic, effects["burst"].Effective);
    }
}
