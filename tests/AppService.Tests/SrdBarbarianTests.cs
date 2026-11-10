using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// C2-1 on the bundled SRD content (srd-&lt;family&gt;-barbarian.json): a Barbarian of each family, levels 1–20, side by side.
/// Values are the SRD class tables' (docs/licensing/srd-pack-review.md, "C2-1"). The update tests cover SPEC I-06 for
/// characters that already hold the older Barbarian revisions.
/// </summary>
public class SrdBarbarianTests
{
    private static readonly string[] Families = [RulesFamilies.Srd51, RulesFamilies.Srd521];

    private static string Prefix(string family) => family == RulesFamilies.Srd51 ? "51" : "52";

    /// <summary>A bundled content: the content id and last group of the content, and the revision's last group (default the same).</summary>
    private static ContentReference Ref(string family, long content, long? revision = null) =>
        new(Guid.Parse($"{Prefix(family)}c00000-0000-4000-8000-{content:D12}"), Guid.Parse($"{Prefix(family)}e00000-0000-4000-8000-{revision ?? content:D12}"));

    private static long ClassContent(string family) => family == RulesFamilies.Srd51 ? 3 : 11;

    /// <summary>The newest Barbarian: the C2-6 v12 revision (srd-&lt;family&gt;-barbarian-v12.json).</summary>
    private static ContentReference NewClass(string family) => Ref(family, ClassContent(family), family == RulesFamilies.Srd51 ? 12000003 : 12000011);

    /// <summary>The 5.1 Barbarian as C2-1 left it, before the v12 revisions.</summary>
    private static ContentReference PreV12Class(string family) => Ref(family, ClassContent(family), family == RulesFamilies.Srd51 ? 409003 : 409011);

    private static ContentReference PreV12Berserker(string family) => Ref(family, BerserkerContent(family), family == RulesFamilies.Srd51 ? 409014 : 409023);

    private static ContentReference V12(long content, long revision) => Ref(RulesFamilies.Srd51, content, revision);

    private static ContentReference M1Class(string family) => Ref(family, ClassContent(family), ClassContent(family));

    private static ContentReference ClassesPackClass(string family) => Ref(family, ClassContent(family), 300001);

    private static long BerserkerContent(string family) => family == RulesFamilies.Srd51 ? 14 : 23;

    private static ContentReference NewBerserker(string family) => Ref(family, BerserkerContent(family), family == RulesFamilies.Srd51 ? 12000014 : 12000023);

    private static ContentReference OldBerserker(string family) => Ref(family, BerserkerContent(family));

    private static ContentReference RageContent(string family) => Ref(family, family == RulesFamilies.Srd51 ? 4 : 12, family == RulesFamilies.Srd51 ? 409004 : 409012);

    private static string SubclassChoice(string family) => family == RulesFamilies.Srd51 ? "primal-path" : "barbarian-subclass";

    private static IEnumerable<ContentReference> Skills(string family, int skip, int take) =>
        Enumerable.Range(family == RulesFamilies.Srd51 ? 8 : 17, 6).Skip(skip).Take(take).Select(n => Ref(family, n));

    private static ContentReference Greataxe(string family) =>
        TomeStackApp.LoadBundledPack($"TomeStack.Content.{(family == RulesFamilies.Srd51 ? "srd-5.1" : "srd-5.2.1")}-equipment.json").Revisions.Single(r => r.Name == "Greataxe").Reference;

    /// <summary>
    /// A Barbarian of <paramref name="level"/> with the choices answered: two class skills, the Berserker from level 3 and, in
    /// 5.2.1, the Primal Knowledge skill. Str 16, Dex 14, Con 16 (+3).
    /// </summary>
    private static Character Barbarian(string family, int level, IReadOnlyList<EquipmentEntry>? equipment = null, AbilityScores? scores = null, ContentReference? cls = null, ContentReference? berserker = null)
    {
        cls ??= NewClass(family);
        List<ChoiceSelection> choices = [new(cls, "barbarian-skills", [.. Skills(family, 0, 2)])];
        if (level >= 3)
        {
            choices.Add(new(cls, SubclassChoice(family), [berserker ?? NewBerserker(family)]));
            if (family == RulesFamilies.Srd521)
                choices.Add(new(Ref(family, 25), "primal-knowledge-skill", [.. Skills(family, 2, 1)]));
        }
        return new Character
        {
            Id = Guid.NewGuid(), Name = "Test SRD Barbarian", RulesFamily = family, Level = level,
            Classes = [new(cls, level)],
            BaseAbilities = scores ?? new(16, 14, 16, 8, 10, 10),
            Choices = choices,
            Equipment = equipment ?? [],
        };
    }

    private static ResourceValue Rages(CharacterSheet sheet) => sheet.Resources!.Single(r => r.ResourceId == "rage");

    private static readonly string[] Broken = ["choice.unresolved", "content.missing", "effect.invalid-formula", "choice.invalid-option"];

    private static void AssertClean(CharacterSheet sheet) => Assert.DoesNotContain(sheet.Diagnostics, d => Broken.Contains(d.Code));

    private static RollMode ModeOf(DerivedValue field) => field.Mode?.Automatic ?? RollMode.Normal;

