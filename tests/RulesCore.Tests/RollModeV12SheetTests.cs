namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (G7), Task 3: rollMode effects with the targets <c>attack</c>, <c>initiative</c> and <c>attacksAgainst</c> and the
/// members <c>tags</c>, <c>ability</c>, <c>unlessCondition</c> and <c>prevent</c>, on the calculated sheet. Original fixtures built here
/// on the companions pack (a character with a Warden companion whose bite is an attack tagged with the companion id).
/// </summary>
public class RollModeV12SheetTests
{
    private static readonly Guid SourceId = Guid.Parse("6a7e0000-0000-4000-8000-000000000001");

    private static InMemoryContentCatalog Catalog(params ContentRevision[] extra)
    {
        var pack = Fixtures.CompanionsPack();
        return new(pack.Sources, [.. pack.Revisions, .. extra]);
    }

    private static Character Handler() => Fixtures.Load("pm-handler-l3.json");

    private static ContentRevision Revision(ContentKind kind, string name, int n, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"6a7e0077-0000-4000-8000-{0x200 + n:X12}"), RevisionId = Guid.Parse($"6a7e0077-0000-4000-8000-{0x300 + n:X12}"),
        SchemaVersion = 12, Kind = kind, Name = name, RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(SourceId), Status = RevisionStatus.Published, Effects = effects,
    };

    private static ContentRevision Feature(string name, int n, params Effect[] effects) => Revision(ContentKind.Feature, name, n, effects);

    private static AttackEffect Strike(string id, WeaponAttack kind, Ability ability, params string[] tags) => new()
    {
        Id = id, AttackId = id, Label = $"Fixture {id}", Kind = kind, Abilities = [ability], Damage = new("1", "6"), DamageType = "slashing",
        Tags = tags.Length == 0 ? null : tags,
    };

    private static RollModeEffect Mode(string id, string target, RollMode mode = RollMode.Advantage, string? toggle = null, string[]? tags = null,
        Ability? ability = null, string? unless = null, bool? prevent = null) => new()
    {
        Id = id, Target = target, Mode = mode, Toggle = toggle, Tags = tags, Ability = ability, UnlessCondition = unless, Prevent = prevent,
    };

    private static ToggleEffect Stance() => new() { Id = "stance", ToggleId = "stance", Label = "Fixture stance" };

    private static CharacterSheet Sheet(Character character, params ContentRevision[] extra) =>
        CharacterCalculator.Calculate(character with { Pins = [.. character.Pins, .. extra.Where(e => e.Kind == ContentKind.Feature).Select(e => e.Reference)] }, Catalog(extra));

    private static Character StanceOn(ContentRevision feature, Character? character = null) =>
        (character ?? Handler()) with { Play = (character ?? Handler()).Play with { Toggles = [new ActiveToggle(feature.ContentId, "stance")] } };

    private static Character WithCondition(Character character, string condition) =>
        character with { Play = character.Play with { Conditions = [condition] } };

    private static AttackEntry Entry(CharacterSheet sheet, string effectId) => Assert.Single(sheet.Attacks!, a => a.EffectId == effectId);

    [Fact]
    public void Initiative_target_reaches_initiative_only()
    {
        var feature = Feature("Fixture Alert", 1, Mode("alert", "initiative"));

        var sheet = Sheet(Handler(), feature);

        Assert.Equal(RollMode.Advantage, sheet.Field(FieldIds.Initiative).Mode!.Automatic);
        Assert.Null(sheet.Field("ability.dex.mod").Mode);
        Assert.Null(sheet.Field("skill.stealth").Mode);
        Assert.Null(sheet.AttackModes);
        Assert.Null(sheet.AttacksAgainst);
    }

    [Fact]
    public void Attack_mode_reaches_matching_entries()
    {
        var feature = Feature("Fixture Duelist", 2,
            Strike("stab", WeaponAttack.Melee, Ability.Str, "blade"), Strike("throw", WeaponAttack.Ranged, Ability.Dex, "blade"),
            Mode("melee-only", "attack", tags: ["melee"]));
        var sword = Revision(ContentKind.Item, "Fixture Sword", 3,
            new WeaponEffect { Id = "sword", Category = WeaponCategory.Martial, Attack = WeaponAttack.Melee, Damage = "1d8", DamageType = "slashing", WeaponKey = "fixture-sword" });
        var bow = Revision(ContentKind.Item, "Fixture Bow", 4,
            new WeaponEffect { Id = "bow", Category = WeaponCategory.Martial, Attack = WeaponAttack.Ranged, Damage = "1d6", DamageType = "piercing", WeaponKey = "fixture-bow" });
        var character = Handler() with { Equipment = [new(sword.Reference, true), new(bow.Reference, true)] };

        var sheet = Sheet(character, feature, sword, bow);

        Assert.Equal(RollMode.Advantage, Entry(sheet, "stab").Mode!.Automatic);
        Assert.Equal(RollMode.Advantage, Entry(sheet, "sword").Mode!.Automatic);
        Assert.Null(Entry(sheet, "throw").Mode);
        Assert.Null(Entry(sheet, "bow").Mode);
        Assert.Equal("Fixture Duelist", Assert.Single(Entry(sheet, "stab").Mode!.Reasons).Source);
        var listed = Assert.Single(sheet.AttackModes!);
        Assert.Equal(RollMode.Advantage, listed.Reason.Mode);
        Assert.Equal(["melee"], listed.Tags);
        Assert.Null(listed.Ability);

        var all = Sheet(character, Feature("Fixture Everything", 5, Strike("stab", WeaponAttack.Melee, Ability.Str), Strike("throw", WeaponAttack.Ranged, Ability.Dex),
            Mode("any", "attack", tags: ["all"])), sword, bow);
        Assert.All(new[] { "stab", "throw", "sword", "bow" }, id => Assert.Equal(RollMode.Advantage, Entry(all, id).Mode!.Automatic));
    }

    [Fact]
    public void Feature_tags_match_the_attack_own_tags()
    {
        var feature = Feature("Fixture Bladework", 6,
            Strike("cut", WeaponAttack.Melee, Ability.Str, "blade"), Strike("bash", WeaponAttack.Melee, Ability.Str, "blunt"),
            Mode("blade-only", "attack", tags: ["blade"]));

        var sheet = Sheet(Handler(), feature);

        Assert.NotNull(Entry(sheet, "cut").Mode);
        Assert.Null(Entry(sheet, "bash").Mode);
    }

    [Fact]
    public void Strength_only_mode_skips_a_Dexterity_entry()
    {
        var feature = Feature("Fixture Brawn", 7,
            Strike("heavy", WeaponAttack.Melee, Ability.Str), Strike("quick", WeaponAttack.Melee, Ability.Dex),
            Mode("brawn", "attack", tags: ["all"], ability: Ability.Str));

        var sheet = Sheet(Handler(), feature);

        Assert.Equal(Ability.Dex, Entry(sheet, "quick").Ability);
        Assert.Null(Entry(sheet, "quick").Mode);
        Assert.Equal(RollMode.Advantage, Entry(sheet, "heavy").Mode!.Automatic);
        Assert.Equal(Ability.Str, Assert.Single(sheet.AttackModes!).Ability);
    }

    [Fact]
    public void Companion_attack_gets_no_mode()
    {
        var feature = Feature("Fixture Fury", 8, Mode("fury", "attack", tags: ["all"]));

        var sheet = Sheet(Handler(), feature);

        var bite = Assert.Single(sheet.Attacks!, a => a.EffectId == "warden-bite");
        Assert.NotNull(bite.CompanionId);
        Assert.Null(bite.Mode);
        Assert.Single(sheet.AttackModes!);
    }

    [Theory]
    [InlineData("attack")]
    [InlineData("initiative")]
    [InlineData("attacksAgainst")]
    [InlineData("save.wis")]
    public void Held_condition_leaves_the_mode_out(string target)
    {
        var feature = Feature("Fixture Calm", 9, Strike("stab", WeaponAttack.Melee, Ability.Str),
            Mode("calm", target, tags: target == "attack" ? ["all"] : null, unless: "frightened"));

        var free = Sheet(Handler(), feature);
        var held = Sheet(WithCondition(Handler(), "frightened"), feature);

        Assert.Single(Reasons(free, target));
        Assert.Empty(Reasons(held, target));
    }

    private static IReadOnlyList<RollModeReason> Reasons(CharacterSheet sheet, string target) => target switch
    {
        "attack" => sheet.AttackModes?.Select(m => m.Reason).ToList() ?? [],
        "attacksAgainst" => sheet.AttacksAgainst?.Reasons ?? [],
        "initiative" => sheet.Field(FieldIds.Initiative).Mode?.Reasons ?? [],
        _ => sheet.Field(target).Mode?.Reasons ?? [],
    };

    [Fact]
    public void Toggle_gates_both_sides()
    {
        var feature = Feature("Fixture Reckless", 10, Stance(), Strike("stab", WeaponAttack.Melee, Ability.Str),
            Mode("out", "attack", toggle: "stance", tags: ["all"]), Mode("in", "attacksAgainst", toggle: "stance"));

        var off = Sheet(Handler(), feature);
        Assert.Null(off.AttackModes);
        Assert.Null(off.AttacksAgainst);
        Assert.Null(Entry(off, "stab").Mode);

        var on = Sheet(StanceOn(feature), feature);
        Assert.Single(on.AttackModes!);
        Assert.Equal(RollMode.Advantage, on.AttacksAgainst!.Automatic);
        Assert.Equal(RollMode.Advantage, Entry(on, "stab").Mode!.Automatic);
    }

    [Fact]
    public void Prevent_cancels_advantage_against_you_until_incapacitated()
    {
        var stance = Feature("Fixture Reckless", 11, Stance(), Mode("in", "attacksAgainst", toggle: "stance"));
        var ward = Feature("Fixture Ward", 12, Mode("ward", "attacksAgainst", prevent: true, unless: "incapacitated"));
        var character = StanceOn(stance);

        var warded = Sheet(character, stance, ward).AttacksAgainst!;
        Assert.Equal(RollMode.Normal, warded.Automatic);
        Assert.Equal(["Fixture Reckless", "Fixture Ward"], warded.Reasons.Select(r => r.Source));
        Assert.True(warded.Reasons[1].Prevent);
        Assert.False(warded.Reasons[0].Prevent);

        var down = Sheet(WithCondition(character, "incapacitated"), stance, ward).AttacksAgainst!;
        Assert.Equal(RollMode.Advantage, down.Automatic);
        Assert.Equal(["Fixture Reckless"], down.Reasons.Select(r => r.Source));
    }

    [Theory]
    [InlineData("paralyzed")]
    [InlineData("petrified")]
    [InlineData("stunned")]
    [InlineData("unconscious")]
    public void Unconscious_counts_as_incapacitated_for_unless(string condition)
    {
        var stance = Feature("Fixture Reckless", 11, Stance(), Mode("in", "attacksAgainst", toggle: "stance"));
        var ward = Feature("Fixture Ward", 12, Mode("ward", "attacksAgainst", prevent: true, unless: "incapacitated"));

        var down = Sheet(WithCondition(StanceOn(stance), condition), stance, ward).AttacksAgainst!;

        Assert.Equal(RollMode.Advantage, down.Automatic);
        Assert.Equal(["Fixture Reckless"], down.Reasons.Select(r => r.Source));
    }

    [Fact]
    public void V11_sheet_is_unchanged()
    {
        var instinct = Fixtures.Load("pm-instinct-l4.json");

        var sheet = CharacterCalculator.Calculate(instinct, Fixtures.PlayMechanicsCatalog());

        Assert.Null(sheet.AttackModes);
        Assert.Null(sheet.AttacksAgainst);
        Assert.All(sheet.Attacks ?? [], a => Assert.Null(a.Mode));
        Assert.Equal(RollMode.Advantage, sheet.Field("save.dex").Mode!.Automatic);
        var json = System.Text.Json.JsonSerializer.SerializeToNode(sheet, RulesJson.Options)!.ToJsonString();
        Assert.DoesNotContain("attackModes", json, StringComparison.Ordinal);
        Assert.DoesNotContain("attacksAgainst", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"prevent\"", json, StringComparison.Ordinal);
    }
}
