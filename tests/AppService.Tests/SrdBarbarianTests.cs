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

    private static ContentReference NewClass(string family) => Ref(family, ClassContent(family), family == RulesFamilies.Srd51 ? 409003 : 409011);

    private static ContentReference M1Class(string family) => Ref(family, ClassContent(family), ClassContent(family));

    private static ContentReference ClassesPackClass(string family) => Ref(family, ClassContent(family), 300001);

    private static long BerserkerContent(string family) => family == RulesFamilies.Srd51 ? 14 : 23;

    private static ContentReference NewBerserker(string family) => Ref(family, BerserkerContent(family), family == RulesFamilies.Srd51 ? 409014 : 409023);

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
    private static Character Barbarian(string family, int level, IReadOnlyList<EquipmentEntry>? equipment = null, AbilityScores? scores = null, ContentReference? cls = null)
    {
        cls ??= NewClass(family);
        List<ChoiceSelection> choices = [new(cls, "barbarian-skills", [.. Skills(family, 0, 2)])];
        if (level >= 3)
        {
            choices.Add(new(cls, SubclassChoice(family), [NewBerserker(family)]));
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
            // d12: 12 + Con 3 at level 1, then 7 + 3 per level (SRD fixed value).
            Assert.Equal(15 + ((level - 1) * 10), sheet.Field(FieldIds.HitPoints).Value);
            Assert.Equal(AutomationStatus.Automatic, sheet.Field(FieldIds.Attacks).Automation);
            AssertClean(sheet);
            Assert.NotEmpty(sheet.Field(FieldIds.ArmorClass).Trace);
            Assert.NotEmpty(rages.Trace);
            Assert.Equal(10 + 2 + 3, sheet.Field(FieldIds.ArmorClass).Value); // Unarmored Defense: 10 + Dex 2 + Con 3
        }
    }

    [Fact]
    public void Levels_1_to_19_are_automatic_where_level_20_is_assisted_because_of_Primal_Champion()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var nineteen = temp.App.SaveCharacter(Barbarian(family, 19)).Sheet;
            var twenty = temp.App.SaveCharacter(Barbarian(family, 20)).Sheet;
            foreach (var field in new[] { FieldIds.ArmorClass, FieldIds.Save(Ability.Str), FieldIds.Save(Ability.Con), FieldIds.Skill("athletics") })
            {
                Assert.Equal(AutomationStatus.Automatic, nineteen.Field(field).Automation);
                Assert.Equal(AutomationStatus.Assisted, twenty.Field(field).Automation);
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
            Assert.True(off.Attacks!.Single(a => a.Item == axe).Riders is null or { Count: 0 });
            Assert.DoesNotContain(off.DamageDefences ?? [], d => d.Kind == DamageDefenceKind.Resistance);
            Assert.Equal(RollMode.Normal, ModeOf(off.Field(FieldIds.Save(Ability.Str))));
            Assert.Equal(RollMode.Normal, ModeOf(off.Field(FieldIds.Modifier(Ability.Str))));

            var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: RageContent(family).ContentId, ToggleId: "rage")).Sheet;
            var rider = Assert.Single(on.Attacks!.Single(a => a.Item == axe).Riders!);
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
    public void Primal_Champion_is_reference_and_leaves_the_scores_unchanged()
    {
        using var temp = new TempApp();
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Barbarian(family, 20, scores: new(20, 14, 16, 8, 10, 10))).Sheet;

            Assert.Equal(20, sheet.Field(FieldIds.Score(Ability.Str)).Value);
            Assert.Equal(16, sheet.Field(FieldIds.Score(Ability.Con)).Value);
            Assert.Equal(AutomationStatus.Assisted, sheet.Field(FieldIds.Score(Ability.Str)).Automation);
            var champion = sheet.Features!.Single(f => f.Name == "Primal Champion");
            Assert.Equal(AutomationStatus.Reference, champion.Automation);
        }
    }

    [Fact]
    public void Reference_features_are_listed_as_reference()
    {
        using var temp = new TempApp();
        var names = new Dictionary<string, string[]>
        {
            [RulesFamilies.Srd521] = ["Reckless Attack", "Feral Instinct", "Brutal Strike", "Persistent Rage", "Indomitable Might", "Mindless Rage"],
            [RulesFamilies.Srd51] = ["Reckless Attack", "Feral Instinct", "Brutal Critical", "Persistent Rage", "Indomitable Might", "Mindless Rage"],
        };
        foreach (var family in Families)
        {
            var sheet = temp.App.SaveCharacter(Barbarian(family, 20)).Sheet;
            foreach (var name in names[family])
                Assert.True(sheet.Features!.Any(f => f.Name == name && f.Automation == AutomationStatus.Reference), $"{family}: {name}");
        }

        // Primal Knowledge (the unchanged M1 revision) has the skill choice as its only effect: the rest of its text, the
        // Strength-check option while raging, is by hand.
        var knowledge = temp.App.SaveCharacter(Barbarian(RulesFamilies.Srd521, 20)).Sheet.Features!.Single(f => f.Name == "Primal Knowledge");
        Assert.All(knowledge.Effects, e => Assert.Equal("choice", e.Type));
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
        {
            // (a) the class only.
            var a = temp.App.SaveCharacter(OnClass(family, M1Class(family))).Character.Id;
            var viaClass = temp.App.ApplyUpdate(a, M1Class(family), NewClass(family), confirm: true);
            Assert.Equal(NewClass(family), viaClass.Character.Classes[0].Class);
            AssertUpdatedBarbarian(viaClass.Sheet, family);
            Assert.Contains(viaClass.Sheet.Active!, r => r == OldBerserker(family)); // still the old Berserker revision, still valid

            // (b) the Berserker first, on the old class, then the class.
            var b = temp.App.SaveCharacter(OnClass(family, M1Class(family))).Character.Id;
            var offer = Assert.Single(temp.App.AvailableUpdates(b), o => o.From == OldBerserker(family));
            Assert.Equal(NewBerserker(family), offer.To);
            var viaBerserker = temp.App.ApplyUpdate(b, offer.From, offer.To, confirm: true);
            Assert.Equal(M1Class(family), viaBerserker.Character.Classes[0].Class);
            Assert.True(viaBerserker.Sheet.Choices!.Single(c => c.ChoiceId == SubclassChoice(family)).Resolved);
            Assert.Contains(viaBerserker.Sheet.Features!, f => f.Name == "Frenzy");
            AssertClean(viaBerserker.Sheet);

            var both = temp.App.ApplyUpdate(b, M1Class(family), NewClass(family), confirm: true);
            AssertUpdatedBarbarian(both.Sheet, family);
            Assert.Contains(both.Sheet.Active!, r => r == NewBerserker(family));
        }
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

            Assert.Equal([NewBerserker(family), OldBerserker(family)], subclass.Options); // the old revision stays valid for saved characters
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

        Assert.Equal(NewClass(view.Character.RulesFamily), view.Character.Classes[0].Class);
        Assert.Equal(int.Parse(level), view.Character.Classes[0].Level);
        AssertClean(view.Sheet);
        Assert.NotEmpty(view.Sheet.Field(FieldIds.ArmorClass).Trace);
        Assert.NotEmpty(view.Sheet.Field(FieldIds.HitPoints).Trace);
        Assert.NotEmpty(Rages(view.Sheet).Trace);
        Assert.All(view.Sheet.Choices!, c => Assert.True(c.Resolved, c.ChoiceId));
    }
}