    [Theory]
    // level, Rages (5.2.1), Rages (5.1: 6 at 20 stands for "Unlimited"), Rage Damage, attacks
    [InlineData(1, 2, 2, 2, 1)]
    [InlineData(2, 2, 2, 2, 1)]
    [InlineData(3, 3, 3, 2, 1)]
    [InlineData(4, 3, 3, 2, 1)]
    [InlineData(5, 3, 3, 2, 2)]
    [InlineData(6, 4, 4, 2, 2)]
    [InlineData(7, 4, 4, 2, 2)]
    [InlineData(8, 4, 4, 2, 2)]
    [InlineData(9, 4, 4, 3, 2)]
    [InlineData(10, 4, 4, 3, 2)]
    [InlineData(11, 4, 4, 3, 2)]
    [InlineData(12, 5, 5, 3, 2)]
    [InlineData(13, 5, 5, 3, 2)]
    [InlineData(14, 5, 5, 3, 2)]
    [InlineData(15, 5, 5, 3, 2)]
    [InlineData(16, 5, 5, 4, 2)]
    [InlineData(17, 6, 6, 4, 2)]
    [InlineData(18, 6, 6, 4, 2)]
    [InlineData(19, 6, 6, 4, 2)]
    [InlineData(20, 6, 6, 4, 2)]
    public void A_Barbarian_follows_its_tables_in_both_families_side_by_side(int level, int rages521, int rages51, int rageDamage, int attacks)
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Barbarian(family, level)).Sheet;

            var rages = Rages(sheet);
            Assert.Equal(family == RulesFamilies.Srd51 ? rages51 : rages521, rages.Maximum);
            Assert.Equal(rageDamage, sheet.Scales!.Single(s => s.ScaleId == "rageDamage").Value);
            Assert.Equal(attacks, sheet.Field(FieldIds.Attacks).Value);
            // d12: 12 + Con at level 1, then 7 + Con per level (SRD fixed value). Con 16 (+3); a level 20 has Primal Champion's
            // +4 applied (Con 20, +5).
            var con = level == 20 ? 5 : 3;
            Assert.Equal(12 + con + ((level - 1) * (7 + con)), sheet.Field(FieldIds.HitPoints).Value);
            Assert.Equal(AutomationStatus.Automatic, sheet.Field(FieldIds.Attacks).Automation);
            AssertClean(sheet);
            Assert.NotEmpty(sheet.Field(FieldIds.ArmorClass).Trace);
            Assert.NotEmpty(rages.Trace);
            Assert.Equal(10 + 2 + con, sheet.Field(FieldIds.ArmorClass).Value); // Unarmored Defense: 10 + Dex 2 + Con
        }
    }

    [Fact]
    public void Levels_1_to_20_are_automatic_in_both_families()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var nineteen = temp.App.SaveCharacter(Barbarian(family, 19)).Sheet;
            var twenty = temp.App.SaveCharacter(Barbarian(family, 20)).Sheet;
            foreach (var field in new[] { FieldIds.ArmorClass, FieldIds.Save(Ability.Str), FieldIds.Save(Ability.Con), FieldIds.Skill("athletics") })
            {
                Assert.Equal(AutomationStatus.Automatic, nineteen.Field(field).Automation);
                // C2-6: Primal Champion is applied (to a maximum of 24 in 5.1, 25 in 5.2.1), so level 20 is automatic too.
                Assert.Equal(AutomationStatus.Automatic, twenty.Field(field).Automation);
            }
        }
    }

    [Fact]
    public void Rage_applies_its_rider_resistances_and_strength_advantage_only_while_on()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var axe = Greataxe(family);
            var id = temp.App.SaveCharacter(Barbarian(family, 9, [new(axe, Equipped: true)])).Character.Id;
            var off = temp.App.GetCharacter(id).Sheet;
            // (At level 9 the attack also carries the v12 riders that are on whether or not the rage is: 5.1 Brutal Critical (critical-only), 5.2.1 Brutal Strike (once per turn, picked).)
            Assert.True((off.Attacks!.Single(a => a.Item == axe).Riders ?? []).All(r => r.Critical || r.OncePerTurn));
            Assert.DoesNotContain(off.DamageDefences ?? [], d => d.Kind == DamageDefenceKind.Resistance);
            Assert.Equal(RollMode.Normal, ModeOf(off.Field(FieldIds.Save(Ability.Str))));
            Assert.Equal(RollMode.Normal, ModeOf(off.Field(FieldIds.Modifier(Ability.Str))));

            var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: RageContent(family).ContentId, ToggleId: "rage")).Sheet;
            var rider = Assert.Single(on.Attacks!.Single(a => a.Item == axe).Riders!, r => !r.Critical && !r.OncePerTurn);
            Assert.Equal(3, rider.DamageBonus); // Rage Damage +3 at level 9
            foreach (var type in new[] { "bludgeoning", "piercing", "slashing" })
                Assert.Contains(on.DamageDefences!, d => d.Type == type && d.Kind == DamageDefenceKind.Resistance);
            Assert.Equal(RollMode.Advantage, ModeOf(on.Field(FieldIds.Save(Ability.Str))));
            Assert.Equal(RollMode.Advantage, ModeOf(on.Field(FieldIds.Modifier(Ability.Str)))); // Strength checks
            Assert.Equal(RollMode.Normal, ModeOf(on.Field(FieldIds.Save(Ability.Wis))));

            var offAgain = temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: RageContent(family).ContentId, ToggleId: "rage")).Sheet;
            Assert.DoesNotContain(offAgain.DamageDefences ?? [], d => d.Kind == DamageDefenceKind.Resistance);
            Assert.Equal(RollMode.Normal, ModeOf(offAgain.Field(FieldIds.Save(Ability.Str))));
        }
    }

    [Theory]
    [InlineData(19, 5)]
    [InlineData(20, 6)]
    public void A_5_1_Barbarian_rages_without_spending_at_level_20_and_spends_one_before(int level, int left)
    {
        using var temp = new TempApp();
        var id = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, level)).Character.Id;
        Assert.Equal(6, Rages(temp.App.GetCharacter(id).Sheet).Current);

        var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: RageContent(RulesFamilies.Srd51).ContentId, ToggleId: "rage")).Sheet;

        Assert.True(on.Toggles!.Single(t => t.ToggleId == "rage").On);
        Assert.Equal(left, Rages(on).Current);
    }

    [Fact]
    public void A_5_2_1_Barbarian_spends_a_rage_at_level_20_too()
    {
        using var temp = new TempApp();
        var id = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 20)).Character.Id;

        var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: RageContent(RulesFamilies.Srd521).ContentId, ToggleId: "rage")).Sheet;

        Assert.Equal(5, Rages(on).Current);
    }

    [Fact]
    public void Fast_Movement_adds_10_feet_from_level_5()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            Assert.Equal(30, temp.App.SaveCharacter(Barbarian(family, 4)).Sheet.Field(FieldIds.Speed).Value);
            var five = temp.App.SaveCharacter(Barbarian(family, 5)).Sheet.Field(FieldIds.Speed);
            Assert.Equal(40, five.Value);
            Assert.Equal(AutomationStatus.Automatic, five.Automation);
        }
    }

    [Fact]
    public void Extra_Attack_sets_two_attacks_from_level_5()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            Assert.Equal(1, temp.App.SaveCharacter(Barbarian(family, 4)).Sheet.Field(FieldIds.Attacks).Value);
            var five = temp.App.SaveCharacter(Barbarian(family, 5)).Sheet;
            Assert.Equal(2, five.Field(FieldIds.Attacks).Value);
            Assert.Contains(five.Features!, f => f.Name == "Extra Attack" && f.Effects.Any(e => e.Automation == AutomationStatus.Automatic));
        }
    }

    [Fact]
    public void Danger_Sense_is_automatic_in_5_2_1_and_assisted_in_5_1()
    {
        using var temp = new TempApp();
        var one = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 1)).Sheet;
        Assert.Equal(RollMode.Normal, ModeOf(one.Field(FieldIds.Save(Ability.Dex)))); // from level 2

        var modern = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 2)).Sheet.Field(FieldIds.Save(Ability.Dex));
        Assert.Equal(RollMode.Advantage, ModeOf(modern));

        var classic = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 2)).Sheet.Field(FieldIds.Save(Ability.Dex));
        Assert.Equal(RollMode.Normal, ModeOf(classic)); // not applied by itself: the sheet cannot see the effect
        Assert.Contains(classic.Mode!.Reasons, r => r.Mode == RollMode.Advantage && r.Source == "Danger Sense" && !r.IsAutomatic);
    }

    [Fact]
    public void Relentless_Rage_offers_a_response_while_raging()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var id = temp.App.SaveCharacter(Barbarian(family, 11)).Character.Id;
            var before = temp.App.GetCharacter(id).Sheet.Responses!.Single(r => r.ContentName == "Relentless Rage");
            Assert.Equal((ResponsePhase.HitPointOutcome, false), (before.Phase, before.Available));
            Assert.Contains("Rage", before.UnavailableReason, StringComparison.Ordinal);

            var raging = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: RageContent(family).ContentId, ToggleId: "rage")).Sheet;
            var response = raging.Responses!.Single(r => r.ContentName == "Relentless Rage");
            Assert.True(response.Available);
            Assert.Equal(family == RulesFamilies.Srd51 ? 1 : 22, response.Amount); // 5.1: 1 hit point; 5.2.1: twice the Barbarian level
        }
    }

    [Fact]
    public void Primal_Champion_in_5_2_1_adds_4_to_Strength_and_Constitution_up_to_25()
    {
        using var temp = new TempApp();
        var low = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 20, scores: new(18, 14, 16, 8, 10, 10))).Sheet;
        Assert.Equal(22, low.Field(FieldIds.Score(Ability.Str)).Value);
        Assert.Equal(20, low.Field(FieldIds.Score(Ability.Con)).Value);
        Assert.Equal(AutomationStatus.Automatic, low.Field(FieldIds.Score(Ability.Str)).Automation);
        Assert.NotEqual(AutomationStatus.Reference, low.Features!.Single(f => f.Name == "Primal Champion").Automation);

        // The cap: 22 goes to 25, not 26.
        var high = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 20, scores: new(22, 14, 22, 8, 10, 10))).Sheet;
        Assert.Equal(25, high.Field(FieldIds.Score(Ability.Str)).Value);
        Assert.Equal(25, high.Field(FieldIds.Score(Ability.Con)).Value);

        var before = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 19, scores: new(22, 14, 22, 8, 10, 10))).Sheet;
        Assert.Equal(22, before.Field(FieldIds.Score(Ability.Str)).Value);
    }

    [Fact]
    public void Reckless_attack_in_5_2_1_gives_advantage_on_any_strength_attack()
    {
        using var temp = new TempApp();
        var axe = Greataxe(RulesFamilies.Srd521);
        var bow = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-equipment.json").Revisions.Single(r => r.Name == "Longbow").Reference;
        var reckless = Ref(RulesFamilies.Srd521, 16, 12000016);
        var id = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 2, [new(axe, Equipped: true), new(bow, Equipped: true)])).Character.Id;

        var off = temp.App.GetCharacter(id).Sheet;
        Assert.Contains(off.Toggles!, t => t.ToggleId == "reckless" && !t.On);
        Assert.Null(AttackOf(off, axe).Mode);
        Assert.Null(off.AttacksAgainst);

        var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: reckless.ContentId, ToggleId: "reckless")).Sheet;
        Assert.Equal(RollMode.Advantage, AttackOf(on, axe).Mode!.Automatic);
        Assert.Equal(RollMode.Advantage, on.AttacksAgainst!.Automatic);
        // No melee tag: only the Strength gate. The longbow (Dexterity) has none.
        Assert.Null(AttackOf(on, bow).Mode);

        var again = temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: reckless.ContentId, ToggleId: "reckless")).Sheet;
        Assert.Null(AttackOf(again, axe).Mode);
        Assert.Null(again.AttacksAgainst);
    }

    [Fact]
    public void Feral_instinct_in_5_2_1_gives_initiative_advantage()
    {
        using var temp = new TempApp();
        Assert.Equal(RollMode.Normal, ModeOf(temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 6)).Sheet.Field(FieldIds.Initiative)));

        var seven = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 7)).Sheet.Field(FieldIds.Initiative);
        Assert.Equal(RollMode.Advantage, ModeOf(seven));
        Assert.Contains(seven.Mode!.Reasons, r => r.Mode == RollMode.Advantage && r.Source == "Feral Instinct" && r.IsAutomatic);
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(9, 1)]
    [InlineData(13, 1)]
    [InlineData(16, 1)]
    [InlineData(17, 2)]
    public void Brutal_strike_offers_a_once_per_turn_d10_rider(int level, int dice)
    {
        using var temp = new TempApp();
        var axe = Greataxe(RulesFamilies.Srd521);
        var strike = Ref(RulesFamilies.Srd521, 409106, 12409106);
        var id = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, level, [new(axe, Equipped: true)])).Character.Id;
        var entry = AttackOf(temp.App.GetCharacter(id).Sheet, axe);

        var plain = temp.App.Roll(new RollCommand(id, Weapon: axe, Damage: true));
        Assert.Equal(1, plain.Dice.Count(d => d.Sides == 12));
        Assert.Equal(0, plain.Dice.Count(d => d.Sides == 10)); // not applied unless picked

        if (dice == 0)
        {
            Assert.DoesNotContain(entry.Riders ?? [], r => r.OncePerTurn);
            return;
        }
        var rider = Assert.Single(entry.Riders!, r => r.OncePerTurn);
        Assert.Equal($"{dice}d10", rider.Dice);

        var picked = temp.App.Roll(new RollCommand(id, Weapon: axe, Damage: true, WithRiders: [new RiderPick(strike, "brutal-strike")]));
        Assert.Equal(dice, picked.Dice.Count(d => d.Sides == 10));
        Assert.Equal($"{entry.Damage}+{dice}d10", picked.Formula);
    }

    [Fact]
    public void Brutal_strike_is_assisted()
    {
        using var temp = new TempApp();
        var sheet = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 9)).Sheet;
        Assert.Equal(AutomationStatus.Assisted, sheet.Features!.Single(f => f.Name == "Brutal Strike").Automation);
    }

    [Fact]
    public void Indomitable_might_in_5_2_1_floors_strength_checks_at_the_score()
    {
        using var temp = new TempApp();
        var seventeen = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 17)).Character.Id;
        Assert.Null(temp.App.Roll(new RollCommand(seventeen, Field: FieldIds.Modifier(Ability.Str))).Floor);

        var id = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 18)).Character.Id;
        var strength = temp.App.Roll(new RollCommand(id, Field: FieldIds.Modifier(Ability.Str)));
        Assert.Equal(16, strength.Floor!.Total);
        Assert.Equal(["Indomitable Might"], strength.Floor.Reasons.Select(r => r.Source));
        Assert.Null(temp.App.Roll(new RollCommand(id, Field: FieldIds.Modifier(Ability.Dex))).Floor);
    }

    [Fact]
    public void Fast_movement_in_5_2_1_stops_in_heavy_armor()
    {
        using var temp = new TempApp();
        var chain = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-armor.json").Revisions.Single(r => r.Name == "Chain Mail").Reference;
        Assert.Equal(30, temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 5, [new(chain, Equipped: true)])).Sheet.Field(FieldIds.Speed).Value);

        var carried = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 5, [new(chain, Equipped: false)])).Sheet;
        Assert.Equal(40, carried.Field(FieldIds.Speed).Value);
        Assert.Equal(AutomationStatus.Automatic, carried.Field(FieldIds.Speed).Automation);
    }

    [Fact]
    public void Mindless_rage_in_5_2_1_grants_charm_and_fright_immunity_while_raging()
    {
        using var temp = new TempApp();
        var five = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 5)).Character.Id;
        var early = temp.App.Play(new(five, PlayActionKind.ToggleOn, Confirm: true, ContentId: RageContent(RulesFamilies.Srd521).ContentId, ToggleId: "rage")).Sheet;
        Assert.Null(early.ConditionImmunities);

        var id = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 6)).Character.Id;
        Assert.Null(temp.App.GetCharacter(id).Sheet.ConditionImmunities);

        var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: RageContent(RulesFamilies.Srd521).ContentId, ToggleId: "rage")).Sheet;
        Assert.Equal(["charmed", "frightened"], on.ConditionImmunities!.Select(c => c.Condition).Order(StringComparer.Ordinal));
        Assert.All(on.ConditionImmunities!, c => Assert.Equal("Mindless Rage", Assert.Single(c.Sources).Name));

        var off = temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: RageContent(RulesFamilies.Srd521).ContentId, ToggleId: "rage")).Sheet;
        Assert.Null(off.ConditionImmunities);
    }

    [Fact]
    public void A_C2_1_5_2_1_Barbarian_calculates_as_before_and_the_v12_updates_apply()
    {
        using var temp = new TempApp();
        var family = RulesFamilies.Srd521;
        var axe = Greataxe(family);
        var id = temp.App.SaveCharacter(PreV12Barbarian(family, axe)).Character.Id;
        var sheet = temp.App.GetCharacter(id).Sheet;

        AssertClean(sheet);
        Assert.DoesNotContain(sheet.Toggles!, t => t.ToggleId == "reckless");
        Assert.Equal(RollMode.Normal, ModeOf(sheet.Field(FieldIds.Initiative)));
        Assert.DoesNotContain(AttackOf(sheet, axe).Riders ?? [], r => r.OncePerTurn);
        Assert.Equal(AutomationStatus.Reference, sheet.Features!.First(f => f.Name == "Brutal Strike").Automation);

        var offers = temp.App.AvailableUpdates(id);
        var offer = Assert.Single(offers, o => o.Role == ReferenceRole.Class && o.Name == "Barbarian");
        Assert.Equal((PreV12Class(family), NewClass(family), true), (offer.From, offer.To, offer.Bundled));
        var berserker = Assert.Single(offers, o => o.From == PreV12Berserker(family));
        Assert.Equal(NewBerserker(family), berserker.To);
        Assert.Equal(PreV12Class(family), temp.App.GetCharacter(id).Character.Classes[0].Class);

        temp.App.ApplyUpdate(id, PreV12Berserker(family), NewBerserker(family), confirm: true);
        var updated = temp.App.ApplyUpdate(id, PreV12Class(family), NewClass(family), confirm: true).Sheet;
        AssertClean(updated);
        Assert.Contains(updated.Toggles!, t => t.ToggleId == "reckless");
        Assert.Equal(RollMode.Advantage, ModeOf(updated.Field(FieldIds.Initiative)));
        Assert.Equal("1d10", AttackOf(updated, axe).Riders!.Single(r => r.OncePerTurn).Dice);
        Assert.Equal(AutomationStatus.Assisted, updated.Features!.First(f => f.Name == "Brutal Strike").Automation);
        Assert.Contains(updated.Active!, r => r == NewBerserker(family));
    }

    [Fact]
    public void Primal_Champion_in_5_1_adds_4_to_Strength_and_Constitution_up_to_24()
    {
        using var temp = new TempApp();
        var low = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 20, scores: new(18, 14, 16, 8, 10, 10))).Sheet;
        Assert.Equal(22, low.Field(FieldIds.Score(Ability.Str)).Value);
        Assert.Equal(20, low.Field(FieldIds.Score(Ability.Con)).Value);
        Assert.Equal(AutomationStatus.Automatic, low.Field(FieldIds.Score(Ability.Str)).Automation);

        // The cap: Strength 22 goes to 24, not 26.
        var high = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 20, scores: new(22, 14, 22, 8, 10, 10))).Sheet;
        Assert.Equal(24, high.Field(FieldIds.Score(Ability.Str)).Value);
        Assert.Equal(24, high.Field(FieldIds.Score(Ability.Con)).Value);

        var before = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 19, scores: new(22, 14, 22, 8, 10, 10))).Sheet;
        Assert.Equal(22, before.Field(FieldIds.Score(Ability.Str)).Value);
    }

    // ---- C2-6: the 5.1 v12 revisions ---------------------------------------------------------------------------------------

    private static ContentReference Equipment51(string pack, string name) =>
        TomeStackApp.LoadBundledPack($"TomeStack.Content.srd-5.1-{pack}.json").Revisions.Single(r => r.Name == name).Reference;

    private static AttackEntry AttackOf(CharacterSheet sheet, ContentReference item) => sheet.Attacks!.Single(a => a.Item == item);

    [Fact]
    public void Reckless_attack_toggle_gives_advantage_and_advantage_against()
    {
        using var temp = new TempApp();
        var axe = Greataxe(RulesFamilies.Srd51);
        var bow = Equipment51("equipment", "Longbow");
        var reckless = V12(6, 12000006);
        var id = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 2, [new(axe, Equipped: true), new(bow, Equipped: true)])).Character.Id;

        var off = temp.App.GetCharacter(id).Sheet;
        Assert.Contains(off.Toggles!, t => t.ToggleId == "reckless" && !t.On);
        Assert.Null(AttackOf(off, axe).Mode);
        Assert.Null(off.AttacksAgainst);

        var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: reckless.ContentId, ToggleId: "reckless")).Sheet;
        Assert.Equal(RollMode.Advantage, AttackOf(on, axe).Mode!.Automatic);
        Assert.Equal(RollMode.Advantage, on.AttacksAgainst!.Automatic);
        // Melee attacks made with Strength only: the longbow (ranged, Dexterity) has none.
        Assert.Null(AttackOf(on, bow).Mode);

        var again = temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: reckless.ContentId, ToggleId: "reckless")).Sheet;
        Assert.Null(AttackOf(again, axe).Mode);
        Assert.Null(again.AttacksAgainst);
    }

    [Fact]
    public void Feral_instinct_gives_initiative_advantage()
    {
        using var temp = new TempApp();
        Assert.Equal(RollMode.Normal, ModeOf(temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 6)).Sheet.Field(FieldIds.Initiative)));

        var seven = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 7)).Sheet.Field(FieldIds.Initiative);
        Assert.Equal(RollMode.Advantage, ModeOf(seven));
        Assert.Contains(seven.Mode!.Reasons, r => r.Mode == RollMode.Advantage && r.Source == "Feral Instinct" && r.IsAutomatic);
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(9, 1)]
    [InlineData(13, 2)]
    [InlineData(17, 3)]
    public void Brutal_critical_adds_scaled_weapon_dice_on_a_critical(int level, int extra)
    {
        using var temp = new TempApp();
        var axe = Greataxe(RulesFamilies.Srd51);
        var id = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, level, [new(axe, Equipped: true)])).Character.Id;
        var entry = AttackOf(temp.App.GetCharacter(id).Sheet, axe);

        var normal = temp.App.Roll(new RollCommand(id, Weapon: axe, Damage: true));
        var critical = temp.App.Roll(new RollCommand(id, Weapon: axe, Damage: true, Critical: true));

        Assert.Equal(1, normal.Dice.Count(d => d.Sides == 12));
        // The weapon die doubles (2), then the Brutal Critical dice are the weapon's d12s, rolled once.
        Assert.Equal(2 + extra, critical.Dice.Count(d => d.Sides == 12));
        Assert.Equal(1 + extra, critical.Dice.Count(d => d.Sides == 12 && d.FromCritical));
        if (extra == 0)
            Assert.True(entry.Riders is null or { Count: 0 } || entry.Riders.All(r => !r.Critical || r.Dice is null or "0d12"));
        else
            Assert.Equal($"{extra}d12", entry.Riders!.Single(r => r.Critical).Dice);
    }

    [Fact]
    public void Indomitable_might_floors_strength_checks_at_the_score()
    {
        using var temp = new TempApp();
        var seventeen = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 17)).Character.Id;
        Assert.Null(temp.App.Roll(new RollCommand(seventeen, Field: FieldIds.Modifier(Ability.Str))).Floor);

        var id = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 18)).Character.Id;
        var strength = temp.App.Roll(new RollCommand(id, Field: FieldIds.Modifier(Ability.Str)));
        Assert.Equal(16, strength.Floor!.Total); // Strength 16
        Assert.Equal(["Indomitable Might"], strength.Floor.Reasons.Select(r => r.Source));
        Assert.True(strength.Total >= 16);

        // Not a Dexterity check.
        Assert.Null(temp.App.Roll(new RollCommand(id, Field: FieldIds.Modifier(Ability.Dex))).Floor);
    }

    [Fact]
    public void Fast_movement_stops_in_heavy_armor()
    {
        using var temp = new TempApp();
        var chain = Equipment51("armor", "Chain mail");
        var worn = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 5, [new(chain, Equipped: true)])).Sheet;
        Assert.Equal(30, worn.Field(FieldIds.Speed).Value);

        var carried = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 5, [new(chain, Equipped: false)])).Sheet;
        Assert.Equal(40, carried.Field(FieldIds.Speed).Value);
        Assert.Equal(AutomationStatus.Automatic, carried.Field(FieldIds.Speed).Automation);
    }

    [Fact]
    public void Mindless_rage_grants_charm_and_fright_immunity_while_raging()
    {
        using var temp = new TempApp();
        var five = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 5)).Character.Id;
        var early = temp.App.Play(new(five, PlayActionKind.ToggleOn, Confirm: true, ContentId: RageContent(RulesFamilies.Srd51).ContentId, ToggleId: "rage")).Sheet;
        Assert.Null(early.ConditionImmunities);

        var id = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 6)).Character.Id;
        Assert.Null(temp.App.GetCharacter(id).Sheet.ConditionImmunities);

        var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: RageContent(RulesFamilies.Srd51).ContentId, ToggleId: "rage")).Sheet;
        Assert.Equal(["charmed", "frightened"], on.ConditionImmunities!.Select(c => c.Condition).Order(StringComparer.Ordinal));
        Assert.All(on.ConditionImmunities!, c => Assert.Equal("Mindless Rage", Assert.Single(c.Sources).Name));

        var off = temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: RageContent(RulesFamilies.Srd51).ContentId, ToggleId: "rage")).Sheet;
        Assert.Null(off.ConditionImmunities);
    }

    [Fact]
    public void Reference_features_are_listed_as_reference()
    {
        using var temp = new TempApp();
        var names = new Dictionary<string, string[]>
        {
            // C2-6: the rest of the Barbarian is re-authored at v12 and is no longer Reference. These are the exact sets.
            [RulesFamilies.Srd521] = ["Persistent Rage", "Improved Brutal Strike", "Weapon Mastery", "Instinctive Pounce", "Retaliation", "Ability Score Improvement", "Epic Boon"],
            [RulesFamilies.Srd51] = ["Persistent Rage", "Intimidating Presence", "Retaliation", "Frenzy", "Ability Score Improvement"],
        };
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Barbarian(family, 20)).Sheet;
            var actual = sheet.Features!.Where(f => f.Automation == AutomationStatus.Reference).Select(f => f.Name).Distinct().Order(StringComparer.Ordinal);
            Assert.Equal(names[family].Order(StringComparer.Ordinal), actual);
        }
        var classic = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd51, 20)).Sheet;
        foreach (var name in new[] { "Reckless Attack", "Feral Instinct", "Brutal Critical", "Indomitable Might", "Mindless Rage", "Fast Movement", "Primal Champion" })
            Assert.DoesNotContain(classic.Features!, f => f.Name == name && f.Automation == AutomationStatus.Reference);

        var modern = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 20)).Sheet;
        foreach (var name in new[] { "Reckless Attack", "Feral Instinct", "Brutal Strike", "Indomitable Might", "Mindless Rage", "Fast Movement", "Primal Champion" })
            Assert.DoesNotContain(modern.Features!, f => f.Name == name && f.Automation == AutomationStatus.Reference);

        // Primal Knowledge (the unchanged M1 revision) has the skill choice as its only effect: the rest of its text, the
        // Strength-check option while raging, is by hand.
        var knowledge = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 20)).Sheet.Features!.Single(f => f.Name == "Primal Knowledge");
        Assert.NotEmpty(knowledge.Effects);
        Assert.All(knowledge.Effects, e => Assert.Equal("choice", e.Type));
        Assert.NotEqual(AutomationStatus.Reference, knowledge.Automation);
    }

    // ---- SPEC I-06: the update --------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, string> M1Fixtures = new()
    {
        [RulesFamilies.Srd521] = "characters/m1-acceptance-srd521-brenna.json",
        [RulesFamilies.Srd51] = "characters/m1-acceptance-srd51-korga.json",
    };

    /// <summary>The M1 fixture as it was saved on the class revision <paramref name="cls"/> (its choices follow the class).</summary>
    private static Character OnClass(string family, ContentReference cls)
    {
        var fixture = TempApp.LoadFixture<Character>(M1Fixtures[family]);
        var m1 = M1Class(family);
        return fixture with
        {
            Id = Guid.NewGuid(),
            Classes = [.. fixture.Classes.Select(c => c with { Class = cls })],
            Choices = [.. fixture.Choices.Select(c => c.Source == m1 ? c with { Source = cls } : c)],
        };
    }

    private static void AssertUpdatedBarbarian(CharacterSheet sheet, string family)
    {
        AssertClean(sheet);
        var subclass = sheet.Choices!.Single(c => c.ChoiceId == SubclassChoice(family));
        Assert.True(subclass.Resolved);
        Assert.Contains(sheet.Features!, f => f.Name == "Frenzy");
        Assert.Equal(3, Rages(sheet).Maximum);
        Assert.Contains(sheet.Toggles!, t => t.ToggleId == "rage");
    }

    [Fact]
    public void Existing_barbarians_see_a_class_update_and_keep_their_sheet()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        foreach (var start in new[] { M1Class(family), ClassesPackClass(family) })
        {
            var saved = temp.App.SaveCharacter(OnClass(family, start));
            var id = saved.Character.Id;
            var json = TempApp.Json(temp.App.GetCharacter(id).Character);

            var offers = temp.App.AvailableUpdates(id);
            var offer = Assert.Single(offers, o => o.Role == ReferenceRole.Class && o.Name == "Barbarian");
            Assert.Equal((start, NewClass(family), true), (offer.From, offer.To, offer.Bundled));
            // Offered, not applied: the sheet is the one it was.
            Assert.Equal(json, TempApp.Json(temp.App.GetCharacter(id).Character));
            Assert.Equal(saved.Sheet.Field(FieldIds.HitPoints).Value, temp.App.GetCharacter(id).Sheet.Field(FieldIds.HitPoints).Value);
            Assert.DoesNotContain(temp.App.GetCharacter(id).Sheet.Toggles ?? [], t => t.ToggleId == "rage" && t.Content.RevisionId == RageContent(family).RevisionId);
        }
    }

    [Fact]
    public void The_class_update_keeps_the_Berserker_resolved_in_either_order()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        foreach (var start in new[] { M1Class(family), ClassesPackClass(family) })
        {
            // (a) the class only.
            var a = temp.App.SaveCharacter(OnClass(family, start)).Character.Id;
            var viaClass = temp.App.ApplyUpdate(a, start, NewClass(family), confirm: true);
            Assert.Equal(NewClass(family), viaClass.Character.Classes[0].Class);
            AssertUpdatedBarbarian(viaClass.Sheet, family);
            Assert.Contains(viaClass.Sheet.Active!, r => r == OldBerserker(family)); // still the old Berserker revision, still valid

            // (b) the Berserker first, on the old class, then the class.
            var b = temp.App.SaveCharacter(OnClass(family, start)).Character.Id;
            var offer = Assert.Single(temp.App.AvailableUpdates(b), o => o.From == OldBerserker(family));
            Assert.Equal(NewBerserker(family), offer.To);
            var viaBerserker = temp.App.ApplyUpdate(b, offer.From, offer.To, confirm: true);
            Assert.Equal(start, viaBerserker.Character.Classes[0].Class);
            Assert.True(viaBerserker.Sheet.Choices!.Single(c => c.ChoiceId == SubclassChoice(family)).Resolved);
            Assert.Contains(viaBerserker.Sheet.Features!, f => f.Name == "Frenzy");
            AssertClean(viaBerserker.Sheet);

            var both = temp.App.ApplyUpdate(b, start, NewClass(family), confirm: true);
            AssertUpdatedBarbarian(both.Sheet, family);
            Assert.Contains(both.Sheet.Active!, r => r == NewBerserker(family));
        }
    }

    /// <summary>A level 9 Barbarian saved on the C2-1 revision of the class and of the Berserker, with a greataxe.</summary>
    private static Character PreV12Barbarian(string family, ContentReference axe) =>
        Barbarian(family, 9, [new(axe, Equipped: true)], cls: PreV12Class(family), berserker: PreV12Berserker(family));

    [Fact]
    public void A_C2_1_Barbarian_calculates_as_before_and_is_offered_the_v12_class()
    {
        using var temp = new TempApp();
        var axe = Greataxe(RulesFamilies.Srd51);
        var saved = temp.App.SaveCharacter(PreV12Barbarian(RulesFamilies.Srd51, axe));
        var id = saved.Character.Id;
        var sheet = temp.App.GetCharacter(id).Sheet;

        // As before: none of the v12 effects, the Reference text of the old revisions.
        AssertClean(sheet);
        Assert.DoesNotContain(sheet.Toggles!, t => t.ToggleId == "reckless");
        Assert.Equal(RollMode.Normal, ModeOf(sheet.Field(FieldIds.Initiative)));
        Assert.DoesNotContain(AttackOf(sheet, axe).Riders ?? [], r => r.Critical);
        Assert.Null(sheet.AttacksAgainst);
        Assert.Equal(AutomationStatus.Reference, sheet.Features!.First(f => f.Name == "Brutal Critical").Automation);
        Assert.Equal(40, sheet.Field(FieldIds.Speed).Value);

        // The update is offered (and not applied), for the class and for the Berserker.
        var offers = temp.App.AvailableUpdates(id);
        var offer = Assert.Single(offers, o => o.Role == ReferenceRole.Class && o.Name == "Barbarian");
        Assert.Equal((PreV12Class(RulesFamilies.Srd51), NewClass(RulesFamilies.Srd51), true), (offer.From, offer.To, offer.Bundled));
        var berserker = Assert.Single(offers, o => o.From == PreV12Berserker(RulesFamilies.Srd51));
        Assert.Equal(NewBerserker(RulesFamilies.Srd51), berserker.To);
        Assert.Equal(PreV12Class(RulesFamilies.Srd51), temp.App.GetCharacter(id).Character.Classes[0].Class);
    }

    [Fact]
    public void After_the_class_and_Berserker_updates_the_v12_effects_apply()
    {
        using var temp = new TempApp();
        var axe = Greataxe(RulesFamilies.Srd51);
        var id = temp.App.SaveCharacter(PreV12Barbarian(RulesFamilies.Srd51, axe)).Character.Id;

        temp.App.ApplyUpdate(id, PreV12Berserker(RulesFamilies.Srd51), NewBerserker(RulesFamilies.Srd51), confirm: true);
        var updated = temp.App.ApplyUpdate(id, PreV12Class(RulesFamilies.Srd51), NewClass(RulesFamilies.Srd51), confirm: true);
        var sheet = updated.Sheet;

        Assert.Equal(NewClass(RulesFamilies.Srd51), updated.Character.Classes[0].Class);
        AssertClean(sheet);
        Assert.Contains(sheet.Toggles!, t => t.ToggleId == "reckless");
        Assert.Equal(RollMode.Advantage, ModeOf(sheet.Field(FieldIds.Initiative)));
        Assert.Equal("1d12", AttackOf(sheet, axe).Riders!.Single(r => r.Critical).Dice);
        Assert.NotEqual(AutomationStatus.Reference, sheet.Features!.First(f => f.Name == "Brutal Critical").Automation);
        Assert.Contains(sheet.Active!, r => r == NewBerserker(RulesFamilies.Srd51));
    }

    [Fact]
    public void New_characters_are_offered_one_Berserker()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var content = temp.App.ListContent(family).ToDictionary(o => o.Reference);
            var sheet = temp.App.SaveCharacter(Barbarian(family, 3) with { Choices = [new(NewClass(family), "barbarian-skills", [.. Skills(family, 0, 2)])] }).Sheet;
            var subclass = sheet.Choices!.Single(c => c.ChoiceId == SubclassChoice(family));

            // The older revisions stay valid for saved characters: 5.1 also keeps the C2-1 one between the v12 and the M1 revision.
            Assert.Equal([NewBerserker(family), PreV12Berserker(family), OldBerserker(family)], subclass.Options);
            var offered = Assert.Single(subclass.Options, o => !content[o].Superseded);
            Assert.Equal(NewBerserker(family), offered);
            Assert.Equal("Path of the Berserker", content[offered].Name);
        }
    }

    [Fact]
    public void Frenzy_in_5_2_1_has_its_three_rolls_and_the_first_is_unchanged()
    {
        using var temp = new TempApp();
        var sheet = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 3)).Sheet;

        var frenzy = sheet.Features!.Single(f => f.Name == "Frenzy");
        Assert.Equal(["2d6", "3d6", "4d6"], frenzy.Effects.Where(e => e.Type == "roll").Select(e => e.Dice));
        Assert.Contains(frenzy.Effects, e => e.Id == "frenzy-damage" && e.Dice == "2d6");
    }

    private static readonly string[] FixtureLevels = ["1", "3", "5", "11", "17", "20"];

    public static TheoryData<string, string> Fixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var family in new[] { "srd521", "srd51" })
            foreach (var level in FixtureLevels)
                data.Add(family, level);
        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_barbarians_calculate_with_a_trace(string family, string level)
    {
        using var temp = new TempApp();
        var fixture = TempApp.LoadFixture<Character>($"characters/{family}-barbarian-l{level}.json");

        var view = temp.App.SaveCharacter(fixture);

        // The 5.1 fixtures were saved on the C2-1 revision and stay there until the owner applies the class update.
        Assert.Equal(PreV12Class(view.Character.RulesFamily), view.Character.Classes[0].Class);
        Assert.Equal(int.Parse(level), view.Character.Classes[0].Level);
        AssertClean(view.Sheet);
        Assert.NotEmpty(view.Sheet.Field(FieldIds.ArmorClass).Trace);
        Assert.NotEmpty(view.Sheet.Field(FieldIds.HitPoints).Trace);
        Assert.NotEmpty(Rages(view.Sheet).Trace);
        Assert.All(view.Sheet.Choices!, c => Assert.True(c.Resolved, c.ChoiceId));
    }
}
