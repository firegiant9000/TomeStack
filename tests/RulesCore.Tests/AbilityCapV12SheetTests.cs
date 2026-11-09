using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content v12 G5, Task 4a: an ability-score increase may carry its own cap (21 to 30). Increases sort lowest ceiling first, so the
/// content order does not matter; penalties come last and are never capped; content without a cap calculates as before.
/// </summary>
public class AbilityCapV12SheetTests
{
    private static readonly Guid Holder = Guid.Parse("5f5dc000-0000-4000-8000-0000000000c1");

    private static ContentRevision Revision(Guid contentId, int schema, params Effect[] effects) => new()
    {
        ContentId = contentId, RevisionId = Guid.NewGuid(), SchemaVersion = schema, Kind = ContentKind.Feat,
        Name = "Fixture: X", RulesFamilies = [RulesFamilies.Srd521], Status = RevisionStatus.Published,
        Provenance = new(Fixtures.SourceShared),
        Effects = effects,
    };

    private static ModifierEffect Bonus(string id, int amount, int? cap = null) =>
        new() { Id = id, Operation = ModifierOperation.Bonus, Target = FieldIds.Score(Ability.Str), Value = $"{amount}", Cap = cap };

    private static DerivedValue Strength(int baseStr, int schema, PlayState? play, params ContentRevision[] revisions)
    {
        var pack = Fixtures.Pack();
        var fixture = Fixtures.Srd521Character();
        var character = fixture with
        {
            Pins = [.. fixture.Pins, .. revisions.Select(r => r.Reference)],
            BaseAbilities = fixture.BaseAbilities with { Str = baseStr },
            Play = play ?? fixture.Play,
        };
        return CharacterCalculator.Calculate(character, new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, .. revisions])).Field(FieldIds.Score(Ability.Str));
    }

    private static DerivedValue Strength(int baseStr, int schema, params Effect[] effects) =>
        Strength(baseStr, schema, null, Revision(Holder, schema, effects));

    [Fact]
    public void Capped_increase_raises_the_score_above_20()
    {
        var strength = Strength(20, 12, Bonus("a", 4, 25));

        Assert.Equal(24, strength.Value);
        Assert.Contains(strength.Trace, t => t.Description == "Bonus from feat 'Fixture: X' (it can raise the score up to 25)");
    }

    [Fact]
    public void Capped_increase_stops_at_its_cap()
    {
        var strength = Strength(23, 12, Bonus("a", 4, 25));

        Assert.Equal(25, strength.Value);
        var step = strength.Trace.Single(t => t.Description.Contains("capped", StringComparison.Ordinal));
        Assert.Equal("Bonus from feat 'Fixture: X', capped: increases cannot raise an ability score above 25 (+4 would give 27)", step.Description);
        Assert.Equal(2, step.Amount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plain_and_capped_increases_give_the_same_score_in_either_order(bool cappedFirst)
    {
        Effect[] effects = cappedFirst ? [Bonus("c", 4, 25), Bonus("p", 2)] : [Bonus("p", 2), Bonus("c", 4, 25)];

        Assert.Equal(24, Strength(18, 12, effects).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Two_caps_combine_lowest_first(bool lowFirst)
    {
        Effect[] effects = lowFirst ? [Bonus("l", 2, 22), Bonus("h", 2, 24)] : [Bonus("h", 2, 24), Bonus("l", 2, 22)];

        Assert.Equal(24, Strength(20, 12, effects).Value);
    }

    [Fact]
    public void Score_above_the_cap_is_not_lowered()
    {
        Assert.Equal(26, Strength(26, 12, Bonus("a", 2, 25)).Value);
    }

    [Fact]
    public void Penalty_applies_after_the_capped_increase()
    {
        Assert.Equal(23, Strength(22, 12, Bonus("down", -2), Bonus("up", 4, 25)).Value);
    }

    [Fact]
    public void Set_still_overrides_a_capped_score()
    {
        var set = new ModifierEffect { Id = "set", Operation = ModifierOperation.Set, Target = FieldIds.Score(Ability.Str), Value = "19" };

        Assert.Equal(19, Strength(23, 12, Bonus("a", 4, 25), set).Value);
    }

    [Fact]
    public void Toggled_capped_increase_applies_only_while_on()
    {
        var holder = Revision(Holder, 12, new ToggleEffect { Id = "t", ToggleId = "might", Label = "Might" }, Bonus("a", 4, 25) with { Toggle = "might", Timing = EffectTiming.WhileActive });

        Assert.Equal(20, Strength(20, 12, null, holder).Value);
        Assert.Equal(24, Strength(20, 12, new PlayState { Toggles = [new ActiveToggle(Holder, "might")] }, holder).Value);
    }

    [Fact]
    public void V11_revision_with_cap_stops_at_20()
    {
        // Below schema 12 the cap is extension data the calculator must not honour.
        var strength = Strength(20, 11, Bonus("a", 4, 25));

        Assert.Equal(20, strength.Value);
        Assert.Contains(strength.Trace, t => t.Description == "Bonus from feat 'Fixture: X', capped: increases cannot raise an ability score above 20 (+4 would give 24)");
    }

    [Fact]
    public void Cap_on_an_item_mod_modifier_is_ignored()
    {
        // An unvalidated item whose mod carries a nested capped bonus: the calculator ignores the cap (validation refuses it).
        var longblade = new ContentReference(Guid.Parse("6a7e00f3-0000-4000-8000-000000000011"), Guid.Parse("6a7e00f3-0000-4000-8000-000000000111"));
        var mod = Revision(Guid.Parse("5f5dc000-0000-4000-8000-0000000000c2"), 12, new ItemModEffect { Id = "im", AppliesTo = ModTarget.Weapon, Modifiers = [Bonus("n", 4, 30)] })
            with { Kind = ContentKind.Item, Name = "Fixture Cap Rune" };
        var packs = new[] { Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.ItemModsPack() };
        var catalog = new InMemoryContentCatalog([.. packs.SelectMany(p => p.Sources)], [.. packs.SelectMany(p => p.Revisions), mod]);
        var tinker = Fixtures.Load("pm-tinker-l3.json");
        var character = tinker with
        {
            BaseAbilities = tinker.BaseAbilities with { Str = 20 },
            Equipment = [new EquipmentEntry(longblade, Equipped: true, Id: Guid.NewGuid(), Mods: [mod.Reference])],
        };

        var strength = CharacterCalculator.Calculate(character, catalog).Field(FieldIds.Score(Ability.Str));

        Assert.Equal(20, strength.Value);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(5)]
    public void Out_of_range_cap_is_ignored_by_the_calculator(int cap)
    {
        var strength = Strength(20, 12, Bonus("a", 4, cap));

        Assert.Equal(20, strength.Value);
        Assert.Contains(strength.Trace, t => t.Description == "Bonus from feat 'Fixture: X', capped: increases cannot raise an ability score above 20 (+4 would give 24)");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_bonuses_in_a_group_prefer_the_higher_cap(bool cappedFirst)
    {
        var plain = Bonus("p", 2) with { Stacking = StackingRule.HighestInGroup, StackGroup = "g" };
        var capped = Bonus("c", 2, 22) with { Stacking = StackingRule.HighestInGroup, StackGroup = "g" };
        Effect[] effects = cappedFirst ? [capped, plain] : [plain, capped];

        Assert.Equal(22, Strength(20, 12, effects).Value);
    }

    [Fact]
    public void Uncapped_increases_keep_todays_trace()
    {
        var strength = Strength(18, 12, Bonus("a", 1), Bonus("b", 2));

        Assert.Equal(20, strength.Value);
        var adds = strength.Trace.Where(t => t.Operation == "add").ToList();
        Assert.Equal("Bonus from feat 'Fixture: X'", adds[0].Description);
        Assert.Equal("Bonus from feat 'Fixture: X', capped: increases cannot raise an ability score above 20 (+2 would give 21)", adds[1].Description);
        Assert.Equal(1, adds[0].Amount);
        Assert.Equal(1, adds[1].Amount);
    }
}
