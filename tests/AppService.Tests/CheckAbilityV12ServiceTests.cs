using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Content v12 (G10): a field roll follows the ability a skill resolved to, and climb and swim speed reach the sheet.
/// Original fixtures (<c>tests/RulesFixtures/fixture-pack-check-ability.json</c>) on the combat pack the development hosts seed.
/// </summary>
public class CheckAbilityV12ServiceTests
{
    private static ContentReference V12(int n) => new(Guid.Parse($"6f14c000-0000-4000-8000-{n:D12}"), Guid.Parse($"6f14e000-0000-4000-8000-{n:D12}"));

    private static readonly Guid PackSource = Guid.Parse("6f145000-0000-4000-8000-000000001501");
    private static readonly ContentReference LimberGrasp = V12(1501);
    private static readonly ContentReference BookishHunch = V12(1502);
    private static readonly ContentReference WallStrider = V12(1503);
    private static readonly ContentReference TidePaddler = V12(1504);
    private static readonly ContentReference QuickstepKnack = V12(1505);

    private static readonly ContentReference Longblade = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000001"), Guid.Parse("5f6de000-0000-4000-8000-000000000001"));
    private static readonly ContentReference Duelist = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000011"), Guid.Parse("5f6de000-0000-4000-8000-000000000011"));

    /// <summary>Hands out the scripted faces in order (1-based faces, as the real source does).</summary>
    private sealed class ScriptedRandom(params int[] faces) : IRandomSource
    {
        private int _next;

        public int Next(int sides) => faces[_next++];
    }

    // Strength 8 (-1), Dexterity 16 (+3): Dexterity is the better Athletics ability.
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

    private static (TempApp Temp, Guid Id) Start(Character character, params int[] faces)
    {
        var temp = new TempApp();
        temp.App.Random = new ScriptedRandom(faces);
        temp.AddPack("fixture-pack-check-ability.json");
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    [Fact]
    public void Athletics_field_roll_uses_the_dex_value()
    {
        var (temp, id) = Start(Hero(), 5);
        using var _ = temp;
        var before = temp.App.GetCharacter(id).Sheet.Field("skill.athletics").Value;
        var character = temp.App.GetCharacter(id).Character with { Pins = [Duelist, LimberGrasp] };
        temp.App.SaveCharacter(character);
        var after = temp.App.GetCharacter(id).Sheet.Field("skill.athletics").Value;

        var record = temp.App.Roll(new RollCommand(id, Field: "skill.athletics"));

        Assert.Equal(before + 4, after); // Dexterity +3 replaces Strength -1
        Assert.Equal(5 + after, record.Total);
        Assert.Null(record.AutomaticMode); // no roll mode applies: reported as none (Normal), not as a mode
    }

    [Fact]
    public void Bookish_hunch_rolls_arcana_with_wisdom()
    {
        // Intelligence 8 (-1), Wisdom 16 (+3): the Wisdom-based Arcana is 4 higher than the printed one.
        var hero = Hero(BookishHunch) with { BaseAbilities = new(10, 10, 12, 8, 16, 10) };
        var (temp, id) = Start(hero, 7);
        using var _ = temp;
        var plain = temp.App.GetCharacter(id).Character with { Pins = [Duelist] };
        temp.App.SaveCharacter(plain);
        var before = temp.App.GetCharacter(id).Sheet.Field("skill.arcana").Value;
        temp.App.SaveCharacter(plain with { Pins = [Duelist, BookishHunch] });
        var after = temp.App.GetCharacter(id).Sheet.Field("skill.arcana").Value;

        var record = temp.App.Roll(new RollCommand(id, Field: "skill.arcana"));

        Assert.Equal(before + 4, after);
        Assert.Equal(7 + after, record.Total);
    }

    [Fact]
    public void Dex_check_mode_applies_to_the_athletics_roll()
    {
        // Advantage on Dexterity skill checks reaches a Dexterity-based Athletics; disadvantage on Strength ones no longer does.
        var (temp, id) = Start(Hero(LimberGrasp, QuickstepKnack), 5, 15);
        using var _ = temp;
        var swapped = temp.App.Roll(new RollCommand(id, Field: "skill.athletics"));

        Assert.Equal(RollMode.Advantage, swapped.AutomaticMode);
        Assert.Equal(2, swapped.Dice.Count);

        // Without the substitution Athletics stays a Strength skill: only the disadvantage applies.
        var (temp2, id2) = Start(Hero(QuickstepKnack), 5, 15);
        using var __ = temp2;
        var printed = temp2.App.Roll(new RollCommand(id2, Field: "skill.athletics"));

        Assert.Equal(RollMode.Disadvantage, printed.AutomaticMode);
        Assert.Equal(2, printed.Dice.Count);
    }

    [Fact]
    public void Climb_speed_equals_walking_speed_and_swim_speed_is_30()
    {
        var (temp, id) = Start(Hero(WallStrider, TidePaddler), 1);
        using var _ = temp;

        var sheet = temp.App.GetCharacter(id).Sheet;

        Assert.True(sheet.Field("speed").Value > 0);
        Assert.Equal(sheet.Field("speed").Value, sheet.Field("speed.climb").Value);
        Assert.Equal(30, sheet.Field("speed.swim").Value);
    }

    [Fact]
    public void Fixture_features_are_counted_automatic()
    {
        var (temp, _) = Start(Hero(LimberGrasp), 1);
        using var __ = temp;

        var report = temp.App.Coverage(new(PackSource));

        var totals = Assert.Single(report.Sources).Totals;
        Assert.Equal(6, totals.Automatic); // one per effect: two checkAbility, two speed modifiers, two roll modes
        Assert.Equal((0, 0, 0), (totals.Assisted, totals.Reference, totals.Unsupported));
    }

    [Fact]
    public void Publishing_the_pack_requires_12()
    {
        using var temp = new TempApp();
        var pack = temp.AddPack("fixture-pack-check-ability.json");

        // Quickstep Knack only uses older roll modes, so it publishes lower; the other four each need v12.
        foreach (var revision in pack.Revisions.Where(r => r.Name != "Fixture: Quickstep Knack"))
        {
            Assert.Equal(12, revision.SchemaVersion);
            var draft = revision with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft };
            var published = temp.App.Publish(temp.App.SaveDraft(draft));
            Assert.Equal((12, 12), (temp.App.Store.FindRevision(published.Published)!.SchemaVersion, published.Report.RequiredSchemaVersion));
        }
    }
}
